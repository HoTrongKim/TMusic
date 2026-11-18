using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TMusic.Application.Providers;

namespace TMusic.Infrastructure.Providers;

public sealed class TikTokMetadata(HttpClient http, TimeProvider clock)
{
    public async Task<ResolvedSource> ReadAsync(SourceUrl source, string original, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        ct = deadline.Token;
        using var response = await http.GetAsync("https://www.tiktok.com/oembed?url=" + Uri.EscapeDataString(source.CanonicalUrl),
            HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return YouTubeApi.Fallback(source, original, "METADATA_UNAVAILABLE");
        using var json = await SafeHttpTransport.ReadJsonAsync(response.Content, 131_072, ct);
        var root = json.RootElement;
        if (!root.TryGetProperty("provider_name", out var provider) || provider.GetString() != "TikTok")
            return YouTubeApi.Fallback(source, original, "METADATA_UNAVAILABLE");
        return new(source.Provider, source.Id, original, source.CanonicalUrl,
            YouTubeApi.Text(root, "title", 512), YouTubeApi.Text(root, "author_name", 256), Thumbnail(root), null,
            YouTubeApi.Text(root, "type", 32) == "video" && YouTubeApi.Text(root, "embed_product_id", 32) == source.Id
                ? "EMBED_SUPPORTED" : "EXTERNAL_OPEN_ONLY", clock.GetUtcNow(), null);
    }

    public static string? Thumbnail(JsonElement root)
    {
        if (YouTubeApi.Text(root, "thumbnail_url", 2048) is not { } url || !SourceUrl.TryHttps(url, out var uri))
            return null;
        var host = uri.Host;
        return host.EndsWith(".tiktokcdn.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".tiktokcdn-us.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".tiktokcdn-eu.com", StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri
            : null;
    }
}

public sealed class SoundCloudRedirectResolver(HttpClient http)
{
    public async Task<SourceUrl?> ResolveAsync(string input, CancellationToken ct)
    {
        if (!SourceUrl.TryHttps(input, out var current)) throw new ProviderException("INVALID_SOURCE_URL");
        if (SourceUrl.ParseVideo(input) is { Provider: "soundcloud" } canonical) return canonical;
        if (!SourceUrl.IsSoundCloudShort(current)) throw new ProviderException("INVALID_SOUNDCLOUD_URL");

        var visited = new HashSet<string>(StringComparer.Ordinal);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            for (var hop = 0; hop < 5; hop++)
            {
                if (!visited.Add(current.AbsoluteUri)) throw new ProviderException("UNSAFE_SOURCE_REDIRECT");
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                    HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)) return null;
                if (response.Headers.Location is not { } location || !Uri.TryCreate(current, location, out var target) ||
                    !SourceUrl.TryHttps(target.AbsoluteUri, out var validated)) throw new ProviderException("UNSAFE_SOURCE_REDIRECT");
                if (SourceUrl.ParseVideo(validated.AbsoluteUri) is { Provider: "soundcloud" } result) return result;
                if (!SourceUrl.IsSoundCloudShort(validated)) throw new ProviderException("UNSAFE_SOURCE_REDIRECT");
                current = validated;
            }
            throw new ProviderException("TOO_MANY_SOURCE_REDIRECTS");
        }
        catch (HttpRequestException) { return null; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }
}

public sealed class SoundCloudMetadata(HttpClient http, TimeProvider clock)
{
    public async Task<ResolvedSource> ReadAsync(SourceUrl source, string original, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        ct = deadline.Token;
        using var response = await http.GetAsync(
            "https://soundcloud.com/oembed?format=json&url=" + Uri.EscapeDataString(source.CanonicalUrl),
            HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return YouTubeApi.Fallback(source, original, "METADATA_UNAVAILABLE");
        using var json = await SafeHttpTransport.ReadJsonAsync(response.Content, 131_072, ct);
        var root = json.RootElement;
        if (!root.TryGetProperty("provider_name", out var provider) ||
            !string.Equals(provider.GetString(), "SoundCloud", StringComparison.OrdinalIgnoreCase))
            return YouTubeApi.Fallback(source, original, "METADATA_UNAVAILABLE");

        return new(
            source.Provider,
            source.Id,
            original,
            source.CanonicalUrl,
            YouTubeApi.Text(root, "title", 512),
            YouTubeApi.Text(root, "author_name", 256),
            Thumbnail(root),
            null,
            "EXTERNAL_OPEN_ONLY",
            clock.GetUtcNow(),
            null);
    }

    public static string? Thumbnail(JsonElement root)
    {
        if (YouTubeApi.Text(root, "thumbnail_url", 2048) is not { } url || !SourceUrl.TryHttps(url, out var uri))
            return null;
        return uri.Host.EndsWith(".sndcdn.com", StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri
            : null;
    }
}

public sealed class SourceResolver(
    YouTubeApi youtube,
    TikTokRedirectResolver redirect,
    TikTokMetadata tiktok,
    SoundCloudRedirectResolver soundCloudRedirect,
    SoundCloudMetadata soundCloud,
    IConfiguration configuration) : ISourceResolver
{
    public async Task<ResolvedSource> ResolveAsync(string input, CancellationToken cancellationToken)
    {
        if (!SourceUrl.TryHttps(input, out var uri)) throw new ProviderException("INVALID_SOURCE_URL");
        if ((SourceUrl.ParseVideo(input)?.Provider == "tiktok" || SourceUrl.IsTikTokShort(uri)) &&
            !configuration.GetValue("Features:EnableTikTokVideoImport", true))
            throw new ProviderException("TIKTOK_IMPORT_DISABLED");
        if ((SourceUrl.ParseVideo(input)?.Provider == "soundcloud" || SourceUrl.IsSoundCloudShort(uri)) &&
            !configuration.GetValue("Features:EnableSoundCloudImport", true))
            throw new ProviderException("SOUNDCLOUD_IMPORT_DISABLED");

        var original = input.Trim();
        var source = SourceUrl.ParseVideo(original);
        if (source is null)
        {
            if (SourceUrl.IsTikTokShort(uri))
            {
                source = await redirect.ResolveAsync(original, cancellationToken)
                    ?? throw new ProviderException("TIKTOK_SHORT_UNRESOLVED");
            }
            else if (SourceUrl.IsSoundCloudShort(uri))
            {
                source = await soundCloudRedirect.ResolveAsync(original, cancellationToken)
                    ?? throw new ProviderException("SOUNDCLOUD_SHORT_UNRESOLVED");
            }
            else
            {
                throw new ProviderException("UNSUPPORTED_SOURCE_URL");
            }
        }
        try
        {
            if (source.Provider == "youtube") return await youtube.ReadVideoAsync(source, original, cancellationToken);
            if (source.Provider == "soundcloud") return await soundCloud.ReadAsync(source, original, cancellationToken);
            if (!configuration.GetValue("Features:EnableTikTokMetadata", true))
                return YouTubeApi.Fallback(source, original, "METADATA_DISABLED");
            return await tiktok.ReadAsync(source, original, cancellationToken);
        }
        catch (ProviderException exception) { return YouTubeApi.Fallback(source, original, exception.Code); }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
            exception is HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        { return YouTubeApi.Fallback(source, original, "METADATA_UNAVAILABLE"); }
    }
}
