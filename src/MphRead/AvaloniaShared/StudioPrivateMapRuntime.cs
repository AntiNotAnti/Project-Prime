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
        _canonicalRoot = MapPublicationLease.CanonicalizeRuntimeDirectory(_root);
        RequirePrivateDestinations();
    }
    private string[] RequirePrivateDestinations(string? room = null)
    {
        string currentRoot = MapPublicationLease.CanonicalizeRuntimeDirectory(_root);
        if (!string.Equals(currentRoot,_canonicalRoot,StringComparison.Ordinal)) throw new IOException("Studio runtime directory changed through a filesystem link.");
        string[] destinations = { room is null ? Path.Combine(_root,"_archives") : Path.Combine(_root,"_archives",room),Path.Combine(_root,"levels","entities"),Path.Combine(_root,"levels","nodes") };
        string game = Paths.FileSystem;
        string? gameRoot = string.IsNullOrWhiteSpace(game) ? null : MapPublicationLease.CanonicalizeRuntimeDirectory(game);
        foreach (string destination in destinations)
        {
            string canonical = MapPublicationLease.CanonicalizeRuntimeDirectory(destination);
            if (!canonical.StartsWith(_canonicalRoot + Path.DirectorySeparatorChar,StringComparison.Ordinal)) throw new IOException("Studio runtime destination escapes its private root.");
            if (gameRoot is not null && (canonical==gameRoot || canonical.StartsWith(gameRoot + Path.DirectorySeparatorChar,StringComparison.Ordinal) || gameRoot.StartsWith(canonical + Path.DirectorySeparatorChar,StringComparison.Ordinal)))
                throw new IOException("Studio runtime output aliases extracted game data.");
        }
        if (gameRoot is not null && (currentRoot==gameRoot || currentRoot.StartsWith(gameRoot + Path.DirectorySeparatorChar,StringComparison.Ordinal) || gameRoot.StartsWith(currentRoot + Path.DirectorySeparatorChar,StringComparison.Ordinal)))
            throw new IOException("Studio runtime root overlaps extracted game data.");
        return destinations;
    }
    public void PublishPrivate(MapBuildResult result, MapDefinition definition, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        MapValidator.RequireRuntimeName(definition.Name);
        string[] destinations = RequirePrivateDestinations(definition.Name.ToLowerInvariant());
        MapBuildScheduler.Publish(result,definition,destinations[0],destinations[1],destinations[2],cancellation);
    }
}
