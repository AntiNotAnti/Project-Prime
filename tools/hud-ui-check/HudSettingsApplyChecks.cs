using System.Reflection;
using MphRead;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Render.Hud;
using MphRead.Mods.Settings;

internal static class HudSettingsApplyChecks
{
    // Exercise the shared desktop/Android settings owner, including its real
    // persistence path. Preference writes are rolled back after each case.
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new Exception(message);
        }
        var original = HudProfiles.CopyCurrent();
        bool autoPrune = LauncherPrefs.ReplayAutoPrune;
        var draftField = typeof(SettingsView).GetField("_hudDraft", BindingFlags.NonPublic | BindingFlags.Instance)!;
        try
        {
            LauncherPrefs.ReplayAutoPrune = false;
            foreach (var mode in new[] { HudMode.Classic, HudMode.ProjectPrime, HudMode.Custom })
            {
                using var rollback = SettingsPersistence.BeginCommit();
                var profile = HudProfileDefaults.Create(mode == HudMode.Classic ? "Classic" : "Project Prime");
                profile.Mode = mode;
                profile.AccentColor = "#123456";
                profile.Health.FullColor = "#BADA55";
                profile.Elements["core.health"].OffsetX = 17;
                HudProfiles.Publish(profile);
                var settings = new MenuSettings();
                var view = new SettingsView(settings, shell: true);
                // A guide-color runtime change must not replace the cloned HUD
                // palette when the user applies an unrelated audio preference.
                StylusZone.GuideColor = "#FEDCBA";
                var volume = typeof(SettingsView).GetField("_musicVolume", BindingFlags.NonPublic | BindingFlags.Instance)!;
                ((SliderRow)volume.GetValue(view)!).Value = 37;
                Check(view.ApplyDraft(), $"settings Apply succeeds for {mode}");
                var saved = HudProfiles.CopyCurrent();
                Check(settings.MusicVolume == "0.37", $"unrelated audio preference applies for {mode}");
                Check(saved.Mode == mode, $"unrelated Apply preserves {mode} mode");
                Check(saved.AccentColor == profile.AccentColor && saved.Health.FullColor == profile.Health.FullColor,
                    $"unrelated Apply preserves {mode} palette");
                Check(saved.Elements["core.health"].OffsetX == 17, $"unrelated Apply preserves {mode} layout");
            }
            using (var rollback = SettingsPersistence.BeginCommit())
            {
                HudProfiles.Publish(HudProfileDefaults.Create("Classic"));
                var view = new SettingsView(new MenuSettings(), shell: true);
                var edited = HudProfiles.CopyCurrent();
                edited.Mode = HudMode.Custom;
                edited.AccentColor = "#FEDCBA";
                edited.Crosshair.Scale = 2.5f;
                edited.Elements["core.health"].OffsetX = 31;
                // Stage the same accepted draft supplied by the HUD editor.
                draftField.SetValue(view, edited);
                StylusZone.GuideColor = "#123456";
                Check(view.ApplyDraft(), "settings Apply accepts an explicit HUD editor draft");
                var saved = HudProfiles.CopyCurrent();
                Check(saved.Mode == HudMode.Custom && saved.AccentColor == edited.AccentColor,
                    "explicit HUD draft mode and palette are applied");
                Check(saved.Crosshair.Scale == 2.5f && saved.Elements["core.health"].OffsetX == 31,
                    "explicit HUD draft crosshair and layout are applied");
                Check(edited.Mode == HudMode.Custom && edited.AccentColor == "#FEDCBA",
                    "Apply does not mutate the editor-owned draft");
            }
        }
        finally
        {
            LauncherPrefs.ReplayAutoPrune = autoPrune;
            HudProfiles.Publish(original);
        }
        Console.WriteLine($"Shared HUD settings Apply checks passed: {checks}.");
        return checks;
    }
}
