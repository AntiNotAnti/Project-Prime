using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Mods.Input;

// Test fixture owner only. Reflection snapshots cached input/edge state so a
// temporary disconnected view cannot leave stale synthetic state after exit.
internal sealed class DisplayPoseInputIsolation : IDisposable
{
    private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic;
    private readonly List<(object? Owner, FieldInfo Field, object? Value)> _saved = new();
    private IDisposable? _scope;
    internal bool PriorConnected { get; }
    private static Type EngineType(string name) => typeof(Scene).Assembly.GetType("MphRead.Mods.Input." + name, true)!;
    private static object? Static(Type type, string name) => type.GetField(name, Fields | BindingFlags.Static)!.GetValue(null);
    private void Save(Type type, object? owner = null)
    {
        foreach (var field in type.GetFields(Fields | (owner == null ? BindingFlags.Static : BindingFlags.Instance)))
            if (!field.IsLiteral && !field.IsInitOnly) _saved.Add((owner, field, field.GetValue(owner)));
    }
    internal DisplayPoseInputIsolation()
    {
        PriorConnected = GamepadManager.ActiveState.Connected;
        Save(typeof(GamepadInput)); Save(typeof(AimInputSourceTracker));
        Save(EngineType("GamepadRuntimeConfig")); Save(typeof(GamepadContexts));
        foreach (string name in new[] { "Edges", "Actions" })
        {
            object value = Static(typeof(GamepadInput), name)!;
            Save(value.GetType(), value);
        }
        try
        {
            _scope = (IDisposable)typeof(GamepadManager).GetMethod("PushDiagnosticInputIsolation", Fields | BindingFlags.Static)!
                .Invoke(null, null)!;
            GamepadInput.InvalidatePresentationAim();
            GamepadInput.BeginFrame();
            Require(Isolated, "scoped disconnected gamepad view was not admitted");
        }
        catch { Dispose(); throw; }
    }
    internal bool Isolated => _scope != null && !GamepadManager.ActiveState.Connected
        && GamepadManager.ActiveDevice == null
        && (bool)typeof(GamepadManager).GetProperty("DiagnosticInputIsolated", Fields | BindingFlags.Static)!.GetValue(null)!;
    public void Dispose()
    {
        if (_scope == null) return;
        try
        {
            for (int i = _saved.Count - 1; i >= 0; i--)
            {
                var saved = _saved[i]; saved.Field.SetValue(saved.Owner, saved.Value);
            }
        }
        finally { _scope.Dispose(); _scope = null; }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Pose input isolation: " + message); }
    internal static int SelfTest()
    {
        const string id = "native-pose-isolation-connected-control";
        GamepadManager.UpdateDevice(id, new GamepadState { Connected = true, Name = "connected diagnostic control",
            RightX = .85f, LeftY = .7f, Buttons = GamepadButtons.A | GamepadButtons.RightTrigger }, mapped: true);
        var before = GamepadManager.Snapshot;
        var devices = GamepadManager.Devices.ToArray();
        var cachedFrame = Static(typeof(GamepadInput), "_frame");
        var cachedRevision = Static(typeof(GamepadInput), "_revision");
        var focused = GamepadContexts.Focused;
        Require(before.State.Connected && before.State.RightX != 0, "negative control did not expose connected live input");
        try
        {
            try
            {
                using var scope = new DisplayPoseInputIsolation();
                Require(scope.PriorConnected && scope.Isolated, "connected pad was not isolated");
                Type desktop = EngineType("GamepadDesktop");
                foreach (string method in new[] { "Poll", "PollAimOnly" })
                    desktop.GetMethod(method, Fields | BindingFlags.Static)!.Invoke(null, null);
                desktop.GetMethod("DeviceChanged", Fields | BindingFlags.Static)!.Invoke(null, new object[] { 0 });
                GamepadManager.ClearAll(); // legacy focus must not mutate saved physical devices
                GamepadInput.BeginFrame(); GamepadInput.CapturePresentationSample();
                Require(scope.Isolated && !GamepadInput.Active && GamepadInput.AimDeltaX == 0 && GamepadInput.AimDeltaY == 0,
                    "physical polling or cached aim contaminated the synthetic frame");
                Require(GamepadManager.Devices.SequenceEqual(devices), "isolation mutated registered devices");
                using (var nested = new DisplayPoseInputIsolation()) Require(nested.Isolated, "nested scope failed");
                Require(scope.Isolated, "nested disposal released outer isolation");
                throw new OperationCanceledException("intentional cleanup control");
            }
            catch (OperationCanceledException) { }
            Require(GamepadManager.Snapshot.Equals(before) && GamepadManager.Devices.SequenceEqual(devices),
                "exception cleanup did not restore exact connected snapshot/device state");
            Require(GamepadManager.ActiveState.Connected, "post-scope negative control remained disconnected");
            Require(Equals(cachedFrame, Static(typeof(GamepadInput), "_frame"))
                && Equals(cachedRevision, Static(typeof(GamepadInput), "_revision")) && GamepadContexts.Focused == focused,
                "cached frame/context state was not restored after exception");
            Console.WriteLine("POSE INPUT ISOLATION PASS: connected negative control, fixed/render polls blocked, nested scope and exception restoration; no physical-device acceptance.");
            return 0;
        }
        finally { GamepadManager.RemoveDevice(id); }
    }
}
