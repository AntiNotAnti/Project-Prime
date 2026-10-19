#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    /// <summary>One authority and persisted-file witness shared by native annotation panels.</summary>
    private static class ReplayAnnotationWitness
    {
        public static readonly object Gate = new();
        public static bool IsCurrent(TheatreBookmarkContext context)
            => GameplayLeaseCurrent && DemoPlayback.IsActive
                && _gameplayScene!.LifetimeGeneration == context.SceneGeneration
                && DemoPlayback.LogicalPath == context.LogicalPath && DemoPlayback.PlaybackPath == context.PlaybackPath;
        public static TheatreBookmarkContext Capture()
        {
            if (!GameplayLeaseCurrent || !DemoPlayback.IsActive
                || DemoPlayback.LogicalPath is not { } logical || DemoPlayback.PlaybackPath is not { } playback)
                throw new InvalidOperationException("No foreground replay is open.");
            return new(_gameplayScene!.LifetimeGeneration, logical, playback,
                RecordingRevision(logical) + ":" + RecordingRevision(playback), AnnotationRevision(logical),
                ReplayController.CurrentFrame, ReplayController.DurationFrames);
        }
        public static void Validate(TheatreBookmarkContext context)
        {
            if (!IsCurrent(context)) throw new InvalidOperationException("The replay owner changed. Close this panel.");
            var current = Capture();
            if (current.RecordingRevision != context.RecordingRevision
                || current.AnnotationRevision != context.AnnotationRevision)
                throw new InvalidOperationException("The replay or its annotations changed. Refresh to review them before editing.");
        }
        // Explicit capture/validation only. Native camera and annotation panels
        // share the same descriptor witness; readonly IsCurrent does no file I/O.
        public static string RecordingRevision(string path)
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.LinkTarget != null) throw new IOException("The replay file is unavailable.");
            long length = file.Length, stamp = file.LastWriteTimeUtc.Ticks;
            string revision = length + ":" + stamp;
            if (!path.EndsWith(ReplayVirtualClips.Extension, StringComparison.OrdinalIgnoreCase)) return revision;
            const int maxDescriptorBytes = 64 * 1024;
            if (length is <= 0 or > maxDescriptorBytes)
                throw new IOException("The virtual replay descriptor is too large or empty to review safely.");
            byte[] bytes = new byte[(int)length];
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                input.ReadExactly(bytes);
                if (input.ReadByte() != -1) throw new IOException("The virtual replay descriptor changed while reading.");
            }
            file.Refresh();
            if (!file.Exists || file.LinkTarget != null || file.Length != length || file.LastWriteTimeUtc.Ticks != stamp)
                throw new IOException("The virtual replay descriptor changed while reading.");
            return revision + ":" + Convert.ToHexString(SHA256.HashData(bytes));
        }
        private static string AnnotationRevision(string logical)
        {
            var file = new FileInfo(logical + ReplayAnnotations.Extension);
            if (file.LinkTarget != null) throw new IOException("The annotation sidecar is a symbolic link.");
            if (!file.Exists) return "missing";
            if (file.Length > 1024 * 1024) throw new IOException("The annotation sidecar is too large to edit safely.");
            byte[] bytes = File.ReadAllBytes(file.FullName);
            try
            {
                var document = JsonSerializer.Deserialize<ReplayAnnotationDocument>(bytes);
                if (document is not { Version: 1, Bookmarks: not null, Highlights: not null }
                    || document.Bookmarks.Any(row => row == null || row.Id == Guid.Empty || string.IsNullOrWhiteSpace(row.Name))
                    || document.Bookmarks.Select(row => row.Id).Distinct().Count() != document.Bookmarks.Count
                    || document.Highlights.Any(row => row == null || row.Id == Guid.Empty || string.IsNullOrWhiteSpace(row.Name)
                        || row.StartFrame >= row.EndFrame || row.EndFrame > ReplayFormatV3.MaxFrame)
                    || document.Highlights.Select(row => row.Id).Distinct().Count() != document.Highlights.Count)
                    throw new InvalidDataException("The annotation sidecar is invalid. Repair it before editing.");
            }
            catch (JsonException error) { throw new InvalidDataException("The annotation sidecar is unreadable. Repair it before editing.", error); }
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
    }
}
#endif
