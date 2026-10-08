using System;
using System.Collections.Generic;

namespace MphRead.Mods.Launcher
{
    internal static class LauncherMenuVisualsCheck
    {
        internal static int Run()
        {
            int checks = 0;
            void Check(bool value, string message)
            {
                if (!value)
                    throw new InvalidOperationException(
                        "MENU BACKDROP CHECK FAILED: " + message);
                Console.WriteLine("MENU BACKDROP PASS " + message);
                checks++;
            }

            Check(LauncherLobbyFormation.Validate(),
                "eight linked lobby slots form a symmetric V with local hero at front");
            LobbyFormationSlot hero = LauncherLobbyFormation.At(0);
            Check(Math.Abs(hero.PadX - .5f) < .001f
                && Math.Abs((hero.HunterLeft + hero.HunterRight) * .5f - .5f) < .01f
                && Math.Abs(hero.LabelX - .5f) < .001f,
                "lobby hero pad, model and nameplate share the horizontal midpoint");
            for (int wing = 1; wing <= 5; wing += 2)
            {
                LobbyFormationSlot left = LauncherLobbyFormation.At(wing);
                LobbyFormationSlot right = LauncherLobbyFormation.At(wing + 1);
                Check(Math.Abs(left.PadX + right.PadX - 1f) < .001f,
                    $"lobby wing {wing}/{wing + 1} is centered as a mirrored pair");
            }

            (int x1, int y1) = RmlUiPointerMapping.FromWindow(210, 140, 1f, 1f);
            Check(x1 == 210 && y1 == 140,
                "native RmlUi pointer position remains unscaled at 1x");
            (int x2, int y2) = RmlUiPointerMapping.FromWindow(210, 140, 2f, 2f);
            Check(x2 == 420 && y2 == 280,
                "Retina mouse converts GLFW window to framebuffer exactly once");
            (int x3, int y3) = RmlUiPointerMapping.FromWindow(210, 140, 1.5f, 1.5f);
            Check(x3 == 315 && y3 == 210,
                "fractional DPI RmlUi clicks preserve target geometry");
            (int outsideX, int outsideY) = RmlUiPointerMapping.FromWindow(-5, 200, 2f, 2f);
            Check(outsideX == -10 && outsideY == 400,
                "RmlUi captured mouse release stays outside the viewport");
            Check(LauncherLobbyFormation.PackPads().Length == LauncherLobbyFormation.Capacity * 4,
                "GL platform uniforms come from the shared formation");
            for (int slot = 0; slot < LauncherLobbyFormation.Capacity; slot++)
            {
                LobbyFormationSlot placement = LauncherLobbyFormation.At(slot);
                Check(placement.Valid, $"slot {slot} model, platform and nameplate are bounded");
            }

            Check(LobbyRuleEditValues.TryDuration("7:00", allowZero: true, out ushort seven)
                && seven == 420,
                "lobby rule inputs keep the original m:ss time contract");
            Check(LobbyRuleEditValues.TryDuration("1.5", allowZero: false, out ushort ninety)
                && ninety == 90,
                "decimal-minute hold goals are accepted");
            Check(!LobbyRuleEditValues.TryDuration("2:60", allowZero: true, out _),
                "invalid second components are rejected");
            Check(!LobbyRuleEditValues.TryDuration("-1", allowZero: true, out _),
                "negative match times are rejected");
            Check(LobbyRuleEditValues.TryGoal(GameMode.Survival, "3",
                    out ushort spareLives, out _) && spareLives == 2,
                "survival UI lives convert to network spare lives");
            Check(LobbyRuleEditValues.TryGoal(GameMode.Hardpoint, "1:30",
                    out ushort holdGoal, out _) && holdGoal == 90,
                "hardpoint hold goals are stored in seconds");
            Check(!LobbyRuleEditValues.TryGoal(GameMode.Battle, "70000",
                    out _, out _),
                "lobby goal validation rejects out-of-range scores");
            Check(LobbyRuleEditValues.TryGoal(GameMode.OneInTheChamber,
                    "ignored", out ushort chamberLives, out _)
                && chamberLives == 2,
                "one-in-the-chamber retains its fixed stock goal");

            double expected = -1;
            foreach (int hz in new[] { 60, 120, 144, 165, 240, 360 })
            {
                var motion = new MenuMotionState();
                motion.Advance(100000000, true, false, LauncherActivityAmbience.ServerBrowser, Hunter.Samus);
                for (int frame = 1; frame <= hz; frame++)
                    motion.Advance(100000000 + frame / (double)hz, true, false, LauncherActivityAmbience.Adventure, Hunter.Trace);
                Check(Math.Abs(motion.Time - 1) < .00001, $"{hz} Hz motion retains subframe precision at long uptime");
                if (expected >= 0) Check(Math.Abs(motion.Theme.Halo.R - expected) < .00001, $"{hz} Hz palette is time-based");
                expected = motion.Theme.Halo.R;
                double phase = motion.Time;
                motion.Advance(100000010, true, false, LauncherActivityAmbience.Studio, Hunter.Noxus);
                Check(motion.Time == phase, "suspension does not jump animation phase");
                motion.Advance(100000010.01, true, true, LauncherActivityAmbience.Studio, Hunter.Noxus);
                Check(motion.Time == phase && motion.Theme == LauncherHunterTheme.For(Hunter.Noxus), "Reduce Motion freezes time and settles state");
            }
            for (int slot = 0; slot < 8; slot++)
            {
                var placement = LauncherLobbyFormation.At(slot);
                Check(Math.Abs((placement.HunterLeft + placement.HunterRight) / 2 - placement.PadX) < .00001,
                    $"hunter {slot} viewport and nameplate centered on platform");
            }
            var lobbyMotion = new MenuMotionState();
            LauncherLobbyVisuals.Active = true;
            LauncherLobbyVisuals.OccupiedMask = 1;
            LauncherLobbyVisuals.SetIdentity(0, 42);
            lobbyMotion.Advance(0, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
            LauncherLobbyVisuals.ReadyMask = 1;
            lobbyMotion.Advance(.01, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
            float readyPulse = lobbyMotion.ReadyPulse[0];
            lobbyMotion.Advance(.02, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
            Check(readyPulse > 0 && lobbyMotion.ReadyPulse[0] < readyPulse, "ready pulse triggers once and decays while ready stays true");
            LauncherLobbyVisuals.Starting = true; LauncherLobbyVisuals.CountdownSeconds = 1;
            lobbyMotion.Advance(.12, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
            float launch = lobbyMotion.Launch;
            LauncherLobbyVisuals.Starting = false;
            lobbyMotion.Advance(.22, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
            Check(launch > 0 && lobbyMotion.Launch < launch, "countdown cancellation settles launch without independent timer");
            LauncherLobbyVisuals.OccupiedMask = 0;
            lobbyMotion.Advance(.32, true, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
            Check(lobbyMotion.Occupancy[0] is > 0 and < 1, "departure fades without retaining authoritative occupancy");
            double focusedTime = lobbyMotion.Time;
            lobbyMotion.Advance(.4, false, false, LauncherActivityAmbience.Lobby, Hunter.Samus);
            Check(lobbyMotion.Time == focusedTime, "focus loss freezes presentation phase");
            LauncherLobbyVisuals.Reset();
            LauncherBackdropStyle style = LauncherMenuVisuals.Style;
            Check(style.Name == "deployment-chamber",
                "universal deployment chamber is the default RmlUi backdrop");
            Check(style.HeroHalo is > 0f and <= 1f
                && style.Fog is >= 0f and <= 1f
                && style.Particles is >= 0f and <= 1f
                && style.FloorGlow is >= 0f and <= 1f,
                "base atmosphere values are bounded");
            Check(style.LeftUiDarken > style.RightUiDarken
                && style.LeftUiDarken < 0.75f,
                "left selector zone receives stronger readability masking");

            var activities = new[]
            {
                LauncherActivityAmbience.QuickPlay,
                LauncherActivityAmbience.ServerBrowser,
                LauncherActivityAmbience.OfflineBattle,
                LauncherActivityAmbience.Adventure,
                LauncherActivityAmbience.AimLab,
                LauncherActivityAmbience.Lobby,
                LauncherActivityAmbience.Community,
                LauncherActivityAmbience.Studio
            };
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LauncherActivityAmbience activity in activities)
            {
                Check(names.Add(activity.Name),
                    $"{activity.Name} ambience name is unique");
                Check(activity.Energy is >= 0f and <= 1.5f
                    && activity.FogBias is >= 0f and <= 1.5f
                    && activity.ParticleBias is >= 0f and <= 1.5f
                    && activity.FloorGrid is >= 0f and <= 1.5f,
                    $"{activity.Name} ambience is bounded");
            }
            Check(LauncherActivityAmbience.OfflineBattle.FloorGrid
                    > LauncherActivityAmbience.QuickPlay.FloorGrid,
                "Offline Battle emphasizes the simulation floor grid");
            Check(LauncherActivityAmbience.Adventure.FogBias
                    > LauncherActivityAmbience.QuickPlay.FogBias,
                "Adventure is more atmospheric than Quick Play");
            Check(LauncherActivityAmbience.ServerBrowser.ParticleBias
                    < LauncherActivityAmbience.QuickPlay.ParticleBias,
                "Server Browser remains the cleaner technical mood");
            Check(LauncherActivityAmbience.AimLab.FloorGrid
                    > LauncherActivityAmbience.OfflineBattle.FloorGrid,
                "Aim Lab uses its own high-definition training grid");
            Check(LauncherActivityAmbience.Lobby.StructureBias
                    > LauncherActivityAmbience.QuickPlay.StructureBias,
                "Actual lobby emphasizes operational formation structure");
            Check(LauncherActivityAmbience.Studio.Accent.B
                    > LauncherActivityAmbience.Studio.Accent.R,
                "Studio has its own violet technical accent");
            Check(LauncherMenuVisuals.Hunter(Hunter.Trace).Halo.R
                    > LauncherMenuVisuals.Hunter(Hunter.Trace).Halo.B,
                "Trace creates a warm crimson chamber identity");

            foreach (Hunter hunter in new[]
            {
                Hunter.Samus, Hunter.Kanden, Hunter.Trace, Hunter.Sylux,
                Hunter.Noxus, Hunter.Spire, Hunter.Weavel
            })
            {
                LauncherHunterTheme theme = LauncherMenuVisuals.Hunter(hunter);
                Check(theme.Name.Length > 0,
                    $"{hunter} has a menu identity theme");
                Check(theme.AccentStrength is >= 0f and <= 1.25f
                    && theme.HaloScale is >= 0.5f and <= 1.5f
                    && theme.ParticleScale is >= 0f and <= 1.5f,
                    $"{hunter} identity strengths are bounded");
                Check(InRange(theme.Halo) && InRange(theme.Rim)
                    && InRange(theme.Floor) && InRange(theme.Particle),
                    $"{hunter} identity colors are normalized");
            }

            LauncherLobbyVisuals.Reset();
            LauncherLobbyVisuals.SetHunter(0, Hunter.Trace);
            LauncherLobbyVisuals.SetHunter(7, Hunter.Noxus);
            Check(LauncherLobbyVisuals.HunterAt(0) == Hunter.Trace
                && LauncherLobbyVisuals.HunterAt(7) == Hunter.Noxus,
                "Eight lobby pads retain their own Hunter lighting identity");
            LauncherLobbyVisuals.Reset();
            Check(LauncherLobbyVisuals.OccupiedMask == 0
                && LauncherLobbyVisuals.HunterAt(0) == Hunter.Samus,
                "Lobby exit clears stale pad-light identities");

            LauncherBackdropScene oldScene = LauncherBackdrop.Scene;
            string oldRoom = LauncherBackdrop.RoomKey;
            try
            {
                LauncherBackdrop.Set(LauncherBackdropScene.Multiplayer);
                Check(LauncherMenuVisuals.Activity.Name == "quick-play",
                    "Multiplayer resolves Quick Play ambience");
                LauncherBackdrop.Set(LauncherBackdropScene.Play);
                Check(LauncherMenuVisuals.Activity.Name == "server-browser",
                    "Play directory resolves Server Browser ambience");
                LauncherBackdrop.Set(LauncherBackdropScene.Offline);
                Check(LauncherMenuVisuals.Activity.Name == "offline-battle",
                    "Offline resolves training ambience");
                LauncherBackdrop.Set(LauncherBackdropScene.Adventure);
                Check(LauncherMenuVisuals.Activity.Name == "adventure",
                    "Adventure resolves solo ambience");
                LauncherBackdrop.Set(LauncherBackdropScene.Training);
                Check(LauncherMenuVisuals.Activity.Name == "aim-lab",
                    "Aim Lab uses a distinct training mood");
                LauncherBackdrop.Set(LauncherBackdropScene.Lobby);
                Check(LauncherMenuVisuals.Activity.Name == "live-lobby",
                    "Live lobby is not the Home Quick Play preset");
                LauncherBackdrop.Set(LauncherBackdropScene.MapEditor);
                Check(LauncherMenuVisuals.Activity.Name == "community-forge",
                    "Community has a teal authoring mood");
                LauncherBackdrop.Set(LauncherBackdropScene.ReplayStudio);
                Check(LauncherMenuVisuals.Activity.Name == "studio-technical",
                    "Studio has a violet technical mood");
            }
            finally
            {
                LauncherBackdrop.Set(oldScene, oldRoom);
            }

            Console.WriteLine($"MENU BACKDROP CHECK PASS {checks}");
            return 0;
        }

        private static bool InRange(MenuRgb color)
            => color.R is >= 0f and <= 1f
            && color.G is >= 0f and <= 1f
            && color.B is >= 0f and <= 1f;
    }
}
