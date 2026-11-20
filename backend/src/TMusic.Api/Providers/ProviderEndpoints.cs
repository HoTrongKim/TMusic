using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TMusic.Api.Common;
using TMusic.Api.Library;
using TMusic.Application.Providers;
using TMusic.Infrastructure.Downloads;
using TMusic.Infrastructure.Persistence;
using TMusic.Infrastructure.Storage;

namespace TMusic.Api.Providers;

public static class ProviderEndpoints
{
    public static void MapProviderEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/songs/resolve", Resolve).RequireAuthorization().RequireRateLimiting("provider");
        api.MapPost("/guest/songs/resolve", GuestResolve).RequireRateLimiting("provider");
        api.MapPost("/guest/songs/extract-mp3", GuestExtractMp3).RequireRateLimiting("provider");
    }

    private static async Task<IResult> Resolve(ResolveRequest request, ClaimsPrincipal principal, ISourceResolver resolver,
        TMusicDb db, TimeProvider clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Url)) return ApiSupport.Problem("INVALID_SOURCE_URL");
        try
        {
            var result = await resolver.ResolveAsync(request.Url, ct);
            var owner = ApiSupport.UserId(principal);
            var existing = await db.UserLibrarySongs.AsNoTracking()
                .Include(x => x.Song)
                .Include(x => x.AlbumSongs)
                .AsSplitQuery()
                .SingleOrDefaultAsync(x => x.UserId == owner &&
                    x.Song.Provider == result.Provider &&
                    x.Song.ProviderContentId == result.ProviderContentId, ct);

            return Results.Ok(new
            {
                result.Provider,
                result.ProviderContentId,
                result.OriginalUrl,
                result.CanonicalUrl,
                result.OriginalTitle,
                result.OriginalCreator,
                result.ThumbnailUrl,
                result.DurationSeconds,
                result.PlaybackCapability,
                result.MetadataFetchedAt,
                result.Notice,
                existingLibrarySongId = existing?.Id,
                existingSong = existing is null ? null : LibraryViews.Song(existing, clock.GetUtcNow())
            });
        }
        catch (ProviderException e)
        {
            return ApiSupport.Problem(e.Code, e.Retryable ? 503 : 422);
        }
    }

    private static async Task<IResult> GuestResolve(ResolveRequest request, ISourceResolver resolver, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Url)) return ApiSupport.Problem("INVALID_SOURCE_URL");
        try
        {
            var result = await resolver.ResolveAsync(request.Url, ct);
            return Results.Ok(new
            {
                result.Provider,
                result.ProviderContentId,
                result.OriginalUrl,
                result.CanonicalUrl,
                result.OriginalTitle,
                result.OriginalCreator,
                result.ThumbnailUrl,
                result.DurationSeconds,
                result.PlaybackCapability,
                result.MetadataFetchedAt,
                result.Notice,
                existingLibrarySongId = (Guid?)null,
                existingSong = (object?)null
            });
        }
        catch (ProviderException e)
        {
            return ApiSupport.Problem(e.Code, e.Retryable ? 503 : 422);
        }
    }

    private static async Task<IResult> GuestExtractMp3(
        GuestExtractRequest request,
        ISourceResolver resolver,
        IAudioExtractor extractor,
        DownloadOrchestrator orchestrator,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Url)) return ApiSupport.Problem("INVALID_SOURCE_URL");
        if (!request.RightsConfirmed)
            return ApiSupport.Problem("RIGHTS_CONFIRMATION_REQUIRED", 400, "Vui lòng xác nhận bạn có quyền tải nội dung này cho mục đích cá nhân.");
        if (!AudioBitrate.IsValid(request.BitrateKbps))
            return ApiSupport.Problem("INVALID_BITRATE", 400, "Bitrate MP3 chỉ hỗ trợ 128, 192 hoặc 320 kbps.");
        if (!extractor.IsAvailable)
            return ApiSupport.Problem("AUDIO_DOWNLOAD_UNAVAILABLE", 503, "Bộ xử lý tải MP3 (yt-dlp / FFmpeg) hiện chưa sẵn sàng.");

        var tempDir = Path.Combine(Path.GetTempPath(), "tmusic_guest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var resolved = await resolver.ResolveAsync(request.Url, ct);
            var download = await orchestrator.DownloadAudioAsync(
                resolved.CanonicalUrl,
                tempDir,
                request.BitrateKbps,
                context.TraceIdentifier,
                (_, _) => Task.CompletedTask,
                ct);
            var mp3Path = download.Mp3FilePath;
            var info = new FileInfo(mp3Path);
            if (!info.Exists || info.Length is <= 16 or > AudioStorage.MaxAudioBytes)
                return ApiSupport.Problem("INVALID_AUDIO_OUTPUT", 422);
            var bytes = await File.ReadAllBytesAsync(mp3Path, ct);
            return Results.Bytes(bytes, "audio/mpeg", fileDownloadName: "track.mp3");
        }
        catch (ProviderException e)
        {
            return ApiSupport.Problem(e.Code, e.Retryable ? 503 : 422);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { }
            }
        }
    }

    private sealed record ResolveRequest(string? Url);
    private sealed record GuestExtractRequest(string? Url, bool RightsConfirmed = false, int? BitrateKbps = null);
}
