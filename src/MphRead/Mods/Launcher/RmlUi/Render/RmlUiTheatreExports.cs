#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private sealed class CurrentReplayExports : ITheatreExportBackend
    {
        public ImmutableArray<TheatreExportPreset> Presets => ReplayExportPresets.All.Select(preset => new TheatreExportPreset(
            preset.Name, (int)preset.Resolution, preset.Fps, preset.GameHud, preset.ReplayOverlay)).ToImmutableArray();
        public ImmutableArray<string> Encoders()
        {
            var probe = ReplayEncoderCapabilities.Available;
            return probe.IsCompletedSuccessfully ? probe.Result.ToImmutableArray() : ImmutableArray.Create("libx264");
        }
        public TheatreExportContext Capture()
        {
            if (DemoPlayback.IsCustomPlayback) throw new InvalidOperationException("Custom replay export is not qualified.");
            lock (ReplayAnnotationWitness.Gate)
            {
                var replay = ReplayAnnotationWitness.Capture();
                if (DemoPlayback.PresentationScene is not { } scene || !DemoPlayback.Owns(scene))
                    throw new InvalidOperationException("Wait for this replay's presentation before reviewing export inputs.");
                string sourceFacts = SourceFacts(replay.LogicalPath, replay.PlaybackPath);
                string source = SourceWitness(replay.LogicalPath, replay.PlaybackPath);
                string camera = SidecarWitness(replay.LogicalPath + ".camera", 8192);
                var track = new ReplayCameraTrack();
                if (!track.Load(replay.LogicalPath)) throw new InvalidDataException(track.LastError);
                string reel = SidecarWitness(replay.LogicalPath + ReplayReels.Extension, 1024 * 1024);
                var segments = ReadReel(replay.LogicalPath, replay.Duration);
                var presentation = ReplayExportPresentation.Capture() with { Keys = track.Keys.ToImmutableArray() };
                var audio=ReplayStudioAudioSettingsStore.Read(replay.LogicalPath,source);
                var audioSnapshot=ReplayExportAudioInput.Capture(audio.Settings,DemoPlayback.Events,DemoPlayback.Metadata?.Players??Array.Empty<ReplayPlayerInfo>());

                if (sourceFacts != SourceFacts(replay.LogicalPath, replay.PlaybackPath)
                    || camera != SidecarWitness(replay.LogicalPath + ".camera", 8192)
                    || reel != SidecarWitness(replay.LogicalPath + ReplayReels.Extension, 1024 * 1024)
                    || audio.Reviewed.Revision != ReplayReviewedMetadata.Read(replay.LogicalPath + ReplayStudioAudioSettingsStore.Extension).Revision)
                    throw new InvalidOperationException("Export source files changed during review. Refresh.");
                return new(replay.SceneGeneration, DemoPlayback.PlaybackOwner, replay.LogicalPath, replay.PlaybackPath,
                    source, camera, replay.AnnotationRevision, reel, replay.Duration, ReplayController.ClipIn, ReplayController.ClipOut,
                    segments.Length, presentation)
                { AudioSnapshot=audioSnapshot,AudioRevision=audio.Reviewed.Revision,
                    AudioSummary=audio.Settings.Enabled?"OFFLINE AUDIO ON // SAVED BUS, MUSIC AND CUE SETTINGS":"OFFLINE AUDIO OFF // SILENT VIDEO" };
            }
        }
        public bool IsCurrent(TheatreExportContext context) => GameplayLeaseCurrent && DemoPlayback.IsActive
            && _gameplayScene!.LifetimeGeneration == context.SceneGeneration && OwnerCurrent(context)
            && ReplayController.DurationFrames == context.Duration && ReplayController.ClipIn == context.MarkIn
            && ReplayController.ClipOut == context.MarkOut && context.Presentation is ReplayExportPresentation presentation
            && presentation.Matches()
            && ReplayReviewedMetadata.Read(context.LogicalPath+ReplayStudioAudioSettingsStore.Extension).Revision==context.AudioRevision;
        public TheatreExportQueueState Queue()
            =>ReplayDetachedExportQueue.Snapshot();
        public void Enqueue(TheatreExportContext context, TheatreExportOptions options)
        {
            if (DemoPlayback.IsCustomPlayback) throw new InvalidOperationException("Custom replay export is not qualified.");
            lock (ReplayAnnotationWitness.Gate)
            {
                if (!IsCurrent(context)) throw new TheatreExportStaleException("The reviewed replay, marks or camera changed. Refresh export inputs.");
                if (SourceError(context) is { } error) throw new TheatreExportStaleException(error);
                if(ReplayReviewedMetadata.Read(context.LogicalPath+ReplayStudioAudioSettingsStore.Extension).Revision!=context.AudioRevision)
                    throw new TheatreExportStaleException("Audio preferences changed. Refresh export inputs.");
                if((options.EncoderExecutable??"").Length>32768||(options.EncoderExecutable??"").Any(char.IsControl))throw new ArgumentException("Review the encoder executable path.");
                if (!ReplayDetachedExportQueue.CheckCapacity()) throw new InvalidOperationException("The independent export queue is full.");
                if (options.Preset < 0 || options.Preset >= ReplayExportPresets.All.Length || options.Resolution is < 0 or > 3
                    || !ReplayExportRates.IsSupported(options.Fps) || !Enum.IsDefined(options.Source) || !Enum.IsDefined(options.Camera)
                    || !Encoders().Contains(options.Encoder)) throw new ArgumentException("Review supported export settings.");
                var presentation = (ReplayExportPresentation)context.Presentation;
                if (options.Camera == TheatreExportCamera.SavedCameraTrack && presentation.Keys.IsEmpty)
                    throw new InvalidOperationException("Save camera keyframes or choose another export camera.");
                var preset = ReplayExportPresets.All[options.Preset] with { Resolution = (ReplayVideoResolution)options.Resolution,
                    Fps = options.Fps, GameHud = options.GameHud, ReplayOverlay = options.ReplayOverlay };
                uint start = options.Source == TheatreExportSource.Selection ? context.MarkIn
                    ?? throw new InvalidOperationException("Set Mark In and refresh.") : 0;
                uint end = options.Source == TheatreExportSource.Selection ? context.MarkOut
                    ?? throw new InvalidOperationException("Set Mark Out and refresh.") : context.Duration;
                if (start >= end || end > context.Duration) throw new InvalidOperationException("Choose a nonempty export range within this replay.");
                ReplaySegmentCamera camera = options.Camera switch { TheatreExportCamera.FirstPerson => ReplaySegmentCamera.FirstPerson,
                    TheatreExportCamera.Chase => ReplaySegmentCamera.Chase, TheatreExportCamera.Orbit => ReplaySegmentCamera.Orbit,
                    TheatreExportCamera.Director => ReplaySegmentCamera.Director, _ => ReplaySegmentCamera.Current };
                ReplayVideoExportManifest manifest;
                if (options.Source == TheatreExportSource.ExistingReel)
                {
                    var segments = ReadReel(context.LogicalPath, context.Duration).Select(segment => segment.Camera == ReplaySegmentCamera.Current
                        ? segment with { Camera = camera } : segment).ToArray();
                    manifest = ReplayVideoExport.CreateReelManifest(context.PlaybackPath, segments, preset)
                        with { CameraTrack = options.Camera == TheatreExportCamera.SavedCameraTrack };
                }
                else
                {
                    manifest = ReplayVideoExport.CreatePresetManifest(context.PlaybackPath, start, end, preset,
                        director: options.Camera == TheatreExportCamera.Director, cameraTrack: options.Camera == TheatreExportCamera.SavedCameraTrack)
                        with { Segments = new[] { new ReplayVideoSegment(start, end, "Reviewed export", camera) } };
                }
                manifest = ReplayEncoderCapabilities.Apply(manifest, options.Encoder) with
                {EncoderExecutable=(options.EncoderExecutable??"").Trim(),Audio=context.AudioSnapshot as ReplayExportAudioInput};
                ReplayVideoExport.WriteReviewedManifest(manifest);
                ReplayDetachedExportQueue.Enqueue(manifest,presentation,ReplayDetachedExportFiles.PlaybackHash(context.SourceRevision),
                    DemoPlayback.Metadata?.RoomKey??throw new InvalidOperationException("The export recording metadata is unavailable."),
                    DemoPlayback.PresentationScene!.Players.MainPlayerIndex,context.LogicalPath);
            }
        }
        public bool CancelExact(TheatreExportQueueState reviewed)=>ReplayDetachedExportQueue.CancelExact(reviewed);
        public bool RetryExact(TheatreExportQueueState reviewed)=>ReplayDetachedExportQueue.RetryExact(reviewed);
        public bool ClearExact(TheatreExportQueueState reviewed)=>ReplayDetachedExportQueue.ClearExact(reviewed);
        public void OpenOutput(string output)
        {
            string full=Path.GetFullPath(output);
            string directory = Directory.Exists(full)?full:Path.GetDirectoryName(full) ?? throw new IOException("The output folder is unavailable.");
            if (!Directory.Exists(directory)) throw new IOException("The output folder no longer exists.");
            try { Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true }); }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or NotSupportedException)
            { throw new InvalidOperationException("The output folder could not be opened: " + error.Message, error); }
        }
        private static TheatreExportJob Job(ReplayExportReceipt receipt)
        {
            var job = receipt.Manifest;
            string cameras = job.CameraTrack ? "SAVED TRACK" : job.Director ? "DIRECTOR"
                : string.Join(", ", job.Segments?.Select(segment => segment.Camera.ToString()).Distinct() ?? new[] { "CURRENT" });
            return new(receipt.Id, receipt, Path.GetFileName(ReplayExportQueue.LogicalPathFor(job)), job.SuggestedOutput,
                $"{job.StartFrame}–{job.EndFrame} // {job.Width}×{job.Height} // {job.Fps} FPS // {job.PresetName}"
                + $" // {cameras} // HUD {(job.IncludeGameHud ? "ON" : "OFF")} // OVERLAY {(job.IncludeReplayOverlay ? "ON" : "OFF")}");
        }
        private static bool OwnerCurrent(TheatreExportContext context) => DemoPlayback.IsActive
            && ReferenceEquals(DemoPlayback.PlaybackOwner, context.PlaybackOwner)
            && DemoPlayback.PlaybackPath == context.PlaybackPath;
        private static string? SourceError(TheatreExportContext context)
        {
            try { return SourceErrorCore(context); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            { return error.Message; }
        }
        private static string? SourceErrorCore(TheatreExportContext context)
        {
            if (!OwnerCurrent(context) || DemoPlayback.LogicalPath != context.LogicalPath) return "The original playback owner changed. Review a new export in that replay.";
            if (SourceWitness(context.LogicalPath, context.PlaybackPath) != context.SourceRevision
                || SidecarWitness(context.LogicalPath + ".camera", 8192) != context.CameraRevision
                || SidecarWitness(context.LogicalPath + ReplayAnnotations.Extension, 1024 * 1024) != context.AnnotationRevision
                || SidecarWitness(context.LogicalPath + ReplayReels.Extension, 1024 * 1024) != context.ReelRevision)
                return "The reviewed replay, camera, annotations or reel changed. Refresh before creating a new export.";
            return null;
        }
        private static string SourceWitness(string logical, string playback) => ReplayNativeFileWitness.Source(logical, playback);
        private static string SourceFacts(string logical, string playback) => ReplayNativeFileWitness.Facts(logical, playback);
        private static string SidecarWitness(string path, int maximum) => ReplayReviewedMetadata.Read(path, maximum).Revision;
        private static byte[]? SidecarBytes(string path, int maximum) => ReplayReviewedMetadata.Read(path, maximum).Bytes;
        private static ImmutableArray<ReplayReelSegment> ReadReel(string logical, uint duration)
        {
            string path = logical + ReplayReels.Extension;
            byte[]? bytes = SidecarBytes(path, 1024 * 1024);
            if (bytes == null) return ImmutableArray<ReplayReelSegment>.Empty;
            ReplayReelDocument? document;
            try { document = JsonSerializer.Deserialize<ReplayReelDocument>(bytes); }
            catch (JsonException error) { throw new InvalidDataException("The saved reel is unreadable.", error); }
            if (document is not { Version: 1, Segments: not null } || document.Segments.Count > 128
                || document.Segments.Any(segment => segment == null || segment.Id == Guid.Empty || segment.StartFrame >= segment.EndFrame
                    || segment.EndFrame > duration || !Enum.IsDefined(segment.Camera) || string.IsNullOrWhiteSpace(segment.Name))
                || document.Segments.Select(segment => segment.Id).Distinct().Count() != document.Segments.Count)
                throw new InvalidDataException("The saved reel is invalid or exceeds 128 segments. Repair it before export.");
            return document.Segments.ToImmutableArray();
        }
    }
}
#endif
