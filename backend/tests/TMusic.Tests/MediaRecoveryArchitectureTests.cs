using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TMusic.Application.Downloads;
using TMusic.Application.Providers;
using TMusic.Infrastructure.Downloads;
using TMusic.Infrastructure.Storage;
using Xunit;

namespace TMusic.Tests;

public sealed class MediaRecoveryArchitectureTests
{
    [Fact]
    public void ErrorClassifierDistinguishesBotDetectionJsRuntimeEjsAndNetworkFailures()
    {
        var bot = DownloadErrorClassifier.Classify("youtube",
            "ERROR: [youtube] aqz-KE-bpKQ: Sign in to confirm you're not a bot. Use --cookies for authentication.");
        Assert.Equal("SOURCE_ACCESS_CHECK_REQUIRED", bot.ErrorCode);
        Assert.Equal(DownloadErrorCategory.BotDetected, bot.Category);
        Assert.Equal(DownloadFailureScope.BackendEnvironment, bot.Scope);
        Assert.False(bot.Retryable);
        Assert.True(bot.IsAccessRestriction);

        var js = DownloadErrorClassifier.Classify("youtube",
            "WARNING: [youtube] No supported JavaScript runtime could be found.");
        Assert.Equal("AUDIO_ENGINE_UNAVAILABLE", js.ErrorCode);
        Assert.Equal(DownloadErrorCategory.JsRuntimeMissing, js.Category);
        Assert.False(js.Retryable);

        var ejs = DownloadErrorClassifier.Classify("youtube",
            "WARNING: Remote components challenge solver script (ejs:github) were skipped.");
        Assert.Equal("AUDIO_ENGINE_UNAVAILABLE", ejs.ErrorCode);
        Assert.Equal(DownloadErrorCategory.EjsComponentMissing, ejs.Category);
        Assert.False(ejs.Retryable);

        var rate = DownloadErrorClassifier.Classify("youtube", "ERROR: HTTP Error 429: Too Many Requests");
        Assert.Equal("RATE_LIMITED", rate.ErrorCode);
        Assert.Equal(DownloadErrorCategory.Http429, rate.Category);
        Assert.True(rate.Retryable);

        var timeout = DownloadErrorClassifier.Classify("youtube", "ERROR: The read operation timed out");
        Assert.Equal("SOURCE_NETWORK_ERROR", timeout.ErrorCode);
        Assert.Equal(DownloadErrorCategory.NetworkTimeout, timeout.Category);
        Assert.True(timeout.Retryable);
    }

    [Fact]
    public async Task CircuitBreakerStopsRepeatedYouTubeBotChallengesWithoutAffectingOtherProviders()
    {
        var fakeClock = new AdjustableTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new AudioDownloadOptions
        {
            AccessCheckCircuitThreshold = 2,
            AccessCheckCooldownSeconds = 60
        });
        var breaker = new SourceAccessCircuitBreaker(fakeClock, options);
        var diagnostics = new MediaDownloadDiagnostics(NullLogger<MediaDownloadDiagnostics>.Instance, fakeClock);
        var extractor = new CountingExtractor(throwCode: "SOURCE_ACCESS_CHECK_REQUIRED");
        var disabledExternal = CreateDisabledExternalProvider(fakeClock);
        var resolver = new StubSourceResolver();

        var ytProvider = new YouTubeMediaProvider(resolver, extractor, disabledExternal, breaker, diagnostics);
        var ttProvider = new TikTokMediaProvider(resolver, new SucceedingExtractor(), diagnostics);
        var orchestrator = new DownloadOrchestrator(new MediaProviderRegistry([ytProvider, ttProvider]));

        // First two YouTube bot challenges invoke extractor and trip circuit breaker
        for (var i = 0; i < 2; i++)
        {
            var err = await Assert.ThrowsAsync<ProviderException>(() => orchestrator.DownloadAudioAsync(
                "https://www.youtube.com/watch?v=aqz-KE-bpKQ", Path.GetTempPath(), 192, $"corr-{i}",
                (_, _) => Task.CompletedTask, CancellationToken.None));
            Assert.Equal("SOURCE_ACCESS_CHECK_REQUIRED", err.Code);
            Assert.False(err.Retryable);
        }
        Assert.Equal(2, extractor.Calls);
        Assert.True(breaker.IsOpen("youtube", out _));

        // Third YouTube attempt fails fast without hitting extractor again
        var fastFail = await Assert.ThrowsAsync<ProviderException>(() => orchestrator.DownloadAudioAsync(
            "https://www.youtube.com/watch?v=aqz-KE-bpKQ", Path.GetTempPath(), 192, "corr-fast",
            (_, _) => Task.CompletedTask, CancellationToken.None));
        Assert.Equal("SOURCE_ACCESS_CHECK_REQUIRED", fastFail.Code);
        Assert.Equal(2, extractor.Calls);

        // TikTok remains unaffected while YouTube circuit is open
        var workDir = Path.Combine(Path.GetTempPath(), "tmusic_dl_cbtest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var ttResult = await orchestrator.DownloadAudioAsync(
                "https://www.tiktok.com/@creator/video/7123456789012345678", workDir, 192, "corr-tt",
                (_, _) => Task.CompletedTask, CancellationToken.None);
            Assert.Equal("tiktok", ttResult.ProviderId);
            Assert.True(File.Exists(ttResult.Mp3FilePath));
        }
        finally
        {
            if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
        }

        // After cooldown expires, circuit closes
        fakeClock.Advance(TimeSpan.FromSeconds(61));
        Assert.False(breaker.IsOpen("youtube", out _));
        Assert.Contains(diagnostics.RecentSnapshot(), e => e.CorrelationId == "corr-fast" && e.Stage == "CIRCUIT_OPEN");
    }

    [Fact]
    public async Task ExternalProviderAdapterValidatesResponsesCredentialsExpirySsrfAndMp3Payload()
    {
        var fakeClock = new AdjustableTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var storage = new AudioStorage(new ConfigurationBuilder().Build());
        var workDir = Path.Combine(Path.GetTempPath(), "tmusic_dl_ext_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            // 1. Valid metadata + valid MP3 delivery
            var validMp3 = new byte[2048];
            validMp3[0] = (byte)'I'; validMp3[1] = (byte)'D'; validMp3[2] = (byte)'3'; validMp3[3] = 0x04;
            var okHandler = new StubHttpHandler(req =>
            {
                Assert.Equal("Bearer", req.RequestUri!.AbsolutePath != "/tunnel" ? req.Headers.Authorization?.Scheme : "Bearer");
                if (req.RequestUri.AbsolutePath == "/info")
                    return JsonResponse(HttpStatusCode.OK, """{"status":"ok","title":"Big Buck Bunny","duration":634,"thumbnail":"https://i.ytimg.com/vi/aqz-KE-bpKQ/hqdefault.jpg","audioFormat":{"container":"mp3","file_size_str":"9.1 MB"}}""");
                if (req.RequestUri.AbsolutePath == "/create")
                    return JsonResponse(HttpStatusCode.OK, """{"status":"ok","url":"https://tunelio.dev/tunnel?id=1&sig=abc","filename":"bbb.mp3","quality":"mp3","mode":"audio"}""");
                if (req.RequestUri.AbsolutePath == "/tunnel")
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(validMp3) { Headers = { ContentType = new("audio/mpeg") } }
                    };
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            var provider = CreateExternalProvider(okHandler, fakeClock, storage, enabled: true, apiKey: "tnl_test_fixture_key");
            var meta = await provider.GetMetadataAsync("https://www.youtube.com/watch?v=aqz-KE-bpKQ", CancellationToken.None);
            Assert.Equal("Big Buck Bunny", meta.Title);
            Assert.Equal(634, meta.DurationSeconds);
            Assert.Equal("mp3", meta.AudioContainer);

            var mp3Path = await provider.DownloadToMp3Async("https://www.youtube.com/watch?v=aqz-KE-bpKQ", workDir, 192, CancellationToken.None);
            Assert.True(File.Exists(mp3Path));
            Assert.Equal(2048, new FileInfo(mp3Path).Length);

            // 2. Invalid credentials -> EXTERNAL_PROVIDER_AUTH_FAILED (non-retryable)
            var unauth = CreateExternalProvider(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)),
                fakeClock, storage, enabled: true, apiKey: "tnl_bad");
            var authErr = await Assert.ThrowsAsync<ProviderException>(() =>
                unauth.CreateDownloadAsync("https://www.youtube.com/watch?v=aqz-KE-bpKQ", 192, CancellationToken.None));
            Assert.Equal("EXTERNAL_PROVIDER_AUTH_FAILED", authErr.Code);
            Assert.False(authErr.Retryable);

            // 3. Rate limited -> RATE_LIMITED (retryable)
            var rateLimited = CreateExternalProvider(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)),
                fakeClock, storage, enabled: true, apiKey: "tnl_ok");
            var rateErr = await Assert.ThrowsAsync<ProviderException>(() =>
                rateLimited.CreateDownloadAsync("https://www.youtube.com/watch?v=aqz-KE-bpKQ", 192, CancellationToken.None));
            Assert.Equal("RATE_LIMITED", rateErr.Code);
            Assert.True(rateErr.Retryable);

            // 4. Provider unavailable -> PROVIDER_TEMPORARILY_UNAVAILABLE (retryable)
            var unavailable = CreateExternalProvider(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
                fakeClock, storage, enabled: true, apiKey: "tnl_ok");
            var unavailErr = await Assert.ThrowsAsync<ProviderException>(() =>
                unavailable.CreateDownloadAsync("https://www.youtube.com/watch?v=aqz-KE-bpKQ", 192, CancellationToken.None));
            Assert.Equal("PROVIDER_TEMPORARILY_UNAVAILABLE", unavailErr.Code);
            Assert.True(unavailErr.Retryable);

            // 5. Expired download URL -> EXTERNAL_DOWNLOAD_URL_EXPIRED (retryable)
            var expiredHandler = new StubHttpHandler(req =>
                req.RequestUri!.AbsolutePath == "/create"
                    ? JsonResponse(HttpStatusCode.OK, """{"status":"ok","url":"https://tunelio.dev/tunnel?id=1","quality":"mp3"}""")
                    : new HttpResponseMessage(HttpStatusCode.Gone));
            var expiredProvider = CreateExternalProvider(expiredHandler, fakeClock, storage, enabled: true, apiKey: "tnl_ok");
            var expErr = await Assert.ThrowsAsync<ProviderException>(() =>
                expiredProvider.DownloadToMp3Async("https://www.youtube.com/watch?v=aqz-KE-bpKQ", workDir, 192, CancellationToken.None));
            Assert.Equal("EXTERNAL_DOWNLOAD_URL_EXPIRED", expErr.Code);
            Assert.True(expErr.Retryable);

            // 6. Malformed JSON -> EXTERNAL_PROVIDER_INVALID_RESPONSE
            var badJson = CreateExternalProvider(new StubHttpHandler(_ => JsonResponse(HttpStatusCode.OK, "{broken")),
                fakeClock, storage, enabled: true, apiKey: "tnl_ok");
            var badJsonErr = await Assert.ThrowsAsync<ProviderException>(() =>
                badJson.GetMetadataAsync("https://www.youtube.com/watch?v=aqz-KE-bpKQ", CancellationToken.None));
            Assert.Equal("EXTERNAL_PROVIDER_INVALID_RESPONSE", badJsonErr.Code);

            // 7. SSRF / untrusted download host rejected before fetching
            var ssrfHandler = new StubHttpHandler(_ =>
                JsonResponse(HttpStatusCode.OK, """{"status":"ok","url":"https://evil.example/steal.mp3","quality":"mp3"}"""));
            var ssrfProvider = CreateExternalProvider(ssrfHandler, fakeClock, storage, enabled: true, apiKey: "tnl_ok");
            var ssrfErr = await Assert.ThrowsAsync<ProviderException>(() =>
                ssrfProvider.CreateDownloadAsync("https://www.youtube.com/watch?v=aqz-KE-bpKQ", 192, CancellationToken.None));
            Assert.Equal("EXTERNAL_PROVIDER_INVALID_RESPONSE", ssrfErr.Code);

            // 8. Non-MP3 payload rejected and temp file cleaned up
            var htmlHandler = new StubHttpHandler(req =>
                req.RequestUri!.AbsolutePath == "/create"
                    ? JsonResponse(HttpStatusCode.OK, """{"status":"ok","url":"https://tunelio.dev/tunnel?id=2","quality":"mp3"}""")
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("<html>not mp3</html>", Encoding.UTF8, "application/octet-stream")
                    });
            var htmlWorkDir = Path.Combine(workDir, "html-check");
            var htmlProvider = CreateExternalProvider(htmlHandler, fakeClock, storage, enabled: true, apiKey: "tnl_ok");
            var invalidAudio = await Assert.ThrowsAsync<ProviderException>(() =>
                htmlProvider.DownloadToMp3Async("https://www.youtube.com/watch?v=aqz-KE-bpKQ", htmlWorkDir, 192, CancellationToken.None));
            Assert.Equal("AUDIO_FILE_INVALID", invalidAudio.Code);
            Assert.False(File.Exists(Path.Combine(htmlWorkDir, "track.mp3")));
        }
        finally
        {
            if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public async Task YouTubeProviderFallsBackToEnabledExternalProviderOnlyOnAccessRestriction()
    {
        var fakeClock = new AdjustableTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var storage = new AudioStorage(new ConfigurationBuilder().Build());
        var workDir = Path.Combine(Path.GetTempPath(), "tmusic_dl_fb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            var validMp3 = new byte[2048];
            validMp3[0] = (byte)'I'; validMp3[1] = (byte)'D'; validMp3[2] = (byte)'3'; validMp3[3] = 0x04;
            var handler = new StubHttpHandler(req =>
                req.RequestUri!.AbsolutePath == "/create"
                    ? JsonResponse(HttpStatusCode.OK, """{"status":"ok","url":"https://tunelio.dev/tunnel?id=ok","quality":"mp3"}""")
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(validMp3) { Headers = { ContentType = new("audio/mpeg") } }
                    });
            var external = CreateExternalProvider(handler, fakeClock, storage, enabled: true, apiKey: "tnl_ok");
            var breaker = new SourceAccessCircuitBreaker(fakeClock, Options.Create(new AudioDownloadOptions()));
            var diagnostics = new MediaDownloadDiagnostics(NullLogger<MediaDownloadDiagnostics>.Instance, fakeClock);
            var yt = new YouTubeMediaProvider(new StubSourceResolver(),
                new CountingExtractor("SOURCE_ACCESS_CHECK_REQUIRED"), external, breaker, diagnostics);

            var result = await yt.DownloadAudioAsync(
                new MediaDownloadRequest("https://www.youtube.com/watch?v=aqz-KE-bpKQ", workDir, 192, "corr-fb", (_, _) => Task.CompletedTask),
                CancellationToken.None);
            Assert.Equal("tunelio", result.EngineName);
            Assert.True(File.Exists(result.Mp3FilePath));
        }
        finally
        {
            if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public void StaleTempWorkspaceCleanupDeletesOnlyOldTMusicDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmusic_cleanup_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var staleDl = Directory.CreateDirectory(Path.Combine(root, "tmusic_dl_old"));
            var staleGuest = Directory.CreateDirectory(Path.Combine(root, "tmusic_guest_old"));
            var freshDl = Directory.CreateDirectory(Path.Combine(root, "tmusic_dl_fresh"));
            var unrelated = Directory.CreateDirectory(Path.Combine(root, "other_app_dir"));

            var now = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
            Directory.SetLastWriteTimeUtc(staleDl.FullName, now.AddHours(-5).UtcDateTime);
            Directory.SetLastWriteTimeUtc(staleGuest.FullName, now.AddHours(-3).UtcDateTime);
            Directory.SetLastWriteTimeUtc(freshDl.FullName, now.AddMinutes(-10).UtcDateTime);
            Directory.SetLastWriteTimeUtc(unrelated.FullName, now.AddHours(-10).UtcDateTime);

            var removed = AudioTempWorkspace.CleanupStaleDirectories(TimeSpan.FromHours(2),
                new AdjustableTimeProvider(now), rootOverride: root);

            Assert.Equal(2, removed);
            Assert.False(Directory.Exists(staleDl.FullName));
            Assert.False(Directory.Exists(staleGuest.FullName));
            Assert.True(Directory.Exists(freshDl.FullName));
            Assert.True(Directory.Exists(unrelated.FullName));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MediaHealthEndpointReportsDisabledWhenAudioDownloadsFeatureIsOff()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Testing");
            web.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TMusic"] = "Server=127.0.0.1,1;Database=NeverUsed;Integrated Security=True;Connect Timeout=1;Encrypt=True",
                ["Auth:SigningKey"] = "CONTRACT-TEST-ONLY-KEY-THIRTYTWOCHARS-PLUS",
                ["Features:EnableAudioDownloads"] = "false"
            }));
        });
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync("/api/health/media");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("disabled", json.GetProperty("status").GetString());
        Assert.Equal("disabled", json.GetProperty("ytDlp").GetString());
        Assert.Equal("disabled", json.GetProperty("ffmpeg").GetString());
        Assert.Equal("disabled", json.GetProperty("ffprobe").GetString());
        Assert.Equal("disabled", json.GetProperty("javascriptRuntime").GetString());
        Assert.Equal("disabled", json.GetProperty("ejs").GetString());
    }

    [FfmpegFact]
    public async Task MediaHealthEndpointReportsHealthyWhenLocalRuntimeToolsAreInstalled()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Testing");
            web.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TMusic"] = "Server=127.0.0.1,1;Database=NeverUsed;Integrated Security=True;Connect Timeout=1;Encrypt=True",
                ["Auth:SigningKey"] = "CONTRACT-TEST-ONLY-KEY-THIRTYTWOCHARS-PLUS",
                ["Features:EnableAudioDownloads"] = "true"
            }));
        });
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync("/api/health/media");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200 healthy, got {(int)response.StatusCode}: {body}");
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("healthy", root.GetProperty("status").GetString());
        Assert.Equal("available", root.GetProperty("ytDlp").GetString());
        Assert.Equal("available", root.GetProperty("ffmpeg").GetString());
        Assert.Equal("available", root.GetProperty("ffprobe").GetString());
        Assert.Equal("available", root.GetProperty("javascriptRuntime").GetString());
        Assert.Equal("available", root.GetProperty("ejs").GetString());
    }

    private static TunelioExternalAudioProvider CreateDisabledExternalProvider(TimeProvider clock) =>
        CreateExternalProvider(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)),
            clock, new AudioStorage(new ConfigurationBuilder().Build()), enabled: false, apiKey: null);

    private static TunelioExternalAudioProvider CreateExternalProvider(
        HttpMessageHandler handler,
        TimeProvider clock,
        IAudioStorage storage,
        bool enabled,
        string? apiKey)
    {
        var http = new HttpClient(handler);
        var opt = Options.Create(new ExternalMediaProviderOptions
        {
            Enabled = enabled,
            Provider = "tunelio",
            BaseUrl = "https://tunelio.dev",
            ApiKey = apiKey,
            TimeoutSeconds = 15
        });
        return new TunelioExternalAudioProvider(http, opt, storage, clock);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset current) : TimeProvider
    {
        private DateTimeOffset now = current;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan delta) => now = now.Add(delta);
    }

    private sealed class CountingExtractor(string throwCode) : IAudioExtractor
    {
        public int Calls { get; private set; }
        public bool IsAvailable => true;
        public Task<string> ExtractMp3Async(string canonicalUrl, string workDir, Func<int, CancellationToken, Task> onProgress, CancellationToken ct)
        {
            Calls++;
            throw DownloadErrorClassifier.ClassifyErrorCode(throwCode).ToProviderException();
        }
    }

    private sealed class SucceedingExtractor : IAudioExtractor
    {
        public bool IsAvailable => true;
        public async Task<string> ExtractMp3Async(string canonicalUrl, string workDir, Func<int, CancellationToken, Task> onProgress, CancellationToken ct)
        {
            Directory.CreateDirectory(workDir);
            var path = Path.Combine(workDir, "track.mp3");
            var bytes = new byte[512];
            bytes[0] = (byte)'I'; bytes[1] = (byte)'D'; bytes[2] = (byte)'3';
            await File.WriteAllBytesAsync(path, bytes, ct);
            return path;
        }
    }

    private sealed class StubSourceResolver : ISourceResolver
    {
        public Task<ResolvedSource> ResolveAsync(string input, CancellationToken cancellationToken)
        {
            var s = SourceUrl.ParseVideo(input)!;
            return Task.FromResult(new ResolvedSource(s.Provider, s.Id, input, s.CanonicalUrl,
                "Fixture Track", "Fixture Artist", null, 120, "EMBED_SUPPORTED", DateTimeOffset.UtcNow, null));
        }
    }
}
