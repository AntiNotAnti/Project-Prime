using System;
using System.Reflection;
using MphRead.Mods.Network;

namespace MphRead.NetTest;
internal static class ClaimStressTests
{
    private static object? Call(string name, params object[] args)
    {
        var method = typeof(NetHitClaims).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        var parameters = method.GetParameters();
        if (args.Length < parameters.Length)
        {
            object[] expanded = new object[parameters.Length];
            args.CopyTo(expanded, 0);
            for (int i = args.Length; i < parameters.Length; i++)
                expanded[i] = parameters[i].DefaultValue!;
            // The legacy fixture uses launch as its explicit shot identity.
            if (name == "NoteLedger" && args.Length == 6) expanded[6] = args[3];
            if (name == "TakeLedger" && args.Length == 7) expanded[7] = args[3];
            var result = method.Invoke(null, expanded);
            Array.Copy(expanded, args, args.Length); return result;
        }
        return method.Invoke(null, args);
    }
    private static void Frame(uint frame) => typeof(NetSession).GetProperty("NetFrame")!.SetValue(null, frame);
    private static void NativeOutcomeFirstAck()
    {
        HealthShotTests.Session(); NetHitClaims.Reset(); NetDamage.Reset(); Frame(100);
        typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, true);
        var savedMode = NetShotProof.Mode;
        var savedSink = NetHitClaims.CombatAckSink;
        try
        {
            NetShotProof.Mode = ShotProofMode.Shadow;
            // Establish the lifecycle/death baseline before the native hit is filed.
            Call("TrackDeaths");
            var shooter = HealthShotTests.Player(0);
            var victim = HealthShotTests.Player(1);
            var key = ShotKey.For(0, 28);
            ushort sequence = NetDamage.Note(victim, shooter, BeamType.Missile, 0, null,
                amount: 32, launchFrame: 90, launchKey: key, componentId: 1,
                damageKind: NetShotProof.Kind(HitClaimPacket.FlagDirect));
            victim.Health = 67;
            NetHitClaims.CompleteAuthorityHit(victim, shooter, 99, 0, sequence, 90, BeamType.Missile);

            var claim = new HitClaimPacket { MatchId = key.MatchId, AuthorityEpoch = key.AuthorityEpoch,
                ShooterGeneration = key.Generation, ShooterLifeId = key.LifeId,
                VictimSlot = 1, VictimGeneration = NetPlayerLifecycle.Generation(1),
                VictimLifeId = NetPlayerLifecycle.Get(1), ClaimId = 1, ShotId = key.ShotId,
                ComponentId = 1, Beam = (byte)BeamType.Missile, Damage = 32,
                Flags = HitClaimPacket.FlagDirect, AckFrame = 100, LaunchFrame = 90 };
            // Use the existing stress fixture's admission seam, then run the actual
            // pending/native-ledger settlement and first CombatAck emission.
            var keys = (ShotKey[,])typeof(NetHitClaims).GetField("_seenKeys", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            keys[0, 1] = key;
            Call("Remember", 0, claim.ClaimId, byte.MaxValue);
            Call("Park", 0, claim);
            CombatAckEntry first = default; int answers = 0;
            NetHitClaims.CombatAckSink = (slot, entries) =>
            { foreach (var ack in entries) if (slot == 0 && ack.ClaimId == claim.ClaimId) { first = ack; answers++; } };
            NetHitClaims.Tick();
            NetArchitectureTests.Check(answers == 1 && first.Result == HitVerdictPacket.ResultDuplicate,
                "native hit receives terminal acknowledgement on first settlement");
            NetArchitectureTests.Check(first.ShotId == key.ShotId && first.ClaimId == claim.ClaimId,
                "first native outcome ACK retains exact requested shot identity");
            NetArchitectureTests.Check(first.VictimSlot == 1 && first.VictimGeneration == claim.VictimGeneration
                && first.VictimLife == claim.VictimLifeId, "native outcome ACK retains victim identity");
            NetArchitectureTests.Check(first.DamageApplied == 32 && first.HealthAfter == 67
                && first.DamageSequence == sequence && (first.Flags & CombatAckFlags.OutcomePresent) != 0,
                "claim correlation preserves native damage, health and sequence");
            NetArchitectureTests.Check(victim.Health == 67 && NetDamage.Sequence(1) == sequence
                && NetDamage.Resolved[1] == 1 && NetHitClaims.ClaimsPendingCurrent == 0,
                "native duplicate acknowledgement never reapplies damage");
            // Wrong-life responses can reuse a cached ClaimId while carrying a
            // different explicit shot identity; filling a native zero must preserve it.
            var wrongLife = first; wrongLife.ShotId = 29;
            Call("Answer", 0, claim.ClaimId, HitVerdictPacket.ResultWrongLife, false, wrongLife);
            Call("FlushVerdicts");
            NetArchitectureTests.Check(answers == 2 && first.ShotId == 29,
                "explicit wrong-life ACK identity survives a colliding cached claim ID");
        }
        finally
        {
            NetHitClaims.CombatAckSink = savedSink; NetShotProof.Mode = savedMode;
            typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, false);
        }
    }
    public static int Run()
    {
        try
        {
            NativeOutcomeFirstAck();
            HealthShotTests.Session(); NetHitClaims.Reset();
            // 36 unconsumed Shock Coil resolutions survive a full 72-frame grace.
            for (uint frame = 1; frame <= 71; frame += 2)
            { Frame(frame); Call("NoteLedger", 0, 1, frame, frame, 1, false); }
            Frame(72);
            for (uint frame = 1; frame <= 71; frame += 2)
            {
                object[] args = { 0, 1, frame, frame, 72u, 72, 0 };
                NetArchitectureTests.Check((bool)Call("TakeLedger", args)!, "sustained exact hit retained");
                NetArchitectureTests.Check((int)args[6] == 1, "authority damage retained");
                NetArchitectureTests.Check(!(bool)Call("TakeLedger", args)!, "resolution consumed once");
            }
            Call("NoteLedger", 0, 1, 72u, 70u, 32, false);
            Call("NoteLedger", 0, 1, 72u, 70u, 20, false);
            object[] splash = { 0, 1, 72u, 70u, 72u, 72, 0 };
            NetArchitectureTests.Check((bool)Call("TakeLedger", splash)! && (int)splash[6] == 32, "direct hit");
            NetArchitectureTests.Check((bool)Call("TakeLedger", splash)! && (int)splash[6] == 20, "splash hit");
            NetArchitectureTests.Check(!(bool)Call("TakeLedger", splash)!, "no third hit");
            Call("NoteLedger", 0, 1, 72u, 71u, 128, false);
            NetArchitectureTests.Check(!(bool)Call("TakeLedger", splash)!, "conflicting valid launches never fall back");
            NetHitClaims.Reset(); Frame(100);
            Call("NoteLedger", 0, 1, 80u, 79u, 3, false);
            var phases = (uint[,,])typeof(NetHitClaims).GetField("_authorityContinuousPhase", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            phases[0, 1, 0] = 60;
            object[] continuous = { 0, 1, 60u, 0 };
            NetArchitectureTests.Check((bool)Call("TakeContinuousLedger", continuous)! && (int)continuous[3] == 3,
                "continuous source tick matches independently of displayed-world launch frame");
            NetArchitectureTests.Check((bool)Call("TakeContinuousLedger", continuous)!, "same tick reuses terminal resolution");
            NetArchitectureTests.Check(NetHitClaims.ContinuousAlreadyResolved(0, 1, 60)
                && !NetHitClaims.ContinuousAlreadyResolved(0, 1, 61), "late physical copy suppressed only for exact continuous tick");
            for (uint launch = 100; launch < 164; launch++) Call("NoteLedger", 0, 1, 100u, launch, 1, false);
            NetArchitectureTests.Check(NetHitClaims.ContinuousAlreadyResolved(0, 1, 60)
                && NetHitClaims.ResolvedLedgerCapacityRefused == 1,
                "settled continuous tick stays protected under ledger pressure until grace expires");
            Frame(100 + (uint)NetHitClaims.MaxGraceFrames + 1);
            NetArchitectureTests.Check(!NetHitClaims.ContinuousAlreadyResolved(0, 1, 60), "continuous identity expires with authoritative ledger");
            NetHitClaims.Reset();
            for (int shooter = 0; shooter < 8; shooter++)
                for (int i = 0; i < NetHitClaims.PendingPerShooter; i++)
                    Call("Park", shooter, new HitClaimPacket { ClaimId = (ushort)(i + 1), VictimSlot = 1 });
            NetArchitectureTests.Check(NetHitClaims.ClaimsPendingCurrent == 512, "isolated per-shooter storage");
            byte capacityVerdict = 255;
            NetHitClaims.VerdictSink = (int slot, ReadOnlySpan<(ushort Id, byte Result)> verdicts) =>
            { foreach (var verdict in verdicts) if (slot == 0 && verdict.Id == 65) capacityVerdict = verdict.Result; };
            Call("Park", 0, new HitClaimPacket { ClaimId = 65, VictimSlot = 1 });
            Call("FlushVerdicts");
            NetArchitectureTests.Check(capacityVerdict == HitVerdictPacket.ResultClaimCapacity, "full partition sends terminal capacity verdict");
            NetArchitectureTests.Check(NetHitClaims.ClaimsCapacityRefused == 1, "capacity is explicit");
            NetArchitectureTests.Check(NetHitClaims.ResolvedLedgerOverwrittenUnused == 0, "no unmatched overwrite");
            NetHitClaims.Reset(); Frame(80);
            for (uint launch = 1; launch <= 65; launch++) Call("NoteLedger", 0, 1, 80u, launch, 1, false);
            NetArchitectureTests.Check(NetHitClaims.ResolvedLedgerCapacityRefused == 1
                && NetHitClaims.ResolvedLedgerOverwrittenUnused == 0, "saturated resolution storage preserves all unused identities");
            Call("Park", 0, new HitClaimPacket { ClaimId = 66, VictimSlot = 1 });
            NetArchitectureTests.Check(NetHitClaims.ClaimsPendingCurrent == 0 && NetHitClaims.ClaimsCapacityRefused == 1,
                "unrecorded physical hit fences rescue until grace expires");
            NetHitClaims.ValidateLedgerCounters();
            Console.WriteLine("PASS: claim retention, multiplicity, exact matching, shooter isolation and capacity refusal");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { NetHitClaims.VerdictSink = null; NetSession.Stop(); }
    }
}
