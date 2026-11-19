namespace TMusic.Domain.Entities;

public static class AdminRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Moderator = "Moderator";
    public const string User = "User";

    public static readonly string[] All = [SuperAdmin, Admin, Moderator, User];
    public static readonly string[] Administrative = [SuperAdmin, Admin, Moderator];

    public static bool IsValid(string? role) =>
        role is SuperAdmin or Admin or Moderator or User;

    public static bool IsAdministrative(string? role) =>
        role is SuperAdmin or Admin or Moderator;

    public static int Rank(string? role) => role switch
    {
        SuperAdmin => 100,
        Admin => 70,
        Moderator => 40,
        User => 10,
        _ => 0
    };
}

public static class AdminPermissions
{
    public const string UsersRead = "users.read";
    public const string UsersUpdate = "users.update";
    public const string UsersSuspend = "users.suspend";
    public const string UsersDelete = "users.delete";
    public const string RolesRead = "roles.read";
    public const string RolesManage = "roles.manage";
    public const string ContentRead = "content.read";
    public const string ContentModerate = "content.moderate";
    public const string AlbumsRead = "albums.read";
    public const string DownloadsRead = "downloads.read";
    public const string DownloadsManage = "downloads.manage";
    public const string StorageRead = "storage.read";
    public const string StorageManage = "storage.manage";
    public const string AnalyticsRead = "analytics.read";
    public const string AuditRead = "audit.read";
    public const string SettingsRead = "settings.read";
    public const string SettingsManage = "settings.manage";
    public const string AdminsManage = "admins.manage";

    public static readonly string[] All =
    [
        UsersRead, UsersUpdate, UsersSuspend, UsersDelete,
        RolesRead, RolesManage,
        ContentRead, ContentModerate,
        AlbumsRead,
        DownloadsRead, DownloadsManage,
        StorageRead, StorageManage,
        AnalyticsRead,
        AuditRead,
        SettingsRead, SettingsManage,
        AdminsManage
    ];

    public static readonly IReadOnlyDictionary<string, string[]> DefaultRolePermissions =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [AdminRoles.SuperAdmin] = All,
            [AdminRoles.Admin] =
            [
                UsersRead, UsersUpdate, UsersSuspend, UsersDelete,
                RolesRead,
                ContentRead, ContentModerate,
                AlbumsRead,
                DownloadsRead, DownloadsManage,
                StorageRead, StorageManage,
                AnalyticsRead,
                AuditRead,
                SettingsRead, SettingsManage
            ],
            [AdminRoles.Moderator] =
            [
                UsersRead,
                ContentRead, ContentModerate,
                AlbumsRead,
                DownloadsRead,
                AnalyticsRead
            ],
            [AdminRoles.User] = []
        };

    public static bool IsValid(string? permission) =>
        permission is not null && All.Contains(permission, StringComparer.Ordinal);
}

public sealed class AccountSuspension
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? ActorId { get; set; }
    public required string Action { get; set; } // SUSPEND, REACTIVATE, DEACTIVATE
    public required string Reason { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AdminInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string RoleName { get; set; }
    public required string TokenHash { get; set; }
    public Guid InvitedByUserId { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public Guid? AcceptedUserId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
}

public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ActorId { get; set; }
    public string? ActorEmail { get; set; }
    public string? ActorRole { get; set; }
    public required string Action { get; set; }
    public required string TargetType { get; set; }
    public string? TargetId { get; set; }
    public required string Result { get; set; } // SUCCESS, FAILURE, DENIED
    public string? Reason { get; set; }
    public string? MetadataJson { get; set; }
    public string? CorrelationId { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SystemEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Severity { get; set; } // INFO, WARNING, ERROR, CRITICAL
    public required string EventType { get; set; } // API_ERROR, DOWNLOAD_ERROR, DB_ERROR, JOB_FAILURE, AUTH_FAILURE, SYSTEM_HEALTH
    public required string Code { get; set; }
    public required string Message { get; set; }
    public string? CorrelationId { get; set; }
    public Guid? UserId { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SystemSetting
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public required string DefaultValue { get; set; }
    public required string ValueType { get; set; } // int, bool, string
    public required string Category { get; set; }
    public required string Description { get; set; }
    public int? MinValue { get; set; }
    public int? MaxValue { get; set; }
    public bool RequiresConfirmation { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedByUserId { get; set; }
}

public sealed class ContentModerationFlag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string TargetType { get; set; } // SONG, USER_SONG, ALBUM
    public Guid TargetId { get; set; }
    public required string FlagType { get; set; } // METADATA_ANOMALY, VIOLATION_REPORT, REMOVED
    public string Status { get; set; } = "OPEN"; // OPEN, RESOLVED, DISMISSED, REMOVED
    public required string Reason { get; set; }
    public Guid? ReportedByUserId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class AdminMfaChallenge
{
    public required string Id { get; set; } // SHA-256 hash of opaque challenge token
    public Guid UserId { get; set; }
    public required string Purpose { get; set; } // MFA_VERIFY, MFA_SETUP
    public required string DeviceName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}
