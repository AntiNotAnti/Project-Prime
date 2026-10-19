using System;
using System.IO;
using SoundFlow.Components;
using SoundFlow.Providers;

namespace MphRead.Mods.Sound
{
    /// <summary>One launcher-wide loop on the process's existing audio output.</summary>
    internal static class MenuMusicAudio
    {
        private static Stream? _stream;
        private static StreamDataProvider? _provider;
        private static SoundPlayer? _player;
        private static bool _failed;

        /// <summary>
        /// Pump once per launcher frame. Active is false while a match, campaign
        /// soundtrack, suspended app, or reduced-background-audio policy owns sound.
        /// Navigation between launcher pages does not restart the loop.
        /// </summary>
        public static void Update(bool active)
        {
            if (active) UiFeedbackAudio.PrepareOwnedCues();
            if (!active || Headless.Active || ThumbnailMode.Active)
            {
                _player?.Pause();
                return;
            }
            if (_failed || !MusicPlayer.Available || MusicPlayer.Engine == null
                || MusicPlayer.PlaybackDevice == null) return;
            try
            {
                if (_player == null)
                {
                    _stream = typeof(MenuMusicAudio).Assembly.GetManifestResourceStream(
                        "MphRead.Assets.Sounds.Menu.prime_hunters_future_atmosphere_loop.mp3");
                    if (_stream == null) { _failed = true; return; }
                    _provider = new StreamDataProvider(MusicPlayer.Engine, _stream);
                    _player = new SoundPlayer(MusicPlayer.Engine, MusicPlayer.Format, _provider)
                    {
                        IsLooping = true
                    };
                    MusicPlayer.PlaybackDevice.MasterMixer.AddComponent(_player);
                    MusicPlayer.PlaybackDevice.Start();
                }
                _player.Volume = Math.Clamp(Music.UserVolume, 0, 1);
                if (_player.State != SoundFlow.Enums.PlaybackState.Playing) _player.Play();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[sound] launcher loop unavailable: " + ex.Message);
                Shutdown();
                _failed = true;
            }
        }

        public static void Shutdown()
        {
            if (_player != null)
            {
                _player.Stop();
                MusicPlayer.PlaybackDevice?.MasterMixer.RemoveComponent(_player);
                _player.Dispose();
            }
            _provider?.Dispose();
            _stream?.Dispose();
            _player = null;
            _provider = null;
            _stream = null;
            _failed = false;
        }
    }
}
