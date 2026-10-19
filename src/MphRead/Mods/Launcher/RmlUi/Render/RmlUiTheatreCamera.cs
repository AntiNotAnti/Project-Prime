#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private sealed partial class CurrentReplayCamera : ITheatreCameraBackend
    {
        public TheatreCameraContext Capture()
        {
            lock (ReplayAnnotationWitness.Gate)
            {
                if (!GameplayLeaseCurrent || !DemoPlayback.IsActive || DemoPlayback.LogicalPath is not { } logical
                    || DemoPlayback.PlaybackPath is not { } playback || DemoPlayback.PresentationScene is not { } scene
                    || !DemoPlayback.Owns(scene))
                    throw new InvalidOperationException("No foreground replay presentation is available.");
                RequireSettled();
                string recording = RecordingWitness(logical, playback);
                ReplayCamera.EnsureTrack();
                string revision = CameraRevision(logical);
                ReplayCamera.ReloadReviewedTrack(logical);
                if (CameraRevision(logical) != revision || RecordingWitness(logical, playback) != recording)
                    throw new TheatreCameraStaleException("The camera sidecar changed while it was being reviewed. Refresh.");
                var transport = DemoPlayback.Session.Transport;
                uint frame = transport.CurrentFrame;
                var pose = scene.CaptureNativeReplayCamera(frame, out long presentationRevision);
                return new(_gameplayScene!.LifetimeGeneration, transport, scene, transport.SeekGeneration,
                    logical, playback, recording, revision, frame, transport.DurationFrames, true,
                    Pose(pose), ReplayCamera.Track.Keys.Select(Key).ToImmutableArray(),
                    ReplayCamera.PlayTrack) { PresentationRevision = presentationRevision };
            }
        }
        public bool IsCurrent(TheatreCameraContext context)
        {
            if (!GameplayLeaseCurrent || !DemoPlayback.IsActive || !ReplayController.IsPaused || ReplayController.IsSeeking
                || ReplayVideoExporter.Active || ReplayCamera.BookmarkRequested || ReplayCamera.RestoreRequested
                || _gameplayScene!.LifetimeGeneration != context.SceneGeneration
                || DemoPlayback.LogicalPath != context.LogicalPath || DemoPlayback.PlaybackPath != context.PlaybackPath
                || !ReferenceEquals(DemoPlayback.Session.Transport, context.Transport)
                || !ReferenceEquals(DemoPlayback.PresentationScene, context.PresentationScene)
                || DemoPlayback.Session.Transport.SeekGeneration != context.SeekGeneration
                || ReplayController.CurrentFrame != context.Frame || ReplayController.DurationFrames != context.Duration
                || ReplayCamera.PlayTrack != context.TrackPlaying) return false;
            return context.PresentationScene is Scene scene && DemoPlayback.Owns(scene)
                && scene.TryCaptureNativeReplayCamera(context.Frame, out var pose, out _)
                && Pose(pose) == context.Pose
                && ReplayCamera.Track.Keys.Select(Key).SequenceEqual(context.Keys);
        }
        public void SetFreeCamera(TheatreCameraContext context)
        { lock (ReplayAnnotationWitness.Gate) { Validate(context); UseFreeCamera(context); } }
        public void AdjustLens(TheatreCameraContext context, float fov, float roll)
        {
            lock (ReplayAnnotationWitness.Gate)
            {
                Validate(context);
                UseFreeCamera(context);
                ReplayCamera.AdjustLens(fov, roll);
            }
        }
        public void Preview(TheatreCameraContext context, TheatreCameraKey key)
        {
            lock (ReplayAnnotationWitness.Gate)
            {
                Validate(context);
                if (!context.Keys.Contains(key)) throw new TheatreCameraStaleException("The selected camera keyframe changed.");
                ReplayCamera.SelectKeyframe(key.Frame);
            }
        }
        public void ToggleTrack(TheatreCameraContext context)
        { lock (ReplayAnnotationWitness.Gate) { Validate(context); ReplayCamera.ToggleTrackPlayback(); } }
        public void Commit(TheatreCameraEdit exact)
        {
            lock (ReplayAnnotationWitness.Gate)
            {
                var context = exact.Context;
                Validate(context);
                var existing = context.Keys.FirstOrDefault(key => key.Frame == exact.Key.Frame);
                if (exact.Key.Frame > context.Duration || !Enum.IsDefined(exact.Change)
                    || (exact.Change == TheatreCameraChange.Add && (existing != null || exact.Key.Frame != context.Frame))
                    || (exact.Change != TheatreCameraChange.Add && existing == null)
                    || (exact.Change == TheatreCameraChange.Remove && existing != exact.Key)
                    || (exact.Change != TheatreCameraChange.Remove && exact.Key.Pose != context.Pose))
                    throw new TheatreCameraStaleException("The exact camera edit no longer matches the reviewed frame and key.");
                ReplayCamera.CommitReviewedKeyframe(context.LogicalPath, NativeKey(exact.Key),
                    exact.Change == TheatreCameraChange.Remove);
            }
        }
        private void Validate(TheatreCameraContext context)
        {
            if (!IsCurrent(context)) throw new TheatreCameraStaleException("The replay, camera pose or seek owner changed. Cancel and refresh.");
            try
            {
                if (RecordingWitness(context.LogicalPath, context.PlaybackPath) != context.RecordingRevision
                    || CameraRevision(context.LogicalPath) != context.CameraRevision)
                    throw new TheatreCameraStaleException("The recording or camera sidecar changed. Cancel and refresh before editing.");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            { throw new TheatreCameraStaleException(error.Message + " Cancel and refresh before editing."); }
        }
        private static void UseFreeCamera(TheatreCameraContext context)
        {
            ((Scene)context.PresentationScene).UseNativeReplayFreeCamera(NativeKey(new(context.Frame, context.Pose)));
            ReplayCamera.FieldOfView = context.Pose.Fov;
            ReplayCamera.Roll = context.Pose.Roll;
            ReplayCamera.LookAtSlot = context.Pose.LookAt;
        }
        private static string RecordingWitness(string logical, string playback)
        {
            string witness = ReplayAnnotationWitness.RecordingRevision(logical) + ":" + ReplayAnnotationWitness.RecordingRevision(playback);
            if (logical.EndsWith(ReplayVirtualClips.Extension, StringComparison.OrdinalIgnoreCase))
            {
                if (!ReplayVirtualClips.TryLoad(logical, out var clip) || clip == null)
                    throw new InvalidDataException("The virtual replay clip is invalid.");
                witness += ":" + Path.GetFullPath(clip.SourceReplay) + ":" + ReplayAnnotationWitness.RecordingRevision(clip.SourceReplay);
            }
            return witness;
        }
        private static void RequireSettled()
        {
            if (!ReplayController.IsPaused || ReplayController.IsSeeking || ReplayVideoExporter.Active
                || ReplayCamera.BookmarkRequested || ReplayCamera.RestoreRequested)
                throw new InvalidOperationException("Pause playback and wait for seeking or camera capture to finish, then open or refresh Camera & Keyframes.");
        }
        private static string CameraRevision(string logical)
        {
            var file = new FileInfo(logical + ".camera");
            if (file.LinkTarget != null) throw new IOException("The camera sidecar is a symbolic link.");
            if (!file.Exists) return "missing";
            if (file.Length > 8192) throw new InvalidDataException("The camera sidecar is too large to edit safely.");
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file.FullName)));
        }
        private static TheatreCameraPose Pose(ReplayCameraKeyframe key) => new(key.Position.X, key.Position.Y, key.Position.Z,
            key.Rotation.X, key.Rotation.Y, key.Rotation.Z, key.Rotation.W,
            MathHelper.RadiansToDegrees(key.Fov), MathHelper.RadiansToDegrees(key.Roll),
            key.LookAtSlot, (int)key.Interpolation, (int)key.Ease,
            key.IncomingTangent.HasValue||key.OutgoingTangent.HasValue||key.FovIncomingTangent!=0||key.FovOutgoingTangent!=0||key.RollIncomingTangent!=0||key.RollOutgoingTangent!=0
                ?new TheatreCameraHandles(key.IncomingTangent is{} input?new(input.X,input.Y,input.Z):null,key.OutgoingTangent is{} output?new(output.X,output.Y,output.Z):null,
                    MathHelper.RadiansToDegrees(key.FovIncomingTangent),MathHelper.RadiansToDegrees(key.FovOutgoingTangent),MathHelper.RadiansToDegrees(key.RollIncomingTangent),MathHelper.RadiansToDegrees(key.RollOutgoingTangent)):null);
        private static TheatreCameraKey Key(ReplayCameraKeyframe key) => new(key.Frame, Pose(key));
        private static ReplayCameraKeyframe NativeKey(TheatreCameraKey key) => new(key.Frame,
            new Vector3(key.Pose.X, key.Pose.Y, key.Pose.Z), new Quaternion(key.Pose.Qx, key.Pose.Qy, key.Pose.Qz, key.Pose.Qw),
            MathHelper.DegreesToRadians(key.Pose.Fov), (sbyte)key.Pose.LookAt, MathHelper.DegreesToRadians(key.Pose.Roll),
            (ReplayCameraInterpolation)key.Pose.Interpolation, (ReplayCameraEase)key.Pose.Ease,
            key.Pose.Handles?.Incoming is{} input?new Vector3(input.X,input.Y,input.Z):null,key.Pose.Handles?.Outgoing is{} output?new Vector3(output.X,output.Y,output.Z):null,
            MathHelper.DegreesToRadians(key.Pose.Handles?.FovIn??0),MathHelper.DegreesToRadians(key.Pose.Handles?.FovOut??0),
            MathHelper.DegreesToRadians(key.Pose.Handles?.RollIn??0),MathHelper.DegreesToRadians(key.Pose.Handles?.RollOut??0));
    }
}
#endif
