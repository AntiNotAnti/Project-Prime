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
        float slide = walkingGait ? ModSlideVisualAmount * amount : 0;
        float alpha = Mods.Render.FrameTiming.HighRefreshPresentation
            ? (float)Mods.Render.FrameTiming.PresentationAlpha : 1;
        var walk = walkingGait ? _modCrouchWalk.Sample(alpha) : default;
        float walkAmount = walk.Amount * amount;
        Vector2 heading = _modSlidePresentation.SampleDirection(alpha);
        Vector3 travel = new(heading.X, 0, heading.Y);
        Vector3 leftHip = model.Nodes[skeleton.Left[0]].Animation.Row3.Xyz;
        Vector3 rightHip = model.Nodes[skeleton.Right[0]].Animation.Row3.Xyz;
        float reach = (model.Nodes[skeleton.Left[1]].Animation.Row3.Xyz - leftHip).Length
            + (model.Nodes[skeleton.Left[2]].Animation.Row3.Xyz
                - model.Nodes[skeleton.Left[1]].Animation.Row3.Xyz).Length;
        Vector3 bodyRight = new(forward.Z, 0, -forward.X);
        Vector3 shift = -travel * (reach * .12f * slide);
        if (walkAmount > 0)
        {
            float cycle = walk.Phase * MathF.Tau;
            shift += bodyRight * (-.025f * _modCrouchWalkReach * MathF.Sin(cycle) * walkAmount * (1 - slide));
            shift.Y -= .005f * _modCrouchWalkReach * (1 - MathF.Cos(cycle * 2)) * walkAmount * (1 - slide);
        }
        Vector3 pivot = (leftHip + rightHip) * .5f;
        Matrix4 lean = Matrix4.CreateFromQuaternion(Quaternion.FromAxisAngle(
            new Vector3(travel.Z, 0, -travel.X), -MathHelper.DegreesToRadians(12) * slide));
        Matrix4 upperPose = Matrix4.CreateTranslation(-pivot) * lean
            * Matrix4.CreateTranslation(pivot + shift - Vector3.UnitY * drop);
        // Lean the torso while the cannon branch retains its native aim rotation.
        // Its elbow follows the body; only its orientation is counter-rotated.
        Matrix4 aimCorrection = Matrix4.Identity;
        if (slide > 0 && skeleton.AimRoot >= 0)
        {
            Vector3 elbow = Matrix.Vec3MultMtx4(model.Nodes[skeleton.AimRoot].Animation.Row3.Xyz, upperPose);
            aimCorrection = Matrix4.CreateTranslation(-elbow) * Matrix4.Transpose(lean)
                * Matrix4.CreateTranslation(elbow);
        }
        Vector3 FootTarget(int[] bones, Vector3 neutral)
        {
            Vector3 ankle = model.Nodes[bones[2]].Animation.Row3.Xyz;
            if (!walkingGait) return ankle;
            neutral = Matrix.Vec3MultMtx4(neutral, world);
            // Neutral stance width is Hunter/LOD-specific. Accepted world travel
            // drives a long support phase and low swing, independent of aim yaw.
            Vector2 step = Mods.Movement.TraversalWalkPresentation.Step(walk.Phase,
                bones == skeleton.Right, _modCrouchWalkReach);
            Vector3 displacement = walk.Direction * step.X;
            if (walkAmount > 0)
                displacement.Y = -(displacement.X * walk.Normal.X + displacement.Z * walk.Normal.Z) / walk.Normal.Y;
            displacement.Y += step.Y;
            Vector3 walking = Vector3.Lerp(ankle, neutral + displacement, walkAmount);
            bool extended = bones == skeleton.Left;
            Vector3 glide = neutral + travel * (reach * (extended ? .35f : -.15f));
            return Vector3.Lerp(walking, glide, slide);
        }
        var left = Leg(model, skeleton.Left, drop, forward, FootTarget(skeleton.Left, skeleton.LeftFoot),
            shift, walkingGait, walkAmount > 0 ? walk.Normal : null,
            Matrix.Vec3MultMtx4(skeleton.LeftFoot, world), Math.Max(walkAmount, slide));
        var right = Leg(model, skeleton.Right, drop, forward, FootTarget(skeleton.Right, skeleton.RightFoot),
            shift, walkingGait, walkAmount > 0 ? walk.Normal : null,
            Matrix.Vec3MultMtx4(skeleton.RightFoot, world), Math.Max(walkAmount, slide));
        for (int index = 0; index < model.Nodes.Count; index++)
        {
            Node node = model.Nodes[index];
            Matrix4 pose = node.Animation;
            int group = skeleton.Groups[index];
            if (group == 0)
            {
                if (slide <= 0)
                {
                    pose.Row3.Y -= drop;
                    if (walkingGait) pose.Row3.Xyz += shift;
                }
                else
                {
                    pose *= upperPose;
                    if (skeleton.AimBranch[index]) pose *= aimCorrection;
                }
            }
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
        Vector3 forward, Vector3 targetAnkle, Vector3 shift = default, bool constrain = false,
        Vector3? floorNormal = null, Vector3 floorPoint = default, float constraintAmount = 1)
    {
        Vector3 hip = model.Nodes[bones[0]].Animation.Row3.Xyz;
        Vector3 knee = model.Nodes[bones[1]].Animation.Row3.Xyz;
        Vector3 ankle = model.Nodes[bones[2]].Animation.Row3.Xyz;
        Vector3 lowered = hip - Vector3.UnitY * drop;
        // Preserve the canonical path's original arithmetic exactly; even a
        // zero cosmetic translation is applied only to the visual solve.
        if (constrain) lowered += shift;
        float thigh = (knee - hip).Length, shin = (ankle - knee).Length;
        // Keep cosmetic targets within reach without lifting a planted ankle.
        // This also bounds extension on Hunters with shorter native legs.
        float vertical = targetAnkle.Y - lowered.Y;
        float maximum = MathF.Max(.001f, thigh + shin - .0001f);
        Vector3 planar = (targetAnkle - lowered).WithY(0);
        float horizontalReach = MathF.Sqrt(MathF.Max(0, maximum * maximum - vertical * vertical));
        if (constrain && planar.LengthSquared > horizontalReach * horizontalReach && planar.LengthSquared > 1e-12f)
            targetAnkle = Vector3.Lerp(targetAnkle, new Vector3(lowered.X, targetAnkle.Y, lowered.Z)
                + planar.Normalized() * horizontalReach, constraintAmount);
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
        Vector3 target = ModCrouchKnee(lowered, targetAnkle, thigh, shin, forward);
        if (floorNormal is { } normal)
        {
            Vector3 clear = ModCrouchKneeAboveFloor(lowered, targetAnkle, thigh, shin, forward, normal, floorPoint);
            if (constraintAmount >= 1) target = clear;
            else
            {
                // Turn around the fixed hip/ankle circle instead of linearly
                // pulling a knee through the leg. Floor correction fades with
                // the same envelope as the neutral lower pose and foot target.
                Vector3 line = targetAnkle - lowered;
                float distance = line.Length;
                if (distance > 1e-6f)
                {
                    line /= distance;
                    float along = Math.Clamp((thigh * thigh - shin * shin + distance * distance) / (2 * distance), -thigh, thigh);
                    Vector3 center = lowered + line * along;
                    Vector3 pole = target - center, desired = clear - center;
                    float angle = MathF.Atan2(Vector3.Dot(line, Vector3.Cross(pole, desired)), Vector3.Dot(pole, desired));
                    float turn = angle * constraintAmount;
                    target = center + pole * MathF.Cos(turn) + Vector3.Cross(line, pole) * MathF.Sin(turn);
                }
            }
        }
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

    // Adjust only the bend pole around the fixed hip/ankle circle. Joint
    // lengths stay exact while a deep low step keeps its knee above the floor.
    internal static Vector3 ModCrouchKneeAboveFloor(Vector3 hip, Vector3 ankle, float thigh, float shin,
        Vector3 forward, Vector3 normal, Vector3 floorPoint)
    {
        Vector3 knee = ModCrouchKnee(hip, ankle, thigh, shin, forward);
        if (Vector3.Dot(normal, knee - floorPoint) >= .015f) return knee;
        Vector3 axis = ankle - hip;
        float distance = axis.Length;
        if (distance < 1e-6f) return knee;
        axis /= distance;
        float along = Math.Clamp((thigh * thigh - shin * shin + distance * distance) / (2 * distance), -thigh, thigh);
        Vector3 center = hip + axis * along;
        float radius = MathF.Sqrt(Math.Max(0, thigh * thigh - along * along));
        Vector3 up = normal - axis * Vector3.Dot(normal, axis);
        float projected = up.Length;
        if (radius < 1e-6f || projected < 1e-6f) return knee;
        up /= projected;
        float required = Math.Clamp((.015f - Vector3.Dot(normal, center - floorPoint)) / (radius * projected), -1, 1);
        Vector3 pole = (knee - center) / radius;
        Vector3 side = pole - up * Vector3.Dot(pole, up);
        side = side.LengthSquared > 1e-8f ? side.Normalized() : Vector3.Cross(axis, up).Normalized();
        return center + radius * (up * required + side * MathF.Sqrt(Math.Max(0, 1 - required * required)));
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
        internal readonly float Reach;
        internal readonly int AimRoot;
        internal readonly bool[] AimBranch;
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
            AimRoot = Find("R_elbow");
            AimBranch = new bool[model.Nodes.Count];
            for (int index = 0; index < AimBranch.Length; index++)
                for (int ancestor = index; ancestor >= 0; ancestor = model.Nodes[ancestor].ParentIndex)
                    if (ancestor == AimRoot) { AimBranch[index] = true; break; }
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
                Reach = (model.Nodes[Left[1]].Animation.Row3.Xyz - model.Nodes[Left[0]].Animation.Row3.Xyz).Length
                    + (LeftFoot - model.Nodes[Left[1]].Animation.Row3.Xyz).Length;
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
