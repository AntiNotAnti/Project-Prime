using System;
using System.Collections.Generic;
using Avalonia.Controls;
using MphRead.Mods.UI.Adapters;
using MphRead.Mods.UI.AppShell;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// Persistent desktop frame around the current launcher/app-shell view.
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
        private LaunchPlan _plan;

        /// <summary>What the screen decided. Kind None means it was closed.</summary>
        public LaunchPlan Plan => _plan;
        public bool IsClosed { get; private set; }
        public ClientUiRuntime Runtime { get; }

        /// <summary>Raised when the shell should yield to a match.</summary>
        public event EventHandler<LaunchPlan>? Selected;

        public HomeWindow(MenuSettings settings, IReadOnlyList<string> rooms)
        {
            Runtime = new ClientUiRuntime(settings, rooms, isAndroid: false);
            Runtime.LaunchRequested += RuntimeLaunchRequested;

            Title = Mods.Branding.Name;
            Icon = GuiTheme.AppIcon.Value;
            Width = 940;
            Height = 560;
            MinWidth = 780;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = GuiTheme.PanelBrush;
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            Content = RootFor(Runtime);
            Closed += (_, _) =>
            {
                IsClosed = true;
                Runtime.LaunchRequested -= RuntimeLaunchRequested;
                Runtime.Dispose();
            };
        }

        internal static AppShellView RootFor(ClientUiRuntime runtime) => runtime.Shell;

        /// <summary>Clear the result from the previous time this persistent window was shown.</summary>
        internal void BeginSelection() => _plan = default;

        private void RuntimeLaunchRequested(object? sender, LaunchPlan plan)
        {
            _plan = plan;
            // Hiding keeps the native shell and Avalonia lifetime intact while
            // OpenTK owns the foreground. Closing is reserved for app exit.
            Hide();
            Selected?.Invoke(this, plan);
        }
    }
}
