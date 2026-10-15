using System;
using MphRead.Mods;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class OutlineColorTests
{
    internal static int Run()
    {
        var original = OutlineColorPalette.CopyCurrent();
        int checks = 0;
        void Check(bool condition, string reason)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("OUTLINE COLORS " + reason);
        }
        try
        {
            var palette = new OutlineColorSettings();
            palette.Teams[0] = "#102030";
            palette.Teams[3] = "#AB34EF";
            palette.Players[0] = "#010203";
            palette.Players[7] = "#DDEE99";
            OutlineColorPalette.Publish(palette);
            Vector4 team = OutlineColorPalette.Color(true, 0);
            Vector4 player = OutlineColorPalette.Color(false, 0);
            Check(MathF.Abs(team.X - 16f / 255f) < .0001f
                && MathF.Abs(team.Y - 32f / 255f) < .0001f
                && MathF.Abs(team.Z - 48f / 255f) < .0001f,
                "exact unbrightened team RGB");
            Check(MathF.Abs(player.X - 1f / 255f) < .0001f
                && MathF.Abs(player.Y - 2f / 255f) < .0001f
                && MathF.Abs(player.Z - 3f / 255f) < .0001f,
                "exact unbrightened FFA RGB");
            Check(BrightSkins.ResolveOutlineColor(PlayerOutlineStyle.Custom, true, 0, 7) == team
                && BrightSkins.ResolveOutlineColor(PlayerOutlineStyle.Custom, false, 3, 0) == player,
                "team and player slot selection independent");
            Check(OutlineColorPalette.Color(true, 3).X > .66f
                && OutlineColorPalette.Color(false, 7).Z > .55f,
                "four team and eight player slots reachable");
            Check(OutlineColorPalette.Color(false, 99).W == 1,
                "out-of-range slot guarded");
            string json = OutlineColorPalette.Json;
            Check(OutlineColorPalette.TryParse(json, out var roundtrip)
                && roundtrip!.Teams[0] == "#102030"
                && roundtrip.Players[7] == "#DDEE99", "roundtrip persistence");
            var edited = OutlineColorPalette.CopyCurrent();
            edited.Teams[0] = "#FFFFFF";
            Check(OutlineColorPalette.Color(true, 0) == team, "unpublished edits detached");
            foreach (string bad in new[] { "no-json", "{}", "null", "{\"teams\":[],\"players\":[]}",
                "{\"teams\":[\"#1\"]}", new string(' ', 2049) })
            {
                Check(!OutlineColorPalette.TryParse(bad, out _), "malformed/bounded JSON refused");
                OutlineColorPalette.Json = bad;
                Check(OutlineColorPalette.Color(true, 0) == team, "invalid persisted colors ignored");
            }
            Check(BrightSkins.ResolveOutlineColor(PlayerOutlineStyle.Red, false, 0, 5)
                == new Vector4(1, .05f, .05f, 1), "legacy red unchanged");
        }
        finally
        {
            OutlineColorPalette.Publish(original);
        }
        Console.WriteLine("OUTLINE COLORS PASS " + checks);
        return 0;
    }
}
