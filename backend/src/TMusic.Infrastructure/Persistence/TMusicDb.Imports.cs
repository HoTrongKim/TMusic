using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TMusic.Domain.Entities;

namespace TMusic.Infrastructure.Persistence;

public sealed partial class TMusicDb
{
    public DbSet<ImportPreview> ImportPreviews => Set<ImportPreview>();
    public DbSet<ImportCandidate> ImportCandidates => Set<ImportCandidate>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<AudioDownloadJob> AudioDownloadJobs => Set<AudioDownloadJob>();
    public DbSet<ProviderQuotaWindow> ProviderQuotaWindows => Set<ProviderQuotaWindow>();

    partial void ConfigureImports(ModelBuilder builder)
    {
        builder.Entity<ImportPreview>(e =>
        {
            ConfigureWork(e);
            e.Property(x => x.PlaylistId).HasMaxLength(128).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.NextPageToken).HasMaxLength(2048).IsUnicode(false);
            e.Property(x => x.Checksum).HasMaxLength(64).IsUnicode(false);
            e.HasIndex(x => x.ExpiresAt);
        });
        builder.Entity<ImportCandidate>(e =>
        {
            e.HasKey(x => new { x.PreviewId, x.Position });
            e.Property(x => x.VideoId).HasMaxLength(11).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.Title).HasMaxLength(512);
            e.Property(x => x.Creator).HasMaxLength(256);
            e.Property(x => x.ThumbnailUrl).HasMaxLength(2048);
            e.Property(x => x.Status).HasMaxLength(16).IsUnicode(false);
            e.HasOne<ImportPreview>().WithMany().HasForeignKey(x => x.PreviewId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<ImportJob>(e =>
        {
            ConfigureWork(e);
            e.Property(x => x.AlbumName).HasMaxLength(160);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.Fingerprint).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.Checksum).HasMaxLength(64).IsUnicode(false);
            e.HasIndex(x => new { x.OwnerId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => x.PreviewId);
        });
        builder.Entity<AudioDownloadJob>(e =>
        {
            ConfigureWork(e);
            e.Property(x => x.Provider).HasMaxLength(16).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.CanonicalUrl).HasMaxLength(512);
            e.Property(x => x.OutputFileName).HasMaxLength(128).IsUnicode(false);
            e.HasIndex(x => new { x.OwnerId, x.UserLibrarySongId });
        });
        builder.Entity<ProviderQuotaWindow>().HasKey(x => x.Day);
    }

    private static void ConfigureWork<T>(EntityTypeBuilder<T> e) where T : ImportWork
    {
        e.HasBaseType((Type?)null);
        e.HasKey(x => x.Id);
        e.Property(x => x.Status).HasMaxLength(16).IsUnicode(false);
        e.Property(x => x.ErrorCode).HasMaxLength(64).IsUnicode(false);
        e.Property(x => x.Version).IsRowVersion();
        e.HasIndex(x => new { x.Status, x.NextAttemptAt, x.LeaseUntil });
        e.HasIndex(x => new { x.OwnerId, x.CreatedAt });
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
    }
}
