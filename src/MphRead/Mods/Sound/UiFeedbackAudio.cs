using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MphRead.Sound;
using SoundFlow.Components;
using SoundFlow.Providers;

namespace MphRead.Mods.Sound
{
    internal enum UiFeedbackCue
    {
        Navigate,
        Confirm,
        Back,
        Error
    }

    /// <summary>
    /// Tiny synthesized launcher cues.
    ///
    /// These deliberately do not use game-bank samples: the launcher has to
    /// sound complete before extracted game files exist, and UI feedback must
    /// remain independent of any copyrighted source asset. Cues are generated
    /// once as PCM WAV data and played through the same SoundFlow output used
    /// by the rest of Project Prime.
    /// </summary>
    internal static class UiFeedbackAudio
    {
        private static readonly object _gate = new();
        private static readonly Dictionary<UiFeedbackCue, CuePlayer> _players = new();
        private static long _lastNavigateMs = -1;

        public static void Play(UiFeedbackCue cue)
        {
            if (MphRead.Mods.Headless.Active || MphRead.Mods.ThumbnailMode.Active
                || !MusicPlayer.Available || MusicPlayer.Engine == null
                || MusicPlayer.PlaybackDevice == null)
            {
                return;
            }

            long now = Environment.TickCount64;
            lock (_gate)
            {
                if (cue == UiFeedbackCue.Navigate
                    && _lastNavigateMs >= 0 && now - _lastNavigateMs < 45)
                {
                    return;
                }
                if (cue == UiFeedbackCue.Navigate)
                    _lastNavigateMs = now;

                if (!_players.TryGetValue(cue, out CuePlayer? player))
                {
                    try
                    {
                        player = new CuePlayer(Build(cue));
                        _players.Add(cue, player);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[sound] UI feedback unavailable ({ex.Message})");
                        return;
                    }
                }

                float cueGain = cue switch
                {
                    UiFeedbackCue.Navigate => 0.22f,
                    UiFeedbackCue.Back => 0.25f,
                    UiFeedbackCue.Error => 0.28f,
                    _ => 0.30f
                };
                float gain = Math.Clamp(
                    Sfx.Volume * AudioMixer.GetVolume(AudioBus.Notifications) * cueGain,
                    0, 1);
                if (gain > 0)
                    player.Play(gain);
            }
        }

        public static void Shutdown()
        {
            lock (_gate)
            {
                foreach (CuePlayer player in _players.Values)
                    player.Dispose();
                _players.Clear();
                _lastNavigateMs = -1;
            }
        }

        internal static byte[] Build(UiFeedbackCue cue)
        {
            const int sampleRate = 22050;
            (double seconds, double startHz, double endHz, double harmonic) = cue switch
            {
                UiFeedbackCue.Navigate => (0.034, 720, 900, 0.08),
                UiFeedbackCue.Confirm => (0.060, 610, 1040, 0.12),
                UiFeedbackCue.Back => (0.050, 590, 390, 0.08),
                UiFeedbackCue.Error => (0.080, 230, 170, 0.22),
                _ => (0.040, 600, 600, 0.08)
            };

            int frames = Math.Max(1, (int)Math.Round(sampleRate * seconds));
            using var stream = new MemoryStream(44 + frames * 2);
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + frames * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(frames * 2);

            double phase = 0;
            for (int i = 0; i < frames; i++)
            {
                double t = frames <= 1 ? 1 : i / (double)(frames - 1);
                double frequency = startHz + (endHz - startHz) * t;
                phase += Math.PI * 2 * frequency / sampleRate;

                double attack = Math.Min(1, t / 0.12);
                double release = Math.Min(1, (1 - t) / 0.42);
                double envelope = Math.Max(0, Math.Min(attack, release));
                double sample = Math.Sin(phase);
                if (harmonic > 0)
                    sample += Math.Sin(phase * 1.51) * harmonic;
                sample *= envelope / (1 + harmonic);

                writer.Write((short)Math.Clamp(
                    (int)Math.Round(sample * short.MaxValue * 0.78),
                    short.MinValue, short.MaxValue));
            }
            writer.Flush();
            return stream.ToArray();
        }

        private sealed class CuePlayer : IDisposable
        {
            private readonly MemoryStream _stream;
            private readonly StreamDataProvider _provider;
            private readonly SoundPlayer _player;

            public CuePlayer(byte[] data)
            {
                _stream = new MemoryStream(data, writable: false);
                _provider = new StreamDataProvider(
                    MusicPlayer.Engine!, MusicPlayer.Format, _stream);
                _player = new SoundPlayer(
                    MusicPlayer.Engine!, MusicPlayer.Format, _provider);
                MusicPlayer.PlaybackDevice!.MasterMixer.AddComponent(_player);
                MusicPlayer.PlaybackDevice.Start();
            }

            public void Play(float gain)
            {
                _player.Stop();
                _provider.Seek(0);
                _player.Volume = gain;
                _player.Play();
            }

            public void Dispose()
            {
                _player.Stop();
                MusicPlayer.PlaybackDevice?.MasterMixer.RemoveComponent(_player);
                _player.Dispose();
                _provider.Dispose();
                _stream.Dispose();
            }
        }
    }
}
