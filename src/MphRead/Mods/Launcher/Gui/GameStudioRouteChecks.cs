#if MPHREAD_SHELL
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;
using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;

namespace MphRead.Mods.Launcher.Gui;

// Game routing uses injected process launchers and the canonical passive replay transport.
internal static class GameStudioRouteChecks
{
    internal static void GameSurfaceSizing(Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var surface = (UiSurface)typeof(UiSurface).GetConstructor(flags, null, Type.EmptyTypes, null)!.Invoke(null);
        var host = (Avalonia.Controls.LayoutTransformControl)typeof(UiSurface).GetField("_host", flags)!.GetValue(surface)!;
        var viewField = typeof(UiSurface).GetField("_view", flags)!;
        bool nativeRaster = UiSurface.NativeRaster;
        double priorBake = UiLayout.BakeScale;
        try
        {
            UiSurface.NativeRaster = false;
            Control[] views = { new ForgeWorkspace((_, _) => null),
                PrimeChrome.Stack(new ReplayViewport(), new ReplayQuickControlsView()) };
            foreach (Control view in views)
            {
                // Attach the actual game controls without Tick's native GPU upload.
                viewField.SetValue(surface, view); host.Child = view;
                foreach (var size in new[] { (Width: 830, Height: 390, Raster: 1.0),
                    (Width: 1280, Height: 720, Raster: 1.0), (Width: 3840, Height: 2160, Raster: 0.5) })
                {
                    surface.Resize(size.Width, size.Height);
                    check(Math.Abs(surface.Scale - UiLayout.Factor(size.Width, size.Height) * size.Raster) < 0.0001
                        && surface.WindowWidth == (int)(size.Width * size.Raster)
                        && surface.WindowHeight == (int)(size.Height * size.Raster),
                        "ordinary game " + view.GetType().Name + " uses the game layout curve and raster cap at " + size.Width + "x" + size.Height);
                }
                check(!view.GetVisualDescendants().Any(control => control.GetType().Name == "MapStudioScreen"),
                    "ordinary game viewing and external launch surfaces do not embed an editor sizing owner");
            }
            UiSurface.NativeRaster = true;
            surface.Resize(3840, 2160);
            check(surface.WindowWidth == 3840 && surface.WindowHeight == 2160
                && Math.Abs(surface.Scale - UiLayout.Factor(3840, 2160)) < 0.0001,
                "native raster preference preserves the same game layout curve for the Theatre viewing surface");
        }
        finally
        {
            host.Child = null; viewField.SetValue(surface, null);
            if (surface.Root is IDisposable disposable) disposable.Dispose();
            UiSurface.NativeRaster = nativeRaster; UiLayout.BakeScale = priorBake;
        }
    }

    internal static void ForgeSurface(Action<bool, string> check)
    {
        var launches = new List<(string? Path, bool Recover)>();
        string? failure = null;
        var forge = new ForgeWorkspace((path, recover) =>
        { launches.Add((path, recover)); return failure; });
        var window = new Window { Width = 1280, Height = 720, Content = forge, ShowInTaskbar = false };
        try
        {
            window.Show(); Drain(window);
            foreach (string id in new[] { "forge.open", "forge.project", "forge.recover", "forge.back" })
                check(ControllerNav.Find(forge, id) is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true },
                    "game Forge exposes reachable external action " + id);
            check(!forge.GetVisualDescendants().OfType<Control>().Any(control =>
                control.GetValue(ControllerNav.NavIdProperty)?.StartsWith("studio.", StringComparison.Ordinal) == true),
                "game Forge exposes no embedded map authoring actions");
            Click(ControllerNav.Find(forge, "forge.open")!);
            check(launches.Count == 1 && launches[0] == (null, false)
                && HasText(forge, "Opening Project Prime Studio"),
                "game Forge launches the external Studio once through its injected owner");
            string project = Path.Combine(Path.GetTempPath(), "prime map project " + Guid.NewGuid().ToString("N") + ".json");
            forge.Open(project, false);
            check(launches.Count == 2 && launches[1] == (project, false),
                "game Forge preserves the exact absolute map-project deep link");
            Click(ControllerNav.Find(forge, "forge.recover")!);
            check(launches.Count == 3 && launches[2] == (null, true),
                "game Forge forwards session recovery to the external Studio");
            const string expectedFailure = "Project Prime Studio is unavailable. Install the complete desktop release.";
            failure = expectedFailure;
            Click(ControllerNav.Find(forge, "forge.open")!);
            check(launches.Count == 4 && HasText(forge, failure)
                && ControllerNav.Find(forge, "forge.open")!.IsEnabled,
                "failed Studio launch leaves a visible actionable error and enabled retry");
            forge.ShowLaunchFailure("Native home already attempted this Studio launch.");
            check(launches.Count == 4 && HasText(forge, "Native home already attempted this Studio launch."),
                "native-home fallback shows its existing failure without retrying the launcher");
            failure = null;
            Click(ControllerNav.Find(forge, "forge.open")!);
            check(launches.Count == 5 && HasText(forge, "Opening Project Prime Studio") && !HasText(forge, expectedFailure),
                "successful Studio retry replaces the prior launch error");
            int closed = 0; forge.Closed += (_, _) => closed++;
            Click(ControllerNav.Find(forge, "forge.back")!);
            check(closed == 1 && launches.Count == 5, "Forge Back requests navigation without launching another process");
            check(!NetSession.Active, "external Forge fixture creates no gameplay session");
        }
        finally { window.Close(); }
        void Click(Control control) { control.Focus(); FocusNavigator.Key(control, Key.Enter); Drain(window); }
    }

    internal static void ReplayViewportNavigation(Action<bool, string> check)
    {
        WithPassiveReplay(check, session =>
        {
            ReplayCamera.EnsureTrack();
            string replay = session.CurrentPath!;
            ReplayCamera.Track.Put(new(0, Vector3.Zero, Quaternion.Identity, 1.2f));
            check(ReplayCamera.Track.Save(replay), "focused viewport fixture persists its existing camera sidecar");
            byte[] camera = File.ReadAllBytes(replay + ".camera");
            var view = new ReplayViewport { MinHeight = 180 };
            var navigation = new PrimeButton("STANDARD NAVIGATION");
            var content = PrimeChrome.Stack(view, navigation);
            using var shell = new PrimeShell(route => route == PrimeRoute.Theatre ? content
                : new PrimePanel(PrimeChrome.Stack(new PrimeButton("OTHER ROUTE"))), () => { });
            shell.Start();
            var window = new Window { Width = 1280, Height = 720, Content = shell, ShowInTaskbar = false };
            try
            {
                window.Show(); shell.Router.Navigate(PrimeRoute.Theatre); Drain(window);
                view.Focus(); Drain(window);
                check(view.IsFocused, "ReplayViewport routing uses an actually focused visual control");
                var q = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Source = view, Key = Key.Q };
                var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Source = view, Key = Key.E };
                view.RaiseEvent(q); view.RaiseEvent(e); Drain(window);
                var held = (HashSet<Key>)typeof(ReplayViewport).GetField("_held", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
                check(shell.Router.Current == PrimeRoute.Theatre && q.Handled && e.Handled && held.Contains(Key.Q) && held.Contains(Key.E),
                    "focused ReplayViewport receives Q/E camera keys before game tab navigation");
                FocusNavigator.Key(view, Key.B); FocusNavigator.Key(view, Key.Delete);
                check(!ReplayCamera.BookmarkRequested && camera.SequenceEqual(File.ReadAllBytes(replay + ".camera")),
                    "actual ReplayViewport key events cannot schedule or persist game camera authoring");
                FocusNavigator.Key(view, Key.Escape); Drain(window);
                check(shell.Router.Current == PrimeRoute.Theatre && held.Count == 0
                    && typeof(ReplayViewport).GetField("_focused", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null) == null,
                    "viewport Escape releases camera input while retaining the Theatre route");
                navigation.Focus(); FocusNavigator.Key(navigation, Key.E); Drain(window);
                check(shell.Router.Current == PrimeRoute.Forge,
                    "ordinary game control E still advances tabs after viewport release");
                FocusNavigator.Key(FocusNavigator.Ensure(shell)!, Key.Q); Drain(window);
                check(shell.Router.Current == PrimeRoute.Theatre && ReferenceEquals(shell.Workspaces.Content, content),
                    "ordinary game Q returns to the retained Theatre viewport workspace");
                navigation.Focus(); FocusNavigator.Key(navigation, Key.Escape); Drain(window);
                check(shell.Router.Current == PrimeRoute.Forge,
                    "ordinary game Escape still follows router Back after viewport release");
                check(camera.SequenceEqual(File.ReadAllBytes(replay + ".camera")) && !NetSession.Active,
                    "viewport/game navigation preserves camera sidecar bytes without gameplay ownership");
            }
            finally { window.Close(); }
        });
    }

    internal static void ForgeNavigation(Action<bool, string> check)
    {
        PrimeShell? shell = null;
        int launches = 0;
        var forge = new ForgeWorkspace((_, _) => { launches++; return null; });
        forge.Closed += (_, _) => shell!.Back();
        using var owner = shell = new PrimeShell(route => route == PrimeRoute.Forge ? forge
            : new PrimePanel(PrimeChrome.Stack(PrimeChrome.Title(route.ToString()), new PrimeButton("CONTINUE"))), () => { });
        shell.Start();
        var window = new Window { Width = 1280, Height = 720, Content = shell, ShowInTaskbar = false };
        try
        {
            window.Show(); shell.Router.Navigate(PrimeRoute.Forge); Drain(window);
            var header = shell.Header; var footer = shell.Footer;
            check(header.IsEffectivelyVisible && footer.IsEffectivelyVisible && header.Bounds.Height > 0 && footer.Bounds.Height > 0,
                "external Forge retains ordinary game navigation header and footer");
            var open = ControllerNav.Find(forge, "forge.open")!;
            open.Focus(); FocusNavigator.Key(open, Key.Down); Drain(window);
            check(FocusNavigator.Focused(shell) is { } focus && focus != open,
                "external Forge retains directional controller navigation");
            open.Focus(); FocusNavigator.Key(open, Key.E); Drain(window);
            check(shell.Router.Current == PrimeRoute.Offline && ReferenceEquals(header, shell.Header) && ReferenceEquals(footer, shell.Footer),
                "external Forge uses ordinary next-tab navigation and persistent chrome");
            FocusNavigator.Key(FocusNavigator.Ensure(shell)!, Key.Q); Drain(window);
            check(shell.Router.Current == PrimeRoute.Forge && ReferenceEquals(forge, shell.Workspaces.Content),
                "tab navigation restores the cached external Forge launch surface");
            open.Focus(); FocusNavigator.Key(open, Key.Escape); Drain(window);
            check(shell.Router.Current == PrimeRoute.Offline, "Escape leaves external Forge through router history");
            shell.Router.Navigate(PrimeRoute.News); shell.Router.Navigate(PrimeRoute.Forge); Drain(window);
            FocusNavigator.Key(ControllerNav.Find(forge, "forge.back")!, Key.Enter); Drain(window);
            check(shell.Router.Current == PrimeRoute.News && launches == 0,
                "external Forge Back restores the previous workspace without spawning a child");
            ControllerNav.Find(header, "prime.nav.Forge")!.Focus();
            FocusNavigator.Key(ControllerNav.Find(header, "prime.nav.Forge")!, Key.Enter); Drain(window);
            check(shell.Router.Current == PrimeRoute.Forge && header.IsEffectivelyVisible,
                "game header opens the external Forge surface after returning Home");
        }
        finally { window.Close(); }
    }

    internal static void QuickReplayControls(Action<bool, string> check)
    {
        WithPassiveReplay(check, session =>
        {
            var controls = new ReplayQuickControlsView();
            var window = new Window { Width = 960, Height = 720, Content = controls, ShowInTaskbar = false };
            try
            {
                window.Show(); Drain(window);
                string[] labels = controls.GetVisualDescendants().OfType<PrimeButton>().Select(button => button.Label).ToArray();
                foreach (string action in new[] { "PAUSE", "−10 SECONDS", "+10 SECONDS", "RESTART", "STEP FRAME", "PREVIOUS PLAYER", "NEXT PLAYER", "EDIT IN REPLAY STUDIO", "BACK TO THEATRE", "FULLSCREEN" })
                    check(labels.Contains(action), "quick replay exposes " + action);
                check(!labels.Any(label => label is "MARK IN" or "MARK OUT" or "SAVE CLIP" or "ADD KEYFRAME" or "UPDATE SELECTED" or "EXPORT"),
                    "game quick replay exposes transport without authoring controls");
                Click("PAUSE"); check(ReplayController.IsPaused, "quick replay Pause controls the canonical transport");
                Click("STEP FRAME"); check(session.Transport.FramesDue() == 1, "quick replay Step schedules one canonical paused frame");
                session.Transport.Begin();
                Click("+10 SECONDS");
                check(session.Transport.RequestedSeekTarget == Math.Min(ReplayController.CurrentFrame + 600, ReplayController.DurationFrames),
                    "quick replay forward seek uses the canonical bounded target");
                session.Transport.Begin();
                var slider = controls.GetVisualDescendants().OfType<Slider>().Single(); slider.Value = 600;
                check(session.Transport.RequestedSeekTarget == 600, "quick replay slider queues canonical seeking");
                session.Transport.Begin();
                var choices = controls.GetVisualDescendants().OfType<ComboBox>().ToArray();
                check(choices.Length == 2, "quick replay has playback speed and camera selectors");
                choices[0].SelectedIndex = 4;
                check(ReplayController.PlaybackRate == 4, "quick replay speed selects the canonical 4x transport rate");
                choices[1].SelectedIndex = 2;
                check(ReplayCamera.Mode == ReplayCameraMode.Free && !ReplayCamera.Director && !ReplayCamera.PlayTrack,
                    "quick replay camera selects free viewing without track authoring");
                int closed = 0, fullscreen = 0;
                controls.Closed += (_, _) => closed++; controls.ResumeRequested += (_, _) => fullscreen++;
                Click("BACK TO THEATRE"); Click("FULLSCREEN");
                check(closed == 1 && fullscreen == 1, "quick replay preserves Back and fullscreen owner callbacks");
                session.Transport.Begin();
                choices[0].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Source = choices[0], Key = Key.Space });
                check(!ReplayController.IsPaused, "quick replay selector input does not toggle playback");
                controls.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Source = controls, Key = Key.Space, KeyModifiers = KeyModifiers.Control });
                check(!ReplayController.IsPaused, "modified quick replay keys do not toggle playback");
                controls.Focus(); FocusNavigator.Key(controls, Key.Space);
                check(ReplayController.IsPaused, "quick replay unmodified Space controls transport");
                var timer = (DispatcherTimer)typeof(ReplayQuickControlsView).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controls)!;
                check(timer.IsEnabled, "attached quick replay owns its refresh clock");
                window.Content = null; Drain(window);
                check(!timer.IsEnabled, "detached quick replay stops its refresh clock");
                check(!NetSession.Active, "quick replay transport fixture creates no gameplay connection");
            }
            finally { window.Close(); }
            void Click(string label)
            {
                var button = controls.GetVisualDescendants().OfType<PrimeButton>().Single(button => button.Label == label);
                button.Focus(); FocusNavigator.Key(button, Key.Enter); Drain(window);
            }
        });
    }

    internal static void TheatreQuickSurface(Action<bool, string> check)
    {
        WithPassiveReplay(check, session =>
        {
            using var theatre = new TheatreWorkspace(manageStorage: false);
            var window = new Window { Width = 1280, Height = 720, Content = theatre, ShowInTaskbar = false };
            int closed = 0, fullscreen = 0;
            try
            {
                window.Show(); Drain(window);
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    theatre.ShowEditor(() => closed++, () => fullscreen++); Drain(window);
                    var controls = theatre.GetVisualDescendants().OfType<ReplayQuickControlsView>().Single();
                    var viewport = theatre.GetVisualDescendants().OfType<ReplayViewport>().Single();
                    check(theatre.EditorActive && controls.Bounds.Width > 0 && viewport.Bounds.Width > 0
                        && viewport.Bounds.Height > 0 && !theatre.GetVisualDescendants().Any(view => view.GetType().Name == "ReplayControlsView"),
                        "actual Theatre watch surface attaches quick controls and viewport without an authoring view");
                    check(!theatre.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("B ADD KEY") == true),
                        "Theatre viewing surface does not advertise removed game authoring keys");
                    foreach (string label in new[] { "BACK TO ARCHIVE", "FULLSCREEN PLAYBACK" })
                    {
                        var button = theatre.GetVisualDescendants().OfType<PrimeButton>().Single(b => b.Label == label);
                        button.Focus(); FocusNavigator.Key(button, Key.Enter); Drain(window);
                    }
                    check(closed == attempt + 1 && fullscreen == attempt + 1,
                        "actual Theatre viewing callbacks preserve archive Back and fullscreen ownership");
                    var timer = (DispatcherTimer)typeof(ReplayQuickControlsView).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controls)!;
                    check(timer.IsEnabled, "Theatre attached quick controls own their refresh clock");
                    theatre.CloseEditor(); Drain(window);
                    check(!theatre.EditorActive && !timer.IsEnabled
                        && !theatre.GetVisualDescendants().OfType<ReplayViewport>().Any(),
                        "Theatre return to library detaches viewport and stops the viewing refresh clock");
                }
                check(DemoPlayback.IsActive && !NetSession.Active,
                    "Theatre presentation open/close leaves transport ownership with the passive replay session");
            }
            finally { window.Close(); }
        });
    }

    internal static void ReplayInputOwnership(Action<bool, string> check)
    {
        PropertyInfo[] bindings = typeof(InputSettings).GetProperties(BindingFlags.Static | BindingFlags.Public)
            .Where(property => property.Name.StartsWith("Replay", StringComparison.Ordinal) && property.Name.EndsWith("Key", StringComparison.Ordinal)
                && property.PropertyType == typeof(Keys)).ToArray();
        object?[] original = bindings.Select(property => property.GetValue(null)).ToArray();
        var pauseOpen = typeof(PauseMenu).GetField("_open", BindingFlags.Static | BindingFlags.NonPublic)!;
        object originalPause = pauseOpen.GetValue(null)!;
        try
        {
            InputSettings.ResetReplayBindings();
            WithPassiveReplay(check, session =>
            {
                string replay = session.CurrentPath!;
                ReplayCamera.ClearBookmarks(); ReplayCamera.EnsureTrack();
                check(ReplayCamera.Track.Put(new(0, Vector3.Zero, Quaternion.Identity, 1.2f)) && ReplayCamera.Track.Save(replay),
                    "input ownership fixture has an actual canonical camera sidecar");
                byte[] camera = File.ReadAllBytes(replay + ".camera"), recording = File.ReadAllBytes(replay);
                DateTime modified = File.GetLastWriteTimeUtc(replay + ".camera");
                foreach (bool pausedUi in new[] { false, true })
                foreach (Keys key in new[] { Keys.B, Keys.Delete })
                {
                    pauseOpen.SetValue(null, pausedUi);
                    session.Transport.Begin(); ReplayCamera.BookmarkRequested = false;
                    InputSettings.ReplayPlayPauseKey = key;
                    check(ReplayInput.HandleKey(key, editor: pausedUi) && ReplayController.IsPaused && !ReplayCamera.BookmarkRequested,
                        "configured transport binding precedes former authoring key " + key + " with paused UI=" + pausedUi);
                    check(camera.SequenceEqual(File.ReadAllBytes(replay + ".camera")),
                        "configured " + key + " transport preserves authored camera bytes");
                }
                InputSettings.ResetReplayBindings();
                var interpolation = ReplayCamera.TrackInterpolation; var easing = ReplayCamera.TrackEase;
                bool constantSpeed = ReplayCamera.TrackConstantSpeed;
                foreach (bool pausedUi in new[] { false, true })
                    foreach (Keys key in new[] { Keys.B, Keys.Delete, InputSettings.ReplayConstantSpeedKey,
                        InputSettings.ReplayInterpolationKey, InputSettings.ReplayEasingKey })
                    {
                        pauseOpen.SetValue(null, pausedUi);
                        ReplayCamera.BookmarkRequested = false;
                        check(!ReplayInput.HandleKey(key, editor: pausedUi), "game replay rejects authoring key " + key + " with paused UI=" + pausedUi);
                        check(!ReplayCamera.BookmarkRequested && ReplayCamera.Track.Keys.Count == 1
                            && ReplayCamera.TrackInterpolation == interpolation && ReplayCamera.TrackEase == easing
                            && ReplayCamera.TrackConstantSpeed == constantSpeed
                            && camera.SequenceEqual(File.ReadAllBytes(replay + ".camera")) && File.GetLastWriteTimeUtc(replay + ".camera") == modified,
                            "game replay authoring key cannot schedule or persist camera edits: " + key);
                    }
                pauseOpen.SetValue(null, true); session.Transport.Begin();
                check(!ReplayInput.HandleKey(InputSettings.ReplayPlayPauseKey) && !ReplayController.IsPaused,
                    "ordinary game playback input remains blocked while its pause UI owns input");
                check(recording.SequenceEqual(File.ReadAllBytes(replay)) && !Directory.EnumerateFiles(Path.GetDirectoryName(replay)!, "*.tmp").Any()
                    && !NetSession.Active, "game replay input preserves immutable recording and private sidecar files without a network session");
            });
        }
        finally
        {
            pauseOpen.SetValue(null, originalPause);
            for (int i = 0; i < bindings.Length; i++) bindings[i].SetValue(null, original[i]);
        }
    }

    private static void WithPassiveReplay(Action<bool, string> check, Action<ReplayPlaybackSession> test)
    {
        check(!DemoPlayback.IsActive && !NetSession.Active, "game route fixture starts without active replay or gameplay ownership");
        string folder = Path.Combine(Path.GetTempPath(), "prime-game-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); string path = Path.Combine(folder, "transport.ppdemo");
        var prepared = typeof(DemoPlayback).GetField("_prepared", BindingFlags.Static | BindingFlags.NonPublic)!;
        object prior = prepared.GetValue(null)!;
        using var session = new ReplayPlaybackSession(new PassiveReplaySessionHost());
        try
        {
            var match = new MatchStatePacket { RoomKey = "MP1 SANCTORUS", NextRoomKey = "", Mode = (byte)GameMode.Battle, MatchId = 1, AuthorityEpoch = 1 };
            byte[] packet = new byte[1 + MatchStatePacket.Size]; packet[0] = (byte)PacketType.MatchState; match.Write(packet.AsSpan(1));
            using (var writer = new DemoWriter(path)) { writer.WriteRecord(0, packet); writer.WriteRecord(600, packet); writer.WriteRecord(1200, packet); }
            check(session.Join(path), "canonical passive transport fixture opens the real replay decoder: " + session.LastError);
            prepared.SetValue(null, session);
            ReplayCamera.ClearBookmarks(); ReplayCamera.Reset();
            test(session);
        }
        finally
        {
            prepared.SetValue(null, prior);
            ReplayCamera.ClearBookmarks(); ReplayCamera.Reset();
            session.Dispose(); Directory.Delete(folder, recursive: true);
        }
    }
    private static bool HasText(Control root, string text) => root.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == text);
    private static void Drain(Window window) { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
}
#endif
