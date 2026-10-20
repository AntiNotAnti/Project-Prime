using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

// Trusted server-owned per-peer dependency. The live bridge checks actual peer
// admission and an engine-consumed post-ready input; WorldIsReady is insufficient.
internal readonly record struct CustomSourceReadyContext(CustomWeaponReady Ready,uint OpenedWorld,
    uint FirstSourceFrame,ulong PeerIncarnation);
internal interface ICustomSourceReadiness
{
    bool TryGet(WeaponOwnerContext owner,out CustomSourceReadyContext context);
}

/// <summary>Developer remote-input admission using actual engine observations.
/// Readiness is supplied by either the explicit fixture or live peer/source bridge.
/// No native shot identity, claimed damage or current pose fallback.</summary>
internal sealed class CustomEngineSourceAdmission : ICustomSourceAdmission
{
    private readonly CustomAuthorityCombatAdapter _root;
    private readonly ICustomSourceReadiness _readiness;
    private readonly CustomSourceReadyContext?[] _pinned = new CustomSourceReadyContext?[8];
    private readonly WeaponOwnerContext[] _occupants = new WeaponOwnerContext[8];
    private readonly bool[] _invalidated = new bool[8];
    internal CustomEngineSourceAdmission(CustomAuthorityCombatAdapter root,ICustomSourceReadiness readiness)
    { _root = root; _readiness = readiness; }
    private static bool Before(uint a,uint b) => unchecked((int)(a-b)) < 0;
    private static bool SameOccupant(WeaponOwnerContext a,WeaponOwnerContext b)
        => a.MatchId == b.MatchId && a.AuthorityEpoch == b.AuthorityEpoch && a.Slot == b.Slot && a.Generation == b.Generation;
    private bool Valid(WeaponOwnerContext owner,in CustomSourceReadyContext context)
        => context.PeerIncarnation != 0 && context.Ready.IsValid && context.Ready.Manifest == _root.Manifest.Digest
            && context.Ready.Slot == owner.Slot && context.Ready.Bootstrap.SlotGeneration == owner.Generation
            && context.Ready.Bootstrap.Start.MatchId == owner.MatchId
            && context.Ready.Bootstrap.Start.AuthorityEpoch == owner.AuthorityEpoch
            && !Before(context.OpenedWorld,context.Ready.Bootstrap.AuthorityFrame)
            && !Before(NetSession.NetFrame,context.OpenedWorld);
    internal bool CanCapture(WeaponOwnerContext owner)
    {
        if (!_root.IsEligible || !owner.IsValid || !_readiness.TryGet(owner,out var context)) return false;
        int slot = owner.Slot;
        if (!SameOccupant(owner,_occupants[slot]))
        { _occupants[slot] = owner; _pinned[slot] = null; _invalidated[slot] = false; }
        if (_invalidated[slot]) return false;
        if (_pinned[slot] is { } pinned && pinned != context)
        { _invalidated[slot] = true; return false; }
        if (!Valid(owner,context)) return false;
        _pinned[slot] = context; return true;
    }
    public CustomSourceDecision Inspect(in CustomWeaponAction action,out CustomSourceCertificate certificate)
    {
        certificate = default;
        if (!_root.IsEligible || !action.IsValid || action.Manifest != _root.Manifest.Digest
            || action.Weapon.Value != 10 || !_root.IsCurrentSourceOwner(action.Owner)) return CustomSourceDecision.Rejected;
        int slot = action.Slot;
        if (_invalidated[slot] || !SameOccupant(action.Owner,_occupants[slot])) return CustomSourceDecision.Rejected;
        if (_pinned[slot] is not { } pinned || !_readiness.TryGet(action.Owner,out var current)) return CustomSourceDecision.Pending;
        if (current != pinned || !Valid(action.Owner,current))
        { _invalidated[slot] = true; return CustomSourceDecision.Rejected; }
        if (action.Bootstrap != pinned.Ready.Bootstrap || Before(action.SourceFrame,pinned.FirstSourceFrame))
            return CustomSourceDecision.Rejected;
        var status = _root.ReadSourceObservation(action.Owner,action.SourceFrame,out var sample);
        if (status != CustomSourceObservationStatus.Available)
            return status == CustomSourceObservationStatus.Missing ? CustomSourceDecision.Pending : CustomSourceDecision.Rejected;
        if (Before(sample.ObservedWorldFrame,pinned.OpenedWorld)) return CustomSourceDecision.Rejected;
        if(!_root.AdmitsPickupAction(action,pinned.PeerIncarnation,sample.ObservedWorldFrame))return CustomSourceDecision.Rejected;
        if (action.Kind != CustomWeaponActionKind.Fire)
        {
            certificate = new(action,sample.ObservedWorldFrame,sample.ObservedWorldFrame,0,
                sample.Body,sample.Aim,sample.CanUseWeapon,false,false,sample.Crouched);
            return CustomSourceDecision.Verified;
        }
        uint now = NetSession.NetFrame;
        if (!sample.CanUseWeapon || action.AckFrame == 0 || action.AckFrame != sample.Input.AckFrame
            || action.AckSubFrame != sample.Input.AckSubFrame || Before(sample.ObservedWorldFrame,action.AckFrame)
            || action.AckFrame == sample.ObservedWorldFrame && action.AckSubFrame != 0
            || Before(action.AckFrame,pinned.Ready.Bootstrap.AuthorityFrame)
            || Before(now,action.AckFrame) || unchecked(now-action.AckFrame) > CustomSourceActionJournal.MaxWorldAge
            || unchecked(sample.ObservedWorldFrame-action.AckFrame) > CustomSourceActionJournal.MaxWorldAge
            || Vector3.Dot(action.Direction.Normalized(),sample.Aim) < .99f
            || (action.Origin-sample.MuzzleOrigin).LengthSquared > .0225f
            || !sample.Modifiers.Matches(action.Owner,action.SourceFrame,sample.ObservedWorldFrame)) return CustomSourceDecision.Rejected;
        // A reliable custom control is the primary trigger, bound internally to
        // this exact consumed source. Raw source-referenced actions retain the
        // preceding physical-level proof; packets cannot assert the witness.
        if (!_root.HasControlWitness(action))
        {
            if ((sample.Input.InputBits & (uint)IntentButtons.Shoot)==0) return CustomSourceDecision.Rejected;
            var priorStatus = _root.ReadSourceObservation(action.Owner,unchecked(action.SourceFrame-1),out var prior);
            if (priorStatus != CustomSourceObservationStatus.Available)
                return priorStatus == CustomSourceObservationStatus.Missing ? CustomSourceDecision.Pending : CustomSourceDecision.Rejected;
            if ((prior.Input.InputBits & (uint)IntentButtons.Shoot) != 0
                || Before(prior.ObservedWorldFrame,pinned.OpenedWorld)) return CustomSourceDecision.Rejected;
        }
        // Validate actual emitted muzzle, not only the unused claimed origin.
        // The history uses the engine's sampled-world geometry contract; it is
        // not a retained copy of the earlier input-phase door state.
        var scene = _root.SourceScene;
        uint world = sample.ObservedWorldFrame;
        if (!NetDynamicGeometryHistory.HasHistoricalFrame(scene,world)) return CustomSourceDecision.Pending;
        Vector3 start = sample.TraceOrigin, muzzle = sample.MuzzleOrigin;
        if (!NetDynamicGeometryHistory.TryTraceDistanceAtFrame(scene,start,muzzle,world,out float distance)
            || distance < .999f) return CustomSourceDecision.Rejected;
        certificate = new(action,sample.ObservedWorldFrame,action.AckFrame,action.AckSubFrame,
            sample.Body,sample.Aim,true,sample.Modifiers.DoubleKnowledge == TimedGrantKnowledge.Granted,false,sample.Crouched);
        return CustomSourceDecision.Verified;
    }
}
