using System;
using System.IO;

namespace ProjectPrime.DesktopShared;

// Compiled as an internal BCL helper by both the game and the independent IPC
// library. They share these exact installation rules without an assembly dependency.
internal static class DesktopInstallationIdentity
{
    // Filesystem containment needs physical path casing. Installation identities
    // deliberately fold casing and collapse paired app bundles instead.
    internal static string ResolveDirectoryAliases(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return Path.TrimEndingDirectorySeparator(Resolve(Path.GetFullPath(directory), 0));
    }

    internal static string CanonicalInstallation(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string full = Resolve(Path.GetFullPath(directory), 0);
        var info = new DirectoryInfo(full);
        if (info.Name == "MacOS" && info.Parent?.Name == "Contents"
            && info.Parent.Parent is { } bundle && bundle.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            full = bundle.Parent?.FullName ?? bundle.FullName;
        full = Path.TrimEndingDirectorySeparator(full);
        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? full.ToUpperInvariant() : full;
    }

    private static string Resolve(string directory, int depth)
    {
        if (depth > 32) throw new IOException("The installation contains too many directory aliases.");
        string root = Path.GetPathRoot(directory)!;
        string current = root;
        foreach (string component in directory[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            var info = new DirectoryInfo(current);
            try
            {
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    current = Resolve(info.ResolveLinkTarget(true)?.FullName
                        ?? throw new IOException("The installation directory alias cannot be resolved."), depth + 1);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return current;
    }
}
