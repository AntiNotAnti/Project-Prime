using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

namespace MphRead.Mods.StudioReplay;

/// <summary>One replay's immutable package and generated-room resolution. The
/// thread scope changes lookup context; no shared catalog or asset root changes.</summary>
internal sealed class StudioReplayResources : IDisposable
{
    [ThreadStatic] private static StudioReplayResources? _current;
    internal static StudioReplayResources? Current => _current;
    private readonly string _root;
    private readonly string[] _packageDirectories;
    private readonly Dictionary<string, Model> _models = new(StringComparer.Ordinal);
    private readonly object _prepareGate = new();
    private readonly object _pinGate = new();
    private IDisposable? _packagePin;
    private bool _disposed;
    private RoomMetadata? _room;
    private MapContentIdentity? _identity;
    internal string? CustomMapRoot { get; private set; }
    internal StudioReplayResources(string root, IEnumerable<string>? packageDirectories = null)
    {
        _root = Path.GetFullPath(root);
        _packageDirectories = (packageDirectories ?? Array.Empty<string>()).Append(CustomRooms.UserMapDirectory)
            .Select(Path.GetFullPath).Distinct().ToArray();
    }
    internal IDisposable Enter()
    {
        var previous = _current; _current = this;
        return new Scope(previous);
    }
    private sealed class Scope(StudioReplayResources? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose() { if (_disposed) return; _disposed = true; _current = previous; }
    }
    internal RoomMetadata? Room(string name) => _room is { } room
        && StringComparer.OrdinalIgnoreCase.Equals(name, room.Name) ? room : null;
    internal RoomMetadata? RoomById(int id) => _room?.Id == id ? _room : null;
    internal bool Matches(MapContentIdentity identity) => _identity is { } own && own.Matches(identity);
    internal Model RoomModel(RoomMetadata room, Func<RoomMetadata, Model> load)
    {
        if (!_models.TryGetValue(room.ModelPath, out var model)) _models.Add(room.ModelPath, model = load(room));
        return model;
    }
    internal void Prepare(ReplayMetadata metadata, CancellationToken cancellation)
    {
        if (ReplayMapIdentity.CustomSession(metadata) is not { } session) return;
        var required = session.Match.MapIdentity.Content(metadata.RoomKey);
        lock (_prepareGate)
        {
            lock (_pinGate) ObjectDisposedException.ThrowIf(_disposed, this);
            cancellation.ThrowIfCancellationRequested();
            if (_identity is { } existing)
            {
                if (!existing.Matches(required)) throw new InvalidDataException("Replay package identity changed during preparation.");
                return;
            }
            string directory = Path.Combine(_root, "packages", required.PackageHash.ToString());
            using var packageLease = MapDiskCache.Acquire(Path.Combine(_root, "packages"), required.PackageHash.ToString(), cancellation);
            Directory.CreateDirectory(directory);
            string package = Path.Combine(directory, "source.ppmap");
            if (!Exact(package, required))
            {
                string? local = FindLocal(required);
                if (local != null)
                {
                    string staging = package + "." + Guid.NewGuid().ToString("N") + ".staging";
                    try
                    {
                        File.Copy(local, staging);
                        if (!Exact(staging, required)) throw new InvalidDataException("Local map package changed during preparation.");
                        cancellation.ThrowIfCancellationRequested(); File.Move(staging, package, overwrite: true);
                    }
                    finally { if (File.Exists(staging)) File.Delete(staging); }
                }
                else
                {
                    string configured = NetworkMapIdentity.ConfiguredDownloadSource();
                    string address = string.IsNullOrWhiteSpace(session.MapDownloadSource) ? configured : session.MapDownloadSource;
                    if (new Uri(address).IsLoopback && new Uri(address) != new Uri(configured))
                        throw new InvalidDataException("The replay's local Community address differs from your configured service.");
                    using var community = new MapCommunityClient(address);
                    community.DownloadExactAsync(required, package, cancellation).GetAwaiter().GetResult();
                }
            }
            var definition = MapDefinition.Load(package);
            var scheduler = new MapBuildScheduler(Path.Combine(directory, "runtime"));
            var build = scheduler.BuildAsync(MapBuildSnapshot.Capture(definition), cancellation).GetAwaiter().GetResult();
            if (!build.Succeeded || build.Outputs == null)
                throw new InvalidDataException("The exact replay map could not be built: " + string.Join("; ", build.Diagnostics.Select(d => d.Message)));
            cancellation.ThrowIfCancellationRequested();
            // Publish to this resolver only after all exact bytes are verified.
            // Failed preparation leaves any currently presented world untouched.
            var room = CustomRooms.MakeMetadata(definition, 128).WithPrivateResources(build.Outputs);
            var pin = MapDiskCache.Pin(Path.Combine(_root, "packages"), required.PackageHash.ToString(), cancellation);
            try
            {
                lock (_pinGate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    _room = room; _identity = required; CustomMapRoot = Path.GetDirectoryName(build.Outputs.Model);
                    _packagePin = pin; pin = null!;
                }
            }
            finally { pin?.Dispose(); }
        }
    }
    private string? FindLocal(MapContentIdentity required)
    {
        if (CustomRooms.Installed.HasExact(required) && CustomRooms.Installed.TryGet(required.MapId, out var installed)
            && Exact(installed.PackagePath, required)) return installed.PackagePath;
        foreach (string directory in _packageDirectories)
        {
            if (!Directory.Exists(directory)) continue;
            foreach (string path in Directory.EnumerateFiles(directory, "*.ppmap", SearchOption.AllDirectories))
                if (Exact(path, required)) return path;
        }
        return null;
    }
    private static bool Exact(string path, MapContentIdentity required)
    {
        try { return File.Exists(path) && MapContentIdentity.FromPackage(path).Matches(required); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return false; }
    }
    internal IDisposable? RetainPackagePin()
    {
        lock (_pinGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _packagePin == null ? null : MapDiskCache.Retain(_packagePin);
        }
    }
    public void Dispose()
    {
        IDisposable? pin;
        lock (_pinGate) { if (_disposed) return; _disposed = true; pin = _packagePin; _packagePin = null; }
        // Never wait for detached package/build preparation on the scene owner.
        pin?.Dispose(); _models.Clear();
    }
}
