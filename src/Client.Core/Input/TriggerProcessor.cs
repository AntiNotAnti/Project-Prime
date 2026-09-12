using System;

namespace MphRead.Mods.Input
{
    /// <summary>The synthesized trigger state and its fixed-step edges.</summary>
    public readonly struct TriggerSample
    {
        public TriggerSample(GamepadButtons buttons, GamepadButtons pressed,
            GamepadButtons released)
        {
            Buttons = buttons;
            Pressed = pressed;
            Released = released;
        }

        public GamepadButtons Buttons { get; }
        public GamepadButtons Pressed { get; }
        public GamepadButtons Released { get; }
        public bool LeftPressed => (Buttons & GamepadButtons.LeftTrigger) != 0;
        public bool RightPressed => (Buttons & GamepadButtons.RightTrigger) != 0;
    }

    /// <summary>
    /// Central trigger hysteresis. Platform adapters publish axes and physical
    /// digital buttons; this is the only code that synthesizes trigger flags.
    /// </summary>
    public struct TriggerProcessor
    {
        public const float DefaultPressThreshold = 0.20f;
        public const float DefaultReleaseThreshold = 0.12f;

        private bool _leftPressed;
        private bool _rightPressed;

        public TriggerProcessor()
            : this(DefaultPressThreshold, DefaultReleaseThreshold)
        {
        }

        public TriggerProcessor(float pressThreshold = DefaultPressThreshold,
            float releaseThreshold = DefaultReleaseThreshold)
        {
            PressThreshold = Sanitize(pressThreshold, DefaultPressThreshold);
            ReleaseThreshold = Math.Clamp(Sanitize(releaseThreshold, DefaultReleaseThreshold),
                0, PressThreshold);
            _leftPressed = false;
            _rightPressed = false;
        }

        public float PressThreshold { get; private set; }
        public float ReleaseThreshold { get; private set; }
        public readonly bool LeftPressed => _leftPressed;
        public readonly bool RightPressed => _rightPressed;

        /// <summary>
        /// Update thresholds without losing the current hysteresis state.
        /// Threshold changes take effect on the next fixed sample.
        /// </summary>
        public void Configure(float pressThreshold, float releaseThreshold)
        {
            PressThreshold = Sanitize(pressThreshold, DefaultPressThreshold);
            ReleaseThreshold = Math.Min(
                Sanitize(releaseThreshold, DefaultReleaseThreshold), PressThreshold);
        }

        public TriggerSample Process(float left, float right)
        {
            if (!float.IsFinite(left) || !float.IsFinite(right))
            {
                GamepadButtons previousButtons = CurrentButtons();
                TriggerSample invalid = new(GamepadButtons.None,
                    pressed: GamepadButtons.None, released: previousButtons);
                Reset();
                return invalid;
            }

            left = Math.Clamp(left, 0, 1);
            right = Math.Clamp(right, 0, 1);
            GamepadButtons previous = CurrentButtons();
            Update(ref _leftPressed, left);
            Update(ref _rightPressed, right);
            GamepadButtons current = CurrentButtons();
            return new TriggerSample(current, current & ~previous, previous & ~current);
        }

        /// <summary>
        /// Evaluate axes without advancing this processor.  Menus and
        /// rebinders can show effective buttons while edge ownership remains a
        /// fixed-step concern.
        /// </summary>
        public readonly TriggerSample Preview(float left, float right)
        {
            TriggerProcessor copy = this;
            return copy.Process(left, right);
        }

        public void Reset()
        {
            _leftPressed = false;
            _rightPressed = false;
        }

        private readonly GamepadButtons CurrentButtons()
        {
            GamepadButtons buttons = GamepadButtons.None;
            if (_leftPressed) buttons |= GamepadButtons.LeftTrigger;
            if (_rightPressed) buttons |= GamepadButtons.RightTrigger;
            return buttons;
        }

        private void Update(ref bool pressed, float value)
        {
            if (!pressed)
            {
                pressed = value >= PressThreshold;
            }
            else if (value <= ReleaseThreshold)
            {
                pressed = false;
            }
        }

        private static float Sanitize(float value, float fallback)
            => !float.IsFinite(value) ? fallback : Math.Clamp(value, 0, 1);
    }
}
