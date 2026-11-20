using TMusic.Application.Providers;

namespace TMusic.Application.Downloads;

public enum DownloadErrorCategory
{
    None,
    BotDetected,
    SourceAccessCheckRequired,
    SourceIpRestricted,
    ExtractorOutdated,
    JsRuntimeMissing,
    EjsComponentMissing,
    FfmpegMissing,
    RateLimited,
    Http403,
    Http429,
    NetworkTimeout,
    ConnectionReset,
    InvalidSource,
    UnauthorizedContent,
    FormatUnavailable,
    StorageError,
    DownloadInterrupted,
    ExternalProviderError,
    UnknownError
}

public enum DownloadFailureScope
{
    Unknown,
    Video,
    Extractor,
    BackendEnvironment,
    ProviderAccount,
    Network,
    Storage,
    ExternalProvider
}

public sealed record DownloadErrorClassification(
    string ErrorCode,
    DownloadErrorCategory Category,
    DownloadFailureScope Scope,
    bool Retryable,
    bool IsAccessRestriction,
    string DiagnosticSummary)
{
    public ProviderException ToProviderException() => new(ErrorCode, Retryable);
}

public static class DownloadErrorClassifier
{
    public static DownloadErrorClassification Classify(string provider, string standardError)
    {
        var text = standardError ?? string.Empty;

        if (text.Contains("TMUSIC_YTDLP_COMPATIBILITY_ERROR", StringComparison.Ordinal))
            return new("AUDIO_ENGINE_UNAVAILABLE", DownloadErrorCategory.ExtractorOutdated, DownloadFailureScope.Extractor,
                Retryable: false, IsAccessRestriction: false, "Pinned yt-dlp compatibility guard rejected the runtime bundle.");

        if (text.Contains("TMUSIC_SOURCE_ACCESS_CHECK_REQUIRED", StringComparison.Ordinal))
            return new("SOURCE_ACCESS_CHECK_REQUIRED", DownloadErrorCategory.SourceAccessCheckRequired, DownloadFailureScope.BackendEnvironment,
                Retryable: false, IsAccessRestriction: true, "Provider requested an interactive access check.");

        if (text.Contains("TMUSIC_SOURCE_AUDIO_UNAVAILABLE", StringComparison.Ordinal))
            return new("SOURCE_AUDIO_UNAVAILABLE", DownloadErrorCategory.FormatUnavailable, DownloadFailureScope.Video,
                Retryable: false, IsAccessRestriction: false, "Source does not expose a publicly downloadable audio stream.");

        if (text.Contains("Sign in to confirm you're not a bot", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Sign in to confirm you\u2019re not a bot", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("not a bot", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("access check required", StringComparison.OrdinalIgnoreCase))
            return new("SOURCE_ACCESS_CHECK_REQUIRED", DownloadErrorCategory.BotDetected, DownloadFailureScope.BackendEnvironment,
                Retryable: false, IsAccessRestriction: true, "YouTube requested bot verification from the current backend environment.");

        if (text.Contains("Sign in", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("login required", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("authentication required", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("HTTP Error 401", StringComparison.OrdinalIgnoreCase))
            return new("SOURCE_AUTHENTICATION_REQUIRED", DownloadErrorCategory.UnauthorizedContent, DownloadFailureScope.ProviderAccount,
                Retryable: false, IsAccessRestriction: true, "Source requires account authentication.");

        if (text.Contains("No supported JavaScript runtime", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("JavaScript runtime could be found", StringComparison.OrdinalIgnoreCase))
            return new("AUDIO_ENGINE_UNAVAILABLE", DownloadErrorCategory.JsRuntimeMissing, DownloadFailureScope.Extractor,
                Retryable: false, IsAccessRestriction: false, "JavaScript runtime required by extractor is missing.");

        if (text.Contains("yt-dlp-ejs", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("challenge solver script", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Remote components", StringComparison.OrdinalIgnoreCase) && text.Contains("ejs", StringComparison.OrdinalIgnoreCase))
            return new("AUDIO_ENGINE_UNAVAILABLE", DownloadErrorCategory.EjsComponentMissing, DownloadFailureScope.Extractor,
                Retryable: false, IsAccessRestriction: false, "Extractor EJS challenge-solver component is missing.");

        if (text.Contains("Requested format is not available", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("No video formats found", StringComparison.OrdinalIgnoreCase))
            return new("SOURCE_AUDIO_UNAVAILABLE", DownloadErrorCategory.FormatUnavailable, DownloadFailureScope.Video,
                Retryable: false, IsAccessRestriction: false, "No compatible audio format available for this source.");

        if (text.Contains("HTTP Error 403", StringComparison.OrdinalIgnoreCase))
            return new("SOURCE_ACCESS_FORBIDDEN", DownloadErrorCategory.Http403, DownloadFailureScope.BackendEnvironment,
                Retryable: false, IsAccessRestriction: true, "Source rejected media access with HTTP 403 Forbidden.");

        var nonRetryable = text.Contains("DRM", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("Private video", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("members-only", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("Video unavailable", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("This video is unavailable", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("File is larger than max-filesize", StringComparison.OrdinalIgnoreCase);
        if (nonRetryable)
            return new("SOURCE_RESTRICTED_OR_UNSUPPORTED", DownloadErrorCategory.UnauthorizedContent, DownloadFailureScope.Video,
                Retryable: false, IsAccessRestriction: false, "Content is private, DRM-protected, unavailable, or exceeds size limits.");

        if (text.Contains("HTTP Error 429", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase))
            return new("RATE_LIMITED", DownloadErrorCategory.Http429, DownloadFailureScope.BackendEnvironment,
                Retryable: true, IsAccessRestriction: false, "Provider rate-limited requests (HTTP 429).");

        if (text.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("HTTP Error 408", StringComparison.OrdinalIgnoreCase))
            return new("SOURCE_NETWORK_ERROR", DownloadErrorCategory.NetworkTimeout, DownloadFailureScope.Network,
                Retryable: true, IsAccessRestriction: false, "Source network request timed out.");

        if (text.Contains("connection reset", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("HTTP Error 500", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("HTTP Error 502", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("HTTP Error 503", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("HTTP Error 504", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Temporary failure in name resolution", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("network is unreachable", StringComparison.OrdinalIgnoreCase))
            return new("SOURCE_NETWORK_ERROR", DownloadErrorCategory.ConnectionReset, DownloadFailureScope.Network,
                Retryable: true, IsAccessRestriction: false, "Transient network or upstream server error.");

        if (text.Contains("Unable to extract", StringComparison.OrdinalIgnoreCase))
            return new("SOURCE_RESPONSE_UNSUPPORTED", DownloadErrorCategory.ExtractorOutdated, DownloadFailureScope.Extractor,
                Retryable: false, IsAccessRestriction: false, "Extractor could not parse the upstream page response.");

        if (string.Equals(provider, "tiktok", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("Unexpected response from webpage request", StringComparison.OrdinalIgnoreCase))
            return new("SOURCE_RESPONSE_UNSUPPORTED", DownloadErrorCategory.ExtractorOutdated, DownloadFailureScope.Extractor,
                Retryable: false, IsAccessRestriction: false, "Unexpected TikTok webpage payload.");

        return new("AUDIO_EXTRACTION_FAILED", DownloadErrorCategory.UnknownError, DownloadFailureScope.Unknown,
            Retryable: true, IsAccessRestriction: false, "Unclassified audio extraction failure.");
    }

    public static DownloadErrorClassification ClassifyErrorCode(string? errorCode)
    {
        var normalized = errorCode?.Trim().ToUpperInvariant() ?? string.Empty;
        return normalized switch
        {
            "SOURCE_ACCESS_CHECK_REQUIRED" => new(normalized, DownloadErrorCategory.SourceAccessCheckRequired,
                DownloadFailureScope.BackendEnvironment, false, true, "Provider access verification required."),
            "SOURCE_AUTHENTICATION_REQUIRED" => new(normalized, DownloadErrorCategory.UnauthorizedContent,
                DownloadFailureScope.ProviderAccount, false, true, "Provider authentication required."),
            "SOURCE_ACCESS_FORBIDDEN" => new(normalized, DownloadErrorCategory.Http403,
                DownloadFailureScope.BackendEnvironment, false, true, "Provider denied access (HTTP 403)."),
            "SOURCE_AUDIO_UNAVAILABLE" => new(normalized, DownloadErrorCategory.FormatUnavailable,
                DownloadFailureScope.Video, false, false, "Public audio format unavailable."),
            "SOURCE_RESTRICTED_OR_UNSUPPORTED" or "SOURCE_UNAVAILABLE" => new(normalized, DownloadErrorCategory.UnauthorizedContent,
                DownloadFailureScope.Video, false, false, "Source content restricted or unavailable."),
            "SOURCE_RESPONSE_UNSUPPORTED" => new(normalized, DownloadErrorCategory.ExtractorOutdated,
                DownloadFailureScope.Extractor, false, false, "Source response unsupported."),
            "AUDIO_CONVERSION_FAILED" or "AUDIO_INPUT_INVALID" => new(normalized, DownloadErrorCategory.FfmpegMissing,
                DownloadFailureScope.Extractor, false, false, "FFmpeg audio conversion failed."),
            "AUDIO_ENGINE_UNAVAILABLE" or "AUDIO_DOWNLOAD_DISABLED" => new(normalized, DownloadErrorCategory.ExtractorOutdated,
                DownloadFailureScope.Extractor, false, false, "Audio extraction engine unavailable."),
            "AUDIO_STORAGE_REJECTED" or "AUDIO_FILE_INVALID" or "AUDIO_FORMAT_INVALID" or "AUDIO_PATH_INVALID" => new(normalized,
                DownloadErrorCategory.StorageError, DownloadFailureScope.Storage, false, false, "Audio storage validation rejected file."),
            "AUDIO_STORAGE_UNAVAILABLE" => new(normalized, DownloadErrorCategory.StorageError,
                DownloadFailureScope.Storage, true, false, "Audio storage temporarily unavailable."),
            "RATE_LIMITED" => new(normalized, DownloadErrorCategory.RateLimited,
                DownloadFailureScope.BackendEnvironment, true, false, "Rate limited by provider."),
            "AUDIO_DOWNLOAD_TIMEOUT" => new(normalized, DownloadErrorCategory.NetworkTimeout,
                DownloadFailureScope.Network, true, false, "Audio download timed out."),
            "SOURCE_NETWORK_ERROR" or "NETWORK_ERROR" or "TEMPORARY_FAILURE" or "PROVIDER_TEMPORARILY_UNAVAILABLE" => new(normalized,
                DownloadErrorCategory.ConnectionReset, DownloadFailureScope.Network, true, false, "Transient network or provider failure."),
            "AUDIO_DOWNLOAD_BUSY" => new(normalized, DownloadErrorCategory.DownloadInterrupted,
                DownloadFailureScope.BackendEnvironment, true, false, "Download worker at concurrency limit."),
            "EXTERNAL_PROVIDER_AUTH_FAILED" or "EXTERNAL_PROVIDER_UNCONFIGURED" or "EXTERNAL_PROVIDER_INVALID_RESPONSE" => new(normalized,
                DownloadErrorCategory.ExternalProviderError, DownloadFailureScope.ExternalProvider, false, false, "External provider rejected or returned invalid data."),
            "EXTERNAL_DOWNLOAD_URL_EXPIRED" => new(normalized, DownloadErrorCategory.ExternalProviderError,
                DownloadFailureScope.ExternalProvider, true, false, "Signed external download URL expired before retrieval."),
            "INVALID_SOURCE_URL" or "UNSUPPORTED_SOURCE_URL" or "RIGHTS_CONFIRMATION_REQUIRED" or "INVALID_BITRATE" => new(normalized,
                DownloadErrorCategory.InvalidSource, DownloadFailureScope.Video, false, false, "Invalid download request."),
            "AUDIO_EXTRACTION_FAILED" => new(normalized, DownloadErrorCategory.UnknownError,
                DownloadFailureScope.Unknown, true, false, "Extraction failed."),
            _ => new(string.IsNullOrWhiteSpace(normalized) ? "UNKNOWN_ERROR" : normalized, DownloadErrorCategory.UnknownError,
                DownloadFailureScope.Unknown, false, false, "Unrecognized failure code.")
        };
    }

    public static bool IsRetryableErrorCode(string? errorCode) => ClassifyErrorCode(errorCode).Retryable;

    public static bool IsAccessRestrictionCode(string? errorCode) => ClassifyErrorCode(errorCode).IsAccessRestriction;
}

public sealed record MediaMetadata(
    string Provider,
    string ProviderContentId,
    string CanonicalUrl,
    string? Title,
    string? Creator,
    string? ThumbnailUrl,
    int? DurationSeconds,
    string PlaybackCapability,
    string? Notice = null);

public sealed record MediaDownloadRequest(
    string CanonicalUrl,
    string WorkDirectory,
    int? BitrateKbps,
    string CorrelationId,
    Func<int, CancellationToken, Task> OnProgress);

public sealed record MediaDownloadResult(
    string Mp3FilePath,
    string ProviderId,
    string EngineName,
    long SizeBytes);

public interface IMediaProvider
{
    string ProviderId { get; }
    bool Supports(SourceUrl source);
    Task<MediaMetadata> GetMetadataAsync(string url, CancellationToken cancellationToken);
    Task<MediaDownloadResult> DownloadAudioAsync(MediaDownloadRequest request, CancellationToken cancellationToken);
}

public sealed record ExternalAudioMetadata(
    string Title,
    int? DurationSeconds,
    string? ThumbnailUrl,
    string? AudioContainer,
    string? FileSizeLabel);

public sealed record ExternalAudioDownload(
    Uri DownloadUrl,
    string? FileName,
    string? Quality,
    string? Mode,
    DateTimeOffset? ExpiresAtUtc);

public sealed record ExternalProviderStatus(
    string ProviderName,
    bool Enabled,
    bool Configured,
    string Status,
    string? BaseHost);

public interface IExternalAudioProvider
{
    string ProviderName { get; }
    bool IsEnabled { get; }
    Task<ExternalAudioMetadata> GetMetadataAsync(string canonicalUrl, CancellationToken cancellationToken);
    Task<ExternalAudioDownload> CreateDownloadAsync(string canonicalUrl, int? bitrateKbps, CancellationToken cancellationToken);
    Task<string> DownloadToMp3Async(string canonicalUrl, string workDirectory, int? bitrateKbps, CancellationToken cancellationToken);
    Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken);
    ExternalProviderStatus GetProviderStatus();
}
