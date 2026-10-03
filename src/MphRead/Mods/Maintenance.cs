using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using MphRead.Mods.Update;

namespace MphRead.Mods
{
    public readonly record struct MaintenanceReport(
        long BytesFreed,
        int FilesDeleted,
        int DirectoriesDeleted,
        string Summary);

    /// <summary>
    /// Safe housekeeping for data that Project Prime can reproduce.
    /// Saves, settings, controls, extracted cartridge data, real replays and
    /// user maps are intentionally outside every delete path in this class.
    /// </summary>
    public static class Maintenance
    {
        private const long MaxMapCacheBytes = 2L * 1024 * 1024 * 1024;
        private static readonly TimeSpan MaxMapCacheAge = TimeSpan.FromDays(60);
        private static readonly TimeSpan TempReplayAge = TimeSpan.FromDays(2);
        private static readonly TimeSpan IncompleteExportAge = TimeSpan.FromDays(7);
        private const string MarkerName = ".maintenance-version";

        public static MaintenanceReport RunStartup()
        {
            try
            {
                // Update staging cannot remove itself, so every ordinary startup
                // gets one cheap delete attempt. The versioned cache sweep below
                // must not call the same cleanup a second time.
                DesktopUpdate.Clean();
                Directory.CreateDirectory(LauncherPrefs.Directory);
                string marker = Path.Combine(LauncherPrefs.Directory, MarkerName);
                string current = BuildVersion.Display;
                string previous = File.Exists(marker) ? File.ReadAllText(marker).Trim() : "";
                if (String.Equals(previous, current, StringComparison.OrdinalIgnoreCase))
                {
                    return default;
                }

                MaintenanceReport report = CleanReproducibleDataCore(cleanUpdate: false);
                File.WriteAllText(marker, current);
                DebugLog.Line("maintenance",
                    $"upgrade/startup sweep {(previous.Length == 0 ? "(first)" : previous)} -> {current}: {report.Summary}");
                return report;
            }
            catch (Exception ex)
            {
                DebugLog.Line("maintenance", "startup sweep skipped: " + ex.Message);
                return default;
            }
        }

        public static MaintenanceReport CleanReproducibleData()
            => CleanReproducibleDataCore(cleanUpdate: true);

        private static MaintenanceReport CleanReproducibleDataCore(bool cleanUpdate)
        {
            int files = 0, directories = 0;
            long freed = 0;

            if (cleanUpdate) DesktopUpdate.Clean();
            PruneMapCache(ref files, ref directories, ref freed);
            CleanReplayTemps(ref files, ref directories, ref freed);
            CleanThumbnails(ref files, ref freed);
            CleanThumbnailLog(ref files, ref freed);

            string summary = $"{FormatBytes(freed)} freed; {files} file(s), {directories} folder(s) removed";
            return new MaintenanceReport(freed, files, directories, summary);
        }

        public static MaintenanceReport ClearMapBuildCache()
        {
            string root = MapCacheRoot();
            if (!Directory.Exists(root))
                return new MaintenanceReport(0, 0, 0, "Map build cache is already empty.");
            long before = DirectoryBytes(root);
            int files = 0;
            bool deleted = TryDeleteDirectory(root, ref files);
            return deleted
                ? new MaintenanceReport(before, files, 1, $"{FormatBytes(before)} map cache cleared.")
                : new MaintenanceReport(0, 0, 0, "Map build cache could not be fully cleared.");
        }

        public static MaintenanceReport ClearThumbnails()
        {
            string root = SafeThumbnailRoot();
            if (root.Length == 0 || !Directory.Exists(root))
                return new MaintenanceReport(0, 0, 0, "Thumbnail cache is already empty.");
            long before = DirectoryBytes(root);
            int files = 0;
            bool deleted = TryDeleteDirectory(root, ref files);
            return deleted
                ? new MaintenanceReport(before, files, 1, $"{FormatBytes(before)} thumbnail cache cleared.")
                : new MaintenanceReport(0, 0, 0, "Thumbnail cache could not be fully cleared.");
        }

        public static string StorageSummary()
        {
            long map = DirectoryBytes(MapCacheRoot());
            long thumbs = DirectoryBytes(SafeThumbnailRoot());
            long replayCache = DirectoryBytes(SafeReplayCacheRoot());
            long logs = DirectoryBytes(Path.Combine(LauncherPrefs.Directory, "logs"));
            return $"Map cache {FormatBytes(map)}  |  Replay cache {FormatBytes(replayCache)}  |  "
                + $"Thumbnails {FormatBytes(thumbs)}  |  Logs {FormatBytes(logs)}";
        }

        public static string PerformanceSummary(MenuSettings settings)
        {
            int scale = RenderOptions.ParseScale(settings.ResolutionScale, 100);
            int cap = Render.FrameTiming.ParseCap(settings.FrameRateCap, Render.FrameTiming.DisplayRate);
            int width = LauncherPrefs.WindowWidth;
            int height = LauncherPrefs.WindowHeight;
            string target = width > 0 && height > 0
                ? $"{Math.Max(1, width * scale / 100)}x{Math.Max(1, height * scale / 100)}"
                : $"{scale}% of the current framebuffer";
            return $"render scale={scale}% (target {target}), "
                + $"fps limit={(cap == Render.FrameTiming.DisplayRate ? "display/vsync" : cap.ToString(CultureInfo.InvariantCulture))}, "
                + $"sampling={settings.TextureSampling}, filtering={settings.TextureFiltering}, "
                + $"mipmaps={settings.TextureMipmaps}, anisotropy={settings.TextureAnisotropy}x, "
                + $"cel={settings.CelShading}, "
                + $"debug log={(LauncherPrefs.DebugLogs ? "disk" : "memory/crash only")}";
        }

        private static void PruneMapCache(ref int files, ref int directories, ref long freed)
        {
            string root = MapCacheRoot();
            if (!Directory.Exists(root)) return;
            var entries = new DirectoryInfo(root).EnumerateDirectories()
                .Select(d => new CacheDirectory(d, DirectoryBytes(d.FullName)))
                .OrderBy(x => x.Info.LastWriteTimeUtc)
                .ToList();
            long total = entries.Sum(x => x.Bytes);
            DateTime cutoff = DateTime.UtcNow - MaxMapCacheAge;
            foreach (CacheDirectory entry in entries)
            {
                if (entry.Info.LastWriteTimeUtc >= cutoff && total <= MaxMapCacheBytes) continue;
                long bytes = entry.Bytes;
                if (TryDeleteDirectory(entry.Info.FullName, ref files))
                {
                    total = Math.Max(0, total - bytes);
                    freed += bytes;
                    directories++;
                }
            }
        }

        private static void CleanReplayTemps(ref int files, ref int directories, ref long freed)
        {
            string root;
            try { root = DemoLibrary.Directory; }
            catch { return; }
            if (!Directory.Exists(root)) return;

            DateTime partCutoff = DateTime.UtcNow - TempReplayAge;
            foreach (string part in Directory.EnumerateFiles(root, "*.part", SearchOption.TopDirectoryOnly))
            {
                TryDeleteOldFile(part, partCutoff, ref files, ref freed);
            }

            string cache = Path.Combine(root, ".virtual-cache");
            if (Directory.Exists(cache))
            {
                foreach (string materialized in Directory.EnumerateFiles(cache, "*",
                    SearchOption.TopDirectoryOnly))
                {
                    string descriptor = Path.Combine(root,
                        Path.GetFileNameWithoutExtension(materialized) + ReplayVirtualClips.Extension);
                    if (!File.Exists(descriptor))
                    {
                        TryDeleteOldFile(materialized, partCutoff, ref files, ref freed);
                    }
                }
            }

            string exports = Path.Combine(root, "exports");
            if (Directory.Exists(exports))
            {
                DateTime exportCutoff = DateTime.UtcNow - IncompleteExportAge;
                foreach (string directory in Directory.EnumerateDirectories(exports, "render_*",
                    SearchOption.TopDirectoryOnly))
                {
                    string movie = Path.Combine(directory, "replay.mp4");
                    var info = new DirectoryInfo(directory);
                    if (!File.Exists(movie) && info.LastWriteTimeUtc < exportCutoff)
                    {
                        long bytes = DirectoryBytes(directory);
                        if (TryDeleteDirectory(directory, ref files))
                        {
                            freed += bytes;
                            directories++;
                        }
                    }
                }
            }
        }

        private static void CleanThumbnails(ref int files, ref long freed)
        {
            string root = SafeThumbnailRoot();
            if (root.Length == 0 || !Directory.Exists(root)) return;
            HashSet<string> expected;
            try
            {
                expected = ThumbnailGenerator.MultiplayerRooms()
                    .Select(ThumbnailGenerator.PathFor)
                    .Select(Path.GetFullPath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var definition in MapGen.CustomRooms.Definitions)
                {
                    expected.Add(Path.GetFullPath(ThumbnailGenerator.PathFor(definition.Name)));
                }
            }
            catch { return; }

            foreach (string path in Directory.EnumerateFiles(root, "*.png", SearchOption.TopDirectoryOnly))
            {
                var info = new FileInfo(path);
                if (info.Length == 0 || !expected.Contains(Path.GetFullPath(path)))
                {
                    TryDeleteFile(path, ref files, ref freed);
                }
            }
            foreach (string source in Directory.EnumerateFiles(root, "*.png.source",
                SearchOption.TopDirectoryOnly))
            {
                string png = source[..^".source".Length];
                if (!File.Exists(png) || !expected.Contains(Path.GetFullPath(png)))
                    TryDeleteFile(source, ref files, ref freed);
            }
            foreach (string marker in Directory.EnumerateFiles(root, ".worker*.done",
                SearchOption.TopDirectoryOnly))
            {
                TryDeleteFile(marker, ref files, ref freed);
            }
        }

        private static void CleanThumbnailLog(ref int files, ref long freed)
        {
            try
            {
                string path = ThumbnailLog.Path;
                if (File.Exists(path)
                    && File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-30))
                {
                    TryDeleteFile(path, ref files, ref freed);
                }
            }
            catch { }
        }

        private static string MapCacheRoot() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProjectPrime", "map-cache");

        private static string SafeThumbnailRoot()
        {
            try { return ThumbnailGenerator.CacheDirectory; }
            catch { return ""; }
        }

        private static string SafeReplayCacheRoot()
        {
            try { return Path.Combine(DemoLibrary.Directory, ".virtual-cache"); }
            catch { return ""; }
        }

        private static long DirectoryBytes(string path)
        {
            if (String.IsNullOrEmpty(path) || !Directory.Exists(path)) return 0;
            try
            {
                long total = 0;
                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; } catch { }
                }
                return total;
            }
            catch { return 0; }
        }

        private static bool TryDeleteDirectory(string path, ref int files)
        {
            try
            {
                int count = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count();
                Directory.Delete(path, recursive: true);
                files += count;
                return true;
            }
            catch { return false; }
        }

        private static void TryDeleteOldFile(string path, DateTime cutoff,
            ref int files, ref long freed)
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                    TryDeleteFile(path, ref files, ref freed);
            }
            catch { }
        }

        private static void TryDeleteFile(string path, ref int files, ref long freed)
        {
            try
            {
                long bytes = 0;
                try { bytes = new FileInfo(path).Length; } catch { }
                File.Delete(path);
                files++;
                freed += Math.Max(0, bytes);
            }
            catch { }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = Math.Max(0, bytes);
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return $"{value:0.#} {units[unit]}";
        }

        private sealed record CacheDirectory(DirectoryInfo Info, long Bytes);
    }
}
