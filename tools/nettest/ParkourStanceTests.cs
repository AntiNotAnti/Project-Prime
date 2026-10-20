using System;
using MphRead.Mods.Movement;

namespace MphRead.NetTest;

// Pure accepted-shape contracts; gameplay clearance and velocity need scene tests.
internal static class ParkourStanceTests
{
    private static int _checks;
    private static void Check(bool good, string label)
    { _checks++; if (!good) throw new InvalidOperationException(label); }
    private static void Near(float actual, float expected, string label)
        => Check(float.IsFinite(actual) && MathF.Abs(actual - expected) <= .000002f,
            $"{label}: {actual} != {expected}");

    internal static int Run()
    {
        try
        {
            _checks = 0;
            for (int bits = 0; bits < 16; bits++)
                Check(ParkourStance.CanTuck((bits & 1) != 0, (bits & 2) != 0,
                    (bits & 4) != 0, (bits & 8) != 0) == (bits == 15),
                    "tuck requires Parkour, eligibility, airborne state and request: " + bits);
            Near(ParkourStance.TuckRise, (3686f / 4096 + .5f) * (1 - .65f),
                "rise uses the fixed native eye and crouch ratio");
            Check(ParkourStance.TuckRise > 0, "tuck raises the short capsule");
            Check(Metadata.PlayerValues.Count == 8, "all eight native dimension rows are covered");
            for (int index = 0; index < Metadata.PlayerValues.Count; index++)
            {
                var values = Metadata.PlayerValues[index];
                float bottom = Fixed.ToFloat(values.MinPickupHeight);
                float top = Fixed.ToFloat(values.MaxPickupHeight);
                float radius = Fixed.ToFloat(values.BipedColRadius);
                float eye = Fixed.ToFloat(values.AimYOffset);
                string row = "native row " + index;
                foreach (bool tucked in new[] { false, true })
                {
                    Check(ParkourStance.Bottom(bottom, false, tucked) == bottom
                        && ParkourStance.Top(bottom, top, radius, false, tucked) == top,
                        row + " standing and invalid tuck-only states retain exact native geometry");
                }
                float crouchTop = TraversalStance.Top(bottom, top, radius, true);
                Check(ParkourStance.Bottom(bottom, true, false) == bottom
                    && ParkourStance.Top(bottom, top, radius, true, false) == crouchTop,
                    row + " ordinary crouch retains exact historical geometry");
                float tuckBottom = ParkourStance.Bottom(bottom, true, true);
                float tuckTop = ParkourStance.Top(bottom, top, radius, true, true);
                Near(tuckBottom - bottom, ParkourStance.TuckRise, row + " raised feet");
                Near(tuckTop - crouchTop, ParkourStance.TuckRise, row + " equally raised capsule top");
                Near(tuckTop - tuckBottom, crouchTop - bottom, row + " crouch height preserved");
                Check(tuckBottom >= bottom && tuckTop <= top && tuckTop - tuckBottom >= radius * 2,
                    row + " tuck fits inside standing shape and retains capsule diameter");
                Check(eye >= tuckBottom && eye <= tuckTop,
                    row + " native eye remains inside tucked shape");
                Check(TraversalStance.EyeOffset(bottom, top, radius, eye, false) == eye,
                    row + " unchanged standing eye helper preserves native aim height");
                Check(Fixed.ToFloat(values.AimYOffset) == eye
                    && Fixed.ToFloat(values.MinPickupHeight) == bottom
                    && Fixed.ToFloat(values.MaxPickupHeight) == top
                    && Fixed.ToFloat(values.BipedColRadius) == radius,
                    row + " shape queries leave native metadata unchanged");
            }
            Console.WriteLine($"PARKOUR STANCE RULES PASS {_checks} assertions; pure eligibility and native shape contracts");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("PARKOUR STANCE RULES FAIL " + error); return 1; }
    }
}
