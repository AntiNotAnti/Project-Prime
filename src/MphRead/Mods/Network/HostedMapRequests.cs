using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Network;

// Owned/pumped by the network loop. Workers only write verified private archives.
internal sealed class HostedMapRequests : IDisposable
{
    private sealed class Pending
    {
        public HostRequestPacket Request;
        public IPEndPoint Sender = null!;
        public byte[] Wire = null!;
        public Task<string> Download = null!;
        public CancellationTokenSource Cancel = null!;
        public HostReplyPacket? Reply;
        public double CompletedAt;
    }
    private readonly List<Pending> _pending = new();
    private readonly string? _cacheDirectory, _source;
    internal HostedMapRequests(string? cacheDirectory = null, string? source = null)
    { _cacheDirectory = cacheDirectory; _source = source; }
    public int ActiveCount => _pending.Count(p => p.Reply == null);
    internal static string CacheDirectory => Path.Combine(Platform.AppPaths.UserDataDirectory, "hosted-map-packages");

    public void Enqueue(HostRequestPacket request, IPEndPoint sender, double now, Action<IPEndPoint, HostReplyPacket> send)
    {
        byte[] wire = new byte[request.Length]; request.Write(wire);
        var existing = _pending.FirstOrDefault(p => p.Sender.Equals(sender));
        if (existing != null)
        {
            if (!existing.Wire.SequenceEqual(wire)) send(sender, new() { Reason = "A different hosting request is already pending on this connection." });
            else if (existing.Reply is { } reply) send(sender, reply);
            return;
        }
        if (_pending.Count >= 32 || _pending.Count(p => p.Reply == null) >= 4)
        { send(sender, new() { Reason = "The host is preparing other maps. Try again shortly." }); return; }
        try
        {
            Validate(request);
            string address = _source ?? NetworkMapIdentity.ConfiguredDownloadSource(); // Operator-controlled; never a client-supplied URL.
            string? installed = CustomRooms.Installed.TryGet(request.MapIdentity.MapId, out var map)
                && map.Identity.Matches(request.MapIdentity.Content(request.RoomKey)) ? map.PackagePath : null;
            var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            _pending.Add(new Pending { Request = request, Sender = sender, Wire = wire, Cancel = cancel,
                Download = Task.Run(() => PrepareArchiveAsync(request, address, _cacheDirectory ?? CacheDirectory, installed, cancel.Token)) });
        }
        catch (Exception ex) { send(sender, new() { Reason = ex.Message }); }
    }

    internal static void Validate(HostRequestPacket request)
    {
        if (!request.MapIdentity.IsCustom || !request.MapIdentity.IsValid || Metadata.IsBuiltInRoom(request.RoomKey))
            throw new InvalidDataException("Invalid custom map hosting identity.");
        MapValidator.RequireRuntimeName(request.RoomKey);
        if (request.Rotation is { Count: > 0 } && request.Rotation[0].RoomKey != request.RoomKey)
            throw new InvalidDataException("The requested package must be the first map in the rotation.");
    }

    internal static async Task<string> PrepareArchiveAsync(HostRequestPacket request, string address, string directory, string? installed, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Validate(request);
        Directory.CreateDirectory(directory);
        var identity = request.MapIdentity.Content(request.RoomKey);
        string path = Path.Combine(directory, identity.PackageHash + ".ppmap");
        if (File.Exists(path))
        {
            try { if (MapContentIdentity.FromPackage(path).Matches(identity)) return path; }
            catch (InvalidDataException) { }
        }
        if (Directory.EnumerateFiles(directory, "*.ppmap").Sum(p => new FileInfo(p).Length) > 2L * 1024 * 1024 * 1024)
            throw new IOException("Hosted package cache is full; ask the server operator to clear unused packages.");
        token.ThrowIfCancellationRequested();
        if (installed != null && File.Exists(installed) && MapContentIdentity.FromPackage(installed).Matches(identity))
            AtomicFile.Write(path, await File.ReadAllBytesAsync(installed, token).ConfigureAwait(false));
        else
        {
            using var client = new MapCommunityClient(address);
            await client.DownloadExactAsync(identity, path, token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        return path;
    }

    public void Pump(double now, Func<HostRequestPacket, IPEndPoint, double, string, HostReplyPacket> start,
        Action<IPEndPoint, HostReplyPacket> send)
    {
        foreach (var pending in _pending)
        {
            if (pending.Reply != null || !pending.Download.IsCompleted) continue;
            try { pending.Reply = start(pending.Request, pending.Sender, now, pending.Download.GetAwaiter().GetResult()); }
            catch (Exception ex) { pending.Reply = new HostReplyPacket { Reason = "Map preparation failed: " + ex.GetBaseException().Message }; }
            pending.CompletedAt = now; pending.Cancel.Dispose();
            send(pending.Sender, pending.Reply.Value);
        }
        _pending.RemoveAll(p => p.Reply != null && now - p.CompletedAt > 240);
    }
    public void Dispose()
    {
        foreach (var pending in _pending.Where(p => p.Reply == null))
        {
            pending.Cancel.Cancel();
            _ = pending.Download.ContinueWith(t => { _ = t.Exception; pending.Cancel.Dispose(); }, TaskScheduler.Default);
        }
        _pending.Clear();
    }
}
