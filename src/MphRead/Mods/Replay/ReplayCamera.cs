using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay
{
    public enum ReplayPresentationProfile { Faithful, Presentation }
    public enum ReplayCameraMode { FirstPerson, Chase, Free, Orbit }
    public static class ReplayCamera
    {
        public static ReplayCameraMode Mode { get; private set; }
        public static float Distance { get; set; } = 4;
        public static float Height { get; set; } = 1.5f;
        public static float FieldOfView { get; set; } = 80;
        public static float Roll { get; set; }
        public static bool Director { get; set; }
        public static bool TrackCollisionAvoidance { get; set; } = true;
        public static bool TrackConstantSpeed { get; set; } = true;
        internal static ReplayCameraInterpolation TrackInterpolation { get; set; } = ReplayCameraInterpolation.Spline;
        internal static ReplayCameraEase TrackEase { get; set; } = ReplayCameraEase.InOut;
        internal static readonly ReplayCameraTrack Track = new();
        private static string? _trackPath;
        private static int _bookmarkIndex;
        private static ReplayCameraKeyframe? _copiedKeyframe;
        public static ReplayPresentationProfile Profile { get; private set; } = ReplayPresentationProfile.Faithful;
        private static bool _playTrack;
        public static bool PlayTrack { get => _playTrack; set { _playTrack = value; Changed = true; } }
        public static int LookAtSlot { get; set; } = -1;
        public static string? TrackError => Track.LastError;
        public static string EditStatus { get; private set; } = "";
        public static int KeyframeCount => Track.Keys.Count;
        private static readonly SortedSet<uint> _selectedFrames = new();
        private static uint? _selectionAnchor;
        public static IReadOnlyCollection<uint> SelectedFrames => _selectedFrames;
        public static int SelectedKeyframeCount => _selectedFrames.Count;
        public static uint? SelectedFrame { get; private set; }
        private static uint EditFrame => SelectedFrame ?? ReplayController.CurrentFrame;
        private static ReplayCameraKeyframe? _restoreKey;

        private static void SetSingleSelection(uint frame)
        {
            _selectedFrames.Clear();
            _selectedFrames.Add(frame);
            _selectionAnchor = frame;
            SelectedFrame = frame;
        }

        private static void ClearSelection()
        {
            _selectedFrames.Clear();
            _selectionAnchor = null;
            SelectedFrame = null;
        }
        public static void SelectKeyframe(uint frame)
        {
            EnsureTrack();
            var key = Track.Keys.Where(k => k.Frame == frame).Select(k => (ReplayCameraKeyframe?)k).FirstOrDefault();
            if (key is not { } saved) return;
            SetSingleSelection(frame);
            EditStatus = $"Editing keyframe {frame}. Use UPDATE SELECTED to save changes.";
            FieldOfView = MathHelper.RadiansToDegrees(saved.Fov);
            Roll = MathHelper.RadiansToDegrees(saved.Roll);
            LookAtSlot = saved.LookAtSlot;
            TrackInterpolation = saved.Interpolation; TrackEase = saved.Ease;
            Director = false;
            SetMode(ReplayCameraMode.Free);
            _restoreKey = saved; RestoreRequested = true;
            ReplayController.Seek(frame, resume: false);
        }
        public static void SelectAdjacentKeyframe(int direction)
        {
            EnsureTrack();
            if (Track.Keys.Count == 0) return;
            int index = Track.Keys.ToList().FindIndex(k => k.Frame == EditFrame);
            SelectKeyframe(Track.Keys[Math.Clamp(index + direction, 0, Track.Keys.Count - 1)].Frame);
        }

        internal static void ModifyKeyframeSelection(uint frame, bool toggle, bool range)
        {
            EnsureTrack();
            if (!Track.Keys.Any(key => key.Frame == frame)) return;
            if (!toggle && !range)
            {
                SelectKeyframe(frame);
                return;
            }
            if (_selectedFrames.Count == 0 || !_selectionAnchor.HasValue)
            {
                SelectKeyframe(frame);
                return;
            }

            if (range)
            {
                uint start = Math.Min(_selectionAnchor.Value, frame);
                uint end = Math.Max(_selectionAnchor.Value, frame);
                _selectedFrames.Clear();
                foreach (var key in Track.Keys)
                    if (key.Frame >= start && key.Frame <= end) _selectedFrames.Add(key.Frame);
                if (SelectedFrame.HasValue && !_selectedFrames.Contains(SelectedFrame.Value))
                    SelectedFrame = null;
            }
            else if (!_selectedFrames.Add(frame))
            {
                _selectedFrames.Remove(frame);
                if (SelectedFrame == frame) SelectedFrame = null;
                if (_selectionAnchor == frame)
                    _selectionAnchor = _selectedFrames.Count == 0 ? null : _selectedFrames.First();
            }

            EditStatus = _selectedFrames.Count switch
            {
                0 => "No camera keyframes selected.",
                1 => "1 camera keyframe selected.",
                _ => $"{_selectedFrames.Count} camera keyframes selected. Delete removes all selected."
            };
            ReplayController.NoteInput();
        }
        public static void AdjustLens(float fov, float roll)
        {
            Director = false;
            if (Mode != ReplayCameraMode.Free || PlayTrack) SetMode(ReplayCameraMode.Free);
            SetProfile(ReplayPresentationProfile.Presentation);
            FieldOfView = Math.Clamp(FieldOfView + fov, 20, 140);
            Roll = Math.Clamp(Roll + roll, -180, 180);
        }
        public static void UpdateSelectedKeyframe()
        {
            if (_selectedFrames.Count > 1)
            {
                EditStatus = "Select one keyframe before updating it.";
                return;
            }
            if (SelectedFrame.HasValue) { _updateSelected = true; Bookmark(); }
            else EditStatus = "Select a keyframe before updating it.";
        }
        private static bool _updateSelected;
        public static void SetProfile(ReplayPresentationProfile profile)
        {
            Profile = profile;
            if (profile == ReplayPresentationProfile.Faithful) PlayTrack = false;
            Changed = true;
            ReplayController.NoteInput();
        }
        public static void ToggleTrackPlayback()
        {
            SetProfile(ReplayPresentationProfile.Presentation);
            PlayTrack = !PlayTrack;
            if (PlayTrack) Director = false;
            EditStatus = $"Camera track: {(PlayTrack ? "ON" : "OFF")}.";
        }
        public static void ToggleConstantSpeed()
        {
            TrackConstantSpeed = !TrackConstantSpeed;
            EditStatus = $"Constant speed: {(TrackConstantSpeed ? "ON" : "OFF")}.";
            ReplayController.NoteInput();
        }
        internal static void CycleInterpolation()
        {
            int count = Enum.GetValues<ReplayCameraInterpolation>().Length;
            TrackInterpolation = (ReplayCameraInterpolation)(((int)TrackInterpolation + 1) % count);
            EditStatus = $"Interpolation: {TrackInterpolation}. Add or update a keyframe to save it.";
            ReplayController.NoteInput();
        }
        internal static void CycleEase()
        {
            int count = Enum.GetValues<ReplayCameraEase>().Length;
            TrackEase = (ReplayCameraEase)(((int)TrackEase + 1) % count);
            EditStatus = $"Easing: {TrackEase}. Add or update a keyframe to save it.";
            ReplayController.NoteInput();
        }
        internal static void ClearBookmarks()
        {
            Track.Clear(); _trackPath = null; _bookmarkIndex = 0;
            EditStatus = "";
            _copiedKeyframe = null; ClearSelection(); _restoreKey = null; _updateSelected = false;
            Profile = ReplayPresentationProfile.Faithful; PlayTrack = false; LookAtSlot = -1;
        }
        internal static void EnsureTrack()
        {
            string? playbackPath = DemoPlayback.PlaybackPath;
            string? path = DemoPlayback.LogicalPath;
            if (path == null || ReplayPathComparer.Comparer.Equals(path, _trackPath)) return;
            _trackPath = path;
            ClearSelection();
            // New sidecars belong to the durable replay identity. Read an old
            // cache-owned v1/v2 sidecar when no logical sidecar exists, then
            // save the next edit beside the logical replay.
            Track.Load(path);
            if (!System.IO.File.Exists(path + ".camera") && playbackPath != null
                && !String.Equals(path, playbackPath, StringComparison.Ordinal))
            {
                if (Track.Load(playbackPath) && System.IO.File.Exists(playbackPath + ".camera"))
                    Track.Save(path); // migrate before disposable cache cleanup can remove authored work
            }
            _bookmarkIndex = 0;
        }
        private static bool EditTrack(Func<ReplayCameraTrack, bool> edit)
        {
            EnsureTrack();
            return _trackPath != null && Track.EditAndSave(_trackPath, edit);
        }
        internal static void SaveKeyframe(Vector3 position, Vector3 facing, float fov)
        {
            bool saved = EditTrack(track => track.Put(new ReplayCameraKeyframe(
                _updateSelected ? EditFrame : ReplayController.CurrentFrame, position, ReplayCameraTrack.FacingRotation(facing),
                fov, (sbyte)Math.Clamp(LookAtSlot, -1, 7),
                MathHelper.DegreesToRadians(Math.Clamp(Roll, -360, 360)), TrackInterpolation, TrackEase)));
            if (saved) SetSingleSelection(_updateSelected ? EditFrame : ReplayController.CurrentFrame);
            _updateSelected = false;
            EditStatus = saved ? "Camera keyframe saved." : "Camera track: " + (Track.LastError ?? "No replay is open.");
            Chat.ChatBox.System(EditStatus);
        }
        internal static bool DuplicateKeyframeAtCurrentFrame()
        {
            EnsureTrack();
            uint frame = ReplayController.CurrentFrame;
            var source = Track.Keys.Where(key => key.Frame < frame)
                .Select(key => (ReplayCameraKeyframe?)key).LastOrDefault();
            return source is { } key && !Track.Keys.Any(other => other.Frame == frame)
                && EditTrack(track => track.Put(key with { Frame = frame }));
        }
        internal static bool CopyKeyframeAtCurrentFrame()
        {
            EnsureTrack();
            if (_selectedFrames.Count > 1) return false;
            _copiedKeyframe = Track.Keys.Where(key => key.Frame == EditFrame)
                .Select(key => (ReplayCameraKeyframe?)key).FirstOrDefault();
            return _copiedKeyframe.HasValue;
        }
        internal static bool PasteKeyframeAtCurrentFrame()
            => _copiedKeyframe is { } key
                && EditTrack(track => track.Put(key with { Frame = ReplayController.CurrentFrame }));

        internal static bool SnapCurrentKeyframe(Func<ReplayEvent, bool> predicate)
        {
            uint frame = ReplayController.CurrentFrame;
            var target = DemoPlayback.Events.Where(predicate)
                .OrderBy(e => Math.Abs((long)e.Frame - frame)).Select(e => (ReplayEvent?)e)
                .FirstOrDefault();
            return target is { } marker && SnapCurrentKeyframe(marker.Frame);
        }
        internal static bool SnapCurrentKeyframe(uint frame)
            => _selectedFrames.Count <= 1 && MoveKeyframeTo(EditFrame, frame);

        private static bool MoveKeyframeTo(uint from, uint to)
        {
            EnsureTrack();
            if (to > ReplayController.DurationFrames || Track.Keys.Any(key => key.Frame == to)) return false;
            var key = Track.Keys.Where(key => key.Frame == from)
                .Select(key => (ReplayCameraKeyframe?)key).FirstOrDefault();
            bool moved = key is { } current && EditTrack(track =>
                track.Remove(from) && track.Put(current with { Frame = to }));
            if (moved)
            {
                if (_selectedFrames.Remove(from)) _selectedFrames.Add(to);
                if (SelectedFrame == from) SelectedFrame = to;
                if (_selectionAnchor == from) _selectionAnchor = to;
            }
            return moved;
        }
        internal static void MoveKeyframe(uint from, uint to) => MoveKeyframeTo(from, to);
        public static void RemoveKeyframe()
        {
            EnsureTrack();
            uint[] frames = _selectedFrames.Count > 0
                ? _selectedFrames.ToArray()
                : Track.Keys.Where(key => key.Frame == ReplayController.CurrentFrame)
                    .Select(key => key.Frame).ToArray();
            bool saved = frames.Length > 0 && EditTrack(track => track.RemoveMany(frames));
            if (saved)
            {
                if (_restoreKey is { } restore && frames.Contains(restore.Frame)) _restoreKey = null;
                ClearSelection();
            }
            EditStatus = saved
                ? frames.Length == 1 ? "Camera keyframe removed." : $"{frames.Length} camera keyframes removed."
                : "Camera track: " + (Track.LastError ?? "Select one or more keyframes to remove.");
            Chat.ChatBox.System(EditStatus);
        }
        internal static bool NextKeyframe(out ReplayCameraKeyframe key)
        {
            EnsureTrack(); key = default;
            if (_restoreKey is { } selected) { key = selected; _restoreKey = null; return true; }
            if (Track.Keys.Count == 0) return false;
            key = Track.Keys[_bookmarkIndex++ % Track.Keys.Count];
            return true;
        }
        internal static bool Changed;
        internal static bool BookmarkRequested;
        internal static bool RestoreRequested;
        public static void SetMode(ReplayCameraMode mode)
        {
            PlayTrack = false;
            Mode = mode;
            if (mode != ReplayCameraMode.FirstPerson) Profile = ReplayPresentationProfile.Presentation;
            Changed = true;
            ReplayController.NoteInput();
        }
        internal static void SetDirectorMode(ReplayCameraMode mode)
        {
            if (PlayTrack || Mode == mode) return;
            Mode = mode;
            Changed = true;
        }
        public static void ToggleFree() => SetMode(Mode == ReplayCameraMode.Free ? ReplayCameraMode.FirstPerson : ReplayCameraMode.Free);
        public static void Bookmark() { BookmarkRequested = true; ReplayController.NoteInput(); }
        public static void RestoreBookmark() { if (NextKeyframe(out var key)) SelectKeyframe(key.Frame); ReplayController.NoteInput(); }
        internal static void Reset()
        {
            Mode = ReplayCameraMode.FirstPerson;
            Changed = true;
            BookmarkRequested = false;
            RestoreRequested = false;
            ReplayDirector.Reset();
        }
        internal static void TickDirector(Scene scene)
        {
            ReplayDirector.Tick(scene);
        }
        public static void WatchEvent(bool victim)
        {
            ReplayEvent? nearest = null;
            foreach (ReplayEvent e in DemoPlayback.Events)
                if (e.Type == ReplayEventType.Kill && e.Frame <= ReplayController.CurrentFrame) nearest = e;
            if (nearest is ReplayEvent selected) SpectatorMode.Watch(victim ? selected.TargetSlot : selected.ActorSlot);
        }
    }
}

namespace MphRead
{
    public partial class Scene
    {
        private double _previousReplayPresentationFrame = double.NaN;
        private long _replaySeekGeneration = -1;
        private int _replayCameraSubject = -1;
        private float _replayOrbit;
        private Vector3? _replayFollowPosition;
        private bool _replayCameraInitialized;
        private Mods.Replay.ReplayCameraMode _appliedReplayCameraMode;

        private double ReplayPresentationTime => double.IsFinite(ReplayPresentationFrame)
            ? ReplayPresentationFrame
            : Math.Max(0, Mods.Network.ReplayController.CurrentFrame - 1d + ReplayRenderAlpha);

        private void ModReplayCamera()
        {
            if (!Mods.Network.DemoPlayback.IsActive) return;
            Mods.Replay.ReplayCamera.EnsureTrack();
            Mods.Replay.ReplayCamera.TickDirector(this);
            var mode = Mods.Replay.ReplayCamera.Mode;
            double presentationFrame = ReplayPresentationTime;
            long seekGeneration = Mods.Network.DemoPlayback.Session.Transport.SeekGeneration;
            int subject = Mods.Replay.ReplayCamera.Director
                ? Mods.Replay.ReplayDirector.CurrentSlot : this.Players.Main.SlotIndex;
            bool resetHistory = Mods.Network.ReplayController.IsSeeking
                || presentationFrame < _previousReplayPresentationFrame
                || seekGeneration != _replaySeekGeneration
                || subject != _replayCameraSubject;
            if (resetHistory)
            {
                _replayFollowPosition = null;
                _previousReplayPresentationFrame = double.NaN;
            }
            float delta = double.IsNaN(_previousReplayPresentationFrame)
                || (Mods.Network.ReplayController.IsPaused && !Mods.Replay.ReplayVideoExporter.Rendering) ? 0
                : (float)Math.Max(0, (presentationFrame - _previousReplayPresentationFrame) / 60.0);
            _previousReplayPresentationFrame = presentationFrame;
            _replaySeekGeneration = seekGeneration;
            _replayCameraSubject = subject;
            if (!_replayCameraInitialized || mode != _appliedReplayCameraMode || Mods.Replay.ReplayCamera.Changed)
            {
                // A warming shell can consume the global change notification before
                // this replica first draws. Every scene must establish its own mode.
                SetFreeCamera(mode != Mods.Replay.ReplayCameraMode.FirstPerson);
                _replayCameraInitialized = true;
                _appliedReplayCameraMode = mode;
                Mods.Replay.ReplayCamera.Changed = false;
                _replayFollowPosition = null;
                delta = 0;
            }
            if (Mods.Replay.ReplayCamera.BookmarkRequested)
            {
                Mods.Replay.ReplayCamera.BookmarkRequested = false;
                // In first-person the scene's auxiliary facing may be stale; capture
                // the camera actually shown, without writing back into the hunter.
                Mods.Replay.ReplayCamera.SaveKeyframe(_freeCam ? _cameraPosition : this.Players.Main.CameraInfo.Position,
                    _freeCam ? _cameraFacing : this.Players.Main.CameraInfo.Facing,
                    _freeCam ? MathHelper.DegreesToRadians(Mods.Replay.ReplayCamera.FieldOfView) : MathHelper.DegreesToRadians(
                        Mods.RenderOptions.ScaleCameraFov(
                            this.Players.Main.CameraInfo.Fov > 0
                                ? this.Players.Main.CameraInfo.Fov
                                : Mods.RenderOptions.DefaultFov)));
            }
            if (Mods.Replay.ReplayCamera.RestoreRequested)
            {
                Mods.Replay.ReplayCamera.RestoreRequested = false;
                if (Mods.Replay.ReplayCamera.NextKeyframe(out var saved))
                {
                    Mods.Replay.ReplayCamera.PlayTrack = false;
                    Mods.Replay.ReplayCamera.SetMode(Mods.Replay.ReplayCameraMode.Free);
                    ApplyReplayKeyframe(saved);
                    return;
                }
            }
            if (Mods.Replay.ReplayCamera.Profile == Mods.Replay.ReplayPresentationProfile.Presentation
                && Mods.Replay.ReplayCamera.PlayTrack
                && Mods.Replay.ReplayCamera.Track.Sample(
                    ReplayPresentationTime,
                    out var trackFrame,
                    Mods.Replay.ReplayCamera.TrackConstantSpeed))
            {
                ApplyReplayKeyframe(trackFrame);
                return;
            }
            if (mode == Mods.Replay.ReplayCameraMode.Free
                && Mods.Replay.ReplayCamera.Profile == Mods.Replay.ReplayPresentationProfile.Presentation)
            {
                _cameraFov = MathHelper.DegreesToRadians(Math.Clamp(
                    Mods.Replay.ReplayCamera.FieldOfView, 20, 140));
                Vector3 right = Vector3.Cross(_cameraFacing, Vector3.UnitY).Normalized();
                _cameraUp = Vector3.Transform(Vector3.Cross(right, _cameraFacing).Normalized(),
                    Quaternion.FromAxisAngle(_cameraFacing, MathHelper.DegreesToRadians(Mods.Replay.ReplayCamera.Roll)));
                _cameraRight = Vector3.Cross(_cameraFacing, _cameraUp).Normalized();
            }
            if (mode is not (Mods.Replay.ReplayCameraMode.Chase or Mods.Replay.ReplayCameraMode.Orbit)) return;
            var player = this.Players.Main;
            if (!player.LoadFlags.TestFlag(LoadFlags.Spawned)) return;
            SetFreeCamera(true);

            Vector3 playerPosition = Services.IsReplica ? player.ReplayDrawTransform.Row3.Xyz : player.Position;
            Vector3 facing = player.CameraInfo.Facing;
            if (ReplayPoses?.SamplePresented(player.SlotIndex, ReplayRenderAlpha, out _, out var replicaFacing) == true)
                facing = replicaFacing;

            Vector3 target = playerPosition
                + Vector3.UnitY * Math.Clamp(Mods.Replay.ReplayCamera.Height, 0.5f, 8);
            facing.Y = 0;
            if (facing.LengthSquared < 0.001f) facing = -Vector3.UnitZ;
            facing.Normalize();
            if (mode == Mods.Replay.ReplayCameraMode.Orbit)
            {
                _replayOrbit = (float)(ReplayPresentationTime / 60 * 0.4);
                facing = new Vector3(MathF.Sin(_replayOrbit), 0, MathF.Cos(_replayOrbit));
            }
            Vector3 desired = target - facing * Math.Clamp(Mods.Replay.ReplayCamera.Distance, 1, 20);
            Vector3 candidate = !Mods.Replay.ReplayVideoExporter.Rendering && Mods.Replay.ReplayCamera.Profile == Mods.Replay.ReplayPresentationProfile.Presentation && _replayFollowPosition.HasValue ? Vector3.Lerp(_replayFollowPosition.Value, desired, 1 - MathF.Exp(-delta * 10)) : desired;
            CollisionResult collision = default;
            if (CollisionDetection.CheckBetweenPoints(target, candidate, TestFlags.Players, this, ref collision))
                candidate = target + (candidate - target) * Math.Max(0, collision.Distance - 0.05f);
            _cameraPosition = candidate;
            _replayFollowPosition = candidate;
            Vector3 view = target - candidate;
            if (view.LengthSquared > 0.0001f) _cameraFacing = view.Normalized();
            _cameraRight = Vector3.Cross(_cameraFacing, Vector3.UnitY).Normalized();
            _cameraUp = Vector3.Cross(_cameraRight, _cameraFacing).Normalized();
            _cameraFov = MathHelper.DegreesToRadians(Math.Clamp(Mods.Replay.ReplayCamera.FieldOfView, 40, 120));
        }
        private void ApplyReplayKeyframe(Mods.Replay.ReplayCameraKeyframe key)
        {
            SetFreeCamera(true);
            Vector3 desired = key.Position;
            if (Mods.Replay.ReplayCamera.TrackCollisionAvoidance
                && Mods.Replay.ReplayCamera.Profile == Mods.Replay.ReplayPresentationProfile.Presentation)
            {
                CollisionResult pathCollision = default;
                Vector3 from = _cameraPosition;
                // A track's collision anchor is recorded, so the same target
                // frame cannot depend on the previously displayed/seeked frame.
                foreach (var anchor in Mods.Replay.ReplayCamera.Track.Keys)
                {
                    if (anchor.Frame > ReplayPresentationTime) break;
                    from = anchor.Position;
                }
                if ((desired - from).LengthSquared < 10000
                    && CollisionDetection.CheckBetweenPoints(from, desired, TestFlags.Players, this, ref pathCollision))
                {
                    desired = from + (desired - from) * Math.Max(0, pathCollision.Distance - 0.05f);
                }
            }
            _cameraPosition = desired;
            _cameraFacing = Vector3.Transform(-Vector3.UnitZ, key.Rotation).Normalized();
            _cameraUp = Vector3.Transform(Vector3.UnitY, key.Rotation).Normalized();
            if (Math.Abs(key.Roll) > 0.00001f)
                _cameraUp = Vector3.Transform(_cameraUp,
                    Quaternion.FromAxisAngle(_cameraFacing, key.Roll)).Normalized();
            if (key.LookAtSlot >= 0 && key.LookAtSlot < this.Players.Items.Count)
            {
                var target = this.Players.Items[key.LookAtSlot];
                if (target.LoadFlags.TestFlag(LoadFlags.Active) && target.LoadFlags.TestFlag(LoadFlags.Spawned))
                {
                    Vector3 direction = (Services.IsReplica ? target.ReplayDrawTransform.Row3.Xyz : target.Position) + Vector3.UnitY - _cameraPosition;
                    if (direction.LengthSquared > 0.0001f) _cameraFacing = direction.Normalized();
                }
            }
            Vector3 right = Vector3.Cross(_cameraFacing, _cameraUp);
            if (right.LengthSquared < 0.0001f)
                right = Vector3.Cross(_cameraFacing, Math.Abs(_cameraFacing.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ);
            _cameraRight = right.Normalized();
            _cameraUp = Vector3.Cross(_cameraRight, _cameraFacing).Normalized();
            _cameraFov = key.Fov;
        }
    }
}
