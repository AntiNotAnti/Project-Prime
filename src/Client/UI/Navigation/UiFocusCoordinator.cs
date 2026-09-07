using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using MphRead.Mods.UI.Components;

namespace MphRead.Mods.UI.Navigation;

public sealed class UiFocusCoordinator
{
    private readonly Dictionary<string, WeakReference<Control>> _controls = [];

    public string? CurrentKey { get; private set; }

    public void Register(string key, Control control)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(control);
        if (_controls.TryGetValue(key, out WeakReference<Control>? existing)
            && existing.TryGetTarget(out Control? registered) && ReferenceEquals(registered, control))
        {
            return;
        }
        _controls[key] = new WeakReference<Control>(control);
        control.GotFocus += (_, _) => CurrentKey = key;
    }

    public bool TryFocus(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)
            || !_controls.TryGetValue(key, out WeakReference<Control>? weak)
            || !weak.TryGetTarget(out Control? control)
            || !control.IsEffectivelyEnabled || !control.IsVisible)
        {
            return false;
        }
        Dispatcher.UIThread.Post(() => control.Focus(), DispatcherPriority.Input);
        return true;
    }

    public bool TryMove(UiFocusNavigationPolicy policy, UiFocusDirection direction)
    {
        if (CurrentKey is not { } current) return false;
        for (int hops = 0; hops < 32 && policy.TryMove(current, direction, out string target); hops++)
        {
            if (TryFocus(target)) return true;
            if (target == current) return false;
            current = target;
        }
        return false;
    }

    public bool TryActivate(string? key = null)
    {
        string? current = key ?? CurrentKey;
        if (current is null
            || !_controls.TryGetValue(current, out WeakReference<Control>? weak)
            || !weak.TryGetTarget(out Control? control)
            || !control.IsEffectivelyEnabled || !control.IsVisible)
        {
            return false;
        }
        return TryActivate(control);
    }

    public static bool TryActivate(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (!control.IsEffectivelyEnabled || !control.IsVisible) return false;
        if (control is UiActionButton action) return action.TryInvoke();
        if (control is ComboBox combo)
        {
            combo.IsDropDownOpen = !combo.IsDropDownOpen;
            return true;
        }
        if (control is CheckBox check)
        {
            check.IsChecked = check.IsChecked != true;
            return true;
        }
        // Text and numeric inputs already receive focus through directional navigation. Treat the
        // accept press as handled so the platform can show its editor without also routing it back.
        return control is TextBox or NumericUpDown;
    }

    public bool TryAdjustSelection(int delta)
    {
        if (CurrentKey is not { } current
            || !_controls.TryGetValue(current, out WeakReference<Control>? weak)
            || !weak.TryGetTarget(out Control? control)) return false;
        return TryAdjustSelection(control, delta);
    }

    public static bool TryAdjustSelection(Control control, int delta)
    {
        if (!control.IsEffectivelyEnabled || !control.IsVisible || delta == 0) return false;
        if (control is ComboBox { IsDropDownOpen: true } combo && combo.ItemCount > 0)
        {
            combo.SelectedIndex = Math.Clamp(combo.SelectedIndex + delta, 0, combo.ItemCount - 1);
            return true;
        }
        if (control is NumericUpDown number)
        {
            decimal value = number.Value ?? number.Minimum;
            number.Value = Math.Clamp(value + number.Increment * delta, number.Minimum, number.Maximum);
            return true;
        }
        return false;
    }
}
