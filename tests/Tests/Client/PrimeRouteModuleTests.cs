using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class PrimeRouteModuleTests
{
    [Fact]
    public void HunterModuleKeepsGuestCareerHonestAndArsenalDiscoverable()
    {
        Control view = HunterPresentation.Build(new HunterPresentationContext(
            SignedIn: false,
            HunterSection.Overview,
            HunterLicensePageState.Initial,
            Array.Empty<HunterDossier>(),
            CanLoadMore: false,
            _ => { },
            () => { },
            (_, _) => { },
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => throw new InvalidOperationException(),
            _ => throw new InvalidOperationException(),
            (_, _) => throw new InvalidOperationException()));

        string text = TextOf(view);
        Assert.Contains("Sign in required", text, StringComparison.Ordinal);
        Assert.Contains("Arsenal", text, StringComparison.Ordinal);
        Assert.Contains("available to guests", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RankingsModuleProvidesLocalizedSignedOutState()
    {
        Control view = RankingsPresentation.Build(new RankingsPresentationContext(
            SignedIn: false,
            RankingsState.Initial,
            () => { },
            (_, _) => { },
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask));

        string text = TextOf(view);
        Assert.Contains("Sign in required", text, StringComparison.Ordinal);
        Assert.Contains("highlighted row", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheatreModuleShowsEmptyAndCapabilityStatesWithoutPaths()
    {
        Control view = TheatrePresentation.Build(new TheatrePresentationContext(
            TheatreState.Initial,
            SupportsImport: false,
            SupportsExport: false,
            SupportsRename: false,
            PendingDeleteId: null,
            (_, _) => { },
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => { },
            _ => Task.CompletedTask,
            _ => { },
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => { },
            () => { }));

        string text = TextOf(view);
        Assert.Contains("No replays yet", text, StringComparison.Ordinal);
        Assert.Contains("Import replay is unavailable", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/tmp/", text, StringComparison.Ordinal);
    }

    private static string TextOf(Control root)
        => String.Join('\n', Walk(root).OfType<TextBlock>().Select(text => text.Text));

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
}
