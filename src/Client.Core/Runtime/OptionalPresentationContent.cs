using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Runtime.Content;

namespace MphRead.Mods.Content;

/// <summary>
/// Resolves only files declared by one already-discovered optional pack. The
/// resolver never accepts a URI or caller-supplied absolute path. At the audio
/// consumption boundary it hashes the same bounded file handle that is handed
/// to the decoder, closing the discovery-to-playback replacement window.
/// </summary>
public sealed class OptionalPresentationAssetResolver
{
    private readonly string _root;
    private readonly Dictionary<string, ContentFileEntry> _files;

    public ContentPackIdentity Identity { get; }
    public OptionalPresentationKind Kind { get; }

    public OptionalPresentationAssetResolver(InstalledOptionalPresentationPack installed)
    {
        ArgumentNullException.ThrowIfNull(installed);
        if (installed.IsBuiltIn || String.IsNullOrWhiteSpace(installed.RootDirectory))
            throw new ArgumentException("A resolver requires an installed optional pack.", nameof(installed));
        OptionalPresentationManifest manifest = ContentManifestValidator.ValidateOptional(installed.Manifest);
        _root = Path.GetFullPath(installed.RootDirectory);
        _files = manifest.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        Identity = manifest.PackIdentity;
        Kind = manifest.Kind;
    }

    public Task<FileStream?> OpenVerifiedAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        if (!_files.TryGetValue(manifestPath, out ContentFileEntry? declaration))
            return Task.FromResult<FileStream?>(null);
        // At most one task is owned by each optional audio presentation. Do not
        // run hashing inline: a valid declared asset may be up to 64 MiB.
        return Task.Run(() => OpenVerified(declaration, cancellationToken), CancellationToken.None);
    }

    private FileStream? OpenVerified(ContentFileEntry declaration, CancellationToken cancellationToken)
    {
        FileStream? verified = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(_root)
                || File.GetAttributes(_root).HasFlag(FileAttributes.ReparsePoint)) return null;
            string candidate = Path.GetFullPath(Path.Combine(_root,
                declaration.Path.Replace('/', Path.DirectorySeparatorChar)));
            StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            string prefix = Path.TrimEndingDirectorySeparator(_root) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, comparison)) return null;

            string current = _root;
            foreach (string part in declaration.Path.Split('/'))
            {
                cancellationToken.ThrowIfCancellationRequested();
                current = Path.Combine(current, part);
                if ((File.Exists(current) || Directory.Exists(current))
                    && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return null;
            }
            var info = new FileInfo(candidate);
            if (!info.Exists || info.Length != declaration.Bytes) return null;

            // FileShare.Read prevents in-place writes while this handle is in
            // use on Windows; on Unix the open handle continues to identify
            // the verified inode even if the directory entry is replaced.
            verified = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, options: FileOptions.SequentialScan);
            if (verified.Length != declaration.Bytes)
            {
                verified.Dispose();
                return null;
            }
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = GC.AllocateUninitializedArray<byte>(64 * 1024);
            int read;
            while ((read = verified.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
            }
            string actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (!String.Equals(actual, declaration.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                verified.Dispose();
                return null;
            }
            verified.Position = 0;
            return verified;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or SecurityException or CryptographicException
            or OperationCanceledException)
        {
            verified?.Dispose();
            return null;
        }
    }
}

/// <summary>
/// Immutable client-side result of one bounded local discovery pass. Required
/// gameplay content is intentionally absent: authoritative admission continues
/// to use the existing server content identity.
/// </summary>
public sealed record ClientPresentationContentState(
    InstalledContentCatalog Catalog,
    OptionalPresentationSelection Announcer,
    OptionalPresentationSelection Music,
    OptionalPresentationAssetResolver? AnnouncerAssets,
    OptionalPresentationAssetResolver? MusicAssets)
{
    public static ClientPresentationContentState BuiltIn { get; } = Create(new InstalledContentCatalog(), null, null);

    internal static ClientPresentationContentState Create(InstalledContentCatalog catalog,
        ContentPackIdentity? requestedAnnouncer, ContentPackIdentity? requestedMusic)
    {
        OptionalPresentationSelection announcer = catalog.SelectOptional(
            OptionalPresentationKind.Announcer, requestedAnnouncer);
        OptionalPresentationSelection music = catalog.SelectOptional(
            OptionalPresentationKind.Music, requestedMusic);
        return new(catalog, announcer, music,
            announcer.Pack is { } voice ? new OptionalPresentationAssetResolver(voice) : null,
            music.Pack is { } songs ? new OptionalPresentationAssetResolver(songs) : null);
    }
}

/// <summary>
/// Process-local owner for optional presentation discovery. Refresh is called
/// by the settings screen and when a ScenePresentation starts; it performs no
/// network access and invalid packs are isolated behind built-in fallback.
/// </summary>
public static class ClientPresentationContent
{
    private static readonly object Gate = new();
    private static ClientPresentationContentState _current = ClientPresentationContentState.BuiltIn;

    public static ClientPresentationContentState Current
    {
        get { lock (Gate) return _current; }
    }

    public static ClientPresentationContentState Refresh()
    {
        InstalledContentCatalog catalog;
        string root = LauncherPrefs.OptionalContentDirectory;
        try
        {
            catalog = Directory.Exists(root)
                ? InstalledContentCatalog.Discover(root)
                : new InstalledContentCatalog();
        }
        catch (Exception error) when (error is ContentManifestValidationException
            or IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or SecurityException)
        {
            catalog = new InstalledContentCatalog(issues:
                [new ContentDiscoveryIssue(root, error.Message, IsGameplay: false)]);
        }

        ClientPresentationContentState state = ClientPresentationContentState.Create(catalog,
            LauncherPrefs.AnnouncerPack, LauncherPrefs.MusicPack);
        lock (Gate) _current = state;
        return state;
    }

    public static IReadOnlyList<InstalledOptionalPresentationPack> Packs(
        ClientPresentationContentState state, OptionalPresentationKind kind)
        => state.Catalog.OptionalPresentationPacks
            .Where(pack => pack.Manifest.Kind == kind)
            .OrderBy(pack => pack.Manifest.StableId, StringComparer.Ordinal)
            .ThenBy(pack => pack.Manifest.Version, StringComparer.Ordinal)
            .ThenBy(pack => pack.Manifest.ContentHash, StringComparer.Ordinal)
            .ToArray();
}
