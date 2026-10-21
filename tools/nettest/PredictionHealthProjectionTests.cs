using System;
using MphRead;
using MphRead.Mods.Network;
using static MphRead.Mods.Network.PredictionHealthProjection;

namespace MphRead.NetTest;

internal static class PredictionHealthProjectionTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }
    private static Pending P(int damage = 20, ushort cursor = 0, int health = 99,
        ushort? formCursor = null, int? formHealth = null) => new(damage, cursor, formCursor ?? cursor,
            health, formHealth ?? health, 0, 9, (byte)BeamType.Missile);
    private static DamageEvent E(ushort sequence = 1, ushort damage = 20, byte attacker = 0,
        ushort generation = 9, BeamType beam = BeamType.Missile) => new()
        { EventId = sequence, Damage = damage, AttackerSlot = attacker, AttackerGeneration = generation, Beam = (byte)beam };
    private static Result Test(int health, ushort sample, Pending[] predictions,
        DamageEvent[] events, int expected, string name, Accepted[]? accepted = null, bool form = false)
    {
        var state = new PlayerState();
        if (events.Length > 0) state.Damage0 = events[0];
        if (events.Length > 1) state.Damage1 = events[1];
        if (events.Length > 2) state.Damage2 = events[2];
        if (events.Length > 3) state.Damage3 = events[3];
        var result = Project(health, sample, state, predictions, accepted ?? Array.Empty<Accepted>(), form);
        Check(result.Health == expected, $"{name}: expected {expected}, got {result.Health}");
        Check(result.PossibleOverlap >= 0 && result.PossibleOverlap <= result.PendingDebit, name + " bounded overlap");
        Check(health == 0 ? result.Health == 0 : result.Health >= 1 && result.Health <= health, name + " authority bounds");
        return result;
    }

    public static int Run()
    {
        try
        {
            Test(99, 0, new[] { P() }, Array.Empty<DamageEvent>(), 79, "immediate prediction");
            Test(79, 1, new[] { P() }, new[] { E() }, 79, "snapshot before ACK");
            Test(79, 1, new[] { P() }, new[] { E() }, 79, "repeated sample stable");
            Test(89, 1, new[] { P() }, new[] { E(damage: 10) }, 79, "partial damage overlap");
            Test(79, 1, new[] { P(), P() }, new[] { E() }, 59, "one event amount only once");
            Test(59, 2, new[] { P(), P(cursor: 1, health: 79) }, new[] { E(2), E(1) }, 59, "out of order compact history");
            Test(79, 1, new[] { P(), P(cursor: 1, health: 79) }, new[] { E() }, 59, "new cursor cannot borrow old event");
            Test(79, 1, new[] { P() }, new[] { E(attacker: 2) }, 59, "other attacker");
            Test(79, 1, new[] { P() }, new[] { E(generation: 8) }, 59, "old attacker generation");
            Test(79, 1, new[] { P() }, new[] { E(beam: BeamType.PowerBeam) }, 59, "different weapon");
            Test(79, 1, new[] { P() }, new[] { E() }, 59, "accepted event excluded", new[] { new Accepted(1, 20) });
            Test(79, 1, new[] { P() }, Array.Empty<DamageEvent>(), 59, "evicted accepted event excluded", new[] { new Accepted(1, 20) });
            Test(79, 1, new[] { P(), P() }, new[] { E(), E() }, 59, "duplicate compact IDs not doubled");
            var missing = Test(79, 1, new[] { P() }, Array.Empty<DamageEvent>(), 79, "missing history optimistic net drop");
            Check(missing.Ambiguous, "missing history explicitly ambiguous");
            var partial = Test(79, 2, new[] { P() }, new[] { E(2, 10, attacker: 2) }, 69, "partial unrelated plus missing");
            Check(partial.Ambiguous, "partial history ambiguous");
            var conflicting = Test(79, 1, new[] { P() }, new[] { E(), E(damage: 7) }, 79, "conflicting history only ambiguous possibility");
            Check(conflicting.Ambiguous, "conflicting event must not be treated as complete");
            Test(59, 2, new[] { P(), P(cursor: 2, health: 59) }, Array.Empty<DamageEvent>(), 39, "unknown older hit cannot absorb current prediction");
            Test(59, 2, new[] { P(), P(cursor: 1, health: 79) }, Array.Empty<DamageEvent>(), 59, "missing history across successive anchors");
            Test(110, 0, new[] { P() }, Array.Empty<DamageEvent>(), 90, "healing releases anchor");
            Test(110, 1, new[] { P() }, Array.Empty<DamageEvent>(), 90, "healing with missing event history");
            Test(1, 0, new[] { P(150) }, Array.Empty<DamageEvent>(), 1, "remote lethal clamp");
            Test(0, 1, new[] { P(150) }, new[] { E() }, 0, "canonical zero decisive");
            Test(99, 1, Array.Empty<Pending>(), new[] { E() }, 99, "no predictions");
            Test(79, 1, new[] { P(cursor: ushort.MaxValue) }, new[] { E() }, 79, "zero skipping sequence wrap");
            var wrap = Test(59, 1, new[] { P(40, ushort.MaxValue - 1) }, new[] { E(ushort.MaxValue), E() }, 59, "complete wrap interval");
            Check(!wrap.Ambiguous, "zero skipping wrap does not invent missing event");
            Test(99, 1, new[] { P() }, new[] { E(2) }, 79, "future event is outside sample");
            Test(99, 0, new[] { P(cursor: 1) }, new[] { E(1) }, 79, "older sample cannot borrow event");
            Test(79, 1, new[] { P(formCursor: 1, formHealth: 79) }, new[] { E() }, 59, "form cursor independent", form: true);
            Test(79, 1, new[] { P(formCursor: 1, formHealth: 79) }, new[] { E() }, 79, "body cursor independent");
            Test(99, 1, new[] { P() }, new[] { E(damage: 0) }, 79, "zero damage is no overlap");
            var full = new Pending[Capacity]; Array.Fill(full, P());
            Test(99, 0, full, Array.Empty<DamageEvent>(), 1, "bounded full pending ring");
            var state = new PlayerState { Damage0 = E() };
            var input = new[] { P() };
            for (int i = 0; i < 50; i++) Project(79, 1, state, input, ReadOnlySpan<Accepted>.Empty);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) Project(79, 1, state, input, ReadOnlySpan<Accepted>.Empty);
            Check(GC.GetAllocatedBytesForCurrentThread() == before, "projection allocates no per-sample heap storage");
            Console.WriteLine($"PASS: {_checks} bounded prediction health projection assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
