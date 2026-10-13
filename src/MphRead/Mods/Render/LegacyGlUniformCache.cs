#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

/// <summary>
/// Program-scoped cache for the legacy desktop OpenGL uniform surface.
///
/// The cache deliberately covers only cheap scalar/vector/single-matrix writes.
/// Array uploads (bone/matrix stacks and lookup tables) are measured but always
/// submitted: hashing large mutable arrays costs enough to erase the win and
/// makes ownership much harder to reason about.
/// </summary>
internal sealed class LegacyGlUniformCache
{
    private readonly Dictionary<long, int> _ints = new();
    private readonly Dictionary<long, int> _floats = new();
    private readonly Dictionary<long, Vector2> _vec2 = new();
    private readonly Dictionary<long, Vector3> _vec3 = new();
    private readonly Dictionary<long, Vector4> _vec4 = new();
    private readonly Dictionary<long, Int4> _ivec4 = new();
    private readonly Dictionary<long, MatrixValue> _mat4 = new();

    private sealed record MatrixArrayLayout(int[] Elements, int[] Independent);
    private readonly Dictionary<int, MatrixArrayLayout> _matrixLayouts = new();
    private readonly List<long> _matrixRemovals = new();
    private int _program;
    private bool _measuring;
    private long _requested;
    private long _submitted;
    private long _skipped;

    private readonly record struct Int4(int X, int Y, int Z, int W);
    private readonly record struct MatrixValue(Matrix4 Value, bool Transpose);

    internal int CurrentProgram => _program;

    internal void UseProgram(int program) => _program = program;

    internal void InvalidateAll(bool preserveLayouts = false)
    {
        _ints.Clear();
        _floats.Clear();
        _vec2.Clear();
        _vec3.Clear();
        _vec4.Clear();
        _ivec4.Clear();
        _mat4.Clear();
        if (!preserveLayouts) _matrixLayouts.Clear();
        _matrixRemovals.Clear();
    }

    internal void InvalidateProgram(int program)
    {
        _matrixLayouts.Remove(program);
        // All cached values may be invalidated conservatively, but unrelated
        // linked programs retain their independently verified matrix layouts.
        InvalidateAll(preserveLayouts: true);
    }

    internal void ResetContext()
    {
        _program = 0;
        InvalidateAll();
    }

    internal void BeginSample()
    {
        _requested = _submitted = _skipped = 0;
        _measuring = true;
    }

    internal LegacyUniformSample EndSample()
    {
        _measuring = false;
        return new LegacyUniformSample(_requested, _submitted, _skipped);
    }

    internal void NoteUncachedWrite()
    {
        if (!_measuring) return;
        _requested++;
        _submitted++;
    }

    // Array uploads overwrite values which may previously have been submitted
    // through a cached single-value overload (notably mtx_stack[0]). Uniform
    // locations are driver-owned; invalidate the matching value type rather
    // than guessing the locations occupied by an array. Other types stay hot.
    internal void InvalidateFloatArray() => _floats.Clear();
    internal void InvalidateVector3Array() => _vec3.Clear();
    internal void InvalidateMatrix4Array() => _mat4.Clear();

    // Exact locations are resolved by the linked shader owner once. Never infer
    // an array's location range from its base or assume matrix location strides.
    internal bool RegisterMatrixArrayLayout(int program, int[] elements, int[] independent)
    {
        _matrixLayouts.Remove(program);
        if (program <= 0 || elements.Length == 0 || independent.Length == 0) return false;
        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i] < 0) return false;
            for (int j = 0; j < i; j++) if (elements[j] == elements[i]) return false;
            if (Array.IndexOf(independent, elements[i]) >= 0) return false;
        }
        if (!Array.Exists(independent, location => location >= 0)) return false;
        _matrixLayouts[program] = new MatrixArrayLayout((int[])elements.Clone(), (int[])independent.Clone());
        return true;
    }

    internal void InvalidateMatrix4Array(int location, int count)
    {
        if (!_matrixLayouts.TryGetValue(_program, out MatrixArrayLayout? layout))
        { InvalidateMatrix4Array(); return; }
        int first = Array.IndexOf(layout.Elements, location);
        if (first < 0 || count <= 0 || count > layout.Elements.Length - first)
        { InvalidateMatrix4Array(); return; }
        _matrixRemovals.Clear();
        foreach (long key in _mat4.Keys)
        {
            // Keep only explicitly independent state for the currently written
            // program. Unknown locations/programs preserve the ordinary fallback.
            if ((int)(key >> 32) != _program || Array.IndexOf(layout.Independent, (int)key) < 0)
                _matrixRemovals.Add(key);
        }
        foreach (long key in _matrixRemovals) _mat4.Remove(key);
        _matrixRemovals.Clear();
    }

    internal bool Submit(int location, int value) =>
        SubmitValue(_ints, location, value);

    internal bool Submit(int location, float value) =>
        SubmitValue(_floats, location, BitConverter.SingleToInt32Bits(value));

    internal bool Submit2(int location, float x, float y) =>
        SubmitValue(_vec2, location, new Vector2(x, y));

    internal bool Submit3(int location, Vector3 value) =>
        SubmitValue(_vec3, location, value);

    internal bool Submit4(int location, Vector4 value) =>
        SubmitValue(_vec4, location, value);

    internal bool Submit4(int location, int x, int y, int z, int w) =>
        SubmitValue(_ivec4, location, new Int4(x, y, z, w));

    internal bool SubmitMatrix4(int location, bool transpose, in Matrix4 value) =>
        SubmitValue(_mat4, location, new MatrixValue(value, transpose));

    private bool SubmitValue<T>(Dictionary<long, T> values, int location, T value)
        where T : struct
    {
        // Preserve OpenGL error behavior if a caller somehow writes without a
        // current program or uses a missing location. The optimization applies
        // only to valid program-local state.
        if (_program <= 0 || location < 0)
        {
            Count(submitted: true);
            return true;
        }

        long key = ((long)_program << 32) | (uint)location;
        if (values.TryGetValue(key, out T prior)
            && EqualityComparer<T>.Default.Equals(prior, value))
        {
            Count(submitted: false);
            return false;
        }
        values[key] = value;
        Count(submitted: true);
        return true;
    }

    private void Count(bool submitted)
    {
        if (!_measuring) return;
        _requested++;
        if (submitted) _submitted++;
        else _skipped++;
    }
}

internal readonly record struct LegacyUniformSample(long Requested, long Submitted, long Skipped)
{
    public double SkipPercent => Requested == 0 ? 0 : Skipped * 100.0 / Requested;
}
#endif
