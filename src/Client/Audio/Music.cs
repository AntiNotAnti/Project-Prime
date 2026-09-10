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
        private static MiniAudioEngine _audioEngine;
        private static AudioPlaybackDevice _playbackDevice;
        private static RawDataProvider? _provider = null;
        private static NCSFPlayerStream? _stream = null;
        private static SoundPlayer? _player = null;
        private static readonly AudioFormat _format;

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
        public static MiniAudioEngine? Engine => Available ? _audioEngine : null;

        public static AudioPlaybackDevice? PlaybackDevice => Available ? _playbackDevice : null;

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
                _audioEngine = new MiniAudioEngine();
                _playbackDevice = _audioEngine.InitializePlaybackDevice(deviceInfo: null, _format);
                Available = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[sound] no audio device ({ex.Message}); continuing without sound");
                _audioEngine = null!;
                _playbackDevice = null!;
                Available = false;
            }
        }

        public static bool Loading { get; private set; }
        public static bool StopLoading { get; set; }

        public static void Load(SeqId seqId, ushort tracks = UInt16.MaxValue, float volume = 1)
        {
            if (!Available)
            {
                return;
            }
            Loading = true;
            Stop();
            if (seqId == SeqId.None)
            {
                Loading = false;
                return;
            }
            Task.Run(() =>
            {
                try
                {
                    while (StopLoading)
                    {
                        Thread.Sleep(1);
                    }
                    Remove();
                    if (StopLoading)
                    {
                        return;
                    }
                    string path = Paths.Combine(Paths.FileSystem, "_seq", Metadata.SequenceFiles[(int)seqId]);
                    // todo: should look at allocations (including recreating these objects, but especially the byte and float lists internal to NCSF)
                    _stream = new NCSFPlayerStream(path, (uint)_sampleRate, Interpolation.None, skipSilenceOnStartSec: 5,
                        defaultLengthInMS: 115000, defaultFadeInMS: 5000, NCSF123.VolumeType.ReplayGainAlbum, PeakType.ReplayGainTrack,
                        playForever: true, volume, channelMutes: 0, trackMutes: 0, ignoreVolume: false);
                    // use volume and mute directly instead of trackMutes to make it easy to potentially fade them in later without the player interfering
                    for (int i = 0; i < 16; i++)
                    {
                        if ((tracks & (1 << i)) == 0)
                        {
                            NCSFCommon.Track? track = MusicPlayer.GetTrack(i);
                            if (track != null)
                            {
                                track.Volume = 0;
                                track.Mute = true;
                            }
                        }
                    }
                    if (StopLoading)
                    {
                        return;
                    }
                    _provider = new RawDataProvider(_stream, SampleFormat.F32, _sampleRate);
                    if (StopLoading)
                    {
                        return;
                    }
                    _player = new SoundPlayer(_audioEngine, _format, _provider);
                    if (StopLoading)
                    {
                        return;
                    }
                    _playbackDevice.MasterMixer.AddComponent(_player);
                    if (StopLoading)
                    {
                        return;
                    }
                    _playbackDevice.Start();
                }
                finally
                {
                    Loading = false;
                    StopLoading = false;
                }
            });
        }

        public static void WaitForLoad(int sleepMs = 100)
        {
            while (MusicPlayer.Loading)
            {
                Thread.Sleep(sleepMs);
            }
        }

        public static void Play(float volume)
        {
            if (!Available)
            {
                return;
            }
            Volume = volume;
            _player?.Play();
        }

        public static void Pause()
        {
            _player?.Pause();
        }

        public static PlaybackState State
        {
            get
            {
                if (_player != null)
                {
                    return _player.State;
                }
                return PlaybackState.Stopped;
            }
        }

        public static float Volume
        {
            get
            {
                if (_stream != null)
                {
                    return _stream.VolumeModification;
                }
                return 0;
            }
            set
            {
                if (_stream != null)
                {
                    _stream.VolumeModification = Math.Clamp(value, 0, 1);
                }
            }
        }

        public static ushort Tempo
        {
            get
            {
                if (_stream != null)
                {
                    return _stream.Player.TempoRatio;
                }
                return 0;
            }
            set
            {
                if (_stream != null)
                {
                    _stream.Player.TempoRatio = value;
                }
            }
        }

        public static NCSFCommon.Track? GetTrack(int index)
        {
            return _stream?.Player.GetTrack(index);
        }

        public static void Stop()
        {
            if (!Available)
            {
                return;
            }
            if (_player != null)
            {
                _player.Stop();
            }
        }

        public static void Remove(bool shutdown = false)
        {
            if (_player != null)
            {
                Debug.Assert(_provider != null);
                Debug.Assert(_stream != null);
                _player.Stop();
                if (shutdown)
                {
                    _playbackDevice.Stop();
                }
                _playbackDevice.MasterMixer.RemoveComponent(_player);
                _provider.Dispose();
                _stream.Dispose();
                _player.Dispose();
                _provider = null;
                _stream = null;
                _player = null;
            }
        }
    }
}
