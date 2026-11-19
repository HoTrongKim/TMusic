using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TMusic.Api.Common;
using TMusic.Application.Providers;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Persistence;

namespace TMusic.Api.Imports;

public static class ImportEndpoints
{
    public static void MapImportEndpoints(this RouteGroupBuilder api)
    {
        var imports = api.MapGroup("/imports").RequireAuthorization();
        imports.MapPost("/previews", CreatePreview).RequireRateLimiting("provider");
        imports.MapGet("/previews/{id:guid}", GetPreview);
        imports.MapPost("/previews/{id:guid}/cancel", CancelPreview);
        imports.MapPost("", Submit).RequireRateLimiting("provider");
        imports.MapGet("", Recent);
        imports.MapGet("/{id:guid}", GetJob);
        imports.MapPost("/{id:guid}/cancel", CancelJob);
        imports.MapPost("/{id:guid}/retry", RetryJob);
    }

    private static async Task<IResult> CreatePreview(PreviewRequest request, ClaimsPrincipal principal,
        TMusicDb db, IYouTubePlaylist youtube, IConfiguration configuration, TimeProvider clock, CancellationToken ct)
    {
        if (!configuration.GetValue("Features:EnableYouTubePlaylistImport", true)) return ApiSupport.Problem("PLAYLIST_IMPORT_DISABLED", 503);
        if (SourceUrl.ParsePlaylist(request.Url) is not { } playlistId) return ApiSupport.Problem("INVALID_PLAYLIST_URL");
        if (!youtube.IsConfigured) return ApiSupport.Problem("YOUTUBE_NOT_CONFIGURED", 503);
        var owner = ApiSupport.UserId(principal); var now = clock.GetUtcNow();
        if (await db.ImportPreviews.CountAsync(x => x.OwnerId == owner && x.ExpiresAt > now &&
            (x.Status == ImportStatus.Queued || x.Status == ImportStatus.Running), ct) >= 3)
            return ApiSupport.Problem("TOO_MANY_ACTIVE_IMPORTS", 429);
        var preview = new ImportPreview { OwnerId = owner, PlaylistId = playlistId, CreatedAt = now,
            NextAttemptAt = now, ExpiresAt = now.AddHours(24) };
        db.ImportPreviews.Add(preview); await db.SaveChangesAsync(ct);
        var statusUrl = $"/api/v1/imports/previews/{preview.Id}";
        return Results.Accepted(statusUrl, new { previewId = preview.Id, statusUrl });
    }

    private static async Task<IResult> GetPreview(Guid id, ClaimsPrincipal principal, TMusicDb db,
        int? page, int? pageSize, TimeProvider clock, CancellationToken ct)
    {
        if (!ApiSupport.PageValid(page, pageSize)) return ApiSupport.Problem("INVALID_QUERY");
        var owner = ApiSupport.UserId(principal);
        var preview = await db.ImportPreviews.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == owner, ct);
        if (preview is null) return Results.NotFound();
        if (preview.ExpiresAt <= clock.GetUtcNow()) return ApiSupport.Problem("PREVIEW_EXPIRED", 410);
        var current = page ?? 1; var size = pageSize ?? 50;
        var items = await db.ImportCandidates.AsNoTracking().Where(x => x.PreviewId == id).OrderBy(x => x.Position)
            .Skip((current - 1) * size).Take(size).Select(x => new { x.Position, x.VideoId, x.Title, x.Creator, x.Status }).ToListAsync(ct);
        return Results.Ok(new
        {
            previewId = preview.Id, preview.Status, preview.ErrorCode, preview.Accepted, preview.Duplicates,
            preview.Unavailable, preview.ExpiresAt, totalKnown = preview.Status == ImportStatus.Ready,
            total = preview.Accepted + preview.Duplicates + preview.Unavailable, page = current, pageSize = size, items
        });
    }

    private static async Task<IResult> Submit(SubmitRequest request, HttpContext http, ClaimsPrincipal principal,
        TMusicDb db, TimeProvider clock, CancellationToken ct)
    {
        var owner = ApiSupport.UserId(principal); var now = clock.GetUtcNow();
        var key = http.Request.Headers["Idempotency-Key"].ToString();
        if (key.Length is < 8 or > 100 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            return ApiSupport.Problem("IDEMPOTENCY_KEY_REQUIRED");
        var name = request.AlbumName?.Trim();
        if (request.AlbumId is null && name is not { Length: > 0 and <= 160 } || request.AlbumId is not null && name is not null)
            return ApiSupport.Problem("INVALID_IMPORT_TARGET");
        var fingerprint = ApiSupport.Hash(JsonSerializer.Serialize(new { request.PreviewId, request.AlbumId, name }));
        var prior = await db.ImportJobs.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerId == owner && x.IdempotencyKey == key, ct);
        if (prior is not null) return prior.Fingerprint == fingerprint ? Accepted(prior.Id) : ApiSupport.Problem("IDEMPOTENCY_CONFLICT", 409);
        var preview = await db.ImportPreviews.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PreviewId && x.OwnerId == owner, ct);
        if (preview is null) return Results.NotFound();
        if (preview.ExpiresAt <= now) return ApiSupport.Problem("PREVIEW_EXPIRED", 410);
        if (preview.Status != ImportStatus.Ready || preview.Checksum is null) return ApiSupport.Problem("PREVIEW_NOT_READY", 409);
        if (request.AlbumId is { } albumId &&
            !await db.Albums.AsNoTracking().AnyAsync(x => x.Id == albumId && x.OwnerId == owner, ct))
            return Results.NotFound();
        if (await db.ImportJobs.CountAsync(x => x.OwnerId == owner &&
            (x.Status == ImportStatus.Queued || x.Status == ImportStatus.Running), ct) >= 3)
            return ApiSupport.Problem("TOO_MANY_ACTIVE_IMPORTS", 429);
        var job = new ImportJob { OwnerId = owner, PreviewId = preview.Id, AlbumId = request.AlbumId, AlbumName = name,
            IdempotencyKey = key, Fingerprint = fingerprint, Checksum = preview.Checksum, CreatedAt = now,
            NextAttemptAt = now, Total = preview.Accepted, Duplicates = preview.Duplicates, Unavailable = preview.Unavailable };
        db.ImportJobs.Add(job);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (ApiSupport.IsUnique(error))
        {
            db.ChangeTracker.Clear();
            prior = await db.ImportJobs.AsNoTracking().SingleAsync(x => x.OwnerId == owner && x.IdempotencyKey == key, ct);
            return prior.Fingerprint == fingerprint ? Accepted(prior.Id) : ApiSupport.Problem("IDEMPOTENCY_CONFLICT", 409);
        }
        return Accepted(job.Id);
    }

    private static async Task<IResult> GetJob(Guid id, ClaimsPrincipal principal, TMusicDb db, CancellationToken ct)
    {
        var owner = ApiSupport.UserId(principal);
        var job = await db.ImportJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == owner, ct);
        return job is null ? Results.NotFound() : Results.Ok(Render(job));
    }

    private static async Task<IResult> Recent(ClaimsPrincipal principal, TMusicDb db, TimeProvider clock, CancellationToken ct)
    {
        var owner = ApiSupport.UserId(principal); var now = clock.GetUtcNow();
        var jobs = await db.ImportJobs.AsNoTracking().Where(x => x.OwnerId == owner).OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync(ct);
        var previews = await db.ImportPreviews.AsNoTracking().Where(x => x.OwnerId == owner && x.ExpiresAt > now)
            .OrderByDescending(x => x.CreatedAt).Take(10).Select(x => new { previewId = x.Id, x.Status, x.CreatedAt }).ToListAsync(ct);
        return Results.Ok(new { items = jobs.Select(Render), previews });
    }

    private static async Task<IResult> CancelJob(Guid id, ClaimsPrincipal principal, TMusicDb db, TimeProvider clock, CancellationToken ct)
    {
        var owner = ApiSupport.UserId(principal); var now = clock.GetUtcNow();
        var jobs = db.ImportJobs.Where(x => x.Id == id && x.OwnerId == owner);
        if (!await jobs.AnyAsync(ct)) return Results.NotFound();
        await jobs.Where(x => x.Status == ImportStatus.Queued || x.Status == ImportStatus.Running).ExecuteUpdateAsync(set => set
            .SetProperty(x => x.Status, ImportStatus.Canceled).SetProperty(x => x.LeaseOwner, (Guid?)null)
            .SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null).SetProperty(x => x.CompletedAt, now), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CancelPreview(Guid id, ClaimsPrincipal principal, TMusicDb db, CancellationToken ct)
    {
        var owner = ApiSupport.UserId(principal); var previews = db.ImportPreviews.Where(x => x.Id == id && x.OwnerId == owner);
        if (!await previews.AnyAsync(ct)) return Results.NotFound();
        await previews.Where(x => x.Status == ImportStatus.Queued || x.Status == ImportStatus.Running).ExecuteUpdateAsync(set => set
            .SetProperty(x => x.Status, ImportStatus.Canceled).SetProperty(x => x.LeaseOwner, (Guid?)null)
            .SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RetryJob(Guid id, ClaimsPrincipal principal, TMusicDb db, TimeProvider clock, CancellationToken ct)
    {
        var owner = ApiSupport.UserId(principal); var now = clock.GetUtcNow();
        var jobs = db.ImportJobs.Where(x => x.Id == id && x.OwnerId == owner);
        if (!await jobs.AnyAsync(ct)) return Results.NotFound();
        var count = await jobs.Where(x => (x.Status == ImportStatus.Failed || x.Status == ImportStatus.Canceled) &&
            db.ImportPreviews.Any(p => p.Id == x.PreviewId && p.OwnerId == owner && p.ExpiresAt > now && p.Status == ImportStatus.Ready))
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, ImportStatus.Queued).SetProperty(x => x.Failures, 0)
                .SetProperty(x => x.ErrorCode, (string?)null).SetProperty(x => x.CompletedAt, (DateTimeOffset?)null)
                .SetProperty(x => x.LeaseOwner, (Guid?)null).SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(x => x.NextAttemptAt, now), ct);
        return count == 0 ? ApiSupport.Problem("IMPORT_NOT_RETRYABLE", 409) : Accepted(id);
    }

    private static IResult Accepted(Guid id)
    {
        var statusUrl = $"/api/v1/imports/{id}";
        return Results.Accepted(statusUrl, new { jobId = id, statusUrl });
    }
    private static object Render(ImportJob job) => new { jobId = job.Id, job.Status, job.ErrorCode, job.AlbumId,
        job.CreatedAt, job.Total, job.Imported, job.AlreadyPresent, job.Duplicates, job.Unavailable,
        totalKnown = true, processed = job.Imported + job.AlreadyPresent };
    private sealed record PreviewRequest(string? Url);
    private sealed record SubmitRequest(Guid PreviewId, Guid? AlbumId, string? AlbumName);
}

