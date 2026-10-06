using System;
using System.Collections.Generic;

namespace MphRead.Mods.Launcher
{
    internal static class LauncherMenuStageCheck
    {
        internal static int Run()
        {
            int checks = 0;
            void Check(bool value, string message)
            {
                if (!value) throw new InvalidOperationException("MENU STAGE CHECK FAILED: " + message);
                Console.WriteLine("MENU STAGE PASS " + message);
                checks++;
            }

            var rooms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MP3 PROVING GROUND"] = "proving-ground-hero",
                ["MP1 SANCTORUS"] = "sanctorus-hero",
                ["UNIT1 ALINOS LANDFALL"] = "alinos-adventure",
                ["MP11 BREAKTHROUGH"] = "breakthrough-studio",
                ["AD2 ALINOS PERCH"] = "perch-settings"
            };

            foreach ((string room, string expectedName) in rooms)
            {
                MenuStageProfile p = LauncherMenuStage.ForRoom(room);
                Check(p.Name == expectedName, $"{room} resolves authored profile");
                Check(p.IntroFrame >= 12 && p.IntroFrame <= 120, $"{room} intro frame is bounded");
                Check(p.Zoom is >= 0.82f and <= 1f, $"{room} crop zoom is safe");
                Check(p.FocusX is > 0.2f and < 0.8f && p.FocusY is > 0.2f and < 0.8f,
                    $"{room} crop focus is safe");
                Check(p.HunterLeft >= 0 && p.HunterLeft < p.HunterRight && p.HunterRight <= 1
                    && p.HunterTop >= 0 && p.HunterTop < p.HunterBottom && p.HunterBottom <= 1,
                    $"{room} Hunter stage rectangle is valid");
                Check(p.HunterDistanceScale is >= 0.65f and <= 1.25f,
                    $"{room} Hunter camera distance is safe");
                Check(p.HunterBackdropHaze is >= 0f and <= 0.55f
                    && p.ForegroundHaze is >= 0f and <= 0.12f,
                    $"{room} Hunter depth haze is bounded");
                Check((p.Visibility & MenuStageVisibility.Default) == MenuStageVisibility.Default,
                    $"{room} suppresses gameplay-only presentation");
            }

            LauncherBackdropScene oldScene = LauncherBackdrop.Scene;
            string oldRoom = LauncherBackdrop.RoomKey;
            try
            {
                LauncherBackdrop.Set(LauncherBackdropScene.Multiplayer, "MP3 PROVING GROUND");
                MenuStageProfile proving = LauncherMenuStage.Current;
                Check(proving.FocusX < 0.47f && proving.FocusY > 0.44f,
                    "Proving Ground framing leads toward the Hunter instead of crossing the torso");

                LauncherBackdrop.Set(LauncherBackdropScene.Adventure, "UNIT1 ALINOS LANDFALL");
                MenuStageProfile adventure = LauncherMenuStage.Current;
                Check(adventure.Atmosphere >= LauncherMenuStage.ForRoom("UNIT1 ALINOS LANDFALL").Atmosphere,
                    "Adventure destination keeps atmospheric stage treatment");

                LauncherBackdrop.Set(LauncherBackdropScene.ReplayStudio, "MP11 BREAKTHROUGH");
                MenuStageProfile studio = LauncherMenuStage.Current;
                Check(studio.CoolShift >= 0.12f && studio.Vignette >= 0.22f,
                    "Studio destination applies cooler technical grade");
            }
            finally
            {
                LauncherBackdrop.Set(oldScene, oldRoom);
            }

            Console.WriteLine($"MENU STAGE CHECK PASS {checks}");
            return 0;
        }
    }
}
