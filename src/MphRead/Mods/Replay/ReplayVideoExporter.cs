using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay
{
    /// <summary>
    /// Deterministic replay-frame export driven by the normal renderer. Jobs may
    /// contain one range or an ordered highlight reel. The exporter steps the
    /// replay exactly once per captured source frame and the queue starts the
    /// next job only after the current renderer has released ownership.
    /// </summary>
    internal enum ReplayExportState { Idle, Queued, Seeking, Rendering, Encoding, Completed, Failed, Cancelled }

    internal static class ReplayVideoExporter
    {
        private static ReplayVideoExportManifest? _job;
        private static ReplayVideoSegment[] _segments = Array.Empty<ReplayVideoSegment>();
        private static int _segmentIndex;
        private static ReplayExportSampler _sampler;
        private static long _sampleIndex;
        private static uint? _stepRequestedAt;
        private static int _written;
        private static int _totalFrames;
        private static string? _directory;
        private static ReplayCameraMode _previousMode;
        private static ReplayPresentationProfile _previousProfile;
        private static bool _previousDirector;
        private static bool _previousTrack;
        private static bool _cameraStateSaved;

        private static ReplayEncoderJob? _encoder;
        private static ReplayVideoExportManifest? _encodingJob;
        private static ReplayExportState _state;
        public static ReplayExportState State { get { PollEncoder(); return _state; } }
        public static bool Active { get { PollEncoder(); return _job != null || _encoder != null; } }
        public static bool Rendering => _job != null;
        public static string? LastError { get; private set; }
        public static float EncodeProgress => _encoder == null ? (_state == ReplayExportState.Completed ? 1 : 0)
            : Math.Clamp(_encoder.Frames / (float)Math.Max(1, _totalFrames), 0, 1);
        public static float RenderProgress => _totalFrames <= 0 ? 0 : Math.Clamp(_written / (float)_totalFrames, 0, 1);
        internal static float PresentationAlpha => Rendering && _sampleIndex < _sampler.Count
            && ReplayController.CurrentFrame == _sampler.At(_sampleIndex).SimulationFrame
                ? _sampler.At(_sampleIndex).Alpha : 1;
        internal static double PresentationFrame => Rendering && _sampleIndex < _sampler.Count
            && ReplayController.CurrentFrame == _sampler.At(_sampleIndex).SimulationFrame
                ? _sampler.At(_sampleIndex).Frame : ReplayController.CurrentFrame;
        internal static OpenTK.Mathematics.Vector2i? OutputSize => _job == null ? null : new(_job.Width, _job.Height);
        public static int FramesWritten => _written;
        public static float Progress => State == ReplayExportState.Encoding ? EncodeProgress : RenderProgress;
        private static string _status = "";
        public static string Status { get { PollEncoder(); return _encoder != null ? $"Encoding {EncodeProgress:P0}" : _status; } private set => _status = value; }
        public static string? LastOutput { get; private set; }

        public static bool Start(ReplayVideoExportManifest job)
        {
            if (!ReplayExportRates.IsSupported(job.Fps) || job.Width is < 64 or > 3840 || job.Height is < 64 or > 2160)
            {
                Status = "Unsupported export dimensions or frame rate."; return false;
            }
            if (Active)
            {
                Status = "A video export is already active.";
                return false;
            }
            if (!DemoPlayback.IsActive || DemoPlayback.PlaybackPath == null
                || !ReplayPathComparer.Same(DemoPlayback.PlaybackPath, job.Replay))
            {
                Status = "Open the replay that belongs to this render job first.";
                return false;
            }

            _segments = job.Segments is { Count: > 0 }
                ? job.Segments.ToArray()
                : new[]
                {
                    new ReplayVideoSegment(job.StartFrame, job.EndFrame,
                        "Selection", ReplaySegmentCamera.Current)
                };
            foreach (ReplayVideoSegment segment in _segments)
            {
                if (segment.StartFrame >= segment.EndFrame
                    || segment.EndFrame > ReplayController.DurationFrames)
                {
                    Status = "A render segment is outside this replay.";
                    _segments = Array.Empty<ReplayVideoSegment>();
                    return false;
                }
            }

            _previousMode = ReplayCamera.Mode;
            _previousProfile = ReplayCamera.Profile;
            _previousDirector = ReplayCamera.Director;
            _previousTrack = ReplayCamera.PlayTrack;
            _cameraStateSaved = true;

            _job = job;
            _directory = Path.GetDirectoryName(job.FramePattern)
                ?? Path.GetDirectoryName(job.SuggestedOutput);
            if (String.IsNullOrWhiteSpace(_directory))
            {
                _job = null;
                _segments = Array.Empty<ReplayVideoSegment>();
                RestoreCameraState();
                Status = "The render output directory is invalid.";
                return false;
            }

            try { Directory.CreateDirectory(_directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { Fail(ex.Message); return false; }
            _written = 0;
            _segmentIndex = 0;
            _sampleIndex = 0;
            _stepRequestedAt = null;
            _totalFrames = EstimateFrames(job.Fps, _segments);
            LastOutput = null; LastError = null; _state = ReplayExportState.Seeking;
            Status = "Seeking to render start...";
            _sampler = new(_segments[0].StartFrame, _segments[0].EndFrame, job.Fps);
            ApplySegmentCamera(_segments[0], job);
            ReplayController.Seek(_segments[0].StartFrame, resume: false);
            return true;
        }

        public static void Cancel()
        {
            _job = null;
            _segments = Array.Empty<ReplayVideoSegment>();
            _segmentIndex = 0;
            RestoreCameraState();
            _encoder?.Cancel();
            if (_encoder == null) _state = ReplayExportState.Cancelled;
            Status = "Video export cancelled.";
        }

        public static void AfterSceneDraw(Scene scene)
        {
            PollEncoder();
            CaptureReplayThumbnails(scene);

            if (_job == null)
            {
                ReplayExportQueue.Pump();
                return; // the framebuffer was drawn before the queued job took ownership
            }

            if (_job != null && !DemoPlayback.IsActive)
            {
                Cancel();
                return;
            }

            ReplayVideoExportManifest? job = _job;
            if (job == null || ReplayController.IsSeeking
                || _segmentIndex >= _segments.Length)
            {
                return;
            }

            _state = ReplayExportState.Rendering;
            ReplayVideoSegment segment = _segments[_segmentIndex];
            uint frame = ReplayController.CurrentFrame;
            if (ReplayController.State == ReplayState.Error)
            {
                Fail(DemoPlayback.LastError ?? "Replay stopped during export.");
                return;
            }
            ReplayExportSample sample = _sampler.At(_sampleIndex);
            if (frame < sample.SimulationFrame)
            {
                RequestStep(frame);
                return;
            }
            if (frame > sample.SimulationFrame)
            {
                Fail("Replay position changed during export. Restart the render job.");
                return;
            }
            // Never capture a framebuffer drawn for a previous sample or seek.
            if (!double.IsFinite(scene.ReplayPresentationFrame)
                || Math.Abs(scene.ReplayPresentationFrame - sample.Frame) > 1e-7)
                return;

            string path = Path.Combine(_directory!, $"frame_{_written:D8}.png");
            bool saved = job.CleanHud
                ? MphRead.Mods.ScreenCapture.Save(scene, path)
                : MphRead.Mods.ScreenCapture.SaveWindow(scene, path);
            if (!saved)
            {
                Fail($"Frame {sample.Frame:0.###} could not be captured; export stopped.");
                return;
            }
            _written++;
            _sampleIndex++;
            Status = $"Rendering {_segmentIndex + 1}/{_segments.Length} "
                + $"{segment.Name} · {ReplayHud.Time(frame)} / "
                + $"{ReplayHud.Time(segment.EndFrame)} · {_written}/{_totalFrames} frames";
            if (_sampleIndex == _sampler.Count)
            {
                AdvanceSegment();
                return;
            }
            if (_sampler.At(_sampleIndex).SimulationFrame > frame) RequestStep(frame);
        }

        private static void RequestStep(uint frame)
        {
            // A display may draw repeatedly before the next 60 Hz update.
            // Queue only one simulation step for each observed world frame.
            if (_stepRequestedAt == frame) return;
            _stepRequestedAt = frame;
            ReplayController.StepForward();
        }

        private static void AdvanceSegment()
        {
            ReplayVideoExportManifest? job = _job;
            if (job == null)
                return;

            _segmentIndex++;
            _sampleIndex = 0;
            _stepRequestedAt = null;
            if (_segmentIndex >= _segments.Length)
            {
                Finish();
                return;
            }

            ReplayVideoSegment next = _segments[_segmentIndex];
            _sampler = new(next.StartFrame, next.EndFrame, job.Fps);
            ApplySegmentCamera(next, job);
            Status = $"Seeking reel segment {_segmentIndex + 1}/{_segments.Length}...";
            ReplayController.Seek(next.StartFrame, resume: false);
        }

        private static void ApplySegmentCamera(ReplayVideoSegment segment,
            ReplayVideoExportManifest job)
        {
            ReplayCamera.Director = false;
            ReplayCamera.PlayTrack = false;

            switch (segment.Camera)
            {
                case ReplaySegmentCamera.FirstPerson:
                    ReplayCamera.SetProfile(ReplayPresentationProfile.Presentation);
                    ReplayCamera.SetMode(ReplayCameraMode.FirstPerson);
                    break;
                case ReplaySegmentCamera.Chase:
                    ReplayCamera.SetProfile(ReplayPresentationProfile.Presentation);
                    ReplayCamera.SetMode(ReplayCameraMode.Chase);
                    break;
                case ReplaySegmentCamera.Orbit:
                    ReplayCamera.SetProfile(ReplayPresentationProfile.Presentation);
                    ReplayCamera.SetMode(ReplayCameraMode.Orbit);
                    break;
                case ReplaySegmentCamera.Director:
                    ReplayCamera.SetProfile(ReplayPresentationProfile.Presentation);
                    ReplayCamera.Director = true;
                    break;
                default:
                    ReplayCamera.Director = job.Director;
                    if (job.CameraTrack)
                    {
                        ReplayCamera.SetProfile(ReplayPresentationProfile.Presentation);
                        ReplayCamera.PlayTrack = true;
                    }
                    break;
            }
        }

        private static int EstimateFrames(int fps,
            IReadOnlyList<ReplayVideoSegment> segments)
        {
            long total = 0;
            foreach (ReplayVideoSegment segment in segments)
                total += new ReplayExportSampler(segment.StartFrame, segment.EndFrame, fps).Count;
            return (int)Math.Min(Int32.MaxValue, total);
        }

        private static void Finish()
        {
            ReplayVideoExportManifest? job = _job;
            _job = null;
            _segments = Array.Empty<ReplayVideoSegment>();
            _segmentIndex = 0;
            if (job == null)
                return;

            RestoreCameraState();
            Status = $"Rendered {_written} frames.";
#if !ANDROID
            _state = ReplayExportState.Encoding;
            _encodingJob = job;
            _encoder = new ReplayEncoderJob("ffmpeg", "-progress pipe:1 -nostats " + job.FfmpegArguments, _directory ?? "");
#else
            _state = ReplayExportState.Completed;
            LastOutput = _directory;
            Status += " PNG sequence is ready for desktop encoding.";
            ReplayExportQueue.NoteFinished(Status);
#endif
        }

        private static void PollEncoder()
        {
            if (_encoder?.Completion.IsCompleted != true) return;
            var result = _encoder.Completion.GetAwaiter().GetResult();
            if (!result.Cancelled && result.Error == null && !File.Exists(_encodingJob?.SuggestedOutput))
                result = result with { Error = "FFmpeg exited without producing the requested video." };
            _encoder.Dispose(); _encoder = null;
            _state = result.Cancelled ? ReplayExportState.Cancelled
                : result.Error == null ? ReplayExportState.Completed : ReplayExportState.Failed;
            LastError = result.Error;
            if (_state == ReplayExportState.Failed) ReplayExportQueue.NoteFailure(_encodingJob, result.Error!);
            LastOutput = _state == ReplayExportState.Completed ? _encodingJob?.SuggestedOutput : null;
            _encodingJob = null;
            Status = _state == ReplayExportState.Completed ? "Video export complete."
                : result.Cancelled ? "Video export cancelled." : "Encoding failed. PNG sequence retained. " + result.Error;
            ReplayExportQueue.NoteFinished(_status);
        }
        private static void Fail(string message)
        {
            ReplayExportQueue.NoteFailure(_job, message);
            _job = null; _segments = Array.Empty<ReplayVideoSegment>();
            RestoreCameraState(); LastError = message; Status = message; _state = ReplayExportState.Failed;
            ReplayExportQueue.NoteFinished(message);
        }

        private static void RestoreCameraState()
        {
            if (!_cameraStateSaved)
                return;
            ReplayCamera.SetProfile(_previousProfile);
            ReplayCamera.SetMode(_previousMode);
            ReplayCamera.Director = _previousDirector;
            ReplayCamera.PlayTrack = _previousTrack;
            _cameraStateSaved = false;
        }

        // Three scene stills turn the replay library into a visual browser. They are
        // opportunistic: watching/exporting a replay populates them for free, while a
        // replay never opened still gets the map thumbnail as its immediate fallback.
        private static string? _thumbReplay;
        private static readonly bool[] _thumbDone = new bool[3];
        private static readonly System.Threading.Tasks.Task<bool>?[] _thumbJobs = new System.Threading.Tasks.Task<bool>?[3];

        private static void CaptureReplayThumbnails(Scene scene)
        {
            string? replay = DemoPlayback.LogicalPath;
            if (replay == null || ReplayController.IsSeeking)
                return;
            if (!ReplayPathComparer.Comparer.Equals(_thumbReplay, replay))
            {
                _thumbReplay = replay;
                for (int i = 0; i < _thumbDone.Length; i++)
                { _thumbJobs[i] = null; _thumbDone[i] = File.Exists(ThumbnailPath(replay, i)); }
            }

            uint duration = Math.Max(1u, ReplayController.DurationFrames);
            uint[] targets =
            {
                Math.Min(duration, Math.Max(60u, duration / 4)),
                Math.Min(duration, Math.Max(120u, duration / 2)),
                Math.Min(duration, Math.Max(180u, duration * 3 / 4))
            };
            uint frame = ReplayController.CurrentFrame;
            for (int i = 0; i < targets.Length; i++)
            {
                if (_thumbJobs[i]?.IsCompleted == true) { _thumbDone[i] = _thumbJobs[i]!.GetAwaiter().GetResult(); _thumbJobs[i] = null; }
                if (_thumbJobs[i] != null || _thumbDone[i] || frame < targets[i])
                    continue;
                _thumbJobs[i] = MphRead.Mods.ScreenCapture.QueueThumbnail(scene,
                    ThumbnailPath(replay, i));
            }
        }

        public static string ThumbnailPath(string replay, int index)
            => replay + $".thumb{Math.Clamp(index, 0, 2)}.png";

        public static string? BestThumbnail(string replay)
        {
            replay = ReplayVirtualClips.LogicalPath(replay);
            for (int i = 0; i < 3; i++)
            {
                string path = ThumbnailPath(replay, i);
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        public static string[] Thumbnails(string replay)
            => Enumerable.Range(0, 3).Select(i => ThumbnailPath(ReplayVirtualClips.LogicalPath(replay), i))
                .Where(File.Exists).ToArray();
    }
}
