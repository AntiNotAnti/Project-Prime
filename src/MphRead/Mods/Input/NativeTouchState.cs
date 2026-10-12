using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Input
{
    /// <summary>
    /// One authenticated player's native-style DS touch observation. Only input
    /// is transported: neither positions nor velocity are trusted from this data.
    /// </summary>
    public readonly record struct NativeTouchReport(
        bool Active, bool Down, bool Continued, short Delta4X, short Delta4Y,
        uint SampleSequence, bool SecondStep)
    {
        public const int WireSize = 9;
        public void Write(Span<byte> destination)
        {
            destination[0] = (byte)((Active ? 1 : 0) | (Down ? 2 : 0)
                | (Continued ? 4 : 0) | (SecondStep ? 8 : 0));
            BinaryPrimitives.WriteInt16LittleEndian(destination[1..], Delta4X);
            BinaryPrimitives.WriteInt16LittleEndian(destination[3..], Delta4Y);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[5..], SampleSequence);
        }

        public static NativeTouchReport Read(ReadOnlySpan<byte> source)
        {
            byte flags = source[0];
            if ((flags & ~15) != 0 || (flags & 1) == 0) return default;
            bool down = (flags & 2) != 0;
            bool continued = down && (flags & 4) != 0;
            return new NativeTouchReport(true, down, continued,
                down ? BinaryPrimitives.ReadInt16LittleEndian(source[1..]) : (short)0,
                down ? BinaryPrimitives.ReadInt16LittleEndian(source[3..]) : (short)0,
                BinaryPrimitives.ReadUInt32LittleEndian(source[5..]), (flags & 8) != 0);
        }
    }

    /// <summary>EU1.1-style 30 Hz producer: FOUR signed deltas and their SUM.</summary>
    public sealed class NativeTouchState
    {
        private readonly short[] _xs = new short[4];
        private readonly short[] _ys = new short[4];
        private int _next;
        private int _lastX, _lastY;
        public bool Down { get; private set; }
        public bool Continued { get; private set; }
        public ushort ContactDuration { get; private set; }
        public short Delta4X { get; private set; }
        public short Delta4Y { get; private set; }

        private static short ClampDelta(int delta) => (short)Math.Clamp(delta, -255, 255);
        public void Clear()
        {
            Down = Continued = false;
            ContactDuration = 0;
            Delta4X = Delta4Y = 0;
            Array.Clear(_xs);
            Array.Clear(_ys);
            _next = 0;
            _lastX = _lastY = 0;
        }

        private bool Begin(bool down)
        {
            if (!down) { Clear(); return false; }
            bool continued = Down;
            if (!continued)
            {
                Array.Clear(_xs);
                Array.Clear(_ys);
                Delta4X = Delta4Y = 0;
                _next = 0;
                ContactDuration = 0;
            }
            Down = true;
            Continued = continued;
            if (ContactDuration < ushort.MaxValue) ContactDuration++;
            return continued;
        }

        private void Push(short x, short y)
        {
            _xs[_next] = x;
            _ys[_next] = y;
            _next = (_next + 1) % 4;
            int sx = 0, sy = 0;
            for (int i = 0; i < 4; i++) { sx += _xs[i]; sy += _ys[i]; }
            Delta4X = unchecked((short)sx);
            Delta4Y = unchecked((short)sy);
        }

        public void UpdateAbsolute(bool down, int x, int y)
        {
            x = Math.Clamp(x, 0, 255);
            y = Math.Clamp(y, 0, 191);
            if (!Begin(down)) { _lastX = x; _lastY = y; return; }
            Push(ClampDelta(x - _lastX), ClampDelta(y - _lastY));
            _lastX = x;
            _lastY = y;
        }

        public void UpdateRelative(bool down, int dx, int dy)
        {
            if (!Begin(down)) return;
            Push(ClampDelta(dx), ClampDelta(dy));
        }

        public NativeTouchReport Report(uint sequence, bool second)
            => new(true, Down, Continued, Delta4X, Delta4Y, sequence, second);
    }

    /// <summary>
    /// A native sample spans two 60 Hz steps. Duplicate/held remote packets
    /// contribute NO impulse; each genuinely admitted substep contributes 1/2.
    /// </summary>
    public sealed class NativeTouchSample
    {
        private readonly NativeTouchState _state = new();
        private uint _sequence;
        private int _phase;
        private float _pendingX, _pendingY;
        private int _idleSteps = 5;
        private bool _remoteSeen, _lastSecond;
        private ushort _generation, _life;
        public NativeTouchReport Current { get; private set; }
        public float RollShare { get; private set; }
        public bool NewSample { get; private set; }

        public void Suspend()
        {
            _state.Clear();
            _phase = 0;
            _pendingX = _pendingY = 0;
            _idleSteps = 5;
            RollShare = 0;
            NewSample = false;
            _remoteSeen = false;
            Current = default;
            _sequence = unchecked(_sequence + 1);
        }

        private bool BeginLocal()
        {
            NewSample = _phase == 0;
            _phase ^= 1;
            if (NewSample) _sequence = unchecked(_sequence + 1);
            return NewSample;
        }

        private void FinishLocal()
        {
            Current = _state.Report(_sequence, _phase == 0);
            RollShare = _state.Down ? 0.5f : 0;
        }

        public void StepAbsolute(bool down, int x, int y)
        {
            if (BeginLocal()) _state.UpdateAbsolute(down, x, y);
            FinishLocal();
        }

        public void StepRelative(bool enabled, float pixelX, float pixelY, float sensitivity)
        {
            if (!enabled) { Suspend(); return; }
            if (float.IsFinite(pixelX) && float.IsFinite(pixelY))
            {
                _pendingX += pixelX * 0.25f * sensitivity;
                _pendingY += pixelY * 0.25f * sensitivity;
                if (MathF.Abs(pixelX) + MathF.Abs(pixelY) > 0.001f) _idleSteps = 0;
                else _idleSteps++;
            }
            else _idleSteps++;
            bool down = _idleSteps <= 4;
            if (BeginLocal())
            {
                int dx = (int)MathF.Round(Math.Clamp(_pendingX, -255f, 255f));
                int dy = (int)MathF.Round(Math.Clamp(_pendingY, -255f, 255f));
                _state.UpdateRelative(down, dx, dy);
                _pendingX = _pendingY = 0;
            }
            FinishLocal();
        }

        public void ApplyRemote(in NativeTouchReport report, ushort generation, ushort life)
        {
            RollShare = 0;
            NewSample = false;
            if (!report.Active || life == 0)
            {
                _remoteSeen = false;
                Current = default;
                return;
            }
            if (!_remoteSeen || generation != _generation || life != _life)
            {
                _remoteSeen = false;
                _generation = generation;
                _life = life;
            }
            if (_remoteSeen)
            {
                if (report.SampleSequence == _sequence)
                {
                    // Only one first and one second step, in that order.
                    if (_lastSecond || !report.SecondStep) return;
                }
                else if (unchecked(report.SampleSequence - _sequence) >= 0x80000000u)
                {
                    return;
                }
            }
            NewSample = !_remoteSeen || report.SampleSequence != _sequence;
            _remoteSeen = true;
            _sequence = report.SampleSequence;
            _lastSecond = report.SecondStep;
            Current = report;
            RollShare = report.Down ? 0.5f : 0;
        }

        public float TakeRollShare()
        {
            float share = RollShare;
            RollShare = 0;
            return share;
        }
    }

    /// <summary>Android's one locally owned aim finger, published before the fixed step.</summary>
    public static class NativeTouchHost
    {
        public static bool Down { get; private set; }
        public static int X { get; private set; }
        public static int Y { get; private set; }
        public static void Publish(bool down, int x = 0, int y = 0)
        {
            Down = down;
            X = Math.Clamp(x, 0, 255);
            Y = Math.Clamp(y, 0, 191);
        }
    }
}
