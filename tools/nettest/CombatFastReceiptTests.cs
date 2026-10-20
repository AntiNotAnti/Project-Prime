using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Constructed ledger outcomes and packet-only actors, using the real serialized
// Receive/admission/Answer path. The caller slot is supplied by this fixture;
// endpoint authentication, native emission/collision and UDP are not exercised.
internal static class CombatFastReceiptTests
{
    private static readonly BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    private static int _checks;
    private static PlayerEntity _shooter = null!, _victim = null!;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("Fast paid receipts: " + label);
        _checks++;
    }
    private static void Frame(uint value)
        => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, value);
    private static T Table<T>(string name)
        => (T)typeof(NetHitClaims).GetField(name, Hidden)!.GetValue(null)!;
    private static void Prepare(CombatFastMode mode = CombatFastMode.Paid)
    {
        HealthShotTests.Session(); NetHitClaims.Reset(); NetDamage.Reset(); NetHitPrediction.Reset();
        NetHitClaims.Enabled = true;
        typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, true);
        for (int slot = 0; slot < PlayerEntity._players.Length; slot++)
        {
            var player = HealthShotTests.Player(slot);
            player.LoadFlags = LoadFlags.Active | LoadFlags.Spawned;
            PlayerEntity._players[slot] = player;
        }
        _shooter = PlayerEntity._players[0]; _victim = PlayerEntity._players[1];
        _shooter.OwningScene.GameState.Mode = GameMode.Battle;
        _shooter.OwningScene.GameState.InstaGib = false;
        _shooter.OwningScene.GameState.OneInTheChamber = false;
        _shooter.OwningScene.GameState.NoImperialist = false;
        NetShotProof.Mode = ShotProofMode.Off; NetTimedShotProof.Mode = TimedGrantProofMode.Off;
        NetCombatFastPath.Mode = mode; NetCombatFastPath.ResetEvidenceForTests();
        Frame(100);
        Check(NetHitClaims.Arbitrating && NetSession.AuthorityEpoch != 0 && NetSession.CurrentMatchId != 0,
            "packet-only fixture has an authority stream and real lifecycle tracker state");
    }
    private static HitClaimPacket Claim(uint shot = 17, ushort id = 19,
        BeamType weapon = BeamType.PowerBeam, bool head = false)
    {
        var key = ShotKey.For(0, shot);
        return new() {
            MatchId = key.MatchId, AuthorityEpoch = key.AuthorityEpoch,
            ShooterGeneration = key.Generation, ShooterLifeId = key.LifeId,
            ClaimId = id, ShotId = shot, ComponentId = 1, Beam = (byte)weapon,
            VictimSlot = 1, VictimGeneration = NetPlayerLifecycle.Generation(1), VictimLifeId = NetPlayerLifecycle.Get(1),
            Flags = (byte)(HitClaimPacket.FlagDirect | (head ? HitClaimPacket.FlagHeadshot : 0)),
            Damage = 12, Frame = 100, AckFrame = 90, LaunchFrame = 90,
            HitPoint = new Vector3(10, 2, 3), Direction = Vector3.Zero };
    }
    private static CombatAckEntry Seed(in HitClaimPacket claim, uint sequence = 31)
    {
        var outcome = new CombatAckEntry {
            ShotId = claim.ShotId, VictimSlot = claim.VictimSlot,
            VictimGeneration = claim.VictimGeneration, VictimLife = claim.VictimLifeId,
            Result = (byte)CombatAckResult.Applied, DamageApplied = 12, HealthAfter = 87,
            DamageSequence = sequence,
            Flags = CombatAckFlags.OutcomePresent
                | ((claim.Flags & HitClaimPacket.FlagHeadshot) != 0 ? CombatAckFlags.Headshot : 0) };
        var key = NetShotProof.PaymentKey(0, claim);
        using (var reservation = NetCombatPayments.Begin(key, claim.Beam))
        {
            Check(reservation.Allowed, "constructed full-key ledger payment can reserve once");
            reservation.Commit(outcome);
        }
        Check(NetCombatPayments.TryGet(key, claim.Beam, out var actual) && actual.Equals(outcome),
            "production ledger retains the exact constructed outcome including full damage sequence");
        return outcome;
    }
    private static byte[] Bytes(in CombatAckEntry value)
    { byte[] bytes = new byte[CombatAckEntry.Size]; value.Write(bytes); return bytes; }
    private static string Gameplay()
    {
        var game = _shooter.OwningScene.GameState;
        return JsonSerializer.Serialize(new {
            ShooterHealth = _shooter.Health, VictimHealth = _victim.Health,
            ShooterSequence = NetDamage.Sequence(0), VictimSequence = NetDamage.Sequence(1),
            VictimFrozen = _victim.ModFrozen, VictimDisrupted = _victim.ModDisruptedTimer,
            game.Points, game.Kills, game.Deaths, game.ShotsFired, game.ShotsHit,
            game.MatchDamageDealt, game.MatchDamageTaken,
            _shooter.OwningScene.Random.Rng1, _shooter.OwningScene.Random.Rng2,
            NetCombatPayments.Payments });
    }
    private static CombatAckEntry? Receive(in HitClaimPacket claim, bool validWire = true)
    {
        byte[] packet = new byte[1 + HitClaimPacket.Size]; packet[0] = 1; claim.Write(packet.AsSpan(1));
        Check(NetHitClaims.ValidateWireClaims(packet) == validWire, "constructed carrier has the declared wire-shape validity");
        var answers = new List<CombatAckEntry>();
        var saved = NetHitClaims.CombatAckSink;
        NetHitClaims.CombatAckSink = (slot, values) => {
            Check(slot == 0, "existing Answer flush keeps the admitted caller slot");
            foreach (var value in values) answers.Add(value);
        };
        string before = Gameplay();
        uint frame = NetSession.NetFrame;
        try
        {
            // The explicit caller slot substitutes only for the transport's
            // authenticated peer lookup. No private Judge/Park is invoked.
            NetHitClaims.Receive(0, packet);
            typeof(NetHitClaims).GetMethod("FlushVerdicts", Hidden)!.Invoke(null, null);
            Check(Gameplay() == before && NetSession.NetFrame == frame,
                "Receive and existing ACK flush change no body, score, effect, RNG, sequence or payment");
            Check(answers.Count <= 1, "one constructed request produces at most one queued terminal ACK");
            if (answers.Count == 1)
                Check(answers[0].ClaimId == claim.ClaimId && answers[0].ShotId == claim.ShotId,
                    "first terminal retains exact request claim and shot correlation");
            return answers.Count == 0 ? null : answers[0];
        }
        finally { NetHitClaims.CombatAckSink = saved; }
    }
    private static void ExactOutcome(CombatAckEntry? actual, in CombatAckEntry paid, in HitClaimPacket claim, string label)
    {
        var expected = paid; expected.ClaimId = claim.ClaimId; expected.Result = HitVerdictPacket.ResultDuplicate;
        Check(actual.HasValue && Bytes(actual.Value).SequenceEqual(Bytes(expected)), label);
    }
    private static void PositiveModesAndRetries()
    {
        foreach (var mode in new[] { CombatFastMode.Off, CombatFastMode.Shadow, CombatFastMode.Paid })
        foreach (var weapon in new[] { BeamType.PowerBeam, BeamType.Imperialist })
        foreach (bool head in new[] { false, true })
        {
            Prepare(mode);
            Check(NetCombatFastPath.PaidEnabled == (mode == CombatFastMode.Paid)
                && NetCombatFastPath.ShadowEnabled == (mode != CombatFastMode.Off),
                "only Paid mutates receipt routing; Shadow and Paid retain observations");
            var claim = Claim(weapon: weapon, head: head);
            var paid = Seed(claim, head ? uint.MaxValue : 31);
            long count = NetCombatFastPath.PaidReceipts, payments = NetCombatPayments.Payments;
            int pending = NetHitClaims.ClaimsPendingCurrent;
            var first = Receive(claim);
            ExactOutcome(first, paid, claim, "all modes return identical serialized immutable paid outcomes");
            long expected = mode == CombatFastMode.Paid ? 1 : 0;
            Check(NetCombatFastPath.PaidReceipts - count == expected
                && NetCombatPayments.Payments == payments && NetHitClaims.ClaimsPendingCurrent == pending,
                "only real Paid first admission bypasses without a new payment or pending claim");
            var retry = Receive(claim);
            Check(first.HasValue && retry.HasValue && Bytes(first.Value).SequenceEqual(Bytes(retry.Value))
                && NetCombatFastPath.PaidReceipts - count == expected
                && NetHitClaims.ClaimsPendingCurrent == pending,
                "lost first ACK retry uses the upstream immutable terminal cache without another bypass");
            if (mode != CombatFastMode.Off)
            {
                var row = NetCombatFastPath.CaptureRows().Single(value => value.Stage == FastProbeStage.ClaimReady);
                Check(row.Decision.State == FastDecisionState.VerifiedPaid && !row.Decision.WouldPay
                    && row.PaidBypass == (mode == CombatFastMode.Paid) && row.Compared && row.Agrees,
                    "ready-row evidence distinguishes actual bypass from shadow eligibility");
            }
            else Check(NetCombatFastPath.CaptureRows().Length == 0, "Off cannot fabricate fast-route observations");
        }
    }
    private static void NoBypass(in HitClaimPacket claim, string label, bool validWire = true)
    {
        long count = NetCombatFastPath.PaidReceipts;
        Receive(claim, validWire);
        Check(NetCombatFastPath.PaidReceipts == count, label);
    }
    private static void MissingReservedExpiredAndFamilies()
    {
        Prepare(); var claim = Claim(); NoBypass(claim, "missing payment retains legacy handling");
        Prepare(); claim = Claim();
        using (var reserved = NetCombatPayments.Begin(NetShotProof.PaymentKey(0, claim), claim.Beam))
        {
            Check(reserved.Allowed && NetCombatPayments.Inspect(NetShotProof.PaymentKey(0, claim), claim.Beam)
                .Knowledge == FastPaymentKnowledge.Reserved, "reservation exists without a committed outcome");
            NoBypass(claim, "reservation is never acknowledged as payment");
        }
        Prepare(); claim = Claim(); Seed(claim); Frame(100 + NetCombatPayments.Retention + 1);
        NoBypass(claim, "expired full-key payment cannot qualify a new fast receipt");
        foreach (var family in new[] { "child", "splash", "turret", "other-weapon" })
        {
            Prepare(); claim = Claim();
            if (family == "child") claim.ComponentId = 2;
            if (family == "splash") claim.Flags = 0;
            if (family == "turret") claim.Flags |= HitClaimPacket.FlagHalfturret;
            if (family == "other-weapon") claim.Beam = (byte)BeamType.Missile;
            Seed(claim);
            NoBypass(claim, "even an exact constructed paid " + family + " is outside the enabled receipt subset");
        }
        Prepare(); claim = Claim(); Seed(claim);
        var weaponMismatch = claim; weaponMismatch.Beam = (byte)BeamType.Imperialist;
        NoBypass(weaponMismatch, "a same-key payment cannot acknowledge another weapon through the fast route");
        Prepare(); claim = Claim(weapon: BeamType.ShockCoil); claim.Flags |= HitClaimPacket.FlagContinuousTick;
        NoBypass(claim, "valid continuous phase remains owned by the existing continuous pipeline");
        Prepare(); claim = Claim(); Seed(claim); claim.Flags |= HitClaimPacket.FlagContinuousTick;
        NoBypass(claim, "ordinary paid receipt cannot bypass malformed continuous shape", validWire: false);
        foreach (bool paidHead in new[] { false, true })
        {
            Prepare(); var original = Claim(weapon: BeamType.Imperialist, head: paidHead); Seed(original);
            claim = original; claim.Flags ^= HitClaimPacket.FlagHeadshot;
            NoBypass(claim, "head/body mismatch stays on existing legacy/correction path");
        }
    }
    private static void LifecycleAndCanonicalKeyGuards()
    {
        foreach (string field in new[] { "epoch", "match", "shooter-generation", "shooter-life",
            "victim-generation", "victim-life", "shot", "component", "kind" })
        {
            Prepare(); var original = Claim(); Seed(original); var claim = original;
            switch (field)
            {
                case "epoch": claim.AuthorityEpoch++; break;
                case "match": claim.MatchId++; break;
                case "shooter-generation": claim.ShooterGeneration++; break;
                case "shooter-life": claim.ShooterLifeId++; break;
                case "victim-generation": claim.VictimGeneration++; break;
                case "victim-life": claim.VictimLifeId++; break;
                case "shot": claim.ShotId++; break;
                case "component": claim.ComponentId++; break;
                case "kind": claim.Flags = 0; break;
            }
            NoBypass(claim, "canonical identity mismatch cannot borrow a paid outcome: " + field);
        }
        Prepare(); var cached = Claim(); var paid = Seed(cached);
        ExactOutcome(Receive(cached), paid, cached, "original current-life paid receipt is retained");
        long count = NetCombatFastPath.PaidReceipts;
        var next = HealthShotTests.State((ushort)(cached.VictimLifeId + 1), 77);
        Check(NetPlayerLifecycle.AcceptState(next, 101), "actual lifecycle tracker accepts declared replacement victim life");
        _victim = HealthShotTests.Player(1, 77); _victim.LoadFlags = LoadFlags.Active | LoadFlags.Spawned;
        PlayerEntity._players[1] = _victim;
        ExactOutcome(Receive(cached), paid, cached, "old-life cached terminal remains immutable without touching replacement body");
        Check(NetCombatFastPath.PaidReceipts == count && _victim.Health == 77,
            "cached terminal retry precedes victim-life gating without re-entering fast bypass");
        var uncached = cached; uncached.ClaimId++;
        var refused = Receive(uncached);
        Check(refused is { Result: HitVerdictPacket.ResultWrongLife }
            && NetCombatFastPath.PaidReceipts == count,
            "uncached old-victim-life payment cannot bypass the current lifecycle gate");
    }
    private static void ClaimIdCapacityAndEndingGuards()
    {
        Prepare(); var claim = Claim(id: 0); Seed(claim);
        NoBypass(claim, "zero claim ID never acquires a fast receipt");
        Prepare(); var first = Claim(); var paid = Seed(first);
        ExactOutcome(Receive(first), paid, first, "original claim ID settles once");
        long count = NetCombatFastPath.PaidReceipts;
        var conflict = first; conflict.ShotId++; Seed(conflict);
        var refused = Receive(conflict);
        Check(refused is { Result: HitVerdictPacket.ResultRefused }
            && NetCombatFastPath.PaidReceipts == count,
            "paid new shot with a pinned same claim ID is refused before bypass");
        ExactOutcome(Receive(first), paid, first, "conflict cannot overwrite the original retriable wire outcome");
        Prepare(); claim = Claim(id: 1); Seed(claim);
        Table<ushort[]>("_newestId")[0] = 300;
        var old = Receive(claim);
        Check(old is { Result: HitVerdictPacket.ResultTooOld } && NetCombatFastPath.PaidReceipts == 0,
            "outside-window unseen claim ID remains too old even with exact payment");

        Prepare(); claim = Claim(); Seed(claim);
        var ids = Table<ushort[,]>("_seenIds"); var results = Table<byte[,]>("_seenResults");
        // Explicit bounded cache-pressure construction, following existing
        // arbitration tests; no fabricated positive collision or ACK is used.
        for (int i = 0; i < NetHitClaims.SeenCapacity; i++)
        { ids[0, i] = (ushort)(i + 1); results[0, i] = byte.MaxValue; }
        var overflow = claim; overflow.ClaimId = 5000;
        var capacity = Receive(overflow);
        Check(capacity is { Result: HitVerdictPacket.ResultClaimCapacity }
            && NetCombatFastPath.PaidReceipts == 0,
            "full pinned receipt cache refuses exact paid overflow before bypass");
        overflow.ClaimId++;
        var unsafeReply = Receive(overflow);
        Check(unsafeReply is { Result: HitVerdictPacket.ResultClaimCapacity }
            && NetCombatFastPath.PaidReceipts == 0,
            "receipt unsafe quiet-window gate remains authoritative for another paid ID");

        Prepare(); claim = Claim(); paid = Seed(claim);
        var match = NetSession.ServerMatch!.Value; match.Flags |= MatchStatePacket.FlagEnding;
        typeof(NetSession).GetProperty(nameof(NetSession.ServerMatch))!.SetValue(null, match);
        ExactOutcome(Receive(claim), paid, claim, "ending-match existing paid outcome uses existing ACK-only path");
        Check(NetCombatFastPath.PaidReceipts == 0 && NetHitClaims.ClaimsPendingCurrent == 0,
            "ending branch precedes fast route and cannot create a rescue or new payment");
    }
    internal static int Run()
    {
        var players = (PlayerEntity[])PlayerEntity._players.Clone();
        var oldMode = NetCombatFastPath.Mode; var oldProof = NetShotProof.Mode;
        var oldTimed = NetTimedShotProof.Mode; bool enabled = NetHitClaims.Enabled;
        var oldSink = NetHitClaims.CombatAckSink;
        try
        {
            _checks = 0;
            PositiveModesAndRetries(); MissingReservedExpiredAndFamilies();
            LifecycleAndCanonicalKeyGuards(); ClaimIdCapacityAndEndingGuards();
            Console.WriteLine($"PASS: {_checks} constructed paid-receipt Receive/Answer assertions; no additional gameplay payment.");
            Console.WriteLine("SCOPE: constructed ledger entries and packet-only actors; actual serialized Receive, upstream admission, paid bypass, immutable retries and ACK flush. NOT_RUN: endpoint authentication/UDP, native asset collision, full Imperialist correction, server scheduling/p95 or presentation.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            NetSession.Stop(); NetCombatFastPath.Mode = oldMode; NetShotProof.Mode = oldProof;
            NetTimedShotProof.Mode = oldTimed; NetHitClaims.Enabled = enabled;
            NetHitClaims.CombatAckSink = oldSink;
            Array.Copy(players, PlayerEntity._players, players.Length);
        }
    }
}
