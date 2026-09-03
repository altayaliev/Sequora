using Microsoft.Extensions.Logging;

namespace Sequora.Internal;

internal static partial class SequoraLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "No handler is registered for job type {JobType}. The job was not executed.")]
    public static partial void HandlerNotFound(ILogger logger, Exception exception, Type jobType);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Job of type {JobType} failed after {Attempt} attempt(s). The worker will continue processing subsequent jobs.")]
    public static partial void JobFailed(ILogger logger, Exception exception, Type jobType, int attempt);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Job of type {JobType} failed on attempt {Attempt}. Retry {Retry} of {RetryCount} will run after {RetryDelay}.")]
    public static partial void JobRetry(
        ILogger logger,
        Exception exception,
        Type jobType,
        int attempt,
        int retry,
        int retryCount,
        TimeSpan retryDelay);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "Retry of job type {JobType} was canceled during the delay. The job will not be retried.")]
    public static partial void RetryCanceled(ILogger logger, Type jobType);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Error,
        Message = "A worker encountered an unexpected error and will continue processing subsequent jobs.")]
    public static partial void WorkerUnexpectedError(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Debug,
        Message = "Sequora started {WorkerCount} worker(s).")]
    public static partial void WorkersStarted(ILogger logger, int workerCount);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Error,
        Message = "Delayed job scheduler failed for job type {JobType}.")]
    public static partial void DelayedSchedulerFailed(ILogger logger, Exception exception, Type jobType);
}
