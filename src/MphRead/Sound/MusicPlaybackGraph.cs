using System;
using System.Threading;
using NCSF123;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace MphRead.Sound;

/// <summary>The F32, stereo, nonseekable music path. This is one mixer component,
/// matching SoundPlayer's direct-read path without an extra callback buffer layer.</summary>
internal sealed class MusicCallbackComponent : SoundComponent
{
    private RawDataProvider? _provider;
    private readonly MusicCallbackLifetime _lifetime = new();
    private int _state = (int)PlaybackState.Stopped;
    internal MusicCallbackComponent(AudioEngine engine, AudioFormat format, RawDataProvider provider) : base(engine, format)
    { _provider = provider; Enabled = false; }
    internal PlaybackState State => (PlaybackState)Volatile.Read(ref _state);
    internal void SetState(PlaybackState state)
    { Volatile.Write(ref _state, (int)state); Enabled = state == PlaybackState.Playing; }
    internal void BeginRetirement()
    { _lifetime.BeginRetirement(); SetState(PlaybackState.Stopped); }
    internal bool IsIdle => _lifetime.IsIdle;
    internal void ForgetProvider() => _provider = null;
    protected override void GenerateAudio(Span<float> output, int channels)
    {
        if (!_lifetime.TryEnter()) { output.Clear(); return; }
        try
        {
            if (State != PlaybackState.Playing || channels == 0 || _provider == null)
            { output.Clear(); return; }
            while (!output.IsEmpty)
            {
                int read = _provider.ReadBytes(output);
                if (read == 0)
                {
                    output.Clear();
                    // SoundPlayer's non-looping live-stream path stays playing
                    // on a transient zero read; finite streams stop at their end.
                    if (_provider.Length > 0 && _provider.Position >= _provider.Length)
                        SetState(PlaybackState.Stopped);
                    return;
                }
                output = output[read..];
            }
        }
        finally { _lifetime.Exit(); }
    }
}

internal sealed class MusicPlaybackGraph : IMusicLoadGraph
{
    internal readonly NCSFPlayerStream Stream;
    internal readonly MusicCallbackComponent Component;
    private readonly RawDataProvider _provider;
    private readonly AudioPlaybackDevice _device;
    private bool _attached, _disposed;
    internal MusicPlaybackGraph(NCSFPlayerStream stream, RawDataProvider provider,
        MusicCallbackComponent component, AudioPlaybackDevice device)
    { Stream = stream; _provider = provider; Component = component; _device = device; }
    public void Attach()
    {
        _device.MasterMixer.AddComponent(Component);
        _attached = true;
        _device.Start();
    }
    public void BeginRetirement() => Component.BeginRetirement();
    public void Retire()
    {
        if (_disposed) return;
        BeginRetirement();
        if (_attached) _device.MasterMixer.RemoveComponent(Component);
        // Removal replaces the mixer snapshot but does not quiesce an old
        // snapshot. A stopped/stale callback cannot enter the retired provider.
        while (!Component.IsIdle) Thread.Sleep(1);
        _disposed = true;
        try { _provider.Dispose(); } // RawDataProvider owns and disposes Stream.
        finally { Component.ForgetProvider(); Component.Dispose(); }
    }
}

/// <summary>Ownership before publication. A provider owns its stream; the
/// component does not own either until the graph is handed to the load owner.</summary>
internal sealed class MusicGraphConstruction : IDisposable
{
    internal IDisposable? Stream, Provider, Component;
    private bool _transferred;
    internal void Transfer() => _transferred = true;
    public void Dispose()
    {
        if (_transferred) return;
        Exception? failure = null;
        bool providerReleased = false;
        void Attempt(Action release)
        {
            try { release(); }
            catch (Exception ex) { failure = failure == null ? ex : new AggregateException(failure, ex); }
        }
        if (Provider != null) Attempt(() => { Provider.Dispose(); providerReleased = true; });
        if (Stream != null && !providerReleased) Attempt(Stream.Dispose);
        if (Component != null) Attempt(Component.Dispose);
        if (failure != null) throw failure;
    }
}
