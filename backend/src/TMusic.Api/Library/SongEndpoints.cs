using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TMusic.Api.Common;
using TMusic.Application.Providers;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Downloads;
using TMusic.Infrastructure.Persistence;
using TMusic.Infrastructure.Storage;
namespace TMusic.Api.Library;

public static class SongEndpoints
{
    public static void MapSongEndpoints(this RouteGroupBuilder api)
    {
        var songs=api.MapGroup("/library/songs").RequireAuthorization();
        songs.MapGet("",List); songs.MapPost("",Create).RequireRateLimiting("provider"); songs.MapGet("/{id:guid}",Get);
        songs.MapPatch("/{id:guid}",Update); songs.MapDelete("/{id:guid}",Delete);
        songs.MapPatch("/{id:guid}/favorite",Favorite);
        api.MapGet("/search",Search).RequireAuthorization();
    }
    private static IQueryable<UserLibrarySong> Filter(TMusicDb db,Guid user,DateTimeOffset now,string? q,string? provider,bool? favorite,bool? unorganized)
    {
        var query=db.UserLibrarySongs.AsNoTracking().Where(x=>x.UserId==user);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term=q.Trim();
            query=query.Where(x=>x.CustomTitle.Contains(term) ||
                (x.CustomArtist!=null && x.CustomArtist.Contains(term)) ||
                (x.Song.MetadataExpiresAt>now && ((x.Song.OriginalTitle!=null && x.Song.OriginalTitle.Contains(term)) || (x.Song.OriginalCreator!=null && x.Song.OriginalCreator.Contains(term)))));
        }
        if (!string.IsNullOrWhiteSpace(provider)) query=query.Where(x=>x.Song.Provider==provider);
        if (favorite.HasValue) query=query.Where(x=>x.IsFavorite==favorite.Value);
        if (unorganized.HasValue) query=unorganized.Value ? query.Where(x=>!x.AlbumSongs.Any()) : query.Where(x=>x.AlbumSongs.Any());
        return query;
    }
    private static async Task<IResult> List(ClaimsPrincipal u,TMusicDb db,TimeProvider time,int? page,int? pageSize,string? q,string? provider,bool? favorite,bool? unorganized,string? sort,CancellationToken ct)
    {
        if (!ApiSupport.PageValid(page,pageSize,q) || provider is not (null or "" or "youtube" or "tiktok" or "soundcloud") || sort is not (null or "recent" or "name")) return ApiSupport.Problem("INVALID_QUERY");
        var now=time.GetUtcNow();
        var query=Filter(db,ApiSupport.UserId(u),now,q,provider,favorite,unorganized); var total=await query.CountAsync(ct); var current=page??1; var size=pageSize??30;
        var sorted=sort=="name"?query.OrderBy(x=>x.CustomTitle).ThenBy(x=>x.Id):query.OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id);
        var items=await sorted.Skip((current-1)*size).Take(size).Include(x=>x.Song).Include(x=>x.AlbumSongs).AsSplitQuery().ToListAsync(ct);
        return Results.Ok(new {items=items.Select(x=>LibraryViews.Song(x,now)),total,page=current,pageSize=size});
    }
    private static async Task<IResult> Get(Guid id,ClaimsPrincipal u,TMusicDb db,TimeProvider time,HttpContext context,CancellationToken ct)
    {
        var user=ApiSupport.UserId(u); var song=await db.UserLibrarySongs.AsNoTracking().Include(x=>x.Song).Include(x=>x.AlbumSongs)
            .AsSplitQuery().FirstOrDefaultAsync(x=>x.Id==id && x.UserId==user,ct);
        if (song is null) return Results.NotFound();
        context.Response.Headers.ETag=$"\"{Convert.ToBase64String(song.Version)}\"";
        return Results.Ok(LibraryViews.Song(song,time.GetUtcNow()));
    }
    private static async Task<IResult> Create(CreateSongRequest r,ClaimsPrincipal u,TMusicDb db,ISourceResolver resolver,IAudioExtractor extractor,IConfiguration config,TimeProvider clock,CancellationToken ct)
    {
        if (r.Url is not { Length: >0 and <=2048 } || !ValidTitle(r.CustomTitle,r.CustomArtist) || r.AlbumIds?.Length>20) return ApiSupport.Problem("INVALID_SONG");
        if (r.DownloadMp3)
        {
            if (!r.RightsConfirmed) return ApiSupport.Problem("RIGHTS_CONFIRMATION_REQUIRED",400,"Vui lòng xác nhận bạn có quyền tải nội dung này cho mục đích cá nhân.");
            if (!extractor.IsAvailable) return ApiSupport.Problem("AUDIO_DOWNLOAD_UNAVAILABLE",503,"Bộ xử lý tải MP3 (yt-dlp / FFmpeg) hiện chưa sẵn sàng.");
        }
        var user=ApiSupport.UserId(u); var albumIds=(r.AlbumIds??[]).Distinct().ToArray();
        ResolvedSource source;
        try { source=await resolver.ResolveAsync(r.Url,ct); } catch (ProviderException e) { return ApiSupport.Problem(e.Code,e.Retryable?503:400); }
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var albums=await db.Albums.Where(x=>x.OwnerId==user && albumIds.Contains(x.Id)).ToListAsync(ct);
        if (albums.Count!=albumIds.Length) return Results.NotFound();
        if (albums.Any(x=>!LibraryViews.CanAttach(x,source.Provider,config))) return ApiSupport.Problem("COLLECTION_POLICY_RESTRICTED",409,"Chọn album cùng nguồn hoặc tạo album mới.");
        var now=clock.GetUtcNow();
        var song=await db.Songs.SingleOrDefaultAsync(x=>x.Provider==source.Provider && x.ProviderContentId==source.ProviderContentId,ct);
        if (song is null)
        {
            song=new Song { Provider=source.Provider,ProviderContentId=source.ProviderContentId,CanonicalUrl=source.CanonicalUrl,
                OriginalUrl=source.CanonicalUrl,CreatedAt=now };
            db.Songs.Add(song);
        }
        if (source.MetadataFetchedAt is not null)
        {
            song.OriginalTitle=source.OriginalTitle; song.OriginalCreator=source.OriginalCreator; song.ThumbnailUrl=source.ThumbnailUrl;
            song.DurationSeconds=source.DurationSeconds; song.MetadataFetchedAt=source.MetadataFetchedAt;
            song.MetadataExpiresAt=source.MetadataFetchedAt.Value.AddDays(29); song.PlaybackCapability=source.PlaybackCapability;
        }
        var entry=await db.UserLibrarySongs.Include(x=>x.AlbumSongs).SingleOrDefaultAsync(x=>x.UserId==user && x.SongId==song.Id,ct);
        var created=entry is null;
        if (entry is null)
        {
            entry=new UserLibrarySong { UserId=user,Song=song,SongId=song.Id,CustomTitle=r.CustomTitle!.Trim(),CustomArtist=r.CustomArtist?.Trim(),
                OriginalUrl=source.OriginalUrl,CreatedAt=now,UpdatedAt=now };
            db.UserLibrarySongs.Add(entry);
        }
        foreach (var album in albums)
        {
            if (entry.AlbumSongs.Any(x=>x.AlbumId==album.Id)) continue;
            var position=await db.AlbumSongs.Where(x=>x.AlbumId==album.Id).MaxAsync(x=>(long?)x.Position,ct)??-1;
            entry.AlbumSongs.Add(new AlbumSong { UserId=user,AlbumId=album.Id,UserLibrarySongId=entry.Id,Position=position+1,AddedAt=now });
            album.SourceBoundary??=source.Provider+":bookmarks"; album.UpdatedAt=now;
        }
        Guid? queuedJobId=null;
        if (r.DownloadMp3 && !(entry.AudioStatus==ImportStatus.Ready && entry.AudioFileName is { Length: >0 }))
        {
            var existingJob=await db.AudioDownloadJobs
                .Where(x=>x.OwnerId==user && x.UserLibrarySongId==entry.Id && (x.Status==ImportStatus.Queued || x.Status==ImportStatus.Running))
                .OrderByDescending(x=>x.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (existingJob is not null)
            {
                queuedJobId=existingJob.Id;
            }
            else
            {
                var job=new AudioDownloadJob
                {
                    OwnerId=user,
                    UserLibrarySongId=entry.Id,
                    Provider=source.Provider,
                    CanonicalUrl=source.CanonicalUrl,
                    RightsConfirmed=true,
                    Status=ImportStatus.Queued,
                    ProgressPercent=0,
                    CreatedAt=now,
                    NextAttemptAt=now
                };
                db.AudioDownloadJobs.Add(job);
                entry.AudioStatus=ImportStatus.Queued;
                entry.UpdatedAt=now;
                queuedJobId=job.Id;
            }
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return created?Results.Created($"/api/v1/library/songs/{entry.Id}",LibraryViews.Song(entry,now,queuedJobId)):Results.Ok(LibraryViews.Song(entry,now,queuedJobId));
    }
    private static async Task<IResult> Update(Guid id,EditSongRequest r,ClaimsPrincipal u,TMusicDb db,TimeProvider time,HttpContext context,CancellationToken ct)
    {
        if (!ValidTitle(r.CustomTitle,r.CustomArtist)) return ApiSupport.Problem("INVALID_SONG");
        var user=ApiSupport.UserId(u); var song=await db.UserLibrarySongs.Include(x=>x.Song).Include(x=>x.AlbumSongs).AsSplitQuery().FirstOrDefaultAsync(x=>x.Id==id && x.UserId==user,ct);
        if (song is null) return Results.NotFound(); if (ApiSupport.Match(context,song.Version) is { } mismatch) return mismatch;
        song.CustomTitle=r.CustomTitle!.Trim(); song.CustomArtist=r.CustomArtist?.Trim(); song.UpdatedAt=time.GetUtcNow();
        await db.SaveChangesAsync(ct); return Results.Ok(LibraryViews.Song(song,time.GetUtcNow()));
    }
    private static async Task<IResult> Favorite(Guid id,FavoriteRequest r,ClaimsPrincipal u,TMusicDb db,TimeProvider time,HttpContext context,CancellationToken ct)
    {
        var user=ApiSupport.UserId(u); var song=await db.UserLibrarySongs.Include(x=>x.Song).Include(x=>x.AlbumSongs).AsSplitQuery().FirstOrDefaultAsync(x=>x.Id==id && x.UserId==user,ct);
        if (song is null) return Results.NotFound(); if (ApiSupport.Match(context,song.Version) is { } mismatch) return mismatch;
        song.IsFavorite=r.IsFavorite; song.UpdatedAt=time.GetUtcNow(); await db.SaveChangesAsync(ct);
        return Results.Ok(LibraryViews.Song(song,time.GetUtcNow()));
    }
    private static async Task<IResult> Delete(Guid id,ClaimsPrincipal u,TMusicDb db,IAudioStorage audio,TimeProvider time,HttpContext context,CancellationToken ct)
    {
        var user=ApiSupport.UserId(u); var song=await db.UserLibrarySongs.FirstOrDefaultAsync(x=>x.Id==id && x.UserId==user,ct);
        if (song is null) return Results.NotFound(); if (ApiSupport.Match(context,song.Version) is { } mismatch) return mismatch;
        var deletedAudioFile=song.AudioFileName;
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var albums=await db.AlbumSongs.Where(x=>x.UserLibrarySongId==id && x.UserId==user).Select(x=>x.AlbumId).ToArrayAsync(ct);
        await db.AlbumSongs.Where(x=>x.UserLibrarySongId==id && x.UserId==user).ExecuteDeleteAsync(ct);
        await db.AudioDownloadJobs.Where(x=>x.UserLibrarySongId==id && x.OwnerId==user).ExecuteDeleteAsync(ct);
        await db.Albums.Where(x=>albums.Contains(x.Id) && x.OwnerId==user).ExecuteUpdateAsync(set=>set.SetProperty(x=>x.UpdatedAt,time.GetUtcNow()),ct);
        db.UserLibrarySongs.Remove(song); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        audio.DeleteAudioFile(user,deletedAudioFile);
        return Results.NoContent();
    }
    private static async Task<IResult> Search(string? q,ClaimsPrincipal u,TMusicDb db,TimeProvider time,int? page,int? pageSize,CancellationToken ct)
    {
        if (!ApiSupport.PageValid(page,pageSize,q) || string.IsNullOrWhiteSpace(q)) return ApiSupport.Problem("INVALID_QUERY");
        var user=ApiSupport.UserId(u); var term=q.Trim(); var current=page??1; var size=pageSize??30; var now=time.GetUtcNow();
        var albumsQuery=db.Albums.AsNoTracking().Where(x=>x.OwnerId==user && (x.Name.Contains(term) || (x.Description!=null && x.Description.Contains(term))));
        var albums=await albumsQuery.OrderBy(x=>x.Name).ThenBy(x=>x.Id).Skip((current-1)*size).Take(size).Select(x=>new { album=x,count=x.Songs.Count }).ToListAsync(ct);
        var songsQuery=Filter(db,user,now,term,null,null,null);
        var songs=await songsQuery.OrderBy(x=>x.CustomTitle).ThenBy(x=>x.Id).Skip((current-1)*size).Take(size).Include(x=>x.Song).Include(x=>x.AlbumSongs).AsSplitQuery().ToListAsync(ct);
        return Results.Ok(new { albums=new {items=albums.Select(x=>LibraryViews.Album(x.album,x.count)),total=await albumsQuery.CountAsync(ct),page=current,pageSize=size},
            songs=new {items=songs.Select(x=>LibraryViews.Song(x,now)),total=await songsQuery.CountAsync(ct),page=current,pageSize=size} });
    }
    private static bool ValidTitle(string? title,string? artist) => title?.Trim() is { Length: >0 and <=200 } && artist?.Length is null or <=200;
    public sealed record CreateSongRequest(string? Url,string? CustomTitle,string? CustomArtist,Guid[]? AlbumIds,bool DownloadMp3=false,bool RightsConfirmed=false);
    public sealed record EditSongRequest(string? CustomTitle,string? CustomArtist);
    public sealed record FavoriteRequest(bool IsFavorite);
}


