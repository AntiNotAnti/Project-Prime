#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.StudioReplay;

/// <summary>Transfers immutable replay/package cache lifetime to queued and detached exports.</summary>
public static class StudioReplayCachePins
{
    public static void ValidateTicket(StudioReplayExportTicket ticket)
    {
        if (ticket == null || ticket.Id == Guid.Empty || ticket.Request == null
            || ticket.RuntimePaths == null || ticket.CameraKeys == null || ticket.PackageDirectories == null)
            throw new InvalidDataException("Export ticket has missing required fields.");
        static void PathValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 32768) throw new InvalidDataException("Export ticket path is missing or exceeds its size limit.");
            _ = Path.GetFullPath(value);
        }
        PathValue(ticket.ReplayPath); PathValue(ticket.CacheRoot); PathValue(ticket.StatusFile); PathValue(ticket.CancelFile); PathValue(ticket.Request.Directory);
        if (string.IsNullOrWhiteSpace(ticket.Request.OutputName) || ticket.Request.OutputName.Length > 255
            || ticket.Request.StartFrame > ticket.Request.EndFrame
            || ticket.Request.Width is <= 0 or > 7680 || ticket.Request.Height is <= 0 or > 4320
            || ticket.Request.Fps is not (24 or 30 or 48 or 60 or 90 or 120 or 144))
            throw new InvalidDataException("Export ticket output request is invalid.");
        if (ticket.PackageDirectories.Count > 64 || ticket.RuntimePaths.Count > 64 || ticket.CameraKeys.Count > 64
            || string.IsNullOrWhiteSpace(ticket.MphKey) || string.IsNullOrWhiteSpace(ticket.FhKey))
            throw new InvalidDataException("Export ticket resource references exceed their limits.");
        foreach (string path in ticket.PackageDirectories) PathValue(path);
        foreach (var path in ticket.RuntimePaths)
            if (string.IsNullOrWhiteSpace(path.Key) || path.Key.Length > 128 || path.Value == null || path.Value.Length > 32768)
                throw new InvalidDataException("Export ticket runtime path is invalid.");
        if (ticket.RequiredPackageHash != null && !MapCommunityClient.ValidHash(ticket.RequiredPackageHash))
            throw new InvalidDataException("Export ticket exact package hash is invalid.");
        if (ticket.CameraKeys.Any(key => key == null)) throw new InvalidDataException("Export ticket contains an empty camera key.");
        if (ticket.CameraState is { Length: > 8192 }) throw new InvalidDataException("Export ticket camera state exceeds its canonical size limit.");
        if (ticket.Request.Audio?.Bindings is { } bindings && (bindings.Count > 64 || bindings.Any(binding => binding == null || string.IsNullOrWhiteSpace(binding.WaveFile))))
            throw new InvalidDataException("Export ticket contains invalid audio bindings.");
    }
    public static IDisposable PinFile(string immutableSource, CancellationToken cancellation = default)
    {
        string path = Path.GetFullPath(immutableSource);
        string directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("Immutable source has no cache directory.");
        string key = Path.GetFileName(directory);
        if (!MapCommunityClient.ValidHash(key)) throw new ArgumentException("Immutable source requires its exact content-hash directory.");
        if (!File.Exists(path)) throw new FileNotFoundException("The queued immutable replay source is unavailable.", path);
        IDisposable pin = MapDiskCache.Pin(Path.GetDirectoryName(directory)!, key, cancellation);
        if (File.Exists(path)) return pin;
        pin.Dispose(); throw new FileNotFoundException("The queued immutable replay source is unavailable.", path);
    }
    public static IDisposable PinPackage(string packagesRoot, string exactHash, CancellationToken cancellation = default)
    {
        if (!MapCommunityClient.ValidHash(exactHash)) throw new ArgumentException("Replay package hash is invalid.");
        string root = Path.GetFullPath(packagesRoot);
        if (!File.Exists(Path.Combine(root, exactHash, "source.ppmap"))) throw new FileNotFoundException("The exact queued map package is unavailable.");
        IDisposable pin = MapDiskCache.Pin(root, exactHash, cancellation);
        if (File.Exists(Path.Combine(root, exactHash, "source.ppmap"))) return pin;
        pin.Dispose(); throw new FileNotFoundException("The exact queued map package is unavailable.");
    }
    public static IDisposable PinReferences(StudioReplayExportTicket ticket, CancellationToken cancellation = default)
    {
        ValidateTicket(ticket);
        var pins = new List<IDisposable>();
        try
        {
            string? parent = Path.GetDirectoryName(Path.GetFullPath(ticket.ReplayPath));
            if (parent != null && MapCommunityClient.ValidHash(Path.GetFileName(parent))) pins.Add(PinFile(ticket.ReplayPath, cancellation));
            foreach (string root in ticket.PackageDirectories)
            {
                if (!Directory.Exists(root)) continue;
                if (ticket.RequiredPackageHash is { } hash)
                {
                    if (Directory.Exists(Path.Combine(root, hash))) pins.Add(PinPackage(root, hash, cancellation));
                }
                else
                {
                    // Tickets written before exact package references were added retain
                    // their available immutable packages until private child preparation.
                    foreach (string directory in Directory.EnumerateDirectories(root).Where(d => MapCommunityClient.ValidHash(Path.GetFileName(d))))
                        if (File.Exists(Path.Combine(directory, "source.ppmap"))) pins.Add(PinPackage(root, Path.GetFileName(directory), cancellation));
                }
            }
            return new Group(pins);
        }
        catch { foreach (var pin in pins) pin.Dispose(); throw; }
    }
    /// <summary>Includes persisted queued references after the parent process exits. Rechecks each entry to cover a ticket created during pruning.</summary>
    public static Func<string, bool> ProtectedDirectories(string replayCacheRoot)
    {
        string root = Path.GetFullPath(replayCacheRoot);
        return directory => IsReferenced(root, directory);
    }
    public static bool IsReferenced(string replayCacheRoot, string directory)
    {
        string root = Path.GetFullPath(replayCacheRoot), candidate = Path.GetFullPath(directory);
        string exports = Path.Combine(root, "exports");
        if (!Directory.Exists(exports)) return false;
        try
        {
            string[] directories = Directory.EnumerateDirectories(exports).Take(4097).ToArray();
            // Oversized queues fail closed. Active/queued work may exceed the nominal
            // idle-cache budget; its sources must not be guessed away.
            if (directories.Length > 4096) return Contains(root, candidate);
            int pending = 0;
            foreach (string job in directories)
            {
                string path = Path.Combine(job, "ticket.json");
                if (!File.Exists(path)) continue;
                using var input = File.OpenRead(path);
                if (input.Length > 1024 * 1024) throw new InvalidDataException("Persisted export ticket exceeds its size limit.");
                StudioReplayExportTicket? ticket = JsonSerializer.Deserialize<StudioReplayExportTicket>(input, new JsonSerializerOptions { IncludeFields = true });
                if (ticket == null) throw new InvalidDataException("Persisted export ticket is empty.");
                ValidateTicket(ticket);
                StudioReplayExportStatus? status = null;
                if (File.Exists(ticket.StatusFile) && new FileInfo(ticket.StatusFile).Length <= 65536)
                {
                    try { status = StudioReplayStatusFile.Read(ticket.StatusFile); }
                    catch (JsonException) { }
                }
                if (status?.Id != ticket.Id) status = null;
                if (status?.State is "Complete" or "Failed" or "Cancelled" || status?.OriginCaptured == true) continue;
                if (++pending > 256) return Contains(root, candidate);
                string? source = Path.GetDirectoryName(Path.GetFullPath(ticket.ReplayPath));
                if (source != null && MapCommunityClient.ValidHash(Path.GetFileName(source)) && Contains(source, candidate)) return true;
                foreach (string packages in ticket.PackageDirectories)
                {
                    if (ticket.RequiredPackageHash is { } hash && MapCommunityClient.ValidHash(hash))
                    { if (Contains(Path.Combine(Path.GetFullPath(packages), hash), candidate)) return true; }
                    else if (Contains(Path.GetFullPath(packages), candidate)) return true;
                }
            }
            return false;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return Contains(root, candidate); }
    }
    private static bool Contains(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(root, path, comparison) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison);
    }
    private sealed class Group(IReadOnlyList<IDisposable> pins) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) foreach (var pin in pins) pin.Dispose(); }
    }
}
#endif
