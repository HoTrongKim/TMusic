using System.Net;
using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.Configuration;
using TMusic.Application.Providers;

namespace TMusic.Infrastructure.Providers;

public sealed class YouTubeApi(HttpClient http, IConfiguration configuration, TimeProvider clock, ProviderQuota? quota = null) : IYouTubePlaylist
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["YouTube:ApiKey"]);

    public async Task<ResolvedSource> ReadVideoAsync(SourceUrl source, string originalUrl, CancellationToken ct)
    {
        if (!IsConfigured) return Fallback(source, originalUrl, "YOUTUBE_NOT_CONFIGURED");
        using var json = await ReadAsync("videos?part=snippet,contentDetails,status&id=" + source.Id, ct);
        var items = json.RootElement.GetProperty("items");
        if (items.GetArrayLength() == 0)
            return Fallback(source, originalUrl, "SOURCE_UNAVAILABLE") with
                { PlaybackCapability = "UNAVAILABLE", MetadataFetchedAt = clock.GetUtcNow() };
        var item = items[0];
        var snippet = item.GetProperty("snippet");
        return new(source.Provider, source.Id, originalUrl, source.CanonicalUrl,
            Text(snippet, "title", 512), Text(snippet, "channelTitle", 256), Thumbnail(snippet), Duration(item),
            Embeddable(item) ? "EMBED_SUPPORTED" : "EXTERNAL_OPEN_ONLY", clock.GetUtcNow(), null);
    }

    public async Task<PlaylistPage> ReadPageAsync(string playlistId, string? cursor, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new ProviderException("YOUTUBE_NOT_CONFIGURED");
        if (SourceUrl.ParsePlaylist($"https://www.youtube.com/playlist?list={playlistId}") != playlistId)
            throw new ProviderException("INVALID_PLAYLIST_URL");
        var id = Uri.EscapeDataString(playlistId);
        if (cursor is null)
        {
            using var playlist = await ReadAsync($"playlists?part=id&id={id}&fields=items(id)", cancellationToken);
            if (playlist.RootElement.GetProperty("items").GetArrayLength() == 0) throw new ProviderException("PLAYLIST_NOT_FOUND");
        }
        var pageToken = cursor is null ? "" : "&pageToken=" + Uri.EscapeDataString(cursor);
        using var listing = await ReadAsync($"playlistItems?part=snippet&maxResults=50&playlistId={id}{pageToken}" +
            "&fields=nextPageToken,items(snippet(position,resourceId(videoId)))", cancellationToken);
        var raw = listing.RootElement.GetProperty("items").EnumerateArray().Select(item =>
        {
            var snippet = item.GetProperty("snippet");
            var videoId = snippet.TryGetProperty("resourceId", out var resource) ? Text(resource, "videoId", 64) : null;
            return new PlaylistVideo(snippet.GetProperty("position").GetInt32(),
                SourceUrl.IsVideoId(videoId) ? videoId : null, null, null, null, null, false, false);
        }).ToArray();
        if (raw.Length > 50) throw new ProviderException("YOUTUBE_INVALID_RESPONSE");
        var ids = raw.Select(x => x.VideoId).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length > 0)
        {
            using var details = await ReadAsync("videos?part=snippet,status,contentDetails&id=" + string.Join(',', ids), cancellationToken);
            var available = details.RootElement.GetProperty("items").EnumerateArray()
                .Where(Available).ToDictionary(x => x.GetProperty("id").GetString()!, StringComparer.Ordinal);
            raw = raw.Select(video => video.VideoId is { } videoId && available.TryGetValue(videoId, out var detail)
                ? video with { Available = true, Embeddable = Embeddable(detail),
                    Title = Text(detail.GetProperty("snippet"), "title", 512),
                    Creator = Text(detail.GetProperty("snippet"), "channelTitle", 256),
                    ThumbnailUrl = Thumbnail(detail.GetProperty("snippet")), DurationSeconds = Duration(detail) }
                : video).ToArray();
        }
        return new(raw, Text(listing.RootElement, "nextPageToken", 2048));
    }

    private async Task<JsonDocument> ReadAsync(string path, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        ct = deadline.Token;
        if (quota is not null) await quota.ReserveAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Goog-Api-Key", configuration["YouTube:ApiKey"]);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.IsSuccessStatusCode) return await SafeHttpTransport.ReadJsonAsync(response.Content, 1_048_576, ct);
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            using var error = await SafeHttpTransport.ReadJsonAsync(response.Content, 65_536, ct);
            var quotaError = error.RootElement.ToString().Contains("quotaExceeded", StringComparison.Ordinal) ||
                error.RootElement.ToString().Contains("dailyLimitExceeded", StringComparison.Ordinal);
            throw new ProviderException(quotaError ? "YOUTUBE_QUOTA_EXCEEDED" : "SOURCE_PERMISSION_REQUIRED", quotaError);
        }
        if (response.StatusCode == HttpStatusCode.NotFound) throw new ProviderException("PLAYLIST_NOT_FOUND");
        if ((int)response.StatusCode is 429 or >= 500) throw new ProviderException("PROVIDER_TEMPORARILY_UNAVAILABLE", true);
        throw new ProviderException("PROVIDER_REQUEST_REJECTED");
    }

    private static bool Available(JsonElement item)
        => item.TryGetProperty("status", out var status) && Text(status, "privacyStatus", 16) is "public" or "unlisted" &&
            (!status.TryGetProperty("uploadStatus", out var upload) || upload.GetString() == "processed");
    private static bool Embeddable(JsonElement item)
        => Available(item) && item.GetProperty("status").TryGetProperty("embeddable", out var embed) && embed.ValueKind == JsonValueKind.True;
    internal static string? Text(JsonElement item, string key, int limit)
        => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text[..Math.Min(text.Length, limit)] : null;
    private static int? Duration(JsonElement item)
    {
        if (!item.TryGetProperty("contentDetails", out var details) || Text(details, "duration", 64) is not { } duration) return null;
        try { return (int)Math.Clamp(XmlConvert.ToTimeSpan(duration).TotalSeconds, 0, int.MaxValue); }
        catch (FormatException) { return null; }
    }
    private static string? Thumbnail(JsonElement snippet)
    {
        if (!snippet.TryGetProperty("thumbnails", out var thumbnails)) return null;
        foreach (var size in new[] { "medium", "default", "high" })
            if (thumbnails.TryGetProperty(size, out var data) && Text(data, "url", 2048) is { } url &&
                SourceUrl.TryHttps(url, out var uri) && uri.Host is "i.ytimg.com" or "img.youtube.com") return uri.AbsoluteUri;
        return null;
    }
    public static ResolvedSource Fallback(SourceUrl source, string original, string notice)
        => new(source.Provider, source.Id, original, source.CanonicalUrl, null, null, null, null, "EXTERNAL_OPEN_ONLY", null, notice);
}

