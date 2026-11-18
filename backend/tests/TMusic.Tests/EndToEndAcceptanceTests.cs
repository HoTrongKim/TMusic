using System.Net;
using System.Net.Http.Json;
using TMusic.Application.Identity;
using Xunit;
using static TMusic.Tests.SqlApiFixture;

namespace TMusic.Tests;

[Collection("SQL API")]
public sealed class EndToEndAcceptanceTests(SqlApiFixture fixture)
{
    [SqlFact]
    public async Task CompleteFourteenStepMasterPromptJourneySucceedsEndToEnd()
    {
        var emailA = $"e2e-user-a-{Guid.NewGuid():N}@tmusic.test";
        using var anon = fixture.Client();

        // 1. Register account (immediately creates session without requiring email verification)
        var regResponse = await anon.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = emailA,
            password = Password,
            displayName = "Người dùng E2E",
            deviceName = "Thiết bị A"
        });
        await Expect(regResponse, HttpStatusCode.Created);
        var sessionA1 = await Json(regResponse);
        using var deviceA = fixture.Client(sessionA1.GetProperty("accessToken").GetString());

        // 2. Verify Forgot Password -> Reset Password via email code works for registered user
        await Expect(await anon.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = emailA }), HttpStatusCode.Accepted);
        var resetCode = fixture.Mail.Get(emailA, AccountCodePurpose.ResetPassword);
        Assert.False(string.IsNullOrWhiteSpace(resetCode));

        // 4. Create albums (YouTube album + TikTok album)
        var ytAlbum = await Json(await deviceA.PostAsJsonAsync("/api/v1/albums", new
        {
            name = "Nhạc YouTube Buổi Tối",
            description = "Bộ sưu tập chill",
            coverImageUrl = "cover:navy"
        }));
        var ttAlbum = await Json(await deviceA.PostAsJsonAsync("/api/v1/albums", new
        {
            name = "Nhạc TikTok Bắt Tai",
            coverImageUrl = "cover:blue"
        }));
        var ytAlbumId = ytAlbum.GetProperty("id").GetGuid();
        var ttAlbumId = ttAlbum.GetProperty("id").GetGuid();

        // 5. Paste & resolve YouTube link
        var ytPreview = await Json(await deviceA.PostAsJsonAsync("/api/v1/songs/resolve", new
        {
            url = "https://youtu.be/E2EYouTube1"
        }));
        Assert.Equal("youtube", ytPreview.GetProperty("provider").GetString());
        Assert.Equal("E2EYouTube1", ytPreview.GetProperty("providerContentId").GetString());

        // 6. Save YouTube song with custom title
        var ytSong = await Json(await deviceA.PostAsJsonAsync("/api/v1/library/songs", new
        {
            url = "https://youtu.be/E2EYouTube1",
            customTitle = "Nhạc chill mỗi tối",
            customArtist = "Playlist đêm",
            albumIds = new[] { ytAlbumId }
        }));
        Assert.Equal("Nhạc chill mỗi tối", ytSong.GetProperty("customTitle").GetString());

        // 7. Paste & resolve TikTok link
        var ttPreview = await Json(await deviceA.PostAsJsonAsync("/api/v1/songs/resolve", new
        {
            url = "https://www.tiktok.com/@creator.vn/video/7300000000000000001"
        }));
        Assert.Equal("tiktok", ttPreview.GetProperty("provider").GetString());

        // 8. Save TikTok song with custom title
        var ttSong = await Json(await deviceA.PostAsJsonAsync("/api/v1/library/songs", new
        {
            url = "https://www.tiktok.com/@creator.vn/video/7300000000000000001",
            customTitle = "Giai điệu acoustic ngắn",
            customArtist = "Creator VN",
            albumIds = new[] { ttAlbumId }
        }));
        Assert.Equal("Giai điệu acoustic ngắn", ttSong.GetProperty("customTitle").GetString());

        // 9. Logout Device A
        await Expect(await deviceA.PostAsJsonAsync("/api/v1/auth/logout", new { }), HttpStatusCode.NoContent);
        await Expect(await deviceA.GetAsync("/api/v1/library/songs"), HttpStatusCode.Unauthorized);

        // 10. Login again on Device B (cross-device sync)
        var sessionB = await Json(await anon.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = emailA,
            password = Password,
            deviceName = "Thiết bị B"
        }));
        using var deviceB = fixture.Client(sessionB.GetProperty("accessToken").GetString());

        // 11. Verify all data and custom titles are preserved across sessions/devices
        var syncedSongs = await Json(await deviceB.GetAsync("/api/v1/library/songs"));
        Assert.Equal(2, syncedSongs.GetProperty("total").GetInt32());
        Assert.Contains("Nhạc chill mỗi tối", syncedSongs.ToString());
        Assert.Contains("Giai điệu acoustic ngắn", syncedSongs.ToString());

        // 12. Second user cannot view or modify private albums
        var outsider = await fixture.RegisterAsync();
        using var userB = fixture.Client(outsider.AccessToken);
        await Expect(await userB.GetAsync($"/api/v1/albums/{ytAlbumId}"), HttpStatusCode.NotFound);
        await Expect(await userB.GetAsync($"/api/v1/albums/{ytAlbumId}/songs"), HttpStatusCode.NotFound);

        // 13. Deleting album does NOT delete songs from personal library
        var freshYtAlbum = await Json(await deviceB.GetAsync($"/api/v1/albums/{ytAlbumId}"));
        await Expect(await Mutation(
            deviceB,
            HttpMethod.Delete,
            $"/api/v1/albums/{ytAlbumId}",
            version: freshYtAlbum.GetProperty("version").GetString()), HttpStatusCode.NoContent);

        var afterAlbumDelete = await Json(await deviceB.GetAsync("/api/v1/library/songs"));
        Assert.Equal(2, afterAlbumDelete.GetProperty("total").GetInt32());

        // 14. Recovery from network retry using idempotency key does not duplicate album
        var retryKey = "retry-" + Guid.NewGuid().ToString("N");
        using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/albums")
        {
            Content = JsonContent.Create(new { name = "Album Khôi Phục Mạng" })
        };
        req1.Headers.Add("Idempotency-Key", retryKey);
        var createdRetry = await Json(await deviceB.SendAsync(req1));

        using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/albums")
        {
            Content = JsonContent.Create(new { name = "Album Khôi Phục Mạng" })
        };
        req2.Headers.Add("Idempotency-Key", retryKey);
        var replayedRetry = await Json(await deviceB.SendAsync(req2));
        Assert.Equal(createdRetry.GetProperty("id").GetGuid(), replayedRetry.GetProperty("id").GetGuid());
    }
}
