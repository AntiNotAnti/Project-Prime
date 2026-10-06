using System;
using System.Collections.Generic;

namespace MphRead.Mods.Render;

internal readonly record struct FrameTraceSample(long Frame, double CpuFrameSpanMs,
    double? PresentReturnIntervalMs, double IdleBeforeFrameMs, double DriverWaitMs,
    double InputSampleOffsetMs, double SimulationMs, double NetworkMs, double PreparationMs,
    double RenderMs, double DriverPresentCallMs, double SurfaceAcquireMs,
    long SimulationAllocatedBytes, long PreparationAllocatedBytes, long RenderAllocatedBytes);

/// <summary>Owner-thread phase ledger. Clock values are injected for deterministic
/// tests. Completion intervals describe driver returns, never display scanout.</summary>
internal sealed class FrameTraceLedger
{
    private readonly double _millisecondsPerTick;
    private readonly int _maximumFrames;
    private long _frame, _begin, _previousPresent, _input, _simulationStart, _simulationEnd,
        _preparationStart, _preparationEnd, _renderStart, _renderEnd, _presentStart;
    private long _simulationAllocationStart, _simulationAllocationEnd, _preparationAllocationStart,
        _preparationAllocationEnd, _renderAllocationStart, _renderAllocationEnd;
    private double _wait, _network, _acquire;
    private bool _open, _hasPrevious;
    internal List<FrameTraceSample> Samples { get; }
    internal int CancelledFrames { get; private set; }
    internal bool Full => Samples.Count >= _maximumFrames;
    internal bool Open => _open;
    internal FrameTraceLedger(long frequency, int maximumFrames = 8192)
    {
        if (frequency <= 0 || maximumFrames <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        _millisecondsPerTick = 1000d / frequency; _maximumFrames = maximumFrames;
        Samples = new List<FrameTraceSample>(maximumFrames);
    }
    internal void Begin(long ticks)
    {
        if (_open) Cancel();
        if (Full) return;
        _frame++; _begin = ticks; _open = true;
        _input = _simulationStart = _simulationEnd = _preparationStart = _preparationEnd = _renderStart = _renderEnd = _presentStart = 0;
        _simulationAllocationStart = _simulationAllocationEnd = _preparationAllocationStart = _preparationAllocationEnd = _renderAllocationStart = _renderAllocationEnd = 0;
        _wait = _network = _acquire = 0;
    }
    internal void Input(long ticks) { if (_open) _input = ticks; }
    internal void SimulationStart(long ticks, long allocated) { if (_open) { _simulationStart = ticks; _simulationAllocationStart = allocated; } }
    internal void SimulationEnd(long ticks, long allocated) { if (_open) { _simulationEnd = ticks; _simulationAllocationEnd = allocated; } }
    internal void PreparationStart(long ticks, long allocated) { if (_open) { _preparationStart = ticks; _preparationAllocationStart = allocated; } }
    internal void PreparationEnd(long ticks, long allocated) { if (_open) { _preparationEnd = ticks; _preparationAllocationEnd = allocated; } }
    internal void RenderStart(long ticks, long allocated) { if (_open) { _renderStart = ticks; _renderAllocationStart = allocated; } }
    internal void RenderEnd(long ticks, long allocated) { if (_open) { _renderEnd = ticks; _renderAllocationEnd = allocated; } }
    internal void PresentStart(long ticks) { if (_open) _presentStart = ticks; }
    internal void AddWait(double ms) { if (_open) _wait += ms; }
    internal void AddNetwork(double ms) { if (_open) _network += ms; }
    internal void AddAcquire(double ms) { if (_open) _acquire += ms; }
    private double Duration(long start, long end) => start == 0 || end < start ? 0 : (end - start) * _millisecondsPerTick;
    internal void Complete(long ticks)
    {
        if (!_open) return;
        if (_presentStart == 0 || ticks < _presentStart || ticks < _begin) { Cancel(); return; }
        Samples.Add(new(_frame, (ticks - _begin) * _millisecondsPerTick,
            _hasPrevious ? (ticks - _previousPresent) * _millisecondsPerTick : null,
            _hasPrevious ? Math.Max(0, (_begin - _previousPresent) * _millisecondsPerTick) : 0,
            _wait, _input == 0 ? 0 : (_input - _begin) * _millisecondsPerTick,
            Duration(_simulationStart, _simulationEnd), _network,
            Duration(_preparationStart, _preparationEnd),
            Duration(_preparationEnd != 0 ? _preparationEnd : _renderStart, _renderEnd),
            Duration(_presentStart, ticks), _acquire,
            Math.Max(0, _simulationAllocationEnd - _simulationAllocationStart),
            Math.Max(0, _preparationAllocationEnd - _preparationAllocationStart),
            Math.Max(0, _renderAllocationEnd - (_preparationEnd != 0 ? _preparationAllocationEnd : _renderAllocationStart))));
        _previousPresent = ticks; _hasPrevious = true; _open = false;
    }
    internal void Cancel() { if (_open) CancelledFrames++; _open = false; }
}
