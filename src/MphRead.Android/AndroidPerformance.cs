using System;
using System.Diagnostics;
using Android.App;
using Activity = Android.App.Activity;
using Environment = System.Environment;
using Android.Content;
using Android.OS;
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
        private const long ThermalPollMs = 1000;

        private static readonly double[] _limiterMs = new double[SampleCapacity];
        private static readonly int[] _lastGcCounts = new int[3];
        private static readonly double[] _frameMs = new double[SampleCapacity];
        private static readonly double[] _simulationMs = new double[SampleCapacity];
        private static readonly double[] _renderMs = new double[SampleCapacity];
        private static readonly double[] _uiMs = new double[SampleCapacity];
        private static readonly double[] _swapMs = new double[SampleCapacity];
        private static readonly double[] _scratch = new double[SampleCapacity];
        private static float[] _supportedRefreshRates = new[] { 60f };

        private static WeakReference<Activity>? _activity;
        private static int _sampleIndex;
        private static int _sampleCount;
        private static long _lastReport;
        private static long _lastThermalPoll;
        private static long _allocatedBytes;
        private static int _thermalStatus;
        private static bool _matchActive;
        private static bool _sustainedPerformanceModeCleared;

        /// <summary>Highest refresh mode reported by the current Android display.</summary>
        public static float DisplayRefreshRate { get; private set; } = 60f;

        /// <summary>Currently active rate, which can differ from the requested/max mode.</summary>
        public static float ActiveDisplayRefreshRate { get; private set; } = 60f;

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

            int savedCap = FrameTiming.ParseCap(settings.FrameRateCap, FrameTiming.DisplayRate);
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
        public static void Attach(Activity activity)
        {
            _activity = new WeakReference<Activity>(activity);
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
                    return;
                }

                ActiveDisplayRefreshRate = Math.Clamp(display.RefreshRate,
                    30f, FrameTiming.MaxCap);
                if (!refreshModes) return;
                var modes = display.GetSupportedModes() ?? Array.Empty<Android.Views.Display.Mode>();
                var rates = new float[modes.Length + 1];
                rates[0] = ActiveDisplayRefreshRate;
                float best = ActiveDisplayRefreshRate;
                for (int i = 0; i < modes.Length; i++)
                {
                    float rate = Math.Clamp(modes[i].RefreshRate, 30f, FrameTiming.MaxCap);
                    rates[i + 1] = rate;
                    best = Math.Max(best, rate);
                }
                _supportedRefreshRates = rates;
                DisplayRefreshRate = Math.Clamp(best, 30f, FrameTiming.MaxCap);
            }
            catch (Exception ex)
            {
                DebugLog.Line("androidperf", $"display refresh probe failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Whether presentation can be paced by SurfaceFlinger instead of the
        /// managed sleep/spin limiter. Native refresh rates and caps at/above
        /// the panel maximum are display-paced; odd rates keep software pacing.
        /// </summary>
        public static bool UseDisplayPacing(int cap)
        {
            if (cap == FrameTiming.DisplayRate)
            {
                return true;
            }

            int requested = Math.Clamp(cap, FrameTiming.MinCap, FrameTiming.MaxCap);
            return AndroidFramePacer.MatchesNativeRefresh(
                requested, DisplayRefreshRate, _supportedRefreshRates);
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
                PollThermal(force: true);
            }
        }

        public static void SetMatchActive(bool active)
        {
            if (_matchActive == active)
            {
                return;
            }
            _matchActive = active;
            _sampleIndex = 0;
            _sampleCount = 0;
            _allocatedBytes = 0;
            for (int generation = 0; generation < 3; generation++)
            {
                _lastGcCounts[generation] = GC.CollectionCount(generation);
            }
            _lastReport = Environment.TickCount64;
            _lastThermalPoll = 0;

            DisableSustainedPerformanceMode();
            if (!active)
            {
                _thermalStatus = 0;
            }
            else
            {
                RefreshDisplayRate();
                PollThermal(force: true);
            }
        }

        public static void PrepareForWindow(int width, int height)
        {
            if (!_matchActive)
            {
                return;
            }

            // Diagnostic only. The requested scale and cap remain untouched.
            PollThermal(force: true);
        }

        /// <summary>
        /// Record one presented frame. All arrays are allocated once so this is
        /// safe to leave enabled in production builds.
        /// </summary>
        public static void RecordFrame(double elapsedSeconds, double limiterMs, double simulationMs,
            double renderMs, double uiMs, double swapMs, long allocatedBytes,
            int width, int height)
        {
            if (!_matchActive)
            {
                return;
            }

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

            PollThermal(force: false);

            long now = Environment.TickCount64;
            if (now - _lastReport >= ReportEveryMs)
            {
                _lastReport = now;
                Report(width, height);
            }
        }

        private static void PollThermal(bool force)
        private static void PollThermal(bool force)
        {
            long now = Environment.TickCount64;
            if (!force && now - _lastThermalPoll < ThermalPollMs)
            {
                return;
            }
            _lastThermalPoll = now;
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

        private static void Report(int width, int height)
        {
            int count = _sampleCount;
            if (count == 0)
            {
                return;
            }

            double p50 = Percentile(_frameMs, count, 0.50);
            double p90 = Percentile(_frameMs, count, 0.90);
            double p95 = Percentile(_frameMs, count, 0.95);
            double p99 = Percentile(_frameMs, count, 0.99);
            double sim95 = Percentile(_simulationMs, count, 0.95);
            double render95 = Percentile(_renderMs, count, 0.95);
            double ui95 = Percentile(_uiMs, count, 0.95);
            double swap95 = Percentile(_swapMs, count, 0.95);
            double limiter95 = Percentile(_limiterMs, count, 0.95);
            int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
            int over20 = 0, over33 = 0, over50 = 0;
            for (int i = 0; i < count; i++)
            {
                double ms = _frameMs[i];
                if (ms > 20) over20++;
                if (ms > 33.333) over33++;
                if (ms > 50) over50++;
            }

            DebugLog.Line("androidperf",
                $"frame ms p50/p90/p95/p99 {p50:0.00}/{p90:0.00}/{p95:0.00}/{p99:0.00}; "
                + $"stage p95 sim/render/ui/swap {sim95:0.00}/{render95:0.00}/{ui95:0.00}/{swap95:0.00}; "
                + $"limiter p95 {limiter95:0.00}; GC delta {gc0 - _lastGcCounts[0]}/{gc1 - _lastGcCounts[1]}/{gc2 - _lastGcCounts[2]}; "
                + $">20/33/50 {over20}/{over33}/{over50} of {count}; "
                + $"alloc {_allocatedBytes / 1024.0:0.0} KiB; "
                + $"{width}x{height} world {RenderOptions.ResolutionScale}% cap "
                + $"{(FrameTiming.FrameRateCap == FrameTiming.DisplayRate ? "display" : FrameTiming.FrameRateCap.ToString())} "
                + $"display active/max {ActiveDisplayRefreshRate:0.#}/{DisplayRefreshRate:0.#} Hz "
                + $"thermal {_thermalStatus} app-governor off");
            _lastGcCounts[0] = gc0; _lastGcCounts[1] = gc1; _lastGcCounts[2] = gc2;
            _allocatedBytes = 0;
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
