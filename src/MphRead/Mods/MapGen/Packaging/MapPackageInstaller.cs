using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

/// <summary>A verified private archive and completed build. Publication is an explicit owner-thread operation.</summary>
public sealed class PreparedMapInstallation : IDisposable
{
    private readonly string _snapshot;
    private readonly MapBuildResult _build;
    private MapFilePublication? _publication;
    private MapDefinition? _definition;
    private string? _library, _manifest, _runtimeNamespace, _runtimeRoot;
    public MapContentIdentity Identity { get; }
    internal PreparedMapInstallation(string snapshot, MapBuildResult build, MapContentIdentity identity)
    { _snapshot = snapshot; _build = build; Identity = identity; }
    internal void PreparePublication(string library, CancellationToken cancellation)
    {
        var publication = new MapFilePublication();
        try
        {
            string destination = Path.Combine(Path.GetFullPath(library), Identity.MapId.ToString("N") + MapBundle.Extension);
            string staged = publication.Stage(_snapshot, destination, cancellation);
            if (!MapContentIdentity.FromPackage(staged).Matches(Identity))
                throw new InvalidDataException("Prepared archive changed before staging.");
            var definition = MapDefinition.Load(staged);
            _manifest = MapBuildScheduler.StageInstallation(_build, definition, publication, cancellation);
            // Metadata is detached and rebound without rereading/re-hashing on the owner.
            definition.SourcePath = definition.BundlePath = destination;
            definition.BaseDirectory = Path.GetDirectoryName(destination);
            if (definition.Import is { } import)
            { import.BundlePath = destination; import.BaseDirectory = definition.BaseDirectory; }
            if (definition.Collision is { } collision)
            { collision.BundlePath = destination; collision.BaseDirectory = definition.BaseDirectory; }
            _definition = definition; _library = Path.GetFullPath(library);
            _runtimeNamespace = CustomRooms.RuntimeNamespace;
            _runtimeRoot = Paths.FileSystem;
            cancellation.ThrowIfCancellationRequested();
            _publication = publication;
        }
        catch { publication.Dispose(); throw; }
    }
    public MapDefinition Commit(string library, bool initialJoin = false, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        lock (MapRuntimeUsage.Gate)
        {
            cancellation.ThrowIfCancellationRequested();
            MapRuntimeUsage.RequireInstallationAllowed(Identity.RoomKey, initialJoin);
            MapPackageInstaller.RequireNoConflict(Identity);
            if (_publication == null || _definition == null || _library != Path.GetFullPath(library)
                || _runtimeNamespace != CustomRooms.RuntimeNamespace || _runtimeRoot != Paths.FileSystem)
                throw new IOException("Map publication target changed. Prepare the map again for this library/runtime.");
            Mods.RoomPrewarm.Invalidate(Identity.RoomKey);
            // Fail promptly when another process owns publication; never sleep in the network pump.
            using var lease = new FileStream(_manifest + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _publication.Commit(cancellation);
            return _definition;
        }
    }
    public void Dispose() { _publication?.Dispose(); if (File.Exists(_snapshot)) File.Delete(_snapshot); }
}

public static class MapPackageInstaller
{
    internal static void RequireNoConflict(MapContentIdentity identity)
    {
        var previous = CustomRooms.Definitions.FirstOrDefault(d =>
            (d.MapId == Guid.Empty ? MapPackageBuilder.LegacyId(d.Name) : d.MapId) == identity.MapId);
        if (previous != null && !previous.Name.Equals(identity.RoomKey, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An installed map's runtime name is immutable. Use a new map identity when renaming it.");
        if (Metadata.IsBuiltInRoom(identity.RoomKey)) throw new InvalidDataException("A custom map cannot replace a built-in room.");
        var conflict = CustomRooms.Definitions.FirstOrDefault(d => d.Name.Equals(identity.RoomKey, StringComparison.OrdinalIgnoreCase)
            && (d.MapId == Guid.Empty ? MapPackageBuilder.LegacyId(d.Name) : d.MapId) != identity.MapId);
        if (conflict != null) throw new InvalidDataException("Another map already uses this runtime name. Rename the map before sharing.");
    }
    public static async Task<PreparedMapInstallation> PrepareAsync(string download, MapContentIdentity required, CancellationToken token = default,
        string? library = null)
    {
        string snapshot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ppmap");
        try
        {
            await using (var source = File.OpenRead(download))
            await using (var output = File.Create(snapshot))
                await MapCommunityClient.CopyBoundedAsync(source, output, MapPackageReader.MaxArchiveBytes, token).ConfigureAwait(false);
            var identity = MapContentIdentity.FromPackage(snapshot);
            if (!identity.Matches(required)) throw new InvalidDataException("Downloaded map identity or package hash does not match the server.");
            RequireNoConflict(identity);
            var definition = MapDefinition.Load(snapshot);
            var build = await MapBuildScheduler.Shared.BuildAsync(MapBuildSnapshot.Capture(definition), token).ConfigureAwait(false);
            MapCompiler.ThrowIfInvalid(build.Validation());
            token.ThrowIfCancellationRequested();
            var prepared = new PreparedMapInstallation(snapshot, build, identity);
            try { prepared.PreparePublication(library ?? CustomRooms.UserMapDirectory, token); return prepared; }
            catch { prepared.Dispose(); throw; }
        }
        catch { if (File.Exists(snapshot)) File.Delete(snapshot); throw; }
    }
    public static MapDefinition Install(string download, Guid id, string contentHash, string archiveHash, string library, CancellationToken cancellation = default)
    {
        var identity = MapContentIdentity.FromPackage(download);
        if (identity.MapId != id || identity.ContentHash != MapHash256.Parse(contentHash) || identity.PackageHash != MapHash256.Parse(archiveHash))
            throw new InvalidDataException("Downloaded map identity does not match the server.");
        using var prepared = PrepareAsync(download, identity, cancellation, library).GetAwaiter().GetResult();
        return prepared.Commit(library, cancellation: cancellation);
    }
}
