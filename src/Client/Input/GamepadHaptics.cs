using System;
using System.Diagnostics;
using System.Threading;

namespace MphRead.Mods.Input
{
    public enum HapticEvent
    {
        WeaponFire,
        TakingDamage,
        ChargedShotRelease,
        Missile,
        MorphBoost,
        Death,
        MajorPickup
    }

    public readonly record struct HapticPattern(ushort LowFrequency,
        ushort HighFrequency, uint DurationMilliseconds, byte Priority);

    internal interface IGamepadHapticsSink
    {
        void Apply(in HapticPattern pattern);
        void Stop();
    }

    /// <summary>
    /// Bounded presentation-event queue. Producers never call native APIs.
    /// The SDL host drains requests on its event thread and applies a stable
    /// priority/max-intensity combination policy.
    /// </summary>
    public static class GamepadHaptics
    {
        private const int Capacity = 32;
        private const int DedupCapacity = 64;
        private static readonly object Gate = new();
        private static readonly Request[] Requests = new Request[Capacity];
        private static readonly ulong[] Dedup = new ulong[DedupCapacity];
        private static int _head;
        private static int _count;
        private static int _dedupHead;
        private static IGamepadHapticsSink? _sink;
        private static HapticPattern _active;
        private static double _activeUntil;
        private static bool _stopPending;

        public static bool Play(HapticEvent value, uint identity = 0)
        {
            if (!InputSettings.GamepadHapticsEnabled) return false;
            ulong key = identity == 0 ? 0 : ((ulong)(byte)value << 32) | identity;
            lock (Gate)
            {
                if (key != 0)
                {
                    for (int i = 0; i < DedupCapacity; i++)
                        if (Dedup[i] == key) return false;
                    Dedup[_dedupHead] = key;
                    _dedupHead = (_dedupHead + 1) % DedupCapacity;
                }
                if (_count == Capacity)
                {
                    _head = (_head + 1) % Capacity;
                    _count--;
                }
                Requests[(_head + _count) % Capacity] = new Request(Pattern(value));
                _count++;
                return true;
            }
        }

        internal static void Attach(IGamepadHapticsSink sink)
        {
            lock (Gate)
            {
                _sink = sink;
                _stopPending = false;
            }
        }

        internal static void Detach(IGamepadHapticsSink sink)
        {
            lock (Gate)
            {
                if (!ReferenceEquals(_sink, sink)) return;
                _sink = null;
                ClearLocked(clearIdentities: false);
                _stopPending = false;
            }
        }

        internal static bool Pump()
        {
            IGamepadHapticsSink? sink;
            HapticPattern result = default;
            bool apply = false;
            bool stop;
            lock (Gate)
            {
                sink = _sink;
                if (sink == null) return false;
                stop = _stopPending;
                _stopPending = false;
                if (!InputSettings.GamepadHapticsEnabled)
                {
                    stop |= _count > 0 || _activeUntil > 0;
                    ClearLocked(clearIdentities: false);
                }
                else if (_count > 0)
                {
                    while (_count > 0)
                    {
                        HapticPattern next = Requests[_head].Pattern;
                        Requests[_head] = default;
                        _head = (_head + 1) % Capacity;
                        _count--;
                        if (next.Priority > result.Priority) result = next;
                        else if (next.Priority == result.Priority)
                        {
                            result = new HapticPattern(Math.Max(result.LowFrequency,
                                next.LowFrequency), Math.Max(result.HighFrequency,
                                next.HighFrequency), Math.Max(result.DurationMilliseconds,
                                next.DurationMilliseconds), result.Priority);
                        }
                    }
                    double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                    if (now < _activeUntil)
                    {
                        if (result.Priority < _active.Priority) result = default;
                        else if (result.Priority == _active.Priority)
                        {
                            result = new HapticPattern(Math.Max(result.LowFrequency,
                                _active.LowFrequency), Math.Max(result.HighFrequency,
                                _active.HighFrequency), Math.Max(result.DurationMilliseconds,
                                (uint)Math.Ceiling((_activeUntil - now) * 1000)),
                                result.Priority);
                        }
                    }
                    if (result.Priority != 0)
                    {
                        _active = result;
                        _activeUntil = now + result.DurationMilliseconds / 1000d;
                        apply = true;
                    }
                }
            }
            // Native callbacks are deliberately outside Gate and only Pump is
            // called by the SDL host/event thread.
            if (stop) sink.Stop();
            if (apply) sink.Apply(result);
            return stop || apply;
        }

        public static void Stop(bool clearIdentities = false)
        {
            lock (Gate)
            {
                ClearLocked(clearIdentities);
                _stopPending = true;
            }
        }

        internal static bool MutationLockHeldByCurrentThread
            => Monitor.IsEntered(Gate);

        public static HapticPattern Pattern(HapticEvent value) => value switch
        {
            HapticEvent.WeaponFire => new(8000, 18000, 45, 1),
            HapticEvent.Missile => new(26000, 22000, 100, 2),
            HapticEvent.ChargedShotRelease => new(30000, 30000, 130, 3),
            HapticEvent.MorphBoost => new(22000, 12000, 110, 2),
            HapticEvent.TakingDamage => new(36000, 18000, 160, 4),
            HapticEvent.MajorPickup => new(18000, 32000, 240, 3),
            HapticEvent.Death => new(52000, 28000, 400, 5),
            _ => default
        };

        private static void ClearLocked(bool clearIdentities)
        {
            Array.Clear(Requests);
            _head = _count = 0;
            _active = default;
            _activeUntil = 0;
            if (clearIdentities)
            {
                Array.Clear(Dedup);
                _dedupHead = 0;
            }
        }

        private readonly record struct Request(HapticPattern Pattern);
    }
}
