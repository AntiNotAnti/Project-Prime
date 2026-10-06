using System;
using System.IO;
using System.Threading;
using MphRead.Mods.MapGen;

namespace MphRead.AvaloniaShared;

/// <summary>Canonical build publication constrained to an explicit Studio-owned runtime root.</summary>
public sealed class StudioPrivateMapRuntime
{
    private readonly string _root;
    private readonly string _canonicalRoot;
    public StudioPrivateMapRuntime(string studioDataDirectory)
    {
        if (!Path.IsPathFullyQualified(studioDataDirectory)) throw new ArgumentException("Studio data root must be absolute.");
        _root = Path.Combine(Path.GetFullPath(studioDataDirectory),"runtime");
        _canonicalRoot = MapPublicationLease.ResolveRuntimeDirectoryAliases(_root);
        RequirePrivateDestinations();
    }
    private string[] RequirePrivateDestinations(string? room = null)
    {
        StringComparison comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
        string currentRoot = MapPublicationLease.ResolveRuntimeDirectoryAliases(_root);
        if (!string.Equals(currentRoot,_canonicalRoot,comparison)) throw new IOException("Studio runtime directory changed through a filesystem link.");
        string[] destinations = { room is null ? Path.Combine(_root,"_archives") : Path.Combine(_root,"_archives",room),Path.Combine(_root,"levels","entities"),Path.Combine(_root,"levels","nodes") };
        string game = Paths.FileSystem;
        string? gameRoot = string.IsNullOrWhiteSpace(game) ? null : MapPublicationLease.CanonicalizeRuntimeDirectory(game);
        foreach (string destination in destinations)
        {
            string physical = MapPublicationLease.ResolveRuntimeDirectoryAliases(destination);
            if (!physical.StartsWith(_canonicalRoot + Path.DirectorySeparatorChar,comparison)) throw new IOException("Studio runtime destination escapes its private root.");
            // Game-root exclusion is deliberately conservative on macOS, while
            // positive private-root containment preserves physical path casing.
            string canonical = MapPublicationLease.CanonicalizeRuntimeDirectory(physical);
            if (gameRoot is not null && Overlaps(canonical,gameRoot))
                throw new IOException("Studio runtime output aliases extracted game data.");
        }
        string exclusionRoot=MapPublicationLease.CanonicalizeRuntimeDirectory(currentRoot);
        if (gameRoot is not null && Overlaps(exclusionRoot,gameRoot))
            throw new IOException("Studio runtime root overlaps extracted game data.");
        return destinations;
    }
    private static bool Overlaps(string first,string second)
    {
        string Prefix(string path)=>Path.EndsInDirectorySeparator(path)?path:path+Path.DirectorySeparatorChar;
        return first==second || first.StartsWith(Prefix(second),StringComparison.Ordinal) || second.StartsWith(Prefix(first),StringComparison.Ordinal);
    }
    public void PublishPrivate(MapBuildResult result, MapDefinition definition, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        MapValidator.RequireRuntimeName(definition.Name);
        string[] destinations = RequirePrivateDestinations(definition.Name.ToLowerInvariant());
        MapBuildScheduler.Publish(result,definition,destinations[0],destinations[1],destinations[2],cancellation);
    }
}
