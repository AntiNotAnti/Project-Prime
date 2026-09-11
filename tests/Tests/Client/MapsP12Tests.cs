using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using AvaloniaButton = Avalonia.Controls.Button;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapGen;
using Xunit;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class MapsP12Tests
{
    [Fact]
    public void MapsStateSeparatesLibraryAuthoringAndCommunitySurfaces()
    {
        InstalledMap installed = Map(MapInstallSource.InstalledPackage,
            MapBuildState.Ready, "Library map");
        InstalledMap project = Map(MapInstallSource.LocalProject,
            MapBuildState.NeedsBuild, "Authoring map");
        InstalledMap legacy = Map(MapInstallSource.LegacyRecipe,
            MapBuildState.NeedsBuild, "Legacy authoring map");
        var state = new MapsState(MapsTab.Library,
            new MapCatalogSnapshot(4, [installed, project, legacy], []),
            Loading: false, Status: null, Error: null, SelectedIdentity: null);

        Assert.Equal(installed, Assert.Single(state.MapsFor(MapsTab.Library)));
        Assert.Equal([project, legacy], state.MapsFor(MapsTab.MyMaps).ToArray());
        Assert.Empty(state.MapsFor(MapsTab.Community));

        Assert.Equal("Library", MapsState.TabLabel(MapsTab.Library));
        Assert.Equal("My Maps", MapsState.TabLabel(MapsTab.MyMaps));
        Assert.Equal("Community", MapsState.TabLabel(MapsTab.Community));
    }

    [AvaloniaFact]
    public void ReadyLibraryCardOffersPlayAndDetailsWithoutTechnicalMetadata()
    {
        InstalledMap map = Map(MapInstallSource.InstalledPackage,
            MapBuildState.Ready, "Ready public map");
        using MapsPresentationView view = MapsPresentation.Build(
            Context(new MapsState(MapsTab.Library,
                new MapCatalogSnapshot(1, [map], []), false, null, null, null)));

        PrimeTabStrip tabs = Assert.Single(Walk(view).OfType<PrimeTabStrip>());
        Assert.Equal(["Library", "My Maps", "Community"],
            tabs.Tabs.Select(tab => tab.Label).ToArray());
        Assert.Equal("Library", tabs.SelectedTab.Label);

        ListBox? items = view.ItemsControl;
        Assert.NotNull(items);
        ListBox list = items!;
        Assert.NotNull(list.ItemsPanel);
        Assert.Single(list.ItemsSource!.Cast<InstalledMap>());

        FuncDataTemplate<InstalledMap> template =
            Assert.IsType<FuncDataTemplate<InstalledMap>>(list.ItemTemplate);
        Control? builtCard = template.Build(map, list);
        Assert.NotNull(builtCard);
        Control card = builtCard!;
        string[] actions = Walk(card).OfType<AvaloniaButton>()
            .Select(button => button.Content as string)
            .OfType<string>()
            .ToArray();
        Assert.Contains("Play", actions);
        Assert.Contains("Details", actions);
        Assert.DoesNotContain("Build", actions);
        Assert.DoesNotContain("Edit", actions);

        string cardText = String.Join('\n', Walk(card).OfType<TextBlock>()
            .Select(text => text.Text).OfType<string>());
        Assert.Contains(map.DisplayName, cardText, StringComparison.Ordinal);
        Assert.DoesNotContain(map.ContentIdentity.Identity.StableId, cardText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(map.ContentIdentity.ContentHash, cardText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(map.SourcePath, cardText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void MyMapsShowsAuthoringActionsAndCommunityStaysClean()
    {
        InstalledMap project = Map(MapInstallSource.LocalProject,
            MapBuildState.NeedsBuild, "Local project");
        using MapsPresentationView authoring = MapsPresentation.Build(
            Context(new MapsState(MapsTab.MyMaps,
                new MapCatalogSnapshot(1, [project], []), false, null, null, null)));
        ListBox? authoringItems = authoring.ItemsControl;
        Assert.NotNull(authoringItems);
        FuncDataTemplate<InstalledMap> authoringTemplate =
            Assert.IsType<FuncDataTemplate<InstalledMap>>(authoringItems!.ItemTemplate);
        Control? builtAuthoringCard = authoringTemplate.Build(project, authoringItems);
        Assert.NotNull(builtAuthoringCard);
        string[] authoringActions = Walk(authoring).OfType<AvaloniaButton>()
            .Select(button => button.Content as string)
            .OfType<string>()
            .Concat(Walk(builtAuthoringCard!).OfType<AvaloniaButton>()
                .Select(button => button.Content as string).OfType<string>())
            .ToArray();
        Assert.Contains("Create Map", authoringActions);
        Assert.Contains("Import Q3", authoringActions);
        Assert.Contains("Edit", authoringActions);
        Assert.Contains("Build", authoringActions);
        Assert.Contains("Export", authoringActions);
        Assert.DoesNotContain("Install Map", authoringActions);
        Assert.DoesNotContain("Play", authoringActions);

        using MapsPresentationView community = MapsPresentation.Build(
            Context(new MapsState(MapsTab.Community,
                new MapCatalogSnapshot(1, [project], []), false, null, null, null)));
        Assert.Null(community.ItemsControl);
        string communityText = String.Join('\n', Walk(community).OfType<TextBlock>()
            .Select(text => text.Text).OfType<string>());
        Assert.Contains("Community maps are coming later.", communityText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Create Map", communityText, StringComparison.Ordinal);
        Assert.DoesNotContain("Import Q3", communityText, StringComparison.Ordinal);
        Assert.DoesNotContain("Install Map", communityText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void MyMapsWithoutCreatorToolsDirectsUsersToDesktopEditor()
    {
        var state = new MapsState(MapsTab.MyMaps, MapCatalogSnapshot.Empty,
            Loading: false, Status: null, Error: null, SelectedIdentity: null);
        using MapsPresentationView view = MapsPresentation.Build(new MapsPresentationContext(
            state, CaptureMode: false, SelectTab: _ => { }, Refresh: () => { },
            Install: () => { }, Create: null, ImportQ3: null, Play: _ => { },
            Build: _ => { }, Edit: null, Export: null, AddTexture: null,
            Remove: _ => { }, Details: _ => { }, LoadPreview: null));

        string text = String.Join('\n', Walk(view).OfType<TextBlock>()
            .Select(block => block.Text).OfType<string>());
        Assert.Contains("bundled desktop Project Prime Editor", text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Create Map", Walk(view).OfType<AvaloniaButton>()
            .Select(button => button.Content as string));
    }

    [Fact]
    public async Task PreviewCacheRejectsMalformedBytesAndHonorsCancellation()
    {
        using var cache = new MapsPreviewCache(capacity: 2);
        Assert.Null(await cache.LoadAsync("invalid",
            _ => Task.FromResult<byte[]?>([0, 1, 2, 3])));
        Assert.Equal(0, cache.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapsPreviewCache(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapsPreviewCache(257));

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.LoadAsync(
            "canceled", _ => Task.FromResult<byte[]?>([1]), canceled.Token));
    }

    [Fact]
    public async Task ControllerRefreshPublishesImmutableRouteState()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "project-prime-maps-p12-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var catalog = new MapCatalog(new MapCatalogOptions
            {
                InstalledDirectory = Path.Combine(root, "installed"),
                ProjectDirectories = [Path.Combine(root, "projects")],
                CacheDirectory = Path.Combine(root, "cache")
            });
            using var previews = new MapsPreviewCache(2);
            using var controller = new MapsController(catalog, previews);
            int changes = 0;
            controller.Changed += (_, _) => changes++;

            await controller.RefreshAsync();

            Assert.False(controller.State.Loading);
            Assert.Empty(controller.State.Catalog.Maps);
            Assert.Empty(controller.State.Catalog.Diagnostics);
            Assert.True(changes >= 1);
            Assert.Same(controller.State, controller.State);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static MapsPresentationContext Context(MapsState state)
        => new(state, CaptureMode: false,
            SelectTab: _ => { }, Refresh: () => { }, Install: () => { },
            Create: () => { }, ImportQ3: () => { }, Play: _ => { }, Build: _ => { },
            Edit: _ => { }, Export: _ => { }, AddTexture: _ => { }, Remove: _ => { },
            Details: _ => { }, LoadPreview: null);

    private static InstalledMap Map(MapInstallSource source, MapBuildState buildState,
        string name)
    {
        var identity = new MapContentIdentity(
            new MapIdentity("community.maps-p12", new MapVersion(1, 0, 0)),
            new string('a', 64));
        var project = new MapProject
        {
            StableId = identity.Identity.StableId,
            Version = identity.Identity.Version,
            Metadata = new MapProjectMetadata { Name = name, Author = "Tests" },
            SourcePath = "/tmp/maps-p12-project.json"
        };
        return new InstalledMap(identity, name, "Tests", "Public map description", source,
            [MapMode.Battle], buildState, null, "/tmp/maps-p12-source.fpmap",
            new string('b', 64), 128, DateTimeOffset.UnixEpoch, null, [], project);
    }

    private static IEnumerable<Control> Walk(Control control)
    {
        yield return control;
        if (control is Panel panel)
        {
            foreach (Control panelChild in panel.Children)
            {
                foreach (Control descendant in Walk(panelChild))
                    yield return descendant;
            }
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
