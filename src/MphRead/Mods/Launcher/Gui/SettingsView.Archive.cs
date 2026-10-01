using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Avalonia.Controls;
using MphRead.Mods.Settings;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class SettingsView
{
    private void BuildSystem(StackPanel page)
    {
        Heading(page, "Settings backup");
        Explain(page, "Back up saved preferences, controls, controller mappings and HUD profiles. Account credentials, game files, saves, replays and caches are excluded.");
        var file = new FieldRow("Archive file", Path.Combine(LauncherPrefs.Directory, "ProjectPrime-settings.zip"), boxWidth: 360) { Tag = "settings.archive.path" };
        page.Children.Add(file);
        var status = Explain(page, "Import replaces the stores in the archive. Missing optional stores keep their current values.");
        void Action(string id, string title, string detail, System.Action action, bool enabled = true)
        {
            var button = new HubNavButton(title, detail, compact: true) { IsEnabled = enabled };
            ControllerNav.Identify(button, id);
            button.Click += (_, _) =>
            {
                try { action(); }
                catch (Exception ex) { status.Text = "Settings archive failed: " + ex.Message; }
            };
            page.Children.Add(button);
        }
        Action("settings.system.export", "EXPORT SETTINGS", "Export the preferences currently saved on disk", () =>
        {
            if (IsDirty) { status.Text = "Apply or discard your settings changes before exporting."; return; }
            SettingsArchive.ExportFile(LauncherPrefs.Directory, file.Value, Program.Version.ToString());
            status.Text = "Settings exported successfully.";
        });
        Action("settings.system.import", "IMPORT SETTINGS", "Validate the complete archive, then restore its preferences", () =>
        {
            if (IsDirty) { status.Text = "Apply or discard your settings changes before importing."; return; }
            using (var stream = File.OpenRead(file.Value)) SettingsArchive.Import(LauncherPrefs.Directory, stream);
            LauncherPrefs.Load();
            Input.GamepadProfiles.Reload();
            InputSettings.Reset(); InputSettings.Load();
            Input.GamepadMappings.ReloadRequested = true;
            MenuSettings loaded = GameState.LoadSettings();
            foreach (var property in typeof(MenuSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                    property.SetValue(_settings, property.GetValue(loaded));
            GameSettings.Apply(_settings);
            // Replace the complete editor so no stale controls can write over the imported values.
            _replacement = new SettingsView(_settings, _inGame, _players, _shell);
            _replacement.Closed += (_, e) => Closed?.Invoke(this, e);
            _replacement.GameFilesRequested += (_, e) => GameFilesRequested?.Invoke(this, e);
            Content = _replacement;
            _replacement.ShowSection("System");
            _replacement._archiveMessage!.Text = "Settings imported successfully. Some changes require restarting Project Prime. You can restart later; no automatic restart will occur.";
        }, !_inGame);
        Action("settings.system.folder", "OPEN SETTINGS FOLDER", "Open the local folder containing your preferences", () =>
        {
            Directory.CreateDirectory(LauncherPrefs.Directory);
            Process.Start(new ProcessStartInfo(LauncherPrefs.Directory) { UseShellExecute = true });
        }, !OperatingSystem.IsAndroid());
        if (_inGame) Explain(page, "Return to the launcher before importing settings.");
        _archiveMessage = status;
    }
    private TextBlock? _archiveMessage;
}
