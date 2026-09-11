using System;
using System.Threading;

namespace MphRead.Mods.Update;

/// <summary>Compatibility projection of a signed manifest package.</summary>
public readonly struct UpdateInfo
{
    public string Tag { get; init; }
    public Version Version { get; init; }
    public string AssetName { get; init; }
    public string AssetUrl { get; init; }
    public long AssetSize { get; init; }
    public string PageUrl { get; init; }
    public string Notes { get; init; }
    public UpdateManifest? Manifest { get; init; }
    public UpdatePackage? Package { get; init; }
    public Uri? PackageUri { get; init; }
    public bool AllowLocalTestInstall { get; init; }
    public string VersionString => Version.ToString(3);
}

/// <summary>
/// Legacy entry point backed by the signed first-party manifest client. It no
/// longer queries the GitHub Releases API and never returns unverified package
/// URLs from arbitrary JSON.
/// </summary>
public static class UpdateCheck
{
    public static bool IsConfigured => !String.IsNullOrWhiteSpace(Branding.UpdateRepository);
    public static string? LastReason { get; private set; }

    public static UpdateInfo? Latest(CancellationToken cancel = default)
    {
        LastReason = null;
        if (!IsConfigured)
        {
            LastReason = "the update feed is not configured";
            return null;
        }
        UpdateCheckResult result = new UpdateManifestClient().CheckAsync(
            UpdateChannel.Stable, cancel).GetAwaiter().GetResult();
        switch (result)
        {
            case UpdateCheckResult.Available available:
                LastReason = null;
                return ToInfo(available);
            case UpdateCheckResult.UpToDate:
                LastReason = $"{BuildVersion.Display} is already the latest";
                return null;
            case UpdateCheckResult.Failed failed:
                LastReason = failed.Message;
                return null;
            default:
                LastReason = "the update feed returned no result";
                return null;
        }
    }

    /// <summary>Compatibility parser for tests/tools; transport callers use signed CheckAsync.</summary>
    public static UpdateInfo? Parse(string json, Version? installed = null)
    {
        try
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            UpdateManifest manifest = UpdateManifestJson.Parse(bytes);
            Version current = BuildVersion.Normalise(installed ?? BuildVersion.Current
                ?? new Version(0, 0, 0));
            Version published = Version.Parse(manifest.Version);
            if (published <= current) return null;
            UpdatePackage? package = null;
            foreach (UpdatePackage candidate in manifest.Packages)
            {
                if (candidate.Rid == RuntimePlatform.Rid())
                {
                    package = candidate;
                    break;
                }
            }
            if (package == null) return null;
            Uri uri = new UpdateManifestClient().BuildPackageUri(package, manifest.Version);
            return ToInfo(new UpdateCheckResult.Available(manifest, package, uri));
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
        {
            LastReason = "the update manifest could not be read";
            return null;
        }
    }

    public static string ReleasesPage => IsConfigured
        ? "https://github.com/" + Branding.UpdateRepository + "/releases" : "";

    public static bool IsServerBuild => false;
    public static string Rid() => RuntimePlatform.Rid();
    public static string PackageSuffix() => Rid();

    internal static UpdateInfo ToInfo(UpdateCheckResult.Available available) => new()
    {
        Tag = "v" + available.Manifest.Version,
        Version = Version.Parse(available.Manifest.Version),
        AssetName = available.Package.FileName,
        AssetUrl = available.PackageUri.ToString(),
        AssetSize = available.Package.Size,
        PageUrl = ReleasesPage,
        Notes = "",
        Manifest = available.Manifest,
        Package = available.Package,
        PackageUri = available.PackageUri
    };
}
