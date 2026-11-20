using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TMusic.Application.Downloads;

namespace TMusic.Infrastructure.Downloads;

public sealed record MediaDownloadDiagnosticEntry(
    string CorrelationId,
    DateTimeOffset TimestampUtc,
    string Provider,
    string Engine,
    string Stage,
    string? ErrorCode,
    DownloadErrorCategory Category,
    DownloadFailureScope Scope,
    bool Retryable,
    string EnvironmentInfo,
    string SanitizedMessage);

public sealed class SourceAccessCircuitBreaker(
    TimeProvider clock,
    IOptions<AudioDownloadOptions> options)
{
    private sealed class ProviderBreakerState
    {
        public int ConsecutiveAccessChecks;
        public DateTimeOffset? OpenUntilUtc;
    }

    private readonly ConcurrentDictionary<string, ProviderBreakerState> states = new(StringComparer.OrdinalIgnoreCase);

    public bool IsOpen(string provider, out DateTimeOffset? openUntilUtc)
    {
        openUntilUtc = null;
        if (!states.TryGetValue(provider, out var state)) return false;
        lock (state)
        {
            if (state.OpenUntilUtc is { } until && until > clock.GetUtcNow())
            {
                openUntilUtc = until;
                return true;
            }
            if (state.OpenUntilUtc is not null)
            {
                state.OpenUntilUtc = null;
                state.ConsecutiveAccessChecks = 0;
            }
            return false;
        }
    }

    public void RecordSuccess(string provider)
    {
        if (!states.TryGetValue(provider, out var state)) return;
        lock (state)
        {
            state.ConsecutiveAccessChecks = 0;
            state.OpenUntilUtc = null;
        }
    }

    public bool RecordAccessCheckFailure(string provider)
    {
        var cooldownSeconds = options.Value.EffectiveAccessCheckCooldownSeconds;
        var threshold = options.Value.EffectiveAccessCheckCircuitThreshold;
        if (cooldownSeconds <= 0 || threshold <= 0) return false;

        var state = states.GetOrAdd(provider, _ => new ProviderBreakerState());
        lock (state)
        {
            var now = clock.GetUtcNow();
            if (state.OpenUntilUtc is { } until && until <= now)
            {
                state.OpenUntilUtc = null;
                state.ConsecutiveAccessChecks = 0;
            }

            state.ConsecutiveAccessChecks++;
            if (state.ConsecutiveAccessChecks >= threshold)
            {
                state.OpenUntilUtc = now.AddSeconds(cooldownSeconds);
                return true;
            }
            return false;
        }
    }
}

public sealed class MediaDownloadDiagnostics(
    ILogger<MediaDownloadDiagnostics> logger,
    TimeProvider clock)
{
    private const int MaxRetainedEntries = 100;
    private readonly ConcurrentQueue<MediaDownloadDiagnosticEntry> recent = new();

    public MediaDownloadDiagnosticEntry RecordFailure(
        string correlationId,
        string provider,
        string engine,
        string stage,
        DownloadErrorClassification classification)
    {
        var entry = new MediaDownloadDiagnosticEntry(
            CorrelationId: correlationId,
            TimestampUtc: clock.GetUtcNow(),
            Provider: provider,
            Engine: engine,
            Stage: stage,
            ErrorCode: classification.ErrorCode,
            Category: classification.Category,
            Scope: classification.Scope,
            Retryable: classification.Retryable,
            EnvironmentInfo: AudioTempWorkspace.EnvironmentSummary(),
            SanitizedMessage: classification.DiagnosticSummary);

        Enqueue(entry);
        logger.LogWarning(
            "Media download failed [CorrelationId={CorrelationId}] [Provider={Provider}] [Engine={Engine}] [Stage={Stage}] [Code={ErrorCode}] [Category={Category}] [Scope={Scope}] [Retryable={Retryable}] [Env={EnvironmentInfo}]",
            entry.CorrelationId, entry.Provider, entry.Engine, entry.Stage, entry.ErrorCode,
            entry.Category, entry.Scope, entry.Retryable, entry.EnvironmentInfo);
        return entry;
    }

    public MediaDownloadDiagnosticEntry RecordSuccess(
        string correlationId,
        string provider,
        string engine,
        long outputSizeBytes)
    {
        var entry = new MediaDownloadDiagnosticEntry(
            CorrelationId: correlationId,
            TimestampUtc: clock.GetUtcNow(),
            Provider: provider,
            Engine: engine,
            Stage: "COMPLETED",
            ErrorCode: null,
            Category: DownloadErrorCategory.None,
            Scope: DownloadFailureScope.Unknown,
            Retryable: false,
            EnvironmentInfo: AudioTempWorkspace.EnvironmentSummary(),
            SanitizedMessage: $"Audio extraction completed ({outputSizeBytes} bytes).");

        Enqueue(entry);
        logger.LogInformation(
            "Media download succeeded [CorrelationId={CorrelationId}] [Provider={Provider}] [Engine={Engine}] [Bytes={Bytes}] [Env={EnvironmentInfo}]",
            entry.CorrelationId, entry.Provider, entry.Engine, outputSizeBytes, entry.EnvironmentInfo);
        return entry;
    }

    public IReadOnlyList<MediaDownloadDiagnosticEntry> RecentSnapshot() => recent.ToArray();

    private void Enqueue(MediaDownloadDiagnosticEntry entry)
    {
        recent.Enqueue(entry);
        while (recent.Count > MaxRetainedEntries && recent.TryDequeue(out _)) { }
    }
}
