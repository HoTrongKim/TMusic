using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TMusic.Application.Providers;
using TMusic.Infrastructure.Providers;
using Xunit;

namespace TMusic.Tests;

public sealed class ProviderTests
{
    [Theory]
    [InlineData(" https://www.youtube.com/watch?v=AbCdEfGh123&si=tracking ", "youtube", "AbCdEfGh123")]
    [InlineData("https://youtu.be/AbCdEfGh123", "youtube", "AbCdEfGh123")]
    [InlineData("https://m.youtube.com/shorts/AbCdEfGh123", "youtube", "AbCdEfGh123")]
    [InlineData("https://music.youtube.com/watch?v=AbCdEfGh123", "youtube", "AbCdEfGh123")]
    [InlineData("https://www.tiktok.com/@artist.name/video/1234567890123456789?share=1", "tiktok", "1234567890123456789")]
    [InlineData("https://soundcloud.com/Indie-Artist/Night-Drive-Mix?si=tracking", "soundcloud", "indie-artist/night-drive-mix")]
    [InlineData("https://m.soundcloud.com/chillhopmusic/lofi-rain", "soundcloud", "chillhopmusic/lofi-rain")]
    public void ProviderIdentityIsCanonicalAndNamespaceSeparated(string url, string provider, string id)
    {
        var source = Assert.IsType<SourceUrl>(SourceUrl.ParseVideo(url));
        Assert.Equal(provider, source.Provider); Assert.Equal(id, source.Id);
        Assert.DoesNotContain("tracking", source.CanonicalUrl);
    }

    [Theory]
    [InlineData("https://youtube.com.evil.test/watch?v=AbCdEfGh123")]
    [InlineData("https://youtube.com@127.0.0.1/watch?v=AbCdEfGh123")]
    [InlineData("https://www.youtube.com:444/watch?v=AbCdEfGh123")]
    [InlineData("http://www.youtube.com/watch?v=AbCdEfGh123")]
    [InlineData("https://www.youtube.com/watch?v=AbCdEfGh123&%76=AbCdEfGh124")]
    [InlineData("https://youtube.com./watch?v=AbCdEfGh123")]
    [InlineData("https://www.youtube.com\\@evil.test/watch?v=AbCdEfGh123")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://[::1]/watch?v=AbCdEfGh123")]
    [InlineData("https://soundcloud.com/discover/sets")]
    [InlineData("https://soundcloud.com.evil.test/artist/track")]
    public void HostileAndAmbiguousLinksAreRejected(string url) => Assert.Null(SourceUrl.ParseVideo(url));

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PLabcDEF_123", "PLabcDEF_123")]
    [InlineData("https://youtu.be/AbCdEfGh123?list=PLabcDEF_123", "PLabcDEF_123")]
    [InlineData("https://youtube.com/watch?v=AbCdEfGh123", null)]
    [InlineData("https://youtube.com/playlist?list=PLabcDEF_123&%6cist=PLabcDEF_456", null)]
    [InlineData("https://youtube.com.evil.test/playlist?list=PLabcDEF_123", null)]
    public void PlaylistRequiresOneExplicitRecognizedId(string input, string? expected)
        => Assert.Equal(expected, SourceUrl.ParsePlaylist(input));

    [Theory]
    [InlineData("127.1.2.3")][InlineData("10.1.2.3")][InlineData("172.31.0.1")][InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")][InlineData("100.100.100.200")][InlineData("0.0.0.0")][InlineData("224.0.0.1")]
    [InlineData("192.0.0.9")][InlineData("198.18.0.1")][InlineData("192.0.2.1")][InlineData("203.0.113.1")]
    [InlineData("::1")][InlineData("::ffff:127.0.0.1")][InlineData("2001:4860:4860::8888")]
    public void PrivateReservedAndTransitionAddressesAreBlocked(string input)
        => Assert.False(SafeHttpTransport.IsPublicAddress(IPAddress.Parse(input)));

    [Theory]
    [InlineData("https://127.0.0.1/")][InlineData("http://www.tiktok.com/@artist/video/123")]
    [InlineData("https://www.tiktok.com.evil.test/@artist/video/123")][InlineData("https://www.youtube.com/watch?v=AbCdEfGh123")]
    [InlineData("https://vm.tiktok.com@127.0.0.1/")]
    public async Task RedirectCannotEscapeAllowlist(string target)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ => { calls++; return new HttpResponseMessage(HttpStatusCode.Redirect)
            { Headers = { Location = new Uri(target) } }; }));
        var resolver = new TikTokRedirectResolver(http);
        await Assert.ThrowsAsync<ProviderException>(() => resolver.ResolveAsync("https://vm.tiktok.com/AbCdEf/", CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RedirectResolvesCanonicalWithoutDownloadingPost()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ => { calls++; return new HttpResponseMessage(HttpStatusCode.Redirect)
            { Headers = { Location = new Uri("https://www.tiktok.com/@artist/video/123456789?share=x") } }; }));
        var result = await new TikTokRedirectResolver(http).ResolveAsync("https://vm.tiktok.com/AbCdEf/", CancellationToken.None);
        Assert.Equal("123456789", result?.Id); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RedirectCyclesStopImmediately()
    {
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
            { Headers = { Location = new Uri("https://vm.tiktok.com/AbCdEf/") } }));
        var error = await Assert.ThrowsAsync<ProviderException>(() => new TikTokRedirectResolver(http)
            .ResolveAsync("https://vm.tiktok.com/AbCdEf/", CancellationToken.None));
        Assert.Equal("UNSAFE_SOURCE_REDIRECT", error.Code);
    }

    [Fact]
    public async Task JsonReaderEnforcesSizeWhenContentLengthUnknown()
    {
        using var input = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("{\"payload\":\"" + new string('x', 1000) + "\"}")));
        await Assert.ThrowsAsync<HttpRequestException>(() => SafeHttpTransport.ReadJsonAsync(input, 100, CancellationToken.None));
    }

    [Theory]
    [InlineData(1)][InlineData(50)][InlineData(51)][InlineData(250)][InlineData(1000)]
    public async Task YouTubeAdapterReadsAllOfficialPagesAt50ItemLimit(int count)
    {
        var calls = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("www.googleapis.com", request.RequestUri!.Host);
            Assert.Equal("fixture-only", Assert.Single(request.Headers.GetValues("X-Goog-Api-Key")));
            var path = request.RequestUri.AbsolutePath; calls.Add(path);
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            object body;
            if (path.EndsWith("/playlists", StringComparison.Ordinal)) body = new { items = new[] { new { id = "PLabcDEF_123" } } };
            else if (path.EndsWith("/playlistItems", StringComparison.Ordinal))
            {
                Assert.Equal("50", query["maxResults"]);
                var start = int.Parse(query["pageToken"] ?? "0"); var end = Math.Min(start + 50, count);
                body = new { nextPageToken = end < count ? end.ToString() : null,
                    items = Enumerable.Range(start, end - start).Select(i => new { snippet = new { position = i, resourceId = new { videoId = Id(i) } } }) };
            }
            else body = new { items = query["id"]!.Split(',').Select(id => new { id,
                status = new { privacyStatus = "public", embeddable = true },
                snippet = new { title = "Fixture source title", channelTitle = "Fixture creator" },
                contentDetails = new { duration = "PT3M20S" } }) };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
        })) { BaseAddress = new Uri("https://www.googleapis.com/youtube/v3/") };
        var api = new YouTubeApi(http, Config(), TimeProvider.System);
        var positions = new List<int>(); string? cursor = null;
        do
        {
            var page = await api.ReadPageAsync("PLabcDEF_123", cursor, CancellationToken.None);
            positions.AddRange(page.Videos.Select(x => x.Position));
            Assert.All(page.Videos, x => { Assert.True(x.Available); Assert.True(x.Embeddable); Assert.Equal(200, x.DurationSeconds); });
            cursor = page.NextPage;
        } while (cursor is not null);
        Assert.Equal(Enumerable.Range(0, count), positions);
        Assert.Equal(1 + 2 * (int)Math.Ceiling(count / 50d), calls.Count);
    }

    [Fact]
    public void DedupIsCaseSensitiveOrderedAndDoesNotUseTitles()
    {
        var videos = new[] { Video(3, "A0000000001"), Video(0, "A0000000001"),
            Video(1, "a0000000001"), Video(2, "A0000000002") with { Available = false } };
        var decisions = PlaylistDeduplication.Classify(videos, new HashSet<string>(StringComparer.Ordinal));
        Assert.Equal(new[] { "ACCEPTED", "ACCEPTED", "UNAVAILABLE", "DUPLICATE" }, decisions.Select(x => x.Status));
        Assert.Equal(new[] { 0, 1, 2, 3 }, decisions.Select(x => x.Video.Position));
    }

    [Fact]
    public async Task TikTokOembedAllowsMissingMetadataAndNeverReturnsExecutableHtml()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = JsonContent.Create(new
            { provider_name = "TikTok", html = "<script>alert('fixture')</script>" }) }));
        var source = SourceUrl.ParseVideo("https://www.tiktok.com/@artist/video/123456789")!;
        var result = await new TikTokMetadata(http, TimeProvider.System).ReadAsync(source, source.CanonicalUrl, CancellationToken.None);
        Assert.Null(result.OriginalTitle); Assert.Null(result.OriginalCreator); Assert.Null(result.ThumbnailUrl);
        Assert.Equal("EXTERNAL_OPEN_ONLY", result.PlaybackCapability);
        Assert.DoesNotContain("script", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task MissingYouTubeCredentialsReturnHonestExternalFallback()
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Must not call provider")));
        var api = new YouTubeApi(http, new ConfigurationBuilder().Build(), TimeProvider.System);
        var source = SourceUrl.ParseVideo("https://youtu.be/AbCdEfGh123")!;
        var result = await api.ReadVideoAsync(source, source.CanonicalUrl, CancellationToken.None);
        Assert.Equal("EXTERNAL_OPEN_ONLY", result.PlaybackCapability); Assert.Equal("YOUTUBE_NOT_CONFIGURED", result.Notice);
        Assert.Null(result.MetadataFetchedAt); Assert.Null(result.OriginalTitle);
    }

    [Theory]
    [InlineData("https://www.tiktok.com/@artist/video/123456789")]
    [InlineData("https://vm.tiktok.com/AbCdEf/")]
    public async Task DisabledTikTokFlagRejectsBeforeAnyNetworkRequest(string input)
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Must not fetch disabled provider")));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Features:EnableTikTokVideoImport"] = "false" }).Build();
        var resolver = new SourceResolver(new YouTubeApi(http, config, TimeProvider.System), new TikTokRedirectResolver(http),
            new TikTokMetadata(http, TimeProvider.System), new SoundCloudRedirectResolver(http),
            new SoundCloudMetadata(http, TimeProvider.System), config);
        var error = await Assert.ThrowsAsync<ProviderException>(() => resolver.ResolveAsync(input, CancellationToken.None));
        Assert.Equal("TIKTOK_IMPORT_DISABLED", error.Code);
    }

    [Fact]
    public async Task SoundCloudOembedExtractsSafeMetadataAndValidatesThumbnailHost()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                provider_name = "SoundCloud",
                title = "Midnight Synthwave",
                author_name = "RetroWave Studio",
                thumbnail_url = "https://i1.sndcdn.com/artworks-000123456-t500x500.jpg"
            })
        }));
        var source = SourceUrl.ParseVideo("https://soundcloud.com/retrowave/midnight-synthwave")!;
        var result = await new SoundCloudMetadata(http, TimeProvider.System).ReadAsync(source, source.CanonicalUrl, CancellationToken.None);
        Assert.Equal("soundcloud", result.Provider);
        Assert.Equal("Midnight Synthwave", result.OriginalTitle);
        Assert.Equal("RetroWave Studio", result.OriginalCreator);
        Assert.Equal("https://i1.sndcdn.com/artworks-000123456-t500x500.jpg", result.ThumbnailUrl);
    }

    [Fact]
    public async Task TikTokEmbedRequiresMatchingOfficialVideoIdentity()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = JsonContent.Create(new
            { provider_name = "TikTok", type = "video", embed_product_id = "123456789" }) }));
        var source = SourceUrl.ParseVideo("https://www.tiktok.com/@artist/video/123456789")!;
        var result = await new TikTokMetadata(http, TimeProvider.System).ReadAsync(source, source.CanonicalUrl, CancellationToken.None);
        Assert.Equal("EMBED_SUPPORTED", result.PlaybackCapability);
    }
    internal static string Id(int i) => $"v{i:D10}";
    internal static PlaylistVideo Video(int i, string? id = null) => new(i, id ?? Id(i), "Same title", "Fixture creator", null, 120, true, true);
    internal static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["YouTube:ApiKey"] = "fixture-only" }).Build();
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }
}


