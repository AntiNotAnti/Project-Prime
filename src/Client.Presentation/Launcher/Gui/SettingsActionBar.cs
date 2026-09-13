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
    private static double NarrowWidth => PrimeLayoutMetrics.CompactWidth;
    private readonly SettingsView _settings;
    private readonly Grid _layout = new();
    private readonly StackPanel _context;
    private readonly Grid _actions;
    private readonly Note _saveStatus;
    private readonly Note _saveError;
    private readonly Note _dirtyCount;
    private readonly SettingsDirtyTracker _dirtyTracker;
    private readonly MenuEntry _discard;
    private readonly MenuEntry _save;
    private PrimeMotionLease? _entryMotion;
    private bool _wasDirty;
    private bool _narrow;

    internal SettingsActionBar(SettingsView settings, bool inGame)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.SaveSucceeded += SettingsSaveSucceeded;
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
        _saveStatus = new Note("Settings saved.", GuiTheme.Good)
        {
            IsVisible = false,
            Margin = new Thickness(4, 0, 4, 0)
        };
        PrimeAccessibility.SetStatus(_saveStatus, _saveStatus.Text!,
            PrimeStatusKind.Success);
        _saveError = new Note("", GuiTheme.Warm) { IsVisible = false };
        _context = new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,
            Children = { copy, _dirtyCount, _saveStatus, _saveError }
        };

        _discard = new MenuEntry("Discard", titleSize: 13)
        {
            Height = 44,
            MinWidth = 76,
            Accent = GuiTheme.TextDim,
            Margin = new Thickness(0, 0, 10, 0)
        };
        PrimeAccessibility.SetDescription(_discard,
            "Discard unsaved settings and restore the previous values.");
        _discard.Click += (_, _) => _settings.DiscardChanges();
        _save = new MenuEntry("Save changes", titleSize: 13)
        {
            Primary = true,
            Height = 44,
            MinWidth = 148
        };
        PrimeAccessibility.SetDescription(_save,
            "Save the changed settings on this device.");
        _save.Click += (_, _) => ApplyAndSave();

        _actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _discard, _save }
        };
        PrimeAccessibility.SetDescription(_actions,
            "Actions for the current settings draft.");
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
    internal string? SaveStatusText => _saveStatus.IsVisible ? _saveStatus.Text : null;
    internal string? SaveErrorText => _saveError.IsVisible ? _saveError.Text : null;

    private void DirtyTrackerChanged(object? sender, EventArgs e)
    {
        // A failed save is useful until the next edit, but should not obscure
        // the new dirty count after the player changes the draft again.
        _saveError.IsVisible = false;
        if (_dirtyTracker.IsDirty)
        {
            _saveStatus.IsVisible = false;
        }
        RefreshDirtyState();
    }

    private void SettingsSaveSucceeded(object? sender, EventArgs e)
    {
        _entryMotion?.Dispose();
        _entryMotion = null;
        Opacity = 1;
        _saveError.IsVisible = false;
        _dirtyCount.IsVisible = false;
        _saveStatus.IsVisible = true;
        _discard.IsEnabled = false;
        _save.IsEnabled = false;
        IsVisible = true;
    }

    private void RefreshDirtyState()
    {
        bool dirty = _dirtyTracker.IsDirty;
        IsVisible = dirty;
        if (dirty && !_wasDirty)
        {
            _entryMotion?.Dispose();
            _entryMotion = PrimeMotion.AnimateEntry(this,
                PrimeMotionPreset.FadeAndSlide,
                reducedMotion: PrimeMotion.ReducedMotion,
                duration: PrimeMotion.FastDuration);
        }
        else if (!dirty && _wasDirty)
        {
            _entryMotion?.Dispose();
            _entryMotion = null;
            Opacity = 1;
        }
        _wasDirty = dirty;
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
            _saveStatus.IsVisible = false;
        }
    }

    internal void ApplyLayout(bool narrow, bool honorRequested = false)
    {
        // A standalone shell host still calls this method from its chrome
        // refresh. Prefer the current measured width when it has one so a
        // stale host threshold cannot turn the shared compact breakpoint back
        // into a desktop footer.
        if (!honorRequested && Bounds.Width > 0 && Double.IsFinite(Bounds.Width))
        {
            narrow = Bounds.Width < NarrowWidth;
        }
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
        _saveStatus.IsVisible = false;
        _saveError.Text = error ?? "Could not save settings.";
        _saveError.IsVisible = true;
        PrimeAccessibility.SetStatus(_saveError, _saveError.Text,
            PrimeStatusKind.Error);
        RefreshDirtyState();
    }
}
