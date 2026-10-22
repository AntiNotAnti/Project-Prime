using System;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class ShotVisibilityTests
{
    internal static int Run()
    {
        try
        {
            void Check(bool value, string message) => NetArchitectureTests.Check(value, message);
            byte[] actual = new byte[12], excluded = new byte[12], mask = new byte[12];
            actual[0] = 1; actual[11] = 255;
            var diff = NetShotVisibilityProbe.Difference(actual, excluded, 2, 2, mask);
            Check(diff.Pixels == 2 && diff.MinX == 0 && diff.MinY == 0 && diff.MaxX == 1 && diff.MaxY == 1,
                "actual RGB differences count pixels, with top-left PNG bounds from bottom-up GL input");
            Check(mask[0] == 255 && mask[2] == 255 && mask[3] == 0 && mask[9] == 255,
                "difference mask attributes exactly changed RGB pixels, not nonblack background");
            diff = NetShotVisibilityProbe.Difference(actual, actual, 2, 2, mask);
            Check(diff.Pixels == 0 && diff.MinX == -1 && diff.MaxY == -1 && mask[0] == 0,
                "identical restored frames have no positive attribution");
            bool bad = false;
            try { NetShotVisibilityProbe.Difference(actual, excluded, 1, 1, mask); }
            catch (ArgumentException) { bad = true; }
            Check(bad, "mismatched readback dimensions refuse attribution");
            var key = new ShotKey(11, 2, 0, 3, 4, uint.MaxValue);
            var owner = new NcxDrawIdentity(key, 7, 0, 0);
            Check(owner.Valid && !default(NcxDrawIdentity).Valid && owner.Token.Length == 64,
                "pixel identity requires a full source lifecycle; output token has no raw identity");
            Check(owner != (owner with { Component = 8 }) && owner != (owner with { Key = key with { LifeId = 5 } })
                && owner != (owner with { Phase = 1 }) && owner != (owner with { Kind = 1 }),
                "prepared-item exclusion cannot substitute another component, life, phase or cue");
            var cue = new NcxDrawIdentity(key, 7, 0, 1, 1, 8, 9, 21, 0, 1);
            Check(!(owner with { Component = 0 }).Valid && cue.Valid
                && cue != (cue with { DamageEvent = 22 }) && !(cue with { DamageEvent = 0 }).Valid
                && cue != (cue with { Weapon = 1 }) && cue != (cue with { Provenance = 2 }),
                "flight requires a component; cue event/weapon/provenance never merge distinct facts");
            Check(owner.Token == "913fb2c9806df44855fa1577f150a70b27ea350b11e5982542d24a616a3ef167",
                "C# pixel token agrees with the versioned Python canonical source token");
            Console.WriteLine("PASS pixel difference, restoration negative control and full shot identity");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
