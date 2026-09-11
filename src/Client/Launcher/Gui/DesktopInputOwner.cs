using System;
using MphRead.Mods.Input;

namespace MphRead.Mods.Launcher.Gui;

internal enum DesktopInputOwnerKind
{
    None,
    Scene,
    Overlay,
    Shell
}

/// <summary>
/// Single-writer ownership for desktop input.  SDL continues to pump native
/// events once, but only the current surface may consume them.  A handoff
/// seeds the new owner with the physical held-button state so a button held
/// while opening a menu cannot become a synthetic menu press.
/// </summary>
internal sealed class DesktopInputOwner
{
    private DesktopInputOwnerKind _current;
    private ulong _generation;
    private GamepadButtons _lastButtons;
    private GamepadButtons _heldAtHandoff;

    public DesktopInputOwnerKind Current => _current;
    public ulong Generation => _generation;

    public event Action<DesktopInputOwnerKind>? Changed;

    internal DesktopInputOwnerToken SetOwner(DesktopInputOwnerKind owner,
        GamepadButtons currentlyHeld = GamepadButtons.None)
    {
        if (_current != owner)
        {
            _current = owner;
            _generation = checked(_generation + 1);
            _lastButtons = currentlyHeld;
            _heldAtHandoff = currentlyHeld;
            Changed?.Invoke(owner);
        }
        else
        {
            // A repeated activation is idempotent, but newly observed held
            // buttons still need to be suppressed for this surface.
            _lastButtons = currentlyHeld;
            _heldAtHandoff |= currentlyHeld;
        }
        return new DesktopInputOwnerToken(this, _generation, owner);
    }

    internal bool Release(DesktopInputOwnerToken token)
    {
        if (!ReferenceEquals(token.Owner, this) || token.Generation != _generation
            || token.Kind != _current)
        {
            return false;
        }
        SetOwner(DesktopInputOwnerKind.None);
        return true;
    }

    internal bool Owns(DesktopInputOwnerKind owner) => _current == owner;

    internal GamepadButtons ConsumePressed(GamepadButtons currentButtons)
    {
        GamepadButtons pressed = currentButtons & ~_lastButtons;
        if (_heldAtHandoff != GamepadButtons.None)
        {
            pressed &= ~_heldAtHandoff;
            _heldAtHandoff &= currentButtons;
        }
        _lastButtons = currentButtons;
        return pressed;
    }

    internal void Reset(GamepadButtons currentlyHeld = GamepadButtons.None)
    {
        _lastButtons = currentlyHeld;
        _heldAtHandoff = currentlyHeld;
    }

    internal readonly struct DesktopInputOwnerToken
    {
        internal DesktopInputOwnerToken(DesktopInputOwner owner, ulong generation,
            DesktopInputOwnerKind kind)
        {
            Owner = owner;
            Generation = generation;
            Kind = kind;
        }

        internal DesktopInputOwner Owner { get; }
        internal ulong Generation { get; }
        internal DesktopInputOwnerKind Kind { get; }
    }
}
