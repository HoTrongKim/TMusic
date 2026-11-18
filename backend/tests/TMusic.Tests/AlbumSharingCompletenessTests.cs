using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Persistence;
using Xunit;
using static TMusic.Tests.SqlApiFixture;

namespace TMusic.Tests;

[Collection("SQL API")]
public sealed class AlbumSharingCompletenessTests(SqlApiFixture fixture)
{
    [SqlFact]
    public async Task PreviewPagesAndImportPreserveEverySongBeyondTwoHundred()
    {
        var owner = await fixture.RegisterAsync();
        var recipient = await fixture.RegisterAsync();
        using var source = fixture.Client(owner.AccessToken);
        using var target = fixture.Client(recipient.AccessToken);
        var album = await Json(await source.PostAsJsonAsync("/api/v1/albums", new { name = "Album đầy đủ" }));
        var albumId = album.GetProperty("id").GetGuid();
        Guid lastId = default;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TMusicDb>();
            for (var i = 0; i < 201; i++)
            {
                var contentId = Guid.NewGuid().ToString("N")[..11];
                var song = new Song { Provider = "youtube", ProviderContentId = contentId, CanonicalUrl = $"https://www.youtube.com/watch?v={contentId}", OriginalUrl = $"https://www.youtube.com/watch?v={contentId}" };
                var entry = new UserLibrarySong { UserId = owner.UserId, SongId = song.Id, Song = song, CustomTitle = $"Bài {i}" };
                db.UserLibrarySongs.Add(entry);
                db.AlbumSongs.Add(new AlbumSong { AlbumId = albumId, UserId = owner.UserId, UserLibrarySongId = entry.Id, Position = i });
                lastId = song.Id;
            }
            await db.SaveChangesAsync();
        }
        var share = await Json(await source.PostAsJsonAsync($"/api/v1/albums/{albumId}/share", new { }));
        var code = share.GetProperty("shareCode").GetString();
        var first = await Json(await target.GetAsync($"/api/v1/shared-albums/{code}"));
        Assert.Equal(201, first.GetProperty("songCount").GetInt32());
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        Assert.Equal(200, first.GetProperty("songs").GetArrayLength());
        var second = await Json(await target.GetAsync($"/api/v1/shared-albums/{code}?page=2&pageSize=200"));
        Assert.False(second.GetProperty("hasMore").GetBoolean());
        Assert.Equal("Bài 200", second.GetProperty("songs")[0].GetProperty("customTitle").GetString());
        Assert.Equal(1, second.GetProperty("songs").GetArrayLength());
        var imported = await Json(await target.PostAsJsonAsync($"/api/v1/shared-albums/{code}/import", new { }));
        Assert.Equal(201, imported.GetProperty("songCount").GetInt32());
        var importedId = imported.GetProperty("id").GetGuid();
        await using var verify = fixture.Factory.Services.CreateAsyncScope();
        var resultDb = verify.ServiceProvider.GetRequiredService<TMusicDb>();
        Assert.Equal(201, await resultDb.AlbumSongs.CountAsync(x => x.AlbumId == importedId && x.UserId == recipient.UserId));
        Assert.True(await resultDb.AlbumSongs.AnyAsync(x => x.AlbumId == importedId && x.Position == 200 && x.UserLibrarySong.SongId == lastId));
    }

    [SqlFact]
    public async Task PreviewRejectsUnboundedOrInvalidPageRequests()
    {
        var owner = await fixture.RegisterAsync();
        using var client = fixture.Client(owner.AccessToken);
        var album = await Json(await client.PostAsJsonAsync("/api/v1/albums", new { name = "Phân trang" }));
        var share = await Json(await client.PostAsJsonAsync($"/api/v1/albums/{album.GetProperty("id").GetGuid()}/share", new { }));
        var code = share.GetProperty("shareCode").GetString();
        foreach (var query in new[] { "page=0", "page=-1", "pageSize=201", "pageSize=0", "page=2147483647" })
            await Expect(await client.GetAsync($"/api/v1/shared-albums/{code}?{query}"), HttpStatusCode.BadRequest);
    }
}
