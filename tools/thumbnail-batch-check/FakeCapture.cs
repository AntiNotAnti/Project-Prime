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
        private static string Result(string room) => Path.Combine(Root, "result-" + room);
        public static bool Exists(string room) => File.Exists(Result(room));
        public static bool HasCinematicPresentation(string room) => Exists(room);
        public static void InvalidateCinematicPresentation(string room)
        {
            string path = Result(room);
            if (File.Exists(path)) File.Delete(path);
        }
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
