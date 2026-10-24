using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.MatchEvents;
using MphRead.Mods.Network;
using MphRead.Sound;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;

namespace MphRead.Mods.Sound;

internal enum CtfAnnouncementCue { Taken, Dropped, Reset }

/// <summary>Three uploaded CTF clips, played once per authoritative event on the Notifications bus.</summary>
internal sealed class CtfAnnouncementAudio : IDisposable
{
    internal const int QueueCapacity = 8;
    internal const long MaxQueueAgeMs = 2500;
    private readonly Queue<(CtfAnnouncementCue Cue, long Time)> _pending = new();
    private readonly Dictionary<CtfAnnouncementCue, ICtfAnnouncementVoice> _voices = new();
    private ICtfAnnouncementVoice? _active;
    private bool _attempted;
    private ushort _match;
    private ulong _epoch;
    private uint _lastEvent;
    internal int PendingCount => _pending.Count;
    internal bool Ready => _voices.Count == 3;

    internal static CtfAnnouncementCue? CueFor(MatchSemanticEventType type) => type switch
    {
        MatchSemanticEventType.ObjectivePickedUp => CtfAnnouncementCue.Taken,
        MatchSemanticEventType.ObjectiveDropped => CtfAnnouncementCue.Dropped,
        MatchSemanticEventType.CtfFlagReset => CtfAnnouncementCue.Reset,
        _ => null // no supplied capture voice; never call a successful capture a reset
    };

    internal static string FileName(CtfAnnouncementCue cue) => cue switch
    {
        CtfAnnouncementCue.Taken => "flag_taken.wav",
        CtfAnnouncementCue.Dropped => "flag_dropped.wav",
        CtfAnnouncementCue.Reset => "flag_reset.wav",
        _ => throw new ArgumentOutOfRangeException(nameof(cue))
    };

    internal static byte[] ReadEmbedded(CtfAnnouncementCue cue)
    {
        var assembly = typeof(CtfAnnouncementAudio).Assembly;
        string suffix = ".Sounds.Ctf." + FileName(cue);
        string resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using Stream input = assembly.GetManifestResourceStream(resource)!;
        using var copy = new MemoryStream();
        input.CopyTo(copy);
        return copy.ToArray();
    }

    internal static bool Allows(Scene scene) => scene.GameState.Mode == GameMode.CaptureTheFlag
        && !Headless.Active && !ThumbnailMode.Active && !scene.Services.IsReplica;

    internal void Warm(Scene scene)
    {
        if (_attempted || !Allows(scene)) return;
        _attempted = true;
        if (!MusicPlayer.Available || MusicPlayer.Engine == null || MusicPlayer.PlaybackDevice == null) return;
        Initialize(cue => new CtfAnnouncementVoice(ReadEmbedded(cue)));
    }

    // Fact delivery is already ordered and deduplicated by MatchSemanticReceiver.
    // Identity tracking below also fences the small voice queue across persistent-lobby rematches.
    internal void OnEvent(Scene scene, in MatchSemanticEventPacket fact)
    {
        if (!Allows(scene) || fact.IsBaseline || CueFor(fact.Type) is not { } cue
            || !scene.TryGetEntity(fact.EntityId, out var entity) || entity is not OctolithFlagEntity) return;
        Observe(fact.MatchId, fact.AuthorityEpoch, fact.EventId, cue, Environment.TickCount64);
    }

    internal void Observe(ushort match, ulong epoch, uint eventId, CtfAnnouncementCue cue, long now)
    {
        if (match == 0 || epoch == 0 || eventId == 0) return;
        if (_match != match || _epoch != epoch)
        {
            Stop(); _match = match; _epoch = epoch; _lastEvent = 0;
        }
        if (eventId <= _lastEvent) return;
        _lastEvent = eventId;
        if (!Ready) return; // missing audio device must not collect an unlimited delayed chorus
        if (_pending.Count == QueueCapacity) _pending.Dequeue();
        _pending.Enqueue((cue, now));
    }

    internal static float Gain(float master, float notifications)
        => float.IsFinite(master) && float.IsFinite(notifications)
            ? Math.Clamp(master, 0, 1) * Math.Clamp(notifications, 0, 1) : 0;

    internal void Update(Scene scene)
    {
        bool suspended = !Allows(scene) || scene.GameState.MatchState != MatchState.InProgress
            || scene.GameState.MenuPause || scene.GameState.DialogPause || NetSession.FreezeGameplay;
        Tick(Environment.TickCount64, Gain(Sfx.Volume, AudioMixer.GetVolume(AudioBus.Notifications)), suspended);
    }

    internal void Tick(long now, float gain, bool suspended)
    {
        // Muting/pause discards stale pending announcements instead of playing them after returning.
        if (suspended || gain <= 0) { Stop(); return; }
        try
        {
            if (_active != null)
            {
                _active.Gain = gain; // live slider changes affect a currently playing clip
                if (_active.IsPlaying) return;
                _active = null;
            }
            while (_pending.Count > 0)
            {
                var next = _pending.Dequeue();
                if (now - next.Time > MaxQueueAgeMs) continue;
                if (!_voices.TryGetValue(next.Cue, out var voice)) continue;
                voice.Play(gain);
                _active = voice;
                break; // a single announcement voice; never overlap or restart another clip
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ctf-audio] playback unavailable: " + ex.Message);
            Dispose(); _attempted = true;
        }
    }

    internal void Initialize(Func<CtfAnnouncementCue, ICtfAnnouncementVoice> create)
    {
        try
        {
            foreach (var cue in Enum.GetValues<CtfAnnouncementCue>())
                _voices.Add(cue, create(cue));
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ctf-audio] loading unavailable: " + ex.Message);
            Dispose(); _attempted = true;
        }
    }

    internal void Stop()
    {
        _pending.Clear();
        try { _active?.Stop(); }
        catch (Exception ex) { Console.WriteLine("[ctf-audio] stop unavailable: " + ex.Message); }
        finally { _active = null; }
    }

    public void Dispose()
    {
        Stop();
        foreach (var voice in _voices.Values)
            try { voice.Dispose(); }
            catch (Exception ex) { Console.WriteLine("[ctf-audio] release unavailable: " + ex.Message); }
        _voices.Clear();
        _match = 0; _epoch = 0; _lastEvent = 0;
        _attempted = false;
    }
}

// Small injectable seam for testing the production scheduler without pretending a fake device is audible.
internal interface ICtfAnnouncementVoice : IDisposable
{
    bool IsPlaying { get; }
    float Gain { set; }
    void Play(float gain);
    void Stop();
}

internal sealed class CtfAnnouncementVoice : ICtfAnnouncementVoice
{
    private readonly MemoryStream _stream;
    private readonly StreamDataProvider _provider;
    private readonly SoundPlayer _player;
    public bool IsPlaying => _player.State == PlaybackState.Playing;
    public float Gain { set => _player.Volume = value; }

    internal CtfAnnouncementVoice(byte[] wav)
    {
        _stream = new MemoryStream(wav, writable: false);
        try
        {
            _provider = new StreamDataProvider(MusicPlayer.Engine!, MusicPlayer.Format, _stream);
            _player = new SoundPlayer(MusicPlayer.Engine!, MusicPlayer.Format, _provider) { IsLooping = false };
            MusicPlayer.PlaybackDevice!.MasterMixer.AddComponent(_player);
        }
        catch
        {
            _player?.Dispose(); _provider?.Dispose(); _stream.Dispose();
            throw;
        }
    }

    public void Play(float gain)
    {
        _player.Stop();
        _player.Seek(0); // resets SoundPlayer's internal resampler as well as the provider
        _player.Volume = gain;
        _player.Play();
        MusicPlayer.PlaybackDevice!.Start();
    }
    public void Stop() => _player.Stop();
    public void Dispose()
    {
        _player.Stop();
        MusicPlayer.PlaybackDevice?.MasterMixer.RemoveComponent(_player);
        _player.Dispose(); _provider.Dispose(); _stream.Dispose();
    }
}
