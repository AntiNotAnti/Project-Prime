using System;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Additive rollout controls for Community Hub PR-14. Default behavior remains
/// exactly the current editor and community backends until release acceptance
/// is recorded. Never use environment flags to weaken package/network trust.
/// </summary>
public static class CommunityCutoverPolicy
{
    public const string StudioCreatorPreviewVariable =
        "PROJECT_PRIME_STUDIO_COMMUNITY_HUB_PREVIEW";
    public const string NetRadiantDefaultVariable =
        "PROJECT_PRIME_NETRADIANT_DEFAULT";

    public static bool StudioCreatorPreview =>
        Enabled(Environment.GetEnvironmentVariable(StudioCreatorPreviewVariable));

    // PR-12/13 still need actual NetRadiant, Q3Map2 and gameplay acceptance.
    // A production default-editor switch is deliberately not implemented here.
    public static bool NetRadiantDefault => false;

    public static bool Enabled(string? value)
        => string.Equals(value?.Trim(), "1", StringComparison.Ordinal);

    public static string Summary =>
        StudioCreatorPreview
            ? "Creator Community Hub preview enabled; legacy management remains available."
            : "Legacy creator manager retained; player Community workspace unchanged.";
}
