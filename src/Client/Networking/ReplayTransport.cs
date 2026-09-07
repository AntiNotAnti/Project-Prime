using System;

namespace MphRead.Mods.Network
{
    /// <summary>Fixed-tick playback scheduling; rates never alter simulation delta time.</summary>
    public sealed class ReplayTransport
    {
        private int _quarters = 4, _fraction;
        private bool _step;
        public bool Paused { get; set; }
        public double Rate
        {
            get => _quarters / 4.0;
            set
            {
                int quarters = value switch { 0.25 => 1, 0.5 => 2, 1 => 4, 2 => 8, 4 => 16, _ => 0 };
                if (quarters == 0) throw new ArgumentOutOfRangeException(nameof(value));
                _quarters = quarters;
            }
        }
        public void Step() { Paused = true; _step = true; }
        public int TakeSteps()
        {
            if (_step) { _step = false; return 1; }
            if (Paused) return 0;
            _fraction += _quarters;
            int steps = _fraction / 4; _fraction %= 4;
            return steps;
        }
        internal void Reset() { Paused = false; _quarters = 4; _fraction = 0; _step = false; }
    }
}
