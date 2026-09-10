using System;

namespace MphRead.Mods.Input
{
    public readonly record struct InputBalanceSnapshot(LookDeviceKind Device,
        int Hunter, int Weapon, uint Shots, uint Hits, uint Damage, uint Kills,
        uint Deaths, double AverageAngularError, double AverageAcquisitionMilliseconds,
        uint AssistTargetAcquisitions, double AverageRotationalAssistDegrees,
        double AverageFrictionMultiplier, uint ZoomedShots, uint ZoomedHits,
        uint UnzoomedShots, uint UnzoomedHits);

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
        private static readonly object Gate = new();
        private static readonly Bucket[] Buckets
            = new Bucket[DeviceCount * HunterCount * WeaponCount];
        private static readonly FiringContext[] FiringContexts
            = new FiringContext[FiringContextCapacity];
        private static int _firingContextCursor;

        public static void RecordShot(LookDeviceKind device, int hunter, int weapon,
            bool zoomed, float angularErrorDegrees, uint? commandSequence = null)
        {
            if (!InputSettings.InputBalanceTelemetryEnabled
                || !TryIndex(device, hunter, weapon, out int index)) return;
            lock (Gate)
            {
                ref Bucket bucket = ref Buckets[index];
                bucket.Shots = SaturatingIncrement(bucket.Shots);
                if (zoomed) bucket.ZoomedShots = SaturatingIncrement(bucket.ZoomedShots);
                else bucket.UnzoomedShots = SaturatingIncrement(bucket.UnzoomedShots);
                if (float.IsFinite(angularErrorDegrees) && angularErrorDegrees >= 0)
                {
                    bucket.AngularErrorTotal += angularErrorDegrees;
                    bucket.AngularErrorSamples = SaturatingIncrement(bucket.AngularErrorSamples);
                }
                if (commandSequence.HasValue)
                {
                    FiringContexts[_firingContextCursor] = new FiringContext(
                        commandSequence.Value, index, (byte)weapon, zoomed, Used: true);
                    _firingContextCursor = (_firingContextCursor + 1) % FiringContextCapacity;
                }
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
                    bucket.AssistAcquisitions = SaturatingIncrement(bucket.AssistAcquisitions);
                if (float.IsFinite(acquisitionMilliseconds) && acquisitionMilliseconds >= 0)
                {
                    bucket.AcquisitionTotal += acquisitionMilliseconds;
                    bucket.AcquisitionSamples = SaturatingIncrement(bucket.AcquisitionSamples);
                }
                if (float.IsFinite(rotationalDegrees))
                {
                    bucket.RotationalTotal += rotationalDegrees;
                    bucket.RotationalSamples = SaturatingIncrement(bucket.RotationalSamples);
                }
                if (float.IsFinite(frictionMultiplier) && frictionMultiplier >= 0)
                {
                    bucket.FrictionTotal += frictionMultiplier;
                    bucket.FrictionSamples = SaturatingIncrement(bucket.FrictionSamples);
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
                    value.UnzoomedShots, value.UnzoomedHits);
                return true;
            }
        }

        public static void Reset()
        {
            lock (Gate)
            {
                Array.Clear(Buckets);
                ClearAttributionLocked();
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

        private struct Bucket
        {
            public uint Shots, Hits, Damage, Kills, Deaths;
            public uint ZoomedShots, ZoomedHits, UnzoomedShots, UnzoomedHits;
            public uint AngularErrorSamples, AcquisitionSamples, AssistAcquisitions;
            public uint RotationalSamples, FrictionSamples;
            public double AngularErrorTotal, AcquisitionTotal, RotationalTotal, FrictionTotal;
        }

        private readonly record struct FiringContext(uint CommandSequence,
            int BucketIndex, byte Weapon, bool Zoomed, bool Used);
    }
}
