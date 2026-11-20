namespace TMusic.Infrastructure.Downloads;

public sealed class AudioDownloadOptions
{
    public const int MaximumConcurrency = 2;
    public const int DefaultTimeoutSeconds = 300;
    public const int DefaultConversionTimeoutSeconds = 240;
    public const int DefaultLeaseSeconds = 720;

    public int Concurrency { get; set; } = MaximumConcurrency;
    public int MaxActivePerUser { get; set; } = 3;
    public int MaxAttempts { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
    public int ConversionTimeoutSeconds { get; set; } = DefaultConversionTimeoutSeconds;
    public int LeaseSeconds { get; set; } = DefaultLeaseSeconds;
    public int MaxBackoffSeconds { get; set; } = 60;
    public int AccessCheckCircuitThreshold { get; set; } = 2;
    public int AccessCheckCooldownSeconds { get; set; } = 60;

    public int EffectiveConcurrency => Math.Clamp(Concurrency, 1, MaximumConcurrency);
    public int EffectiveMaxActivePerUser => Math.Clamp(MaxActivePerUser, 1, 10);
    public int EffectiveMaxAttempts => Math.Clamp(MaxAttempts, 1, 5);
    public TimeSpan EffectiveTimeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 30, 900));
    public TimeSpan EffectiveConversionTimeout => TimeSpan.FromSeconds(Math.Clamp(ConversionTimeoutSeconds, 30, 600));
    public TimeSpan EffectiveLease => TimeSpan.FromSeconds(Math.Max(
        Math.Clamp(LeaseSeconds, 120, 2400),
        (int)(EffectiveTimeout.TotalSeconds + EffectiveConversionTimeout.TotalSeconds) + 120));
    public int EffectiveMaxBackoffSeconds => Math.Clamp(MaxBackoffSeconds, 1, 900);
    public int EffectiveAccessCheckCircuitThreshold => Math.Clamp(AccessCheckCircuitThreshold, 0, 20);
    public int EffectiveAccessCheckCooldownSeconds => Math.Clamp(AccessCheckCooldownSeconds, 0, 900);
}
