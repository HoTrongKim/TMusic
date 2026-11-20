using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TMusic.Infrastructure.Downloads;
using Xunit;

namespace TMusic.Tests;

public sealed class AuthorizedAudioExtractionFactAttribute : FactAttribute
{
    public AuthorizedAudioExtractionFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TMUSIC_LIVE_AUDIO_TEST_URL")))
            Skip = "Set TMUSIC_LIVE_AUDIO_TEST_URL to a source that is public and authorized for audio download.";
    }
}

public sealed class AuthorizedAudioExtractionIntegrationTests
{
    [AuthorizedAudioExtractionFact]
    public async Task AuthorizedSourceCanBeExtractedAndConvertedToMp3()
    {
        var url = Environment.GetEnvironmentVariable("TMUSIC_LIVE_AUDIO_TEST_URL")!;
        var workDir = Path.Combine(Path.GetTempPath(), "tmusic-authorized-audio-" + Guid.NewGuid().ToString("N"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:EnableAudioDownloads"] = "true",
            ["AudioDownload:TimeoutSeconds"] = "300",
            ["AudioDownload:MaxBackoffSeconds"] = "1"
        }).Build();
        var extractor = new YtDlpAudioExtractor(config,
            Options.Create(new AudioDownloadOptions { TimeoutSeconds = 300 }));

        try
        {
            Assert.True(await extractor.ValidateToolsAsync(CancellationToken.None));
            var output = await extractor.ExtractMp3Async(url, workDir,
                (_, _) => Task.CompletedTask, 192, CancellationToken.None);
            Assert.True(File.Exists(output));
            Assert.EndsWith(".mp3", output, StringComparison.OrdinalIgnoreCase);
            var info = new FileInfo(output);
            Assert.InRange(info.Length, 1024, 500L * 1024 * 1024);
            var header = new byte[3];
            await using var stream = File.OpenRead(output);
            Assert.Equal(3, await stream.ReadAsync(header));
            Assert.True(header.SequenceEqual("ID3"u8.ToArray()) || (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0),
                "The converted output did not have an MP3 frame or ID3 header.");

            if (double.TryParse(Environment.GetEnvironmentVariable("TMUSIC_LIVE_AUDIO_EXPECTED_DURATION_SECONDS"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedDuration))
            {
                var ffprobe = extractor.ResolveFfprobePath();
                Assert.False(string.IsNullOrWhiteSpace(ffprobe), "ffprobe is required to verify extracted MP3 duration.");
                var probe = await BoundedAudioProcess.RunAsync(ffprobe!,
                    ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", output],
                    workDir, null, CancellationToken.None, TimeSpan.FromSeconds(15), captureStandardOutput: true);
                Assert.Equal(0, probe.ExitCode);
                Assert.True(double.TryParse(probe.StandardOutput.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var actualDuration),
                    $"Unexpected ffprobe duration output: {probe.StandardOutput}");
                Assert.InRange(actualDuration, expectedDuration - 2.0, expectedDuration + 2.0);
            }
        }
        finally
        {
            if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
        }
    }
}
