using System;
using System.Runtime.CompilerServices;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal enum CampaignOracleWorld { Unavailable, CurrentNativeSegment, HistoricalFirstSegment }
internal enum CampaignOracleSource { UnqualifiedNativeCopy, AcceptedAuthoredSource, AuthorityNativeSource }
internal enum CampaignOracleStatus { Unavailable, SourceQualifiedMiss, SourceQualifiedContact }
internal enum CampaignOracleReason
{
    None, InvalidSource, UnsupportedFamily, SourceUnqualified, WorldUnavailable,
    InvalidGeometry, LifecycleAmbiguous, UnsupportedBody, AmbiguousContact, Obstructed
}

// Detached input values: no claimant point, contested hit, verdict or payment is
// accepted by this API. WorldFrame is geometry time, not a Stopwatch timestamp.
internal readonly record struct CampaignOracleRay(ShotKey Key, uint Component, byte Weapon,
    uint SourceFrame, uint AckFrame, byte AckSubFrame, uint WorldFrame,
    CampaignOracleWorld World, CampaignOracleSource Source, ShotProofReason SourceReason,
    Vector3 Start, Vector3 End, float Radius, float MaximumDistance, bool FirstSegment, bool SupportedFamily,
    bool CompleteWorld);
internal readonly record struct CampaignOracleBody(HistoricalBody Body, ushort Generation,
    ushort Life, uint WorldFrame);
internal readonly record struct CampaignOracleObstacle(bool Available, bool Hit, float Distance);
internal readonly record struct CampaignOracleResult(CampaignOracleStatus Status,
    CampaignOracleReason Reason, int Victim = -1, ushort VictimGeneration = 0,
    ushort VictimLife = 0, bool HeadBand = false, float Distance = 0, Vector3 Point = default);

[InlineArray(8)]
internal struct CampaignOracleBodies { private CampaignOracleBody _first; }
internal struct CampaignOracleSample
{
    internal CampaignOracleRay Ray;
    internal CampaignOracleObstacle Obstacle;
    internal CampaignOracleResult Result;
    internal CampaignOracleBodies Bodies;
    internal int Count;
}

internal sealed class CampaignOracleBuffer
{
    private readonly CampaignOracleSample[] _rows;
    private int _count;
    internal long Overflow { get; private set; }
    internal CampaignOracleBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _rows = new CampaignOracleSample[capacity];
    }
    internal void Add(in CampaignOracleRay ray, in CampaignOracleObstacle obstacle,
        ReadOnlySpan<CampaignOracleBody> bodies, in CampaignOracleResult result)
    {
        if (_count == _rows.Length || bodies.Length > 8) { Overflow++; return; }
        ref var row = ref _rows[_count++];
        row.Ray = ray; row.Obstacle = obstacle; row.Result = result; row.Count = bodies.Length;
        for (int i = 0; i < bodies.Length; i++) row.Bodies[i] = bodies[i];
    }
    internal CampaignOracleSample[] Capture() => _rows.AsSpan(0, _count).ToArray();
}

/// <summary>Read-only NCX measurement of a narrow native first segment. The
/// independent decision uses frozen source/body/obstacle inputs and the existing
/// native player intersection primitive. It never reads the selected native
/// victim, client hit, claim, receipt, payment or claimed proximity. Contacts are
/// geometric expectations, not legal-damage or health-payment certificates.</summary>
internal static class NetCampaignHitOracle
{
    private static CampaignOracleBuffer? _buffer;
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static CampaignOracleResult Missing(CampaignOracleReason reason) => new(CampaignOracleStatus.Unavailable, reason);
    internal static CampaignOracleResult Evaluate(in CampaignOracleRay ray,
        ReadOnlySpan<CampaignOracleBody> bodies, in CampaignOracleObstacle obstacle)
    {
        var key = ray.Key;
        if (key.AuthorityEpoch == 0 || key.MatchId == 0 || (uint)key.ShooterSlot >= 8
            || key.Generation == 0 || key.LifeId == 0 || key.ShotId == 0 || ray.Component == 0)
            return Missing(CampaignOracleReason.InvalidSource);
        if (!ray.SupportedFamily || !ray.FirstSegment || ray.Component != 1
            || ray.Weapon != (byte)BeamType.Imperialist)
            return Missing(CampaignOracleReason.UnsupportedFamily);
        if (ray.Source is not (CampaignOracleSource.AcceptedAuthoredSource or CampaignOracleSource.AuthorityNativeSource)
            || ray.SourceReason != ShotProofReason.Verified)
            return Missing(CampaignOracleReason.SourceUnqualified);
        if (!ray.CompleteWorld || ray.World is not (CampaignOracleWorld.CurrentNativeSegment or CampaignOracleWorld.HistoricalFirstSegment)
            || !obstacle.Available)
            return Missing(CampaignOracleReason.WorldUnavailable);
        if (!Finite(ray.Start) || !Finite(ray.End) || !float.IsFinite(ray.Radius) || ray.Radius < 0
            || !float.IsFinite(ray.MaximumDistance) || ray.MaximumDistance < 0
            || (ray.End - ray.Start).LengthSquared < .000001f || bodies.Length > 8
            || obstacle.Hit && (!float.IsFinite(obstacle.Distance) || obstacle.Distance < 0 || obstacle.Distance > 1))
            return Missing(CampaignOracleReason.InvalidGeometry);
        // Native range termination is another collision candidate. This narrow
        // oracle declines clipped segments rather than ignoring that competitor.
        if (ray.MaximumDistance > 0 && ray.MaximumDistance <= (ray.End - ray.Start).Length)
            return Missing(CampaignOracleReason.UnsupportedFamily);
        // Validate the complete competitor inventory before deciding even a miss.
        // One unknown body could occlude the offered ray or win an earlier hit.
        uint seen = 0;
        foreach (var frozen in bodies)
        {
            var body = frozen.Body;
            if ((uint)body.Slot >= 8 || body.Slot == key.ShooterSlot || frozen.Generation == 0
                || frozen.Life == 0 || frozen.WorldFrame != ray.WorldFrame || (seen & (1u << body.Slot)) != 0)
                return Missing(CampaignOracleReason.LifecycleAmbiguous);
            seen |= 1u << body.Slot;
            if (body.Type != HistoricalBodyType.BipedCylinder)
                return Missing(CampaignOracleReason.UnsupportedBody);
            if (!Finite(body.Position) || !float.IsFinite(body.Radius) || body.Radius <= 0
                || !float.IsFinite(body.Bottom) || !float.IsFinite(body.Top) || body.Top <= body.Bottom)
                return Missing(CampaignOracleReason.InvalidGeometry);
        }
        float closest = obstacle.Hit ? obstacle.Distance : 2f;
        CampaignOracleBody winner = default;
        CollisionResult selected = default;
        bool found = false, tie = false;
        foreach (var frozen in bodies)
        {
            if (!NetHistoricalTrace.Intersect(frozen.Body, ray.Start, ray.End, ray.Radius, out var hit)) continue;
            if (!float.IsFinite(hit.Distance) || hit.Distance < 0 || hit.Distance > 1 || !Finite(hit.Position))
                return Missing(CampaignOracleReason.InvalidGeometry);
            // Native wall-before-player strict comparison is retained. Equal
            // player intersections have ambiguous ownership in a detached set.
            if (hit.Distance == closest && found) { tie = true; continue; }
            if (hit.Distance >= closest) continue;
            closest = hit.Distance; winner = frozen; selected = hit; found = true; tie = false;
        }
        if (tie) return Missing(CampaignOracleReason.AmbiguousContact);
        if (!found) return new(CampaignOracleStatus.SourceQualifiedMiss,
            obstacle.Hit ? CampaignOracleReason.Obstructed : CampaignOracleReason.None);
        bool head = selected.Position.Y - winner.Body.Position.Y >= winner.Body.Top - .3f;
        return new(CampaignOracleStatus.SourceQualifiedContact, CampaignOracleReason.None,
            winner.Body.Slot, winner.Generation, winner.Life, head, closest, selected.Position);
    }

    // Hook at the existing SegmentScope seam before native damage. It is after
    // candidate discovery, but no chosen candidate/result is consumed here.
    // No Begin/Restore, simulation, RNG, lifecycle or gameplay write occurs.
    internal static void ObserveSegment(BeamProjectileEntity beam, Vector3 start, Vector3 end)
    {
        if (beam.ModCustomShot != null || !NetCampaignEvidence.TimelineEnabled || beam.OwningScene.Services.IsReplica
            || start != beam.SpawnPosition) return;
        bool authority = NetSession.IsAuthority;
        var owner = beam.Owner as PlayerEntity;
        FireEvent fire = default;
        // TryTiming's non-accepted fallback calls For(), which can retire source
        // lifecycle state. Only the raw accepted scope is read here; native-owner
        // and client copies deliberately stay unqualified in this first adapter.
        bool acceptedSource = authority && owner != null
            && NetFireEvents.TryAcceptedKey(owner, out var accepted) && accepted == beam.ModLaunchKey;
        bool offered = acceptedSource && NetFireEvents.TryTiming(owner!, out fire)
            && fire.ShotId == beam.ModLaunchKey.ShotId && fire.SourceLife == beam.ModLaunchKey.LifeId;
        var source = offered ? CampaignOracleSource.AcceptedAuthoredSource : CampaignOracleSource.UnqualifiedNativeCopy;
        bool supported = offered && beam.Beam == BeamType.Imperialist && beam.ModCustomDefinition == null
            && beam.ModComponentId == 1 && beam.Acceleration == Vector3.Zero && beam.SpeedDecayTime == 0
            && beam.Homing == 0 && beam.RicochetWeapon == null && beam.EnhancedBounceCount == 0
            && !beam.EnhancedMicroSeeker && fire.Charge == 0
            && fire.Kind is not (FireEventKind.ContinuousTick or FireEventKind.TurretFire)
            && (beam.Flags & (BeamFlags.Continuous | BeamFlags.Ricochet | BeamFlags.Collided | BeamFlags.SelfDamage)) == 0;
        var world = CampaignOracleWorld.Unavailable; uint frame = NetSession.NetFrame;
        if (authority && owner != null && supported)
        {
            if (NetUnlagged.TryTimedFirstCollisionFrame(owner, out uint historical))
            { world = CampaignOracleWorld.HistoricalFirstSegment; frame = historical; }
            else if (owner.IsBot || owner.SlotIndex == NetSession.LocalSlot)
                world = CampaignOracleWorld.CurrentNativeSegment;
        }
        bool complete = authority && world != CampaignOracleWorld.Unavailable;
        Span<CampaignOracleBody> bodies = stackalloc CampaignOracleBody[8]; int count = 0;
        if (complete)
        {
            foreach (var enemy in beam.OwningScene.GetEnemyInstanceEntities())
                if (enemy.Flags.TestFlag(EnemyFlags.CollideBeam)) { complete = false; break; }
            foreach (var player in beam.OwningScene.GetPlayerEntities())
            {
                if (player == owner || player.Health == 0 || player.Flags2.TestFlag(PlayerFlags2.Spectating)) continue;
                ushort generation = NetPlayerLifecycle.Generation(player.SlotIndex), life = NetPlayerLifecycle.Get(player.SlotIndex);
                if (count == bodies.Length || !NetUnlagged.CollisionTargetAvailable(player)
                    || NetUnlagged.TryCollisionHalfturret(player, out _) || generation == 0 || life == 0)
                { complete = false; break; }
                if (world == CampaignOracleWorld.HistoricalFirstSegment
                    && (!NetUnlagged.TryHistoricalCollisionEligibility(player.SlotIndex, frame,
                        out bool active, out bool turret, out ushort historicalGeneration, out ushort historicalLife)
                        || !active || turret || generation != historicalGeneration || life != historicalLife
                        || !player.ModHistoricalCollisionActive))
                { complete = false; break; }
                bodies[count++] = new(NetHistoricalTrace.CurrentBody(player), generation, life, frame);
            }
        }
        var ray = new CampaignOracleRay(beam.ModLaunchKey, beam.ModComponentId, (byte)beam.Beam,
            fire.SourceFrame, fire.AckFrame, fire.AckSubFrame, frame, world, source,
            authority && offered ? NetShotProof.EmissionReason(beam.ModLaunchKey) : ShotProofReason.SourceUnavailable,
            start, end, beam.CylinderRadius, beam.MaxDistance, true, supported, complete);
        CampaignOracleObstacle obstacle = default;
        if (complete)
        {
            bool hit = BeamProjectileEntity.TryBeamObstacle(start, end, beam.OwningScene, out var result, out _);
            obstacle = new(true, hit, hit ? result.Distance : 0);
        }
        var expected = Evaluate(ray, bodies[..count], obstacle);
        _buffer ??= new CampaignOracleBuffer(Math.Min(4096, NetCampaignEvidence.EvidenceRowLimit));
        _buffer.Add(ray, obstacle, bodies[..count], expected);
    }

    private static object Vector(Vector3 v) => new { v.X, v.Y, v.Z };
    internal static object Capture()
    {
        var retained = _buffer?.Capture() ?? Array.Empty<CampaignOracleSample>();
        var rows = new object[retained.Length];
        int contactRows = 0, missRows = 0, unavailableRows = 0;
        for (int i = 0; i < retained.Length; i++)
        {
            ref var sample = ref retained[i]; var ray = sample.Ray; var result = sample.Result;
            if (result.Status == CampaignOracleStatus.SourceQualifiedContact) contactRows++;
            else if (result.Status == CampaignOracleStatus.SourceQualifiedMiss) missRows++;
            else unavailableRows++;
            var bodies = new object[sample.Count];
            for (int j = 0; j < sample.Count; j++)
            {
                var frozen = sample.Bodies[j]; var body = frozen.Body;
                bodies[j] = new { body.Slot, frozen.Generation, frozen.Life, frozen.WorldFrame,
                    Position = Vector(body.Position), body.Radius, body.Bottom, body.Top, Type = body.Type.ToString() };
            }
            rows[i] = new { ray.Key, ray.Component, Kind = (byte)1, Phase = 0u, ray.Weapon, ray.SourceFrame, ray.AckFrame, ray.AckSubFrame,
                ray.WorldFrame, World = ray.World.ToString(), Source = ray.Source.ToString(),
                SourceReason = ray.SourceReason.ToString(), Start = Vector(ray.Start), End = Vector(ray.End),
                ray.Radius, ray.MaximumDistance, ray.FirstSegment, ray.SupportedFamily, ray.CompleteWorld, Bodies = bodies,
                sample.Obstacle, Status = result.Status.ToString(), Reason = result.Reason.ToString(),
                result.Victim, result.VictimGeneration, result.VictimLife, result.HeadBand, result.Distance,
                Point = Vector(result.Point), DamageEligibility = "NOT_ASSESSED", Payment = "NOT_ASSESSED" };
        }
        return new { Schema = 1, Enabled = NetCampaignEvidence.TimelineEnabled, Rows = rows,
            ObservedFirstSegmentRows = retained.Length, SourceQualifiedContactRows = contactRows,
            SourceQualifiedMissRows = missRows, UnavailableRows = unavailableRows,
            LegalDamageDenominator = "UNAVAILABLE",
            Overflow = _buffer?.Overflow ?? 0,
            StorageComplete = NetCampaignEvidence.TimelineEnabled && (_buffer?.Overflow ?? 0) == 0,
            Complete = NetCampaignEvidence.TimelineEnabled && retained.Length > 0 && unavailableRows == 0 && (_buffer?.Overflow ?? 0) == 0,
            Scope = "Frozen Imperialist first-segment biped geometry only; source-qualified contact/miss is independent of claim and payment. Current live immunity/team/damage gates are not replayed; legal damage/payment denominator unavailable. Unsupported/lifecycle-ambiguous histories remain unavailable. Observed native copies are not authenticated accepted sources. No gameplay mutation; no dynamic history Begin." };
    }
}
