using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TMusic.Application.Providers;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Downloads;
using TMusic.Infrastructure.Persistence;
using Xunit;

namespace TMusic.Tests;

[Collection("SQL API")]
public sealed class AudioFailureCancellationRaceTests(SqlApiFixture fixture)
{
    [SqlFact]
    public Task CancelAfterFailureReadKeepsCanceledSongState() => CancelAfterFailureRead(removeAudio: false);

    [SqlFact]
    public Task RemoveAudioAfterFailureReadKeepsCanceledSongState() => CancelAfterFailureRead(removeAudio: true);

    private async Task CancelAfterFailureRead(bool removeAudio)
    {
        var barrier = new FailureReadBarrier();
        await using var factory = fixture.Factory.WithWebHostBuilder(web => web.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAudioExtractor>();
            services.AddSingleton<IAudioExtractor>(new FailingExtractor());
            services.RemoveAll<TMusicDb>();
            services.RemoveAll<DbContextOptions<TMusicDb>>();
            services.AddDbContext<TMusicDb>(options => options.UseSqlServer(fixture.ConnectionString)
                .AddInterceptors(barrier));
        }));
        var account = await fixture.RegisterAsync();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        var saved = await SqlApiFixture.Json(await client.PostAsJsonAsync("/api/v1/library/songs", new
        {
            url = "https://www.youtube.com/watch?v=FailCancel1",
            customTitle = "Hủy trong lúc báo lỗi",
            downloadMp3 = true,
            rightsConfirmed = true
        }));
        var songId = saved.GetProperty("id").GetGuid();
        var jobId = saved.GetProperty("downloadJobId").GetGuid();
        var processing = Task.Run(async () =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<AudioDownloadProcessor>()
                .ProcessOneAsync(CancellationToken.None);
        });
        await barrier.Read.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            using var canceled = await client.DeleteAsync(removeAudio
                ? $"/api/v1/library/songs/{songId}/audio"
                : $"/api/v1/downloads/{jobId}").WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(HttpStatusCode.NoContent, canceled.StatusCode);
        }
        finally { barrier.Continue.TrySetResult(); }
        Assert.True(await processing.WaitAsync(TimeSpan.FromSeconds(15)));

        var job = await SqlApiFixture.Json(await client.GetAsync($"/api/v1/downloads/{jobId}"));
        Assert.Equal("CANCELED", job.GetProperty("status").GetString());
        var song = await SqlApiFixture.Json(await client.GetAsync($"/api/v1/library/songs/{songId}"));
        Assert.Equal("NONE", song.GetProperty("audioStatus").GetString());
        Assert.False(song.GetProperty("audioReady").GetBoolean());
    }

    private sealed class FailingExtractor : IAudioExtractor
    {
        public bool IsAvailable => true;
        public Task<string> ExtractMp3Async(string canonicalUrl, string workDir,
            Func<int, CancellationToken, Task> onProgress, CancellationToken ct) =>
            Task.FromException<string>(new ProviderException("SOURCE_RESTRICTED_OR_UNSUPPORTED"));
    }

    private sealed class FailureReadBarrier : IMaterializationInterceptor
    {
        private int paused;
        public TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
        {
            // Claim reads QUEUED; the failure path's fresh read sees RUNNING and its lease.
            if (entity is AudioDownloadJob { Status: ImportStatus.Running } &&
                Interlocked.Exchange(ref paused, 1) == 0)
            {
                Read.TrySetResult();
                Continue.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
            }
            return entity;
        }
    }
}
