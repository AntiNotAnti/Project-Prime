using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ImpactBaselineTests
{
    internal static ReplayShotFact Fact => new(1, 2, 100, 90, 7, 65535, 0, 3, 4, 1, 5, 6,
        (byte)BeamType.Imperialist, ReplayShotFactFlags.Headshot, 20, 79, 0, 0, new(2, 3, 4));
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
        try
        {
            var f = Fact; var id = CombatImpactIdentity.From(f);
            Check(id == CombatImpactIdentity.From(f), "stable join key");
            foreach (var other in new[] { f with { AuthorityEpoch = 3 }, f with { MatchId = 2 },
                f with { ShooterSlot = 2 }, f with { ShooterGeneration = 4 }, f with { ShooterLifeId = 5 },
                f with { ShotId = 8 }, f with { VictimSlot = 2 }, f with { VictimGeneration = 6 },
                f with { VictimLifeId = 7 }, f with { DamageEventId = 1 }, f with { ResolveTick = 65636 } })
                Check(id != CombatImpactIdentity.From(other), "each identity field fences a hit");
            Check(id != CombatImpactIdentity.From(f, 1), "component identity");
            Check(BeamProjectileEntity.ModReplayIdentityMatches(id.Shot, f.ShotId, BeamType.Imperialist, f), "full shot matcher");
            Check(!BeamProjectileEntity.ModReplayIdentityMatches(id.Shot with { LifeId = 9 }, f.ShotId, BeamType.Imperialist, f), "no prior-life projectile match");
            Span<byte> wire = stackalloc byte[ReplayShotFactPacket.Size];
            ReplayShotFactPacket.Write(f, wire);
            Check(ReplayShotFactPacket.TryRead(wire, out var copy) && copy == f, "legacy fact byte roundtrip");
            ReplayShotFactPacket.Write(f with { ImpactPoint = new(float.NaN, 0, 0) }, wire);
            Check(!ReplayShotFactPacket.TryRead(wire, out _), "nonfinite position rejected");
            NetImpactDiagnostics.Reset(); NetImpactDiagnostics.Enabled = false;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) NetImpactDiagnostics.Record(f, ImpactStage.Authority);
            Check(GC.GetAllocatedBytesForCurrentThread() == before && NetImpactDiagnostics.Count == 0, "disabled telemetry allocates and retains nothing");
            NetImpactDiagnostics.Enabled = true;
            for (int i = 0; i < 5000; i++) NetImpactDiagnostics.Record(f with { ResolveTick = (uint)i }, ImpactStage.Authority);
            Check(NetImpactDiagnostics.Count == 4096 && NetImpactDiagnostics.Overwritten == 904, "bounded telemetry");
            Check(NetImpactDiagnostics.Snapshot()[0].Identity.ResolveTick == 904, "oldest eviction");
            NetImpactDiagnostics.Reset(); Check(NetImpactDiagnostics.Count == 0, "room reset");
            Console.WriteLine($"PASS: {checks} impact baseline assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { NetImpactDiagnostics.Enabled = false; NetImpactDiagnostics.Reset(); }
    }
}
