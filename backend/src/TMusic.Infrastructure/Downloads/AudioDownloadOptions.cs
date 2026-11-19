namespace TMusic.Infrastructure.Downloads;

public sealed class AudioDownloadOptions
{
    public const int MaximumConcurrency = 2;
    public const int DefaultTimeoutSeconds = 180;
    public const int DefaultLeaseSeconds = 480;

    public int Concurrency { get; set; } = MaximumConcurrency;
    public int MaxAttempts { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
    public int LeaseSeconds { get; set; } = DefaultLeaseSeconds;
    public int MaxBackoffSeconds { get; set; } = 60;

    public int EffectiveConcurrency => Math.Clamp(Concurrency, 1, MaximumConcurrency);
    public int EffectiveMaxAttempts => Math.Clamp(MaxAttempts, 1, 5);
    public TimeSpan EffectiveTimeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 30, 600));
    public TimeSpan EffectiveLease => TimeSpan.FromSeconds(Math.Max(
        Math.Clamp(LeaseSeconds, 120, 1800),
        (int)(EffectiveTimeout.TotalSeconds * 2) + 120));
    public int EffectiveMaxBackoffSeconds => Math.Clamp(MaxBackoffSeconds, 1, 900);
}
