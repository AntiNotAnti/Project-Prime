#if !MPHREAD_SERVER
using System;
using System.IO;
using System.Linq;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay
{
    public static partial class ReplayCamera
    {
        internal static readonly ReplayCameraPresentationCache<Scene> NativePresented = new();
        internal sealed record NativeCameraSettings(ReplayCameraMode Mode, ReplayPresentationProfile Profile,
            float Fov, float Roll, int LookAt, bool Director, bool Track, bool Speed, bool Collision,
            ReplayCameraInterpolation Interpolation, ReplayCameraEase Ease, int Subject);
        internal static NativeCameraSettings PresentedSettings(Scene scene) => new(Mode, Profile,
            FieldOfView, Roll, LookAtSlot, Director, PlayTrack, TrackConstantSpeed, TrackCollisionAvoidance,
            TrackInterpolation, TrackEase, scene.Players.MainPlayerIndex);
        internal static void ReloadReviewedTrack(string logicalPath)
        {
            EnsureTrack();
            if(MemoryDocumentDirty)return; // The source-bound editor owns this in-memory document until Save or Discard.
            var loaded = new ReplayCameraTrack();
            if (!loaded.Load(logicalPath)) throw new InvalidDataException(loaded.LastError);
            if (!ReplayPathComparer.Comparer.Equals(_trackPath, logicalPath))
                throw new InvalidOperationException("The replay camera owner changed.");
            if (Track.Keys.SequenceEqual(loaded.Keys)) return;
            Track.Clear();
            foreach (var key in loaded.Keys) Track.Put(key);
            ClearSelection();
        }

        /// <summary>Commit the already captured pose/frame; no global draw-time request can retarget this edit.</summary>
        internal static void CommitReviewedKeyframe(string logicalPath, ReplayCameraKeyframe key, bool remove)
        {
            EnsureTrack();
            if (!ReplayPathComparer.Comparer.Equals(_trackPath, logicalPath))
                throw new InvalidOperationException("The replay camera owner changed.");
            if(MemoryDocumentDirty)throw new InvalidOperationException("Save or discard the advanced camera document before editing individual saved keys.");
            if (!Track.EditAndSave(logicalPath, track => remove ? track.Remove(key.Frame) : track.Put(key)))
                throw new IOException(Track.LastError ?? "The selected camera keyframe could not be saved.");
            if (remove)
            {
                if (_restoreKey is { } restore && restore.Frame == key.Frame) _restoreKey = null;
                ClearSelection();
            }
            else SetSingleSelection(key.Frame);
            EditStatus = remove ? "Camera keyframe removed." : "Camera keyframe saved.";
        }
    }
}

namespace MphRead
{
    public partial class Scene
    {
        internal void RestoreNativeReplayMemoryCamera(Mods.Replay.ReplayCameraKeyframe key)
        {
            _cameraPosition=key.Position;
            _cameraFacing=Vector3.Transform(-Vector3.UnitZ,key.Rotation).Normalized();
            _cameraUp=Vector3.Transform(Vector3.Transform(Vector3.UnitY,key.Rotation),Quaternion.FromAxisAngle(_cameraFacing,key.Roll)).Normalized();
            _cameraRight=Vector3.Cross(_cameraFacing,_cameraUp).Normalized();_cameraFov=key.Fov;
            Mods.Replay.ReplayCamera.Changed=true;
        }
        internal void UseNativeReplayFreeCamera(Mods.Replay.ReplayCameraKeyframe reviewed)
        {
            if (!ReferenceEquals(DemoPlayback.PresentationScene, this))
                throw new InvalidOperationException("The replay presentation scene changed.");
            Mods.Replay.ReplayCamera.Director = false;
            Mods.Replay.ReplayCamera.SetMode(Mods.Replay.ReplayCameraMode.Free);
            SetFreeCamera(true);
            // Preserve the picture the user reviewed when handing control to the free camera.
            _cameraPosition = reviewed.Position;
            _cameraFacing = Vector3.Transform(-Vector3.UnitZ, reviewed.Rotation).Normalized();
            _cameraUp = Vector3.Transform(Vector3.Transform(Vector3.UnitY, reviewed.Rotation),
                Quaternion.FromAxisAngle(_cameraFacing, reviewed.Roll)).Normalized();
            _cameraRight = Vector3.Cross(_cameraFacing, _cameraUp).Normalized();
            _cameraFov = reviewed.Fov;
        }

        // Only locals/static weak storage are added: these presentation facts never enter the Scene replay schema.
        private Mods.Replay.ReplayPresentedCamera? StageNativeReplayCamera()
        {
            #if !ANDROID
            if(NativeReplayMultiViewActive)return null;
#endif
            Mods.Replay.ReplayCamera.NativePresented.DiscardRendered(this);
            if (!ReferenceEquals(DemoPlayback.PresentationScene, this) || !DemoPlayback.Owns(this)
                || !DemoPlayback.IsActive || ReplayController.IsSeeking || DemoPlayback.LogicalPath is not { } logical
                || DemoPlayback.PlaybackPath is not { } playback
                || !Mods.Replay.ReplayCameraPresentationMath.TryDecode(_viewMatrix, _perspectiveMatrix, out var pose)) return null;
            var transport = DemoPlayback.Session.Transport;
            return new(0, transport, transport.SeekGeneration, transport.CurrentFrame, logical, playback,
                Mods.Replay.ReplayCamera.PresentedSettings(this), pose);
        }
        private void QueueRenderedNativeReplayCamera(Mods.Replay.ReplayPresentedCamera? staged)
        {
            if (staged != null) Mods.Replay.ReplayCamera.NativePresented.QueueRendered(this, staged);
        }
        internal void PublishNativeReplayPresentedFrame()
        {
            var presentation = DemoPlayback.PresentationScene;
            if (presentation == null || !(ReferenceEquals(presentation, this)
                || ReferenceEquals(DemoPlayback.Presentation(this), presentation))) return;
            if (Mods.Replay.ReplayCamera.NativePresented.TryTakeRendered(presentation, out var rendered))
                presentation.CompleteNativeReplayCamera(rendered);
        }
        private void CompleteNativeReplayCamera(Mods.Replay.ReplayPresentedCamera? staged)
        {
            if (staged == null || !ReferenceEquals(DemoPlayback.PresentationScene, this) || !DemoPlayback.Owns(this)
                || !DemoPlayback.IsActive || ReplayController.IsSeeking
                || !ReferenceEquals(DemoPlayback.Session.Transport, staged.Transport)
                || DemoPlayback.Session.Transport.SeekGeneration != staged.SeekGeneration
                || ReplayController.CurrentFrame != staged.Frame || DemoPlayback.LogicalPath != staged.LogicalPath
                || DemoPlayback.PlaybackPath != staged.PlaybackPath
                || !Equals(Mods.Replay.ReplayCamera.PresentedSettings(this), staged.Settings)) return;
            Mods.Replay.ReplayCamera.NativePresented.Publish(this, staged);
        }
        internal bool TryCaptureNativeReplayCamera(uint frame, out Mods.Replay.ReplayCameraKeyframe key, out long revision)
        {
            key = default; revision = 0;
            if (!ReferenceEquals(DemoPlayback.PresentationScene, this) || !DemoPlayback.Owns(this)
                || !DemoPlayback.IsActive || ReplayController.IsSeeking || !ReplayController.IsPaused
                || DemoPlayback.LogicalPath is not { } logical || DemoPlayback.PlaybackPath is not { } playback) return false;
            var transport = DemoPlayback.Session.Transport;
            if (transport.CurrentFrame != frame || !Mods.Replay.ReplayCamera.NativePresented.TryRead(this, transport,
                transport.SeekGeneration, frame, logical, playback, Mods.Replay.ReplayCamera.PresentedSettings(this), out var saved)) return false;
            var pose = saved!.Pose;
            // The displayed roll is decomposed from the view basis; quaternion contains yaw/pitch only.
            // Capture a literal displayed orientation. A new look-at target would rotate this pose again during playback.
            key = new(frame, pose.Position, pose.Rotation, pose.Fov, -1,
                pose.Roll, Mods.Replay.ReplayCamera.TrackInterpolation, Mods.Replay.ReplayCamera.TrackEase);
            revision = saved.Revision; return true;
        }
        internal Mods.Replay.ReplayCameraKeyframe CaptureNativeReplayCamera(uint frame, out long revision)
        {
            if (!TryCaptureNativeReplayCamera(frame, out var key, out revision))
                throw new InvalidOperationException("Wait for a successful replay picture after pausing or changing the camera, then refresh the review.");
            return key;
        }
    }
}
#endif
