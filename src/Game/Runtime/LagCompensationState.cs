using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Only the values read by BeamProjectileEntity.CheckCollision's player
    /// tests. No live entity is repositioned while testing historical geometry.
    /// </summary>
    public readonly record struct LagCompensationState
    {
        public int Slot { get; init; }
        public ulong ConnectionId { get; init; }
        public uint LifeId { get; init; }
        public Hunter Hunter { get; init; }
        public bool Alive { get; init; }
        public bool Spectating { get; init; }
        public bool AltForm { get; init; }
        public bool HasHalfturret { get; init; }
        public Vector3 Position { get; init; }
        public Vector3 Facing { get; init; }
        public Vector3 SpherePosition { get; init; }
        public float SphereRadius { get; init; }
        public float MinPickupHeight { get; init; }
        public float MaxPickupHeight { get; init; }
        public Vector3 KandenSegment1 { get; init; }
        public Vector3 KandenSegment2 { get; init; }
        public Vector3 KandenSegment3 { get; init; }
        public Vector3 HalfturretPosition { get; init; }
        public bool CanBeHit => Alive && !Spectating;

        public static LagCompensationState Capture(PlayerEntity player, ulong connectionId, uint lifeId)
        {
            return new LagCompensationState
            {
                Slot = player.SlotIndex,
                ConnectionId = connectionId,
                LifeId = lifeId,
                Hunter = player.Hunter,
                Alive = player.Health > 0,
                Spectating = player.Flags2.TestFlag(PlayerFlags2.Spectating),
                AltForm = player.IsAltForm,
                HasHalfturret = player.Hunter == Hunter.Weavel && player.Flags2.TestFlag(PlayerFlags2.Halfturret),
                Position = player.Position,
                Facing = player.FacingVector,
                SpherePosition = player.Volume.SpherePosition,
                SphereRadius = player.Volume.SphereRadius,
                MinPickupHeight = Fixed.ToFloat(player.Values.MinPickupHeight),
                MaxPickupHeight = Fixed.ToFloat(player.Values.MaxPickupHeight),
                KandenSegment1 = player.KandenSegPos[1],
                KandenSegment2 = player.KandenSegPos[2],
                KandenSegment3 = player.KandenSegPos[3],
                HalfturretPosition = player.Halfturret.Position
            };
        }

        public bool CheckPlayer(Vector3 back, Vector3 front, float beamRadius, ref CollisionResult result)
        {
            if (!CanBeHit)
            {
                return false;
            }
            float radii = SphereRadius + beamRadius;
            if (!AltForm)
            {
                return CollisionDetection.CheckCylindersOverlap(back, front, Position.AddY(MinPickupHeight),
                    Vector3.UnitY, MaxPickupHeight - MinPickupHeight, radii, ref result);
            }
            if (Hunter != Hunter.Kanden)
            {
                return CollisionDetection.CheckCylinderOverlapSphere(back, front, SpherePosition, radii, ref result);
            }
            // Keep the original broad-phase radius and short-circuit order.
            // Taking the nearest of all segments would change game mechanics.
            return CollisionDetection.CheckCylinderOverlapSphere(back, front, KandenSegment2, 1.6f, ref result)
                && (CollisionDetection.CheckCylinderOverlapSphere(back, front, SpherePosition, radii, ref result)
                    || CollisionDetection.CheckCylinderOverlapSphere(back, front, KandenSegment1, radii, ref result)
                    || CollisionDetection.CheckCylinderOverlapSphere(back, front, KandenSegment2, radii, ref result)
                    || CollisionDetection.CheckCylinderOverlapSphere(back, front, KandenSegment3, radii, ref result));
        }

        public bool CheckHalfturret(Vector3 back, Vector3 front, float beamRadius, ref CollisionResult result)
            => CanBeHit && HasHalfturret
                && CollisionDetection.CheckCylinderOverlapSphere(back, front, HalfturretPosition, beamRadius + 0.45f, ref result);

        public bool IsHeadshot(Vector3 hitPosition)
            => CanBeHit && !AltForm && hitPosition.Y - Position.Y >= MaxPickupHeight - 0.3f;
    }
}
