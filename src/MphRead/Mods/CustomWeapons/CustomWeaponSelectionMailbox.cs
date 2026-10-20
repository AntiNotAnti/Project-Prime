using System;
using System.Threading;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

internal readonly record struct CustomWeaponSelectionContext(long SceneGeneration,
    WeaponOwnerContext Owner, WorldBootstrapIdentity Bootstrap, WeaponDigest Manifest, WeaponIdentity Weapon)
{
    internal bool IsValid => SceneGeneration > 0 && Owner.IsValid && Manifest.IsValid
        && Weapon.Kind == WeaponIdentityKind.Custom;
}
internal readonly record struct CustomWeaponSelectionState(CustomWeaponSelectionContext Context,
    bool Available, bool ActorReady, bool Selected, bool NativePending, bool NativeSuppressed);

/// <summary>Fixed-step publication; UI reads cannot advance inventory or networking.</summary>
internal sealed class CustomWeaponSelectionSnapshot(CustomWeaponSelectionState state)
{
    internal CustomWeaponSelectionState State { get; } = state;
    internal CustomWeaponSelectionContext Context => State.Context;
    internal bool Available => State.Available;
    internal bool Selected => State.Selected;
    internal bool NativePending => State.NativePending;
    internal bool CanEquip => Available && State.ActorReady && !Selected;
    internal bool CanReturnNative => Available && State.ActorReady && State.NativeSuppressed && !NativePending;
}

/// <summary>One coalescing desired selection. Only the input sink can issue a control.</summary>
internal sealed class CustomWeaponSelectionMailbox
{
    internal const long DeadlineMilliseconds = 2000;
    private readonly object _gate = new();
    private readonly record struct Request(CustomWeaponSelectionContext Context, bool Custom, long Deadline);
    private CustomWeaponSelectionSnapshot? _snapshot;
    private Request? _request;
    internal CustomWeaponSelectionSnapshot? Snapshot => Volatile.Read(ref _snapshot);
    internal void Publish(in CustomWeaponSelectionState state)
    {
        lock (_gate)
        {
            if (!state.Context.IsValid)
            { _request = null; Volatile.Write(ref _snapshot, null); return; }
            if (!state.Available || !state.ActorReady || _request is { } request && request.Context != state.Context)
                _request = null;
            if (_snapshot?.State != state) Volatile.Write(ref _snapshot, new(state));
        }
    }
    internal bool TryRequest(CustomWeaponSelectionSnapshot? expected, bool custom, long now)
    {
        lock (_gate)
        {
            if (expected == null || !ReferenceEquals(expected, _snapshot) || !expected.Available || !expected.State.ActorReady
                || !(custom ? expected.CanEquip : expected.CanReturnNative) && !_request.HasValue) return false;
            _request = new(expected.Context, custom, now + DeadlineMilliseconds);
            return true;
        }
    }
    internal bool TryTake(bool inputAllowed, bool menuTransition, bool nativeIntent, long now, out bool custom)
    {
        custom = false;
        lock (_gate)
        {
            if (_request is not { } request) return false;
            if (_snapshot is not { Available: true } current || !current.State.ActorReady
                || request.Context != current.Context || now > request.Deadline || nativeIntent)
            { _request = null; return false; }
            if (!inputAllowed)
            {
                // The menu button can resume asynchronously on Android. Hold
                // only that bounded transition; other restrictions consume it.
                if (!menuTransition) _request = null;
                return false;
            }
            _request = null; custom = request.Custom; return true;
        }
    }
    internal void Clear()
    {
        lock (_gate) { _request = null; Volatile.Write(ref _snapshot, null); }
    }
}
