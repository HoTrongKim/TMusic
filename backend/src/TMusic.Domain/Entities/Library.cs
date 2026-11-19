namespace TMusic.Domain.Entities;

public sealed class Song
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Provider { get; set; }
    public required string ProviderContentId { get; set; }
    public required string CanonicalUrl { get; set; }
    public required string OriginalUrl { get; set; }
    public string? OriginalTitle { get; set; }
    public string? OriginalCreator { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int? DurationSeconds { get; set; }
    public DateTimeOffset? MetadataFetchedAt { get; set; }
    public DateTimeOffset? MetadataExpiresAt { get; set; }
    public string PlaybackCapability { get; set; } = "EXTERNAL_OPEN_ONLY";
    public string ModerationStatus { get; set; } = "ACTIVE";
    public string? ModerationReason { get; set; }
    public DateTimeOffset? ModeratedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserLibrarySong
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid SongId { get; set; }
    public Song Song { get; set; } = null!;
    public required string CustomTitle { get; set; }
    public string? OriginalUrl { get; set; }
    public string? CustomArtist { get; set; }
    public bool IsFavorite { get; set; }
    public string? AudioFileName { get; set; }
    public long? AudioSizeBytes { get; set; }
    public string? AudioStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public byte[] Version { get; set; } = [];
    public List<AlbumSong> AlbumSongs { get; set; } = [];
}

public sealed class Album
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }
    public int SortOrder { get; set; }
    public bool IsFavorite { get; set; }
    public string? SourceBoundary { get; set; }
    public string? CreateKey { get; set; }
    public string? CreateFingerprint { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
    public byte[] Version { get; set; } = [];
    public List<AlbumSong> Songs { get; set; } = [];
}

public sealed class AlbumSong
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid AlbumId { get; set; }
    public Album Album { get; set; } = null!;
    public Guid UserLibrarySongId { get; set; }
    public UserLibrarySong UserLibrarySong { get; set; } = null!;
    public long Position { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AlbumShare
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AlbumId { get; set; }
    public Guid OwnerId { get; set; }
    public Album Album { get; set; } = null!;
    public required string ShareCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
}

