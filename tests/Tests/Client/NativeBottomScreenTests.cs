using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Droid;
using MphRead.Mods;
using MphRead.Mods.Input;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class NativeBottomScreenTests
{
    [Fact]
    public void LayoutKeepsThePanelFourByThreeAndRoundTripsCoordinates()
    {
        NativeBottomScreenLayout layout = NativeBottomScreenLayout.Compute(
            new Vector2i(1920, 1080), new Vector2i(3840, 2160));

        Assert.True(layout.IsValid);
        Assert.Equal(4f / 3f,
            layout.PanelLogical.Width / layout.PanelLogical.Height, 3);
        Assert.Equal(4f / 3f,
            layout.PanelFramebuffer.Width / layout.PanelFramebuffer.Height, 3);

        Vector2 ds = layout.LogicalToDs(
            (layout.PanelLogical.Left + layout.PanelLogical.Right) * .5f,
            (layout.PanelLogical.Top + layout.PanelLogical.Bottom) * .5f);
        Assert.Equal(new Vector2(128, 96), ds, new Vector2Comparer(.01f));

        Vector2 framebuffer = layout.DsToFramebuffer(64, 48);
        Assert.Equal(layout.PanelFramebuffer.Left + layout.PanelFramebuffer.Width * .25f,
            framebuffer.X, 3);
        Assert.Equal(layout.PanelFramebuffer.Top + layout.PanelFramebuffer.Height * .25f,
            framebuffer.Y, 3);

        NativeBottomScreenLayout narrow = NativeBottomScreenLayout.Compute(
            new Vector2i(800, 1280), new Vector2i(800, 1280));
        Assert.Equal(4f / 3f,
            narrow.PanelLogical.Width / narrow.PanelLogical.Height, 3);
        Assert.True(narrow.PanelLogical.Width < 800);
    }

    [Fact]
    public void NativeSelectorUsesTheSixAffinitySectorsAndAvailabilityMask()
    {
        int available = 0;
        for (int slot = 0; slot < NativeWeaponSelector.SlotCount; slot++)
            available |= 1 << NativeWeaponSelector.WeaponAtSlot(slot);

        float[] divisions = { .2f, .4f, .8f, 1.2f, 2.5f, 4.5f };
        for (int slot = 0; slot < divisions.Length; slot++)
        {
            Vector2 point = SelectorPoint(divisions[slot],
                slot >= 5 ? 30 : 100);
            Assert.True(NativeWeaponSelector.TrySelect(point, available,
                out byte weapon));
            Assert.Equal(NativeWeaponSelector.WeaponAtSlot(slot), weapon);
        }

        Assert.False(NativeWeaponSelector.TrySelect(SelectorPoint(.4f, 100),
            available & ~(1 << NativeWeaponSelector.WeaponAtSlot(1)), out _));
        Assert.False(NativeWeaponSelector.TrySelect(new Vector2(220, 57),
            available, out _));
        Assert.False(NativeWeaponSelector.TrySelect(
            new Vector2(float.NaN, 10), available, out _));
    }

    [Fact]
    public void StaleGenerationsAndQueueOverflowCannotCommitOldInput()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1280, 720), new Vector2i(2560, 1440),
            NativeBottomScreenMode.AlwaysVisible);
        long staleGeneration = controller.BeginPresentation();
        NativeBottomScreenLayout layout = controller.Layout;
        PointerSample sample = PanelSample(layout, 7, 64, 48);
        Assert.True(controller.TryPointerDown(sample));
        Assert.True(controller.TryPointerUp(sample));

        long currentGeneration = controller.BeginPresentation();
        Assert.NotEqual(staleGeneration, currentGeneration);
        Assert.Empty(controller.Consume(staleGeneration));

        Assert.True(controller.TryPointerDown(sample));
        for (int i = 0; i < 160; i++)
        {
            PointerSample move = sample with { X = sample.X + i % 4 };
            controller.TryPointerMove(move);
        }
        IReadOnlyList<NativeBottomScreenPointerEvent> overflow
            = controller.Consume(currentGeneration);
        Assert.Contains(overflow, item =>
            item.Phase == NativeBottomScreenPointerPhase.Cancel);
        Assert.False(controller.TryPointerUp(sample));
        Assert.DoesNotContain(overflow, item =>
            item.Phase == NativeBottomScreenPointerPhase.Up);
    }

    [Fact]
    public void PopupDismissesAfterASelectionWithoutAKeyboard()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1280, 720), new Vector2i(1280, 720),
            NativeBottomScreenMode.Popup);
        controller.BeginPresentation();
        NativeBottomScreenLayout layout = controller.Layout;
        PointerSample tab = new(9, PointerToolKind.Finger,
            (layout.TabLogical.Left + layout.TabLogical.Right) / 2,
            (layout.TabLogical.Top + layout.TabLogical.Bottom) / 2,
            0, StylusButtons.None, 1);
        Assert.True(controller.TryPointerDown(tab));
        Assert.True(controller.TryPointerUp(tab));
        Assert.True(controller.PopupOpen);

        controller.DismissPopupAfterSelection();

        Assert.False(controller.PopupOpen);
    }

    [Fact]
    public void AndroidRouterGivesThePanelExclusivePointerOwnership()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1000, 500), new Vector2i(1000, 500),
            NativeBottomScreenMode.AlwaysVisible);
        long generation = controller.BeginPresentation();
        NativeBottomScreenPlatformRegistration registration
            = NativeBottomScreenPlatformBridge.Register(controller);
        try
        {
            var touch = new TouchControls();
            touch.Layout(1000, 500, 1);
            var stylus = new StylusInput(new LookInputCoordinator());
            stylus.Configure(true, 1, false, false, .35f, 1);
            var router = new PointerInputRouter(touch, stylus);
            PointerSample sample = PanelSample(controller.Layout, 11, 122, 142);

            router.PointerDown(sample);
            router.PointerMove(sample with { X = sample.X + 1 });
            router.PointerUp(sample);

            Assert.False(stylus.Active);
            Assert.Equal(TouchControls.Dir.None, touch.Direction);
            Assert.Equal(new[]
            {
                NativeBottomScreenPointerPhase.Down,
                NativeBottomScreenPointerPhase.Move,
                NativeBottomScreenPointerPhase.Up
            }, controller.Consume(generation).Select(item => item.Phase));
        }
        finally
        {
            NativeBottomScreenPlatformBridge.Unregister(registration);
        }
    }

    [Fact]
    public void ReplacedSceneRegistrationRejectsOldPlatformEvents()
    {
        var oldController = new NativeBottomScreenController();
        oldController.Configure(new Vector2i(800, 600), new Vector2i(800, 600),
            NativeBottomScreenMode.AlwaysVisible);
        oldController.BeginPresentation();
        NativeBottomScreenPlatformRegistration oldRegistration
            = NativeBottomScreenPlatformBridge.Register(oldController);
        var currentController = new NativeBottomScreenController();
        currentController.Configure(new Vector2i(800, 600), new Vector2i(800, 600),
            NativeBottomScreenMode.AlwaysVisible);
        currentController.BeginPresentation();
        NativeBottomScreenPlatformRegistration currentRegistration
            = NativeBottomScreenPlatformBridge.Register(currentController);
        try
        {
            PointerSample sample = PanelSample(currentController.Layout, 12, 90, 109);
            Assert.False(NativeBottomScreenPlatformBridge.TryPointerDown(
                oldRegistration, sample));
            Assert.True(NativeBottomScreenPlatformBridge.TryPointerDown(
                currentRegistration, sample));
        }
        finally
        {
            NativeBottomScreenPlatformBridge.Unregister(currentRegistration);
        }
    }

    [Fact]
    public void PlatformBridgeConfiguresANewlyRegisteredSceneBeforeAnyResize()
    {
        var controller = new NativeBottomScreenController();
        long generation = controller.BeginPresentation();
        NativeBottomScreenPlatformRegistration registration
            = NativeBottomScreenPlatformBridge.Register(controller);
        try
        {
            NativeBottomScreenPlatformBridge.Configure(
                new Vector2i(1280, 720), new Vector2i(2560, 1440),
                NativeBottomScreenMode.Popup);

            Assert.Equal(generation, controller.Generation);
            Assert.Equal(NativeBottomScreenMode.Popup, controller.Mode);
            Assert.True(controller.Layout.IsValid);
        }
        finally
        {
            NativeBottomScreenPlatformBridge.Unregister(registration);
        }
    }

    [Fact]
    public void LegacySelectorRatiosPreserveWidescreenSectorGeometry()
    {
        int available = Enumerable.Range(0, NativeWeaponSelector.SlotCount)
            .Aggregate(0, (mask, slot) => mask
                | 1 << NativeWeaponSelector.WeaponAtSlot(slot));
        Vector2 canonical = SelectorPoint(.4f, 100);

        Assert.True(NativeWeaponSelector.TrySelect(canonical, 1, 1,
            available, out byte fourByThree));
        Assert.True(NativeWeaponSelector.TrySelect(
            new Vector2(canonical.X * 2, canonical.Y), 2, 1,
            available, out byte widescreen));
        Assert.Equal(fourByThree, widescreen);
    }

    [Fact]
    public void UpBeforeConsumeIsCancelledAndPopupTabCapturesOnlyWhenEnabled()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1280, 720), new Vector2i(2560, 1440),
            NativeBottomScreenMode.Off);
        long disabledGeneration = controller.BeginPresentation();
        Assert.False(controller.TogglePopup());
        Assert.False(controller.TryPointerDown(new PointerSample(1,
            PointerToolKind.Finger, 640, 360, 1, StylusButtons.None, 1)));
        Assert.Empty(controller.Consume(disabledGeneration));

        controller.Configure(new Vector2i(1280, 720), new Vector2i(2560, 1440),
            NativeBottomScreenMode.Popup);
        long generation = controller.BeginPresentation();
        NativeBottomScreenLayout layout = controller.Layout;
        Vector2 tab = new(
            (layout.TabLogical.Left + layout.TabLogical.Right) * .5f,
            (layout.TabLogical.Top + layout.TabLogical.Bottom) * .5f);
        PointerSample tabSample = new(2, PointerToolKind.Stylus, tab.X, tab.Y,
            1, StylusButtons.None, 2);
        Assert.True(controller.TryPointerDown(tabSample));
        Assert.True(controller.PopupOpen);
        Assert.True(controller.TryPointerUp(tabSample));

        PointerSample panelSample = PanelSample(layout, 3, 64, 48);
        Assert.True(controller.TryPointerDown(panelSample));
        Assert.True(controller.TryPointerUp(panelSample));
        controller.Cancel();

        var events = controller.Consume(generation);
        Assert.Equal(new[]
        {
            NativeBottomScreenPointerPhase.Down,
            NativeBottomScreenPointerPhase.Up,
            NativeBottomScreenPointerPhase.Cancel
        }, events.Select(item => item.Phase));
    }

    [Fact]
    public void BottomScreenSettingRoundTripsWithoutChangingCustomHudOverlayBinding()
    {
        InputSettings.Snapshot prior = InputSettings.CaptureSnapshot();
        try
        {
            InputSettings.LoadLines(new[]
            {
                "HudOverlay=Key:F12",
                "bottom_screen_mode=AlwaysVisible"
            });

            Assert.Equal(PrimeKey.F12, InputSettings.Current.HudOverlay.Key);
            Assert.Equal(NativeBottomScreenMode.AlwaysVisible,
                InputSettings.BottomScreenMode);
            Assert.Contains("bottom_screen_mode=AlwaysVisible",
                InputSettings.GetSaveLines());

            InputSettings.LoadLines(new[] { "bottom_screen_mode=not-a-mode" });
            Assert.Equal(NativeBottomScreenMode.AlwaysVisible,
                InputSettings.BottomScreenMode);
            Assert.Equal(PrimeKey.F12, InputSettings.Current.HudOverlay.Key);
        }
        finally
        {
            prior.Restore();
        }
    }

    private static Vector2 SelectorPoint(float division, float distY)
        => new(224 - division * distY, 38 + distY);

    private static PointerSample PanelSample(NativeBottomScreenLayout layout,
        int id, float dsX, float dsY)
        => new(id, PointerToolKind.Finger,
            layout.PanelLogical.Left + dsX / 256f * layout.PanelLogical.Width,
            layout.PanelLogical.Top + dsY / 192f * layout.PanelLogical.Height,
            1, StylusButtons.None, 1);

    private sealed class Vector2Comparer(float tolerance) : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 x, Vector2 y)
            => MathF.Abs(x.X - y.X) <= tolerance
                && MathF.Abs(x.Y - y.Y) <= tolerance;

        public int GetHashCode(Vector2 obj) => 0;
    }
}
