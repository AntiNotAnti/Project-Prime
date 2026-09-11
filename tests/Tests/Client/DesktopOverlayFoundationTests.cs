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

    [AvaloniaFact]
    public void OverlayModeSurvivesMinimizeAndRestoresWithoutReplacingContent()
    {
        var surface = new RecordingSurface();
        GameHostPresentationState state = State(visible: true, minimized: false);
        using var coordinator = new DesktopGameOverlayCoordinator(surface, () => state);
        using var scene = new Scene();

        Assert.True(coordinator.OpenPause(scene));
        Assert.NotNull(surface.Content);
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
        Assert.Equal(DesktopInputOwnerKind.Overlay, coordinator.InputOwner.Current);

        coordinator.ApplyHostPresentationForTests(
            State(visible: true, minimized: false));
        Assert.Equal(mode, coordinator.Mode);
        Assert.Same(content, surface.Content);
        Assert.True(surface.NativeVisible);
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

    private static GameHostPresentationState State(bool visible, bool minimized)
        => new(new(1280, 720), new(2560, 1440), new(10, 20), visible,
            minimized, IsFocused: true, ActivationDeferred: false);

    private sealed class RecordingSurface : IDesktopGameOverlaySurface
    {
        public DesktopOverlayMode Mode { get; private set; }
        public bool NativeVisible { get; private set; }
        public Control? Content { get; private set; }
        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }
        public bool ZOrderOwned { get; private set; }
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
            if (activate) ZOrderOwned = true;
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
