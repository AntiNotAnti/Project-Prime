using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace MphRead.Mods.Network;

/// <summary>Cross-process disk reservations and live pins for immutable hosted archives.</summary>
internal sealed class HostedPackageCache
{
    private readonly string _directory;
    private readonly long _budget, _reservation;
    internal HostedPackageCache(string directory, long budget = 2L * 1024 * 1024 * 1024,
        long reservation = 512L * 1024 * 1024)
    { _directory = Path.GetFullPath(directory); _budget = budget; _reservation = reservation; Directory.CreateDirectory(_directory); }

    private FileStream Lock(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(_directory, ".cache-lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { if (token.WaitHandle.WaitOne(25)) token.ThrowIfCancellationRequested(); }
        }
    }
    internal IDisposable Reserve(CancellationToken token, long? bytes = null)
    {
        long requested = bytes ?? _reservation;
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        using var gate = Lock(token);
        Reap(".reserve-*"); Reap(".pin-*"); ReapLibraries();
        if (!Directory.EnumerateFiles(_directory, ".reserve-*").Any())
            foreach (string abandoned in Directory.EnumerateFiles(_directory, "*.download")) File.Delete(abandoned);
        long reserved = Directory.EnumerateFiles(_directory, ".reserve-*")
            .Sum(path => long.TryParse(Path.GetFileName(path).Split('-')[1], out long bytes) ? bytes : _reservation);
        var archives = Directory.EnumerateFiles(_directory, "*.ppmap").Select(path => new FileInfo(path)).ToList();
        // Live partials are already covered by their reservation; counting them
        // again is conservative. It also covers a dead worker's partial while
        // another worker prevents the idle cleanup from removing it.
        long bytesUsed = archives.Sum(file => file.Length)
            + Directory.EnumerateFiles(_directory, "*.download").Sum(path => new FileInfo(path).Length);
        var pinned = Directory.EnumerateFiles(_directory, ".pin-*")
            .Select(path => Path.GetFileName(path).Split('-')[1]).ToHashSet(StringComparer.Ordinal);
        string libraries = Path.Combine(_directory, "lobbies");
        if (Directory.Exists(libraries))
        {
            foreach (string marker in Directory.EnumerateFiles(libraries, ".cache-pins", SearchOption.AllDirectories))
                foreach (string hash in File.ReadLines(marker)) pinned.Add(hash);
            foreach (string marker in Directory.EnumerateFiles(libraries, ".cache-copy-bytes", SearchOption.AllDirectories))
                bytesUsed += File.ReadLines(marker).Sum(line => long.TryParse(line, out long size) ? size : _reservation);
        }
        foreach (var archive in archives.OrderBy(file => file.LastWriteTimeUtc).ThenBy(file => file.Name, StringComparer.Ordinal))
        {
            if (bytesUsed + reserved + requested <= _budget) break;
            if (pinned.Contains(Path.GetFileNameWithoutExtension(archive.Name))) continue;
            long size = archive.Length; File.Delete(archive.FullName); bytesUsed -= size;
        }
        if (requested > _budget || bytesUsed + reserved + requested > _budget)
            throw new IOException("Hosted package cache has no unpinned space for this download. Try after an active lobby closes.");
        return Lease.Create(Path.Combine(_directory, ".reserve-" + requested + "-" + Guid.NewGuid().ToString("N")));
    }
    internal IDisposable Pin(string hash, CancellationToken token)
    {
        if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("Invalid package hash.");
        using var gate = Lock(token);
        return Lease.Create(Path.Combine(_directory, ".pin-" + hash.ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N")));
    }
    internal void RecordCopy(string library, long bytes, CancellationToken token)
    {
        using var gate = Lock(token);
        File.AppendAllLines(Path.Combine(library, ".cache-copy-bytes"),
            new[] { bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }
    internal void PublishPins(string library, IEnumerable<string> hashes, CancellationToken token)
    {
        using var gate = Lock(token);
        File.WriteAllLines(Path.Combine(library, ".cache-pins"), hashes.Distinct());
    }
    internal void Touch(string path) => File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
    private void Reap(string pattern)
    {
        foreach (string path in Directory.EnumerateFiles(_directory, pattern))
        {
            try { using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } File.Delete(path); }
            catch (IOException) { } // Another process still owns this reservation/pin.
        }
    }
    internal static void SetLibraryOwner(string library, Process? owner = null)
    {
        using var current = owner == null ? Process.GetCurrentProcess() : null;
        var process = owner ?? current!;
        string marker = Path.Combine(library, ".cache-owner");
        string temporary = marker + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, process.Id + ":" + process.StartTime.ToUniversalTime().Ticks);
        File.Move(temporary, marker, overwrite: true);
    }
    private void ReapLibraries()
    {
        string libraries = Path.Combine(_directory, "lobbies");
        if (!Directory.Exists(libraries)) return;
        foreach (string library in Directory.EnumerateDirectories(libraries))
        {
            string marker = Path.Combine(library, ".cache-owner");
            // Leave a short handoff window for a child to register after parent
            // exit. Child startup replaces this marker before reading its catalog.
            if (!File.Exists(marker) || DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) < TimeSpan.FromMinutes(2)) continue;
            string[] owner;
            using (var file = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(file)) owner = reader.ReadToEnd().Split(':');
            if (owner.Length != 2 || !int.TryParse(owner[0], out int pid) || !long.TryParse(owner[1], out long incarnation)) continue;
            bool running;
            try
            {
                using var process = Process.GetProcessById(pid);
                running = !process.HasExited && process.StartTime.ToUniversalTime().Ticks == incarnation;
            }
            catch (ArgumentException) { running = false; }
            catch (InvalidOperationException) { running = false; }
            catch (System.ComponentModel.Win32Exception) { continue; } // Cannot inspect another owner: retain its pin.
            if (!running) Directory.Delete(library, recursive: true);
        }
    }
    private sealed class Lease(string path, FileStream guard) : IDisposable
    {
        internal static Lease Create(string path) => new(path, new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None));
        public void Dispose() { guard.Dispose(); if (File.Exists(path)) File.Delete(path); }
    }
    internal sealed class Pins(HostedPackageCache cache) : IDisposable
    {
        private readonly List<IDisposable> _leases = new();
        internal void Add(string hash, CancellationToken token) => _leases.Add(cache.Pin(hash, token));
        public void Dispose() { foreach (var lease in _leases) lease.Dispose(); }
    }
}
