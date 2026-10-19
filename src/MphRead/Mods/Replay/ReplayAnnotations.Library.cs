using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MphRead.Mods.Replay;

internal sealed record ReplayAnnotationLibrarySnapshot(bool Available, int BookmarkCount,
    int HighlightCount, string[] Tags, string[] Collections, string Names);

internal static partial class ReplayAnnotations
{
    // Library metadata is a bounded worker snapshot, never a replay-stream scan.
    internal const int MaximumLibrarySidecarBytes = 1024 * 1024;
    internal static ReplayAnnotationLibrarySnapshot ReadLibrarySnapshot(string replay)
    {
        static ReplayAnnotationLibrarySnapshot Empty(bool available) => new(available, 0, 0,
            Array.Empty<string>(), Array.Empty<string>(), "");
        if (String.IsNullOrWhiteSpace(replay)) return Empty(false);
        try
        {
            string path = Sidecar(replay);
            var before = new FileInfo(path);
            if (!UnlinkedParents(before.Directory)) return Empty(false);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (FileNotFoundException) { return Empty(true); }
            catch (DirectoryNotFoundException) { return Empty(true); }
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                return Empty(false);
            before.Refresh();
            long length = before.Length, stamp = before.LastWriteTimeUtc.Ticks;
            if (length <= 0 || length > MaximumLibrarySidecarBytes) return Empty(false);
            byte[] bytes = new byte[(int)length];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                stream.ReadExactly(bytes);
                if (stream.ReadByte() != -1) return Empty(false);
            }
            // Match the retained owner’s BOM-aware text encoding semantics.
            using var text = new StreamReader(new MemoryStream(bytes));
            var document = JsonSerializer.Deserialize<ReplayAnnotationDocument>(text.ReadToEnd(), JsonOptions);
            if (document is not { Version: 1 }) return Empty(false);
            var after = new FileInfo(path);
            if (!after.Exists || after.LinkTarget != null || after.Length != length
                || after.LastWriteTimeUtc.Ticks != stamp || !UnlinkedParents(after.Directory)) return Empty(false);
            document = Sanitize(document);
            return new(true, document.Bookmarks.Count, document.Highlights.Count,
                document.Tags.ToArray(), document.Collections.ToArray(),
                String.Join(" ", document.Bookmarks.Select(x => x.Name).Concat(document.Highlights.Select(x => x.Name))));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or NotSupportedException)
        { return Empty(false); }
    }

    private static bool UnlinkedParents(DirectoryInfo? directory)
    {
        for (; directory != null; directory = directory.Parent)
            if (directory.LinkTarget != null) return false;
        return true;
    }
}
