using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using OpenTK.Windowing.GraphicsLibraryFramework;

internal sealed partial class ReplayWindowAcceptance
{
    private readonly List<object> _auxiliaryBackdropChecks = new();

    private void PlanNativeHomeAuxiliaryBackgrounds()
    {
        object? front = null, playbackOwner = null;
        Wait("actual Home owns auxiliary Social and News background journey", () => HeaderRouteReady(MainHeaderRoutes[0]), 12000);
        DoHeader("retain Home owners before actual auxiliary background navigation", () =>
        {
            front = RetainedUpdateFront(); playbackOwner = DemoPlayback.PlaybackOwner;
        });
        foreach (var target in new[]
        {
            (Name: "Social", Button: "home_social_open", Field: "_social", PageKey: "social-friends",
                Backdrop: LauncherBackdropScene.Play, Capture: "native-social-home-chamber", Pointer: true),
            (Name: "News", Button: "home_feature_open", Field: "_news", PageKey: "news",
                Backdrop: LauncherBackdropScene.ReplayStudio, Capture: "native-news-home-chamber", Pointer: false)
        })
        {
            RmlUiDocumentToken document = default;
            object? pageOwner = null;
            if (target.Pointer)
            {
                DoHeader("scroll actual Home Social link into its visible right rail", () =>
                {
                    var before = AuxiliaryBounds(target.Button);
                    _auxiliaryBackdropChecks.Add(new { Stage = "before-native-focus-scroll", Target = target.Button,
                        Bounds = before, Swap = _observedFrames });
                    Focus(target.Button);
                });
                WaitHeaderFrames(2, "actual Home Social link settles after native ScrollIntoView", () =>
                    Require(Host!.FocusedElement() == target.Button, "native focus scroll reaches the authored Social link without activation"));
            }
            DoHeader("actual Home opens " + target.Name + " for composed backdrop capture", () =>
            {
                if (target.Pointer) PressVisibleHomeSocialLink(target.Button);
                else Activate(target.Button);
            });
            Wait("actual " + target.Name + " owns native foreground without waiting for account service", () =>
            {
                if (!AuxiliaryForeground(target.Field, target.PageKey)) return false;
                document = Host!.CurrentInputDocument; pageOwner = ReadStatic<object>(target.Field);
                return true;
            }, 12000);
            WaitHeaderFrames(3, "actual " + target.Name + " backdrop and retained scene-free owner settle", () =>
            {
                Require(AuxiliaryForeground(target.Field, target.PageKey) && Host!.CurrentInputDocument == document
                    && ReferenceEquals(pageOwner, ReadStatic<object>(target.Field)) && ReferenceEquals(front, RetainedUpdateFront())
                    && !RetainedUpdateFront()!.Prime.Overlays.IsOpen && LauncherBackdrop.Scene == target.Backdrop
                    && !_window!.HasScene && _window.CanonicalScene == null && !DemoPlayback.IsActive
                    && DemoPlayback.ReplicaScene == null && DemoPlayback.PresentationScene == null
                    && ReferenceEquals(playbackOwner, DemoPlayback.PlaybackOwner),
                    target.Name + " uses its explicit composed native backdrop without legacy UI, stale map or replay scene");
                Capture(target.Capture);
                _auxiliaryBackdropChecks.Add(new { Page = target.Name, Input = target.Pointer ? "native-focus-scroll-then-production-pointer" : "native-keyboard",
                    Document = document, Owner = Id(pageOwner), Front = Id(front), Backdrop = LauncherBackdrop.Scene.ToString(),
                    CanonicalScene = false, ReplayScene = false, Swap = _observedFrames, Capture = target.Capture });
            });
            DoHeader("actual " + target.Name + " returns to Home after background capture", () =>
            {
                if (target.Name == "News") Activate("news_back");
                else PressHeader("nav_play", true);
            });
            Wait("actual Home resumes after " + target.Name + " backdrop capture", () =>
                HeaderRouteReady(MainHeaderRoutes[0]) && ReadStatic<object>(target.Field) == null, 12000);
        }
    }

    private readonly record struct AuxiliaryBox(float X, float Y, float Width, float Height)
    {
        internal float Right => X + Width;
        internal float Bottom => Y + Height;
    }

    private AuxiliaryBox AuxiliaryBounds(string id)
    {
        Require(Host!.TryGetElementBounds(Host.CurrentInputDocument, id, out float x, out float y, out float width, out float height)
            && width > 0 && height > 0, "current native auxiliary element bounds: " + id);
        return new(x, y, width, height);
    }

    private void PressVisibleHomeSocialLink(string id)
    {
        var host = Host!;
        var window = _window!;
        var target = AuxiliaryBounds(id);
        var rail = AuxiliaryBounds("home_right_rail");
        var stage = AuxiliaryBounds("stage");
        var footer = AuxiliaryBounds("footerbar");
        var topbar = AuxiliaryBounds("topbar");
        float left = Math.Max(0, Math.Max(target.X, Math.Max(rail.X, stage.X)));
        float top = Math.Max(topbar.Bottom, Math.Max(target.Y, Math.Max(rail.Y, stage.Y)));
        float right = Math.Min(window.FramebufferSize.X, Math.Min(target.Right, Math.Min(rail.Right, stage.Right)));
        float bottom = Math.Min(footer.Y, Math.Min(window.FramebufferSize.Y, Math.Min(target.Bottom, Math.Min(rail.Bottom, stage.Bottom))));
        using var snapshot = JsonDocument.Parse(NativeSemanticReadback.Read(host, host.CurrentInputDocument).RawJson);
        var node = snapshot.RootElement.GetProperty("nodes").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
        Require(right - left >= 4 && bottom - top >= 4 && node.GetProperty("enabled").GetBoolean()
            && !node.GetProperty("offscreen").GetBoolean(),
            "native-scrolled Social link has a real visible clipped pointer target; "
            + $"target={target}, rail={rail}, stage={stage}, footer={footer}, clipped=({left},{top})–({right},{bottom})");
        double clientX = (left + (right - left) / 2) * window.ClientSize.X / window.FramebufferSize.X;
        double clientY = (top + (bottom - top) / 2) * window.ClientSize.Y / window.FramebufferSize.Y;
        Require(RmlUiOpenGlSmoke.PointerMoved(clientX, clientY)
            && RmlUiOpenGlSmoke.PointerButton(MouseButton.Left, clientX, clientY, true)
            && RmlUiOpenGlSmoke.PointerButton(MouseButton.Left, clientX, clientY, false)
            && host.HoveredAction() == "route:social"
            && (host.FocusedElement() == id || host.FocusedElement() == "home_social_open_label"),
            "actual production pointer hits visible Social link after native scrolling; "
            + $"client=({clientX},{clientY}), focused={host.FocusedElement()}, hoveredAction={host.HoveredAction()}, target={target}, rail={rail}");
        _auxiliaryBackdropChecks.Add(new { Stage = "native-scrolled-production-pointer", Target = id, Bounds = target,
            Rail = rail, StageBounds = stage, Footer = footer, Clipped = new AuxiliaryBox(left, top, right - left, bottom - top),
            ClientX = clientX, ClientY = clientY, Focused = host.FocusedElement(), HoveredAction = host.HoveredAction(), Swap = _observedFrames });
    }

    private bool AuxiliaryForeground(string field, string pageKey)
    {
        if (!HeaderWindowFocused()) return false;
        var pages = ReadStatic<RmlUiPageManager>("_pages");
        return Host is { Active: true } host && pages != null && pages.PageKey == pageKey
            && pages.Page != default && pages.Top == pages.Page && host.CurrentInputDocument == pages.Page
            && host.IsVisible(pages.Page) && ReadStatic<object>(field) != null
            && ReadStatic<RmlUiLauncherPages>("_launcherPages") is { Suspended: true };
    }
}
