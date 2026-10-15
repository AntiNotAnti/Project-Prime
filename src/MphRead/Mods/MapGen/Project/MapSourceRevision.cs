using System;
using System.IO;
using System.Security.Cryptography;

namespace MphRead.Mods.MapGen;

/// <summary>Editor-only optimistic revision; never serialized into a map recipe.</summary>
internal sealed record MapSourceRevision(string PhysicalPath, string? Hash)
{
    // macOS commonly preserves casing while resolving both spellings to one
    // file. Treat case-only paths conservatively, including on sensitive volumes.
    private static StringComparison Comparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static bool SamePath(string left, string right) => string.Equals(left, right, Comparison);

    internal static string ResolvePath(string path, int depth = 0)
    {
        if (depth > 32) throw new IOException("The map source contains too many symbolic links.");
        path = Path.GetFullPath(path);
        string root = Path.GetPathRoot(path)!;
        string current = root;
        foreach (string part in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget != null)
                current = ResolvePath(info.ResolveLinkTarget(true)?.FullName
                    ?? throw new IOException("The map source alias cannot be resolved."), depth + 1);
        }
        return current;
    }

    internal static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static string? HashFile(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    internal static MapSourceRevision Capture(string path)
    {
        string physical = ResolvePath(path);
        return new(physical, HashFile(physical));
    }
    internal void RequireUnchanged(string path)
    {
        string physical = ResolvePath(path);
        if (!SamePath(physical, PhysicalPath) || HashFile(physical) != Hash)
            throw new IOException("This map source changed outside this document. Your edits were kept in the editor; "
                + "reopen the current source or use Save As with a different filename before saving.");
    }
    internal static FileStream AcquireSave(string physical)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        string path = physical + ".prime-save-lock";
        if (new FileInfo(path).LinkTarget != null)
            throw new IOException("The map save lock cannot be a symbolic link.");
        // A short-lived commit lease serializes cooperating Game/Studio saves.
        // Never unlink the lock inode while another process may have it open.
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("Another document is saving this map source. Retry after it finishes.", ex); }
    }

    internal static void Commit(string physical,byte[] bytes,Action requireUnchanged)
    {
        string temporary=physical+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {output.Write(bytes);output.Flush(flushToDisk:true);}
            // Check after staging large recipes, directly before the atomic
            // replacement, so an external save during staging is preserved.
            requireUnchanged();
            File.Move(temporary,physical,overwrite:true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
