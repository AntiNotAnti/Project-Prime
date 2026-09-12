using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Formats.Sound;
using MphRead.Mods.Audio;
using MphRead.Mods.Content;
using NCSF123;
using NCSFPlayer;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace MphRead
{
    public static class Music
    {
        public static float UserVolume { get; set; } = 1;

        /// <summary>
        /// Set the player's own volume and push it to whatever is playing.
        ///
        /// Assigning UserVolume alone is not enough: the gain only reaches the
        /// stream when a track starts (MusicPlayer.Play) or while a fade is
        /// running, so a volume changed mid-match took effect at the next
        /// track and looked like a setting that did nothing.
        /// </summary>
        public static void SetUserVolume(float volume)
        {
            UserVolume = Math.Clamp(volume, 0, 1);
            _optionalPresentation?.SetVolume(Volume);
            if (MusicPlayer.State != PlaybackState.Stopped)
            {
                MusicPlayer.Volume = Volume;
            }
        }
        public static float MusicVolume { get; set; } = 1;

        public static float Volume => UserVolume * MusicVolume;

        private static IReadOnlyList<MusicTrack> _musicInfo = null!;
        private static IReadOnlyList<RoomMusic> _roomMusic = null!;
        private static OptionalMusicPresentation? _optionalPresentation;

        private static bool _playing = false;
        private static bool _paused = false;
        public static bool IsPaused => _paused;
        private static bool _nextTrackNotReady = false;
        private static bool _isReady = false;
        private static bool _musicQueued = false;
        private static ushort _nextFadeInFrames = 0;

        private static MusicId _currentMusicId = MusicId.None;
        private static SeqId _currentMusicSeq = SeqId.None;
        private static SeqId _nextMusicSeq = SeqId.None;


        private static ushort _nextTracks = 0;
        private static ushort _pendingTracks = 0;
        private static ushort _activeTracks = 0;
        private static ushort _mutedTracks = 0;
        private static ushort _fadingTracks = 0;

        /// <summary>
        /// The small piece of static mixer state a temporary replay may borrow.
        /// It intentionally excludes handles and decoder objects: those are
        /// owned by MusicPlayer and are rebuilt through the normal sequence
        /// path on restore.
        /// </summary>
        internal readonly record struct PresentationAudioSnapshot(
            MusicId MusicId, SeqId MusicSequence, SeqId NextSequence,
            ushort ActiveTracks, ushort PendingTracks, ushort NextTracks,
            bool Playing, bool Paused, bool Queued, float MusicVolume,
            ushort BaseTempo, bool Available)
        {
            internal bool IsValid => Available;
        }

        internal static PresentationAudioSnapshot CapturePresentationAudio()
        {
            if (_musicInfo == null || _roomMusic == null)
                return default;
            return new(_currentMusicId, _currentMusicSeq, _nextMusicSeq,
                _activeTracks, _pendingTracks, _nextTracks, _playing, _paused,
                _musicQueued, MusicVolume, _baseTempo, Available: true);
        }

        /// <summary>
        /// Restore a snapshot only through the existing music sequence loader.
        /// No room lookup is performed, so a replay cannot replace the live
        /// scene's selected track with a guessed room default.
        /// </summary>
        internal static bool TryRestorePresentationAudio(
            in PresentationAudioSnapshot snapshot)
        {
            if (!snapshot.IsValid || _musicInfo == null) return false;
            try
            {
                if (snapshot.MusicSequence != SeqId.None)
                {
                    PlaySeq(snapshot.MusicSequence,
                        snapshot.ActiveTracks == 0 ? UInt16.MaxValue : snapshot.ActiveTracks,
                        queue: false, notReady: false);
                }
                else if (snapshot.MusicId != MusicId.None)
                {
                    PlayMusic(snapshot.MusicId,
                        snapshot.PendingTracks == 0 ? null : snapshot.PendingTracks,
                        toggleOnTracks: false, toggleOffTracks: false);
                }
                else
                {
                    Stop();
                }

                _currentMusicId = snapshot.MusicId;
                _currentMusicSeq = snapshot.MusicSequence;
                _nextMusicSeq = snapshot.NextSequence;
                _activeTracks = snapshot.ActiveTracks;
                _pendingTracks = snapshot.PendingTracks;
                _nextTracks = snapshot.NextTracks;
                _playing = snapshot.Playing;
                _paused = snapshot.Paused;
                _musicQueued = snapshot.Queued;
                MusicVolume = snapshot.MusicVolume;
                _baseTempo = snapshot.BaseTempo;
                UpdateTempo(snapshot.BaseTempo, 0);
                if (snapshot.Paused) MusicPlayer.Pause();
                else if (snapshot.Playing) MusicPlayer.Play(Volume);
                return true;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Mods.DebugLog.Exception("music-restore", error);
                return false;
            }
        }

        public static void Init(ClientPresentationContentState? content = null)
        {
            _optionalPresentation?.Dispose();
            _optionalPresentation = null;
            content ??= ClientPresentationContent.Refresh();
            if (content.Music.Pack is { } music && content.MusicAssets is { } assets)
            {
                _optionalPresentation = new OptionalMusicPresentation(new OptionalMusicPack(music, assets));
            }
            _musicInfo = SoundRead.ReadInterMusicInfo();
            _roomMusic = SoundRead.ReadAssignMusic();
            _pendingTracks = 0;
            _currentMusicId = MusicId.None;
            _playing = false;
            _paused = false;
            _nextTrackNotReady = false;
            _isReady = false;
            _currentMusicSeq = SeqId.None;
            _musicQueued = false;
            _nextMusicSeq = SeqId.None;
            _nextFadeInFrames = 0;
            _nextTracks = 0;
            _tempoUpdateStart = 256;
            _tempoUpdateTarget = 256;
            _tempoUpdateTimeMs = 0;
            _tempoUpdateTimer.Reset();
            _volumeFadeStart = 1;
            _volumeFadeTarget = 1;
            _volumeFadeTimeMs = 0;
            _volumeFadeTimer.Reset();
            _stopAfterFade = false;
            for (int i = 0; i < _trackFaders.Length; i++)
            {
                _trackFaders[i].Reset();
            }
            if (!MusicPlayer.Available)
            {
                return;
            }
            // play empty seq to ensure initialization
            MusicPlayer.Load(SeqId.WIN);
            MusicPlayer.WaitForLoad();
            MusicPlayer.Play(Volume);
            MusicPlayer.Stop();
        }

        public static void PlayMusic(MusicId musicId, ushort? tracks = null, bool toggleOnTracks = false, bool toggleOffTracks = false)
            => PlayMusic(musicId, tracks, toggleOnTracks, toggleOffTracks, allowOptional: true);

        private static void PlayMusic(MusicId musicId, ushort? tracks, bool toggleOnTracks,
            bool toggleOffTracks, bool allowOptional)
        {
            int index = (int)musicId;
            if (index < 0 || index >= _musicInfo.Count)
            {
                return;
            }
            MusicTrack info = _musicInfo[index];
            if (info.SeqId == SeqId.None)
            {
                return;
            }
            if (allowOptional && TryPlayOptional(index, 0, musicId)) return;
            if (!tracks.HasValue)
            {
                tracks = info.Tracks;
            }
            if (toggleOnTracks)
            {
                _pendingTracks |= tracks.Value;
            }
            else if (toggleOffTracks)
            {
                _pendingTracks &= (ushort)(~tracks.Value);
            }
            else
            {
                _pendingTracks = tracks.Value;
            }
            _currentMusicId = musicId;
            _paused = false;
            PlaySeq(info.SeqId, _pendingTracks, queue: true, notReady: true, info.FadeOutFrames, info.FadeInFrames);
        }

        public static void TryPlayRoomMusic(int roomId, int track)
        {
            PlayRoomMusic(roomId, track);
        }

        public static void PlayRoomMusic(int roomId, int track)
        {
            track = Math.Clamp(track, 0, 2);
            for (int i = 0; i < _roomMusic.Count; i++)
            {
                RoomMusic room = _roomMusic[i];
                if (room.RoomId == roomId)
                {
                    MusicId musicId = (MusicId)room.TrackIds[track];
                    if (!TryPlayOptional(roomId, track, musicId))
                        PlayMusic(musicId, tracks: null, toggleOnTracks: false,
                            toggleOffTracks: false, allowOptional: false);
                    return;
                }
            }
        }

        private static bool TryPlayOptional(int contextId, int variant, MusicId musicId)
        {
            if (_optionalPresentation?.TryPlay(contextId, variant, Volume) != true) return false;
            MusicPlayer.Stop();
            _currentMusicId = musicId;
            _playing = false;
            _paused = false;
            return true;
        }

        public static void PlaySeq(SeqId seqId, bool notReady = true)
        {
            PlaySeq(seqId, UInt16.MaxValue, notReady: notReady);
        }

        public static void PlaySeq(SeqId seqId, ushort tracks, bool queue = false, bool notReady = false,
            ushort fadeOutFrames = 0, ushort fadeInFrames = 0)
        {
            if (Mods.Network.ReplayPlayback.IsSeeking) return;
            // the game may update the seq ID first using download play values
            if (!queue)
            {
                _nextTrackNotReady = false;
                Stop();
                _isReady = !notReady;
                _activeTracks = tracks;
                // init seq
                // the game has a redundant check for stopping the music again that preserves the ready value
                _currentMusicSeq = seqId;
                _mutedTracks = 0;
                _fadingTracks = (ushort)(~_activeTracks);
                // the game sets up a possible volume factor from the seq here, which NCSF should handle internally
                MusicVolume = 1;
                _volumeFadeTarget = 1;
                _volumeFadeTimer.Stop();
                for (int i = 0; i < _trackFaders.Length; i++)
                {
                    _trackFaders[i].Reset(volume: (byte)((_activeTracks & (1 << i)) != 0 ? 127 : 0));
                }
                // start music player
                MusicPlayer.Load(seqId, tracks);
                _playing = true;
                if (_isReady)
                {
                    MusicPlayer.WaitForLoad();
                    MusicPlayer.Play(Volume);
                    _mutedTracks = 0;
                    UpdateTrackVolume(_fadingTracks, 0);
                    _fadingTracks = 0;
                }
                UpdateTempo(256, 0);
                _musicQueued = false;
                _nextMusicSeq = seqId;
            }
            else
            {
                if (!_musicQueued)
                {
                    if (_nextMusicSeq == seqId)
                    {
                        if (_isReady)
                        {
                            SetTrackFaders(tracks, target: 127, time: fadeInFrames / 30f);
                            SetTrackFaders((ushort)(tracks ^ UInt16.MaxValue), target: 0, time: fadeOutFrames / 30f);
                        }
                        else
                        {
                            _activeTracks = tracks;
                        }
                        return;
                    }
                    // the game sets the fade out frames in an unused music state field
                    // the game checks a pause flag we don't have before calling stop
                    Stop(fadeOutFrames / 30f);
                    _musicQueued = true;
                }
                _nextTrackNotReady = notReady;
                _nextMusicSeq = seqId;
                _nextTracks = tracks;
                _nextFadeInFrames = fadeInFrames;
            }
        }

        public static void UpdateMusic()
        {
            if (_optionalPresentation?.Active == true)
            {
                _optionalPresentation.Update(Volume);
                if (_optionalPresentation.ConsumeVerificationFailure())
                {
                    MusicId fallback = _currentMusicId;
                    _optionalPresentation.Stop();
                    PlayMusic(fallback, tracks: null, toggleOnTracks: false,
                        toggleOffTracks: false, allowOptional: false);
                    return;
                }
                ProcessVolume();
                return;
            }
            if (!_isReady && !MusicPlayer.Loading)
            {
                _isReady = true;
                if (_playing)
                {
                    MusicPlayer.Volume = Volume;
                    MusicPlayer.Tempo = _baseTempo;
                    MusicPlayer.Play(Volume);
                    // the game applies the likely frontend pause flag to the player, as well as lid mute/unmute
                    for (int i = 0; i < _trackFaders.Length; i++)
                    {
                        if ((_fadingTracks & (1 << i)) == 0)
                        {
                            _fadingTracks |= (ushort)(1 << i);
                            _mutedTracks = 0;
                            _trackFaders[i].TimeMs = Single.Epsilon;
                        }
                    }
                }
            }
            // the game checks the likely frontend pause flag before proceeding
            if (!_isReady || MusicPlayer.State != PlaybackState.Stopped)
            {
                // if not ready, there's no player to update, but we can still update our own fields
                ProcessVolume();
                ProcessTempo();
                ProcessTrackFaders();
            }
            else if (_musicQueued)
            {
                // if ready and stopped, we can proceed to queued music
                PlaySeq(_nextMusicSeq, _nextTracks, queue: false, _nextTrackNotReady, fadeOutFrames: 0, _nextFadeInFrames);
            }
        }

        public static void UpdateMusicIdIfPaused(MusicId musicId)
        {
            if (_paused)
            {
                _currentMusicId = musicId;
            }
        }

        public static void PlayPausedMusic()
        {
            if (!_paused)
            {
                return;
            }
            if (_optionalPresentation?.Resume() == true)
            {
                _paused = false;
                return;
            }
            int index = (int)_currentMusicId;
            if (index < 0 || index >= _musicInfo.Count)
            {
                return;
            }
            MusicTrack info = _musicInfo[index];
            if (info.SeqId == SeqId.None)
            {
                return;
            }
            _paused = false;
            PlaySeq(info.SeqId, _pendingTracks, queue: true, notReady: true);
        }

        public static void Pause()
        {
            if (!_paused)
            {
                if (_optionalPresentation?.Pause() != true) Stop();
                _paused = true;
            }
        }

        public static void Stop(float fadeTime = 0)
        {
            _optionalPresentation?.Stop();
            _playing = false;
            _musicQueued = false;
            _nextMusicSeq = SeqId.None;
            if (!_isReady && MusicPlayer.Loading)
            {
                MusicPlayer.StopLoading = true;
            }
            _isReady = true;
            if (fadeTime <= 0)
            {
                MusicPlayer.Stop();
            }
            else
            {
                FadeVolume(0, fadeTime, stopAfterFade: true);
            }
        }

        private static float _volumeFadeStart = 1;
        private static float _volumeFadeTarget = 1;
        private static float _volumeFadeTimeMs = 0;
        private static readonly Stopwatch _volumeFadeTimer = new Stopwatch();
        // kind of a hack since there's no connection between fading out and stopping
        private static bool _stopAfterFade = false;

        public static void FadeVolume(float volume, float time, bool stopAfterFade = false)
        {
            float optionalStart = MusicVolume;
            MusicVolume = volume;
            _volumeFadeStart = _optionalPresentation?.Active == true
                ? optionalStart : MusicPlayer.Volume;
            _volumeFadeTarget = volume;
            _volumeFadeTimeMs = time * 1000;
            if (_volumeFadeTimeMs <= 0)
            {
                _volumeFadeTimeMs = 1;
            }
            _volumeFadeTimer.Restart();
            _stopAfterFade = stopAfterFade;
        }

        private static void ProcessVolume()
        {
            if (_volumeFadeTimer.IsRunning)
            {
                float pct = _volumeFadeTimer.ElapsedMilliseconds / _volumeFadeTimeMs;
                if (pct >= 1)
                {
                    ApplyMusicVolume(_volumeFadeTarget);
                    _volumeFadeTimer.Stop();
                    if (_stopAfterFade)
                    {
                        _optionalPresentation?.Stop();
                        MusicPlayer.Stop();
                    }
                    _stopAfterFade = false;
                }
                else
                {
                    ApplyMusicVolume(_volumeFadeStart + (_volumeFadeTarget - _volumeFadeStart) * pct);
                }
                MusicPlayer.Volume = Volume;
            }
        }

        private static void ApplyMusicVolume(float volume)
        {
            MusicVolume = volume;
            _optionalPresentation?.SetVolume(Volume);
        }

        private static ushort _baseTempo = 256;
        private static ushort _tempoUpdateStart = 256;
        private static ushort _tempoUpdateTarget = 256;
        private static float _tempoUpdateTimeMs = 0;
        private static readonly Stopwatch _tempoUpdateTimer = new Stopwatch();

        public static void UpdateTempo(ushort tempo, float time)
        {
            if (time <= 0)
            {
                MusicPlayer.Tempo = tempo;
                _baseTempo = tempo;
                _tempoUpdateStart = _tempoUpdateTarget = tempo;
                _tempoUpdateTimeMs = 0;
                _tempoUpdateTimer.Reset();
            }
            else if (_tempoUpdateTarget != tempo)
            {
                _tempoUpdateStart = MusicPlayer.Tempo;
                _tempoUpdateTarget = tempo;
                _tempoUpdateTimeMs = time * 1000;
                _tempoUpdateTimer.Restart();
            }
        }

        private static void ProcessTempo()
        {
            if (MusicPlayer.Tempo != _tempoUpdateTarget && _tempoUpdateTimer.IsRunning)
            {
                float pct = _tempoUpdateTimer.ElapsedMilliseconds / _tempoUpdateTimeMs;
                if (pct >= 1)
                {
                    MusicPlayer.Tempo = _tempoUpdateTarget;
                    _tempoUpdateTimer.Stop();
                }
                else
                {
                    MusicPlayer.Tempo = (ushort)(_tempoUpdateStart + (_tempoUpdateTarget - _tempoUpdateStart) * pct);
                }
            }
        }

        private class TrackFader
        {
            public byte Start { get; set; } = 127;
            public byte Target { get; set; } = 127;
            public float TimeMs { get; set; }
            public Stopwatch Timer { get; } = new Stopwatch();

            public void Reset(byte volume = 127)
            {
                Start = volume;
                Target = volume;
                TimeMs = 0;
                Timer.Reset();
            }
        }

        private static readonly ImmutableArray<TrackFader> _trackFaders =
        [
            new TrackFader(), new TrackFader(), new TrackFader(), new TrackFader(),
            new TrackFader(), new TrackFader(), new TrackFader(), new TrackFader(),
            new TrackFader(), new TrackFader(), new TrackFader(), new TrackFader(),
            new TrackFader(), new TrackFader(), new TrackFader(), new TrackFader()
        ];

        private static void UpdateTrackVolume(ushort tracks, byte volume)
        {
            if (volume > 0)
            {
                for (int i = 0; i < _trackFaders.Length; i++)
                {
                    if ((tracks & (1 << i)) != 0)
                    {
                        NCSFCommon.Track? track = MusicPlayer.GetTrack(i);
                        if (track != null)
                        {
                            track.Volume = volume;
                        }
                    }
                }
                if ((_mutedTracks & tracks) != 0)
                {
                    if (_isReady)
                    {
                        for (int i = 0; i < _trackFaders.Length; i++)
                        {
                            if ((tracks & (1 << i)) != 0)
                            {
                                NCSFCommon.Track? track = MusicPlayer.GetTrack(i);
                                if (track != null)
                                {
                                    track.Mute = false;
                                }
                            }
                        }
                    }
                    _mutedTracks &= (ushort)(~(_mutedTracks & tracks));
                }
            }
            else if ((_mutedTracks & tracks) != tracks)
            {
                if (_isReady)
                {
                    for (int i = 0; i < _trackFaders.Length; i++)
                    {
                        if ((tracks & (1 << i)) != 0)
                        {
                            NCSFCommon.Track? track = MusicPlayer.GetTrack(i);
                            if (track != null)
                            {
                                track.Mute = true;
                            }
                        }
                    }
                }
                _mutedTracks |= tracks;
            }
        }

        private static void SetTrackFaders(ushort tracks, byte target, float time)
        {
            if (target == 0)
            {
                _activeTracks &= (ushort)~tracks;
            }
            else
            {
                _activeTracks |= tracks;
            }
            for (int i = 0; i < _trackFaders.Length; i++)
            {
                if ((tracks & (1 << i)) != 0)
                {
                    TrackFader trackFader = _trackFaders[i];
                    NCSFCommon.Track? track = MusicPlayer.GetTrack(i);
                    if (track != null)
                    {
                        trackFader.Start = track.Volume;
                    }
                    trackFader.Target = target;
                    trackFader.TimeMs = time * 1000;
                    trackFader.Timer.Restart();
                }
            }
            _fadingTracks |= tracks;
        }

        private static void ProcessTrackFaders()
        {
            if (_fadingTracks == 0)
            {
                return;
            }
            for (int i = 0; i < _trackFaders.Length; i++)
            {
                if ((_fadingTracks & (1 << i)) == 0)
                {
                    continue;
                }
                TrackFader trackFader = _trackFaders[i];
                if (trackFader.Timer.IsRunning)
                {
                    NCSFCommon.Track? track = MusicPlayer.GetTrack(i);
                    if (track != null && track.Volume != trackFader.Target)
                    {
                        float pct = trackFader.Timer.ElapsedMilliseconds / trackFader.TimeMs;
                        if (pct >= 1)
                        {
                            UpdateTrackVolume((ushort)(1 << i), trackFader.Target);
                            trackFader.Timer.Stop();
                            _fadingTracks &= (ushort)(~(1 << i));
                        }
                        else
                        {
                            UpdateTrackVolume((ushort)(1 << i), (byte)(trackFader.Start + (trackFader.Target - trackFader.Start) * pct));
                        }
                        track.Mute = track.Volume == 0;
                    }
                }
            }
        }
    }

    public static class MusicPlayer
    {
        // Public callers may arrive from the renderer, scene transition, or
        // an optional-presentation completion. Keep teardown as one ordered
        // operation even though decoding itself remains off-thread.
        private static readonly object _lifecycleGate = new();
        private static readonly object _stateGate = new();
        private static MiniAudioEngine _audioEngine;
        private static AudioPlaybackDevice _playbackDevice;
        private static RawDataProvider? _provider = null;
        private static NCSFPlayerStream? _stream = null;
        private static SoundPlayer? _player = null;
        private static readonly AudioFormat _format;
        private static Task _loadTail = Task.CompletedTask;
        private static CancellationTokenSource? _loadCancellation;
        private static int _loadGeneration;
        private static int _loading;
        private static int _stopLoading;
        private static bool _shutdownRequested;

        private const int _sampleRate = 32728;

        /// <summary>
        /// False when no playback device could be opened. The game then runs
        /// in silence instead of not running: a machine with no sound card,
        /// an audio server that is not up yet, or a second copy of the game
        /// holding the device would otherwise take the whole process down
        /// with a TypeInitializationException from a static constructor,
        /// before a single frame was drawn.
        /// </summary>
        public static bool Available { get; private set; }

        /// <summary>
        /// The one output the process mixes into, for anything else that has
        /// to share it.
        ///
        /// Android has no OpenAL native, so the sound effects there are mixed
        /// by <c>Mods.Sound.AlEs</c> and handed to this same device rather than
        /// opening a second one: two playback devices on a phone is two audio
        /// callbacks, two buffers of latency, and whichever one the system
        /// decides to duck.
        /// </summary>
        public static MiniAudioEngine? Engine
        {
            get { lock (_stateGate) return Available ? _audioEngine : null; }
        }

        public static AudioPlaybackDevice? PlaybackDevice
        {
            get { lock (_stateGate) return Available ? _playbackDevice : null; }
        }

        public static AudioFormat Format => _format;

        static MusicPlayer()
        {
            _format = new AudioFormat()
            {
                SampleRate = _sampleRate,
                Channels = 2,
                Format = SampleFormat.F32
            };
            try
            {
                lock (_stateGate)
                {
                    _audioEngine = new MiniAudioEngine();
                    _playbackDevice = _audioEngine.InitializePlaybackDevice(deviceInfo: null, _format);
                    Available = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[sound] no audio device ({ex.Message}); continuing without sound");
                lock (_stateGate)
                {
                    _audioEngine = null!;
                    _playbackDevice = null!;
                    Available = false;
                }
            }
        }

        public static bool Loading => Volatile.Read(ref _loading) != 0;
        public static bool StopLoading
        {
            get => Volatile.Read(ref _stopLoading) != 0;
            set => Volatile.Write(ref _stopLoading, value ? 1 : 0);
        }

        public static void Load(SeqId seqId, ushort tracks = UInt16.MaxValue, float volume = 1)
        {
            lock (_lifecycleGate)
            {
                lock (_stateGate)
                {
                    if (!Available || _shutdownRequested)
                    {
                        return;
                    }
                    StopCore();
                    _loadCancellation?.Cancel();
                    _loadCancellation = null;
                    int generation = ++_loadGeneration;
                    if (seqId == SeqId.None)
                    {
                        StopLoading = true;
                        Volatile.Write(ref _loading, 0);
                        return;
                    }
                    CancellationTokenSource cancellation = new();
                    _loadCancellation = cancellation;
                    StopLoading = false;
                    Task previous = _loadTail;
                    Volatile.Write(ref _loading, 1);
                    _loadTail = Task.Run(() =>
                    {
                        try { previous.GetAwaiter().GetResult(); }
                        catch (Exception error) when (error is not OutOfMemoryException)
                        { Console.WriteLine($"[sound] previous music load failed: {error.Message}"); }
                        LoadCore(seqId, tracks, volume, generation, cancellation.Token);
                    });
                }
            }
        }

        private static void LoadCore(SeqId seqId, ushort tracks, float volume,
            int generation, CancellationToken cancellation)
        {
            NCSFPlayerStream? stream = null;
            RawDataProvider? provider = null;
            SoundPlayer? player = null;
            bool attached = false;
            try
            {
                // The previous load has completed before this worker starts,
                // so RemoveLoaded cannot race a decoder or its SoundFlow
                // component. Decoder implementation and timing are unchanged.
                lock (_stateGate) RemoveLoaded();
                if (ShouldStop(generation, cancellation)) return;
                string path = Paths.Combine(Paths.FileSystem, "_seq", Metadata.SequenceFiles[(int)seqId]);
                // todo: should look at allocations (including recreating these objects, but especially the byte and float lists internal to NCSF)
                stream = new NCSFPlayerStream(path, (uint)_sampleRate, Interpolation.None, skipSilenceOnStartSec: 5,
                    defaultLengthInMS: 115000, defaultFadeInMS: 5000, NCSF123.VolumeType.ReplayGainAlbum, PeakType.ReplayGainTrack,
                    playForever: true, volume, channelMutes: 0, trackMutes: 0, ignoreVolume: false);
                // use volume and mute directly instead of trackMutes to make it easy to potentially fade them in later without the player interfering
                for (int i = 0; i < 16; i++)
                {
                    if ((tracks & (1 << i)) == 0)
                    {
                        NCSFCommon.Track? track = stream.Player.GetTrack(i);
                        if (track != null)
                        {
                            track.Volume = 0;
                            track.Mute = true;
                        }
                    }
                }
                if (ShouldStop(generation, cancellation)) return;
                provider = new RawDataProvider(stream, SampleFormat.F32, _sampleRate);
                if (ShouldStop(generation, cancellation)) return;
                player = new SoundPlayer(_audioEngine, _format, provider);
                if (ShouldStop(generation, cancellation)) return;
                lock (_stateGate)
                {
                    if (ShouldStop(generation, cancellation)) return;
                    _playbackDevice.MasterMixer.AddComponent(player);
                    try
                    {
                        _playbackDevice.Start();
                    }
                    catch
                    {
                        _playbackDevice.MasterMixer.RemoveComponent(player);
                        throw;
                    }
                    // Publish the complete graph only after it is attached to
                    // the process output. Other callers therefore never see
                    // a half-built stream/provider/player during a transition.
                    _stream = stream;
                    _provider = provider;
                    _player = player;
                    stream = null;
                    provider = null;
                    player = null;
                    attached = true;
                }
            }
            finally
            {
                if (!attached) DisposeUnattached(player, provider, stream);
                lock (_stateGate)
                {
                    if (generation == _loadGeneration)
                    {
                        Volatile.Write(ref _loading, 0);
                        StopLoading = false;
                    }
                }
            }
        }

        private static void DisposeUnattached(SoundPlayer? player,
            RawDataProvider? provider, NCSFPlayerStream? stream)
        {
            if (player != null)
            {
                try { player.Stop(); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { Console.WriteLine($"[sound] music player stop failed: {error.Message}"); }
                try { player.Dispose(); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { Console.WriteLine($"[sound] music player disposal failed: {error.Message}"); }
            }
            try { provider?.Dispose(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { Console.WriteLine($"[sound] music provider disposal failed: {error.Message}"); }
            try { stream?.Dispose(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { Console.WriteLine($"[sound] music stream disposal failed: {error.Message}"); }
        }

        private static bool ShouldStop(int generation, CancellationToken cancellation)
            => cancellation.IsCancellationRequested || StopLoading
                || generation != Volatile.Read(ref _loadGeneration);

        public static void WaitForLoad(int sleepMs = 100)
        {
            _ = sleepMs; // retained for source compatibility with callers
            Task load;
            lock (_stateGate) load = _loadTail;
            try
            {
                load.GetAwaiter().GetResult();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Console.WriteLine($"[sound] music load failed: {error.Message}");
            }
        }

        public static void Play(float volume)
        {
            lock (_lifecycleGate)
            {
                lock (_stateGate)
                {
                    if (!Available) return;
                    Volume = volume;
                    _player?.Play();
                }
            }
        }

        public static void Pause()
        {
            lock (_lifecycleGate)
            {
                lock (_stateGate) _player?.Pause();
            }
        }

        public static PlaybackState State
        {
            get
            {
                lock (_stateGate)
                {
                    if (_player != null) return _player.State;
                    return PlaybackState.Stopped;
                }
            }
        }

        public static float Volume
        {
            get
            {
                lock (_lifecycleGate)
                {
                    lock (_stateGate)
                    {
                        if (_stream != null) return _stream.VolumeModification;
                        return 0;
                    }
                }
            }
            set
            {
                lock (_lifecycleGate)
                {
                    lock (_stateGate)
                    {
                        if (_stream != null) _stream.VolumeModification = Math.Clamp(value, 0, 1);
                    }
                }
            }
        }

        public static ushort Tempo
        {
            get
            {
                lock (_lifecycleGate)
                {
                    lock (_stateGate)
                    {
                        if (_stream != null) return _stream.Player.TempoRatio;
                        return 0;
                    }
                }
            }
            set
            {
                lock (_lifecycleGate)
                {
                    lock (_stateGate)
                    {
                        if (_stream != null) _stream.Player.TempoRatio = value;
                    }
                }
            }
        }

        public static NCSFCommon.Track? GetTrack(int index)
        {
            lock (_stateGate) return _stream?.Player.GetTrack(index);
        }

        public static void Stop()
        {
            lock (_lifecycleGate)
            {
                lock (_stateGate)
                {
                    if (!Available) return;
                    StopCore();
                }
            }
        }

        private static void StopCore()
        {
            if (_player != null)
            {
                _player.Stop();
            }
        }

        public static void Remove(bool shutdown = false)
        {
            lock (_lifecycleGate)
            {
                Task load;
                CancellationTokenSource? cancellation;
                lock (_stateGate)
                {
                    StopLoading = true;
                    cancellation = _loadCancellation;
                    cancellation?.Cancel();
                    load = _loadTail;
                }
                try
                {
                    load.GetAwaiter().GetResult();
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    Console.WriteLine($"[sound] music teardown observed load failure: {error.Message}");
                }
                lock (_stateGate)
                {
                    RemoveLoaded();
                    if (ReferenceEquals(_loadCancellation, cancellation))
                    {
                        _loadCancellation?.Dispose();
                        _loadCancellation = null;
                    }
                    Volatile.Write(ref _loading, 0);
                    StopLoading = false;
                    if (shutdown && Available)
                    {
                        try { _playbackDevice.Stop(); }
                        catch (Exception error) when (error is not OutOfMemoryException)
                        { Console.WriteLine($"[sound] playback device stop failed: {error.Message}"); }
                    }
                }
            }
        }

        private static void RemoveLoaded()
        {
            SoundPlayer? player = _player;
            RawDataProvider? provider = _provider;
            NCSFPlayerStream? stream = _stream;
            _player = null;
            _provider = null;
            _stream = null;
            if (player != null)
            {
                try { player.Stop(); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { Console.WriteLine($"[sound] music player stop failed: {error.Message}"); }
                try { _playbackDevice?.MasterMixer.RemoveComponent(player); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { Console.WriteLine($"[sound] music mixer detach failed: {error.Message}"); }
                try { player.Dispose(); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { Console.WriteLine($"[sound] music player disposal failed: {error.Message}"); }
            }
            try { provider?.Dispose(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { Console.WriteLine($"[sound] music provider disposal failed: {error.Message}"); }
            try { stream?.Dispose(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { Console.WriteLine($"[sound] music stream disposal failed: {error.Message}"); }
        }

        /// <summary>
        /// Final SoundFlow output shutdown.  Normal scene transitions call
        /// <see cref="Remove"/> or <see cref="Stop"/> and retain the process
        /// device; this method is only used by the process-owned service.
        /// </summary>
        public static void ShutdownOutput()
        {
            lock (_lifecycleGate)
            {
                Remove(shutdown: true);
                lock (_stateGate)
                {
                    if (_shutdownRequested) return;
                    _shutdownRequested = true;
                    try { _playbackDevice?.Dispose(); }
                    catch (Exception error) when (error is not OutOfMemoryException)
                    { Console.WriteLine($"[sound] playback device shutdown failed: {error.Message}"); }
                    try { _audioEngine?.Dispose(); }
                    catch (Exception error) when (error is not OutOfMemoryException)
                    { Console.WriteLine($"[sound] audio engine shutdown failed: {error.Message}"); }
                    _playbackDevice = null!;
                    _audioEngine = null!;
                    Available = false;
                }
            }
        }
    }
}
