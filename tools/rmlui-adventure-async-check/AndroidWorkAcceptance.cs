using MphRead.Droid;
using MphRead.Mods.Launcher.RmlUi.Core;

internal static class AndroidWorkAcceptance
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool ok, string label) { checks++; if (!ok) throw new InvalidOperationException(label); }
        var change = new RmlUiAdventureSaveChange(RmlUiAdventureSaveAction.Copy,
            new(1, true, false, "SYNTHETIC", 1, 99, 99, new string('A', 64)),
            new(2, false, false, "", 0, 0, 0, "EMPTY"));
        var queue = new Queue<Action>();
        int writes = 0;
        bool admitted = true;
        RmlUiAdventureSaveResult Perform(RmlUiAdventureSaveChange captured, CancellationToken token)
        { token.ThrowIfCancellationRequested(); writes++; return new(captured, true, "SYNTHETIC VERIFIED"); }
        using var canceled = new CancellationTokenSource();
        var pending = RmlUiAndroidSaveWork.Run(queue.Enqueue, () => admitted, Perform, change, canceled.Token);
        Check(!pending.IsCompleted && writes == 0, "posting UI save command cannot block or claim early success");
        canceled.Cancel(); queue.Dequeue()();
        try { pending.GetAwaiter().GetResult(); throw new InvalidOperationException("canceled queued work unexpectedly completed"); }
        catch (OperationCanceledException) { Check(writes == 0, "cancellation before UI callback prevents writer invocation"); }
        var stale = RmlUiAndroidSaveWork.Run(queue.Enqueue, () => admitted, Perform, change, CancellationToken.None);
        admitted = false; queue.Dequeue()();
        Check(!stale.GetAwaiter().GetResult().Success && writes == 0, "stale view/lifetime/admission gate refuses queued UI work");
        admitted = true;
        var valid = RmlUiAndroidSaveWork.Run(queue.Enqueue, () => admitted, Perform, change, CancellationToken.None);
        var callback = queue.Dequeue(); callback(); callback();
        Check(valid.GetAwaiter().GetResult() is { Success: true } result && result.Change == change && writes == 1,
            "current admitted UI callback executes exact captured command once even when dispatch repeats");
        using var preflight = new CancellationTokenSource();
        var duringPreflight = RmlUiAndroidSaveWork.Run(queue.Enqueue, () => { preflight.Cancel(); return true; }, Perform, change, preflight.Token);
        queue.Dequeue()();
        try { duringPreflight.GetAwaiter().GetResult(); throw new InvalidOperationException("preflight cancellation unexpectedly completed"); }
        catch (OperationCanceledException) { Check(writes == 1, "token canceled during admission is checked before writer"); }
        var failure = RmlUiAndroidSaveWork.Run(queue.Enqueue, () => true, (_, _) => throw new IOException("PRIVATE FILE PATH"), change, CancellationToken.None);
        queue.Dequeue()(); var failed = failure.GetAwaiter().GetResult();
        Check(!failed.Success && failed.Status.Contains("UNVERIFIED") && !failed.Status.Contains("PRIVATE"),
            "writer failure exposes conservative fixed diagnostic rather than private exception details");
        using var afterEffect = new CancellationTokenSource();
        var committed = RmlUiAndroidSaveWork.Run(queue.Enqueue, () => true, (captured, _) =>
        { writes++; afterEffect.Cancel(); return new(captured, true, "SYNTHETIC VERIFIED"); }, change, afterEffect.Token);
        queue.Dequeue()();
        try { committed.GetAwaiter().GetResult(); throw new InvalidOperationException("retired effect unexpectedly published success"); }
        catch (OperationCanceledException) { Check(writes == 2, "post-effect cancellation retires UI authority and does not undo or repeat writer"); }
        Console.WriteLine($"PASS {checks} Android async save dispatch checks (real stateless dispatcher source, fake UI queue/admission/writer; no user files or Android runtime).");
    }
}
