using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using TMusic.Application.Providers;
using TMusic.Infrastructure.Storage;

namespace TMusic.Infrastructure.Downloads;

public static class AudioExtractionCapacity
{
    private static readonly SemaphoreSlim Slots = new(2, 2);

    public static IDisposable Acquire()
    {
        if (!Slots.Wait(0)) throw new ProviderException("AUDIO_DOWNLOAD_BUSY", retryable: true);
        return new Lease();
    }

    private sealed class Lease : IDisposable
    {
        private int released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) Slots.Release();
        }
    }
}

public static class AudioBitrate
{
    public static readonly int[] Supported = [128, 192, 320];
    public static bool IsValid(int? bitrateKbps) => bitrateKbps is null or 128 or 192 or 320;
}

public static class LocalMp3Converter
{
    public static async Task ConvertAsync(string ffmpegExecutable, string sourceFile, string outputFile,
        CancellationToken ct, int? bitrateKbps = null, TimeSpan? timeout = null)
    {
        if (!AudioBitrate.IsValid(bitrateKbps))
            throw new ProviderException("INVALID_BITRATE");
        sourceFile = Path.GetFullPath(sourceFile);
        outputFile = Path.GetFullPath(outputFile);
        if (string.Equals(sourceFile, outputFile, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(sourceFile) || new FileInfo(sourceFile).Length is <= 16 or > AudioStorage.MaxAudioBytes)
            throw new ProviderException("AUDIO_INPUT_INVALID");

        // No ffprobe or yt-dlp postprocessor handles downloaded bytes. Restrict both protocols
        // and demuxers before opening the input: playlist/concat containers cannot read URLs or
        // additional local files. Only this explicitly constrained FFmpeg process decodes media.
        var qualityFlag = bitrateKbps is { } kbps ? "-b:a" : "-q:a";
        var qualityValue = bitrateKbps is { } b ? $"{b}k" : "2";
        var result = await BoundedAudioProcess.RunAsync(ffmpegExecutable,
        [
            "-nostdin", "-hide_banner", "-loglevel", "error", "-max_alloc", "67108864",
            "-protocol_whitelist", "file,pipe",
            "-format_whitelist", "mov,matroska,webm,mpegts,mp3,aac,ogg,flac,wav",
            "-probesize", "5242880", "-analyzeduration", "5000000", "-threads", "2",
            "-i", sourceFile,
            "-map", "0:a:0", "-vn", "-sn", "-dn", "-map_metadata", "-1", "-map_chapters", "-1",
            "-c:a", "libmp3lame", qualityFlag, qualityValue, "-threads", "2",
            "-fs", AudioStorage.MaxAudioBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-f", "mp3", "-y", outputFile
        ], Path.GetDirectoryName(sourceFile)!, null, ct, timeout);
        if (result.ExitCode != 0 || !File.Exists(outputFile) || new FileInfo(outputFile).Length is <= 16 or > AudioStorage.MaxAudioBytes)
            throw new ProviderException("AUDIO_CONVERSION_FAILED");
    }
}

public sealed record AudioProcessResult(int ExitCode, string StandardError, string StandardOutput = "");

public static class BoundedAudioProcess
{
    public static async Task<AudioProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        string workingDirectory, Func<string, Task>? onOutputLine, CancellationToken ct,
        TimeSpan? timeout = null, bool captureStandardOutput = false)
    {
        ct.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo
        {
            FileName = executable, WorkingDirectory = workingDirectory, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        foreach (var key in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "all_proxy", "no_proxy" })
            start.Environment.Remove(key);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(AudioDownloadOptions.DefaultTimeoutSeconds));
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new ProviderException("AUDIO_ENGINE_UNAVAILABLE");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Missing execute permission or a damaged binary is a terminal engine error.
            // Do not leak the executable path or leave a worker lease retrying indefinitely.
            throw new ProviderException("AUDIO_ENGINE_UNAVAILABLE");
        }
        process.StandardInput.Close();
        var output = ReadAsync(process.StandardOutput, capture: captureStandardOutput, onOutputLine, deadline);
        var error = ReadAsync(process.StandardError, capture: true, null, deadline);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(deadline.Token), output, error);
            return new AudioProcessResult(process.ExitCode, await error, await output);
        }
        catch
        {
            await deadline.CancelAsync();
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception e) when (e is TimeoutException or InvalidOperationException) { }
            foreach (var task in new[] { output, error })
            {
                if (task.Exception?.InnerException is { } failure && failure is not OperationCanceledException)
                    ExceptionDispatchInfo.Capture(failure).Throw();
            }
            throw;
        }
    }

    private static async Task<string> ReadAsync(StreamReader reader, bool capture, Func<string, Task>? onLine, CancellationTokenSource stop)
    {
        var captured = capture ? new StringBuilder() : null;
        var line = onLine is not null ? new StringBuilder() : null;
        var buffer = new char[2048];
        var total = 0;
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(), stop.Token);
                if (read == 0) break;
                total += read;
                if (total > 128 * 1024) throw new ProviderException("AUDIO_ENGINE_OUTPUT_LIMIT");
                captured?.Append(buffer, 0, read);
                if (line is null) continue;
                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] is '\r' or '\n')
                    {
                        if (line.Length > 0) { await onLine!(line.ToString()); line.Clear(); }
                    }
                    else
                    {
                        line.Append(buffer[i]);
                        if (line.Length > 16 * 1024) throw new ProviderException("AUDIO_ENGINE_OUTPUT_LIMIT");
                    }
                }
            }
            if (line?.Length > 0) await onLine!(line.ToString());
            return captured?.ToString() ?? "";
        }
        catch { await stop.CancelAsync(); throw; }
    }
}
