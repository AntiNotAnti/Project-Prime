using System;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

// Only the actual native collision call can open this scope. No live beam,
// player, claimant point or mutable owner is retained in the value.
internal readonly record struct NativeImpactWitness(ShotPaymentKey Payment, byte Weapon,
    uint Frame, Vector3 Point, uint NativeChannel, bool NativeHead, Vector3 Origin,
    Vector3 SegmentStart, Vector3 SegmentEnd, bool Direct, bool OrdinaryDirect,
    CombatAckEntry Outcome = default, bool Completed = false)
{
    private static bool Finite(Vector3 value) => float.IsFinite(value.X)
        && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    internal bool Valid => Payment.Shot.AuthorityEpoch != 0 && Payment.Shot.MatchId != 0
        && (uint)Payment.Shot.ShooterSlot < 8 && Payment.Shot.Generation != 0
        && Payment.Shot.LifeId != 0 && Payment.Shot.ShotId != 0 && Payment.Component != 0
        && Payment.Victim < 8 && Payment.Generation != 0 && Payment.Life != 0
        && Payment.Kind is 1 or 2 or 5 or 6 && Weapon <= (byte)BeamType.OmegaCannon
        && NativeChannel != 0 && Finite(Point) && Finite(Origin)
        && Finite(SegmentStart) && Finite(SegmentEnd);
    internal NativeImpactWitness WithOutcome(CombatAckEntry outcome)
        => this with { Outcome = outcome, Completed = true };
}

internal static class NetNativeImpactWitness
{
    [ThreadStatic] private static NativeImpactWitness? _current;
    private readonly record struct Segment(ShotKey Key, uint Component, Vector3 Start, Vector3 End);
    [ThreadStatic] private static Segment? _segment;

    internal static bool TryCapture(out NativeImpactWitness witness)
    {
        witness = _current.GetValueOrDefault();
        return _current.HasValue && witness.Valid && !NetDamage.ApplyingClaim && !NetDamage.Replaying;
    }

    internal readonly struct Scope : IDisposable
    {
        private readonly NativeImpactWitness? _prior;
        private readonly bool _active;
        internal Scope(NativeImpactWitness? value) { _active = true; _prior = _current; _current = value; }
        public void Dispose() { if (_active) _current = _prior; }
    }

    internal readonly struct SegmentScope : IDisposable
    {
        private readonly Segment? _prior;
        private readonly bool _active;
        internal SegmentScope(BeamProjectileEntity beam, Vector3 start, Vector3 end)
        {
            _active = NetTimedShotProof.Active;
            _prior = _active ? _segment : null;
            if (_active) _segment = new Segment(beam.ModLaunchKey, beam.ModComponentId, start, end);
        }
        public void Dispose() { if (_active) _segment = _prior; }
    }

    internal static Scope Enter(PlayerEntity victim, BeamProjectileEntity beam,
        uint channel, DamageFlags flags, Vector3 point)
    {
        if (!NetTimedShotProof.Active) return default;
        if (beam.OwningScene.Services.IsReplica || NetDamage.ApplyingClaim || NetDamage.Replaying)
            return new Scope(null);
        bool direct = beam.EnhancedDirectHit;
        bool turret = flags.TestFlag(DamageFlags.Halfturret);
        byte kind = (byte)((direct ? 1 : 2) | (turret ? 4 : 0));
        var payment = new ShotPaymentKey(beam.ModLaunchKey, (byte)victim.SlotIndex,
            NetPlayerLifecycle.Generation(victim.SlotIndex), NetPlayerLifecycle.Get(victim.SlotIndex),
            beam.ModComponentId, kind);
        var segment = _segment;
        bool exactSegment = segment is { } value && value.Key == beam.ModLaunchKey
            && value.Component == beam.ModComponentId;
        bool ordinary = exactSegment && direct && !turret && beam.Owner is PlayerEntity
            && beam.ModComponentId == 1 && NetTimedShotModifiers.Ordinary((byte)beam.Beam)
            && !beam.EnhancedMicroSeeker && beam.Homing <= 0
            && (beam.Flags & (BeamFlags.Continuous | BeamFlags.Ricochet)) == 0
            && beam.RicochetWeapon == null && beam.EnhancedBounceCount == 0;
        var witness = new NativeImpactWitness(payment, (byte)beam.Beam, NetSession.NetFrame,
            point, channel, flags.TestFlag(DamageFlags.Headshot), beam.SpawnPosition,
            exactSegment ? segment!.Value.Start : beam.BackPosition,
            exactSegment ? segment!.Value.End : beam.Position, direct, ordinary);
        return new Scope(witness.Valid ? witness : null);
    }
}
