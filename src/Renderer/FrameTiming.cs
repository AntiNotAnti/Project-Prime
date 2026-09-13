using System;
using MphRead.Mods.Network;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// The clock that lets the picture run faster than the game.
    ///
    /// Every timer in this engine is counted in frames, not in seconds: the
    /// DS ran its logic at 30 Hz and upstream doubled each interval and halved
    /// each increment to reach 60, which is what the 800-odd
    /// <c>// todo: FPS stuff</c> markers are. The simulation therefore cannot
    /// be asked to run at any other rate without rewriting all of them, and
    /// rewriting them would move the wire format too, since an intent is sent
    /// per frame and a replay is a count of frames.
    ///
    /// So the simulation is not asked. It stays pinned at exactly 60 Hz here
    /// and the *drawing* is what runs at the display's rate. A machine
    /// holding 144 fps runs the same 60 simulation steps a second it always
    /// did, sends the same packets on the same frames, and records a replay
    /// another build can play back. RenderAlpha exposes the fractional remainder to the client renderer,
    /// which may blend copied submission transforms between completed steps
    /// without changing the simulation state.
    ///
    /// One property is worth stating plainly because it is a change, and an
    /// improvement: the game's speed no longer depends on whether the machine
    /// can keep up. Update and render used to be one call, so a box managing
    /// 40 fps played the game in slow motion. The accumulator here runs the
    /// steps that are owed regardless of how long the frame took, up to
    /// <see cref="MaxCatchUpSteps"/>.
    /// </summary>
    public sealed class FrameTiming
    {
        /// <summary>The rate the simulation runs at, and the only one it can.</summary>
        public const int SimulationHz = MphRead.SimTicks.Hz;

        public const double StepSeconds = 1.0 / SimulationHz;

        /// <summary>
        /// The most simulation steps one drawn frame may run.
        ///
        /// Without a ceiling, a two-second stall -- a room finishing loading,
        /// a window being dragged, a debugger stopping the process -- comes
        /// back owing 120 steps, runs them all in one frame, takes longer than
        /// a frame doing it, and owes more than it did before. That is the
        /// spiral every fixed-timestep loop has to be told not to enter. The
        /// steps past the ceiling are dropped, which is exactly what the old
        /// single-rate loop did with every frame it missed.
        /// </summary>
        public const int MaxCatchUpSteps = 5;

        /// <summary>
        /// Anything longer than this is a stall, not a slow frame: the
        /// accumulator is reset rather than paid off.
        /// </summary>
        private const double StallSeconds = 0.25;

        /// <summary>
        /// Frames per second to draw at. 0 is <see cref="DisplayRate"/>: no
        /// cap of our own, VSync on, so the rate is whatever the monitor
        /// refreshes at. Any other value caps there with VSync off.
        /// </summary>
        public static int FrameRateCap
        {
            get => _frameRateCap;
            set => _frameRateCap = value <= 0 ? DisplayRate : Math.Clamp(value, MinCap, MaxCap);
        }

        private static int _frameRateCap = DisplayRate;

        public const int DisplayRate = 0;
        public const int MinCap = 30;

        /// <summary>
        /// OpenTK clamps a render frequency above 500 to 500; past that the
        /// number is decoration anyway, since the simulation is the thing that
        /// decides what the game does and it is not moving.
        /// </summary>
        public const int MaxCap = 500;

        /// <summary>
        /// True while the loop is running the picture at a rate of its own.
        /// The harness clients drive <c>Scene.OnUpdateFrame</c> one step per
        /// frame and never come through here, so their timing is untouched
        /// whatever this says.
        /// </summary>
        public bool Active { get; private set; }

        private double _accumulator;
        public float RenderAlpha => Active ? (float)Math.Clamp(_accumulator / StepSeconds, 0, 1) : 1;
        /// <summary>
        /// The wall-clock interval after the last fixed step that has not yet
        /// been represented by simulation. Render-side stateful input uses
        /// this remainder rather than measuring time from a second clock; the
        /// latter can include time spent outside the frame loop and over-predict
        /// a controller between ticks.
        /// </summary>
        public double SimulationRemainderSeconds
            => Active ? Math.Clamp(_accumulator, 0, StepSeconds) : 0;
        public long Discontinuities { get; private set; }

        /// <summary>Steps run for the frame <see cref="Advance"/> last answered.</summary>
        public int StepsThisFrame { get; private set; }

        #region diagnostics

        // What the debug log reports. Cheap enough to keep always on: the
        // question these answer -- "is the simulation actually still running
        // at 60 while the picture runs at 144" -- is the whole point of the
        // split, and it cannot be asked after the fact.
        public long TotalSteps { get; private set; }
        public long TotalFrames { get; private set; }
        public long DroppedSteps { get; private set; }
        public long Stalls { get; private set; }

        private const int RuntimeSampleCapacity = 256;
        private readonly BoundedPercentileSampler _inputFrameTimes
            = new(RuntimeSampleCapacity);
        private readonly BoundedPercentileSampler _simulationFrameTimes
            = new(RuntimeSampleCapacity);
        private readonly BoundedPercentileSampler _scenePreparationFrameTimes
            = new(RuntimeSampleCapacity);
        private readonly BoundedPercentileSampler _drawListBuildFrameTimes
            = new(RuntimeSampleCapacity);
        private readonly BoundedPercentileSampler _renderEncodeFrameTimes
            = new(RuntimeSampleCapacity);
        private readonly BoundedPercentileSampler _renderSubmitFrameTimes
            = new(RuntimeSampleCapacity);
        private readonly BoundedPercentileSampler _presentFrameTimes
            = new(RuntimeSampleCapacity);
        private readonly BoundedPercentileSampler _overlayUiFrameTimes
            = new(RuntimeSampleCapacity);
        private readonly BoundedPercentileSampler _afterFrameTimes
            = new(RuntimeSampleCapacity);
        private readonly BoundedPercentileSampler _wholeFrameTimes
            = new(RuntimeSampleCapacity);
        private readonly BoundedPercentileSampler _legacyTotalRenderFrameTimes
            = new(RuntimeSampleCapacity);
        private long _gcAllocatedBaseline;
        private long _gcAllocatedBytes;
        private double _gcElapsedSeconds;
        private int _gcGen0Baseline;
        private int _gcGen1Baseline;
        private int _gcGen2Baseline;
        private bool _gcBaselineInitialized;

        /// <summary>
        /// Bounded observations captured by the owning host's frame loop. The
        /// phase samples are CPU wall time around existing callbacks; they
        /// never alter scheduling or authority.
        /// </summary>
        public FrameTimingDiagnosticsSnapshot CaptureDiagnostics()
        {
            _inputFrameTimes.TrySnapshot(out BoundedPercentileSnapshot input);
            _simulationFrameTimes.TrySnapshot(out BoundedPercentileSnapshot simulation);
            _scenePreparationFrameTimes.TrySnapshot(out BoundedPercentileSnapshot scenePreparation);
            _drawListBuildFrameTimes.TrySnapshot(out BoundedPercentileSnapshot drawListBuild);
            _renderEncodeFrameTimes.TrySnapshot(out BoundedPercentileSnapshot renderEncode);
            _renderSubmitFrameTimes.TrySnapshot(out BoundedPercentileSnapshot renderSubmit);
            _presentFrameTimes.TrySnapshot(out BoundedPercentileSnapshot present);
            _overlayUiFrameTimes.TrySnapshot(out BoundedPercentileSnapshot overlayUi);
            _afterFrameTimes.TrySnapshot(out BoundedPercentileSnapshot afterFrame);
            _wholeFrameTimes.TrySnapshot(out BoundedPercentileSnapshot wholeFrame);
            _legacyTotalRenderFrameTimes.TrySnapshot(out BoundedPercentileSnapshot legacyTotalRender);
            double allocationRate = _gcElapsedSeconds > 0
                ? _gcAllocatedBytes / _gcElapsedSeconds : 0;
            return new FrameTimingDiagnosticsSnapshot(input, simulation, scenePreparation,
                drawListBuild, renderEncode, renderSubmit, present, overlayUi, afterFrame,
                wholeFrame, legacyTotalRender,
                TotalFrames, TotalSteps, DroppedSteps, Stalls, allocationRate,
                _gcBaselineInitialized ? Math.Max(0,
                    GC.CollectionCount(0) - _gcGen0Baseline) : 0,
                _gcBaselineInitialized ? Math.Max(0,
                    GC.CollectionCount(1) - _gcGen1Baseline) : 0,
                _gcBaselineInitialized ? Math.Max(0,
                    GC.CollectionCount(2) - _gcGen2Baseline) : 0);
        }

        /// <summary>
        /// Records one frame-loop observation. This is intentionally public so
        /// deterministic hosts and focused tests can feed the same core as a
        /// platform window without constructing a GPU surface.
        /// </summary>
        public void RecordRuntimeFrame(in FramePhaseTimingSample sample)
        {
            _inputFrameTimes.Record(sample.InputMilliseconds);
            _simulationFrameTimes.Record(sample.SimulationMilliseconds);
            _scenePreparationFrameTimes.Record(sample.ScenePreparationMilliseconds);
            _drawListBuildFrameTimes.Record(sample.DrawListBuildMilliseconds);
            _renderEncodeFrameTimes.Record(sample.RenderEncodeMilliseconds);
            _renderSubmitFrameTimes.Record(sample.RenderSubmitMilliseconds);
            _presentFrameTimes.Record(sample.PresentMilliseconds);
            _overlayUiFrameTimes.Record(sample.OverlayUiMilliseconds);
            _afterFrameTimes.Record(sample.AfterFrameMilliseconds);
            _wholeFrameTimes.Record(sample.WholeFrameMilliseconds);
            _legacyTotalRenderFrameTimes.Record(sample.LegacyTotalRenderMilliseconds);
            if (!Double.IsFinite(sample.ElapsedSeconds) || sample.ElapsedSeconds <= 0)
                return;

            long allocated = GC.GetTotalAllocatedBytes(false);
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);
            if (!_gcBaselineInitialized)
            {
                _gcAllocatedBaseline = allocated;
                _gcGen0Baseline = gen0;
                _gcGen1Baseline = gen1;
                _gcGen2Baseline = gen2;
                _gcBaselineInitialized = true;
                return;
            }
            if (allocated >= _gcAllocatedBaseline)
                _gcAllocatedBytes += allocated - _gcAllocatedBaseline;
            _gcAllocatedBaseline = allocated;
            _gcElapsedSeconds += sample.ElapsedSeconds;
        }

        /// <summary>
        /// How many frames ran 0, 1, 2, 3, 4 or 5+ simulation steps. A healthy
        /// 144 Hz run is mostly 0s and 1s in roughly 84/60 proportion; a run
        /// with 2s and 3s in it is a machine that is not keeping up.
        /// </summary>
        public readonly long[] StepHistogram = new long[MaxCatchUpSteps + 1];

        public double MeasuredSimulationHz { get; private set; }
        public double MeasuredFrameHz { get; private set; }

        private double _windowSeconds;
        private long _windowSteps;
        private long _windowFrames;

        public void ResetDiagnostics()
        {
            TotalSteps = 0;
            TotalFrames = 0;
            DroppedSteps = 0;
            Stalls = 0;
            Array.Clear(StepHistogram);
            MeasuredSimulationHz = 0;
            MeasuredFrameHz = 0;
            _windowSeconds = 0;
            _windowSteps = 0;
            _windowFrames = 0;
            _inputFrameTimes.Clear();
            _simulationFrameTimes.Clear();
            _scenePreparationFrameTimes.Clear();
            _drawListBuildFrameTimes.Clear();
            _renderEncodeFrameTimes.Clear();
            _renderSubmitFrameTimes.Clear();
            _presentFrameTimes.Clear();
            _overlayUiFrameTimes.Clear();
            _afterFrameTimes.Clear();
            _wholeFrameTimes.Clear();
            _legacyTotalRenderFrameTimes.Clear();
            _gcAllocatedBaseline = 0;
            _gcAllocatedBytes = 0;
            _gcElapsedSeconds = 0;
            _gcGen0Baseline = 0;
            _gcGen1Baseline = 0;
            _gcGen2Baseline = 0;
            _gcBaselineInitialized = false;
        }

        public string Describe()
        {
            return $"sim {MeasuredSimulationHz:0.00} Hz / draw {MeasuredFrameHz:0.0} Hz, "
                + $"{TotalSteps} steps over {TotalFrames} frames, "
                + $"{DroppedSteps} dropped, {Stalls} stalls, "
                + $"steps per frame [{string.Join(", ", StepHistogram)}], "
                + $"cap {(FrameRateCap == DisplayRate ? "display" : FrameRateCap.ToString())}";
        }

        #endregion

        public void Reset()
        {
            _accumulator = 0;
            Discontinuities++;
            StepsThisFrame = 0;
            Active = false;
        }

        /// <summary>
        /// Take the wall-clock time one drawn frame took and answer how many
        /// simulation steps are owed before it is drawn.
        /// </summary>
        public int Advance(double elapsedSeconds)
        {
            Active = true;
            TotalFrames++;
            if (elapsedSeconds > StallSeconds || elapsedSeconds < 0 || double.IsNaN(elapsedSeconds))
            {
                // A stall is not a debt. Run one step so the game does not
                // stop dead, and start the accumulator over.
                Stalls++;
                Discontinuities++;
                _accumulator = 0;
                StepsThisFrame = 1;
                TotalSteps++;
                StepHistogram[1]++;
                Tally(StepSeconds, 1);
                return 1;
            }
            _accumulator += elapsedSeconds;
            int steps = 0;
            while (_accumulator >= StepSeconds && steps < MaxCatchUpSteps)
            {
                _accumulator -= StepSeconds;
                steps++;
            }
            if (_accumulator >= StepSeconds)
            {
                // Past the ceiling: throw the rest away rather than owe it.
                int owed = (int)(_accumulator / StepSeconds);
                DroppedSteps += owed;
                Discontinuities++;
                _accumulator -= owed * StepSeconds;
            }
            StepsThisFrame = steps;
            TotalSteps += steps;
            StepHistogram[steps]++;
            Tally(elapsedSeconds, steps);
            return steps;
        }

        /// <summary>
        /// Mark one host-driven frame-advance presentation without allowing
        /// wall-clock debt to create additional fixed ticks. The host calls
        /// <see cref="Reset"/> immediately before this method.
        /// </summary>
        public int ManualStep()
        {
            Active = true;
            StepsThisFrame = 1;
            return 1;
        }

        private void Tally(double elapsedSeconds, int steps)
        {
            _windowSeconds += elapsedSeconds;
            _windowSteps += steps;
            _windowFrames++;
            // Two seconds, not one. The simulation is a discrete 60 Hz
            // process and the window boundary does not land on a step, so a
            // one-second window reads 60 or 61 steps and alternates between
            // 59.8 and 60.7 Hz -- 1.7% of quantisation, which is larger than
            // any drift worth reporting and made the log cry wolf every other
            // window. Two seconds halves it.
            if (_windowSeconds >= 2.0)
            {
                MeasuredSimulationHz = _windowSteps / _windowSeconds;
                MeasuredFrameHz = _windowFrames / _windowSeconds;
                _windowSeconds = 0;
                _windowSteps = 0;
                _windowFrames = 0;
                ReportWindow();
            }
        }

        private int _windowsSinceReport;
        private long _reportedDrops;
        private long _reportedStalls;

        /// <summary>
        /// What the debug log gets, and when.
        ///
        /// "It crashes when the map loads" is answered by a log of everything;
        /// "the game runs slightly fast on my machine" is answered by this and
        /// nothing else -- the simulation rate cannot be measured after the
        /// fact and a player cannot see it at all, since the counter on the
        /// HUD reports the picture. A quiet run says so every five seconds; a
        /// run that dropped a step or hit a stall says so the moment it does.
        /// </summary>
        private void ReportWindow()
        {
            bool trouble = DroppedSteps != _reportedDrops || Stalls != _reportedStalls
                || Math.Abs(MeasuredSimulationHz - SimulationHz) > SimulationHz * 0.02;
            _windowsSinceReport++;
            if (!trouble && _windowsSinceReport < 5) // ten seconds, at two each
            {
                return;
            }
            _windowsSinceReport = 0;
            _reportedDrops = DroppedSteps;
            _reportedStalls = Stalls;
            RendererLog.Line(trouble ? "frametiming!" : "frametiming", Describe());
        }

        public static int ParseCap(string? value, int fallback)
        {
            if (value == null)
            {
                return fallback;
            }
            string trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                return fallback;
            }
            if (trimmed.Equals("display", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("vsync", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase)
                || trimmed == "0")
            {
                return DisplayRate;
            }
            if (trimmed.Equals("uncapped", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("unlimited", StringComparison.OrdinalIgnoreCase))
            {
                return MaxCap;
            }
            if (Int32.TryParse(trimmed, out int parsed))
            {
                return parsed <= 0 ? DisplayRate : Math.Clamp(parsed, MinCap, MaxCap);
            }
            return fallback;
        }

        public static string CapString(int cap)
        {
            return cap == DisplayRate ? "display" : cap.ToString();
        }
    }

    public readonly record struct FramePhaseTimingSample(
        double InputMilliseconds,
        double SimulationMilliseconds,
        double ScenePreparationMilliseconds,
        double DrawListBuildMilliseconds,
        double RenderEncodeMilliseconds,
        double RenderSubmitMilliseconds,
        double PresentMilliseconds,
        double OverlayUiMilliseconds,
        double AfterFrameMilliseconds,
        double WholeFrameMilliseconds,
        double LegacyTotalRenderMilliseconds,
        double ElapsedSeconds);

    public readonly record struct FrameTimingDiagnosticsSnapshot(
        BoundedPercentileSnapshot Input,
        BoundedPercentileSnapshot Simulation,
        BoundedPercentileSnapshot ScenePreparation,
        BoundedPercentileSnapshot DrawListBuild,
        BoundedPercentileSnapshot RenderEncode,
        BoundedPercentileSnapshot RenderSubmit,
        BoundedPercentileSnapshot Present,
        BoundedPercentileSnapshot OverlayUi,
        BoundedPercentileSnapshot AfterFrame,
        BoundedPercentileSnapshot WholeFrame,
        BoundedPercentileSnapshot LegacyTotalRender,
        long TotalFrames, long TotalSteps, long DroppedSteps, long Stalls,
        double GcAllocatedBytesPerSecond, int Gen0Collections,
        int Gen1Collections, int Gen2Collections)
    {
        /// <summary>Compatibility alias for the original broad render window.</summary>
        public BoundedPercentileSnapshot Render => LegacyTotalRender;
    }
}
