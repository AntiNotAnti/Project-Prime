using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using AvaloniaButton = Avalonia.Controls.Button;
using MphRead;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class TheatrePresentationTests
{
    [AvaloniaFact]
    public void NormalReplaySurfacesUseFriendlyMetadataAndKeepRawValuesAdvanced()
    {
        PrimeReplayEntry replay = Replay();
        Control view = TheatrePresentation.Build(Context(replay,
            supportsImport: true, supportsExport: true, supportsRename: true,
            supportsReveal: true));

        Expander advanced = Assert.Single(Walk(view).OfType<Expander>(),
            item => Equals(item.Header, "Advanced"));
        Assert.False(advanced.IsExpanded);
        Control advancedContent = Assert.IsAssignableFrom<Control>(advanced.Content);
        string advancedText = TextOf(advancedContent);
        Assert.Contains(replay.FileName, advancedText, StringComparison.Ordinal);
        Assert.Contains(replay.Path, advancedText, StringComparison.Ordinal);
        Assert.Contains(replay.Room, advancedText, StringComparison.Ordinal);
        Assert.Contains(replay.Recorded.ToString("O"), advancedText,
            StringComparison.Ordinal);
        Assert.Contains(replay.Bytes.ToString(CultureInfo.InvariantCulture), advancedText,
            StringComparison.Ordinal);

        PrimeSectionPanel[] normalPanels = Walk(view).OfType<PrimeSectionPanel>().ToArray();
        Assert.Equal(2, normalPanels.Length);
        string normalText = String.Join('\n', normalPanels.Select(TextOf));
        Assert.Contains("Alinos Perch", normalText, StringComparison.Ordinal);
        Assert.Contains("Sep 9, 2026 · 18:42", normalText, StringComparison.Ordinal);
        Assert.Contains("2.4 MB", normalText, StringComparison.Ordinal);
        Assert.DoesNotContain(replay.FileName, normalText, StringComparison.Ordinal);
        Assert.DoesNotContain(replay.Path, normalText, StringComparison.Ordinal);
        Assert.DoesNotContain(replay.Room, normalText, StringComparison.Ordinal);
        Assert.DoesNotContain(replay.Recorded.ToString("O"), normalText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(replay.Bytes.ToString(CultureInfo.InvariantCulture), normalText,
            StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void ReplaySelectionIsSingleSurfaceWithDirectWatchAndCompactManagement()
    {
        PrimeReplayEntry replay = Replay();
        Control view = TheatrePresentation.Build(Context(replay,
            supportsImport: true, supportsExport: true, supportsRename: true,
            supportsReveal: true));
        PrimeSectionPanel[] panels = Walk(view).OfType<PrimeSectionPanel>().ToArray();
        PrimeSectionPanel list = panels[0];
        PrimeSectionPanel detail = panels[1];

        Assert.Empty(Walk(list).OfType<PrimeCard>());
        PrimeSelectedRow selectedRow = Assert.Single(Walk(list)
            .OfType<PrimeSelectedRow>());
        Assert.True(selectedRow.IsSelected);
        Assert.Contains("prime-selected", selectedRow.Classes);
        Assert.Contains(Walk(list).OfType<AvaloniaButton>(),
            button => Equals(button.Content, "Watch"));

        AvaloniaButton watchReplay = Assert.Single(Walk(detail).OfType<AvaloniaButton>(),
            button => Equals(button.Content, "Watch Replay"));
        Assert.Equal(HorizontalAlignment.Left, watchReplay.HorizontalAlignment);
        Assert.Equal(160d, watchReplay.MinWidth);
        Assert.Contains("prime-primary", watchReplay.Classes);

        PrimePreviewStage preview = Assert.Single(Walk(detail)
            .OfType<PrimePreviewStage>());
        Assert.NotNull(preview.Child);
        Assert.Equal(220d, preview.Child.Height);

        Expander manage = Assert.Single(Walk(detail).OfType<Expander>(),
            item => Equals(item.Header, "Manage replay"));
        Assert.False(manage.IsExpanded);
        Control managementContent = Assert.IsAssignableFrom<Control>(manage.Content);
        string[] labels = Walk(managementContent).OfType<AvaloniaButton>()
            .Select(button => button.Content as string)
            .OfType<string>()
            .ToArray();
        Assert.Contains("Export Replay", labels);
        Assert.Contains("Show File", labels);
        Assert.Contains("Rename", labels);
        Assert.Contains("Delete", labels);
    }

    [AvaloniaFact]
    public void ManagementDoesNotOfferUnsupportedFileCapabilities()
    {
        Control view = TheatrePresentation.Build(Context(Replay(),
            supportsImport: false, supportsExport: false, supportsRename: false,
            supportsReveal: false));
        Expander manage = Assert.Single(Walk(view).OfType<Expander>(),
            item => Equals(item.Header, "Manage replay"));
        Control managementContent = Assert.IsAssignableFrom<Control>(manage.Content);
        string[] labels = Walk(managementContent).OfType<AvaloniaButton>()
            .Select(button => button.Content as string)
            .OfType<string>()
            .ToArray();

        Assert.Equal(new[] { "Delete" }, labels);
    }

    [AvaloniaFact]
    public void PopulatedReplayUsesPreviewFirstCardsAndAccessibleBoundedTimeline()
    {
        ReplayLibraryMetadata metadata = new(
            new string('a', 64), "/private/replays/generated-123.fpreplay",
            "generated-123.fpreplay", "AD2 ALINOS PERCH", "Alinos Perch",
            GameMode.BattleTeams, new DateTime(2026, 9, 9, 18, 42, 0),
            TimeSpan.FromSeconds(12), 2_500_000, ReplayFile.IndexedFormatVersion,
            NetHeader.Version, 4, 0, ReplayRecoveryStatus.Complete,
            ReplayCompatibilityStatus.Ready);
        PrimeReplayEntry replay = Replay() with { Metadata = metadata };
        TheatreState state = new([replay], replay, Loading: false, Error: null,
            Timeline:
            [
                new ReplayEventTimelineMarker(0, ReplayMarker.Kill, "KILL"),
                new ReplayEventTimelineMarker(720, ReplayMarker.MatchEnd, "MATCH_END")
            ]);
        Control view = TheatrePresentation.Build(Context(state, replay,
            supportsImport: false, supportsExport: false, supportsRename: false,
            supportsReveal: false));

        PrimeSelectedRow listRow = Assert.Single(Walk(view).OfType<PrimeSelectedRow>());
        Assert.Contains("prime-replay-row", listRow.Classes);
        Assert.Contains(Walk(listRow), control =>
            control.Classes.Contains("prime-replay-card-preview"));

        PrimeSectionPanel detail = Assert.Single(
            Walk(view).OfType<PrimeSectionPanel>(), panel =>
                TextOf(panel).Contains("Replay details", StringComparison.Ordinal));
        PrimePreviewStage preview = Assert.Single(Walk(detail)
            .OfType<PrimePreviewStage>());
        StackPanel detailContent = Assert.IsType<StackPanel>(detail.Child);
        Assert.Same(preview, detailContent.Children[1]);
        string detailText = TextOf(detail);
        Assert.Contains("Team Battle", detailText, StringComparison.Ordinal);
        Assert.Contains("00:12", detailText, StringComparison.Ordinal);
        Assert.Contains("Outcome unavailable", detailText, StringComparison.Ordinal);
        Assert.DoesNotContain("Compatibility", detailText, StringComparison.Ordinal);
        Assert.DoesNotContain("Replay protocol", detailText, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayTimelineMarkerRatioAndLabelsHandleZeroDuration()
    {
        Assert.Equal(0.5, TheatrePresentation.ReplayTimelineMarkerRatio(
            frame: 0, durationFrames: 0, index: 0, count: 1));
        Assert.Equal(1, TheatrePresentation.ReplayTimelineMarkerRatio(
            frame: 1_800, durationFrames: 1_800, index: 0, count: 1));
        Assert.Equal(0, TheatrePresentation.ReplayTimelineMarkerRatio(
            frame: 900, durationFrames: 0, index: 0, count: 3));
        Assert.Equal(1, TheatrePresentation.ReplayTimelineMarkerRatio(
            frame: 0, durationFrames: 0, index: 2, count: 3));
        Assert.Equal(156, TheatrePresentation.ReplayTimelineMarkerLeft(
            canvasWidth: 200, markerWidth: 44, ratio: 1));
        Assert.Equal(0, TheatrePresentation.ReplayTimelineMarkerLeft(
            canvasWidth: 200, markerWidth: 44, ratio: 0));
        Assert.Equal("Match end", TheatrePresentation.ReplayEventLabel("MATCH_END"));
        Assert.Equal("Event", TheatrePresentation.ReplayEventLabel("_"));
    }

    private static PrimeReplayEntry Replay()
        => new("generated-id", "/private/replays/generated-123.fpreplay",
            "generated-123.fpreplay", "AD2 ALINOS PERCH",
            new DateTime(2026, 9, 9, 18, 42, 0), 2_500_000);

    private static TheatrePresentationContext Context(PrimeReplayEntry replay,
        bool supportsImport, bool supportsExport, bool supportsRename,
        bool supportsReveal)
        => Context(new TheatreState([replay], replay, Loading: false, Error: null),
            replay, supportsImport, supportsExport, supportsRename, supportsReveal);

    private static TheatrePresentationContext Context(TheatreState state,
        PrimeReplayEntry replay, bool supportsImport, bool supportsExport,
        bool supportsRename, bool supportsReveal)
        => new(
            state,
            supportsImport,
            supportsExport,
            supportsRename,
            PendingDeleteId: null,
            (_, task) => task().GetAwaiter().GetResult(),
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
            () => { },
            supportsReveal,
            _ => Task.CompletedTask,
            _ => Task.FromResult<PrimePreviewImage?>(null));

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
