using System;

namespace MphRead.Mods.Render;

/// <summary>
/// Reversible policy for presentation-only animation blending. Supported
/// implementations default ON. Use the global or per-channel environment
/// variable set to 0, false or off to restore canonical draw sampling.
/// </summary>
internal static class AnimationRollout
{
    internal static bool Enabled(string channelName)
        => EnabledValues(Environment.GetEnvironmentVariable("PROJECT_PRIME_ANIMATION_BLEND"),
            Environment.GetEnvironmentVariable(channelName));

    internal static bool EnabledValues(string? global, string? channel)
        => Allows(global) && Allows(channel);

    private static bool Allows(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        return value.Trim().ToLowerInvariant() is not ("0" or "false" or "off" or "disabled");
    }
}
