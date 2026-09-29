using System;
using System.IO;

namespace MphRead.Mods.Network;

/// <summary>Path comparison rules for replay files. Linux remains case-sensitive;
/// Only Windows folds case. macOS can have case-sensitive volumes, so preserve
/// spelling there to avoid merging distinct recordings or sidecars.</summary>
internal static class ReplayPathComparer
{
    internal static StringComparer Comparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static string Normalize(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) ?? string.Empty;
        return full.Length == root.Length ? full
            : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    internal static bool Same(string left, string right)
        => Comparer.Equals(Normalize(left), Normalize(right));
}
