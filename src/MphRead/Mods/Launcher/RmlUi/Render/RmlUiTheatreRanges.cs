#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private sealed class CurrentReplayRanges : ITheatreRangeBackend
    {
        public bool MaterializedClipsAvailable => true;
        public TheatreRangeContext Capture()
            => new(ReplayAnnotationWitness.Capture(), ReplayController.ClipIn, ReplayController.ClipOut);
        public bool IsCurrent(TheatreRangeContext context) => ReplayAnnotationWitness.IsCurrent(context.Replay);
        public TheatreRangeSelection ReadSelection(TheatreRangeContext context)
        {
            if (!IsCurrent(context)) throw new InvalidOperationException("The replay owner changed. Close this panel.");
            return new(ReplayController.ClipIn, ReplayController.ClipOut, ReplayController.DurationFrames);
        }
        public void MarkIn(TheatreRangeContext context)
        {
            lock (ReplayAnnotationWitness.Gate)
            { Validate(context); ReplayController.SetMarkIn(context.Replay.Frame); }
        }
        public void MarkOut(TheatreRangeContext context)
        {
            lock (ReplayAnnotationWitness.Gate)
            { Validate(context); ReplayController.SetMarkOut(context.Replay.Frame); }
        }
        public string SaveHighlight(TheatreRangeContext context, string name)
        {
            lock (ReplayAnnotationWitness.Gate)
            {
                ValidateRange(context);
                return ReplayAnnotations.AddHighlight(context.Replay.LogicalPath, context.MarkIn!.Value,
                    context.MarkOut!.Value, name).Name;
            }
        }
        public string CreateVirtualClip(TheatreRangeContext context, string name)
        {
            lock (ReplayAnnotationWitness.Gate)
            {
                ValidateRange(context);
                // The existing writer flattens virtual parent clips and rebases their marks.
                string path = ReplayVirtualClips.Save(context.Replay.PlaybackPath, context.MarkIn!.Value,
                    context.MarkOut!.Value, name);
                return Path.GetFileName(path);
            }
        }
        public Task<TheatreMaterializedClipResult> SaveMaterializedClip(TheatreRangeContext context, string name,
            CancellationToken cancellation)
        {
            lock (ReplayAnnotationWitness.Gate)
            {
                ValidateRange(context);
                // Capture before the first await: a seek can replace the session/transport while extraction runs.
                ReplayTransport transport = DemoPlayback.Session.Transport;
                Task<ReplayOpenResult> operation = transport.SaveSelectionAsync(cancellation);
                return CompleteMaterializedClip(operation, transport, name, cancellation);
            }
        }
        private static async Task<TheatreMaterializedClipResult> CompleteMaterializedClip(Task<ReplayOpenResult> operation,
            ReplayTransport transport, string name, CancellationToken cancellation)
        {
            ReplayOpenResult result = await operation.ConfigureAwait(false);
            if (result != ReplayOpenResult.Success)
                return new(false, "", "Could not save materialized clip: " + result + ".");
            // Read the operation's retained transport, never the Current facade after an async seek.
            string? output = transport.LastSavedSelectionPath;
            if (string.IsNullOrWhiteSpace(output)) return new(false, "", "Clip extraction returned no output path.");
            string warning = "";
            if (name.Length > 0 && !cancellation.IsCancellationRequested)
                try { DemoLibrary.Rename(output, name); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
                { warning = "The clip was saved, but its name could not be saved: " + error.Message; }
            return new(true, output, warning);
        }
        private static void Validate(TheatreRangeContext context)
        {
            ReplayAnnotationWitness.Validate(context.Replay);
            if (ReplayController.ClipIn != context.MarkIn || ReplayController.ClipOut != context.MarkOut)
                throw new InvalidOperationException("The replay marks changed. Refresh before creating anything.");
        }
        private static void ValidateRange(TheatreRangeContext context)
        {
            Validate(context);
            if (context.MarkIn is not uint start || context.MarkOut is not uint end
                || start >= end || end > context.Replay.Duration)
                throw new InvalidOperationException("Mark a start and a later end within this replay first.");
        }
    }
}
#endif
