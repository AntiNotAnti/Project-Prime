using System;
using System.Threading;
using System.Diagnostics;
using Android.App;
using Activity = Android.App.Activity;
using Environment = System.Environment;
using Android.Content;
using Android.OS;
using Android.Hardware.Display;
using MphRead.Mods;
using MphRead.Mods.Render;

namespace MphRead.Droid
{
    /// <summary>
    /// Android-only display pacing and diagnostics.
    ///
    /// The game simulation stays at 60 Hz. Android may still apply hardware/OS
    /// DVFS and thermal protection, but Project Prime does not impose a second
    /// runtime governor: this class never rewrites the player's FPS cap or render
    /// scale in response to load or thermal state. It only applies the one-time
    /// mobile startup default, negotiates display pacing, and records telemetry.
    /// </summary>
    internal static class AndroidPerformance
    {
        private const string BalancedProfile = "balanced-v2-display";
        private const string LegacyBalancedProfile = "balanced-v1";
        private const string CustomProfile = "custom-v1";
        private const int DefaultRenderScale = 90;
        private const int SampleCapacity = 600;
        private const long ReportEveryMs = 5000;

        private static readonly double[] _limiterMs = new double[SampleCapacity];
        private static readonly int[] _lastGcCounts = new int[3];
        private static readonly double[] _frameMs = new double[SampleCapacity];
        private static readonly double[] _simulationMs = new double[SampleCapacity];
        private static readonly double[] _renderMs = new double[SampleCapacity];
        private static readonly double[] _uiMs = new double[SampleCapacity];
        private static readonly double[] _swapMs = new double[SampleCapacity];
        private static readonly AndroidPresentedFrameClock _presentClock = new(Stopwatch.Frequency);
        public static void ResetPresentTiming() => _presentClock.Reset();
        private static readonly double[] _scratch = new double[SampleCapacity];
        // One detached report slot: a busy diagnostic worker never blocks presentation
        // or creates an unbounded queue. Producer copies only at the report boundary.
        private sealed class ReportSnapshot
        {
            internal readonly double[][] Samples = new double[6][];
            internal int Count, Width, Height, Cap, Scale, Generation, AppliedInterval;
            internal int Gc0, Gc1, Gc2, FailedPresents, LastPresentError, SkippedReports;
            internal long Allocated;
            internal double ManagedPause;
            internal ReportSnapshot() { for (int i = 0; i < Samples.Length; i++) Samples[i] = new double[SampleCapacity]; }
        }
        private static readonly ReportSnapshot _report = new();
        private static int _reportBusy, _skippedReports, _matchGeneration, _sampleGeneration;
        private static int _failedPresents, _lastPresentError;
        private static int _artGeneration = -1;
        private static readonly WaitCallback _reportWork = _ =>
        {
            try
            {
                PollThermal();
                if (_report.Generation == Volatile.Read(ref _matchGeneration)) Report(_report);
            }
            catch (Exception ex) { DebugLog.Line("androidperf", "telemetry report failed: " + ex.Message); }
            finally { Volatile.Write(ref _reportBusy, 0); }
        };

        private static WeakReference<Activity>? _activity;
        private static int _sampleIndex;
        private static int _sampleCount;
        private static long _lastReport;
        private static long _allocatedBytes;
        private static double _lastManagedGcPauseMs;
        private static long? _lastArtGcCount, _lastArtGcTimeMs;
        private static int _thermalStatus;
        private static volatile bool _matchActive;
        private static bool _sustainedPerformanceModeCleared;

        private static DisplayManager? _displayManager;
        private static readonly DisplayListener _displayListener = new();
        private static int _displayGeneration;
        private static int _displayId = -1;
        public static int DisplayGeneration => Volatile.Read(ref _displayGeneration);
        private sealed class DisplayListener : Java.Lang.Object, DisplayManager.IDisplayListener
        {
            public void OnDisplayAdded(int displayId) => RefreshDisplayRate();
            public void OnDisplayChanged(int displayId) => RefreshDisplayRate();
            public void OnDisplayRemoved(int displayId) => RefreshDisplayRate();
        }
        private static float _activeRefreshRate = 60f;
        private static bool _hasObservedDisplayRate;
        public static double ObservedDisplayRefreshRate => Volatile.Read(ref _hasObservedDisplayRate)
            ? ActiveDisplayRefreshRate : double.NaN;

        /// <summary>Highest refresh mode reported by the current Android display.</summary>
        public static float DisplayRefreshRate { get; private set; } = 60f;

        /// <summary>Currently active rate, which can differ from the requested/max mode.</summary>
        public static float ActiveDisplayRefreshRate => Volatile.Read(ref _activeRefreshRate);

        /// <summary>
        /// Give old Android installs a sustainable starting point once. A marker
        /// in settings.json prevents a user-selected configuration from being
        /// overwritten on later launches.
        /// </summary>
        public static bool ApplyStartupDefaults(MenuSettings settings)
        {
            bool baselineGraphics =
                String.Equals(settings.GraphicsPreset, "original", StringComparison.OrdinalIgnoreCase)
                && String.Equals(settings.AntiAliasing, "off", StringComparison.OrdinalIgnoreCase)
                && String.Equals(settings.ShadowQuality, "off", StringComparison.OrdinalIgnoreCase)
                && String.Equals(settings.AmbientOcclusion, "off", StringComparison.OrdinalIgnoreCase)
                && String.Equals(settings.Bloom, "off", StringComparison.OrdinalIgnoreCase)
                && String.Equals(settings.EnhancedLighting, "off", StringComparison.OrdinalIgnoreCase)
                && String.Equals(settings.DeferredPbr, "off", StringComparison.OrdinalIgnoreCase)
                && String.Equals(settings.Reflections, "off", StringComparison.OrdinalIgnoreCase)
                && String.Equals(settings.VolumetricFog, "off", StringComparison.OrdinalIgnoreCase)
                && String.Equals(settings.TextureUpscale, "off", StringComparison.OrdinalIgnoreCase)
                && String.Equals(settings.TextureQuality, "automatic", StringComparison.OrdinalIgnoreCase);

            int savedCap = FrameTiming.ParseSavedCap(settings.FrameRateCap, FrameTiming.DisplayRate);
            int savedScale = RenderOptions.ParseScale(settings.ResolutionScale, 100);
            bool legacyBalanced = String.Equals(settings.AndroidPerformanceProfile,
                    LegacyBalancedProfile, StringComparison.OrdinalIgnoreCase)
                && savedCap == 60 && savedScale == DefaultRenderScale && baselineGraphics;

            // balanced-v1 was written by Project Prime itself and forced an
            // otherwise untouched 120 Hz phone to a numeric 60 FPS cap. Migrate
            // only that exact generated baseline; customized installs stay put.
            if (!String.IsNullOrWhiteSpace(settings.AndroidPerformanceProfile)
                && !legacyBalanced)
            {
                return false;
            }

            bool untouched = savedCap == FrameTiming.DisplayRate
                && savedScale == 100 && baselineGraphics;

            settings.AndroidPerformanceProfile =
                untouched || legacyBalanced ? BalancedProfile : CustomProfile;
            if (untouched)
            {
                settings.FrameRateCap = "display";
                settings.ResolutionScale = DefaultRenderScale.ToString();
            }
            else if (legacyBalanced)
            {
                settings.FrameRateCap = "display";
            }

            try
            {
                GameState.CommitSettings(settings);
            }
            catch (Exception ex)
            {
                // The settings still apply for this run. Failing to persist an
                // optimisation must never block startup.
                DebugLog.Line("androidperf", $"could not persist Android performance profile: {ex.Message}");
            }

            if (untouched)
            {
                DebugLog.Line("androidperf",
                    $"applied mobile defaults: display-paced, {DefaultRenderScale}% base render scale");
            }
            else if (legacyBalanced)
            {
                DebugLog.Line("androidperf",
                    "migrated legacy 60 FPS Android default back to display pacing");
            }
            return untouched || legacyBalanced;
        }

        public static void Attach(Activity activity)
        {
            _activity = new WeakReference<Activity>(activity);
            try
            {
                _displayManager?.UnregisterDisplayListener(_displayListener);
                _displayManager = activity.GetSystemService(Context.DisplayService) as DisplayManager;
                _displayManager?.RegisterDisplayListener(_displayListener, new Handler(Looper.MainLooper!));
            }
            catch (Exception ex) { DebugLog.Line("androidperf", "display listener unavailable: " + ex.Message); }
            DisableSustainedPerformanceMode();
            RefreshDisplayRate();
            DebugLog.Line("androidperf",
                $"device {Build.Manufacturer} {Build.Model}, display max {DisplayRefreshRate:0.#} Hz");
        }

        public static void RefreshDisplayRate(bool refreshModes = true)
        {
            if (_activity == null || !_activity.TryGetTarget(out Activity? activity))
            {
                return;
            }

            try
            {
#pragma warning disable CS0618
                var display = activity.WindowManager?.DefaultDisplay;
#pragma warning restore CS0618
                if (display == null)
                {
                    Volatile.Write(ref _hasObservedDisplayRate, false);
                    return;
                }

                if (_displayId != display.DisplayId)
                {
                    _displayId = display.DisplayId;
                    Interlocked.Increment(ref _displayGeneration);
                }
                float activeRate = Math.Clamp(display.RefreshRate, 30f, FrameTiming.MaxCap);
                if (Math.Abs(activeRate - ActiveDisplayRefreshRate) > 0.1f)
                {
                    Volatile.Write(ref _activeRefreshRate, activeRate);
                    Interlocked.Increment(ref _displayGeneration);
                }
                Volatile.Write(ref _hasObservedDisplayRate, true);
                if (!refreshModes) return;
                var modes = display.GetSupportedModes() ?? Array.Empty<Android.Views.Display.Mode>();
                float best = ActiveDisplayRefreshRate;
                for (int i = 0; i < modes.Length; i++)
                {
                    float rate = Math.Clamp(modes[i].RefreshRate, 30f, FrameTiming.MaxCap);
                    best = Math.Max(best, rate);
                }
                float maximum = Math.Clamp(best, 30f, FrameTiming.MaxCap);
                if (Math.Abs(maximum - DisplayRefreshRate) > 0.1f)
                {
                    DisplayRefreshRate = maximum;
                    Interlocked.Increment(ref _displayGeneration);
                }
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _hasObservedDisplayRate, false);
                DebugLog.Line("androidperf", $"display refresh probe failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Whether presentation can be paced by SurfaceFlinger instead of the
        /// managed sleep/spin limiter. Only an observed active rate within the
        /// cap can enforce its upper bound; supported modes alone prove nothing.
        /// </summary>
        public static bool UseDisplayPacing(int cap)
        {
            if (cap == FrameTiming.DisplayRate)
            {
                return true;
            }
            if (cap == FrameTiming.Unlimited)
            {
                return false;
            }

            return AndroidFramePacer.PresentationOwnsCadence(cap, ObservedDisplayRefreshRate, 1);
        }

        public static void SetForeground(bool foreground)
        {
            if (!_matchActive)
            {
                return;
            }

            DisableSustainedPerformanceMode();
            if (foreground)
            {
                RefreshDisplayRate();
                PollThermal();
            }
        }

        public static void SetMatchActive(bool active)
        {
            if (_matchActive == active)
            {
                return;
            }
            _matchActive = active;
            Interlocked.Increment(ref _matchGeneration);
            DisableSustainedPerformanceMode();
            if (!active)
            {
                _thermalStatus = 0;
            }
            else
            {
                RefreshDisplayRate();
                PollThermal();
            }
        }

        public static void PrepareForWindow(int width, int height)
        {
            if (!_matchActive)
            {
                return;
            }

            // The next detached report refreshes diagnostic services. No Binder
            // calls belong in this render-owner preparation boundary.
        }

        /// <summary>
        /// Record one presented frame. All arrays are allocated once so this is
        /// safe to leave enabled in production builds.
        /// </summary>
        public static void RecordFrame(long completedPresentTicks, double limiterMs, double simulationMs,
            double renderMs, double uiMs, double swapMs, long allocatedBytes,
            int width, int height, int appliedInterval = -1)
        {
            if (!_matchActive)
            {
                return;
            }

            int generation = Volatile.Read(ref _matchGeneration);
            if (_sampleGeneration != generation)
            {
                _sampleGeneration = generation;
                _sampleIndex = _sampleCount = 0;
                _allocatedBytes = 0;
                _lastReport = Environment.TickCount64;
                for (int i = 0; i < 3; i++) _lastGcCounts[i] = GC.CollectionCount(i);
                _lastManagedGcPauseMs = GC.GetTotalPauseDuration().TotalMilliseconds;
                _presentClock.Reset();
            }
            // The first successful completion is an anchor, never a fabricated
            // zero interval. Every stored interval encloses these current stages.
            if (!_presentClock.TryObserve(completedPresentTicks, out double elapsedSeconds)) return;
            double frameMs = Math.Max(0, elapsedSeconds * 1000.0);
            int index = _sampleIndex;
            _frameMs[index] = frameMs;
            _limiterMs[index] = limiterMs;
            _simulationMs[index] = simulationMs;
            _renderMs[index] = renderMs;
            _uiMs[index] = uiMs;
            _swapMs[index] = swapMs;
            _allocatedBytes += Math.Max(0, allocatedBytes);
            _sampleIndex = (index + 1) % SampleCapacity;
            _sampleCount = Math.Min(_sampleCount + 1, SampleCapacity);

            long now = Environment.TickCount64;
            if (now - _lastReport >= ReportEveryMs)
            {
                _lastReport = now;
                if (Interlocked.CompareExchange(ref _reportBusy, 1, 0) != 0)
                {
                    _skippedReports++;
                    return;
                }
                ReportSnapshot report = _report;
                report.Count = _sampleCount; report.Width = width; report.Height = height;
                report.Cap = FrameTiming.FrameRateCap; report.Scale = RenderOptions.ResolutionScale;
                report.Generation = generation; report.AppliedInterval = appliedInterval;
                report.Allocated = _allocatedBytes;
                report.FailedPresents = Volatile.Read(ref _failedPresents);
                report.LastPresentError = Volatile.Read(ref _lastPresentError);
                report.SkippedReports = _skippedReports; _skippedReports = 0;
                double[][] samples = report.Samples;
                Array.Copy(_frameMs, samples[0], _sampleCount);
                Array.Copy(_limiterMs, samples[1], _sampleCount);
                Array.Copy(_simulationMs, samples[2], _sampleCount);
                Array.Copy(_renderMs, samples[3], _sampleCount);
                Array.Copy(_uiMs, samples[4], _sampleCount);
                Array.Copy(_swapMs, samples[5], _sampleCount);
                int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
                report.Gc0 = gc0 - _lastGcCounts[0];
                report.Gc1 = gc1 - _lastGcCounts[1];
                report.Gc2 = gc2 - _lastGcCounts[2];
                _lastGcCounts[0] = gc0; _lastGcCounts[1] = gc1; _lastGcCounts[2] = gc2;
                double pauseNow = GC.GetTotalPauseDuration().TotalMilliseconds;
                report.ManagedPause = Math.Max(0, pauseNow - _lastManagedGcPauseMs);
                _lastManagedGcPauseMs = pauseNow;
                _allocatedBytes = 0;
                try
                {
                    if (!ThreadPool.QueueUserWorkItem(_reportWork))
                    {
                        Volatile.Write(ref _reportBusy, 0);
                        _skippedReports++;
                    }
                }
                catch { Volatile.Write(ref _reportBusy, 0); _skippedReports++; }
            }
        }

        public static void RecordFailedPresent(int error)
        {
            Interlocked.Increment(ref _failedPresents);
            Volatile.Write(ref _lastPresentError, error);
        }

        private static void PollThermal()
        {
            // Surface.SetFrameRate is a request. Battery policy/thermal state
            // can leave the panel at a lower rate or switch it during a match.
            RefreshDisplayRate(refreshModes: false);

            int status = ReadThermalStatus();
            if (status == _thermalStatus)
            {
                return;
            }

            int before = _thermalStatus;
            _thermalStatus = status;
            // Observation only. Android can thermally downclock the hardware,
            // but the game no longer compounds that by changing FPS or scale.
            DebugLog.Line("androidperf", $"thermal status {before} -> {status} (diagnostic only)");
        }

        private static int ReadThermalStatus()
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(29)
                || _activity == null || !_activity.TryGetTarget(out Activity? activity))
            {
                return 0;
            }

            try
            {
                if (activity.GetSystemService(Context.PowerService) is PowerManager manager)
                {
                    return (int)manager.CurrentThermalStatus;
                }
            }
            catch
            {
                // Vendor power services have occasionally thrown while the
                // activity is changing state. Treat that sample as cool and
                // try again on the next poll.
            }
            return 0;
        }

        private static void DisableSustainedPerformanceMode()
        {
            if (_sustainedPerformanceModeCleared
                || !OperatingSystem.IsAndroidVersionAtLeast(24)
                || _activity == null || !_activity.TryGetTarget(out Activity? activity)
                || activity.Window == null)
            {
                return;
            }

            try
            {
                if (activity.GetSystemService(Context.PowerService) is not PowerManager manager
                    || !manager.IsSustainedPerformanceModeSupported)
                {
                    _sustainedPerformanceModeCleared = true;
                    return;
                }

                // Sustained-performance mode trades peak clocks for a lower,
                // steadier operating point. Leave normal Android DVFS available.
                activity.Window.SetSustainedPerformanceMode(false);
                _sustainedPerformanceModeCleared = true;
                DebugLog.Line("androidperf",
                    "sustained performance mode disabled; app-side runtime throttling is off");
            }
            catch (Exception ex)
            {
                DebugLog.Line("androidperf",
                    $"could not disable sustained performance mode: {ex.GetBaseException().Message}");
            }
        }

        // Binder/runtime-stat lookups are deliberately only performed at the 5s reporting boundary.
        private static long? ReadArtStat(string key)
        {
            try { return AndroidGcTelemetry.ParseCounter(Android.OS.Debug.GetRuntimeStat(key)); }
            catch (Exception) { return null; }
        }

        private static void Report(ReportSnapshot report)
        {
            int count = report.Count;
            if (count == 0)
            {
                return;
            }

            double p50 = Percentile(report.Samples[0], count, 0.50);
            double p90 = Percentile(report.Samples[0], count, 0.90);
            double p95 = Percentile(report.Samples[0], count, 0.95);
            double p99 = Percentile(report.Samples[0], count, 0.99);
            double sim95 = Percentile(report.Samples[2], count, 0.95);
            double render95 = Percentile(report.Samples[3], count, 0.95);
            double ui95 = Percentile(report.Samples[4], count, 0.95);
            double swap95 = Percentile(report.Samples[5], count, 0.95);
            double limiter95 = Percentile(report.Samples[1], count, 0.95);
            int over20 = 0, over33 = 0, over50 = 0;
            for (int i = 0; i < count; i++)
            {
                double ms = report.Samples[0][i];
                if (ms > 20) over20++;
                if (ms > 33.333) over33++;
                if (ms > 50) over50++;
            }

            if (_artGeneration != report.Generation)
            {
                _lastArtGcCount = _lastArtGcTimeMs = null;
                _artGeneration = report.Generation;
            }
            long? artCountNow = ReadArtStat("art.gc.gc-count");
            long? artTimeNow = ReadArtStat("art.gc.gc-time");
            string artDelta = AndroidGcTelemetry.CounterDelta(artCountNow, _lastArtGcCount);
            string artTimeDelta = AndroidGcTelemetry.CounterDelta(artTimeNow, _lastArtGcTimeMs);
            _lastArtGcCount = artCountNow;
            _lastArtGcTimeMs = artTimeNow;

#if PROJECTPRIME_ANDROID_AUDIO_DIAGNOSTICS
            DebugLog.Line("androidaudio", MphRead.Mods.Sound.SfxMixer.ReadAndResetAudioCallbackMetrics());
#endif
            DebugLog.Line("androidperf",
                $"frame ms p50/p90/p95/p99 {p50:0.00}/{p90:0.00}/{p95:0.00}/{p99:0.00}; "
                + $"stage p95 sim/render/ui/swap {sim95:0.00}/{render95:0.00}/{ui95:0.00}/{swap95:0.00}; "
                + $"limiter p95 {limiter95:0.00}; GC delta {report.Gc0}/{report.Gc1}/{report.Gc2}; "
                 + $"GC pause {report.ManagedPause:0.00}ms ART gc {artDelta} time {artTimeDelta}ms; "
                + $">20/33/50 {over20}/{over33}/{over50} of {count}; "
                + $"alloc {report.Allocated / 1024.0:0.0} KiB; "
                + $"{report.Width}x{report.Height} world {report.Scale}% cap "
                + $"{FrameTiming.CapString(report.Cap)} "
                + $"display active/max {ActiveDisplayRefreshRate:0.#}/{DisplayRefreshRate:0.#} Hz "
                + $"thermal {_thermalStatus} app-governor off; applied swap interval {report.AppliedInterval}; "
                + $"telemetry v2 completion-interval; failed presents total {report.FailedPresents} last 0x{report.LastPresentError:X}; skipped reports {report.SkippedReports}");
        }

        private static double Percentile(double[] source, int count, double percentile)
        {
            count = Math.Min(count, SampleCapacity);
            Array.Copy(source, _scratch, count);
            Array.Sort(_scratch, 0, count);
            int index = Math.Clamp((int)Math.Ceiling((count - 1) * percentile), 0, count - 1);
            return _scratch[index];
        }
    }
}
