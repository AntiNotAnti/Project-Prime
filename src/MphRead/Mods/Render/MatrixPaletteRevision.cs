using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace MphRead.Mods.Render;

/// <summary>Versions change at the only model-palette write boundary, including replay restore.</summary>
internal static class MatrixPaletteRevision
{
    private sealed class Version { internal long Value; }
    private static readonly ConditionalWeakTable<object, Version> Versions = new();
    internal static readonly bool Enabled = OpenGlOptimizationPolicy.Enabled("UNIFORM_REVISIONS");
    internal static void Changed(object source)
    { if (Enabled) Versions.GetOrCreateValue(source).Value++; }
    internal static long Read(object source)
        => Enabled && Versions.TryGetValue(source, out var version) ? version.Value : 0;
}

/// <summary>
/// Last known array upload. A stamp describes the immutable render-item snapshot,
/// never a mutable array address on its own. Unknown and ordinary matrix writes
/// invalidate it, as do program switches, relinks and context recreation.
/// </summary>
internal sealed class MatrixPaletteUploadCache
{
    private object? _source;
    private long _revision;
    private int _program, _location, _count;
    private float _scale;
    private readonly Dictionary<int, int[]> _layouts = new();
    private const int MaxLayouts = 256;
    internal void RegisterLayout(int program, params int[] independentMatrices)
    {
        if (program <= 0) return;
        if (_program == program) Invalidate();
        if (!_layouts.ContainsKey(program) && _layouts.Count >= MaxLayouts) _layouts.Clear();
        _layouts[program] = (int[])independentMatrices.Clone();
    }
    internal void ForgetLayout(int program)
    { _layouts.Remove(program); if (_program == program) Invalidate(); }
    internal void ForgetLayout() { Invalidate(); _layouts.Clear(); }
    internal void MatrixWrite(int program, int location)
    {
        // These are separately declared uniforms resolved by the shader owner,
        // not guessed offsets within the matrix palette's location range.
        if (program > 0 && location >= 0 && _layouts.TryGetValue(program, out var independent) && Array.IndexOf(independent, location) >= 0) return;
        Invalidate();
    }
    internal void Invalidate() => _source = null;
    internal bool Matches(int program, int location, int count, object? source, long revision, float scale)
        => program > 0 && location >= 0 && source != null && revision > 0
        && ReferenceEquals(source, _source) && revision == _revision && program == _program
        && location == _location && count == _count && scale.Equals(_scale);
    internal void Record(int program, int location, int count, object? source, long revision, float scale)
    { _program = program; _location = location; _count = count; _source = source; _revision = revision; _scale = scale; }
}
