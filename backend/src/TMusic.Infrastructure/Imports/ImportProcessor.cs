using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TMusic.Application.Providers;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Persistence;

namespace TMusic.Infrastructure.Imports;

public sealed class ImportProcessor(TMusicDb db, IYouTubePlaylist youtube, IConfiguration configuration, TimeProvider clock)
{
    private const int BatchSize = 100;
    private const int MaximumItems = 5000;

    public async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        var job = await ClaimAsync(db.ImportJobs, ct);
        if (job is not null) { await ProcessAsync(job, () => CommitBatchAsync(job, ct), ct); return true; }
        var preview = await ClaimAsync(db.ImportPreviews, ct);
        if (preview is null) return false;
        await ProcessAsync(preview, () => ReadPageAsync(preview, ct), ct);
        return true;
    }

    public async Task PurgeExpiredAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.ImportPreviews.Where(x => x.ExpiresAt <= now).ExecuteDeleteAsync(ct);
        await db.ImportJobs.Where(x => x.CompletedAt < now.AddDays(-30)).ExecuteDeleteAsync(ct);
        await db.AudioDownloadJobs.Where(x => x.CompletedAt < now.AddDays(-30)).ExecuteDeleteAsync(ct);
        await db.ProviderQuotaWindows.Where(x => x.Day < DateOnly.FromDateTime(now.UtcDateTime.AddDays(-14))).ExecuteDeleteAsync(ct);
        // The user-owned bookmark, title, artist, favorite and album membership survive provider cache expiration.
        await db.Songs.Where(x => x.MetadataExpiresAt != null && x.MetadataExpiresAt <= now).ExecuteUpdateAsync(set => set
            .SetProperty(x => x.OriginalTitle, (string?)null).SetProperty(x => x.OriginalCreator, (string?)null)
            .SetProperty(x => x.ThumbnailUrl, (string?)null).SetProperty(x => x.DurationSeconds, (int?)null)
            .SetProperty(x => x.MetadataFetchedAt, (DateTimeOffset?)null).SetProperty(x => x.MetadataExpiresAt, (DateTimeOffset?)null)
            .SetProperty(x => x.PlaybackCapability, "EXTERNAL_OPEN_ONLY"), ct);
    }

    private async Task<T?> ClaimAsync<T>(DbSet<T> set, CancellationToken ct) where T : ImportWork
    {
        var now = clock.GetUtcNow();
        var work = await set.Where(x => (x.Status == ImportStatus.Queued || x.Status == ImportStatus.Running) &&
            x.NextAttemptAt <= now && (x.LeaseUntil == null || x.LeaseUntil <= now))
            .OrderBy(x => x.NextAttemptAt).ThenBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (work is null) return null;
        work.Status = ImportStatus.Running;
        work.LeaseOwner = Guid.NewGuid();
        work.HeartbeatAt = now;
        work.LeaseUntil = now.AddMinutes(2);
        try { await db.SaveChangesAsync(ct); return work; }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return null; }
    }

    private async Task ProcessAsync<T>(T work, Func<Task> operation, CancellationToken ct) where T : ImportWork
    {
        var lease = work.LeaseOwner;
        try { await operation(); }
        catch (ProviderException e) { await FailAsync<T>(work.Id, lease, e.Code, e.Retryable, ct); }
        catch (Exception e) when (!ct.IsCancellationRequested && e is HttpRequestException or OperationCanceledException or
            JsonException or KeyNotFoundException or FormatException)
        { await FailAsync<T>(work.Id, lease, "PROVIDER_TEMPORARILY_UNAVAILABLE", true, ct); }
        catch (DbUpdateException e) when (e is DbUpdateConcurrencyException || e.InnerException is SqlException { Number: 1205 or 2601 or 2627 or 547 })
        { await FailAsync<T>(work.Id, lease, "CONCURRENT_MODIFICATION", true, ct); }
        catch (SqlException e) when (e.Number == 1205)
        { await FailAsync<T>(work.Id, lease, "CONCURRENT_MODIFICATION", true, ct); }
        finally { db.ChangeTracker.Clear(); }
    }

    private async Task ReadPageAsync(ImportPreview preview, CancellationToken ct)
    {
        if (!configuration.GetValue("Features:EnableYouTubePlaylistImport", true)) throw new ProviderException("PLAYLIST_IMPORT_DISABLED");
        if (preview.ExpiresAt <= clock.GetUtcNow()) throw new ProviderException("PREVIEW_EXPIRED");
        if (!youtube.IsConfigured) throw new ProviderException("YOUTUBE_NOT_CONFIGURED");
        var page = await youtube.ReadPageAsync(preview.PlaylistId, preview.NextPageToken, ct);
        var priorCount = preview.Accepted + preview.Duplicates + preview.Unavailable;
        if (priorCount + page.Videos.Count > MaximumItems) throw new ProviderException("PLAYLIST_LIMIT_EXCEEDED");
        var positions = page.Videos.Select(x => x.Position).ToArray();
        if (positions.Any(x => x <= preview.LastPosition) || positions.Distinct().Count() != positions.Length)
            throw new ProviderException("PLAYLIST_CHANGED");
        var tokens = JsonSerializer.Deserialize<List<string>>(preview.PageTokensJson)!;
        if (page.NextPage is { } next)
        {
            if (next.Length > 2048 || next.Length == 0 || tokens.Contains(next, StringComparer.Ordinal) || page.Videos.Count == 0)
                throw new ProviderException("YOUTUBE_INVALID_RESPONSE");
            tokens.Add(next);
        }
        var prior = await db.ImportCandidates.AsNoTracking().Where(x => x.PreviewId == preview.Id).OrderBy(x => x.Position).ToListAsync(ct);
        var seen = prior.Where(x => x.Status == "ACCEPTED").Select(x => x.VideoId!).ToHashSet(StringComparer.Ordinal);
        var candidates = PlaylistDeduplication.Classify(page.Videos, seen).Select(x => new ImportCandidate
        {
            PreviewId = preview.Id, Position = x.Video.Position, VideoId = x.Video.VideoId, Status = x.Status,
            Title = x.Video.Title, Creator = x.Video.Creator, ThumbnailUrl = x.Video.ThumbnailUrl,
            DurationSeconds = x.Video.DurationSeconds, Embeddable = x.Video.Embeddable
        }).ToArray();
        db.ImportCandidates.AddRange(candidates);
        preview.Accepted += candidates.Count(x => x.Status == "ACCEPTED");
        preview.Duplicates += candidates.Count(x => x.Status == "DUPLICATE");
        preview.Unavailable += candidates.Count(x => x.Status == "UNAVAILABLE");
        preview.NextPageToken = page.NextPage;
        preview.PageTokensJson = JsonSerializer.Serialize(tokens);
        if (candidates.Length > 0) preview.LastPosition = candidates[^1].Position;
        if (page.NextPage is null) preview.Checksum = Checksum(prior.Concat(candidates));
        Release(preview, page.NextPage is null ? ImportStatus.Ready : ImportStatus.Queued);
        await db.SaveChangesAsync(ct);
    }

    private async Task CommitBatchAsync(ImportJob job, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var preview = await db.ImportPreviews.AsNoTracking().SingleOrDefaultAsync(x => x.Id == job.PreviewId &&
            x.OwnerId == job.OwnerId && x.Status == ImportStatus.Ready && x.ExpiresAt > now, ct)
            ?? throw new ProviderException("PREVIEW_EXPIRED");
        var snapshot = await db.ImportCandidates.AsNoTracking().Where(x => x.PreviewId == preview.Id).OrderBy(x => x.Position).ToListAsync(ct);
        if (preview.Checksum != job.Checksum || Checksum(snapshot) != job.Checksum) throw new ProviderException("PREVIEW_INTEGRITY_FAILED");
        Album album;
        if (job.AlbumId is { } albumId)
            album = await db.Albums.SingleOrDefaultAsync(x => x.Id == albumId && x.OwnerId == job.OwnerId, ct)
                ?? throw new ProviderException("ALBUM_NOT_FOUND");
        else
        {
            album = new Album { Name = job.AlbumName!, OwnerId = job.OwnerId, CreatedAt = now, UpdatedAt = now };
            db.Albums.Add(album);
            job.AlbumId = album.Id;
        }
        album.UpdatedAt = now;
        if (db.Entry(album).State != EntityState.Added) db.Entry(album).Property(x => x.UpdatedAt).IsModified = true;
        var remaining = snapshot.Where(x => x.Status == "ACCEPTED" && x.Position > job.LastPosition).Take(BatchSize + 1).ToArray();
        var batch = remaining.Take(BatchSize).ToArray();
        var ids = batch.Select(x => x.VideoId!).ToArray();
        var existingSongs = await db.Songs.Where(x => x.Provider == "youtube" && ids.Contains(x.ProviderContentId))
            .ToDictionaryAsync(x => x.ProviderContentId, StringComparer.Ordinal, ct);
        var existingLibrary = await db.UserLibrarySongs.Include(x => x.Song).Where(x => x.UserId == job.OwnerId &&
                x.Song.Provider == "youtube" && ids.Contains(x.Song.ProviderContentId))
            .ToDictionaryAsync(x => x.Song.ProviderContentId, StringComparer.Ordinal, ct);
        var members = await db.AlbumSongs.Where(x => x.AlbumId == album.Id).Select(x => x.UserLibrarySongId).ToListAsync(ct);
        var memberIds = members.ToHashSet();
        var position = await db.AlbumSongs.Where(x => x.AlbumId == album.Id).MaxAsync(x => (long?)x.Position, ct) ?? 0;
        foreach (var candidate in batch)
        {
            var id = candidate.VideoId!;
            if (!existingSongs.TryGetValue(id, out var song))
            {
                song = new Song { Provider = "youtube", ProviderContentId = id, CanonicalUrl = SourceUrl.Watch(id),
                    OriginalUrl = SourceUrl.Watch(id), CreatedAt = now };
                db.Songs.Add(song);
            }
            if (song.MetadataFetchedAt is null || song.MetadataFetchedAt < preview.CreatedAt)
            {
                song.OriginalTitle = candidate.Title; song.OriginalCreator = candidate.Creator;
                song.ThumbnailUrl = candidate.ThumbnailUrl; song.DurationSeconds = candidate.DurationSeconds;
                song.MetadataFetchedAt = preview.CreatedAt; song.MetadataExpiresAt = preview.CreatedAt.AddDays(29);
                song.PlaybackCapability = candidate.Embeddable ? "EMBED_SUPPORTED" : "EXTERNAL_OPEN_ONLY";
            }
            if (!existingLibrary.TryGetValue(id, out var library))
            {
                library = new UserLibrarySong { UserId = job.OwnerId, SongId = song.Id, Song = song,
                    OriginalUrl = song.CanonicalUrl, CustomTitle = string.Empty, CreatedAt = now, UpdatedAt = now };
                db.UserLibrarySongs.Add(library);
            }
            if (memberIds.Contains(library.Id)) job.AlreadyPresent++;
            else
            {
                db.AlbumSongs.Add(new AlbumSong { UserId = job.OwnerId, AlbumId = album.Id, Album = album,
                    UserLibrarySongId = library.Id, UserLibrarySong = library, Position = ++position, AddedAt = now });
                job.Imported++;
            }
        }
        if (batch.Length > 0) job.LastPosition = batch[^1].Position;
        var complete = remaining.Length <= BatchSize;
        if (complete) job.CompletedAt = now;
        Release(job, complete ? ImportStatus.Complete : ImportStatus.Queued);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task FailAsync<T>(Guid id, Guid? lease, string code, bool retryable, CancellationToken ct) where T : ImportWork
    {
        db.ChangeTracker.Clear();
        var work = await db.Set<T>().SingleOrDefaultAsync(x => x.Id == id && x.LeaseOwner == lease &&
            x.Status == ImportStatus.Running && x.LeaseOwner != null, ct);
        if (work is null) return;
        work.Failures++;
        work.ErrorCode = code;
        work.LeaseOwner = null;
        work.LeaseUntil = null;
        if (retryable && work.Failures < 5)
        {
            work.Status = ImportStatus.Queued;
            work.NextAttemptAt = clock.GetUtcNow().AddSeconds(Math.Pow(2, work.Failures) + Random.Shared.NextDouble() * 2);
        }
        else
        {
            work.Status = ImportStatus.Failed;
            if (work is ImportJob job) job.CompletedAt = clock.GetUtcNow();
        }
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { }
    }

    private void Release(ImportWork work, string status)
    {
        work.Status = status; work.Failures = 0; work.ErrorCode = null;
        work.LeaseUntil = null; work.LeaseOwner = null;
        work.NextAttemptAt = clock.GetUtcNow(); work.HeartbeatAt = work.NextAttemptAt;
    }

    public static string Checksum(IEnumerable<ImportCandidate> candidates)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(candidates.OrderBy(x => x.Position)
            .Select(x => new { x.Position, x.VideoId, x.Status, x.Title, x.Creator, x.ThumbnailUrl, x.DurationSeconds, x.Embeddable }))));
}

