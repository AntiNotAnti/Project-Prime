using System;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        internal static float GetBipedPitch(Vector3 facing)
        {
            float limit = Fixed.ToFloat(2896);
            float cos = MathF.Sqrt(MathF.Max(0, 1 - facing.Y * facing.Y));
            float sin = facing.Y;
            if (MathF.Abs(facing.Y) > limit)
            {
                cos = limit;
                sin = facing.Y <= 0 ? -limit : limit;
            }
            return MathF.Atan2(sin, cos);
        }

        // Legs and torso use independent animation tracks but compose into one
        // instance-owned pose, also used by muzzle, ice and death effects.
        internal static void AnimateBipedPose(Model pose, AnimationInfo legs, AnimationInfo torso, float pitch)
        {
            Node spine = pose.GetNodeByName("Spine_1")!;
            spine.AnimIgnoreChild = true;
            spine.AfterTransform = Matrix4.CreateRotationZ(pitch);
            try
            {
                pose.AnimateNodes(0, false, Matrix4.Identity, Vector3.One, legs);
                spine.AnimIgnoreChild = false;
                pose.AnimateNodes(spine.ChildIndex, false, Matrix4.Identity, Vector3.One, torso);
            }
            finally
            {
                spine.AnimIgnoreChild = false;
                spine.AfterTransform = null;
            }
        }
    }
}
