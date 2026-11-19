using TMusic.Domain.Entities;
namespace TMusic.Api.Library;

public static class LibraryViews
{
    public static object Song(UserLibrarySong entry,DateTimeOffset now,Guid? downloadJobId=null)
    {
        var source=entry.Song; var live=source.MetadataExpiresAt > now;
        var audioReady=entry.AudioStatus==ImportStatus.Ready && entry.AudioFileName is { Length: >0 };
        return new { entry.Id,entry.SongId,source.Provider,source.ProviderContentId,source.CanonicalUrl,
            originalUrl=entry.OriginalUrl ?? source.CanonicalUrl,
            originalTitle=live?source.OriginalTitle:null,originalCreator=live?source.OriginalCreator:null,
            thumbnailUrl=live?source.ThumbnailUrl:null,durationSeconds=live?source.DurationSeconds:null,
            metadataFetchedAt=live?source.MetadataFetchedAt:null,
            playbackCapability=live?source.PlaybackCapability:"EXTERNAL_OPEN_ONLY",
            audioReady,
            audioStatus=entry.AudioStatus ?? "NONE",
            audioStreamUrl=audioReady?$"/api/v1/library/songs/{entry.Id}/audio":null,
            audioSizeBytes=audioReady?entry.AudioSizeBytes:null,
            downloadJobId,
            entry.CustomTitle,entry.CustomArtist,entry.IsFavorite,entry.CreatedAt,entry.UpdatedAt,entry.Version,
            albumIds=entry.AlbumSongs.Select(x=>x.AlbumId).ToArray() };
    }
    public static object Album(Album album,int count) => new { album.Id,album.Name,album.Description,album.CoverImageUrl,
        album.SortOrder,album.IsFavorite,album.CreatedAt,album.UpdatedAt,album.Version,songCount=count };
}

