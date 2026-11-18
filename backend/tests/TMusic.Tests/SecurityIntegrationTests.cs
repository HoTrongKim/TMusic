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
public sealed class SecurityIntegrationTests(SqlApiFixture fixture)
{
    [SqlFact]
    public async Task Private_endpoints_are_inaccessible_without_a_session()
    {
        using var client=fixture.Client();
        foreach(var path in new[]{"/albums","/library/songs","/search?q=test","/users/me","/users/me/settings","/auth/sessions","/imports"})
            await Expect(await client.GetAsync("/api/v1"+path),HttpStatusCode.Unauthorized);
        await Expect(await client.PostAsJsonAsync("/api/v1/songs/resolve",new{url="https://youtu.be/AbCdEfGhI01"}),HttpStatusCode.Unauthorized);
        await Expect(await client.PostAsJsonAsync("/api/v1/albums",new{name="Không được tạo"}),HttpStatusCode.Unauthorized);
    }
    [SqlFact]
    public async Task Another_user_cannot_read_edit_attach_or_delete_private_resources()
    {
        var alice=await fixture.RegisterAsync(); var bob=await fixture.RegisterAsync();
        using var a=fixture.Client(alice.AccessToken); using var b=fixture.Client(bob.AccessToken);
        var album=await Json(await a.PostAsJsonAsync("/api/v1/albums",new{name="Bí mật A"}));
        var albumId=album.GetProperty("id").GetGuid(); var version=album.GetProperty("version").GetString();
        await Json(await a.PostAsJsonAsync("/api/v1/library/songs",new{url="https://youtu.be/AbCdEfGhI05",customTitle="Bài của A",albumIds=new[]{albumId}}));
        var songs=await Json(await a.GetAsync("/api/v1/library/songs")); var song=songs.GetProperty("items")[0]; var songId=song.GetProperty("id").GetGuid();
        await Expect(await b.GetAsync($"/api/v1/albums/{albumId}"),HttpStatusCode.NotFound);
        await Expect(await b.GetAsync($"/api/v1/albums/{albumId}/songs"),HttpStatusCode.NotFound);
        await Expect(await b.GetAsync($"/api/v1/library/songs/{songId}"),HttpStatusCode.NotFound);
        await Expect(await Mutation(b,HttpMethod.Patch,$"/api/v1/albums/{albumId}",new{name="B sửa trộm"},version),HttpStatusCode.NotFound);
        await Expect(await Mutation(b,HttpMethod.Delete,$"/api/v1/albums/{albumId}",version:version),HttpStatusCode.NotFound);
        var attachment=await b.PostAsJsonAsync("/api/v1/library/songs",new{url="https://youtu.be/AbCdEfGhI06",customTitle="Không có quyền",albumIds=new[]{albumId}});
        Assert.False(attachment.IsSuccessStatusCode);
        var list=await Json(await b.GetAsync("/api/v1/library/songs")); Assert.Equal(0,list.GetProperty("total").GetInt32());
        await using var scope=fixture.Factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<TMusicDb>();
        var bobAlbum=new Album{OwnerId=bob.UserId,Name="Album B"}; db.Albums.Add(bobAlbum); await db.SaveChangesAsync();
        db.AlbumSongs.Add(new AlbumSong{AlbumId=bobAlbum.Id,UserId=bob.UserId,UserLibrarySongId=songId,Position=0});
        await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
    }
    [SqlFact]
    public async Task Device_session_revocation_checks_owner_and_logout_all_invalidates_access()
    {
        var alice=await fixture.RegisterAsync(); var bob=await fixture.RegisterAsync();
        using var a=fixture.Client(alice.AccessToken); using var b=fixture.Client(bob.AccessToken);
        var sessions=await Json(await a.GetAsync("/api/v1/auth/sessions")); var id=sessions.GetProperty("items")[0].GetProperty("id").GetGuid();
        await b.DeleteAsync($"/api/v1/auth/sessions/{id}");
        await Expect(await a.GetAsync("/api/v1/users/me"),HttpStatusCode.OK);
        await Expect(await a.PostAsJsonAsync("/api/v1/auth/logout-all",new{}),HttpStatusCode.NoContent);
        await Expect(await a.GetAsync("/api/v1/users/me"),HttpStatusCode.Unauthorized);
        await Expect(await b.GetAsync("/api/v1/users/me"),HttpStatusCode.OK);
    }
    [SqlFact]
    public async Task Favorites_search_profile_and_settings_persist_per_account()
    {
        var account=await fixture.RegisterAsync(); using var client=fixture.Client(account.AccessToken);
        await Json(await client.PostAsJsonAsync("/api/v1/library/songs",new{url="https://youtu.be/AbCdEfGhI07",customTitle="Nhạc nhẹ đêm",customArtist="Ghi chú cá nhân"}));
        var list=await Json(await client.GetAsync("/api/v1/library/songs")); var song=list.GetProperty("items")[0]; var id=song.GetProperty("id").GetGuid();
        var updated=await Json(await Mutation(client,HttpMethod.Patch,$"/api/v1/library/songs/{id}/favorite",new{isFavorite=true},song.GetProperty("version").GetString()));
        Assert.True(updated.GetProperty("isFavorite").GetBoolean());
        var favorites=await Json(await client.GetAsync("/api/v1/library/songs?favorite=true&provider=youtube&q="+Uri.EscapeDataString("Nhạc nhẹ")));
        Assert.Equal(1,favorites.GetProperty("total").GetInt32());
        await Expect(await client.GetAsync("/api/v1/library/songs?pageSize=10001"),HttpStatusCode.BadRequest);
        var profile=await Json(await Mutation(client,HttpMethod.Patch,"/api/v1/users/me",new{displayName="Tên cá nhân"}));
        Assert.Equal("Tên cá nhân",profile.GetProperty("displayName").GetString());
        await Json(await Mutation(client,HttpMethod.Patch,"/api/v1/users/me/settings",new{theme="dark",language="vi",notificationsEnabled=false}));
        var settings=await Json(await client.GetAsync("/api/v1/users/me/settings")); Assert.Equal("dark",settings.GetProperty("theme").GetString());
        var search=await Json(await client.GetAsync("/api/v1/search?q="+Uri.EscapeDataString("Nhạc nhẹ"))); Assert.Contains("Nhạc nhẹ",search.ToString());
    }
    [SqlFact]
    public async Task Album_idempotency_replays_same_request_and_rejects_different_body()
    {
        var account=await fixture.RegisterAsync(); using var client=fixture.Client(account.AccessToken);
        client.DefaultRequestHeaders.Add("Idempotency-Key",Guid.NewGuid().ToString("N"));
        var first=await Json(await client.PostAsJsonAsync("/api/v1/albums",new{name="Chỉ một album"}));
        var second=await Json(await client.PostAsJsonAsync("/api/v1/albums",new{name="Chỉ một album"}));
        Assert.Equal(first.GetProperty("id").GetGuid(),second.GetProperty("id").GetGuid());
        await Expect(await client.PostAsJsonAsync("/api/v1/albums",new{name="Khác dữ liệu"}),HttpStatusCode.Conflict);
    }
    [SqlFact]
    public async Task Wrong_password_attempts_lock_account_and_errors_do_not_leak_sensitive_details()
    {
        var account=await fixture.RegisterAsync(); using var client=fixture.Client();
        for(var i=0;i<6;i++)
            await Expect(await client.PostAsJsonAsync("/api/v1/auth/login",new{email=account.Email,password="incorrect-Password!45"}),HttpStatusCode.Unauthorized);
        var response=await client.PostAsJsonAsync("/api/v1/auth/login",new{email=account.Email,password=Password});
        await Expect(response,HttpStatusCode.Unauthorized);
        var body=await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("PasswordHash",body); Assert.DoesNotContain("SqlException",body); Assert.DoesNotContain(Password,body);
    }
}
