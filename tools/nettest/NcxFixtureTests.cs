using System;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class NcxFixtureTests
{
    internal static int Run()
    {
        try
        {
            foreach (var (name, mode, room) in new[] {
                ("ncx-static", HitRig.RigMode.NcxStatic, "NCX EMULATED STATIC"),
                ("ncx-lateral", HitRig.RigMode.NcxLateral, "NCX EMULATED LATERAL"),
                ("ncx-jump", HitRig.RigMode.NcxJump, "NCX EMULATED JUMP") })
            {
                NetArchitectureTests.Check(HitRig.Configure(name) && HitRig.Mode == mode
                    && HitRig.NcxFixtureRoom(mode) == room, "explicit fixture name and room match");
                for (int slot = -1; slot < 9; slot++)
                    NetArchitectureTests.Check(HitRig.NcxFixtureCanFire(mode, slot) == (slot is 0 or 1),
                        "only both participant slots may fire in the emulated cell script");
            }
            NetArchitectureTests.Check(HitRig.Configure("ncx-observe") && HitRig.Mode == HitRig.RigMode.NcxObserve,
                "observer has an explicit no-fire harness mode");
            for (int slot = -1; slot < 9; slot++)
                NetArchitectureTests.Check(!HitRig.NcxFixtureCanFire(HitRig.RigMode.NcxObserve, slot),
                    "observer cannot acquire a firing role at any slot");
            NetArchitectureTests.Check(HitRig.Configure("powerbeam") && HitRig.Mode == HitRig.RigMode.Volley
                && !HitRig.NcxFixtureActive, "ordinary Power Beam route keeps its existing mode");
            NetArchitectureTests.Check(!HitRig.Configure("ncx-unknown"), "unknown fixture never silently selects a workload");
            NetArchitectureTests.Check(HitRig.NcxFixtureAimHeight(0) == .95f
                && HitRig.NcxFixtureAimHeight(299) == .95f
                && HitRig.NcxFixtureAimHeight(300) == .30f
                && HitRig.NcxFixtureAimHeight(599) == .30f
                && HitRig.NcxFixtureAimHeight(600) == .95f,
                "fixture preserves both exact native head and chest-height intervals");
            Console.WriteLine("PASS explicit NCX emulated fixture configuration and no-fire observer roles");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
