using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Update;

/// <summary>Result of checking the signed first-party update feed.</summary>
public interface UpdateCheckResult
{
    public sealed record UpToDate(UpdateManifest? Manifest = null) : UpdateCheckResult;
    public sealed record Available(UpdateManifest Manifest, UpdatePackage Package,
        Uri PackageUri) : UpdateCheckResult;
    /// <summary>
    /// The check was deliberately skipped because this build cannot be
    /// compared with a signed release. This is informational, not a failure.
    /// </summary>
    public sealed record NotApplicable(string Reason) : UpdateCheckResult
    {
        public string DiagnosticCode => Reason;
    }
    public sealed record Failed(string Message) : UpdateCheckResult;
}

/// <summary>
/// Fetches and verifies the Project Prime manifest without using the GitHub API.
/// The default constructor is the production path; handler/trust/version/RID
/// overrides exist for deterministic focused tests only.
/// </summary>
public sealed class UpdateManifestClient
{
    public static readonly Uri DefaultManifestUri = new(
        $"https://github.com/{Mods.Branding.UpdateRepository}/releases/latest/download/update-manifest.json");
    public static readonly Uri DefaultSignatureUri = new(
        $"https://github.com/{Mods.Branding.UpdateRepository}/releases/latest/download/update-manifest.sig");

    private readonly HttpMessageHandler? _handler;
    private readonly UpdateTrust _trust;
    private readonly Uri _manifestUri;
    private readonly Uri _signatureUri;
    private readonly Version? _installedVersion;
    private readonly string _rid;
    private readonly bool _allowLocalBuild;
    private readonly TimeSpan _operationTimeout;

    public UpdateManifestClient()
        : this(null, null, null, null, null, null, allowLocalBuildForTests: false) { }

    public UpdateManifestClient(HttpMessageHandler? handler, UpdateTrust? trust = null,
        Uri? manifestUri = null, Uri? signatureUri = null, Version? installedVersion = null,
        string? rid = null, bool allowLocalBuildForTests = false,
        TimeSpan? operationTimeout = null)
    {
        if (handler == null && (trust != null || manifestUri != null
            || signatureUri != null || installedVersion != null || rid != null
            || allowLocalBuildForTests || operationTimeout != null))
        {
            throw new ArgumentException(
                "custom update trust and transport are available only with an injected test handler");
        }
        _handler = handler;
        _trust = trust ?? new UpdateTrust();
        _manifestUri = manifestUri ?? DefaultManifestUri;
        _signatureUri = signatureUri ?? DefaultSignatureUri;
        _installedVersion = installedVersion;
        _rid = rid ?? RuntimePlatform.Rid();
        _operationTimeout = operationTimeout ?? UpdateTransport.MetadataTimeout;
        // Local/dev builds never check through the production constructor. A
        // handler plus explicit opt-in is the narrow test seam, not a setting
        // a player can persist or pass through the launcher.
        _allowLocalBuild = allowLocalBuildForTests && handler != null;
    }

    public async Task<UpdateCheckResult> CheckAsync(UpdateChannel channel = UpdateChannel.Stable,
        CancellationToken cancellationToken = default)
    {
        if (channel != UpdateChannel.Stable)
            return new UpdateCheckResult.Failed("only the stable update channel is supported");

        // Injected transports are a deterministic test seam. They must not
        // inherit the test host's assembly version (for example vstest's own
        // release number) when the caller deliberately omitted a client
        // release version to model a local build.
        Version? installed = _installedVersion
            ?? (_handler == null ? BuildVersion.Current : null);
        if (installed == null && !_allowLocalBuild)
            return new UpdateCheckResult.NotApplicable("LocalBuild");
        if (installed == null)
            return new UpdateCheckResult.Failed("the installed version is unknown");
        if (String.IsNullOrWhiteSpace(Mods.Branding.UpdateRepository))
            return new UpdateCheckResult.Failed("the update feed is not configured");
        if (!UpdateTransport.IsAllowedUri(_manifestUri)
            || !UpdateTransport.IsAllowedUri(_signatureUri))
            return new UpdateCheckResult.Failed("the update feed address is not trusted");

        using CancellationTokenSource timeout = UpdateTransport.CreateTimeoutToken(
            cancellationToken, _operationTimeout);
        CancellationToken operationToken = timeout.Token;
        try
        {
            using HttpClient client = UpdateTransport.CreateClient(_handler, out _);
            byte[] manifestBytes = await ReadBoundedAsync(client, _manifestUri,
                UpdateManifestValidator.MaxManifestBytes, operationToken).ConfigureAwait(false);
            byte[] signature = await ReadBoundedAsync(client, _signatureUri,
                UpdateManifestValidator.MaxSignatureBytes, operationToken).ConfigureAwait(false);

            // Verify the bytes as downloaded before any parser sees them.
            if (!_trust.VerifyManifest(manifestBytes, signature))
                return new UpdateCheckResult.Failed("Update verification failed. Project Prime was not modified.");

            UpdateManifest manifest = UpdateManifestJson.Parse(manifestBytes);
            Version published = Version.Parse(manifest.Version);
            Version current = BuildVersion.Normalise(installed);
            if (published <= current)
                return new UpdateCheckResult.UpToDate(manifest);

            UpdatePackage? package = null;
            foreach (UpdatePackage candidate in manifest.Packages)
            {
                if (String.Equals(candidate.Rid, _rid, StringComparison.Ordinal))
                {
                    package = candidate;
                    break;
                }
            }
            if (package == null)
                return new UpdateCheckResult.Failed(
                    $"release {manifest.Version} has no package for {_rid}");

            Uri packageUri = BuildPackageUri(package, manifest.Version);
            return new UpdateCheckResult.Available(manifest, package, packageUri);
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult.Failed(cancellationToken.IsCancellationRequested
                ? "update check cancelled" : "update check timed out");
        }
        catch (UpdateManifestValidationException ex)
        {
            return new UpdateCheckResult.Failed(ex.Message);
        }
        catch (Exception ex)
        {
            // Keep UI diagnostics useful without exposing response bodies or
            // credentials supplied by a custom handler.
            return new UpdateCheckResult.Failed(
                $"could not check for updates ({ex.GetType().Name})");
        }
    }

    public Uri BuildPackageUri(UpdatePackage package, string version)
    {
        if (!UpdateManifestValidator.IsExactVersion(version))
            throw new UpdateManifestValidationException("release version is invalid");
        UpdateManifestValidator.Validate(new UpdateManifest(1, "stable", "1.0.0",
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), [package]));
        return new Uri($"https://github.com/{Mods.Branding.UpdateRepository}/releases/download/v{version}/"
            + Uri.EscapeDataString(package.FileName));
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpClient client, Uri uri, int limit,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await UpdateTransport.SendFollowingRedirectsAsync(
            client, uri, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"update host returned {(int)response.StatusCode}");
        long? contentLength = response.Content.Headers.ContentLength;
        if (contentLength is < 0 or > UpdateTransport.MaxMetadataBytes || contentLength > limit)
            throw new InvalidDataException("update metadata is too large");

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var output = new MemoryStream(contentLength is > 0 and <= int.MaxValue
            ? (int)contentLength.Value : Math.Min(limit, 4096));
        byte[] buffer = new byte[8192];
        int total = 0;
        using CancellationTokenSource idleTimeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        while (true)
        {
            int read = await UpdateTransport.ReadWithIdleTimeoutAsync(stream, buffer,
                idleTimeout).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > limit) throw new InvalidDataException("update metadata is too large");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
