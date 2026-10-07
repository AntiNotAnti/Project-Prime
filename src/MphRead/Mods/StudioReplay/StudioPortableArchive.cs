using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace MphRead.Mods.StudioReplay;

/// <summary>Extracts only the fixed portable-replay members into an empty private job directory.</summary>
internal static class StudioPortableArchive
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
        { "manifest.json", "replay.ppdemo", "map.ppmap", "replay.ppdemo.studio.json", "replay.ppdemo.reel.json" };
    internal static void Extract(string archivePath, string directory, CancellationToken cancellation)
    {
        Directory.CreateDirectory(directory);
        using (var members = Directory.EnumerateFileSystemEntries(directory).GetEnumerator())
            if (members.MoveNext()) throw new InvalidDataException("Portable replay extraction requires an empty private directory.");
        using var archive = ZipFile.OpenRead(archivePath);
        var names = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            int mode = (entry.ExternalAttributes >> 16) & 0xf000;
            if (!Names.Contains(entry.FullName) || !names.Add(entry.FullName) || mode != 0 && mode != 0x8000 || (entry.ExternalAttributes & 0x10) != 0)
                throw new InvalidDataException("Portable replay contains an unexpected, duplicate or linked archive member.");
            long maximum = entry.FullName.EndsWith(".json", StringComparison.Ordinal) ? 512 * 1024 : 1024L * 1024 * 1024;
            total = checked(total + entry.Length);
            if (entry.Length < 0 || entry.Length > maximum || total > 1024L * 1024 * 1024)
                throw new InvalidDataException("Portable replay exceeds the extraction budget.");
        }
        if (!names.Contains("manifest.json") || !names.Contains("replay.ppdemo")) throw new InvalidDataException("Portable replay is incomplete.");
        byte[] buffer = new byte[65536];
        foreach (var entry in archive.Entries)
        {
            using var input = entry.Open();
            using var output = new FileStream(Path.Combine(directory, entry.FullName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            long copied = 0; int count;
            while ((count = input.Read(buffer)) != 0)
            {
                cancellation.ThrowIfCancellationRequested(); copied = checked(copied + count);
                if (copied > entry.Length) throw new InvalidDataException("Portable replay member expands beyond its declared size.");
                output.Write(buffer, 0, count);
            }
            if (copied != entry.Length) throw new InvalidDataException("Portable replay member is truncated.");
            output.Flush(flushToDisk: true);
        }
    }
}
