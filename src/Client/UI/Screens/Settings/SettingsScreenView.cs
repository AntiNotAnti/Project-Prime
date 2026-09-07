using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Screens.Settings;

public sealed class SettingsScreenView : ScreenViewBase
{
    private readonly ISettingsScreenController _controller;
    private readonly bool _isAndroid;
    private readonly WrapPanel _categoryPanel = new() { Orientation = Orientation.Vertical };
    private readonly StackPanel _settings = new() { Spacing = UiSpacing.Space3 };
    private readonly ErrorBanner _feedback = new() { IsVisible = false };
    private readonly Grid _layout;
    private SettingsCategory _category;

    public SettingsScreenView(ISettingsScreenController controller, bool isAndroid)
        : base("Settings", "Settings are stored by their existing domain services.", "settings:category:gameplay")
    {
        _controller = controller;
        _isAndroid = isAndroid;
        foreach (SettingsCategory category in Enum.GetValues<SettingsCategory>())
        {
            SettingsCategory selected = category;
            var button = Register($"settings:category:{category.ToString().ToLowerInvariant()}",
                new SecondaryButton
                {
                    Content = category.ToString(), AccessibleName = $"{category} settings",
                    Margin = new Thickness(0, 0, UiSpacing.Space2, UiSpacing.Space2)
                });
            button.Click += (_, _) => SelectCategory(selected);
            _categoryPanel.Children.Add(button);
        }
        _layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = UiSpacing.Space5 };
        _layout.Children.Add(_categoryPanel);
        _layout.Children.Add(_settings);
        Grid.SetColumn(_settings, 1);
        Body.Content = new StackPanel { Spacing = UiSpacing.Space4, Children = { _layout, _feedback } };
        SelectCategory(SettingsCategory.Gameplay);
    }

    protected override void OnLayoutModeChanged(UiLayoutMode mode)
    {
        bool stacked = mode != UiLayoutMode.Wide;
        _categoryPanel.Orientation = stacked ? Orientation.Horizontal : Orientation.Vertical;
        _layout.ColumnDefinitions = stacked ? new ColumnDefinitions("*") : new ColumnDefinitions("Auto,*");
        _layout.RowDefinitions = stacked ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
        Grid.SetColumn(_settings, stacked ? 0 : 1);
        Grid.SetRow(_settings, stacked ? 1 : 0);
        _settings.Margin = stacked ? new Thickness(0, UiSpacing.Space4, 0, 0) : default;
    }

    private void SelectCategory(SettingsCategory category)
    {
        _category = category;
        _settings.Children.Clear();
        _settings.Children.Add(Text(category.ToString().ToUpperInvariant(), size: UiTypography.TextHeading));
        SettingPlatform hidden = _isAndroid ? SettingPlatform.Desktop : SettingPlatform.Android;
        SettingDescriptor[] visible = _controller.Read(category)
            .Where(setting => setting.Platform != hidden).ToArray();
        if (visible.Length == 0)
        {
            var empty = new EmptyState();
            empty.SetContent("No settings", "No settings in this category apply to this platform.");
            _settings.Children.Add(empty);
        }
        foreach (SettingDescriptor setting in visible)
        {
            var value = new TextBox { Text = setting.Value?.ToString() ?? string.Empty };
            Register($"settings:value:{setting.Key}", value);
            AutomationProperties.SetName(value, setting.Label);
            var apply = Register($"settings:apply:{setting.Key}", new SecondaryButton
            {
                Content = "Apply", AccessibleName = $"Apply {setting.Label}"
            });
            apply.Click += async (_, _) => await ApplyAsync(setting.Key, value.Text);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = UiSpacing.Space3 };
            row.Children.Add(new StackPanel
            {
                Spacing = UiSpacing.Space1,
                Children =
                {
                    Text(setting.Label),
                    Text(setting.Description
                        + (setting.RestartRequired ? " Restart required." : " Applies immediately.")
                        + (setting.Conflict is null ? "" : $" Conflict: {setting.Conflict}"), muted: true),
                    value
                }
            });
            row.Children.Add(apply);
            Grid.SetColumn(apply, 1);
            _settings.Children.Add(new Border
            {
                Background = UiColors.PanelBrush,
                BorderBrush = setting.Conflict is null ? UiColors.EdgeBrush : UiColors.WarningBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(UiMetrics.RadiusMedium),
                Padding = new Thickness(UiSpacing.Space4),
                Child = row
            });
        }

        var reset = Register($"settings:reset:{category.ToString().ToLowerInvariant()}", new SecondaryButton
        {
            Content = "Reset Category", AccessibleName = $"Reset {category} settings"
        });
        reset.Click += async (_, _) => await ResetCategoryAsync(category);
        var defaults = Register("settings:restore-defaults", new SecondaryButton
        {
            Content = "Restore All Defaults", AccessibleName = "Restore all settings defaults"
        });
        defaults.Click += async (_, _) => await RestoreDefaultsAsync();
        _settings.Children.Add(new WrapPanel { Children = { reset, defaults } });
    }

    private async System.Threading.Tasks.Task ApplyAsync(string key, object? value)
    {
        try { ShowResult(await _controller.ApplyAsync(key, value, ScreenCancellation)); }
        catch (Exception error)
        {
            _feedback.Message = AsyncScreenState.FriendlyFailure(error, "The setting");
            _feedback.IsVisible = true;
        }
    }

    private async System.Threading.Tasks.Task ResetCategoryAsync(SettingsCategory category)
    {
        try
        {
            UiActionResult result = await _controller.ResetCategoryAsync(category, ScreenCancellation);
            ShowResult(result);
            if (result.Succeeded) SelectCategory(category);
        }
        catch (Exception error)
        {
            _feedback.Message = AsyncScreenState.FriendlyFailure(error, "Resetting this category");
            _feedback.IsVisible = true;
        }
    }

    private async System.Threading.Tasks.Task RestoreDefaultsAsync()
    {
        try
        {
            UiActionResult result = await _controller.RestoreDefaultsAsync(ScreenCancellation);
            ShowResult(result);
            if (result.Succeeded) SelectCategory(_category);
        }
        catch (Exception error)
        {
            _feedback.Message = AsyncScreenState.FriendlyFailure(error, "Restoring defaults");
            _feedback.IsVisible = true;
        }
    }

    private void ShowResult(UiActionResult result)
    {
        _feedback.IsVisible = !result.Succeeded;
        if (!result.Succeeded) _feedback.Message = result.Message;
    }
}
