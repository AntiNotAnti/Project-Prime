using Avalonia.Controls;
using MphRead.Mods.UI.Screens.Settings;
using MphRead.Mods.UI.State;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>Desktop frame for the shared settings screen while a match is running.</summary>
    internal sealed class SettingsWindow : Window
    {
        public SettingsWindow(ISettingsScreenController controller)
        {
            Title = $"{Mods.Branding.Name} - settings";
            Icon = GuiTheme.AppIcon.Value;
            Background = GuiTheme.InkBrush;
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            CanResize = false;
            SystemDecorations = SystemDecorations.None;
            Topmost = true;
            ShowInTaskbar = false;
            Content = new SettingsScreenView(controller, isAndroid: false);
            PauseMenuWindow.CoverGameWindow(this);
        }

        protected override void OnOpened(System.EventArgs e)
        {
            base.OnOpened(e);
            PauseMenuWindow.CoverGameWindow(this);
        }
    }
}
