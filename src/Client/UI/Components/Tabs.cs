using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Components;

public sealed class Tabs : StackPanel
{
    private readonly List<SecondaryButton> _buttons = [];

    public Tabs()
    {
        Orientation = Orientation.Horizontal;
        Spacing = UiSpacing.Space2;
    }

    public event EventHandler<int>? SelectionChanged;
    public int SelectedIndex { get; private set; } = -1;

    public void SetItems(IReadOnlyList<string> labels, int selectedIndex = 0)
    {
        Children.Clear();
        _buttons.Clear();
        for (int i = 0; i < labels.Count; i++)
        {
            int index = i;
            var button = new SecondaryButton
            {
                Content = labels[i],
                AccessibleName = $"{labels[i]} tab"
            };
            button.Click += (_, _) => Select(index);
            _buttons.Add(button);
            Children.Add(button);
        }
        if (labels.Count > 0)
        {
            Select(System.Math.Clamp(selectedIndex, 0, labels.Count - 1), notify: false);
        }
    }

    public void Select(int index, bool notify = true)
    {
        if ((uint)index >= (uint)_buttons.Count || SelectedIndex == index)
        {
            return;
        }
        SelectedIndex = index;
        for (int i = 0; i < _buttons.Count; i++)
        {
            _buttons[i].BorderBrush = i == index ? UiColors.AccentBrush : UiColors.EdgeBrush;
        }
        if (notify) SelectionChanged?.Invoke(this, index);
    }
}
