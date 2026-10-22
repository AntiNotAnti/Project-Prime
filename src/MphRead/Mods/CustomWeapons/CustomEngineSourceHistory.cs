using MphRead.Mods.Movement;
using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

// Engine placement/control observations only. Neither type is a client certificate
// or admission verdict; omitted source frames are never synthesized.
internal readonly record struct CustomSourceInput(WeaponOwnerContext Owner,uint Frame,
    uint AckFrame,byte AckSubFrame,Vector3 ReportedBody,Vector3 ReportedAim,uint InputBits,CustomResourceKnowledge Knowledge=default,uint IngressWorld=0)
{
    internal bool IsValid => Owner.IsValid && Knowledge.IsValid && Finite(ReportedBody) && Finite(ReportedAim)
        && ReportedAim.LengthSquared > 1e-8f && float.IsFinite(ReportedAim.LengthSquared);
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)
        && MathF.Abs(p.X) <= 1000000 && MathF.Abs(p.Y) <= 1000000 && MathF.Abs(p.Z) <= 1000000;
}
internal readonly record struct CustomEngineObservation(CustomSourceInput Input,uint ObservedWorldFrame,
    Vector3 Body,Vector3 Aim,bool CanUseWeapon,CustomSourceModifierEvidence Modifiers = default, bool Crouched = false, bool Tucked = false)
{
    internal Vector3 MuzzleOrigin => Body + Vector3.UnitY * (TraversalStance.CustomMuzzleOffset(Crouched) + (Crouched && Tucked ? ParkourStance.TuckRise : 0));
    internal Vector3 TraceOrigin => Body + Vector3.UnitY * (TraversalStance.CustomTraceOffset(Crouched) + (Crouched && Tucked ? ParkourStance.TuckRise : 0));
}
internal enum CustomSourceObservationStatus : byte { Missing,Rejected,Available }
internal enum CustomObservationResult : byte { Captured,Duplicate,Conflict,Unavailable,Invalid }

/// <summary>Bounded first engine observation, keyed by complete life and exact
/// source frame. A repeated input cannot refresh its age or recapture eligibility.
/// Contradictory repeated input permanently invalidates that retained source cell.</summary>
internal sealed class CustomEngineSourceHistory
{
    internal const int Capacity = 128;
    private readonly record struct Entry(CustomEngineObservation Observation,bool HasPose,bool Conflict);
    private readonly Entry?[] _entries = new Entry?[Capacity];
    private readonly WeaponOwnerContext _owner;
    private readonly CustomSourceClock _clock;
    private readonly uint _birth;
    private uint _frontier,_world;
    internal CustomEngineSourceHistory(WeaponOwnerContext owner,CustomSourceClock clock,uint birth,uint world)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (!owner.IsValid) throw new ArgumentException("Valid owner required.",nameof(owner));
        _owner = owner; _clock = clock; _birth = _frontier = birth; _world = world;
    }
    internal CustomObservationResult Observe(in CustomSourceInput input,in CustomPrecombatFacts facts,uint world)
        => ObserveCore(input,facts,world);
    internal CustomObservationResult ObserveDenied(in CustomSourceInput input,uint world)
        => ObserveCore(input,null,world);
    private CustomObservationResult ObserveCore(in CustomSourceInput input,CustomPrecombatFacts? facts,uint world)
    {
        if (input.Owner != _owner || !input.IsValid || unchecked((int)(world-_world)) < 0
            || unchecked((int)(input.Frame-_birth)) < 0) return CustomObservationResult.Invalid;
        if (unchecked((int)(_frontier-input.Frame)) >= Capacity) return CustomObservationResult.Unavailable;
        int index = (int)(input.Frame % Capacity);
        if (_entries[index] is { } prior && prior.Observation.Input.Frame == input.Frame)
        {
            _world = world;
            if (prior.Conflict || prior.Observation.Input != input)
            { _entries[index] = prior with { Conflict = true }; return CustomObservationResult.Conflict; }
            return CustomObservationResult.Duplicate;
        }
        if (facts.HasValue && !CustomSourceInputValidFacts(facts.Value) || !_clock.Observe(_owner,input.Frame,world))
            return CustomObservationResult.Invalid;
        _world = world;
        if (unchecked((int)(input.Frame-_frontier)) > 0) _frontier = input.Frame;
        _entries[index] = new(new(input,world,facts?.Body ?? default,facts?.Aim ?? default,facts?.CanUseWeapon ?? false,facts?.Modifiers ?? default,facts?.Crouched ?? false,facts?.Tucked ?? false),facts.HasValue,false);
        return CustomObservationResult.Captured;
    }
    private static bool CustomSourceInputValidFacts(in CustomPrecombatFacts facts)
        => (!facts.Tucked || facts.Crouched) && float.IsFinite(facts.Body.X) && float.IsFinite(facts.Body.Y) && float.IsFinite(facts.Body.Z)
            && MathF.Abs(facts.Body.X) <= 1000000 && MathF.Abs(facts.Body.Y) <= 1000000 && MathF.Abs(facts.Body.Z) <= 1000000
            && float.IsFinite(facts.Aim.LengthSquared) && MathF.Abs(facts.Aim.LengthSquared-1) <= .02f;
    internal bool TryGet(WeaponOwnerContext owner,uint source,uint now,out CustomEngineObservation observation)
        => Read(owner,source,now,out observation) == CustomSourceObservationStatus.Available;
    internal CustomSourceObservationStatus Read(WeaponOwnerContext owner,uint source,uint now,out CustomEngineObservation observation)
    {
        observation = default;
        if (owner != _owner || unchecked((int)(source-_birth)) < 0) return CustomSourceObservationStatus.Rejected;
        if (_entries[source % Capacity] is not { } entry || entry.Observation.Input.Frame != source)
            return unchecked((int)(_frontier-source)) >= Capacity ? CustomSourceObservationStatus.Rejected : CustomSourceObservationStatus.Missing;
        if (entry.Conflict || !entry.HasPose || unchecked((int)(now-entry.Observation.ObservedWorldFrame)) < 0
            || unchecked(now-entry.Observation.ObservedWorldFrame) > CustomSourceActionJournal.MaxWorldAge)
            return CustomSourceObservationStatus.Rejected;
        observation = entry.Observation; return CustomSourceObservationStatus.Available;
    }
    internal void Clear() => Array.Clear(_entries);
}
