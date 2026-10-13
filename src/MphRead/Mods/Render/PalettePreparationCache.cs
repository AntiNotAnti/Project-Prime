using System;
using System.Collections.Generic;

namespace MphRead.Mods.Render;

/// <summary>
/// Collection-local native palette arithmetic only. Each render item keeps its
/// own existing snapshot; this scratch is never exposed to shaders or items.
/// Known revisions require the existing native write-boundary contract.
/// </summary>
internal sealed class PalettePreparationCache
{
    internal const int MaximumMatrices = 32;
    private readonly float[] _scaled = new float[MaximumMatrices * 16];
    private IReadOnlyList<float>? _source;
    private long _revision;
    private int _count, _scaleBits;

    internal void Clear()
    {
        _source = null; _revision = 0; _count = _scaleBits = 0;
    }

    internal bool TryCopy(IReadOnlyList<float> source, long revision, int matrixCount,
        float scale, float[] destination, out bool reused)
    {
        reused = false;
        int floats = matrixCount * 16;
        if (revision <= 0 || matrixCount <= 0 || matrixCount > MaximumMatrices
            || !float.IsFinite(scale) || source.Count != floats || destination.Length < floats)
        { Clear(); return false; }
        int scaleBits = BitConverter.SingleToInt32Bits(scale);
        reused = ReferenceEquals(_source, source) && _revision == revision
            && _count == matrixCount && _scaleBits == scaleBits;
        if (!reused)
        {
            // A failed source read must not leave an old identity eligible.
            Clear();
            for (int i = 0; i < floats; i++)
            {
                int component = i - (i / 16) * 16;
                float factor = component < 12 && (component & 3) != 3 ? scale : 1f;
                _scaled[i] = source[i] * factor;
            }
        }
        Array.Copy(_scaled, destination, floats);
        _source = source; _revision = revision; _count = matrixCount; _scaleBits = scaleBits;
        return true;
    }

    internal bool HasSource => _source != null;
}
