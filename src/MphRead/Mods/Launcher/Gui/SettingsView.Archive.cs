using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
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
        if (!OperatingSystem.IsAndroid()) page.Children.Add(file);
        var status = Explain(page, "Import replaces stores in the archive. Missing optional stores stay unchanged. Imported or reset settings activate on restart.");
        bool busy = false;
        void Action(string id, string title, string detail, Func<Task> action, bool enabled = true)
        {
            var button = new HubNavButton(title, detail, compact: true) { IsEnabled = enabled };
            ControllerNav.Identify(button, id);
            button.Click += async (_, _) =>
            {
                if (busy || SettingsPersistence.RestartRequired) return;
                busy = true;
                try { await action(); }
                catch (Exception ex) { if (SettingsPersistence.RestartRequired) ShowArchiveRestart("Recovery required: " + ex.Message); else status.Text = "Settings archive failed: " + ex.Message; }
                finally { busy = false; }
            };
            page.Children.Add(button);
        }
        bool Ready()
        {
            if (!IsDirty) return true;
            status.Text = "Apply or discard your settings changes first."; return false;
        }
        Action("settings.system.export", "EXPORT SETTINGS", "Export the preferences currently saved on disk", async () =>
        {
            if (!Ready()) return;
            if (OperatingSystem.IsAndroid())
            {
                using var archive = new MemoryStream();
                SettingsArchive.Export(LauncherPrefs.Directory, archive, Program.Version.ToString());
                var picker = SettingsArchivePlatform.PickDocument ?? throw new InvalidOperationException("Document picker unavailable.");
                using var output = await picker(true);
                if (output == null) { status.Text = "Export cancelled."; return; }
                archive.Position = 0; await archive.CopyToAsync(output); await output.FlushAsync();
            }
            else SettingsArchive.ExportFile(LauncherPrefs.Directory, file.Value, Program.Version.ToString());
            status.Text = "Settings exported successfully.";
        });
        Action("settings.system.import", "IMPORT SETTINGS", "Validate and restore preferences, then choose when to restart", async () =>
        {
            if (!Ready()) return;
            using Stream? stream = OperatingSystem.IsAndroid()
                ? await (SettingsArchivePlatform.PickDocument ?? throw new InvalidOperationException("Document picker unavailable."))(false)
                : File.OpenRead(file.Value);
            if (stream == null) { status.Text = "Import cancelled."; return; }
            SettingsPersistence.Replace(() => SettingsArchive.Import(LauncherPrefs.Directory, stream));
            ShowArchiveRestart("Settings imported successfully.");
        }, !_inGame);
        Action("settings.system.reset", "RESET SETTINGS", "Reset preferences only; accounts, saves and game data are kept", () =>
        {
            if (Ready()) ShowResetConfirmation();
            return Task.CompletedTask;
        }, !_inGame);
        if (!OperatingSystem.IsAndroid())
            Action("settings.system.folder", "OPEN SETTINGS FOLDER", "Open the local preferences folder", () =>
            {
                Directory.CreateDirectory(LauncherPrefs.Directory);
                Process.Start(new ProcessStartInfo(LauncherPrefs.Directory) { UseShellExecute = true });
                return Task.CompletedTask;
            });
        if (_inGame) Explain(page, "Return to the launcher before importing or resetting settings.");
    }

    private void ShowResetConfirmation()
    {
        object? previous = Content;
        var panel = new StackPanel { Spacing = 12, Margin = new Avalonia.Thickness(24) };
        Heading(panel, "Reset saved settings?");
        Explain(panel, "This removes saved preferences, controls, controller profiles/mappings and HUD profiles. Accounts, saves, game data and downloaded content are kept. Defaults activate after restart.");
        var status = Explain(panel, "");
        var confirm = new HubNavButton("CONFIRM RESET", "Restore default preferences", compact: true);
        ControllerNav.Identify(confirm, "settings.system.reset.confirm");
        confirm.Click += (_, _) =>
        {
            try { SettingsPersistence.Replace(() => SettingsArchive.Reset(LauncherPrefs.Directory)); ShowArchiveRestart("Settings reset successfully."); }
            catch (Exception ex) { if (SettingsPersistence.RestartRequired) ShowArchiveRestart("Recovery required: " + ex.Message); else status.Text = "Reset failed: " + ex.Message; }
        };
        var cancel = new HubNavButton("CANCEL", compact: true);
        cancel.Click += (_, _) => Content = previous;
        panel.Children.Add(confirm); panel.Children.Add(cancel); Content = panel;
    }

    private void ShowArchiveRestart(string message = "Saved settings are waiting for a restart.")
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Avalonia.Thickness(24) };
        Heading(panel, message);
        var status = Explain(panel, "Restart to activate the saved settings. Until then this process keeps its current configuration, and preference saves are paused so older runtime values cannot overwrite the imported or reset stores.");
        var now = new HubNavButton("RESTART NOW", "Close this process and reopen the launcher", compact: true);
        ControllerNav.Identify(now, "settings.system.restart.now");
        now.Click += (_, _) => { try { SettingsArchivePlatform.Restart(); } catch (Exception ex) { status.Text = "Restart failed: " + ex.Message; } };
        var later = new HubNavButton("RESTART LATER", "Continue with this process's current configuration", compact: true);
        ControllerNav.Identify(later, "settings.system.restart.later");
        later.Click += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
        panel.Children.Add(now); panel.Children.Add(later); Content = panel;
    }
}
