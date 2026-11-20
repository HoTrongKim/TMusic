using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TMusic.Application.Downloads;
using TMusic.Application.Providers;
using TMusic.Infrastructure.Providers;
using TMusic.Infrastructure.Storage;

namespace TMusic.Infrastructure.Downloads;

public sealed class ExternalMediaProviderOptions
{
    public const string SectionName = "MediaProviders:External";

    public bool Enabled { get; set; }
    public string Provider { get; set; } = "tunelio";
    public string BaseUrl { get; set; } = "https://tunelio.dev";
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public string[] AllowedDownloadHosts { get; set; } = [];

    public TimeSpan EffectiveTimeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 5, 120));

    public bool TryGetValidatedBaseUri(out Uri? uri)
    {
        uri = null;
        if (!SourceUrl.TryHttps(BaseUrl, out var parsed)) return false;
        if (parsed.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            parsed.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            parsed.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            return false;
        uri = parsed;
        return true;
    }

    public IReadOnlySet<string> BuildAllowedDownloadHosts()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (TryGetValidatedBaseUri(out var baseUri) && baseUri is not null)
            set.Add(baseUri.Host);
        foreach (var host in AllowedDownloadHosts ?? [])
        {
            var trimmed = host?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && !IPAddress.TryParse(trimmed, out _))
                set.Add(trimmed);
        }
        return set;
    }
}

/// <summary>
/// Optional documented third-party audio provider adapter (Tunelio API: GET /info, GET /create).
/// Disabled by default (MediaProviders:External:Enabled=false). Requires server-side API credentials,
/// strict HTTPS host allowlisting, public IP pinning, no redirect following, size caps, and MP3 validation.
/// Note: Undocumented/unverified sites such as hiyoutube.com are intentionally excluded.
/// </summary>
public sealed class TunelioExternalAudioProvider(
    HttpClient http,
    IOptions<ExternalMediaProviderOptions> options,
    IAudioStorage storage,
    TimeProvider clock) : IExternalAudioProvider
{
    private const int MaxJsonResponseBytes = 256 * 1024;

    public string ProviderName => string.IsNullOrWhiteSpace(options.Value.Provider)
        ? "tunelio"
        : options.Value.Provider.Trim().ToLowerInvariant();

    public bool IsEnabled => options.Value.Enabled && IsConfigured();

    public bool IsConfigured() =>
        options.Value.TryGetValidatedBaseUri(out _) &&
        !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public ExternalProviderStatus GetProviderStatus()
    {
        var opt = options.Value;
        var hasValidBase = opt.TryGetValidatedBaseUri(out var baseUri);
        var configured = hasValidBase && !string.IsNullOrWhiteSpace(opt.ApiKey);
        var status = !opt.Enabled
            ? "disabled"
            : configured ? "configured" : "misconfigured";
        return new ExternalProviderStatus(ProviderName, opt.Enabled, configured, status, hasValidBase ? baseUri!.Host : null);
    }

    public Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken)
        => Task.FromResult(IsEnabled);

    public async Task<ExternalAudioMetadata> GetMetadataAsync(string canonicalUrl, CancellationToken cancellationToken)
    {
        EnsureReady(out var baseUri, out var apiKey);
        var parsed = SourceUrl.ParseVideo(canonicalUrl);
        if (parsed is null || parsed.Provider != "youtube")
            throw new ProviderException("UNSUPPORTED_SOURCE_URL");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(options.Value.EffectiveTimeout);

        var endpoint = BuildEndpoint(baseUri, "/info", $"url={Uri.EscapeDataString(parsed.CanonicalUrl)}");
        using var request = CreateAuthorizedRequest(endpoint, apiKey);
        using var response = await SendSafelyAsync(request, cts.Token);
        ValidateApiStatus(response.StatusCode);

        using var json = await ReadJsonSafelyAsync(response.Content, cts.Token);
        var root = json.RootElement;
        if (!IsOkStatus(root))
            throw new ProviderException("EXTERNAL_PROVIDER_INVALID_RESPONSE");

        var title = ReadString(root, "title", 512) ?? parsed.Id;
        int? duration = null;
        if (root.TryGetProperty("duration_seconds", out var ds) && ds.ValueKind == JsonValueKind.Number && ds.TryGetInt32(out var secDs))
            duration = secDs;
        else if (root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out var sec))
            duration = sec;
        var thumbnail = ReadString(root, "thumbnail", 2048);
        string? container = null;
        string? sizeLabel = null;
        if (root.TryGetProperty("audioFormat", out var audioFmt) && audioFmt.ValueKind == JsonValueKind.Object)
        {
            container = ReadString(audioFmt, "format", 32) ?? ReadString(audioFmt, "container", 32);
            sizeLabel = ReadString(audioFmt, "file_size_str", 64);
        }

        return new ExternalAudioMetadata(title, duration, thumbnail, container, sizeLabel);
    }

    public async Task<ExternalAudioDownload> CreateDownloadAsync(
        string canonicalUrl,
        int? bitrateKbps,
        CancellationToken cancellationToken)
    {
        EnsureReady(out var baseUri, out var apiKey);
        if (!AudioBitrate.IsValid(bitrateKbps))
            throw new ProviderException("INVALID_BITRATE");
        var parsed = SourceUrl.ParseVideo(canonicalUrl);
        if (parsed is null || parsed.Provider != "youtube")
            throw new ProviderException("UNSUPPORTED_SOURCE_URL");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(options.Value.EffectiveTimeout);

        var endpoint = BuildEndpoint(baseUri, "/create",
            $"url={Uri.EscapeDataString(parsed.CanonicalUrl)}&quality=mp3");
        using var request = CreateAuthorizedRequest(endpoint, apiKey);
        using var response = await SendSafelyAsync(request, cts.Token);
        ValidateApiStatus(response.StatusCode);

        using var json = await ReadJsonSafelyAsync(response.Content, cts.Token);
        var root = json.RootElement;
        if (!IsOkStatus(root))
            throw new ProviderException("EXTERNAL_PROVIDER_INVALID_RESPONSE");

        var rawDownloadUrl = ReadString(root, "url", 4096);
        if (!ValidateDownloadUri(rawDownloadUrl, out var downloadUri))
            throw new ProviderException("EXTERNAL_PROVIDER_INVALID_RESPONSE");

        DateTimeOffset? expiresAt = ParseExpiry(root, "expires_at", clock) ?? ParseExpiry(root, "expires", clock);
        if (expiresAt is not null && expiresAt.Value <= clock.GetUtcNow())
            throw new ProviderException("EXTERNAL_DOWNLOAD_URL_EXPIRED", retryable: true);

        return new ExternalAudioDownload(
            DownloadUrl: downloadUri!,
            FileName: ReadString(root, "filename", 256),
            Quality: ReadString(root, "quality", 32) ?? "mp3",
            Mode: ReadString(root, "mode", 32) ?? "audio",
            ExpiresAtUtc: expiresAt);
    }

    public async Task<string> DownloadToMp3Async(
        string canonicalUrl,
        string workDirectory,
        int? bitrateKbps,
        CancellationToken cancellationToken)
    {
        var download = await CreateDownloadAsync(canonicalUrl, bitrateKbps, cancellationToken);
        if (download.ExpiresAtUtc is not null && download.ExpiresAtUtc.Value <= clock.GetUtcNow())
            throw new ProviderException("EXTERNAL_DOWNLOAD_URL_EXPIRED", retryable: true);

        Directory.CreateDirectory(workDirectory);
        var outputPath = Path.Combine(Path.GetFullPath(workDirectory), "track.mp3");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var streamTimeout = TimeSpan.FromSeconds(Math.Max(options.Value.EffectiveTimeout.TotalSeconds, 180));
        cts.CancelAfter(streamTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, download.DownloadUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));
        using var response = await SendSafelyAsync(request, cts.Token);

        if (response.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            throw new ProviderException("EXTERNAL_DOWNLOAD_URL_EXPIRED", retryable: true);
        if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or
            HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            throw new ProviderException("UNSAFE_SOURCE_REDIRECT");
        ValidateApiStatus(response.StatusCode);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!string.IsNullOrWhiteSpace(contentType) &&
            !contentType.Equals("audio/mpeg", StringComparison.OrdinalIgnoreCase) &&
            !contentType.Equals("audio/mp3", StringComparison.OrdinalIgnoreCase) &&
            !contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            throw new ProviderException("AUDIO_FORMAT_INVALID");

        if (response.Content.Headers.ContentLength is { } contentLen &&
            (contentLen <= 16 || contentLen > AudioStorage.MaxAudioBytes))
            throw new ProviderException("AUDIO_FILE_INVALID");

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        await using var file = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 32 * 1024, useAsync: true);
        var buffer = new byte[32 * 1024];
        var header = new byte[16];
        var headerFilled = 0;
        long total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cts.Token);
            if (read == 0) break;
            total += read;
            if (total > AudioStorage.MaxAudioBytes)
            {
                file.Close();
                try { File.Delete(outputPath); } catch (IOException) { }
                throw new ProviderException("AUDIO_FILE_INVALID");
            }
            if (headerFilled < header.Length)
            {
                var take = Math.Min(header.Length - headerFilled, read);
                Buffer.BlockCopy(buffer, 0, header, headerFilled, take);
                headerFilled += take;
            }
            await file.WriteAsync(buffer.AsMemory(0, read), cts.Token);
        }

        if (total <= 16 || headerFilled < 4 || !storage.IsValidMp3Header(header.AsSpan(0, headerFilled)))
        {
            file.Close();
            try { File.Delete(outputPath); } catch (IOException) { }
            throw new ProviderException("AUDIO_FILE_INVALID");
        }

        return outputPath;
    }

    public bool ValidateDownloadUri(string? rawUrl, out Uri? downloadUri)
    {
        downloadUri = null;
        if (!SourceUrl.TryHttps(rawUrl, out var candidate)) return false;
        var allowedHosts = options.Value.BuildAllowedDownloadHosts();
        var host = candidate.Host;
        var matched = allowedHosts.Any(allowed =>
            host.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase));
        if (!matched) return false;
        downloadUri = candidate;
        return true;
    }

    private void EnsureReady(out Uri baseUri, out string apiKey)
    {
        if (!options.Value.Enabled)
            throw new ProviderException("EXTERNAL_PROVIDER_UNCONFIGURED");
        if (!options.Value.TryGetValidatedBaseUri(out var validated) || validated is null ||
            string.IsNullOrWhiteSpace(options.Value.ApiKey))
            throw new ProviderException("EXTERNAL_PROVIDER_UNCONFIGURED");
        baseUri = validated;
        apiKey = options.Value.ApiKey.Trim();
    }

    private static Uri BuildEndpoint(Uri baseUri, string path, string query)
    {
        var builder = new UriBuilder(baseUri)
        {
            Path = path,
            Query = query
        };
        return builder.Uri;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(Uri endpoint, string apiKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return req;
    }

    private async Task<HttpResponseMessage> SendSafelyAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ProviderException("AUDIO_DOWNLOAD_TIMEOUT", retryable: true);
        }
        catch (HttpRequestException)
        {
            throw new ProviderException("SOURCE_NETWORK_ERROR", retryable: true);
        }
    }

    private static async Task<JsonDocument> ReadJsonSafelyAsync(HttpContent content, CancellationToken ct)
    {
        try
        {
            return await SafeHttpTransport.ReadJsonAsync(content, MaxJsonResponseBytes, ct);
        }
        catch (JsonException)
        {
            throw new ProviderException("EXTERNAL_PROVIDER_INVALID_RESPONSE");
        }
    }

    private static void ValidateApiStatus(HttpStatusCode status)
    {
        if ((int)status is >= 200 and < 300) return;
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new ProviderException("EXTERNAL_PROVIDER_AUTH_FAILED");
        if (status == HttpStatusCode.TooManyRequests)
            throw new ProviderException("RATE_LIMITED", retryable: true);
        if (status == HttpStatusCode.NotFound)
            throw new ProviderException("SOURCE_UNAVAILABLE");
        if ((int)status >= 500 || status == HttpStatusCode.RequestTimeout)
            throw new ProviderException("PROVIDER_TEMPORARILY_UNAVAILABLE", retryable: true);
        throw new ProviderException("EXTERNAL_PROVIDER_INVALID_RESPONSE");
    }

    private static bool IsOkStatus(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (!root.TryGetProperty("status", out var status)) return true;
        return status.ValueKind == JsonValueKind.String &&
               string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? ParseExpiry(JsonElement root, string propertyName, TimeProvider clock)
    {
        if (!root.TryGetProperty(propertyName, out var exp)) return null;
        if (exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out var num))
        {
            if (num >= 1_000_000_000_000L) return DateTimeOffset.FromUnixTimeMilliseconds(num);
            if (num >= 1_000_000_000L) return DateTimeOffset.FromUnixTimeSeconds(num);
            if (num > 0) return clock.GetUtcNow().AddSeconds(num);
        }
        else if (exp.ValueKind == JsonValueKind.String &&
                 DateTimeOffset.TryParse(exp.GetString(), out var dt))
        {
            return dt.ToUniversalTime();
        }
        return null;
    }

    private static string? ReadString(JsonElement root, string name, int maxLength)
    {
        if (!root.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String) return null;
        var val = prop.GetString()?.Trim();
        if (string.IsNullOrEmpty(val)) return null;
        return val.Length <= maxLength ? val : val[..maxLength];
    }
}
