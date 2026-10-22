#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Core;

namespace MphRead.Droid;

/// <summary>Dispatches one exact rule save to the existing UI owner, without blocking EGL.</summary>
internal static class RmlUiAndroidOfflineRulesWork
{
    internal static async Task<RmlUiOfflineRulesSaveResult> Run(Action<Action> post, Func<bool> admitted,
        Func<RmlUiOfflineRulesChange, CancellationToken, RmlUiOfflineRulesSaveResult> perform,
        Action<RmlUiOfflineRules?> publish, RmlUiOfflineRulesChange change, CancellationToken token,
        int deadlineMilliseconds = 10000, Func<RmlUiOfflineRules?>? readCurrent = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(deadlineMilliseconds);
        var cancellation = deadline.Token;
        cancellation.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<RmlUiOfflineRulesSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellation.Register(() => completion.TrySetCanceled(cancellation));
        int invoked = 0;
        RmlUiOfflineRulesSaveResult Unverified() => new(change, false,
            "RULE SAVE OUTCOME UNVERIFIED // REOPEN RULES TO VERIFY; A COMMITTED WRITE IS NOT UNDONE", null);
        RmlUiOfflineRules? ReadAfterFailure()
        {
            try { return admitted() ? readCurrent?.Invoke() : null; }
            catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException)) { return null; }
        }
        void Apply()
        {
            if (Interlocked.Exchange(ref invoked, 1) != 0 || cancellation.IsCancellationRequested) return;
            var result = new RmlUiOfflineRulesSaveResult(change, false,
                "RULE SAVE REFUSED // ACTIVE OR RETIRED SETTINGS OWNER", null);
            try
            {
                if (admitted() && !cancellation.IsCancellationRequested) result = perform(change, cancellation);
                if (result.Change != change) result = Unverified() with { Current = ReadAfterFailure() };
                // The write may already be committed when cancellation wins
                // the task race. Publish copied owner readback independently;
                // retiring a modal must never restore the old cached rules.
                publish(result.Current);
            }
            catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
            { result = Unverified() with { Current = ReadAfterFailure() }; publish(result.Current); }
            completion.TrySetResult(result);
        }
        try { post(Apply); }
        catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
        { publish(null); completion.TrySetResult(Unverified()); }
        return await completion.Task.ConfigureAwait(false);
    }
}
#endif
