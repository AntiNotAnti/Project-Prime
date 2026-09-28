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
    public MapContentIdentity Identity { get; }
    internal PreparedMapInstallation(string snapshot, MapBuildResult build, MapContentIdentity identity)
    { _snapshot = snapshot; _build = build; Identity = identity; }
    public MapDefinition Commit(string library, bool initialJoin = false)
    {
        lock (MapRuntimeUsage.Gate)
        {
            MapRuntimeUsage.RequireInstallationAllowed(Identity.RoomKey, initialJoin);
            MapPackageInstaller.RequireNoConflict(Identity);
            if (!MapContentIdentity.FromPackage(_snapshot).Matches(Identity)) throw new InvalidDataException("Prepared map changed before installation.");
            Directory.CreateDirectory(library);
            string destination = Path.Combine(library, Identity.MapId.ToString("N") + MapBundle.Extension);
            Mods.RoomPrewarm.Invalidate(Identity.RoomKey);
            string backup = destination + "." + Guid.NewGuid().ToString("N") + ".backup";
            bool existed = File.Exists(destination);
            if (existed) File.Copy(destination, backup);
            try
            {
                AtomicFile.Write(destination, File.ReadAllBytes(_snapshot));
                var installed = MapDefinition.Load(destination);
                MapBuildScheduler.Install(_build, installed, CustomRooms.ArchiveDirectory(installed), CustomRooms.EntityDirectory(), CustomRooms.NodeDirectory());
                return installed;
            }
            catch
            {
                if (existed) File.Move(backup, destination, overwrite: true);
                else if (File.Exists(destination)) File.Delete(destination);
                throw;
            }
            finally { if (File.Exists(backup)) File.Delete(backup); }
        }
    }
    public void Dispose() { if (File.Exists(_snapshot)) File.Delete(_snapshot); }
}

public static class MapPackageInstaller
{
    internal static void RequireNoConflict(MapContentIdentity identity)
    {
        if (CustomRooms.Installed.TryGet(identity.MapId, out var previous)
            && !previous.Identity.RoomKey.Equals(identity.RoomKey, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An installed map's runtime name is immutable. Use a new map identity when renaming it.");
        if (Metadata.IsBuiltInRoom(identity.RoomKey)) throw new InvalidDataException("A custom map cannot replace a built-in room.");
        var conflict = CustomRooms.Definitions.FirstOrDefault(d => d.Name.Equals(identity.RoomKey, StringComparison.OrdinalIgnoreCase)
            && (d.MapId == Guid.Empty ? MapPackageBuilder.LegacyId(d.Name) : d.MapId) != identity.MapId);
        if (conflict != null) throw new InvalidDataException("Another map already uses this runtime name. Rename the map before sharing.");
    }
    public static async Task<PreparedMapInstallation> PrepareAsync(string download, MapContentIdentity required, CancellationToken token = default)
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
            return new(snapshot, build, identity);
        }
        catch { if (File.Exists(snapshot)) File.Delete(snapshot); throw; }
    }
    public static MapDefinition Install(string download, Guid id, string contentHash, string archiveHash, string library)
    {
        var identity = MapContentIdentity.FromPackage(download);
        if (identity.MapId != id || identity.ContentHash != MapHash256.Parse(contentHash) || identity.PackageHash != MapHash256.Parse(archiveHash))
            throw new InvalidDataException("Downloaded map identity does not match the server.");
        using var prepared = PrepareAsync(download, identity).GetAwaiter().GetResult();
        return prepared.Commit(library);
    }
}
