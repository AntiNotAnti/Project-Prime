using System;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class MomentumCombatEvidenceTests
{
    internal static int Run()
    {
        try
        {
            NetArchitectureTests.Check(NetCampaignEvidence.Enabled,
                "set PRIME_NET2_EVIDENCE to a fresh output path for this process-owned evidence test");
            NetShotDiagnostics.Reset();
            var baseline = NetCampaignEvidence.CaptureShotRewind();
            const int weapon = 0;
            NetShotDiagnostics.RewindSamples[weapon] = 2;
            NetShotDiagnostics.RewindFrames[weapon] = 17;
            NetShotDiagnostics.RewindClamps[weapon] = 1;
            NetShotDiagnostics.Reset();
            NetArchitectureTests.Check(NetShotDiagnostics.RewindSamples[weapon] == 0
                && NetShotDiagnostics.RewindFrames[weapon] == 0, "native world counters still reset normally");
            NetShotDiagnostics.RewindSamples[weapon] = 3;
            NetShotDiagnostics.RewindFrames[weapon] = 21;
            var active = NetCampaignEvidence.CaptureShotRewind();
            NetArchitectureTests.Check(active.SamplesByWeapon[weapon] - baseline.SamplesByWeapon[weapon] == 5
                && active.FramesByWeapon[weapon] - baseline.FramesByWeapon[weapon] == 38
                && active.ClampsByWeapon[weapon] - baseline.ClampsByWeapon[weapon] == 1,
                "completed and current worlds contribute exactly once");
            active.SamplesByWeapon[weapon] = -100;
            NetArchitectureTests.Check(NetCampaignEvidence.CaptureShotRewind().SamplesByWeapon[weapon]
                - baseline.SamplesByWeapon[weapon] == 5, "captures cannot mutate retained evidence");
            NetShotDiagnostics.Reset();
            NetShotDiagnostics.Reset();
            var empty = NetCampaignEvidence.CaptureShotRewind();
            NetArchitectureTests.Check(empty.SamplesByWeapon[weapon] - baseline.SamplesByWeapon[weapon] == 5
                && empty.FramesByWeapon[weapon] - baseline.FramesByWeapon[weapon] == 38,
                "empty-room and repeated resets retain evidence without duplication");
            Console.WriteLine("PASS process-owned shot rewind evidence across native world resets");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
