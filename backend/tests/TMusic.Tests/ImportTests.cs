using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TMusic.Application.Providers;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Imports;
using TMusic.Infrastructure.Persistence;
using TMusic.Infrastructure.Providers;
using Xunit;
using static TMusic.Tests.SqlApiFixture;

namespace TMusic.Tests;

[Collection("SQL API")]
public sealed class ImportTests(SqlApiFixture fixture)
{
    [SqlFact]
    public async Task CheckpointsRestartCancelRetryAndIdempotencyPreserveOrderedMembership()
    {
        var account = await fixture.RegisterAsync();
        using var client = fixture.Client(account.AccessToken);
        var create = await client.PostAsJsonAsync("/api/v1/imports/previews", new { url = "https://www.youtube.com/playlist?list=PLfixture_001" });
        await Expect(create, HttpStatusCode.Accepted);
        var previewId = (await Json(create)).GetProperty("previewId").GetGuid();
        var provider = new PlaylistFixture(125);
        await Step(provider);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TMusicDb>();
            var preview = await db.ImportPreviews.SingleAsync(x => x.Id == previewId);
            Assert.Equal("50", preview.NextPageToken);
            preview.Status = ImportStatus.Running; preview.LeaseOwner = Guid.NewGuid();
            preview.LeaseUntil = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        await Step(provider); await Step(provider);
        Assert.Equal(new string?[] { null, "50", "100" }, provider.Cursors);
        var previewResult = await Json(await client.GetAsync($"/api/v1/imports/previews/{previewId}"));
        Assert.Equal("READY", previewResult.GetProperty("status").GetString());
        Assert.Equal(123, previewResult.GetProperty("accepted").GetInt32());
        Assert.Equal(1, previewResult.GetProperty("duplicates").GetInt32());
        Assert.Equal(1, previewResult.GetProperty("unavailable").GetInt32());
        var payload = new { previewId, albumName = "SQL import fixture" };
        var key = Guid.NewGuid().ToString("N");
        var response = await Submit(client, payload, key); await Expect(response, HttpStatusCode.Accepted);
        var jobId = (await Json(response)).GetProperty("jobId").GetGuid();
        var replay = await Json(await Submit(client, payload, key));
        Assert.Equal(jobId, replay.GetProperty("jobId").GetGuid());
        await Expect(await Submit(client, new { previewId, albumName = "Different target" }, key), HttpStatusCode.Conflict);
        await Step(provider);
        await Expect(await client.PostAsync($"/api/v1/imports/{jobId}/cancel", null), HttpStatusCode.NoContent);
        var canceled = await Json(await client.GetAsync($"/api/v1/imports/{jobId}"));
        Assert.Equal("CANCELED", canceled.GetProperty("status").GetString());
        Assert.Equal(100, canceled.GetProperty("imported").GetInt32());
        await Expect(await client.PostAsync($"/api/v1/imports/{jobId}/retry", null), HttpStatusCode.Accepted);
        await Step(provider);
        var complete = await Json(await client.GetAsync($"/api/v1/imports/{jobId}"));
        Assert.Equal("COMPLETE", complete.GetProperty("status").GetString());
        Assert.Equal(123, complete.GetProperty("imported").GetInt32());
        var albumId = complete.GetProperty("albumId").GetGuid();
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TMusicDb>();
            var ids = await db.AlbumSongs.Where(x => x.AlbumId == albumId).OrderBy(x => x.Position)
                .Select(x => x.UserLibrarySong.Song.ProviderContentId).ToArrayAsync();
            Assert.Equal(Enumerable.Range(0, 125).Where(x => x is not (50 or 51)).Select(ProviderTests.Id), ids);
            Assert.Equal(123, await db.UserLibrarySongs.CountAsync(x => x.UserId == account.UserId));
            Assert.All(await db.UserLibrarySongs.Where(x => x.UserId == account.UserId).Select(x => x.CustomTitle).ToArrayAsync(), Assert.Empty);
        }
        var append = await Json(await Submit(client, new { previewId, albumId }, Guid.NewGuid().ToString("N")));
        var secondJob = append.GetProperty("jobId").GetGuid();
        await Step(provider); await Step(provider);
        var reimport = await Json(await client.GetAsync($"/api/v1/imports/{secondJob}"));
        Assert.Equal(0, reimport.GetProperty("imported").GetInt32());
        Assert.Equal(123, reimport.GetProperty("alreadyPresent").GetInt32());
        var outsider = await fixture.RegisterAsync(); using var other = fixture.Client(outsider.AccessToken);
        await Expect(await other.GetAsync($"/api/v1/imports/{jobId}"), HttpStatusCode.NotFound);
        await Expect(await other.GetAsync($"/api/v1/imports/previews/{previewId}"), HttpStatusCode.NotFound);
        await Expect(await other.PostAsync($"/api/v1/imports/{jobId}/cancel", null), HttpStatusCode.NotFound);
    }

    [SqlFact]
    public async Task ExpiryPurgesProviderMetadataButPreservesUserBookmarksAndNames()
    {
        var account = await fixture.RegisterAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TMusicDb>();
        var source = new Song { Provider = "tiktok", ProviderContentId = DateTime.UtcNow.Ticks.ToString(),
            CanonicalUrl = "https://www.tiktok.com/@fixture/video/123456789", OriginalUrl = "https://www.tiktok.com/@fixture/video/123456789",
            OriginalTitle = "API data expires", OriginalCreator = "API creator", MetadataFetchedAt = DateTimeOffset.UtcNow.AddDays(-30),
            MetadataExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1), PlaybackCapability = "EMBED_SUPPORTED" };
        var library = new UserLibrarySong { UserId = account.UserId, Song = source, SongId = source.Id,
            CustomTitle = "Tên người dùng giữ lại", CustomArtist = "Ghi chú riêng", IsFavorite = true };
        var album = new Album { OwnerId = account.UserId, Name = "User collection" };
        db.AlbumSongs.Add(new AlbumSong { UserId = account.UserId, Album = album, AlbumId = album.Id,
            UserLibrarySong = library, UserLibrarySongId = library.Id });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var processor = new ImportProcessor(db, new PlaylistFixture(0), ProviderTests.Config(), TimeProvider.System);
        await processor.PurgeExpiredAsync(CancellationToken.None);
        var saved = await db.UserLibrarySongs.Include(x => x.Song).SingleAsync(x => x.Id == library.Id);
        Assert.Equal("Tên người dùng giữ lại", saved.CustomTitle); Assert.Equal("Ghi chú riêng", saved.CustomArtist);
        Assert.True(saved.IsFavorite); Assert.Null(saved.Song.OriginalTitle); Assert.Null(saved.Song.MetadataFetchedAt);
        Assert.Equal("EXTERNAL_OPEN_ONLY", saved.Song.PlaybackCapability);
        Assert.True(await db.AlbumSongs.AnyAsync(x => x.UserLibrarySongId == library.Id));
    }

    [SqlFact]
    public async Task ImmutablePreviewTamperingFailsBeforeAnyLibraryWrite()
    {
        var account = await fixture.RegisterAsync(); using var client = fixture.Client(account.AccessToken);
        var response = await Json(await client.PostAsJsonAsync("/api/v1/imports/previews", new { url = "https://www.youtube.com/playlist?list=PLfixture_002" }));
        var previewId = response.GetProperty("previewId").GetGuid();
        await Step(new PlaylistFixture(1));
        var jobId = (await Json(await Submit(client, new { previewId, albumName = "Integrity test" }, Guid.NewGuid().ToString("N"))))
            .GetProperty("jobId").GetGuid();
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TMusicDb>();
            await db.ImportCandidates.Where(x => x.PreviewId == previewId).ExecuteUpdateAsync(set => set.SetProperty(x => x.Title, "Tampered"));
        }
        await Step(new PlaylistFixture(1));
        var status = await Json(await client.GetAsync($"/api/v1/imports/{jobId}"));
        Assert.Equal("FAILED", status.GetProperty("status").GetString());
        Assert.Equal("PREVIEW_INTEGRITY_FAILED", status.GetProperty("errorCode").GetString());
        await using var check = fixture.Factory.Services.CreateAsyncScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<TMusicDb>().UserLibrarySongs.AnyAsync(x => x.UserId == account.UserId));
    }

    [SqlFact]
    public async Task SqlQuotaBudgetCannotBeExceededByAnotherReservation()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TMusicDb>();
        await db.ProviderQuotaWindows.ExecuteDeleteAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["YouTube:DailyQuotaBudget"] = "1" }).Build();
        var quota = new ProviderQuota(db, config, TimeProvider.System);
        await quota.ReserveAsync(CancellationToken.None);
        var error = await Assert.ThrowsAsync<ProviderException>(() => quota.ReserveAsync(CancellationToken.None));
        Assert.Equal("YOUTUBE_QUOTA_EXCEEDED", error.Code);
        Assert.Equal(1, await db.ProviderQuotaWindows.Select(x => x.Used).SingleAsync());
    }

    [SqlFact]
    public async Task CancellationDuringProviderFetchFencesStaleWorkerCommit()
    {
        var account = await fixture.RegisterAsync(); using var client = fixture.Client(account.AccessToken);
        var created = await Json(await client.PostAsJsonAsync("/api/v1/imports/previews",
            new { url = "https://www.youtube.com/playlist?list=PLfixture_003" }));
        var previewId = created.GetProperty("previewId").GetGuid();
        var provider = new CallbackPlaylist(async () =>
            await Expect(await client.PostAsync($"/api/v1/imports/previews/{previewId}/cancel", null), HttpStatusCode.NoContent));
        await Step(provider);
        var status = await Json(await client.GetAsync($"/api/v1/imports/previews/{previewId}"));
        Assert.Equal("CANCELED", status.GetProperty("status").GetString());
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<TMusicDb>().ImportCandidates.AnyAsync(x => x.PreviewId == previewId));
    }
    private async Task Step(IYouTubePlaylist provider)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var processor = new ImportProcessor(scope.ServiceProvider.GetRequiredService<TMusicDb>(), provider,
            scope.ServiceProvider.GetRequiredService<IConfiguration>(), TimeProvider.System);
        Assert.True(await processor.ProcessOneAsync(CancellationToken.None));
    }

    private static Task<HttpResponseMessage> Submit(HttpClient client, object body, string key)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/imports") { Content = JsonContent.Create(body) };
        message.Headers.Add("Idempotency-Key", key); return client.SendAsync(message);
    }

    private sealed class CallbackPlaylist(Func<Task> callback) : IYouTubePlaylist
    {
        public bool IsConfigured => true;
        public async Task<PlaylistPage> ReadPageAsync(string playlistId, string? cursor, CancellationToken cancellationToken)
        {
            await callback();
            return new PlaylistPage([ProviderTests.Video(0)], null);
        }
    }
    private sealed class PlaylistFixture(int count) : IYouTubePlaylist
    {
        public bool IsConfigured => true;
        public List<string?> Cursors { get; } = [];
        public Task<PlaylistPage> ReadPageAsync(string playlistId, string? cursor, CancellationToken cancellationToken)
        {
            Cursors.Add(cursor); var start = int.Parse(cursor ?? "0"); var end = Math.Min(start + 50, count);
            return Task.FromResult(new PlaylistPage(Enumerable.Range(start, end - start)
                .Select(i => ProviderTests.Video(i, ProviderTests.Id(i == 50 ? 0 : i)) with { Available = i != 51 }).ToArray(),
                end < count ? end.ToString() : null));
        }
    }
}


