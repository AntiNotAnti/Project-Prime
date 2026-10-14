using System;
using System.IO;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Community Hub defaults after the PR-10..14 integration. Optional creator
/// workflows are available by default, while safety-critical prerequisites
/// (trusted CDN hosts and installed external tools) must be satisfied.
/// Existing flags remain explicit rollback switches; no protocol, package
/// or historical replay identity changes are permitted here.
/// </summary>
public static class CommunityCutoverPolicy
{
    public const string StudioCreatorPreviewVariable =
        "PROJECT_PRIME_STUDIO_COMMUNITY_HUB_PREVIEW";
    public const string NetRadiantDefaultVariable =
        "PROJECT_PRIME_NETRADIANT_DEFAULT";
    public const string NetRadiantExecutableVariable =
        "PROJECT_PRIME_NETRADIANT_EXE";
    public const string Q3Map2ExecutableVariable =
        "PROJECT_PRIME_Q3MAP2_EXE";
    public const string ObjectMirrorVariable =
        "PROJECT_PRIME_MAP_OBJECT_MIRROR_ENABLED";

    /// <summary>
    /// A missing value means enabled. Administrators can explicitly restore
    /// the old behavior using 0/false/off/no. Unknown values fail closed,
    /// rather than quietly enabling a feature due to a misspelled setting.
    /// </summary>
    public static bool EnabledByDefault(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "on" or "yes" => true,
            "0" or "false" or "off" or "no" => false,
            _ => false
        };
    }

    // Retain the previous strict opt-in interpreter for old callers.
    public static bool Enabled(string? value)
        => string.Equals(value?.Trim(), "1", StringComparison.Ordinal);

    public static bool StudioCreatorPreview =>
        EnabledByDefault(Environment.GetEnvironmentVariable(StudioCreatorPreviewVariable));

    // Public mirror is available by default, but public CDN URLs still
    // require an operator-provided HTTPS base AND immutable verified bytes.
    // Draft/unlisted content never becomes public because of this switch.
    public static bool ObjectMirrorEnabled =>
        EnabledByDefault(Environment.GetEnvironmentVariable(ObjectMirrorVariable));

    public static string? NetRadiantExecutable =>
        ConfiguredExternalTool(NetRadiantExecutableVariable);

    public static string? Q3Map2Executable =>
        ConfiguredExternalTool(Q3Map2ExecutableVariable);

    // Prefer NetRadiant only when a separately installed, regular desktop
    // executable is explicitly configured. Native JSON Studio remains the
    // automatic fallback on Android or a missing/invalid external binary.
    public static bool NetRadiantDefault =>
        EnabledByDefault(Environment.GetEnvironmentVariable(NetRadiantDefaultVariable))
        && NetRadiantExecutable != null;

    private static string? ConfiguredExternalTool(string variable)
    {
        if (OperatingSystem.IsAndroid()) return null;
        string? configured = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(configured)
            || !Path.IsPathFullyQualified(configured)) return null;
        try
        {
            string path = Path.GetFullPath(configured);
            return File.Exists(path)
                && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)
                ? path : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException
            or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public static string Summary =>
        (StudioCreatorPreview
            ? "Community creator workspace enabled; Advanced Revisions remains available."
            : "Legacy creator dashboard enabled.")
        + (NetRadiantDefault
            ? " External NetRadiant is the preferred configured editor."
            : " Native Map Studio remains the editor; external NetRadiant needs an installed executable.")
        + (ObjectMirrorEnabled
            ? " Verified public-map object mirroring enabled."
            : " Object mirroring disabled by operator.");
}
