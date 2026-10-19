using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;

internal sealed partial class ReplayWindowAcceptance
{
    private readonly List<object> _ownedDisplayTrialChecks = new();
    private Action? _displayTrialDetach;

    private void PlanOwnedDisplayTrialQualification()
    {
        NativeFullSettingsPage? settings = null;
        NativeFullSettingsController? controller = null;
        NativeSettingRow? modeRow = null;
        RmlUiDocumentToken document = default;
        object? front = null, playbackOwner = null;
        WindowStartMode beforeMode = default, beforePreference = default;
        Vector2i beforeClient = default, beforeFramebuffer = default, beforeLocation = default, beforeClientLocation = default;
        WindowBorder beforeBorder = default;
        WindowState beforeState = default;
        Dictionary<string, byte[]?>? stores = null;
        string cycle = "", originalValue = "";
        bool sawPreAckResize = false;
        long applySwap = -1, preAckSwap = -1, acknowledgedSwap = -1;
        int resizeObservations = 0;
        Wait("real display trial begins at clean native Home", () => HeaderRouteReady(MainHeaderRoutes[0]), 12000);
        DoHeader("open actual full Settings for private window display trial", () => Activate("header_settings"));
        Wait("actual full Settings owns the private window before display trial", () =>
        {
            if (!HeaderWindowFocused()) return false;
            settings = ReadStatic<NativeFullSettingsPage>("_fullSettings");
            controller = Read<NativeFullSettingsController>(settings, "_controller");
            if (settings is not { OwnsForeground: true, CanNavigateAway: true } || controller == null) return false;
            document = settings.Document; front = RetainedUpdateFront(); playbackOwner = DemoPlayback.PlaybackOwner;
            modeRow = controller.Rows.Single(row => row.Category == "Display" && row.Group == "Window");
            originalValue = modeRow.Value;
            beforeMode = WindowMode.Current; beforePreference = LauncherPrefs.WindowMode;
            beforeClient = _window!.ClientSize; beforeFramebuffer = _window.FramebufferSize;
            beforeLocation = _window.Location; beforeClientLocation = _window.ClientLocation;
            beforeBorder = _window.WindowBorder; beforeState = _window.WindowState;
            Require(beforeMode == WindowStartMode.Windowed && beforePreference == beforeMode
                && Enum.Parse<WindowStartMode>(originalValue, true) == beforeMode && !controller.CanKeepVideo,
                "private fixture starts with matching Windowed actual/saved/draft modes and no acknowledged display trial");
            stores = new[] { Path.Combine(LauncherPrefs.Directory, "launcher.txt"), Path.Combine(LauncherPrefs.Directory, "controls.txt"),
                Paths.Combine("Savedata", "settings.json") }.Distinct().ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
            return true;
        }, 12000);
        DoHeader("find actual native Window Mode setting", () =>
        {
            Host!.SetField(document, "native_settings_search", "Window Mode");
            PressSettingsSemanticButton("FIND SETTING");
        });
        Wait("Find Setting focuses actual Window Mode cycle", () =>
        {
            if (!HeaderWindowFocused()) return false;
            var visible = Read<NativeSettingRow[]>(settings, "_visible");
            int index = visible == null ? -1 : Array.IndexOf(visible, modeRow!);
            if (index < 0) return false;
            cycle = "native_settings_cycle_" + index;
            return Host!.FocusedElement() == cycle;
        }, 12000);
        DoHeader("stage Borderless Fullscreen through actual native Window Mode cycle", () => Activate(cycle));
        Wait("native Window Mode draft is Borderless while actual window remains Windowed", () =>
        {
            if (!HeaderWindowFocused() || !controller!.Dirty) return false;
            Require(Enum.Parse<WindowStartMode>(modeRow!.Value, true) == WindowStartMode.BorderlessFullscreen
                && WindowMode.Current == beforeMode && LauncherPrefs.WindowMode == beforePreference
                && !controller.ConfirmingVideo && !controller.CanKeepVideo,
                "cycling stages Borderless without changing actual mode, live preference or Keep authority");
            return true;
        }, 12000);
        DoHeader("observe actual window resize boundary then apply native display proposal", () =>
        {
            void ObserveResize()
            {
                resizeObservations++;
                // Read only: the production callback still performs the actual mode change.
                if (controller!.ConfirmingVideo && !controller.CanKeepVideo)
                { sawPreAckResize = true; preAckSwap = _observedFrames; }
            }
            Action<ResizeEventArgs> resized = _ => ObserveResize();
            Action<FramebufferResizeEventArgs> framebufferResized = _ => ObserveResize();
            _window!.Resize += resized; _window.FramebufferResize += framebufferResized;
            _displayTrialDetach = () => { _window.Resize -= resized; _window.FramebufferResize -= framebufferResized; };
            applySwap = _observedFrames;
            Activate("native_settings_apply");
        });
        Wait("actual Borderless trial receives production postdraw usable-frame acknowledgement", () =>
        {
            if (!HeaderWindowFocused() || !controller!.ConfirmingVideo || !controller.CanKeepVideo) return false;
            Require(WindowMode.Current == WindowStartMode.BorderlessFullscreen && _window!.WindowBorder == WindowBorder.Hidden
                && _observedFrames > applySwap && sawPreAckResize && _observedFrames > preAckSwap,
                "actual mode changed at a resize boundary without Keep authority, then a later drawn frame acknowledged it; "
                + $"actualMode={WindowMode.Current}, applySwap={applySwap}, preAckSwap={preAckSwap}, currentSwap={_observedFrames}, resizeEvents={resizeObservations}");
            acknowledgedSwap = _observedFrames;
            RequireSameDisplayOwners(); RequireUnwrittenDisplayStores();
            return true;
        }, 12000);
        Wait("actual native Keep control enables after production frame acknowledgement", () =>
        {
            if (!HeaderWindowFocused() || !NativeDisplayKeepEnabled(document)) return false;
            Require(controller!.CanKeepVideo && controller.ConfirmingVideo && _observedFrames >= acknowledgedSwap,
                "visible enabled Keep is backed by the actual postdraw acknowledgement");
            RequireSameDisplayOwners(); RequireUnwrittenDisplayStores();
            _ownedDisplayTrialChecks.Add(new { Stage = "actual-borderless-acknowledged", Mode = WindowMode.Current.ToString(),
                ApplySwap = applySwap, PreAckSwap = preAckSwap, AcknowledgedSwap = acknowledgedSwap, Swap = _observedFrames,
                ResizeEvents = resizeObservations, Document = document, SettingsOwner = Id(settings), KeepEnabled = true,
                Client = _window!.ClientSize.ToString(), Framebuffer = _window.FramebufferSize.ToString() });
            return true;
        }, 12000);
        DoHeader("capture actual private-window display trial without Keep", () => Capture("native-settings-owned-borderless-trial"));
        DoHeader("revert actual window through native Revert display mode action", () => PressSettingsSemanticButton("REVERT"));
        Wait("native Revert restores exact original actual window geometry and retains dirty draft", () =>
        {
            if (!HeaderWindowFocused() || controller!.ConfirmingVideo || WindowMode.Current != beforeMode
                || _window!.ClientSize != beforeClient || _window.FramebufferSize != beforeFramebuffer
                || _window.Location != beforeLocation || _window.ClientLocation != beforeClientLocation
                || _window.WindowBorder != beforeBorder || _window.WindowState != beforeState) return false;
            Require(controller.Dirty && !controller.CanKeepVideo && modeRow!.Value != originalValue
                && LauncherPrefs.WindowMode == beforePreference && !WindowGeometry.DisplayTrialActive,
                "Revert restores actual window and saved preference while preserving uncommitted display draft");
            RequireSameDisplayOwners(); RequireUnwrittenDisplayStores();
            DetachDisplayTrialObserver();
            _ownedDisplayTrialChecks.Add(new { Stage = "actual-window-restored", Mode = WindowMode.Current.ToString(),
                Swap = _observedFrames, Document = document, Dirty = controller.Dirty, KeepRequested = false,
                Client = _window.ClientSize.ToString(), Framebuffer = _window.FramebufferSize.ToString(),
                Location = _window.Location.ToString(), ClientLocation = _window.ClientLocation.ToString(), StoresUnchanged = true });
            return true;
        }, 12000);
        DoHeader("discard real display draft without applying or saving", () => PressSettingsSemanticButton("DISCARD"));
        Wait("display draft discard retains actual mode and all original preference bytes", () =>
        {
            if (!HeaderWindowFocused() || !settings!.CanNavigateAway || controller!.Dirty) return false;
            Require(modeRow!.Value == originalValue && WindowMode.Current == beforeMode, "discard restores original display draft");
            RequireSameDisplayOwners(); RequireUnwrittenDisplayStores(); return true;
        }, 12000);
        PlanHeaderTransition(MainHeaderRoutes[0], "clean Home after actual window display Revert and Discard");

        void RequireSameDisplayOwners() => Require(ReferenceEquals(settings, ReadStatic<NativeFullSettingsPage>("_fullSettings"))
            && settings!.OwnsForeground && settings.Document == document && Host!.CurrentInputDocument == document
            && ReferenceEquals(front, RetainedUpdateFront()) && !RetainedUpdateFront()!.Prime.Overlays.IsOpen
            && ReferenceEquals(playbackOwner, DemoPlayback.PlaybackOwner) && !DemoPlayback.IsActive
            && DemoPlayback.ReplicaScene == null && DemoPlayback.PresentationScene == null && !_window!.HasScene,
            "real display trial retains native Settings/front/playback owners with no legacy overlay or scene");
        void RequireUnwrittenDisplayStores() => Require(stores!.All(pair => pair.Value == null ? !File.Exists(pair.Key)
            : File.Exists(pair.Key) && File.ReadAllBytes(pair.Key).AsSpan().SequenceEqual(pair.Value)),
            "real display trial, Revert and Discard leave original private preference bytes unchanged");
    }

    private bool NativeDisplayKeepEnabled(RmlUiDocumentToken document)
    {
        using var snapshot = JsonDocument.Parse(NativeSemanticReadback.Read(Host!, document).RawJson);
        return snapshot.RootElement.GetProperty("nodes").EnumerateArray().Any(node =>
            node.GetProperty("id").GetString() == "native_settings_keep_video"
            && node.GetProperty("enabled").GetBoolean() && !node.GetProperty("offscreen").GetBoolean());
    }

    private void DetachDisplayTrialObserver()
    {
        var detach = _displayTrialDetach; _displayTrialDetach = null; detach?.Invoke();
    }
}
