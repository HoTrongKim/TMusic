using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TMusic.Infrastructure.Downloads;

public sealed class AudioDownloadQueueRunner(
    IServiceScopeFactory scopes,
    IOptions<AudioDownloadOptions> options)
{
    public async Task<bool> ProcessAvailableAsync(CancellationToken ct)
        => await ProcessAvailableAsync(ProcessOneAsync, options.Value.EffectiveConcurrency, ct);

    public static async Task<bool> ProcessAvailableAsync(
        Func<CancellationToken, Task<bool>> processOne,
        int maxConcurrency,
        CancellationToken ct)
    {
        var workers = Enumerable.Range(0, Math.Clamp(maxConcurrency, 1, AudioDownloadOptions.MaximumConcurrency))
            .Select(_ => DrainAsync(processOne, ct));
        return (await Task.WhenAll(workers)).Any(processed => processed);
    }

    private static async Task<bool> DrainAsync(Func<CancellationToken, Task<bool>> processOne, CancellationToken ct)
    {
        var processedAny = false;
        while (await processOne(ct)) processedAny = true;
        return processedAny;
    }

    private async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AudioDownloadProcessor>().ProcessOneAsync(ct);
    }
}
