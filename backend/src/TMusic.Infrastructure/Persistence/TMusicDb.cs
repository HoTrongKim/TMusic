using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TMusic.Domain.Entities;
namespace TMusic.Infrastructure.Persistence;

public sealed partial class TMusicDb(DbContextOptions<TMusicDb> options) : IdentityDbContext<Account, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Song> Songs => Set<Song>();
    public DbSet<UserLibrarySong> UserLibrarySongs => Set<UserLibrarySong>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<AlbumSong> AlbumSongs => Set<AlbumSong>();
    public DbSet<AlbumShare> AlbumShares => Set<AlbumShare>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<UserAuthProvider> UserAuthProviders => Set<UserAuthProvider>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<ExternalChallenge> ExternalChallenges => Set<ExternalChallenge>();
    partial void ConfigureImports(ModelBuilder builder);
    partial void ConfigureAdmin(ModelBuilder builder);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        const string binary = "Latin1_General_100_BIN2";
        builder.Entity<Account>(e => {
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.AvatarUrl).HasMaxLength(512);
            e.HasIndex(x => x.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();
        });
        builder.Entity<UserAuthProvider>(e => {
            e.Property(x => x.Provider).HasMaxLength(32).IsUnicode(false).UseCollation(binary);
            e.Property(x => x.ProviderSubject).HasMaxLength(128).IsUnicode(false).UseCollation(binary);
            e.HasIndex(x => new { x.Provider, x.ProviderSubject }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Provider });
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<IdentityUserLogin<Guid>>().Property(x => x.ProviderKey).UseCollation(binary);
        builder.Entity<UserSession>(e => {
            e.Property(x => x.RefreshTokenHash).HasMaxLength(64).IsUnicode(false).UseCollation(binary);
            e.Property(x => x.DeviceName).HasMaxLength(100);
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => x.RefreshTokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.FamilyId });
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<ExternalChallenge>(e => {
            e.Property(x => x.Id).HasMaxLength(64).IsUnicode(false).UseCollation(binary);
            e.HasIndex(x => x.ExpiresAt);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<UserSettings>(e => {
            e.HasKey(x => x.UserId);
            e.Property(x => x.Theme).HasMaxLength(16);
            e.Property(x => x.Language).HasMaxLength(10);
            e.HasOne<Account>().WithOne().HasForeignKey<UserSettings>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Song>(e => {
            e.Property(x => x.Provider).HasMaxLength(16).IsUnicode(false).UseCollation(binary);
            e.Property(x => x.ProviderContentId).HasMaxLength(128).IsUnicode(false).UseCollation(binary);
            e.Property(x => x.CanonicalUrl).HasMaxLength(512);
            e.Property(x => x.OriginalUrl).HasMaxLength(2048);
            e.Property(x => x.OriginalTitle).HasMaxLength(1000);
            e.Property(x => x.OriginalCreator).HasMaxLength(300);
            e.Property(x => x.ThumbnailUrl).HasMaxLength(2048);
            e.Property(x => x.PlaybackCapability).HasMaxLength(32).IsUnicode(false);
            e.HasIndex(x => new { x.Provider, x.ProviderContentId }).IsUnique();
            e.HasIndex(x => x.MetadataExpiresAt);
        });
        builder.Entity<UserLibrarySong>(e => {
            e.HasAlternateKey(x => new { x.Id, x.UserId });
            e.Property(x => x.CustomTitle).HasMaxLength(200);
            e.Property(x => x.OriginalUrl).HasMaxLength(2048);
            e.Property(x => x.CustomArtist).HasMaxLength(200);
            e.Property(x => x.AudioFileName).HasMaxLength(128).IsUnicode(false);
            e.Property(x => x.AudioStatus).HasMaxLength(24).IsUnicode(false);
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => new { x.UserId, x.SongId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Song).WithMany().HasForeignKey(x => x.SongId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Album>(e => {
            e.HasAlternateKey(x => new { x.Id, x.OwnerId });
            e.Property(x => x.Name).HasMaxLength(160);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.CoverImageUrl).HasMaxLength(512);
            e.Property(x => x.SourceBoundary).HasMaxLength(160).IsUnicode(false).UseCollation(binary);
            e.Property(x => x.CreateKey).HasMaxLength(100).IsUnicode(false).UseCollation(binary);
            e.Property(x => x.CreateFingerprint).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => new { x.OwnerId, x.CreateKey }).IsUnique().HasFilter("[CreateKey] IS NOT NULL");
            e.HasIndex(x => new { x.OwnerId, x.SortOrder, x.Id });
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<AlbumSong>(e => {
            e.HasIndex(x => new { x.AlbumId, x.UserLibrarySongId }).IsUnique();
            e.HasIndex(x => new { x.AlbumId, x.Position, x.Id });
            e.HasOne(x => x.Album).WithMany(x => x.Songs).HasForeignKey(x => new { x.AlbumId, x.UserId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.UserLibrarySong).WithMany(x => x.AlbumSongs)
                .HasForeignKey(x => new { x.UserLibrarySongId, x.UserId })
                .HasPrincipalKey(x => new { x.Id, x.UserId }).OnDelete(DeleteBehavior.NoAction);
        });
        builder.Entity<AlbumShare>(e => {
            e.Property(x => x.ShareCode).HasMaxLength(24).IsUnicode(false).UseCollation(binary);
            e.HasIndex(x => x.ShareCode).IsUnique();
            e.HasIndex(x => new { x.AlbumId, x.RevokedAt });
            e.HasOne(x => x.Album).WithMany().HasForeignKey(x => new { x.AlbumId, x.OwnerId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerId }).OnDelete(DeleteBehavior.Cascade);
        });
        ConfigureImports(builder);
        ConfigureAdmin(builder);
    }
}

