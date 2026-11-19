using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TMusic.Api.Common;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Downloads;
using TMusic.Infrastructure.Persistence;
using TMusic.Infrastructure.Storage;

namespace TMusic.Api.Downloads;

public static class AudioDownloadEndpoints
{
    public static void MapAudioDownloadEndpoints(this RouteGroupBuilder api)
    {
        var songs = api.MapGroup("/library/songs").RequireAuthorization();
        songs.MapPost("/{id:guid}/download", Enqueue).RequireRateLimiting("provider");
        songs.MapGet("/{id:guid}/audio", StreamAudio);
        songs.MapDelete("/{id:guid}/audio", RemoveAudio);

        var downloads = api.MapGroup("/downloads").RequireAuthorization();
        downloads.MapGet("", ListJobs);
        downloads.MapGet("/{id:guid}", GetJob);
        downloads.MapPost("/{id:guid}/retry", RetryJob).RequireRateLimiting("provider");
        downloads.MapDelete("/{id:guid}", CancelOrDeleteJob);
    }

    private static async Task<IResult> Enqueue(
        Guid id,
        EnqueueDownloadRequest r,
        ClaimsPrincipal u,
        TMusicDb db,
        IAudioExtractor extractor,
        IAudioStorage storage,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!r.RightsConfirmed)
            return ApiSupport.Problem("RIGHTS_CONFIRMATION_REQUIRED", 400, "Vui lòng xác nhận bạn có quyền hợp pháp để tải MP3 cá nhân.");
        if (!AudioBitrate.IsValid(r.BitrateKbps))
            return ApiSupport.Problem("INVALID_BITRATE", 400, "Bitrate MP3 chỉ hỗ trợ 128, 192 hoặc 320 kbps.");
        if (!extractor.IsAvailable)
            return ApiSupport.Problem("AUDIO_DOWNLOAD_UNAVAILABLE", 503, "Bộ xử lý tải MP3 (yt-dlp / FFmpeg) hiện chưa sẵn sàng.");

        var user = ApiSupport.UserId(u);
        var entry = await db.UserLibrarySongs
            .Include(x => x.Song)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == user, ct);
        if (entry is null) return Results.NotFound();

        var now = clock.GetUtcNow();
        if (!r.ForceRedownload && entry.AudioStatus == ImportStatus.Ready && entry.AudioFileName is { Length: > 0 } existingFile)
        {
            var opened = await storage.OpenReadAsync(user, existingFile, ct);
            if (opened is not null)
            {
                await opened.Value.Stream.DisposeAsync();
                var completed = await db.AudioDownloadJobs
                    .AsNoTracking()
                    .Where(x => x.OwnerId == user && x.UserLibrarySongId == id && x.Status == ImportStatus.Complete)
                    .OrderByDescending(x => x.CompletedAt)
                    .FirstOrDefaultAsync(ct);
                if (completed is not null)
                    return Results.Ok(ToView(completed, entry));

                var synthetic = new AudioDownloadJob
                {
                    Id = entry.Id,
                    OwnerId = user,
                    UserLibrarySongId = entry.Id,
                    Provider = entry.Song.Provider,
                    CanonicalUrl = entry.Song.CanonicalUrl,
                    Status = ImportStatus.Complete,
                    ProgressPercent = 100,
                    OutputFileName = existingFile,
                    OutputSizeBytes = entry.AudioSizeBytes,
                    CreatedAt = entry.CreatedAt,
                    CompletedAt = entry.UpdatedAt
                };
                return Results.Ok(ToView(synthetic, entry));
            }
        }

        var active = await db.AudioDownloadJobs
            .AsNoTracking()
            .Where(x => x.OwnerId == user && x.UserLibrarySongId == id &&
                        (x.Status == ImportStatus.Queued || x.Status == ImportStatus.Running))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (active is not null)
            return Results.Accepted($"/api/v1/downloads/{active.Id}", ToView(active, entry));

        var job = new AudioDownloadJob
        {
            OwnerId = user,
            UserLibrarySongId = entry.Id,
            Provider = entry.Song.Provider,
            CanonicalUrl = entry.Song.CanonicalUrl,
            RightsConfirmed = true,
            BitrateKbps = r.BitrateKbps,
            Status = ImportStatus.Queued,
            ProgressPercent = 0,
            CreatedAt = now,
            NextAttemptAt = now
        };
        db.AudioDownloadJobs.Add(job);
        entry.AudioStatus = ImportStatus.Queued;
        entry.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return Results.Accepted($"/api/v1/downloads/{job.Id}", ToView(job, entry));
    }

    private static async Task<IResult> StreamAudio(
        Guid id,
        ClaimsPrincipal u,
        TMusicDb db,
        IAudioStorage storage,
        HttpContext context,
        CancellationToken ct)
    {
        var user = ApiSupport.UserId(u);
        var entry = await db.UserLibrarySongs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == user, ct);
        if (entry is null || entry.AudioStatus != ImportStatus.Ready || entry.AudioFileName is not { Length: > 0 } fileName)
            return Results.NotFound();

        var opened = await storage.OpenReadAsync(user, fileName, ct);
        if (opened is null) return Results.NotFound();

        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(
            opened.Value.Stream,
            contentType: "audio/mpeg",
            enableRangeProcessing: true);
    }

    private static async Task<IResult> RemoveAudio(
        Guid id,
        ClaimsPrincipal u,
        TMusicDb db,
        IAudioStorage storage,
        TimeProvider clock,
        CancellationToken ct)
    {
        var user = ApiSupport.UserId(u);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        await db.AudioDownloadJobs
            .Where(x => x.OwnerId == user && x.UserLibrarySongId == id &&
                (x.Status == ImportStatus.Queued || x.Status == ImportStatus.Running))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ImportStatus.Canceled)
                .SetProperty(x => x.CompletedAt, now)
                .SetProperty(x => x.LeaseOwner, (Guid?)null)
                .SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null), ct);
        var entry = await db.UserLibrarySongs
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == user, ct);
        if (entry is null) return Results.NotFound();

        var oldFile = entry.AudioFileName;
        entry.AudioFileName = null;
        entry.AudioSizeBytes = null;
        entry.AudioStatus = "NONE";
        entry.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        storage.DeleteAudioFile(user, oldFile);
        return Results.NoContent();
    }

    private static async Task<IResult> ListJobs(
        ClaimsPrincipal u,
        TMusicDb db,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        if (!ApiSupport.PageValid(page, pageSize, null))
            return ApiSupport.Problem("INVALID_QUERY");

        var user = ApiSupport.UserId(u);
        var current = page ?? 1;
        var size = pageSize ?? 30;
        var query = db.AudioDownloadJobs.AsNoTracking().Where(x => x.OwnerId == user);
        var total = await query.CountAsync(ct);
        var jobs = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Skip((current - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        var songIds = jobs.Select(x => x.UserLibrarySongId).Distinct().ToArray();
        var songs = await db.UserLibrarySongs
            .AsNoTracking()
            .Include(x => x.Song)
            .Where(x => x.UserId == user && songIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        return Results.Ok(new
        {
            items = jobs.Select(j => ToView(j, songs.GetValueOrDefault(j.UserLibrarySongId))),
            total,
            page = current,
            pageSize = size
        });
    }

    private static async Task<IResult> GetJob(
        Guid id,
        ClaimsPrincipal u,
        TMusicDb db,
        CancellationToken ct)
    {
        var user = ApiSupport.UserId(u);
        var job = await db.AudioDownloadJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == user, ct);
        if (job is null) return Results.NotFound();

        var song = await db.UserLibrarySongs
            .AsNoTracking()
            .Include(x => x.Song)
            .FirstOrDefaultAsync(x => x.Id == job.UserLibrarySongId && x.UserId == user, ct);

        return Results.Ok(ToView(job, song));
    }

    private static async Task<IResult> RetryJob(
        Guid id,
        ClaimsPrincipal u,
        TMusicDb db,
        IAudioExtractor extractor,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!extractor.IsAvailable)
            return ApiSupport.Problem("AUDIO_DOWNLOAD_UNAVAILABLE", 503, "Bộ xử lý tải MP3 (yt-dlp / FFmpeg) hiện chưa sẵn sàng.");

        var user = ApiSupport.UserId(u);
        var job = await db.AudioDownloadJobs
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == user, ct);
        if (job is null) return Results.NotFound();

        var song = await db.UserLibrarySongs
            .Include(x => x.Song)
            .FirstOrDefaultAsync(x => x.Id == job.UserLibrarySongId && x.UserId == user, ct);
        if (song is null) return Results.NotFound();

        if (job.Status == ImportStatus.Failed && !YtDlpAudioExtractor.IsRetryableErrorCode(job.ErrorCode))
            return ApiSupport.Problem(job.ErrorCode ?? "DOWNLOAD_NOT_RETRYABLE", 422);

        if (job.Status is ImportStatus.Failed or ImportStatus.Canceled)
        {
            var now = clock.GetUtcNow();
            job.Status = ImportStatus.Queued;
            job.ProgressPercent = 0;
            job.Failures = 0;
            job.ErrorCode = null;
            job.CompletedAt = null;
            job.LeaseOwner = null;
            job.LeaseUntil = null;
            job.NextAttemptAt = now;
            song.AudioStatus = ImportStatus.Queued;
            song.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return Results.Accepted($"/api/v1/downloads/{job.Id}", ToView(job, song));
    }

    private static async Task<IResult> CancelOrDeleteJob(
        Guid id,
        ClaimsPrincipal u,
        TMusicDb db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var user = ApiSupport.UserId(u);
        var job = await db.AudioDownloadJobs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == user, ct);
        if (job is null) return Results.NotFound();

        if (job.Status is ImportStatus.Queued or ImportStatus.Running)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var changed = await db.AudioDownloadJobs
                .Where(x => x.Id == id && x.OwnerId == user &&
                    (x.Status == ImportStatus.Queued || x.Status == ImportStatus.Running))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ImportStatus.Canceled)
                    .SetProperty(x => x.CompletedAt, clock.GetUtcNow())
                    .SetProperty(x => x.LeaseOwner, (Guid?)null)
                    .SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null), ct);
            if (changed > 0)
                await db.UserLibrarySongs
                    .Where(x => x.Id == job.UserLibrarySongId && x.UserId == user)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.AudioStatus,
                        x => x.AudioFileName == null ? "NONE" : ImportStatus.Ready)
                        .SetProperty(x => x.UpdatedAt, clock.GetUtcNow()), ct);
            await tx.CommitAsync(ct);
        }
        else if (job.Status is ImportStatus.Complete or ImportStatus.Failed or ImportStatus.Canceled)
        {
            db.AudioDownloadJobs.Remove(job);
            await db.SaveChangesAsync(ct);
        }

        return Results.NoContent();
    }

    private static object ToView(AudioDownloadJob job, UserLibrarySong? song)
    {
        var audioReady = song?.AudioStatus == ImportStatus.Ready && song.AudioFileName is { Length: > 0 };
        var title = song is null
            ? null
            : (!string.IsNullOrWhiteSpace(song.CustomTitle) ? song.CustomTitle : song.Song?.OriginalTitle);
        var retryable = job.Status == ImportStatus.Canceled ||
            (job.Status == ImportStatus.Failed && YtDlpAudioExtractor.IsRetryableErrorCode(job.ErrorCode));
        return new
        {
            job.Id,
            songId = job.UserLibrarySongId,
            songTitle = title,
            songArtist = song?.CustomArtist ?? song?.Song?.OriginalCreator,
            job.Provider,
            job.CanonicalUrl,
            job.Status,
            job.ProgressPercent,
            job.ErrorCode,
            retryable,
            bitrateKbps = job.BitrateKbps,
            audioReady,
            audioStreamUrl = audioReady ? $"/api/v1/library/songs/{job.UserLibrarySongId}/audio" : null,
            outputSizeBytes = job.OutputSizeBytes ?? song?.AudioSizeBytes,
            job.CreatedAt,
            job.CompletedAt
        };
    }

    public sealed record EnqueueDownloadRequest(bool RightsConfirmed = false, bool ForceRedownload = false, int? BitrateKbps = null);
}
