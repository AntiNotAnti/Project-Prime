using System;
using System.Collections.Generic;
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

    public static NetRadiantTools NetRadiantTools => NetRadiantToolDiscovery.Resolve(
        AppContext.BaseDirectory, NetRadiantToolDiscovery.CurrentPlatform,
        Environment.GetEnvironmentVariable(NetRadiantExecutableVariable),
        Environment.GetEnvironmentVariable(Q3Map2ExecutableVariable));

    public static string? NetRadiantExecutable => NetRadiantTools.EditorExecutable;

    public static string? Q3Map2Executable => NetRadiantTools.CompilerExecutable;

    /// <summary>
    /// Hosts trusted by the local operator, not by a remote service's URL.
    /// Allows the established comma-separated host allowlist and an explicit
    /// local HTTPS CDN base. If neither is configured, clients stay on the
    /// authenticated origin downloader without trusting a new domain.
    /// </summary>
    public static string[] TrustedCdnHosts
    {
        get
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            string? configured = Environment.GetEnvironmentVariable(
                "PROJECT_PRIME_TRUSTED_CDN_HOSTS");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                foreach (string value in configured.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (Uri.CheckHostName(value) != UriHostNameType.Unknown)
                        allowed.Add(value.ToLowerInvariant());
                }
            }
            string? baseUrl = Environment.GetEnvironmentVariable(
                "PROJECT_PRIME_MAP_CDN_BASE");
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? cdn)
                && cdn.Scheme == Uri.UriSchemeHttps
                && string.IsNullOrEmpty(cdn.UserInfo)
                && string.IsNullOrEmpty(cdn.Query)
                && string.IsNullOrEmpty(cdn.Fragment)
                && cdn.HostNameType != UriHostNameType.Unknown)
                allowed.Add(cdn.IdnHost.ToLowerInvariant());
            return new List<string>(allowed).ToArray();
        }
    }

    // Prefer the shipped geometry editor or an explicit operator override.
    // Android and invalid overrides retain the native workspace.
    public static bool NetRadiantDefault =>
        EnabledByDefault(Environment.GetEnvironmentVariable(NetRadiantDefaultVariable))
        && NetRadiantExecutable != null;

    public static string Summary =>
        (StudioCreatorPreview
            ? "Community creator workspace enabled; Advanced Revisions remains available."
            : "Legacy creator dashboard enabled.")
        + (NetRadiantDefault
            ? " Radiant is the primary geometry editor; Studio owns gameplay and packages."
            : " Native geometry fallback is available; install Project Prime Radiant to use the primary geometry editor.")
        + (ObjectMirrorEnabled
            ? " Verified public-map object mirroring enabled."
            : " Object mirroring disabled by operator.");
}
