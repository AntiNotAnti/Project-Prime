using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;
internal static class PickupClaimTests
{
    private static int _checks;
    private static void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); _checks++; }
    private static ShotKey Life(int slot = 0, ushort generation = 1, ushort life = 1) => new(11, 7, slot, generation, life, 0);
    private sealed class Rig
    {
        internal readonly NetTimedGrants Grants = new(11, 7, 3);
        internal readonly NetPickupClaims Claims;
        internal readonly PowerUpSpawnKey Spawn;
        internal int Calls;
        internal Rig()
        {
            Claims = new(11, 7, 3, Grants);
            for (int slot = 0; slot < 8; slot++) Grants.BeginLife(Life(slot), 0);
            Check(Grants.RegisterSpawn(10, TimedPowerUp.DoubleDamage, out Spawn), "fixture registers item");
        }
        internal PickupContactPose Pose(uint source = 100, uint authority = 10, int slot = 0, bool alt = false)
            => new(Life(slot), source, authority, Vector3.Zero, new Vector3(0, .5f, 0), .5f, 0, 2, alt, true, true);
        internal PickupItemFact Item(Vector3? position = null, uint frame = 10, bool available = true)
            => new(Spawn, TimedPowerUp.DoubleDamage, frame, available, position ?? new Vector3(.5f, 1, 0));
        internal PickupClaim Claim(uint id = 1, uint source = 100, int slot = 0)
            => new(new(11, 7, slot, 1, 1, id), Spawn, TimedPowerUp.DoubleDamage, source, Vector3.Zero);
        internal TimedGrantResult Commit(in PickupClaim claim, uint now, out TimedGrantRecord? grant)
        { Calls++; return Grants.TryGrant(claim.Spawn, claim.Key.LifeKey, now, claim.SourceFrame, out grant); }
        internal PickupVerdict Judge(PickupClaim claim, uint now = 11, int? sender = null, ShotKey? life = null,
            bool alive = true, double rtt = 0, double jitter = 0)
            => Claims.Judge(claim, sender ?? claim.Key.Slot, life ?? Life(claim.Key.Slot), alive, now, rtt, jitter, Commit);
        internal void Observe(uint source = 100, uint authority = 10, int slot = 0)
            => Check(Claims.Observe(Pose(source, authority, slot), new[] { Item(frame: authority) }), "valid contact captured");
    }
    private static void ReservationAndRetries()
    {
        var r = new Rig(); r.Observe(); var request = r.Claim(); var first = r.Judge(request, 15);
        Check(first.Result == PickupResult.Grant && first.GrantFrame == 15 && first.ProofFrame == 10
            && first.ExpiresAtFrame == 1815 && r.Calls == 1, "claim starts grant at decision, never historical contact");
        Check(r.Judge(request, 30) == first && r.Calls == 1, "retry returns immutable terminal without callback");
        var divergent = request with { SourceFrame = 999, Position = new Vector3(50, 0, 0), Type = TimedPowerUp.Cloak };
        Check(r.Judge(divergent, 40) == first && r.Calls == 1, "same valid ID divergent payload returns original terminal");
        Check(r.Judge(request, 40, sender: 1).Result == PickupResult.Invalid, "spoof cannot retrieve another owner cache");
        r.Observe(slot: 1); Check(r.Judge(r.Claim(slot: 1), 15).Result == PickupResult.Taken && r.Calls == 1,
            "other player cannot take claimed reservation");
        Check(r.Grants.TryGrant(r.Spawn, Life(2), 16, 100, out _) == TimedGrantResult.Taken, "native path sees claimed reservation");
        r = new Rig(); r.Observe();
        Check(r.Grants.TryGrant(r.Spawn, Life(), 11, 100, out var native) == TimedGrantResult.Granted, "native wins first");
        var acknowledgement = r.Judge(r.Claim(), 12);
        Check(acknowledgement.Result == PickupResult.Grant && acknowledgement.GrantSequence == native!.Sequence
            && acknowledgement.GrantFrame == 11 && r.Calls == 0, "same owner claim acknowledges native grant without renewing");
        r.Observe(slot: 1);
        Check(r.Judge(r.Claim(slot: 1), 12).Result == PickupResult.Taken && r.Calls == 0, "native winner denies contender without callback");
    }
    private static void Geometry()
    {
        foreach (bool alt in new[] { false, true })
        foreach (float distance in new[] { .949f, .95f, .951f })
        {
            var r = new Rig(); var pose = r.Pose(alt: alt);
            var item = r.Item(new Vector3(distance, alt ? .5f : 1, 0));
            Check(r.Claims.Observe(pose, new[] { item }), "contact geometry captured");
            Check(r.Judge(r.Claim()).Result == (distance < .95f ? PickupResult.Grant : PickupResult.TooFar),
                "native strict sphere/lateral reach boundary");
        }
        foreach (float height in new[] { -.001f, 0, 2, 2.001f })
        {
            var r = new Rig(); Check(r.Claims.Observe(r.Pose(), new[] { r.Item(new Vector3(0, height, 0)) }), "height captured");
            Check(r.Judge(r.Claim()).Result == (height >= 0 && height <= 2 ? PickupResult.Grant : PickupResult.TooFar),
                "biped height inclusive endpoints");
        }
        var moving = new Rig(); var facts = new[] { moving.Item() };
        Check(moving.Claims.Observe(moving.Pose(), facts), "moving item snapshot captured");
        facts[0] = facts[0] with { Position = new Vector3(99, 1, 0) };
        Check(moving.Judge(moving.Claim()).Result == PickupResult.Grant, "moving item uses copied historical position");
        var shifted = new Rig(); shifted.Observe();
        Check(shifted.Judge(shifted.Claim() with { Position = new Vector3(-.75f, 0, 0) }).Result == PickupResult.TooFar,
            "tolerance alone cannot authorize reported pose outside item reach");
    }
    private static void InvalidAndUnavailable()
    {
        var r = new Rig(); Check(r.Judge(r.Claim()).Result == PickupResult.HistoryUnavailable && r.Calls == 0, "missing contact unavailable");
        r = new Rig(); r.Observe();
        foreach (var position in new[] { new Vector3(float.NaN, 0, 0), new Vector3(float.PositiveInfinity, 0, 0), new Vector3(100000,0,0) })
            Check(r.Judge(r.Claim() with { Position = position }).Result == PickupResult.Invalid, "nonfinite/unbounded position refused");
        Check(r.Judge(r.Claim(2), alive: false).Result == PickupResult.Invalid, "currently dead owner refused");
        Check(r.Judge(r.Claim(3), life: Life(life: 2)).Result == PickupResult.Invalid, "current life mismatch refused");
        Check(r.Judge(r.Claim(8), life: Life(generation: 2)).Result == PickupResult.Invalid, "same-slot replacement generation refused");
        Check(r.Judge(r.Claim(4) with { Position = new Vector3(2, 0, 0) }).Result == PickupResult.TooFar, "spoofed owner position refused");
        Check(r.Judge(r.Claim(5) with { Spawn = r.Spawn with { Generation = 2 } }).Result == PickupResult.Gone, "stale/wrong incarnation unavailable");
        Check(r.Judge(r.Claim(6) with { Spawn = r.Spawn with { Room = 4 } }).Result == PickupResult.Replayed, "wrong room fenced");
        var wrongEpoch = r.Claim(9) with { Key = r.Claim(9).Key with { Epoch = 12 }, Spawn = r.Spawn with { Epoch = 12 } };
        Check(r.Judge(wrongEpoch).Result == PickupResult.Replayed, "wrong authority epoch fenced");
        var wrong = r.Claim(7) with { Key = r.Claim(7).Key with { Match = 8 }, Spawn = r.Spawn with { Match = 8 } };
        Check(r.Judge(wrong).Result == PickupResult.Replayed && r.Calls == 0, "wrong match fenced before callback");
        foreach (var pose in new[] { r.Pose() with { Alive = false }, r.Pose() with { CanPickup = false } })
        {
            var other = new Rig(); Check(other.Claims.Observe(pose, new[] { other.Item() }), "historical eligibility recorded");
            Check(other.Judge(other.Claim()).Result == PickupResult.Invalid, "historically dead/ineligible owner refused");
        }
        var unavailable = new Rig(); Check(unavailable.Claims.Observe(unavailable.Pose(), new[] { unavailable.Item(available:false) }), "taken item observed");
        Check(unavailable.Judge(unavailable.Claim()).Result == PickupResult.Taken && unavailable.Calls == 0, "historically taken item refused");
    }
    private static void ClocksAndAge()
    {
        var r = new Rig(); r.Observe();
        Check(!r.Claims.Observe(r.Pose() with { Position = new Vector3(90, 0, 0) }, new[] { r.Item() }), "repeated source cannot rewrite contact");
        Check(r.Judge(r.Claim()).Result == PickupResult.Grant, "first observation remains usable");
        r = new Rig(); r.Observe();
        Check(!r.Claims.Observe(r.Pose(134,11),new[] { r.Item(frame:11) }), "accelerated source clock refused");
        Check(!r.Claims.Observe(r.Pose(101,140),new[] { r.Item(frame:140) }), "stalled source clock refused");
        Check(r.Judge(r.Claim(source:134),11).Result == PickupResult.HistoryUnavailable, "rejected clock leaves no contact proof");
        Check(NetPickupClaims.HistoryWindow(250,40) == 26 && NetPickupClaims.HistoryWindow(100000,100000) == 120
            && NetPickupClaims.HistoryWindow(double.NaN,0) == 12, "authority RTT window bounded and nonfinite safe");
        r = new Rig(); r.Observe();
        Check(r.Judge(r.Claim(),36,rtt:250,jitter:40).Result == PickupResult.Grant, "exact measured age boundary accepted");
        r = new Rig(); r.Observe();
        Check(r.Judge(r.Claim(),37,rtt:250,jitter:40).Result == PickupResult.TooOld, "one beyond measured age refused");
        r = new Rig(); uint frame = uint.MaxValue - 2;
        Check(r.Claims.Observe(r.Pose(uint.MaxValue,frame),new[] { r.Item(frame:frame) }), "wrap anchor captured");
        Check(r.Claims.Observe(r.Pose(0,unchecked(frame+1)),new[] { r.Item(frame:unchecked(frame+1)) }), "source wrap progresses");
        Check(r.Judge(r.Claim(source:0),2).Result == PickupResult.Grant, "authority/source rollover valid bounded age");
        // A rejected item snapshot must not establish a clock anchor.
        r = new Rig(); Check(!r.Claims.Observe(r.Pose(1000,10),new[] { r.Item() with { Position = new Vector3(float.NaN,0,0) } }), "invalid item rejected");
        Check(r.Claims.Observe(r.Pose(2000,11),new[] { r.Item(frame:11) }), "first valid observation owns clock anchor");
    }
    private static void ContactPhaseOffsets()
    {
        foreach (uint offset in new uint[] { 1, 2 })
        foreach (bool nativeFirst in new[] { false, true })
        {
            var r = new Rig();
            var bad = r.Pose() with { Position = new Vector3(3, 0, 0), SpherePosition = new Vector3(3, .5f, 0) };
            Check(r.Claims.Observe(bad, new[] { r.Item() }), "pre-contact exact source captured outside reach");
            var claim = r.Claim();
            Check(!r.Claims.CanProveContact(claim, 10, 0, 0), "bad exact pose alone cannot prove later contact");
            r.Observe(100 + offset, 10 + offset);
            Check(r.Claims.HasFollowingObservation(Life(), 100) && r.Claims.CanProveContact(claim, 12, 0, 0),
                "captured following phase proves contact within two source steps");
            TimedGrantRecord? native = null;
            if (nativeFirst)
                Check(r.Grants.TryGrant(r.Spawn, Life(), 12, 100 + offset, out native) == TimedGrantResult.Granted,
                    "following native contact wins before original claim");
            var verdict = r.Judge(claim, 13);
            Check(verdict.Result == PickupResult.Grant && verdict.ProofFrame == 10 + offset
                && verdict.GrantFrame == (nativeFirst ? 12u : 13u) && r.Calls == (nativeFirst ? 0 : 1)
                && (!nativeFirst || verdict.GrantSequence == native!.Sequence),
                "phase offset grant/native acknowledgement retains actual proof and grant clocks");
            Check(r.Judge(claim, 14) == verdict && r.Calls == (nativeFirst ? 0 : 1), "phase-offset retry cannot renew grant");
        }
        for (int scenario = 0; scenario < 6; scenario++)
        {
            var r = new Rig(); var claim = r.Claim();
            uint source = scenario == 0 ? 103u : 101u;
            var pose = r.Pose(source, scenario == 2 ? 20u : 11u);
            if (scenario == 1) pose = pose with { Life = Life(life: 2) };
            if (scenario == 4) pose = pose with { Position = new Vector3(2, 0, 0), SpherePosition = new Vector3(2, .5f, 0) };
            var item = r.Item(frame: pose.AuthorityFrame, available: scenario != 3);
            if (scenario == 5) item = item with { Position = new Vector3(4, 1, 0) };
            Check(r.Claims.Observe(pose, new[] { item }), "negative following sample captured");
            Check(!r.Claims.CanProveContact(claim, 12, 0, 0) && r.Judge(claim, 12).Result != PickupResult.Grant
                && r.Calls == 0, "offset-three/wrong-life/future/taken/position/reach cannot prove contact " + scenario);
        }
        var exact = new Rig();
        Check(exact.Claims.Observe(exact.Pose(), new[] { exact.Item(new Vector3(4, 1, 0)) }), "bad exact reach retained");
        Check(exact.Judge(exact.Claim()).Result == PickupResult.TooFar && exact.Calls == 0,
            "without a following proof the exact native refusal remains unchanged");
    }

    private static void NativeReservationProofs()
    {
        void Anchor(Rig rig, uint source = 100, uint authority = 10)
        {
            var pose = rig.Pose(source, authority) with { Position = new Vector3(3, 0, 0), SpherePosition = new Vector3(3, .5f, 0) };
            Check(rig.Claims.Observe(pose, new[] { rig.Item(frame: authority) }), "native proof has independent out-of-reach source anchor");
        }
        foreach (uint offset in new uint[] { 2, 3 })
        {
            var r = new Rig(); var claim = r.Claim();
            var early = r.Pose(100 + offset, 10) with { Position = new Vector3(3, 0, 0), SpherePosition = new Vector3(3, .5f, 0) };
            Check(r.Claims.Observe(early, new[] { r.Item() }), "first repeated source pose captures pre-contact geometry");
            Check(r.Grants.TryGrant(r.Spawn, Life(), 11, 100 + offset, out var native) == TimedGrantResult.Granted,
                "actual native reservation exists before native proof");
            var actual = r.Pose(100 + offset, 11);
            Check(!r.Claims.Observe(actual, new[] { r.Item(frame: 11) }), "native contact does not overwrite first source observation");
            Check(r.Claims.ObserveNativeReservation(native!, actual, r.Item(frame: 11)), "actual native reservation proof retained separately");
            Check(!r.Claims.ObserveNativeReservation(native!, actual, r.Item(frame: 11)), "native reservation snapshot is immutable on retry");
            Check(r.Claims.NativeReservationCount == 1 && r.Claims.CanProveContact(claim, 12, 0, 0),
                "repeated-source native proof supports bounded original contact");
            var verdict = r.Judge(claim, 12);
            Check(verdict.Result == PickupResult.Grant && verdict.GrantSequence == native!.Sequence
                && verdict.GrantFrame == 11 && verdict.ProofFrame == 11 && r.Calls == 0,
                "native fallback acknowledges exact reservation without new callback or award");
            Check(r.Judge(claim, 13) == verdict && r.Calls == 0, "native fallback terminal stays immutable");
        }
        for (int scenario = 0; scenario < 9; scenario++)
        {
            var r = new Rig(); Anchor(r); uint at = scenario == 7 ? 20u : 11u;
            Check(r.Grants.TryGrant(r.Spawn, Life(), at, 103, out var native) == TimedGrantResult.Granted, "negative native reservation setup");
            var pose = r.Pose(103, at); var item = r.Item(frame: at);
            if (scenario == 0) native = native! with { Sequence = 999 };
            if (scenario == 1) pose = pose with { Life = Life(life: 2) };
            if (scenario == 2) item = item with { Spawn = item.Spawn with { Generation = 999 } };
            if (scenario == 3) item = item with { Type = TimedPowerUp.Cloak };
            if (scenario == 4) pose = pose with { Alive = false };
            if (scenario == 5) item = item with { Available = false };
            if (scenario == 6) item = item with { Position = new Vector3(4, 1, 0) };
            bool observed = r.Claims.ObserveNativeReservation(native!, pose, item);
            if (scenario < 7) Check(!observed, "unowned/mismatched/dead/unavailable/out-of-reach native proof rejected " + scenario);
            else Check(observed, "valid native proof captured for clock rejection");
            uint now = scenario == 8 ? 24u : 12u;
            Check(!r.Claims.CanProveContact(r.Claim(), now, 0, 0) && r.Judge(r.Claim(), now).Result != PickupResult.Grant
                && r.Calls == 0, "invalid/future/expired native proof cannot authorize acknowledgement " + scenario);
        }
        var unanchored = new Rig();
        Check(unanchored.Grants.TryGrant(unanchored.Spawn, Life(), 11, 103, out var unanchoredGrant) == TimedGrantResult.Granted
            && !unanchored.Claims.ObserveNativeReservation(unanchoredGrant!, unanchored.Pose(103, 11), unanchored.Item(frame: 11)),
            "native award alone cannot invent its independent source-clock anchor");
        var clone = new Rig(); Anchor(clone);
        Check(clone.Grants.TryGrant(clone.Spawn, Life(), 11, 103, out var owned) == TimedGrantResult.Granted
            && !clone.Claims.ObserveNativeReservation(owned! with { }, clone.Pose(103, 11), clone.Item(frame: 11)),
            "equal-value cloned record is not an owned native reservation");
        var removed = new Rig(); Anchor(removed);
        Check(removed.Grants.TryGrant(removed.Spawn, Life(), 11, 103, out var old) == TimedGrantResult.Granted
            && removed.Claims.ObserveNativeReservation(old!, removed.Pose(103, 11), removed.Item(frame: 11)), "removed grant proof setup");
        removed.Grants.RetireSlot(0, 12, TimedGrantEnd.Death); removed.Grants.Advance(1037);
        Check(!removed.Claims.CanProveContact(removed.Claim(), 12, 0, 0)
            && removed.Judge(removed.Claim(), 12).Result != PickupResult.Grant && removed.Calls == 0,
            "removed retained reservation cannot acknowledge or fall through to a new grant callback");
        var tooEarly = new Rig(); Anchor(tooEarly);
        Check(tooEarly.Grants.TryGrant(tooEarly.Spawn, Life(), 11, 113, out var later) == TimedGrantResult.Granted
            && tooEarly.Claims.ObserveNativeReservation(later!, tooEarly.Pose(113, 11), tooEarly.Item(frame: 11)), "source-boundary fixture");
        Check(!tooEarly.Claims.CanProveContact(tooEarly.Claim(), 12, 0, 0)
            && !tooEarly.Claims.CanProveContact(tooEarly.Claim(source: 114), 12, 0, 0), "native source delta excludes over-window and future claim");
        var wrap = new Rig(); uint frame = uint.MaxValue - 1; Anchor(wrap, uint.MaxValue - 1, uint.MaxValue - 2);
        Check(wrap.Grants.TryGrant(wrap.Spawn, Life(), frame, 1, out var wrapped) == TimedGrantResult.Granted
            && wrap.Claims.ObserveNativeReservation(wrapped!, wrap.Pose(1, frame), wrap.Item(frame: frame)), "native wrap proof captured");
        Check(wrap.Judge(wrap.Claim(source: uint.MaxValue - 1), 1).Result == PickupResult.Grant && wrap.Calls == 0,
            "native source and authority rollover preserve bounded acknowledgement");
        var bounded = new Rig(); Anchor(bounded, 100, 0); bounded.Grants.RemoveSpawn(bounded.Spawn);
        for (uint index = 0; index < NetTimedGrants.GrantCapacity; index++)
        {
            Check(bounded.Grants.RegisterSpawn(10, TimedPowerUp.DoubleDamage, out var spawn), "capacity native spawn registers");
            Check(bounded.Grants.TryGrant(spawn, Life(), index, 100 + index, out var grant) == TimedGrantResult.Granted,
                "capacity native reservation grants");
            Check(bounded.Claims.ObserveNativeReservation(grant!, bounded.Pose(100 + index, index),
                new(spawn, TimedPowerUp.DoubleDamage, index, true, new(.5f, 1, 0))), "capacity native proof retained");
            bounded.Grants.RemoveSpawn(spawn);
        }
        Check(bounded.Claims.NativeReservationCount == NetTimedGrants.GrantCapacity, "native proof table bounded by actual retained grant capacity");
        bounded.Grants.Advance(2000);
        Check(bounded.Grants.RegisterSpawn(10, TimedPowerUp.DoubleDamage, out var fresh)
            && bounded.Grants.TryGrant(fresh, Life(), 2000, 2100, out var freshGrant) == TimedGrantResult.Granted
            && bounded.Claims.ObserveNativeReservation(freshGrant!, bounded.Pose(2100, 2000),
                new(fresh, TimedPowerUp.DoubleDamage, 2000, true, new(.5f, 1, 0))), "post-retention native proof admitted");
        Check(bounded.Claims.NativeReservationCount <= 2, "native proof pruning follows removed authority grant history");
    }

    private static void Capacity()
    {
        var r = new Rig();
        for (uint id=1; id<=NetPickupClaims.ClaimsPerSlot; id++)
            Check(r.Judge(r.Claim(id)).Result == PickupResult.HistoryUnavailable, "terminal quota admits bounded failed decisions");
        var capacity=r.Judge(r.Claim(257));
        Check(capacity.Result == PickupResult.Capacity && capacity.DecisionFrame == 0 && r.Calls == 0,
            "permanent saturated slot refuses without callback");
        Check(r.Judge(r.Claim(257),999).Result == PickupResult.Capacity && r.Claims.TerminalCount == 256,
            "capacity retry never grows terminal cache");
        Check(r.Judge(r.Claim(),999).Result == PickupResult.HistoryUnavailable, "old ID retains terminal after quota exhaustion");
        r.Observe(slot:1); Check(r.Judge(r.Claim(slot:1)).Result == PickupResult.Grant && r.Calls == 1, "other slot unaffected by saturation");
        Check(r.Grants.RegisterSpawn(11,TimedPowerUp.Cloak,out var extra)
            && r.Grants.TryGrant(extra,Life(),12,101,out _) == TimedGrantResult.Granted, "native pickup remains available for saturated slot");
        var bounded = new Rig();
        Check(!bounded.Claims.Observe(bounded.Pose(), new[] { bounded.Item(), bounded.Item() }),
            "duplicate incarnation in one observation refused");
        var oversized = new PickupItemFact[NetTimedGrants.SpawnCapacity + 1];
        Check(!bounded.Claims.Observe(bounded.Pose(), oversized) && bounded.Claims.ObservationCount == 0,
            "oversized item snapshot rejected without history growth");
        r = new Rig();
        for (int slot=0;slot<8;slot++)
        for (uint index=0;index<131;index++) r.Observe(100+index,10+index,slot);
        Check(r.Claims.ObservationCount == 8*128, "contact history bounded separately for eight slots");
        Check(r.Judge(r.Claim(),141).Result == PickupResult.HistoryUnavailable, "evicted entire contact-phase window is unavailable");
    }
    public static int Run()
    {
        try { _checks=0; ReservationAndRetries(); Geometry(); InvalidAndUnavailable(); ClocksAndAge(); ContactPhaseOffsets(); NativeReservationProofs(); Capacity();
            Console.WriteLine($"PASS: {_checks} pure pickup claim assertions"); return 0; }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
