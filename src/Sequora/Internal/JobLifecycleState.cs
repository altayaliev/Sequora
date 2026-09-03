namespace Sequora.Internal;

/// <summary>
/// In-process lifecycle of a queued job. Not part of the public API.
/// </summary>
internal enum JobLifecycleState
{
    Delayed = 0,
    Queued = 1,
    Processing = 2,
    Retrying = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6
}
