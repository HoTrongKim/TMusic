using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TMusic.Application.Downloads;
using TMusic.Application.Providers;
using TMusic.Domain.Entities;
using TMusic.Infrastructure.Persistence;
using TMusic.Infrastructure.Storage;

namespace TMusic.Infrastructure.Downloads;

public interface IAudioExtractor
{
    bool IsAvailable { get; }
    Task<bool> ValidateToolsAsync(CancellationToken ct) => Task.FromResult(IsAvailable);
    Task<string> ExtractMp3Async(
        string canonicalUrl,
        string workDir,
        Func<int, CancellationToken, Task> onProgress,
        CancellationToken ct);

    Task<string> ExtractMp3Async(
        string canonicalUrl,
        string workDir,
        Func<int, CancellationToken, Task> onProgress,
        int? bitrateKbps,
        CancellationToken ct) => ExtractMp3Async(canonicalUrl, workDir, onProgress, ct);
}

public sealed partial class YtDlpAudioExtractor(
    IConfiguration config,
    IOptions<AudioDownloadOptions> options) : IAudioExtractor
{
    public YtDlpAudioExtractor(IConfiguration config)
        : this(config, Options.Create(new AudioDownloadOptions())) { }

    [GeneratedRegex(@"\[download\]\s+(\d{1,3}(?:\.\d+)?)%", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex DownloadPercentRegex();

    public bool IsAvailable =>
        config.GetValue("Features:EnableAudioDownloads", false) &&
        ResolveYtDlpPath() is not null &&
        ResolveFfmpegPath() is not null;

    public async Task<bool> ValidateToolsAsync(CancellationToken ct)
    {
        var ytDlp = ResolveYtDlpPath();
        var ffmpeg = ResolveFfmpegPath();
        if (ytDlp is null || ffmpeg is null) return false;
        var ytDlpResult = await BoundedAudioProcess.RunAsync(ytDlp, ["--version"], Path.GetTempPath(), null, ct,
            TimeSpan.FromSeconds(15), captureStandardOutput: true);
        var ffmpegResult = await BoundedAudioProcess.RunAsync(ffmpeg, ["-version"], Path.GetTempPath(), null, ct,
            TimeSpan.FromSeconds(15), captureStandardOutput: true);
        if (!ffmpegResult.StandardError.Contains("ffmpeg version", StringComparison.OrdinalIgnoreCase) &&
            !ffmpegResult.StandardOutput.Contains("ffmpeg version", StringComparison.OrdinalIgnoreCase))
            return false;
        var expectedVersion = config["AudioDownload:YtDlpVersion"];
        if (ytDlpResult.ExitCode != 0 || ffmpegResult.ExitCode != 0 ||
            (!string.IsNullOrWhiteSpace(expectedVersion) &&
             !string.Equals(ytDlpResult.StandardOutput.Trim(), expectedVersion, StringComparison.Ordinal)))
            return false;

        var expectedDenoVersion = config["AudioDownload:DenoVersion"];
        if (string.IsNullOrWhiteSpace(expectedDenoVersion)) return true;
        var deno = ResolveDenoPath();
        if (deno is null) return false;
        var denoResult = await BoundedAudioProcess.RunAsync(deno, ["--version"], Path.GetTempPath(), null, ct,
            TimeSpan.FromSeconds(15), captureStandardOutput: true);
        return denoResult.ExitCode == 0 && denoResult.StandardOutput.StartsWith(
            $"deno {expectedDenoVersion}", StringComparison.Ordinal);
    }

    public string? ResolveYtDlpPath() => ResolveBinary(
        config["AudioDownload:YtDlpPath"],
        OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp",
        "yt-dlp.yt-dlp*");

    public string? ResolveFfmpegPath() => ResolveBinary(
        config["AudioDownload:FfmpegPath"],
        OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg",
        "*FFmpeg*");

    public string? ResolveFfprobePath() => ResolveBinary(
        config["AudioDownload:FfprobePath"],
        OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe",
        "*FFmpeg*");

    public string? ResolveDenoPath() => ResolveBinary(
        config["AudioDownload:DenoPath"],
        OperatingSystem.IsWindows() ? "deno.exe" : "deno",
        "deno.deno*");

    private static string? ResolveBinary(string? configured, string exeName, string wingetFolderGlob)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetFullPath(configured);

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(dir, exeName);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var wingetLink = Path.Combine(localAppData, "Microsoft", "WinGet", "Links", exeName);
            if (File.Exists(wingetLink)) return Path.GetFullPath(wingetLink);

            var wingetPackages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(wingetPackages))
            {
                foreach (var pkgDir in Directory.EnumerateDirectories(wingetPackages, wingetFolderGlob))
                {
                    var found = Directory.EnumerateFiles(pkgDir, exeName, SearchOption.AllDirectories).FirstOrDefault();
                    if (found is not null) return Path.GetFullPath(found);
                }
            }
        }

        return null;
    }

    public static IReadOnlyList<string> BuildSafeArguments(string canonicalUrl, string disabledToolsDirectory,
        string outputTemplate, string? proxyAddress = null, string? denoPath = null)
    {
        var parsed = SourceUrl.ParseVideo(canonicalUrl)
            ?? throw new ProviderException("INVALID_SOURCE_URL");
        if (!Uri.TryCreate(proxyAddress, UriKind.Absolute, out var proxy) || proxy.Scheme != "http" ||
            proxy.Host != "127.0.0.1" || proxy.IsDefaultPort || proxy.UserInfo.Length == 0)
            throw new ProviderException("AUDIO_EGRESS_REQUIRED");
        var arguments = new List<string>
        {
            "--no-playlist",
            "--no-cookies",
            "--no-cookies-from-browser",
            "--no-config",
            "--no-plugin-dirs",
            "--no-remote-components",
            "--no-cache-dir",
            "--proxy", proxy.AbsoluteUri,
            "--downloader", "native",
            "--hls-prefer-native",
            "--retries", "2",
            "--fragment-retries", "2",
            "--extractor-retries", "1",
            "--format", "bestaudio/best",
            "--fixup", "never",
            "--max-filesize", "100M",
            "--socket-timeout", "20",
            "--newline",
            "--no-warnings",
            // Hide FFmpeg/ffprobe from yt-dlp: only C# may launch the constrained converter.
            "--ffmpeg-location", disabledToolsDirectory,
            "-o", outputTemplate,
        };
        if (!string.IsNullOrWhiteSpace(denoPath))
        {
            arguments.Add("--js-runtimes");
            arguments.Add($"deno:{Path.GetFullPath(denoPath)}");
        }
        arguments.Add("--");
        arguments.Add(parsed.CanonicalUrl);
        return arguments;
    }

    public static ProviderException ClassifyFailure(string provider, string standardError) =>
        DownloadErrorClassifier.Classify(provider, standardError).ToProviderException();

    public static bool IsRetryableErrorCode(string? errorCode) =>
        DownloadErrorClassifier.IsRetryableErrorCode(errorCode);

    public Task<string> ExtractMp3Async(
        string canonicalUrl,
        string workDir,
        Func<int, CancellationToken, Task> onProgress,
        CancellationToken ct) => ExtractMp3Async(canonicalUrl, workDir, onProgress, null, ct);

    public async Task<string> ExtractMp3Async(
        string canonicalUrl,
        string workDir,
        Func<int, CancellationToken, Task> onProgress,
        int? bitrateKbps,
        CancellationToken ct)
    {
        if (!config.GetValue("Features:EnableAudioDownloads", false))
            throw new ProviderException("AUDIO_DOWNLOAD_DISABLED");
        if (!AudioBitrate.IsValid(bitrateKbps))
            throw new ProviderException("INVALID_BITRATE");

        using var capacity = AudioExtractionCapacity.Acquire();
        var ytDlp = ResolveYtDlpPath() ?? throw new ProviderException("AUDIO_ENGINE_UNAVAILABLE");
        var ffmpeg = ResolveFfmpegPath() ?? throw new ProviderException("AUDIO_ENGINE_UNAVAILABLE");

        Directory.CreateDirectory(workDir);
        workDir = Path.GetFullPath(workDir);
        var downloadedFile = Path.Combine(workDir, "source.media");
        var disabledTools = Path.Combine(workDir, "disabled-tools");
        Directory.CreateDirectory(disabledTools);
        var source = SourceUrl.ParseVideo(canonicalUrl) ?? throw new ProviderException("INVALID_SOURCE_URL");
        var timeout = options.Value.EffectiveTimeout;
        await using var egress = new AudioDownloadEgressProxy(source.Provider, timeout);
        var args = BuildSafeArguments(canonicalUrl, disabledTools, downloadedFile, egress.ProxyAddress, ResolveDenoPath());

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var token = timeoutCts.Token;

        var lastReportedPercent = 0;
        var lastReportTime = Stopwatch.GetTimestamp();

        var downloaded = await BoundedAudioProcess.RunAsync(ytDlp, args, workDir, async line =>
            {
                var match = DownloadPercentRegex().Match(line);
                if (match.Success &&
                    double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                {
                    var rounded = Math.Clamp((int)Math.Round(pct * 0.9), 1, 90);
                    var elapsedMs = Stopwatch.GetElapsedTime(lastReportTime).TotalMilliseconds;
                    if (rounded > lastReportedPercent && (elapsedMs >= 1000 || rounded >= 90))
                    {
                        lastReportedPercent = rounded;
                        lastReportTime = Stopwatch.GetTimestamp();
                        await onProgress(rounded, token);
                    }
                }
            }, token, timeout);
        if (downloaded.ExitCode != 0)
        {
            throw ClassifyFailure(source.Provider, downloaded.StandardError);
        }

        await onProgress(95, token);
        var mp3File = Path.Combine(workDir, "track.mp3");
        await LocalMp3Converter.ConvertAsync(ffmpeg, downloadedFile, mp3File, token, bitrateKbps, timeout);

        return mp3File;
    }
}

public sealed class AudioDownloadProcessor(
    TMusicDb db,
    IAudioExtractor extractor,
    IAudioStorage storage,
    TimeProvider clock,
    IOptions<AudioDownloadOptions> options,
    DownloadOrchestrator? orchestrator = null)
{
    public async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        var job = await ClaimAsync(ct);
        if (job is null) return false;

        var lease = job.LeaseOwner;
        var tempDir = Path.Combine(Path.GetTempPath(), "tmusic_dl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            await ExecuteJobAsync(job, tempDir, ct);
        }
        catch (AudioStorageException e)
        {
            await FailAsync(job.Id, lease, e.Code, e.Retryable, ct);
        }
        catch (ProviderException e)
        {
            await FailAsync(job.Id, lease, e.Code, e.Retryable, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await FailAsync(job.Id, lease, "AUDIO_DOWNLOAD_TIMEOUT", retryable: true, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested &&
            (e is HttpRequestException or IOException or SocketException))
        {
            await FailAsync(job.Id, lease, "TEMPORARY_FAILURE", retryable: true, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested &&
            (e is DbUpdateConcurrencyException ||
             e is DbUpdateException { InnerException: SqlException { Number: 1205 or 2601 or 2627 } }))
        {
            await FailAsync(job.Id, lease, "TEMPORARY_FAILURE", retryable: true, ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { }
            }
        }

        return true;
    }

    private async Task<AudioDownloadJob?> ClaimAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var job = await db.AudioDownloadJobs
            .Where(x => (x.Status == ImportStatus.Queued || x.Status == ImportStatus.Running) &&
                        x.NextAttemptAt <= now &&
                        (x.LeaseUntil == null || x.LeaseUntil <= now))
            .OrderBy(x => x.NextAttemptAt)
            .ThenBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (job is null) return null;

        job.Status = ImportStatus.Running;
        job.LeaseOwner = Guid.NewGuid();
        job.HeartbeatAt = now;
        job.LeaseUntil = now.Add(options.Value.EffectiveLease);
        if (job.ProgressPercent < 5) job.ProgressPercent = 5;

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await db.UserLibrarySongs
                .Where(x => x.Id == job.UserLibrarySongId && x.UserId == job.OwnerId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.AudioStatus, ImportStatus.Running)
                    .SetProperty(x => x.UpdatedAt, now), ct);
            await tx.CommitAsync(ct);
            return job;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return null;
        }
    }

    private async Task ExecuteJobAsync(AudioDownloadJob job, string tempDir, CancellationToken ct)
    {
        if (!job.RightsConfirmed)
            throw new ProviderException("RIGHTS_CONFIRMATION_REQUIRED", retryable: false);

        var entry = await db.UserLibrarySongs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == job.UserLibrarySongId && x.UserId == job.OwnerId, ct);
        if (entry is null)
        {
            await db.AudioDownloadJobs
                .Where(x => x.Id == job.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, ImportStatus.Canceled)
                    .SetProperty(x => x.LeaseOwner, (Guid?)null)
                    .SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null), ct);
            return;
        }

        string mp3Path;
        try
        {
            Func<int, CancellationToken, Task> reportProgress = async (pct, innerCt) =>
            {
                var beat = clock.GetUtcNow();
                var changed = await db.AudioDownloadJobs
                    .Where(x => x.Id == job.Id && x.LeaseOwner == job.LeaseOwner && x.Status == ImportStatus.Running)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.ProgressPercent, pct)
                        .SetProperty(x => x.HeartbeatAt, beat)
                        .SetProperty(x => x.LeaseUntil, beat.Add(options.Value.EffectiveLease)), innerCt);
                if (changed == 0) throw new OperationCanceledException("Audio download lease was revoked.", innerCt);
            };

            if (orchestrator is not null)
            {
                var result = await orchestrator.DownloadAudioAsync(
                    job.CanonicalUrl,
                    tempDir,
                    job.BitrateKbps,
                    job.Id.ToString("N"),
                    reportProgress,
                    ct);
                mp3Path = result.Mp3FilePath;
            }
            else
            {
                mp3Path = await extractor.ExtractMp3Async(
                    job.CanonicalUrl,
                    tempDir,
                    reportProgress,
                    job.BitrateKbps,
                    ct);
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or SocketException)
        {
            throw new ProviderException("SOURCE_NETWORK_ERROR", retryable: true);
        }

        var stored = await storage.SaveFromTempFileAsync(job.OwnerId, mp3Path, ct);

        db.ChangeTracker.Clear();
        var published = false;
        string? previousFile = null;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = clock.GetUtcNow();
            // Take the job write lock before touching the song, matching cancellation's lock order.
            // This fence prevents a canceled/reclaimed worker from attaching its output.
            var changed = await db.AudioDownloadJobs
                .Where(x => x.Id == job.Id && x.OwnerId == job.OwnerId &&
                    x.LeaseOwner == job.LeaseOwner && x.Status == ImportStatus.Running)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ImportStatus.Complete)
                    .SetProperty(x => x.ProgressPercent, 100)
                    .SetProperty(x => x.OutputFileName, stored.FileName)
                    .SetProperty(x => x.OutputSizeBytes, stored.SizeBytes)
                    .SetProperty(x => x.CompletedAt, now)
                    .SetProperty(x => x.ErrorCode, (string?)null)
                    .SetProperty(x => x.LeaseOwner, (Guid?)null)
                    .SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null)
                    .SetProperty(x => x.HeartbeatAt, now), ct);
            if (changed == 0) return;
            var trackedEntry = await db.UserLibrarySongs
                .FirstOrDefaultAsync(x => x.Id == job.UserLibrarySongId && x.UserId == job.OwnerId, ct);
            if (trackedEntry is null) return;
            previousFile = trackedEntry.AudioFileName;
            trackedEntry.AudioFileName = stored.FileName;
            trackedEntry.AudioSizeBytes = stored.SizeBytes;
            trackedEntry.AudioStatus = ImportStatus.Ready;
            trackedEntry.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            published = true;
        }
        finally
        {
            if (!published) storage.DeleteAudioFile(job.OwnerId, stored.FileName);
        }

        if (previousFile is not null && !string.Equals(previousFile, stored.FileName, StringComparison.OrdinalIgnoreCase))
        {
            storage.DeleteAudioFile(job.OwnerId, previousFile);
        }
    }

    private async Task FailAsync(Guid id, Guid? lease, string code, bool retryable, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var job = await db.AudioDownloadJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.LeaseOwner == lease && x.Status == ImportStatus.Running, ct);
        if (job is null) return;

        var now = clock.GetUtcNow();
        var failures = job.Failures + 1;
        var willRetry = retryable && failures < options.Value.EffectiveMaxAttempts;
        var nextAttempt = willRetry
            ? now.AddSeconds(Math.Min(options.Value.EffectiveMaxBackoffSeconds,
                Math.Pow(2, failures) + Random.Shared.NextDouble() * 2))
            : job.NextAttemptAt;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Acquire the job write lock first, matching cancellation's lock order. A stale
        // failure handler must not change the song after its lease has been revoked.
        var changed = await db.AudioDownloadJobs
            .Where(x => x.Id == id && x.OwnerId == job.OwnerId &&
                x.LeaseOwner == lease && x.Status == ImportStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Failures, failures)
                .SetProperty(x => x.ErrorCode, code)
                .SetProperty(x => x.LeaseOwner, (Guid?)null)
                .SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(x => x.Status, willRetry ? ImportStatus.Queued : ImportStatus.Failed)
                .SetProperty(x => x.NextAttemptAt, nextAttempt)
                .SetProperty(x => x.CompletedAt, willRetry ? (DateTimeOffset?)null : now), ct);
        if (changed == 0) return;

        if (!willRetry)
        {
            await db.UserLibrarySongs
                .Where(x => x.Id == job.UserLibrarySongId && x.UserId == job.OwnerId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.AudioStatus,
                        x => x.AudioFileName == null ? ImportStatus.Failed : ImportStatus.Ready)
                    .SetProperty(x => x.UpdatedAt, now), ct);
        }

        await tx.CommitAsync(ct);
    }
}
