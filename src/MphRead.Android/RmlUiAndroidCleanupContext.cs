#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;

namespace MphRead.Droid;

internal interface IRmlUiAndroidCleanupContext
{
    bool CurrentMatchesOwner { get; }
    bool HasForeignCurrentContext { get; }
    bool TryBindSurfaceless();
    bool TryCreatePbuffer();
    bool TryBindPbuffer();
    void UnbindTemporary();
    void DestroyPbuffer();
}

/// <summary>Only binds the caller's existing context, never creates another owner.</summary>
internal static class RmlUiAndroidCleanupContext
{
    internal static bool Run(IRmlUiAndroidCleanupContext context, Func<bool, bool> retire, Action<Exception> failure)
    {
        bool valid = false, temporary = false, success = true, retired = false;
        void Attempt(Action action)
        {
            try { action(); }
            catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
            { success = false; failure(error); }
        }
        try
        {
            Attempt(() =>
            {
                if (context.CurrentMatchesOwner) { valid = true; return; }
                if (context.HasForeignCurrentContext) throw new InvalidOperationException("Another EGL context is current on the cleanup thread.");
                temporary = true;
                if (!context.TryBindSurfaceless() && (!context.TryCreatePbuffer() || !context.TryBindPbuffer()))
                    throw new InvalidOperationException("The existing EGL context cannot be bound for scene cleanup.");
                valid = context.CurrentMatchesOwner;
                if (!valid) throw new InvalidOperationException("Cleanup did not bind the exact existing EGL context.");
            });
            // Non-GL cleanup/save/native retirement must still be attempted
            // when binding fails. The adapter refuses UnloadGl without valid
            // context identity, preventing foreground release and fresh Home.
            Attempt(() => retired = retire(valid));
        }
        finally
        {
            if (temporary)
            {
                try { Attempt(context.UnbindTemporary); }
                finally { Attempt(context.DestroyPbuffer); }
            }
        }
        return success && valid && retired;
    }
}
#endif
