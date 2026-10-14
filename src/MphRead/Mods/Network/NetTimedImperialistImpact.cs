using System;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal readonly record struct TimedImperialistImpact(ShotPaymentKey Payment, uint Frame,
    Vector3 Point, Vector3 ResolvedPosition, uint NativeChannel, bool NativeHead)
{
    internal bool Valid => Payment.Shot.AuthorityEpoch != 0 && Payment.Shot.MatchId != 0
        && (uint)Payment.Shot.ShooterSlot < 8 && Payment.Shot.Generation != 0
        && Payment.Shot.LifeId != 0 && Payment.Shot.ShotId != 0 && Payment.Component == 1
        && Payment.Kind == 1 && Payment.Victim < 8 && Payment.Generation != 0 && Payment.Life != 0
        && Frame != 0 && NativeChannel <= int.MaxValue
        && float.IsFinite(Point.X) && float.IsFinite(Point.Y) && float.IsFinite(Point.Z)
        && float.IsFinite(ResolvedPosition.X) && float.IsFinite(ResolvedPosition.Y) && float.IsFinite(ResolvedPosition.Z);
}

// Exact integer-frame first segment only. Later travel and incomplete competing
// bodies are unavailable; a claimant's body origin never enters this trace.
internal static class NetTimedImperialistImpact
{
    private static bool Finite(Vector3 value) => float.IsFinite(value.X)
        && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    internal static bool TryResolve(in TimedShotCertificate shot, in HitClaimPacket claim,
        out TimedImperialistImpact impact)
    {
        impact = default;
        var damage = shot.Damage; var key = shot.Key;
        if (!NetTimedShotModifiers.Enforcing || !shot.TrustedSource || !damage.Present
            || !damage.FirstSegmentKnown || shot.Components != 1 || claim.ComponentId != 1
            || shot.Fire.Weapon != (byte)BeamType.Imperialist || claim.Beam != shot.Fire.Weapon
            || shot.Fire.Kind is FireEventKind.ContinuousTick or FireEventKind.TurretFire
            || shot.Fire.AckSubFrame != 0 || claim.AckFrame != shot.Fire.AckFrame
            || damage.FirstCollisionFrame != unchecked(shot.Fire.AckFrame + 1)
            || claim.LaunchFrame != shot.Fire.AckFrame || claim.AckFrame > NetSession.NetFrame
            || key != NetShotProof.Key(key.ShooterSlot, claim)
            || (claim.Flags & HitClaimPacket.FlagDirect) == 0
            || (claim.Flags & (HitClaimPacket.FlagHalfturret | HitClaimPacket.FlagContinuousTick)) != 0
            || !Finite(damage.Origin) || !Finite(damage.NativeVelocity)
            || damage.NativeVelocity.LengthSquared <= .000001f
            || !float.IsFinite(damage.Radius) || damage.Radius < 0
            || !float.IsFinite(damage.Reach) || damage.Reach < damage.NativeVelocity.Length
            || (uint)key.ShooterSlot >= PlayerEntity.Players.Count) return false;
        var shooter = PlayerEntity.Players[key.ShooterSlot]; var scene = shooter.OwningScene;
        if (scene.Services.IsReplica || !scene.GameState.Multiplayer
            || !NetPlayerLifecycle.Matches(claim.VictimSlot, claim.VictimGeneration, claim.VictimLifeId)) return false;
        // Native multiplayer beams may select enemies before player bodies.
        // Their historical hurt volume is not represented by this contract.
        foreach (var enemy in scene.GetEnemyInstanceEntities())
            if (enemy.Flags.TestFlag(EnemyFlags.CollideBeam)) return false;
        Vector3 end = damage.Origin + damage.NativeVelocity;
        if (!NetDynamicGeometryHistory.TryNativeBeamObstacleAtFrame(scene, damage.Origin, end,
            damage.FirstCollisionFrame, out bool wallHit, out var wall)) return false;
        float closest = wallHit ? wall.Distance : 2f;
        CollisionResult winner = default; int victimSlot = -1;
        HistoricalBody winningBody = default;
        foreach (var player in scene.GetPlayerEntities())
        {
            if (player == shooter) continue; // The native Imperialist has no self damage.
            if (!NetUnlagged.TryHistoricalCollisionEligibility(player.SlotIndex, damage.FirstCollisionFrame,
                out bool active, out bool turret, out ushort generation, out ushort life)) return false;
            if (!active) continue;
            if (turret || !player.ModIsInPlay || !player.LoadFlags.TestFlag(LoadFlags.Active)
                || player.Flags2.TestFlag(PlayerFlags2.Spectating)
                || !NetPlayerLifecycle.Matches(player.SlotIndex, generation, life)
                || !NetTimedDamageProfile.TryGet(player.SlotIndex, generation, life,
                    damage.FirstCollisionFrame, out var profile) || profile.Hunter != player.Hunter
                || !NetUnlagged.TryHistoricalPose(player, damage.FirstCollisionFrame, out var pose)) return false;
            var body = NetHistoricalTrace.Body(player, pose);
            if (NetHistoricalTrace.Intersect(body, damage.Origin, end, damage.Radius, out var hit)
                && hit.Distance < closest)
            { closest = hit.Distance; winner = hit; victimSlot = player.SlotIndex; winningBody = body; }
        }
        if (victimSlot != claim.VictimSlot || closest < 0 || closest > 1) return false;
        if (!TryChannel(damage, winningBody, winner, out uint channel, out bool head,
            out var resolved)) return false;
        impact = new(NetShotProof.PaymentKey(key.ShooterSlot, claim), damage.FirstCollisionFrame,
            winner.Position, resolved, channel, head);
        return true;
    }

    // The same post-collision position offset and interpolation used by the
    // native kernel; Point is the collision result, not claim.HitPoint.
    internal static bool TryChannel(in TimedShotDamageTemplate damage, in HistoricalBody body,
        in CollisionResult collision, out uint channel, out bool nativeHead, out Vector3 resolved)
    {
        channel = 0; nativeHead = false; resolved = default;
        if (!Finite(collision.Position) || !Finite(collision.Plane.Xyz)
            || !float.IsFinite(damage.Body) || !float.IsFinite(damage.Head)
            || damage.Body < 0 || damage.Head < 0 || !float.IsFinite(damage.Reach)
            || damage.Reach <= 0 || damage.Curve is < 0 or > 3) return false;
        resolved = collision.Position + collision.Plane.Xyz * Fixed.ToFloat(204);
        bool head = body.Type == HistoricalBodyType.BipedCylinder
            && collision.Position.Y - body.Position.Y >= body.Top - .3f;
        float value = BeamProjectileEntity.GetInterpolatedValue(damage.Curve,
            head ? damage.Head : damage.Body, 0, Vector3.Distance(resolved, damage.Origin) / damage.Reach);
        if (!float.IsFinite(value) || value < 0) return false;
        channel = (uint)Math.Clamp(value, 0, int.MaxValue);
        nativeHead = head && MathF.Abs(damage.Body - damage.Head) > 1 / 4096f;
        return true;
    }
}
