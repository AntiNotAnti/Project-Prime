using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Detached native path witnesses. Pool slots and ACK clocks never
/// identify a shot. Bounded rings retain collision-trimmed segments, native
/// explosion centres, and the native ice-wave volume under full ShotKey.</summary>
internal static class NetAttackPaths
{
    internal const int Capacity = 8192, ShotCapacity = 1024;
    private enum Kind : byte { Segment, Splash, IceWave }
    private struct Sample
    {
        public ShotKey Key;
        public Vector3 Start, End, Up, Origin, TravelPosition, Velocity;
        public float Radius, Angle, Damage, HeadDamage, MaxDistance, DirectionMagnitude;
        public int Interpolation;
        public BeamType Beam;
        public byte DirectionType;
        public uint Serial, PreviousSerial, Frame, TravelFrames, Component;
        public int ExcludedVictim;
        public int Previous;
        public Kind Kind;
        public bool HeadRange, BalancedRange;
    }
    private readonly record struct Head(int Index, uint Serial, uint Seen);
    private sealed class Slot
    {
        public readonly Sample[] Samples = new Sample[Capacity];
        public readonly Dictionary<ShotKey, Head> Heads = new(ShotCapacity);
        public int Cursor;
        public uint Serial;
    }
    private static readonly Slot?[] Slots = new Slot?[8];
    public static long Recorded, CapacityRefused, Matches, Missing;
    internal static void Reset()
    {
        foreach (Slot? slot in Slots) if (slot != null)
        { slot.Heads.Clear(); slot.Cursor = 0; Array.Clear(slot.Samples); }
        Recorded = CapacityRefused = Matches = Missing = 0;
    }
    private static void Record(BeamProjectileEntity beam, Sample sample)
    {
        if (!NetSession.IsAuthority || beam.OwningScene.Services.IsReplica
            || !NetPlayerLifecycle.CurrentProjectile(beam)) return;
        ShotKey key = beam.ModLaunchKey;
        if ((uint)key.ShooterSlot >= 8 || key.ShotId == 0) return;
        Slot slot = Slots[key.ShooterSlot] ??= new();
        if (!slot.Heads.TryGetValue(key, out Head head))
        {
            if (slot.Heads.Count >= ShotCapacity)
            {
                var expired = new List<ShotKey>();
                foreach (var item in slot.Heads)
                    if (unchecked(NetSession.NetFrame - item.Value.Seen) > NetAcceptedAttacks.LifetimeFrames)
                        expired.Add(item.Key);
                foreach (ShotKey old in expired) slot.Heads.Remove(old);
                if (slot.Heads.Count >= ShotCapacity) { CapacityRefused++; return; }
            }
            head = new(-1, 0, 0);
        }
        uint serial = ++slot.Serial; if (serial == 0) serial = ++slot.Serial;
        sample.Key = key; sample.Serial = serial; sample.Previous = head.Index;
        sample.Origin = beam.SpawnPosition; sample.Beam = beam.Beam;
        sample.DirectionMagnitude = beam.DamageDirMag; sample.DirectionType = beam.DamageDirType; sample.Velocity = beam.Velocity;
        sample.TravelPosition = beam.Position; sample.BalancedRange = beam.Owner is PlayerEntity;
        sample.TravelFrames = beam.ModClaimTravelFrames; sample.Component = beam.ModWitnessComponent;
        sample.PreviousSerial = head.Serial; sample.Frame = NetSession.NetFrame;
        slot.Samples[slot.Cursor] = sample;
        slot.Heads[key] = new(slot.Cursor, serial, NetSession.NetFrame);
        slot.Cursor = (slot.Cursor + 1) % Capacity; Recorded++;
    }
    internal static void Segment(BeamProjectileEntity beam, Vector3 end)
        => Record(beam, new() { Kind = Kind.Segment, Start = beam.BackPosition,
            End = end, Radius = beam.CylinderRadius, Damage = beam.Damage,
            HeadDamage = beam.HeadshotDamage, HeadRange = NativeHeadRange(beam.Position, beam.SpawnPosition), MaxDistance = beam.MaxDistance, Interpolation = beam.DamageInterpolation });
    internal static void Splash(BeamProjectileEntity beam, Vector3 origin, int excludedVictim = -1)
        => Record(beam, new() { Kind = Kind.Splash, Start = origin, Radius = beam.SplashRadius,
            Damage = beam.SplashDamage, Interpolation = beam.SplashDamageType, ExcludedVictim = excludedVictim });
    internal static void IceWave(BeamProjectileEntity beam, float angle)
        => Record(beam, new() { Kind = Kind.IceWave, Start = beam.Position,
            End = beam.Direction, Up = beam.Up, Radius = beam.MaxDistance, Angle = angle, Damage = beam.Damage });

    internal static bool Supports(PlayerEntity shooter, PlayerEntity victim,
        in HitClaimPacket claim, in FireEvent fire, ReadOnlySpan<uint> used, out uint component, out Vector3 resolvedPoint, out Vector3 resolvedDirection)
    {
            using var combatProfile = NetCombatProfile.Measure(CombatProfileSection.Supports);
        component = 0; resolvedPoint = resolvedDirection = default;
        ShotKey key = new(claim.AuthorityEpoch, claim.MatchId, shooter.SlotIndex,
            claim.ShooterGeneration, claim.ShooterLifeId, claim.ShotId);
        Slot? slot = Slots[shooter.SlotIndex];
        if (slot == null || !slot.Heads.TryGetValue(key, out Head head)) { Missing++; return false; }
        double claimTime = claim.AckFrame + claim.AckSubFrame / 256.0;
        if (!NetUnlagged.TryHistoricalPose(victim, claimTime, out var pose)) return false;
        // The proximity allowance is presentation tolerance; native contact
        // is proved independently at the exact fractional viewed body.
        if ((claim.HitPoint - pose.Position).LengthSquared > NetHitClaims.ClaimRadius * NetHitClaims.ClaimRadius) return false;
        HistoricalBody body = NetHistoricalTrace.Body(victim, pose);
        if ((claim.Flags & HitClaimPacket.FlagHalfturret) != 0)
        {
            if (!NetUnlagged.TryHistoricalHalfturretPosition(victim.SlotIndex, claimTime,
                claim.VictimGeneration, claim.VictimLifeId, out Vector3 turret)) return false;
            body = new(victim.SlotIndex, turret, .45f, 0, 0, HistoricalBodyType.AltSphere);
        }
        int at = head.Index; uint serial = head.Serial;
        // A overwritten ring link is unavailable evidence, never evidence for
        // the replacement shot. Limit work even for many pellets/children.
        for (int examined = 0; at >= 0 && examined < 512; examined++)
        {
            Sample sample = slot.Samples[at];
            if (sample.Serial != serial || sample.Key != key) break;
            if (unchecked(NetSession.NetFrame - sample.Frame) <= NetAcceptedAttacks.LifetimeFrames
                && TimingSupports(fire, claim.Frame, sample.TravelFrames)
                && !used.Contains(sample.Component))
            {
                bool headshot = (claim.Flags & HitClaimPacket.FlagHeadshot) != 0;
                bool direct = (claim.Flags & HitClaimPacket.FlagDirect) != 0 || headshot;
                bool valid = false; float damage = sample.Damage; Vector3 impact = body.Position;
                if (sample.Kind == Kind.Segment && direct
                    && NetHistoricalTrace.Intersect(body, sample.Start, sample.End, sample.Radius + .05f, out var collision))
                {
                    valid = (!headshot || body.Type == HistoricalBodyType.BipedCylinder
                            && collision.Position.Y - body.Position.Y >= body.Top - .32f
                            && (sample.Beam == BeamType.Imperialist || sample.HeadRange));
                    impact = collision.Position;
                    if (headshot) damage = sample.HeadDamage;
                    if (sample.MaxDistance > 0) damage = BeamProjectileEntity.GetInterpolatedValue(sample.Interpolation,
                        damage, 0, Vector3.Distance(sample.TravelPosition, sample.Origin) / sample.MaxDistance);
                }
                else if (sample.Kind == Kind.Splash && !direct && sample.Radius > 0
                    && (claim.Flags & HitClaimPacket.FlagHalfturret) == 0
                    && sample.ExcludedVictim != victim.SlotIndex
                    && !DirectOverlap(slot, head, sample, body)
                    && (pose.Position - sample.Start).LengthSquared < sample.Radius * sample.Radius
                    && NetDynamicGeometryHistory.TryTraceDistanceAtFrame(shooter.OwningScene,
                        sample.Start, pose.Position, claimTime, out float world) && world >= .999f)
                {
                    valid = true; impact = sample.Start;
                    damage = BeamProjectileEntity.GetInterpolatedValue(sample.Interpolation, damage, 0,
                        Vector3.Distance(pose.Position, sample.Start) / sample.Radius);
                }
                else if (sample.Kind == Kind.IceWave && !headshot && direct)
                    valid = BeamProjectileEntity.ModIceWaveContains(sample.Start, sample.End,
                        sample.Up, sample.Radius, (claim.Flags & HitClaimPacket.FlagHalfturret) != 0 ? body.Position : pose.Position, MathF.Cos(MathHelper.DegreesToRadians(sample.Angle)),
                        shooter.OwningScene.GameState.ShadowFreeze);
                if (valid && sample.BalancedRange && shooter.OwningScene.GameState.BalancedMode && BalancedModeRules.HasRangeDamageCurve(sample.Beam))
                    damage *= BalancedModeRules.RangeDamageMultiplier(sample.Beam, Vector3.Distance(impact, sample.Origin));
                valid &= claim.Direction.LengthSquared <= (sample.DirectionMagnitude + .001f) * (sample.DirectionMagnitude + .001f);
                valid &= claim.Damage <= NetAcceptedAttacks.FinalDamage(shooter, victim, damage, sample.Beam, headshot);
                if (valid) { component = sample.Component; resolvedPoint = impact;
                    resolvedDirection = claim.Direction == Vector3.Zero ? Vector3.Zero
                        : BeamProjectileEntity.ModDamageDirection(sample.DirectionType, sample.DirectionMagnitude, sample.Velocity, impact, pose.Position);
                    Matches++; return true; }
            }
            at = sample.Previous; serial = sample.PreviousSerial;
        }
        Missing++; return false;
    }
    internal static bool NativeHeadRange(Vector3 position, Vector3 origin)
        => (position - origin).LengthSquared <= 15f * 15f;
    // Source-frame travel is independent of ACK/arrival. Native catch-up and
    // delayed emission increment the same counter as live simulation; a later
    // pass through an old victim location cannot support an earlier owner hit.
    internal static bool TimingSupports(in FireEvent fire, uint claimFrame, uint travelFrames)
    {
        if (fire.Kind == FireEventKind.ContinuousTick)
            return claimFrame == fire.ContinuousPhase && travelFrames <= 2;
        uint elapsed = unchecked(claimFrame - fire.SourceFrame);
        return elapsed <= NetAcceptedAttacks.LifetimeFrames
            && Math.Abs((long)travelFrames - (elapsed + 1L)) <= 2;
    }
    private static bool DirectOverlap(Slot slot, Head head, in Sample splash, in HistoricalBody body)
    {
        int at = head.Index; uint serial = head.Serial;
        for (int examined = 0; at >= 0 && examined < 512; examined++)
        {
            Sample prior = slot.Samples[at];
            if (prior.Serial != serial) break;
            if (prior.Component == splash.Component && prior.TravelFrames <= splash.TravelFrames
                && prior.Kind == Kind.Segment
                && NetHistoricalTrace.Intersect(body, prior.Start, prior.End, prior.Radius + .05f, out _)) return true;
            at = prior.Previous; serial = prior.PreviousSerial;
        }
        return false;
    }
}
