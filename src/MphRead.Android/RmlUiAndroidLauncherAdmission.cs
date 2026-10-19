#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;

namespace MphRead.Droid;

internal enum RmlUiAndroidLauncherAdmissionState { Open, AwaitingUi, Accepted, Retired }
internal sealed record RmlUiAndroidLauncherAdmissionSnapshot(Guid Lifetime, long Generation,
    RmlUiAndroidLauncherAdmissionState State);
internal readonly record struct RmlUiAndroidLauncherRequest<T>(Guid Lifetime, long Generation, T Request) where T : struct;

/// <summary>One copied local request, asynchronously admitted by the existing UI launcher.</summary>
internal sealed class RmlUiAndroidLauncherAdmission<TRequest, TPlan> where TRequest : struct where TPlan : struct
{
    private readonly object _gate = new();
    private RmlUiAndroidLauncherAdmissionSnapshot _current = new(Guid.NewGuid(), 1, RmlUiAndroidLauncherAdmissionState.Open);
    private RmlUiAndroidLauncherRequest<TRequest> _request;
    private TPlan _plan;
    private int _owner;
    internal RmlUiAndroidLauncherAdmissionSnapshot Current { get { lock (_gate) return _current; } }
    internal bool TryRequest(TRequest request, out RmlUiAndroidLauncherRequest<TRequest> witness)
    {
        Owner();
        lock (_gate)
        {
            witness = default;
            if (_current.State != RmlUiAndroidLauncherAdmissionState.Open) return false;
            _current = _current with { Generation = _current.Generation + 1, State = RmlUiAndroidLauncherAdmissionState.AwaitingUi };
            witness = _request = new(_current.Lifetime, _current.Generation, request);
            return true;
        }
    }
    internal bool IsCurrent(RmlUiAndroidLauncherRequest<TRequest> witness)
    {
        lock (_gate) return Matches(witness);
    }
    internal bool TryAccept(RmlUiAndroidLauncherRequest<TRequest> witness, TPlan plan)
    {
        lock (_gate)
        {
            if (!Matches(witness)) return false;
            _plan = plan; _current = _current with { State = RmlUiAndroidLauncherAdmissionState.Accepted };
            return true;
        }
    }
    internal bool TryTakePlan(out TPlan plan)
        => TryTakePlan(out plan, out _);
    private bool TryTakePlan(out TPlan plan, out TRequest request)
    {
        Owner();
        lock (_gate)
        {
            plan = default; request = default;
            if (_current.State != RmlUiAndroidLauncherAdmissionState.Accepted) return false;
            plan = _plan; request = _request.Request; RetireCore(); return true;
        }
    }
    internal bool TryTakePlan(Func<bool> retireOwner, out TPlan plan)
        => TryTakePlan(retireOwner, out plan, out _);
    internal bool TryTakePlan(Func<bool> retireOwner, out TPlan plan, out TRequest request)
    {
        ArgumentNullException.ThrowIfNull(retireOwner);
        if (!TryTakePlan(out TPlan accepted, out TRequest captured)) { plan = default; request = default; return false; }
        // The copied plan is never exposed while its native document/context
        // owner may still be live. A failed retirement consumes admission.
        if (!retireOwner()) { plan = default; request = default; return false; }
        plan = accepted; request = captured; return true;
    }
    internal void Retire() { lock (_gate) RetireCore(); }
    private bool Matches(RmlUiAndroidLauncherRequest<TRequest> witness)
        => _current.State == RmlUiAndroidLauncherAdmissionState.AwaitingUi && _request.Equals(witness)
            && witness.Lifetime == _current.Lifetime && witness.Generation == _current.Generation;
    private void RetireCore()
    {
        _plan = default; _request = default;
        _current = _current with { Generation = _current.Generation + 1, State = RmlUiAndroidLauncherAdmissionState.Retired };
    }
    private void Owner()
    {
        int current = Environment.CurrentManagedThreadId;
        if (_owner == 0) _owner = current;
        else if (_owner != current) throw new InvalidOperationException("Native launcher decisions belong to the existing EGL owner.");
    }
}

// Activity recreation must not let the new Activity's legacy preview create a
// pbuffer while an older native GameView still owns EGL/scene teardown.
internal static class RmlUiAndroidLauncherPreviewBarrier
{
    private static readonly object Gate = new();
    private static Guid? _owner;
    internal static bool Blocked { get { lock (Gate) return _owner.HasValue; } }
    internal static bool IsOwned(Guid owner) { lock (Gate) return _owner == owner; }
    internal static bool TryAcquire(Guid owner)
    {
        lock (Gate)
        {
            if (_owner.HasValue) return false;
            _owner = owner; return true;
        }
    }
    internal static void Release(Guid owner) { lock (Gate) if (_owner == owner) _owner = null; }
}
#endif
