using System;

namespace MphRead.Mods.Render;

/// <summary>Normalized mip footprints overlap at odd boundaries so no depth sample is lost.</summary>
internal static class ConservativeHiZ
{
    internal static (int Start, int End) Footprint(int cell, int sourceSize, int destinationSize)
    {
        if (sourceSize < 1 || destinationSize < 1 || cell < 0 || cell >= destinationSize)
            throw new ArgumentOutOfRangeException(nameof(cell));
        return (checked((int)((long)cell * sourceSize / destinationSize)),
            checked((int)(((long)(cell + 1) * sourceSize + destinationSize - 1) / destinationSize)));
    }

    // Previous complete-world depth includes moving actors/doors. Until a static
    // occluder pass or conservative disocclusion is available, it cannot reject
    // current room geometry. GPU current-frustum/indirect work remains enabled.
    internal const bool TemporalOcclusionEnabled = false;
}
