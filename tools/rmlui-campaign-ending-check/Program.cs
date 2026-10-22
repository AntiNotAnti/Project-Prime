using MphRead.Mods.Launcher.Core;

int checks = 0;
void Check(bool value, string label)
{
    if (!value) throw new InvalidOperationException(label);
    checks++;
}

// Synthetic detached receipts only: no scene, cartridge, save, account or audio backend is loaded.
var complete = new NativeCampaignEndingReport(2, true, true, 93, 8, 12, 34, 567);
using (var controller = new NativeCampaignEndingController(complete))
{
    var initial = controller.Snapshot();
    Check(initial.Report == complete, "snapshot preserves the exact captured receipt");
    Check(initial.Revision == 1 && !initial.Closed, "initial summary is open");
    Check(controller.Snapshot() == initial, "unchanged reads retain revision");
    Check(!controller.TryTakeReturn(out var missing) && missing == default, "opening never requests navigation");
    Check(controller.Continue(), "continue captures return");
    Check(controller.Snapshot().Closed && controller.Snapshot().Revision == 2, "return closes once");
    Check(controller.Snapshot().Report == initial.Report, "return cannot mutate ending values");
    Check(!controller.Back() && !controller.Continue(), "duplicate actions cannot overwrite pending receipt");
    Check(controller.TryTakeReturn(out var receipt) && receipt == complete, "command uses captured receipt");
    Check(!controller.TryTakeReturn(out _), "command drains once");
    Check(!controller.Back() && !controller.Continue(), "drained command cannot be recreated");
    Check(controller.Snapshot().Revision == 2, "duplicate actions do not advance revision");
}

var incomplete = new NativeCampaignEndingReport(3, false, false, null, 6, 42, 17, 1234);
using (var controller = new NativeCampaignEndingController(incomplete))
{
    Check(controller.Snapshot().Report == incomplete, "incomplete receipt and unavailable percent preserved");
    Check(controller.Back(), "back captures same return as continue");
    Check(controller.TryTakeReturn(out var receipt) && receipt == incomplete, "back returns exact incomplete receipt");
    Check(receipt.CompletionPercent == null && !receipt.Complete && !receipt.SaveCommitted,
        "no success, percentage or save commit is invented");
    Check(!controller.TryTakeReturn(out _), "back drains once");
}

// Values are reported, not recalculated or sanitized into different campaign facts.
var extremes = new NativeCampaignEndingReport(1, false, true, 0, 0, uint.MaxValue, 0, uint.MaxValue);
using (var controller = new NativeCampaignEndingController(extremes))
{
    Check(controller.Snapshot().Report == extremes, "zero and unsigned totals round-trip exactly");
    Check(controller.Back(), "extreme values do not affect navigation");
    Check(controller.TryTakeReturn(out var receipt) && receipt == extremes, "no totals overflow or coercion");
}

var cancelled = new NativeCampaignEndingController(complete);
cancelled.Continue();
cancelled.Dispose();
Check(!cancelled.TryTakeReturn(out _), "disposing discards unconsumed return");
Check(!cancelled.Back() && !cancelled.Continue(), "disposed owner cannot navigate");
cancelled.Dispose();
Check(cancelled.Snapshot().Report == complete, "disposal never mutates captured data");

using (var controller = new NativeCampaignEndingController(complete))
{
    void OwnerOnly(Action action, string label)
    {
        Exception? observed = null;
        var worker = new Thread(() => { try { action(); } catch (Exception error) { observed = error; } });
        worker.Start(); worker.Join();
        Check(observed is InvalidOperationException, label);
    }
    OwnerOnly(() => controller.Snapshot(), "snapshot requires owner thread");
    OwnerOnly(() => controller.Continue(), "continue requires owner thread");
    OwnerOnly(() => controller.Back(), "back requires owner thread");
    OwnerOnly(() => controller.TryTakeReturn(out _), "command drain requires owner thread");
    OwnerOnly(controller.Dispose, "disposal requires owner thread");
    Check(!controller.Snapshot().Closed, "wrong-thread attempts leave owner state unchanged");
    Check(controller.Continue() && controller.TryTakeReturn(out var receipt) && receipt == complete,
        "owner still navigates after rejected foreign calls");
}

Console.WriteLine($"PASS {checks} campaign ending checks (synthetic immutable receipts; no game assets or save writes).");
