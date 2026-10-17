using System;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;

static void Check(bool pass, string label)
{
    if (!pass) throw new InvalidOperationException(label);
    Console.WriteLine("RML QUICK SETTINGS PASS " + label);
}
var fake = new Store();
using var controller = new QuickLauncherSettingsController(fake);
var first = controller.Current();
Check(!first.Dirty && first.Options.ReduceMotion == false
    && first.Options.AutoUpdate && first.Options.DebugLogs,
    "capture current settings without changing live preferences");
Check(controller.Toggle(QuickPreference.ReduceMotion)
    && controller.Toggle(QuickPreference.AutoUpdate)
    && controller.Current().Dirty && controller.Current().Revision > first.Revision
    && !fake.Value.ReduceMotion && fake.Value.AutoUpdate,
    "draft toggles do not mutate live settings before apply");
Check(!controller.Toggle((QuickPreference)123),
    "out-of-range setting cannot modify anything");
Check(controller.Apply() && fake.Saves == 1 && !controller.Current().Dirty
    && fake.Value.ReduceMotion && !fake.Value.AutoUpdate,
    "single transactional apply saves exactly once");
Check(controller.Apply() && fake.Saves == 1,
    "second apply does not rewrite disk with an identical value");
Check(controller.Toggle(QuickPreference.DebugLogs), "debug log choice staged");
Check(controller.Discard() && !controller.Current().Dirty && fake.Value.DebugLogs,
    "discard restores current live preferences");
Check(controller.Toggle(QuickPreference.ReduceMotion), "draft toggled again");
fake.Value = fake.Value with { AutoUpdate = true };
Check(!controller.Apply() && fake.Saves == 1
    && controller.Current().Status.Contains("CHANGED ELSEWHERE", StringComparison.Ordinal),
    "stale native settings cannot overwrite concurrent legacy settings");
Check(controller.Discard() && !controller.Current().Dirty
    && controller.Current().Options.AutoUpdate,
    "discard rehydrates latest authoritative on-disk values");
Check(controller.Toggle(QuickPreference.ReduceMotion), "retry edits staged");
fake.ThrowOnSave = true;
Check(!controller.Apply() && controller.Current().Dirty
    && controller.Current().Status.Contains("COULD NOT SAVE", StringComparison.Ordinal),
    "persistence failure never pretends to have saved data");
fake.ThrowOnSave = false;
Exception? wrong = null;
Task.Run(() => { try { controller.Current(); } catch (Exception e) { wrong = e; } }).GetAwaiter().GetResult();
Check(wrong is InvalidOperationException,
    "current UI thread owns preference snapshots and actions");
Console.WriteLine("RML quick settings contract complete.");

sealed class Store : IQuickLauncherPreferenceStore
{
    public QuickLauncherPreferences Value = new(false, true, true);
    public int Saves;
    public bool ThrowOnSave;
    public QuickLauncherPreferences Capture() => Value;
    public void Save(QuickLauncherPreferences value)
    {
        if (ThrowOnSave) throw new InvalidOperationException("simulated disk error");
        Value = value; Saves++;
    }
}
