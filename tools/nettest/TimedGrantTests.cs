using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        bool observed = ledger.TryReadObservation(life, source, type, out var cached, out _);
        Check(cached == answer && (observed || expected == TimedGrantKnowledge.Unavailable),
            name + ": raw lookup agrees with unchanged legacy result after legacy advancement");
    }

    private static void ReadAnswer(NetTimedGrants ledger, ShotKey life, uint source,
        TimedPowerUp type, TimedGrantKnowledge knowledge, uint authority, ulong sequence = 0)
    {
        Check(ledger.TryReadObservation(life, source, type, out var answer, out uint frame)
            && answer == new TimedGrantAnswer(knowledge, sequence) && frame == authority,
            $"raw retained answer {type}/{knowledge}/{sequence} preserves authority frame {authority}");
    }

    private static void Missing(NetTimedGrants ledger, ShotKey life, uint source,
        TimedPowerUp type = TimedPowerUp.DoubleDamage)
    {
        Check(!ledger.TryReadObservation(life, source, type, out var answer, out uint frame)
            && answer == new TimedGrantAnswer(TimedGrantKnowledge.Unavailable) && frame == 0,
            "missing/invalid raw lookup returns false with unavailable answer and no authority frame");
    }

    private static void ReadOnlyObservations()
    {
        {
            var life = Life(); var ledger = Ledger(life);
            ledger.ObserveInput(life, 0, 0);
            ReadAnswer(ledger, life, 0, TimedPowerUp.DoubleDamage, TimedGrantKnowledge.NotGranted, 0);
            Missing(ledger, life, 1); // frame zero is valid observed data, not the absence flag
        }
        foreach (var type in Enum.GetValues<TimedPowerUp>())
        {
            var life = Life(); var ledger = Ledger(life);
            Missing(ledger, life, 0, type); // must not seed the first source clock
            var first = Grant(ledger, life, 100, 5000, type);
            ledger.ObserveInput(life, 5000, 100);
            ReadAnswer(ledger, life with { ShotId = 55 }, 5000, type, TimedGrantKnowledge.NotGranted, 100);
            ledger.ObserveInput(life, 5001, 101);
            ReadAnswer(ledger, life, 5001, type, TimedGrantKnowledge.Granted, 101, first.Sequence);
            var renewal = Grant(ledger, life, 102, 5002, type);
            ledger.ObserveInput(life, 5001, 103);
            ReadAnswer(ledger, life, 5001, type, TimedGrantKnowledge.Granted, 101, first.Sequence);
            ledger.ObserveInput(life, 5003, 103);
            ReadAnswer(ledger, life, 5003, type, TimedGrantKnowledge.Granted, 103, renewal.Sequence);
            ledger.RetireSlot(0, 103, TimedGrantEnd.Death);
            ReadAnswer(ledger, life with { ShotId = 999 }, 5003, type,
                TimedGrantKnowledge.Granted, 103, renewal.Sequence);
            var next = Life(life: 2); ledger.BeginLife(next, 104); ledger.ObserveInput(next, 5003, 104);
            ReadAnswer(ledger, next, 5003, type, TimedGrantKnowledge.NotGranted, 104);
            var replacement = Life(generation: 2, life: 2);
            ledger.BeginLife(replacement, 105); ledger.ObserveInput(replacement, 5003, 105);
            ReadAnswer(ledger, replacement, 5003, type, TimedGrantKnowledge.NotGranted, 105);
            ReadAnswer(ledger, next, 5003, type, TimedGrantKnowledge.NotGranted, 104);
            ReadAnswer(ledger, life, 5003, type, TimedGrantKnowledge.Granted, 103, renewal.Sequence);
            foreach (var wrong in new[] { life with { AuthorityEpoch = 12 }, life with { MatchId = 8 },
                life with { ShooterSlot = -1 }, life with { ShooterSlot = 8 },
                life with { Generation = 0 }, life with { LifeId = 0 }, life with { LifeId = 3 } })
                Missing(ledger, wrong, 5003, type);
            Missing(ledger, life, 5004, type);
            Missing(ledger, life, 5003, (TimedPowerUp)3);
            Missing(ledger, life, 5003, (TimedPowerUp)255);
            var otherRoom = new NetTimedGrants(11, 7, 4);
            Check(ledger.Room == 3 && otherRoom.Room == 4, "raw lookup exposes immutable owning room scope");
            Missing(otherRoom, life, 5003, type);
        }
        {
            var life = Life(); var ledger = Ledger(life); Grant(ledger, life, 100, null);
            ledger.ObserveInput(life, 9000, 101);
            ReadAnswer(ledger, life, 9000, TimedPowerUp.DoubleDamage, TimedGrantKnowledge.Unavailable, 101);
            Missing(ledger, life, 9001);
            ledger.ObserveInput(life, 9034, 102);
            ReadAnswer(ledger, life, 9034, TimedPowerUp.Cloak, TimedGrantKnowledge.Unavailable, 102);
        }
        {
            var life = Life(); var ledger = Ledger(life);
            uint contact = uint.MaxValue - 900;
            var grant = Grant(ledger, life, contact, uint.MaxValue);
            ledger.ObserveInput(life, 0, unchecked(contact + 1));
            ReadAnswer(ledger, life, 0, TimedPowerUp.DoubleDamage,
                TimedGrantKnowledge.Granted, unchecked(contact + 1), grant.Sequence);
            ledger.Advance(unchecked(contact + 1 + NetTimedGrants.Retention));
            ReadAnswer(ledger, life, 0, TimedPowerUp.DoubleDamage,
                TimedGrantKnowledge.Granted, unchecked(contact + 1), grant.Sequence);
            ledger.Advance(unchecked(contact + 2 + NetTimedGrants.Retention));
            Missing(ledger, life, 0);
        }
    }

    private static void ReadOnlyPublicationSilence()
    {
        var frameProperty = typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!;
        uint savedFrame = NetSession.NetFrame;
        var revisionField = typeof(NetGrantNetwork).GetField("_revision", BindingFlags.Static | BindingFlags.NonPublic)!;
        var advancedField = typeof(NetTimedGrants).GetField("_advancedFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var clocksField = typeof(NetTimedGrants).GetField("_clocks", BindingFlags.Instance | BindingFlags.NonPublic)!;
        NetGrantNetwork.Reset();
        try
        {
            frameProperty.SetValue(null, 100u);
            var peer = NetGrantNetwork.Join();
            int callbacks = 0;
            var ledger = new NetTimedGrants(11, 7, 3, grant =>
            { callbacks++; NetGrantNetwork.Changed(grant); });
            var life = Life(); ledger.BeginLife(life, 100);
            var grant = Grant(ledger, life, 100, 5000);
            ledger.ObserveInput(life, 5001, 101);
            uint revision = (uint)revisionField.GetValue(null)!;
            var clocks = ((Array)clocksField.GetValue(ledger)!).Cast<object>().ToArray();
            var counts = (ledger.SpawnCount, ledger.HistoryCount, ledger.ObservationCount, ledger.CapacityRefused);
            byte[] before = Array.Empty<byte>();
            Check(NetGrantNetwork.Pump(peer, (_, bytes) => { before = bytes.ToArray(); return false; })
                && before.Length == TimedGrantFact.Size && callbacks == 1,
                "real production callback publishes one retained grant packet before raw-query witness");
            // An elapsed external clock does not impose a TTL on raw cached
            // truth or advance the authority ledger as a side effect of reading.
            frameProperty.SetValue(null, 3000u);
            for (int i = 0; i < 32; i++)
            {
                ReadAnswer(ledger, life with { ShotId = (uint)(i + 1) }, 5001,
                    TimedPowerUp.DoubleDamage, TimedGrantKnowledge.Granted, 101, grant.Sequence);
                ReadAnswer(ledger, life, 5001, TimedPowerUp.Cloak, TimedGrantKnowledge.NotGranted, 101);
                Missing(ledger, life, 5002);
                Missing(ledger, life, 5001, (TimedPowerUp)255);
            }
            byte[] after = Array.Empty<byte>();
            Check(NetGrantNetwork.Pump(peer, (_, bytes) => { after = bytes.ToArray(); return false; })
                && before.SequenceEqual(after) && NetGrantNetwork.Pending(peer) == 1
                && (uint)revisionField.GetValue(null)! == revision && callbacks == 1,
                "raw lookup preserves actual production revision, retained packet bytes and callback count");
            Check(counts == (ledger.SpawnCount, ledger.HistoryCount, ledger.ObservationCount, ledger.CapacityRefused)
                && (uint)advancedField.GetValue(ledger)! == 101
                && clocks.SequenceEqual(((Array)clocksField.GetValue(ledger)!).Cast<object>())
                && grant.EndReason == TimedGrantEnd.Active && grant.EndFrame == grant.ExpiresAtFrame,
                "raw lookup never advances expiry, evicts records, seeds clocks or mutates ledger counts");
            var legacy = ledger.GrantedAt(life, 5001, (TimedPowerUp)255, grant.ExpiresAtFrame);
            Check(legacy.Knowledge == TimedGrantKnowledge.Unavailable && callbacks == 2
                && (uint)revisionField.GetValue(null)! == revision + 1
                && NetGrantNetwork.Pending(peer) == 2 && grant.EndReason == TimedGrantEnd.Expired,
                "unchanged legacy invalid-type query still advances expiry and publishes its original transition");
            Missing(ledger, life, 5001);
        }
        finally { NetGrantNetwork.Reset(); frameProperty.SetValue(null, savedFrame); }
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
            ReadOnlyObservations(); ReadOnlyPublicationSilence();
            Console.WriteLine($"PASS: {_checks} pure timed grant assertions");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
