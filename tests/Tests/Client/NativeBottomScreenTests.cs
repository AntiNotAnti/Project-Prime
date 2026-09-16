using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Droid;
using MphRead.Entities;
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
    public void LayoutOptionsResizeAndPlaceThePanelWithoutStretchingIt()
    {
        NativeBottomScreenLayout full = NativeBottomScreenLayout.Compute(
            new Vector2i(1600, 900), new Vector2i(3200, 1800));
        NativeBottomScreenLayout placed = NativeBottomScreenLayout.Compute(
            new Vector2i(1600, 900), new Vector2i(3200, 1800),
            new NativeBottomScreenLayoutOptions(.5f, .1f, .2f));

        Assert.Equal(full.PanelLogical.Width * .5f,
            placed.PanelLogical.Width, 3);
        Assert.Equal(4f / 3f,
            placed.PanelLogical.Width / placed.PanelLogical.Height, 3);
        Assert.True(placed.PanelLogical.Left < full.PanelLogical.Left);
        Assert.True(placed.PanelLogical.Top < full.PanelLogical.Top);
        Assert.Equal(new Vector2(128, 96), placed.LogicalToDs(
            (placed.PanelLogical.Left + placed.PanelLogical.Right) * .5f,
            (placed.PanelLogical.Top + placed.PanelLogical.Bottom) * .5f),
            new Vector2Comparer(.01f));
    }

    [Fact]
    public void ClassicLayoutUsesOriginalButtonsAndLeavesTheMiddleForAim()
    {
        Assert.Equal(NativeBottomScreenRegion.PowerBeam,
            NativeBottomScreenClassicLayout.RegionAt(new Vector2(26, 26)));
        Assert.Equal(NativeBottomScreenRegion.Missile,
            NativeBottomScreenClassicLayout.RegionAt(new Vector2(80, 24)));
        Assert.Equal(NativeBottomScreenRegion.NextWeapon,
            NativeBottomScreenClassicLayout.RegionAt(new Vector2(150, 28)));
        Assert.Equal(NativeBottomScreenRegion.WeaponSelect,
            NativeBottomScreenClassicLayout.RegionAt(new Vector2(222, 28)));
        Assert.Equal(NativeBottomScreenRegion.AltForm,
            NativeBottomScreenClassicLayout.RegionAt(new Vector2(228, 166)));
        Assert.Equal(NativeBottomScreenRegion.Aim,
            NativeBottomScreenClassicLayout.RegionAt(new Vector2(128, 108)));
        Assert.Equal(NativeBottomScreenRegion.None,
            NativeBottomScreenClassicLayout.RegionAt(new Vector2(-1, 10)));
    }

    [Fact]
    public void ClassicLayoutSanitizesSafeCentersAndResolvesOverlapsInDeclarationOrder()
    {
        NativeBottomScreenClassicLayoutOptions options = new(
            .5f, .5f,
            .5f, .5f,
            .5f, .5f,
            .5f, .5f,
            float.NaN, float.PositiveInfinity);
        NativeBottomScreenClassicLayoutSnapshot layout = options.CreateLayout();

        foreach (NativeBottomScreenButton button in layout.Buttons)
        {
            Assert.InRange(button.Position.X, button.Radius,
                NativeBottomScreenClassicLayoutSnapshot.DsWidth - button.Radius);
            Assert.InRange(button.Position.Y, button.Radius,
                NativeBottomScreenClassicLayoutSnapshot.DsHeight - button.Radius);
        }

        // All five centers overlap at the same point. Strict nearest-distance
        // comparison makes the declared Power Beam button win deterministically.
        Assert.Equal(NativeBottomScreenRegion.PowerBeam,
            layout.RegionAt(new Vector2(128, 96)));
        Assert.Equal(NativeBottomScreenClassicLayoutOptions.Default.AltFormX,
            layout.Options.AltFormX);
        Assert.Equal(NativeBottomScreenClassicLayoutOptions.Default.AltFormY,
            layout.Options.AltFormY);
    }

    [Fact]
    public void ClassicDirectionalSwipeAssistHonorsDeadzoneAndAngle()
    {
        NativeBottomScreenClassicLayoutSnapshot layout
            = NativeBottomScreenClassicLayoutOptions.Default.CreateLayout();
        Vector2 start = new(128, 96);
        Vector2 nextWeapon = layout.GetButton(
            NativeBottomScreenRegion.NextWeapon).Position;
        Vector2 direction = (nextWeapon - start).Normalized();

        Assert.Equal(NativeBottomScreenRegion.Aim,
            layout.ResolveRegion(start + direction * (NativeBottomScreenSnapshotDeadzone - .5f),
                start, directionalSwipeAssist: true));
        Assert.Equal(NativeBottomScreenRegion.NextWeapon,
            layout.ResolveRegion(start + direction * (NativeBottomScreenSnapshotDeadzone + 1),
                start, directionalSwipeAssist: true));
        Assert.Equal(NativeBottomScreenRegion.Aim,
            layout.ResolveRegion(start + new Vector2(0, 40), start,
                directionalSwipeAssist: true));

        // A direct hit always wins even when the contact began elsewhere.
        Assert.Equal(NativeBottomScreenRegion.PowerBeam,
            layout.ResolveRegion(layout.GetButton(
                NativeBottomScreenRegion.PowerBeam).Position, start,
                directionalSwipeAssist: true));
    }

    [Fact]
    public void AffinityLayoutPreservesNativeIconCentersAndFreeActionCluster()
    {
        NativeBottomScreenAffinityLayoutSnapshot layout
            = NativeBottomScreenAffinityLayoutOptions.Default.CreateLayout();
        NativeBottomScreenRegion[] affinityRegions =
        {
            NativeBottomScreenRegion.VoltDriver,
            NativeBottomScreenRegion.Battlehammer,
            NativeBottomScreenRegion.Imperialist,
            NativeBottomScreenRegion.Judicator,
            NativeBottomScreenRegion.Magmaul,
            NativeBottomScreenRegion.ShockCoil
        };

        Assert.Equal(9, layout.Buttons.Count);
        for (int i = 0; i < affinityRegions.Length; i++)
        {
            Assert.Equal(NativeWeaponSelector.SlotPosition(i),
                layout.GetButton(affinityRegions[i]).Position);
        }
        foreach (NativeBottomScreenButton button in layout.Buttons)
        {
            Assert.InRange(button.Position.X, button.Radius,
                NativeBottomScreenAffinityLayoutSnapshot.DsWidth - button.Radius);
            Assert.InRange(button.Position.Y, button.Radius,
                NativeBottomScreenAffinityLayoutSnapshot.DsHeight - button.Radius);
        }
        for (int first = 0; first < layout.Buttons.Count; first++)
        {
            for (int second = first + 1; second < layout.Buttons.Count; second++)
            {
                NativeBottomScreenButton a = layout.Buttons[first];
                NativeBottomScreenButton b = layout.Buttons[second];
                Assert.True((a.Position - b.Position).LengthSquared
                    > (a.Radius + b.Radius) * (a.Radius + b.Radius));
            }
        }

        Assert.Equal(NativeBottomScreenRegion.PowerBeam,
            layout.RegionAt(new Vector2(230, 66)));
        Assert.Equal(NativeBottomScreenRegion.Missile,
            layout.RegionAt(new Vector2(230, 106)));
        Assert.Equal(NativeBottomScreenRegion.AltForm,
            layout.RegionAt(new Vector2(190, 106)));
    }

    [Fact]
    public void AffinityLayoutSanitizesAndResolvesOverlapsInNativeOrder()
    {
        NativeBottomScreenAffinityLayoutOptions options = new(
            .5f, .5f, .5f, .5f, .5f, .5f, .5f, .5f, .5f, .5f,
            .5f, .5f, float.NaN, float.PositiveInfinity,
            .5f, .5f, .5f, .5f);
        NativeBottomScreenAffinityLayoutSnapshot layout = options.CreateLayout();

        Assert.Equal(NativeBottomScreenAffinityLayoutOptions.Default.PowerBeamX,
            layout.Options.PowerBeamX);
        Assert.Equal(NativeBottomScreenAffinityLayoutOptions.Default.PowerBeamY,
            layout.Options.PowerBeamY);
        Assert.Equal(NativeBottomScreenRegion.VoltDriver,
            layout.RegionAt(new Vector2(128, 96)));
    }

    [Fact]
    public void AffinityDirectionalAssistUsesAllTargetsAndExactHitsWin()
    {
        NativeBottomScreenAffinityLayoutOptions options
            = NativeBottomScreenAffinityLayoutOptions.Default with
            {
                VoltDriverX = .2f, VoltDriverY = .8f,
                BattlehammerX = .2f, BattlehammerY = .8f,
                ImperialistX = .2f, ImperialistY = .8f,
                JudicatorX = .2f, JudicatorY = .8f,
                MagmaulX = .2f, MagmaulY = .8f,
                ShockCoilX = .2f, ShockCoilY = .8f,
                PowerBeamX = .8f, PowerBeamY = .8f,
                MissileX = .8f, MissileY = .8f,
                AltFormX = .5f, AltFormY = .2f
            };
        NativeBottomScreenAffinityLayoutSnapshot layout = options.CreateLayout();
        Vector2 start = new(128, 96);

        Assert.Equal(NativeBottomScreenRegion.Aim,
            layout.ResolveRegion(start + new Vector2(0, -23), start, true));
        Assert.Equal(NativeBottomScreenRegion.AltForm,
            layout.ResolveRegion(start + new Vector2(0, -25), start, true));
        Assert.Equal(NativeBottomScreenRegion.Aim,
            layout.ResolveRegion(start + new Vector2(40, 0), start, true));
        Assert.Equal(NativeBottomScreenRegion.AltForm,
            layout.ResolveRegion(layout.GetButton(NativeBottomScreenRegion.AltForm).Position,
                start, true));
    }

    [Fact]
    public void AffinityWeaponMappingKeepsAltFormAsAnAction()
    {
        Assert.Equal((byte)BeamType.VoltDriver,
            PlayerPresentation.AffinityWeaponId(NativeBottomScreenRegion.VoltDriver));
        Assert.Equal((byte)BeamType.ShockCoil,
            PlayerPresentation.AffinityWeaponId(NativeBottomScreenRegion.ShockCoil));
        Assert.Equal((byte)BeamType.PowerBeam,
            PlayerPresentation.AffinityWeaponId(NativeBottomScreenRegion.PowerBeam));
        Assert.Equal((byte)BeamType.Missile,
            PlayerPresentation.AffinityWeaponId(NativeBottomScreenRegion.Missile));
        Assert.Equal(WeaponSelectionIntent.None,
            PlayerPresentation.AffinityWeaponId(NativeBottomScreenRegion.AltForm));
        Assert.Equal(BottomScreenAction.Morph,
            PlayerPresentation.AffinityAction(NativeBottomScreenRegion.AltForm));
        Assert.Equal(BottomScreenAction.None,
            PlayerPresentation.AffinityAction(NativeBottomScreenRegion.PowerBeam));
    }

    [Fact]
    public void ClassicQuickButtonsUseTheirBeamEnumValues()
    {
        Assert.Equal((byte)BeamType.PowerBeam,
            PlayerPresentation.ClassicBeamId(NativeBottomScreenRegion.PowerBeam));
        Assert.Equal((byte)BeamType.Missile,
            PlayerPresentation.ClassicBeamId(NativeBottomScreenRegion.Missile));
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
    public void PopupHasNoHiddenTouchTabAndDismissesAfterASelection()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1280, 720), new Vector2i(1280, 720),
            NativeBottomScreenMode.Popup);
        controller.BeginPresentation();
        NativeBottomScreenLayout layout = controller.Layout;
        PointerSample formerTab = new(9, PointerToolKind.Finger,
            layout.LogicalSize.X / 2f, MathF.Max(0, layout.PanelLogical.Top - 10),
            0, StylusButtons.None, 1);
        Assert.False(controller.TryPointerDown(formerTab));
        Assert.False(controller.TryPointerUp(formerTab));
        Assert.False(controller.PopupOpen);

        Assert.True(controller.TogglePopup());
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
    public void UpBeforeConsumeIsCancelledAndPopupRequiresExplicitActivation()
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
        PointerSample formerTab = new(2, PointerToolKind.Stylus,
            layout.LogicalSize.X / 2f, MathF.Max(0, layout.PanelLogical.Top - 10),
            1, StylusButtons.None, 2);
        Assert.False(controller.TryPointerDown(formerTab));
        Assert.False(controller.TryPointerUp(formerTab));
        Assert.False(controller.PopupOpen);
        Assert.True(controller.TogglePopup());
        Assert.True(controller.PopupOpen);

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
    public void ClassicScreenCapturesButtonsButLetsItsAimSurfaceUseStylusLook()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1280, 720), new Vector2i(1280, 720),
            NativeBottomScreenMode.AlwaysVisible);
        long generation = controller.BeginPresentation();
        controller.UpdatePreferences(NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenStyle.ClassicDs,
            NativeBottomScreenLayoutOptions.Default);
        NativeBottomScreenLayout layout = controller.Layout;

        Assert.False(controller.TryPointerDown(PanelSample(layout, 1, 128, 108)));
        PointerSample beam = PanelSample(layout, 2, 26, 26);
        Assert.True(controller.TryPointerDown(beam));
        Assert.True(controller.TryPointerUp(beam));
        Assert.Equal(new[]
        {
            NativeBottomScreenPointerPhase.Down,
            NativeBottomScreenPointerPhase.Up
        }, controller.Consume(generation).Select(item => item.Phase));

        controller.SetSelectorOpen(true);
        PointerSample selector = PanelSample(layout, 3, 128, 108);
        Assert.True(controller.TryPointerDown(selector));
        Assert.True(controller.TryPointerUp(selector));
        controller.DismissPopupAfterSelection();
        Assert.False(controller.SelectorOpen);
        Assert.True(controller.Visible);
    }

    [Fact]
    public void TrueDsAimRouteUsesCanonicalStylusSamplesWithoutQueueTraffic()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1280, 720), new Vector2i(1280, 720),
            NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenAimMode.TrueDs);
        long generation = controller.BeginPresentation();
        controller.UpdatePreferences(NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenStyle.ClassicDs,
            NativeBottomScreenLayoutOptions.Default,
            aimMode: NativeBottomScreenAimMode.TrueDs);
        NativeBottomScreenLayout layout = controller.Layout;
        PointerSample down = PanelSample(layout, 41, 128, 96);
        Assert.Equal(NativeBottomScreenPointerRoute.Aim,
            controller.RoutePointerDown(down));
        Assert.Empty(controller.Consume(generation));

        var coordinator = new LookInputCoordinator();
        var stylus = new StylusInput(coordinator);
        stylus.Configure(true, 1, false, true, .35f, 1);
        PointerSample canonicalDown = controller.MapAimSample(down);
        Assert.Equal(new Vector2(128, 96),
            new Vector2(canonicalDown.X, canonicalDown.Y),
            new Vector2Comparer(.01f));
        Assert.Equal(0, canonicalDown.Pressure);
        Assert.Equal(StylusButtons.None, canonicalDown.Buttons);
        Assert.True(stylus.PointerDown(canonicalDown));
        Assert.Equal(Vector2.Zero,
            coordinator.ConsumeForSimulation(1f / 60f).DeltaDegrees);

        PointerSample move = down with
        {
            X = down.X + layout.PanelLogical.Width * 8 / 256f,
            Y = down.Y + layout.PanelLogical.Height * 4 / 192f
        };
        Assert.True(controller.TryPointerMove(move));
        Assert.True(stylus.PointerMove(controller.MapAimSample(move)));
        LocalLookFrame moved = coordinator.ConsumeForSimulation(1f / 60f);
        Assert.Equal(new Vector2(-2, -1), moved.DeltaDegrees,
            new Vector2Comparer(.01f));
        Assert.Empty(controller.Consume(generation));

        Assert.True(controller.TryPointerUp(move));
        Assert.True(stylus.PointerUp(controller.MapAimSample(move)));
        Assert.Equal(Vector2.Zero,
            coordinator.ConsumeForSimulation(1f / 60f).DeltaDegrees);
    }

    [Fact]
    public void TrueDsDuplicateDownRetainsTheOriginalAimRoute()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1280, 720), new Vector2i(1280, 720),
            NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenAimMode.TrueDs);
        controller.BeginPresentation();
        controller.UpdatePreferences(NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenStyle.ClassicDs,
            NativeBottomScreenLayoutOptions.Default,
            aimMode: NativeBottomScreenAimMode.TrueDs);
        PointerSample sample = PanelSample(controller.Layout, 42, 128, 96);

        Assert.Equal(NativeBottomScreenPointerRoute.Aim,
            controller.RoutePointerDown(sample));
        Assert.Equal(NativeBottomScreenPointerRoute.Aim,
            controller.RoutePointerDown(sample));
        Assert.True(controller.TryPointerUp(sample));
    }

    [Fact]
    public void TrueDsAimContactSnapshotStartsAtDownAndClampsOwnerMoves()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1280, 720), new Vector2i(1280, 720),
            NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenAimMode.TrueDs);
        controller.BeginPresentation();
        controller.UpdatePreferences(NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenStyle.ClassicDs,
            NativeBottomScreenLayoutOptions.Default,
            aimMode: NativeBottomScreenAimMode.TrueDs);
        NativeBottomScreenLayout layout = controller.Layout;
        PointerSample down = PanelSample(layout, 81, 130, 100);

        Assert.Equal(NativeBottomScreenPointerRoute.Aim,
            controller.RoutePointerDown(down));
        NativeBottomScreenAimContactSnapshot initial = controller.AimContact;
        Assert.True(initial.Active);
        Assert.Equal(new Vector2(130, 100), initial.PositionDs,
            new Vector2Comparer(.01f));

        PointerSample outside = down with
        {
            X = layout.PanelLogical.Right + layout.PanelLogical.Width,
            Y = layout.PanelLogical.Top - layout.PanelLogical.Height,
            Tool = PointerToolKind.Unknown
        };
        Assert.True(controller.TryPointerMove(outside));
        NativeBottomScreenAimContactSnapshot moved = controller.AimContact;
        Assert.True(moved.Active);
        Assert.Equal(new Vector2(256, 0), moved.PositionDs,
            new Vector2Comparer(.01f));
        Assert.True(controller.TryPointerUp(outside));
        Assert.False(controller.AimContact.Active);
    }

    [Fact]
    public void TrueDsAimContactSnapshotCancelsAcrossReconfigureAndPresentationReset()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1280, 720), new Vector2i(1280, 720),
            NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenAimMode.TrueDs);
        controller.BeginPresentation();
        controller.UpdatePreferences(NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenStyle.ClassicDs,
            NativeBottomScreenLayoutOptions.Default,
            aimMode: NativeBottomScreenAimMode.TrueDs);
        PointerSample sample = PanelSample(controller.Layout, 82, 128, 96);

        Assert.Equal(NativeBottomScreenPointerRoute.Aim,
            controller.RoutePointerDown(sample));
        Assert.True(controller.AimContact.Active);
        Assert.True(controller.CancelPointer(sample.Id));
        Assert.False(controller.AimContact.Active);

        Assert.Equal(NativeBottomScreenPointerRoute.Aim,
            controller.RoutePointerDown(sample with { Timestamp = 2 }));
        Assert.True(controller.AimContact.Active);
        controller.Configure(new Vector2i(1000, 700), new Vector2i(1000, 700),
            NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenAimMode.TrueDs);
        Assert.False(controller.AimContact.Active);

        controller.UpdatePreferences(NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenStyle.ClassicDs,
            NativeBottomScreenLayoutOptions.Default,
            aimMode: NativeBottomScreenAimMode.TrueDs);
        PointerSample recontact = PanelSample(controller.Layout, 83, 128, 96);
        Assert.Equal(NativeBottomScreenPointerRoute.Aim,
            controller.RoutePointerDown(recontact));
        Assert.True(controller.AimContact.Active);
        controller.BeginPresentation();
        Assert.False(controller.AimContact.Active);
    }

    [Fact]
    public void TrueDsAimContactSnapshotExcludesMouseControlSuppressedAndDesktop()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1280, 720), new Vector2i(1280, 720),
            NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenAimMode.TrueDs);
        controller.BeginPresentation();
        controller.UpdatePreferences(NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenStyle.ClassicDs,
            NativeBottomScreenLayoutOptions.Default,
            aimMode: NativeBottomScreenAimMode.TrueDs);
        NativeBottomScreenLayout layout = controller.Layout;

        PointerSample mouseAim = PanelSample(layout, 84, 128, 96) with
        {
            Tool = PointerToolKind.Mouse
        };
        Assert.Equal(NativeBottomScreenPointerRoute.Aim,
            controller.RoutePointerDown(mouseAim));
        Assert.False(controller.AimContact.Active);
        Assert.True(controller.TryPointerMove(mouseAim with
        {
            Tool = PointerToolKind.Stylus,
            X = mouseAim.X + layout.PanelLogical.Width / 4f
        }));
        Assert.False(controller.AimContact.Active);
        Assert.True(controller.TryPointerUp(mouseAim));

        PointerSample control = PanelSample(layout, 85, 26, 26);
        Assert.Equal(NativeBottomScreenPointerRoute.Control,
            controller.RoutePointerDown(control));
        PointerSample suppressedAim = PanelSample(layout, 86, 128, 96);
        Assert.Equal(NativeBottomScreenPointerRoute.Suppressed,
            controller.RoutePointerDown(suppressedAim));
        Assert.False(controller.AimContact.Active);
        Assert.True(controller.TryPointerUp(control));

        controller.BeginPresentation();
        Assert.True(controller.BeginDesktopSession(
            NativeBottomScreenActivationMode.Hold));
        Assert.False(controller.AimContact.Active);
        controller.EndDesktopSession();
    }

    [Fact]
    public void AndroidTrueDsSuppressesOutsideFingerAimButKeepsPanelAim()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1000, 500), new Vector2i(1000, 500),
            NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenAimMode.TrueDs);
        controller.BeginPresentation();
        controller.UpdatePreferences(NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenStyle.ClassicDs,
            NativeBottomScreenLayoutOptions.Default,
            aimMode: NativeBottomScreenAimMode.TrueDs);
        NativeBottomScreenPlatformRegistration registration
            = NativeBottomScreenPlatformBridge.Register(controller);
        try
        {
            var touch = new TouchControls();
            touch.Layout(1000, 500, 1);
            var stylus = new StylusInput(new LookInputCoordinator());
            stylus.Configure(true, 1, false, false, .35f, 1);
            var router = new PointerInputRouter(touch, stylus);
            PointerSample outside = new(71, PointerToolKind.Finger,
                500, 10, 1, StylusButtons.None, 1);

            router.PointerDown(outside);
            router.PointerMove(outside with { X = 600, Timestamp = 2 });
            Assert.Equal((0f, 0f), touch.TakeAimDelta());
            Assert.False(stylus.Active);
            router.PointerUp(outside with { X = 600, Timestamp = 3 });

            TouchButton fire = touch.Buttons.Single(button =>
                button.Action == TouchAction.Shoot);
            PointerSample fireFinger = new(73, PointerToolKind.Finger,
                fire.CentreX, fire.CentreY, 1, StylusButtons.None, 4);
            router.PointerDown(fireFinger);
            Assert.True(touch.IsHeld(TouchAction.Shoot));
            router.PointerMove(fireFinger with
            {
                X = fireFinger.X + 50,
                Timestamp = 5
            });
            Assert.Equal((0f, 0f), touch.TakeAimDelta());
            router.PointerUp(fireFinger with { Timestamp = 6 });
            Assert.False(touch.IsHeld(TouchAction.Shoot));

            PointerSample panel = PanelSample(controller.Layout, 72, 128, 96)
                with { Tool = PointerToolKind.Finger };
            router.PointerDown(panel);
            Assert.True(stylus.Active);
            controller.Cancel();
            router.ReconcileBottomScreenEpoch();
            Assert.False(stylus.Active);
        }
        finally
        {
            NativeBottomScreenPlatformBridge.Unregister(registration);
        }
    }

    [Fact]
    public void TrueDsRouteIsImmutableAndOnlyTheFirstPanelPointerWins()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1280, 720), new Vector2i(1280, 720),
            NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenAimMode.TrueDs);
        long generation = controller.BeginPresentation();
        controller.UpdatePreferences(NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenStyle.ClassicDs,
            NativeBottomScreenLayoutOptions.Default,
            aimMode: NativeBottomScreenAimMode.TrueDs);
        NativeBottomScreenLayout layout = controller.Layout;

        PointerSample aim = PanelSample(layout, 51, 128, 96);
        PointerSample control = PanelSample(layout, 52, 26, 26);
        Assert.Equal(NativeBottomScreenPointerRoute.Aim,
            controller.RoutePointerDown(aim));
        Assert.Equal(NativeBottomScreenPointerRoute.Suppressed,
            controller.RoutePointerDown(control));
        Assert.True(controller.TryPointerMove(aim with { X = control.X }));
        Assert.True(controller.TryPointerUp(aim));
        Assert.False(controller.TryPointerUp(control));
        Assert.Empty(controller.Consume(generation));

        controller.BeginPresentation();
        generation = controller.Generation;
        Assert.Equal(NativeBottomScreenPointerRoute.Control,
            controller.RoutePointerDown(control));
        Assert.True(controller.TryPointerMove(control with { X = aim.X }));
        Assert.True(controller.TryPointerUp(control));
        Assert.Equal(new[]
        {
            NativeBottomScreenPointerPhase.Down,
            NativeBottomScreenPointerPhase.Move,
            NativeBottomScreenPointerPhase.Up
        }, controller.Consume(generation).Select(item => item.Phase));
    }

    [Fact]
    public void TrueDsRejectsContactsOutsideTheVisibleClassicPanel()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1280, 720), new Vector2i(1280, 720),
            NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenAimMode.TrueDs);
        long generation = controller.BeginPresentation();
        controller.UpdatePreferences(NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenStyle.ClassicDs,
            NativeBottomScreenLayoutOptions.Default,
            aimMode: NativeBottomScreenAimMode.TrueDs);
        NativeBottomScreenLayout layout = controller.Layout;
        PointerSample outside = new(61, PointerToolKind.Stylus,
            layout.PanelLogical.Left - 1, layout.PanelLogical.Top + 10,
            1, StylusButtons.None, 1);

        Assert.Equal(NativeBottomScreenPointerRoute.None,
            controller.RoutePointerDown(outside));
        Assert.False(controller.TryPointerDown(outside));
        Assert.Empty(controller.Consume(generation));
    }

    [Fact]
    public void DesktopToggleSessionCentersClampsAndAcceptsMouseClicks()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1280, 720), new Vector2i(2560, 1440),
            NativeBottomScreenMode.Popup);
        long generation = controller.BeginPresentation();

        Assert.True(controller.BeginDesktopSession(
            NativeBottomScreenActivationMode.Toggle));
        Assert.True(controller.DesktopSessionActive);
        Assert.True(controller.Visible);
        Assert.Equal(new Vector2(128, 96), controller.DesktopCursorDs);

        Assert.True(controller.TryDesktopCursorMove(new Vector2(10000, -10000)));
        Assert.Equal(new Vector2(256, 0), controller.DesktopCursorDs);
        Assert.True(controller.TryDesktopPointerDown());
        Assert.True(controller.TryDesktopCursorMove(new Vector2(-10, 10)));
        Assert.True(controller.TryDesktopPointerUp());
        Assert.Equal(new[]
        {
            NativeBottomScreenPointerPhase.Down,
            NativeBottomScreenPointerPhase.Move,
            NativeBottomScreenPointerPhase.Up
        }, controller.Consume(generation).Select(item => item.Phase));

        Assert.True(controller.CompleteDesktopSelection());
        Assert.False(controller.DesktopSessionActive);
        Assert.False(controller.Visible);

        Assert.True(controller.BeginDesktopSession(
            NativeBottomScreenActivationMode.Toggle));
        Assert.True(controller.BeginDesktopSession(
            NativeBottomScreenActivationMode.Toggle));
        Assert.False(controller.DesktopSessionActive);
        Assert.False(controller.Visible);
    }

    [Fact]
    public void DesktopHoldSessionStaysOpenAfterSelectionUntilReleased()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1280, 720), new Vector2i(1280, 720),
            NativeBottomScreenMode.Popup);
        controller.BeginPresentation();

        Assert.True(controller.BeginDesktopSession(
            NativeBottomScreenActivationMode.Hold));
        controller.SetSelectorOpen(true);
        Assert.True(controller.CompleteDesktopSelection());
        Assert.True(controller.DesktopSessionActive);
        Assert.True(controller.Visible);
        Assert.False(controller.SelectorOpen);

        Assert.True(controller.EndDesktopSession());
        Assert.False(controller.DesktopSessionActive);
        Assert.False(controller.Visible);
    }

    [Fact]
    public void DesktopHoldBeginsAContactAndUsesConfiguredCursorOptions()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1280, 720), new Vector2i(2560, 1440),
            NativeBottomScreenMode.Popup);
        long generation = controller.BeginPresentation();
        controller.UpdateDesktopCursorPreferences(2, .25f, .75f);

        Assert.True(controller.BeginDesktopSession(
            NativeBottomScreenActivationMode.Hold));
        Assert.Equal(new Vector2(64, 144), controller.DesktopCursorDs);
        Assert.True(controller.DesktopHoldContactActive);

        NativeBottomScreenLayout layout = controller.Layout;
        Assert.True(controller.TryDesktopCursorMove(new Vector2(
            layout.PanelLogical.Width / 256f, 0)));
        Assert.Equal(new Vector2(66, 144), controller.DesktopCursorDs,
            new Vector2Comparer(.01f));
        Assert.True(controller.TryDesktopPointerUp());

        Assert.True(controller.DesktopSessionActive);
        Assert.True(controller.DesktopHoldContactActive);
        Assert.False(controller.TryDesktopPointerDown());
        Assert.Equal(new[]
        {
            NativeBottomScreenPointerPhase.Down,
            NativeBottomScreenPointerPhase.Move,
            NativeBottomScreenPointerPhase.Up
        }, controller.Consume(generation).Select(item => item.Phase));

        controller.EndDesktopSession();
        Assert.False(controller.DesktopSessionActive);
    }

    [Fact]
    public void HoldSelectorPromotionSurvivesAQueuedRelease()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1280, 720), new Vector2i(1280, 720),
            NativeBottomScreenMode.Popup);
        long generation = controller.BeginPresentation();
        controller.UpdatePreferences(NativeBottomScreenMode.Popup,
            NativeBottomScreenStyle.ClassicDs,
            NativeBottomScreenLayoutOptions.Default);

        Assert.True(controller.BeginDesktopSession(
            NativeBottomScreenActivationMode.Hold));
        Assert.True(controller.TryDesktopPointerUp());
        Assert.NotEmpty(controller.Consume(generation));

        Assert.True(controller.OpenSelectorForDesktopDrag());
        Assert.True(controller.SelectorOpen);
        Assert.True(controller.DesktopHoldContactActive);
    }

    [Fact]
    public void DesktopCursorOptionsClampAndResetToConfiguredStart()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(800, 600), new Vector2i(800, 600),
            NativeBottomScreenMode.AlwaysVisible);
        controller.BeginPresentation();
        controller.UpdateDesktopCursorPreferences(100, -1, 2);

        NativeBottomScreenCursorOptions options
            = controller.DesktopCursorOptions;
        Assert.Equal(NativeBottomScreenCursorOptions.MaximumSensitivity,
            options.Sensitivity);
        Assert.Equal(0, options.StartX);
        Assert.Equal(1, options.StartY);
        Assert.True(controller.BeginDesktopSession(
            NativeBottomScreenActivationMode.Toggle));
        Assert.Equal(new Vector2(0, 192), controller.DesktopCursorDs);

        controller.EndDesktopSession();
        controller.UpdateDesktopCursorPreferences(float.NaN,
            float.PositiveInfinity, float.NegativeInfinity);
        Assert.Equal(NativeBottomScreenCursorOptions.Default,
            controller.DesktopCursorOptions);
    }

    [Fact]
    public void AffinityLayoutChangeCancelsTheActiveOverlayContact()
    {
        var controller = new NativeBottomScreenController();
        controller.Configure(new Vector2i(1280, 720), new Vector2i(2560, 1440),
            NativeBottomScreenMode.AlwaysVisible);
        long generation = controller.BeginPresentation();
        controller.UpdatePreferences(NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenStyle.AffinitySelector,
            NativeBottomScreenLayoutOptions.Default,
            NativeBottomScreenClassicLayoutOptions.Default,
            NativeBottomScreenAffinityLayoutOptions.Default);
        Assert.True(controller.BeginDesktopSession(
            NativeBottomScreenActivationMode.Toggle));
        Assert.True(controller.TryDesktopPointerDown());

        NativeBottomScreenAffinityLayoutOptions changed
            = NativeBottomScreenAffinityLayoutOptions.Default with
            { AltFormX = .75f };
        controller.UpdatePreferences(NativeBottomScreenMode.AlwaysVisible,
            NativeBottomScreenStyle.AffinitySelector,
            NativeBottomScreenLayoutOptions.Default,
            NativeBottomScreenClassicLayoutOptions.Default, changed);

        Assert.False(controller.DesktopSessionActive);
        Assert.Contains(controller.Consume(generation), item =>
            item.Phase == NativeBottomScreenPointerPhase.Cancel);
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
                "bottom_screen_mode=AlwaysVisible",
                "bottom_screen_aim_mode=TrueDs",
                "bottom_screen_activation=Hold",
                "bottom_screen_cursor_sensitivity=1.75",
                "bottom_screen_cursor_start_x=0.2",
                "bottom_screen_cursor_start_y=0.8",
                "bottom_screen_power_beam_x=0.11",
                "bottom_screen_power_beam_y=0.22",
                "bottom_screen_missile_x=0.33",
                "bottom_screen_missile_y=0.44",
                "bottom_screen_next_weapon_x=0.55",
                "bottom_screen_next_weapon_y=0.66",
                "bottom_screen_weapon_select_x=0.77",
                "bottom_screen_weapon_select_y=0.84",
                "bottom_screen_alt_form_x=0.12",
                "bottom_screen_alt_form_y=0.34",
                "bottom_screen_directional_swipe_assist=false",
                "bottom_screen_style=AffinitySelector",
                "bottom_screen_scale=0.65",
                "bottom_screen_center_x=0.2",
                "bottom_screen_center_y=0.3",
                "bottom_screen_opacity=0.4",
                "bottom_screen_labels=false"
            });

            Assert.Equal(PrimeKey.F12, InputSettings.Current.HudOverlay.Key);
            Assert.Equal(NativeBottomScreenMode.AlwaysVisible,
                InputSettings.BottomScreenMode);
            Assert.Equal(NativeBottomScreenAimMode.TrueDs,
                InputSettings.BottomScreenAimMode);
            Assert.Contains("bottom_screen_aim_mode=TrueDs",
                InputSettings.GetSaveLines());
            Assert.Contains("bottom_screen_mode=AlwaysVisible",
                InputSettings.GetSaveLines());
            Assert.Equal(NativeBottomScreenActivationMode.Hold,
                InputSettings.BottomScreenActivation);
            Assert.Contains("bottom_screen_activation=Hold",
                InputSettings.GetSaveLines());
            Assert.Equal(1.75f, InputSettings.BottomScreenCursorSensitivity);
            Assert.Equal(.2f, InputSettings.BottomScreenCursorStartX);
            Assert.Equal(.8f, InputSettings.BottomScreenCursorStartY);
            Assert.Equal(.11f, InputSettings.BottomScreenPowerBeamX);
            Assert.Equal(.22f, InputSettings.BottomScreenPowerBeamY);
            Assert.Equal(.33f, InputSettings.BottomScreenMissileX);
            Assert.Equal(.44f, InputSettings.BottomScreenMissileY);
            Assert.Equal(.55f, InputSettings.BottomScreenNextWeaponX);
            Assert.Equal(.66f, InputSettings.BottomScreenNextWeaponY);
            Assert.Equal(.77f, InputSettings.BottomScreenWeaponSelectX);
            Assert.Equal(.84f, InputSettings.BottomScreenWeaponSelectY);
            Assert.Equal(.12f, InputSettings.BottomScreenAltFormX);
            Assert.Equal(.34f, InputSettings.BottomScreenAltFormY);
            Assert.False(InputSettings.BottomScreenDirectionalSwipeAssist);
            Assert.Contains("bottom_screen_cursor_sensitivity=1.75",
                InputSettings.GetSaveLines());
            Assert.Contains("bottom_screen_cursor_start_x=0.2",
                InputSettings.GetSaveLines());
            Assert.Contains("bottom_screen_cursor_start_y=0.8",
                InputSettings.GetSaveLines());
            Assert.Contains("bottom_screen_power_beam_x=0.11",
                InputSettings.GetSaveLines());
            Assert.Contains("bottom_screen_missile_y=0.44",
                InputSettings.GetSaveLines());
            Assert.Contains("bottom_screen_next_weapon_x=0.55",
                InputSettings.GetSaveLines());
            Assert.Contains("bottom_screen_weapon_select_y=0.84",
                InputSettings.GetSaveLines());
            Assert.Contains("bottom_screen_alt_form_x=0.12",
                InputSettings.GetSaveLines());
            Assert.Contains("bottom_screen_directional_swipe_assist=false",
                InputSettings.GetSaveLines());
            Assert.Equal(NativeBottomScreenStyle.AffinitySelector,
                InputSettings.BottomScreenStyle);
            Assert.Equal(.65f, InputSettings.BottomScreenScale);
            Assert.Equal(.2f, InputSettings.BottomScreenCenterX);
            Assert.Equal(.3f, InputSettings.BottomScreenCenterY);
            Assert.Equal(.4f, InputSettings.BottomScreenOpacity);
            Assert.False(InputSettings.BottomScreenLabels);
            Assert.Contains("bottom_screen_style=AffinitySelector",
                InputSettings.GetSaveLines());
            Assert.Contains("bottom_screen_scale=0.65",
                InputSettings.GetSaveLines());

            InputSettings.LoadLines(new[] { "bottom_screen_mode=not-a-mode" });
            Assert.Equal(NativeBottomScreenMode.AlwaysVisible,
                InputSettings.BottomScreenMode);
            Assert.Equal(PrimeKey.F12, InputSettings.Current.HudOverlay.Key);
            Assert.Equal("Touch screen", InputSettings.ActionName(
                InputSettings.Bindings.Single(property => property.Name == "HudOverlay")));
        }
        finally
        {
            prior.Restore();
        }
    }

    [Fact]
    public void BottomScreenSettingsResetToNativeDefaults()
    {
        InputSettings.Snapshot prior = InputSettings.CaptureSnapshot();
        try
        {
            InputSettings.BottomScreenCursorSensitivity = 3.2f;
            InputSettings.BottomScreenAimMode = NativeBottomScreenAimMode.TrueDs;
            InputSettings.BottomScreenCursorStartX = .1f;
            InputSettings.BottomScreenCursorStartY = .9f;
            InputSettings.BottomScreenPowerBeamX = .9f;
            InputSettings.BottomScreenMissileY = .9f;
            InputSettings.BottomScreenDirectionalSwipeAssist = false;

            InputSettings.Reset();

            Assert.Equal(NativeBottomScreenCursorOptions.Default.Sensitivity,
                InputSettings.BottomScreenCursorSensitivity);
            Assert.Equal(NativeBottomScreenCursorOptions.Default.StartX,
                InputSettings.BottomScreenCursorStartX);
            Assert.Equal(NativeBottomScreenCursorOptions.Default.StartY,
                InputSettings.BottomScreenCursorStartY);
            Assert.Equal(NativeBottomScreenClassicLayoutOptions.Default,
                InputSettings.CurrentBottomScreenClassicLayout);
            Assert.Equal(NativeBottomScreenAffinityLayoutOptions.Default,
                InputSettings.CurrentBottomScreenAffinityLayout);
            Assert.True(InputSettings.BottomScreenDirectionalSwipeAssist);
            Assert.Equal(NativeBottomScreenAimMode.Free,
                InputSettings.BottomScreenAimMode);
        }
        finally
        {
            prior.Restore();
        }
    }

    [Fact]
    public void AffinitySettingsRoundTripAndPartialLegacyConfigKeepsOtherDefaults()
    {
        InputSettings.Snapshot prior = InputSettings.CaptureSnapshot();
        try
        {
            InputSettings.Reset();
            InputSettings.LoadLines(new[]
            {
                "bottom_screen_affinity_volt_driver_x=0.21",
                "bottom_screen_affinity_volt_driver_y=0.32",
                "bottom_screen_affinity_alt_form_y=0.74"
            });

            Assert.Equal(.21f, InputSettings.BottomScreenAffinityVoltDriverX);
            Assert.Equal(.32f, InputSettings.BottomScreenAffinityVoltDriverY);
            Assert.Equal(.74f, InputSettings.BottomScreenAffinityAltFormY);
            Assert.Equal(NativeBottomScreenAffinityLayoutOptions.Default.BattlehammerX,
                InputSettings.BottomScreenAffinityBattlehammerX);
            Assert.Contains("bottom_screen_affinity_volt_driver_x=0.21",
                InputSettings.GetSaveLines());
            Assert.Contains("bottom_screen_affinity_alt_form_y=0.74",
                InputSettings.GetSaveLines());
        }
        finally
        {
            prior.Restore();
        }
    }

    private static Vector2 SelectorPoint(float division, float distY)
        => new(224 - division * distY, 38 + distY);

    private const float NativeBottomScreenSnapshotDeadzone
        = NativeBottomScreenClassicLayoutSnapshot.SwipeDeadzone;

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
