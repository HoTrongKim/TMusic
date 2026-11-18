using Microsoft.AspNetCore.Identity;
namespace TMusic.Infrastructure.Persistence;

public sealed class Account : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class UserAuthProvider
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public required string Provider { get; set; }
    public required string ProviderSubject { get; set; }
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public required string RefreshTokenHash { get; set; }
    public required string DeviceName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset AuthenticatedAt { get; set; }
    public DateTimeOffset? RotatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class ExternalChallenge
{
    public required string Id { get; set; }
    public Guid? UserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class UserSettings
{
    public Guid UserId { get; set; }
    public string Theme { get; set; } = "system";
    public string Language { get; set; } = "vi";
    public bool NotificationsEnabled { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

