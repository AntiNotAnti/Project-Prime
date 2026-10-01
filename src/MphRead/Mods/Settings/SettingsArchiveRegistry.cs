using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.Render.Hud;

namespace MphRead.Mods.Settings;

internal sealed record SettingsArchiveStore(string Path, string Category, int MaximumBytes, Action<string> Validate);

/// <summary>The only paths an archive may read or install. Never enumerate the data root recursively.</summary>
internal static class SettingsArchiveRegistry
{
    private static readonly SettingsArchiveStore[] Stores =
    {
        new("Savedata/settings.json", "General", 2 * 1024 * 1024, SceneGameState.ValidateSettingsArchive),
        new("launcher.txt", "Launcher", 2 * 1024 * 1024, text => SettingsArchiveValidator.ValidatePreferences(text, launcher: true)),
        new("controls.txt", "Controls", 2 * 1024 * 1024, text => SettingsArchiveValidator.ValidatePreferences(text, launcher: false)),
        new("controller-profiles.json", "Controls", 1024 * 1024, text => Input.GamepadProfiles.ParseLibrary(text)),
        new("gamecontrollerdb.txt", "Controls", 2 * 1024 * 1024, SettingsArchiveValidator.ValidateMappings)
    };

    internal static SettingsArchiveStore Resolve(string path)
    {
        ValidatePath(path);
        var known = Stores.FirstOrDefault(s => s.Path == path);
        if (known != null) return known;
        const string prefix = "Savedata/hud-profiles/";
        if (path.StartsWith(prefix, StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal))
        {
            string name = path[prefix.Length..^5];
            // Use exactly the profile store's portable filename policy.
            HudProfileStore.ValidateName(name);
            return new(path, "HUD", HudProfileStore.MaximumBytes, text => HudProfileStore.Parse(text));
        }
        throw new InvalidDataException("Unknown settings store: " + path);
    }

    internal static IEnumerable<SettingsArchiveStore> Enumerate(string root)
    {
        foreach (var store in Stores)
            if (File.Exists(Destination(root, store.Path))) yield return store;
        string profiles = Destination(root, "Savedata/hud-profiles");
        if (!Directory.Exists(profiles)) yield break;
        foreach (string file in Directory.EnumerateFiles(profiles, "*.json").OrderBy(p => p, StringComparer.Ordinal))
            yield return Resolve("Savedata/hud-profiles/" + System.IO.Path.GetFileName(file));
    }

    internal static void ValidatePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 200 || path.Contains('\\') || path.Contains(':')
            || path.StartsWith('/') || path.Split('/').Any(p => p is "" or "." or "..")
            || path.Any(char.IsControl)) throw new InvalidDataException("Invalid settings path.");
    }

    internal static string Destination(string root, string path)
    {
        ValidatePath(path);
        root = System.IO.Path.GetFullPath(root);
        // The application chooses the trusted root; platform ancestry (e.g. Android
        // /data/user/0 or macOS /var) may legitimately contain a system alias.
        // Archive-controlled descendants must never redirect installation.
        RejectLink(root);
        string current = root;
        foreach (string part in path.Split('/'))
        {
            current = System.IO.Path.Combine(current, part);
            RejectLink(current);
        }
        return current;
    }
    private static void RejectLink(string path)
    {
        if (new FileInfo(path).LinkTarget != null || new DirectoryInfo(path).LinkTarget != null
            || File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Settings archives cannot follow symbolic links: " + path);
    }
}
