using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class ControllerDiagnosticSettingsTests
{
    [AvaloniaFact]
    public void GamepadSettingsExposeManagedDiagnosticsWithoutASecondInputOwner()
    {
        using var view = new SettingsView(new MenuSettings());
        var window = new Window
        {
            Width = 940,
            Height = 720,
            Content = view
        };
        try
        {
            window.Show();
            view.ShowSection("Controls");
            view.ShowControlsTab("Gamepad");

            Assert.NotNull(view.RenderedControllerDiagnosticsSummary);
            Assert.NotNull(view.RenderedControllerDiagnosticsDetails);
            Assert.Contains("controllerTuning:",
                view.RenderedControllerDiagnosticsDetails!);
            Assert.Contains("responseCurve=",
                view.RenderedControllerDiagnosticsDetails!);
            Assert.Contains("binding.",
                view.RenderedControllerDiagnosticsDetails!);
            Assert.Contains("Write privacy-safe",
                view.ControllerDiagnosticsExportStatus!);
            Assert.DoesNotContain(LauncherPrefs.Directory,
                view.RenderedControllerDiagnosticsDetails!);
        }
        finally
        {
            window.Close();
        }
    }
}
