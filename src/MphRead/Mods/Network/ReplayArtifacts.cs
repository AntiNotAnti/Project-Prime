using System;
using System.IO;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Network;

/// <summary>Owns the durable files associated with a replay identity.</summary>
internal static class ReplayArtifacts
{
    private static readonly string[] SidecarSuffixes =
    {
        ".favorite", ".name", ".camera", ".studio.json", ".reel.json",
        ".analytics.json", ".export.json", ".thumb0.png", ".thumb1.png", ".thumb2.png"
    };

    internal static void DeleteAll(string logicalPath, bool deleteReplay = true, bool deleteVirtualCache = true)
    {
        string path = Path.GetFullPath(logicalPath);
        if (deleteReplay) File.Delete(path);
        foreach (string suffix in SidecarSuffixes) TryDelete(path + suffix);
        DeleteTemporaries(path + ".camera.*.tmp");
        DeleteTemporaries(path + ".studio.json.*.tmp");
        DeleteTemporaries(path + ".reel.json.*.tmp");
        ReplayLibraryIndex.Remove(path);
        if (deleteVirtualCache && path.EndsWith(ReplayVirtualClips.Extension, StringComparison.OrdinalIgnoreCase))
            DeleteCache(ReplayVirtualClips.CachePath(path));
    }

    internal static void DeleteCache(string cachePath)
    {
        string path = Path.GetFullPath(cachePath);
        string logical = ReplayVirtualClips.LogicalPath(path);
        if (!ReplayPathComparer.Same(path, logical) && File.Exists(logical)
            && File.Exists(path + ".camera") && !File.Exists(logical + ".camera"))
        {
            var legacyTrack = new ReplayCameraTrack();
            if (!legacyTrack.Load(path) || !legacyTrack.Save(logical))
                throw new IOException("Cannot remove a cache containing unmigrated camera edits: " + legacyTrack.LastError);
        }
        File.Delete(path);
        foreach (string suffix in SidecarSuffixes) TryDelete(path + suffix);
        DeleteTemporaries(path + ".*");
    }

    internal static string[] Enumerate(string logicalPath)
    {
        string path = Path.GetFullPath(logicalPath);
        var results = new System.Collections.Generic.List<string>();
        if (File.Exists(path)) results.Add(path);
        foreach (string suffix in SidecarSuffixes)
            if (File.Exists(path + suffix)) results.Add(path + suffix);
        if (path.EndsWith(ReplayVirtualClips.Extension, StringComparison.OrdinalIgnoreCase))
        {
            string cache = ReplayVirtualClips.CachePath(path);
            if (File.Exists(cache)) results.Add(cache);
            foreach (string suffix in SidecarSuffixes)
                if (File.Exists(cache + suffix)) results.Add(cache + suffix);
        }
        return results.ToArray();
    }

    internal static void Move(string logicalSource, string logicalDestination)
    {
        string source = Path.GetFullPath(logicalSource);
        string destination = Path.GetFullPath(logicalDestination);
        if (ReplayPathComparer.Same(source, destination)) return;
        ReplayCameraTrack? cameraTrack = null;
        if (File.Exists(source + ".camera"))
        {
            var candidate = new ReplayCameraTrack();
            if (candidate.Load(source)) cameraTrack = candidate;
        }
        string? directory = Path.GetDirectoryName(destination);
        if (directory != null) Directory.CreateDirectory(directory);
        var moves = new System.Collections.Generic.List<(string Source, string Destination)>();
        if (File.Exists(source)) moves.Add((source, destination));
        foreach (string suffix in SidecarSuffixes)
            if (File.Exists(source + suffix)) moves.Add((source + suffix, destination + suffix));
        if (source.EndsWith(ReplayVirtualClips.Extension, StringComparison.OrdinalIgnoreCase))
        {
            string oldCache = ReplayVirtualClips.CachePath(source);
            string newCache = ReplayVirtualClips.CachePath(destination);
            if (File.Exists(oldCache)) moves.Add((oldCache, newCache));
            foreach (string suffix in SidecarSuffixes)
                if (File.Exists(oldCache + suffix)) moves.Add((oldCache + suffix, newCache + suffix));
        }
        foreach (var move in moves)
            if (File.Exists(move.Destination) || Directory.Exists(move.Destination))
                throw new IOException("Replay destination already exists: " + move.Destination);
        var completed = new System.Collections.Generic.List<(string Source, string Destination)>();
        try
        {
            foreach (var move in moves)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(move.Destination)!);
                File.Move(move.Source, move.Destination, overwrite: false);
                completed.Add(move);
            }
            if (cameraTrack != null && !cameraTrack.Save(destination))
                throw new IOException(cameraTrack.LastError ?? "Could not rebind the camera track.");
        }
        catch (Exception failure)
        {
            var failures = new System.Collections.Generic.List<Exception> { failure };
            for (int i = completed.Count - 1; i >= 0; i--)
            {
                try { File.Move(completed[i].Destination, completed[i].Source, overwrite: false); }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
                { failures.Add(rollback); }
            }
            if (failures.Count > 1) throw new AggregateException("Replay move could not be fully rolled back.", failures);
            throw;
        }
        ReplayLibraryIndex.Remove(source);
    }

    private static void DeleteTemporaries(string pattern)
    {
        string directory = Path.GetDirectoryName(pattern) ?? ".";
        string filePattern = Path.GetFileName(pattern);
        try
        {
            if (!Directory.Exists(directory)) return;
            foreach (string path in Directory.EnumerateFiles(directory, filePattern)) TryDelete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
