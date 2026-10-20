using System;
using System.Runtime.CompilerServices;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private static readonly ConditionalWeakTable<Model, CrouchSkeleton> CrouchSkeletons = new();

    // Procedural two-bone crouch: lower the torso, bend each actual Hunter leg,
    // and keep the animated feet planted. Model axes/leg lengths are sampled
    // from its current animation, not copied from a particular Hunter asset.
    internal void ModApplyCrouchPose(Model model, Matrix4 world)
        => ModApplyCrouchVisualPose(model, world, ModCrouched ? 1 : 0);

    private void ModApplyCrouchVisualPose(Model model, Matrix4 world, float amount, bool walkingGait = false)
    {
        amount = Math.Clamp(amount, 0, 1);
        if (amount <= 0) return;
        float drop = (Fixed.ToFloat(Values.MaxPickupHeight) - ModBipedTopFor(true)) * amount;
        CrouchSkeleton skeleton = CrouchSkeletons.GetValue(model, value => new(value));
        if (!skeleton.Valid)
        {
            // Cosmetic replacement skeletons may lack the native leg names.
            // Match the accepted height without changing gameplay or assets.
            float bottom = world.Row3.Y + ModBipedBottom;
            float ratio = 1 - drop / (Fixed.ToFloat(Values.MaxPickupHeight) - ModBipedBottom);
            foreach (Node node in model.Nodes)
            {
                Matrix4 pose = node.Animation;
                pose.Row0.Y *= ratio; pose.Row1.Y *= ratio; pose.Row2.Y *= ratio;
                pose.Row3.Y = bottom + (pose.Row3.Y - bottom) * ratio;
                node.Animation = pose;
            }
            return;
        }
        Vector3 forward = -world.Row2.Xyz.WithY(0);
        if (forward.LengthSquared < 1e-8f) forward = Vector3.UnitZ;
        else forward.Normalize();
        Vector3 FootTarget(int[] bones, Vector3 neutral)
        {
            Vector3 ankle = model.Nodes[bones[2]].Animation.Row3.Xyz;
            if (!walkingGait) return ankle;
            neutral = Matrix.Vec3MultMtx4(neutral, world);
            // Retain each Hunter's own stance width. Shorten the animated
            // excursion around it, and lower only the airborne foot lift.
            float stride = 1 + (Mods.Movement.TraversalGaitPresentation.StrideScale - 1) * amount;
            float lift = 1 + (Mods.Movement.TraversalGaitPresentation.SwingLiftScale - 1) * amount;
            return new(neutral.X + (ankle.X - neutral.X) * stride,
                ankle.Y <= neutral.Y ? ankle.Y : neutral.Y + (ankle.Y - neutral.Y) * lift,
                neutral.Z + (ankle.Z - neutral.Z) * stride);
        }
        var left = Leg(model, skeleton.Left, drop, forward, FootTarget(skeleton.Left, skeleton.LeftFoot));
        var right = Leg(model, skeleton.Right, drop, forward, FootTarget(skeleton.Right, skeleton.RightFoot));
        for (int index = 0; index < model.Nodes.Count; index++)
        {
            Node node = model.Nodes[index];
            Matrix4 pose = node.Animation;
            int group = skeleton.Groups[index];
            if (group == 0) pose.Row3.Y -= drop;
            else if (group == 1) pose *= left.Hip;
            else if (group == 2) pose *= left.Knee;
            else if (group == 4) pose *= right.Hip;
            else if (group == 5) pose *= right.Knee;
            else if (group == 3 && walkingGait) pose *= left.Foot;
            else if (group == 6 && walkingGait) pose *= right.Foot;
            node.Animation = pose;
        }
    }

    private static (Matrix4 Hip, Matrix4 Knee, Matrix4 Foot) Leg(Model model, int[] bones, float drop,
        Vector3 forward, Vector3 targetAnkle)
    {
        Vector3 hip = model.Nodes[bones[0]].Animation.Row3.Xyz;
        Vector3 knee = model.Nodes[bones[1]].Animation.Row3.Xyz;
        Vector3 ankle = model.Nodes[bones[2]].Animation.Row3.Xyz;
        Vector3 lowered = hip - Vector3.UnitY * drop;
        // Retain some of the animated leg's bend plane for natural asymmetric
        // stances; near-straight legs keep the stable body-forward fallback.
        Vector3 axis = ankle - hip;
        if (axis.LengthSquared > 1e-8f)
        {
            axis.Normalize();
            Vector3 animatedPole = knee - hip - axis * Vector3.Dot(knee - hip, axis);
            if (animatedPole.LengthSquared > 1e-6f && Vector3.Dot(animatedPole, forward) > 0)
                forward = (forward * .8f + animatedPole.Normalized() * .2f).Normalized();
        }
        Vector3 target = ModCrouchKnee(lowered, targetAnkle, (knee - hip).Length, (ankle - knee).Length, forward);
        return (MoveBone(hip, lowered, knee - hip, target - lowered),
            MoveBone(knee, target, ankle - knee, targetAnkle - target),
            Matrix4.CreateTranslation(targetAnkle - ankle));
    }

    internal static Vector3 ModCrouchKnee(Vector3 hip, Vector3 ankle, float thigh, float shin, Vector3 forward)
    {
        if (!FinitePose(hip) || !FinitePose(ankle) || !FinitePose(forward)
            || !float.IsFinite(thigh) || !float.IsFinite(shin) || thigh < 0 || shin < 0)
            throw new ArgumentOutOfRangeException(nameof(hip), "Crouch pose requires finite joints and nonnegative lengths.");
        Vector3 axis = ankle - hip;
        float distance = axis.Length;
        if (distance < 1e-6f || thigh < 1e-6f || shin < 1e-6f) return hip;
        axis /= distance;
        Vector3 pole = forward - axis * Vector3.Dot(forward, axis);
        if (pole.LengthSquared < 1e-8f) pole = Vector3.Cross(axis, Vector3.UnitX);
        if (pole.LengthSquared < 1e-8f) pole = Vector3.Cross(axis, Vector3.UnitZ);
        pole.Normalize();
        float along = (thigh * thigh - shin * shin + distance * distance) / (2 * distance);
        along = Math.Clamp(along, -thigh, thigh);
        float height = MathF.Sqrt(Math.Max(0, thigh * thigh - along * along));
        return hip + axis * along + pole * height;
    }

    private static Matrix4 MoveBone(Vector3 origin, Vector3 target, Vector3 before, Vector3 after)
    {
        Matrix4 rotation = Matrix4.Identity;
        if (before.LengthSquared > 1e-8f && after.LengthSquared > 1e-8f)
        {
            before.Normalize(); after.Normalize();
            Vector3 axis = Vector3.Cross(before, after);
            float length = axis.Length;
            float dot = Math.Clamp(Vector3.Dot(before, after), -1, 1);
            if (length > 1e-6f)
                rotation = Matrix4.CreateFromQuaternion(Quaternion.FromAxisAngle(axis / length, MathF.Atan2(length, dot)));
            else if (dot < 0)
            {
                axis = Vector3.Cross(before, MathF.Abs(before.X) < .9f ? Vector3.UnitX : Vector3.UnitZ).Normalized();
                rotation = Matrix4.CreateFromQuaternion(Quaternion.FromAxisAngle(axis, MathF.PI));
            }
        }
        return Matrix4.CreateTranslation(-origin) * rotation * Matrix4.CreateTranslation(target);
    }

    private static bool FinitePose(Vector3 value) => float.IsFinite(value.X)
        && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private sealed class CrouchSkeleton
    {
        internal readonly int[] Left, Right, Groups;
        internal readonly Vector3 LeftFoot, RightFoot;
        internal readonly bool Valid;
        internal CrouchSkeleton(Model model)
        {
            int Find(string name)
            {
                for (int index = 0; index < model.Nodes.Count; index++)
                    if (model.Nodes[index].Name == name) return index;
                return -1;
            }
            Left = new[] { Find("L_hip"), Find("L_knee"), Find("L_ankle") };
            Right = new[] { Find("R_hip"), Find("R_knee"), Find("R_ankle") };
            Valid = Array.TrueForAll(Left, index => index >= 0) && Array.TrueForAll(Right, index => index >= 0)
                && model.AnimationGroups.Node.Count > (int)PlayerAnimation.Idle
                && model.AnimationGroups.Node[(int)PlayerAnimation.Idle].Count > 0;
            Groups = new int[model.Nodes.Count];
            if (!Valid) return;
            // Read neutral feet once from the native idle clip, restoring all
            // shared scratch nodes before the current pose continues. Never
            // derive a neutral anchor from an arbitrary first walking frame.
            var saved = new Matrix4[model.Nodes.Count];
            for (int index = 0; index < saved.Length; index++) saved[index] = model.Nodes[index].Animation;
            try
            {
                var idle = new AnimationInfo();
                idle.Node.Group = model.AnimationGroups.Node[(int)PlayerAnimation.Idle];
                model.AnimateNodes(0, false, Matrix4.Identity, Vector3.One, idle);
                LeftFoot = model.Nodes[Left[2]].Animation.Row3.Xyz;
                RightFoot = model.Nodes[Right[2]].Animation.Row3.Xyz;
            }
            finally
            {
                for (int index = 0; index < saved.Length; index++) model.Nodes[index].Animation = saved[index];
            }
            for (int index = 0; index < Groups.Length; index++)
                for (int ancestor = index; ancestor >= 0; ancestor = model.Nodes[ancestor].ParentIndex)
                {
                    int left = Array.IndexOf(Left, ancestor), right = Array.IndexOf(Right, ancestor);
                    if (left >= 0) { Groups[index] = left + 1; break; }
                    if (right >= 0) { Groups[index] = right + 4; break; }
                }
        }
    }
}
