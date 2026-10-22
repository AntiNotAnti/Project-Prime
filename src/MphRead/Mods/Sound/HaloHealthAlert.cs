using System;
using System.IO;
using MphRead.Mods.Render.Hud;
using MphRead.Sound;
using SoundFlow.Components;
using SoundFlow.Providers;

namespace MphRead.Mods.Sound;

internal static class HaloHealthAlert
{
    private static MemoryStream? _stream;
    private static StreamDataProvider? _provider;
    private static SoundPlayer? _player;
    private static Scene? _owner;
    private static bool _playing, _attempted;
    internal static bool IsPlaying => _playing;

    // Decode/allocate at scene audio initialization, never on the input path.
    internal static void Warm()
    {
        if (_attempted || Headless.Active || !MusicPlayer.Available) return;
        _attempted = true;
        try
        {
            // Optional installed replacement-character audio stays in the user's pack.
            string path = Path.Combine(Render.Characters.CharacterModelPack.DefaultDirectory, "audio", "low-health.wav");
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 16 * 1024 * 1024)
                throw new IOException("Replacement-character low-health sound exceeds 16 MiB.");
            _stream = new MemoryStream(File.ReadAllBytes(path), writable: false);
            _provider = new StreamDataProvider(MusicPlayer.Engine!, MusicPlayer.Format, _stream);
            _player = new SoundPlayer(MusicPlayer.Engine!, MusicPlayer.Format, _provider) { IsLooping = true };
            MusicPlayer.PlaybackDevice!.MasterMixer.AddComponent(_player);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[hud-audio] Halo alert unavailable: " + ex.Message);
            Dispose(); _attempted = true;
        }
    }

    internal static bool Eligible(int health, bool enabled, bool active, bool suspended)
        => enabled && active && !suspended && health > 0 && health <= 99;

    internal static void Update(Scene scene)
    {
        bool enabled = HudProfiles.Runtime.HaloLowHealthSound;
        var main = scene.Players.Main;
        bool active = !scene.Services.IsReplica && !Headless.Active && !SpectatorMode.IsSpectating
            && main.ModIsInPlay && scene.GameState.MatchState == MatchState.InProgress;
        bool suspended = PauseMenu.Open || scene.GameState.MenuPause || scene.GameState.DialogPause
            || Network.NetSession.FreezeGameplay;
        bool play = Eligible(main.Health, enabled, active, suspended);
        if (!play || _owner != null && _owner != scene) Stop();
        if (!play || _player == null) return;
        _owner = scene;
        _player.Volume = Math.Clamp(Sfx.Volume * AudioMixer.GetVolume(AudioBus.Notifications), 0, 1);
        if (!_playing)
        {
            _provider!.Seek(0); _player.Play(); MusicPlayer.PlaybackDevice!.Start(); _playing = true;
        }
    }

    internal static void Stop(Scene? scene = null)
    {
        if (scene != null && scene != _owner) return;
        if (_playing) _player?.Stop(); _playing = false; _owner = null;
    }

    internal static void Dispose()
    {
        Stop();
        if (_player != null) MusicPlayer.PlaybackDevice?.MasterMixer.RemoveComponent(_player);
        _player?.Dispose(); _provider?.Dispose(); _stream?.Dispose();
        _player = null; _provider = null; _stream = null; _attempted = false;
    }
}
