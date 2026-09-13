using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.Launcher.Settings;
using MphRead.Mods.Launcher.Theme;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// One discard/save surface for every SettingsView host. The view retains all
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
    private readonly Note _dirtyCount;
    private readonly SettingsDirtyTracker _dirtyTracker;
    private readonly MenuEntry _discard;
    private readonly MenuEntry _save;
    private bool _narrow;

    internal SettingsActionBar(SettingsView settings, bool inGame)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dirtyTracker = settings.DirtyTracker;
        _dirtyTracker.Changed += DirtyTrackerChanged;
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
        _dirtyCount = new Note("", GuiTheme.TextDim)
        {
            IsVisible = false,
            Margin = new Thickness(4, 0, 4, 0)
        };
        _saveError = new Note("", GuiTheme.Warm) { IsVisible = false };
        _context = new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,
            Children = { copy, _dirtyCount, _saveError }
        };

        _discard = new MenuEntry("Discard", titleSize: 13)
        {
            Height = 44,
            MinWidth = 76,
            Accent = GuiTheme.TextDim,
            Margin = new Thickness(0, 0, 10, 0)
        };
        _discard.Click += (_, _) => _settings.DiscardChanges();
        _save = new MenuEntry("Save changes", titleSize: 13)
        {
            Primary = true,
            Height = 44,
            MinWidth = 148
        };
        _save.Click += (_, _) => ApplyAndSave();

        _actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _discard, _save }
        };
        Grid.SetColumn(_discard, 0);
        Grid.SetColumn(_save, 1);
        _layout.Children.Add(_context);
        _layout.Children.Add(_actions);
        Child = _layout;
        ApplyLayout(narrow: false);
        SizeChanged += (_, args) => ApplyLayout(args.NewSize.Width < NarrowWidth);
        RefreshDirtyState();
    }

    internal string ContextCopy { get; }
    internal bool IsNarrow => _narrow;
    internal bool IsDirty => _dirtyTracker.IsDirty;
    internal string? DirtyCountText => _dirtyCount.IsVisible ? _dirtyCount.Text : null;

    private void DirtyTrackerChanged(object? sender, EventArgs e)
    {
        // A failed save is useful until the next edit, but should not obscure
        // the new dirty count after the player changes the draft again.
        _saveError.IsVisible = false;
        RefreshDirtyState();
    }

    private void RefreshDirtyState()
    {
        bool dirty = _dirtyTracker.IsDirty;
        IsVisible = dirty;
        _dirtyCount.IsVisible = dirty && _dirtyTracker.ChangedCount > 0;
        if (_dirtyCount.IsVisible)
        {
            int count = _dirtyTracker.ChangedCount;
            _dirtyCount.Text = count == 1
                ? "1 unsaved change"
                : $"{count} unsaved changes";
        }
        _discard.IsEnabled = dirty;
        _save.IsEnabled = dirty;
        if (!dirty)
        {
            _saveError.IsVisible = false;
        }
    }

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
        RefreshDirtyState();
    }
}
