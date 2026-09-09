using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Combat;
using MphRead.Mods.Content;
using MphRead.Sound;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Metadata.Models;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace MphRead.Mods.Audio;

internal interface IAnnouncerFilePlayer : IDisposable
{
    // Takes ownership of the already-verified stream on both success and failure.
    bool TryPlay(FileStream stream, float volume);
    void Stop();
}

/// <summary>
/// Drains at most one semantic cue per presentation pass. Optional local files
/// are resolved by the QZ6 integrity boundary and use one replaceable player;
/// any resolution, decode, or device failure falls back to a built-in cue.
/// </summary>
public sealed class AnnouncerAudioPresentation : IDisposable
{
    private readonly AnnouncerService _announcer;
    private readonly OptionalPresentationAssetResolver? _assets;
    private readonly IAnnouncerFilePlayer _files;
    private readonly SoundSource _source;
    private CancellationTokenSource? _pendingCancellation;
    private Task<FileStream?>? _pendingOpen;
    private AnnouncerCue _pendingCue;

    public long CuesConsumed { get; private set; }
    public long OptionalFilesPlayed { get; private set; }
    public long BuiltInsPlayed { get; private set; }
    public long OptionalFallbacks { get; private set; }
    public long SuppressedCues { get; private set; }

    public AnnouncerAudioPresentation(Scene scene, AnnouncerService announcer,
        OptionalPresentationAssetResolver? assets = null)
        : this(scene.Audio, announcer, assets, new SoundFlowAnnouncerFilePlayer()) { }

    internal AnnouncerAudioPresentation(AudioRequests requests, AnnouncerService announcer,
        OptionalPresentationAssetResolver? assets, IAnnouncerFilePlayer files)
    {
        ArgumentNullException.ThrowIfNull(requests);
        _announcer = announcer ?? throw new ArgumentNullException(nameof(announcer));
        _assets = assets;
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _source = new SoundSource(requests) { Self = true };
    }

    public bool PresentNext()
    {
        if (_pendingOpen != null) return CompletePending();
        if (!_announcer.TryDequeue(out AnnouncerCue cue)) return false;
        if (CuesConsumed < long.MaxValue) CuesConsumed++;
        float gain = Gain;
        if (gain <= 0 || Sfx.TimedSfxMute > 0)
        {
            _files.Stop();
            if (SuppressedCues < long.MaxValue) SuppressedCues++;
            return true;
        }

        bool optional = !cue.AssetKey.StartsWith("builtin:", StringComparison.Ordinal);
        if (optional && _assets != null)
        {
            _pendingCue = cue;
            _pendingCancellation = new CancellationTokenSource();
            _pendingOpen = _assets.OpenVerifiedAsync(cue.AssetKey, _pendingCancellation.Token);
            return true;
        }

        PresentBuiltIn(cue, optional);
        return true;
    }

    private bool CompletePending()
    {
        Task<FileStream?> pending = _pendingOpen!;
        if (!pending.IsCompleted) return true;
        AnnouncerCue cue = _pendingCue;
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

        float gain = Gain;
        if (gain <= 0 || Sfx.TimedSfxMute > 0)
        {
            stream?.Dispose();
            _files.Stop();
            if (SuppressedCues < long.MaxValue) SuppressedCues++;
            return true;
        }
        if (stream != null && _files.TryPlay(stream, gain))
        {
            if (OptionalFilesPlayed < long.MaxValue) OptionalFilesPlayed++;
            return true;
        }
        PresentBuiltIn(cue, optional: true);
        return true;
    }

    private void PresentBuiltIn(AnnouncerCue cue, bool optional)
    {
        _files.Stop();
        _source.Volume = Gain;
        PlayBuiltIn(cue.Event);
        if (BuiltInsPlayed < long.MaxValue) BuiltInsPlayed++;
        if (optional && OptionalFallbacks < long.MaxValue) OptionalFallbacks++;
    }

    public void Dispose()
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
        _files.Dispose();
    }

    private static float Gain
        => float.IsFinite(FeedbackAudio.Volume) ? Math.Clamp(FeedbackAudio.Volume, 0, 1) : 0;

    private void PlayBuiltIn(AnnouncerEvent value)
    {
        switch (value)
        {
            case AnnouncerEvent.DoubleKill:
            case AnnouncerEvent.TripleKill:
                _source.QueueStream(VoiceId.VOICE_CONSECUTIVE_KILLS);
                return;
            case AnnouncerEvent.MatchPoint:
                _source.QueueStream(VoiceId.VOICE_ONE_KILL_TO_WIN);
                return;
            case AnnouncerEvent.PrimeSlayer:
                _source.QueueStream(VoiceId.VOICE_PRIME);
                return;
            case AnnouncerEvent.Capture:
                _source.QueueStream(VoiceId.VOICE_OCTO_SCORE);
                return;
            case AnnouncerEvent.Defeat:
                _source.QueueStream(VoiceId.VOICE_ELIMINATED);
                return;
        }

        SfxId sound = value switch
        {
            AnnouncerEvent.Three or AnnouncerEvent.Two or AnnouncerEvent.One => SfxId.MENU_CURSOR,
            AnnouncerEvent.Go or AnnouncerEvent.Victory => SfxId.MENU_CONFIRM,
            AnnouncerEvent.Overtime => SfxId.ALARM,
            AnnouncerEvent.FirstHunt => SfxId.POWER_UP1,
            AnnouncerEvent.Interceptor or AnnouncerEvent.Defender => SfxId.CAPTURE_RING_5,
            AnnouncerEvent.Assist => SfxId.LETTER_BLIP,
            _ => SfxId.MENU_CONFIRM
        };
        _source.PlaySfx(sound);
    }
}

/// <summary>One bounded in-memory SoundFlow asset at a time.</summary>
internal sealed class SoundFlowAnnouncerFilePlayer : IAnnouncerFilePlayer
{
    private StreamDataProvider? _provider;
    private FileStream? _stream;
    private SoundPlayer? _player;

    public bool TryPlay(FileStream stream, float volume)
    {
        ArgumentNullException.ThrowIfNull(stream);
        MiniAudioEngine? engine = MusicPlayer.Engine;
        AudioPlaybackDevice? device = MusicPlayer.PlaybackDevice;
        if (engine == null || device == null || volume <= 0)
        {
            stream.Dispose();
            return false;
        }
        Stop();
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
            _player = new SoundPlayer(engine, MusicPlayer.Format, _provider)
            {
                Volume = volume
            };
            device.MasterMixer.AddComponent(_player);
            device.Start();
            _player.Play();
            return true;
        }
        catch (Exception)
        {
            Stop();
            return false;
        }
    }

    public void Stop()
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
            catch (Exception) { }
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
