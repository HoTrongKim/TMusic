namespace TMusic.Domain.Entities;

public static class ImportStatus
{
    public const string Queued = "QUEUED", Running = "RUNNING", Ready = "READY", Complete = "COMPLETE",
        Failed = "FAILED", Canceled = "CANCELED";
}

public abstract class ImportWork
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public string Status { get; set; } = ImportStatus.Queued;
    public string? ErrorCode { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset? HeartbeatAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public int Failures { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class ImportPreview : ImportWork
{
    public required string PlaylistId { get; set; }
    public string? NextPageToken { get; set; }
    public string PageTokensJson { get; set; } = "[]";
    public int LastPosition { get; set; } = -1;
    public int Accepted { get; set; }
    public int Duplicates { get; set; }
    public int Unavailable { get; set; }
    public string? Checksum { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class ImportCandidate
{
    public Guid PreviewId { get; set; }
    public int Position { get; set; }
    public string? VideoId { get; set; }
    public string? Title { get; set; }
    public string? Creator { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int? DurationSeconds { get; set; }
    public bool Embeddable { get; set; }
    public required string Status { get; set; }
}

public sealed class ImportJob : ImportWork
{
    public Guid PreviewId { get; set; }
    public Guid? AlbumId { get; set; }
    public string? AlbumName { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string Fingerprint { get; set; }
    public required string Checksum { get; set; }
    public int LastPosition { get; set; } = -1;
    public int Total { get; set; }
    public int Imported { get; set; }
    public int AlreadyPresent { get; set; }
    public int Duplicates { get; set; }
    public int Unavailable { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class ProviderQuotaWindow
{
    public DateOnly Day { get; set; }
    public int Used { get; set; }
}

public sealed class AudioDownloadJob : ImportWork
{
    public Guid UserLibrarySongId { get; set; }
    public required string Provider { get; set; }
    public required string CanonicalUrl { get; set; }
    public bool RightsConfirmed { get; set; }
    public int ProgressPercent { get; set; }
    public string? OutputFileName { get; set; }
    public long? OutputSizeBytes { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
