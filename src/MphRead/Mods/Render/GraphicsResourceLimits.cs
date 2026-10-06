using System;

namespace MphRead.Mods.Render;

internal static class GraphicsResourceLimits
{
    // GL callers use signed dimensions, while WebGPU reports enabled limits as
    // unsigned values. Never advertise a fallback above the device's limit.
    internal static int ToCompatibilityTextureLimit(uint enabledLimit)
    {
        if (enabledLimit == 0 || enabledLimit > int.MaxValue)
            throw new InvalidOperationException($"Invalid enabled graphics texture limit: {enabledLimit}.");
        return (int)enabledLimit;
    }
}
