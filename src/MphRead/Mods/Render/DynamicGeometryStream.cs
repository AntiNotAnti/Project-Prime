using System;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render;

/// <summary>Bounded reusable index storage and ordering barriers for adjacent streamed geometry.</summary>
internal sealed class DynamicGeometryStream
{
    internal const int DefaultBudgetBytes = 4 * 1024 * 1024;
    private readonly int _maxIndices;
    private int[] _indices = Array.Empty<int>();
    internal int ResidentBytes => _indices.Length * sizeof(int);

    internal static bool NeedsPrimitiveBarrier(PrimitiveType mode, int triangles, int lines)
    {
        bool line = mode is PrimitiveType.Lines or PrimitiveType.LineLoop or PrimitiveType.LineStrip;
        // The translator stores triangle and line indices separately. Never
        // allow concatenation to move a line across an earlier/later triangle.
        return line ? triangles != 0 : lines != 0;
    }

    internal DynamicGeometryStream(int budgetBytes = DefaultBudgetBytes)
    {
        if (budgetBytes < 0) throw new ArgumentOutOfRangeException(nameof(budgetBytes));
        _maxIndices = budgetBytes / sizeof(int);
    }

    // Oversize arrays are transient; they never expand retained scratch beyond its budget.
    internal ReadOnlySpan<int> Prepare(LegacyGeometryBatch batch)
    {
        int count = batch.IndexCount;
        if (count > _maxIndices) return batch.BuildIndexArray();
        if (count > _indices.Length)
        {
            int capacity = (int)Math.Min(_maxIndices, Math.Max((long)count,
                Math.Max(256L, (long)_indices.Length * 2)));
            _indices = new int[capacity];
        }
        batch.CopyIndicesTo(_indices);
        return _indices.AsSpan(0, count);
    }

    internal void Reset() => _indices = Array.Empty<int>();
}
