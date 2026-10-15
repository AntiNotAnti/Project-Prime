#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Linq;
using MphRead.Mods.MapGen;
using ProjectPrime.Studio.Protocol;

namespace MphRead.Mods.StudioIntegration;

/// <summary>Only private staging and the game's package library may supply broker packages.</summary>
public static class StudioPackagePathPolicy
{
    public static string Validate(string packagePath, params string[] allowedRoots)
    {
        if (!Path.IsPathFullyQualified(packagePath) || packagePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part is "." or "..") || !Path.GetExtension(packagePath).Equals(".ppmap", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The broker requires a canonical staged .ppmap path.");
        string path = Path.GetFullPath(packagePath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!allowedRoots.Any(root => path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))
            + Path.DirectorySeparatorChar, comparison)))
            throw new UnauthorizedAccessException("Copy the frozen package to Studio's private staging directory before sending it to the game.");
        // Reject aliases on the complete path, rather than checking only the leaf.
        // OS-owned ancestors such as macOS /var -> /private/var are resolved once by
        // the caller choosing a physical staging root; user aliases are never accepted.
        string current = Path.GetPathRoot(path)!;
        foreach (string part in path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Broker package paths cannot cross symbolic links or reparse points.");
        }
        if (!File.Exists(path)) throw new FileNotFoundException("The staged map package is missing.");
        return path;
    }

    internal static string ValidateDefault(string path) => Validate(path,
        Path.Combine(LocalIpcEndpointStore.DefaultUserDataDirectory, "staging"),
        Path.Combine(LocalIpcEndpointStore.DefaultUserDataDirectory, "playtest"),
        CustomRooms.UserMapDirectory, Path.Combine(AppContext.BaseDirectory, "studio-staging"));
}
#endif
