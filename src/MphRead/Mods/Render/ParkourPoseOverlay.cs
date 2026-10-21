using System;
using System.Runtime.CompilerServices;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

// Native and weighted replacement bodies borrow this palette for submission
// only. The shooting branch is untouched, and disposal restores the exact
// input matrices even if material submission throws or a scope is disposed twice.
internal static class ParkourPoseOverlay
{
    private static readonly ConditionalWeakTable<Model, Profile> Profiles = new();
    internal static bool SuppressForCheck;
    internal static void Prewarm(Model model) => Profiles.GetValue(model, m => new(m));
    internal static Scope Begin(Model model, Matrix4 world, ParkourAnimationPose pose)
    {
        if (SuppressForCheck || !pose.Active || (pose.MantleWeight <= 0 && pose.GrappleAmount <= 0)
            || !float.IsFinite(pose.MantleProgress)
            || !float.IsFinite(pose.GrappleAmount) || !double.IsFinite(pose.Seconds)
            || !Profiles.TryGetValue(model, out var profile) || !profile.Valid || profile.InUse)
            return default;
        return new(profile, world, pose);
    }

    internal sealed class Profile
    {
        internal readonly Model Model;
        internal readonly Matrix4[] Saved;
        internal readonly int Spine, LeftShoulder, LeftElbow, LeftWrist, RightShoulder;
        internal readonly int[] LeftLeg, RightLeg, Groups;
        internal readonly bool[] Torso, Weapon;
        internal readonly bool Valid;
        internal bool InUse;
        internal ulong Version;
        internal Profile(Model model)
        {
            Model = model;
            int count = model.Nodes.Count;
            Saved = new Matrix4[count <= 128 ? count : 0];
            Groups = new int[Saved.Length]; Torso = new bool[Saved.Length]; Weapon = new bool[Saved.Length];
            int Find(string name)
            { for (int i = 0; i < Saved.Length; i++) if (model.Nodes[i].Name == name) return i; return -1; }
            Spine = Find("Spine_1"); LeftShoulder = Find("L_shoulder"); LeftElbow = Find("L_elbow");
            LeftWrist = Find("L_wrist"); RightShoulder = Find("R_shoulder");
            LeftLeg = [Find("L_hip"), Find("L_knee"), Find("L_ankle")];
            RightLeg = [Find("R_hip"), Find("R_knee"), Find("R_ankle")];
            Valid = Saved.Length > 0 && Spine >= 0 && LeftShoulder >= 0 && LeftElbow >= 0 && RightShoulder >= 0
                && Array.TrueForAll(LeftLeg, i => i >= 0) && Array.TrueForAll(RightLeg, i => i >= 0);
            if (!Valid) return;
            for (int i = 0; i < Saved.Length; i++)
            {
                for (int a = i, depth = 0; a >= 0 && a < count && depth < count;
                    a = model.Nodes[a].ParentIndex, depth++)
                {
                    if (a == RightShoulder) Weapon[i] = true;
                    if (a == Spine) Torso[i] = true;
                    if (Groups[i] == 0)
                    {
                        int l = Array.IndexOf(LeftLeg, a), r = Array.IndexOf(RightLeg, a);
                        if (a == LeftElbow) Groups[i] = 2;
                        else if (a == LeftShoulder) Groups[i] = 1;
                        else if (l >= 0) Groups[i] = 3 + l;
                        else if (r >= 0) Groups[i] = 6 + r;
                    }
                }
            }
        }
        internal void Restore()
        {
            try
            { for (int i = 0; i < Saved.Length; i++) Model.Nodes[i].Animation = Saved[i]; Model.UpdateMatrixStack(); }
            finally { InUse = false; }
        }
    }

    internal readonly struct Scope : IDisposable
    {
        private readonly Profile? _profile;
        private readonly ulong _version;
        internal Scope(Profile profile, Matrix4 world, ParkourAnimationPose pose)
        {
            _profile = profile; _version = ++profile.Version; profile.InUse = true;
            for (int i = 0; i < profile.Saved.Length; i++) profile.Saved[i] = profile.Model.Nodes[i].Animation;
            try { Apply(profile, world, pose); }
            catch { profile.Restore(); throw; }
        }
        public void Dispose()
        { if (_profile is { InUse: true } p && p.Version == _version) p.Restore(); }
    }

    private static void Apply(Profile p, Matrix4 world, ParkourAnimationPose pose)
    {
        Vector3 forward = Unit(-world.Row2.Xyz.WithY(0), Vector3.UnitZ);
        Vector3 right = new(forward.Z, 0, -forward.X);
        float mantle = pose.MantleWeight, grapple = Math.Clamp(pose.GrappleAmount, 0, 1);
        float progress = Math.Clamp(pose.MantleProgress, 0, 1);
        Vector3 hip = (p.Saved[p.LeftLeg[0]].Row3.Xyz + p.Saved[p.RightLeg[0]].Row3.Xyz) * .5f;
        float lean = mantle * MathHelper.DegreesToRadians(13) * MathF.Sin(progress * MathF.PI)
            - grapple * MathHelper.DegreesToRadians(5);
        Matrix4 torso = Around(hip, right, lean);
        for (int i = 0; i < p.Saved.Length; i++)
            if (p.Torso[i] && !p.Weapon[i]) p.Model.Nodes[i].Animation = p.Saved[i] * torso;

        Vector3 shoulder = p.Model.Nodes[p.LeftShoulder].Animation.Row3.Xyz;
        Vector3 elbow = p.Model.Nodes[p.LeftElbow].Animation.Row3.Xyz;
        Vector3 hand = p.LeftWrist >= 0 ? p.Model.Nodes[p.LeftWrist].Animation.Row3.Xyz
            : elbow + Unit(elbow - shoulder, forward) * ((elbow - shoulder).Length * .8f);
        float reach = (elbow - shoulder).Length + (hand - elbow).Length;
        float pull = ParkourAnimationPose.Smooth((progress - .2f) / .65f);
        Vector3 mantleTarget = shoulder + forward * (reach * (.83f - .58f * pull))
            + Vector3.UnitY * (reach * (.36f - .6f * pull));
        Vector3 rope = Unit(pose.RopeDirection, forward + Vector3.UnitY);
        Vector3 target = Vector3.Lerp(hand, mantleTarget, mantle);
        target = Vector3.Lerp(target, shoulder + rope * (reach * .97f), grapple);
        Solve(shoulder, elbow, hand, target, BlendPole(shoulder, elbow, hand,
                -right + forward * .25f, Math.Max(mantle, grapple)),
            out Matrix4 upper, out Matrix4 lower);
        for (int i = 0; i < p.Saved.Length; i++)
        {
            Matrix4 current = p.Model.Nodes[i].Animation;
            if (p.Groups[i] == 1) current *= upper;
            else if (p.Groups[i] == 2) current *= lower;
            p.Model.Nodes[i].Animation = current;
        }
        float lift = MathF.Sin(progress * MathF.PI) * mantle;
        float swing = (float)Math.Sin(pose.Seconds * 4) * .025f * grapple;
        Leg(p, p.LeftLeg, 3, forward, lift * .3f + grapple * .2f, lift * .22f + swing, Math.Max(mantle, grapple));
        Leg(p, p.RightLeg, 6, forward, lift * .12f + grapple * .16f, -lift * .1f - swing, Math.Max(mantle, grapple));
        p.Model.UpdateMatrixStack();
    }

    private static void Leg(Profile p, int[] chain, int group, Vector3 forward, float lift, float travel, float weight)
    {
        Vector3 hip = p.Saved[chain[0]].Row3.Xyz, knee = p.Saved[chain[1]].Row3.Xyz,
            ankle = p.Saved[chain[2]].Row3.Xyz;
        float reach = (knee - hip).Length + (ankle - knee).Length;
        Solve(hip, knee, ankle, ankle + Vector3.UnitY * (reach * lift) + forward * (reach * travel),
            BlendPole(hip, knee, ankle, forward, weight), out Matrix4 upper, out Matrix4 lower);
        for (int i = 0; i < p.Saved.Length; i++)
        {
            if (p.Groups[i] == group) p.Model.Nodes[i].Animation = p.Saved[i] * upper;
            else if (p.Groups[i] is var g && (g == group + 1 || g == group + 2))
                p.Model.Nodes[i].Animation = p.Saved[i] * lower;
        }
    }

    private static Vector3 BlendPole(Vector3 a, Vector3 b, Vector3 c, Vector3 desired, float weight)
    {
        Vector3 axis = Unit(c - a, Vector3.UnitY);
        Vector3 native = b - a - axis * Vector3.Dot(b - a, axis);
        return Vector3.Lerp(Unit(native, desired), Unit(desired, native), Math.Clamp(weight, 0, 1));
    }

    private static void Solve(Vector3 a, Vector3 b, Vector3 c, Vector3 target, Vector3 pole,
        out Matrix4 upper, out Matrix4 lower)
    {
        float length1 = (b - a).Length, length2 = (c - b).Length;
        upper = lower = Matrix4.Identity;
        if (length1 < .00001f || length2 < .00001f || !Finite(target)) return;
        Vector3 axis = Unit(target - a, c - a);
        float distance = Math.Clamp((target - a).Length,
            MathF.Abs(length1 - length2) + .00001f, length1 + length2 - .00001f);
        target = a + axis * distance;
        float along = (length1 * length1 - length2 * length2 + distance * distance) / (2 * distance);
        Vector3 bend = pole - axis * Vector3.Dot(pole, axis);
        bend = Unit(bend, Vector3.Cross(axis, MathF.Abs(axis.Y) < .9f ? Vector3.UnitY : Vector3.UnitZ));
        Vector3 joint = a + axis * along + bend * MathF.Sqrt(MathF.Max(0, length1 * length1 - along * along));
        upper = Move(a, a, b - a, joint - a);
        lower = Move(b, joint, c - b, target - joint);
    }
    private static Matrix4 Move(Vector3 origin, Vector3 target, Vector3 from, Vector3 to)
    {
        from = Unit(from, Vector3.UnitY); to = Unit(to, from);
        float dot = Math.Clamp(Vector3.Dot(from, to), -1, 1);
        Vector3 axis = Vector3.Cross(from, to);
        if (axis.LengthSquared < 1e-10f)
            return dot > 0 ? Matrix4.CreateTranslation(target - origin)
                : Matrix4.CreateTranslation(-origin) * Matrix4.CreateFromAxisAngle(
                    Unit(Vector3.Cross(from, Vector3.UnitX), Vector3.UnitZ), MathF.PI) * Matrix4.CreateTranslation(target);
        return Matrix4.CreateTranslation(-origin) * Matrix4.CreateFromAxisAngle(axis.Normalized(), MathF.Acos(dot))
            * Matrix4.CreateTranslation(target);
    }
    private static Matrix4 Around(Vector3 pivot, Vector3 axis, float angle)
        => Matrix4.CreateTranslation(-pivot) * Matrix4.CreateFromAxisAngle(axis, angle) * Matrix4.CreateTranslation(pivot);
    private static Vector3 Unit(Vector3 value, Vector3 fallback)
        => Finite(value) && value.LengthSquared > 1e-10f ? value.Normalized()
            : Finite(fallback) && fallback.LengthSquared > 1e-10f ? fallback.Normalized() : Vector3.UnitZ;
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
