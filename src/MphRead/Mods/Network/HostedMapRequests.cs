using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Network;

public sealed record HostedMapArchive(
    string RoomKey, MapContentIdentity Identity, string PackagePath);

public sealed record HostedMapPreparation(IReadOnlyList<HostedMapArchive> Archives,
    string? LibraryPath = null, string? RuntimeNamespace = null)
{
    public HostedMapArchive? Find(string roomKey, MapHash256 packageHash)
        => Archives.FirstOrDefault(a => StringComparer.OrdinalIgnoreCase.Equals(a.RoomKey, roomKey)
            && a.Identity.PackageHash == packageHash);
}

// Owned/pumped by the network loop. Workers only write verified private archives.
internal sealed class HostedMapRequests : IDisposable
{
    private sealed class Pending
    {
        public HostRequestPacket Request;
        public IPEndPoint Sender = null!;
        public byte[] Wire = null!;
        public Task<HostedMapPreparation> Download = null!;
        public CancellationTokenSource Cancel = null!;
        public HostReplyPacket? Reply;
        public double CompletedAt;
    }

    private readonly List<Pending> _pending = new();
    private readonly string? _cacheDirectory, _source;

    internal HostedMapRequests(string? cacheDirectory = null, string? source = null)
    {
        _cacheDirectory = cacheDirectory;
        _source = source;
    }

    public int ActiveCount => _pending.Count(p => p.Reply == null);
    internal static string CacheDirectory => Path.Combine(
        Platform.AppPaths.UserDataDirectory, "hosted-map-packages");

    public void Enqueue(HostRequestPacket request, IPEndPoint sender, double now,
        Action<IPEndPoint, HostReplyPacket> send)
    {
        byte[] wire = new byte[request.Length];
        request.Write(wire);
        var existing = _pending.FirstOrDefault(p => p.Sender.Equals(sender));
        if (existing != null)
        {
            if (!existing.Wire.SequenceEqual(wire))
                send(sender, new() { Reason = "A different hosting request is already pending on this connection." });
            else if (existing.Reply is { } reply)
                send(sender, reply);
            return;
        }
        if (_pending.Count >= 32 || _pending.Count(p => p.Reply == null) >= 4)
        {
            send(sender, new() { Reason = "The host is preparing other maps. Try again shortly." });
            return;
        }

        try
        {
            Validate(request);
            string address = _source ?? NetworkMapIdentity.ConfiguredDownloadSource();
            var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            _pending.Add(new Pending
            {
                Request = request,
                Sender = sender,
                Wire = wire,
                Cancel = cancel,
                Download = Task.Run(() => PrepareArchivesAsync(
                    request, address, _cacheDirectory ?? CacheDirectory, null, cancel.Token))
            });
        }
        catch (Exception ex)
        {
            send(sender, new() { Reason = ex.Message });
        }
    }

    internal static IReadOnlyList<HostRotationEntry> RequestedMaps(HostRequestPacket request)
    {
        if (request.Rotation is { Count: > 0 })
            return request.Rotation;

        GameMode mode = Enum.IsDefined(typeof(GameMode), request.Mode)
            ? (GameMode)request.Mode
            : GameMode.Battle;
        return new[]
        {
            new HostRotationEntry(request.RoomKey, mode,
                request.MapIdentity.IsCustom ? request.MapIdentity.PackageHash : default)
        };
    }

    internal static void Validate(HostRequestPacket request)
    {
        if (!request.MapIdentity.IsValid)
            throw new InvalidDataException("Invalid first-map hosting identity.");

        IReadOnlyList<HostRotationEntry> entries = RequestedMaps(request);
        if (entries.Count == 0 || entries.Count > HostRequestPacket.MaxRotation
            || !StringComparer.OrdinalIgnoreCase.Equals(entries[0].RoomKey, request.RoomKey))
        {
            throw new InvalidDataException("The requested first map must be rotation entry zero.");
        }

        for (int i = 0; i < entries.Count; i++)
        {
            HostRotationEntry entry = entries[i];
            bool builtIn = Metadata.IsBuiltInRoom(entry.RoomKey);
            if (builtIn)
            {
                if (!entry.PackageHash.IsZero)
                    throw new InvalidDataException("Built-in rotation maps cannot carry a Community package hash.");
            }
            else
            {
                MapValidator.RequireRuntimeName(entry.RoomKey);
                if (entry.PackageHash.IsZero)
                    throw new InvalidDataException("Every custom rotation map requires an exact Community package hash.");
            }
        }

        HostRotationEntry first = entries[0];
        if (request.MapIdentity.IsCustom)
        {
            if (Metadata.IsBuiltInRoom(request.RoomKey)
                || first.PackageHash != request.MapIdentity.PackageHash)
            {
                throw new InvalidDataException("The first rotation package does not match the requested map identity.");
            }
        }
        else if (!Metadata.IsBuiltInRoom(request.RoomKey) || !first.PackageHash.IsZero)
        {
            throw new InvalidDataException("A custom first map requires a full map identity.");
        }

        foreach (var group in entries.Where(e => e.IsCustom)
            .GroupBy(e => e.RoomKey, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Select(e => e.PackageHash).Distinct().Skip(1).Any())
                throw new InvalidDataException("One hosted rotation cannot request two versions of the same runtime map.");
        }
    }

    internal static async Task<HostedMapPreparation> PrepareArchivesAsync(
        HostRequestPacket request, string address, string directory,
        string? firstInstalledOverride, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Validate(request);
        Directory.CreateDirectory(directory);
        var cache = new HostedPackageCache(directory);
        using var cachePins = new HostedPackageCache.Pins(cache);

        IReadOnlyList<HostRotationEntry> entries = RequestedMaps(request);
        var archives = new List<HostedMapArchive>();
        var resolved = new Dictionary<MapHash256, HostedMapArchive>();

        for (int i = 0; i < entries.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            HostRotationEntry entry = entries[i];
            if (!entry.IsCustom)
                continue;
            cachePins.Add(entry.PackageHash.ToString(), token);
            if (resolved.TryGetValue(entry.PackageHash, out HostedMapArchive? duplicate))
            {
                if (!StringComparer.OrdinalIgnoreCase.Equals(duplicate.RoomKey, entry.RoomKey))
                    throw new InvalidDataException("One Community package cannot stand for two runtime map names.");
                archives.Add(duplicate);
                continue;
            }

            MapContentIdentity identity;
            if (i == 0 && request.MapIdentity.IsCustom)
            {
                identity = request.MapIdentity.Content(entry.RoomKey);
            }
            else if (TryReadCachedIdentity(directory, entry, token) is { } cachedIdentity)
            {
                // A validated content-addressed package already contains its
                // hash-bound manifest facts. Offline rotation reuse does
                // not need a second Community metadata availability dependency.
                identity = cachedIdentity;
            }
            else
            {
                using var lookup = new MapCommunityClient(address);
                CommunityMap? metadata = await lookup.GetPackageAsync(
                    entry.PackageHash.ToString(), token).ConfigureAwait(false);
                if (metadata == null)
                    throw new InvalidDataException("A rotation map is no longer available from Community: " + entry.RoomKey);
                if (!StringComparer.OrdinalIgnoreCase.Equals(metadata.Name, entry.RoomKey)
                    || metadata.Hash != entry.PackageHash.ToString())
                {
                    throw new InvalidDataException("Community metadata does not match rotation map " + entry.RoomKey + ".");
                }
                if (metadata.MinimumProtocol > NetConfig.ProtocolVersion)
                    throw new InvalidDataException("A rotation map requires a newer Project Prime protocol: " + entry.RoomKey);
                identity = new MapContentIdentity(metadata.MapId, metadata.Name,
                    MapHash256.Parse(metadata.ContentHash),
                    MapHash256.Parse(metadata.Hash), true);
            }

            string? installed = i == 0 && firstInstalledOverride != null
                ? firstInstalledOverride
                : CustomRooms.Installed.TryGet(entry.RoomKey, out var local)
                    && local.Identity.Matches(identity)
                    ? local.PackagePath
                    : null;

            string package = await PrepareOneAsync(
                identity, address, directory, installed, token).ConfigureAwait(false);
            var archive = new HostedMapArchive(entry.RoomKey, identity, package);
            resolved[entry.PackageHash] = archive;
            archives.Add(archive);
        }

        foreach (var group in archives.GroupBy(a => a.Identity.MapId))
        {
            if (group.Select(a => a.Identity.PackageHash).Distinct().Skip(1).Any())
                throw new InvalidDataException("A hosted rotation cannot stage two versions with the same map identity.");
        }

        if (archives.Count == 0)
            return new HostedMapPreparation(archives);

        // Child-library staging belongs to this background preparation job,
        // not to the parent server's packet loop. Packages are immutable and
        // content-addressed, so hard links avoid a second hundreds-of-megabytes
        // copy per lobby. Filesystems without hard links fall back to copying.
        string runtimeNamespace = Guid.NewGuid().ToString("N");
        string library = Path.Combine(directory, "lobbies", runtimeNamespace);
        try
        {
            Directory.CreateDirectory(library);
            HostedPackageCache.SetLibraryOwner(library);
            foreach (HostedMapArchive archive in archives
                .GroupBy(a => a.Identity.MapId).Select(group => group.First()))
            {
                token.ThrowIfCancellationRequested();
                string target = Path.Combine(library,
                    archive.Identity.MapId.ToString("N") + MapBundle.Extension);
                if (!File.Exists(target))
                {
                    if (!TryCreateHardLink(archive.PackagePath, target))
                    {
                        long copyBytes = new FileInfo(archive.PackagePath).Length;
                        using var reservation = cache.Reserve(token, copyBytes);
                        File.Copy(archive.PackagePath, target, overwrite: false);
                        cache.RecordCopy(library, copyBytes, token);
                    }
                }
            }
            cache.PublishPins(library, archives.Select(a => a.Identity.PackageHash.ToString()), token);
            return new HostedMapPreparation(archives, library, runtimeNamespace);
        }
        catch
        {
            TryDeleteDirectory(library);
            throw;
        }
    }

    private static MapContentIdentity? TryReadCachedIdentity(string directory, HostRotationEntry entry, CancellationToken token)
    {
        string path = Path.Combine(directory, entry.PackageHash + ".ppmap");
        using var lease = MapDiskCache.Acquire(directory, entry.PackageHash.ToString(), token);
        try
        {
            if (!File.Exists(path)) return null;
            var identity = MapContentIdentity.FromPackage(path);
            return identity.PackageHash == entry.PackageHash
                && StringComparer.OrdinalIgnoreCase.Equals(identity.RoomKey, entry.RoomKey)
                ? identity : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return null; }
        finally
        {
            lease.Dispose();
            MapDiskCache.RemoveIdleKey(directory, entry.PackageHash.ToString());
        }
    }

    private static async Task<string> PrepareOneAsync(
        MapContentIdentity identity, string address, string directory,
        string? installed, CancellationToken token)
    {
        string path = Path.Combine(directory, identity.PackageHash + ".ppmap");
        // One publisher per content address also keeps child hard links on the
        // same inode. Concurrent equivalent overwrites could otherwise strand
        // physical archive copies outside the cache's byte accounting.
        using var archiveLease = MapDiskCache.Acquire(directory, identity.PackageHash.ToString(), token);
        try
        {
            if (File.Exists(path))
            {
                try
                {
                    if (MapContentIdentity.FromPackage(path).Matches(identity))
                    {
                        new HostedPackageCache(directory).Touch(path);
                        return path;
                    }
                }
                catch (InvalidDataException) { }
            }

            using var reservation = new HostedPackageCache(directory).Reserve(token);

            token.ThrowIfCancellationRequested();
            if (installed != null && File.Exists(installed)
                && MapContentIdentity.FromPackage(installed).Matches(identity))
            {
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".download";
                try
                {
                    await using (var input = new FileStream(installed, FileMode.Open, FileAccess.Read, FileShare.Read))
                    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        await MapCommunityClient.CopyBoundedAsync(input, output, MapPackageReader.MaxArchiveBytes, token).ConfigureAwait(false);
                    if (!MapContentIdentity.FromPackage(temporary).Matches(identity)) throw new InvalidDataException("Installed archive changed while caching.");
                    token.ThrowIfCancellationRequested(); File.Move(temporary, path, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            else
            {
                using var client = new MapCommunityClient(address);
                await client.DownloadExactAsync(identity, path, token).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            return path;
        }
        finally
        {
            archiveLease.Dispose();
            // Stable owner fencing also reclaims idle content-key lock files;
            // failed arbitrary hashes must not accumulate cache inodes forever.
            MapDiskCache.RemoveIdleKey(directory, identity.PackageHash.ToString());
        }
    }

    internal static void LinkOrCopy(string source, string target)
    {
        if (TryCreateHardLink(source, target)) return;
        File.Copy(source, target, overwrite: false);
    }

    private static bool TryCreateHardLink(string source, string target)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return CreateHardLinkWindows(target, source, IntPtr.Zero);
            return LinkUnix(source, target) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException
            or PlatformNotSupportedException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true,
        EntryPoint = "CreateHardLinkW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkWindows(
        string fileName, string existingFileName, IntPtr securityAttributes);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int LinkUnix(string existingPath, string newPath);

    internal static void TryDeleteDirectory(string? path)
    {
        if (String.IsNullOrWhiteSpace(path)) return;
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Compatibility helper for the focused single-map tests and tools.
    internal static async Task<string> PrepareArchiveAsync(
        HostRequestPacket request, string address, string directory,
        string? installed, CancellationToken token)
    {
        HostedMapPreparation prepared = await PrepareArchivesAsync(
            request, address, directory, installed, token).ConfigureAwait(false);
        HostedMapArchive? first = prepared.Find(
            request.RoomKey, request.MapIdentity.PackageHash);
        return first?.PackagePath
            ?? throw new InvalidDataException("The requested first custom map was not prepared.");
    }

    public void Pump(double now,
        Func<HostRequestPacket, IPEndPoint, double, HostedMapPreparation, HostReplyPacket> start,
        Action<IPEndPoint, HostReplyPacket> send)
    {
        foreach (var pending in _pending)
        {
            if (pending.Reply != null || !pending.Download.IsCompleted)
                continue;
            HostedMapPreparation? prepared = null;
            try
            {
                prepared = pending.Download.GetAwaiter().GetResult();
                pending.Reply = start(pending.Request, pending.Sender, now, prepared);
                if (pending.Reply is { Started: false })
                    TryDeleteDirectory(prepared.LibraryPath);
            }
            catch (Exception ex)
            {
                if (prepared != null) TryDeleteDirectory(prepared.LibraryPath);
                pending.Reply = new HostReplyPacket
                { Reason = "Map preparation failed: " + ex.GetBaseException().Message };
            }
            pending.CompletedAt = now;
            pending.Cancel.Dispose();
            send(pending.Sender, pending.Reply.Value);
        }
        _pending.RemoveAll(p => p.Reply != null && now - p.CompletedAt > 240);
    }

    public void Dispose()
    {
        foreach (var pending in _pending.Where(p => p.Reply == null))
        {
            pending.Cancel.Cancel();
            _ = pending.Download.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion)
                    TryDeleteDirectory(t.Result.LibraryPath);
                else
                    _ = t.Exception;
                pending.Cancel.Dispose();
            }, TaskScheduler.Default);
        }
        _pending.Clear();
    }
}
