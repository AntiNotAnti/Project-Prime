using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.Launcher.Theme;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// One save/cancel surface for every SettingsView host. The view retains all
/// validation, persistence, rollback, and close ownership; hosts only choose
/// where this bar is mounted.
/// </summary>
internal sealed class SettingsActionBar : Border
{
    private const double NarrowWidth = 620;
    private readonly SettingsView _settings;
    private readonly Grid _layout = new();
    private readonly StackPanel _context;
    private readonly Grid _actions;
    private readonly Note _saveError;
    private bool _narrow;

    internal SettingsActionBar(SettingsView settings, bool inGame)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ContextCopy = inGame
            ? "Changes apply immediately where supported."
            : "Changes apply to this device.";

        Background = GuiTheme.PanelBrush;
        BorderBrush = GuiTheme.EdgeBrush;
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(12, 10);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Classes.Add("prime-settings-action-footer");
        PrimeAccessibility.SetName(this, "Settings actions");

        var copy = new TextBlock
        {
            Text = ContextCopy,
            FontFamily = GuiTheme.Display,
            FontSize = 11,
            Foreground = GuiTheme.TextDimBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        _saveError = new Note("", GuiTheme.Warm) { IsVisible = false };
        _context = new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,
            Children = { copy, _saveError }
        };

        var cancel = new MenuEntry("Cancel", titleSize: 13)
        {
            Height = 44,
            MinWidth = 76,
            Accent = GuiTheme.TextDim,
            Margin = new Thickness(0, 0, 10, 0)
        };
        cancel.Click += (_, _) => _settings.Cancel();
        var apply = new MenuEntry("Apply & Save", titleSize: 13)
        {
            Primary = true,
            Height = 44,
            MinWidth = 148
        };
        // SettingsView contains eager input mutations and custom controls that
        // do not expose one complete change stream. Keep Apply enabled until a
        // truthful dirty model can cover every setting.
        apply.Click += (_, _) => ApplyAndSave();

        _actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { cancel, apply }
        };
        Grid.SetColumn(cancel, 0);
        Grid.SetColumn(apply, 1);
        _layout.Children.Add(_context);
        _layout.Children.Add(_actions);
        Child = _layout;
        ApplyLayout(narrow: false);
        SizeChanged += (_, args) => ApplyLayout(args.NewSize.Width < NarrowWidth);
    }

    internal string ContextCopy { get; }
    internal bool IsNarrow => _narrow;

    internal void ApplyLayout(bool narrow)
    {
        if (_narrow == narrow && _layout.ColumnDefinitions.Count > 0) return;
        _narrow = narrow;
        _layout.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,Auto");
        _layout.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto" : "Auto");
        Grid.SetColumn(_context, 0);
        Grid.SetRow(_context, 0);
        Grid.SetColumn(_actions, narrow ? 0 : 1);
        Grid.SetRow(_actions, narrow ? 1 : 0);
        _actions.Margin = narrow ? new Thickness(0, 8, 0, 0) : default;
    }

    private void ApplyAndSave()
    {
        if (_settings.ApplyAndSave(out string? error)) return;
        _saveError.Text = error ?? "Could not save settings.";
        _saveError.IsVisible = true;
    }
}
