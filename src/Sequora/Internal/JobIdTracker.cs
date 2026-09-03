using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Sequora.Internal;

/// <summary>
/// Process-local registry of active job ids. An id is registered when enqueue
/// accepts the job and removed when the job reaches a terminal state, so the
/// dictionary cannot grow without bound from completed work.
/// </summary>
internal sealed class JobIdTracker
{
    private readonly ConcurrentDictionary<string, JobLifecycle> _active = new(StringComparer.Ordinal);

    public int Count => _active.Count;

    public bool TryAdd(string jobId, JobLifecycle lifecycle)
        => _active.TryAdd(jobId, lifecycle);

    public bool TryGet(string jobId, [NotNullWhen(true)] out JobLifecycle? lifecycle)
        => _active.TryGetValue(jobId, out lifecycle);

    public bool TryRemove(string jobId)
        => _active.TryRemove(jobId, out _);
}
