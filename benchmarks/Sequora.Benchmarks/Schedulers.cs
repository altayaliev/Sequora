namespace Sequora.Benchmarks;

/// <summary>
/// Minimal ready-queue item: just the two fields the selection algorithm
/// needs. Deliberately decoupled from <c>Sequora.Internal.JobEnvelope</c> so
/// this project needs no access to library internals — it measures the
/// scheduling algorithm itself, not the library's plumbing around it.
/// </summary>
public sealed class BenchItem
{
    public int Priority { get; init; }

    public long Sequence { get; init; }
}

/// <summary>
/// Reproduces the pre-optimization <c>ReadyQueue</c> selection: an O(n) linear
/// scan for the highest-priority (oldest-on-tie) item on every dequeue. This
/// is the "before" baseline for <see cref="HeapScheduler"/>.
/// </summary>
internal sealed class LinearScanScheduler
{
    private readonly List<BenchItem> _items = [];

    public int Count => _items.Count;

    public void Enqueue(BenchItem item) => _items.Add(item);

    public bool TryDequeue(out BenchItem? item)
    {
        if (_items.Count == 0)
        {
            item = null;
            return false;
        }

        int bestIndex = 0;
        for (int i = 1; i < _items.Count; i++)
        {
            if (IsBetter(_items[i], _items[bestIndex]))
            {
                bestIndex = i;
            }
        }

        item = _items[bestIndex];
        _items.RemoveAt(bestIndex);
        return true;
    }

    private static bool IsBetter(BenchItem candidate, BenchItem current)
        => candidate.Priority > current.Priority
            || (candidate.Priority == current.Priority && candidate.Sequence < current.Sequence);
}

/// <summary>
/// The current <c>ReadyQueue</c> selection: a binary heap ordered by priority
/// (ties broken by age), giving O(log n) enqueue/dequeue. Structurally the
/// same heap as <c>Sequora.Internal.ReadyQueue.BinaryHeap&lt;T&gt;</c>, kept
/// as its own copy here so this project has no dependency on library
/// internals.
/// </summary>
internal sealed class HeapScheduler
{
    private readonly List<BenchItem> _heap = [];

    public int Count => _heap.Count;

    public void Enqueue(BenchItem item)
    {
        _heap.Add(item);
        SiftUp(_heap.Count - 1);
    }

    public bool TryDequeue(out BenchItem? item)
    {
        if (_heap.Count == 0)
        {
            item = null;
            return false;
        }

        item = _heap[0];
        int last = _heap.Count - 1;
        _heap[0] = _heap[last];
        _heap.RemoveAt(last);
        if (_heap.Count > 0)
        {
            SiftDown(0);
        }

        return true;
    }

    private static bool IsBetter(BenchItem candidate, BenchItem current)
        => candidate.Priority > current.Priority
            || (candidate.Priority == current.Priority && candidate.Sequence < current.Sequence);

    private void SiftUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (!IsBetter(_heap[index], _heap[parent]))
            {
                break;
            }

            (_heap[index], _heap[parent]) = (_heap[parent], _heap[index]);
            index = parent;
        }
    }

    private void SiftDown(int index)
    {
        int count = _heap.Count;
        while (true)
        {
            int left = (2 * index) + 1;
            int right = (2 * index) + 2;
            int best = index;

            if (left < count && IsBetter(_heap[left], _heap[best]))
            {
                best = left;
            }

            if (right < count && IsBetter(_heap[right], _heap[best]))
            {
                best = right;
            }

            if (best == index)
            {
                break;
            }

            (_heap[index], _heap[best]) = (_heap[best], _heap[index]);
            index = best;
        }
    }
}
