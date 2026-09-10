using System;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    public readonly record struct FixedStepLookObservation(
        LookDeviceKind Device, Vector2 PreAssistDelta, Vector2 PostAssistDelta,
        float PreAssistAngularErrorDegrees = float.NaN,
        float PostAssistAngularErrorDegrees = float.NaN);

    public readonly record struct InputBalanceSnapshot(LookDeviceKind Device,
        int Hunter, int Weapon, uint Shots, uint Hits, uint Damage, uint Kills,
        uint Deaths, double AverageAngularError, double AverageAcquisitionMilliseconds,
        uint AssistTargetAcquisitions, double AverageRotationalAssistDegrees,
        double AverageFrictionMultiplier, uint ZoomedShots, uint ZoomedHits,
        uint UnzoomedShots, uint UnzoomedHits)
    {
        /// <summary>Shots for which no valid nearest enemy was visible.</summary>
        public uint NoTargetShots { get; init; }
        /// <summary>Shots for which no retained assist target was valid.</summary>
        public uint NoRetainedAssistTargetShots { get; init; }
        /// <summary>Bounded nearest-enemy target-error histogram.</summary>
        public uint[] TargetErrorHistogram { get; init; } = Array.Empty<uint>();
        /// <summary>Bounded retained-target error histogram.</summary>
        public uint[] RetainedTargetErrorHistogram { get; init; } = Array.Empty<uint>();
        /// <summary>Bounded target-acquisition-time histogram.</summary>
        public uint[] AcquisitionHistogram { get; init; } = Array.Empty<uint>();
        /// <summary>Bounded rotational-assist histogram.</summary>
        public uint[] RotationHistogram { get; init; } = Array.Empty<uint>();
        /// <summary>
        /// Last fixed-step pre/post values associated with a shot in this
        /// bucket. These are copied values, never render-owned references.
        /// </summary>
        public bool HasFixedStepLook { get; init; }
        public Vector2 LastPreAssistDelta { get; init; }
        public Vector2 LastPostAssistDelta { get; init; }
        public double AveragePreAssistAngularError { get; init; }
        public double AveragePostAssistAngularError { get; init; }
        public uint[] PreAssistErrorHistogram { get; init; } = Array.Empty<uint>();
        public uint[] PostAssistErrorHistogram { get; init; } = Array.Empty<uint>();
        // Useful aliases for consumers that call the angular-error metric by
        // its older name.
        public uint[] AngularErrorHistogram => TargetErrorHistogram;
        public uint[] RotationalAssistHistogram => RotationHistogram;
    }

    /// <summary>
    /// Opt-in, process-local input tuning counters. No identity, session, or
    /// protocol value is accepted. Fixed arrays keep the recording hot path
    /// bounded and allocation-free.
    /// </summary>
    public static class InputBalanceTelemetry
    {
        private const int DeviceCount = 5;
        private const int HunterCount = 8;
        private const int WeaponCount = 11;
        private const int FiringContextCapacity = 128;

        // The final bin is a saturating overflow bin. Values are deliberately
        // coarse: these are tuning diagnostics, not an unbounded event log.
        public const int TargetErrorHistogramBinCount = 91;
        public const float TargetErrorHistogramBinWidthDegrees = 1f;
        public const int AcquisitionHistogramBinCount = 51;
        public const float AcquisitionHistogramBinWidthMilliseconds = 100f;
        public const int RotationHistogramBinCount = 61;
        public const float RotationHistogramBinWidthDegrees = 0.5f;

        private static readonly object Gate = new();
        private static readonly Bucket[] Buckets
            = new Bucket[DeviceCount * HunterCount * WeaponCount];
        private static readonly uint[] TargetErrorHistograms
            = new uint[Buckets.Length * TargetErrorHistogramBinCount];
        private static readonly uint[] RetainedTargetErrorHistograms
            = new uint[Buckets.Length * TargetErrorHistogramBinCount];
        private static readonly uint[] PreAssistErrorHistograms
            = new uint[Buckets.Length * TargetErrorHistogramBinCount];
        private static readonly uint[] PostAssistErrorHistograms
            = new uint[Buckets.Length * TargetErrorHistogramBinCount];
        private static readonly uint[] AcquisitionHistograms
            = new uint[Buckets.Length * AcquisitionHistogramBinCount];
        private static readonly uint[] RotationHistograms
            = new uint[Buckets.Length * RotationHistogramBinCount];
        private static readonly FiringContext[] FiringContexts
            = new FiringContext[FiringContextCapacity];
        private static int _firingContextCursor;
        private static LookDeviceKind _fixedStepDevice;
        private static Vector2 _fixedStepPreAssist;
        private static Vector2 _fixedStepPostAssist;
        private static float _fixedStepPreAssistError = float.NaN;
        private static float _fixedStepPostAssistError = float.NaN;
        private static bool _hasFixedStepLook;

        /// <summary>
        /// Device latched by the last fixed-step consume. It intentionally does
        /// not read the render-rate ownership tracker.
        /// </summary>
        public static LookDeviceKind FixedStepLookDevice
        {
            get { lock (Gate) return _fixedStepDevice; }
        }

        /// <summary>Begin a fixed-step telemetry boundary.</summary>
        public static void BeginFixedStepLook(LookDeviceKind device)
        {
            if (!InputSettings.InputBalanceTelemetryEnabled) return;
            lock (Gate)
            {
                _fixedStepDevice = device;
                _fixedStepPreAssist = Vector2.Zero;
                _fixedStepPostAssist = Vector2.Zero;
                _fixedStepPreAssistError = float.NaN;
                _fixedStepPostAssistError = float.NaN;
                _hasFixedStepLook = false;
            }
        }

        /// <summary>
        /// Latch pre/post-assist values produced by the consumed fixed-step
        /// frame. Render prediction never writes this state.
        /// </summary>
        public static void RecordFixedStepLook(LookDeviceKind device,
            Vector2 preAssistDelta, Vector2 postAssistDelta,
            float preAssistAngularErrorDegrees = float.NaN,
            float postAssistAngularErrorDegrees = float.NaN)
        {
            if (!InputSettings.InputBalanceTelemetryEnabled
                || !IsFinite(preAssistDelta) || !IsFinite(postAssistDelta)) return;
            lock (Gate)
            {
                _fixedStepDevice = device;
                _fixedStepPreAssist = preAssistDelta;
                _fixedStepPostAssist = postAssistDelta;
                _fixedStepPreAssistError = SanitizeAngularError(
                    preAssistAngularErrorDegrees);
                _fixedStepPostAssistError = SanitizeAngularError(
                    postAssistAngularErrorDegrees);
                _hasFixedStepLook = true;
            }
        }

        public static bool TryGetFixedStepLook(
            out FixedStepLookObservation observation)
        {
            lock (Gate)
            {
                observation = new FixedStepLookObservation(_fixedStepDevice,
                    _fixedStepPreAssist, _fixedStepPostAssist,
                    _fixedStepPreAssistError, _fixedStepPostAssistError);
                return _hasFixedStepLook;
            }
        }

        public static void RecordShot(LookDeviceKind device, int hunter, int weapon,
            bool zoomed, float angularErrorDegrees, uint? commandSequence = null)
        {
            bool hasTarget = float.IsFinite(angularErrorDegrees)
                && angularErrorDegrees >= 0;
            RecordShotCore(device, hunter, weapon, zoomed,
                hasTarget ? angularErrorDegrees : float.NaN,
                hasTarget, float.NaN, hasRetainedTarget: false, commandSequence);
        }

        /// <summary>
        /// Record a shot with fixed-step target measurements. The target flags
        /// keep no-target samples explicit while preserving the existing
        /// bounded command-sequence attribution ring.
        /// </summary>
        public static void RecordShot(LookDeviceKind device, int hunter, int weapon,
            bool zoomed, AimAssistTargetObservation targets,
            uint? commandSequence = null,
            FixedStepLookObservation? fixedStepLook = null)
        {
            bool hasNearest = targets.HasNearestEnemy
                && float.IsFinite(targets.NearestEnemyErrorDegrees)
                && targets.NearestEnemyErrorDegrees >= 0;
            bool hasRetained = targets.HasRetainedAssistTarget
                && float.IsFinite(targets.RetainedAssistTargetErrorDegrees)
                && targets.RetainedAssistTargetErrorDegrees >= 0;
            RecordShotCore(device, hunter, weapon, zoomed,
                hasNearest ? targets.NearestEnemyErrorDegrees : float.NaN,
                hasNearest,
                hasRetained ? targets.RetainedAssistTargetErrorDegrees : float.NaN,
                hasRetained, commandSequence, fixedStepLook);
        }

        private static void RecordShotCore(LookDeviceKind device, int hunter,
            int weapon, bool zoomed, float angularErrorDegrees, bool hasTarget,
            float retainedTargetErrorDegrees, bool hasRetainedTarget,
            uint? commandSequence,
            FixedStepLookObservation? fixedStepLook = null)
        {
            if (!InputSettings.InputBalanceTelemetryEnabled
                || !TryIndex(device, hunter, weapon, out int index)) return;
            lock (Gate)
            {
                ref Bucket bucket = ref Buckets[index];
                bucket.Shots = SaturatingIncrement(bucket.Shots);
                if (zoomed) bucket.ZoomedShots = SaturatingIncrement(bucket.ZoomedShots);
                else bucket.UnzoomedShots = SaturatingIncrement(bucket.UnzoomedShots);
                if (hasTarget)
                {
                    bucket.AngularErrorTotal += angularErrorDegrees;
                    bucket.AngularErrorSamples = SaturatingIncrement(
                        bucket.AngularErrorSamples);
                    IncrementHistogram(TargetErrorHistograms, index,
                        angularErrorDegrees, TargetErrorHistogramBinWidthDegrees,
                        TargetErrorHistogramBinCount);
                }
                else
                {
                    bucket.NoTargetShots = SaturatingIncrement(bucket.NoTargetShots);
                }
                if (hasRetainedTarget)
                {
                    IncrementHistogram(RetainedTargetErrorHistograms, index,
                        retainedTargetErrorDegrees,
                        TargetErrorHistogramBinWidthDegrees,
                        TargetErrorHistogramBinCount);
                }
                else
                {
                    bucket.NoRetainedAssistTargetShots = SaturatingIncrement(
                        bucket.NoRetainedAssistTargetShots);
                }
                if (fixedStepLook is { } look
                    && IsFinite(look.PreAssistDelta)
                    && IsFinite(look.PostAssistDelta))
                {
                    bucket.HasFixedStepLook = true;
                    bucket.LastPreAssistDelta = look.PreAssistDelta;
                    bucket.LastPostAssistDelta = look.PostAssistDelta;
                    if (look.Device == LookDeviceKind.GamepadStick)
                    {
                        RecordAssistErrorLocked(ref bucket, index,
                            look.PreAssistAngularErrorDegrees,
                            preAssist: true);
                        RecordAssistErrorLocked(ref bucket, index,
                            look.PostAssistAngularErrorDegrees,
                            preAssist: false);
                    }
                }
                StoreFiringContextLocked(index, weapon, zoomed, commandSequence);
            }
        }

        public static void RecordHit(LookDeviceKind device, int hunter, int weapon,
            bool zoomed, uint damage)
        {
            if (!InputSettings.InputBalanceTelemetryEnabled
                || !TryIndex(device, hunter, weapon, out int index)) return;
            lock (Gate)
            {
                ref Bucket bucket = ref Buckets[index];
                bucket.Hits = SaturatingIncrement(bucket.Hits);
                bucket.Damage = SaturatingAdd(bucket.Damage, damage);
                if (zoomed) bucket.ZoomedHits = SaturatingIncrement(bucket.ZoomedHits);
                else bucket.UnzoomedHits = SaturatingIncrement(bucket.UnzoomedHits);
            }
        }

        public static bool RecordAttributedHit(uint commandSequence,
            byte weapon, uint damage)
        {
            if (!InputSettings.InputBalanceTelemetryEnabled) return false;
            lock (Gate)
            {
                for (int offset = 1; offset <= FiringContextCapacity; offset++)
                {
                    int index = (_firingContextCursor - offset + FiringContextCapacity)
                        % FiringContextCapacity;
                    FiringContext context = FiringContexts[index];
                    if (!context.Used || context.CommandSequence != commandSequence
                        || context.Weapon != weapon) continue;
                    ref Bucket bucket = ref Buckets[context.BucketIndex];
                    bucket.Hits = SaturatingIncrement(bucket.Hits);
                    bucket.Damage = SaturatingAdd(bucket.Damage, damage);
                    if (context.Zoomed)
                        bucket.ZoomedHits = SaturatingIncrement(bucket.ZoomedHits);
                    else bucket.UnzoomedHits = SaturatingIncrement(bucket.UnzoomedHits);
                    return true;
                }
                return false;
            }
        }

        public static void RecordKill(LookDeviceKind device, int hunter, int weapon)
            => Increment(device, hunter, weapon, kill: true);

        public static void RecordDeath(LookDeviceKind device, int hunter, int weapon)
            => Increment(device, hunter, weapon, kill: false);

        public static void RecordAssist(LookDeviceKind device, int hunter, int weapon,
            float acquisitionMilliseconds, float rotationalDegrees, float frictionMultiplier,
            bool acquiredTarget)
        {
            if (!InputSettings.InputBalanceTelemetryEnabled
                || !TryIndex(device, hunter, weapon, out int index)) return;
            lock (Gate)
            {
                ref Bucket bucket = ref Buckets[index];
                if (acquiredTarget)
                    bucket.AssistAcquisitions = SaturatingIncrement(
                        bucket.AssistAcquisitions);
                if (float.IsFinite(acquisitionMilliseconds)
                    && acquisitionMilliseconds >= 0)
                {
                    bucket.AcquisitionTotal += acquisitionMilliseconds;
                    bucket.AcquisitionSamples = SaturatingIncrement(
                        bucket.AcquisitionSamples);
                    IncrementHistogram(AcquisitionHistograms, index,
                        acquisitionMilliseconds,
                        AcquisitionHistogramBinWidthMilliseconds,
                        AcquisitionHistogramBinCount);
                }
                if (float.IsFinite(rotationalDegrees) && rotationalDegrees >= 0)
                {
                    bucket.RotationalTotal += rotationalDegrees;
                    bucket.RotationalSamples = SaturatingIncrement(
                        bucket.RotationalSamples);
                    IncrementHistogram(RotationHistograms, index, rotationalDegrees,
                        RotationHistogramBinWidthDegrees,
                        RotationHistogramBinCount);
                }
                if (float.IsFinite(frictionMultiplier) && frictionMultiplier >= 0)
                {
                    bucket.FrictionTotal += frictionMultiplier;
                    bucket.FrictionSamples = SaturatingIncrement(
                        bucket.FrictionSamples);
                }
            }
        }

        public static bool TrySnapshot(LookDeviceKind device, int hunter, int weapon,
            out InputBalanceSnapshot snapshot)
        {
            snapshot = default;
            if (!TryIndex(device, hunter, weapon, out int index)) return false;
            lock (Gate)
            {
                Bucket value = Buckets[index];
                snapshot = new InputBalanceSnapshot(device, hunter, weapon,
                    value.Shots, value.Hits, value.Damage, value.Kills, value.Deaths,
                    Average(value.AngularErrorTotal, value.AngularErrorSamples),
                    Average(value.AcquisitionTotal, value.AcquisitionSamples),
                    value.AssistAcquisitions,
                    Average(value.RotationalTotal, value.RotationalSamples),
                    Average(value.FrictionTotal, value.FrictionSamples),
                    value.ZoomedShots, value.ZoomedHits,
                    value.UnzoomedShots, value.UnzoomedHits)
                {
                    NoTargetShots = value.NoTargetShots,
                    NoRetainedAssistTargetShots = value.NoRetainedAssistTargetShots,
                    TargetErrorHistogram = CopyHistogram(TargetErrorHistograms,
                        index, TargetErrorHistogramBinCount),
                    RetainedTargetErrorHistogram = CopyHistogram(
                        RetainedTargetErrorHistograms, index,
                        TargetErrorHistogramBinCount),
                    AcquisitionHistogram = CopyHistogram(AcquisitionHistograms,
                        index, AcquisitionHistogramBinCount),
                    RotationHistogram = CopyHistogram(RotationHistograms, index,
                        RotationHistogramBinCount),
                    HasFixedStepLook = value.HasFixedStepLook,
                    LastPreAssistDelta = value.LastPreAssistDelta,
                    LastPostAssistDelta = value.LastPostAssistDelta,
                    AveragePreAssistAngularError = Average(
                        value.PreAssistAngularErrorTotal,
                        value.PreAssistAngularErrorSamples),
                    AveragePostAssistAngularError = Average(
                        value.PostAssistAngularErrorTotal,
                        value.PostAssistAngularErrorSamples),
                    PreAssistErrorHistogram = CopyHistogram(
                        PreAssistErrorHistograms, index,
                        TargetErrorHistogramBinCount),
                    PostAssistErrorHistogram = CopyHistogram(
                        PostAssistErrorHistograms, index,
                        TargetErrorHistogramBinCount)
                };
                return true;
            }
        }

        public static void Reset()
        {
            lock (Gate)
            {
                Array.Clear(Buckets);
                Array.Clear(TargetErrorHistograms);
                Array.Clear(RetainedTargetErrorHistograms);
                Array.Clear(PreAssistErrorHistograms);
                Array.Clear(PostAssistErrorHistograms);
                Array.Clear(AcquisitionHistograms);
                Array.Clear(RotationHistograms);
                ClearAttributionLocked();
                _fixedStepDevice = LookDeviceKind.None;
                _fixedStepPreAssist = Vector2.Zero;
                _fixedStepPostAssist = Vector2.Zero;
                _fixedStepPreAssistError = float.NaN;
                _fixedStepPostAssistError = float.NaN;
                _hasFixedStepLook = false;
            }
        }

        public static void ResetAttribution()
        {
            lock (Gate) ClearAttributionLocked();
        }

        private static void ClearAttributionLocked()
        {
            Array.Clear(FiringContexts);
            _firingContextCursor = 0;
        }

        private static void Increment(LookDeviceKind device, int hunter, int weapon,
            bool kill)
        {
            if (!InputSettings.InputBalanceTelemetryEnabled
                || !TryIndex(device, hunter, weapon, out int index)) return;
            lock (Gate)
            {
                ref Bucket bucket = ref Buckets[index];
                if (kill) bucket.Kills = SaturatingIncrement(bucket.Kills);
                else bucket.Deaths = SaturatingIncrement(bucket.Deaths);
            }
        }

        private static void StoreFiringContextLocked(int bucketIndex, int weapon,
            bool zoomed, uint? commandSequence)
        {
            if (!commandSequence.HasValue) return;
            FiringContexts[_firingContextCursor] = new FiringContext(
                commandSequence.Value, bucketIndex, (byte)weapon, zoomed, Used: true);
            _firingContextCursor = (_firingContextCursor + 1) % FiringContextCapacity;
        }

        private static uint[] CopyHistogram(uint[] source, int bucketIndex,
            int binCount)
        {
            uint[] copy = new uint[binCount];
            Array.Copy(source, bucketIndex * binCount, copy, 0, binCount);
            return copy;
        }

        private static void IncrementHistogram(uint[] histograms, int bucketIndex,
            float value, float width, int binCount)
        {
            if (!float.IsFinite(value) || value < 0) return;
            int bin = value >= width * (binCount - 1)
                ? binCount - 1 : (int)(value / width);
            int offset = bucketIndex * binCount + bin;
            histograms[offset] = SaturatingIncrement(histograms[offset]);
        }

        private static void RecordAssistErrorLocked(ref Bucket bucket,
            int bucketIndex, float value, bool preAssist)
        {
            value = SanitizeAngularError(value);
            if (!float.IsFinite(value)) return;
            if (preAssist)
            {
                bucket.PreAssistAngularErrorTotal += value;
                bucket.PreAssistAngularErrorSamples = SaturatingIncrement(
                    bucket.PreAssistAngularErrorSamples);
                IncrementHistogram(PreAssistErrorHistograms, bucketIndex, value,
                    TargetErrorHistogramBinWidthDegrees,
                    TargetErrorHistogramBinCount);
            }
            else
            {
                bucket.PostAssistAngularErrorTotal += value;
                bucket.PostAssistAngularErrorSamples = SaturatingIncrement(
                    bucket.PostAssistAngularErrorSamples);
                IncrementHistogram(PostAssistErrorHistograms, bucketIndex, value,
                    TargetErrorHistogramBinWidthDegrees,
                    TargetErrorHistogramBinCount);
            }
        }

        private static float SanitizeAngularError(float value)
            => float.IsFinite(value) && value >= 0 ? value : float.NaN;

        private static bool TryIndex(LookDeviceKind device, int hunter, int weapon,
            out int index)
        {
            int deviceIndex = device switch
            {
                LookDeviceKind.Mouse => 0,
                LookDeviceKind.GamepadStick => 1,
                LookDeviceKind.GamepadGyro => 2,
                LookDeviceKind.Touch => 3,
                LookDeviceKind.Stylus => 4,
                _ => -1
            };
            if (deviceIndex < 0 || hunter < 0 || hunter >= HunterCount
                || weapon < 0 || weapon >= WeaponCount)
            {
                index = -1;
                return false;
            }
            index = (deviceIndex * HunterCount + hunter) * WeaponCount + weapon;
            return true;
        }

        private static uint SaturatingIncrement(uint value)
            => value == uint.MaxValue ? value : value + 1;

        private static uint SaturatingAdd(uint value, uint addition)
            => uint.MaxValue - value < addition ? uint.MaxValue : value + addition;

        private static double Average(double total, uint count)
            => count == 0 ? 0 : total / count;

        private static bool IsFinite(Vector2 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y);

        private struct Bucket
        {
            public uint Shots, Hits, Damage, Kills, Deaths;
            public uint ZoomedShots, ZoomedHits, UnzoomedShots, UnzoomedHits;
            public uint AngularErrorSamples, AcquisitionSamples, AssistAcquisitions;
            public uint RotationalSamples, FrictionSamples;
            public uint PreAssistAngularErrorSamples, PostAssistAngularErrorSamples;
            public uint NoTargetShots, NoRetainedAssistTargetShots;
            public bool HasFixedStepLook;
            public Vector2 LastPreAssistDelta, LastPostAssistDelta;
            public double AngularErrorTotal, AcquisitionTotal, RotationalTotal, FrictionTotal;
            public double PreAssistAngularErrorTotal, PostAssistAngularErrorTotal;
        }

        private readonly record struct FiringContext(uint CommandSequence,
            int BucketIndex, byte Weapon, bool Zoomed, bool Used);
    }
}
