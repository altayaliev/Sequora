namespace Sequora.Internal;

internal sealed class JobLifecycle
{
    private readonly object _gate = new();
    private readonly List<JobLifecycleState> _history;
    private JobLifecycleState _state;

    public JobLifecycle()
        : this(JobLifecycleState.Queued)
    {
    }

    public JobLifecycle(JobLifecycleState initial)
    {
        _state = initial;
        _history = [initial];
    }

    public JobLifecycleState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public JobLifecycleState[] SnapshotHistory()
    {
        lock (_gate)
        {
            return [.. _history];
        }
    }

    public void MoveTo(JobLifecycleState state)
    {
        lock (_gate)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
            _history.Add(state);
        }
    }
}
