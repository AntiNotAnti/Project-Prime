using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.Mods;
using MphRead.Mods.Launcher.Gui;
using Xunit;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class BrandPresentationTests
{
    [AvaloniaFact]
    public void ShellHeaderOwnsTheOnlyGatewayBrandMarkAndLandingStaysActionFirst()
    {
        PrimeShellView shell = PrimeShellView.CreateCapture(new MenuSettings(),
            Array.Empty<string>(), PrimeRoute.Gateway);
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Single(shell.GetVisualDescendants().OfType<PrimeBrandMark>());
            Assert.Empty(shell.GetVisualDescendants().OfType<PrimeDiamond>());

            ContentControl pageHost = shell.GetVisualDescendants()
                .OfType<ContentControl>()
                .Single(control => control.Name == "PageHost");
            Control gateway = Assert.IsAssignableFrom<Control>(pageHost.Content);
            string[] copy = gateway.GetVisualDescendants().OfType<TextBlock>()
                .Select(text => text.Text)
                .Where(text => !String.IsNullOrWhiteSpace(text))
                .Select(text => text!)
                .ToArray();
            Assert.Contains("ENTER THE ARENA", copy);
            Assert.Contains("Choose how you want to play.", copy);
            Assert.DoesNotContain("PROJECT PRIME", copy);
            Assert.DoesNotContain("Play now", copy);
            Assert.Empty(gateway.GetVisualDescendants().OfType<PrimeBrandMark>());
            Assert.Empty(gateway.GetVisualDescendants().OfType<PrimeDiamond>());

            AvaloniaButton[] actions = gateway.GetVisualDescendants()
                .OfType<AvaloniaButton>()
                .Where(button => button.Content is string)
                .ToArray();
            Assert.Equal(["Play as Guest", "Sign In", "Create Account"],
                actions.Select(button => (string)button.Content!).ToArray());
            Assert.Contains("prime-primary", actions[0].Classes);
            Assert.DoesNotContain("prime-primary", actions[1].Classes);
            Assert.DoesNotContain("prime-primary", actions[2].Classes);
        }
        finally
        {
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void TitleFallbackAndContinuePromptUseBrandSemantics()
    {
        using var title = new PrimeTitleScreenView(PrimeTitleScreenPhase.Ready,
            reducedMotion: true, allowBlink: false,
            openBackground: () => throw new InvalidOperationException("fixture"));
        var window = new Window { Width = 940, Height = 560, Content = title };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Single(title.GetVisualDescendants().OfType<PrimeBrandMark>());
            Assert.Empty(title.GetVisualDescendants().OfType<PrimeDiamond>());

            TextBlock brand = title.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text == "PROJECT PRIME");
            TextBlock prompt = title.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Name == "PromptText");
            TextBlock loading = title.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Name == "LoadingText");
            Assert.Equal(GuiTheme.Brand,
                Assert.IsType<SolidColorBrush>(brand.Foreground).Color);
            Assert.Equal(GuiTheme.Brand,
                Assert.IsType<SolidColorBrush>(loading.Foreground).Color);
            Assert.Equal(GuiTheme.Brand,
                Assert.IsType<SolidColorBrush>(prompt.Foreground).Color);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }
}
