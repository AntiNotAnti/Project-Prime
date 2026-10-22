using MphRead.Mods.Launcher.RmlUi.Host;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using MphRead;
using MphRead.Mods.Sound;
using MphRead.Mods.Launcher.RmlUi.Core;
using SoundFlow.Components;
using SoundFlow.Enums;

internal sealed partial class ReplayWindowAcceptance
{
    private readonly List<object> _menuAudioChecks = new();
    private SoundPlayer? _menuAudioPlayer;
    private object? _menuAudioEngine, _menuAudioDevice;
    private bool _menuAudioUnavailable;
    private const string MenuAudioSourceSha = "BAC0981B47734D6BA37AB5BEE8AC2A2310620268DB2201D4A4998B5877CDFF31";
    private static SoundPlayer? MenuAudioPlayer => (SoundPlayer?)typeof(MenuMusicAudio)
        .GetField("_player", StaticPrivate)!.GetValue(null);

    private void PlanMenuAudio()
    {
        Do("embedded menu loop matches supplied authored MP3 SHA256", () =>
        {
            using var source = typeof(Music).Assembly.GetManifestResourceStream(
                "MphRead.Assets.Sounds.Menu.prime_hunters_future_atmosphere_loop.mp3");
            Require(source != null, "authored menu loop is embedded");
            string hash = Convert.ToHexString(SHA256.HashData(source!));
            Require(hash == MenuAudioSourceSha, "embedded loop exactly matches user-supplied music");
            _menuAudioChecks.Add(new { Case = "resource", Sha256 = hash });
        });
        Wait("actual Home acquires existing shared output for one looping menu player", () =>
        {
            if (!MusicPlayer.Available)
            {
                _menuAudioUnavailable = true;
                _menuAudioChecks.Add(new { Case = "device", Available = false,
                    Scope = "Output unavailable; playback state and audibility not asserted" });
                return true;
            }
            if (!HeaderWindowFocused()) return false;
            if (MenuAudioPlayer is not { State: PlaybackState.Playing } player) return false;
            _menuAudioPlayer = player;
            _menuAudioEngine = MusicPlayer.Engine;
            _menuAudioDevice = MusicPlayer.PlaybackDevice;
            Require(player.IsLooping && ReferenceEquals(player.Engine, _menuAudioEngine), "loop uses shared engine");
            RequireSharedMenuPlayer("initial Home", PlaybackState.Playing);
            return true;
        }, 10000);
        Wait("owned menu cue worker completes without opening another output", () =>
        {
            if (_menuAudioUnavailable) return true;
            var waves = (System.Collections.IDictionary?)typeof(UiFeedbackAudio)
                .GetField("_ownedWaves", StaticPrivate)!.GetValue(null);
            return waves?.Count == 5;
        }, 10000);
        foreach (UiFeedbackCue cue in new[] { UiFeedbackCue.Navigate, UiFeedbackCue.Confirm,
            UiFeedbackCue.Back, UiFeedbackCue.Tab, UiFeedbackCue.Error })
        {
            Wait("owned menu cue shares output: " + cue, () =>
            {
                if (_menuAudioUnavailable) return true;
                UiFeedbackAudio.Play(cue);
                var owners = (System.Collections.IDictionary)typeof(UiFeedbackAudio)
                    .GetField("_players", StaticPrivate)!.GetValue(null)!;
                if (!owners.Contains(cue)) return false;
                object owner = owners[cue] ?? throw new InvalidOperationException("Owned cue player disappeared.");
                var player = (SoundPlayer)owner.GetType().GetField("_player", Private)!.GetValue(owner)!;
                Require(ReferenceEquals(player.Engine, _menuAudioEngine)
                    && ReferenceEquals(_menuAudioDevice, MusicPlayer.PlaybackDevice)
                    && MusicPlayer.PlaybackDevice!.MasterMixer.Components.Count(c => ReferenceEquals(c, player)) == 1,
                    "owned cue uses one registered player on existing shared output");
                Require((bool)typeof(UiFeedbackAudio).GetField("_usingOwnedSamples", StaticPrivate)!.GetValue(null)!,
                    "ROM-backed cue cache installed by UI thread");
                _menuAudioChecks.Add(new { Case = "owned cue", Cue = cue.ToString(),
                    SampleId = (int)UiFeedbackAudio.OwnedId(cue), SharedOutput = true,
                    Scope = "Owned cache and player registration; waveform identity separately CPU verified; human audibility not asserted" });
                return true;
            }, 5000);
        }
        Do("menu owner inactive pauses same loop without a new output", () =>
        {
            if (_menuAudioUnavailable) return;
            MenuMusicAudio.Update(false);
            RequireSharedMenuPlayer("explicit inactive owner", PlaybackState.Paused);
        });
        Wait("Shell owner resumes same loop on following active frame", () =>
        {
            if (_menuAudioUnavailable) return true;
            if (!HeaderWindowFocused()) return false;
            if (MenuAudioPlayer?.State != PlaybackState.Playing) return false;
            RequireSharedMenuPlayer("Shell resumed", PlaybackState.Playing); return true;
        }, 5000);
        Do("menu audio native Theatre tab transition", () => Activate("nav_theatre"));
        Wait("same loop survives actual Theatre native tab", () =>
        {
            if (ReadStatic<MphRead.Mods.Launcher.RmlUi.Host.RmlUiLauncherPages>("_launcherPages")
                is not { Suspended: false, Page: RmlUiMenuPage.Theatre }) return false;
            if (!_menuAudioUnavailable)
            {
                if (!HeaderWindowFocused() || MenuAudioPlayer?.State != PlaybackState.Playing) return false;
                RequireSharedMenuPlayer("Theatre tab", PlaybackState.Playing);
            }
            return true;
        });
        Do("menu audio returns through actual PLAY tab", () => Activate("nav_play"));
        Wait("same loop survives native return Home", () =>
        {
            if (ReadStatic<MphRead.Mods.Launcher.RmlUi.Host.RmlUiLauncherPages>("_launcherPages")
                is not { Suspended: false, Page: RmlUiMenuPage.Home }) return false;
            if (!_menuAudioUnavailable)
            {
                if (!HeaderWindowFocused() || MenuAudioPlayer?.State != PlaybackState.Playing) return false;
                RequireSharedMenuPlayer("Home tab", PlaybackState.Playing);
            }
            return true;
        });
    }

    private void PlanMenuAudioScenePause()
    {
        Wait("real replay scene pauses launcher loop on its existing output", () =>
        {
            if (_menuAudioUnavailable) return true;
            if (!_window!.HasScene) return false;
            if (MenuAudioPlayer == null)
            {
                Require(!MusicPlayer.PlaybackDevice!.MasterMixer.Components.Contains(_menuAudioPlayer!),
                    "scene Sfx.Load retired launcher loop before acquiring game sound");
                Require(ReferenceEquals(_menuAudioEngine, MusicPlayer.Engine)
                    && ReferenceEquals(_menuAudioDevice, MusicPlayer.PlaybackDevice), "scene retains process output");
                _menuAudioChecks.Add(new { Case = "actual replay scene", State = "Retired by Sfx.Load", SharedOutput = true });
                return true;
            }
            if (MenuAudioPlayer.State != PlaybackState.Paused) return false;
            RequireSharedMenuPlayer("actual replay scene", PlaybackState.Paused); return true;
        }, 5000);
    }

    private void PlanMenuAudioResume()
    {
        Wait("normal scene teardown restores one menu loop on original output", () =>
        {
            if (_menuAudioUnavailable) return true;
            if (!HeaderWindowFocused()) return false;
            if (_window!.HasScene || MenuAudioPlayer?.State != PlaybackState.Playing) return false;
            // Sfx.Load legitimately retires launcher cue/loop owners; a new
            // launcher owner after return must retain the same process output.
            _menuAudioPlayer = MenuAudioPlayer;
            RequireSharedMenuPlayer("scene teardown return", PlaybackState.Playing); return true;
        }, 5000);
        Do("menu audio teardown removes retained player from shared mixer", () =>
        {
            if (_menuAudioUnavailable) return;
            var retired = _menuAudioPlayer!;
            MenuMusicAudio.Shutdown();
            Require(MenuAudioPlayer == null && !MusicPlayer.PlaybackDevice!.MasterMixer.Components.Contains(retired),
                "shutdown removes exactly owned loop without closing shared output");
            Require(ReferenceEquals(_menuAudioEngine, MusicPlayer.Engine)
                && ReferenceEquals(_menuAudioDevice, MusicPlayer.PlaybackDevice), "shutdown retains process output");
            _menuAudioChecks.Add(new { Case = "loop teardown", Removed = true, SharedOutputRetained = true });
            _menuAudioPlayer = null;
        });
        Wait("active Shell recreates one menu player on original shared output", () =>
        {
            if (_menuAudioUnavailable) return true;
            if (!HeaderWindowFocused()) return false;
            if (MenuAudioPlayer is not { State: PlaybackState.Playing } player) return false;
            _menuAudioPlayer = player;
            RequireSharedMenuPlayer("post-shutdown restart", PlaybackState.Playing); return true;
        }, 5000);
    }

    private void RequireSharedMenuPlayer(string name, PlaybackState state)
    {
        var player = MenuAudioPlayer ?? throw new InvalidOperationException("Menu player disappeared.");
        Require(ReferenceEquals(player, _menuAudioPlayer) && player.State == state && player.IsLooping,
            "one menu player retains loop and expected playback state");
        Require(ReferenceEquals(_menuAudioEngine, MusicPlayer.Engine)
            && ReferenceEquals(_menuAudioDevice, MusicPlayer.PlaybackDevice)
            && ReferenceEquals(player.Engine, MusicPlayer.Engine), "existing process engine/device retained");
        Require(MusicPlayer.PlaybackDevice!.MasterMixer.Components.Count(c => ReferenceEquals(c, player)) == 1,
            "menu player registered exactly once in shared mixer");
        Require(Math.Abs(player.Volume - Math.Clamp(Music.UserVolume, 0, 1)) < 0.0001f,
            "menu gain follows existing music setting");
        _menuAudioChecks.Add(new { Case = name, State = state.ToString(), player.IsLooping,
            Gain = player.Volume, SharedOutput = true, MixerRegistrations = 1,
            Scope = "Output-device acquisition and owner state; human audibility not asserted" });
    }
}
