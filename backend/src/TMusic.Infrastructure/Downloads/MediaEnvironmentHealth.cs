using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using TMusic.Application.Downloads;

namespace TMusic.Infrastructure.Downloads;

public sealed record MediaDependencyHealthReport(
    string Status,
    string YtDlp,
    string Ffmpeg,
    string Ffprobe,
    string JavascriptRuntime,
    string Ejs,
    ExternalProviderStatus ExternalProvider,
    DateTimeOffset CheckedAtUtc,
    string Note);

public sealed class MediaDependencyHealthService(
    IConfiguration config,
    YtDlpAudioExtractor ytDlpExtractor,
    IExternalAudioProvider externalProvider,
    TimeProvider clock)
{
    public const string DefaultLinuxBundlePath = "/usr/local/lib/tmusic/yt-dlp.pyz";

    public async Task<MediaDependencyHealthReport> CheckAsync(CancellationToken ct)
    {
        var enabled = config.GetValue("Features:EnableAudioDownloads", false);
        var extStatus = externalProvider.GetProviderStatus();
        if (!enabled)
        {
            return new MediaDependencyHealthReport(
                Status: "disabled",
                YtDlp: "disabled",
                Ffmpeg: "disabled",
                Ffprobe: "disabled",
                JavascriptRuntime: "disabled",
                Ejs: "disabled",
                ExternalProvider: extStatus,
                CheckedAtUtc: clock.GetUtcNow(),
                Note: "Audio download feature flag (Features:EnableAudioDownloads) is disabled.");
        }

        var ytDlpPath = ytDlpExtractor.ResolveYtDlpPath();
        var ffmpegPath = ytDlpExtractor.ResolveFfmpegPath();
        var ffprobePath = ytDlpExtractor.ResolveFfprobePath();
        var denoPath = ytDlpExtractor.ResolveDenoPath();

        var ytDlpState = await CheckYtDlpAsync(ytDlpPath, ct);
        var ffmpegState = await CheckBinaryBannerAsync(ffmpegPath, ["-version"], "ffmpeg version", ct);
        var ffprobeState = await CheckBinaryBannerAsync(ffprobePath, ["-version"], "ffprobe version", ct);
        var jsState = await CheckDenoAsync(denoPath, ct);
        var ejsState = await CheckEjsAsync(ytDlpPath, ytDlpState == "available", ct);

        var allHealthy = ytDlpState == "available" &&
                         ffmpegState == "available" &&
                         ffprobeState == "available" &&
                         jsState == "available" &&
                         ejsState == "available";

        var status = allHealthy
            ? "healthy"
            : (ytDlpState == "available" && ffmpegState == "available" ? "degraded" : "unhealthy");

        return new MediaDependencyHealthReport(
            Status: status,
            YtDlp: ytDlpState,
            Ffmpeg: ffmpegState,
            Ffprobe: ffprobeState,
            JavascriptRuntime: jsState,
            Ejs: ejsState,
            ExternalProvider: extStatus,
            CheckedAtUtc: clock.GetUtcNow(),
            Note: "Dependency health verifies local runtime tools only; external provider access checks (e.g. YouTube bot verification) are controlled upstream.");
    }

    private async Task<string> CheckYtDlpAsync(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path)) return "missing";
        try
        {
            var result = await BoundedAudioProcess.RunAsync(path, ["--version"], Path.GetTempPath(), null, ct,
                TimeSpan.FromSeconds(15), captureStandardOutput: true);
            if (result.ExitCode != 0) return "unhealthy";
            var expected = config["AudioDownload:YtDlpVersion"];
            if (!string.IsNullOrWhiteSpace(expected) &&
                !string.Equals(result.StandardOutput.Trim(), expected.Trim(), StringComparison.Ordinal))
                return "version_mismatch";
            return "available";
        }
        catch
        {
            return "unhealthy";
        }
    }

    private async Task<string> CheckDenoAsync(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path)) return "missing";
        try
        {
            var result = await BoundedAudioProcess.RunAsync(path, ["--version"], Path.GetTempPath(), null, ct,
                TimeSpan.FromSeconds(15), captureStandardOutput: true);
            if (result.ExitCode != 0) return "unhealthy";
            var expected = config["AudioDownload:DenoVersion"];
            if (!string.IsNullOrWhiteSpace(expected) &&
                !result.StandardOutput.StartsWith($"deno {expected.Trim()}", StringComparison.Ordinal))
                return "version_mismatch";
            return result.StandardOutput.StartsWith("deno ", StringComparison.OrdinalIgnoreCase)
                ? "available"
                : "unhealthy";
        }
        catch
        {
            return "unhealthy";
        }
    }

    private static async Task<string> CheckBinaryBannerAsync(string? path, string[] args, string marker, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path)) return "missing";
        try
        {
            var result = await BoundedAudioProcess.RunAsync(path, args, Path.GetTempPath(), null, ct,
                TimeSpan.FromSeconds(15), captureStandardOutput: true);
            if (result.ExitCode != 0) return "unhealthy";
            return result.StandardOutput.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                   result.StandardError.Contains(marker, StringComparison.OrdinalIgnoreCase)
                ? "available"
                : "unhealthy";
        }
        catch
        {
            return "unhealthy";
        }
    }

    private async Task<string> CheckEjsAsync(string? ytDlpPath, bool ytDlpAvailable, CancellationToken ct)
    {
        if (!ytDlpAvailable || string.IsNullOrWhiteSpace(ytDlpPath)) return "unavailable";

        // Fast offline inspection for the pinned Linux zipapp bundle when present.
        var bundlePath = config["AudioDownload:YtDlpBundlePath"] ?? DefaultLinuxBundlePath;
        if (File.Exists(bundlePath) && ZipContainsEjs(bundlePath))
            return "available";

        if (ZipContainsEjs(ytDlpPath))
            return "available";

        // Offline verbose probe: prints "[debug] Optional libraries: ... yt_dlp_ejs-..." without any network access.
        try
        {
            var probe = await BoundedAudioProcess.RunAsync(
                ytDlpPath, ["-v", "--ignore-config"], Path.GetTempPath(), null, ct,
                TimeSpan.FromSeconds(15), captureStandardOutput: true);
            var combined = probe.StandardError + "\n" + probe.StandardOutput;
            if (combined.Contains("yt_dlp_ejs", StringComparison.OrdinalIgnoreCase))
                return "available";
            return "missing";
        }
        catch
        {
            return "unavailable";
        }
    }

    internal static bool ZipContainsEjs(string filePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            return archive.Entries.Any(e => e.FullName.Contains("yt_dlp_ejs", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}

public static class AudioTempWorkspace
{
    public static readonly string[] TempPrefixes = ["tmusic_dl_", "tmusic_guest_", "tmusic-audio-"];

    public static int CleanupStaleDirectories(TimeSpan maxAge, TimeProvider? clock = null, string? rootOverride = null)
    {
        var root = rootOverride ?? Path.GetTempPath();
        if (!Directory.Exists(root)) return 0;
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var cutoff = now - maxAge;
        var deleted = 0;

        foreach (var prefix in TempPrefixes)
        {
            IEnumerable<string> dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(root, prefix + "*");
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var dir in dirs)
            {
                try
                {
                    var info = new DirectoryInfo(dir);
                    if (info.LastWriteTimeUtc <= cutoff.UtcDateTime)
                    {
                        info.Delete(recursive: true);
                        deleted++;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        return deleted;
    }

    public static string EnvironmentSummary()
    {
        var railway = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RAILWAY_ENVIRONMENT")) ||
                      !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RAILWAY_PROJECT_ID"));
        var region = Environment.GetEnvironmentVariable("RAILWAY_REPLICA_REGION");
        var hostTag = railway
            ? (string.IsNullOrWhiteSpace(region) ? "railway" : $"railway:{region}")
            : "local";
        return $"{hostTag}/{RuntimeInformation.OSArchitecture}/{Environment.OSVersion.Platform}";
    }
}
