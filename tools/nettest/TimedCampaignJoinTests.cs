using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Constructed storage checks only; actual native/UDP joins need separate runs.
internal static class TimedCampaignJoinTests
{
    private static int _checks;
    private static void Check(bool ok, string text)
    { if (!ok) throw new InvalidOperationException("Timed campaign join: " + text); _checks++; }
    internal static int Run()
    {
        Check(!RuntimeHelpers.IsReferenceOrContainsReferences<TimedJoinCertificate>(), "certificate contains values only");
        Check(!RuntimeHelpers.IsReferenceOrContainsReferences<TimedJoinComponent>(), "component contains values only");
        Check(!RuntimeHelpers.IsReferenceOrContainsReferences<TimedJoinClaim>(), "claim contains values only");
        Check(!RuntimeHelpers.IsReferenceOrContainsReferences<TimedJoinTerminal>(), "terminal contains values only");
        Check(!RuntimeHelpers.IsReferenceOrContainsReferences<TimedJoinPayment>(), "payment/witness contains values only");
        Check(!RuntimeHelpers.IsReferenceOrContainsReferences<TimedJoinContinuous>(), "continuous contains values only");
        int bytes = 0;
        var rows = new TimedJoinValues<int>(2, value => { bytes += value; return true; });
        rows.Add(17); rows.Add(23); rows.Add(99);
        Check(rows.Rows().SequenceEqual(new[] { 17, 23 }), "full buffer never overwrites first observations");
        Check(rows.Count == 2 && rows.Observed == 3 && rows.Dropped == 1, "drop has explicit denominator");
        Check(bytes == 2 * sizeof(int), "partial final page obeys row bound");
        var blocked = new TimedJoinValues<int>(2048, _ => false);
        blocked.Add(1); blocked.Add(2);
        Check(blocked.Count == 0 && blocked.Observed == 2 && blocked.Dropped == 2, "byte refusal retains no synthetic row");
        bytes = 0;
        var pages = new TimedJoinValues<int>(1025, value => { bytes += value; return true; });
        for (int i = 0; i < 1026; i++) pages.Add(i);
        Check(pages.Rows().Last() == 1024 && pages.Count == 1025 && pages.Dropped == 1, "page boundary does not wrap");
        Check(bytes == 1025 * sizeof(int), "page byte accounting matches allocated values");
        var recorder = new TimedJoinRecorder(4);
        var key = new ShotKey(17, 2, 0, 1, 3, 9);
        var outcome = new CombatAckEntry { ShotId = 9, VictimSlot = 1, VictimGeneration = 2,
            VictimLife = 4, DamageApplied = 10, HealthAfter = 90, DamageSequence = 70000,
            Flags = CombatAckFlags.OutcomePresent };
        var copied = TimedJoinOutcome.From(outcome);
        var payment = new ShotPaymentKey(key, 1, 2, 4, 1, 1);
        recorder.Payments.Add(new(recorder.Next(), 100, 101, payment, 0, TimedJoinLane.Native, 0, copied, 0, false, null));
        outcome.HealthAfter = 0; outcome.DamageSequence = 1;
        Check(recorder.Payments.Rows().Single().Outcome.HealthAfter == 90
            && recorder.Payments.Rows().Single().Outcome.DamageSequence == 70000, "full outcome is immutable copied value");
        recorder.Payments.Add(new(recorder.Next(), 100, 102, payment, 0, TimedJoinLane.Correction, 6,
            copied with { DamageApplied = 20, HealthAfter = 80 }, 0, true, null));
        Check(recorder.Payments.Rows().Count(value => !value.Correction) == 1
            && recorder.Payments.Rows().Last().Ordinal == 2, "ordered correction is distinct from a new payment");
        NetTimedSourceCapabilities.Reset();
        Check(recorder.Payments.Count == 2, "world capability reset cannot discard process-owned rows");
        string json = JsonSerializer.Serialize(recorder.Capture());
        Check(json.Contains("\"DamageSequence\":70000") && json.Contains("\"DamageApplied\":20"),
            "report preserves public-field outcome through value projection");
        Check(recorder.AllocatedBytes > 0 && recorder.AllocatedBytes <= TimedJoinRecorder.MaximumBytes,
            "actual pages obey total byte cap");
        Check(!NetTimedCampaignJoin.TryRequestKey(0, 6, out _), "no request correlation is invented outside scope");
        using (NetTimedCampaignJoin.ClaimScope.Enter(0, 6, key))
        {
            Check(NetTimedCampaignJoin.TryRequestKey(0, 6, out var actual) && actual == key, "incoming full key captured");
            using (NetTimedCampaignJoin.ClaimScope.Enter(0, 6, key with { LifeId = 99 }))
                Check(NetTimedCampaignJoin.TryRequestKey(0, 6, out actual) && actual.LifeId == 99,
                    "same claim/shot ids in another life cannot borrow prior key");
            Check(NetTimedCampaignJoin.TryRequestKey(0, 6, out actual) && actual == key, "nested scope restores prior full key");
        }
        Check(!NetTimedCampaignJoin.TryRequestKey(0, 6, out _), "request scope closes without retained live state");
        Console.WriteLine($"Timed campaign join: {_checks} checks passed (constructed values; native/UDP NOT_RUN).");
        return 0;
    }
}
