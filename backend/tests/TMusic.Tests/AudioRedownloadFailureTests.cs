using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TMusic.Application.Providers;
using TMusic.Infrastructure.Downloads;
using Xunit;

namespace TMusic.Tests;

[Collection("SQL API")]
public sealed class AudioRedownloadFailureTests(SqlApiFixture fixture)
{
    [SqlFact]
    public async Task FailedReplacementPreservesPreviouslyDownloadedAudioAndPlayback()
    {
        var extractor = new FirstDownloadOnlyExtractor();
        await using var factory = fixture.Factory.WithWebHostBuilder(web => web.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAudioExtractor>();
            services.AddSingleton<IAudioExtractor>(extractor);
        }));
        var account = await fixture.RegisterAsync();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        var saved = await SqlApiFixture.Json(await client.PostAsJsonAsync("/api/v1/library/songs", new
        {
            url = "https://www.youtube.com/watch?v=RetryAudio1",
            customTitle = "Bản đã tải phải được giữ lại",
            downloadMp3 = true,
            rightsConfirmed = true
        }));
        var songId = saved.GetProperty("id").GetGuid();
        await ProcessOnce();
        using var originalResponse = await client.GetAsync($"/api/v1/library/songs/{songId}/audio");
        Assert.Equal(HttpStatusCode.OK, originalResponse.StatusCode);
        var originalBytes = await originalResponse.Content.ReadAsByteArrayAsync();
        var ownerAudioDirectory = Path.Combine(fixture.AudioPath, account.UserId.ToString("N"));
        var originalFile = Assert.Single(Directory.GetFiles(ownerAudioDirectory, "*.mp3"));

        var replacement = await SqlApiFixture.Json(await client.PostAsJsonAsync($"/api/v1/library/songs/{songId}/download", new
        {
            rightsConfirmed = true,
            forceRedownload = true
        }));
        var replacementJobId = replacement.GetProperty("id").GetGuid();
        await ProcessOnce();

        var job = await SqlApiFixture.Json(await client.GetAsync($"/api/v1/downloads/{replacementJobId}"));
        Assert.Equal("FAILED", job.GetProperty("status").GetString());
        Assert.Equal("SOURCE_RESTRICTED_OR_UNSUPPORTED", job.GetProperty("errorCode").GetString());
        var song = await SqlApiFixture.Json(await client.GetAsync($"/api/v1/library/songs/{songId}"));
        Assert.Equal("READY", song.GetProperty("audioStatus").GetString());
        Assert.True(song.GetProperty("audioReady").GetBoolean());
        Assert.True(job.GetProperty("audioReady").GetBoolean());
        using var afterFailure = await client.GetAsync($"/api/v1/library/songs/{songId}/audio");
        Assert.Equal(HttpStatusCode.OK, afterFailure.StatusCode);
        Assert.Equal(originalBytes, await afterFailure.Content.ReadAsByteArrayAsync());
        Assert.Equal(originalFile, Assert.Single(Directory.GetFiles(ownerAudioDirectory, "*.mp3")));

        async Task ProcessOnce()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            Assert.True(await scope.ServiceProvider.GetRequiredService<AudioDownloadProcessor>()
                .ProcessOneAsync(CancellationToken.None));
        }
    }

    // Test-only storage fixture; this does not stand in for a live provider/decoder check.
    private sealed class FirstDownloadOnlyExtractor : IAudioExtractor
    {
        private int calls;
        public bool IsAvailable => true;
        public async Task<string> ExtractMp3Async(string canonicalUrl, string workDir,
            Func<int, CancellationToken, Task> onProgress, CancellationToken ct)
        {
            await onProgress(50, ct);
            if (Interlocked.Increment(ref calls) > 1)
                throw new ProviderException("SOURCE_RESTRICTED_OR_UNSUPPORTED");
            var payload = new byte[2048];
            payload[0] = (byte)'I'; payload[1] = (byte)'D'; payload[2] = (byte)'3';
            var path = Path.Combine(workDir, "track.mp3");
            await File.WriteAllBytesAsync(path, payload, ct);
            return path;
        }
    }
}
