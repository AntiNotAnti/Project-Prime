using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Mods.Launcher.Gui;

internal sealed record PrimeTabItem(string Label, bool IsSelected, Action Select);

/// <summary>
/// Compact local navigation whose selected state is exposed both visually and
/// to assistive technology. Route state remains owned by the caller.
/// </summary>
internal sealed class PrimeTabStrip : WrapPanel
{
    private readonly IReadOnlyList<PrimeTabButton> _tabs;

    public PrimeTabStrip(IEnumerable<PrimeTabItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        PrimeTabItem[] materialized = items.ToArray();
        if (materialized.Length == 0)
            throw new ArgumentException("At least one tab is required.", nameof(items));
        if (materialized.Count(item => item.IsSelected) != 1)
            throw new ArgumentException("Exactly one tab must be selected.", nameof(items));

        Orientation = Orientation.Horizontal;
        Classes.Add("prime-tab-strip");
        var tabs = new List<PrimeTabButton>(materialized.Length);
        foreach (PrimeTabItem item in materialized)
        {
            if (String.IsNullOrWhiteSpace(item.Label))
                throw new ArgumentException("Every tab requires a label.", nameof(items));
            ArgumentNullException.ThrowIfNull(item.Select);
            var tab = new PrimeTabButton(item.Label.Trim(), item.IsSelected, item.Select);
            tabs.Add(tab);
            Children.Add(tab);
        }
        _tabs = tabs;
    }

    public IReadOnlyList<PrimeTabButton> Tabs => _tabs;
    public PrimeTabButton SelectedTab => _tabs.Single(tab => tab.IsSelected);
}

internal sealed class PrimeTabButton : AvaloniaButton
{
    private readonly Action _select;

    public PrimeTabButton(string label, bool selected, Action select)
    {
        Label = label;
        IsSelected = selected;
        _select = select;
        Content = label;
        Classes.Add("prime-tab");
        if (selected) Classes.Add("prime-selected");
        AutomationProperties.SetName(this, label);
        AutomationProperties.SetItemStatus(this, selected ? "Selected" : "Not selected");
        Click += (_, _) => _select();
    }

    public string Label { get; }
    public bool IsSelected { get; }

    public void Invoke() => _select();
}
