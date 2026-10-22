#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Launcher.RmlUi.Render;

/// <summary>
/// Native Theatre projection over the existing virtual descriptor owner. It
/// owns no descriptor serializer, replay reader, playback clock or source file.
/// Listing/materialization run on ReplayStorageJobs; rendering reads only rows.
/// </summary>
internal static class RmlUiVirtualReplayLibrary
{
    private const int MaxDescriptorBytes = 64 * 1024;
    private static readonly object CacheWitnessGate = new();
    private static readonly Dictionary<string, Prepared> CacheWitnesses = new(ReplayPathComparer.Comparer);
    internal readonly record struct Prepared(RmlUiReplayEntry Entry, string PlaybackPath,
        long Bytes, long LastWriteTicks);

    internal static bool IsVirtual(string path) => path.EndsWith(ReplayVirtualClips.Extension,
        StringComparison.OrdinalIgnoreCase);

    internal static RmlUiReplayEntry[] List(string library, IReadOnlyList<DemoRecording> sources,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!SafeLibrary(library)) return Array.Empty<RmlUiReplayEntry>();
        var byPath = sources.ToDictionary(x => Path.GetFullPath(x.Path), ReplayPathComparer.Comparer);
        var rows = new List<RmlUiReplayEntry>();
        foreach (string path in ReplayVirtualClips.List())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!TryDescriptor(path, library, out var info, out var document, out string sha)) continue;
                string source = Path.GetFullPath(document.SourceReplay);
                bool sourceOwned = PhysicalSource(source, library);
                var sourceInfo = new FileInfo(source);
                bool available = sourceOwned && sourceInfo.Exists && sourceInfo.LinkTarget == null;
                byPath.TryGetValue(source, out DemoRecording recording);
                bool compatible = available && recording.Path != null
                    && recording.Compatibility == ReplayOpenResult.Success
                    && document.EndFrame <= recording.DurationFrames;
                uint duration = document.EndFrame - document.StartFrame;
                string detail = $"virtual clip // {ReplayHud.Time(duration)}";
                if (compatible && recording.Metadata is { } sourceMetadata) detail += $" // Movement: {sourceMetadata.MovementLabel}";
                if (!available) detail += " // SOURCE UNAVAILABLE";
                else if (!compatible) detail += " // SOURCE REQUIRES VALIDATION";
                bool annotationsSafe = SafeSidecar(path + ReplayAnnotations.Extension);
                var entry = new RmlUiReplayEntry(path, document.Name, detail,
                    recording.Path != null ? recording.Room : "", document.CreatedUtc.ToLocalTime().Ticks,
                    info.Length, info.LastWriteTimeUtc.Ticks,
                    SafeSidecar(path + ".favorite") && ReplayVirtualClips.IsFavorite(path),
                    Clip: true, Recoverable: false, Playable: compatible)
                {
                    VirtualWitness = new(sha, sourceOwned ? CanonicalSourcePath(source, library) : source,
                        available ? sourceInfo.Length : -1,
                        available ? sourceInfo.LastWriteTimeUtc.Ticks : 0)
                };
                rows.Add(ProjectMetadata(entry, available && recording.Path != null ? recording.Metadata : null,
                    duration, annotationsSafe));
            }
            catch (Exception error) when (FileFailure(error)) { }
        }
        return rows.ToArray();
    }

    // One bounded sidecar read per worker row keeps tags, collections and named
    // moments from the same snapshot. Missing/invalid metadata stays unknown.
    internal static RmlUiReplayEntry ProjectMetadata(RmlUiReplayEntry entry, ReplayMetadata? metadata,
        uint duration, bool annotationsSafe = true)
    {
        var annotations = annotationsSafe ? ReplayAnnotations.ReadLibrarySnapshot(entry.Path) : null;
        return entry with
        {
            DurationFrames = duration > 0 ? duration : null,
            PlayerNamesText = metadata == null ? null : String.Join(" ", metadata.Players.Select(x => x.Name)),
            BookmarkCount = annotations is { Available: true } ? annotations.BookmarkCount : null,
            HighlightCount = annotations is { Available: true } ? annotations.HighlightCount : null,
            Tags = annotations is { Available: true } ? String.Join(", ", annotations.Tags) : "",
            Collections = annotations is { Available: true } ? String.Join(", ", annotations.Collections) : "",
            AnnotationNames = annotations is { Available: true } ? annotations.Names : ""
        };
    }

    internal static bool Exact(RmlUiReplayEntry entry, string library, bool requireSource)
    {
        try
        {
            if (entry.VirtualWitness is not { } expected
                || !TryDescriptor(entry.Path, library, out var info, out var document, out string sha)
                || info.Length != entry.Bytes || info.LastWriteTimeUtc.Ticks != entry.LastWriteTicks
                || sha != expected.DescriptorSha256) return false;
            string source = Path.GetFullPath(document.SourceReplay);
            if (!PhysicalSource(source, library)
                || !ReplayPathComparer.Same(CanonicalSourcePath(source, library), expected.SourcePath)) return false;
            var sourceInfo = new FileInfo(source);
            if (sourceInfo.LinkTarget != null) return false;
            if (!sourceInfo.Exists) return !requireSource && expected.SourceBytes == -1;
            return sourceInfo.Length == expected.SourceBytes
                && sourceInfo.LastWriteTimeUtc.Ticks == expected.SourceLastWriteTicks;
        }
        catch (Exception error) when (FileFailure(error)) { return false; }
    }

    internal static bool SafeArtifacts(RmlUiReplayEntry entry, string library)
    {
        try
        {
            if (!Exact(entry, library, requireSource: false) || !SafeCache(entry.Path, library)
                || !SafeSidecar(entry.Path + ".favorite") || !SafeSidecar(entry.Path + ".name")
                || !SafeSidecar(entry.Path + ReplayAnnotations.Extension)) return false;
            return ReplayArtifacts.Enumerate(entry.Path).All(SafeSidecar);
        }
        catch (Exception error) when (FileFailure(error)) { return false; }
    }

    internal static Prepared? Resolve(RmlUiReplayEntry entry, string library,
        CancellationToken token, out ReplayOpenResult result)
    {
        result = ReplayOpenResult.Corrupt;
        token.ThrowIfCancellationRequested();
        lock (CacheWitnessGate)
        {
            token.ThrowIfCancellationRequested();
            if (!entry.Playable || !Exact(entry, library, requireSource: true) || !SafeCache(entry.Path, library)) return null;
            string cache = ReplayVirtualClips.CachePath(entry.Path);
            // The shared owner's historical cache test uses timestamps only.
            // A native row additionally binds descriptor bytes/source facts;
            // rebuild an unproven cache through that same owner rather than
            // accepting an old range whose timestamp happened to be preserved.
            if (File.Exists(cache) && (!CacheWitnesses.TryGetValue(entry.Path, out var known)
                || known.Entry.VirtualWitness != entry.VirtualWitness
                || known.Entry.Bytes != entry.Bytes || known.Entry.LastWriteTicks != entry.LastWriteTicks
                || !CacheUnchanged(known))) ReplayArtifacts.DeleteCache(cache);
            string? path = ReplayVirtualClips.ResolveForPlayback(entry.Path, out result);
            token.ThrowIfCancellationRequested();
            if (path == null || result != ReplayOpenResult.Success
                || !Exact(entry, library, requireSource: true) || !SafeCache(entry.Path, library)
                || !ReplayPathComparer.Same(path, cache)
                || !ReplayPathComparer.Same(ReplayVirtualClips.LogicalPath(path), entry.Path)) return null;
            var info = new FileInfo(path);
            if (!info.Exists || info.LinkTarget != null) return null;
            var prepared = new Prepared(entry, path, info.Length, info.LastWriteTimeUtc.Ticks);
            if (CacheWitnesses.Count >= 64) CacheWitnesses.Clear();
            CacheWitnesses[entry.Path] = prepared;
            return prepared;
        }
    }

    private static bool CacheUnchanged(Prepared prepared)
    {
        var info = new FileInfo(prepared.PlaybackPath);
        return info.Exists && info.LinkTarget == null && info.Length == prepared.Bytes
            && info.LastWriteTimeUtc.Ticks == prepared.LastWriteTicks;
    }

    // Invoked only for an explicit launch action. No hashing/polling occurs in
    // native Tick/Draw, and the source recording is never hashed on the UI owner.
    internal static bool PreparedUnchanged(Prepared prepared, RmlUiReplayEntry entry, string library)
    {
        try
        {
            var info = new FileInfo(prepared.PlaybackPath);
            return prepared.Entry == entry && Exact(entry, library, requireSource: true)
                && SafeCache(entry.Path, library) && info.Exists && info.LinkTarget == null
                && info.Length == prepared.Bytes && info.LastWriteTimeUtc.Ticks == prepared.LastWriteTicks
                && ReplayPathComparer.Same(prepared.PlaybackPath, ReplayVirtualClips.CachePath(entry.Path))
                && ReplayPathComparer.Same(ReplayVirtualClips.LogicalPath(prepared.PlaybackPath), entry.Path);
        }
        catch (Exception error) when (FileFailure(error)) { return false; }
    }

    private static bool TryDescriptor(string path, string library, out FileInfo info,
        out ReplayVirtualClipDocument document, out string sha)
    {
        info = new FileInfo(path); document = null!; sha = "";
        if (!SafeLibrary(library) || !TopLevel(path, library) || !IsVirtual(path)
            || !info.Exists || info.LinkTarget != null || info.Length is <= 0 or > MaxDescriptorBytes) return false;
        long length = info.Length, stamp = info.LastWriteTimeUtc.Ticks;
        if (!ReplayVirtualClips.TryLoad(path, out var loaded) || loaded == null
            || String.IsNullOrWhiteSpace(loaded.Name) || loaded.Name.Length > 100
            || loaded.SourceReplay.Length > 4096 || !Path.IsPathFullyQualified(loaded.SourceReplay)) return false;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            sha = Convert.ToHexString(SHA256.HashData(stream));
        info.Refresh();
        if (!info.Exists || info.LinkTarget != null || info.Length != length || info.LastWriteTimeUtc.Ticks != stamp) return false;
        document = loaded;
        return true;
    }

    private static bool PhysicalSource(string path, string library) => TopLevel(path, library)
        && (path.EndsWith(DemoFile.Extension, StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(DemoFile.LegacyExtension, StringComparison.OrdinalIgnoreCase));

    private static bool SafeLibrary(string library)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(library));
        return directory.Exists && directory.LinkTarget == null;
    }

    private static bool TopLevel(string path, string library) => Path.IsPathFullyQualified(path)
        && ReplayPathComparer.Same(Path.GetDirectoryName(Path.GetFullPath(path)) ?? "", library);

    private static string CanonicalSourcePath(string source, string library)
    {
        // Resolve existing ancestors once at a snapshot/action boundary. This
        // accepts legitimate platform aliases (e.g. macOS temporary roots) but
        // pins their resolved directory: a later ancestor redirect cannot borrow
        // matching length/mtime facts from a different physical library.
        return Path.Combine(CanonicalDirectory(library, 40), Path.GetFileName(source));
    }

    private static string CanonicalDirectory(string path, int remainingLinks)
    {
        if (remainingLinks <= 0) throw new IOException("Replay library ancestry contains too many links.");
        var names = new Stack<string>();
        var part = new DirectoryInfo(Path.GetFullPath(path));
        while (part.Parent is { } parent) { names.Push(part.Name); part = parent; }
        string canonical = part.FullName;
        while (names.TryPop(out string? name))
        {
            part = new DirectoryInfo(Path.Combine(canonical, name));
            if (!part.Exists) throw new DirectoryNotFoundException("Replay library ancestry is unavailable.");
            // Resolve the target's parents too. ResolveLinkTarget(true) alone
            // can leave an alias within its returned target's ancestry.
            canonical = part.ResolveLinkTarget(returnFinalTarget: false) is { } target
                ? CanonicalDirectory(target.FullName, remainingLinks - 1) : part.FullName;
        }
        return canonical;
    }

    private static bool SafeCache(string descriptor, string library)
    {
        if (!SafeLibrary(library) || !TopLevel(descriptor, library)) return false;
        string cache = ReplayVirtualClips.CachePath(descriptor);
        var directory = new DirectoryInfo(Path.GetDirectoryName(cache)!);
        return directory.LinkTarget == null && SafeSidecar(cache) && SafeSidecar(cache + ".part")
            && (!directory.Exists || ReplayArtifacts.Enumerate(descriptor).All(SafeSidecar));
    }

    internal static bool SafeSidecar(string path) => new FileInfo(path).LinkTarget == null;
    private static bool FileFailure(Exception error) => error is IOException or UnauthorizedAccessException
        or ArgumentException or NotSupportedException;
}
#endif
