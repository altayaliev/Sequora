using System.Diagnostics.CodeAnalysis;

namespace Sequora.Internal;

/// <summary>
/// In-memory ready queue. Default priority 0 is FIFO. Higher priority is
/// dequeued first; equal priorities keep enqueue order. After
/// <see cref="SequoraOptions.PriorityFairnessLimit"/> consecutive skips of an
/// older lower-priority job, that oldest job is dequeued next.
/// </summary>
/// <remarks>
/// Selection is backed by two binary heaps that share the same envelopes: one
/// ordered by priority (ties broken by age) for "the best job to run", one
/// ordered by age for "the oldest job waiting". Both give O(log n) peek/pop.
/// A job removed through one heap is lazily discarded from the other the next
/// time it would surface at that heap's root, tracked via <see cref="_present"/>.
/// </remarks>
internal sealed class ReadyQueue
{
    private readonly object _gate = new();
    private readonly BinaryHeap<JobEnvelope> _priorityHeap;
    private readonly BinaryHeap<JobEnvelope> _ageHeap;
    private readonly HashSet<JobEnvelope> _present = new(ReferenceEqualityComparer.Instance);
    private readonly List<TaskCompletionSource<bool>> _waiters = [];
    private readonly int _fairnessLimit;
    private long _sequence;
    private int _highPriorityStreak;
    private bool _completed;

    public ReadyQueue(int fairnessLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fairnessLimit);
        _fairnessLimit = fairnessLimit;
        _priorityHeap = new BinaryHeap<JobEnvelope>(ComparePriority);
        _ageHeap = new BinaryHeap<JobEnvelope>(CompareAge);
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _present.Count;
            }
        }
    }

    public bool TryEnqueue(JobEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        TaskCompletionSource<bool>? waiter;
        lock (_gate)
        {
            if (_completed)
            {
                return false;
            }

            envelope.Sequence = ++_sequence;
            _priorityHeap.Push(envelope);
            _ageHeap.Push(envelope);
            _present.Add(envelope);
            waiter = DequeueWaiterLocked();
        }

        waiter?.TrySetResult(true);
        return true;
    }

    public async ValueTask<JobEnvelope?> DequeueAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TaskCompletionSource<bool>? waiter = null;
            lock (_gate)
            {
                if (_present.Count > 0)
                {
                    return SelectLocked();
                }

                if (_completed)
                {
                    return null;
                }

                waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add(waiter);
            }

            using CancellationTokenRegistration registration = cancellationToken.Register(
                static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
                waiter);

            try
            {
                await waiter.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                lock (_gate)
                {
                    _waiters.Remove(waiter);
                }

                cancellationToken.ThrowIfCancellationRequested();
                throw new OperationCanceledException(cancellationToken);
            }
        }
    }

    public bool TryPeek([NotNullWhen(true)] out JobEnvelope? envelope)
    {
        lock (_gate)
        {
            if (_present.Count == 0)
            {
                envelope = null;
                return false;
            }

            envelope = PreviewLocked();
            return envelope is not null;
        }
    }

    public bool TryDequeue([NotNullWhen(true)] out JobEnvelope? envelope)
    {
        lock (_gate)
        {
            if (_present.Count == 0)
            {
                envelope = null;
                return false;
            }

            envelope = SelectLocked();
            return envelope is not null;
        }
    }

    public void Complete()
    {
        List<TaskCompletionSource<bool>> waiters;
        lock (_gate)
        {
            _completed = true;
            waiters = [.. _waiters];
            _waiters.Clear();
        }

        foreach (TaskCompletionSource<bool> waiter in waiters)
        {
            waiter.TrySetResult(true);
        }
    }

    private TaskCompletionSource<bool>? DequeueWaiterLocked()
    {
        if (_waiters.Count == 0)
        {
            return null;
        }

        TaskCompletionSource<bool> waiter = _waiters[0];
        _waiters.RemoveAt(0);
        return waiter;
    }

    private JobEnvelope? PreviewLocked()
        => PeekBestLocked();

    private JobEnvelope? SelectLocked()
    {
        JobEnvelope? best = PeekBestLocked();
        if (best is null)
        {
            return null;
        }

        JobEnvelope? oldest = PeekOldestLocked();

        JobEnvelope chosen;
        if (_fairnessLimit == 0
            || ReferenceEquals(best, oldest)
            || oldest!.Priority >= best.Priority)
        {
            _highPriorityStreak = 0;
            chosen = best;
        }
        else if (_highPriorityStreak >= _fairnessLimit)
        {
            _highPriorityStreak = 0;
            chosen = oldest;
        }
        else
        {
            _highPriorityStreak++;
            chosen = best;
        }

        _present.Remove(chosen);
        return chosen;
    }

    private JobEnvelope? PeekBestLocked()
        => PeekLiveTopLocked(_priorityHeap);

    private JobEnvelope? PeekOldestLocked()
        => PeekLiveTopLocked(_ageHeap);

    private JobEnvelope? PeekLiveTopLocked(BinaryHeap<JobEnvelope> heap)
    {
        while (heap.Count > 0)
        {
            JobEnvelope candidate = heap.Peek();
            if (_present.Contains(candidate))
            {
                return candidate;
            }

            heap.Pop();
        }

        return null;
    }

    private static int ComparePriority(JobEnvelope a, JobEnvelope b)
    {
        int byPriority = b.Priority.CompareTo(a.Priority);
        return byPriority != 0 ? byPriority : a.Sequence.CompareTo(b.Sequence);
    }

    private static int CompareAge(JobEnvelope a, JobEnvelope b)
        => a.Sequence.CompareTo(b.Sequence);

    /// <summary>
    /// Minimal array-backed binary min-heap. "Smallest" per <paramref name="compare"/>
    /// surfaces at <see cref="Peek"/>. Not thread-safe; callers hold <see cref="_gate"/>.
    /// </summary>
    private sealed class BinaryHeap<T>(Comparison<T> compare)
    {
        private readonly List<T> _items = [];

        public int Count => _items.Count;

        public T Peek()
        {
            if (_items.Count == 0)
            {
                throw new InvalidOperationException("The heap is empty.");
            }

            return _items[0];
        }

        public void Push(T item)
        {
            _items.Add(item);
            SiftUp(_items.Count - 1);
        }

        public T Pop()
        {
            if (_items.Count == 0)
            {
                throw new InvalidOperationException("The heap is empty.");
            }

            T top = _items[0];
            int last = _items.Count - 1;
            _items[0] = _items[last];
            _items.RemoveAt(last);
            if (_items.Count > 0)
            {
                SiftDown(0);
            }

            return top;
        }

        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (compare(_items[index], _items[parent]) >= 0)
                {
                    break;
                }

                (_items[index], _items[parent]) = (_items[parent], _items[index]);
                index = parent;
            }
        }

        private void SiftDown(int index)
        {
            int count = _items.Count;
            while (true)
            {
                int left = (2 * index) + 1;
                int right = (2 * index) + 2;
                int smallest = index;

                if (left < count && compare(_items[left], _items[smallest]) < 0)
                {
                    smallest = left;
                }

                if (right < count && compare(_items[right], _items[smallest]) < 0)
                {
                    smallest = right;
                }

                if (smallest == index)
                {
                    break;
                }

                (_items[index], _items[smallest]) = (_items[smallest], _items[index]);
                index = smallest;
            }
        }
    }
}
