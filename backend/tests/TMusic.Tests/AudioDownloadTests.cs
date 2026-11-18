using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TMusic.Application.Providers;
using TMusic.Infrastructure.Downloads;
using TMusic.Infrastructure.Storage;
using Xunit;

namespace TMusic.Tests;

[Collection("SQL API")]
public sealed class AudioDownloadTests(SqlApiFixture fixture)
{
    [Fact]
    public void ExtractorArgumentsRejectMissingControlledEgress()
    {
        Assert.Throws<ProviderException>(() => YtDlpAudioExtractor.BuildSafeArguments(
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ", "ffmpeg", "track.%(ext)s"));
    }

    [Fact]
    public void SafeYtDlpArgumentsUseNativeDownloadOnlyAndDoubleDashSentinel()
    {
        var args = YtDlpAudioExtractor.BuildSafeArguments(
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            @"C:\tools\ffmpeg\bin\ffmpeg.exe",
            @"C:\temp\track.%(ext)s", "http://tmusic:test@127.0.0.1:18234");

        Assert.Contains("--no-playlist", args);
        Assert.Contains("--no-cookies", args);
        Assert.Contains("--no-config", args);
        Assert.Contains("--no-plugin-dirs", args);
        Assert.Contains("--proxy", args);
        Assert.Contains("native", args);
        Assert.DoesNotContain("-x", args);
        Assert.DoesNotContain("--audio-format", args);
        Assert.DoesNotContain("--postprocessor-args", args);
        Assert.Contains("bestaudio/best", args);
        Assert.Contains("--fixup", args);
        Assert.Contains("never", args);

        var sentinelIndex = args.ToList().IndexOf("--");
        Assert.True(sentinelIndex > 0);
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", args[sentinelIndex + 1]);

        Assert.Throws<ProviderException>(() => YtDlpAudioExtractor.BuildSafeArguments(
            "https://evil.example/watch?v=dQw4w9WgXcQ",
            @"C:\tools\ffmpeg\bin\ffmpeg.exe",
            @"C:\temp\track.%(ext)s"));
    }

    [Fact]
    public void AudioStorageValidatesMp3HeadersAndRejectsArbitraryPayloads()
    {
        var config = new ConfigurationBuilder().Build();
        var storage = new AudioStorage(config);

        ReadOnlySpan<byte> validId3 = [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00];
        ReadOnlySpan<byte> validMpegFrame = [0xFF, 0xFB, 0x90, 0x64, 0x00, 0x00, 0x00, 0x00];
        ReadOnlySpan<byte> htmlPayload = "<!DOCTYPE html>"u8;

        Assert.True(storage.IsValidMp3Header(validId3));
        Assert.True(storage.IsValidMp3Header(validMpegFrame));
        Assert.False(storage.IsValidMp3Header(htmlPayload));
    }

    [SqlFact]
    public async Task EndToEndAudioDownloadCreatesPrivateAlbumMp3SupportsRangeAndBlocksIdor()
    {
        await using var customFactory = fixture.Factory.WithWebHostBuilder(web =>
        {
            web.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:EnableAudioDownloads"] = "true"
            }));
            web.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAudioExtractor>();
                services.AddSingleton<IAudioExtractor, FixtureMp3Extractor>();
            });
        });

        var alice = await fixture.RegisterAsync();
        var bob = await fixture.RegisterAsync();

        using var aliceClient = customFactory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        aliceClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", alice.AccessToken);

        using var bobClient = customFactory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        bobClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bob.AccessToken);

        var caps = await SqlApiFixture.Json(await aliceClient.GetAsync("/api/v1/system/capabilities"));
        Assert.True(caps.GetProperty("licensedAudioDownload").GetBoolean());

        // Create private album
        using var createAlbumReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/albums")
        {
            Content = JsonContent.Create(new { name = "Album MP3 Riêng", description = "Nhạc offline cá nhân" })
        };
        createAlbumReq.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var album = await SqlApiFixture.Json(await aliceClient.SendAsync(createAlbumReq));
        var albumId = album.GetProperty("id").GetGuid();

        // Missing rightsConfirmed must be rejected with 400
        await SqlApiFixture.Expect(
            await aliceClient.PostAsJsonAsync("/api/v1/library/songs", new
            {
                url = "https://www.youtube.com/watch?v=Mp3TestVid1",
                customTitle = "Bản MP3 Riêng Của Tôi",
                customArtist = "Nghệ Sĩ Độc Lập",
                albumIds = new[] { albumId },
                downloadMp3 = true,
                rightsConfirmed = false
            }),
            HttpStatusCode.BadRequest);

        // Save song with downloadMp3=true & rightsConfirmed=true
        var saved = await SqlApiFixture.Json(await aliceClient.PostAsJsonAsync("/api/v1/library/songs", new
        {
            url = "https://www.youtube.com/watch?v=Mp3TestVid1",
            customTitle = "Bản MP3 Riêng Của Tôi",
            customArtist = "Nghệ Sĩ Độc Lập",
            albumIds = new[] { albumId },
            downloadMp3 = true,
            rightsConfirmed = true
        }));

        var songId = saved.GetProperty("id").GetGuid();
        var jobId = saved.GetProperty("downloadJobId").GetGuid();
        Assert.Equal("QUEUED", saved.GetProperty("audioStatus").GetString());

        // Run worker processor once
        await using (var scope = customFactory.Services.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<AudioDownloadProcessor>();
            Assert.True(await processor.ProcessOneAsync(CancellationToken.None));
        }

        // Verify job status & song audioReady
        var job = await SqlApiFixture.Json(await aliceClient.GetAsync($"/api/v1/downloads/{jobId}"));
        Assert.Equal("COMPLETE", job.GetProperty("status").GetString());
        Assert.Equal(100, job.GetProperty("progressPercent").GetInt32());
        Assert.True(job.GetProperty("audioReady").GetBoolean());

        var albumSongs = await SqlApiFixture.Json(await aliceClient.GetAsync($"/api/v1/albums/{albumId}/songs"));
        var firstSong = albumSongs.GetProperty("items")[0];
        Assert.True(firstSong.GetProperty("audioReady").GetBoolean());
        Assert.Equal($"/api/v1/library/songs/{songId}/audio", firstSong.GetProperty("audioStreamUrl").GetString());

        // Verify HTTP Range streaming for owner
        using var rangeReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/library/songs/{songId}/audio");
        rangeReq.Headers.Range = new RangeHeaderValue(0, 15);
        var rangeResp = await aliceClient.SendAsync(rangeReq);
        Assert.Equal(HttpStatusCode.PartialContent, rangeResp.StatusCode);
        Assert.Equal("audio/mpeg", rangeResp.Content.Headers.ContentType?.MediaType);
        var rangeBytes = await rangeResp.Content.ReadAsByteArrayAsync();
        Assert.Equal(16, rangeBytes.Length);
        Assert.Equal((byte)'I', rangeBytes[0]);
        Assert.Equal((byte)'D', rangeBytes[1]);
        Assert.Equal((byte)'3', rangeBytes[2]);

        // Anti-IDOR: Bob cannot stream or delete Alice's MP3
        await SqlApiFixture.Expect(await bobClient.GetAsync($"/api/v1/library/songs/{songId}/audio"), HttpStatusCode.NotFound);
        await SqlApiFixture.Expect(await bobClient.DeleteAsync($"/api/v1/library/songs/{songId}/audio"), HttpStatusCode.NotFound);

        // Removing MP3 preserves Alice's bookmark & custom title
        await SqlApiFixture.Expect(await aliceClient.DeleteAsync($"/api/v1/library/songs/{songId}/audio"), HttpStatusCode.NoContent);
        var afterRemove = await SqlApiFixture.Json(await aliceClient.GetAsync($"/api/v1/library/songs/{songId}"));
        Assert.False(afterRemove.GetProperty("audioReady").GetBoolean());
        Assert.Equal("Bản MP3 Riêng Của Tôi", afterRemove.GetProperty("customTitle").GetString());
    }

    private sealed class FixtureMp3Extractor : IAudioExtractor
    {
        public bool IsAvailable => true;

        public async Task<string> ExtractMp3Async(
            string canonicalUrl,
            string workDir,
            Func<int, CancellationToken, Task> onProgress,
            CancellationToken ct)
        {
            await onProgress(50, ct);
            var outPath = Path.Combine(workDir, "track.mp3");
            var payload = new byte[2048];
            payload[0] = (byte)'I';
            payload[1] = (byte)'D';
            payload[2] = (byte)'3';
            payload[3] = 0x04;
            payload[10] = 0xFF;
            payload[11] = 0xFB;
            await File.WriteAllBytesAsync(outPath, payload, ct);
            return outPath;
        }
    }
}
