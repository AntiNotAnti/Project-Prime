using System;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class ShotLifecycleTests
{
    private static void Check(bool value, string name)
    { if (!value) throw new InvalidOperationException(name); }
    private static ShotKey Key(uint id, ushort life = 1, ushort generation = 1)
        => new(1, 1, 0, generation, life, id);
    private static FireEvent Fire(uint id, uint frame = 10)
        => new(id, frame, frame, 0, FireEventKind.PressFire, 0, 0, 0);
    private static void Accounting(AcceptedShotLedger ledger)
        => Check(ledger.Accepted == ledger.Fired + ledger.Rejected + ledger.Expired + ledger.Pending,
            "every admitted shot has one outcome or remains pending");
    public static int Run()
    {
        try
        {
            var ledger = new AcceptedShotLedger();
            Check(ledger.Accept(Key(2), Fire(2, 12), 12) == ShotAdmission.Accepted, "first admission");
            Check(ledger.Accept(Key(1), Fire(1), 12) == ShotAdmission.Accepted, "late older admission");
            Check(ledger.TryPeek(out var shot) && shot.Key == Key(1), "source order beats arrival order");
            Check(ledger.Accept(Key(1), Fire(1), 12) == ShotAdmission.Duplicate, "pending duplicate");
            Check(ledger.Accept(Key(1), Fire(1) with { Charge = 12 }, 12) == ShotAdmission.Conflict,
                "conflicting payload rejected");
            Check(ledger.TryGet(Key(1), out shot, out var state, out _) && shot.Fire.Charge == 0
                && state == ShotDisposition.Pending, "conflict preserves original pending shot");
            // A failed spawn does not complete the shot. The next pump sees the same key.
            Check(ledger.TryPeek(out shot) && shot.Key == Key(1), "spawn failure remains retryable");
            Check(ledger.Complete(Key(1), ShotDisposition.Fired, "spawn succeeded"), "successful spawn commits");
            Check(!ledger.Complete(Key(1), ShotDisposition.Fired, "duplicate success"), "terminal transition once");
            Check(ledger.Accept(Key(1), Fire(1), 12) == ShotAdmission.Duplicate, "fired duplicate");
            Check(ledger.Accept(Key(1, 2), Fire(1, 12), 12) == ShotAdmission.Accepted,
                "same ID in new life independent");
            ledger.Retire(1, 1, 0, 1);
            Check(ledger.Pending == 2, "death and respawn do not retire life entries");
            ledger.Retire(1, 1, 0, 2);
            Check(ledger.Pending == 0 && ledger.Rejected == 2, "occupant replacement explicitly retires shots");
            Accounting(ledger);

            var bounded = new AcceptedShotLedger();
            for (uint i = 1; i <= AcceptedShotLedger.Capacity; i++)
                Check(bounded.Accept(Key(i), Fire(i), 10) == ShotAdmission.Accepted, "capacity admission");
            Check(bounded.Accept(Key(999), Fire(999), 10) == ShotAdmission.Overflow
                && bounded.Pending == AcceptedShotLedger.Capacity && bounded.Overflows == 1,
                "full queue cannot silently evict pending");
            bounded.Expire(75);
            Check(bounded.Pending == 0 && bounded.Expired == AcceptedShotLedger.Capacity, "explicit expiration");
            Check(bounded.TryGet(Key(1), out _, out state, out var reason)
                && state == ShotDisposition.Expired && !string.IsNullOrEmpty(reason), "expiration proof retained");
            Check(bounded.Accept(Key(1000), Fire(1000, 75), 75) == ShotAdmission.Accepted,
                "expired terminals release bounded storage");
            Check(bounded.Accept(Key(1), Fire(1), 75) == ShotAdmission.Expired,
                "evicted old identity cannot resurrect");
            Accounting(bounded);

            var wrapped = new AcceptedShotLedger();
            Check(wrapped.Accept(Key(uint.MaxValue), Fire(uint.MaxValue, uint.MaxValue - 2), 1)
                == ShotAdmission.Accepted, "frame wrap admission");
            Check(wrapped.Accept(Key(1), Fire(1, 0), 1) == ShotAdmission.Accepted, "post wrap admission");
            Check(wrapped.TryPeek(out shot) && shot.Key.ShotId == uint.MaxValue, "frame wrap ordering");
            wrapped.Expire(uint.MaxValue); // Reordered observation must not rewind expiration baseline.
            Check(wrapped.Pending == 2, "old observed frame does not expire new shots");
            wrapped.Retire(2, 1, 0, 1);
            Check(wrapped.Pending == 0 && wrapped.Rejected == 2, "authority epoch retirement");
            Accounting(wrapped);

            var restarted = new AcceptedShotLedger();
            restarted.Accept(Key(1), Fire(1, 1000), 1000);
            restarted.Retire(1, 1, 0, 2);
            restarted.ResetObservationClock();
            Check(restarted.Accept(Key(1, generation: 2), Fire(1, 1), 1) == ShotAdmission.Accepted,
                "replacement occupant may restart source clock after retirement");
            restarted.Expire(1);
            Check(restarted.Pending == 1, "same-clock expiration preserves newly admitted pending shot");
            restarted.Expire(66);
            Check(restarted.Pending == 0 && restarted.Expired == 1, "advanced source clock expires retained shot");
            Accounting(restarted);

            var terminal = new AcceptedShotLedger();
            int callbacks = 0;
            terminal.OnDisposition = (accepted, disposition, explanation) =>
            {
                callbacks++;
                Check(terminal.TryGet(accepted.Key, out _, out var recorded, out var recordedReason)
                    && recorded == disposition && recordedReason == explanation,
                    "callback observes committed terminal record before retirement clear");
            };
            terminal.Accept(Key(1), Fire(1), 10);
            Check(terminal.Complete(Key(1), ShotDisposition.Rejected, "invalid spatial state")
                && terminal.TryGet(Key(1), out _, out state, out reason)
                && reason == "invalid spatial state", "unrecoverable spawn failure has explicit outcome");
            Check(terminal.Accept(Key(2), Fire(2) with { Kind = FireEventKind.ContinuousTick }, 10)
                == ShotAdmission.Invalid, "continuous policy remains separate");
            terminal.Accept(Key(3), Fire(3), 10);
            terminal.Retire(1, 2, 0, 1);
            Check(terminal.Pending == 0 && terminal.Rejected == 2, "match retirement");
            Accounting(terminal);
            Check(callbacks == 2, "one callback per terminal transition");
            terminal.Accept(Key(4), Fire(4, 11), 11);
            terminal.Expire(76);
            Check(callbacks == 3, "expiration publishes exact terminal disposition");
            Console.WriteLine("shot lifecycle: PASS");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"shot lifecycle: FAIL {exception.Message}");
            return 1;
        }
    }
}
