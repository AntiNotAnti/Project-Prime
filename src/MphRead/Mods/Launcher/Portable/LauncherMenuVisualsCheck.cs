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
                LauncherActivityAmbience.Adventure
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
