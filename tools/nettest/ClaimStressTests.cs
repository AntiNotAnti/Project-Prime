using System;
using System.Reflection;
using MphRead.Entities;
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
    private static Array Outbox => (Array)typeof(NetHitClaims)
        .GetField("_outbox", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    private static object Outgoing => Outbox.GetValue(0)!;
    private static void OutgoingField(string name, object value)
    {
        var entry = Outgoing;
        entry.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(entry, value);
        Outbox.SetValue(entry, 0);
    }
    private static T OutgoingValue<T>(string name) => (T)Outgoing.GetType()
        .GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Outgoing)!;
    private static ShotKey RetryFixture(int ping)
    {
        HealthShotTests.Session(); NetHitClaims.Reset(); Frame(100);
        NetSession.SlotPing[0] = ping;
        var key = ShotKey.For(0, 19);
        OutgoingField("Live", true); OutgoingField("Id", (ushort)2); OutgoingField("ShotId", key.ShotId);
        OutgoingField("MatchId", key.MatchId); OutgoingField("AuthorityEpoch", key.AuthorityEpoch);
        OutgoingField("ShooterGeneration", key.Generation); OutgoingField("ShooterLifeId", key.LifeId);
        OutgoingField("VictimSlot", (byte)1); OutgoingField("VictimGeneration", NetPlayerLifecycle.Generation(1));
        OutgoingField("VictimLifeId", NetPlayerLifecycle.Get(1)); OutgoingField("Beam", (byte)BeamType.Missile);
        return key;
    }
    private static void AgeOutgoing()
    {
        Frame(NetSession.NetFrame + 1);
        Call("TickOutbox");
    }
    private static void RetrySchedule()
    {
        byte[] wire = new byte[1 + HitClaimPacket.MaxPerPacket * HitClaimPacket.Size];
        var key = RetryFixture(1000); // Native interval clamps to forty frames.
        NetArchitectureTests.Check(NetHitClaims.Compose(wire) > 0 && OutgoingValue<int>("Sends") == 1,
            "claim first transmission is immediate");
        for (int age = 1; age <= 39; age++)
        {
            AgeOutgoing();
            NetArchitectureTests.Check(NetHitClaims.Compose(wire) == 0, "claim waits for elapsed retry interval");
        }
        AgeOutgoing(); // Deliberately miss the exact age-forty Compose call.
        AgeOutgoing();
        NetArchitectureTests.Check(NetHitClaims.Compose(wire) > 0 && OutgoingValue<int>("Sends") == 2,
            "missed Compose deadline resends on the next eligible frame");
        NetArchitectureTests.Check(NetHitClaims.Compose(wire) == 0,
            "retry resets its elapsed interval rather than repeating immediately");
        uint sequence = NetDamage.Sequence(1);
        var ack = new CombatAckEntry { ClaimId = 2, ShotId = key.ShotId + 1,
            VictimSlot = 1, VictimGeneration = NetPlayerLifecycle.Generation(1), VictimLife = NetPlayerLifecycle.Get(1),
            Result = (byte)CombatAckResult.AlreadyResolved, DamageApplied = 32, HealthAfter = 67,
            DamageSequence = sequence, Flags = CombatAckFlags.OutcomePresent };
        byte[] verdict = new byte[HitVerdictPacket.HeaderSize + CombatAckEntry.Size];
        HitVerdictPacket.Write(verdict, new[] { ack }, key.MatchId, key.AuthorityEpoch, key.Generation, key.LifeId);
        NetHitClaims.ApplyVerdicts(verdict);
        NetArchitectureTests.Check(OutgoingValue<bool>("Live"), "retry still rejects an ACK for a different shot");
        ack.ShotId = key.ShotId;
        HitVerdictPacket.Write(verdict, new[] { ack }, key.MatchId, key.AuthorityEpoch, key.Generation, key.LifeId);
        NetHitClaims.ApplyVerdicts(verdict);
        NetArchitectureTests.Check(!OutgoingValue<bool>("Live") && NetHitClaims.Compose(wire) == 0
            && NetDamage.Sequence(1) == sequence, "exact retry ACK retires outbox without a new native payment");

        RetryFixture(517); // Interval thirty-seven, then RTT changes after age thirty-six.
        NetArchitectureTests.Check(NetHitClaims.Compose(wire) > 0, "dynamic RTT fixture sends initial claim");
        for (int age = 1; age <= 36; age++)
        {
            AgeOutgoing();
            NetArchitectureTests.Check(NetHitClaims.Compose(wire) == 0, "dynamic RTT retains minimum wait");
        }
        NetSession.SlotPing[0] = 500; AgeOutgoing(); // New interval thirty-six has already elapsed.
        NetArchitectureTests.Check(NetHitClaims.Compose(wire) > 0 && OutgoingValue<int>("Sends") == 2,
            "RTT interval moving behind current age cannot skip the retry");
        NetArchitectureTests.Check(NetHitClaims.Compose(wire) == 0,
            "dynamic interval change does not create an immediate extra send");

        RetryFixture(0); int sends = NetHitClaims.Compose(wire) > 0 ? 1 : 0;
        for (int age = 1; age <= 60; age++)
        {
            AgeOutgoing();
            if (NetHitClaims.Compose(wire) > 0) sends++;
        }
        NetArchitectureTests.Check(sends == 6 && OutgoingValue<int>("Sends") == 6
            && OutgoingValue<bool>("Live") && NetHitClaims.Unanswered == 0,
            "retry keeps six-send cap and remains live through age sixty");
        AgeOutgoing();
        NetArchitectureTests.Check(OutgoingValue<bool>("Live") && NetHitClaims.Unanswered == 0,
            "age sixty-one releases prediction while terminal receipt remains live");
        NetArchitectureTests.Check(OutgoingValue<bool>("PredictionReleased") && ReceiptCount() == 1
            && DrainClaims() == 1 && NetHitClaims.Compose(wire) == 0,
            "six-send cap retains one receipt in client drain after prediction release");
        const int deliveryDeadline = 6 * 40 + 2 * NetHitClaims.MaxGraceFrames + 40;
        for (int age = 62; age <= deliveryDeadline; age++)
        {
            AgeOutgoing();
            NetArchitectureTests.Check(NetHitClaims.Compose(wire) == 0,
                "delivery retention cannot exceed six sends");
        }
        NetArchitectureTests.Check(OutgoingValue<bool>("Live") && NetHitClaims.Unanswered == 0
            && ReceiptCount() == 1, "receipt remains bounded and live through delivery deadline");
        AgeOutgoing();
        NetArchitectureTests.Check(!OutgoingValue<bool>("Live") && NetHitClaims.Unanswered == 1
            && NetHitClaims.Compose(wire) == 0 && ReceiptCount() == 0 && DrainClaims() == 0,
            "receipt exhausts once beyond bounded delivery deadline");
        AgeOutgoing();
        NetArchitectureTests.Check(NetHitClaims.Unanswered == 1, "expired receipt is counted only once");
        ReceiptAfterPredictionRelease();
        NetHitClaims.Reset();
    }
    private static int ReceiptCount() => (int)typeof(NetHitClaims)
        .GetProperty("OutgoingReceipts", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    private static int DrainClaims()
    {
        object status = NetCampaignDrain.Capture();
        return (int)status.GetType().GetProperty("ClaimsPending")!.GetValue(status)!;
    }
    private static void ReceiptAfterPredictionRelease()
    {
        HealthShotTests.Session(); NetHitClaims.Reset(); NetHitPrediction.Reset(); Frame(100);
        NetSession.SlotPing[0] = 1000;
        var key = ShotKey.For(0, 19);
        var victim = HealthShotTests.Player(1);
        var savedVictim = PlayerEntity._players[1];
        try
        {
            PlayerEntity._players[1] = victim;
            DamageFlags flags = 0; uint damage = 32;
            NetHitPrediction.NoteHit(victim, HealthShotTests.Player(0), ref flags, ref damage,
                BeamType.Missile, launchFrame: 90, direct: true, shotId: key.ShotId, shotKey: key, componentId: 1);
            victim.Health = NetHitPrediction.HealthFor(1, 99);
            NetArchitectureTests.Check(victim.Health == 67 && ReceiptCount() == 1,
                "receipt fixture starts with a real named health prediction");
            byte[] wire = new byte[1 + HitClaimPacket.MaxPerPacket * HitClaimPacket.Size];
            NetHitClaims.Compose(wire);
            for (int age = 1; age <= 60; age++) { AgeOutgoing(); NetHitClaims.Compose(wire); }
            NetArchitectureTests.Check(NetHitPrediction.Denied == 0 && !OutgoingValue<bool>("PredictionReleased"),
                "visual prediction is not released before age sixty-one");
            AgeOutgoing();
            NetArchitectureTests.Check(OutgoingValue<bool>("Live") && OutgoingValue<bool>("PredictionReleased")
                && NetHitPrediction.Denied == 1 && ReceiptCount() == 1 && DrainClaims() == 1,
                "visual prediction is released exactly once while delivery remains pending");
            victim.Health = NetHitPrediction.HealthFor(1, 99);
            int health = victim.Health;
            long denied = NetHitPrediction.Denied, confirmed = NetHitPrediction.Confirmed;
            long combatAcks = NetHitPrediction.CombatAcks, damageCorrections = NetHitPrediction.DamageCorrections;
            long healthCorrections = NetHitPrediction.HealthCorrections, headCorrections = NetHitPrediction.HeadshotCorrections;
            uint sequence = NetDamage.Sequence(1);
            for (int age = 62; age <= 70; age++) { AgeOutgoing(); NetHitClaims.Compose(wire); }
            ushort claimId = OutgoingValue<ushort>("Id");
            var ack = new CombatAckEntry { ClaimId = claimId, ShotId = key.ShotId,
                VictimSlot = 1, VictimGeneration = NetPlayerLifecycle.Generation(1), VictimLife = NetPlayerLifecycle.Get(1),
                Result = (byte)CombatAckResult.AlreadyResolved, DamageApplied = 32, HealthAfter = 67,
                DamageSequence = sequence + 1, Flags = CombatAckFlags.OutcomePresent };
            byte[] verdict = new byte[HitVerdictPacket.HeaderSize + CombatAckEntry.Size];
            void Answer(ushort match, ulong epoch, ushort generation, ushort life)
            {
                HitVerdictPacket.Write(verdict, new[] { ack }, match, epoch, generation, life);
                NetHitClaims.ApplyVerdicts(verdict);
            }
            ack.ShotId++;
            Answer(key.MatchId, key.AuthorityEpoch, key.Generation, key.LifeId);
            NetArchitectureTests.Check(OutgoingValue<bool>("Live"), "late wrong-shot ACK cannot retire receipt");
            ack.ShotId = key.ShotId;
            Answer(key.MatchId, key.AuthorityEpoch + 1, key.Generation, key.LifeId);
            NetArchitectureTests.Check(OutgoingValue<bool>("Live"), "late wrong-stream ACK cannot retire receipt");
            Answer(key.MatchId, key.AuthorityEpoch, key.Generation, (ushort)(key.LifeId + 1));
            NetArchitectureTests.Check(OutgoingValue<bool>("Live"), "late wrong-shooter-life ACK cannot retire receipt");
            ack.VictimLife++;
            Answer(key.MatchId, key.AuthorityEpoch, key.Generation, key.LifeId);
            NetArchitectureTests.Check(OutgoingValue<bool>("Live"), "late wrong-victim-life ACK cannot retire receipt");
            ack.VictimLife--;
            Answer(key.MatchId, key.AuthorityEpoch, key.Generation, key.LifeId);
            NetArchitectureTests.Check(!OutgoingValue<bool>("Live") && ReceiptCount() == 0 && DrainClaims() == 0
                && NetHitClaims.Duplicate == 1 && NetHitClaims.Unanswered == 0,
                "exact age-seventy ACK retires only the retained delivery receipt");
            Answer(key.MatchId, key.AuthorityEpoch, key.Generation, key.LifeId);
            NetArchitectureTests.Check(victim.Health == health && NetHitPrediction.HealthFor(1, 99) == health
                && NetDamage.Sequence(1) == sequence && NetHitPrediction.Denied == denied
                && NetHitPrediction.Confirmed == confirmed && NetHitPrediction.CombatAcks == combatAcks
                && NetHitPrediction.DamageCorrections == damageCorrections
                && NetHitPrediction.HealthCorrections == healthCorrections
                && NetHitPrediction.HeadshotCorrections == headCorrections && NetHitClaims.Duplicate == 1,
                "late and duplicate receipts cannot replay visual health, corrections, settlement or native damage");
            RetryFixture(0);
            for (int age = 1; age <= 61; age++) AgeOutgoing();
            NetHitClaims.ForgetSlot(1, preserveFlights: true);
            NetArchitectureTests.Check(ReceiptCount() == 0 && DrainClaims() == 0,
                "victim lifecycle cancellation clears retained receipt even when physical flights are preserved");
            RetryFixture(0);
            for (int age = 1; age <= 61; age++) AgeOutgoing();
            NetHitClaims.ForgetSlot(0, preserveFlights: true);
            NetArchitectureTests.Check(ReceiptCount() == 0 && DrainClaims() == 0,
                "shooter lifecycle cancellation clears retained receipt");
        }
        finally { PlayerEntity._players[1] = savedVictim; }
    }
    private static void NativeOutcomeFirstAck()
    {
        HealthShotTests.Session(); NetHitClaims.Reset(); NetDamage.Reset(); Frame(100);
        typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, true);
        var savedMode = NetShotProof.Mode;
        var savedSink = NetHitClaims.CombatAckSink;
        var players = PlayerEntity._players;
        var savedPlayers = (PlayerEntity[])players.Clone();
        try
        {
            NetShotProof.Mode = ShotProofMode.Shadow;
            // Packet-only actors need explicit registration for Tick's death tracking.
            for (int i = 0; i < players.Length; i++) players[i] = HealthShotTests.Player(i);
            var shooter = players[0];
            var victim = players[1];
            shooter.LoadFlags = victim.LoadFlags = LoadFlags.Active | LoadFlags.Spawned;
            // Establish the lifecycle/death baseline before the native hit is filed.
            Call("TrackDeaths");
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
            Array.Copy(savedPlayers, players, players.Length);
            NetHitClaims.CombatAckSink = savedSink; NetShotProof.Mode = savedMode;
            typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, false);
        }
    }
    public static int Run()
    {
        try
        {
            NativeOutcomeFirstAck();
            RetrySchedule();
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
