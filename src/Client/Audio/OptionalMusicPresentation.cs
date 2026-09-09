using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Content;
using MphRead.Runtime.Content;
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
    private SoundPlayer? _player;
    private StreamDataProvider? _provider;
    private FileStream? _stream;
    private CancellationTokenSource? _pendingCancellation;
    private Task<FileStream?>? _pendingOpen;
    private bool _verificationFailed;

    public bool Active => _player != null || _pendingOpen != null;

    public OptionalMusicPresentation(OptionalMusicPack pack)
        => _pack = pack ?? throw new ArgumentNullException(nameof(pack));

    public bool TryPlay(int contextId, int variant, float volume)
    {
        Stop();
        MiniAudioEngine? engine = MusicPlayer.Engine;
        AudioPlaybackDevice? device = MusicPlayer.PlaybackDevice;
        if (engine == null || device == null) return false;
        _pendingCancellation = new CancellationTokenSource();
        _pendingOpen = _pack.OpenTrackAsync(contextId, variant, _pendingCancellation.Token);
        _verificationFailed = false;
        return true;
    }

    public void Update(float volume)
    {
        Task<FileStream?>? pending = _pendingOpen;
        if (pending == null || !pending.IsCompleted) return;
        _pendingOpen = null;
        _pendingCancellation?.Dispose();
        _pendingCancellation = null;
        FileStream? stream;
        try
        {
            stream = pending.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            stream = null;
        }
        if (stream == null || !StartVerified(stream, volume)) _verificationFailed = true;
    }

    public bool ConsumeVerificationFailure()
    {
        bool failed = _verificationFailed;
        _verificationFailed = false;
        return failed;
    }

    private bool StartVerified(FileStream stream, float volume)
    {
        MiniAudioEngine? engine = MusicPlayer.Engine;
        AudioPlaybackDevice? device = MusicPlayer.PlaybackDevice;
        if (engine == null || device == null)
        {
            stream.Dispose();
            return false;
        }
        try
        {
            _stream = stream;
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
            return true;
        }
        catch (Exception)
        {
            StopPlayer();
            return false;
        }
    }

    public void SetVolume(float volume)
    {
        if (_player != null) _player.Volume = Math.Clamp(volume, 0, 1);
    }

    public bool Pause()
    {
        if (_player == null) return false;
        _player.Pause();
        return true;
    }

    public bool Resume()
    {
        if (_player == null) return false;
        _player.Play();
        return true;
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation = _pendingCancellation;
        Task<FileStream?>? pending = _pendingOpen;
        _pendingCancellation = null;
        _pendingOpen = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        if (pending != null)
        {
            _ = pending.ContinueWith(completed => completed.Result?.Dispose(),
                CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion
                    | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        _verificationFailed = false;
        StopPlayer();
    }

    private void StopPlayer()
    {
        SoundPlayer? player = _player;
        _player = null;
        if (player != null)
        {
            try
            {
                player.Stop();
                MusicPlayer.PlaybackDevice?.MasterMixer.RemoveComponent(player);
            }
            catch (Exception)
            {
                // The scene's shared audio device already shut down.
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

    public void Dispose() => Stop();
}
