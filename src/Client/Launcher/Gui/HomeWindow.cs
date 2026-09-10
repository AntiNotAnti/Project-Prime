using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// A frame around <see cref="PrimeShellView"/>, and nothing else.
    ///
    /// Everything the front screen *is* lives in the view, which is what the
    /// Android head shows directly; this is the title bar, the icon and the
    /// size, which are the three things a phone has no use for. What is
    /// deliberately not the same as the WinForms screen it replaced is the
    /// chrome: an ordinary decorated window rather than a borderless panel,
    /// because a window with no frame that a Linux window manager will not let
    /// you move is a trap, and there are many window managers.
    /// </summary>
    internal sealed class HomeWindow : Window
    {
        private readonly PrimeShellView _view;

        /// <summary>What the screen decided. Kind None means it was closed.</summary>
        public LaunchPlan Plan => IsClosed ? default : _view.Plan;
        public bool IsClosed { get; private set; }
        public event EventHandler? LaunchRequested;
        public void Resume(MatchRunResult? result)
        {
            _view.Reset();
            if (result != null) _view.ShowMatchOutcome(result);
            Show();
            _view.Activate();
            Activate();
        }

        public HomeWindow(MenuSettings settings, IReadOnlyList<string> rooms)
        {
            _view = new PrimeShellView(settings, rooms);
            _view.Done += (_, plan) =>
            {
                if (plan.Kind == LaunchKind.None) { Close(); return; }
                _view.Deactivate();
                Hide();
                LaunchRequested?.Invoke(this, EventArgs.Empty);
            };
            Closed += (_, _) => IsClosed = true;
            Closed += (_, _) => _ = _view.DisposeAsync().AsTask();

            Title = Mods.Branding.Name;
            Icon = GuiTheme.AppIcon.Value;
            Width = 940;
            Height = 560;
            MinWidth = 780;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = GuiTheme.PanelBrush;
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            Content = _view;
        }
    }
}
