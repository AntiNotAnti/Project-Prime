using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

internal readonly record struct DisplayListAttributeEffect(
    bool WritesColor, Vector4 Color, bool WritesNormal, Vector3 Normal,
    bool WritesTexcoord, Vector3 Texcoord,
    bool ReadsInheritedNormal = false, bool ReadsInheritedTexcoord = false)
{
    // Compose in execution order; an untouched attribute inherits the prior state.
    internal DisplayListAttributeEffect Merge(DisplayListAttributeEffect next) => new(
        WritesColor || next.WritesColor, next.WritesColor ? next.Color : Color,
        WritesNormal || next.WritesNormal, next.WritesNormal ? next.Normal : Normal,
        WritesTexcoord || next.WritesTexcoord, next.WritesTexcoord ? next.Texcoord : Texcoord,
        ReadsInheritedNormal || next.ReadsInheritedNormal && !WritesNormal,
        ReadsInheritedTexcoord || next.ReadsInheritedTexcoord && !WritesTexcoord);
}

/// <summary>Context-owned explicit current-attribute effects; never queries GL state.</summary>
internal sealed class DisplayListAttributeEffects
{
    private sealed record Entry(DisplayListAttributeEffect Effect, HashSet<int>? Dependencies);
    internal const int DefaultMaxLists = 16384;
    private readonly int _maxLists;
    private readonly Dictionary<int, Entry> _known = new();
    private readonly List<int> _invalidations = new();
    private int _capturing;
    private bool _invalid;
    private DisplayListAttributeEffect _effect;
    private HashSet<int>? _dependencies;
    internal bool IsCapturing => _capturing != 0;
    internal DisplayListAttributeEffects(int maxLists = DefaultMaxLists)
    { if (maxLists < 0) throw new ArgumentOutOfRangeException(nameof(maxLists)); _maxLists = maxLists; }
    internal bool TryGet(int id, out DisplayListAttributeEffect effect)
    {
        if (_known.TryGetValue(id, out Entry? entry)) { effect = entry.Effect; return true; }
        effect = default; return false;
    }
    internal void Begin(int id)
    {
        if (IsCapturing) { _invalid = true; return; }
        Invalidate(id);
        _capturing = id > 0 ? id : 0;
        _invalid = id <= 0; _effect = default; _dependencies = null;
    }
    internal void End()
    {
        if (IsCapturing && !_invalid && _known.Count < _maxLists)
            _known[_capturing] = new Entry(_effect, _dependencies);
        _capturing = 0; _invalid = false; _dependencies = null; _effect = default;
    }
    internal void Color(Vector4 value)
    { if (IsCapturing) _effect = _effect.Merge(new(true, value, false, default, false, default)); }
    internal void Normal(Vector3 value)
    { if (IsCapturing) _effect = _effect.Merge(new(false, default, true, value, false, default)); }
    internal void Texcoord(Vector3 value)
    { if (IsCapturing) _effect = _effect.Merge(new(false, default, false, default, true, value)); }
    internal void Vertex()
    {
        if (IsCapturing) _effect = _effect with {
            ReadsInheritedNormal = _effect.ReadsInheritedNormal || !_effect.WritesNormal,
            ReadsInheritedTexcoord = _effect.ReadsInheritedTexcoord || !_effect.WritesTexcoord };
    }
    internal void Unknown() { if (IsCapturing) _invalid = true; }
    internal void Call(int id)
    {
        if (!IsCapturing) return;
        if (!TryGet(id, out var effect)) { Unknown(); return; }
        (_dependencies ??= new()).Add(id);
        _effect = _effect.Merge(effect);
    }
    internal void Delete(int first, int count)
    {
        if (count <= 0) return;
        long end = (long)first + count;
        _invalidations.Clear();
        foreach (int id in _known.Keys) if (id >= first && id < end) _invalidations.Add(id);
        // Unknown names can still be dependencies after a prior invalidation;
        // existing entries never retain those, but current capture can.
        if (IsCapturing && ((_capturing >= first && _capturing < end)
            || _dependencies != null && _dependencies.Overlaps(_invalidations))) Unknown();
        InvalidateQueued();
    }
    private void Invalidate(int id)
    {
        _invalidations.Clear(); _invalidations.Add(id); InvalidateQueued();
    }
    private void InvalidateQueued()
    {
        for (int i = 0; i < _invalidations.Count; i++)
        {
            int id = _invalidations[i]; _known.Remove(id);
            if (_dependencies?.Contains(id) == true) Unknown();
            foreach (var pair in _known)
                if (pair.Value.Dependencies?.Contains(id) == true && !_invalidations.Contains(pair.Key))
                    _invalidations.Add(pair.Key);
        }
        _invalidations.Clear();
    }
    internal static int Check()
    {
        int failures = 0;
        void Assert(bool condition, string name)
        {
            Console.WriteLine($"[displaylistattributecheck] {(condition ? "PASS" : "FAIL")}: {name}");
            if (!condition) failures++;
        }
        var tracker = new DisplayListAttributeEffects();
        Vector4 color = new(.2f, .4f, .6f, .8f);
        Vector3 normal = new(.1f, .2f, .3f), uv = new(.4f, .5f, 7);
        tracker.Begin(1); tracker.Color(color); tracker.Normal(normal); tracker.Texcoord(uv); tracker.Vertex(); tracker.End();
        Assert(tracker.TryGet(1, out var first) && first.WritesColor && first.Color == color
            && first.WritesNormal && first.Normal == normal && first.WritesTexcoord && first.Texcoord == uv
            && !first.ReadsInheritedNormal && !first.ReadsInheritedTexcoord,
            "explicit attributes precede vertices and retain exact final color/normal/3D UV");
        tracker.Color(Vector4.One); tracker.Normal(Vector3.One); tracker.Texcoord(Vector3.One); tracker.Vertex();
        Assert(tracker.TryGet(1, out var outside) && outside == first,
            "ordinary noncompile attribute writes never mutate stored list effects");
        tracker.Begin(2); tracker.Vertex(); tracker.Normal(Vector3.UnitX); tracker.Texcoord(Vector3.UnitY); tracker.End();
        Assert(tracker.TryGet(2, out var inherited) && inherited.ReadsInheritedNormal && inherited.ReadsInheritedTexcoord
            && !inherited.WritesColor && inherited.WritesNormal && inherited.WritesTexcoord,
            "vertex reads before late explicit writes remain inherited dependencies");
        tracker.Begin(3); tracker.Normal(normal); tracker.Texcoord(uv); tracker.Call(2); tracker.End();
        Assert(tracker.TryGet(3, out var parent) && !parent.ReadsInheritedNormal && !parent.ReadsInheritedTexcoord
            && parent.Normal == Vector3.UnitX && parent.Texcoord == Vector3.UnitY,
            "nested inherited reads resolve against preceding explicit parent attributes");
        tracker.Begin(4); tracker.Call(2); tracker.End();
        Assert(tracker.TryGet(4, out var unresolved) && unresolved.ReadsInheritedNormal && unresolved.ReadsInheritedTexcoord,
            "nested inherited reads propagate when parent has no explicit attributes");
        tracker.Begin(5); tracker.Call(1); tracker.Color(Vector4.One); tracker.End();
        Assert(tracker.TryGet(5, out var merged) && merged.Color == Vector4.One && merged.Normal == normal && merged.Texcoord == uv,
            "execution-order merge updates only explicitly touched final attributes");
        tracker.Begin(6); tracker.Call(5); tracker.End();
        tracker.Begin(1); tracker.Color(Vector4.Zero); tracker.End();
        Assert(!tracker.TryGet(5, out _) && !tracker.TryGet(6, out _) && tracker.TryGet(3, out _),
            "callee recompilation invalidates dependent parents transitively but preserves unrelated lists");
        tracker.Delete(2, 1);
        Assert(!tracker.TryGet(2, out _) && !tracker.TryGet(3, out _) && !tracker.TryGet(4, out _),
            "callee deletion invalidates every parent which cached its final effect");
        tracker.Begin(7); tracker.Call(999); tracker.Color(Vector4.One); tracker.End();
        Assert(!tracker.TryGet(7, out _), "unknown external nested list never becomes trusted metadata");
        tracker.Begin(8); tracker.Unknown(); tracker.Normal(normal); tracker.End();
        Assert(!tracker.TryGet(8, out _), "unknown attribute-stack/nonzero texture effects force fallback");
        tracker.Begin(9); tracker.End();
        Assert(tracker.TryGet(9, out var empty) && empty == default,
            "empty known list preserves untouched flags without invented initial values");
        tracker.Begin(10); tracker.Color(color); tracker.Reset(); tracker.End();
        Assert(!tracker.TryGet(1, out _) && !tracker.TryGet(9, out _) && !tracker.TryGet(10, out _),
            "context reset clears completed and partial list metadata");
        var bounded = new DisplayListAttributeEffects(1);
        bounded.Begin(1); bounded.End(); bounded.Begin(2); bounded.End();
        Assert(bounded.TryGet(1, out _) && !bounded.TryGet(2, out _), "metadata budget overflow uses unknown-list fallback");
        bounded.Begin(1); bounded.Normal(normal); bounded.End();
        Assert(bounded.TryGet(1, out var replacement) && replacement.Normal == normal,
            "recompilation reclaims its bounded metadata slot");
        return failures == 0 ? 0 : 1;
    }

    internal void Reset()
    { _known.Clear(); _invalidations.Clear(); _capturing = 0; _invalid = false; _effect = default; _dependencies = null; }
}
