using System;
using System.Diagnostics;
using System.Threading;

namespace MphRead.Mods.Network
{
    /// <summary>60 Hz absolute deadlines, bounded catch-up, one owner thread.</summary>
    public sealed class FixedTickScheduler
    {
        public const int Rate = 60;
        public const int MaxCatchUp = 4;
        private readonly double _interval;
        private double _next;
        public long DroppedTicks { get; private set; }
        public long CatchUpTicks { get; private set; }
        public long Overloads { get; private set; }
        public int TicksBehind { get; private set; }
        public NetSample DriftMs;
        public NetSample DurationMs;
        private readonly long _frequency;

        public FixedTickScheduler(long start, long frequency)
        {
            if (start < 0 || frequency < Rate)
            {
                throw new ArgumentOutOfRangeException(nameof(frequency));
            }
            _frequency = frequency;
            _interval = frequency / (double)Rate;
            _next = start + _interval;
        }

        public FixedTickScheduler() : this(Stopwatch.GetTimestamp(), Stopwatch.Frequency) { }

        public int TakeDue(long now)
        {
            if (now < _next)
            {
                return 0;
            }
            double late = now - _next;
            DriftMs.Record(late * 1000 / _frequency);
            int due = (int)Math.Min(Int32.MaxValue, Math.Floor(late / _interval) + 1);
            TicksBehind = due - 1;
            int steps = Math.Min(due, MaxCatchUp);
            CatchUpTicks += steps - 1;
            if (due > MaxCatchUp)
            {
                DroppedTicks += due - MaxCatchUp;
                Overloads++;
                _next = now + _interval;
            }
            else
            {
                _next += steps * _interval;
            }
            return steps;
        }

        public void Wait()
        {
            double remainingMs = (_next - Stopwatch.GetTimestamp()) * 1000 / _frequency;
            if (remainingMs >= 2)
            {
                Thread.Sleep(Math.Max(1, (int)remainingMs - 1));
            }
            else if (remainingMs > 0)
            {
                Thread.Yield();
            }
        }
    }
}
