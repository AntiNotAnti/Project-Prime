// The opt-in native Android entry reuses the real match renderer and session owners.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;

namespace MphRead.Droid
{
    [Activity(
        Label = "Project Prime",
        Theme = "@style/ProjectPrimeNative",
        MainLauncher = true,
        EnableOnBackInvokedCallback = true,
        ScreenOrientation = ScreenOrientation.SensorLandscape,
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize | ConfigChanges.ScreenLayout
            | ConfigChanges.UiMode | ConfigChanges.Density | ConfigChanges.KeyboardHidden)]
    public partial class MainActivity : Activity,
        Android.Hardware.Display.DisplayManager.IDisplayListener
    {
        internal static MainActivity? Instance { get; private set; }

        private ViewGroup? _content;
        private GameView? _gameView;
        private TouchOverlayView? _overlay;
        private TextView? _notice;
        private volatile Task _rendererStop = Task.CompletedTask;
        private Task _hunterStop = Task.CompletedTask;
        private long _matchGeneration;
        private volatile bool _destroyed;
        private volatile bool _renderingPreviews;
        private volatile bool _renderingHere;

        private volatile bool _stopPreviews;
        private readonly TouchControls _controls = new TouchControls();
        private ScreenOrientation _orientationBefore = ScreenOrientation.SensorLandscape;
        private NativeBackCallback? _nativeBackCallback;
        private Android.Window.IOnBackInvokedDispatcher? _nativeBackDispatcher;

        private sealed class NativeBackCallback(MainActivity activity) : Java.Lang.Object, Android.Window.IOnBackInvokedCallback
        {
            private readonly WeakReference<MainActivity> _activity = new(activity);
            public void OnBackInvoked()
            {
                if (_activity.TryGetTarget(out var owner) && !owner._destroyed)
                    owner.OnBackPressed();
            }
        }

        internal bool InMatch => _gameView != null;
        internal bool GraphicsOwnedByMatch => InMatch || _pending != null || !_rendererStop.IsCompleted || NativeRmlOwnsGraphics;
        internal bool HunterPreviewBlocked => GraphicsOwnedByMatch || _renderingHere || _destroyed;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            Instance = this;
            base.OnCreate(savedInstanceState);
            InstallSettingsArchiveServices();
            AndroidPerformance.Attach(this);
            GamepadBridge.Start(this);
            MphRead.Mods.Input.GamepadContexts.MenuVisible = true;
            _content = new FrameLayout(this);
            SetContentView(_content);
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                _nativeBackCallback = new(this);
                _nativeBackDispatcher = OnBackInvokedDispatcher;
                // Default priority lets the system dismiss its IME first.
                _nativeBackDispatcher?.RegisterOnBackInvokedCallback(
                    Android.Window.IOnBackInvokedDispatcher.PriorityDefault, _nativeBackCallback);
            }
            BeginNativeLauncher();
        }

        internal Task<int> RenderPreviews(IReadOnlyList<string> rooms, Action<string> report)
        {
            if (rooms.Count == 0 || _renderingPreviews)
            {
                return Task.FromResult(0);
            }
            _renderingPreviews = true;
            _stopPreviews = false;
            void Report(string line) => RunOnUiThread(() => report(line));
            RunOnUiThread(() => Window?.AddFlags(WindowManagerFlags.KeepScreenOn));
            return Task.Run(() =>
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    ThumbnailGenerator.EnsureCacheDirectory();
                    int written = PreviewWorkers.Run(this, rooms,
                        PreviewRun.Width, PreviewRun.Height, Report);
                    var left = new List<string>();
                    for (int i = 0; i < rooms.Count; i++)
                    {
                        if (!ThumbnailGenerator.Exists(rooms[i]))
                        {
                            left.Add(rooms[i]);
                        }
                    }
                    if (left.Count > 0)
                    {
                        Report($"[thumbnails] {left.Count} left to render here");
                        written += RenderHere(left, Report);
                    }
                    Report($"[thumbnails] {written}/{rooms.Count} in "
                        + $"{clock.Elapsed.TotalSeconds:0.0}s");
                    return written;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[thumbnails] the run failed: {ex}");
                    Report($"[thumbnails] {ex.Message}");
                    return 0;
                }
                finally
                {
                    _renderingPreviews = false;
                    _stopPreviews = false;
                    RunOnUiThread(() =>
                    {
                        if (!InMatch)
                        {
                            Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
                        }
                    });
                }
            });
        }

        private int RenderHere(IReadOnlyList<string> rooms, Action<string> report)
        {
            if (GraphicsOwnedByMatch || _destroyed)
            {
                report("[thumbnails] not while a match is running");
                return 0;
            }
            _renderingHere = true;
            _stopPreviews = false;
            var lifetime = new AndroidRenderLifetime();
            IDisposable? ownership = null;
            OffscreenGl? gl = null;
            Exception? retirementFailure = null;
            try
            {
                AndroidHunterShot.Current?.RetireAsync().GetAwaiter().GetResult();
                ownership = lifetime.Enter();
                if (_stopPreviews || GraphicsOwnedByMatch || _destroyed) return 0;
                AndroidMaps.EnsureBuilt(rooms, () => _stopPreviews);
                if (_stopPreviews)
                    return 0;
                gl = OffscreenGl.Create(PreviewRun.Width, PreviewRun.Height);
                return PreviewRun.Render(rooms, PreviewRun.Width, PreviewRun.Height, report,
                    () => _stopPreviews);
            }
            catch (Exception ex)
            {
                if (ex is AndroidGraphicsTeardownException) retirementFailure = ex;
                Console.WriteLine($"[thumbnails] the offscreen context failed: {ex}");
                report($"[thumbnails] {ex.Message}");
                return 0;
            }
            finally
            {
                try { gl?.Dispose(); }
                catch (Exception ex)
                {
                    retirementFailure = ex;
                    Console.WriteLine($"[thumbnails] native teardown failed; restart required: {ex}");
                }
                lifetime.Complete(ownership, retirementFailure);
                _renderingHere = false;
                _stopPreviews = false;
            }
        }

        private int _lastRotation = -1;

        private Android.Hardware.Display.DisplayManager? _displays;

        private int CurrentRotation()
        {
            try
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(30))
                {
                    return (int?)Display?.Rotation ?? -1;
                }
#pragma warning disable CA1422 // the pre-30 way, for the devices that need it
                return (int?)WindowManager?.DefaultDisplay?.Rotation ?? -1;
#pragma warning restore CA1422
            }
            catch (Exception)
            {
                return -1;
            }
        }

        public void OnDisplayAdded(int displayId)
        {
        }

        public void OnDisplayRemoved(int displayId)
        {
        }

        public void OnDisplayChanged(int displayId)
        {
            int rotation = CurrentRotation();
            if (rotation < 0 || rotation == _lastRotation)
            {
                return;
            }
            int before = _lastRotation;
            _lastRotation = rotation;
            if (before < 0)
            {
                return;
            }
            Console.WriteLine($"[android] display rotation {before} -> {rotation} at "
                + $"{ContentSize.Width}x{ContentSize.Height}");
            AfterRotation();
            _content?.PostDelayed(AfterRotation, SettleMs);
        }

        private void AfterRotation()
        {
            _controls.ReleaseEverything();
            _overlay?.Refresh();
            _content?.RequestLayout();
            _content?.Invalidate();
            Window?.DecorView?.RequestLayout();
            GoImmersive(true);
        }

        public override void OnConfigurationChanged(Android.Content.Res.Configuration newConfig)
        {
            base.OnConfigurationChanged(newConfig);
            OnDisplayChanged(0);
        }

        protected override void OnPause()
        {
            if (_displays != null)
            {
                _displays.UnregisterDisplayListener(this);
                _displays = null;
            }
            _controls.ReleaseEverything();
            _overlay?.Invalidate();
            AndroidPerformance.SetForeground(false);
            MphRead.Mods.Launcher.SocialPartyClient.Suspend();
            MphRead.Mods.Launcher.SocialInviteClient.Suspend();
            MphRead.Mods.Launcher.SocialPresenceClient.Suspend();
            GamepadBridge.Clear();
            _gameView?.OnPause();
            PauseNativeLauncher(true);
            base.OnPause();
        }

        protected override void OnResume()
        {
            base.OnResume();
            GoImmersive(true);
            AndroidPerformance.SetForeground(true);
            MphRead.Mods.Launcher.SocialPresenceClient.Resume();
            MphRead.Mods.Launcher.SocialInviteClient.Resume();
            MphRead.Mods.Launcher.SocialPartyClient.Resume();
            AndroidPerformance.RefreshDisplayRate();
            _lastRotation = CurrentRotation();
            if (_displays == null
                && GetSystemService(DisplayService) is Android.Hardware.Display.DisplayManager manager)
            {
                _displays = manager;
                manager.RegisterDisplayListener(this, null);
            }
            _gameView?.OnResume();
            PauseNativeLauncher(false);
        }

        public override bool DispatchKeyEvent(KeyEvent? e)
        {
            bool down = e?.Action == KeyEventActions.Down;
            if (e != null && (down || e.Action == KeyEventActions.Up)
                && GamepadBridge.HandleKey(e.KeyCode, e, down))
            {
                if (down)
                {
                    _controls.NotePadActivity();
                }
                return true;
            }
            return base.DispatchKeyEvent(e);
        }

        public override bool DispatchTouchEvent(MotionEvent? e)
        {
            if (e?.ActionMasked == MotionEventActions.Down)
                MphRead.Mods.Input.InputSourceTracker.Note(MphRead.Mods.Input.InputSource.Touch);
            return base.DispatchTouchEvent(e);
        }

        public override bool DispatchGenericMotionEvent(MotionEvent? e)
        {
            if (GamepadBridge.HandleMotion(e))
            {
                if (MphRead.Mods.Input.GamepadInput.InUse)
                {
                    _controls.NotePadActivity();
                }
                return true;
            }
            return base.DispatchGenericMotionEvent(e);
        }

        public override void OnWindowFocusChanged(bool hasFocus)
        {
            base.OnWindowFocusChanged(hasFocus);
            MphRead.Mods.Input.GamepadContexts.Focused = hasFocus;
            if (!hasFocus) GamepadBridge.Clear();
            if (hasFocus)
            {
                GoImmersive(true);
            }
        }

        protected override void OnDestroy()
        {
            _destroyed = true;
            if (OperatingSystem.IsAndroidVersionAtLeast(33) && _nativeBackCallback != null)
                _nativeBackDispatcher?.UnregisterOnBackInvokedCallback(_nativeBackCallback);
            _nativeBackDispatcher = null;
            _nativeBackCallback?.Dispose();
            _nativeBackCallback = null;
            _matchGeneration++;
            _pending = null;
            _stopPreviews = true;
            PreviewWorkers.Stop(this);
            MphRead.Mods.Launcher.SocialPartyClient.Stop();
            MphRead.Mods.Launcher.SocialInviteClient.Stop();
            MphRead.Mods.Launcher.SocialPresenceClient.Stop();
            DisposeSettingsArchiveServices();
            if (Instance == this)
            {
                Instance = null;
            }
            _gameView?.StopAsync();
            DestroyNativeLauncher();
            _hunterStop = AndroidHunterShot.Current?.RetireAsync() ?? Task.CompletedTask;
            GamepadBridge.Stop();
            base.OnDestroy();
        }

#pragma warning disable CA1422
        public override void OnBackPressed()
        {
            if (InMatch)
            {
                if (Mods.KillCam.RequestSkip()) return;
#if MPHREAD_RMLUI_ANDROID
                if (_gameView?.NativeUiVisible == true) { _gameView.BackNativeMenu(); return; }
#endif
                TogglePauseMenu();
                return;
            }
            if (_pending != null)
            {
                CancelPending("the player went back");
                return;
            }
            if (NativeRmlOwnsGraphics && NativeLauncherBack()) return;
            if (NativeLauncherBack()) return;
            Finish();
        }
#pragma warning restore CA1422

        private bool _spectateOnLoad;
        private bool _replayEditorOnLoad;
        internal void StartMatch(LaunchPlan plan)
        {
            if (_content == null || InMatch || _destroyed)
            {
                return;
            }
            if (_pending != null)
            {
                Console.WriteLine("[android] a match is already starting; ignoring");
                return;
            }
            RetireNativeLauncherForMatch();
            _spectateOnLoad = plan.Spectate;
            _replayEditorOnLoad = plan.Kind == LaunchKind.Demo;
            if (_renderingPreviews)
            {
                PreviewWorkers.Stop(this);
                _stopPreviews = true;
            }
            if (_renderingHere)
            {
                Console.WriteLine("[android] stopping the preview run: a match was asked for");
                _stopPreviews = true;
            }
            _hunterStop = AndroidHunterShot.Current?.RetireAsync() ?? Task.CompletedTask;
            var input = new AndroidInput();
            _controls.ReleaseEverything();
            _controls.ReloadSettings();
            _controls.SetSpectator(spectating: false, freeCamera: false);
            _orientationBefore = RequestedOrientation;
            RequestedOrientation = ScreenOrientation.SensorLandscape;
            Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
            GoImmersive(true);
            _pending = (plan, input);
            _waitingSince = SystemClock.UptimeMillis();
            if (_endPanelTick == null)
            {
                _endPanelTick = TickEndPanel;
                _content.PostDelayed(_endPanelTick, 100);
            }
            _lastSize = ContentSize;
            _sizeSettledAt = _waitingSince;
            ShowNotice(plan.RoomKey.Length > 0
                ? $"Loading {plan.RoomKey}..."
                : "Loading your game...");
            Console.WriteLine($"[android] starting {plan.RoomKey} from "
                + $"{_lastSize.Width}x{_lastSize.Height}");
            WaitForSteadyWindow();
        }

        private (LaunchPlan Plan, AndroidInput Input)? _pending;
        private (int Width, int Height) _lastSize;
        private long _waitingSince;
        private long _sizeSettledAt;

        private const int SettleMs = 250;

        private const int RotateMs = 3000;

        private const int GiveUpMs = 8000;

        private (int Width, int Height) ContentSize =>
            _content == null ? (0, 0) : (_content.Width, _content.Height);

        private void WaitForSteadyWindow()
        {
            if (_pending == null || _content == null || InMatch || _destroyed)
            {
                return;
            }
            if (_renderingHere || !_rendererStop.IsCompleted || !_hunterStop.IsCompleted)
            {
                _waitingSince = SystemClock.UptimeMillis();
                _sizeSettledAt = _waitingSince;
                _content.PostDelayed(WaitForSteadyWindow, 50);
                return;
            }
            if (_rendererStop.IsFaulted || _hunterStop.IsFaulted)
            {
                CancelPending("the previous renderer could not finish shutting down");
                return;
            }
            long now = SystemClock.UptimeMillis();
            (int Width, int Height) size = ContentSize;
            if (size != _lastSize)
            {
                _lastSize = size;
                _sizeSettledAt = now;
            }
            bool haveSize = size.Width > 0 && size.Height > 0;
            bool landscape = size.Width > size.Height;
            bool steady = haveSize && now - _sizeSettledAt >= SettleMs;
            long waited = now - _waitingSince;
            if (steady && landscape)
            {
                StartPending(null);
                return;
            }
            if (haveSize && waited >= RotateMs)
            {
                StartPending(landscape
                    ? $"the window was still moving after {waited} ms"
                    : $"the display did not turn landscape in {waited} ms");
                return;
            }
            if (waited >= GiveUpMs)
            {
                CancelPending($"the window never took a size ({size.Width}x{size.Height})");
                return;
            }
            _content.PostDelayed(WaitForSteadyWindow, 50);
        }

        private void CancelPending(string reason)
        {
            if (_pending == null)
            {
                return;
            }
            Console.WriteLine($"[android] the match was not started: {reason}");
            _pending = null;
            HideNotice();
            _controls.ReleaseEverything();
            MphRead.Mods.Input.GamepadContexts.MenuVisible = true;
            ResetLauncher();
            Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
            GoImmersive(true);
            RequestedOrientation = _orientationBefore;
            RestoreNativeLauncher(false);
            Toast.MakeText(this, $"Could not start the match: {reason}",
                ToastLength.Long)?.Show();
        }

        private void StartPending(string? note)
        {
            if (_pending == null || InMatch)
            {
                return;
            }
            (LaunchPlan plan, AndroidInput input) = _pending.Value;
            _pending = null;
            if (note != null)
            {
                Console.WriteLine($"[android] starting the match anyway: {note}");
            }
            Console.WriteLine($"[android] building the match at "
                + $"{ContentSize.Width}x{ContentSize.Height}");
            MphRead.Mods.Input.GamepadContexts.MenuVisible = false;
            RequestedOrientation = ScreenOrientation.Locked;
            BeginMatch(plan, input);
        }

        private void ShowSoftKeyboard(bool show)
        {
            if (_gameView == null
                || GetSystemService(InputMethodService) is not InputMethodManager ime)
            {
                return;
            }
            if (show)
            {
                _gameView.RequestFocus();
                ime.ShowSoftInput(_gameView, ShowFlags.Implicit);
            }
            else
            {
                ime.HideSoftInputFromWindow(_gameView.WindowToken, HideSoftInputFlags.None);
            }
        }

        private void BeginMatch(LaunchPlan plan, AndroidInput input)
        {
            if (_content == null)
            {
                return;
            }
            long generation = ++_matchGeneration;
            AndroidPerformance.SetMatchActive(true);
            OfflineRematch.StartNext = selected =>
            {
                if (NetSession.Active || !OfflineRematch.TryPlan(plan, selected, out var next)) return false;
                RunOnUiThread(() =>
                {
                    if (_matchGeneration != generation || _destroyed) return;
                    EndMatch();
                    StartMatch(next);
                });
                return true;
            };
            _gameView = new GameView(this, _controls, input,
                (i, size, sceneCreated, cancellation) => AndroidMatch.Build(i, size, plan,
                    () => RunOnUiThread(() =>
                    { if (_matchGeneration == generation && !_destroyed) EndMatch(); }),
                    cancellation, sceneCreated),
                keepSession => RunOnUiThread(() =>
                {
                    if (_matchGeneration != generation || _destroyed) return;
                    if (keepSession) EndMatchToLobby();
                    else EndMatch();
                }),
                () => RunOnUiThread(() =>
                { if (_matchGeneration == generation && !_destroyed) MatchLoaded(); }),
                error => RunOnUiThread(() =>
                { if (_matchGeneration == generation && !_destroyed) FailMatch(error); }),
                () => RunOnUiThread(() =>
                { if (_matchGeneration == generation && !_destroyed) TogglePauseMenu(); }),
                show => RunOnUiThread(() =>
                { if (_matchGeneration == generation && !_destroyed) ShowSoftKeyboard(show); }));
            _gameView.SetZOrderMediaOverlay(true);
            _overlay = new TouchOverlayView(this, _controls);
#if MPHREAD_RMLUI_ANDROID
            _gameView.ConfigureNativeResults((training, action) =>
            {
                if (_matchGeneration != generation || _destroyed) return;
                if (action == MphRead.Mods.Launcher.Core.AimResultsAction.Retry)
                {
                    var next = AimTrainerLaunch.Create(training.Definition.Retry(), training.Plan.Hunter, LauncherPrefs.LastColor);
                    EndMatch(); StartMatch(next);
                }
                else
                {
                    EndMatch();
                    _rmlLauncher?.Enqueue(session => session.OpenTraining(action == MphRead.Mods.Launcher.Core.AimResultsAction.ChangeDrill));
                }
            });
            _overlay.NativeVisible = () => _gameView?.NativeUiVisible == true;
            _overlay.NativeTouch = e => _gameView?.NativeTouch(e) == true;
            _overlay.NativeHover = e => _gameView?.NativeHover(e) == true;
#endif
            _content.AddView(_gameView);
            _content.AddView(_overlay);
            ShowNotice($"Loading {plan.RoomKey}...");
            _notice?.BringToFront();
        }

        private void ShowNotice(string text)
        {
            if (_content == null)
            {
                return;
            }
            if (_notice != null)
            {
                _notice.Text = text;
                return;
            }
            _notice = new TextView(this)
            {
                Text = text,
                TextAlignment = Android.Views.TextAlignment.Center
            };
            _notice.SetTextColor(Android.Graphics.Color.Argb(230, 230, 234, 242));
            _notice.SetBackgroundColor(Android.Graphics.Color.Argb(255, 10, 12, 16));
            _notice.Gravity = GravityFlags.Center;
            _content.AddView(_notice, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        }

        private void MatchLoaded()
        {
            if (_spectateOnLoad) { _spectateOnLoad = false; _gameView?.RequestSpectate(); }
            bool openReplayEditor = _replayEditorOnLoad;
            _replayEditorOnLoad = false;
            RequestedOrientation = ScreenOrientation.SensorLandscape;
            ReleaseLoadedMatch();
            if (openReplayEditor)
            {
                ShowReplayEditor();
            }
        }

        private void ReleaseLoadedMatch()
        {
            if (!InMatch)
            {
                return;
            }
            if (!MphRead.Mods.Network.NetSession.FreezeGameplay)
            {
                HideNotice();
                return;
            }
            double remaining = MphRead.Mods.Network.NetSession.StartCountdownRemainingSeconds;
            ShowNotice(remaining > 0
                ? $"Match starts in {Math.Max(1, (int)Math.Ceiling(remaining))}..."
                : "Waiting for players...");
            _notice?.BringToFront();
            _content?.PostDelayed(ReleaseLoadedMatch, 50);
        }

        private void HideNotice()
        {
            if (_notice != null && _content != null)
            {
                _content.RemoveView(_notice);
                _notice = null;
            }
        }

        private void FailMatch(string message)
        {
            if (_notice != null)
            {
                _notice.Text = message;
            }
            else
            {
                Toast.MakeText(this, message, ToastLength.Long)?.Show();
            }
            long generation = _matchGeneration;
            _content?.PostDelayed(() => { if (generation == _matchGeneration && !_destroyed) EndMatch(); }, 4000);
        }

        private bool _pauseMenuOpen;

        private Action? _endPanelTick;

        private void TickEndPanel()
        {
            if (!InMatch && _pending == null) { _endPanelTick = null; return; }
            MphRead.Mods.Input.GamepadContexts.MenuVisible = _pauseMenuOpen || _gameView?.NativeUiVisible == true;
            if (_gameView?.NativeUiVisible == true) _controls.ReleaseEverything();
            if (_endPanelTick != null) _content?.PostDelayed(_endPanelTick, 100);
        }

        private void ShowReplayEditor()
        {
            if (!InMatch || !DemoPlayback.IsActive) return;
            _pauseMenuOpen = false;
            _controls.ReleaseEverything();
            _gameView?.OpenNativeReplay(EndMatch);
            _overlay?.Invalidate();
            MphRead.Mods.Input.GamepadContexts.MenuVisible = true;
        }

        internal void TogglePauseMenu()
        {
            if (Mods.KillCam.RequestSkip() || !InMatch) return;
            if (_pauseMenuOpen) { ClosePauseMenu(); return; }
            _pauseMenuOpen = true;
            _controls.ReleaseEverything();
            MphRead.Mods.Input.GamepadContexts.MenuVisible = true;
            _gameView?.OpenNativeMenu(ClosePauseMenu, () => { }, ShowReplayEditor, EndMatch, Finish);
            _overlay?.Invalidate();
            GoImmersive(true);
        }

        private void ClosePauseMenu()
        {
            if (!_pauseMenuOpen) return;
            _pauseMenuOpen = false;
            _gameView?.CloseNativeMenu();
            MphRead.Mods.Input.GamepadContexts.MenuVisible = _gameView?.NativeUiVisible == true;
            _controls.ReleaseEverything();
            _controls.ReloadSettings();
            _overlay?.Invalidate();
            GoImmersive(true);
        }

        internal void EndMatch() => EndMatchCore(false);
        internal void EndMatchToLobby() => EndMatchCore(true);
        private void EndMatchCore(bool keepSession)
        {
            if (!_rendererStop.IsCompleted && _gameView == null) return;
            _rendererStop = EndMatchCoreAsync(keepSession);
        }

        private async Task EndMatchCoreAsync(bool keepSession)
        {
            if (_content == null)
            {
                return;
            }
            _matchGeneration++;
            OfflineRematch.StartNext = null;
            _pending = null;
            _pauseMenuOpen = false;
            _replayEditorOnLoad = false;
            HideNotice();
            _endPanelTick = null;
            if (_overlay != null)
            {
                _content.RemoveView(_overlay);
                _overlay = null;
            }
            bool retiredRenderer = _gameView != null;
            if (_gameView != null)
            {
                GameView retiring = _gameView;
                _gameView = null;
                Task stopped = retiring.StopAsync(keepSession);
                try { await stopped.ConfigureAwait(false); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[android] renderer retirement failed; restart required: {ex}");
                    await CompleteOnUiThread(() =>
                    {
                        if (_destroyed) return;
                        _content?.RemoveView(retiring);
                        ShowNotice("Renderer shutdown failed. Restart Project Prime before starting another match.");
                    });
                    throw;
                }
                await CompleteOnUiThread(() =>
                {
                    if (_destroyed) return;
                    _content?.RemoveView(retiring);
                    RestoreAfterMatch(keepSession, retiredRenderer: true);
                });
                return;
            }
            RestoreAfterMatch(keepSession, retiredRenderer);
        }

        private Task CompleteOnUiThread(Action action)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                RunOnUiThread(() =>
                {
                    try { action(); completion.TrySetResult(); }
                    catch (Exception ex) { completion.TrySetException(ex); }
                });
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            return completion.Task;
        }

        private void RestoreAfterMatch(bool keepSession, bool retiredRenderer)
        {
            if (_destroyed) return;
            AndroidPerformance.SetMatchActive(false);
            MphRead.Mods.Input.GamepadContexts.MenuVisible = true;
            _controls.ReleaseEverything();
            _controls.SetSpectator(spectating: false, freeCamera: false);
            if (keepSession)
            {
                if (!retiredRenderer) NetSession.ResetMatchState();
            }
            else
            {
                if (!retiredRenderer) { NetSession.Stop(); NetHostSession.Stop(); }
                ResetLauncher();
            }
            if (!retiredRenderer) DemoPlayback.Stop();
            Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
            GoImmersive(true);
            RequestedOrientation = _orientationBefore;
            RestoreNativeLauncher(keepSession);
        }

        private static void ResetLauncher()
        {
            MenuSettings settings = GameState.LoadSettings();
            GameSettings.Apply(settings);
        }

        private void GoImmersive(bool immersive)
        {
            Window? window = Window;
            if (window == null)
            {
                return;
            }
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                IWindowInsetsController? controller = window.InsetsController;
                if (controller != null)
                {
                    if (immersive)
                    {
                        controller.SystemBarsBehavior =
                            (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
                        controller.Hide(WindowInsets.Type.SystemBars());
                    }
                    else
                    {
                        controller.Show(WindowInsets.Type.SystemBars());
                    }
                }
                return;
            }
#pragma warning disable CA1422, CS0618 // the pre-30 way, for the devices that need it
            window.DecorView.SystemUiVisibility = immersive
                ? (StatusBarVisibility)(SystemUiFlags.ImmersiveSticky | SystemUiFlags.HideNavigation
                    | SystemUiFlags.Fullscreen | SystemUiFlags.LayoutStable)
                : StatusBarVisibility.Visible;
#pragma warning restore CA1422, CS0618
        }
    }
}
