using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class TimedGrantTests
{
    private static int _checks;
    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        _checks++;
    }
    private static ShotKey Life(int slot = 0, ushort generation = 1, ushort life = 1)
        => new(11, 7, slot, generation, life, 0);
    private static NetTimedGrants Ledger(params ShotKey[] lives)
    {
        var ledger = new NetTimedGrants(11, 7, 3);
        foreach (var life in lives) ledger.BeginLife(life, 0);
        return ledger;
    }
    private static PowerUpSpawnKey Spawn(NetTimedGrants ledger, TimedPowerUp type = TimedPowerUp.DoubleDamage, int id = 10)
    {
        Check(ledger.RegisterSpawn(id, type, out var key), "spawn registration succeeds");
        return key;
    }
    private static TimedGrantRecord Grant(NetTimedGrants ledger, ShotKey life, uint at, uint? source,
        TimedPowerUp type = TimedPowerUp.DoubleDamage)
    {
        var spawn = Spawn(ledger, type);
        Check(ledger.TryGrant(spawn, life, at, source, out var record) == TimedGrantResult.Granted && record != null,
            "authority grants available incarnation to current life");
        return record!;
    }
    private static void Answer(NetTimedGrants ledger, ShotKey life, uint source, uint now,
        TimedGrantKnowledge expected, string name, TimedPowerUp type = TimedPowerUp.DoubleDamage, ulong sequence = 0)
    {
        var answer = ledger.GrantedAt(life, source, type, now);
        Check(answer.Knowledge == expected && (sequence == 0 || answer.Sequence == sequence),
            $"{name}: expected {expected}/{sequence}, got {answer}");
    }

    private static void BoundariesAndClocks()
    {
        foreach (var type in Enum.GetValues<TimedPowerUp>())
        {
            var life = Life(); var ledger = Ledger(life);
            var grant = Grant(ledger, life, 100, 5000, type);
            Check(grant.ExpiresAtFrame == 1900 && grant.EffectiveFrame == 101 && grant.SourceEffectiveFrame == 5001,
                "1800 authority lifetime and distinct contact+1 source boundary");
            ledger.ObserveInput(life, 5000, 100);
            Answer(ledger, life, 5000, 100, TimedGrantKnowledge.NotGranted, "contact input cannot fund itself", type);
            ledger.ObserveInput(life, 5001, 101);
            Answer(ledger, life, 5001, 101, TimedGrantKnowledge.Granted, "first subsequent input", type, grant.Sequence);
            ledger.ObserveInput(life, 6799, 1899);
            Answer(ledger, life, 6799, 1899, TimedGrantKnowledge.Granted, "last authority input before expiry", type);
            ledger.ObserveInput(life, 6800, 1900);
            Answer(ledger, life, 6800, 1900, TimedGrantKnowledge.NotGranted, "exclusive expiry", type);
            Check(grant.EndReason == TimedGrantEnd.Expired && grant.EndFrame == 1900, "exact recorded expiration");
            ledger.ObserveInput(life, 6799, 1901);
            Answer(ledger, life, 6799, 1901, TimedGrantKnowledge.Granted, "repeat after expiry cannot overwrite original observation", type);
            Answer(ledger, life, 6798, 1901, TimedGrantKnowledge.Unavailable, "unobserved frame cannot authorize", type);
        }
        {
            var life = Life(); var ledger = Ledger(life); Grant(ledger, life, 100, null);
            ledger.ObserveInput(life, 600, 101);
            Answer(ledger, life, 600, 101, TimedGrantKnowledge.Unavailable, "missing pickup source boundary");
        }
        {
            var life = Life(); var ledger = Ledger(life);
            ledger.ObserveInput(life, 4000, 100); Grant(ledger, life, 100, 4000);
            ledger.ObserveInput(life, 4034, 101);
            Answer(ledger, life, 4034, 101, TimedGrantKnowledge.Unavailable, "accelerated source clock");
            ledger.ObserveInput(life, 4001, 1126);
            Answer(ledger, life, 4001, 1126, TimedGrantKnowledge.Unavailable, "source clock stalled beyond bounded lag");
            ledger.ObserveInput(life, 4002, 1900);
            Answer(ledger, life, 4002, 1900, TimedGrantKnowledge.Unavailable, "expired stalled source never gains authorization");
        }
    }

    private static void RenewalContentionAndTypes()
    {
        var a = Life(); var b = Life(1); var ledger = Ledger(a, b);
        var spawn = Spawn(ledger);
        Check(ledger.TryGrant(spawn, a, 10, 10, out var first) == TimedGrantResult.Granted, "first contender wins");
        Check(ledger.TryGrant(spawn, b, 10, 10, out var loser) == TimedGrantResult.Taken && loser == null,
            "second player receives no grant");
        Check(ledger.TryGrant(spawn, a, 10, 10, out _) == TimedGrantResult.Taken && ledger.HistoryCount == 1,
            "duplicate pickup cannot renew");
        ledger.ObserveInput(a, 11, 11);
        var second = Grant(ledger, a, 20, 20);
        Check(second.Sequence != first!.Sequence && first.EndReason == TimedGrantEnd.Renewed && first.EndFrame == 21,
            "same-life renewal records distinct grant and ends old one at new effective frame");
        ledger.ObserveInput(a, 21, 21);
        Answer(ledger, a, 11, 21, TimedGrantKnowledge.Granted, "pre-renewal history retained", sequence: first.Sequence);
        Answer(ledger, a, 21, 21, TimedGrantKnowledge.Granted, "renewed sequence effective", sequence: second.Sequence);
        Grant(ledger, a, 21, 21, TimedPowerUp.Cloak);
        Grant(ledger, a, 21, 21, TimedPowerUp.DeathAlt);
        ledger.ObserveInput(a, 22, 22);
        foreach (var type in Enum.GetValues<TimedPowerUp>())
            Answer(ledger, a, 22, 22, TimedGrantKnowledge.Granted, "distinct timed types coexist", type);
        Check(second.EndReason == TimedGrantEnd.Active, "other powerups do not renew Double");
        ledger.RemoveSpawn(spawn);
        var reincarnation = Spawn(ledger);
        Check(reincarnation.SpawnId == spawn.SpawnId && reincarnation.Generation > spawn.Generation,
            "spawner reuse advances incarnation independently of native spawn count");
        Check(ledger.TryGrant(spawn, b, 22, 22, out _) == TimedGrantResult.Gone, "removed incarnation remains gone");
    }

    private static void RetirementAndFences()
    {
        var life = Life(); var ledger = Ledger(life); var grant = Grant(ledger, life, 10, 1000);
        ledger.ObserveInput(life, 1001, 11);
        ledger.RetireSlot(0, 12, TimedGrantEnd.Death);
        Check(grant.EndReason == TimedGrantEnd.Death && grant.EndFrame == 12, "death closes active lifetime");
        Answer(ledger, life with { ShotId = 55 }, 1001, 13, TimedGrantKnowledge.Granted,
            "delayed admitted predeath shot retains original observation", sequence: grant.Sequence);
        ledger.ObserveInput(life, 1002, 13);
        Answer(ledger, life, 1002, 13, TimedGrantKnowledge.Unavailable, "dead life cannot create new source authorization");
        var available = Spawn(ledger);
        Check(ledger.TryGrant(available, life, 13, 1002, out _) == TimedGrantResult.WrongLife, "dead life cannot take another item");
        var next = Life(life: 2); ledger.BeginLife(next, 14); ledger.ObserveInput(next, 1001, 14);
        Answer(ledger, next, 1001, 14, TimedGrantKnowledge.NotGranted, "respawn cannot inherit old grant");
        Answer(ledger, life, 1001, 14, TimedGrantKnowledge.Granted, "respawn preserves old-life delayed evidence");
        var replacement = Life(generation: 2); ledger.BeginLife(replacement, 15); ledger.ObserveInput(replacement, 1001, 15);
        Answer(ledger, replacement, 1001, 15, TimedGrantKnowledge.NotGranted, "same-slot replacement cannot inherit");
        foreach (var wrong in new[] { replacement with { AuthorityEpoch = 12 }, replacement with { MatchId = 8 }, replacement with { LifeId = 0 } })
            Check(ledger.TryGrant(available, wrong, 15, 1001, out _) == TimedGrantResult.WrongLife, "life stream fence rejects mismatch");
        foreach (var wrong in new[] { available with { Epoch = 12 }, available with { Match = 8 }, available with { Room = 4 } })
            Check(ledger.TryGrant(wrong, replacement, 15, 1001, out _) == TimedGrantResult.Gone, "spawn stream/room fence rejects mismatch");
        var otherRoom = new NetTimedGrants(11, 7, 4); otherRoom.BeginLife(replacement, 15);
        Answer(otherRoom, replacement, 1001, 15, TimedGrantKnowledge.Unavailable, "room reset has no prior-room observations");
    }

    private static void RolloverAndRetention()
    {
        var life = Life(); var ledger = Ledger(life);
        uint contact = uint.MaxValue - 900, source = uint.MaxValue;
        var grant = Grant(ledger, life, contact, source);
        ledger.ObserveInput(life, source, contact);
        ledger.ObserveInput(life, 0, unchecked(contact + 1));
        Answer(ledger, life, 0, unchecked(contact + 1), TimedGrantKnowledge.Granted, "source contact+1 wraps to zero");
        uint last = unchecked(contact + 1799); ledger.ObserveInput(life, 1798, last);
        Answer(ledger, life, 1798, last, TimedGrantKnowledge.Granted, "authority lifetime crosses uint wrap");
        uint expiry = unchecked(contact + 1800); ledger.ObserveInput(life, 1799, expiry);
        Answer(ledger, life, 1799, expiry, TimedGrantKnowledge.NotGranted, "wrapped expiry remains exclusive");
        Check(grant.EndFrame == expiry && grant.EndReason == TimedGrantEnd.Expired, "wrapped expiry recorded exactly");
        ledger.Advance(unchecked(expiry + NetTimedGrants.Retention));
        Check(ledger.HistoryCount == 1, "history retained at exact retention boundary");
        ledger.Advance(unchecked(expiry + NetTimedGrants.Retention + 1));
        Check(ledger.HistoryCount == 0 && ledger.ObservationCount == 0, "bounded ended history and observations retire after horizon");
        Answer(ledger, life, 1798, unchecked(expiry + NetTimedGrants.Retention + 1), TimedGrantKnowledge.Unavailable,
            "evicted evidence is unavailable rather than known absence");
    }

    private static void EightSlots()
    {
        var ledger = new NetTimedGrants(11, 7, 3);
        for (int slot = 0; slot < 8; slot++)
        {
            var life = Life(slot); ledger.BeginLife(life, 0);
            var grant = Grant(ledger, life, 10, (uint)(1000 + slot * 100));
            uint source = (uint)(1001 + slot * 100);
            ledger.ObserveInput(life, source, 11);
            Answer(ledger, life, source, 11, TimedGrantKnowledge.Granted, "independent eight-slot grant", sequence: grant.Sequence);
        }
        Check(ledger.HistoryCount == 8 && ledger.ObservationCount == 8, "all eight slot histories coexist");
        ledger.RetireSlot(3, 12, TimedGrantEnd.Death);
        for (int slot = 0; slot < 8; slot++)
        {
            var life = Life(slot); uint source = (uint)(1002 + slot * 100);
            ledger.ObserveInput(life, source, 12);
            Answer(ledger, life, source, 12, slot == 3 ? TimedGrantKnowledge.Unavailable : TimedGrantKnowledge.Granted,
                "one slot retirement does not affect other grants");
        }
    }

    private static void AuditTransitions()
    {
        foreach (var reason in new[] { TimedGrantEnd.Expired, TimedGrantEnd.Death, TimedGrantEnd.Renewed,
            TimedGrantEnd.Room, TimedGrantEnd.Session })
        {
            var audit = new List<TimedGrantRecord>();
            // The native evidence sink copies mutable ledger records at emission.
            // Audit history must keep creation facts intact after later closure.
            var ledger = new NetTimedGrants(11, 7, 3, record => audit.Add(record with { }));
            var life = Life(); ledger.BeginLife(life, 0);
            var grant = Grant(ledger, life, 10, 1000);
            Check(audit.Count == 1 && audit[0].Sequence == grant.Sequence
                && audit[0].EndReason == TimedGrantEnd.Active && audit[0].EndFrame == 1810,
                "audit emits one copied creation with exact lifetime");
            uint end;
            if (reason == TimedGrantEnd.Expired) { end = 1810; ledger.Advance(end); }
            else if (reason == TimedGrantEnd.Renewed) { end = 21; Grant(ledger, life, 20, 1010); }
            else if (reason is TimedGrantEnd.Room or TimedGrantEnd.Session) { end = 20; ledger.CloseAll(end, reason); }
            else { end = 20; ledger.RetireSlot(0, end, reason); }
            var transitions = audit.Where(record => record.Sequence == grant.Sequence).ToArray();
            Check(transitions.Length == 2 && transitions[1].EndReason == reason && transitions[1].EndFrame == end,
                $"{reason}: exactly one creation and one close with exact end frame");
            Check(transitions[0].EndReason == TimedGrantEnd.Active && transitions[0].EndFrame == 1810
                && !ReferenceEquals(transitions[0], transitions[1]), "later close cannot mutate copied creation evidence");
            int count = transitions.Length;
            ledger.Advance(end); ledger.Advance(end);
            ledger.RetireSlot(0, end, reason); ledger.RetireSlot(0, end, reason);
            ledger.CloseAll(end, reason);
            Check(audit.Count(record => record.Sequence == grant.Sequence) == count,
                "repeated advance and retirement never duplicate a closed grant transition");
        }
        foreach (var reason in new[] { TimedGrantEnd.Room, TimedGrantEnd.Session })
        {
            var audit = new List<TimedGrantRecord>();
            var ledger = new NetTimedGrants(11, 7, 3, record => audit.Add(record with { }));
            for (int slot = 0; slot < 8; slot++)
            {
                var life = Life(slot); ledger.BeginLife(life, 0);
                Grant(ledger, life, 10, (uint)(1000 + slot * 100));
                ledger.ObserveInput(life, (uint)(1001 + slot * 100), 11);
            }
            ledger.CloseAll(12, reason); ledger.CloseAll(12, reason);
            Check(audit.Count == 16 && audit.Count(record => record.EndReason == reason) == 8,
                "scope closure emits one close for each of eight owners");
            for (int slot = 0; slot < 8; slot++)
            {
                var life = Life(slot); uint observed = (uint)(1001 + slot * 100);
                Answer(ledger, life, observed, 13, TimedGrantKnowledge.Granted,
                    "closing scope retains previously observed delayed-shot evidence");
                ledger.ObserveInput(life, observed + 1, 13);
                Answer(ledger, life, observed + 1, 13, TimedGrantKnowledge.Unavailable,
                    "scope closure prevents all owners acquiring new source authorization");
                var spawn = Spawn(ledger);
                Check(ledger.TryGrant(spawn, life, 13, observed + 1, out _) == TimedGrantResult.WrongLife,
                    "scope closure retires ownership in every slot");
                var newScope = new NetTimedGrants(12, 8, 4); newScope.BeginLife(Life(slot) with { AuthorityEpoch = 12, MatchId = 8 }, 13);
                Answer(newScope, life, observed, 13, TimedGrantKnowledge.Unavailable,
                    "new scope never inherits historical authorization");
            }
        }
    }

    private static void Capacity()
    {
        var life = Life(); var ledger = Ledger(life);
        for (int i = 0; i < NetTimedGrants.SpawnCapacity; i++) Spawn(ledger, id: i);
        Check(!ledger.RegisterSpawn(999, TimedPowerUp.DoubleDamage, out _) && ledger.SpawnCount == NetTimedGrants.SpawnCapacity,
            "spawn capacity refuses without growing");
        ledger = Ledger(life);
        for (int i = 0; i < NetTimedGrants.GrantCapacity; i++)
        {
            var spawn = Spawn(ledger, id: i);
            Check(ledger.TryGrant(spawn, life, 1, 1, out _) == TimedGrantResult.Granted, "bounded history fills with renewals");
            ledger.RemoveSpawn(spawn);
        }
        var pending = Spawn(ledger);
        Check(ledger.TryGrant(pending, life, 1, 1, out _) == TimedGrantResult.Capacity
            && ledger.HistoryCount == NetTimedGrants.GrantCapacity, "grant capacity explicitly refuses");
        ledger.Advance(1 + NetTimedGrants.Duration + NetTimedGrants.Retention + 1);
        Check(ledger.TryGrant(pending, life, 3000, 3000, out _) == TimedGrantResult.Granted,
            "capacity failure does not reserve item; retry succeeds after history expiry");
        ledger = Ledger(life);
        // Even invalid-clock inputs have finite storage: a flood cannot grow the
        // dictionary or convert missing evidence into an authorization.
        for (uint i = 0; i < NetTimedGrants.ObservationCapacity; i++) ledger.ObserveInput(life, i, 1);
        long before = ledger.CapacityRefused;
        ledger.ObserveInput(life, NetTimedGrants.ObservationCapacity, 1);
        Check(ledger.ObservationCount == NetTimedGrants.ObservationCapacity && ledger.CapacityRefused == before + 1,
            "observation capacity explicitly refuses without growing");
        Answer(ledger, life, NetTimedGrants.ObservationCapacity, 1, TimedGrantKnowledge.Unavailable, "refused observation unavailable");
        ledger.Advance(1 + NetTimedGrants.Retention + 1);
        Check(ledger.ObservationCount == 0, "observation capacity recovers after retention");
    }

    public static int Run()
    {
        try
        {
            _checks = 0;
            BoundariesAndClocks(); RenewalContentionAndTypes(); RetirementAndFences(); RolloverAndRetention(); EightSlots(); AuditTransitions(); Capacity();
            Console.WriteLine($"PASS: {_checks} pure timed grant assertions");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
