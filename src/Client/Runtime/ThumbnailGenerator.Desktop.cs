using System;
using System.Collections.Generic;
using System.Linq;
namespace MphRead.Mods
{
 internal static class DesktopThumbnailGenerator
 {
        public static void EnsureCustomPreviews(Action<string>? report = null)
        {
            if (!Launcher.GameFiles.Ready || !ThumbnailBatch.CanRun)
            {
                return;
            }
            List<string> missing;
            try
            {
                var custom = MapGen.CustomRooms.Definitions.Select(d => d.Name).ToHashSet();
                missing = ThumbnailGenerator.MissingThumbnails().Where(r => custom.Contains(r)).ToList();
            }
            catch
            {
                return;
            }
            if (missing.Count == 0)
            {
                return;
            }
            report ??= line => Console.WriteLine($"  {line}");
            report($"Rendering {missing.Count} custom map preview(s)...");
            try
            {
                ThumbnailBatch.Run(missing, ThumbnailBatch.DefaultParallelism,
                    ThumbnailGenerator.ThumbnailWidth, ThumbnailGenerator.ThumbnailHeight, report);
            }
            catch (Exception ex)
            {
                // a map with no picture is a cosmetic problem; it must not
                // stand between the player and the front screen
                report($"could not render: {ex.Message}");
            }
        }

 }
}
