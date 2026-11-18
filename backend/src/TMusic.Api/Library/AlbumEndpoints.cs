using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TMusic.Api.Common;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Persistence;
using TMusic.Infrastructure.Storage;
namespace TMusic.Api.Library;

public static class AlbumEndpoints
{
    private static readonly HashSet<string> PresetCovers = new(StringComparer.Ordinal)
    {
        "cover:navy", "cover:blue", "cover:green", "cover:terracotta", "cover:slate", "cover:amber"
    };

    public static void MapAlbumEndpoints(this RouteGroupBuilder api)
    {
        var albums = api.MapGroup("/albums").RequireAuthorization();
        albums.MapGet("", List);
        albums.MapPost("", Create);
        albums.MapPatch("/order", ReorderAlbums);
        albums.MapGet("/{id:guid}", Get);
        albums.MapPatch("/{id:guid}", Update);
        albums.MapDelete("/{id:guid}", Delete);
        albums.MapPost("/{id:guid}/cover", UploadCover)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(CoverImageStorage.MaxFileBytes + 65_536));
        albums.MapDelete("/{id:guid}/cover", DeleteCover);
        albums.MapGet("/{id:guid}/songs", Songs);
        albums.MapPost("/{id:guid}/songs", AddSong);
        albums.MapDelete("/{id:guid}/songs/{songId:guid}", RemoveSong);
        albums.MapPatch("/{id:guid}/songs/order", Reorder);
        albums.MapGet("/{id:guid}/share", GetShare);
        albums.MapPost("/{id:guid}/share", CreateOrGetShare);
        albums.MapDelete("/{id:guid}/share", RevokeShare);

        api.MapGet("/shared-albums/{code}", GetSharedAlbum).RequireRateLimiting("provider");
        api.MapPost("/shared-albums/{code}/import", ImportSharedAlbum).RequireAuthorization();

        api.MapGet("/covers/{userId:guid}/{fileName}", GetCover).RequireAuthorization();
    }

    private static async Task<IResult> List(ClaimsPrincipal u, TMusicDb db, int? page, int? pageSize, string? q, string? sort, bool? favorite, CancellationToken ct)
    {
        if (!ApiSupport.PageValid(page, pageSize, q) || sort is not (null or "recent" or "name" or "order")) return ApiSupport.Problem("INVALID_QUERY");
        var user = ApiSupport.UserId(u);
        var query = db.Albums.AsNoTracking().Where(x => x.OwnerId == user);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x => x.Name.Contains(term) || (x.Description != null && x.Description.Contains(term)));
        }
        if (favorite.HasValue) query = query.Where(x => x.IsFavorite == favorite.Value);
        var total = await query.CountAsync(ct);
        var current = page ?? 1;
        var size = pageSize ?? 30;
        var ordered = sort switch
        {
            "name" => query.OrderBy(x => x.Name).ThenBy(x => x.Id),
            "order" => query.OrderBy(x => x.SortOrder).ThenByDescending(x => x.UpdatedAt).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id)
        };
        var values = await ordered.Skip((current - 1) * size).Take(size).Select(x => new { album = x, count = x.Songs.Count }).ToListAsync(ct);
        return Results.Ok(new { items = values.Select(x => LibraryViews.Album(x.album, x.count)), total, page = current, pageSize = size });
    }

    private static async Task<IResult> Create(AlbumRequest r, ClaimsPrincipal u, HttpContext context, TMusicDb db, TimeProvider time, CancellationToken ct)
    {
        var user = ApiSupport.UserId(u);
        if (!Valid(r, user)) return ApiSupport.Problem("INVALID_ALBUM");
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (key.Length > 0 && (key.Length is < 16 or > 100 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            return ApiSupport.Problem("INVALID_IDEMPOTENCY_KEY");
        var fingerprint = ApiSupport.Hash(JsonSerializer.Serialize(r));
        if (key.Length > 0)
        {
            var previous = await db.Albums.AsNoTracking().FirstOrDefaultAsync(x => x.OwnerId == user && x.CreateKey == key, ct);
            if (previous is not null)
                return previous.CreateFingerprint == fingerprint
                    ? Results.Ok(LibraryViews.Album(previous, await db.AlbumSongs.CountAsync(x => x.AlbumId == previous.Id, ct)))
                    : ApiSupport.Problem("IDEMPOTENCY_CONFLICT", 409);
        }
        var now = time.GetUtcNow();
        var cover = string.IsNullOrWhiteSpace(r.CoverImageUrl) ? null : r.CoverImageUrl;
        var album = new Album
        {
            OwnerId = user, Name = r.Name!.Trim(), Description = r.Description?.Trim(), CoverImageUrl = cover,
            SortOrder = r.SortOrder ?? 0, IsFavorite = r.IsFavorite ?? false, CreatedAt = now, UpdatedAt = now,
            CreateKey = key.Length > 0 ? key : null, CreateFingerprint = fingerprint
        };
        db.Albums.Add(album);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (key.Length > 0 && ApiSupport.IsUnique(e))
        {
            db.ChangeTracker.Clear();
            var previous = await db.Albums.AsNoTracking().SingleAsync(x => x.OwnerId == user && x.CreateKey == key, ct);
            return previous.CreateFingerprint == fingerprint
                ? Results.Ok(LibraryViews.Album(previous, await db.AlbumSongs.CountAsync(x => x.AlbumId == previous.Id, ct)))
                : ApiSupport.Problem("IDEMPOTENCY_CONFLICT", 409);
        }
        return Results.Created($"/api/v1/albums/{album.Id}", LibraryViews.Album(album, 0));
    }

    private static async Task<IResult> Get(Guid id, ClaimsPrincipal u, TMusicDb db, HttpContext context, CancellationToken ct)
    {
        var user = ApiSupport.UserId(u);
        var value = await db.Albums.AsNoTracking().Where(x => x.Id == id && x.OwnerId == user).Select(x => new { album = x, count = x.Songs.Count }).FirstOrDefaultAsync(ct);
        if (value is null) return Results.NotFound();
        context.Response.Headers.ETag = $"\"{Convert.ToBase64String(value.album.Version)}\"";
        return Results.Ok(LibraryViews.Album(value.album, value.count));
    }

    private static async Task<IResult> Update(Guid id, AlbumRequest r, ClaimsPrincipal u, TMusicDb db, ICoverImageStorage covers, HttpContext context, TimeProvider time, CancellationToken ct)
    {
        var user = ApiSupport.UserId(u);
        if (!Valid(r, user)) return ApiSupport.Problem("INVALID_ALBUM");
        var album = await db.Albums.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == user, ct);
        if (album is null) return Results.NotFound();
        if (ApiSupport.Match(context, album.Version) is { } mismatch) return mismatch;

        var previousCover = album.CoverImageUrl;
        var nextCover = string.IsNullOrWhiteSpace(r.CoverImageUrl) ? null : r.CoverImageUrl;
        if (nextCover is not null && CoverImageStorage.ExtractOwnedFileName(user, nextCover) is not null &&
            !string.Equals(previousCover, nextCover, StringComparison.Ordinal))
        {
            return ApiSupport.Problem("INVALID_ALBUM");
        }

        album.Name = r.Name!.Trim();
        album.Description = r.Description?.Trim();
        album.CoverImageUrl = nextCover;
        if (r.SortOrder.HasValue) album.SortOrder = r.SortOrder.Value;
        if (r.IsFavorite.HasValue) album.IsFavorite = r.IsFavorite.Value;
        album.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);

        if (!string.Equals(previousCover, nextCover, StringComparison.Ordinal))
            covers.DeleteCoverUrl(user, previousCover);

        return Results.Ok(LibraryViews.Album(album, await db.AlbumSongs.CountAsync(x => x.AlbumId == id, ct)));
    }

    private static async Task<IResult> Delete(Guid id, ClaimsPrincipal u, TMusicDb db, ICoverImageStorage covers, HttpContext context, CancellationToken ct)
    {
        var user = ApiSupport.UserId(u);
        var album = await db.Albums.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == user, ct);
        if (album is null) return Results.NotFound();
        if (ApiSupport.Match(context, album.Version) is { } mismatch) return mismatch;
        var oldCover = album.CoverImageUrl;
        db.Albums.Remove(album);
        await db.SaveChangesAsync(ct);
        covers.DeleteCoverUrl(user, oldCover);
        return Results.NoContent();
    }

    private static async Task<IResult> ReorderAlbums(AlbumOrderRequest r, ClaimsPrincipal u, TMusicDb db, TimeProvider time, CancellationToken ct)
    {
        if (r.AlbumIds is null || r.AlbumIds.Length is 0 or > 1000 || r.AlbumIds.Distinct().Count() != r.AlbumIds.Length)
            return ApiSupport.Problem("INVALID_ORDER");
        var user = ApiSupport.UserId(u);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var entries = await db.Albums.Where(x => x.OwnerId == user).ToDictionaryAsync(x => x.Id, ct);
        if (entries.Count != r.AlbumIds.Length || r.AlbumIds.Any(id => !entries.ContainsKey(id)))
            return ApiSupport.Problem("INVALID_ORDER");
        var now = time.GetUtcNow();
        for (var i = 0; i < r.AlbumIds.Length; i++)
        {
            var item = entries[r.AlbumIds[i]];
            item.SortOrder = i;
            item.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UploadCover(Guid id, HttpContext context, ClaimsPrincipal u, TMusicDb db, ICoverImageStorage covers, TimeProvider time, CancellationToken ct)
    {
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
            sizeFeature.MaxRequestBodySize = CoverImageStorage.MaxFileBytes + 65_536;

        if (!context.Request.HasFormContentType)
            return ApiSupport.Problem("INVALID_COVER_REQUEST", 400, "Yêu cầu multipart/form-data với trường 'file'.");

        var user = ApiSupport.UserId(u);
        var album = await db.Albums.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == user, ct);
        if (album is null) return Results.NotFound();
        if (ApiSupport.Match(context, album.Version) is { } mismatch) return mismatch;

        IFormCollection form;
        try { form = await context.Request.ReadFormAsync(ct); }
        catch (InvalidDataException) { return ApiSupport.Problem("COVER_SIZE_INVALID", 400, "Ảnh bìa vượt quá dung lượng tối đa 2 MB."); }

        var file = form.Files.GetFile("file") ?? (form.Files.Count == 1 ? form.Files[0] : null);
        if (file is null || file.Length <= 0 || file.Length > CoverImageStorage.MaxFileBytes)
            return ApiSupport.Problem("COVER_SIZE_INVALID", 400, "Ảnh bìa phải từ 1 byte đến tối đa 2 MB.");

        using var buffer = new MemoryStream((int)Math.Min(file.Length, CoverImageStorage.MaxFileBytes));
        await using (var input = file.OpenReadStream())
        {
            var chunk = new byte[16_384];
            while (true)
            {
                var read = await input.ReadAsync(chunk, ct);
                if (read == 0) break;
                if (buffer.Length + read > CoverImageStorage.MaxFileBytes)
                    return ApiSupport.Problem("COVER_SIZE_INVALID", 400, "Ảnh bìa vượt quá dung lượng tối đa 2 MB.");
                buffer.Write(chunk, 0, read);
            }
        }

        string storedUrl;
        try
        {
            storedUrl = await covers.SaveAsync(user, buffer.ToArray(), ct);
        }
        catch (CoverValidationException e)
        {
            return ApiSupport.Problem(e.Code, 422, e.Detail);
        }

        var previousCover = album.CoverImageUrl;
        album.CoverImageUrl = storedUrl;
        album.UpdatedAt = time.GetUtcNow();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            covers.DeleteCoverUrl(user, storedUrl);
            throw;
        }

        if (!string.Equals(previousCover, storedUrl, StringComparison.Ordinal))
            covers.DeleteCoverUrl(user, previousCover);

        context.Response.Headers.ETag = $"\"{Convert.ToBase64String(album.Version)}\"";
        return Results.Ok(LibraryViews.Album(album, await db.AlbumSongs.CountAsync(x => x.AlbumId == id, ct)));
    }

    private static async Task<IResult> DeleteCover(Guid id, HttpContext context, ClaimsPrincipal u, TMusicDb db, ICoverImageStorage covers, TimeProvider time, CancellationToken ct)
    {
        var user = ApiSupport.UserId(u);
        var album = await db.Albums.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == user, ct);
        if (album is null) return Results.NotFound();
        if (ApiSupport.Match(context, album.Version) is { } mismatch) return mismatch;

        var previous = album.CoverImageUrl;
        album.CoverImageUrl = null;
        album.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        covers.DeleteCoverUrl(user, previous);
        context.Response.Headers.ETag = $"\"{Convert.ToBase64String(album.Version)}\"";
        return Results.Ok(LibraryViews.Album(album, await db.AlbumSongs.CountAsync(x => x.AlbumId == id, ct)));
    }

    private static async Task<IResult> GetCover(Guid userId, string fileName, ClaimsPrincipal u, HttpContext context, ICoverImageStorage covers, CancellationToken ct)
    {
        if (ApiSupport.UserId(u) != userId) return Results.NotFound();
        var opened = await covers.OpenReadAsync(userId, fileName, ct);
        if (opened is null) return Results.NotFound();

        context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.ContentDisposition = "inline";
        return Results.Stream(opened.Value.Stream, opened.Value.ContentType);
    }

    private static async Task<IResult> Songs(Guid id, ClaimsPrincipal u, TMusicDb db, TimeProvider time, int? page, int? pageSize, CancellationToken ct)
    {
        if (!ApiSupport.PageValid(page, pageSize)) return ApiSupport.Problem("INVALID_QUERY");
        var user = ApiSupport.UserId(u);
        if (!await db.Albums.AnyAsync(x => x.Id == id && x.OwnerId == user, ct)) return Results.NotFound();
        var query = db.AlbumSongs.AsNoTrackingWithIdentityResolution().Where(x => x.AlbumId == id && x.UserId == user);
        var total = await query.CountAsync(ct);
        var current = page ?? 1;
        var size = pageSize ?? 30;
        var memberships = await query.OrderBy(x => x.Position).ThenBy(x => x.Id).Skip((current - 1) * size).Take(size)
            .Include(x => x.UserLibrarySong).ThenInclude(x => x.Song)
            .Include(x => x.UserLibrarySong).ThenInclude(x => x.AlbumSongs).AsSplitQuery().ToListAsync(ct);
        var values = memberships.Select(x => x.UserLibrarySong);
        return Results.Ok(new { items = values.Select(x => LibraryViews.Song(x, time.GetUtcNow())), total, page = current, pageSize = size });
    }

    private static async Task<IResult> AddSong(Guid id, MembershipRequest r, ClaimsPrincipal u, TMusicDb db, HttpContext context, IConfiguration config, TimeProvider time, CancellationToken ct)
    {
        var user = ApiSupport.UserId(u);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var album = await db.Albums.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == user, ct);
        if (album is null) return Results.NotFound();
        if (ApiSupport.Match(context, album.Version) is { } mismatch) return mismatch;
        var song = await db.UserLibrarySongs.Include(x => x.Song).FirstOrDefaultAsync(x => x.Id == r.UserLibrarySongId && x.UserId == user, ct);
        if (song is null) return Results.NotFound();
        if (await db.AlbumSongs.AnyAsync(x => x.AlbumId == id && x.UserLibrarySongId == song.Id, ct)) return Results.NoContent();
        if (!LibraryViews.CanAttach(album, song.Song.Provider, config)) return ApiSupport.Problem("COLLECTION_POLICY_RESTRICTED", 409, "Album này giữ riêng dữ liệu theo nguồn.");
        album.SourceBoundary ??= song.Song.Provider + ":bookmarks";
        album.UpdatedAt = time.GetUtcNow();
        var position = await db.AlbumSongs.Where(x => x.AlbumId == id).MaxAsync(x => (long?)x.Position, ct) ?? -1;
        db.AlbumSongs.Add(new AlbumSong { AlbumId = id, UserId = user, UserLibrarySongId = song.Id, Position = position + 1, AddedAt = time.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveSong(Guid id, Guid songId, ClaimsPrincipal u, TMusicDb db, HttpContext context, TimeProvider time, CancellationToken ct)
    {
        var user = ApiSupport.UserId(u);
        var album = await db.Albums.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == user, ct);
        if (album is null) return Results.NotFound();
        if (ApiSupport.Match(context, album.Version) is { } mismatch) return mismatch;
        var membership = await db.AlbumSongs.FirstOrDefaultAsync(x => x.AlbumId == id && x.UserId == user && x.UserLibrarySongId == songId, ct);
        if (membership is null) return Results.NotFound();
        db.AlbumSongs.Remove(membership);
        album.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Reorder(Guid id, OrderRequest r, ClaimsPrincipal u, TMusicDb db, HttpContext context, TimeProvider time, CancellationToken ct)
    {
        if (r.SongIds is null || r.SongIds.Length > 5000 || r.SongIds.Distinct().Count() != r.SongIds.Length) return ApiSupport.Problem("INVALID_ORDER");
        var user = ApiSupport.UserId(u);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var album = await db.Albums.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == user, ct);
        if (album is null) return Results.NotFound();
        if (ApiSupport.Match(context, album.Version) is { } mismatch) return mismatch;
        var entries = await db.AlbumSongs.Where(x => x.AlbumId == id && x.UserId == user).ToDictionaryAsync(x => x.UserLibrarySongId, ct);
        if (entries.Count != r.SongIds.Length || r.SongIds.Any(x => !entries.ContainsKey(x))) return ApiSupport.Problem("INVALID_ORDER");
        for (var i = 0; i < r.SongIds.Length; i++) entries[r.SongIds[i]].Position = i;
        album.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static bool Valid(AlbumRequest r, Guid user) =>
        r.Name?.Trim() is { Length: > 0 and <= 160 } &&
        r.Description?.Length is null or <= 1000 &&
        r.SortOrder is null or >= 0 and <= 1000000 &&
        (string.IsNullOrWhiteSpace(r.CoverImageUrl) ||
         PresetCovers.Contains(r.CoverImageUrl) ||
         CoverImageStorage.ExtractOwnedFileName(user, r.CoverImageUrl) is not null);

    private static readonly char[] ShareAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();
    private static readonly Regex ShareCodePattern = new(@"\A[A-Z2-9]{6,24}\z", RegexOptions.NonBacktracking);

    private static string GenerateShareCode()
    {
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = ShareAlphabet[RandomNumberGenerator.GetInt32(ShareAlphabet.Length)];
        }
        return new string(chars);
    }

    private static async Task<IResult> GetShare(Guid id, ClaimsPrincipal u, TMusicDb db, CancellationToken ct)
    {
        var user = ApiSupport.UserId(u);
        if (!await db.Albums.AnyAsync(x => x.Id == id && x.OwnerId == user, ct)) return Results.NotFound();
        var active = await db.AlbumShares.AsNoTracking()
            .Where(x => x.AlbumId == id && x.OwnerId == user && x.RevokedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (active is null) return Results.NotFound();
        return Results.Ok(new { shareCode = active.ShareCode, albumId = id, createdAt = active.CreatedAt });
    }

    private static async Task<IResult> CreateOrGetShare(Guid id, ClaimsPrincipal u, TMusicDb db, TimeProvider time, CancellationToken ct)
    {
        var user = ApiSupport.UserId(u);
        var album = await db.Albums.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == user, ct);
        if (album is null) return Results.NotFound();

        var existing = await db.AlbumShares.AsNoTracking()
            .Where(x => x.AlbumId == id && x.OwnerId == user && x.RevokedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            return Results.Ok(new { shareCode = existing.ShareCode, albumId = id, albumName = album.Name, createdAt = existing.CreatedAt });
        }

        var now = time.GetUtcNow();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var share = new AlbumShare
            {
                AlbumId = id,
                OwnerId = user,
                ShareCode = GenerateShareCode(),
                CreatedAt = now
            };
            db.AlbumShares.Add(share);
            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { shareCode = share.ShareCode, albumId = id, albumName = album.Name, createdAt = share.CreatedAt });
            }
            catch (DbUpdateException e) when (ApiSupport.IsUnique(e))
            {
                db.ChangeTracker.Clear();
            }
        }
        return ApiSupport.Problem("SHARE_CODE_CONFLICT", 503);
    }

    private static async Task<IResult> RevokeShare(Guid id, ClaimsPrincipal u, TMusicDb db, TimeProvider time, CancellationToken ct)
    {
        var user = ApiSupport.UserId(u);
        if (!await db.Albums.AnyAsync(x => x.Id == id && x.OwnerId == user, ct)) return Results.NotFound();
        var now = time.GetUtcNow();
        await db.AlbumShares
            .Where(x => x.AlbumId == id && x.OwnerId == user && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetSharedAlbum(string code, int? page, int? pageSize, TMusicDb db, TimeProvider time, CancellationToken ct)
    {
        if (page is < 1 or > 10000 || pageSize is < 1 or > 200)
            return ApiSupport.Problem("INVALID_QUERY");
        var current = page ?? 1;
        var size = pageSize ?? 200;
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (!ShareCodePattern.IsMatch(normalized)) return Results.NotFound();

        var share = await db.AlbumShares.AsNoTracking()
            .Include(x => x.Album)
            .FirstOrDefaultAsync(x => x.ShareCode == normalized && x.RevokedAt == null, ct);
        if (share is null) return Results.NotFound();

        var ownerName = await db.Users.AsNoTracking()
            .Where(x => x.Id == share.OwnerId)
            .Select(x => x.DisplayName)
            .FirstOrDefaultAsync(ct);

        var query = db.AlbumSongs.AsNoTrackingWithIdentityResolution()
            .Where(x => x.AlbumId == share.AlbumId && x.UserId == share.OwnerId);
        var total = await query.CountAsync(ct);
        var entries = await query
            .OrderBy(x => x.Position).ThenBy(x => x.Id)
            .Include(x => x.UserLibrarySong).ThenInclude(x => x.Song)
            .Skip((current - 1) * size).Take(size)
            .ToListAsync(ct);

        var now = time.GetUtcNow();
        var safeCover = share.Album.CoverImageUrl is { } c && PresetCovers.Contains(c)
            ? c
            : entries.Select(e => e.UserLibrarySong.Song.ThumbnailUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));

        return Results.Ok(new
        {
            shareCode = share.ShareCode,
            albumName = share.Album.Name,
            description = share.Album.Description,
            coverImageUrl = safeCover,
            sharedBy = string.IsNullOrWhiteSpace(ownerName) ? "Người dùng TMusic" : ownerName,
            songCount = total,
            page = current,
            pageSize = size,
            hasMore = current * size < total,
            createdAt = share.CreatedAt,
            songs = entries.Select(m =>
            {
                var entry = m.UserLibrarySong;
                var s = entry.Song;
                var live = s.MetadataExpiresAt > now;
                return new
                {
                    customTitle = entry.CustomTitle,
                    customArtist = entry.CustomArtist,
                    provider = s.Provider,
                    providerContentId = s.ProviderContentId,
                    canonicalUrl = s.CanonicalUrl,
                    originalUrl = entry.OriginalUrl ?? s.CanonicalUrl,
                    originalTitle = live ? s.OriginalTitle : null,
                    originalCreator = live ? s.OriginalCreator : null,
                    thumbnailUrl = live ? s.ThumbnailUrl : null,
                    durationSeconds = live ? s.DurationSeconds : null,
                    playbackCapability = live ? s.PlaybackCapability : "EXTERNAL_OPEN_ONLY"
                };
            })
        });
    }

    private static async Task<IResult> ImportSharedAlbum(string code, ClaimsPrincipal u, TMusicDb db, TimeProvider time, CancellationToken ct)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (!ShareCodePattern.IsMatch(normalized)) return Results.NotFound();

        var recipient = ApiSupport.UserId(u);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var share = await db.AlbumShares.AsNoTracking()
            .Include(x => x.Album)
            .FirstOrDefaultAsync(x => x.ShareCode == normalized && x.RevokedAt == null, ct);
        if (share is null) return Results.NotFound();

        var ownerName = await db.Users.AsNoTracking()
            .Where(x => x.Id == share.OwnerId)
            .Select(x => x.DisplayName)
            .FirstOrDefaultAsync(ct);
        var sharedByLabel = string.IsNullOrWhiteSpace(ownerName) ? "Người dùng TMusic" : ownerName;

        var sourceEntries = await db.AlbumSongs.AsNoTrackingWithIdentityResolution()
            .Where(x => x.AlbumId == share.AlbumId && x.UserId == share.OwnerId)
            .OrderBy(x => x.Position).ThenBy(x => x.Id)
            .Include(x => x.UserLibrarySong).ThenInclude(x => x.Song)
            .Take(5001)
            .ToListAsync(ct);
        if (sourceEntries.Count > 5000)
            return ApiSupport.Problem("SHARED_ALBUM_TOO_LARGE", 409,
                "Mỗi lần nhận album hỗ trợ tối đa 5.000 bài hát. Hãy chia thành các album nhỏ hơn.");

        var now = time.GetUtcNow();
        var safeCover = share.Album.CoverImageUrl is { } c && PresetCovers.Contains(c) ? c : "cover:green";
        var distinctProviders = sourceEntries.Select(x => x.UserLibrarySong.Song.Provider).Distinct().ToArray();

        var importedAlbum = new Album
        {
            OwnerId = recipient,
            Name = share.Album.Name,
            Description = string.IsNullOrWhiteSpace(share.Album.Description)
                ? $"Chia sẻ từ {sharedByLabel}"
                : share.Album.Description,
            CoverImageUrl = safeCover,
            SourceBoundary = distinctProviders.Length == 1 ? distinctProviders[0] + ":bookmarks" : null,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Albums.Add(importedAlbum);

        var sourceSongIds = sourceEntries.Select(x => x.UserLibrarySong.SongId).Distinct().ToArray();
        var existingUserSongs = await db.UserLibrarySongs
            .Where(x => x.UserId == recipient && sourceSongIds.Contains(x.SongId))
            .ToDictionaryAsync(x => x.SongId, ct);

        long pos = 0;
        foreach (var item in sourceEntries)
        {
            var srcEntry = item.UserLibrarySong;
            if (!existingUserSongs.TryGetValue(srcEntry.SongId, out var recipientSong))
            {
                recipientSong = new UserLibrarySong
                {
                    UserId = recipient,
                    SongId = srcEntry.SongId,
                    CustomTitle = srcEntry.CustomTitle,
                    CustomArtist = srcEntry.CustomArtist,
                    OriginalUrl = srcEntry.OriginalUrl ?? srcEntry.Song.CanonicalUrl,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.UserLibrarySongs.Add(recipientSong);
                existingUserSongs[srcEntry.SongId] = recipientSong;
            }

            db.AlbumSongs.Add(new AlbumSong
            {
                UserId = recipient,
                AlbumId = importedAlbum.Id,
                UserLibrarySongId = recipientSong.Id,
                Position = pos++,
                AddedAt = now
            });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Created($"/api/v1/albums/{importedAlbum.Id}", LibraryViews.Album(importedAlbum, sourceEntries.Count));
    }

    public sealed record AlbumRequest(string? Name, string? Description, string? CoverImageUrl, int? SortOrder, bool? IsFavorite);
    public sealed record MembershipRequest(Guid UserLibrarySongId);
    public sealed record OrderRequest(Guid[]? SongIds);
    public sealed record AlbumOrderRequest(Guid[]? AlbumIds);
}
