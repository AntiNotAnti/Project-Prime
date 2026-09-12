using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Content;
using MphRead.Runtime.Content;
using MphRead.Sound;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Metadata.Models;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace MphRead.Mods.Audio;

/// <summary>
/// Deterministic data-only view of a selected QZ6 music pack. Manifest order
/// is the playlist; room and variant choose a stable entry without touching
/// simulation or network state.
/// </summary>
public sealed class OptionalMusicPack
{
    private readonly OptionalPresentationAssetResolver _assets;
    private readonly string[] _tracks;

    public ContentPackIdentity Identity { get; }
    public int TrackCount => _tracks.Length;

    public OptionalMusicPack(InstalledOptionalPresentationPack installed,
        OptionalPresentationAssetResolver assets)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(assets);
        OptionalPresentationManifest manifest = ContentManifestValidator.ValidateOptional(installed.Manifest);
        if (manifest.Kind != OptionalPresentationKind.Music)
            throw new ArgumentException("A music presentation requires a music pack.", nameof(installed));
        Identity = manifest.PackIdentity;
        if (Identity != assets.Identity || assets.Kind != OptionalPresentationKind.Music)
            throw new ArgumentException("The music resolver belongs to another pack.", nameof(assets));
        _assets = assets;
        _tracks = Array.ConvertAll(manifest.Files, file => file.Path);
    }

    public Task<FileStream?> OpenTrackAsync(int roomId, int variant, CancellationToken cancellationToken)
    {
        if (_tracks.Length == 0) return Task.FromResult<FileStream?>(null);
        uint key = unchecked((uint)roomId * 397u + (uint)Math.Max(variant, 0));
        return _assets.OpenVerifiedAsync(_tracks[key % (uint)_tracks.Length], cancellationToken);
    }
}

/// <summary>
/// Presentation-only music component attached to the process's existing
/// SoundFlow output. Decoder failures are contained and return control to the
/// built-in NCSF music path.
/// </summary>
internal sealed class OptionalMusicPresentation : IDisposable
{
    private readonly OptionalMusicPack _pack;
    private readonly object _stateGate = new();
    private SoundPlayer? _player;
    private AudioPlaybackDevice? _device;
    private StreamDataProvider? _provider;
    private FileStream? _stream;
    private CancellationTokenSource? _pendingCancellation;
    private Task<FileStream?>? _pendingOpen;
    private int _pendingGeneration;
    private int _generation;
    private bool _verificationFailed;
    private bool _disposed;

    public bool Active
    {
        get
        {
            lock (_stateGate) return _player != null || _pendingOpen != null;
        }
    }

    public OptionalMusicPresentation(OptionalMusicPack pack)
        => _pack = pack ?? throw new ArgumentNullException(nameof(pack));

    public bool TryPlay(int contextId, int variant, float volume)
    {
        Stop();
        int generation = 0;
        bool accepted = false;
        if (!AudioService.Process.TryExecute(() =>
        {
            lock (_stateGate)
            {
                if (_disposed) return;
                // Availability is sampled under the process owner as well;
                // the async file open must not outlive a failed/shutting
                // down SoundFlow output just to report a later fallback.
                if (MusicPlayer.Engine == null || MusicPlayer.PlaybackDevice == null) return;
                generation = ++_generation;
                _verificationFailed = false;
                accepted = true;
            }
        }) || !accepted)
        {
            return false;
        }

        CancellationTokenSource cancellation = new();
        Task<FileStream?> pending;
        try
        {
            pending = _pack.OpenTrackAsync(contextId, variant, cancellation.Token);
        }
        catch (Exception)
        {
            cancellation.Dispose();
            return false;
        }

        bool stale;
        lock (_stateGate)
        {
            stale = _disposed || generation != _generation;
            if (!stale)
            {
                _pendingCancellation = cancellation;
                _pendingOpen = pending;
                _pendingGeneration = generation;
            }
        }
        if (stale)
        {
            try { cancellation.Cancel(); }
            catch (Exception) { }
            cancellation.Dispose();
            _ = pending.ContinueWith(completed => completed.Result?.Dispose(),
                CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion
                    | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return false;
        }
        return true;
    }

    public void Update(float volume)
    {
        Task<FileStream?>? pending;
        CancellationTokenSource? cancellation;
        int generation;
        lock (_stateGate)
        {
            pending = _pendingOpen;
            generation = _pendingGeneration;
            if (pending == null || !pending.IsCompleted) return;
            _pendingOpen = null;
            _pendingGeneration = 0;
            cancellation = _pendingCancellation;
            _pendingCancellation = null;
        }

        cancellation?.Dispose();
        FileStream? stream;
        try
        {
            stream = pending.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            stream = null;
        }
        if (stream == null || !StartVerified(stream, volume, generation))
        {
            lock (_stateGate)
            {
                if (!_disposed && generation == _generation) _verificationFailed = true;
            }
        }
    }

    public bool ConsumeVerificationFailure()
    {
        lock (_stateGate)
        {
            bool failed = _verificationFailed;
            _verificationFailed = false;
            return failed;
        }
    }

    private bool StartVerified(FileStream stream, float volume, int generation)
    {
        FileStream? unowned = stream;
        bool started = false;
        try
        {
            started = AudioService.Process.TryExecute(() =>
            {
                lock (_stateGate)
                {
                    if (_disposed || generation != _generation) return;
                    MiniAudioEngine? engine = MusicPlayer.Engine;
                    AudioPlaybackDevice? device = MusicPlayer.PlaybackDevice;
                    if (engine == null || device == null) return;
                    try
                    {
                        _device = device;
                        _stream = unowned;
                        unowned = null;
                        _provider = new StreamDataProvider(engine, _stream, new ReadOptions
                        {
                            ReadTags = false,
                            ReadAlbumArt = false,
                            ReadCueSheet = false,
                            DurationAccuracy = DurationAccuracy.FastEstimate
                        });
                        AudioFormat format = MusicPlayer.Format;
                        _player = new SoundPlayer(engine, format, _provider)
                        {
                            IsLooping = true,
                            Volume = Math.Clamp(volume, 0, 1)
                        };
                        device.MasterMixer.AddComponent(_player);
                        device.Start();
                        _player.Play();
                        started = true;
                    }
                    catch (Exception)
                    {
                        StopPlayerCore();
                    }
                }
            });
        }
        catch (Exception)
        {
            started = false;
        }
        if (unowned != null)
        {
            try { unowned.Dispose(); }
            catch (Exception) { }
        }
        return started;
    }

    public void SetVolume(float volume)
    {
        AudioService.Process.TryExecute(() =>
        {
            lock (_stateGate)
            {
                if (!_disposed && _player != null)
                    _player.Volume = Math.Clamp(volume, 0, 1);
            }
        });
    }

    public bool Pause()
    {
        bool paused = false;
        AudioService.Process.TryExecute(() =>
        {
            lock (_stateGate)
            {
                if (_disposed || _player == null) return;
                _player.Pause();
                paused = true;
            }
        });
        return paused;
    }

    public bool Resume()
    {
        bool resumed = false;
        AudioService.Process.TryExecute(() =>
        {
            lock (_stateGate)
            {
                if (_disposed || _player == null) return;
                _player.Play();
                resumed = true;
            }
        });
        return resumed;
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation;
        Task<FileStream?>? pending;
        lock (_stateGate)
        {
            ++_generation;
            cancellation = _pendingCancellation;
            pending = _pendingOpen;
            _pendingCancellation = null;
            _pendingOpen = null;
            _pendingGeneration = 0;
            _verificationFailed = false;
        }
        try { cancellation?.Cancel(); }
        catch (Exception) { }
        cancellation?.Dispose();
        if (pending != null)
        {
            _ = pending.ContinueWith(completed => completed.Result?.Dispose(),
                    CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion
                        | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        AudioService.Process.TryExecute(() =>
        {
            lock (_stateGate) StopPlayerCore();
        });
    }

    private void StopPlayerCore()
    {
        SoundPlayer? player = _player;
        AudioPlaybackDevice? device = _device;
        _player = null;
        _device = null;
        if (player != null)
        {
            try
            {
                player.Stop();
                device?.MasterMixer.RemoveComponent(player);
            }
            catch (Exception)
            {
                // The process audio owner is already unwinding.
            }
            try { player.Dispose(); }
            catch (Exception) { }
        }
        try { _provider?.Dispose(); }
        catch (Exception) { }
        _provider = null;
        try { _stream?.Dispose(); }
        catch (Exception) { }
        _stream = null;
    }

    public void Dispose()
    {
        lock (_stateGate) _disposed = true;
        Stop();
    }
}
