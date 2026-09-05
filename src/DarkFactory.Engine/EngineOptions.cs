namespace DarkFactory.Engine;

public sealed class EngineOptions
{
    public const string SectionName = "Engine";

    public string WorkerId { get; set; } = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():n}";

    /// <summary>How long a claim holds before it's eligible for reclaim if never renewed or released.</summary>
    public double LeaseDurationSeconds { get; set; } = 30;

    /// <summary>How long to wait before polling again after finding nothing claimable.</summary>
    public double PollIntervalSeconds { get; set; } = 2;

    /// <summary>docs/adr/0007: retryable failures retry with backoff, up to this many attempts.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Base for exponential backoff between retry attempts (attempt 1 waits this long, attempt 2 waits 2x, etc.).</summary>
    public double RetryBackoffBaseSeconds { get; set; } = 1;

    public TimeSpan LeaseDuration => TimeSpan.FromSeconds(LeaseDurationSeconds);

    public TimeSpan PollInterval => TimeSpan.FromSeconds(PollIntervalSeconds);

    public TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(RetryBackoffBaseSeconds * Math.Pow(2, attempt - 1));
}
