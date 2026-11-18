using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using TMusic.Application.Providers;
using TMusic.Infrastructure.Downloads;
using TMusic.Infrastructure.Storage;
using Xunit;

namespace TMusic.Tests;

public sealed class LocalAudioConversionTests
{
    [Fact]
    public async Task ExtractionCapacityRejectsThirdJobAndReleasesDisposedSlot()
    {
        using var first = AudioExtractionCapacity.Acquire();
        using var second = AudioExtractionCapacity.Acquire();
        var busy = Assert.Throws<ProviderException>(() => AudioExtractionCapacity.Acquire());
        Assert.Equal("AUDIO_DOWNLOAD_BUSY", busy.Code);
        Assert.True(busy.Retryable);
        var extractor = new YtDlpAudioExtractor(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Features:EnableAudioDownloads"] = "true" }).Build());
        var rejected = await Assert.ThrowsAsync<ProviderException>(() => extractor.ExtractMp3Async(
            "https://www.youtube.com/watch?v=CancelTest1", Path.GetTempPath(), (_, _) => Task.CompletedTask, CancellationToken.None));
        Assert.Equal("AUDIO_DOWNLOAD_BUSY", rejected.Code);
        first.Dispose();
        using var replacement = AudioExtractionCapacity.Acquire();
    }

    [FfmpegFact]
    public async Task GeneratedWavConvertsToValidatedMp3WithoutExternalProbe()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TMusicConversionTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.media");
            await File.WriteAllBytesAsync(source, WavFixture());
            var output = Path.Combine(directory, "track.mp3");
            await LocalMp3Converter.ConvertAsync(Ffmpeg(), source, output, CancellationToken.None);
            var storage = new AudioStorage(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:AudioPath"] = Path.Combine(directory, "stored")
            }).Build());
            var saved = await storage.SaveFromTempFileAsync(Guid.NewGuid(), output, CancellationToken.None);
            Assert.True(saved.SizeBytes > 100);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [FfmpegFact]
    public async Task GeneratedM4aContainerConvertsWithRestrictedDemuxers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TMusicConversionTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var wav = Path.Combine(directory, "fixture.wav");
            await File.WriteAllBytesAsync(wav, WavFixture());
            var source = Path.Combine(directory, "source.media");
            var wrapped = await BoundedAudioProcess.RunAsync(Ffmpeg(),
                ["-nostdin", "-hide_banner", "-loglevel", "error", "-i", wav, "-c:a", "aac", "-f", "ipod", "-y", source],
                directory, null, CancellationToken.None);
            Assert.Equal(0, wrapped.ExitCode);
            var output = Path.Combine(directory, "track.mp3");
            await LocalMp3Converter.ConvertAsync(Ffmpeg(), source, output, CancellationToken.None);
            Assert.True(new FileInfo(output).Length > 100);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [FfmpegFact]
    public async Task ChildProcessOutputLimitStopsExcessiveOutput()
    {
        var error = await Assert.ThrowsAsync<ProviderException>(() => BoundedAudioProcess.RunAsync(Ffmpeg(),
            ["-hide_banner", "-h", "full"], Path.GetTempPath(), null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("AUDIO_ENGINE_OUTPUT_LIMIT", error.Code);
    }

    [FfmpegFact]
    public async Task MaliciousDownloadedPlaylistCannotOpenNetworkDuringConversion()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TMusicConversionTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var honeypot = new TcpListener(IPAddress.Loopback, 0);
        honeypot.Start();
        try
        {
            var port = ((IPEndPoint)honeypot.LocalEndpoint).Port;
            var source = Path.Combine(directory, "source.media");
            await File.WriteAllTextAsync(source,
                $"#EXTM3U\n#EXT-X-TARGETDURATION:10\n#EXTINF:10,\nhttp://127.0.0.1:{port}/private.ts\n#EXT-X-ENDLIST\n");
            var error = await Assert.ThrowsAsync<ProviderException>(() => LocalMp3Converter.ConvertAsync(
                Ffmpeg(), source, Path.Combine(directory, "track.mp3"), CancellationToken.None));
            Assert.Equal("AUDIO_CONVERSION_FAILED", error.Code);
            Assert.False(honeypot.Pending());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [FfmpegFact]
    public async Task ConversionArgumentsConstrainInputBeforeOpeningDownloadedContainer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TMusicConversionTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var privateWav = Path.Combine(directory, "private.wav");
            await File.WriteAllBytesAsync(privateWav, WavFixture());
            var source = Path.Combine(directory, "source.media");
            await File.WriteAllTextAsync(source, "ffconcat version 1.0\nfile 'private.wav'\n");
            await Assert.ThrowsAsync<ProviderException>(() => LocalMp3Converter.ConvertAsync(
                Ffmpeg(), source, Path.Combine(directory, "track.mp3"), CancellationToken.None));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [FfmpegFact]
    public async Task CancelingRunningChildProcessWaitsForTermination()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TMusicConversionTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var cancel = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = Path.Combine(directory, "infinite-fixture.mp3");
        var running = BoundedAudioProcess.RunAsync(Ffmpeg(),
        [
            "-nostdin", "-hide_banner", "-loglevel", "error", "-re", "-f", "lavfi",
            "-i", "anullsrc=r=8000:cl=mono", "-c:a", "libmp3lame", "-progress", "pipe:1",
            "-f", "mp3", "-y", output
        ], directory, _ => { started.TrySetResult(); return Task.CompletedTask; }, cancel.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await cancel.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(File.Exists(output));
            using var exclusive = new FileStream(output, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            await cancel.CancelAsync();
            try { await running; } catch (OperationCanceledException) { }
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string Ffmpeg() => new YtDlpAudioExtractor(new ConfigurationBuilder().Build()).ResolveFfmpegPath()!;

    private static byte[] WavFixture()
    {
        const int samples = 4000;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000);
        writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(samples * 2);
        for (var i = 0; i < samples; i++) writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / 8000) * 2000));
        return stream.ToArray();
    }
}

public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (new YtDlpAudioExtractor(new ConfigurationBuilder().Build()).ResolveFfmpegPath() is null)
            Skip = "Install FFmpeg to run synthetic local audio conversion tests.";
    }
}
