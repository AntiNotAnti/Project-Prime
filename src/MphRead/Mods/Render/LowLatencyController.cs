using System;

namespace MphRead.Mods.Render;

public enum LowLatencyMode
{
    Disabled,
    Enabled,
    Boost
}

internal enum LowLatencyMarker
{
    InputSample = 1,
    SimulationStart = 2,
    SimulationEnd = 3,
    RenderSubmitStart = 4,
    RenderSubmitEnd = 5,
    PresentStart = 6,
    PresentEnd = 7
}

internal interface ILowLatencyProvider : IDisposable
{
    string Name { get; }
    bool Supported { get; }
    void Configure(LowLatencyMode mode);
    void Mark(ulong frameId, LowLatencyMarker marker);
}

internal sealed class NullLowLatencyProvider : ILowLatencyProvider
{
    internal static readonly NullLowLatencyProvider Instance = new();
    public string Name => "none";
    public bool Supported => false;
    public void Configure(LowLatencyMode mode) { }
    public void Mark(ulong frameId, LowLatencyMarker marker) { }
    public void Dispose() { }
}

internal readonly record struct LowLatencyStatus(
    string Provider,
    bool Supported,
    LowLatencyMode RequestedMode,
    LowLatencyMode ActiveMode,
    ulong LastFrameId,
    long DroppedMarkers,
    long IncompleteFrames);

/// <summary>
/// Backend-neutral low-latency marker session. A future DX12/Vulkan provider
/// (for example NVIDIA Reflex) plugs in here; gameplay/render code emits one
/// stable marker vocabulary and never depends on a vendor API.
/// </summary>
internal sealed class LowLatencySession : IDisposable
{
    private ILowLatencyProvider _provider;
    private LowLatencyMode _requested;
    private LowLatencyMode _active;
    private ulong _nextFrame;
    private ulong _activeFrame;
    private LowLatencyMarker _lastMarker;
    private bool _frameOpen;
    private long _droppedMarkers;
    private long _incompleteFrames;

    internal LowLatencySession(ILowLatencyProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    internal LowLatencyStatus Status => new(
        _provider.Name,
        _provider.Supported,
        _requested,
        _active,
        _nextFrame,
        _droppedMarkers,
        _incompleteFrames);

    internal void Configure(LowLatencyMode mode)
    {
        _requested = mode;
        _active = _provider.Supported ? mode : LowLatencyMode.Disabled;
        _provider.Configure(_active);
    }

    internal ulong BeginFrame()
    {
        if (_frameOpen)
            _incompleteFrames++;
        _activeFrame = ++_nextFrame;
        _lastMarker = 0;
        _frameOpen = true;
        return _activeFrame;
    }

    internal bool Mark(ulong frameId, LowLatencyMarker marker)
    {
        if (!_frameOpen || frameId != _activeFrame || marker <= _lastMarker)
        {
            _droppedMarkers++;
            return false;
        }
        _lastMarker = marker;
        if (_active != LowLatencyMode.Disabled && _provider.Supported)
            _provider.Mark(frameId, marker);
        if (marker == LowLatencyMarker.PresentEnd)
            _frameOpen = false;
        return true;
    }

    internal void CancelFrame(ulong frameId)
    {
        if (_frameOpen && frameId == _activeFrame)
            _frameOpen = false;
    }

    internal void ReplaceProvider(ILowLatencyProvider provider)
    {
        if (ReferenceEquals(_provider, provider)) return;
        _provider.Dispose();
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _active = _provider.Supported ? _requested : LowLatencyMode.Disabled;
        _provider.Configure(_active);
    }

    public void Dispose()
    {
        _frameOpen = false;
        _provider.Dispose();
        _provider = NullLowLatencyProvider.Instance;
        _active = LowLatencyMode.Disabled;
    }
}

internal static class LowLatencyController
{
    private static readonly LowLatencySession _session =
        new(NullLowLatencyProvider.Instance);

    internal static LowLatencyStatus Status => _session.Status;

    // Disabled until a concrete backend provider and player-facing policy are
    // added. Marker plumbing is live now so provider integration is mechanical.
    internal static void Configure(LowLatencyMode mode) => _session.Configure(mode);
    internal static ulong BeginFrame() => _session.BeginFrame();
    internal static bool Mark(ulong frameId, LowLatencyMarker marker) =>
        _session.Mark(frameId, marker);
    internal static void CancelFrame(ulong frameId) => _session.CancelFrame(frameId);

    internal static void InstallProvider(ILowLatencyProvider provider) =>
        _session.ReplaceProvider(provider);
}
