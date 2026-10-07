using System;

namespace MphRead.Mods.Input
{
    public readonly record struct MorphTouchReport(
        bool Down, bool Continued, short Delta4X, short Delta4Y);

    /// <summary>
    /// The DS touch producer used by Morph Ball. It keeps the four-sample
    /// rolling delta that EU1.1 consumes for touch roll and touch boost.
    /// </summary>
    public sealed class NativeTouchState
    {
        private readonly short[] _dx = new short[4];
        private readonly short[] _dy = new short[4];
        private int _at;
        private short _x;
        private short _y;

        public bool Down { get; private set; }
        public bool Continued { get; private set; }
        public short Delta4X { get; private set; }
        public short Delta4Y { get; private set; }

        public void Update(bool down, short x, short y)
        {
            bool continued = Down && down;
            if (!down)
            {
                Clear();
                return;
            }
            if (!continued)
            {
                Array.Clear(_dx); Array.Clear(_dy);
                _at = 0; Delta4X = Delta4Y = 0;
                _x = x; _y = y; Down = true; Continued = false;
                return;
            }

            short dx = (short)Math.Clamp(x - _x, short.MinValue, short.MaxValue);
            short dy = (short)Math.Clamp(y - _y, short.MinValue, short.MaxValue);
            _x = x; _y = y;
            _dx[_at] = dx; _dy[_at] = dy; _at = (_at + 1) & 3;
            int sx = 0, sy = 0;
            for (int i = 0; i < 4; i++) { sx += _dx[i]; sy += _dy[i]; }
            Delta4X = (short)Math.Clamp(sx, short.MinValue, short.MaxValue);
            Delta4Y = (short)Math.Clamp(sy, short.MinValue, short.MaxValue);
            Down = true; Continued = true;
        }

        public void Assign(MorphTouchReport report)
        {
            Down = report.Down;
            Continued = report.Continued;
            Delta4X = report.Delta4X;
            Delta4Y = report.Delta4Y;
        }

        public MorphTouchReport Report()
            => new(Down, Continued, Delta4X, Delta4Y);

        public void Clear()
        {
            Down = Continued = false;
            Delta4X = Delta4Y = 0;
            Array.Clear(_dx); Array.Clear(_dy); _at = 0; _x = _y = 0;
        }
    }

    /// <summary>Android's aim finger, published on the UI/GL boundary.</summary>
    public static class MorphTouchHost
    {
        public static bool Published { get; private set; }
        public static bool Down { get; private set; }
        public static short X { get; private set; }
        public static short Y { get; private set; }

        public static void Publish(bool down, float normalizedX, float normalizedY)
        {
            Published = true; Down = down;
            X = (short)Math.Clamp((int)MathF.Round(normalizedX * 255f), 0, 255);
            Y = (short)Math.Clamp((int)MathF.Round(normalizedY * 191f), 0, 191);
        }

        public static void Clear()
        {
            Published = false; Down = false; X = Y = 0;
        }
    }

    /// <summary>
    /// One 30 Hz native sample shared by two 60 Hz simulation steps. Remote
    /// reports use IntentPacket.Frame/2 as the sample identity, so held or
    /// duplicate intents cannot manufacture extra roll or touch boosts.
    /// </summary>
    public sealed class NativeTouchSample
    {
        private readonly NativeTouchState _state = new();
        private uint _identity;
        private bool _hasIdentity;
        private float _rollShare;
        private float _pendingMouseX, _pendingMouseY;
        private float _syntheticX = 128, _syntheticY = 96;
        private int _relativeIdleTicks;
        private uint _lastReportedFrame = uint.MaxValue;

        public NativeTouchState State => _state;
        public uint Identity => _identity;
        public float TakeRollShare()
        {
            float share = _rollShare;
            _rollShare = 0;
            return share;
        }

        public void StepLocal(ulong frame, bool enabled, bool contact,
            float mouseDeltaX, float mouseDeltaY)
        {
            _rollShare = 0;
            if (!enabled)
            {
                Suspend();
                return;
            }

            _pendingMouseX += mouseDeltaX;
            _pendingMouseY += mouseDeltaY;
            bool newNative = (frame & 1UL) == 0;
            if (newNative)
            {
                _identity = (uint)(frame / 2);
                _hasIdentity = true;
                if (MorphTouchHost.Published)
                {
                    _state.Update(MorphTouchHost.Down, MorphTouchHost.X, MorphTouchHost.Y);
                }
                else
                {
                    // melonPrimeDS/Fruity's desktop adapter: 0.25 DS units for
                    // one host relative-motion pixel. Relative mouse contact
                    // remains down for four 60 Hz steps after motion, matching
                    // the native adapter's short idle tail.
                    bool moved = _pendingMouseX != 0 || _pendingMouseY != 0;
                    if (moved) _relativeIdleTicks = 2;
                    else if (_relativeIdleTicks > 0) _relativeIdleTicks--;
                    _syntheticX = Math.Clamp(_syntheticX + _pendingMouseX * 0.25f, short.MinValue + 1, short.MaxValue - 1);
                    _syntheticY = Math.Clamp(_syntheticY + _pendingMouseY * 0.25f, short.MinValue + 1, short.MaxValue - 1);
                    _state.Update(contact || moved || _relativeIdleTicks > 0,
                        (short)MathF.Round(_syntheticX), (short)MathF.Round(_syntheticY));
                }
                _pendingMouseX = _pendingMouseY = 0;
            }
            // One native 30 Hz impulse is distributed over its two 60 Hz steps.
            _rollShare = 0.5f;
        }

        public void ApplyReported(MorphTouchReport report, uint intentFrame)
        {
            _rollShare = 0;
            if (_lastReportedFrame == intentFrame) return;
            _lastReportedFrame = intentFrame;
            uint identity = intentFrame / 2;
            bool second = (intentFrame & 1) != 0;
            if (_hasIdentity && identity < _identity) return;
            if (!_hasIdentity || identity != _identity)
            {
                _identity = identity; _hasIdentity = true;
                _state.Assign(report);
                _rollShare = 0.5f;
                return;
            }
            // The sibling contributes the second half once. A held/duplicate
            // frame cannot contribute again because its frame identity is the
            // same and consumers take the share.
            if (second) _rollShare = 0.5f;
            _state.Assign(report);
        }

        public MorphTouchReport Report() => _state.Report();

        public void Suspend()
        {
            _state.Clear(); _rollShare = 0; _pendingMouseX = _pendingMouseY = 0;
            _relativeIdleTicks = 0; _lastReportedFrame = uint.MaxValue;
            _hasIdentity = false; _identity++;
        }
    }
}
