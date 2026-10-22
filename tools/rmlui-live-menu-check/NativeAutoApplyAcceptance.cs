using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using OpenTK.Windowing.GraphicsLibraryFramework;

internal static class NativeAutoApplyAcceptance
{
    // Called only by the existing fresh-process, temporary-user-data fixture.
    internal static void Run(string root, MenuSettings settings)
    {
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException("AUTO SETTINGS: " + label);
            Console.WriteLine("AUTO SETTINGS PASS " + label);
        }
        void Settle(NativeFullSettingsController controller)
        {
            typeof(NativeFullSettingsController).GetField("_autoApplyChangedAt", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(controller, Stopwatch.GetTimestamp() - Stopwatch.Frequency);
            controller.TickAutoApply();
        }
        string settingsPath = Path.Combine(root, "Savedata", "settings.json");
        using (var controller = new NativeFullSettingsController(settings) { AutoApply = true })
        {
            var volume = controller.Rows.Single(row => row.Category == "Audio" && row.Label == "Sfx Volume (%)");
            string before = settings.SfxVolume;
            byte[] oldFile = File.ReadAllBytes(settingsPath);
            Check(controller.Stage(volume, "41") && controller.Stage(volume, "42"), "rapid edits are accepted as pending values");
            controller.TickAutoApply();
            Check(settings.SfxVolume == before && File.ReadAllBytes(settingsPath).SequenceEqual(oldFile), "quiet-time debounce does not publish intermediate values");
            Settle(controller);
            Check(settings.SfxVolume == "0.42" && !controller.Dirty && !controller.AutoApplyFailed, "settled edit saves through existing preference and runtime owners");
            DateTime written = File.GetLastWriteTimeUtc(settingsPath);
            for (int i = 0; i < 20; i++) Settle(controller);
            Check(File.GetLastWriteTimeUtc(settingsPath) == written, "idle frames do not rewrite preferences");
            Check(!controller.Stage(volume, "-") && !controller.Dirty, "incomplete numeric values never enter the saved draft");

            controller.Stage(volume, "43");
            typeof(NativeFullSettingsController).GetField("_autoApplyChangedAt", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(controller, Stopwatch.GetTimestamp() - Stopwatch.Frequency);
            controller.TickAutoApply(inputActive: false);
            Check(settings.SfxVolume == "0.42", "covered or invalid form cannot auto-save pending changes");
            controller.CalibrationAction(26);
            Settle(controller);
            Check(settings.SfxVolume == "0.42" && !controller.Back() && !controller.Closed,
                "active calibration blocks saving and Escape cancels it first");
            Check(controller.FlushAutoApply() && settings.SfxVolume == "0.43", "cancelled calibration releases valid pending changes");

            var first = controller.Rows.First(row => row.Group == "Keyboard bindings");
            var second = controller.Rows.First(row => row.Group == "Keyboard bindings" && row != first);
            controller.BeginCapture(first); controller.KeyDown(Keys.F23);
            Check(!controller.BindingConflict && controller.FlushAutoApply(), "completed keyboard binding saves automatically");
            controller.BeginCapture(second); controller.KeyDown(Keys.F23);
            Settle(controller);
            Check(controller.BindingConflict && first.Read() == "F23" && second.Read() != "F23", "conflicting capture never saves before resolution");
            Check(controller.ResolveBinding("Replace") && controller.FlushAutoApply() && first.Read() == "Unknown" && second.Read() == "F23",
                "resolved cross-tab conflict persists both binding changes together");

            int attempts = 0;
            string faultValue = "clear";
            var fault = new NativeSettingRow("System", "Test fault", "Automatic save fault", () => faultValue,
                value => { if (value == "fail") { attempts++; throw new IOException("Injected automatic save failure"); } faultValue = value; }, _ => null);
            controller.Rows.Add(fault);
            oldFile = File.ReadAllBytes(settingsPath);
            controller.Stage(volume, "44"); controller.Stage(fault, "fail"); Settle(controller);
            Check(controller.AutoApplyFailed && controller.Dirty && settings.SfxVolume == "0.43"
                && File.ReadAllBytes(settingsPath).SequenceEqual(oldFile), "failed save rolls back files and runtime while retaining editable values");
            for (int i = 0; i < 20; i++) Settle(controller);
            Check(attempts == 1, "failed revision does not retry on every frame");
            controller.Stage(fault, "clear"); Settle(controller);
            Check(!controller.AutoApplyFailed && !controller.Dirty && settings.SfxVolume == "0.44", "correcting an edit retries and clears the save error");
            controller.Stage(volume, "45"); controller.RequestGameFiles();
            Check(controller.GameFilesRequested && !controller.Dirty && settings.SfxVolume == "0.45", "external settings handoff flushes the final valid edit");
            controller.Stage(volume, "46");
            Check(controller.Back() && controller.Closed && !controller.Dirty && settings.SfxVolume == "0.46", "Back saves the final edit before closing");
        }
        using (var controller = new NativeFullSettingsController(settings) { AutoApply = true })
        {
            var mouse = controller.Rows.Single(row => row.Category == "Controls" && row.Label == "Mouse Sensitivity");
            controller.Stage(mouse, mouse.Value == "2" ? "1" : "2");
            GamepadRuntimeConfig.Current = GamepadRuntimeConfig.Current.Clone();
            Check(controller.FlushAutoApply() && !controller.Dirty, "controller replacement preserves unrelated mouse edits");
            var layout = controller.Rows.Single(row => row.Label == "Control layout");
            controller.Stage(layout, layout.Choices.First(value => value != layout.Value && value != "Custom"));
            GamepadRuntimeConfig.Current = GamepadRuntimeConfig.Current.Clone();
            Check(!controller.FlushAutoApply() && controller.AutoApplyFailed && controller.Dirty,
                "stale controller draft is retained with an explicit recovery error");
            controller.Discard();
            Check(!controller.Dirty && !controller.AutoApplyFailed, "failure recovery rebases the replacement controller and clears unsaved values");
            controller.Stage(mouse, mouse.Value == "2" ? "1" : "2");
            Check(controller.FlushAutoApply() && controller.Back(), "recovered settings can save and exit normally");
        }
        var modes = new System.Collections.Generic.List<WindowStartMode>();
        using (var controller = new NativeFullSettingsController(settings, modes.Add) { AutoApply = true })
        {
            var mode = controller.Rows.Single(row => row.Group == "Window");
            string previous = mode.Saved, proposed = mode.Choices.First(value => value != previous);
            byte[] oldFile = File.ReadAllBytes(Path.Combine(root, "launcher.txt"));
            controller.Stage(mode, proposed); Settle(controller);
            Check(controller.ConfirmingVideo && modes.Count == 1 && !controller.KeepVideo()
                && File.ReadAllBytes(Path.Combine(root, "launcher.txt")).SequenceEqual(oldFile), "automatic display change waits for usable frame and Keep before saving");
            controller.RevertVideo(); Settle(controller);
            Check(!controller.ConfirmingVideo && !controller.Dirty && mode.Value == previous && modes.Count == 2,
                "reverted display change does not start another automatic trial");
            controller.Stage(mode, proposed); Settle(controller); controller.AcknowledgeVideoFrame();
            Check(controller.KeepVideo() && !controller.Dirty && mode.Saved == proposed, "Keep saves acknowledged display mode through the same transaction");
            int failures = 0;
            string current = "clear";
            var fault = new NativeSettingRow("System", "Test fault", "Display save fault", () => current,
                value => { if (value == "fail") { failures++; throw new IOException("Injected display save failure"); } current = value; }, _ => null);
            controller.Rows.Add(fault);
            controller.Stage(mode, previous); controller.Stage(fault, "fail"); Settle(controller); controller.AcknowledgeVideoFrame();
            Check(!controller.KeepVideo() && controller.AutoApplyFailed && !controller.ConfirmingVideo
                && controller.Status.Contains("Injected display save failure", StringComparison.Ordinal) && mode.Value == proposed,
                "failed display save restores the previous mode and retains the save error");
            for (int i = 0; i < 20; i++) Settle(controller);
            Check(failures == 1, "failed display save does not retry on every frame");
            controller.Stage(fault, "clear");
            Check(controller.FlushAutoApply(), "failed display edit can be corrected without another mode trial");
        }
    }
}
