using Microsoft.EntityFrameworkCore;
using TMusic.Domain.Entities;

namespace TMusic.Infrastructure.Persistence;

public sealed partial class TMusicDb
{
    public DbSet<AccountSuspension> AccountSuspensions => Set<AccountSuspension>();
    public DbSet<AdminInvitation> AdminInvitations => Set<AdminInvitation>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemEvent> SystemEvents => Set<SystemEvent>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<ContentModerationFlag> ContentModerationFlags => Set<ContentModerationFlag>();
    public DbSet<AdminMfaChallenge> AdminMfaChallenges => Set<AdminMfaChallenge>();

    partial void ConfigureAdmin(ModelBuilder builder)
    {
        const string binary = "Latin1_General_100_BIN2";

        builder.Entity<Account>(e =>
        {
            e.Property(x => x.SuspensionReason).HasMaxLength(500);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.LastActiveAt);
            e.HasIndex(x => x.IsSuspended);
        });

        builder.Entity<Song>(e =>
        {
            e.Property(x => x.ModerationStatus).HasMaxLength(24).IsUnicode(false).HasDefaultValue("ACTIVE");
            e.Property(x => x.ModerationReason).HasMaxLength(500);
            e.HasIndex(x => x.ModerationStatus);
        });

        builder.Entity<AccountSuspension>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(24).IsUnicode(false);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.CorrelationId).HasMaxLength(128).IsUnicode(false);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AdminInvitation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.NormalizedEmail).HasMaxLength(256);
            e.Property(x => x.RoleName).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.TokenHash).HasMaxLength(64).IsUnicode(false).UseCollation(binary);
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.NormalizedEmail, x.ExpiresAt });
        });

        builder.Entity<AuditLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ActorEmail).HasMaxLength(256);
            e.Property(x => x.ActorRole).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.Action).HasMaxLength(80).IsUnicode(false);
            e.Property(x => x.TargetType).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.TargetId).HasMaxLength(128).IsUnicode(false);
            e.Property(x => x.Result).HasMaxLength(16).IsUnicode(false);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.MetadataJson).HasMaxLength(4000);
            e.Property(x => x.CorrelationId).HasMaxLength(128).IsUnicode(false);
            e.Property(x => x.IpAddress).HasMaxLength(64).IsUnicode(false);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.Action, x.CreatedAt });
            e.HasIndex(x => new { x.ActorId, x.CreatedAt });
        });

        builder.Entity<SystemEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Severity).HasMaxLength(16).IsUnicode(false);
            e.Property(x => x.EventType).HasMaxLength(48).IsUnicode(false);
            e.Property(x => x.Code).HasMaxLength(80).IsUnicode(false);
            e.Property(x => x.Message).HasMaxLength(1000);
            e.Property(x => x.CorrelationId).HasMaxLength(128).IsUnicode(false);
            e.Property(x => x.MetadataJson).HasMaxLength(4000);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.Severity, x.CreatedAt });
            e.HasIndex(x => new { x.EventType, x.CreatedAt });
            e.HasIndex(x => x.CorrelationId);
        });

        builder.Entity<SystemSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64).IsUnicode(false).UseCollation(binary);
            e.Property(x => x.Value).HasMaxLength(500);
            e.Property(x => x.DefaultValue).HasMaxLength(500);
            e.Property(x => x.ValueType).HasMaxLength(16).IsUnicode(false);
            e.Property(x => x.Category).HasMaxLength(32).IsUnicode(false);
            e.Property(x => x.Description).HasMaxLength(500);
        });

        builder.Entity<ContentModerationFlag>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TargetType).HasMaxLength(24).IsUnicode(false);
            e.Property(x => x.FlagType).HasMaxLength(32).IsUnicode(false);
            e.Property(x => x.Status).HasMaxLength(24).IsUnicode(false);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.ResolutionNote).HasMaxLength(500);
            e.HasIndex(x => new { x.TargetType, x.TargetId });
            e.HasIndex(x => new { x.Status, x.CreatedAt });
        });

        builder.Entity<AdminMfaChallenge>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64).IsUnicode(false).UseCollation(binary);
            e.Property(x => x.Purpose).HasMaxLength(24).IsUnicode(false);
            e.Property(x => x.DeviceName).HasMaxLength(100);
            e.HasIndex(x => x.ExpiresAt);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
