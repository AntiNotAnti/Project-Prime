using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Updates;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Replay;
using MphRead.Mods.Update;
using MphRead.Mods.Network;

internal sealed partial class ReplayWindowAcceptance
{
    private readonly List<object> _updateOwnerChecks = new();

    private void PlanNativeUpdateOwnerIntegration()
    {
        const string fakeTag = "SYNTHETIC-RML96-NATIVE-OWNER-DO-NOT-INSTALL";
        var fakeUpdate = new UpdateInfo
        {
            Tag = fakeTag, Version = new Version(0, 0, 0), AssetName = "", AssetUrl = "",
            AssetSize = 0, AssetDigest = "", PageUrl = "",
            Notes = "SYNTHETIC owner-routing fixture. Later only; no update check, install, download or browser action."
        };
        StartScreen? front = null;
        object? baselinePlaybackOwner = null;
        NativeUpdatePrompt? prompt = null;
        RmlUiDocumentToken modal = default, home = default;
        PlanHeaderTransition(MainHeaderRoutes[3], "Theatre before synthetic automatic update offer");
        Wait("Theatre scan settles before update owner test", () => ReadStatic<RmlUiMenuController>("_controller") is { Busy: false });
        Do("queue synthetic update on retained actual StartScreen while Theatre owns native UI", () =>
        {
            front = RetainedUpdateFront();
            Require(front != null && !front.Prime.Overlays.IsOpen && !RmlUiOpenGlSmoke.NativeUpdateOpen,
                "synthetic offer begins with retained Shell front and no legacy/native modal");
            baselinePlaybackOwner = DemoPlayback.PlaybackOwner;
            RequireNoUpdateInstall(front!, baselinePlaybackOwner);
            typeof(StartScreen).GetMethod("QueueUpdatePrompt", Private)!.Invoke(front, new object[] { fakeUpdate });
        });
        WaitHeaderFrames(3, "synthetic automatic update remains queued in Theatre without legacy modal", () =>
        {
            Require(ReferenceEquals(front, RetainedUpdateFront()) && HeaderRouteReady(MainHeaderRoutes[3])
                && !RmlUiOpenGlSmoke.NativeUpdateOpen && ReadStatic<NativeUpdatePrompt>("_updatePrompt") == null
                && !front!.Prime.Overlays.IsOpen && PendingSyntheticUpdate(front, fakeTag),
                "retained front defers offer through three actual Shell frames while Theatre owns foreground");
            RequireNoUpdateInstall(front!, baselinePlaybackOwner!);
            _updateOwnerChecks.Add(new { Stage = "deferred-in-Theatre", Tag = fakeTag, Swap = _observedFrames,
                Front = Id(front), PlaybackOwner = Id(baselinePlaybackOwner), LegacyOverlay = false, NativeModal = false, Pending = true });
        });
        DoHeader("actual PLAY header returns Home for Shell-owned synthetic update", () => Activate("nav_play"));
        Wait("Shell owner tick opens actual native update modal over Home", () =>
        {
            if (!RmlUiOpenGlSmoke.NativeUpdateOpen) return false;
            prompt = ReadStatic<NativeUpdatePrompt>("_updatePrompt");
            var pages = ReadStatic<RmlUiPageManager>("_pages")!;
            Require(prompt != null && ReferenceEquals(front, RetainedUpdateFront())
                && ReadStatic<RmlUiLauncherPages>("_launcherPages") is { Suspended: false, Page: RmlUiMenuPage.Home }
                && pages.ModalCount == 1 && pages.Top == prompt.Document && Host!.CurrentInputDocument == prompt.Document
                && !front!.Prime.Overlays.IsOpen && typeof(StartScreen).GetField("_pendingUpdatePrompt", Private)!.GetValue(front) == null,
                "actual owner handoff clears pending offer and uses exactly one native modal on retained Home");
            modal = prompt!.Document; home = pages.Page;
            RequireNoUpdateInstall(front!, baselinePlaybackOwner!);
            // Shell opens the modal during its owner tick; semantic geometry is
            // available only after the next native layout update.
            if (!Host!.TryGetElementBounds(modal, "update_available", out _, out _, out float versionWidth, out float versionHeight)
                || versionWidth <= 0 || versionHeight <= 0
                || !NativeSemanticReadback.Read(Host, modal).ContainsText(fakeTag)) return false;
            _updateOwnerChecks.Add(new { Stage = "native-modal-on-Home", Tag = fakeTag, Swap = _observedFrames,
                Front = Id(front), Modal = modal, Home = home, LegacyOverlay = false, Pending = false });
            return true;
        });
        Do("capture actual synthetic native automatic update modal", () => Capture("native-update-synthetic-owner-handoff"));
        // Intentionally use only Later. Accept/Back decisions are exercised by
        // standalone presenter tests, without the production installation owner.
        DoHeader("dismiss synthetic automatic update through actual native Later action", () => Activate("update_later"));
        WaitHeaderFrames(3, "Later closes one synthetic update modal and retains exact Home owner", () =>
        {
            var pages = ReadStatic<RmlUiPageManager>("_pages")!;
            Require(prompt is { Closed: true, Accepted: false } && !RmlUiOpenGlSmoke.NativeUpdateOpen
                && ReadStatic<NativeUpdatePrompt>("_updatePrompt") == null && !Host!.IsAlive(modal)
                && pages.ModalCount == 0 && pages.Page == home && pages.Top == home && Host.CurrentInputDocument == home
                && HeaderRouteReady(MainHeaderRoutes[0]) && ReferenceEquals(front, RetainedUpdateFront())
                && !front!.Prime.Overlays.IsOpen && typeof(StartScreen).GetField("_pendingUpdatePrompt", Private)!.GetValue(front) == null
                && (string?)typeof(StartScreen).GetField("_lastPromptedUpdateTag", Private)!.GetValue(front) == fakeTag,
                "one Later decision retires exact modal without replacing Home/front or requeueing offer");
            RequireNoUpdateInstall(front!, baselinePlaybackOwner!);
            _updateOwnerChecks.Add(new { Stage = "Later-retired-once", Tag = fakeTag, Swap = _observedFrames,
                Front = Id(front), Modal = modal, Home = home, Accepted = false, Pending = false, LegacyOverlay = false });
        });
    }

    private static StartScreen? RetainedUpdateFront()
        => (StartScreen?)typeof(Shell).GetField("_front", StaticPrivate)!.GetValue(null);

    private static bool PendingSyntheticUpdate(StartScreen front, string tag)
        => typeof(StartScreen).GetField("_pendingUpdatePrompt", Private)!.GetValue(front) is UpdateInfo pending && pending.Tag == tag;

    private void RequireNoUpdateInstall(StartScreen front, object baselinePlaybackOwner)
    {
        object? updating = typeof(StartScreen).GetField("_updating", Private)!.GetValue(front);
        object currentPlaybackOwner = DemoPlayback.PlaybackOwner;
        bool active = DemoPlayback.IsActive;
        var replica = DemoPlayback.ReplicaScene;
        var presentation = DemoPlayback.PresentationScene;
        bool hasScene = _window!.HasScene;
        Require(updating is false && !active && ReferenceEquals(currentPlaybackOwner, baselinePlaybackOwner)
            && replica == null && presentation == null && !hasScene,
            "Later-only synthetic offer preserves dormant playback ownership and acquires no install or scene; "
            + $"updating={updating}, active={active}, baselineOwner={Id(baselinePlaybackOwner)}, currentOwner={Id(currentPlaybackOwner)}, "
            + $"sameOwner={ReferenceEquals(currentPlaybackOwner, baselinePlaybackOwner)}, replica={Id(replica)}, presentation={Id(presentation)}, windowHasScene={hasScene}");
    }
}
