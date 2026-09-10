using System;
using System.Linq;
using Avalonia;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests;

public sealed class PrimeInputNavigatorTests
{
    [Fact]
    public void SpatialNavigationChoosesTheAlignedCandidateInEveryDirection()
    {
        PrimeNavigationCandidate a = Candidate("a", 0, 0);
        PrimeNavigationCandidate b = Candidate("b", 100, 0);
        PrimeNavigationCandidate c = Candidate("c", 0, 100);
        PrimeNavigationCandidate d = Candidate("d", 100, 100);
        PrimeNavigationCandidate[] grid = [a, b, c, d];

        Assert.Equal("b", PrimeSpatialNavigation.Select("a", grid,
            PrimeNavigationDirection.Right)!.FocusId);
        Assert.Equal("c", PrimeSpatialNavigation.Select("a", grid,
            PrimeNavigationDirection.Down)!.FocusId);
        Assert.Equal("c", PrimeSpatialNavigation.Select("d", grid,
            PrimeNavigationDirection.Left)!.FocusId);
        Assert.Equal("b", PrimeSpatialNavigation.Select("d", grid,
            PrimeNavigationDirection.Up)!.FocusId);
    }

    [Fact]
    public void SpatialNavigationPrefersAlignedRowOverDiagonalAndBreaksTiesByStableId()
    {
        PrimeNavigationCandidate current = Candidate("current", 0, 0);
        PrimeNavigationCandidate aligned = Candidate("aligned", 80, 0);
        PrimeNavigationCandidate diagonal = Candidate("diagonal", 60, 60);
        PrimeNavigationCandidate firstTie = Candidate("a-tie", 100, 0);
        PrimeNavigationCandidate secondTie = Candidate("b-tie", 100, 0);

        Assert.Equal("aligned", PrimeSpatialNavigation.Select("current",
            [diagonal, aligned], PrimeNavigationDirection.Right)!.FocusId);
        Assert.Equal("a-tie", PrimeSpatialNavigation.Select("current",
            [secondTie, firstTie], PrimeNavigationDirection.Right)!.FocusId);
        Assert.Equal("a-tie", PrimeSpatialNavigation.Select("current",
            [firstTie, secondTie], PrimeNavigationDirection.Right)!.FocusId);
    }

    [Fact]
    public void LocalBoundsAreComparedAfterTranslationToOneCoordinateRoot()
    {
        PrimeNavigationCandidate current = new("current",
            PrimeNavigationBounds.FromLocal(new Rect(0, 0, 20, 20),
                new Point(400, 300), "shell"));
        PrimeNavigationCandidate right = new("right",
            PrimeNavigationBounds.FromLocal(new Rect(40, 0, 20, 20),
                new Point(400, 300), "shell"));
        PrimeNavigationCandidate down = new("down",
            PrimeNavigationBounds.FromLocal(new Rect(0, 40, 20, 20),
                new Point(400, 300), "shell"));

        Assert.Equal("right", PrimeSpatialNavigation.Select("current",
            [down, right, current], PrimeNavigationDirection.Right)!.FocusId);
        Assert.Equal("down", PrimeSpatialNavigation.Select("current",
            [right, current, down], PrimeNavigationDirection.Down)!.FocusId);

        PrimeNavigationCandidate translatedCurrent = new("current",
            PrimeNavigationBounds.FromLocal(new Rect(0, 0, 20, 20),
                new Point(-100, -50), "shell"));
        PrimeNavigationCandidate translatedRight = new("right",
            PrimeNavigationBounds.FromLocal(new Rect(40, 0, 20, 20),
                new Point(-100, -50), "shell"));
        Assert.Equal("right", PrimeSpatialNavigation.Select("current",
            [translatedRight, translatedCurrent], PrimeNavigationDirection.Right)!.FocusId);
    }

    [Fact]
    public void IneligibleCandidatesAreSkippedButRevealableOffscreenTargetsRemainEligible()
    {
        PrimeNavigationCandidate current = Candidate("current", 0, 0);
        PrimeNavigationCandidate disabled = Candidate("disabled", 40, 0,
            isEnabled: false);
        PrimeNavigationCandidate hidden = Candidate("hidden", 60, 0,
            isVisible: false);
        PrimeNavigationCandidate notFocusable = Candidate("not-focusable", 80, 0,
            isFocusable: false);
        PrimeNavigationCandidate blockedOffscreen = Candidate("blocked", 100, 0,
            isOffscreen: true);
        PrimeNavigationCandidate revealable = Candidate("revealable", 120, 0,
            isOffscreen: true, canScrollIntoView: true, scrollHostId: "rows");

        PrimeNavigationResult result = PrimeSpatialNavigation.Navigate("current",
            [revealable, blockedOffscreen, notFocusable, hidden, disabled, current],
            PrimeNavigationDirection.Right);

        Assert.True(result.Moved);
        Assert.Equal("revealable", result.FocusId);
        Assert.Equal("rows", result.ScrollRequest!.Value.ScrollHostId);
    }

    [Fact]
    public void LayerPriorityKeepsModalEditorPageAndChromeSeparate()
    {
        PrimeNavigationCandidate chrome = Candidate("chrome", 200, 0,
            layer: PrimeNavigationLayer.ShellChrome);
        PrimeNavigationCandidate page = Candidate("page", 0, 0,
            layer: PrimeNavigationLayer.ActivePage);
        PrimeNavigationCandidate editor = Candidate("editor", 0, 60,
            layer: PrimeNavigationLayer.OpenEditor);
        PrimeNavigationCandidate modal = Candidate("modal", 0, 120,
            layer: PrimeNavigationLayer.Modal, modalId: "confirm");

        Assert.Equal("modal", PrimeSpatialNavigation.Select("page",
            [chrome, page, editor, modal], PrimeNavigationDirection.Down)!.FocusId);
        Assert.Equal("editor", PrimeSpatialNavigation.Select("page",
            [chrome, page, editor], PrimeNavigationDirection.Down)!.FocusId);
        Assert.Equal("page", PrimeSpatialNavigation.Select("unknown",
            [chrome, page], PrimeNavigationDirection.Down)!.FocusId);
        Assert.Equal("chrome", PrimeSpatialNavigation.Select("page",
            [chrome, page], PrimeNavigationDirection.Right)!.FocusId);
        Assert.Equal("page", PrimeSpatialNavigation.Select("chrome",
            [chrome, page], PrimeNavigationDirection.Left)!.FocusId);

        var navigator = new PrimeInputNavigator();
        navigator.EnterScope(new PrimeFocusScope("hunter", "overview"),
            [page, chrome]);
        Assert.Equal("chrome", navigator.Move(PrimeNavigationDirection.Right,
            [page, chrome]).FocusId);
    }

    [Fact]
    public void ModalOpenTrapsFocusAndCloseRestoresPreviousStableId()
    {
        var navigator = new PrimeInputNavigator();
        PrimeFocusScope pageScope = new("play", "home");
        PrimeNavigationCandidate pageA = Candidate("page-a", 0, 0);
        PrimeNavigationCandidate pageB = Candidate("page-b", 100, 0);
        PrimeNavigationCandidate modalCancel = Candidate("cancel", 0, 0,
            layer: PrimeNavigationLayer.Modal, modalId: "dialog");
        PrimeNavigationCandidate modalAccept = Candidate("accept", 100, 0,
            layer: PrimeNavigationLayer.Modal, priority: 10, modalId: "dialog");

        navigator.EnterScope(pageScope, [pageA, pageB]);
        navigator.Move(PrimeNavigationDirection.Right, [pageA, pageB]);
        Assert.Equal("page-b", navigator.FocusedId);

        PrimeNavigationResult opened = navigator.OpenModal("dialog",
            [pageA, pageB, modalCancel, modalAccept]);
        Assert.Equal("accept", opened.FocusId);
        Assert.Equal("dialog", navigator.ActiveModalId);

        PrimeNavigationResult trapped = navigator.Move(PrimeNavigationDirection.Left,
            [pageA, pageB, modalCancel, modalAccept]);
        Assert.Equal("cancel", trapped.FocusId);
        Assert.DoesNotContain(navigator.FocusedId, new[] { "page-a", "page-b" });

        PrimeNavigationResult closed = navigator.CloseModal("dialog",
            [pageA, pageB, modalCancel, modalAccept]);
        Assert.Equal("page-b", closed.FocusId);
        Assert.Null(navigator.ActiveModalId);
        Assert.Equal(pageScope, navigator.CurrentScope);
    }

    [Fact]
    public void FocusMemoryIsPerScopeAndEvictsLeastRecentlyUsedEntry()
    {
        var memory = new PrimeFocusMemory(capacity: 2);
        PrimeFocusScope first = new("play", "home");
        PrimeFocusScope second = new("play", "hunter");
        PrimeFocusScope third = new("settings", "controller");
        memory.Remember(first, "first");
        memory.Remember(second, "second");
        Assert.True(memory.TryGet(first, out _)); // Make second the eviction target.
        memory.Remember(third, "third");

        Assert.True(memory.TryGet(first, out string firstId));
        Assert.Equal("first", firstId);
        Assert.False(memory.TryGet(second, out _));
        Assert.True(memory.TryGet(third, out string thirdId));
        Assert.Equal("third", thirdId);
        Assert.Equal(2, memory.Count);
    }

    [Fact]
    public void EnterScopeRestoresMemoryOnlyWhenTheCandidateStillExists()
    {
        var navigator = new PrimeInputNavigator();
        PrimeFocusScope scope = new("settings", "controller");
        PrimeNavigationCandidate first = Candidate("first", 0, 0);
        PrimeNavigationCandidate second = Candidate("second", 100, 0);
        navigator.EnterScope(scope, [first, second]);
        navigator.Move(PrimeNavigationDirection.Right, [first, second]);
        Assert.Equal("second", navigator.FocusedId);

        PrimeNavigationResult restored = navigator.EnterScope(scope, [first, second]);
        Assert.Equal("second", restored.FocusId);
        PrimeNavigationResult fallback = navigator.EnterScope(scope, [first]);
        Assert.Equal("first", fallback.FocusId);
    }

    [Fact]
    public void SectionMovementIsCyclicAndIndependentFromSpatialMovement()
    {
        PrimeNavigationSection[] sections =
        [
            new("settings", 10, "settings-row"),
            new("play", 0, "play-action"),
            new("hidden", 20, "hidden-row", isVisible: false),
            new("hunter", 11, "hunter-action")
        ];

        Assert.Equal("hunter", PrimeSectionNavigation.Move("settings", sections,
            PrimeNavigationSectionDirection.Next).SectionId);
        Assert.Equal("play", PrimeSectionNavigation.Move("settings", sections,
            PrimeNavigationSectionDirection.Previous).SectionId);
        Assert.Equal("hunter", PrimeSectionNavigation.Move("play", sections,
            PrimeNavigationSectionDirection.Previous).SectionId);
        Assert.Equal("play", PrimeSectionNavigation.Move("hunter", sections,
            PrimeNavigationSectionDirection.Next).SectionId);

        var navigator = new PrimeInputNavigator();
        PrimeFocusScope scope = new("settings", "settings");
        navigator.EnterScope(scope, [Candidate("row", 0, 0)]);
        PrimeSectionNavigationResult result = navigator.MoveSection("settings", sections,
            PrimeNavigationSectionDirection.Next);
        Assert.True(result.Changed);
        Assert.Equal("hunter", result.SectionId);
    }

    [Fact]
    public void BumpersMapToSectionsWithoutChangingDirectionalSemantics()
    {
        Assert.True(PrimeSectionNavigation.TryGetDirection(GamepadButtons.LeftBumper,
            out PrimeNavigationSectionDirection previous));
        Assert.Equal(PrimeNavigationSectionDirection.Previous, previous);
        Assert.True(PrimeSectionNavigation.TryGetDirection(GamepadButtons.RightBumper,
            out PrimeNavigationSectionDirection next));
        Assert.Equal(PrimeNavigationSectionDirection.Next, next);
        Assert.False(PrimeSectionNavigation.TryGetDirection(GamepadButtons.DpadRight,
            out _));
    }

    [Fact]
    public void ComboSessionDelegatesOpenNextPreviousAcceptAndCancelToExistingNavigation()
    {
        var session = new PrimeControllerComboSession();
        session.Begin(ControllerComboSelector.Mode, selectedIndex: 1, itemCount: 3);
        Assert.True(session.IsOpen);
        Assert.Equal(1, session.State.OriginalIndex);

        Assert.Equal(2, session.Transition(3, ControllerComboCommand.Next).SelectedIndex);
        Assert.Equal(1, session.Transition(3, ControllerComboCommand.Previous).SelectedIndex);
        Assert.Equal(1, session.Transition(3, ControllerComboCommand.Cancel).SelectedIndex);
        Assert.False(session.IsOpen);

        session.Transition(3, ControllerComboCommand.Accept);
        session.Transition(3, ControllerComboCommand.Next);
        ControllerComboState committed = session.Transition(3,
            ControllerComboCommand.Accept);
        Assert.False(committed.IsOpen);
        Assert.Equal(2, committed.SelectedIndex);
        Assert.Equal(ControllerComboSelector.Mode, committed.Selector);
    }

    private static PrimeNavigationCandidate Candidate(string id, double x, double y,
        PrimeNavigationLayer layer = PrimeNavigationLayer.ActivePage, int priority = 0,
        bool isEnabled = true, bool isVisible = true, bool isFocusable = true,
        bool isOffscreen = false, bool canScrollIntoView = false,
        string? scrollHostId = null, string? modalId = null)
        => new(id, new Rect(x, y, 20, 20), layer, priority, isEnabled, isVisible,
            isFocusable, isOffscreen, canScrollIntoView, scrollHostId,
            modalId: modalId);
}
