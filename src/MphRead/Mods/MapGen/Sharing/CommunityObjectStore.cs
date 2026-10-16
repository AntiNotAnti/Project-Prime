using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Explicit opt-in immutable object-store mirroring. This filesystem adapter
/// models a mount backed by external object storage; it does not claim that a
/// CDN has been deployed. Public URLs are advertised only when the operator
/// has selected a concrete HTTPS base and verified its storage mapping.
/// </summary>
public sealed class CommunityObjectStore
{
    private readonly string _objects;
    private readonly string? _cdn;
    // Immutable public objects are strictly hashed at promotion and whenever
    // freshness is uncertain. Metadata requests can reuse that verification
    // briefly if the file identity/size/timestamps have not changed.
    private sealed record Verification(long Bytes, DateTime CreatedUtc,
        DateTime ModifiedUtc, long VerifiedTick);
    private readonly ConcurrentDictionary<string, Verification> _recentlyVerified =
        new(StringComparer.Ordinal);
    private const long VerificationTtlMilliseconds = 30_000;

    public CommunityObjectStore(string storage, string? cdnBase = null)
    {
        _objects = Path.Combine(Path.GetFullPath(storage), "objects-v3", "sha256");
        Directory.CreateDirectory(_objects);
        if (!string.IsNullOrWhiteSpace(cdnBase))
        {
            Uri endpoint = new(cdnBase.TrimEnd('/') + "/", UriKind.Absolute);
            if (endpoint.Scheme != Uri.UriSchemeHttps
                || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0
                || endpoint.Fragment.Length != 0
                || endpoint.HostNameType == UriHostNameType.Unknown)
                throw new ArgumentException("Public CDN must be a trusted absolute HTTPS base.");
            _cdn = endpoint.ToString();
        }
    }

    private string Key(string hash)
    {
        if (!MapCommunityClient.ValidHash(hash))
            throw new InvalidDataException("Invalid immutable object SHA-256.");
        return Path.Combine(_objects, hash[..2], hash + ".ppmap");
    }

    private void RememberVerified(string hash, FileInfo info)
        => _recentlyVerified[hash] = new Verification(
            info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc,
            Environment.TickCount64);

    // Mutations and operator reconciliation deliberately use a fresh full
    // digest. Only the presentation-only CDN-location query has a fast path.
    public bool HasVerified(string hash, long expectedBytes)
    {
        string path = Key(hash);
        var before = new FileInfo(path);
        if (!before.Exists || before.Length != expectedBytes)
        {
            _recentlyVerified.TryRemove(hash, out _);
            return false;
        }
        string digest = MapBuildFingerprint.HashFile(path);
        var after = new FileInfo(path);
        if (digest != hash || !after.Exists || after.Length != expectedBytes
            || after.CreationTimeUtc != before.CreationTimeUtc
            || after.LastWriteTimeUtc != before.LastWriteTimeUtc)
        {
            _recentlyVerified.TryRemove(hash, out _);
            return false;
        }
        RememberVerified(hash, after);
        return true;
    }

    private bool RecentlyVerified(string hash, long expectedBytes)
    {
        if (!_recentlyVerified.TryGetValue(hash, out Verification? verified))
            return false;
        long elapsed = Environment.TickCount64 - verified.VerifiedTick;
        if (elapsed < 0 || elapsed >= VerificationTtlMilliseconds
            || verified.Bytes != expectedBytes)
            return false;
        var info = new FileInfo(Key(hash));
        if (info.Exists && info.Length == expectedBytes
            && info.CreationTimeUtc == verified.CreatedUtc
            && info.LastWriteTimeUtc == verified.ModifiedUtc)
            return true;
        _recentlyVerified.TryRemove(hash, out _);
        return false;
    }

    /// <summary>
    /// Never overwrites a valid object. Incomplete stage data is private and
    /// only made visible after exact checksum and byte-length verification.
    /// Mirror only authorized public immutable .ppmap bytes.
    /// </summary>
    public bool MirrorPublic(string source, string hash, long expectedBytes)
    {
        if (expectedBytes is <= 0 or > MapPackageReader.MaxArchiveBytes)
            throw new InvalidDataException("Invalid published object size.");
        string output = Key(hash);
        if (HasVerified(hash, expectedBytes)) return false;
        if (File.Exists(output))
            throw new InvalidDataException("Object store contains a corrupt existing immutable key.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string temp = output + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            using (var from = new FileStream(source, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, FileOptions.SequentialScan))
            using (var to = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 65536, FileOptions.SequentialScan))
            {
                if (from.Length != expectedBytes)
                    throw new InvalidDataException("Published origin file size changed.");
                from.CopyTo(to, 65536);
                to.Flush(flushToDisk: true);
            }
            if (new FileInfo(temp).Length != expectedBytes
                || MapBuildFingerprint.HashFile(temp) != hash)
                throw new InvalidDataException("Immutable mirror failed SHA-256 verification.");
            // A concurrent worker may already have placed the same object.
            if (File.Exists(output))
                return HasVerified(hash, expectedBytes) ? false
                    : throw new InvalidDataException("Immutable mirror destination changed.");
            File.Move(temp, output);
            RememberVerified(hash, new FileInfo(output));
            return true;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    /// <summary>Optional public location only. Never generate a global CDN
    /// link to an unlisted/draft or deleted package.</summary>
    public Uri? PublicLocation(string hash, long bytes, bool published)
    {
        if (!published || _cdn == null
            || !(RecentlyVerified(hash, bytes) || HasVerified(hash, bytes)))
            return null;
        return new Uri(new Uri(_cdn), "sha256/" + hash[..2] + "/" + hash + ".ppmap");
    }

    public (int Verified, int Missing, int Corrupted) Inspect(
        IEnumerable<(string Hash, long Bytes)> publicPackages)
    {
        int verified = 0, missing = 0, damaged = 0;
        foreach (var item in publicPackages)
        {
            string path = Key(item.Hash);
            if (!File.Exists(path)) { missing++; continue; }
            if (HasVerified(item.Hash, item.Bytes)) verified++;
            else damaged++;
        }
        return (verified, missing, damaged);
    }
}

public sealed record CommunityPackageDistribution(
    int SchemaVersion,
    string PackageHash,
    long Bytes,
    string OriginPath,
    string? PublicCdnUrl,
    bool CdnEnabled);

/// <summary>
/// Client-side allowlist, not server-provided redirects. A CDN failure or
/// digest mismatch always returns to the existing authorized origin request.
/// </summary>
public sealed class CommunityCdnPolicy
{
    private readonly HashSet<string> _allowed;

    public CommunityCdnPolicy(IEnumerable<string> trustedHosts)
        => _allowed = trustedHosts
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

    public bool CanRead(Uri? url, string hash)
    {
        if (url == null || !url.IsAbsoluteUri
            || !MapCommunityClient.ValidHash(hash)
            || url.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(url.UserInfo)
            || !string.IsNullOrEmpty(url.Query)
            || !string.IsNullOrEmpty(url.Fragment)
            || !_allowed.Contains(url.IdnHost.ToLowerInvariant()))
            return false;
        return url.AbsolutePath.EndsWith("/" + hash + ".ppmap",
            StringComparison.Ordinal);
    }
}

public static class CommunityCdnDownloader
{
    public static async Task<bool> TryDownloadAsync(
        CommunityPackageDistribution location, CommunityCdnPolicy policy,
        string output, CancellationToken cancellation, HttpClient? client = null)
    {
        if (!MapCommunityClient.ValidHash(location.PackageHash)
            || location.Bytes is <= 0 or > MapPackageReader.MaxArchiveBytes
            || location.PublicCdnUrl == null
            || !Uri.TryCreate(location.PublicCdnUrl, UriKind.Absolute, out var uri)
            || !policy.CanRead(uri, location.PackageHash))
            return false;

        bool ownsClient = client == null;
        client ??= new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false
        }) { Timeout = TimeSpan.FromMinutes(10) };
        string temporary = Path.GetFullPath(output) + ".cdn-"
            + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var response = await client.GetAsync(uri,
                HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK
                || response.Content.Headers.ContentLength != location.Bytes) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(temporary)!);
            await using (var dest = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                await MapCommunityClient.CopyBoundedAsync(
                    await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false),
                    dest, location.Bytes, cancellation).ConfigureAwait(false);
                await dest.FlushAsync(cancellation).ConfigureAwait(false);
            }
            if (new FileInfo(temporary).Length != location.Bytes
                || MapBuildFingerprint.HashFile(temporary) != location.PackageHash)
                return false;
            // The caller still applies strict MapContentIdentity/installer
            // validation and never marks Ready from this transfer alone.
            File.Move(temporary, output, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException
            or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (ownsClient) client.Dispose();
        }
    }
}
