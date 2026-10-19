using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MphRead.Mods.Cosmetics;
using MphRead.Sound;
using OpenTK.Mathematics;
using SoundFlow.Components;
using SoundFlow.Providers;

namespace MphRead.Mods.Sound
{
    /// <summary>
    /// Original generated energy accents mixed under the native Imperialist
    /// SFX. No sample-bank assets, extra collision events, render or gameplay
    /// randomness. The game's original sound source owns true 3D spatial audio;
    /// this restrained secondary layer is distance attenuated and bounded.
    /// </summary>
    internal static class InstagibBeamAudio
    {
        private static readonly object Gate = new();
        // At most nine resident voices, one per effect. Overlapping sounds of
        // the same style restart instead of exhausting the output mixer.
        private static readonly Dictionary<BeamFxStyle, CuePlayer> Players = new();

        public static void Play(Scene scene, BeamFxStyle style, Vector3 origin)
        {
            if (Headless.Active || ThumbnailMode.Active
                || !MusicPlayer.Available || MusicPlayer.Engine == null
                || MusicPlayer.PlaybackDevice == null || !scene.GameState.InstaGib
                || !scene.GameState.Multiplayer
                || Mods.Replay.ReplayVideoExporter.Rendering
                || scene.Services.IsReplica && !Mods.Replay.ReplayAudioOwner.MayPlay(scene)
                || !scene.Services.IsReplica && !scene.Services.AllowsPresentationSideEffects)
                return;
            Vector3 listener = scene.Audio.GetListenerPosition();
            float distance = (listener - origin).Length;
            if (!float.IsFinite(distance) || distance > 50f) return;

            // Keep it a quiet signature on top of existing positional combat
            // sound, not a 2D audio advantage for hearing distant opponents.
            float attenuation = 1f / (1f + 0.16f * distance + 0.012f * distance * distance);
            float gain = Math.Clamp(Sfx.Volume * AudioMixer.GetVolume(AudioBus.SoundEffects)
                * 0.19f * attenuation, 0f, 0.21f);
            if (gain < 0.003f) return;
            lock (Gate)
            {
                if (!Players.TryGetValue(style, out CuePlayer? player))
                {
                    try
                    {
                        player = new CuePlayer(BuildWave(style));
                        Players.Add(style, player);
                    }
                    catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
                    {
                        Console.WriteLine("[sound] Insta-Gib signature unavailable: " + e.GetType().Name);
                        return;
                    }
                }
                player.Play(gain);
            }
        }

        internal static byte[] BuildWave(BeamFxStyle style)
        {
            // Inspired by the existing owned UI-feedback synthesizer. Each
            // distinct pairing defines pitch sweep, harmonic tension and
            // digital shimmer rather than repitching one common sound.
            const int rate = 22050;
            const double seconds = 0.165;
            var palette = style switch
            {
                BeamFxStyle.VoidRift => (280d, 160d, 0.31d, 11d),
                BeamFxStyle.QuantumSplit => (890d, 1380d, 0.24d, 41d),
                BeamFxStyle.AlimbicResonance => (600d, 750d, 0.19d, 18d),
                BeamFxStyle.OmegaPulse => (490d, 230d, 0.36d, 28d),
                BeamFxStyle.ArcDisruptor => (1170d, 560d, 0.42d, 77d),
                BeamFxStyle.SolarSpear => (980d, 460d, 0.26d, 13d),
                BeamFxStyle.CryoShatter => (1400d, 760d, 0.23d, 29d),
                BeamFxStyle.PhantomTrace => (820d, 1180d, 0.10d, 57d),
                _ => (730d, 1040d, 0.22d, 23d)
            };
            int frames = (int)Math.Round(rate * seconds);
            using var stream = new MemoryStream(44 + frames * 2);
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + frames * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1); writer.Write((short)1);
            writer.Write(rate); writer.Write(rate * 2);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(frames * 2);

            double phase = 0;
            for (int i = 0; i < frames; i++)
            {
                double t = i / (double)(frames - 1);
                double frequency = palette.Item1 + (palette.Item2 - palette.Item1) * t;
                // Time-local modulation creates distinct crystalline, arc and
                // warp qualities without RNG or dependence on simulation ticks.
                frequency += Math.Sin(t * Math.PI * 2 * palette.Item4)
                    * (style == BeamFxStyle.PhantomTrace ? 7d : 29d);
                phase += 2 * Math.PI * frequency / rate;
                double attack = Math.Min(1, t / 0.036);
                double release = Math.Pow(Math.Max(0, 1 - t), 2.3);
                double env = Math.Max(0, attack * release);
                double sound = Math.Sin(phase)
                    + Math.Sin(phase * 1.51) * palette.Item3;
                // A very brief energy snap at onset.
                if (t < 0.08)
                    sound += Math.Sin(phase * 3.02) * (0.19 * (1 - t / 0.08));
                sound *= env / (1.6 + palette.Item3);
                writer.Write((short)Math.Clamp(
                    (int)Math.Round(sound * short.MaxValue * 0.65),
                    short.MinValue, short.MaxValue));
            }
            writer.Flush();
            return stream.ToArray();
        }

        public static void Shutdown()
        {
            lock (Gate)
            {
                foreach (CuePlayer player in Players.Values) player.Dispose();
                Players.Clear();
            }
        }

        private sealed class CuePlayer : IDisposable
        {
            private readonly MemoryStream _stream;
            private readonly StreamDataProvider _provider;
            private readonly SoundPlayer _player;

            public CuePlayer(byte[] wave)
            {
                _stream = new MemoryStream(wave, writable: false);
                _provider = new StreamDataProvider(MusicPlayer.Engine!, MusicPlayer.Format, _stream);
                _player = new SoundPlayer(MusicPlayer.Engine!, MusicPlayer.Format, _provider);
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
