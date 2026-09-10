using System;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Stateful bridge for a controller-opened combo/editor. The actual command
/// semantics stay in <see cref="ControllerComboNavigation"/>; this wrapper
/// only tracks the active selector and exposes the existing open/preview/
/// commit/cancel state transitions to the shell integration layer.
/// </summary>
internal sealed class PrimeControllerComboSession
{
    private ControllerComboState _state;

    public ControllerComboState State => _state;
    public bool IsOpen => _state.IsOpen;
    public ControllerComboSelector Selector => _state.Selector;

    public void Begin(ControllerComboSelector selector, int selectedIndex,
        int itemCount)
    {
        ValidateItemCount(itemCount);
        int clamped = itemCount == 0 ? 0 : Math.Clamp(selectedIndex, 0, itemCount - 1);
        _state = new ControllerComboState(selector, clamped, clamped, false);
        _state = ControllerComboNavigation.Transition(_state, itemCount,
            ControllerComboCommand.Accept);
    }

    public ControllerComboState Transition(int itemCount,
        ControllerComboCommand command)
    {
        ValidateItemCount(itemCount);
        _state = ControllerComboNavigation.Transition(_state, itemCount, command);
        return _state;
    }

    public void Reset(ControllerComboSelector selector = ControllerComboSelector.Generic,
        int selectedIndex = 0)
        => _state = new ControllerComboState(selector, selectedIndex, selectedIndex,
            false);

    private static void ValidateItemCount(int itemCount)
    {
        if (itemCount < 0)
            throw new ArgumentOutOfRangeException(nameof(itemCount));
    }
}
