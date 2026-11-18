using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace TMusic.Application.Providers;

public sealed record SourceUrl(string Provider, string Id, string CanonicalUrl)
{
    private static readonly Regex VideoPattern = new(@"\A[A-Za-z0-9_-]{11}\z", RegexOptions.NonBacktracking);
    private static readonly Regex PlaylistPattern = new(@"\A[A-Za-z0-9_-]{10,128}\z", RegexOptions.NonBacktracking);
    private static readonly Regex TikTokPattern = new(@"\A/@(?<creator>[A-Za-z0-9_.]{1,32})/(?<kind>video|photo)/(?<id>[0-9]{1,24})/?\z", RegexOptions.NonBacktracking);
    private static readonly Regex SoundCloudTrackPattern = new(@"\A/([A-Za-z0-9_-]{1,48})/([A-Za-z0-9_-]{1,78})/?\z", RegexOptions.NonBacktracking);
    private static readonly Regex ShortPattern = new(@"\A/[A-Za-z0-9_-]{3,64}/?\z", RegexOptions.NonBacktracking);

    private static readonly HashSet<string> ReservedSoundCloudRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "discover", "search", "stream", "you", "settings", "upload", "messages",
        "notifications", "charts", "pages", "terms-of-use", "imprint", "oembed",
        "stations", "people", "signin", " logout"
    };

    private static readonly HashSet<string> ReservedSoundCloudSubpaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "sets", "likes", "reposts", "tracks", "albums", "playlists",
        "popular-tracks", "followers", "following", "comments", "spotlight"
    };

    public static bool TryHttps(string? input, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (input is not { Length: > 0 and <= 2048 }) return false;
        input = input.Trim();
        if (input.Length == 0 || input.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\') ||
            !Uri.TryCreate(input, UriKind.Absolute, out var parsed) || parsed.Scheme != "https" ||
            parsed.UserInfo.Length != 0 || !parsed.IsDefaultPort || parsed.HostNameType != UriHostNameType.Dns ||
            parsed.IdnHost != parsed.Host || parsed.Host.EndsWith(".", StringComparison.Ordinal)) return false;
        uri = parsed;
        return true;
    }

    public static bool IsVideoId(string? id) => id is not null && VideoPattern.IsMatch(id);
    public static string Watch(string id) => $"https://www.youtube.com/watch?v={id}";

    public static SourceUrl? ParseVideo(string? input)
    {
        if (!TryHttps(input, out var uri)) return null;
        if (uri.Host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com")
        {
            var id = uri.AbsolutePath == "/watch" ? SingleQuery(uri, "v") :
                uri.AbsolutePath.StartsWith("/shorts/", StringComparison.Ordinal) ? uri.AbsolutePath[8..].TrimEnd('/') :
                uri.AbsolutePath.StartsWith("/live/", StringComparison.Ordinal) ? uri.AbsolutePath[6..].TrimEnd('/') : null;
            return IsVideoId(id) ? new("youtube", id!, Watch(id!)) : null;
        }
        if (uri.Host == "youtu.be")
        {
            var id = uri.AbsolutePath.Trim('/');
            return IsVideoId(id) ? new("youtube", id, Watch(id)) : null;
        }
        if (uri.Host is "www.tiktok.com" or "tiktok.com" or "m.tiktok.com")
        {
            var match = TikTokPattern.Match(uri.AbsolutePath);
            return match.Success ? new("tiktok", match.Groups["id"].Value,
                $"https://www.tiktok.com/@{match.Groups["creator"].Value}/{match.Groups["kind"].Value}/{match.Groups["id"].Value}") : null;
        }
        if (uri.Host is "soundcloud.com" or "www.soundcloud.com" or "m.soundcloud.com")
        {
            var sc = SoundCloudTrackPattern.Match(uri.AbsolutePath);
            if (!sc.Success) return null;
            var artist = sc.Groups[1].Value;
            var track = sc.Groups[2].Value;
            if (ReservedSoundCloudRoots.Contains(artist) || ReservedSoundCloudSubpaths.Contains(track))
                return null;
            var normalizedId = $"{artist.ToLowerInvariant()}/{track.ToLowerInvariant()}";
            return new("soundcloud", normalizedId, $"https://soundcloud.com/{artist}/{track}");
        }
        return null;
    }

    public static string? ParsePlaylist(string? input)
    {
        if (!TryHttps(input, out var uri)) return null;
        var supported = uri.Host switch
        {
            "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com" => uri.AbsolutePath is "/playlist" or "/watch",
            "youtu.be" => IsVideoId(uri.AbsolutePath.Trim('/')),
            _ => false
        };
        var id = supported ? SingleQuery(uri, "list") : null;
        return id is not null && PlaylistPattern.IsMatch(id) ? id : null;
    }

    public static bool IsTikTokShort(Uri uri) => uri.Host switch
    {
        "vt.tiktok.com" or "vm.tiktok.com" => ShortPattern.IsMatch(uri.AbsolutePath),
        "www.tiktok.com" or "tiktok.com" => uri.AbsolutePath.StartsWith("/t/", StringComparison.Ordinal) && ShortPattern.IsMatch(uri.AbsolutePath[2..]),
        _ => false
    };

    public static bool IsSoundCloudShort(Uri uri) =>
        uri.Host == "on.soundcloud.com" && ShortPattern.IsMatch(uri.AbsolutePath);

    private static string? SingleQuery(Uri uri, string key)
    {
        var found = uri.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2))
            .Where(x => Uri.UnescapeDataString(x[0]) == key).ToArray();
        return found is [[_, var value]] ? Uri.UnescapeDataString(value) : null;
    }
}
