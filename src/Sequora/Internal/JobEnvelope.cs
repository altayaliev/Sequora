namespace Sequora.Internal;

internal sealed class JobEnvelope
{
    public JobEnvelope(
        object job,
        Type jobType,
        string? jobId,
        int retryCount,
        TimeSpan retryDelay,
        RetryBackoffStrategy retryBackoff,
        TimeSpan maxRetryDelay,
        int priority,
        TimeSpan delay,
        JobLifecycle lifecycle,
        Func<IServiceProvider, CancellationToken, Task> executeAsync)
    {
        Job = job;
        JobType = jobType;
        JobId = jobId;
        RetryCount = retryCount;
        RetryDelay = retryDelay;
        RetryBackoff = retryBackoff;
        MaxRetryDelay = maxRetryDelay;
        Priority = priority;
        Delay = delay;
        Lifecycle = lifecycle;
        ExecuteAsync = executeAsync;
    }

    public object Job { get; }

    public Type JobType { get; }

    public string? JobId { get; }

    public int RetryCount { get; }

    public TimeSpan RetryDelay { get; }

    public RetryBackoffStrategy RetryBackoff { get; }

    public TimeSpan MaxRetryDelay { get; }

    public int Priority { get; }

    public TimeSpan Delay { get; }

    public long Sequence { get; set; }

    public JobLifecycle Lifecycle { get; }

    public Func<IServiceProvider, CancellationToken, Task> ExecuteAsync { get; }
}
