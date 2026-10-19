using System;
using System.IO;
using System.Security.Cryptography;

namespace MphRead.Mods.Launcher;

public sealed record NativeStudioFileStamp(string Path, string UserRoot, long Length, long ModifiedUtcTicks, string Sha256);

/// <summary>Confirmed deletion addresses one unchanged user JSON file, never a live document or asset folder.</summary>
public static class NativeStudioProjectFiles
{
    public static NativeStudioFileStamp Capture(string path, string userRoot)
    {
        string file = System.IO.Path.GetFullPath(path), root = System.IO.Path.GetFullPath(userRoot);
        if (!Safe(file, root)) throw new IOException("Only regular JSON projects inside the user map library can be deleted.");
        var info = new FileInfo(file);
        return new(file, root, info.Length, info.LastWriteTimeUtc.Ticks, Hash(file));
    }
    public static bool Matches(NativeStudioFileStamp stamp)
    {
        try
        {
            if (!Safe(stamp.Path, stamp.UserRoot)) return false;
            var info = new FileInfo(stamp.Path);
            return info.Length == stamp.Length && info.LastWriteTimeUtc.Ticks == stamp.ModifiedUtcTicks && Hash(stamp.Path) == stamp.Sha256;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
    public static bool Delete(NativeStudioFileStamp stamp, Func<string, bool> editorOwnsPath)
    {
        ArgumentNullException.ThrowIfNull(editorOwnsPath);
        if (editorOwnsPath(stamp.Path) || !Matches(stamp)) return false;
        File.Delete(stamp.Path); return true;
    }
    private static bool Safe(string file, string root)
    {
        if (!file.StartsWith(root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar,
            StringComparison.Ordinal) || !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(file) || File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) return false;
        for (var directory = new DirectoryInfo(System.IO.Path.GetDirectoryName(file)!); directory != null; directory = directory.Parent)
        {
            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint)
                || directory.Name is ".autosave" or ".studio" or ".installed") return false;
            if (directory.FullName.Equals(root, StringComparison.Ordinal)) return true;
        }
        return false;
    }
    private static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
