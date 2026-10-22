using System;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class ContinuousHealingPredictionTests
{
    private static int _checks;
    private static PlayerEntity _owner = null!;
    private static void Check(bool value, string name)
    { _checks++; if (!value) throw new InvalidOperationException(name); }

    public static int Run()
    {
        try
        {
            SnapshotFirst(); AckFirst(); UnrelatedHealthChanges(); DuplicateAndReorderedTicks();
            RejectionWhileDisabled(); MaximumAndDeadHealth(); OwnerLifecycleReset();
            WrongSnapshotLifecycle(); ExpirationDuringContinuedDrain(); WrappedTickReceipt();
            UnkeyedDrainCannotCredit();
            SpeculativeHealChecksExactTickBeforeGain();
            Console.WriteLine($"PASS: {_checks} continuous healing prediction assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { NetHitPrediction.Enabled = true; NetSession.Stop(); }
    }

    private static void Prepare()
    {
        HealthShotTests.Session(); NetHitPrediction.Reset(); NetHitPrediction.Enabled = true;
        _owner = HealthShotTests.Player(0, 70); HealthShotTests.Field(_owner, "_healthMax", 99);
        var state = HealthShotTests.State(2, 70); state.SlotIndex = 0; state.SlotGeneration = 9;
        NetSession.RemoteStates[0] = state; NetSession.RemoteStateValid[0] = true;
    }
    private static int Health(int authorityHealth) => NetHitPrediction.LocalHealthFor(_owner, authorityHealth);
    private static void Credit(uint tick, int amount = 2, ushort claim = 1)
        => NetHitPrediction.NoteDrain(_owner, amount, tick, claim);
    private static void Receipt(uint tick, ulong low, ulong high = 0, ushort health = 72)
    {
        var state = NetSession.RemoteStates[0]; state.Health = health;
        state.ContinuousPaidTick = tick; state.ContinuousPaidMaskLow = low; state.ContinuousPaidMaskHigh = high;
        NetSession.RemoteStates[0] = state;
    }
    private static void Advance(uint frames)
        => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, NetSession.NetFrame + frames);

    private static void SnapshotFirst()
    {
        Prepare(); Credit(10);
        Check(Health(70) == 72, "owner drain prediction is immediate");
        Receipt(10, 1);
        Check(Health(72) == 72 && NetHitPrediction.DrainConfirmed == 2 && NetHitPrediction.DrainCurrentPoints == 0,
            "same-state snapshot proof retires credit before ACK without double healing");
        NetHitPrediction.SettleDrain(1, true);
        Check(Health(72) == 72, "later accepted ACK cannot recreate snapshot-settled credit");
        Credit(10);
        Check(Health(72) == 72 && NetHitPrediction.DrainPredicted == 2,
            "duplicate physical tick after snapshot settlement cannot recreate credit");

        Prepare(); Receipt(10, 1); Credit(10);
        Check(Health(72) == 72 && NetHitPrediction.DrainCurrentPoints == 0,
            "snapshot preceding local drain cannot add already included tick twice");
    }
    private static void AckFirst()
    {
        Prepare(); Credit(20); NetHitPrediction.SettleDrain(1, true);
        Check(Health(70) == 72 && NetHitPrediction.DrainCurrentPoints == 2,
            "accepted ACK keeps credit until its canonical owner snapshot arrives");
        Receipt(20, 1);
        Check(Health(72) == 72 && NetHitPrediction.DrainConfirmed == 2,
            "ACK then snapshot converges without an HP dip or double credit");
    }
    private static void UnrelatedHealthChanges()
    {
        Prepare(); Credit(30); Receipt(31, 1, health: 80);
        Check(Health(80) == 82 && NetHitPrediction.DrainConfirmed == 0,
            "unrelated pickup and unrelated paid tick cannot retire credit by HP-rise guessing");
        Receipt(31, 1, health: 50);
        Check(Health(50) == 52, "incoming authority damage remains visible while exact drain is pending");
        Receipt(31, 3, health: 52);
        Check(Health(52) == 52 && NetHitPrediction.DrainCurrentPoints == 0,
            "only the corresponding paid bit retires the pending credit");
    }
    private static void DuplicateAndReorderedTicks()
    {
        Prepare(); Credit(40, 2, 4); Credit(42, 3, 6); Credit(40, 2, 4); Credit(41, 4, 5);
        Check(Health(70) == 79 && NetHitPrediction.DrainPredicted == 9,
            "duplicate phase cannot credit twice and distinct reordered phases remain independent");
        Receipt(42, 1, health: 73);
        Check(Health(73) == 79 && NetHitPrediction.DrainConfirmed == 3,
            "sparse latest receipt does not FIFO-retire earlier ticks");
        NetHitPrediction.SettleDrain(4, false);
        Check(Health(73) == 77 && NetHitPrediction.DrainRejected == 2,
            "rejected earlier claim removes only its own credit");
        NetHitPrediction.SettleDrain(4, false); Credit(40, 2, 4);
        Check(Health(73) == 77 && NetHitPrediction.DrainRejected == 2,
            "duplicate rejection and local tick cannot pay rejected phase again");
        Receipt(42, 3, health: 77);
        Check(Health(77) == 77 && NetHitPrediction.DrainConfirmed == 7,
            "later reordered receipt retires remaining exact phase");
    }
    private static void RejectionWhileDisabled()
    {
        Prepare(); Credit(50); _owner.Health = 0; NetHitPrediction.Enabled = false;
        NetHitPrediction.SettleDrain(1, false);
        Check(NetHitPrediction.DrainRejected == 2 && NetHitPrediction.DrainCurrentPoints == 0,
            "rejection retires exact credit even disabled and owner appears dead");
        NetHitPrediction.Enabled = true;
        Check(Health(70) == 70 && _owner.Health == 0, "reenabling cannot resurrect credit or mutate a dead owner");
    }
    private static void MaximumAndDeadHealth()
    {
        Prepare(); Credit(60, 5);
        Check(Health(98) == 99, "presentation credit respects owner maximum health");
        Check(Health(0) == 0, "authority dead health cannot become predicted alive health");
        Receipt(60, 1, health: 0);
        Check(Health(0) == 0 && NetHitPrediction.DrainCurrentPoints == 0,
            "dead canonical snapshot can retire included tick without healing the corpse");
        Check(NetSession.RemoteStates[0].Health == 0, "prediction never rewrites canonical owner health");
    }
    private static void OwnerLifecycleReset()
    {
        Prepare(); Credit(70);
        var state = NetSession.RemoteStates[0]; state.LifeId = 3;
        NetPlayerLifecycle.AcceptState(state, 3); NetSession.RemoteStates[0] = state;
        Check(Health(70) == 70 && NetHitPrediction.DrainCurrentPoints == 0,
            "new owner life cannot inherit old healing credit");
        Credit(70);
        Check(Health(70) == 72, "same tick number in new owner life is a distinct prediction");
        NetHitPrediction.ForgetPending();
        Check(Health(70) == 70 && NetHitPrediction.DrainCurrentPoints == 0,
            "room/session pending reset removes owner healing credits");
    }
    private static void WrongSnapshotLifecycle()
    {
        Prepare(); Credit(80); Receipt(80, 1);
        var state = NetSession.RemoteStates[0]; state.LifeId = 3; NetSession.RemoteStates[0] = state;
        Check(Health(70) == 72, "other owner life snapshot cannot retire current healing credit");
        state.LifeId = 2; state.SlotGeneration = 10; NetSession.RemoteStates[0] = state;
        Check(Health(70) == 72, "other occupant snapshot cannot retire current healing credit");
        state.SlotGeneration = 9; NetSession.RemoteStates[0] = state;
        Check(Health(72) == 72, "matching owner lifecycle restores exact snapshot retirement");
    }
    private static void ExpirationDuringContinuedDrain()
    {
        Prepare(); Credit(90); Advance(119);
        Check(Health(70) == 72, "credit lasts its fixed 120-frame bounded deadline");
        Credit(91, 3, 2); Advance(1); NetHitPrediction.Tick();
        Check(Health(70) == 73 && NetHitPrediction.DrainExpired == 2 && NetHitPrediction.DrainCurrentPoints == 3,
            "new drain cannot extend expired older credit");
        Advance(120); NetHitPrediction.Tick();
        Check(Health(70) == 70 && NetHitPrediction.DrainExpired == 5,
            "missing receipts eventually release every predicted healing point");
    }
    private static void WrappedTickReceipt()
    {
        Prepare(); Credit(uint.MaxValue); Credit(0, 3, 2); Receipt(0, 3, health: 75);
        Check(Health(75) == 75 && NetHitPrediction.DrainConfirmed == 5,
            "exact paid bitmap handles uint tick wrap and phase zero");
        Prepare(); Credit(100); Receipt(227, 0, 1UL << 63);
        Check(Health(72) == 72 && NetHitPrediction.DrainConfirmed == 2,
            "oldest high bitmap bit retires exact phase at distance 127");
        Prepare(); Credit(100); Receipt(228, 0, 1UL << 63, health: 70);
        Check(Health(70) == 72, "phase outside bitmap history cannot consume neighboring receipt");
    }
    private static void UnkeyedDrainCannotCredit()
    {
        Prepare(); NetHitPrediction.NoteDrain(_owner, 2);
        Check(Health(70) == 70 && NetHitPrediction.DrainPredicted == 0,
            "unkeyed compatibility call cannot introduce uncorrelated live healing credit");
    }

    private static void SpeculativeHealChecksExactTickBeforeGain()
    {
        Prepare();
        Check(NetHitPrediction.CanPredictDrain(_owner, 300), "new local owner tick permits immediate speculative healing");
        int before = _owner.Health;
        _owner.GainHealth(2); NetHitPrediction.NoteDrain(_owner, _owner.Health - before, 300, 7);
        Check(!NetHitPrediction.CanPredictDrain(_owner, 300) && _owner.Health == 72,
            "duplicate phase is refused before it can call GainHealth twice");
        NetHitPrediction.SettleDrain(7, false);
        Check(!NetHitPrediction.CanPredictDrain(_owner, 300), "rejected phase tombstone prevents another speculative heal");
        Check(NetHitPrediction.CanPredictDrain(_owner, 301), "independent new tick remains eligible after earlier rejection");
        Receipt(301, 1); _owner.Health = 72;
        Check(!NetHitPrediction.CanPredictDrain(_owner, 301) && _owner.Health == 72,
            "snapshot-first included phase is refused before physical healing");
        Credit(302, 2, 8); Receipt(302, 3, health: 74);
        Check(!NetHitPrediction.CanPredictDrain(_owner, 302) && NetHitPrediction.DrainCurrentPoints == 0,
            "early guard reconciles exact snapshot inclusion before allowing another heal");
        NetHitPrediction.Enabled = false;
        Check(!NetHitPrediction.CanPredictDrain(_owner, 303), "prediction disabled cannot grant speculative healing");
        NetHitPrediction.Enabled = true; _owner.Health = 0;
        Check(!NetHitPrediction.CanPredictDrain(_owner, 303), "dead owner cannot grant speculative healing");
        _owner.Health = 70;
        Check(!NetHitPrediction.CanPredictDrain(HealthShotTests.Player(1), 303),
            "another player's local copy cannot receive speculative owner healing");
        Prepare(); _owner.Health = 99;
        NetHitPrediction.NoteDrain(_owner, 0, 310, 9); _owner.Health = 70;
        Check(!NetHitPrediction.CanPredictDrain(_owner, 310) && NetHitPrediction.DrainCurrentPoints == 0,
            "zero-gain full-health tick cannot heal again after intervening damage");
    }
}
