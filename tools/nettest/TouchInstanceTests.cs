using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Droid;
using MphRead.Mods.Input;

namespace MphRead.NetTest;

// Production layout codec/model and Android pointer ownership; no Android UI,
// graphics, replay file save or physical device is represented by this fixture.
internal static class TouchInstanceTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    {
        _checks++;
        if (!value) throw new InvalidOperationException("TOUCH INSTANCES " + label);
    }
    private static TouchButtonInstance Extra(int id = 1000, TouchControl action = TouchControl.Jump,
        float x = .5f, float y = .6f, bool enabled = true)
        => new(id, action, enabled, x, y, 1);
    private static TouchControls Controls()
    {
        var c = new TouchControls { ClipEnabled = true, ChatEnabled = true };
        c.Layout(1920, 1080, 1);
        c.ReloadSettings();
        return c;
    }
    private static TouchButton Button(TouchControls c, int id = 1000)
        => c.Buttons.Single(x => x.InstanceId == id);
    private static void Down(TouchControls c, int pointer, int id = 1000)
    {
        var b = Button(c, id); c.PointerDown(pointer, b.CentreX, b.CentreY);
    }
    internal static int Run()
    {
        var layout = TouchSettings.CopyLayout(); var extras = TouchSettings.CopyInstances();
        var enabled = TouchSettings.Order.ToDictionary(x => x.Control, x => TouchSettings.IsEnabled(x.Control));
        bool visible = TouchSettings.ButtonsVisible;
        float scale = TouchSettings.ButtonScale, stick = TouchSettings.StickScale, opacity = TouchSettings.OverlayOpacity;
        try
        {
            TouchSettings.Reset();
            _checks = 0;
            Codec();
            LegacyAndDraft();
            foreach (var c in TouchSettings.Order) TouchSettings.SetEnabled(c.Control, false);
            AllActions();
            IndependentPointers();
            Lifecycle();
            Console.WriteLine($"TOUCH INSTANCES PASS {_checks} checks (production input/codec models; device verification pending)");
            return 0;
        }
        finally
        {
            TouchSettings.ReplaceLayout(layout); TouchSettings.ReplaceInstances(extras);
            foreach (var pair in enabled) TouchSettings.SetEnabled(pair.Key, pair.Value);
            TouchSettings.ButtonsVisible = visible; TouchSettings.ButtonScale = scale;
            TouchSettings.StickScale = stick; TouchSettings.OverlayOpacity = opacity;
        }
    }
    private static void Codec()
    {
        var values = new[] { Extra(1003, TouchControl.Clip, .73f, .9f), Extra() };
        string text = TouchSettings.SerializeInstances(values);
        Check(TouchSettings.TryParseInstances(text, out var parsed) && parsed.SequenceEqual(values.OrderBy(x => x.Id)), "round trip and stable ID ordering");
        Check(TouchSettings.SerializeInstances(parsed) == text, "canonical repeated serialization");
        Check(TouchSettings.TryParseInstances("none", out parsed) && parsed.Length == 0, "explicit empty layout");
        foreach (string invalid in new[] { "", new string('x', 4097), "999,Jump,True,0.5,0.5,1", "65536,Jump,True,0.5,0.5,1",
            "1000,999,True,0.5,0.5,1", "1000,Jump,True,NaN,0.5,1", "1000,Jump,True,0.5,Infinity,1",
            "1000,Jump,True,0.5,0.5,-1", "1000,Jump,True,1.1,0.5,1", "1000,Jump,True,0.5,0.5,1;1000,Clip,True,0.5,0.5,1" })
            Check(!TouchSettings.TryParseInstances(invalid, out _), "reject malformed: " + invalid[..Math.Min(50, invalid.Length)]);
        var maximum = Enumerable.Range(1000, TouchSettings.MaximumExtraInstances).Select(id => Extra(id)).ToArray();
        Check(TouchSettings.TryParseInstances(TouchSettings.SerializeInstances(maximum), out parsed) && parsed.Length == TouchSettings.MaximumExtraInstances, "accept exact capacity");
        Check(!TouchSettings.TryParseInstances(TouchSettings.SerializeInstances(maximum.Append(Extra(2000))), out _), "reject over capacity");
        Check(TouchSettings.NextInstanceId(maximum) == -1 && TouchSettings.NextInstanceId(new[] { Extra(1001) }) == 1000, "bounded deterministic new IDs");
        TouchSettings.ReplaceInstances(values);
        foreach (var invalid in new[] { new[] { Extra(), Extra() }, new[] { Extra(999) }, maximum.Append(Extra(2000)).ToArray(), new[] { Extra() with { X = float.NaN } } })
        {
            bool rejected = false;
            try { TouchSettings.ReplaceInstances(invalid); } catch (ArgumentException) { rejected = true; }
            Check(rejected && TouchSettings.CopyInstances().SequenceEqual(values.OrderBy(x => x.Id)), "invalid replacement is atomic");
        }
        Check(!TouchSettings.ReadSetting("touch_instances_v2", text), "unknown record version is not consumed");
        Check(TouchSettings.ReadSetting(TouchSettings.InstancesSettingKey, "corrupt") && TouchSettings.ExtraInstances.Count == 0, "malformed extras fall back to primary defaults");
    }
    private static void LegacyAndDraft()
    {
        TouchSettings.SetLayout(TouchControl.Jump, .2f, .8f, 1.2f);
        var before = TouchSettings.Geometry(TouchControl.Jump, 1920, 1080);
        TouchSettings.ReplaceInstances(new[] { Extra() });
        var after = TouchSettings.Geometry(TouchControl.Jump, 1920, 1080);
        Check(before.X == after.X && before.Y == after.Y && before.Radius == after.Radius, "extra buttons preserve legacy geometry");
        var draft = TouchSettings.CopyInstances(); draft[0] = draft[0] with { Action = TouchControl.Clip };
        Check(TouchSettings.ExtraInstances[0].Action == TouchControl.Jump, "draft copy does not mutate live settings");
        TouchSettings.ReplaceInstances(draft); draft[0] = Extra();
        Check(TouchSettings.ExtraInstances[0].Action == TouchControl.Clip, "replacement owns its copied storage");
        var edge = TouchSettings.InstanceGeometry(Extra(x: 0, y: 1), 1080, 1920, 1);
        Check(edge.X >= edge.Radius && edge.Y + edge.Radius <= 1920, "rotated screen bounds contain entire target");
        TouchSettings.ResetLayout();
    }
    private static void AllActions()
    {
        foreach (var action in Enum.GetValues<TouchControl>())
        {
            TouchSettings.ReplaceInstances(new[] { Extra(action: action) });
            var c = Controls();
            if (action == TouchControl.Scan) c.ScanVisorActive = true;
            var button = Button(c);
            Check(button.Action.ToString() == action.ToString() && button.Visible, "action parity/visibility " + action);
            Down(c, 1);
            Check(c.IsHeld(button.Action), "extra press maps to " + action);
            c.PointerUp(1);
            Check(!c.IsHeld(button.Action), "extra release maps to " + action);
        }
        TouchSettings.ReplaceInstances(new[] { Extra(enabled: false) });
        var disabled = new TouchControls(); disabled.Layout(1920, 1080, 1);
        Check(!Button(disabled).Visible, "disabled extras hidden at initial layout");
    }
    private static void IndependentPointers()
    {
        TouchSettings.ReplaceInstances(new[] { Extra(), Extra(1001, x: .35f) });
        var c = Controls(); Down(c, 1); Down(c, 2, 1001);
        var second = Button(c, 1001);
        c.PointerMove(2, second.CentreX, second.CentreY, Environment.TickCount64, Environment.TickCount64);
        c.PointerUp(1);
        Check(c.IsHeld(TouchAction.Jump), "moving second duplicate uses its own hitbox");
        c.PointerUp(2); Check(!c.IsHeld(TouchAction.Jump), "last duplicate release clears action");
        Down(c, 3); Down(c, 4, 1001);
        Check(c.PointerMove(3, 960, 50, Environment.TickCount64, Environment.TickCount64), "slide off first duplicate releases pointer");
        Check(c.IsHeld(TouchAction.Jump), "slide off one duplicate preserves other");
        c.PointerUp(4); Check(!c.IsHeld(TouchAction.Jump), "slide off then final release clears action");
        TouchSettings.ReplaceInstances(new[] { Extra(action: TouchControl.Clip), Extra(1001, TouchControl.Clip, .35f) });
        c = Controls(); Down(c, 5); Down(c, 6, 1001); c.PointerUp(5); c.PointerUp(6);
        Check(c.TakeClipPress() && c.TakeClipPress() && !c.TakeClipPress(), "simultaneous clip instance taps retain exactly two edges");
    }
    private static void Lifecycle()
    {
        TouchSettings.ReplaceInstances(new[] { Extra() });
        var c = Controls(); Down(c, 1); c.Layout(1080, 1920, 1);
        Check(!c.IsHeld(TouchAction.Jump), "rotation cancels stale pointer ownership");
        Down(c, 2); c.ReloadSettings();
        Check(!c.IsHeld(TouchAction.Jump), "settings reload cancels stale pointer ownership");
        var published = c.Buttons;
        using var drawing = published.GetEnumerator(); drawing.MoveNext();
        int count = published.Count;
        TouchSettings.ReplaceInstances(Array.Empty<TouchButtonInstance>()); c.ReloadSettings();
        int visited = 1; while (drawing.MoveNext()) visited++;
        Check(visited == count && published.Count == count, "overlay enumeration survives instance publication without list mutation");
        Check(c.Buttons.Count < published.Count, "new overlay publication removes deleted instances independently");
        TouchSettings.ReplaceInstances(new[] { Extra() }); c.ReloadSettings();
        Down(c, 3); TouchSettings.ReplaceInstances(new[] { Extra(action: TouchControl.Morph) }); c.ReloadSettings();
        Check(!c.IsHeld(TouchAction.Jump) && !c.IsHeld(TouchAction.Morph), "reassignment does not manufacture held edge");
    }
}
