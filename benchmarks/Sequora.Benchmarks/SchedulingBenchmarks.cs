using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Sequora.Benchmarks;

/// <summary>
/// Steady-state churn benchmark: pre-fill a scheduler to <see cref="QueueSize"/>
/// mixed-priority items, then repeatedly dequeue the best item and enqueue a
/// new one, keeping the queue size constant. This is the operation
/// <c>ReadyQueue</c> performs on every job dequeue while workers are busy.
/// </summary>
/// <remarks>
/// Compares the pre-optimization O(n) linear scan (<see cref="LinearScanScheduler"/>)
/// against the current O(log n) binary-heap selection (<see cref="HeapScheduler"/>)
/// at increasing queue sizes. If per-operation time stays roughly flat as
/// <see cref="QueueSize"/> grows for the heap scheduler, but grows roughly
/// linearly for the linear-scan scheduler, that is the O(n) vs O(log n) claim
/// made visible in real numbers instead of asserted from the algorithm alone.
/// </remarks>
[MemoryDiagnoser(displayGenColumns: false)]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 3, iterationCount: 5)]
public class SchedulingBenchmarks
{
    private const int PriorityLevels = 5;

    [Params(100, 1_000, 10_000, 50_000)]
    public int QueueSize { get; set; }

    private LinearScanScheduler _linear = null!;
    private HeapScheduler _heap = null!;
    private long _linearSequence;
    private long _heapSequence;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _linear = new LinearScanScheduler();
        _heap = new HeapScheduler();
        _linearSequence = 0;
        _heapSequence = 0;

        for (int i = 0; i < QueueSize; i++)
        {
            _linear.Enqueue(NextItem(ref _linearSequence));
            _heap.Enqueue(NextItem(ref _heapSequence));
        }
    }

    [Benchmark(Baseline = true, Description = "Linear scan (pre-fix, O(n))")]
    public BenchItem? LinearScan()
    {
        _linear.TryDequeue(out BenchItem? item);
        _linear.Enqueue(NextItem(ref _linearSequence));
        return item;
    }

    [Benchmark(Description = "Binary heap (current, O(log n))")]
    public BenchItem? HeapScan()
    {
        _heap.TryDequeue(out BenchItem? item);
        _heap.Enqueue(NextItem(ref _heapSequence));
        return item;
    }

    private static BenchItem NextItem(ref long sequence)
    {
        long next = ++sequence;
        return new BenchItem
        {
            Priority = (int)(next % PriorityLevels),
            Sequence = next
        };
    }
}
