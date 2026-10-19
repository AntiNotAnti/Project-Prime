#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Core;

namespace MphRead.Droid;

/// <summary>Nonblocking dispatch to the existing UI save owner; no lifecycle, serialization or file authority.</summary>
internal static class RmlUiAndroidSaveWork
{
    internal static async Task<RmlUiAdventureSaveResult> Run(Action<Action> post, Func<bool> admitted,
        Func<RmlUiAdventureSaveChange, CancellationToken, RmlUiAdventureSaveResult> perform,
        RmlUiAdventureSaveChange change, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<RmlUiAdventureSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        int invoked = 0;
        post(() =>
        {
            if (Interlocked.Exchange(ref invoked, 1) != 0 || token.IsCancellationRequested) return;
            var result = new RmlUiAdventureSaveResult(change, false, "SAVE CHANGE REFUSED // ACTIVE OR RETIRED SESSION; REFRESH SAVES");
            try
            {
                if (admitted() && !token.IsCancellationRequested) result = perform(change, token);
            }
            catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
            { result = new(change, false, "SAVE CHANGE OUTCOME UNVERIFIED // REFRESH SAVES; A COMMITTED WRITE IS NOT UNDONE"); }
            completion.TrySetResult(result);
        });
        return await completion.Task.ConfigureAwait(false);
    }
}
#endif
