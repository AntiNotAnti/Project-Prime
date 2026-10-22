#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;
using System.Threading;

namespace MphRead.Droid;

internal enum RmlUiAndroidLocalPhase { Home, Loading, Match, ReturnRequested, Retiring, AwaitingHomeAck, HomeAccepted, Faulted, Stopped }
internal sealed record RmlUiAndroidLocalWitness(Guid Lifetime, long Cycle, long Match, RmlUiAndroidLocalPhase Phase);

/// <summary>One View/context, with copied UI acknowledgements and EGL-only decisions.</summary>
internal sealed class RmlUiAndroidLocalCycle<THome> where THome : class
{
    private readonly object _gate = new();
    private RmlUiAndroidLocalWitness _current = new(Guid.NewGuid(), 1, 0, RmlUiAndroidLocalPhase.Home);
    private THome? _home;
    private int _owner;
    internal RmlUiAndroidLocalWitness Current => Volatile.Read(ref _current);
    internal bool IsCurrent(RmlUiAndroidLocalWitness witness) => ReferenceEquals(Current, witness);
    internal bool TryBeginMatch(out RmlUiAndroidLocalWitness witness)
    {
        Owner(); lock (_gate)
        {
            witness = Current;
            if (witness.Phase != RmlUiAndroidLocalPhase.Home) return false;
            Publish(witness with { Match = witness.Match + 1, Phase = RmlUiAndroidLocalPhase.Loading });
            witness = Current; return true;
        }
    }
    internal bool MarkMatchReady(RmlUiAndroidLocalWitness loading)
    {
        Owner(); lock (_gate)
        {
            if (!ReferenceEquals(Current, loading) || loading.Phase != RmlUiAndroidLocalPhase.Loading) return false;
            Publish(loading with { Phase = RmlUiAndroidLocalPhase.Match }); return true;
        }
    }
    internal bool TryRequestReturn(RmlUiAndroidLocalWitness match)
    {
        lock (_gate)
        {
            var now = Current;
            if (now.Phase != RmlUiAndroidLocalPhase.Match || match.Lifetime != now.Lifetime
                || match.Cycle != now.Cycle || match.Match != now.Match) return false;
            Publish(now with { Phase = RmlUiAndroidLocalPhase.ReturnRequested }); return true;
        }
    }
    internal bool TryRetire(Func<bool> retire, out RmlUiAndroidLocalWitness witness)
    {
        Owner(); ArgumentNullException.ThrowIfNull(retire);
        lock (_gate)
        {
            witness = Current;
            if (witness.Phase != RmlUiAndroidLocalPhase.ReturnRequested) return false;
            Publish(witness with { Phase = RmlUiAndroidLocalPhase.Retiring });
        }
        bool complete;
        try { complete = retire(); }
        catch { Fault(); throw; }
        lock (_gate)
        {
            if (Current.Phase == RmlUiAndroidLocalPhase.Stopped) { witness = Current; return false; }
            Publish(Current with { Phase = complete ? RmlUiAndroidLocalPhase.AwaitingHomeAck : RmlUiAndroidLocalPhase.Faulted });
            witness = Current; return complete;
        }
    }
    internal bool TryAcknowledgeHome(RmlUiAndroidLocalWitness witness, THome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        lock (_gate)
        {
            if (!ReferenceEquals(Current, witness) || witness.Phase != RmlUiAndroidLocalPhase.AwaitingHomeAck) return false;
            _home = home; Publish(witness with { Phase = RmlUiAndroidLocalPhase.HomeAccepted }); return true;
        }
    }
    internal bool TryTakeHome(out THome? home)
    {
        Owner(); lock (_gate)
        {
            home = null;
            if (Current.Phase != RmlUiAndroidLocalPhase.HomeAccepted || _home == null) return false;
            home = _home; _home = null;
            Publish(Current with { Cycle = Current.Cycle + 1, Phase = RmlUiAndroidLocalPhase.Home }); return true;
        }
    }
    internal void Stop() { lock (_gate) { _home = null; Publish(Current with { Phase = RmlUiAndroidLocalPhase.Stopped }); } }
    internal void Fault() { lock (_gate) { if (Current.Phase != RmlUiAndroidLocalPhase.Stopped) { _home = null; Publish(Current with { Phase = RmlUiAndroidLocalPhase.Faulted }); } } }
    private void Publish(RmlUiAndroidLocalWitness witness) => Volatile.Write(ref _current, witness);
    private void Owner()
    {
        int current = Environment.CurrentManagedThreadId;
        if (_owner == 0) _owner = current;
        else if (_owner != current) throw new InvalidOperationException("Local cycle decisions belong to the existing EGL owner.");
    }
}

internal interface IRmlUiAndroidLocalRetirement
{
    void ReleaseInput();
    bool RetireNative();
    void CaptureCommit();
    void CleanupScene();
    void UnloadScene();
    void Commit();
    bool ReleaseForeground();
    void ResetPresentation();
}

/// <summary>Uses the existing cleanup/save owners; failure never authorizes a fresh Home.</summary>
internal static class RmlUiAndroidLocalRetirement
{
    internal static bool Run(IRmlUiAndroidLocalRetirement owner, Action<Exception> failure)
    {
        bool success = true;
        bool Attempt(Action action)
        {
            try { action(); return true; }
            catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
            { success = false; failure(error); return false; }
        }
        Attempt(owner.ReleaseInput);
        Attempt(() => { if (!owner.RetireNative()) throw new InvalidOperationException("Native UI did not retire safely."); });
        bool captured = Attempt(owner.CaptureCommit);
        bool cleaned = Attempt(owner.CleanupScene);
        bool unloaded = Attempt(owner.UnloadScene);
        bool committed = captured && Attempt(owner.Commit);
        if (cleaned && unloaded && committed)
            Attempt(() => { if (!owner.ReleaseForeground()) throw new InvalidOperationException("The old scene still owns foreground state."); });
        Attempt(owner.ResetPresentation);
        return success;
    }
}
#endif
