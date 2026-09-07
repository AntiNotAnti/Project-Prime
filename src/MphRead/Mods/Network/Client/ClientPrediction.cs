using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>Bounded, error-based reconciliation; no gameplay side-effect replay.</summary>
    public sealed class ClientPrediction
    {
        public const int Capacity = 256;
        private readonly Entry[] _history = new Entry[Capacity];
        private readonly record struct Entry(uint Sequence, Vector3 Position, bool Alt, bool Valid);
        private Vector3 _corrections;
        private Vector3 _visual;
        private bool _hasAck;
        private uint _lastAck;
        private int _count;
        private NetSample _error;
        public NetSample Error => _error;
        public long Corrections { get; private set; }
        public long HardCorrections { get; private set; }
        public long HistoryMisses { get; private set; }
        public bool LastCorrectionHard { get; private set; }
        public Vector3 VisualOffset => _visual;

        public void Record(uint sequence, Vector3 position, bool alt)
        {
            _history[sequence % Capacity] = new Entry(sequence, position - _corrections, alt, true);
            _count = Math.Min(_count + 1, Capacity);
        }

        public Vector3 Reconcile(uint acknowledgement, Vector3 position, bool alt, Vector3 current)
        {
            LastCorrectionHard = false;
            if (_hasAck && !Sequence32.IsNewer(acknowledgement, _lastAck)) { return current; }
            _lastAck = acknowledgement;
            _hasAck = true;
            Entry entry = _history[acknowledgement % Capacity];
            if (!entry.Valid || entry.Sequence != acknowledgement)
            {
                HistoryMisses++;
                // Input can precede the first spawn snapshot. It has no
                // prediction history; wait for the first recorded command.
                return _count < Capacity && Finite(current) ? current : Hard(position);
            }
            Vector3 error = position - (entry.Position + _corrections);
            if (!Finite(current) || !Finite(error)) { return Hard(position); }
            _error.Record(error.Length);
            if (entry.Alt != alt || error.LengthSquared > 36) { return Hard(position); }
            if (error.LengthSquared < 0.000225f) { return current; }
            // Every pending historical position implicitly receives this
            // translation too, so a later ACK cannot apply the same error twice.
            _corrections += error;
            _visual -= error;
            if (_visual.LengthSquared > 36) { return Hard(position); }
            Corrections++;
            return current + error;
        }

        private Vector3 Hard(Vector3 position)
        {
            Array.Clear(_history);
            _count = 0;
            _corrections = _visual = Vector3.Zero;
            HardCorrections++;
            LastCorrectionHard = true;
            return position;
        }

        public void Reset()
        {
            Array.Clear(_history);
            _count = 0;
            _corrections = _visual = Vector3.Zero;
            _hasAck = false;
            LastCorrectionHard = false;
        }

        public void AdvanceVisual(double seconds)
        {
            if (Double.IsFinite(seconds) && seconds > 0)
            {
                _visual *= (float)Math.Exp(-Math.Min(seconds, 1) * 20);
                if (_visual.LengthSquared < 0.000001f) { _visual = Vector3.Zero; }
            }
        }

        private static bool Finite(Vector3 value) => Single.IsFinite(value.X)
            && Single.IsFinite(value.Y) && Single.IsFinite(value.Z);
    }
}
