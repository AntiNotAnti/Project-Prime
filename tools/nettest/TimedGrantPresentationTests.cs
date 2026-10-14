using System;
using System.IO;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;
internal static class TimedGrantPresentationTests
{
    private static int _checks;
    private static void Check(bool ok, string message)
    { _checks++; if (!ok) throw new InvalidOperationException(message); }
    private static void Throws(Action action, string message)
    { bool failed = false; try { action(); } catch (InvalidDataException) { failed = true; } catch (EndOfStreamException) { failed = true; } Check(failed, message); }
    private static ShotKey Life(ushort life = 1, ushort generation = 1) => new(11, 7, 0, generation, life, 0);
    private static PickupClaim Claim(uint sequence = 1, uint spawn = 1, ushort life = 1, uint room = 1)
        => new(new(11, 7, 0, 1, life, sequence), new(11, 7, room, 10, spawn), TimedPowerUp.DoubleDamage, 100, Vector3.Zero);
    private static TimedGrantFact Fact(uint revision = 1, ulong sequence = 1, uint spawn = 1, ushort life = 1,
        uint contact = 100, uint authority = 100, uint room = 1)
        => new(Life(life), room, revision, TimedPowerUp.DoubleDamage, sequence, 10, spawn, contact,
            unchecked(contact + 1800), TimedGrantEnd.Active, authority);
    private static PickupVerdict Grant(PickupClaim claim, ulong sequence = 1, uint contact = 100)
        => new(claim, PickupResult.Grant, contact, contact, sequence, contact, unchecked(contact + 1800));
    private static PickupVerdict Deny(PickupClaim claim) => new(claim, PickupResult.Taken, 110, 100, 0, 0, 0);
    private static byte[] Save(TimedGrantPresentation state)
    { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); state.Write(writer); return stream.ToArray(); }
    private static TimedGrantPresentation Load(byte[] bytes)
    { using var stream = new MemoryStream(bytes); using var reader = new BinaryReader(stream); return TimedGrantPresentation.Read(reader); }
    private static void PredictionAndTruth()
    {
        var state = new TimedGrantPresentation(); var claim = Claim();
        Check(state.Predict(claim, 100), "owner prediction admitted");
        var predicted = state.View(Life(), TimedPowerUp.DoubleDamage, 100);
        Check(predicted.Active && predicted.Remaining == 1800 && predicted.ConfirmedRemaining == 0
            && state.Hides(claim.Spawn, 100), "prediction is immediate presentation with zero confirmed combat duration");
        var absent = new TimedGrantFact(Life(), 1, 1, TimedPowerUp.DoubleDamage, 0, -1, 0, 0, 0, TimedGrantEnd.Retired, 90);
        Check(state.Accept(absent, 100) && state.PendingCount == 1 && state.View(Life(), TimedPowerUp.DoubleDamage, 100).Active,
            "older authoritative absence never denies in-flight prediction");
        Check(state.Accept(Fact(revision: 2), 100) && state.PendingCount == 0 && state.Hides(claim.Spawn, 100), "matching delta confirms before verdict and keeps consumed item hidden");
        var confirmed = state.View(Life(), TimedPowerUp.DoubleDamage, 100);
        Check(confirmed.Remaining == 1800 && confirmed.ConfirmedRemaining == 0 && confirmed.PredictionSequence == 0
            && confirmed.TransitionFrom(predicted, false) == TimedEffectTransition.Retime, "confirmation retimes without gain restart");
        Check(state.Settle(Grant(claim), 100) && !state.Settle(Grant(claim), 100), "late matching ACK applies once");
        Check(!state.Predict(claim, 110) && state.PendingCount == 0, "already confirmed incarnation cannot be predicted as renewal");
        var renewal = Claim(2, 2); Check(state.Predict(renewal, 120), "new incarnation renewal predicts");
        Check(state.Settle(Deny(renewal), 125) && !state.Settle(Deny(renewal), 126), "exact denial disposes once");
        Check(state.View(Life(), TimedPowerUp.DoubleDamage, 125).GrantSequence == 1
            && state.View(Life(), TimedPowerUp.DoubleDamage, 125).ConfirmedRemaining > 0, "denied renewal retains older confirmed truth");
        var denied = new TimedGrantPresentation(); denied.Predict(claim, 100); var before = denied.View(Life(), TimedPowerUp.DoubleDamage, 100);
        Check(denied.Settle(Deny(claim), 110) && !denied.View(Life(), TimedPowerUp.DoubleDamage, 110).Active
            && denied.View(Life(), TimedPowerUp.DoubleDamage, 110).TransitionFrom(before, false) == TimedEffectTransition.End,
            "sole refused prediction ends its effect");
        Check(predicted.TransitionFrom(default, false) == TimedEffectTransition.Start
            && predicted.TransitionFrom(default, true) == TimedEffectTransition.Resume
            && predicted.TransitionFrom(predicted, false) == TimedEffectTransition.Keep, "native transition vocabulary");
    }
    private static void PendingCannotShortenConfirmedTruth()
    {
        var state = new TimedGrantPresentation();
        var prediction = Claim(2, 2);
        Check(state.Predict(prediction, 100), "unrelated older prediction enters delivery overlay");
        Check(state.Accept(Fact(sequence: 3, spawn: 3, contact: 120, authority: 120), 120),
            "newer authority grant arrives independently of pending item");
        var view = state.View(Life(), TimedPowerUp.DoubleDamage, 125);
        Check(view.ConfirmedRemaining == 1795 && view.Remaining == view.ConfirmedRemaining
            && view.PredictionSequence == prediction.Key.Sequence,
            "older outstanding prediction cannot shorten confirmed native visual timer");
        Check(state.Settle(Deny(prediction), 125), "unrelated prediction denial settles");
        var denied = state.View(Life(), TimedPowerUp.DoubleDamage, 125);
        Check(denied.Remaining == 1795 && denied.ConfirmedRemaining == 1795
            && denied.GrantSequence == 3 && denied.PredictionSequence == 0,
            "denial retains complete independent authority timer and identity");

        var ordered = new TimedGrantPresentation();
        ordered.Predict(Claim(4, 4), 120);
        ordered.Predict(Claim(5, 5), 100);
        var latest = ordered.View(Life(), TimedPowerUp.DoubleDamage, 125);
        Check(latest.PredictionSequence == 4 && latest.Remaining == 1795,
            "latest prediction selected independently of enumeration order");
    }
    private static void RenewalClosurePreservesContactInput()
    {
        var state = new TimedGrantPresentation();
        Check(state.Accept(Fact(), 100), "original grant enters authority presentation");
        var closing = Fact(revision: 2) with
            { Reason = TimedGrantEnd.Renewed, EndFrame = 151, AuthorityFrame = 150 };
        Check(closing.IsValid && state.Accept(closing, 150),
            "renewal closure may precede its next-input end by one frame");
        var contact = state.View(Life(), TimedPowerUp.DoubleDamage, 150);
        Check(contact.Active && contact.Remaining == 1750 && contact.ConfirmedRemaining == 1750
            && contact.GrantSequence == 1,
            "renewal-contact input retains old grant natural duration before replacement fact arrives");
        var next = state.View(Life(), TimedPowerUp.DoubleDamage, 151);
        Check(!next.Active && next.Remaining == 0 && next.ConfirmedRemaining == 0,
            "old grant closes at next input without replacement lookahead");
        Check(state.Accept(Fact(revision: 3, sequence: 2, spawn: 2, contact: 150, authority: 152), 152),
            "later replacement fact arrives on its own authority clock");
        var replacement = state.View(Life(), TimedPowerUp.DoubleDamage, 152);
        Check(replacement.Active && replacement.Remaining == 1798 && replacement.ConfirmedRemaining == 1798
            && replacement.GrantSequence == 2 && replacement.PredictionSequence == 0,
            "replacement restores new grant through current fact without source-proof lookahead");
    }
    private static void NativeInputPhase()
    {
        foreach (var type in new[] { TimedPowerUp.DoubleDamage, TimedPowerUp.Cloak, TimedPowerUp.DeathAlt })
        foreach (bool fromVerdict in new[] { false, true })
        {
            var state = new TimedGrantPresentation();
            if (fromVerdict) state.Settle(Grant(Claim() with { Type = type }), 100);
            else state.Accept(Fact() with { Type = type }, 100);
            var contact = state.View(Life(), type, 100);
            Check(contact.Remaining == 1800 && contact.ConfirmedRemaining == 0,
                "contact frame has immediate visual effect but no retroactive confirmed input " + type);
            Check(state.View(Life(), type, 101).ConfirmedRemaining == 1799,
                "following input first owns confirmed grant " + type);
            Check(state.View(Life(), type, 1899).ConfirmedRemaining == 1,
                "last native eligible input retains one confirmed frame " + type);
            Check(!state.View(Life(), type, 1900).Active && state.View(Life(), type, 1900).ConfirmedRemaining == 0,
                "exact authority expiry ends visual and confirmed eligibility " + type);
        }
    }

    private static void FencesClocksAndBounds()
    {
        var state = new TimedGrantPresentation(); state.Settle(Grant(Claim(life: 2), 2), 100);
        Check(!state.Settle(Grant(Claim(), 1), 110) && state.View(Life(2), TimedPowerUp.DoubleDamage, 110).ConfirmedRemaining > 0,
            "delayed prior-life ACK cannot displace newer ACK-only truth");
        Check(!state.Predict(Claim(), 110), "older-life prediction cannot overlay newer truth");
        Check(state.Accept(Fact(life: 3, sequence: 3), 110) && !state.Settle(Grant(Claim(life: 2), 4), 111), "newer fact fences older-life higher-sequence ACK");
        Check(state.View(Life(2), TimedPowerUp.DoubleDamage, 111).ConfirmedRemaining == 0, "new life prevents old acknowledgement resurrection");
        Check(!state.Accept(Fact(revision: 2, life: 2), 112), "delayed old-life fact cannot replace current fact");
        Check(state.Accept(Fact(room: 2), 120) && !state.Predict(Claim(room: 1), 120)
            && !state.Settle(Grant(Claim(room: 1)), 120), "room transition fences prior claims and ACKs");
        var deadline = new TimedGrantPresentation(); deadline.Predict(Claim(), uint.MaxValue - 4);
        Check(deadline.View(Life(), TimedPowerUp.DoubleDamage, 0).Remaining == 1795, "prediction timer handles local wrap");
        deadline.Advance(895); Check(deadline.PendingCount == 0, "prediction deadline expires across wrap");
        deadline.Predict(Claim(2), 1000); deadline.Advance(1001, _ => false); Check(deadline.PendingCount == 0, "life/spectator fence disposes predictions");
        var wrapped = new TimedGrantPresentation(); uint contact = uint.MaxValue - 5;
        wrapped.Accept(Fact(contact: contact, authority: contact), 50);
        Check(wrapped.AuthorityNow(56) == 0 && wrapped.View(Life(), TimedPowerUp.DoubleDamage, 56).ConfirmedRemaining == 1794,
            "authority countdown handles wrap independently of local clock");
        wrapped.ObserveClock(contact, 56); Check(wrapped.AuthorityNow(56) == 0, "stale authority clock cannot rewind expiry");
        Check(!wrapped.View(Life(), TimedPowerUp.DoubleDamage, 1850).Active, "exact authority grant expiry ends effect");
        var bounded = new TimedGrantPresentation(); for (uint i = 1; i <= 16; i++) Check(bounded.Predict(Claim(i, i), 100), "bounded distinct prediction admitted");
        Check(!bounded.Predict(Claim(17, 17), 100) && bounded.PendingCount == 16, "seventeenth prediction refuses without growth");
        var revision = new TimedGrantPresentation(); revision.Accept(Fact(revision: 2), 100);
        Check(!revision.Accept(Fact(revision: 2), 100) && !revision.Accept(Fact(), 100), "identical revision and older revision are inert");
        Throws(() => revision.Accept(Fact(revision: 2, spawn: 2), 100), "same revision divergent fact fails explicitly");
        var conflicting = new TimedGrantPresentation(); conflicting.Predict(Claim(), 100);
        Throws(() => conflicting.Predict(Claim() with { Position = Vector3.UnitX }, 100), "same prediction ID divergent payload fails");
    }
    private static void Checkpoints()
    {
        var a = new TimedGrantPresentation(); a.Accept(Fact(), 100); a.Settle(Grant(Claim()), 100); a.Predict(Claim(2, 2), 110);
        var b = new TimedGrantPresentation(); b.Predict(Claim(3, 3, life: 2), 200);
        var restored = Load(Save(a)); var restoredB = Load(Save(b));
        Check(restored.View(Life(), TimedPowerUp.DoubleDamage, 120) == a.View(Life(), TimedPowerUp.DoubleDamage, 120)
            && restored.PendingCount == 1 && restored.Hides(Claim(2, 2).Spawn, 120), "checkpoint restores truth, ACK, pending item and independent clocks");
        restored.Settle(Deny(Claim(2, 2)), 120);
        Check(a.PendingCount == 1 && b.PendingCount == 1 && restoredB.PendingCount == 1, "interleaved private restores never mutate other worlds");
        var empty = Save(new TimedGrantPresentation()); empty[14] = 2; Throws(() => Load(empty), "checkpoint rejects noncanonical boolean");
        empty = Save(new TimedGrantPresentation()); BitConverter.GetBytes(17).CopyTo(empty, 71); Throws(() => Load(empty), "checkpoint rejects oversized pending count");
        var wrongSlot = Save(a); wrongSlot[34] = 1; Throws(() => Load(wrongSlot), "checkpoint rejects fact in wrong slot cell");
        var duplicate = new TimedGrantPresentation(); duplicate.Predict(Claim(), 100); duplicate.Predict(Claim(2, 2), 100);
        var bytes = Save(duplicate); bytes.AsSpan(75, 48).CopyTo(bytes.AsSpan(127, 48)); Throws(() => Load(bytes), "checkpoint rejects duplicate pending key");
        var truncated = Save(a); Array.Resize(ref truncated, truncated.Length - 1); Throws(() => Load(truncated), "checkpoint rejects truncation");
    }
    private static void ConsumptionCapacityIsTransactional()
    {
        var state = new TimedGrantPresentation();
        for (uint i = 1; i <= NetTimedGrants.GrantCapacity; i++)
            Check(state.Accept(Fact(revision: i, sequence: i, spawn: i), 100),
                "distinct consumed incarnation admitted within bound");
        uint overflow = NetTimedGrants.GrantCapacity + 1;
        var before = Save(state);
        Throws(() => state.Accept(Fact(revision: overflow, sequence: overflow, spawn: overflow), 100),
            "513th unexpired consumed incarnation refuses explicitly");
        Check(before.AsSpan().SequenceEqual(Save(state)) && !state.Hides(Claim(overflow, overflow).Spawn, 100),
            "refused fact preserves prior checkpoint bytes and item visibility");
        var pending = Claim(overflow, overflow);
        Check(state.Predict(pending, 100), "delivery overlay remains independent of consumption capacity");
        before = Save(state);
        Throws(() => state.Settle(Grant(pending, overflow), 100),
            "513th terminal grant refuses explicitly");
        Check(before.AsSpan().SequenceEqual(Save(state)) && state.PendingCount == 1,
            "refused terminal preserves prediction obligation and prior truth");
        Check(Load(before).PendingCount == 1, "full bound plus pending prediction roundtrips");
        Check(state.Accept(Fact(revision: overflow, sequence: overflow, spawn: overflow,
                contact: 4000, authority: 4000), 4000),
            "expired history frees capacity for a later native incarnation");
        Check(!state.Hides(Claim().Spawn, 4000) && state.Hides(pending.Spawn, 4000),
            "retention pruning keeps only current consumed identities");
    }
    internal static int Run()
    {
        try { _checks = 0; PredictionAndTruth(); PendingCannotShortenConfirmedTruth(); RenewalClosurePreservesContactInput(); NativeInputPhase(); FencesClocksAndBounds(); Checkpoints(); ConsumptionCapacityIsTransactional(); Console.WriteLine($"PASS: {_checks} timed grant presentation assertions"); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
