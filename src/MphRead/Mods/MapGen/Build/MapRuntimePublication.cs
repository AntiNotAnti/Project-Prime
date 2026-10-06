using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace MphRead.Mods.MapGen;

/// <summary>Every writer of installed room outputs shares the live game reader fence.</summary>
internal static class MapRuntimePublication
{
    // A private compiler/cache destination needs only its own writer lease. A
    // destination inside either configured runtime root must be the complete
    // expected room output set; partial or cross-namespace destinations fail
    // before any runtime output can be changed.
    internal static MapPublicationLease? Acquire(MapDefinition definition, MapOutputSet destination,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        string[] targets = destination.Files.Append(destination.Manifest).Select(CanonicalFile).ToArray();
        string runtimeNamespace = CustomRooms.RuntimeNamespace;
        foreach (string configured in new[] { CustomRooms.RuntimePublicationRoot, Paths.FileSystem }
            .Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.Ordinal))
        {
            string root = MapPublicationLease.CanonicalizeRuntimeDirectory(configured);
            if (!targets.Any(path => Within(path, root))) continue;
            string runtimePath = string.IsNullOrEmpty(runtimeNamespace) ? "" : Path.Combine("hosted", runtimeNamespace);
            var expected = MapOutputSet.Create(definition,
                Paths.Combine(configured, @"_archives", Path.Combine(runtimePath, definition.Name.ToLowerInvariant())),
                Paths.Combine(configured, @"levels\entities", runtimePath),
                Paths.Combine(configured, @"levels\nodeData", runtimePath));
            if (!targets.SequenceEqual(expected.Files.Append(expected.Manifest).Select(CanonicalFile)))
                throw new IOException("Game runtime outputs must use the complete destination for this room and host namespace. Choose a private output directory or publish the canonical room outputs.");
            lock (MapRuntimeUsage.Gate)
            {
                cancellation.ThrowIfCancellationRequested();
                MapRuntimeUsage.RequireInstallationAllowed(definition.Name);
                return MapPublicationLease.AcquirePublication(configured, runtimeNamespace, definition.Name,
                    cancellation: cancellation);
            }
        }
        return null;
    }

    private static bool Within(string path, string root) => path.Equals(root, StringComparison.Ordinal)
        || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static string CanonicalFile(string path)
    {
        string full = Path.GetFullPath(path);
        // Atomic publication replaces a file, never follows a user-supplied
        // file alias. Refuse aliases explicitly so ownership stays unambiguous.
        var file = new FileInfo(full);
        if (file.Exists && (file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Map runtime publication cannot target a symbolic-link file.");
        string parent = MapPublicationLease.CanonicalizeRuntimeDirectory(Path.GetDirectoryName(full)!);
        string name = Path.GetFileName(full);
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsWindows()) name = name.ToUpperInvariant();
        return Path.Combine(parent, name);
    }
}
