using System;
using System.Linq;
using System.Reflection;
using MphRead.Mods.Launcher;

internal static class HomePartyPadAcceptance
{
    internal static void Run()
    {
        // Keep the production helper internal; inspect its structured geometry,
        // not GLSL text. The shader consumes this same helper's geometry.
        Type helper = typeof(LauncherLobbyFormation).Assembly.GetType(
            "MphRead.Mods.Launcher.LauncherHomePartyPads", throwOnError: true)!;
        MethodInfo at = helper.GetMethod("At", BindingFlags.NonPublic | BindingFlags.Static)!;
        var pads = Enumerable.Range(0, 3).Select(i =>
            ((float X, float Y, float RadiusX, float RadiusY))at.Invoke(null, new object[] { i })!).ToArray();
        const float visibleScale = 1.025f; // Conservative bound for shader edge's q <= 1.05.
        var bounds = pads.Select(p => (Left: p.X - p.RadiusX * visibleScale,
            Right: p.X + p.RadiusX * visibleScale, Top: p.Y - p.RadiusY * visibleScale,
            Bottom: p.Y + p.RadiusY * visibleScale)).ToArray();
        Check(bounds.All(b => b.Left >= .27f && b.Right <= .73f && b.Top > .50f && b.Bottom < .65f),
            "three Home party pads stay inside central bay upper floor band");
        for (int a = 0; a < pads.Length; a++)
        for (int b = a + 1; b < pads.Length; b++)
        {
            float gap = Math.Max(bounds[a].Left - bounds[b].Right, bounds[b].Left - bounds[a].Right);
            Check(gap >= .04f, "Home party pedestal visible extents have separate horizontal bounds");
        }
        var hero = LauncherLobbyFormation.At(0);
        Check(bounds.All(b => hero.PadY - hero.RadiusY - b.Bottom >= .10f),
            "Home party pads leave ten percent framebuffer height before original foreground platform");
        Check(pads.All(p => p.RadiusX <= hero.RadiusX * .26f && p.RadiusY <= hero.RadiusY * .34f)
            && pads[0].RadiusX < pads[1].RadiusX && pads[0].RadiusY < pads[1].RadiusY,
            "Home party pads remain subordinate to hero and far center is smaller than near wings");
        Check(bounds[0].Bottom + .04f < bounds[1].Top && bounds[0].Bottom + .04f < bounds[2].Top
            && pads[1].Y == pads[2].Y && Math.Abs(pads[1].X + pads[2].X - 1f) < .001f,
            "far party pad has a separate depth row behind mirrored near wings");
        CheckAdaptiveLayout();
    }

    private static void CheckAdaptiveLayout()
    {
        Type layoutType = typeof(LauncherLobbyFormation).Assembly.GetType(
            "MphRead.Mods.Launcher.LauncherHomeStageLayout", throwOnError: true)!;
        MethodInfo create = layoutType.GetMethod("TryCreate", BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo padAt = layoutType.GetMethod("Pad", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var frame in new[] { (.26f, .80f, .14f, .96f), (.28f, .74f, .18f, .94f), (.30f, .72f, .22f, .92f) })
        {
            object?[] args = { frame.Item1, frame.Item2, frame.Item3, frame.Item4, MenuStageProfile.Default, null };
            Check((bool)create.Invoke(null, args)!, "measured Home rails yield a valid stage");
            object layout = args[5]!;
            float Value(string name) => (float)layoutType.GetProperty(name)!.GetValue(layout)!;
            float scale = Value("Scale");
            var pads = Enumerable.Range(0, 4).Select(i =>
                ((float X, float Y, float RadiusX, float RadiusY))padAt.Invoke(layout, new object[] { i })!).ToArray();
            Check(Math.Abs(Value("CenterX") - (frame.Item1 + frame.Item2) * .5f) < .0001f
                && Math.Abs((Value("HunterLeft") + Value("HunterRight")) * .5f - Value("CenterX")) < .0001f,
                "local Hunter stays centered between measured rails");
            Check(Value("HunterTop") >= frame.Item3 + .029f
                && pads[1].Y - .32f * scale >= frame.Item3 + .029f,
                "local viewport and hypothetical far Hunter retain full header headroom");
            Check(pads.All(p => p.X - p.RadiusX * 1.025f > frame.Item1
                && p.X + p.RadiusX * 1.025f < frame.Item2),
                "all four visible pedestal bounds fit measured center gap");
            for (int a = 0; a < pads.Length; a++)
            for (int b = a + 1; b < pads.Length; b++)
            {
                var pa = pads[a]; var pb = pads[b];
                Check(Math.Abs(pa.X - pb.X) > (pa.RadiusX + pb.RadiusX) * 1.025f
                    || Math.Abs(pa.Y - pb.Y) > (pa.RadiusY + pb.RadiusY) * 1.025f,
                    "adaptive Home pedestal visible bounds never overlap");
            }
        }
    }
    private static void Check(bool pass, string label)
    { if (!pass) throw new InvalidOperationException(label); Console.WriteLine("HOME PARTY PAD PASS " + label); }
}
