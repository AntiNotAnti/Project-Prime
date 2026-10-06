#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Threading;
using System.Threading.Tasks;
using ProjectPrime.Studio.Protocol;

namespace MphRead.Mods.StudioIntegration;

/// <summary>A cancellable queue entry whose resources remain owned until actual owner execution finishes.</summary>
internal sealed class GameStudioOwnerWork
{
    private readonly Func<StudioGameResult> _action;
    private readonly CancellationToken _token;
    private readonly TaskCompletionSource<StudioGameResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenRegistration _registration;
    // Pending cancellation and owner execution race to claim the entry exactly once.
    private int _state;

    internal GameStudioOwnerWork(Func<StudioGameResult> action, CancellationToken token)
    {
        _action = action;
        _token = token;
        _registration = token.UnsafeRegister(static value => ((GameStudioOwnerWork)value!).CancelPending(), this);
    }

    internal Task<StudioGameResult> Completion => _completion.Task;

    private void CancelPending()
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            _completion.TrySetCanceled(_token);
    }

    internal void CancelPendingAndRelease()
    {
        CancelPending();
        _registration.Dispose();
    }

    internal void Execute()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            _registration.Dispose();
            return;
        }
        try
        {
            _token.ThrowIfCancellationRequested();
            _completion.TrySetResult(_action());
        }
        catch (OperationCanceledException) { _completion.TrySetCanceled(_token); }
        catch (Exception ex) { _completion.TrySetException(ex); }
        finally
        {
            Volatile.Write(ref _state, 2);
            _registration.Dispose();
        }
    }
}
#endif
