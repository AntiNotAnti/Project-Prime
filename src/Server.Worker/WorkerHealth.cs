using System.Diagnostics;
using ProjectPrime.Server.Shared;

namespace ProjectPrime.Server.Worker;

internal sealed class WorkerHealthSampler
{
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly long _start = Stopwatch.GetTimestamp();
    private long _sampleTime = Stopwatch.GetTimestamp();
    private TimeSpan _cpu = Process.GetCurrentProcess().TotalProcessorTime;
    private long _allocationSampleTime = Stopwatch.GetTimestamp();
    private long _allocatedBytes = GC.GetTotalAllocatedBytes(false);
    public double CpuPercent { get; private set; }
    public double AllocationBytesPerSecond { get; private set; }
    public long WorkingSetBytes { get; private set; }
    public long MemoryHeadroomBytes { get; private set; } = long.MaxValue;
    public long UptimeMilliseconds => (long)Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
    public void Sample()
    {
        _process.Refresh();
        long now = Stopwatch.GetTimestamp();
        double elapsed = Stopwatch.GetElapsedTime(_sampleTime, now).TotalMilliseconds;
        TimeSpan cpu = _process.TotalProcessorTime;
        if (elapsed >= 100)
        {
            CpuPercent = Math.Clamp((cpu - _cpu).TotalMilliseconds / elapsed / Environment.ProcessorCount * 100, 0, 100);
            _sampleTime = now; _cpu = cpu;
        }
        double allocationElapsed = Stopwatch.GetElapsedTime(_allocationSampleTime, now).TotalSeconds;
        long allocated = GC.GetTotalAllocatedBytes(false);
        if (allocationElapsed >= 0.1)
        {
            long delta = allocated >= _allocatedBytes ? allocated - _allocatedBytes : 0;
            AllocationBytesPerSecond = delta / allocationElapsed;
            _allocationSampleTime = now;
            _allocatedBytes = allocated;
        }
        WorkingSetBytes = _process.WorkingSet64;
        long available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        MemoryHeadroomBytes = available > 0 ? Math.Max(0, available - WorkingSetBytes) : long.MaxValue;
    }
}
