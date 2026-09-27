using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Android.App;
using Android.Content;
using Android.OS;
using MphRead.Mods;
using MphRead.Mods.Render;

namespace MphRead.Droid
{
    /// <summary>
    /// Android-only performance policy and diagnostics.
    ///
    /// The game simulation stays at 60 Hz. This class only controls the picture:
    /// the Android default, dynamic world render scale, thermal fallback and
    /// low-overhead frame telemetry. HUD rendering remains at the native window
    /// size because <see cref="RenderOptions.ResolutionScale"/> only sizes the
    /// scene target.
    /// </summary>
    internal static class AndroidPerformance
    {
        private const string BalancedProfile = "balanced-v1";
        private const string CustomProfile = "custom-v1";
        private const int DefaultFrameRate = 60;
        private const int DefaultRenderScale = 90;
        private const int SampleCapacity = 600;
        private const long ReportEveryMs = 5000;
        private const long ThermalPollMs = 1000;
        private const long ScaleDownCooldownMs = 1500;
        private const long ScaleUpCooldownMs = 7000;

        private static readonly double[] _frameMs = new double[SampleCapacity];
        private static readonly double[] _simulationMs = new double[SampleCapacity];
        private static readonly double[] _renderMs = new double[SampleCapacity];
        private static readonly double[] _uiMs = new double[SampleCapacity];
        private static readonly double[] _swapMs = new double[SampleCapacity];
        private static readonly double[] _scratch = new double[SampleCapacity];

        private static WeakReference<Activity>? _activity;
        private static int _sampleIndex;
        private static int _sampleCount;
        private static long _lastReport;
        private static long _lastThermalPoll;
        private static long _lastScaleChange;
        private static long _allocatedBytes;
        private static int _thermalStatus;
        private static int _requestedScale = DefaultRenderScale;
        private static int _currentScale = DefaultRenderScale;
        private static int _requestedCap = DefaultFrameRate;
        private static int _badFrames;
        private static int _goodFrames;
        private static bool _matchActive;
        private static bool _adaptive;
        private static bool _pixelBudgetApplied;

        /// <summary>Highest refresh mode reported by the current Android display.</summary>
        public static float DisplayRefreshRate { get; private set; } = 60f;

        /// <summary>
        /// Give old Android installs a sustainable starting point once. A marker
        /// in settings.json prevents a user-selected configuration from being
        /// overwritten on later launches.
        /// </summary>
        public static bool ApplyStartupDefaults(MenuSettings settings)
        {
            if (!String.IsNullOrWhiteSpace(settings.AndroidPerformanceProfile))
            {
                return false;
            }

            bool untouched = FrameTiming.ParseCap(settings.FrameRateCap, FrameTiming.DisplayRate)
                    == FrameTiming.DisplayRate
                && RenderOptions.ParseScale(settings.ResolutionScale, 100) == 100;

            settings.AndroidPerformanceProfile = untouched ? BalancedProfile : CustomProfile;
            if (untouched)
            {
                settings.FrameRateCap = DefaultFrameRate.ToString();
                settings.ResolutionScale = DefaultRenderScale.ToString();
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
                    $"applied mobile defaults: {DefaultFrameRate} fps, {DefaultRenderScale}% base render scale");
            }
            return untouched;
        }

        /// <summary>Called by shared GameSettings after it applies the user's values.</summary>
        public static void NoteSettingsApplied(MenuSettings settings)
        {
            _requestedScale = RenderOptions.ParseScale(settings.ResolutionScale,
                RenderOptions.ResolutionScale);
            _requestedCap = FrameTiming.ParseCap(settings.FrameRateCap,
                FrameTiming.FrameRateCap);
            _adaptive = String.Equals(settings.AndroidPerformanceProfile,
                BalancedProfile, StringComparison.OrdinalIgnoreCase);
            _currentScale = _requestedScale;
            _pixelBudgetApplied = false;
            _badFrames = 0;
            _goodFrames = 0;

            ApplyThermalLimits(forceScale: true);
        }

        public static void Attach(Activity activity)
        {
            _activity = new WeakReference<Activity>(activity);
            RefreshDisplayRate();
            DebugLog.Line("androidperf",
                $"device {Build.Manufacturer} {Build.Model}, display max {DisplayRefreshRate:0.#} Hz");
        }

        public static void RefreshDisplayRate()
        {
            if (_activity == null || !_activity.TryGetTarget(out Activity? activity))
            {
                return;
            }

            try
            {
#pragma warning disable CS0618
                object? display = activity.WindowManager?.DefaultDisplay;
#pragma warning restore CS0618
                if (display == null)
                {
                    return;
                }

                float best = ReadFloatProperty(display, "RefreshRate", 60f);
                PropertyInfo? supportedModes = display.GetType().GetProperty("SupportedModes");
                if (supportedModes?.GetValue(display) is IEnumerable modes)
                {
                    foreach (object? mode in modes)
                    {
                        if (mode != null)
                        {
                            best = Math.Max(best, ReadFloatProperty(mode, "RefreshRate", best));
                        }
                    }
                }
                DisplayRefreshRate = Math.Clamp(best, 30f, FrameTiming.MaxCap);
            }
            catch (Exception ex)
            {
                DebugLog.Line("androidperf", $"display refresh probe failed: {ex.Message}");
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
            _badFrames = 0;
            _goodFrames = 0;
            _pixelBudgetApplied = false;
            _lastReport = Environment.TickCount64;
            _lastThermalPoll = 0;
            _lastScaleChange = 0;

            SetSustainedPerformanceMode(active);
            if (!active)
            {
                _thermalStatus = 0;
                FrameTiming.FrameRateCap = _requestedCap;
                RenderOptions.ResolutionScale = _requestedScale;
                _currentScale = _requestedScale;
            }
            else
            {
                RefreshDisplayRate();
                PollThermal(force: true);
                ApplyThermalLimits(forceScale: true);
            }
        }

        /// <summary>
        /// Record one presented frame. All arrays are allocated once so this is
        /// safe to leave enabled in production builds.
        /// </summary>
        public static void RecordFrame(double elapsedSeconds, double simulationMs,
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
            _simulationMs[index] = simulationMs;
            _renderMs[index] = renderMs;
            _uiMs[index] = uiMs;
            _swapMs[index] = swapMs;
            _allocatedBytes += Math.Max(0, allocatedBytes);
            _sampleIndex = (index + 1) % SampleCapacity;
            _sampleCount = Math.Min(_sampleCount + 1, SampleCapacity);

            PollThermal(force: false);
            ApplyPixelBudget(width, height);
            UpdateGovernor(frameMs, simulationMs + renderMs + uiMs + swapMs);

            long now = Environment.TickCount64;
            if (now - _lastReport >= ReportEveryMs)
            {
                _lastReport = now;
                Report(width, height);
            }
        }

        private static void ApplyPixelBudget(int width, int height)
        {
            if (_pixelBudgetApplied || !_adaptive || width <= 0 || height <= 0)
            {
                return;
            }
            _pixelBudgetApplied = true;

            double hz = EffectiveRefreshRate();
            double targetPixels = hz <= 45 ? 3_200_000
                : hz <= 60 ? 2_300_000
                : hz <= 90 ? 1_700_000
                : 1_300_000;
            double nativePixels = (double)width * height;
            int budgetScale = nativePixels <= targetPixels
                ? _requestedScale
                : (int)Math.Floor(Math.Sqrt(targetPixels / nativePixels) * 100.0);

            int minimum = Math.Min(_requestedScale, 60);
            int thermalMax = ThermalScaleCeiling();
            int wanted = Math.Clamp(Math.Min(_requestedScale, budgetScale),
                Math.Min(minimum, thermalMax), thermalMax);
            ApplyScale(wanted, "pixel budget");
        }

        private static void UpdateGovernor(double frameMs, double workMs)
        {
            if (!_adaptive)
            {
                return;
            }

            double budgetMs = 1000.0 / EffectiveRefreshRate();
            bool missed = frameMs > budgetMs * 1.12 || workMs > budgetMs * 0.96;
            bool comfortable = frameMs <= budgetMs * 1.08 && workMs < budgetMs * 0.72;

            if (missed)
            {
                _badFrames++;
                _goodFrames = 0;
            }
            else if (comfortable)
            {
                _goodFrames++;
                _badFrames = Math.Max(0, _badFrames - 1);
            }
            else
            {
                _badFrames = Math.Max(0, _badFrames - 1);
                _goodFrames = Math.Max(0, _goodFrames - 1);
            }

            long now = Environment.TickCount64;
            int minimum = Math.Min(_requestedScale, 60);
            int maximum = ThermalScaleCeiling();

            if (_currentScale > maximum)
            {
                ApplyScale(maximum, "thermal ceiling");
                return;
            }

            if (_badFrames >= 12 && now - _lastScaleChange >= ScaleDownCooldownMs
                && _currentScale > minimum)
            {
                _badFrames = 0;
                ApplyScale(Math.Max(minimum, _currentScale - 5), "missed frame budget");
            }
            else if (_goodFrames >= 300 && now - _lastScaleChange >= ScaleUpCooldownMs
                && _currentScale < maximum)
            {
                _goodFrames = 0;
                ApplyScale(Math.Min(maximum, _currentScale + 5), "sustained headroom");
            }
        }

        private static void ApplyScale(int scale, string reason)
        {
            scale = Math.Clamp(scale, RenderOptions.MinScale, RenderOptions.MaxScale);
            if (scale == _currentScale)
            {
                return;
            }

            int before = _currentScale;
            _currentScale = scale;
            RenderOptions.ResolutionScale = scale;
            _lastScaleChange = Environment.TickCount64;
            DebugLog.Line("androidperf", $"render scale {before}% -> {scale}% ({reason})");
        }

        private static void PollThermal(bool force)
        {
            long now = Environment.TickCount64;
            if (!force && now - _lastThermalPoll < ThermalPollMs)
            {
                return;
            }
            _lastThermalPoll = now;

            int status = ReadThermalStatus();
            if (status == _thermalStatus)
            {
                return;
            }

            int before = _thermalStatus;
            _thermalStatus = status;
            DebugLog.Line("androidperf", $"thermal status {before} -> {status}");
            ApplyThermalLimits(forceScale: true);
        }

        private static void ApplyThermalLimits(bool forceScale)
        {
            int effectiveCap = EffectiveCap();
            if (FrameTiming.FrameRateCap != effectiveCap)
            {
                DebugLog.Line("androidperf",
                    $"frame cap {FrameTiming.FrameRateCap} -> {effectiveCap} for thermal state {_thermalStatus}");
                FrameTiming.FrameRateCap = effectiveCap;
            }

            int ceiling = ThermalScaleCeiling();
            if (forceScale && _currentScale > ceiling)
            {
                ApplyScale(ceiling, "thermal pressure");
            }
        }

        private static int EffectiveCap()
        {
            // Java thermal states: NONE 0, LIGHT 1, MODERATE 2, SEVERE 3,
            // CRITICAL 4, EMERGENCY 5, SHUTDOWN 6.
            if (_thermalStatus >= 3)
            {
                if (_requestedCap == FrameTiming.DisplayRate)
                {
                    return 60;
                }
                return Math.Min(_requestedCap, 60);
            }
            return _requestedCap;
        }

        private static int ThermalScaleCeiling()
        {
            int reduction = _thermalStatus switch
            {
                >= 4 => 25,
                3 => 15,
                2 => 5,
                _ => 0
            };
            return Math.Clamp(_requestedScale - reduction,
                RenderOptions.MinScale, RenderOptions.MaxScale);
        }

        private static double EffectiveRefreshRate()
        {
            int cap = EffectiveCap();
            double hz = cap == FrameTiming.DisplayRate ? DisplayRefreshRate : cap;
            return Math.Clamp(hz, FrameTiming.MinCap, FrameTiming.MaxCap);
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
                object? manager = activity.GetSystemService(Context.PowerService);
                PropertyInfo? property = manager?.GetType().GetProperty("CurrentThermalStatus");
                object? value = property?.GetValue(manager);
                return value == null ? 0 : Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }

        private static void SetSustainedPerformanceMode(bool enabled)
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(24)
                || _activity == null || !_activity.TryGetTarget(out Activity? activity)
                || activity.Window == null)
            {
                return;
            }

            try
            {
                MethodInfo? method = activity.Window.GetType().GetMethod(
                    "SetSustainedPerformanceMode", new[] { typeof(bool) });
                method?.Invoke(activity.Window, new object[] { enabled });
                DebugLog.Line("androidperf",
                    $"sustained performance mode {(enabled ? "enabled" : "disabled")}");
            }
            catch (Exception ex)
            {
                DebugLog.Line("androidperf",
                    $"sustained performance mode unavailable: {ex.GetBaseException().Message}");
            }
        }

        private static float ReadFloatProperty(object value, string name, float fallback)
        {
            try
            {
                object? result = value.GetType().GetProperty(name)?.GetValue(value);
                return result == null ? fallback : Convert.ToSingle(result);
            }
            catch
            {
                return fallback;
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
                + $">20/33/50 {over20}/{over33}/{over50} of {count}; "
                + $"alloc {_allocatedBytes / 1024.0:0.0} KiB; "
                + $"{width}x{height} world {_currentScale}% cap "
                + $"{(FrameTiming.FrameRateCap == FrameTiming.DisplayRate ? "display" : FrameTiming.FrameRateCap.ToString())} "
                + $"display {DisplayRefreshRate:0.#} Hz thermal {_thermalStatus}");
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
