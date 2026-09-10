using System;

namespace MphRead.Mods.Input
{
    public enum ControllerFamily
    {
        Generic,
        Xbox,
        PlayStation,
        Nintendo
    }

    /// <summary>Presentation-only labels for normalized gamepad bindings.</summary>
    public static class ControllerGlyphs
    {
        public static string Label(GamepadButtons button, ControllerFamily family)
        {
            if (button == GamepadButtons.None) return "Unbound";
            if (!IsSingle(button)) return string.Join(" + ", Labels(button, family));
            return SingleLabel(button, family);
        }

        public static ControllerFamily InferFromName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ControllerFamily.Generic;
            if (name.Contains("PlayStation", StringComparison.OrdinalIgnoreCase)
                || name.Contains("DualShock", StringComparison.OrdinalIgnoreCase)
                || name.Contains("DualSense", StringComparison.OrdinalIgnoreCase))
                return ControllerFamily.PlayStation;
            if (name.Contains("Nintendo", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Switch", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Joy-Con", StringComparison.OrdinalIgnoreCase))
                return ControllerFamily.Nintendo;
            if (name.Contains("Xbox", StringComparison.OrdinalIgnoreCase)
                || name.Contains("XInput", StringComparison.OrdinalIgnoreCase))
                return ControllerFamily.Xbox;
            return ControllerFamily.Generic;
        }

        private static string[] Labels(GamepadButtons buttons, ControllerFamily family)
        {
            var labels = new System.Collections.Generic.List<string>(4);
            foreach (GamepadButtons value in Enum.GetValues<GamepadButtons>())
                if (value != GamepadButtons.None && IsSingle(value)
                    && (buttons & value) != 0) labels.Add(SingleLabel(value, family));
            return labels.ToArray();
        }

        private static string SingleLabel(GamepadButtons button, ControllerFamily family)
            => button switch
            {
                GamepadButtons.A => family switch
                {
                    ControllerFamily.PlayStation => "Cross",
                    ControllerFamily.Nintendo => "B",
                    _ => "A"
                },
                GamepadButtons.B => family switch
                {
                    ControllerFamily.PlayStation => "Circle",
                    ControllerFamily.Nintendo => "A",
                    _ => "B"
                },
                GamepadButtons.X => family switch
                {
                    ControllerFamily.PlayStation => "Square",
                    ControllerFamily.Nintendo => "Y",
                    _ => "X"
                },
                GamepadButtons.Y => family switch
                {
                    ControllerFamily.PlayStation => "Triangle",
                    ControllerFamily.Nintendo => "X",
                    _ => "Y"
                },
                GamepadButtons.LeftBumper => family == ControllerFamily.PlayStation ? "L1" : "LB",
                GamepadButtons.RightBumper => family == ControllerFamily.PlayStation ? "R1" : "RB",
                GamepadButtons.LeftTrigger => family == ControllerFamily.PlayStation ? "L2" : "LT",
                GamepadButtons.RightTrigger => family == ControllerFamily.PlayStation ? "R2" : "RT",
                GamepadButtons.Back => family switch
                {
                    ControllerFamily.PlayStation => "Create",
                    ControllerFamily.Nintendo => "−",
                    _ => "View"
                },
                GamepadButtons.Start => family switch
                {
                    ControllerFamily.PlayStation => "Options",
                    ControllerFamily.Nintendo => "+",
                    _ => "Menu"
                },
                GamepadButtons.LeftThumb => "L3",
                GamepadButtons.RightThumb => "R3",
                GamepadButtons.DpadUp => "D-pad Up",
                GamepadButtons.DpadRight => "D-pad Right",
                GamepadButtons.DpadDown => "D-pad Down",
                GamepadButtons.DpadLeft => "D-pad Left",
                _ => button.ToString()
            };

        private static bool IsSingle(GamepadButtons value)
        {
            int raw = (int)value;
            return raw > 0 && (raw & (raw - 1)) == 0;
        }
    }
}
