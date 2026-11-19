using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Downloads;
using TMusic.Infrastructure.Persistence;
using TMusic.Infrastructure.Storage;
using Xunit;

namespace TMusic.Tests;

[Collection("SQL API")]
public sealed class AudioDownloadCancellationTests(SqlApiFixture fixture)
{
    [SqlFact]
    public Task CancelRunningDownloadPreventsPublishingAudio() => CancelDuringExtraction(removeAudio: false);

    [SqlFact]
    public Task RemoveAudioDuringRunningDownloadPreventsReattachment() => CancelDuringExtraction(removeAudio: true);

    private async Task CancelDuringExtraction(bool removeAudio)
    {
        var extractor = new PausedExtractor();
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
            url = "https://www.youtube.com/watch?v=CancelTest1",
            customTitle = "Bài đang tải",
            downloadMp3 = true,
            rightsConfirmed = true
        }));
        var songId = saved.GetProperty("id").GetGuid();
        var jobId = saved.GetProperty("downloadJobId").GetGuid();
        await using var workerScope = factory.Services.CreateAsyncScope();
        var processing = workerScope.ServiceProvider.GetRequiredService<AudioDownloadProcessor>().ProcessOneAsync(CancellationToken.None);
        await extractor.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            await SqlApiFixture.Expect(await client.DeleteAsync(removeAudio
                ? $"/api/v1/library/songs/{songId}/audio"
                : $"/api/v1/downloads/{jobId}"), HttpStatusCode.NoContent);
        }
        finally { extractor.Continue.TrySetResult(); }
        Assert.True(await processing.WaitAsync(TimeSpan.FromSeconds(15)));

        var job = await SqlApiFixture.Json(await client.GetAsync($"/api/v1/downloads/{jobId}"));
        Assert.Equal("CANCELED", job.GetProperty("status").GetString());
        var song = await SqlApiFixture.Json(await client.GetAsync($"/api/v1/library/songs/{songId}"));
        Assert.False(song.GetProperty("audioReady").GetBoolean());
        Assert.Equal("NONE", song.GetProperty("audioStatus").GetString());
        await SqlApiFixture.Expect(await client.GetAsync($"/api/v1/library/songs/{songId}/audio"), HttpStatusCode.NotFound);
        Assert.Empty(Directory.Exists(Path.Combine(fixture.AudioPath, account.UserId.ToString("N")))
            ? Directory.GetFiles(Path.Combine(fixture.AudioPath, account.UserId.ToString("N")), "*.mp3") : []);
    }

    [SqlFact]
    public async Task WorkerWithRevokedLeaseDoesNotPublishAfterFileStorage()
    {
        var storage = new PausedStorage(new AudioStorage(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:AudioPath"] = fixture.AudioPath }).Build()));
        await using var factory = fixture.Factory.WithWebHostBuilder(web => web.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAudioExtractor>();
            services.AddSingleton<IAudioExtractor>(new PausedExtractor(paused: false));
            services.RemoveAll<IAudioStorage>();
            services.AddSingleton<IAudioStorage>(storage);
        }));
        var account = await fixture.RegisterAsync();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        var saved = await SqlApiFixture.Json(await client.PostAsJsonAsync("/api/v1/library/songs", new
        {
            url = "https://www.youtube.com/watch?v=CancelTest2",
            customTitle = "Không công bố lease cũ",
            downloadMp3 = true,
            rightsConfirmed = true
        }));
        var songId = saved.GetProperty("id").GetGuid();
        var jobId = saved.GetProperty("downloadJobId").GetGuid();
        await using var workerScope = factory.Services.CreateAsyncScope();
        var processing = workerScope.ServiceProvider.GetRequiredService<AudioDownloadProcessor>().ProcessOneAsync(CancellationToken.None);
        await storage.Stored.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            await using var cancellationScope = factory.Services.CreateAsyncScope();
            var db = cancellationScope.ServiceProvider.GetRequiredService<TMusicDb>();
            await db.AudioDownloadJobs.Where(x => x.Id == jobId).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, ImportStatus.Canceled)
                .SetProperty(x => x.LeaseOwner, (Guid?)null));
        }
        finally { storage.Continue.TrySetResult(); }
        Assert.True(await processing.WaitAsync(TimeSpan.FromSeconds(15)));
        var song = await SqlApiFixture.Json(await client.GetAsync($"/api/v1/library/songs/{songId}"));
        Assert.False(song.GetProperty("audioReady").GetBoolean());
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.AudioPath, account.UserId.ToString("N")), "*.mp3"));
    }

    private sealed class PausedExtractor(bool paused = true) : IAudioExtractor
    {
        public bool IsAvailable => true;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string> ExtractMp3Async(string canonicalUrl, string workDir, Func<int, CancellationToken, Task> onProgress, CancellationToken ct)
        {
            Started.TrySetResult();
            if (paused) await Continue.Task.WaitAsync(ct);
            var bytes = new byte[2048];
            bytes[0] = (byte)'I'; bytes[1] = (byte)'D'; bytes[2] = (byte)'3';
            var path = Path.Combine(workDir, "track.mp3");
            await File.WriteAllBytesAsync(path, bytes, ct);
            return path;
        }
    }

    private sealed class PausedStorage(IAudioStorage inner) : IAudioStorage
    {
        public TaskCompletionSource Stored { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsValidMp3Header(ReadOnlySpan<byte> header) => inner.IsValidMp3Header(header);
        public async Task<StoredAudio> SaveFromTempFileAsync(Guid userId, string tempFilePath, CancellationToken ct)
        {
            var result = await inner.SaveFromTempFileAsync(userId, tempFilePath, ct);
            Stored.TrySetResult();
            await Continue.Task.WaitAsync(ct);
            return result;
        }
        public Task<StoredAudio> SaveBytesAsync(Guid userId, ReadOnlyMemory<byte> bytes, CancellationToken ct) => inner.SaveBytesAsync(userId, bytes, ct);
        public Task<(Stream Stream, long Length)?> OpenReadAsync(Guid userId, string fileName, CancellationToken ct) => inner.OpenReadAsync(userId, fileName, ct);
        public string StorageRootPath => inner.StorageRootPath;
        public void DeleteAudioFile(Guid userId, string? fileName) => inner.DeleteAudioFile(userId, fileName);
        public void DeleteUserAudio(Guid userId) => inner.DeleteUserAudio(userId);
        public IReadOnlyList<StoredFileEntry> ListAllFiles() => inner.ListAllFiles();
        public bool QuarantineFile(Guid userId, string fileName) => inner.QuarantineFile(userId, fileName);
    }
}
