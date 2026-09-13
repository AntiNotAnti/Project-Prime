using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Conservative spawn-volume validation using the same collision state as
    /// the authoritative simulation. The authored marker is one unit below the
    /// final player origin, matching <see cref="PlayerEntity.Spawn"/>.
    /// </summary>
    public static class SpawnGeometry
    {
        private static readonly Vector3[] HorizontalDirections =
        [
            Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ
        ];

        private static readonly float[] BodySampleHeights = [-0.25f, 0.35f, 0.9f];

        public static bool IsSafe(Scene scene, PlayerSpawnEntity spawn)
        {
            if (spawn.NodeRef == NodeRef.None
                || !VectorMath.IsFinite(spawn.Position)
                || !VectorMath.IsFinite(spawn.FacingVector)
                || !VectorMath.IsFinite(spawn.UpVector)
                || spawn.FacingVector.LengthSquared <= 0.0001f
                || spawn.UpVector.LengthSquared <= 0.0001f)
            {
                return false;
            }

            Vector3 forward = new(spawn.FacingVector.X, 0, spawn.FacingVector.Z);
            if (forward.LengthSquared <= 0.0001f)
            {
                return false;
            }

            Vector3 body = spawn.Position.AddY(1);
            CollisionResult floor = default;
            if (!CollisionDetection.CheckBetweenPoints(body.AddY(0.2f), body.AddY(-2.5f),
                    TestFlags.None, scene, ref floor)
                || floor.Flags.TestFlag(CollisionFlags.Damaging))
            {
                return false;
            }

            CollisionResult collision = default;
            if (CollisionDetection.CheckBetweenPoints(body.AddY(0.2f), body.AddY(1.2f),
                TestFlags.None, scene, ref collision))
            {
                return false;
            }

            foreach (float height in BodySampleHeights)
            {
                Vector3 center = body.AddY(height);
                foreach (Vector3 direction in HorizontalDirections)
                {
                    collision = default;
                    if (CollisionDetection.CheckBetweenPoints(center,
                        center + direction * 0.5f, TestFlags.None, scene, ref collision))
                    {
                        return false;
                    }
                }
            }

            collision = default;
            return !CollisionDetection.CheckBetweenPoints(body.AddY(0.5f),
                body.AddY(0.5f) + forward.Normalized(), TestFlags.None, scene,
                ref collision);
        }
    }
}
