using System.Collections.Concurrent;
using TMusic.Infrastructure.Downloads;
using Xunit;

namespace TMusic.Tests;

public sealed class AudioDownloadQueueRunnerTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(20, 2)]
    public void QueueConcurrencyIsAlwaysBoundedPerProcess(int configured, int expected)
        => Assert.Equal(expected, new AudioDownloadOptions { Concurrency = configured }.EffectiveConcurrency);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 3)]
    [InlineData(20, 5)]
    public void RetryAttemptsAreBounded(int configured, int expected)
        => Assert.Equal(expected, new AudioDownloadOptions { MaxAttempts = configured }.EffectiveMaxAttempts);

    [Fact]
    public async Task RunnerExecutesJobsConcurrentlyWithoutExceedingConfiguredSlots()
    {
        var next = 0;
        var active = 0;
        var peak = 0;
        var processed = new ConcurrentBag<int>();
        async Task<bool> ProcessOne(CancellationToken ct)
        {
            var work = Interlocked.Increment(ref next);
            if (work > 6) return false;
            var concurrent = Interlocked.Increment(ref active);
            InterlockedExtensions.Max(ref peak, concurrent);
            try
            {
                await Task.Delay(20, ct);
                processed.Add(work);
                return true;
            }
            finally { Interlocked.Decrement(ref active); }
        }

        var didWork = await AudioDownloadQueueRunner.ProcessAvailableAsync(ProcessOne, 2, CancellationToken.None);

        Assert.True(didWork);
        Assert.InRange(peak, 2, 2);
        Assert.Equal(6, processed.Count);
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            var current = Volatile.Read(ref location);
            while (value > current)
            {
                var observed = Interlocked.CompareExchange(ref location, value, current);
                if (observed == current) return;
                current = observed;
            }
        }
    }
}
