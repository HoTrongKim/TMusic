using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TMusic.Application.Identity;
using TMusic.Infrastructure.Persistence;
using Xunit;
using static TMusic.Tests.SqlApiFixture;
namespace TMusic.Tests;

[Collection("SQL API")]
public sealed class AccountLibraryIntegrationTests(SqlApiFixture fixture)
{
    [SqlFact]
    public async Task Register_issues_session_immediately_without_email_verification_and_stores_only_framework_password_hash()
    {
        var email=$"register-{Guid.NewGuid():N}@tmusic.test";
        using var client=fixture.Client();
        var createdResponse=await client.PostAsJsonAsync("/api/v1/auth/register",new {email,password=Password,displayName="Người dùng mới"});
        await Expect(createdResponse,HttpStatusCode.Created);
        var session=await Json(createdResponse);
        await Expect(await client.PostAsJsonAsync("/api/v1/auth/register",new {email,password=Password}),HttpStatusCode.Conflict);
        await Expect(await client.PostAsJsonAsync("/api/v1/auth/verify-email",new {email,code="removed"}),HttpStatusCode.NotFound);
        using var authorized=fixture.Client(session.GetProperty("accessToken").GetString());
        var profile=await Json(await authorized.GetAsync("/api/v1/users/me"));
        Assert.Equal("Người dùng mới",profile.GetProperty("displayName").GetString());
        await using var scope=fixture.Factory.Services.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<TMusicDb>();
        var account=await db.Users.SingleAsync(x=>x.Email==email);
        Assert.NotEqual(Password,account.PasswordHash);
        Assert.True(account.PasswordHash?.Length>40);
        Assert.DoesNotContain(await db.UserSessions.Where(x=>x.UserId==account.Id).ToListAsync(),x=>x.RefreshTokenHash==session.GetProperty("refreshToken").GetString());
    }
    [SqlFact]
    public async Task Library_deduplicates_by_source_id_and_album_deletion_keeps_personal_song()
    {
        var account=await fixture.RegisterAsync();
        using var client=fixture.Client(account.AccessToken);
        var first=await Json(await client.PostAsJsonAsync("/api/v1/albums",new {name="Buổi tối",description="Album thứ nhất"}));
        var second=await Json(await client.PostAsJsonAsync("/api/v1/albums",new {name="Yêu thích"}));
        var firstId=first.GetProperty("id").GetGuid(); var secondId=second.GetProperty("id").GetGuid();
        var saved=await Json(await client.PostAsJsonAsync("/api/v1/library/songs",new {url="https://youtu.be/AbCdEfGhI01",customTitle="Tên riêng của tôi",customArtist="Ghi chú",albumIds=new[]{firstId,secondId}}));
        var duplicate=await Json(await client.PostAsJsonAsync("/api/v1/library/songs",new {url="https://www.youtube.com/shorts/AbCdEfGhI01",customTitle="Không ghi đè tên cũ",albumIds=new[]{firstId}}));
        var songs=await Json(await client.GetAsync("/api/v1/library/songs"));
        Assert.Equal(1,songs.GetProperty("total").GetInt32());
        var song=songs.GetProperty("items")[0];
        Assert.Equal("Tên riêng của tôi",song.GetProperty("customTitle").GetString());
        var songId=song.GetProperty("id").GetGuid();
        var firstNow=await Json(await client.GetAsync($"/api/v1/albums/{firstId}"));
        await Expect(await Mutation(client,HttpMethod.Delete,$"/api/v1/albums/{firstId}",version:firstNow.GetProperty("version").GetString()),HttpStatusCode.NoContent);
        await Expect(await client.GetAsync($"/api/v1/library/songs/{songId}"),HttpStatusCode.OK);
        var secondSongs=await Json(await client.GetAsync($"/api/v1/albums/{secondId}/songs"));
        Assert.Equal(1,secondSongs.GetProperty("total").GetInt32());
        await client.PostAsJsonAsync("/api/v1/auth/logout",new{});
        var session=await Json(await fixture.Client().PostAsJsonAsync("/api/v1/auth/login",new{email=account.Email,password=Password}));
        using var relogged=fixture.Client(session.GetProperty("accessToken").GetString());
        var afterLogin=await Json(await relogged.GetAsync("/api/v1/library/songs"));
        Assert.Equal(1,afterLogin.GetProperty("total").GetInt32());
    }
    [SqlFact]
    public async Task Concurrent_album_edits_conflict_and_source_identity_is_case_sensitive()
    {
        var account=await fixture.RegisterAsync(); using var client=fixture.Client(account.AccessToken);
        var album=await Json(await client.PostAsJsonAsync("/api/v1/albums",new{name="Ban đầu"}));
        var id=album.GetProperty("id").GetGuid(); var version=album.GetProperty("version").GetString();
        await Expect(await Mutation(client,HttpMethod.Patch,$"/api/v1/albums/{id}",new{name="Thiết bị A"}), (HttpStatusCode)428);
        await Expect(await Mutation(client,HttpMethod.Patch,$"/api/v1/albums/{id}",new{name="Thiết bị A"},version),HttpStatusCode.OK);
        await Expect(await Mutation(client,HttpMethod.Patch,$"/api/v1/albums/{id}",new{name="Thiết bị B"},version),HttpStatusCode.Conflict);
        await Json(await client.PostAsJsonAsync("/api/v1/library/songs",new{url="https://youtu.be/AbCdEfGhI02",customTitle="Một"}));
        await Json(await client.PostAsJsonAsync("/api/v1/library/songs",new{url="https://youtu.be/abcdEfGhI02",customTitle="Hai"}));
        var songs=await Json(await client.GetAsync("/api/v1/library/songs"));
        Assert.Equal(2,songs.GetProperty("total").GetInt32());
    }
    [SqlFact]
    public async Task Refresh_rotation_detects_reuse_and_revokes_descendants()
    {
        var account=await fixture.RegisterAsync(); using var client=fixture.Client();
        var rotated=await Json(await client.PostAsJsonAsync("/api/v1/auth/refresh",new{refreshToken=account.RefreshToken}));
        var next=rotated.GetProperty("refreshToken").GetString(); Assert.NotEqual(account.RefreshToken,next);
        await Expect(await client.PostAsJsonAsync("/api/v1/auth/refresh",new{refreshToken=account.RefreshToken}),HttpStatusCode.Unauthorized);
        await Expect(await client.PostAsJsonAsync("/api/v1/auth/refresh",new{refreshToken=next}),HttpStatusCode.Unauthorized);
        using var current=fixture.Client(rotated.GetProperty("accessToken").GetString());
        await Expect(await current.GetAsync("/api/v1/users/me"),HttpStatusCode.Unauthorized);
    }
    [SqlFact]
    public async Task Password_reset_is_single_use_revokes_sessions_and_account_deletion_erases_private_data()
    {
        var account=await fixture.RegisterAsync(); using var anon=fixture.Client(); using var client=fixture.Client(account.AccessToken);
        await Expect(await anon.PostAsJsonAsync("/api/v1/auth/forgot-password",new{email=account.Email}),HttpStatusCode.Accepted);
        var code=fixture.Mail.Get(account.Email,AccountCodePurpose.ResetPassword); const string replacement="Changed-Test!2026Strong";
        await Expect(await anon.PostAsJsonAsync("/api/v1/auth/reset-password",new{email=account.Email,code,password=replacement}),HttpStatusCode.NoContent);
        await Expect(await anon.PostAsJsonAsync("/api/v1/auth/reset-password",new{email=account.Email,code,password=replacement}),HttpStatusCode.BadRequest);
        await Expect(await client.GetAsync("/api/v1/users/me"),HttpStatusCode.Unauthorized);
        var session=await Json(await anon.PostAsJsonAsync("/api/v1/auth/login",new{email=account.Email,password=replacement}));
        using var fresh=fixture.Client(session.GetProperty("accessToken").GetString());
        var album=await Json(await fresh.PostAsJsonAsync("/api/v1/albums",new{name="Dữ liệu sẽ xóa"}));
        await Json(await fresh.PostAsJsonAsync("/api/v1/library/songs",new{url="https://youtu.be/AbCdEfGhI03",customTitle="Riêng tư",albumIds=new[]{album.GetProperty("id").GetGuid()}}));
        await Expect(await Mutation(fresh,HttpMethod.Delete,"/api/v1/users/me",new{password="wrong"}),HttpStatusCode.Unauthorized);
        await Expect(await Mutation(fresh,HttpMethod.Delete,"/api/v1/users/me",new{password=replacement}),HttpStatusCode.NoContent);
        await using var scope=fixture.Factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<TMusicDb>();
        Assert.False(await db.Users.AnyAsync(x=>x.Id==account.UserId));
        Assert.False(await db.UserLibrarySongs.AnyAsync(x=>x.UserId==account.UserId));
        Assert.False(await db.Albums.AnyAsync(x=>x.OwnerId==account.UserId));
        Assert.False(await db.UserSessions.AnyAsync(x=>x.UserId==account.UserId));
    }

    [SqlFact]
    public async Task New_account_defaults_to_dark_theme_and_supports_extended_song_sorting()
    {
        var account=await fixture.RegisterAsync(); using var client=fixture.Client(account.AccessToken);
        var settings=await Json(await client.GetAsync("/api/v1/users/me/settings"));
        Assert.Equal("dark",settings.GetProperty("theme").GetString());

        await Json(await client.PostAsJsonAsync("/api/v1/library/songs",new{url="https://youtu.be/SortTest001",customTitle="Zeta",customArtist="Bravo"}));
        await Json(await client.PostAsJsonAsync("/api/v1/library/songs",new{url="https://youtu.be/SortTest002",customTitle="Alpha",customArtist="Charlie"}));

        var byName=await Json(await client.GetAsync("/api/v1/library/songs?sort=name"));
        Assert.Equal("Alpha",byName.GetProperty("items")[0].GetProperty("customTitle").GetString());

        var byNameDesc=await Json(await client.GetAsync("/api/v1/library/songs?sort=name_desc"));
        Assert.Equal("Zeta",byNameDesc.GetProperty("items")[0].GetProperty("customTitle").GetString());

        var byArtist=await Json(await client.GetAsync("/api/v1/library/songs?sort=artist"));
        Assert.Equal("Zeta",byArtist.GetProperty("items")[0].GetProperty("customTitle").GetString());

        var byOldest=await Json(await client.GetAsync("/api/v1/library/songs?sort=oldest"));
        Assert.Equal("Zeta",byOldest.GetProperty("items")[0].GetProperty("customTitle").GetString());

        var byDuration=await Json(await client.GetAsync("/api/v1/library/songs?sort=duration"));
        Assert.Equal(2,byDuration.GetProperty("total").GetInt32());
    }
}
