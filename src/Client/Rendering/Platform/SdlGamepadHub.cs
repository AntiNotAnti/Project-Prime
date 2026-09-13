using System;
using System.Collections.Generic;
using MphRead.Mods.Input;
using SDL;
using PrimeGamepadState = MphRead.Mods.Input.GamepadState;

namespace MphRead;

/// <summary>
/// Host-thread owner for SDL gamepads. The host remains responsible for
/// polling SDL and deciding when a gamepad event belongs to the active window,
/// while this type owns native handle lifetime, active selection, neutral
/// mapping, sensor state, and rumble.
/// </summary>
internal unsafe sealed class SdlGamepadHub : IDisposable
{
    private readonly Dictionary<uint, IntPtr> _handles = new();
    private PrimeGamepadState _state;
    private uint _activeId;
    private bool _hasActive;
    private bool _gyroEnabled;

    internal PrimeGamepadState State => _state;
    internal GamepadButtons Buttons => _state.Buttons;
    internal bool HasActive => _hasActive;

    /// <summary>Opens a newly reported SDL gamepad and selects it if needed.</summary>
    internal bool Open(SDL_JoystickID id, out SdlGamepadCapabilities capabilities)
    {
        capabilities = default;
        uint rawId = (uint)id;
        if (_handles.ContainsKey(rawId)) return false;
        SDL_Gamepad* handle = SDL3.SDL_OpenGamepad(id);
        if (handle == null) return false;
        _handles.Add(rawId, (IntPtr)handle);
        if (_hasActive) return false;

        _activeId = rawId;
        _hasActive = true;
        _state = StateFor(handle);
        capabilities = CapabilitiesFor(rawId, handle);
        // Bias belongs to the physical device. A newly active device must
        // never inherit calibration from the previous one.
        GamepadGyro.ResetDevice();
        return true;
    }

    /// <summary>
    /// Closes one SDL gamepad. If the active device was removed, the next
    /// retained handle becomes active and its capability snapshot is returned.
    /// </summary>
    internal SdlGamepadCapabilities? Close(SDL_JoystickID id, out bool wasActive)
    {
        uint rawId = (uint)id;
        wasActive = _hasActive && rawId == _activeId;
        if (wasActive)
        {
            SetGyroSensor(false);
            StopRumble();
        }
        if (_handles.Remove(rawId, out IntPtr pointer))
            SDL3.SDL_CloseGamepad((SDL_Gamepad*)pointer);
        if (!wasActive) return null;

        GamepadGyro.ResetDevice();
        _hasActive = false;
        _activeId = 0;
        _state = default;
        foreach (KeyValuePair<uint, IntPtr> gamepad in _handles)
        {
            _activeId = gamepad.Key;
            _hasActive = true;
            SDL_Gamepad* handle = (SDL_Gamepad*)gamepad.Value;
            _state = StateFor(handle);
            return CapabilitiesFor(gamepad.Key, handle);
        }
        return null;
    }

    internal bool IsActive(SDL_JoystickID id)
        => _hasActive && (uint)id == _activeId;

    internal void HandleAxis(SDL_GamepadAxisEvent evt)
    {
        if (!_hasActive || (uint)evt.which != _activeId) return;
        _state.Connected = true;
        float normalized = evt.value / 32767f;
        switch ((SDL_GamepadAxis)evt.axis)
        {
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX: _state.LeftX = normalized; break;
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY: _state.LeftY = -normalized; break;
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX: _state.RightX = normalized; break;
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY: _state.RightY = -normalized; break;
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER: _state.LeftTrigger = Math.Clamp(normalized, 0, 1); break;
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER: _state.RightTrigger = Math.Clamp(normalized, 0, 1); break;
        }
    }

    internal void HandleButton(SDL_GamepadButtonEvent evt)
    {
        if (!_hasActive || (uint)evt.which != _activeId) return;
        _state.Connected = true;
        GamepadButtons button = (SDL_GamepadButton)evt.button switch
        {
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH => GamepadButtons.A,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST => GamepadButtons.B,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST => GamepadButtons.X,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH => GamepadButtons.Y,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER => GamepadButtons.LeftBumper,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER => GamepadButtons.RightBumper,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK => GamepadButtons.Back,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START => GamepadButtons.Start,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK => GamepadButtons.LeftThumb,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK => GamepadButtons.RightThumb,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP => GamepadButtons.DpadUp,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT => GamepadButtons.DpadRight,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN => GamepadButtons.DpadDown,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT => GamepadButtons.DpadLeft,
            _ => GamepadButtons.None
        };
        if (button == GamepadButtons.None) return;
        if (evt.down) _state.Buttons |= button;
        else _state.Buttons &= ~button;
    }

    internal void HandleSensor(SDL_GamepadSensorEvent evt)
    {
        if (!_hasActive || !_gyroEnabled || (uint)evt.which != _activeId
            || evt.sensor != (int)SDL_SensorType.SDL_SENSOR_GYRO) return;
        // SDL specifies gamepad gyro values in radians/second around the
        // controller's right-handed X/Y/Z axes. Preserve its sensor timestamp
        // instead of assigning render arrival time.
        double seconds = (evt.sensor_timestamp != 0
            ? evt.sensor_timestamp : evt.timestamp) / 1_000_000_000d;
        GamepadGyro.SubmitRadiansPerSecond(
            new OpenTK.Mathematics.Vector3(evt.data[0], evt.data[1], evt.data[2]), seconds);
    }

    internal void SetGyroSensor(bool enabled)
    {
        if (!_hasActive || !_handles.TryGetValue(_activeId, out IntPtr pointer))
        {
            _gyroEnabled = false;
            GamepadGyro.Reset();
            return;
        }
        SDL_Gamepad* handle = (SDL_Gamepad*)pointer;
        bool applied = SDL3.SDL_SetGamepadSensorEnabled(handle,
            SDL_SensorType.SDL_SENSOR_GYRO, enabled);
        _gyroEnabled = enabled && applied;
        if (!_gyroEnabled) GamepadGyro.Reset();
    }

    internal bool ApplyRumble(in HapticPattern pattern)
    {
        if (!_hasActive || !_handles.TryGetValue(_activeId, out IntPtr pointer)) return false;
        return SDL3.SDL_RumbleGamepad((SDL_Gamepad*)pointer,
            pattern.LowFrequency, pattern.HighFrequency, pattern.DurationMilliseconds);
    }

    internal void StopRumble()
    {
        if (_hasActive && _handles.TryGetValue(_activeId, out IntPtr pointer))
            SDL3.SDL_RumbleGamepad((SDL_Gamepad*)pointer, 0, 0, 0);
    }

    /// <summary>
    /// Clears held input after focus loss without dropping the active device.
    /// SDL will not report another add event for a gamepad that stayed
    /// connected, so the active id must survive presentation handoffs.
    /// </summary>
    internal void ResetState()
    {
        SetGyroSensor(false);
        _state = default;
        GamepadGyro.Reset();
    }

    public void Dispose()
    {
        if (_handles.Count != 0)
        {
            SetGyroSensor(false);
            StopRumble();
            foreach (IntPtr pointer in _handles.Values)
                SDL3.SDL_CloseGamepad((SDL_Gamepad*)pointer);
            _handles.Clear();
        }
        _state = default;
        _activeId = 0;
        _hasActive = false;
        _gyroEnabled = false;
        GamepadGyro.ResetDevice();
        GamepadGyro.Reset();
    }

    private static PrimeGamepadState StateFor(SDL_Gamepad* handle)
        => new()
        {
            Connected = true,
            Name = SDL3.SDL_GetGamepadName(handle) ?? "SDL gamepad",
            Family = FamilyFrom(SDL3.SDL_GetGamepadType(handle))
        };

    private static SdlGamepadCapabilities CapabilitiesFor(uint id, SDL_Gamepad* handle)
    {
        PrimeGamepadState state = StateFor(handle);
        return new(id, state.Name, state.Family, TryHasGyroscope(handle),
            TryHasAnalogTriggers(handle));
    }

    private static bool? TryHasAnalogTriggers(SDL_Gamepad* handle)
    {
        bool? left = TryHasAxis(handle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER);
        bool? right = TryHasAxis(handle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER);
        return left == false || right == false ? false
            : left == true && right == true ? true : null;
    }

    private static bool? TryHasGyroscope(SDL_Gamepad* handle)
    {
        try
        {
            return SDL3.SDL_GamepadHasSensor(handle, SDL_SensorType.SDL_SENSOR_GYRO);
        }
        catch (Exception ex) when (ex is DllNotFoundException
            || ex is EntryPointNotFoundException || ex is BadImageFormatException)
        {
            return null;
        }
    }

    private static bool? TryHasAxis(SDL_Gamepad* handle, SDL_GamepadAxis axis)
    {
        try
        {
            return SDL3.SDL_GamepadHasAxis(handle, axis);
        }
        catch (Exception ex) when (ex is DllNotFoundException
            || ex is EntryPointNotFoundException || ex is BadImageFormatException)
        {
            return null;
        }
    }

    private static ControllerFamily FamilyFrom(SDL_GamepadType type)
        => type switch
        {
            SDL_GamepadType.SDL_GAMEPAD_TYPE_XBOX360
                or SDL_GamepadType.SDL_GAMEPAD_TYPE_XBOXONE => ControllerFamily.Xbox,
            SDL_GamepadType.SDL_GAMEPAD_TYPE_PS3
                or SDL_GamepadType.SDL_GAMEPAD_TYPE_PS4
                or SDL_GamepadType.SDL_GAMEPAD_TYPE_PS5 => ControllerFamily.PlayStation,
            SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_PRO
                or SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_LEFT
                or SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_RIGHT
                or SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_PAIR
                or SDL_GamepadType.SDL_GAMEPAD_TYPE_GAMECUBE => ControllerFamily.Nintendo,
            _ => ControllerFamily.Generic
        };
}

internal readonly record struct SdlGamepadCapabilities(
    uint Id, string Name, ControllerFamily Family, bool? HasGyroscope,
    bool? HasAnalogTriggers);
