using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Screens.Home;

public sealed class HomeScreenView : ScreenViewBase
{
    private readonly IHomeScreenController _controller;
    private readonly TextBlock _identityText;

    public HomeScreenView(UiRouter router, IHomeScreenController controller)
        : base("Home", "Your session starts here.", "home:play")
    {
        _controller = controller;
        _identityText = Text(string.Empty);
        UpdateIdentity();
        _controller.IdentityChanged += ControllerIdentityChanged;

        var identityCard = new Border
        {
            Background = UiColors.PanelBrush,
            BorderBrush = UiColors.EdgeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(UiMetrics.RadiusMedium),
            Padding = new Thickness(UiSpacing.Space4),
            Child = _identityText
        };

        var actions = new StackPanel { Spacing = UiSpacing.Space3 };
        AddAction(actions, router, "home:play", "Play", UiRoute.Play, primary: true);
        AddAction(actions, router, "home:license", "Hunter License", UiRoute.HunterLicense);
        AddAction(actions, router, "home:replays", "Replays", UiRoute.Replays);
        AddAction(actions, router, "home:settings", "Settings", UiRoute.Settings);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*"), ColumnSpacing = UiSpacing.Space5 };
        grid.Children.Add(actions);
        grid.Children.Add(identityCard);
        Grid.SetColumn(identityCard, 1);
        Body.Content = grid;
    }

    private void ControllerIdentityChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) UpdateIdentity();
        else Dispatcher.UIThread.Post(UpdateIdentity);
    }

    private void UpdateIdentity()
    {
        UiAccountIdentity identity = _controller.Identity;
        _identityText.Text = identity.IsGuest
            ? "Guest\nCreate Hunter License"
            : $"{identity.DisplayName}\n{identity.RankTitle}"
                + (identity.RankingPoints is int points ? $"\n{points} RP" : string.Empty);
        AutomationProperties.SetName(_identityText, identity.IsGuest
            ? "Guest. Create Hunter License"
            : $"{identity.DisplayName}, {identity.RankTitle}, {identity.RankingPoints} Ranking Points");
    }

    protected override void OnLayoutModeChanged(UiLayoutMode mode)
    {
        if (Body.Content is not Grid grid) return;
        bool compact = mode == UiLayoutMode.Compact;
        grid.ColumnDefinitions = compact ? new ColumnDefinitions("*") : new ColumnDefinitions("2*,*");
        grid.RowDefinitions = compact ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
        Control identity = grid.Children[1];
        Grid.SetColumn(identity, compact ? 0 : 1);
        Grid.SetRow(identity, compact ? 1 : 0);
        identity.Margin = compact ? new Thickness(0, UiSpacing.Space4, 0, 0) : default;
    }

    private void AddAction(StackPanel panel, UiRouter router, string key, string label,
        UiRoute route, bool primary = false)
    {
        UiActionButton button = primary ? new PrimaryButton() : new SecondaryButton();
        button.Content = label;
        button.AccessibleName = $"Open {label}";
        button.Click += (_, _) => router.Navigate(route, sourceFocusKey: key);
        panel.Children.Add(Register(key, button));
    }
}
