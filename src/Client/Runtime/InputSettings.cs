using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MphRead.Entities;
using MphRead.Mods.Input;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods
{
    /// <summary>
    /// Keys and mouse feel, kept where a settings screen can edit them and a
    /// player can keep them.
    ///
    /// Upstream builds a fresh <see cref="ClientPlayerBindings"/> per player from
    /// <see cref="ClientPlayerBindings.GetDefault"/> and never reads a file, so
    /// rebinding anything lasted exactly as long as the process. This holds
    /// one canonical set, applies it to every set upstream creates, and writes
    /// it to controls.txt beside the executable -- deliberately its own file
    /// rather than a corner of MenuSettings, for the same reason launcher.txt
    /// is.
    ///
    /// Mouse sensitivity was a literal in the aim path with an "itodo" beside
    /// it. One multiplier lives here instead, where 1.0 is exactly the feel
    /// that literal gave.
    /// </summary>
    public static class InputSettings
    {
        /// <summary>
        /// controls.txt, beside the executable -- except where the program
        /// does not own that folder.
        ///
        /// It follows <see cref="Launcher.LauncherPrefs.Directory"/> rather
        /// than <c>AppContext.BaseDirectory</c> because an Android package's
        /// own directory is read-only: every write went to a path the app is
        /// not allowed to create, the exception was swallowed (as it has to
        /// be, see <see cref="Save"/>), and every rebind, every sensitivity
        /// and every pad binding was lost the moment the game was closed.
        /// The head points that one property at the app's data directory
        /// before anything reads, so this lands where the rest of a player's
        /// settings already do.
        /// </summary>
        private static string Path
            => System.IO.Path.Combine(Launcher.LauncherPrefs.Directory, "controls.txt");

        /// <summary>Multiplier on mouse movement. 1.0 is the original feel.</summary>
        public static float MouseSensitivity { get; set; } = 1;

        public static bool InvertMouseY { get; set; }
        public static bool InvertMouseX { get; set; }

        /// <summary>
        /// Whether the wheel cycles every weapon or only the affinity slots.
        ///
        /// On by default, which upstream's constant was not, and the
        /// difference is not a nicety: the cycling code runs only when this is
        /// set *or* the equipped weapon is neither the Power Beam nor the
        /// Missile, so with it off the wheel was dead in the hand every player
        /// spawns with -- "the scroll wheel does not change weapons", exactly.
        /// </summary>
        public static bool ScrollAllWeapons { get; set; } = true;

        /// <summary>
        /// The key that opens the chat prompt. T, which is where every
        /// shooter since Quake has put it.
        ///
        /// Not a <see cref="Keybind"/> on <see cref="ClientPlayerBindings"/> like
        /// everything else here, and deliberately so: that class is upstream's
        /// and its every member is read by <c>ProcessAllInput</c> as something
        /// the *player* does in the world. Chat is the opposite -- it takes
        /// the keyboard away from the player -- so it is handled by the window
        /// before the game sees the key at all, and a binding the game never
        /// reads has no business in the game's binding set.
        /// </summary>
        public static Keys ChatKey { get; set; } = Keys.T;

        /// <summary>
        /// How far a stick must move before it counts, 0 to 0.9.
        ///
        /// Applied radially rather than per axis -- see
        /// <see cref="Input.GamepadInput"/> for why that is not the same
        /// thing. 0.2 clears the resting drift of a worn stick without
        /// swallowing a deliberate nudge.
        /// </summary>
        public static float GamepadDeadZone
        {
            get => GamepadMoveDeadZone;
            set
            {
                GamepadMoveDeadZone = value;
                GamepadLookDeadZone = value;
            }
        }

        /// <summary>Multiplier on the right stick's turn rate. 1.0 is 210 degrees a second.</summary>
        public static float GamepadLookSensitivity
        {
            get => GamepadHorizontalSensitivity;
            set
            {
                GamepadHorizontalSensitivity = value;
                GamepadVerticalSensitivity = value;
            }
        }

        /// <summary>
        /// Invert the right stick's vertical aim. Its own setting rather than
        /// sharing the mouse's, because a great many people invert one and not
        /// the other, and there is no third thing they would rather set.
        /// </summary>
        public static bool GamepadInvertY { get; set; }

        // Controller tuning is deliberately split by purpose. The old
        // GamepadDeadZone/GamepadLookSensitivity properties below remain as
        // compatibility aliases for callers and legacy controls files.
        public static float GamepadMoveDeadZone
        {
            get => _gamepadMoveDeadZone;
            set => _gamepadMoveDeadZone = Clamp(value, 0.15f, 0, 0.9f);
        }

        private static float _gamepadMoveDeadZone = 0.15f;

        public static float GamepadLookDeadZone
        {
            get => _gamepadLookDeadZone;
            set => _gamepadLookDeadZone = Clamp(value, 0.10f, 0, 0.9f);
        }

        private static float _gamepadLookDeadZone = 0.10f;

        public static float GamepadOuterDeadZone
        {
            get => _gamepadOuterDeadZone;
            set => _gamepadOuterDeadZone = Clamp(value, 0.02f, 0, 0.5f);
        }

        private static float _gamepadOuterDeadZone = 0.02f;

        public static float GamepadMoveActivateThreshold
        {
            get => _gamepadMoveActivateThreshold;
            set
            {
                _gamepadMoveActivateThreshold = Clamp(value, 0.25f, 0, 1);
                if (_gamepadMoveReleaseThreshold > _gamepadMoveActivateThreshold)
                {
                    _gamepadMoveReleaseThreshold = _gamepadMoveActivateThreshold;
                }
            }
        }

        private static float _gamepadMoveActivateThreshold = 0.25f;

        public static float GamepadMoveReleaseThreshold
        {
            get => _gamepadMoveReleaseThreshold;
            set => _gamepadMoveReleaseThreshold = Math.Min(
                Clamp(value, 0.18f, 0, 1), _gamepadMoveActivateThreshold);
        }

        private static float _gamepadMoveReleaseThreshold = 0.18f;

        public static float GamepadLookExponent
        {
            get => _gamepadLookExponent;
            set => _gamepadLookExponent = Clamp(value, 1.60f, 0.05f, 8);
        }

        private static float _gamepadLookExponent = 1.60f;

        public static float GamepadYawRate
        {
            get => _gamepadYawRate;
            set => _gamepadYawRate = Clamp(value, 300, 0, 2000);
        }

        private static float _gamepadYawRate = 300;

        public static float GamepadPitchRate
        {
            get => _gamepadPitchRate;
            set => _gamepadPitchRate = Clamp(value, 240, 0, 2000);
        }

        private static float _gamepadPitchRate = 240;

        public static float GamepadOuterBoostStart
        {
            get => _gamepadOuterBoostStart;
            set => _gamepadOuterBoostStart = Clamp(value, 0.95f, 0, 1);
        }

        private static float _gamepadOuterBoostStart = 0.95f;

        public static float GamepadOuterYawBoost
        {
            get => _gamepadOuterYawBoost;
            set => _gamepadOuterYawBoost = Clamp(value, 150, 0, 2000);
        }

        private static float _gamepadOuterYawBoost = 150;

        public static float GamepadOuterPitchBoost
        {
            get => _gamepadOuterPitchBoost;
            set => _gamepadOuterPitchBoost = Clamp(value, 80, 0, 2000);
        }

        private static float _gamepadOuterPitchBoost = 80;

        public static float GamepadBoostDelaySeconds
        {
            get => _gamepadBoostDelaySeconds;
            set => _gamepadBoostDelaySeconds = Clamp(value, 0.18f, 0, 10);
        }

        private static float _gamepadBoostDelaySeconds = 0.18f;

        public static float GamepadBoostRampSeconds
        {
            get => _gamepadBoostRampSeconds;
            set => _gamepadBoostRampSeconds = Clamp(value, 0.12f, 0.001f, 10);
        }

        private static float _gamepadBoostRampSeconds = 0.12f;

        public static bool GamepadOuterBoostEnabled { get; set; } = true;

        public static float GamepadTriggerPressThreshold
        {
            get => _gamepadTriggerPressThreshold;
            set
            {
                _gamepadTriggerPressThreshold = Clamp(value, 0.20f, 0, 1);
                if (_gamepadTriggerReleaseThreshold > _gamepadTriggerPressThreshold)
                {
                    _gamepadTriggerReleaseThreshold = _gamepadTriggerPressThreshold;
                }
            }
        }

        private static float _gamepadTriggerPressThreshold = 0.20f;

        public static float GamepadTriggerReleaseThreshold
        {
            get => _gamepadTriggerReleaseThreshold;
            set => _gamepadTriggerReleaseThreshold = Math.Min(
                Clamp(value, 0.12f, 0, 1), _gamepadTriggerPressThreshold);
        }

        private static float _gamepadTriggerReleaseThreshold = 0.12f;

        public static float GamepadHorizontalSensitivity
        {
            get => _gamepadHorizontalSensitivity;
            set => _gamepadHorizontalSensitivity = Clamp(value, 1, 0.01f, 10);
        }

        private static float _gamepadHorizontalSensitivity = 1;

        public static float GamepadVerticalSensitivity
        {
            get => _gamepadVerticalSensitivity;
            set => _gamepadVerticalSensitivity = Clamp(value, 1, 0.01f, 10);
        }

        private static float _gamepadVerticalSensitivity = 1;

        // More descriptive aliases for settings/UI callers.
        public static float GamepadLookSensitivityHorizontal
        {
            get => GamepadHorizontalSensitivity;
            set => GamepadHorizontalSensitivity = value;
        }

        public static float GamepadLookSensitivityVertical
        {
            get => GamepadVerticalSensitivity;
            set => GamepadVerticalSensitivity = value;
        }

        public static float GamepadZoomMultiplier
        {
            get => _gamepadZoomMultiplier;
            set => _gamepadZoomMultiplier = Clamp(value, 1, 0.01f, 10);
        }

        private static float _gamepadZoomMultiplier = 1;

        // P0 only exposes and persists these placeholders. Behaviour belongs
        // to the later aim-assist pass.
        public static bool GamepadAimAssistEnabled { get; set; } = true;
        public static float GamepadAimAssistStrength
        {
            get => _gamepadAimAssistStrength;
            set => _gamepadAimAssistStrength = Clamp(value, 1, 0, 1);
        }

        private static float _gamepadAimAssistStrength = 1;

        /// <summary>SDL gamepad gyro is opt-in; unsupported hosts leave it inert.</summary>
        public static bool GamepadGyroEnabled { get; set; }
        public static float GamepadGyroSensitivity
        {
            get => _gamepadGyroSensitivity;
            set => _gamepadGyroSensitivity = Clamp(value, 1, 0.01f, 10);
        }
        private static float _gamepadGyroSensitivity = 1;
        public static bool GamepadGyroInvertX { get; set; }
        public static bool GamepadGyroInvertY { get; set; }
        public static bool GamepadHapticsEnabled { get; set; } = true;
        public static bool InputBalanceTelemetryEnabled { get; set; }

        public static bool StylusAimingEnabled { get; set; } = true;
        public static float StylusSensitivity
        {
            get => _stylusSensitivity;
            set => _stylusSensitivity = Clamp(value, 1, 0.01f, 10);
        }
        private static float _stylusSensitivity = 1;
        public static bool StylusInvertY { get; set; }
        public static Input.StylusAction StylusPrimaryAction { get; set; }
            = Input.StylusAction.Fire;
        public static Input.StylusAction StylusSecondaryAction { get; set; }
            = Input.StylusAction.Zoom;
        public static bool StylusClassicGestures { get; set; } = true;
        public static bool StylusDoubleTapJump { get; set; } = true;
        public static bool StylusFlickBoost { get; set; } = true;
        public static bool StylusPressureToFire { get; set; }
        public static float StylusPressureThreshold
        {
            get => _stylusPressureThreshold;
            set => _stylusPressureThreshold = Clamp(value, 0.35f, 0, 1);
        }
        private static float _stylusPressureThreshold = 0.35f;

        public static Input.StylusBindings CurrentStylusBindings
            => new(StylusPrimaryAction, StylusSecondaryAction);

        public static Input.ControllerPreset ControllerPreset
        {
            get => _controllerPreset;
            set => _controllerPreset = Enum.IsDefined(value)
                ? value : Input.ControllerPreset.Classic;
        }

        public static Input.ControllerPreset GamepadPreset
        {
            get => ControllerPreset;
            set => ControllerPreset = value;
        }

        private static Input.ControllerPreset _controllerPreset = Input.ControllerPreset.Classic;
        private static bool _applyingPreset;

        private static float Clamp(float value, float fallback, float minimum,
            float maximum)
            => !float.IsFinite(value) ? fallback : Math.Clamp(value, minimum, maximum);

        static InputSettings()
        {
            Input.PadBindings.Changed += () =>
            {
                if (!_applyingPreset)
                {
                    _controllerPreset = Input.ControllerPreset.Custom;
                }
            };
        }

        private static bool _creating;
        private static ClientPlayerBindings? _current;

        /// <summary>
        /// The bindings every player is created with. The settings screen
        /// edits this set; <see cref="Apply"/> copies it onto each set upstream
        /// creates, and <see cref="ApplyToPlayers"/> onto the ones that already
        /// exist.
        /// </summary>
        public static ClientPlayerBindings Current
        {
            get
            {
                if (_current == null)
                {
                    // GetDefault calls Apply, which asks for Current: build the
                    // canonical set without letting that come back around.
                    _creating = true;
                    _current = ClientPlayerBindings.GetDefault();
                    _creating = false;
                }
                return _current;
            }
        }

        /// <summary>Every rebindable control, in the order a screen should list them.</summary>
        public static IReadOnlyList<PropertyInfo> Bindings => _bindings ??= FindBindings();

        private static PropertyInfo[]? _bindings;

        /// <summary>
        /// The ones worth putting first. Everything else -- the nine weapon
        /// slots, the roll and aim keys -- follows in declaration order, so a
        /// control added upstream shows up without being listed here.
        /// </summary>
        private static readonly string[] _order =
        {
            nameof(ClientPlayerBindings.MoveUp), nameof(ClientPlayerBindings.MoveDown),
            nameof(ClientPlayerBindings.MoveLeft), nameof(ClientPlayerBindings.MoveRight),
            nameof(ClientPlayerBindings.Jump), nameof(ClientPlayerBindings.Boost),
            nameof(ClientPlayerBindings.Shoot), nameof(ClientPlayerBindings.Zoom),
            nameof(ClientPlayerBindings.Morph), nameof(ClientPlayerBindings.AltAttack),
            nameof(ClientPlayerBindings.NextWeapon), nameof(ClientPlayerBindings.PrevWeapon),
            nameof(ClientPlayerBindings.WeaponMenu),
            nameof(ClientPlayerBindings.Pause), nameof(ClientPlayerBindings.HudOverlay)
        };

        private static PropertyInfo[] FindBindings()
        {
            PropertyInfo[] all = typeof(ClientPlayerBindings)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(Keybind))
                .ToArray();
            return all
                .OrderBy(p =>
                {
                    int index = Array.IndexOf(_order, p.Name);
                    return index < 0 ? _order.Length : index;
                })
                .ToArray();
        }

        public static Keybind Bind(PropertyInfo property)
        {
            return (Keybind)property.GetValue(Current)!;
        }

        /// <summary>"Left Shift", "Mouse left", "Scroll up", "1".</summary>
        public static string Describe(Keybind bind)
        {
            switch (bind.Type)
            {
                case ButtonType.Mouse:
                    // OpenTK's enum names these Button1..Button8 and aliases
                    // the first three; ToString picks the number, which is not
                    // what anybody calls them.
                    return bind.MouseButton switch
                    {
                        MouseButton.Left => "Mouse left",
                        MouseButton.Right => "Mouse right",
                        MouseButton.Middle => "Mouse middle",
                        _ => $"Mouse {(int)bind.MouseButton + 1}"
                    };
                case ButtonType.ScrollUp:
                    return "Scroll up";
                case ButtonType.ScrollDown:
                    return "Scroll down";
                default:
                    return bind.Key == Keys.Unknown ? "unbound" : KeyName(bind.Key);
            }
        }

        /// <summary>"D1" -> "1", "LeftShift" -> "Left shift", "KeyPad4" -> "Key pad 4".</summary>
        public static string KeyName(Keys key)
        {
            string name = key.ToString();
            if (name.Length == 2 && name[0] == 'D' && Char.IsDigit(name[1]))
            {
                return name[1].ToString();
            }
            var builder = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && Char.IsUpper(name[i]) && !Char.IsUpper(name[i - 1]))
                {
                    builder.Append(' ');
                    builder.Append(Char.ToLowerInvariant(name[i]));
                }
                else
                {
                    builder.Append(name[i]);
                }
            }
            return builder.ToString();
        }

        /// <summary>"Humanise" a control's name for a screen: "AltAttack" -> "Alt attack".</summary>
        public static string ActionName(PropertyInfo property)
        {
            string name = property.Name == nameof(ClientPlayerBindings.Pause)
                ? "Scoreboard"
                : property.Name == nameof(ClientPlayerBindings.RolltLeft) ? "Roll left" : property.Name;
            var builder = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && Char.IsUpper(name[i]) && !Char.IsUpper(name[i - 1]))
                {
                    builder.Append(' ');
                    builder.Append(Char.ToLowerInvariant(name[i]));
                }
                else
                {
                    builder.Append(i == 0 ? Char.ToUpperInvariant(name[i]) : name[i]);
                }
            }
            return builder.ToString();
        }

        /// <summary>Point a control at a key, a mouse button or the wheel.</summary>
        public static void Rebind(PropertyInfo property, ButtonType type, Keys key,
            MouseButton button)
        {
            Keybind bind = Bind(property);
            bind.Type = type;
            bind.Key = type == ButtonType.Key ? key : Keys.Unknown;
            bind.MouseButton = button;
        }

        /// <summary>
        /// Copy the canonical bindings onto a set upstream just created. Called
        /// from GetDefault, so it covers every player in every match.
        /// </summary>
        public static void Apply(ClientPlayerBindings controls)
        {
            if (_creating || _current == null)
            {
                return;
            }
            foreach (PropertyInfo property in Bindings)
            {
                var source = (Keybind)property.GetValue(_current)!;
                var target = (Keybind)property.GetValue(controls)!;
                target.Type = source.Type;
                target.Key = source.Key;
                target.MouseButton = source.MouseButton;
            }
            controls.ScrollAllWeapons = ScrollAllWeapons;
        }

        /// <summary>
        /// Push the current bindings onto players that already exist.
        ///
        /// <see cref="Apply"/> copies values into each player's own Keybind
        /// objects, so editing this set afterwards reaches nobody -- and a
        /// rebind made from the pause menu is by definition made in the middle
        /// of a match, where waiting for the next one is not an answer.
        /// </summary>
        public static void ApplyToPlayers(Scene scene)
        {
            try
            {
                for (int i = 0; i < scene.Players.Count; i++)
                {
                    Apply(scene.Players[i].GetPresentation().Bindings);
                }
            }
            catch (Exception)
            {
                // The pause menu's settings window runs on its own thread, so
                // this can land in the middle of a room load rebuilding the
                // player list. Losing the push is nothing -- the bindings are
                // saved, and ClientPlayerBindings.GetDefault applies them to every
                // set the load is creating anyway.
            }
        }

        public static void Load()
        {
            if (!File.Exists(Path))
            {
                return;
            }
            try
            {
                LoadLines(File.ReadAllLines(Path));
            }
            catch (Exception)
            {
                // Bindings are a convenience; an unreadable file must not stop
                // the game from starting. Every exception, not only IOException:
                // a folder the user cannot read raises UnauthorizedAccessException.
            }
        }

        /// <summary>
        /// Parse controls-file lines without touching the filesystem. This is
        /// also the migration boundary used by the launcher and focused tests.
        /// Modern values are collected before legacy aliases are applied, so a
        /// modern key wins regardless of file ordering.
        /// </summary>
        public static void LoadLines(IEnumerable<string> lines)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            float? legacyDeadzone = null;
            float? legacyLook = null;
            bool modernMoveDeadzone = false, modernLookDeadzone = false;
            bool modernHorizontal = false, modernVertical = false;
            var explicitBindings = new HashSet<Input.PadAction>();
            Input.ControllerPreset? requestedPreset = null;
            _applyingPreset = true;
            try
            {
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    int split = line.IndexOf('=');
                    if (line.Length == 0 || line[0] == '#' || split <= 0)
                    {
                        continue;
                    }
                    string key = line[..split].Trim();
                    string value = line[(split + 1)..].Trim();
                    if (key == "sensitivity")
                    {
                        if (Single.TryParse(value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float parsed))
                            MouseSensitivity = Math.Clamp(parsed, 0.05f, 10f);
                        continue;
                    }
                    if (key == "invert_y" && Boolean.TryParse(value, out bool invertY))
                    {
                        InvertMouseY = invertY;
                        continue;
                    }
                    if (key == "invert_x" && Boolean.TryParse(value, out bool invertX))
                    {
                        InvertMouseX = invertX;
                        continue;
                    }
                    if (key == "scroll_all_weapons" && Boolean.TryParse(value, out bool scrollAll))
                    {
                        ScrollAllWeapons = scrollAll;
                        continue;
                    }
                    if (key == "gamepad" || key == "input_schema")
                    {
                        continue;
                    }
                    if ((key == "controller_preset" || key == "gamepad_preset")
                        && Enum.TryParse(value, ignoreCase: true, out Input.ControllerPreset parsedPreset)
                        && Enum.IsDefined(parsedPreset))
                    {
                        requestedPreset = parsedPreset;
                        continue;
                    }
                    if (Input.PadBindings.TryLoad(key, value))
                    {
                        if (Enum.TryParse(key[4..], out Input.PadAction action))
                            explicitBindings.Add(action);
                        continue;
                    }
                    if (Input.TouchSettings.ReadSetting(key, value))
                    {
                        continue;
                    }
                    if (key == "gamepad_deadzone"
                        && Single.TryParse(value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float deadZone))
                    {
                        legacyDeadzone = deadZone;
                        continue;
                    }
                    if (key == "gamepad_look"
                        && Single.TryParse(value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float look))
                    {
                        legacyLook = look;
                        continue;
                    }
                    if (key == "gamepad_invert_y" && Boolean.TryParse(value, out bool padInvert))
                    {
                        GamepadInvertY = padInvert;
                        continue;
                    }
                    if (TryModernSetting(key, value, ref modernMoveDeadzone,
                        ref modernLookDeadzone, ref modernHorizontal, ref modernVertical))
                    {
                        continue;
                    }
                    if (key == "chat_key")
                    {
                        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
                            ChatKey = Keys.Unknown;
                        else if (Enum.TryParse(value, out Keys chatKey)) ChatKey = chatKey;
                        continue;
                    }
                    PropertyInfo? property = Bindings.FirstOrDefault(p => p.Name == key);
                    if (property != null) ParseBind(property, value);
                }

                if (legacyDeadzone.HasValue)
                {
                    if (!modernMoveDeadzone) GamepadMoveDeadZone = legacyDeadzone.Value;
                    if (!modernLookDeadzone) GamepadLookDeadZone = legacyDeadzone.Value;
                }
                if (legacyLook.HasValue)
                {
                    if (!modernHorizontal) GamepadHorizontalSensitivity = legacyLook.Value;
                    if (!modernVertical) GamepadVerticalSensitivity = legacyLook.Value;
                }
                if (requestedPreset.HasValue)
                {
                    ApplyPresetCore(requestedPreset.Value, explicitBindings);
                }
                else if (explicitBindings.Count != 0)
                {
                    _controllerPreset = Input.ControllerPreset.Custom;
                }
            }
            finally
            {
                _applyingPreset = false;
            }
        }

        private static bool TryModernSetting(string key, string value,
            ref bool modernMoveDeadzone, ref bool modernLookDeadzone,
            ref bool modernHorizontal, ref bool modernVertical)
        {
            bool parsed = Single.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float number);
            bool boolean = Boolean.TryParse(value, out bool flag);
            switch (key)
            {
                case "gamepad_move_deadzone":
                    if (parsed) GamepadMoveDeadZone = number;
                    modernMoveDeadzone = true;
                    return true;
                case "gamepad_look_deadzone":
                    if (parsed) GamepadLookDeadZone = number;
                    modernLookDeadzone = true;
                    return true;
                case "gamepad_outer_deadzone": if (parsed) GamepadOuterDeadZone = number; return true;
                case "gamepad_move_activate":
                case "gamepad_move_activate_threshold":
                    if (parsed) GamepadMoveActivateThreshold = number; return true;
                case "gamepad_move_release":
                case "gamepad_move_release_threshold":
                    if (parsed) GamepadMoveReleaseThreshold = number; return true;
                case "gamepad_look_exponent":
                case "gamepad_response_exponent":
                    if (parsed) GamepadLookExponent = number; return true;
                case "gamepad_yaw_rate":
                case "gamepad_yaw":
                    if (parsed) GamepadYawRate = number; return true;
                case "gamepad_pitch_rate":
                case "gamepad_pitch":
                    if (parsed) GamepadPitchRate = number; return true;
                case "gamepad_outer_boost_start": if (parsed) GamepadOuterBoostStart = number; return true;
                case "gamepad_outer_yaw_boost": if (parsed) GamepadOuterYawBoost = number; return true;
                case "gamepad_outer_pitch_boost": if (parsed) GamepadOuterPitchBoost = number; return true;
                case "gamepad_boost_delay":
                case "gamepad_outer_boost_delay": if (parsed) GamepadBoostDelaySeconds = number; return true;
                case "gamepad_boost_ramp":
                case "gamepad_outer_boost_ramp": if (parsed) GamepadBoostRampSeconds = number; return true;
                case "gamepad_outer_boost_enabled": if (boolean) GamepadOuterBoostEnabled = flag; return true;
                case "gamepad_trigger_press":
                case "gamepad_trigger_press_threshold":
                    if (parsed) GamepadTriggerPressThreshold = number; return true;
                case "gamepad_trigger_release":
                case "gamepad_trigger_release_threshold":
                    if (parsed) GamepadTriggerReleaseThreshold = number; return true;
                case "gamepad_horizontal_sensitivity":
                case "gamepad_sensitivity_horizontal":
                    if (parsed) GamepadHorizontalSensitivity = number;
                    modernHorizontal = true;
                    return true;
                case "gamepad_vertical_sensitivity":
                case "gamepad_sensitivity_vertical":
                    if (parsed) GamepadVerticalSensitivity = number;
                    modernVertical = true;
                    return true;
                case "gamepad_zoom_multiplier":
                case "gamepad_zoom":
                    if (parsed) GamepadZoomMultiplier = number; return true;
                case "gamepad_aim_assist":
                case "gamepad_aim_assist_enabled":
                    if (boolean) GamepadAimAssistEnabled = flag; return true;
                case "gamepad_aim_assist_strength": if (parsed) GamepadAimAssistStrength = number; return true;
                case "gamepad_gyro":
                case "gamepad_gyro_enabled": if (boolean) GamepadGyroEnabled = flag; return true;
                case "gamepad_gyro_sensitivity": if (parsed) GamepadGyroSensitivity = number; return true;
                case "gamepad_gyro_invert_x": if (boolean) GamepadGyroInvertX = flag; return true;
                case "gamepad_gyro_invert_y": if (boolean) GamepadGyroInvertY = flag; return true;
                case "gamepad_haptics":
                case "gamepad_haptics_enabled": if (boolean) GamepadHapticsEnabled = flag; return true;
                case "input_balance_telemetry": if (boolean) InputBalanceTelemetryEnabled = flag; return true;
                case "stylus_aiming": if (boolean) StylusAimingEnabled = flag; return true;
                case "stylus_sensitivity": if (parsed) StylusSensitivity = number; return true;
                case "stylus_invert_y": if (boolean) StylusInvertY = flag; return true;
                case "stylus_primary":
                    if (Enum.TryParse(value, true, out Input.StylusAction primary)) StylusPrimaryAction = primary;
                    return true;
                case "stylus_secondary":
                    if (Enum.TryParse(value, true, out Input.StylusAction secondary)) StylusSecondaryAction = secondary;
                    return true;
                case "stylus_classic_gestures": if (boolean) StylusClassicGestures = flag; return true;
                case "stylus_double_tap_jump": if (boolean) StylusDoubleTapJump = flag; return true;
                case "stylus_flick_boost": if (boolean) StylusFlickBoost = flag; return true;
                case "stylus_pressure_to_fire": if (boolean) StylusPressureToFire = flag; return true;
                case "stylus_pressure_threshold": if (parsed) StylusPressureThreshold = number; return true;
                default: return false;
            }
        }

        public static void ApplyPreset(Input.ControllerPreset preset)
        {
            if (!Enum.IsDefined(preset))
            {
                preset = Input.ControllerPreset.Classic;
            }
            if (preset == Input.ControllerPreset.Custom)
            {
                _controllerPreset = preset;
                return;
            }
            _applyingPreset = true;
            try
            {
                ApplyPresetCore(preset, preserve: null);
            }
            finally
            {
                _applyingPreset = false;
            }
        }

        private static void ApplyPresetCore(Input.ControllerPreset preset,
            HashSet<Input.PadAction>? preserve)
        {
            // Custom is a label for the current bindings, not a default layout.
            // Partial settings files must leave omitted bindings unchanged.
            if (preset == Input.ControllerPreset.Custom)
            {
                _controllerPreset = preset;
                return;
            }
            foreach (Input.PadAction action in Input.PadBindings.Actions)
            {
                if (preserve?.Contains(action) == true) continue;
                GamepadButtons value = Input.PadBindings.Default(action);
                if (preset == Input.ControllerPreset.Competitive)
                {
                    value = action switch
                    {
                        Input.PadAction.Jump => GamepadButtons.LeftBumper | GamepadButtons.A,
                        Input.PadAction.PrevWeapon => GamepadButtons.DpadLeft,
                        _ => value
                    };
                }
                Input.PadBindings.SetFromPreset(action, value);
            }
            _controllerPreset = preset;
        }

        private static void ParseBind(PropertyInfo property, string value)
        {
            string[] parts = value.Split(':', 2);
            string type = parts[0].Trim();
            string name = parts.Length > 1 ? parts[1].Trim() : "";
            if (type == "ScrollUp")
            {
                Rebind(property, ButtonType.ScrollUp, Keys.Unknown, MouseButton.Left);
            }
            else if (type == "ScrollDown")
            {
                Rebind(property, ButtonType.ScrollDown, Keys.Unknown, MouseButton.Left);
            }
            else if (type == "Mouse" && Enum.TryParse(name, out MouseButton button))
            {
                Rebind(property, ButtonType.Mouse, Keys.Unknown, button);
            }
            else if (type == "Key" && Enum.TryParse(name, out Keys key))
            {
                Rebind(property, ButtonType.Key, key, MouseButton.Left);
            }
        }

        public static void Save()
        {
            try
            {
                var lines = GetSaveLines();
                File.WriteAllLines(Path, lines);
            }
            catch (Exception)
            {
                // Same reason as Load: the folder beside the executable is not
                // guaranteed to be writable, and losing a rebind is a far
                // smaller thing than taking down the window that made it --
                // which, from the pause menu, is the thread the menu runs on.
            }
        }

        /// <summary>
        /// Serialize settings without touching the filesystem. Keeping this
        /// separate makes the schema/migration boundary testable and ensures
        /// every save writes the modern controller values.
        /// </summary>
        public static IReadOnlyList<string> GetSaveLines()
        {
            var lines = new List<string>
            {
                    $"# {Branding.Name} controls. Delete a line to go back to the default.",
                    "input_schema=4",
                    $"sensitivity={MouseSensitivity.ToString("0.###", CultureInfo.InvariantCulture)}",
                    $"invert_y={InvertMouseY.ToString().ToLowerInvariant()}",
                    $"invert_x={InvertMouseX.ToString().ToLowerInvariant()}",
                    $"scroll_all_weapons={ScrollAllWeapons.ToString().ToLowerInvariant()}",
                    $"chat_key={(ChatKey == Keys.Unknown ? "none" : ChatKey.ToString())}",
                    $"controller_preset={ControllerPreset}",
                    "gamepad_move_deadzone=" + GamepadMoveDeadZone.ToString("0.###", CultureInfo.InvariantCulture),
                    "gamepad_look_deadzone=" + GamepadLookDeadZone.ToString("0.###", CultureInfo.InvariantCulture),
                    "gamepad_outer_deadzone=" + GamepadOuterDeadZone.ToString("0.###", CultureInfo.InvariantCulture),
                    "gamepad_move_activate=" + GamepadMoveActivateThreshold.ToString("0.###", CultureInfo.InvariantCulture),
                    "gamepad_move_release=" + GamepadMoveReleaseThreshold.ToString("0.###", CultureInfo.InvariantCulture),
                    "gamepad_look_exponent=" + GamepadLookExponent.ToString("0.###", CultureInfo.InvariantCulture),
                    "gamepad_yaw_rate=" + GamepadYawRate.ToString("0.###", CultureInfo.InvariantCulture),
                    "gamepad_pitch_rate=" + GamepadPitchRate.ToString("0.###", CultureInfo.InvariantCulture),
                    "gamepad_outer_boost_start=" + GamepadOuterBoostStart.ToString("0.###", CultureInfo.InvariantCulture),
                    "gamepad_outer_yaw_boost=" + GamepadOuterYawBoost.ToString("0.###", CultureInfo.InvariantCulture),
                    "gamepad_outer_pitch_boost=" + GamepadOuterPitchBoost.ToString("0.###", CultureInfo.InvariantCulture),
                    "gamepad_boost_delay=" + GamepadBoostDelaySeconds.ToString("0.###", CultureInfo.InvariantCulture),
                    "gamepad_boost_ramp=" + GamepadBoostRampSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                    $"gamepad_outer_boost_enabled={GamepadOuterBoostEnabled.ToString().ToLowerInvariant()}",
                    "gamepad_trigger_press=" + GamepadTriggerPressThreshold.ToString("0.###", CultureInfo.InvariantCulture),
                    "gamepad_trigger_release=" + GamepadTriggerReleaseThreshold.ToString("0.###", CultureInfo.InvariantCulture),
                    "gamepad_horizontal_sensitivity=" + GamepadHorizontalSensitivity.ToString("0.###", CultureInfo.InvariantCulture),
                    "gamepad_vertical_sensitivity=" + GamepadVerticalSensitivity.ToString("0.###", CultureInfo.InvariantCulture),
                    "gamepad_zoom_multiplier=" + GamepadZoomMultiplier.ToString("0.###", CultureInfo.InvariantCulture),
                    $"gamepad_aim_assist={GamepadAimAssistEnabled.ToString().ToLowerInvariant()}",
                    "gamepad_aim_assist_strength=" + GamepadAimAssistStrength.ToString("0.###", CultureInfo.InvariantCulture),
                    $"gamepad_gyro_enabled={GamepadGyroEnabled.ToString().ToLowerInvariant()}",
                    "gamepad_gyro_sensitivity=" + GamepadGyroSensitivity.ToString("0.###", CultureInfo.InvariantCulture),
                    $"gamepad_gyro_invert_x={GamepadGyroInvertX.ToString().ToLowerInvariant()}",
                    $"gamepad_gyro_invert_y={GamepadGyroInvertY.ToString().ToLowerInvariant()}",
                    $"gamepad_haptics_enabled={GamepadHapticsEnabled.ToString().ToLowerInvariant()}",
                    $"input_balance_telemetry={InputBalanceTelemetryEnabled.ToString().ToLowerInvariant()}",
                    $"stylus_aiming={StylusAimingEnabled.ToString().ToLowerInvariant()}",
                    "stylus_sensitivity=" + StylusSensitivity.ToString("0.###", CultureInfo.InvariantCulture),
                    $"stylus_invert_y={StylusInvertY.ToString().ToLowerInvariant()}",
                    $"stylus_primary={StylusPrimaryAction}",
                    $"stylus_secondary={StylusSecondaryAction}",
                    $"stylus_classic_gestures={StylusClassicGestures.ToString().ToLowerInvariant()}",
                    $"stylus_double_tap_jump={StylusDoubleTapJump.ToString().ToLowerInvariant()}",
                    $"stylus_flick_boost={StylusFlickBoost.ToString().ToLowerInvariant()}",
                    $"stylus_pressure_to_fire={StylusPressureToFire.ToString().ToLowerInvariant()}",
                    "stylus_pressure_threshold=" + StylusPressureThreshold.ToString("0.###", CultureInfo.InvariantCulture),
                    "gamepad_deadzone=" + GamepadDeadZone.ToString(CultureInfo.InvariantCulture),
                    "gamepad_look=" + GamepadLookSensitivity.ToString(CultureInfo.InvariantCulture),
                    $"gamepad_invert_y={GamepadInvertY.ToString().ToLowerInvariant()}"
            };
            foreach (Input.PadAction action in Input.PadBindings.Actions)
            {
                lines.Add($"{Input.PadBindings.SettingKey(action)}="
                    + Input.PadBindings.Get(action));
            }
            Input.TouchSettings.WriteSettings(lines);
            foreach (PropertyInfo property in Bindings)
            {
                Keybind bind = Bind(property);
                string value = bind.Type switch
                {
                    ButtonType.Mouse => $"Mouse:{bind.MouseButton}",
                    ButtonType.ScrollUp => "ScrollUp",
                    ButtonType.ScrollDown => "ScrollDown",
                    _ => $"Key:{bind.Key}"
                };
                lines.Add($"{property.Name}={value}");
            }
            return lines;
        }

        /// <summary>Put everything back the way it shipped.</summary>
        public static void Reset()
        {
            _creating = true;
            _current = ClientPlayerBindings.GetDefault();
            _creating = false;
            MouseSensitivity = 1;
            InvertMouseY = false;
            InvertMouseX = false;
            ScrollAllWeapons = true;
            ChatKey = Keys.T;
            _applyingPreset = true;
            try
            {
                Input.PadBindings.Reset();
            }
            finally
            {
                _applyingPreset = false;
            }
            Input.TouchSettings.Reset();
            GamepadMoveDeadZone = 0.15f;
            GamepadLookDeadZone = 0.10f;
            GamepadOuterDeadZone = 0.02f;
            GamepadMoveActivateThreshold = 0.25f;
            GamepadMoveReleaseThreshold = 0.18f;
            GamepadLookExponent = 1.60f;
            GamepadYawRate = 300;
            GamepadPitchRate = 240;
            GamepadOuterBoostStart = 0.95f;
            GamepadOuterYawBoost = 150;
            GamepadOuterPitchBoost = 80;
            GamepadBoostDelaySeconds = 0.18f;
            GamepadBoostRampSeconds = 0.12f;
            GamepadOuterBoostEnabled = true;
            GamepadTriggerPressThreshold = 0.20f;
            GamepadTriggerReleaseThreshold = 0.12f;
            GamepadHorizontalSensitivity = 1f;
            GamepadVerticalSensitivity = 1f;
            GamepadZoomMultiplier = 1f;
            GamepadAimAssistEnabled = true;
            GamepadAimAssistStrength = 1f;
            GamepadGyroEnabled = false;
            GamepadGyroSensitivity = 1f;
            GamepadGyroInvertX = false;
            GamepadGyroInvertY = false;
            GamepadHapticsEnabled = true;
            InputBalanceTelemetryEnabled = false;
            GamepadInvertY = false;
            StylusAimingEnabled = true;
            StylusSensitivity = 1;
            StylusInvertY = false;
            StylusPrimaryAction = Input.StylusAction.Fire;
            StylusSecondaryAction = Input.StylusAction.Zoom;
            StylusClassicGestures = true;
            StylusDoubleTapJump = true;
            StylusFlickBoost = true;
            StylusPressureToFire = false;
            StylusPressureThreshold = 0.35f;
            _controllerPreset = Input.ControllerPreset.Classic;
        }
    }
}
