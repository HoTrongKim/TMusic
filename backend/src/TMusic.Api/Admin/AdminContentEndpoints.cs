using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TMusic.Api.Common;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Persistence;
using TMusic.Infrastructure.Storage;

namespace TMusic.Api.Admin;

public static class AdminContentEndpoints
{
    public static void MapAdminContentEndpoints(this RouteGroupBuilder admin)
    {
        var songs = admin.MapGroup("/songs").AddEndpointFilter<AdminCsrfEndpointFilter>();
        songs.MapGet("", ListSongs).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.ContentRead));
        songs.MapPost("/{id:guid}/flag", FlagSong).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.ContentModerate));
        songs.MapPost("/{id:guid}/moderate", ModerateSong).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.ContentModerate));

        var albums = admin.MapGroup("/albums").AddEndpointFilter<AdminCsrfEndpointFilter>();
        albums.MapGet("", ListAlbums).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.AlbumsRead));
        albums.MapGet("/integrity", CheckAlbumIntegrity).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.AlbumsRead));
        albums.MapPost("/{id:guid}/flag", FlagAlbum).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.ContentModerate));

        var flags = admin.MapGroup("/moderation/flags").AddEndpointFilter<AdminCsrfEndpointFilter>();
        flags.MapGet("", ListFlags).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.ContentRead));
        flags.MapPost("/{id:guid}/resolve", ResolveFlag).RequireAuthorization(AdminAuthConstants.PolicyFor(AdminPermissions.ContentModerate));
    }

    private static async Task<IResult> ListSongs(
        string? q,
        string? provider,
        string? audioStatus,
        string? moderationStatus,
        int? page,
        int? pageSize,
        TMusicDb db,
        CancellationToken ct)
    {
        if (!ApiSupport.PageValid(page, pageSize, q))
            return ApiSupport.Problem("INVALID_QUERY", 400);

        var current = page ?? 1;
        var size = pageSize ?? 20;

        var query = db.UserLibrarySongs.AsNoTracking().Include(x => x.Song).AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            if (Guid.TryParse(term, out var idSearch))
                query = query.Where(x => x.Id == idSearch || x.SongId == idSearch || x.UserId == idSearch);
            else
                query = query.Where(x =>
                    x.CustomTitle.Contains(term) ||
                    (x.Song.OriginalTitle != null && x.Song.OriginalTitle.Contains(term)) ||
                    (x.Song.OriginalCreator != null && x.Song.OriginalCreator.Contains(term)) ||
                    x.Song.CanonicalUrl.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            var p = provider.Trim().ToLowerInvariant();
            query = query.Where(x => x.Song.Provider == p);
        }

        if (!string.IsNullOrWhiteSpace(audioStatus))
        {
            var s = audioStatus.Trim().ToUpperInvariant();
            if (s == "NONE")
                query = query.Where(x => x.AudioStatus == null || x.AudioStatus == "NONE");
            else
                query = query.Where(x => x.AudioStatus == s);
        }

        if (!string.IsNullOrWhiteSpace(moderationStatus))
        {
            var m = moderationStatus.Trim().ToUpperInvariant();
            query = query.Where(x => x.Song.ModerationStatus == m);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Skip((current - 1) * size)
            .Take(size)
            .Select(x => new
            {
                id = x.Id,
                songId = x.SongId,
                ownerId = x.UserId,
                title = !string.IsNullOrWhiteSpace(x.CustomTitle) ? x.CustomTitle : (x.Song.OriginalTitle ?? "(Không tiêu đề)"),
                creator = x.CustomArtist ?? x.Song.OriginalCreator,
                provider = x.Song.Provider,
                canonicalUrl = x.Song.CanonicalUrl,
                durationSeconds = x.Song.DurationSeconds,
                audioStatus = x.AudioStatus ?? "NONE",
                serverFilePresent = x.AudioStatus == ImportStatus.Ready && x.AudioFileName != null,
                storageScope = x.AudioStatus == ImportStatus.Ready && x.AudioFileName != null
                    ? "SERVER_STORED"
                    : "METADATA_OR_DEVICE_ONLY",
                audioSizeBytes = x.AudioSizeBytes,
                moderationStatus = x.Song.ModerationStatus,
                moderationReason = x.Song.ModerationReason,
                createdAt = x.CreatedAt,
                updatedAt = x.UpdatedAt
            })
            .ToListAsync(ct);

        var entryIds = rows.Select(r => r.id).ToArray();
        var latestErrors = await db.AudioDownloadJobs.AsNoTracking()
            .Where(j => entryIds.Contains(j.UserLibrarySongId) && j.ErrorCode != null)
            .GroupBy(j => j.UserLibrarySongId)
            .Select(g => new
            {
                EntryId = g.Key,
                LastError = g.OrderByDescending(x => x.CreatedAt).Select(x => x.ErrorCode).FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.EntryId, x => x.LastError, ct);

        var openFlagCounts = await db.ContentModerationFlags.AsNoTracking()
            .Where(f => f.Status == "OPEN" && (f.TargetType == "SONG" || f.TargetType == "USER_SONG"))
            .GroupBy(f => f.TargetId)
            .Select(g => new { TargetId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TargetId, x => x.Count, ct);

        var items = rows.Select(r => new
        {
            r.id,
            r.songId,
            r.ownerId,
            r.title,
            r.creator,
            r.provider,
            r.canonicalUrl,
            r.durationSeconds,
            r.audioStatus,
            r.serverFilePresent,
            r.storageScope,
            r.audioSizeBytes,
            r.moderationStatus,
            r.moderationReason,
            lastErrorCode = latestErrors.GetValueOrDefault(r.id),
            openFlags = openFlagCounts.GetValueOrDefault(r.songId, 0) + openFlagCounts.GetValueOrDefault(r.id, 0),
            r.createdAt,
            r.updatedAt
        });

        return Results.Ok(new
        {
            items,
            total,
            page = current,
            pageSize = size,
            notice = "Chỉ hiển thị bản ghi đã đồng bộ với server. Tệp MP3 lưu cục bộ trên điện thoại người dùng không bao giờ bị server truy cập."
        });
    }

    private static async Task<IResult> FlagSong(
        Guid id,
        CreateFlagRequest req,
        ClaimsPrincipal principal,
        TMusicDb db,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        var flagType = (req.FlagType ?? "METADATA_ANOMALY").Trim().ToUpperInvariant();
        if (flagType is not ("METADATA_ANOMALY" or "VIOLATION_REPORT"))
            return ApiSupport.Problem("INVALID_FLAG_TYPE", 400);
        if (string.IsNullOrWhiteSpace(req.Reason) || req.Reason.Trim().Length > 500)
            return ApiSupport.Problem("REASON_REQUIRED", 400);

        var entry = await db.UserLibrarySongs.Include(x => x.Song).FirstOrDefaultAsync(x => x.Id == id || x.SongId == id, ct);
        if (entry is null) return Results.NotFound();

        var now = clock.GetUtcNow();
        var flag = new ContentModerationFlag
        {
            TargetType = "SONG",
            TargetId = entry.SongId,
            FlagType = flagType,
            Status = "OPEN",
            Reason = req.Reason.Trim(),
            ReportedByUserId = ApiSupport.UserId(principal),
            CreatedAt = now
        };
        db.ContentModerationFlags.Add(flag);
        if (entry.Song.ModerationStatus == "ACTIVE")
        {
            entry.Song.ModerationStatus = "FLAGGED";
            entry.Song.ModerationReason = req.Reason.Trim();
            entry.Song.ModeratedAt = now;
        }
        await db.SaveChangesAsync(ct);

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "CONTENT_SONG_FLAGGED", "SONG", entry.SongId.ToString("D"), "SUCCESS",
            req.Reason.Trim(), new { flagId = flag.Id, flagType }, ct);

        return Results.Created($"/api/admin/moderation/flags", new { flag.Id, songId = entry.SongId, flag.FlagType, flag.Status, flag.Reason, flag.CreatedAt });
    }

    private static async Task<IResult> ModerateSong(
        Guid id,
        ModerateSongRequest req,
        ClaimsPrincipal principal,
        TMusicDb db,
        IAudioStorage audioStorage,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        var action = (req.Action ?? "").Trim().ToUpperInvariant();
        if (action is not ("REMOVE" or "RESTORE"))
            return ApiSupport.Problem("INVALID_ACTION", 400, "Hành động hợp lệ: REMOVE hoặc RESTORE.");
        if (string.IsNullOrWhiteSpace(req.Reason) || req.Reason.Trim().Length > 500)
            return ApiSupport.Problem("REASON_REQUIRED", 400);

        var song = await db.Songs.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (song is null)
        {
            var libraryEntry = await db.UserLibrarySongs.Include(x => x.Song).FirstOrDefaultAsync(x => x.Id == id, ct);
            song = libraryEntry?.Song;
        }
        if (song is null) return Results.NotFound();

        var now = clock.GetUtcNow();
        var reason = req.Reason.Trim();
        var quarantinedFiles = 0;

        if (action == "REMOVE")
        {
            song.ModerationStatus = "REMOVED";
            song.ModerationReason = reason;
            song.ModeratedAt = now;

            if (req.QuarantineServerAudio)
            {
                var entries = await db.UserLibrarySongs.Where(x => x.SongId == song.Id && x.AudioFileName != null).ToListAsync(ct);
                foreach (var entry in entries)
                {
                    if (entry.AudioFileName is not null && audioStorage.QuarantineFile(entry.UserId, entry.AudioFileName))
                        quarantinedFiles++;
                    entry.AudioFileName = null;
                    entry.AudioSizeBytes = null;
                    entry.AudioStatus = "REMOVED";
                    entry.UpdatedAt = now;
                }
            }

            await db.ContentModerationFlags
                .Where(f => f.TargetType == "SONG" && f.TargetId == song.Id && f.Status == "OPEN")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(f => f.Status, "REMOVED")
                    .SetProperty(f => f.ReviewedByUserId, ApiSupport.UserId(principal))
                    .SetProperty(f => f.ResolutionNote, reason)
                    .SetProperty(f => f.ResolvedAt, now), ct);
        }
        else
        {
            song.ModerationStatus = "ACTIVE";
            song.ModerationReason = null;
            song.ModeratedAt = now;
        }

        await db.SaveChangesAsync(ct);

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService,
            action == "REMOVE" ? "CONTENT_REMOVED" : "CONTENT_RESTORED",
            "SONG", song.Id.ToString("D"), "SUCCESS", reason,
            new { action, quarantinedFiles }, ct);

        return Results.Ok(new { songId = song.Id, moderationStatus = song.ModerationStatus, quarantinedFiles });
    }

    private static async Task<IResult> ListAlbums(
        string? q,
        bool? onlyIssues,
        int? page,
        int? pageSize,
        TMusicDb db,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!ApiSupport.PageValid(page, pageSize, q))
            return ApiSupport.Problem("INVALID_QUERY", 400);

        var now = clock.GetUtcNow();
        var current = page ?? 1;
        var size = pageSize ?? 20;

        var totalAlbums = await db.Albums.AsNoTracking().CountAsync(a => a.DeletedAt == null, ct);
        var created7d = await db.Albums.AsNoTracking().CountAsync(a => a.DeletedAt == null && a.CreatedAt >= now.AddDays(-7), ct);
        var softDeletedCount = await db.Albums.AsNoTracking().CountAsync(a => a.DeletedAt != null, ct);

        var query = db.Albums.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            if (Guid.TryParse(term, out var idSearch))
                query = query.Where(a => a.Id == idSearch || a.OwnerId == idSearch);
            else
                query = query.Where(a => a.Name.Contains(term));
        }

        if (onlyIssues == true)
        {
            var flaggedAlbumIds = db.ContentModerationFlags.Where(f => f.TargetType == "ALBUM" && f.Status == "OPEN").Select(f => f.TargetId);
            query = query.Where(a => a.DeletedAt != null || flaggedAlbumIds.Contains(a.Id) || a.Name == "");
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .Skip((current - 1) * size)
            .Take(size)
            .Select(a => new
            {
                a.Id,
                a.OwnerId,
                a.Name,
                a.SourceBoundary,
                a.IsFavorite,
                a.CreatedAt,
                a.UpdatedAt,
                a.DeletedAt,
                songCount = a.Songs.Count
            })
            .ToListAsync(ct);

        var albumIds = rows.Select(r => r.Id).ToArray();
        var activeShares = await db.AlbumShares.AsNoTracking()
            .Where(s => albumIds.Contains(s.AlbumId) && s.RevokedAt == null)
            .GroupBy(s => s.AlbumId)
            .Select(g => new { AlbumId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AlbumId, x => x.Count, ct);

        var openFlags = await db.ContentModerationFlags.AsNoTracking()
            .Where(f => f.TargetType == "ALBUM" && albumIds.Contains(f.TargetId) && f.Status == "OPEN")
            .GroupBy(f => f.TargetId)
            .Select(g => new { TargetId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TargetId, x => x.Count, ct);

        var items = rows.Select(r => new
        {
            r.Id,
            r.OwnerId,
            r.Name,
            r.SourceBoundary,
            r.songCount,
            activeShareCount = activeShares.GetValueOrDefault(r.Id, 0),
            openFlagCount = openFlags.GetValueOrDefault(r.Id, 0),
            syncState = r.DeletedAt is not null ? "SOFT_DELETED" : "SYNCED",
            hasIssue = r.DeletedAt is not null || string.IsNullOrWhiteSpace(r.Name) || openFlags.GetValueOrDefault(r.Id, 0) > 0,
            r.CreatedAt,
            r.UpdatedAt,
            r.DeletedAt
        });

        return Results.Ok(new
        {
            summary = new { totalAlbums, created7d, softDeletedCount },
            items,
            total,
            page = current,
            pageSize = size
        });
    }

    private static async Task<IResult> CheckAlbumIntegrity(TMusicDb db, CancellationToken ct)
    {
        var softDeletedWithActiveShares = await db.AlbumShares.AsNoTracking()
            .CountAsync(s => s.RevokedAt == null && s.Album.DeletedAt != null, ct);
        var albumsWithBlankName = await db.Albums.AsNoTracking()
            .CountAsync(a => a.DeletedAt == null && (a.Name == null || a.Name == ""), ct);
        var orphanedAlbumSongs = await db.AlbumSongs.AsNoTracking()
            .CountAsync(x => !db.UserLibrarySongs.Any(s => s.Id == x.UserLibrarySongId && s.UserId == x.UserId), ct);
        var openAlbumFlags = await db.ContentModerationFlags.AsNoTracking()
            .CountAsync(f => f.TargetType == "ALBUM" && f.Status == "OPEN", ct);
        var unlinkedLibrarySongs = await db.UserLibrarySongs.AsNoTracking()
            .CountAsync(s => !db.Songs.Any(cat => cat.Id == s.SongId), ct);

        var totalIssues = softDeletedWithActiveShares + albumsWithBlankName + orphanedAlbumSongs + openAlbumFlags + unlinkedLibrarySongs;

        return Results.Ok(new
        {
            healthy = totalIssues == 0,
            totalIssues,
            checks = new[]
            {
                new { code = "ORPHANED_ALBUM_SONGS", label = "Liên kết album-bài hát mồ côi", count = orphanedAlbumSongs },
                new { code = "UNLINKED_LIBRARY_SONGS", label = "Bản ghi thư viện mất tham chiếu bài hát gốc", count = unlinkedLibrarySongs },
                new { code = "DELETED_ALBUM_ACTIVE_SHARES", label = "Album đã xóa mềm nhưng còn liên kết chia sẻ", count = softDeletedWithActiveShares },
                new { code = "BLANK_ALBUM_NAMES", label = "Album thiếu tên hiển thị", count = albumsWithBlankName },
                new { code = "OPEN_ALBUM_REPORTS", label = "Báo cáo vi phạm album đang mở", count = openAlbumFlags }
            }
        });
    }

    private static async Task<IResult> FlagAlbum(
        Guid id,
        CreateFlagRequest req,
        ClaimsPrincipal principal,
        TMusicDb db,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason) || req.Reason.Trim().Length > 500)
            return ApiSupport.Problem("REASON_REQUIRED", 400);
        if (!await db.Albums.AsNoTracking().AnyAsync(a => a.Id == id, ct))
            return Results.NotFound();

        var flag = new ContentModerationFlag
        {
            TargetType = "ALBUM",
            TargetId = id,
            FlagType = "VIOLATION_REPORT",
            Status = "OPEN",
            Reason = req.Reason.Trim(),
            ReportedByUserId = ApiSupport.UserId(principal),
            CreatedAt = clock.GetUtcNow()
        };
        db.ContentModerationFlags.Add(flag);
        await db.SaveChangesAsync(ct);

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "ALBUM_FLAGGED", "ALBUM", id.ToString("D"), "SUCCESS", req.Reason.Trim(), new { flagId = flag.Id }, ct);
        return Results.Created("/api/admin/moderation/flags", new { flag.Id, albumId = id, flag.Status, flag.Reason, flag.CreatedAt });
    }

    private static async Task<IResult> ListFlags(
        string? status,
        string? targetType,
        int? page,
        int? pageSize,
        TMusicDb db,
        CancellationToken ct)
    {
        if (!ApiSupport.PageValid(page, pageSize, null))
            return ApiSupport.Problem("INVALID_QUERY", 400);

        var current = page ?? 1;
        var size = pageSize ?? 20;
        var query = db.ContentModerationFlags.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(f => f.Status == status.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(targetType))
            query = query.Where(f => f.TargetType == targetType.Trim().ToUpperInvariant());

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((current - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return Results.Ok(new { items, total, page = current, pageSize = size });
    }

    private static async Task<IResult> ResolveFlag(
        Guid id,
        ResolveFlagRequest req,
        ClaimsPrincipal principal,
        TMusicDb db,
        AdminPermissionService permissionService,
        AdminAuditService audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        var resolution = (req.Status ?? "RESOLVED").Trim().ToUpperInvariant();
        if (resolution is not ("RESOLVED" or "DISMISSED"))
            return ApiSupport.Problem("INVALID_STATUS", 400);

        var flag = await db.ContentModerationFlags.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (flag is null) return Results.NotFound();

        flag.Status = resolution;
        flag.ReviewedByUserId = ApiSupport.UserId(principal);
        flag.ResolutionNote = req.Note?.Trim();
        flag.ResolvedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await audit.RecordAuditFromPrincipalAsync(principal, permissionService, "MODERATION_FLAG_RESOLVED", flag.TargetType, flag.TargetId.ToString("D"), "SUCCESS",
            req.Note?.Trim() ?? resolution, new { flagId = id, resolution }, ct);

        return Results.Ok(flag);
    }

    public sealed record CreateFlagRequest(string? FlagType, string? Reason);
    public sealed record ModerateSongRequest(string? Action, string? Reason, bool QuarantineServerAudio = true);
    public sealed record ResolveFlagRequest(string? Status, string? Note);
}
