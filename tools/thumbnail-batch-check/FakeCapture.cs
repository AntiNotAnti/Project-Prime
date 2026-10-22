using System;
using System.Collections.Concurrent;
using System.IO;

namespace MphRead.Mods
{
    // The real process supervisor, with asset-free completion markers in place
    // of scene rendering. Child processes never load game files or graphics.
    public static class ThumbnailGenerator
    {
        public static string Root => Environment.GetEnvironmentVariable("FRUITY_PREVIEW_FIXTURE")!;
        public static bool Exists(string room) => File.Exists(Path.Combine(Root, "result-" + room));
    }
    public static class ThumbnailLog
    {
        public static readonly ConcurrentQueue<string> Lines = new();
        public static void Begin(int count) { }
        public static void Write(string line) => Lines.Enqueue(line);
    }
    public static class ThumbnailCapture
    {
        public static bool CaptureRoom(string room, int width, int height)
            => throw new InvalidOperationException("The test must use real child processes.");
    }
}

// Production reads Launcher.GameFiles.Root when starting isolated thumbnail
// workers. In this asset-free harness, the private capture fixture is the
// configured writable data root that each child must inherit.
namespace MphRead.Mods.Launcher
{
    public static class GameFiles
    {
        public static string Root => global::MphRead.Mods.ThumbnailGenerator.Root;
    }
}
