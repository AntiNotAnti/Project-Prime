using System;
using System.Collections.Generic;
using System.Globalization;

namespace MphRead.Mods.Input
{
    /// <summary>
    /// One control on the phone's screen. Kept in shared code so both the
    /// Android overlay and the settings preview edit the same layout model.
    /// </summary>
    public enum TouchControl
    {
        Shoot,
        Jump,
        Morph,
        ScanVisor,
        Scan,
        Missile,
        WeaponMenu,
        Zoom,
        Pause,
        Scoreboard,
        Chat,
        Clip,
        PowerBeam,
        MissileSelect,
        VoltDriver,
        Battlehammer,
        Imperialist,
        Judicator,
        Magmaul,
        ShockCoil,
        OmegaCannon
    }

    /// <summary>Normalized centre and per-button size multiplier.</summary>
    public readonly struct TouchButtonLayout
    {
        public float X { get; }
        public float Y { get; }
        public float Scale { get; }

        public TouchButtonLayout(float x, float y, float scale)
        {
            X = x;
            Y = y;
            Scale = scale;
        }
    }

    /// <summary>Resolved pixel geometry for one overlay button.</summary>
    public readonly struct TouchButtonGeometry
    {
        public float X { get; }
        public float Y { get; }
        public float Radius { get; }

        public TouchButtonGeometry(float x, float y, float radius)
        {
            X = x;
            Y = y;
            Radius = radius;
        }
    }

    /// <summary>
    /// Touch overlay visibility, appearance and layout.
    ///
    /// The defaults preserve the Android layout that shipped before the editor.
    /// Direct weapon buttons are intentionally opt-in so an update does not fill
    /// an existing player's screen with nine new controls. Their positions still
    /// exist in the editor while disabled, ready to be arranged and enabled.
    ///
    /// Everything persists in controls.txt alongside the rest of the input
    /// configuration. Positions are stored as normalized screen coordinates so
    /// a layout survives rotation, resolution changes and different phone sizes.
    /// </summary>
    public static class TouchSettings
    {
        public const float MinButtonScale = 0.50f;
        public const float MaxButtonScale = 1.75f;
        public const float MinStickScale = 0.60f;
        public const float MaxStickScale = 1.80f;
        public const float MinOverlayOpacity = 0.20f;
        public const float MaxOverlayOpacity = 1.00f;
        public const float MinIndividualScale = 0.40f;
        public const float MaxIndividualScale = 2.00f;

        public static bool ButtonsVisible { get; set; } = true;

        private static float _buttonScale = 1f;
        private static float _stickScale = 1f;
        private static float _overlayOpacity = 1f;

        public static float ButtonScale
        {
            get => _buttonScale;
            set => _buttonScale = Math.Clamp(value, MinButtonScale, MaxButtonScale);
        }

        public static float StickScale
        {
            get => _stickScale;
            set => _stickScale = Math.Clamp(value, MinStickScale, MaxStickScale);
        }

        public static float OverlayOpacity
        {
            get => _overlayOpacity;
            set => _overlayOpacity = Math.Clamp(value, MinOverlayOpacity, MaxOverlayOpacity);
        }

        /// <summary>The order used by settings and the touch-layout editor.</summary>
        public static readonly (TouchControl Control, string Label)[] Order =
        {
            (TouchControl.Shoot, "FIRE"),
            (TouchControl.Jump, "JUMP"),
            (TouchControl.Morph, "MORPH"),
            (TouchControl.ScanVisor, "VISOR"),
            (TouchControl.Scan, "SCAN"),
            (TouchControl.Missile, "MISSILE toggle"),
            (TouchControl.WeaponMenu, "WEAPON wheel"),
            (TouchControl.Zoom, "ZOOM"),
            (TouchControl.Pause, "MENU"),
            (TouchControl.Scoreboard, "SCORE"),
            (TouchControl.Chat, "CHAT"),
            (TouchControl.Clip, "CLIP"),
            (TouchControl.PowerBeam, "Power Beam button"),
            (TouchControl.MissileSelect, "Missile button"),
            (TouchControl.VoltDriver, "Volt Driver button"),
            (TouchControl.Battlehammer, "Battlehammer button"),
            (TouchControl.Imperialist, "Imperialist button"),
            (TouchControl.Judicator, "Judicator button"),
            (TouchControl.Magmaul, "Magmaul button"),
            (TouchControl.ShockCoil, "Shock Coil button"),
            (TouchControl.OmegaCannon, "Omega Cannon button")
        };

        private static readonly TouchControl[] _directWeapons =
        {
            TouchControl.PowerBeam,
            TouchControl.MissileSelect,
            TouchControl.VoltDriver,
            TouchControl.Battlehammer,
            TouchControl.Imperialist,
            TouchControl.Judicator,
            TouchControl.Magmaul,
            TouchControl.ShockCoil,
            TouchControl.OmegaCannon
        };

        private static readonly HashSet<TouchControl> _hidden = CreateDefaultHidden();
        private static readonly Dictionary<TouchControl, TouchButtonLayout> _layout = new();

        private static HashSet<TouchControl> CreateDefaultHidden()
        {
            var hidden = new HashSet<TouchControl>();
            foreach (TouchControl control in _directWeapons)
            {
                hidden.Add(control);
            }
            return hidden;
        }

        public static bool IsDirectWeapon(TouchControl control)
        {
            return control is TouchControl.PowerBeam
                or TouchControl.MissileSelect
                or TouchControl.VoltDriver
                or TouchControl.Battlehammer
                or TouchControl.Imperialist
                or TouchControl.Judicator
                or TouchControl.Magmaul
                or TouchControl.ShockCoil
                or TouchControl.OmegaCannon;
        }

        public static string LabelOf(TouchControl control)
        {
            foreach ((TouchControl item, string label) in Order)
            {
                if (item == control)
                {
                    return label;
                }
            }
            return control.ToString();
        }

        public static string ShortLabel(TouchControl control)
        {
            return control switch
            {
                TouchControl.Shoot => "FIRE",
                TouchControl.Jump => "JUMP",
                TouchControl.Morph => "MORPH",
                TouchControl.ScanVisor => "VISOR",
                TouchControl.Scan => "SCAN",
                TouchControl.Missile => "MSSL",
                TouchControl.WeaponMenu => "WEAPON",
                TouchControl.Zoom => "ZOOM",
                TouchControl.Pause => "MENU",
                TouchControl.Scoreboard => "SCORE",
                TouchControl.Chat => "CHAT",
                TouchControl.Clip => "CLIP",
                TouchControl.PowerBeam => "PB",
                TouchControl.MissileSelect => "MSL",
                TouchControl.VoltDriver => "VOLT",
                TouchControl.Battlehammer => "BH",
                TouchControl.Imperialist => "IMP",
                TouchControl.Judicator => "JUD",
                TouchControl.Magmaul => "MAG",
                TouchControl.ShockCoil => "COIL",
                TouchControl.OmegaCannon => "OMEGA",
                _ => control.ToString()
            };
        }

        public static bool IsEnabled(TouchControl control)
        {
            return !_hidden.Contains(control);
        }

        public static void SetEnabled(TouchControl control, bool enabled)
        {
            if (enabled)
            {
                _hidden.Remove(control);
            }
            else
            {
                _hidden.Add(control);
            }
        }

        public static bool Shown(TouchControl control)
        {
            return ButtonsVisible && IsEnabled(control);
        }

        public static Dictionary<TouchControl, TouchButtonLayout> CopyLayout()
        {
            return new Dictionary<TouchControl, TouchButtonLayout>(_layout);
        }

        public static void ReplaceLayout(IReadOnlyDictionary<TouchControl, TouchButtonLayout> layout)
        {
            _layout.Clear();
            foreach ((TouchControl control, TouchButtonLayout value) in layout)
            {
                SetLayout(control, value.X, value.Y, value.Scale);
            }
        }

        public static void SetLayout(TouchControl control, float x, float y, float scale)
        {
            _layout[control] = new TouchButtonLayout(
                Math.Clamp(x, 0f, 1f),
                Math.Clamp(y, 0f, 1f),
                Math.Clamp(scale, MinIndividualScale, MaxIndividualScale));
        }

        public static void ResetLayout(TouchControl control)
        {
            _layout.Remove(control);
        }

        public static void ResetLayout()
        {
            _layout.Clear();
        }

        public static float IndividualScale(TouchControl control,
            IReadOnlyDictionary<TouchControl, TouchButtonLayout>? layout = null)
        {
            IReadOnlyDictionary<TouchControl, TouchButtonLayout> source = layout ?? _layout;
            return source.TryGetValue(control, out TouchButtonLayout value)
                ? value.Scale : 1f;
        }

        /// <summary>
        /// The pre-editor geometry. Existing buttons deliberately retain the
        /// exact old height-anchored positions. The nine direct weapon buttons
        /// start as a compact 3x3 bank in the upper middle of the screen.
        /// </summary>
        public static TouchButtonGeometry DefaultGeometry(TouchControl control,
            float width, float height)
        {
            float w = Math.Max(1, width);
            float h = Math.Max(1, height);
            return control switch
            {
                TouchControl.Shoot or TouchControl.Scan
                    => new TouchButtonGeometry(w - 0.17f * h, h - 0.19f * h, 0.105f * h),
                TouchControl.Jump
                    => new TouchButtonGeometry(w - 0.40f * h, h - 0.15f * h, 0.085f * h),
                TouchControl.Morph
                    => new TouchButtonGeometry(w - 0.15f * h, h - 0.47f * h, 0.080f * h),
                TouchControl.ScanVisor
                    => new TouchButtonGeometry(w - 0.38f * h, h - 0.42f * h, 0.075f * h),
                TouchControl.Missile
                    => new TouchButtonGeometry(w - 0.62f * h, h - 0.28f * h, 0.075f * h),
                TouchControl.WeaponMenu
                    => new TouchButtonGeometry(w - 0.12f * h, 0.15f * h, 0.075f * h),
                TouchControl.Zoom
                    => new TouchButtonGeometry(w - 0.33f * h, 0.12f * h, 0.065f * h),
                TouchControl.Pause
                    => new TouchButtonGeometry(0.11f * h, 0.12f * h, 0.060f * h),
                TouchControl.Scoreboard
                    => new TouchButtonGeometry(0.28f * h, 0.12f * h, 0.060f * h),
                TouchControl.Chat
                    => new TouchButtonGeometry(0.45f * h, 0.12f * h, 0.060f * h),
                TouchControl.Clip
                    => new TouchButtonGeometry(0.62f * h, 0.12f * h, 0.060f * h),
                TouchControl.PowerBeam
                    => new TouchButtonGeometry(0.54f * w, 0.18f * h, 0.052f * h),
                TouchControl.MissileSelect
                    => new TouchButtonGeometry(0.62f * w, 0.18f * h, 0.052f * h),
                TouchControl.VoltDriver
                    => new TouchButtonGeometry(0.70f * w, 0.18f * h, 0.052f * h),
                TouchControl.Battlehammer
                    => new TouchButtonGeometry(0.54f * w, 0.30f * h, 0.052f * h),
                TouchControl.Imperialist
                    => new TouchButtonGeometry(0.62f * w, 0.30f * h, 0.052f * h),
                TouchControl.Judicator
                    => new TouchButtonGeometry(0.70f * w, 0.30f * h, 0.052f * h),
                TouchControl.Magmaul
                    => new TouchButtonGeometry(0.54f * w, 0.42f * h, 0.052f * h),
                TouchControl.ShockCoil
                    => new TouchButtonGeometry(0.62f * w, 0.42f * h, 0.052f * h),
                TouchControl.OmegaCannon
                    => new TouchButtonGeometry(0.70f * w, 0.42f * h, 0.052f * h),
                _ => new TouchButtonGeometry(0.5f * w, 0.5f * h, 0.06f * h)
            };
        }

        public static TouchButtonGeometry Geometry(TouchControl control,
            float width, float height)
        {
            return Geometry(control, width, height, _layout, ButtonScale);
        }

        /// <summary>
        /// Resolve a draft layout for the settings preview without changing the
        /// live overlay until Apply is pressed.
        /// </summary>
        public static TouchButtonGeometry Geometry(TouchControl control,
            float width, float height,
            IReadOnlyDictionary<TouchControl, TouchButtonLayout> layout,
            float buttonScale)
        {
            TouchButtonGeometry baseline = DefaultGeometry(control, width, height);
            float x = baseline.X;
            float y = baseline.Y;
            float individual = 1f;
            if (layout.TryGetValue(control, out TouchButtonLayout custom))
            {
                x = custom.X * width;
                y = custom.Y * height;
                individual = custom.Scale;
            }

            float radius = baseline.Radius
                * Math.Clamp(buttonScale, MinButtonScale, MaxButtonScale)
                * Math.Clamp(individual, MinIndividualScale, MaxIndividualScale);
            radius = Math.Clamp(radius, 1f, Math.Max(1f, Math.Min(width, height) * 0.24f));

            // Keep the complete hit target reachable after either global or
            // per-button scaling. Dragging against an edge therefore parks the
            // circle at the edge instead of allowing half of it off-screen.
            float minX = Math.Min(radius, width / 2f);
            float maxX = Math.Max(minX, width - minX);
            float minY = Math.Min(radius, height / 2f);
            float maxY = Math.Max(minY, height - minY);
            return new TouchButtonGeometry(
                Math.Clamp(x, minX, maxX),
                Math.Clamp(y, minY, maxY),
                radius);
        }

        public static void Reset()
        {
            ButtonsVisible = true;
            ButtonScale = 1f;
            StickScale = 1f;
            OverlayOpacity = 1f;
            _hidden.Clear();
            foreach (TouchControl control in _directWeapons)
            {
                _hidden.Add(control);
            }
            _layout.Clear();
        }

        public static string SettingKey(TouchControl control)
        {
            return $"touch_{control.ToString().ToLowerInvariant()}";
        }

        public static string LayoutKey(TouchControl control)
        {
            return $"touch_layout_{control.ToString().ToLowerInvariant()}";
        }

        public const string ButtonsSettingKey = "touch_buttons";
        public const string ButtonScaleSettingKey = "touch_button_scale";
        public const string StickScaleSettingKey = "touch_stick_scale";
        public const string OverlayOpacitySettingKey = "touch_overlay_opacity";

        public static bool ReadSetting(string key, string value)
        {
            if (key == ButtonsSettingKey)
            {
                if (Boolean.TryParse(value, out bool visible))
                {
                    ButtonsVisible = visible;
                }
                return true;
            }
            if (key == ButtonScaleSettingKey)
            {
                if (Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float scale))
                {
                    ButtonScale = scale;
                }
                return true;
            }
            if (key == StickScaleSettingKey)
            {
                if (Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float scale))
                {
                    StickScale = scale;
                }
                return true;
            }
            if (key == OverlayOpacitySettingKey)
            {
                if (Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float opacity))
                {
                    OverlayOpacity = opacity;
                }
                return true;
            }

            foreach ((TouchControl control, _) in Order)
            {
                if (key == SettingKey(control))
                {
                    if (Boolean.TryParse(value, out bool enabled))
                    {
                        SetEnabled(control, enabled);
                    }
                    return true;
                }

                if (key == LayoutKey(control))
                {
                    if (value.Equals("default", StringComparison.OrdinalIgnoreCase))
                    {
                        ResetLayout(control);
                        return true;
                    }
                    string[] parts = value.Split(',');
                    if (parts.Length == 3
                        && Single.TryParse(parts[0], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float x)
                        && Single.TryParse(parts[1], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float y)
                        && Single.TryParse(parts[2], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float scale))
                    {
                        SetLayout(control, x, y, scale);
                    }
                    return true;
                }
            }
            return false;
        }

        public static void WriteSettings(List<string> lines)
        {
            lines.Add($"{ButtonsSettingKey}={ButtonsVisible.ToString().ToLowerInvariant()}");
            lines.Add($"{ButtonScaleSettingKey}={ButtonScale.ToString("0.###", CultureInfo.InvariantCulture)}");
            lines.Add($"{StickScaleSettingKey}={StickScale.ToString("0.###", CultureInfo.InvariantCulture)}");
            lines.Add($"{OverlayOpacitySettingKey}={OverlayOpacity.ToString("0.###", CultureInfo.InvariantCulture)}");
            foreach ((TouchControl control, _) in Order)
            {
                lines.Add($"{SettingKey(control)}={IsEnabled(control).ToString().ToLowerInvariant()}");
                if (_layout.TryGetValue(control, out TouchButtonLayout layout))
                {
                    lines.Add($"{LayoutKey(control)}="
                        + $"{layout.X.ToString("0.####", CultureInfo.InvariantCulture)},"
                        + $"{layout.Y.ToString("0.####", CultureInfo.InvariantCulture)},"
                        + $"{layout.Scale.ToString("0.###", CultureInfo.InvariantCulture)}");
                }
                else
                {
                    // Always write the key. InputSettings preserves unknown
                    // lines from newer versions, so omission would resurrect an
                    // old custom position after RESET LAYOUT.
                    lines.Add($"{LayoutKey(control)}=default");
                }
            }
        }
    }
}
