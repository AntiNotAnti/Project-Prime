using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods;

namespace MphRead
{
    internal readonly record struct TexturePackOption(string Id, string Label,
        bool Installed);

    /// <summary>
    /// Discovers only direct, manifest-bearing children of the launcher's
    /// enhancement directory. A selected missing pack remains visible so
    /// opening and saving Settings cannot silently replace the preference.
    /// </summary>
    internal static class TexturePackCatalog
    {
        internal const int MaximumDiscoveredPacks = 64;

        public static IReadOnlyList<TexturePackOption> Discover(
            string launcherDirectory, string? selectedId = null)
        {
            string selected = RenderOptions.NormalizeTexturePackId(selectedId);
            string root = Path.GetFullPath(Path.Combine(launcherDirectory,
                "enhancements"));
            var options = new List<TexturePackOption>
            {
                new(RenderOptions.OriginalTexturePackId, "Original", true)
            };

            bool defaultInstalled = HasManifest(root,
                RenderOptions.DefaultTexturePackId);
            options.Add(new TexturePackOption(RenderOptions.DefaultTexturePackId,
                defaultInstalled ? "Enhanced (default)" : "Enhanced (default, not installed)",
                defaultInstalled));

            if (Directory.Exists(root))
            {
                foreach (string directory in Directory.EnumerateDirectories(root)
                    .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .Take(MaximumDiscoveredPacks))
                {
                    string id = Path.GetFileName(directory);
                    if (RenderOptions.NormalizeTexturePackId(id) != id
                        || String.Equals(id, RenderOptions.OriginalTexturePackId,
                            StringComparison.OrdinalIgnoreCase)
                        || String.Equals(id, RenderOptions.DefaultTexturePackId,
                            StringComparison.OrdinalIgnoreCase)
                        || !IsUsablePackDirectory(directory))
                    {
                        continue;
                    }
                    options.Add(new TexturePackOption(id, DisplayName(id), true));
                }
            }

            if (!String.Equals(selected, RenderOptions.OriginalTexturePackId,
                    StringComparison.OrdinalIgnoreCase)
                && options.All(option => !String.Equals(option.Id, selected,
                    StringComparison.Ordinal)))
            {
                options.Add(new TexturePackOption(selected,
                    $"{DisplayName(selected)} (not installed)", false));
            }
            return options;
        }

        public static string PackRoot(string launcherDirectory, string packId)
        {
            string normalized = RenderOptions.NormalizeTexturePackId(packId);
            if (normalized == RenderOptions.OriginalTexturePackId)
            {
                throw new ArgumentException("The original textures do not have a pack directory.",
                    nameof(packId));
            }
            string root = Path.GetFullPath(Path.Combine(launcherDirectory,
                "enhancements"));
            string candidate = Path.GetFullPath(Path.Combine(root, normalized));
            string prefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root : root + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new ArgumentException("Texture pack path leaves the enhancement directory.",
                    nameof(packId));
            }
            return candidate;
        }

        private static bool HasManifest(string root, string id)
            => IsUsablePackDirectory(Path.Combine(root, id));

        public static bool IsUsablePackDirectory(string directory)
        {
            var info = new DirectoryInfo(directory);
            var manifest = new FileInfo(Path.Combine(info.FullName, "materials.json"));
            return info.Exists
                && (info.Attributes & FileAttributes.ReparsePoint) == 0
                && manifest.Exists
                && (manifest.Attributes & FileAttributes.ReparsePoint) == 0;
        }

        private static string DisplayName(string id)
        {
            string value = id.Replace('_', ' ').Replace('-', ' ').Trim();
            return value.Length == 0 ? id : value;
        }
    }
}
