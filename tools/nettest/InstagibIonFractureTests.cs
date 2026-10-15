using System;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Runs without assets or a GL context. Render-path/occlusion acceptance is
// deliberately a separate integration gate, not implied by these checks.
internal static class InstagibIonFractureTests
{
    private static int _checks;
    private static void Check(bool ok, string description)
    {
        _checks++;
        if (!ok) throw new InvalidOperationException("ION FRACTURE: " + description);
    }

    internal static int Run()
    {
        Check(InstagibIonFracture.Capacity > 0 && InstagibIonFracture.Capacity <= 64,
            "bounded world effect capacity");
        Check(InstagibIonFracture.LifeSeconds > 0.5f &&
            InstagibIonFracture.LifeSeconds <= 1.0f, "short presentation lifetime");

        Vector3[] palette = new Vector3[8];
        for (int slot = 0; slot < palette.Length; slot++)
        {
            Vector3 color = InstagibIonFracture.ColorForSlot(slot);
            Check(float.IsFinite(color.X) && float.IsFinite(color.Y) && float.IsFinite(color.Z)
                && color.X >= 0 && color.X <= 1
                && color.Y >= 0 && color.Y <= 1
                && color.Z >= 0 && color.Z <= 1, "normalized finite slot color " + slot);
            for (int other = 0; other < slot; other++)
                Check((palette[other] - color).LengthSquared > 0.02f,
                    "distinct colors " + other + "/" + slot);
            palette[slot] = color;
        }
        Check(InstagibIonFracture.ColorForSlot(8) == palette[0],
            "deterministic eight-player wrap");
        Check(InstagibIonFracture.ColorForSlot(-1) == palette[0],
            "invalid slot safe fallback");
        Check(InstagibIonFracture.FadeAt(-1) == 1f
            && InstagibIonFracture.FadeAt(0) == 1f
            && InstagibIonFracture.FadeAt(1) == 0f
            && InstagibIonFracture.FadeAt(2) == 0f,
            "bounded fade endpoints");
        float previous = 1;
        for (int step = 1; step <= 100; step++)
        {
            float current = InstagibIonFracture.FadeAt(step / 100f);
            Check(current >= 0 && current <= previous, "monotone fade " + step);
            previous = current;
        }
        var world = new InstagibIonFracture();
        Check(world.Count == 0, "new scene owns empty pool");
        world.Clear();
        Check(world.Count == 0, "clear is idempotent");

        Console.WriteLine("ION FRACTURE PASS " + _checks + " asset-free policy assertions");
        return 0;
    }
}
