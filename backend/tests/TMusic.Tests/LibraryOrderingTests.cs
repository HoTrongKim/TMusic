using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TMusic.Infrastructure.Persistence;
using Xunit;
using static TMusic.Tests.SqlApiFixture;
namespace TMusic.Tests;

[Collection("SQL API")]
public sealed class LibraryOrderingTests(SqlApiFixture fixture)
{
    [SqlFact]
    public async Task RemoveAndReorderAffectOnlyMembershipAndValidateCompleteOrder()
    {
        var account=await fixture.RegisterAsync(); using var client=fixture.Client(account.AccessToken);
        var album=await Json(await client.PostAsJsonAsync("/api/v1/albums",new {name="Thứ tự riêng"})); var albumId=album.GetProperty("id").GetGuid();
        var first=await Json(await client.PostAsJsonAsync("/api/v1/library/songs",new {url="https://youtu.be/Ordering001",customTitle="Một",albumIds=new[]{albumId}}));
        var second=await Json(await client.PostAsJsonAsync("/api/v1/library/songs",new {url="https://youtu.be/Ordering002",customTitle="Hai",albumIds=new[]{albumId}}));
        var firstId=first.GetProperty("id").GetGuid(); var secondId=second.GetProperty("id").GetGuid();
        album=await Json(await client.GetAsync($"/api/v1/albums/{albumId}")); var version=album.GetProperty("version").GetString();
        await Expect(await Mutation(client,HttpMethod.Patch,$"/api/v1/albums/{albumId}/songs/order",new {songIds=new[]{firstId}},version),HttpStatusCode.BadRequest);
        await Expect(await Mutation(client,HttpMethod.Patch,$"/api/v1/albums/{albumId}/songs/order",new {songIds=new[]{secondId,firstId}},version),HttpStatusCode.NoContent);
        var ordered=await Json(await client.GetAsync($"/api/v1/albums/{albumId}/songs"));
        Assert.Equal(secondId,ordered.GetProperty("items")[0].GetProperty("id").GetGuid());
        album=await Json(await client.GetAsync($"/api/v1/albums/{albumId}"));
        await Expect(await Mutation(client,HttpMethod.Delete,$"/api/v1/albums/{albumId}/songs/{firstId}",version:album.GetProperty("version").GetString()),HttpStatusCode.NoContent);
        await Expect(await client.GetAsync($"/api/v1/library/songs/{firstId}"),HttpStatusCode.OK);
        ordered=await Json(await client.GetAsync($"/api/v1/albums/{albumId}/songs")); Assert.Equal(1,ordered.GetProperty("total").GetInt32());
    }
    [SqlFact]
    public async Task SongVersionProtectsEditsAndRemovingSongCleansEveryMembership()
    {
        var account=await fixture.RegisterAsync(); using var client=fixture.Client(account.AccessToken);
        var a=await Json(await client.PostAsJsonAsync("/api/v1/albums",new {name="A"})); var b=await Json(await client.PostAsJsonAsync("/api/v1/albums",new {name="B"}));
        var song=await Json(await client.PostAsJsonAsync("/api/v1/library/songs",new {url="https://youtu.be/Ordering003",customTitle="Gốc",albumIds=new[]{a.GetProperty("id").GetGuid(),b.GetProperty("id").GetGuid()}}));
        var id=song.GetProperty("id").GetGuid(); var version=song.GetProperty("version").GetString();
        var updated=await Json(await Mutation(client,HttpMethod.Patch,$"/api/v1/library/songs/{id}",new {customTitle="Tên mới",customArtist="Ghi chú"},version));
        await Expect(await Mutation(client,HttpMethod.Patch,$"/api/v1/library/songs/{id}",new {customTitle="Stale"},version),HttpStatusCode.Conflict);
        await Expect(await Mutation(client,HttpMethod.Delete,$"/api/v1/library/songs/{id}",version:updated.GetProperty("version").GetString()),HttpStatusCode.NoContent);
        await using var scope=fixture.Factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<TMusicDb>();
        Assert.False(await db.AlbumSongs.AnyAsync(x=>x.UserLibrarySongId==id));
        Assert.Equal(2,await db.Albums.CountAsync(x=>x.OwnerId==account.UserId));
    }
    [SqlFact]
    public async Task PrivateOriginalUrlDoesNotLeakAcrossUsersAndMixedCollectionGateIsEnforced()
    {
        var alice=await fixture.RegisterAsync(); var bob=await fixture.RegisterAsync();
        using var a=fixture.Client(alice.AccessToken); using var b=fixture.Client(bob.AccessToken);
        await Json(await a.PostAsJsonAsync("/api/v1/library/songs",new {url="https://youtu.be/Ordering004?si=alice-private",customTitle="A"}));
        var other=await Json(await b.PostAsJsonAsync("/api/v1/library/songs",new {url="https://youtu.be/Ordering004",customTitle="B"}));
        Assert.DoesNotContain("alice-private",other.GetProperty("originalUrl").GetString());
        var album=await Json(await a.PostAsJsonAsync("/api/v1/albums",new {name="YouTube riêng"})); var id=album.GetProperty("id").GetGuid();
        await Json(await a.PostAsJsonAsync("/api/v1/library/songs",new {url="https://youtu.be/Ordering005",customTitle="YouTube",albumIds=new[]{id}}));
        await Expect(await a.PostAsJsonAsync("/api/v1/library/songs",new {url="https://www.tiktok.com/@tester/video/1234567890123456789",customTitle="TikTok",albumIds=new[]{id}}),HttpStatusCode.Conflict);
        var list=await Json(await a.GetAsync("/api/v1/library/songs?provider=tiktok")); Assert.Equal(0,list.GetProperty("total").GetInt32());
    }
}
