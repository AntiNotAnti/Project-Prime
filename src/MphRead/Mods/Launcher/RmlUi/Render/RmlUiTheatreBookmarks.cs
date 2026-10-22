#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    /// <summary>Borrow the current scene lease and existing sidecar writer; never open another replay.</summary>
    private sealed class CurrentReplayBookmarks : ITheatreBookmarkBackend
    {
        public bool IsCurrent(TheatreBookmarkContext context) => ReplayAnnotationWitness.IsCurrent(context);
        public TheatreBookmarkContext Capture() => ReplayAnnotationWitness.Capture();
        public ImmutableArray<TheatreBookmarkEntry> Read(TheatreBookmarkContext context)
        {
            lock (ReplayAnnotationWitness.Gate)
            {
                Validate(context);
                var rows = ReplayAnnotations.Bookmarks(context.LogicalPath)
                    .Select(row => new TheatreBookmarkEntry(row.Id, row.Frame, row.Name, row.CreatedUtc)).ToImmutableArray();
                Validate(context);
                return rows;
            }
        }
        public void Add(TheatreBookmarkContext context, string name)
        {
            lock (ReplayAnnotationWitness.Gate)
            {
                Validate(context);
                ReplayAnnotations.AddBookmark(context.LogicalPath, context.Frame, name);
            }
        }
        public void Remove(TheatreBookmarkContext context, TheatreBookmarkEntry exact)
        {
            lock (ReplayAnnotationWitness.Gate)
            {
                ValidateExact(context, exact);
                ReplayAnnotations.RemoveBookmark(context.LogicalPath, exact.Id);
            }
        }
        public void Seek(TheatreBookmarkContext context, TheatreBookmarkEntry exact)
        {
            lock (ReplayAnnotationWitness.Gate)
            {
                ValidateExact(context, exact);
                ReplayController.Seek(exact.Frame, resume: false);
            }
        }
        private void ValidateExact(TheatreBookmarkContext context, TheatreBookmarkEntry exact)
        {
            Validate(context);
            var rows = ReplayAnnotations.Bookmarks(context.LogicalPath).Where(row => row.Id == exact.Id).ToArray();
            if (rows.Length != 1 || rows[0].Frame != exact.Frame || rows[0].Name != exact.Name
                || rows[0].CreatedUtc != exact.CreatedUtc || exact.Frame > ReplayController.DurationFrames)
                throw new InvalidOperationException("The selected bookmark changed. Cancel removal and refresh.");
            Validate(context);
        }
        private static void Validate(TheatreBookmarkContext context) => ReplayAnnotationWitness.Validate(context);
    }
}
#endif
