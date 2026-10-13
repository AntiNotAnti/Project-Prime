using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

// Model.Nodes membership and Node.MinBounds/MaxBounds are constructor-owned.
// Values contain no model/node references; the weak key owns their lifetime.
internal sealed class ShadowCasterSpatialGroups
{
    internal const int NodesPerGroup = 16;
    internal readonly record struct Bounds(Vector3 Min, Vector3 Max, bool Known);
    private static readonly ConditionalWeakTable<Model, ShadowCasterSpatialGroups> Cache = new();
    internal Bounds[] Groups { get; }
    internal static ShadowCasterSpatialGroups For(Model model)
        => Cache.GetValue(model, static owner => new(owner.Nodes));
    private ShadowCasterSpatialGroups(IReadOnlyList<Node> nodes)
    {
        Groups = new Bounds[(nodes.Count + NodesPerGroup - 1) / NodesPerGroup];
        for (int group = 0; group < Groups.Length; group++)
        {
            Bounds bounds = new(new Vector3(float.PositiveInfinity), new Vector3(float.NegativeInfinity), true);
            int end = Math.Min(nodes.Count, (group + 1) * NodesPerGroup);
            for (int i = group * NodesPerGroup; i < end; i++) bounds = Extend(bounds, nodes[i].MinBounds, nodes[i].MaxBounds);
            Groups[group] = bounds;
        }
    }
    internal static Bounds Extend(Bounds union, Vector3 min, Vector3 max)
    {
        bool known = union.Known && float.IsFinite(min.X) && float.IsFinite(min.Y) && float.IsFinite(min.Z)
            && float.IsFinite(max.X) && float.IsFinite(max.Y) && float.IsFinite(max.Z)
            && min.X <= max.X && min.Y <= max.Y && min.Z <= max.Z;
        return known ? new(Vector3.ComponentMin(union.Min, min), Vector3.ComponentMax(union.Max, max), true)
            : new(default, default, false);
    }
    internal static bool Visible(Bounds bounds, Matrix4 projection)
        => !bounds.Known || Entities.RoomEntity.ShadowCasterOverlapsLightXY(bounds.Min, bounds.Max, projection);
    internal static int Check()
    {
        int failures = 0;
        void Check(bool value, string name)
        { Console.WriteLine($"[shadowgroupscheck] {(value ? "PASS" : "FAIL")}: {name}"); if (!value) failures++; }
        Bounds empty = new(new Vector3(float.PositiveInfinity), new Vector3(float.NegativeInfinity), true);
        var left = Extend(empty, new Vector3(-8, -1, -1), new Vector3(-6, 1, 1));
        var right = Extend(empty, new Vector3(6, -1, -1), new Vector3(8, 1, 1));
        Check(!Visible(left, Matrix4.Identity) && !Visible(right, Matrix4.Identity), "fully outside group can reject its original nodes");
        var crossing = Extend(left, right.Min, right.Max);
        Check(Visible(crossing, Matrix4.Identity), "union spanning viewport remains conservative even with outside members");
        var edge = Extend(empty, new Vector3(3, 0, 0), new Vector3(3, 1, 1));
        Check(Visible(edge, Matrix4.Identity), "existing two-unit margin retains exact light boundary");
        Check(Visible(Extend(left, new Vector3(float.NaN), Vector3.One), Matrix4.Identity), "unknown member makes whole cluster visible");
        Check(Visible(Extend(left, Vector3.One, Vector3.Zero), Matrix4.Identity), "inverted member cannot hide any caster");
        Check(Visible(Extend(left, new Vector3(float.PositiveInfinity), Vector3.One), Matrix4.Identity), "nonfinite member conservatively retains cluster");
        Check(!Extend(Extend(left, new Vector3(float.NaN), Vector3.One), right.Min, right.Max).Known,
            "later valid bounds cannot repair an unknown union");
        Matrix4 behind = Matrix4.Identity; behind.M44 = -1;
        Check(Visible(left, behind), "nonpositive projection W never rejects a group");
        Check(crossing.Min.X == -8 && crossing.Max.X == 8 && crossing.Known, "union exactly encloses every immutable member bound");
        return failures;
    }
}
