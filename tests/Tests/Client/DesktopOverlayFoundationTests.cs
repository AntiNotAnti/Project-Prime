using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Gui;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class DesktopOverlayFoundationTests
{
    [Theory]
    [InlineData(false, false, true, false, false, false)]
    [InlineData(false, true, true, false, false, true)]
    [InlineData(true, false, true, false, false, true)]
    [InlineData(true, true, true, false, true, false)]
    [InlineData(true, true, false, false, false, false)]
    [InlineData(true, true, true, true, false, false)]
    public void NativeWindowFocusIsAcceptedOnlyAfterDeferredActivationEnds(
        bool nativeInputFocus, bool keyboardFocus, bool isVisible,
        bool isMinimized, bool activationDeferred, bool expected)
    {
        Assert.Equal(expected,
            SdlGameHost.ResolveNativeFocus(nativeInputFocus, keyboardFocus,
                isVisible, isMinimized, activationDeferred));
    }

    [Theory]
    [InlineData(true, false, false, true, false, false, true)]
    [InlineData(false, true, true, true, false, false, false)]
    [InlineData(true, true, true, true, true, false, false)]
    [InlineData(true, true, true, true, false, true, false)]
    [InlineData(true, true, true, true, false, false, true)]
    public void FocusEventWinsOverLaggingNativeStateForCurrentPump(
        bool focusEvent, bool nativeInputFocus, bool keyboardFocus,
        bool isVisible, bool isMinimized, bool activationDeferred,
        bool expected)
    {
        Assert.Equal(expected, SdlGameHost.ResolveFocusAfterPump(focusEvent,
            nativeInputFocus, keyboardFocus, isVisible, isMinimized,
            activationDeferred));
    }

    [Theory]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, false, true, false, true)]
    [InlineData(false, false, false, false, false)]
    public void OverlayActivationEligibilityRequiresVisibleRestoredFocusedHost(
        bool visible, bool minimized, bool focused, bool activationDeferred,
        bool expected)
    {
        Assert.Equal(expected, DesktopGameOverlayCoordinator.IsHostActivationEligible(
            State(visible, minimized, focused, activationDeferred)));
    }

    [Theory]
    [InlineData(true, false, false, true, false, false)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(true, true, true, true, false, false)]
    [InlineData(false, false, true, true, false, false)]
    [InlineData(true, false, true, false, false, true)]
    [InlineData(true, false, true, false, true, true)]
    [InlineData(true, false, false, true, true, true)]
    public void InteractivePresentationRequiresDrawableVisibleSurfaceAndFocusOrPreparation(
        bool visible, bool minimized, bool focused, bool occluded,
        bool activationDeferred, bool expected)
    {
        Assert.Equal(expected,
            SdlGameHost.IsInteractivePresentationAvailable(visible, minimized,
                occluded, focused, activationDeferred));
    }

    [Theory]
    [InlineData((int)DesktopInputOwnerKind.None, true, false, true)]
    [InlineData((int)DesktopInputOwnerKind.None, true, true, false)]
    [InlineData((int)DesktopInputOwnerKind.None, false, false, false)]
    [InlineData((int)DesktopInputOwnerKind.Scene, true, false, false)]
    [InlineData((int)DesktopInputOwnerKind.Overlay, true, false, false)]
    public void FocusRegainRepairsOnlyAnOrphanedLiveScene(
        int currentOwner, bool hasPresentation,
        bool pauseOpen, bool expected)
    {
        Assert.Equal(expected, SdlGameHost.ShouldRestoreSceneInputOwner(
            (DesktopInputOwnerKind)currentOwner, hasPresentation, pauseOpen));
    }

    [Fact]
    public void PresentationTrackerDeduplicatesAndRetainsLastValidGeometryWhenMinimized()
    {
        var tracker = new GameHostPresentationTracker(new(1280, 720),
            new(2560, 1440), new(10, 20));
        int changes = 0;
        tracker.Changed += _ => changes++;

        tracker.UpdateVisibility(false, true);
        tracker.UpdateGeometry(new(0, 0), new(0, 0), new(10, 20));
        tracker.UpdateVisibility(false, true);

        Assert.Equal(1, changes);
        Assert.Equal(new Vector2i(1280, 720), tracker.Current.LogicalSizeUnits);
        Assert.Equal(new Vector2i(2560, 1440), tracker.Current.FramebufferSizePixels);
        Assert.True(tracker.Current.IsMinimized);
        Assert.False(tracker.Current.IsVisible);
    }

    [Fact]
    public void PresentationTrackerPublishesVisibilityFocusGeometryAndFullscreenTransitions()
    {
        var tracker = new GameHostPresentationTracker(new(1280, 720),
            new(2560, 1440), new(10, 20));
        var changes = new List<GameHostPresentationState>();
        tracker.Changed += changes.Add;

        // Resize and move, then enter fullscreen.
        tracker.UpdateGeometry(new(1600, 900), new(3200, 1800), new(30, 40));
        tracker.Update(true, false, true, false, new(1600, 900),
            new(3200, 1800), new(30, 40), isFullscreen: true);
        // Focus loss is explicitly deferred; minimize/restore and hide/show
        // must preserve that state until a real focus event arrives.
        tracker.UpdateFocus(false, true);
        tracker.UpdateVisibility(false, true);
        tracker.UpdateVisibility(true, false);
        tracker.UpdateVisibility(false, false);
        tracker.UpdateVisibility(true, false);
        tracker.UpdateFocus(true, false);
        tracker.Update(true, false, true, false, new(1600, 900),
            new(3200, 1800), new(30, 40), isFullscreen: false);
        tracker.Update(true, false, true, false, new(1600, 900),
            new(3200, 1800), new(30, 40), isFullscreen: false);

        Assert.Equal(9, changes.Count);
        Assert.Equal(new Vector2i(1600, 900), changes[0].LogicalSizeUnits);
        Assert.Equal(new Vector2i(3200, 1800), changes[0].FramebufferSizePixels);
        Assert.Equal(new Vector2i(30, 40), changes[0].WindowPositionScreenUnits);
        Assert.True(changes[1].IsFullscreen);
        Assert.False(changes[2].IsFocused);
        Assert.True(changes[2].ActivationDeferred);
        Assert.True(changes[3].IsMinimized);
        Assert.False(changes[4].IsMinimized);
        Assert.False(changes[5].IsVisible);
        Assert.True(changes[6].IsVisible);
        Assert.True(changes[7].IsFocused);
        Assert.False(changes[7].ActivationDeferred);
        Assert.True(changes[7].IsFullscreen);
        Assert.False(changes[8].IsFullscreen);
    }

    [Fact]
    public void InputOwnerSuppressesHeldButtonAcrossHandoffUntilRelease()
    {
        var owner = new DesktopInputOwner();
        owner.SetOwner(DesktopInputOwnerKind.Scene, GamepadButtons.A);
        Assert.Equal(GamepadButtons.None, owner.ConsumePressed(GamepadButtons.A));

        owner.SetOwner(DesktopInputOwnerKind.Overlay, GamepadButtons.A);
        Assert.Equal(GamepadButtons.None, owner.ConsumePressed(GamepadButtons.A));
        Assert.Equal(GamepadButtons.None, owner.ConsumePressed(GamepadButtons.None));
        Assert.Equal(GamepadButtons.A, owner.ConsumePressed(GamepadButtons.A));
    }

    [Fact]
    public void FullscreenTrackerObservesStaleOppositeAcknowledgementAndKeepsLatestIntent()
    {
        var tracker = new FullscreenTransitionTracker();

        Assert.True(tracker.NeedsRequest(true));
        tracker.Request(true);
        Assert.False(tracker.NeedsRequest(true));
        Assert.True(tracker.NeedsRequest(false));
        Assert.False(tracker.Confirmed);
        Assert.True(tracker.Requested);
        Assert.Equal(true, tracker.Pending);

        // Rapid reversal before the first acknowledgement: the stale ENTER
        // event is opposite the latest LEAVE intent, but it is still the
        // native state that must be observed.
        tracker.Request(false);
        Assert.False(tracker.Confirm(true));
        Assert.True(tracker.Confirmed);
        Assert.Equal(false, tracker.Pending);
        Assert.True(tracker.Confirm(false));
        Assert.False(tracker.Confirmed);
        Assert.Null(tracker.Pending);

        Assert.False(tracker.NeedsRequest(false));
        tracker.Request(false);
        Assert.Null(tracker.Pending);
        tracker.Request(true);
        Assert.Equal(true, tracker.Pending);
        Assert.False(tracker.NeedsRequest(true));

        Assert.True(tracker.Confirm(true));
        Assert.True(tracker.Confirmed);
        Assert.Null(tracker.Pending);
    }

    [AvaloniaFact]
    public void OverlayModeSurvivesMinimizeAndRestoresWithoutReplacingContent()
    {
        var surface = new RecordingSurface();
        GameHostPresentationState state = State(visible: true, minimized: false);
        using var coordinator = new DesktopGameOverlayCoordinator(surface, () => state);
        using var scene = new Scene();

        Assert.True(coordinator.OpenPause(scene));
        Assert.NotNull(surface.Content);
        Assert.False(surface.ZOrderOwned);
        surface.RaiseActivated();
        Assert.True(surface.ZOrderOwned);
        Control content = surface.Content!;
        state = State(visible: false, minimized: true);
        coordinator.ApplyHostPresentationForTests(state);
        Assert.Equal(DesktopOverlayMode.Pause, coordinator.Mode);
        Assert.Same(content, surface.Content);
        Assert.Equal(1, surface.HideCount);

        state = State(visible: true, minimized: false);
        coordinator.ApplyHostPresentationForTests(state);
        Assert.Same(content, surface.Content);
        Assert.Equal(2, surface.ShowCount);
        Assert.False(surface.ZOrderOwned);
        surface.RaiseActivated();
        Assert.True(surface.ZOrderOwned);

        surface.RaiseDeactivated();
        Assert.Equal(DesktopInputOwnerKind.None, coordinator.InputOwner.Current);
        Assert.False(surface.ZOrderOwned);
        coordinator.ApplyHostPresentationForTests(state);
        Assert.Equal(DesktopInputOwnerKind.None, coordinator.InputOwner.Current);
        Assert.False(surface.ZOrderOwned);
        surface.RaiseActivated();
        Assert.Equal(DesktopInputOwnerKind.Overlay, coordinator.InputOwner.Current);
        Assert.True(surface.ZOrderOwned);
    }

    [AvaloniaFact]
    public void ConfirmedOverlayRetainsOwnershipAcrossHostFocusLossUntilDeactivated()
    {
        var surface = new RecordingSurface { ActivateSynchronously = true };
        GameHostPresentationState state = State(visible: true,
            minimized: false, focused: true);
        using var coordinator = new DesktopGameOverlayCoordinator(surface,
            () => state);
        using var scene = new Scene();

        Assert.True(coordinator.OpenPause(scene));
        Assert.Equal(DesktopInputOwnerKind.Overlay, coordinator.InputOwner.Current);
        Assert.True(surface.ZOrderOwned);

        // SDL focus loss can be queued after the synchronous overlay
        // acknowledgement. It must not revoke the acknowledged owner.
        state = State(visible: true, minimized: false, focused: false);
        coordinator.ApplyHostPresentationForTests(state);
        Assert.Equal(DesktopInputOwnerKind.Overlay, coordinator.InputOwner.Current);
        Assert.True(surface.ZOrderOwned);

        surface.RaiseDeactivated();
        Assert.Equal(DesktopInputOwnerKind.None, coordinator.InputOwner.Current);
        Assert.False(surface.ZOrderOwned);
    }

    [AvaloniaFact]
    public void AltTabReturnReactivatesOpenOverlayExactlyOnce()
    {
        GameHostPresentationState state = State(visible: true,
            minimized: false, focused: true);
        var surface = new RecordingSurface();
        using var coordinator = new DesktopGameOverlayCoordinator(surface,
            () => state);
        using var scene = new Scene();

        Assert.True(coordinator.OpenPause(scene));
        Assert.Equal(1, surface.ActivationRequestCount);
        Assert.False(surface.ZOrderOwned);
        surface.RaiseActivated();
        Assert.True(surface.ZOrderOwned);

        surface.RaiseDeactivated();
        state = State(visible: true, minimized: false, focused: false);
        coordinator.ApplyHostPresentationForTests(state);
        Assert.Equal(1, surface.ActivationRequestCount);
        Assert.Equal(DesktopInputOwnerKind.None, coordinator.InputOwner.Current);

        state = State(visible: true, minimized: false, focused: true);
        coordinator.ApplyHostPresentationForTests(state);
        Assert.Equal(2, surface.ActivationRequestCount);
        Assert.False(surface.ZOrderOwned);
        coordinator.ApplyHostPresentationForTests(state);
        Assert.Equal(2, surface.ActivationRequestCount);

        surface.RaiseActivated();
        Assert.Equal(DesktopInputOwnerKind.Overlay, coordinator.InputOwner.Current);
        Assert.True(surface.ZOrderOwned);
    }

    [AvaloniaTheory]
    [InlineData((int)DesktopOverlayMode.Pause)]
    [InlineData((int)DesktopOverlayMode.Settings)]
    [InlineData((int)DesktopOverlayMode.Results)]
    [InlineData((int)DesktopOverlayMode.ContinuationLoading)]
    public void EveryLogicalOverlayModeSuspendsNativelyAndRestoresItsSameContent(
        int modeValue)
    {
        DesktopOverlayMode mode = (DesktopOverlayMode)modeValue;
        var surface = new RecordingSurface();
        using var coordinator = new DesktopGameOverlayCoordinator(surface,
            () => State(visible: true, minimized: false));
        using var scene = new Scene();
        Assert.True(coordinator.OpenPause(scene));
        Control content = Assert.IsAssignableFrom<Control>(surface.Content);

        typeof(DesktopGameOverlayCoordinator).GetField("_mode",
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(coordinator, mode);
        surface.SetContent(content, mode);

        coordinator.ApplyHostPresentationForTests(
            State(visible: false, minimized: true));
        Assert.Equal(mode, coordinator.Mode);
        Assert.Same(content, surface.Content);
        Assert.False(surface.NativeVisible);
        Assert.Equal(DesktopInputOwnerKind.None, coordinator.InputOwner.Current);

        coordinator.ApplyHostPresentationForTests(
            State(visible: true, minimized: false));
        Assert.Equal(mode, coordinator.Mode);
        Assert.Same(content, surface.Content);
        Assert.True(surface.NativeVisible);
    }

    [AvaloniaFact]
    public void HiddenInitialOverlayPreservesContentUntilHostBecomesEligible()
    {
        GameHostPresentationState state = State(visible: false,
            minimized: false, focused: false);
        var surface = new RecordingSurface();
        using var coordinator = new DesktopGameOverlayCoordinator(surface,
            () => state);
        using var scene = new Scene();

        Assert.True(coordinator.OpenPause(scene));
        Control content = Assert.IsAssignableFrom<Control>(surface.Content);
        Assert.Equal(DesktopOverlayMode.Pause, coordinator.Mode);
        Assert.False(surface.NativeVisible);
        Assert.Equal(1, surface.HideCount);
        Assert.Equal(0, surface.ActivationRequestCount);

        state = State(visible: true, minimized: false, focused: false);
        coordinator.ApplyHostPresentationForTests(state);
        Assert.Same(content, surface.Content);
        Assert.True(surface.NativeVisible);
        Assert.Equal(0, surface.ActivationRequestCount);

        state = State(visible: true, minimized: false, focused: true);
        coordinator.ApplyHostPresentationForTests(state);
        Assert.Equal(1, surface.ActivationRequestCount);
        Assert.False(surface.ZOrderOwned);
        surface.RaiseActivated();
        Assert.True(surface.ZOrderOwned);
    }

    [AvaloniaFact]
    public void FocusBeforeRestoreAndRestoreBeforeFocusEachReactivateOnce()
    {
        var surface = new RecordingSurface();
        GameHostPresentationState state = State(visible: false,
            minimized: true, focused: false);
        using var coordinator = new DesktopGameOverlayCoordinator(surface,
            () => state);
        using var scene = new Scene();
        Assert.True(coordinator.OpenPause(scene));
        Assert.Equal(0, surface.ActivationRequestCount);

        // Focus-before-restore: the host is still ineligible at focus gain;
        // only the restored eligible edge requests activation.
        state = State(visible: false, minimized: true, focused: true);
        coordinator.ApplyHostPresentationForTests(state);
        Assert.Equal(0, surface.ActivationRequestCount);
        state = State(visible: true, minimized: false, focused: true);
        coordinator.ApplyHostPresentationForTests(state);
        Assert.Equal(1, surface.ActivationRequestCount);

        surface.RaiseActivated();
        surface.RaiseDeactivated();
        // Restore-before-focus: visibility/restore alone remains ineligible;
        // the later focus edge requests exactly one more activation.
        state = State(visible: false, minimized: true, focused: false);
        coordinator.ApplyHostPresentationForTests(state);
        state = State(visible: true, minimized: false, focused: false);
        coordinator.ApplyHostPresentationForTests(state);
        Assert.Equal(1, surface.ActivationRequestCount);
        state = State(visible: true, minimized: false, focused: true);
        coordinator.ApplyHostPresentationForTests(state);
        Assert.Equal(2, surface.ActivationRequestCount);
        coordinator.ApplyHostPresentationForTests(state);
        Assert.Equal(2, surface.ActivationRequestCount);
    }

    [AvaloniaFact]
    public void DeniedOverlayActivationWaitsForFreshOpportunityAndCannotRetryAfterClose()
    {
        var surface = new RecordingSurface();
        GameHostPresentationState state = State(visible: true, minimized: false);
        using var coordinator = new DesktopGameOverlayCoordinator(surface, () => state);
        using var scene = new Scene();

        Assert.True(coordinator.OpenPause(scene));
        Assert.Equal(1, surface.ActivationRequestCount);
        Assert.Equal(DesktopInputOwnerKind.None, coordinator.InputOwner.Current);

        // Re-publishing an unchanged host snapshot is not a user opportunity.
        coordinator.ApplyHostPresentationForTests(state);
        coordinator.ApplyHostPresentationForTests(state);
        Assert.Equal(1, surface.ActivationRequestCount);

        // The seam models a confirmed native input edge while the SDL host is
        // already focused; a denied request may be retried exactly then.
        coordinator.InvokeActivationOpportunityForTests();
        Assert.Equal(2, surface.ActivationRequestCount);
        Assert.Equal(DesktopInputOwnerKind.None, coordinator.InputOwner.Current);

        coordinator.CloseFromMenu();
        coordinator.InvokeActivationOpportunityForTests();
        Assert.Equal(2, surface.ActivationRequestCount);
    }

    [AvaloniaFact]
    public void SynchronousOverlayActivationAcknowledgementIsReentrancySafe()
    {
        var surface = new RecordingSurface { ActivateSynchronously = true };
        using var coordinator = new DesktopGameOverlayCoordinator(surface,
            () => State(visible: true, minimized: false));
        using var scene = new Scene();

        Assert.True(coordinator.OpenPause(scene));
        Assert.Equal(DesktopInputOwnerKind.Overlay, coordinator.InputOwner.Current);
        Assert.Equal(1, surface.ActivationRequestCount);

        coordinator.InvokeActivationOpportunityForTests();
        Assert.Equal(1, surface.ActivationRequestCount);
    }

    [AvaloniaFact]
    public void ReentrantUserCloseIsIdempotentAndDisposesOverlaySession()
    {
        var surface = new RecordingSurface();
        using var coordinator = new DesktopGameOverlayCoordinator(surface,
            () => State(visible: true, minimized: false));
        using var scene = new Scene();
        Assert.True(coordinator.OpenPause(scene));

        surface.RaiseUserClose();
        surface.RaiseUserClose();

        Assert.Equal(DesktopOverlayMode.None, coordinator.Mode);
        Assert.Null(surface.Content);
        coordinator.CloseForTransition();
    }

    private static GameHostPresentationState State(bool visible, bool minimized,
        bool focused = true, bool activationDeferred = false)
        => new(new(1280, 720), new(2560, 1440), new(10, 20), visible,
            minimized, IsFocused: focused, ActivationDeferred: activationDeferred);

    private sealed class RecordingSurface : IDesktopGameOverlaySurface
    {
        public DesktopOverlayMode Mode { get; private set; }
        public bool NativeVisible { get; private set; }
        public Control? Content { get; private set; }
        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }
        public int ActivationRequestCount { get; private set; }
        public bool ZOrderOwned { get; private set; }
        public bool ActivateSynchronously { get; init; }
        public event EventHandler? UserCloseRequested;
        public event EventHandler? Activated;
        public event EventHandler? Deactivated;

        public void SetContent(Control? content, DesktopOverlayMode mode)
        {
            Content = content;
            Mode = mode;
        }

        public void ShowForHost(GameHostPresentationState state, bool activate)
        {
            NativeVisible = true;
            ShowCount++;
            if (activate)
            {
                ActivationRequestCount++;
                if (ActivateSynchronously) RaiseActivated();
            }
        }

        public void HideForHostPreservingContent(GameHostPresentationState state)
        {
            NativeVisible = false;
            HideCount++;
        }

        public void SetZOrderOwned(bool owned) => ZOrderOwned = owned;

        public void ReleaseContent()
        {
            Content = null;
            Mode = DesktopOverlayMode.None;
            NativeVisible = false;
            ZOrderOwned = false;
        }

        public void RaiseUserClose() => UserCloseRequested?.Invoke(this, EventArgs.Empty);
        public void RaiseActivated() => Activated?.Invoke(this, EventArgs.Empty);
        public void RaiseDeactivated() => Deactivated?.Invoke(this, EventArgs.Empty);
        public void Dispose() { }
    }
}
