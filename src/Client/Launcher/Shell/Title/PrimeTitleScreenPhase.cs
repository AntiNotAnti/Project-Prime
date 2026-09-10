using System;
using System.Collections.Generic;
using Avalonia.Input;
using MphRead.Mods.Input;

namespace MphRead.Mods.Launcher.Gui;

internal enum PrimeTitleScreenPhase
{
    Loading,
    Ready,
    Dismissing,
    Hidden
}

internal sealed class PrimeTitleScreenLifecycle
{
    public PrimeTitleScreenLifecycle(PrimeTitleScreenPhase initial =
        PrimeTitleScreenPhase.Loading)
    {
        if (initial == PrimeTitleScreenPhase.Dismissing)
            throw new ArgumentOutOfRangeException(nameof(initial));
        Phase = initial;
    }

    public PrimeTitleScreenPhase Phase { get; private set; }
    public bool IsBlocking => Phase != PrimeTitleScreenPhase.Hidden;

    public bool MarkReady()
    {
        if (Phase != PrimeTitleScreenPhase.Loading) return false;
        Phase = PrimeTitleScreenPhase.Ready;
        return true;
    }

    public bool BeginDismissal()
    {
        if (Phase != PrimeTitleScreenPhase.Ready) return false;
        Phase = PrimeTitleScreenPhase.Dismissing;
        return true;
    }

    public bool CompleteDismissal()
    {
        if (Phase != PrimeTitleScreenPhase.Dismissing) return false;
        Phase = PrimeTitleScreenPhase.Hidden;
        return true;
    }

    public bool HideForTeardown()
    {
        if (Phase == PrimeTitleScreenPhase.Hidden) return false;
        Phase = PrimeTitleScreenPhase.Hidden;
        return true;
    }
}

internal sealed record PrimeTitleScreenCaptureState(
    PrimeTitleScreenPhase Phase,
    PrimeInputDevice InputDevice = PrimeInputDevice.KeyboardMouse,
    ControllerFamily ControllerFamily = ControllerFamily.Generic,
    bool ReducedMotion = false,
    bool ForceFallback = false);

/// <summary>Release-gated physical input edges for the one-time title layer.</summary>
internal sealed class PrimeTitleInputState
{
    private readonly HashSet<Key> _heldKeys = new();
    private GamepadButtons _previousButtons;
    private bool _controllerConnected;
    private bool _pointerHeld;

    public bool PressKey(Key key) => _heldKeys.Add(key);
    public void ReleaseKey(Key key) => _heldKeys.Remove(key);
    public bool IsKeyHeld(Key key) => _heldKeys.Contains(key);

    public bool PressPointer()
    {
        if (_pointerHeld) return false;
        _pointerHeld = true;
        return true;
    }

    public void ReleasePointer() => _pointerHeld = false;
    public bool PointerHeld => _pointerHeld;

    public void SeedController(GamepadState state)
    {
        _controllerConnected = state.Connected;
        _previousButtons = state.Connected ? state.Buttons : GamepadButtons.None;
    }

    public GamepadButtons ObserveController(GamepadState state)
    {
        if (state.Connected != _controllerConnected)
        {
            SeedController(state);
            return GamepadButtons.None;
        }
        if (!state.Connected)
        {
            _previousButtons = GamepadButtons.None;
            return GamepadButtons.None;
        }

        // Title continuation intentionally uses physical digital buttons.
        // Trigger axes and stick motion never synthesize a title press.
        GamepadButtons pressed = state.Buttons & ~_previousButtons;
        _previousButtons = state.Buttons;
        return pressed;
    }
}
