namespace TMusic.Application.Providers;

public sealed record ResolvedSource(
    string Provider, string ProviderContentId, string OriginalUrl, string CanonicalUrl,
    string? OriginalTitle, string? OriginalCreator, string? ThumbnailUrl, int? DurationSeconds,
    string PlaybackCapability, DateTimeOffset? MetadataFetchedAt, string? Notice);

public interface ISourceResolver
{
    Task<ResolvedSource> ResolveAsync(string input, CancellationToken cancellationToken);
}

public interface IYouTubePlaylist
{
    bool IsConfigured { get; }
    Task<PlaylistPage> ReadPageAsync(string playlistId, string? cursor, CancellationToken cancellationToken);
}

public sealed record PlaylistVideo(int Position, string? VideoId, string? Title, string? Creator,
    string? ThumbnailUrl, int? DurationSeconds, bool Available, bool Embeddable);
public sealed record PlaylistPage(IReadOnlyList<PlaylistVideo> Videos, string? NextPage);
public sealed record PlaylistDecision(PlaylistVideo Video, string Status);

public sealed class ProviderException(string code, bool retryable = false) : Exception(code)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}

public static class PlaylistDeduplication
{
    public static IReadOnlyList<PlaylistDecision> Classify(IEnumerable<PlaylistVideo> videos, ISet<string> seen)
        => videos.OrderBy(x => x.Position).Select(video => new PlaylistDecision(video,
            !video.Available || !SourceUrl.IsVideoId(video.VideoId) ? "UNAVAILABLE" :
            seen.Add(video.VideoId!) ? "ACCEPTED" : "DUPLICATE")).ToArray();
}
