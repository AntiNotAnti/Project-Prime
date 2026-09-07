using Avalonia.Controls;
using MphRead.Mods.UI.AppShell;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.Navigation;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class ControllerInputTests
{
    [Fact]
    public void FocusCoordinatorAcceptInvokesTheFocusedShellAction()
    {
        var focus = new UiFocusCoordinator();
        var button = new PrimaryButton();
        bool invoked = false;
        button.Click += (_, _) => invoked = true;
        focus.Register("action", button);

        Assert.True(focus.TryActivate("action"));
        Assert.True(invoked);
    }

    [Fact]
    public void ShellBackInputUsesTheRouterHistory()
    {
        using var state = new AppShellState();
        var shell = new AppShellView(state);
        state.Router.Navigate(UiRoute.Play);

        Assert.True(shell.HandleControllerInput(UiControllerInput.Back));
        Assert.Equal(UiRoute.Home, state.Router.CurrentRoute);
    }

    [Fact]
    public void ShoulderInputsCycleTopLevelShellTabs()
    {
        using var state = new AppShellState();
        var shell = new AppShellView(state);

        Assert.True(shell.HandleControllerInput(UiControllerInput.NextTab));
        Assert.Equal(UiRoute.Play, state.Router.CurrentRoute);
        Assert.True(shell.HandleControllerInput(UiControllerInput.PreviousTab));
        Assert.Equal(UiRoute.Home, state.Router.CurrentRoute);
    }

    [Fact]
    public void FocusCoordinatorAcceptOpensAComboBox()
    {
        var focus = new UiFocusCoordinator();
        var combo = new ComboBox { ItemsSource = new[] { "Samus", "Noxus", "Trace" }, SelectedIndex = 0 };
        focus.Register("combo", combo);

        Assert.True(focus.TryActivate("combo"));
        Assert.True(combo.IsDropDownOpen);
        Assert.True(UiFocusCoordinator.TryAdjustSelection(combo, 1));
        Assert.Equal(1, combo.SelectedIndex);
    }
}
