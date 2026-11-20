using TMusic.Application.Downloads;
using TMusic.Application.Providers;

namespace TMusic.Infrastructure.Downloads;

public sealed class YouTubeMediaProvider(
    ISourceResolver sourceResolver,
    IAudioExtractor primaryExtractor,
    IExternalAudioProvider externalProvider,
    SourceAccessCircuitBreaker circuitBreaker,
    MediaDownloadDiagnostics diagnostics) : IMediaProvider
{
    public string ProviderId => "youtube";

    public bool Supports(SourceUrl source) => string.Equals(source.Provider, ProviderId, StringComparison.OrdinalIgnoreCase);

    public async Task<MediaMetadata> GetMetadataAsync(string url, CancellationToken cancellationToken)
    {
        var resolved = await sourceResolver.ResolveAsync(url, cancellationToken);
        return new MediaMetadata(
            resolved.Provider,
            resolved.ProviderContentId,
            resolved.CanonicalUrl,
            resolved.OriginalTitle,
            resolved.OriginalCreator,
            resolved.ThumbnailUrl,
            resolved.DurationSeconds,
            resolved.PlaybackCapability,
            resolved.Notice);
    }

    public async Task<MediaDownloadResult> DownloadAudioAsync(
        MediaDownloadRequest request,
        CancellationToken cancellationToken)
    {
        if (circuitBreaker.IsOpen(ProviderId, out _))
        {
            if (externalProvider.IsEnabled)
            {
                return await DownloadViaExternalAsync(request, cancellationToken);
            }

            var restricted = DownloadErrorClassifier.ClassifyErrorCode("SOURCE_ACCESS_CHECK_REQUIRED");
            diagnostics.RecordFailure(request.CorrelationId, ProviderId, "yt-dlp", "CIRCUIT_OPEN", restricted);
            throw restricted.ToProviderException();
        }

        try
        {
            var mp3Path = await primaryExtractor.ExtractMp3Async(
                request.CanonicalUrl,
                request.WorkDirectory,
                request.OnProgress,
                request.BitrateKbps,
                cancellationToken);
            circuitBreaker.RecordSuccess(ProviderId);
            var size = File.Exists(mp3Path) ? new FileInfo(mp3Path).Length : 0;
            diagnostics.RecordSuccess(request.CorrelationId, ProviderId, "yt-dlp", size);
            return new MediaDownloadResult(mp3Path, ProviderId, "yt-dlp", size);
        }
        catch (ProviderException ex)
        {
            var classification = DownloadErrorClassifier.ClassifyErrorCode(ex.Code);
            diagnostics.RecordFailure(request.CorrelationId, ProviderId, "yt-dlp", "EXTRACTION", classification);

            if (classification.IsAccessRestriction)
            {
                if (string.Equals(ex.Code, "SOURCE_ACCESS_CHECK_REQUIRED", StringComparison.OrdinalIgnoreCase))
                    circuitBreaker.RecordAccessCheckFailure(ProviderId);

                if (externalProvider.IsEnabled)
                    return await DownloadViaExternalAsync(request, cancellationToken);
            }

            throw;
        }
    }

    private async Task<MediaDownloadResult> DownloadViaExternalAsync(
        MediaDownloadRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await request.OnProgress(35, cancellationToken);
            var mp3Path = await externalProvider.DownloadToMp3Async(
                request.CanonicalUrl,
                request.WorkDirectory,
                request.BitrateKbps,
                cancellationToken);
            await request.OnProgress(95, cancellationToken);
            var size = File.Exists(mp3Path) ? new FileInfo(mp3Path).Length : 0;
            diagnostics.RecordSuccess(request.CorrelationId, ProviderId, externalProvider.ProviderName, size);
            return new MediaDownloadResult(mp3Path, ProviderId, externalProvider.ProviderName, size);
        }
        catch (ProviderException ex)
        {
            var classification = DownloadErrorClassifier.ClassifyErrorCode(ex.Code);
            diagnostics.RecordFailure(request.CorrelationId, ProviderId, externalProvider.ProviderName, "EXTERNAL_FALLBACK", classification);
            throw;
        }
    }
}

public sealed class TikTokMediaProvider(
    ISourceResolver sourceResolver,
    IAudioExtractor extractor,
    MediaDownloadDiagnostics diagnostics) : IMediaProvider
{
    public string ProviderId => "tiktok";

    public bool Supports(SourceUrl source) => string.Equals(source.Provider, ProviderId, StringComparison.OrdinalIgnoreCase);

    public async Task<MediaMetadata> GetMetadataAsync(string url, CancellationToken cancellationToken)
    {
        var resolved = await sourceResolver.ResolveAsync(url, cancellationToken);
        return new MediaMetadata(resolved.Provider, resolved.ProviderContentId, resolved.CanonicalUrl,
            resolved.OriginalTitle, resolved.OriginalCreator, resolved.ThumbnailUrl, resolved.DurationSeconds,
            resolved.PlaybackCapability, resolved.Notice);
    }

    public async Task<MediaDownloadResult> DownloadAudioAsync(MediaDownloadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var mp3Path = await extractor.ExtractMp3Async(
                request.CanonicalUrl, request.WorkDirectory, request.OnProgress, request.BitrateKbps, cancellationToken);
            var size = File.Exists(mp3Path) ? new FileInfo(mp3Path).Length : 0;
            diagnostics.RecordSuccess(request.CorrelationId, ProviderId, "yt-dlp", size);
            return new MediaDownloadResult(mp3Path, ProviderId, "yt-dlp", size);
        }
        catch (ProviderException ex)
        {
            diagnostics.RecordFailure(request.CorrelationId, ProviderId, "yt-dlp", "EXTRACTION",
                DownloadErrorClassifier.ClassifyErrorCode(ex.Code));
            throw;
        }
    }
}

public sealed class SoundCloudMediaProvider(
    ISourceResolver sourceResolver,
    IAudioExtractor extractor,
    MediaDownloadDiagnostics diagnostics) : IMediaProvider
{
    public string ProviderId => "soundcloud";

    public bool Supports(SourceUrl source) => string.Equals(source.Provider, ProviderId, StringComparison.OrdinalIgnoreCase);

    public async Task<MediaMetadata> GetMetadataAsync(string url, CancellationToken cancellationToken)
    {
        var resolved = await sourceResolver.ResolveAsync(url, cancellationToken);
        return new MediaMetadata(resolved.Provider, resolved.ProviderContentId, resolved.CanonicalUrl,
            resolved.OriginalTitle, resolved.OriginalCreator, resolved.ThumbnailUrl, resolved.DurationSeconds,
            resolved.PlaybackCapability, resolved.Notice);
    }

    public async Task<MediaDownloadResult> DownloadAudioAsync(MediaDownloadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var mp3Path = await extractor.ExtractMp3Async(
                request.CanonicalUrl, request.WorkDirectory, request.OnProgress, request.BitrateKbps, cancellationToken);
            var size = File.Exists(mp3Path) ? new FileInfo(mp3Path).Length : 0;
            diagnostics.RecordSuccess(request.CorrelationId, ProviderId, "yt-dlp", size);
            return new MediaDownloadResult(mp3Path, ProviderId, "yt-dlp", size);
        }
        catch (ProviderException ex)
        {
            diagnostics.RecordFailure(request.CorrelationId, ProviderId, "yt-dlp", "EXTRACTION",
                DownloadErrorClassifier.ClassifyErrorCode(ex.Code));
            throw;
        }
    }
}

public sealed class AuthorizedExternalMediaProvider(
    IExternalAudioProvider externalProvider,
    MediaDownloadDiagnostics diagnostics) : IMediaProvider
{
    public string ProviderId => "external";

    public bool Supports(SourceUrl source) =>
        externalProvider.IsEnabled && string.Equals(source.Provider, "youtube", StringComparison.OrdinalIgnoreCase);

    public async Task<MediaMetadata> GetMetadataAsync(string url, CancellationToken cancellationToken)
    {
        var parsed = SourceUrl.ParseVideo(url) ?? throw new ProviderException("INVALID_SOURCE_URL");
        var meta = await externalProvider.GetMetadataAsync(parsed.CanonicalUrl, cancellationToken);
        return new MediaMetadata(parsed.Provider, parsed.Id, parsed.CanonicalUrl,
            meta.Title, null, meta.ThumbnailUrl, meta.DurationSeconds, "EXTERNAL_OPEN_ONLY", null);
    }

    public async Task<MediaDownloadResult> DownloadAudioAsync(MediaDownloadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await request.OnProgress(30, cancellationToken);
            var mp3Path = await externalProvider.DownloadToMp3Async(
                request.CanonicalUrl, request.WorkDirectory, request.BitrateKbps, cancellationToken);
            await request.OnProgress(95, cancellationToken);
            var size = File.Exists(mp3Path) ? new FileInfo(mp3Path).Length : 0;
            diagnostics.RecordSuccess(request.CorrelationId, "youtube", externalProvider.ProviderName, size);
            return new MediaDownloadResult(mp3Path, "youtube", externalProvider.ProviderName, size);
        }
        catch (ProviderException ex)
        {
            diagnostics.RecordFailure(request.CorrelationId, "youtube", externalProvider.ProviderName, "EXTERNAL_DOWNLOAD",
                DownloadErrorClassifier.ClassifyErrorCode(ex.Code));
            throw;
        }
    }
}

public sealed class MediaProviderRegistry(IEnumerable<IMediaProvider> providers)
{
    private readonly IReadOnlyList<IMediaProvider> providers = providers.ToArray();

    public IMediaProvider? Resolve(SourceUrl source) =>
        providers.FirstOrDefault(p =>
            !string.Equals(p.ProviderId, "external", StringComparison.OrdinalIgnoreCase) &&
            p.Supports(source))
        ?? providers.FirstOrDefault(p => p.Supports(source));

    public IReadOnlyList<string> RegisteredProviderIds =>
        providers.Select(p => p.ProviderId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

public sealed class DownloadOrchestrator(MediaProviderRegistry registry)
{
    public Task<MediaMetadata> GetMetadataAsync(string url, CancellationToken cancellationToken)
    {
        var source = SourceUrl.ParseVideo(url) ?? throw new ProviderException("INVALID_SOURCE_URL");
        var provider = registry.Resolve(source) ?? throw new ProviderException("UNSUPPORTED_SOURCE_URL");
        return provider.GetMetadataAsync(source.CanonicalUrl, cancellationToken);
    }

    public Task<MediaDownloadResult> DownloadAudioAsync(
        string canonicalUrl,
        string workDirectory,
        int? bitrateKbps,
        string correlationId,
        Func<int, CancellationToken, Task> onProgress,
        CancellationToken cancellationToken)
    {
        var source = SourceUrl.ParseVideo(canonicalUrl) ?? throw new ProviderException("INVALID_SOURCE_URL");
        if (!AudioBitrate.IsValid(bitrateKbps))
            throw new ProviderException("INVALID_BITRATE");
        var provider = registry.Resolve(source) ?? throw new ProviderException("UNSUPPORTED_SOURCE_URL");
        return provider.DownloadAudioAsync(
            new MediaDownloadRequest(source.CanonicalUrl, workDirectory, bitrateKbps, correlationId, onProgress),
            cancellationToken);
    }
}
