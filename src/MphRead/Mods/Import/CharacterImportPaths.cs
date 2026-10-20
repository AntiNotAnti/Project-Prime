using System;
using System.IO;
using MphRead.Mods.Platform;

namespace MphRead.Mods.Import;

/// <summary>Writable character inventory root, configured once by the Android head.</summary>
public static class CharacterImportPaths
{
    private static readonly object Gate = new();
    private static string? _androidRoot;

    public static void ConfigureAndroidRoot(string absoluteCanonicalRoot)
    {
        ArgumentNullException.ThrowIfNull(absoluteCanonicalRoot);
        if (string.IsNullOrWhiteSpace(absoluteCanonicalRoot) || !Path.IsPathFullyQualified(absoluteCanonicalRoot))
            throw new InvalidDataException("Android character import root must be an absolute existing directory.");
        string root = Path.TrimEndingDirectorySeparator(absoluteCanonicalRoot);
        if (!string.Equals(root, Path.GetFullPath(root), StringComparison.Ordinal))
            throw new InvalidDataException("Android character import root must be canonical.");
        LocalImportBundle.CheckPath(root, root);
        if (!Directory.Exists(root))
            throw new InvalidDataException("Android character import root must be an existing directory.");
        lock (Gate)
        {
            if (_androidRoot != null && !string.Equals(_androidRoot, root, StringComparison.Ordinal))
                throw new InvalidOperationException("Android character import root is already configured.");
            _androidRoot = root;
        }
    }

    internal static string Resolve(bool android)
    {
        if (!android) return Path.Combine(AppPaths.UserDataDirectory, "source-character-previews");
        lock (Gate)
            return Path.Combine(_androidRoot ?? throw new InvalidOperationException(
                "Android character import root has not been configured."), "source-character-previews");
    }
}
