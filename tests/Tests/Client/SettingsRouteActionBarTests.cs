using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Gui;
using OpenTK.Mathematics;
using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
using Xunit;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class SettingsRouteActionBarTests
{
    [AvaloniaFact]
    public void EmbeddedAndShellSettingsEachRenderExactlyOneSharedActionFooter()
    {
        using var embedded = new SettingsView(new MenuSettings());
        SettingsActionBar embeddedBar = Assert.Single(
            Walk(embedded).OfType<SettingsActionBar>());
        Assert.True(embedded.HasEmbeddedActionBar);
        Assert.Equal("Changes apply to this device.", embeddedBar.ContextCopy);

        PrimeShellView shell = PrimeShellView.CreateCapture(new MenuSettings(),
            Array.Empty<string>(), PrimeRoute.Settings);
        var window = new Window { Width = 1024, Height = 720, Content = shell };
        window.Show();
        try
        {
            ContentControl pageHost = Find<ContentControl>(shell, "PageHost");
            SettingsView route = Assert.IsType<SettingsView>(pageHost.Content);
            Assert.False(route.HasEmbeddedActionBar);
            SettingsActionBar routeBar = Assert.Single(shell.GetVisualDescendants()
                .OfType<SettingsActionBar>());
            Assert.Equal("Changes apply to this device.", routeBar.ContextCopy);

            Panel shellActionBar = Find<Panel>(shell, "ActionBar");
            Assert.Single(shellActionBar.Children);
            Assert.Same(routeBar, shellActionBar.Children[0]);
            Assert.False(routeBar.IsNarrow);
            Assert.True(routeBar.Bounds.Width > 0);
            Assert.Equal(shellActionBar.Bounds.Width, routeBar.Bounds.Width, 1);
            using (WriteableBitmap desktop = window.CaptureRenderedFrame()!)
                Assert.Equal(new PixelSize(1024, 720), desktop.PixelSize);

            typeof(PrimeShellView).GetMethod("RefreshChrome",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null);
            Assert.Same(routeBar, Assert.Single(shellActionBar.Children));

            window.Width = 560;
            window.Height = 800;
            window.Measure(new Size(560, 800));
            window.Arrange(new Rect(0, 0, 560, 800));
            Dispatcher.UIThread.RunJobs();
            Assert.True(routeBar.IsNarrow);
            Assert.Equal(shellActionBar.Bounds.Width, routeBar.Bounds.Width, 1);
            using WriteableBitmap mobile = window.CaptureRenderedFrame()!;
            Assert.Equal(new PixelSize(560, 800), mobile.PixelSize);
        }
        finally
        {
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void InGameSettingsKeepTheirDraftAcrossMinimizeAndCancelBackToPause()
    {
        InputSettings.Snapshot prior = InputSettings.CaptureSnapshot();
        try
        {
            InputSettings.ChatKey = Keys.Y;
            var surface = new RecordingSurface();
            using var coordinator = new DesktopGameOverlayCoordinator(surface,
                () => State(visible: true, minimized: false));
            using var scene = new Scene();
            Assert.True(coordinator.OpenPause(scene));

            coordinator.OpenSettings();

            Assert.Equal(DesktopOverlayMode.Settings, coordinator.Mode);
            Control settingsHost = Assert.IsAssignableFrom<Control>(surface.Content);
            SettingsView settings = Assert.Single(Walk(settingsHost)
                .OfType<SettingsView>());
            SettingsActionBar actionBar = Assert.Single(Walk(settingsHost)
                .OfType<SettingsActionBar>());
            Assert.False(settings.HasEmbeddedActionBar);
            Assert.Equal("Changes apply immediately where supported.",
                actionBar.ContextCopy);

            InputSettings.ChatKey = Keys.Q;
            coordinator.ApplyHostPresentationForTests(
                State(visible: false, minimized: true));

            Assert.Equal(DesktopOverlayMode.Settings, coordinator.Mode);
            Assert.Same(settingsHost, surface.Content);
            Assert.Equal(Keys.Q, InputSettings.ChatKey);

            MenuEntry cancel = Assert.Single(Walk(actionBar).OfType<MenuEntry>(),
                entry => entry.Title == "Cancel");
            ((IControllerNavigable)cancel).ControllerActivate();

            Assert.Equal(DesktopOverlayMode.Pause, coordinator.Mode);
            Assert.IsType<PauseMenuView>(surface.Content);
            Assert.Equal(Keys.Y, InputSettings.ChatKey);
            coordinator.CloseFromMenu();
        }
        finally
        {
            prior.Restore();
        }
    }

    [AvaloniaFact]
    public void EmbeddedInGameSettingsReturnToPauseInsteadOfResumingGameplay()
    {
        PrimeShellView shell = PrimeShellView.CreateCapture(new MenuSettings(),
            Array.Empty<string>(), PrimeRoute.Play);
        var window = new Window { Width = 940, Height = 560, Content = shell };
        using var scene = new Scene();
        int resumed = 0;
        int left = 0;
        int quit = 0;
        window.Show();
        try
        {
            shell.ShowPauseMenu(scene, () => resumed++, () => left++, () => quit++);
            Dispatcher.UIThread.RunJobs();
            PauseMenuView pause = Assert.Single(shell.GetVisualDescendants()
                .OfType<PauseMenuView>());
            MenuEntry settingsEntry = Assert.Single(pause.GetVisualDescendants()
                .OfType<MenuEntry>(), entry => entry.Title == "Settings");

            ((IControllerNavigable)settingsEntry).ControllerActivate();
            Dispatcher.UIThread.RunJobs();

            SettingsView settings = Assert.Single(shell.GetVisualDescendants()
                .OfType<SettingsView>());
            Assert.True(settings.HasEmbeddedActionBar);
            SettingsActionBar actionBar = Assert.Single(settings.GetVisualDescendants()
                .OfType<SettingsActionBar>());
            MenuEntry cancel = Assert.Single(actionBar.GetVisualDescendants()
                .OfType<MenuEntry>(), entry => entry.Title == "Cancel");
            ((IControllerNavigable)cancel).ControllerActivate();
            Dispatcher.UIThread.RunJobs();

            PauseMenuView returnedPause = Assert.Single(shell.GetVisualDescendants()
                .OfType<PauseMenuView>());
            Assert.NotSame(pause, returnedPause);
            Assert.Equal(0, resumed);
            Assert.Equal(0, left);
            Assert.Equal(0, quit);
        }
        finally
        {
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static GameHostPresentationState State(bool visible, bool minimized)
        => new(new Vector2i(1280, 720), new Vector2i(2560, 1440),
            new Vector2i(10, 20), visible, minimized, IsFocused: true,
            ActivationDeferred: false, IsFullscreen: false);

    private static T Find<T>(Control root, string name) where T : Control
        => root.GetVisualDescendants().OfType<T>().Single(control =>
            String.Equals(control.Name, name, StringComparison.Ordinal));

    private static IEnumerable<Control> Walk(Control control)
    {
        yield return control;
        if (control is Panel panel)
        {
            foreach (Control child in panel.Children)
                foreach (Control descendant in Walk(child))
                    yield return descendant;
        }
        else if (control is ContentControl content && content.Content is Control contentChild)
        {
            foreach (Control descendant in Walk(contentChild))
                yield return descendant;
        }
        else if (control is Decorator decorator && decorator.Child is Control decoratorChild)
        {
            foreach (Control descendant in Walk(decoratorChild))
                yield return descendant;
        }
    }

    private sealed class RecordingSurface : IDesktopGameOverlaySurface
    {
        public DesktopOverlayMode Mode { get; private set; }
        public bool NativeVisible { get; private set; }
        public Control? Content { get; private set; }
        public event EventHandler? UserCloseRequested { add { } remove { } }
        public event EventHandler? Activated { add { } remove { } }
        public event EventHandler? Deactivated { add { } remove { } }

        public void SetContent(Control? content, DesktopOverlayMode mode)
        {
            Content = content;
            Mode = mode;
        }

        public void ShowForHost(GameHostPresentationState state, bool activate)
            => NativeVisible = true;

        public void HideForHostPreservingContent(GameHostPresentationState state)
            => NativeVisible = false;

        public void SetZOrderOwned(bool owned) { }

        public void ReleaseContent()
        {
            Content = null;
            Mode = DesktopOverlayMode.None;
            NativeVisible = false;
        }

        public void Dispose() { }
    }
}
