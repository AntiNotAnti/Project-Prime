#if MPHREAD_RMLUI_POC
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// Opt-in RmlUi 6.3 proof of concept for the front screen.
    ///
    /// This deliberately does not replace any authoritative launcher service.
    /// It proves the rendering/input seam only: RmlUi draws directly into the
    /// shell's OpenGL back buffer while HubState supplies the same player/hunter
    /// snapshot the Avalonia shell consumes. Actions hand back to the existing
    /// Prime shell until a destination is migrated for real.
    /// </summary>
    internal static class RmlUiPrototype
    {
        private const string NativeLibraryName = "ProjectPrime.RmlUi.Native";
        private const int ActionBufferSize = 512;
        private static readonly byte[] _actionBuffer = new byte[ActionBufferSize];
        private static readonly Queue<string> _commands = new();
        private static readonly GamepadUiRouter _gamepad = new();
        private static bool _active;
        private static bool _failed;
        private static int _width;
        private static int _height;
        private static float _density = 1;
        private static float _pointerScaleX = 1;
        private static float _pointerScaleY = 1;
        private static long _nextStateRefresh;
        private static int _renderSamples;
        private static double _renderMs;
        private static int _captureFrames;
        private static string? _captureDirectory;
        private static Task<int>? _stageRefresh;
        private static CancellationTokenSource? _stageRefreshCancel;
        private static string _stageRefreshRoom = "";
        private static bool _diagnosticsVisible;
        private static CancellationTokenSource? _socialCancel;
        private static Task<SocialSnapshot>? _socialLoad;
        private static Task<SocialMutationResult>? _socialMutation;
        private static Task<SocialLookupResult>? _socialLookup;
        private static Task<SocialInviteMutationResult>? _socialInviteMutation;
        private static Task<SocialPartyMutationResult>? _socialPartyMutation;
        private static Task<PartyGameInviteResult>? _socialPartyGameInvite;
        private static Task<SocialJoinResolution>? _socialJoin;
        private static readonly Queue<SocialJoinResolution> _verifiedSocialJoins = new();
        private static string _socialLookupQuery = "";
        private static SocialSnapshot? _socialSnapshot;
        private static SocialPlayer? _socialLookupPlayer;
        private static string _socialPendingAction = "";
        private static string _socialSearch = "";
        private static string _socialFingerprint = "";
        private static int _socialTab;
        private static long _nextSocialReload;
        private static bool _socialDrawerOpen;
        private static bool _socialFixture;

        static RmlUiPrototype()
        {
            _gamepad.Action += HandleGamepad;
        }

        public static bool Requested => Array.Exists(Environment.GetCommandLineArgs(),
            value => value.Equals("-rmluipoc", StringComparison.OrdinalIgnoreCase)
                || value.Equals("-rmluipocshot", StringComparison.OrdinalIgnoreCase));

        public static bool CaptureRequested => CaptureDirectory() != null;
        public static bool Active => _active;
        public static bool Failed => _failed;

        public static bool TryActivate(RenderWindow window)
        {
            if (!Requested || _active || _failed)
                return _active;

            if (ModernGraphicsCompat.Active || GraphicsBackendPolicy.ModernGameplayRequested)
            {
                Mods.DebugLog.Line("rmlui", "POC requires the compatibility OpenGL renderer; falling back to Avalonia");
                return false;
            }

            string root = Path.Combine(AppContext.BaseDirectory, "rmlui");
            string document = Path.Combine(root, "prime_home.rml");
            if (!File.Exists(document))
            {
                Mods.DebugLog.Line("rmlui", $"POC assets are missing at {document}; falling back to Avalonia");
                _failed = true;
                return false;
            }

            try
            {
                if (CaptureSize() is Vector2i requested)
                {
                    window.ClientSize = requested;
                    // The diagnostic needs the framebuffer size produced by
                    // the resize, not the previous frame's cached value.
                    NativeWindow.ProcessWindowEvents(false);
                }
                ReadSize(window, out int width, out int height, out float density);
                int ok = NativeInitialize(width, height, density, root);
                if (ok == 0)
                {
                    Mods.DebugLog.Line("rmlui", "native RmlUi host refused initialization; falling back to Avalonia");
                    _failed = true;
                    return false;
                }

                _width = width;
                _height = height;
                _density = density;
                _active = true;
                _nextStateRefresh = 0;
                _captureFrames = 0;
                _captureDirectory = CaptureDirectory();
                _diagnosticsVisible = false;
                _socialCancel?.Cancel();
                _socialCancel?.Dispose();
                _socialCancel = new CancellationTokenSource();
                _socialLoad = null;
                _socialMutation = null;
                _socialLookup = null;
                _socialInviteMutation = null;
                _socialPartyMutation = null;
                _socialPartyGameInvite = null;
                _socialJoin = null;
                ClearVerifiedSocialJoins();
                _socialLookupQuery = "";
                _socialSnapshot = null;
                _socialLookupPlayer = null;
                _socialPendingAction = "";
                _socialSearch = "";
                _socialFingerprint = "";
                _socialTab = 0;
                _nextSocialReload = 0;
                _socialDrawerOpen = false;
                _socialFixture = SocialCaptureRequested();
                GamepadContexts.MenuVisible = true;
                _gamepad.Reset();

                LauncherBackdrop.Set(LauncherBackdropScene.Multiplayer);
                HubSnapshot snapshot = HubState.Capture();
                ConfigureHunter(snapshot);
                RefreshState(snapshot, force: true);
                BeginMenuStageRefresh(snapshot);
                if (_socialFixture)
                    SeedSocialCapture();
                else
                    BeginSocialLoad(force: true);
                Mods.DebugLog.Line("rmlui", $"RmlUi 6.3 POC active at {width}x{height} ({density:0.##}x density)");
                return true;
            }
            catch (DllNotFoundException ex)
            {
                Fail("native bridge not found", ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                Fail("native bridge ABI mismatch", ex);
            }
            catch (Exception ex)
            {
                Fail("initialization failed", ex);
            }
            return false;
        }

        public static void Tick(RenderWindow window)
        {
            if (!_active)
                return;
            try
            {
                ReadSize(window, out int width, out int height, out float density);
                if (width != _width || height != _height || Math.Abs(density - _density) > 0.01f)
                {
                    _width = width;
                    _height = height;
                    _density = density;
                    NativeResize(width, height, density);
                }

                long now = Environment.TickCount64;
                if (now >= _nextStateRefresh)
                {
                    HubSnapshot snapshot = HubState.Capture();
                    ConfigureHunter(snapshot);
                    RefreshState(snapshot, force: false);
                    if (!_socialFixture)
                    {
                        PollSocialWork();
                        if (now >= _nextSocialReload)
                            BeginSocialLoad(force: false);
                        RefreshSocialUi();
                    }
                    _nextStateRefresh = now + 1000;
                }

                Mods.Input.GamepadDesktop.Poll();
                if (!GamepadContexts.Focused)
                    _gamepad.Reset();
                else
                    _gamepad.Update(GamepadManager.Snapshot, GamepadContext.Menu, now);

                PollMenuStageRefresh();
                NativeUpdate();
                DrainActions();
            }
            catch (Exception ex)
            {
                Fail("update failed", ex);
            }
        }

        public static void Render(int width, int height)
        {
            if (!_active || width <= 0 || height <= 0)
                return;
            try
            {
                long started = Stopwatch.GetTimestamp();
                NativeRender(width, height);
                double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                _renderMs += elapsed;
                _renderSamples++;
                if (_renderSamples >= 30)
                {
                    SetText("ui_cost", $"RMLUI DRAW {_renderMs / _renderSamples:0.00} MS AVG // NO UI BITMAP UPLOAD");
                    _renderSamples = 0;
                    _renderMs = 0;
                }
            }
            catch (Exception ex)
            {
                Fail("render failed", ex);
            }
        }

        public static void AfterDraw(RenderWindow window)
        {
            if (!_active || _captureDirectory == null)
                return;

            _captureFrames++;
            if (_captureFrames < 30)
                return;

            string directory = Path.GetFullPath(_captureDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "rmlui-home.png");
            bool saved = Mods.ScreenCapture.SaveWindow(
                window.FramebufferSize.X, window.FramebufferSize.Y, path);
            if (saved)
            {
                Mods.Render.FinalCompositeCapture.WriteEvidence(path,
                    window.FramebufferSize.X, window.FramebufferSize.Y,
                    "RmlUi proof final composite: cinematic ground + engine Hunter + direct RmlUi overlay");
                Mods.DebugLog.Line("rmlui", $"proof capture: {path}");
            }
            else
            {
                Mods.DebugLog.Line("rmlui", "proof capture failed");
                Environment.ExitCode = 1;
            }

            _captureDirectory = null;
            Shell.RequestQuit();
        }

        public static bool TryTakeCommand(out string command)
        {
            if (_commands.Count > 0)
            {
                command = _commands.Dequeue();
                return true;
            }
            command = string.Empty;
            return false;
        }

        public static bool TryTakeSocialJoin(out SocialJoinResolution resolution)
        {
            if (_verifiedSocialJoins.Count > 0)
            {
                resolution = _verifiedSocialJoins.Dequeue();
                return true;
            }
            resolution = default;
            return false;
        }

        private static void ClearVerifiedSocialJoins()
        {
            while (_verifiedSocialJoins.Count > 0)
            {
                SocialJoinResolution pending = _verifiedSocialJoins.Dequeue();
                pending.PartyAdmission?.Dispose();
            }
        }

        public static void PointerMoved(double x, double y)
        {
            if (_active)
            {
                NativeMouseMove(
                    (int)Math.Round(x * _pointerScaleX),
                    (int)Math.Round(y * _pointerScaleY), 0);
            }
        }

        public static void PointerButton(MouseButton button, double x, double y, bool down)
        {
            if (!_active) return;
            PointerMoved(x, y);
            int translated = button switch
            {
                MouseButton.Button2 => 1,
                MouseButton.Button3 => 2,
                _ => 0
            };
            NativeMouseButton(translated, down ? 1 : 0, 0);
        }

        public static void PointerWheel(double deltaX, double deltaY)
        {
            if (_active) NativeMouseWheel((float)deltaY, 0);
        }

        public static void KeyDown(KeyboardKeyEventArgs e)
        {
            if (!_active) return;
            if (e.Key == Keys.F10)
            {
                _diagnosticsVisible = !_diagnosticsVisible;
                SetBool("diagnostics_visible", _diagnosticsVisible);
                return;
            }
            if (e.Key == Keys.Escape)
            {
                // Give RmlUi overlays first refusal before handing Back to the
                // production shell. This keeps Escape/controller B inside the
                // social drawer and its context sheet.
                if (NativeBack() != 0)
                    return;
                _commands.Enqueue("route:news");
                return;
            }
            int key = TranslateKey(e.Key);
            if (key != 0) NativeKey(key, 1, Modifiers(e));
        }

        public static void KeyUp(KeyboardKeyEventArgs e)
        {
            if (!_active) return;
            int key = TranslateKey(e.Key);
            if (key != 0) NativeKey(key, 0, Modifiers(e));
        }

        public static void TextInput(string text)
        {
            if (!_active || string.IsNullOrEmpty(text)) return;
            foreach (Rune rune in text.EnumerateRunes())
                NativeText((uint)rune.Value);
        }

        public static void Shutdown()
        {
            if (!_active)
                return;
            try { NativeShutdown(); }
            catch (Exception ex) { Mods.DebugLog.Exception("rmlui", ex); }
            _active = false;
            _commands.Clear();
            _gamepad.Reset();
            _captureDirectory = null;
            _captureFrames = 0;
            _stageRefreshCancel?.Cancel();
            _stageRefreshCancel = null;
            _stageRefresh = null;
            _stageRefreshRoom = "";
            _socialCancel?.Cancel();
            _socialCancel?.Dispose();
            _socialCancel = null;
            _socialLoad = null;
            _socialMutation = null;
            _socialLookup = null;
            _socialInviteMutation = null;
            _socialPartyMutation = null;
            _socialPartyGameInvite = null;
            _socialJoin = null;
            ClearVerifiedSocialJoins();
            _socialSnapshot = null;
            _socialLookupPlayer = null;
            _socialPendingAction = "";
            _socialSearch = "";
            _socialFingerprint = "";
            _socialDrawerOpen = false;
            _socialFixture = false;
            LauncherHunter.Reset();
            GamepadContexts.MenuVisible = false;
            Mods.DebugLog.Line("rmlui", "RmlUi POC shut down");
        }

        private static void RefreshState(HubSnapshot snapshot, bool force)
        {
            SetText("player_name", snapshot.PlayerName.ToUpperInvariant());
            SetText("hunter_name", snapshot.DisplayHunter.ToString().ToUpperInvariant());
            SetText("profile_state", snapshot.GameFilesReady ? "LOCAL PROFILE // GAME DATA READY" : "LOCAL PROFILE // SETUP REQUIRED");
            SetText("game_data_state", snapshot.GameFilesReady ? "GAME DATA READY" : "GAME DATA NOT CONFIGURED");
            SetText("build_version", Update.BuildVersion.Display);
            SetBool("reduce_motion", LauncherPrefs.ReduceMotion);
            SetBool("diagnostics_visible", _diagnosticsVisible);
            if (force)
            {
                SetText("renderer_name", "OPENGL // RMLUI 6.3");
                SetText("ui_cost", "RMLUI DIRECT GPU OVERLAY // MEASURING");
            }
        }

        private static void ConfigureHunter(HubSnapshot snapshot)
        {
            LauncherHunter.Wanted = snapshot.GameFilesReady;
            LauncherHunter.CanPresent = () => _active;
            LauncherHunter.Hunter = snapshot.DisplayHunter;
            LauncherHunter.Suit = snapshot.Suit;
            // The room profile owns composition now, not the RML document.
            // Changing activity can therefore move the map camera and Hunter
            // together without duplicating presentation constants in UI code.
            MenuStageProfile stage = LauncherMenuStage.Current;
            LauncherHunter.Left = stage.HunterLeft;
            LauncherHunter.Top = stage.HunterTop;
            LauncherHunter.Right = stage.HunterRight;
            LauncherHunter.Bottom = stage.HunterBottom;
            LauncherHunter.DistanceScale = stage.HunterDistanceScale;
            LauncherHunter.TransparentBackground = true;
        }

        private static void BeginMenuStageRefresh(HubSnapshot snapshot)
        {
            if (!snapshot.GameFilesReady || !ThumbnailBatch.CanRun || _stageRefresh != null)
                return;

            string room = LauncherBackdrop.RoomKey;
            if (String.IsNullOrWhiteSpace(room)
                || ThumbnailGenerator.HasCinematicPresentation(room))
                return;

            _stageRefreshRoom = room;
            _stageRefreshCancel = new CancellationTokenSource();
            CancellationToken token = _stageRefreshCancel.Token;
            Mods.DebugLog.Line("rmlui",
                $"refreshing cinematic menu stage for {room} in a background preview worker");
            _stageRefresh = Task.Run(() => ThumbnailBatch.Run(
                new[] { room },
                parallelism: 1,
                width: ThumbnailGenerator.ThumbnailWidth,
                height: ThumbnailGenerator.ThumbnailHeight,
                report: line => Mods.DebugLog.Line("rmlui", line),
                cancel: token,
                force: true), token);
        }

        private static void PollMenuStageRefresh()
        {
            Task<int>? task = _stageRefresh;
            if (task == null || !task.IsCompleted)
                return;

            string room = _stageRefreshRoom;
            CancellationTokenSource? completedCancel = _stageRefreshCancel;
            _stageRefresh = null;
            _stageRefreshRoom = "";
            _stageRefreshCancel = null;
            try
            {
                int written = task.GetAwaiter().GetResult();
                if (written > 0 || ThumbnailGenerator.HasCinematicPresentation(room))
                {
                    // The old GPU texture is still valid while the worker
                    // replaces the PNG, so the menu never flashes to black.
                    // Swap to the clean image now, on the GL owner thread.
                    LauncherPhoto.Invalidate();
                    LauncherBackdrop.Refresh();
                    Mods.DebugLog.Line("rmlui",
                        $"cinematic menu stage refreshed for {room}");
                }
            }
            catch (OperationCanceledException)
            {
                Mods.DebugLog.Line("rmlui", "cinematic menu-stage refresh cancelled");
            }
            catch (Exception ex)
            {
                // A stale backdrop is cosmetic. The frontend remains usable.
                Mods.DebugLog.Line("rmlui",
                    $"cinematic menu-stage refresh failed: {ex.Message}");
            }
            finally
            {
                completedCancel?.Dispose();
                // The player may have selected another activity while this
                // worker was rendering. Chain the current room now that the
                // single preview-worker slot is free.
                if (_active && !String.Equals(room, LauncherBackdrop.RoomKey,
                    StringComparison.OrdinalIgnoreCase))
                    BeginMenuStageRefresh(HubState.Capture());
            }
        }

        private static void DrainActions()
        {
            for (int i = 0; i < 8; i++)
            {
                Array.Clear(_actionBuffer, 0, _actionBuffer.Length);
                int length = NativeTakeAction(_actionBuffer, _actionBuffer.Length);
                if (length <= 0) return;
                string action = Encoding.UTF8.GetString(
                    _actionBuffer, 0, Math.Min(length, _actionBuffer.Length - 1));
                if (action.StartsWith("stage:", StringComparison.Ordinal))
                {
                    ApplyStageAction(action);
                    continue;
                }
                if (action.StartsWith("social:", StringComparison.Ordinal))
                {
                    HandleSocialAction(action);
                    continue;
                }
                _commands.Enqueue(action);
            }
        }

        private static void ApplyStageAction(string action)
        {
            switch (action)
            {
                case "stage:quick":
                    LauncherBackdrop.Set(LauncherBackdropScene.Multiplayer,
                        "MP3 PROVING GROUND");
                    break;
                case "stage:browser":
                    LauncherBackdrop.Set(LauncherBackdropScene.Play,
                        "MP1 SANCTORUS");
                    break;
                case "stage:offline":
                    LauncherBackdrop.Set(LauncherBackdropScene.Offline,
                        "MP3 PROVING GROUND");
                    break;
                case "stage:adventure":
                    LauncherBackdrop.Set(LauncherBackdropScene.Adventure,
                        "UNIT1 ALINOS LANDFALL");
                    break;
                default:
                    return;
            }

            HubSnapshot snapshot = HubState.Capture();
            ConfigureHunter(snapshot);
            BeginMenuStageRefresh(snapshot);
            Mods.DebugLog.Line("rmlui",
                $"menu stage -> {LauncherBackdrop.Scene}/{LauncherBackdrop.RoomKey} "
                + $"profile={LauncherMenuStage.Current.Name}");
        }

        private readonly record struct SocialUiRow(
            string PrimeId, string Name, string Activity, string Detail,
            string Relation, string InviteId, bool Online, bool FriendOnline,
            bool CanInvite, bool CanJoin, bool CanAcceptInvite,
            bool CanDeclineInvite, bool CanCancelInvite,
            bool CanPartyInvite = false);

        private static void BeginSocialLoad(bool force)
        {
            if (_socialFixture || _socialCancel == null || _socialCancel.IsCancellationRequested)
                return;
            if (_socialMutation is { IsCompleted: false })
                return;
            if (_socialLoad is { IsCompleted: false })
                return;

            long now = Environment.TickCount64;
            if (!force && now < _nextSocialReload)
                return;

            SetText("social_status", "SYNCING SOCIAL");
            SetBool("social_loading", true);
            _socialLoad = SocialClient.LoadAsync(_socialCancel.Token);
            _nextSocialReload = now + (_socialDrawerOpen ? 15000 : 60000);
        }

        private static void BeginSocialLookup(string primeId)
        {
            if (_socialFixture || _socialCancel == null || _socialCancel.IsCancellationRequested
                || _socialLookup is { IsCompleted: false })
                return;
            _socialLookupPlayer = null;
            _socialLookupQuery = primeId;
            SetText("social_status", "LOOKING UP PRIME ID");
            SetBool("social_loading", true);
            _socialLookup = SocialClient.LookupAsync(primeId, _socialCancel.Token);
        }

        private static void BeginSocialMutation(string action, string primeId)
        {
            if (_socialFixture || _socialCancel == null || _socialCancel.IsCancellationRequested)
                return;
            if (_socialMutation is { IsCompleted: false }
                || _socialInviteMutation is { IsCompleted: false }
                || _socialJoin is { IsCompleted: false })
            {
                SetText("social_status", "SOCIAL ACTION ALREADY IN PROGRESS");
                return;
            }

            CancellationToken token = _socialCancel.Token;
            _socialPendingAction = action;
            _socialMutation = action switch
            {
                "add" => SocialClient.SendFriendRequestAsync(primeId, token),
                "accept" => SocialClient.AcceptFriendRequestAsync(primeId, token),
                "decline" => SocialClient.DeclineFriendRequestAsync(primeId, token),
                "cancel" => SocialClient.CancelFriendRequestAsync(primeId, token),
                "remove" => SocialClient.RemoveFriendAsync(primeId, token),
                "block" => SocialClient.BlockPlayerAsync(primeId, token),
                "unblock" => SocialClient.UnblockPlayerAsync(primeId, token),
                _ => null
            };
            if (_socialMutation == null)
                return;

            SetText("social_status", action switch
            {
                "add" => "SENDING FRIEND REQUEST",
                "accept" => "ACCEPTING FRIEND REQUEST",
                "decline" => "DECLINING FRIEND REQUEST",
                "cancel" => "CANCELLING FRIEND REQUEST",
                "remove" => "REMOVING FRIEND",
                "block" => "BLOCKING PLAYER",
                "unblock" => "UNBLOCKING PLAYER",
                _ => "UPDATING SOCIAL"
            });
            SetBool("social_loading", true);
        }

        private static void BeginInviteMutation(string action, string id)
        {
            if (_socialFixture || _socialCancel == null || _socialCancel.IsCancellationRequested)
                return;
            if (_socialMutation is { IsCompleted: false }
                || _socialInviteMutation is { IsCompleted: false }
                || _socialJoin is { IsCompleted: false })
            {
                SetText("social_status", "SOCIAL ACTION ALREADY IN PROGRESS");
                return;
            }

            CancellationToken token = _socialCancel.Token;
            _socialInviteMutation = action switch
            {
                "invite-friend" => SocialInviteClient.SendInviteAsync(id, token),
                "invite-decline" => SocialInviteClient.DeclineInviteAsync(id, token),
                "invite-cancel" => SocialInviteClient.CancelInviteAsync(id, token),
                _ => null
            };
            if (_socialInviteMutation == null)
                return;

            SetText("social_status", action switch
            {
                "invite-friend" => "SENDING GAME INVITE",
                "invite-decline" => "DECLINING GAME INVITE",
                "invite-cancel" => "CANCELLING GAME INVITE",
                _ => "UPDATING INVITES"
            });
            SetBool("social_loading", true);
        }

        private static void BeginPartyMutation(string action, string id = "")
        {
            if (_socialFixture || _socialCancel == null || _socialCancel.IsCancellationRequested)
                return;
            if (_socialMutation is { IsCompleted: false }
                || _socialInviteMutation is { IsCompleted: false }
                || _socialPartyMutation is { IsCompleted: false }
                || _socialPartyGameInvite is { IsCompleted: false }
                || _socialJoin is { IsCompleted: false })
            {
                SetText("social_status", "SOCIAL ACTION ALREADY IN PROGRESS");
                return;
            }

            CancellationToken token = _socialCancel.Token;
            _socialPartyMutation = action switch
            {
                "party-invite" => SocialPartyClient.InviteAsync(id, token),
                "party-accept" => SocialPartyClient.AcceptInviteAsync(id, token),
                "party-decline" => SocialPartyClient.DeclineInviteAsync(id, token),
                "party-cancel" => SocialPartyClient.CancelInviteAsync(id, token),
                "party-leave" => SocialPartyClient.LeaveAsync(token),
                "party-disband" => SocialPartyClient.DisbandAsync(token),
                "party-kick" => SocialPartyClient.KickAsync(id, token),
                "party-promote" => SocialPartyClient.PromoteAsync(id, token),
                _ => null
            };
            if (_socialPartyMutation == null)
                return;

            SetText("social_status", action switch
            {
                "party-invite" => "SENDING PARTY INVITE",
                "party-accept" => "JOINING PARTY",
                "party-decline" => "DECLINING PARTY INVITE",
                "party-cancel" => "CANCELLING PARTY INVITE",
                "party-leave" => "LEAVING PARTY",
                "party-disband" => "DISBANDING PARTY",
                "party-kick" => "REMOVING PARTY MEMBER",
                "party-promote" => "PROMOTING PARTY LEADER",
                _ => "UPDATING PARTY"
            });
            SetBool("social_loading", true);
        }

        private static void BeginPartyGameInvites()
        {
            if (_socialFixture || _socialCancel == null || _socialCancel.IsCancellationRequested)
                return;
            if (_socialPartyGameInvite is { IsCompleted: false }
                || _socialPartyMutation is { IsCompleted: false }
                || _socialJoin is { IsCompleted: false })
                return;

            _socialPartyGameInvite =
                SocialPartyClient.InvitePartyToLobbyAsync(_socialCancel.Token);
            SetText("social_status", "INVITING PARTY TO LOBBY");
            SetBool("social_loading", true);
        }

        private static void BeginPartyLeaderJoin()
        {
            if (_socialFixture || _socialCancel == null || _socialCancel.IsCancellationRequested)
                return;
            if (NetSession.Active)
            {
                SetText("social_status", "LEAVE YOUR CURRENT SESSION BEFORE JOINING ANOTHER");
                return;
            }
            if (_socialJoin is { IsCompleted: false })
                return;
            _socialJoin = SocialPartyClient.PrepareLeaderJoinAsync(_socialCancel.Token);
            SetText("social_status", SocialPartyClient.Current.Travel != null
                ? "FOLLOWING PARTY // VERIFYING DESTINATION"
                : "VERIFYING PARTY LEADER LOBBY");
            SetBool("social_loading", true);
        }

        private static void BeginPartyTravelDecline()
        {
            if (_socialFixture || _socialCancel == null || _socialCancel.IsCancellationRequested)
                return;
            if (_socialPartyMutation is { IsCompleted: false }
                || _socialJoin is { IsCompleted: false })
                return;
            _socialPartyMutation =
                SocialPartyClient.DeclineTravelAsync(_socialCancel.Token);
            SetText("social_status", "DECLINING PARTY TRAVEL");
            SetBool("social_loading", true);
        }

        private static void BeginSocialJoin(string action, string id)
        {
            if (_socialFixture || _socialCancel == null || _socialCancel.IsCancellationRequested)
                return;
            if (NetSession.Active)
            {
                SetText("social_status", "LEAVE YOUR CURRENT SESSION BEFORE JOINING ANOTHER");
                return;
            }
            if (_socialMutation is { IsCompleted: false }
                || _socialInviteMutation is { IsCompleted: false }
                || _socialJoin is { IsCompleted: false })
            {
                SetText("social_status", "SOCIAL ACTION ALREADY IN PROGRESS");
                return;
            }

            CancellationToken token = _socialCancel.Token;
            _socialJoin = action switch
            {
                "join-friend" => SocialInviteClient.PrepareFriendJoinAsync(id, token),
                "invite-accept" => SocialInviteClient.PrepareInviteJoinAsync(
                    id, accept: true, token),
                _ => null
            };
            if (_socialJoin == null)
                return;

            SetText("social_status", action == "invite-accept"
                ? "ACCEPTING INVITE // VERIFYING SERVER"
                : "VERIFYING FRIEND LOBBY");
            SetBool("social_loading", true);
        }

        private static void PollSocialWork()
        {
            if (_socialLoad is { IsCompleted: true } load)
            {
                _socialLoad = null;
                try
                {
                    _socialSnapshot = load.GetAwaiter().GetResult();
                    SetText("social_status", "SOCIAL READY");
                    _socialFingerprint = "";
                }
                catch (OperationCanceledException) when (_socialCancel?.IsCancellationRequested == true)
                {
                }
                catch (Exception ex)
                {
                    SetText("social_status", "SOCIAL UNAVAILABLE // " + ShortSocialError(ex));
                }
            }

            if (_socialLookup is { IsCompleted: true } lookup)
            {
                string completedQuery = _socialLookupQuery;
                _socialLookup = null;
                _socialLookupQuery = "";
                try
                {
                    SocialLookupResult result = lookup.GetAwaiter().GetResult();
                    if (_socialTab == 1 && _socialSearch.Equals(
                        completedQuery, StringComparison.OrdinalIgnoreCase))
                    {
                        _socialLookupPlayer = result.Found ? result.Player : null;
                        SetText("social_status",
                            result.Found ? "PRIME ID FOUND" : "PRIME ID NOT FOUND");
                        _socialFingerprint = "";
                    }
                }
                catch (OperationCanceledException) when (_socialCancel?.IsCancellationRequested == true)
                {
                }
                catch (Exception ex)
                {
                    if (_socialSearch.Equals(completedQuery, StringComparison.OrdinalIgnoreCase))
                    {
                        _socialLookupPlayer = null;
                        SetText("social_status", "LOOKUP FAILED // " + ShortSocialError(ex));
                    }
                }

                if (_socialTab == 1 && LooksLikePrimeId(_socialSearch)
                    && !_socialSearch.Equals(completedQuery, StringComparison.OrdinalIgnoreCase))
                    BeginSocialLookup(_socialSearch.ToUpperInvariant());
            }

            if (_socialMutation is { IsCompleted: true } mutation)
            {
                _socialMutation = null;
                try
                {
                    SocialMutationResult result = mutation.GetAwaiter().GetResult();
                    if (result.Success)
                    {
                        if (result.Snapshot != null)
                            _socialSnapshot = result.Snapshot;
                        SetText("social_status", SocialMutationStatus(result.Status));
                        _nextSocialReload = 0;
                    }
                    else
                    {
                        SetText("social_status", "ACTION REFUSED // "
                            + result.Status.Replace('_', ' ').ToUpperInvariant());
                    }
                    SocialInviteClient.RefreshNow();
                    _socialFingerprint = "";
                }
                catch (OperationCanceledException) when (_socialCancel?.IsCancellationRequested == true)
                {
                }
                catch (Exception ex)
                {
                    SetText("social_status", "ACTION FAILED // " + ShortSocialError(ex));
                }
                finally
                {
                    _socialPendingAction = "";
                }
            }

            if (_socialInviteMutation is { IsCompleted: true } inviteMutation)
            {
                _socialInviteMutation = null;
                try
                {
                    SocialInviteMutationResult result =
                        inviteMutation.GetAwaiter().GetResult();
                    SetText("social_status", result.Success
                        ? InviteMutationStatus(result.Status)
                        : "INVITE REFUSED // "
                            + result.Status.Replace('_', ' ').ToUpperInvariant());
                    SocialInviteClient.RefreshNow();
                    _socialFingerprint = "";
                }
                catch (OperationCanceledException) when (_socialCancel?.IsCancellationRequested == true)
                {
                }
                catch (Exception ex)
                {
                    SetText("social_status", "INVITE FAILED // " + ShortSocialError(ex));
                }
            }

            if (_socialPartyMutation is { IsCompleted: true } partyMutation)
            {
                _socialPartyMutation = null;
                try
                {
                    SocialPartyMutationResult result =
                        partyMutation.GetAwaiter().GetResult();
                    SetText("social_status", result.Success
                        ? PartyMutationStatus(result.Status)
                        : "PARTY REFUSED // "
                            + result.Status.Replace('_', ' ').ToUpperInvariant());
                    SocialPartyClient.RefreshNow();
                    _socialFingerprint = "";
                }
                catch (OperationCanceledException) when (_socialCancel?.IsCancellationRequested == true)
                {
                }
                catch (Exception ex)
                {
                    SetText("social_status", "PARTY FAILED // " + ShortSocialError(ex));
                }
            }

            if (_socialPartyGameInvite is { IsCompleted: true } partyInviteAll)
            {
                _socialPartyGameInvite = null;
                try
                {
                    PartyGameInviteResult result = partyInviteAll.GetAwaiter().GetResult();
                    SetText("social_status", result.Status switch
                    {
                        "party_invites_sent" => $"PARTY INVITED // {result.Sent} SENT",
                        "party_invites_partial" => $"PARTY INVITES // {result.Sent} SENT / {result.Refused} REFUSED",
                        _ => "PARTY INVITE REFUSED // "
                            + result.Status.Replace('_', ' ').ToUpperInvariant()
                    });
                }
                catch (OperationCanceledException) when (_socialCancel?.IsCancellationRequested == true)
                {
                }
                catch (Exception ex)
                {
                    SetText("social_status", "PARTY INVITE FAILED // " + ShortSocialError(ex));
                }
            }

            if (_socialJoin is { IsCompleted: true } join)
            {
                _socialJoin = null;
                try
                {
                    SocialJoinResolution result = join.GetAwaiter().GetResult();
                    if (result.Success)
                    {
                        _verifiedSocialJoins.Enqueue(result);
                        SetText("social_status", "LOBBY VERIFIED // OPENING PLAY");
                    }
                    else
                    {
                        SetText("social_status", "JOIN REFUSED // "
                            + result.Error.Replace('_', ' ').ToUpperInvariant());
                    }
                }
                catch (OperationCanceledException) when (_socialCancel?.IsCancellationRequested == true)
                {
                }
                catch (Exception ex)
                {
                    SetText("social_status", "JOIN FAILED // " + ShortSocialError(ex));
                }
                _socialFingerprint = "";
            }

            SetBool("social_loading",
                _socialLoad is { IsCompleted: false }
                || _socialLookup is { IsCompleted: false }
                || _socialMutation is { IsCompleted: false }
                || _socialInviteMutation is { IsCompleted: false }
                || _socialPartyMutation is { IsCompleted: false }
                || _socialPartyGameInvite is { IsCompleted: false }
                || _socialJoin is { IsCompleted: false });
        }

        private static void HandleSocialAction(string action)
        {
            if (action == "social:open")
            {
                _socialDrawerOpen = true;
                _nextSocialReload = 0;
                BeginSocialLoad(force: true);
                SocialInviteClient.RefreshNow();
                SocialPartyClient.RefreshNow();
                RefreshSocialUi(force: true);
                NativeFocus("social_tab_friends");
                return;
            }
            if (action == "social:close")
            {
                _socialDrawerOpen = false;
                _nextSocialReload = Environment.TickCount64 + 60000;
                NativeFocus("social");
                return;
            }
            if (action.StartsWith("social:context:", StringComparison.Ordinal))
            {
                NativeFocus("social_context_close");
                return;
            }
            if (action == "social:context-close")
            {
                NativeFocus(SocialTabFocusId());
                return;
            }
            if (action == "social:refresh")
            {
                SocialPresenceClient.RefreshNow();
                SocialInviteClient.RefreshNow();
                SocialPartyClient.RefreshNow();
                _nextSocialReload = 0;
                BeginSocialLoad(force: true);
                SetText("social_status", "REFRESHING SOCIAL");
                return;
            }
            if (action == "social:dnd")
            {
                LauncherPrefs.DoNotDisturb = !LauncherPrefs.DoNotDisturb;
                LauncherPrefs.SocialPrivacyConfigured = true;
                LauncherPrefs.Save();
                SocialPresenceClient.NotifyPrivacyChanged();
                SetText("social_dnd_label",
                    LauncherPrefs.DoNotDisturb ? "DND ON" : "DND OFF");
                SetText("social_status",
                    LauncherPrefs.DoNotDisturb
                        ? "DO NOT DISTURB ENABLED"
                        : "DO NOT DISTURB DISABLED");
                _socialFingerprint = "";
                return;
            }
            if (action == "social:party-game-invite")
            {
                BeginPartyGameInvites();
                return;
            }
            if (action == "social:party-follow")
            {
                BeginPartyLeaderJoin();
                return;
            }
            if (action == "social:party-decline-travel")
            {
                BeginPartyTravelDecline();
                return;
            }
            if (action == "social:party-join-leader")
            {
                BeginPartyLeaderJoin();
                return;
            }
            if (action.StartsWith("social:tab:", StringComparison.Ordinal))
            {
                if (Int32.TryParse(action["social:tab:".Length..], out int tab))
                    _socialTab = Math.Clamp(tab, 0, 6);
                _socialLookupPlayer = null;
                _socialFingerprint = "";
                RefreshSocialUi(force: true);
                return;
            }
            if (action.StartsWith("social:search:", StringComparison.Ordinal))
            {
                string query = action["social:search:".Length..].Trim();
                if (query.Length > 48) query = query[..48];
                _socialSearch = query;
                _socialLookupPlayer = null;
                _socialFingerprint = "";
                if (_socialTab == 1 && LooksLikePrimeId(query))
                    BeginSocialLookup(query.ToUpperInvariant());
                else
                    SetText("social_status",
                        query.Length == 0 ? "SOCIAL READY" : "FILTER APPLIED");
                RefreshSocialUi(force: true);
                return;
            }

            const string prefix = "social:";
            int separator = action.IndexOf(':', prefix.Length);
            if (separator <= prefix.Length || separator + 1 >= action.Length)
                return;
            string verb = action[prefix.Length..separator];
            string id = action[(separator + 1)..].Trim();

            if (verb is "invite-accept" or "invite-decline" or "invite-cancel"
                or "party-accept" or "party-decline" or "party-cancel")
            {
                if (!Guid.TryParse(id, out _))
                {
                    SetText("social_status", "INVALID INVITE ID");
                    return;
                }
                if (verb == "invite-accept")
                    BeginSocialJoin(verb, id);
                else if (verb.StartsWith("party-", StringComparison.Ordinal))
                    BeginPartyMutation(verb, id);
                else
                    BeginInviteMutation(verb, id);
                NativeFocus(SocialTabFocusId());
                return;
            }

            string primeId = id.ToUpperInvariant();
            if (!LooksLikePrimeId(primeId))
            {
                SetText("social_status", "INVALID PRIME ID");
                return;
            }
            if (verb == "invite-friend")
                BeginInviteMutation(verb, primeId);
            else if (verb == "join-friend")
                BeginSocialJoin(verb, primeId);
            else if (verb is "party-invite" or "party-leave"
                or "party-disband" or "party-kick" or "party-promote")
                BeginPartyMutation(verb, primeId);
            else
                BeginSocialMutation(verb, primeId);
            NativeFocus(SocialTabFocusId());
        }

        private static string SocialTabFocusId() => _socialTab switch
        {
            1 => "social_tab_players",
            2 => "social_tab_requests",
            3 => "social_tab_invites",
            4 => "social_party",
            5 => "social_recent",
            6 => "social_blocks",
            _ => "social_tab_friends"
        };

        private static void RefreshSocialUi(bool force = false)
        {
            if (!_active || _socialFixture)
                return;

            SocialPresenceSnapshot presence = SocialPresenceClient.Current;
            SocialInviteSnapshot invites = SocialInviteClient.Current;
            SocialPartySnapshot partyState = SocialPartyClient.Current;
            SocialParty? party = partyState.Party;
            SocialLobbyLocator? ownLobby = SocialInviteClient.CurrentLobby;
            bool canInvite = ownLobby != null
                && ownLobby.ExpiresAt > DateTimeOffset.UtcNow;
            var presenceById = new Dictionary<string, SocialOnlinePlayer>(
                StringComparer.OrdinalIgnoreCase);
            var onlineFriends = new List<SocialOnlinePlayer>();
            foreach (SocialOnlinePlayer player in presence.Players)
            {
                if (player.PrimeId.Length == 0) continue;
                presenceById[player.PrimeId] = player;
                if (player.IsFriend) onlineFriends.Add(player);
            }
            onlineFriends.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(
                a.DisplayName, b.DisplayName));

            int requests = (_socialSnapshot?.IncomingRequests.Count ?? 0)
                + (_socialSnapshot?.OutgoingRequests.Count ?? 0);
            int incomingInvites = invites.Incoming.Count;
            int allInvites = incomingInvites + invites.Outgoing.Count;
            int incomingPartyInvites = partyState.IncomingPartyInvites.Count;
            int badge = requests + incomingInvites + incomingPartyInvites;
            SetText("social_online_count", $"{presence.Players.Count} ONLINE");
            SetText("social_friend_count", $"{onlineFriends.Count} ONLINE");
            SetText("social_request_count",
                requests == 1 ? "1 REQUEST" : $"{requests} REQUESTS");
            SetText("social_invite_count",
                allInvites == 1 ? "1 ACTIVE" : $"{allInvites} ACTIVE");
            SetText("social_party_count", party == null
                ? incomingPartyInvites == 0 ? "SOLO"
                    : incomingPartyInvites == 1 ? "1 INVITE"
                    : $"{incomingPartyInvites} INVITES"
                : $"{party.Members.Count}/8 MEMBERS");
            SetText("social_recent_count",
                partyState.RecentPlayers.Count == 1
                    ? "1 RECENT" : $"{partyState.RecentPlayers.Count} RECENT");
            SetText("social_dnd_label",
                LauncherPrefs.DoNotDisturb ? "DND ON" : "DND OFF");
            SetBool("social_party_game_invite_ready",
                party is { IsLeader: true }
                && party.Members.Count > 1
                && ownLobby != null
                && ownLobby.ExpiresAt > DateTimeOffset.UtcNow);

            SocialPartyTravel? travel = partyState.Travel;
            bool sameTravelAuthority = false;
            if (travel != null
                && travel.TryAuthorityEpoch(out ulong travelEpoch)
                && NetSession.Active
                && NetSession.AuthorityEpoch == travelEpoch)
            {
                sameTravelAuthority = true;
            }

            bool travelFollowReady = travel is { IsLeader: false }
                && !sameTravelAuthority
                && !NetSession.Active;
            bool travelDeclineReady = travelFollowReady
                && !travel!.SelfStatus.Equals(
                    "declined", StringComparison.OrdinalIgnoreCase);
            SetBool("social_party_follow_ready", travelFollowReady);
            SetBool("social_party_decline_travel_ready", travelDeclineReady);
            SetText("social_party_travel_label",
                travel == null ? "" : PartyTravelLabel(travel));

            bool leaderJoinable = false;
            if (travel == null
                && party is { IsLeader: false }
                && !NetSession.Active
                && RelationshipFor(party.LeaderPrimeId) == "FRIEND"
                && presenceById.TryGetValue(
                    party.LeaderPrimeId, out SocialOnlinePlayer? leaderPresence))
            {
                leaderJoinable = leaderPresence.Joinable
                    && !String.IsNullOrWhiteSpace(leaderPresence.LobbyId);
            }
            SetBool("social_party_join_leader_ready", leaderJoinable);
            SetText("social_badge",
                badge > 0 ? Math.Min(badge, 99).ToString(CultureInfo.InvariantCulture) : "");

            SocialGameInvite? newestInvite = invites.Incoming.Count > 0
                ? invites.Incoming[0] : null;
            SocialPartyInvite? newestPartyInvite =
                partyState.IncomingPartyInvites.Count > 0
                    ? partyState.IncomingPartyInvites[0] : null;
            string notice = LauncherPrefs.DoNotDisturb
                ? "DO NOT DISTURB // GAME AND PARTY INVITES PAUSED"
                : travel is { IsLeader: false }
                    && !sameTravelAuthority
                    && !travel.SelfStatus.Equals(
                        "declined", StringComparison.OrdinalIgnoreCase)
                        ? PartyTravelNotice(travel)
                : newestInvite != null
                    ? $"GAME INVITE // {newestInvite.DisplayName.ToUpperInvariant()} // "
                        + SocialRoomLabel(newestInvite.RoomKey)
                    : newestPartyInvite != null
                        ? $"PARTY INVITE // {newestPartyInvite.DisplayName.ToUpperInvariant()}"
                        : "";
            SetText("social_notice", notice);

            List<SocialUiRow> rows = BuildSocialRows(presenceById, canInvite);
            var filtered = new List<SocialUiRow>();
            foreach (SocialUiRow row in rows)
            {
                if (SocialMatches(row, _socialSearch))
                    filtered.Add(row);
            }

            if (_socialTab == 1 && _socialLookupPlayer is { } lookup
                && !filtered.Exists(row => row.PrimeId.Equals(
                    lookup.PrimeId, StringComparison.OrdinalIgnoreCase)))
            {
                string relation = RelationshipFor(lookup.PrimeId);
                var row = new SocialUiRow(
                    lookup.PrimeId,
                    lookup.DisplayName.ToUpperInvariant(),
                    "OFFLINE",
                    "PRIME ID LOOKUP",
                    relation,
                    "",
                    false,
                    false,
                    canInvite && relation != "BLOCKED",
                    false,
                    false,
                    false,
                    false,
                    CanInviteToParty(lookup.PrimeId));
                if (SocialMatches(row, _socialSearch))
                    filtered.Insert(0, row);
            }

            var fingerprint = new StringBuilder();
            fingerprint.Append(_socialTab).Append('|').Append(_socialSearch).Append('|')
                .Append(presence.Players.Count).Append('|').Append(onlineFriends.Count)
                .Append('|').Append(requests).Append('|').Append(allInvites)
                .Append('|').Append(incomingPartyInvites)
                .Append('|').Append(party?.PartyId ?? "")
                .Append('|').Append(partyState.RecentPlayers.Count)
                .Append('|').Append(LauncherPrefs.DoNotDisturb ? '1' : '0')
                .Append('|').Append(ownLobby?.LobbyId ?? "")
                .Append('|').Append(travel?.TravelId ?? "")
                .Append(':').Append(travel?.Revision ?? 0)
                .Append(':').Append(travel?.SelfStatus ?? "");
            foreach (SocialUiRow row in filtered)
                fingerprint.Append('|').Append(row.PrimeId).Append(':').Append(row.Activity)
                    .Append(':').Append(row.Detail).Append(':').Append(row.Relation)
                    .Append(':').Append(row.InviteId)
                    .Append(':').Append(row.Online ? '1' : '0')
                    .Append(':').Append(row.CanInvite ? '1' : '0')
                    .Append(':').Append(row.CanJoin ? '1' : '0')
                    .Append(':').Append(row.CanPartyInvite ? '1' : '0');
            for (int i = 0; i < onlineFriends.Count && i < 3; i++)
                fingerprint.Append("|H:").Append(onlineFriends[i].PrimeId)
                    .Append(':').Append(onlineFriends[i].Activity)
                    .Append(':').Append(onlineFriends[i].RoomKey);

            string key = fingerprint.ToString();
            if (!force && key == _socialFingerprint)
                return;
            _socialFingerprint = key;

            NativeSocialClear();
            foreach (SocialUiRow row in filtered)
            {
                NativeSocialAddRow(row.PrimeId, row.Name, row.Activity, row.Detail,
                    row.Relation, row.InviteId,
                    row.Online ? 1 : 0, row.FriendOnline ? 1 : 0,
                    row.CanInvite ? 1 : 0, row.CanJoin ? 1 : 0,
                    row.CanAcceptInvite ? 1 : 0, row.CanDeclineInvite ? 1 : 0,
                    row.CanCancelInvite ? 1 : 0, row.CanPartyInvite ? 1 : 0);
            }

            for (int i = 0; i < onlineFriends.Count && i < 3; i++)
            {
                SocialOnlinePlayer friend = onlineFriends[i];
                NativeSocialAddHomeFriend(
                    friend.PrimeId,
                    friend.DisplayName.ToUpperInvariant(),
                    ActivityLabel(friend.Activity),
                    SocialRoomLabel(friend.RoomKey));
            }
            NativeSocialCommit();
        }

        private static List<SocialUiRow> BuildSocialRows(
            Dictionary<string, SocialOnlinePlayer> presenceById, bool canInvite)
        {
            var rows = new List<SocialUiRow>();
            if (_socialTab == 0)
            {
                if (_socialSnapshot == null) return rows;
                foreach (SocialPlayer friend in _socialSnapshot.Friends)
                    rows.Add(RowForPersistent(
                        friend, "FRIEND", presenceById, canInvite));
                return rows;
            }

            if (_socialTab == 1)
            {
                foreach (SocialOnlinePlayer player in SocialPresenceClient.Current.Players)
                {
                    string relation = RelationshipFor(player.PrimeId);
                    rows.Add(new SocialUiRow(
                        player.PrimeId,
                        player.DisplayName.ToUpperInvariant(),
                        ActivityLabel(player.Activity),
                        SocialRoomLabel(player.RoomKey),
                        relation,
                        "",
                        true,
                        player.IsFriend,
                        canInvite && relation != "BLOCKED",
                        !NetSession.Active && relation == "FRIEND"
                            && player.Joinable && !String.IsNullOrWhiteSpace(player.LobbyId),
                        false,
                        false,
                        false,
                        CanInviteToParty(player.PrimeId)));
                }
                return rows;
            }

            if (_socialTab == 2)
            {
                if (_socialSnapshot == null) return rows;
                foreach (SocialPlayer incoming in _socialSnapshot.IncomingRequests)
                    rows.Add(RowForPersistent(
                        incoming, "INCOMING", presenceById, canInvite,
                        offlineActivity: "REQUEST RECEIVED"));
                foreach (SocialPlayer outgoing in _socialSnapshot.OutgoingRequests)
                    rows.Add(RowForPersistent(
                        outgoing, "OUTGOING", presenceById, canInvite,
                        offlineActivity: "REQUEST SENT"));
                return rows;
            }

            if (_socialTab == 3)
            {
                SocialInviteSnapshot invites = SocialInviteClient.Current;
                foreach (SocialGameInvite invite in invites.Incoming)
                {
                    presenceById.TryGetValue(invite.PrimeId, out SocialOnlinePlayer? online);
                    rows.Add(new SocialUiRow(
                        invite.PrimeId,
                        invite.DisplayName.ToUpperInvariant(),
                        invite.Status.Equals("accepted", StringComparison.OrdinalIgnoreCase)
                            ? "INVITE ACCEPTED" : "GAME INVITE",
                        InviteDetail(invite),
                        "GAME INVITE",
                        invite.InviteId,
                        online != null,
                        online?.IsFriend == true,
                        false,
                        false,
                        !NetSession.Active,
                        invite.Status.Equals("pending", StringComparison.OrdinalIgnoreCase),
                        false));
                }
                foreach (SocialGameInvite invite in invites.Outgoing)
                {
                    presenceById.TryGetValue(invite.PrimeId, out SocialOnlinePlayer? online);
                    rows.Add(new SocialUiRow(
                        invite.PrimeId,
                        invite.DisplayName.ToUpperInvariant(),
                        "INVITE SENT",
                        InviteDetail(invite),
                        "INVITE SENT",
                        invite.InviteId,
                        online != null,
                        online?.IsFriend == true,
                        false,
                        false,
                        false,
                        false,
                        true));
                }
                return rows;
            }

            if (_socialTab == 4)
            {
                SocialPartySnapshot partyState = SocialPartyClient.Current;
                SocialParty? party = partyState.Party;
                if (party != null)
                {
                    foreach (SocialPartyMember member in party.Members)
                    {
                        presenceById.TryGetValue(
                            member.PrimeId, out SocialOnlinePlayer? online);
                        string relation = member.IsSelf
                            ? (member.IsLeader ? "PARTY LEADER SELF" : "PARTY MEMBER SELF")
                            : party.IsLeader && !member.IsLeader
                                ? "PARTY MEMBER MANAGE"
                                : member.IsLeader ? "PARTY LEADER" : "PARTY MEMBER";
                        string memberDetail = online == null
                            ? "" : SocialRoomLabel(online.RoomKey);
                        string travelStatus = PartyTravelMemberStatus(member.PrimeId);
                        if (travelStatus.Length > 0)
                            memberDetail = memberDetail.Length == 0
                                ? travelStatus : memberDetail + " // " + travelStatus;
                        rows.Add(new SocialUiRow(
                            member.PrimeId,
                            member.DisplayName.ToUpperInvariant(),
                            online == null ? "OFFLINE" : ActivityLabel(online.Activity),
                            memberDetail,
                            relation,
                            "",
                            online != null,
                            online?.IsFriend == true,
                            canInvite && !member.IsSelf,
                            !NetSession.Active && member.IsLeader && !member.IsSelf
                                && online?.Joinable == true
                                && !String.IsNullOrWhiteSpace(online.LobbyId),
                            false, false, false, false));
                    }
                }

                foreach (SocialPartyInvite invite in partyState.IncomingPartyInvites)
                {
                    presenceById.TryGetValue(
                        invite.PrimeId, out SocialOnlinePlayer? online);
                    rows.Add(new SocialUiRow(
                        invite.PrimeId,
                        invite.DisplayName.ToUpperInvariant(),
                        "PARTY INVITE",
                        "EXPIRES " + invite.ExpiresAt.ToLocalTime().ToString(
                            "t", CultureInfo.CurrentCulture),
                        "PARTY INVITE",
                        invite.InviteId,
                        online != null,
                        online?.IsFriend == true,
                        false, false, false, false, false, false));
                }
                foreach (SocialPartyInvite invite in partyState.OutgoingPartyInvites)
                {
                    presenceById.TryGetValue(
                        invite.PrimeId, out SocialOnlinePlayer? online);
                    rows.Add(new SocialUiRow(
                        invite.PrimeId,
                        invite.DisplayName.ToUpperInvariant(),
                        "PARTY INVITE SENT",
                        "EXPIRES " + invite.ExpiresAt.ToLocalTime().ToString(
                            "t", CultureInfo.CurrentCulture),
                        "PARTY INVITE SENT",
                        invite.InviteId,
                        online != null,
                        online?.IsFriend == true,
                        false, false, false, false, false, false));
                }
                return rows;
            }

            if (_socialTab == 5)
            {
                foreach (SocialRecentPlayer recent in SocialPartyClient.Current.RecentPlayers)
                {
                    presenceById.TryGetValue(
                        recent.PrimeId, out SocialOnlinePlayer? online);
                    string relationship = RelationshipFor(recent.PrimeId);
                    rows.Add(new SocialUiRow(
                        recent.PrimeId,
                        recent.DisplayName.ToUpperInvariant(),
                        online == null ? "RECENT PLAYER" : ActivityLabel(online.Activity),
                        RecentDetail(recent, online),
                        relationship == "FRIEND" ? "RECENT FRIEND" : "RECENT",
                        "",
                        online != null,
                        online?.IsFriend == true,
                        canInvite && relationship != "BLOCKED",
                        !NetSession.Active && relationship == "FRIEND"
                            && online?.Joinable == true
                            && !String.IsNullOrWhiteSpace(online.LobbyId),
                        false, false, false,
                        CanInviteToParty(recent.PrimeId)));
                }
                return rows;
            }

            if (_socialSnapshot == null) return rows;
            foreach (SocialPlayer blocked in _socialSnapshot.Blocked)
                rows.Add(RowForPersistent(
                    blocked, "BLOCKED", presenceById, false,
                    offlineActivity: "BLOCKED"));
            return rows;
        }

        private static SocialUiRow RowForPersistent(
            SocialPlayer player, string relation,
            Dictionary<string, SocialOnlinePlayer> presenceById, bool canInvite,
            string offlineActivity = "OFFLINE")
        {
            if (presenceById.TryGetValue(player.PrimeId, out SocialOnlinePlayer online))
            {
                return new SocialUiRow(
                    player.PrimeId,
                    player.DisplayName.ToUpperInvariant(),
                    ActivityLabel(online.Activity),
                    SocialRoomLabel(online.RoomKey),
                    relation,
                    "",
                    true,
                    relation == "FRIEND",
                    canInvite && relation != "BLOCKED",
                    !NetSession.Active && relation == "FRIEND"
                        && online.Joinable && !String.IsNullOrWhiteSpace(online.LobbyId),
                    false,
                    false,
                    false,
                    relation == "FRIEND" && CanInviteToParty(player.PrimeId));
            }
            return new SocialUiRow(
                player.PrimeId,
                player.DisplayName.ToUpperInvariant(),
                offlineActivity,
                "",
                relation,
                "",
                false,
                false,
                canInvite && relation != "BLOCKED",
                false,
                false,
                false,
                false,
                relation == "FRIEND" && CanInviteToParty(player.PrimeId));
        }

        private static string PartyTravelLabel(SocialPartyTravel travel)
        {
            string mode = travel.Reason switch
            {
                "quick_play" => "PARTY QUICK PLAY",
                "regroup" => "POST-MATCH REGROUP",
                _ => "PARTY TRAVEL"
            };
            string server = travel.ServerName.Trim().ToUpperInvariant();
            string room = SocialRoomLabel(travel.RoomKey);
            string target = server.Length == 0 ? room
                : room.Length == 0 ? server
                : server + " // " + room;
            string status = travel.IsLeader
                ? "LEADER READY"
                : travel.SelfStatus.Replace('_', ' ').ToUpperInvariant();
            return target.Length == 0
                ? $"{mode} // {status}"
                : $"{mode} // {target} // {status}";
        }

        private static string PartyTravelNotice(SocialPartyTravel travel)
        {
            string leader = travel.LeaderDisplayName.ToUpperInvariant();
            string room = SocialRoomLabel(travel.RoomKey);
            string mode = travel.Reason == "regroup"
                ? "REGROUP WITH PARTY"
                : travel.Reason == "quick_play"
                    ? "PARTY QUICK PLAY READY"
                    : "FOLLOW PARTY LEADER";
            return room.Length == 0
                ? $"{mode} // {leader}"
                : $"{mode} // {leader} // {room}";
        }

        private static string InviteDetail(SocialGameInvite invite)
        {
            string room = SocialRoomLabel(invite.RoomKey);
            string server = invite.ServerName.Trim().ToUpperInvariant();
            if (server.Length == 0) return room;
            if (room.Length == 0) return server;
            return server + " // " + room;
        }

        private static string PartyTravelMemberStatus(string primeId)
        {
            SocialPartyTravel? travel = SocialPartyClient.Current.Travel;
            if (travel == null) return "";
            foreach (SocialPartyTravelMember member in travel.Members)
            {
                if (!member.PrimeId.Equals(
                    primeId, StringComparison.OrdinalIgnoreCase))
                    continue;
                return member.Status switch
                {
                    "following" => "FOLLOWING PARTY",
                    "joined" => "REGROUPED",
                    "declined" => "TRAVEL DECLINED",
                    "pending" => "TRAVEL PENDING",
                    _ => member.Status.Replace('_', ' ').ToUpperInvariant()
                };
            }
            return "";
        }

        private static bool CanInviteToParty(string primeId)
        {
            if (!String.Equals(
                RelationshipFor(primeId), "FRIEND", StringComparison.Ordinal))
                return false;

            SocialPartySnapshot state = SocialPartyClient.Current;
            if (state.Party is { IsLeader: false })
                return false;
            if (state.Party != null)
            {
                foreach (SocialPartyMember member in state.Party.Members)
                    if (member.PrimeId.Equals(
                        primeId, StringComparison.OrdinalIgnoreCase))
                        return false;
            }
            foreach (SocialPartyInvite invite in state.OutgoingPartyInvites)
                if (invite.PrimeId.Equals(
                    primeId, StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        private static string RecentDetail(
            SocialRecentPlayer recent, SocialOnlinePlayer? online)
        {
            if (online != null && !String.IsNullOrWhiteSpace(online.RoomKey))
                return SocialRoomLabel(online.RoomKey);

            TimeSpan age = DateTimeOffset.UtcNow - recent.LastSeen;
            string when = age.TotalMinutes < 2 ? "JUST NOW"
                : age.TotalHours < 1 ? $"{Math.Max(2, (int)age.TotalMinutes)}M AGO"
                : age.TotalDays < 1 ? $"{Math.Max(1, (int)age.TotalHours)}H AGO"
                : $"{Math.Max(1, (int)age.TotalDays)}D AGO";
            string encounters = recent.Encounters == 1
                ? "1 ENCOUNTER" : $"{recent.Encounters} ENCOUNTERS";
            return when + " // " + encounters;
        }

        private static string RelationshipFor(string primeId)
        {
            if (_socialSnapshot == null)
                return "PLAYER";
            if (ContainsPrimeId(_socialSnapshot.Friends, primeId))
                return "FRIEND";
            if (ContainsPrimeId(_socialSnapshot.IncomingRequests, primeId))
                return "INCOMING";
            if (ContainsPrimeId(_socialSnapshot.OutgoingRequests, primeId))
                return "OUTGOING";
            if (ContainsPrimeId(_socialSnapshot.Blocked, primeId))
                return "BLOCKED";
            return "PLAYER";
        }

        private static bool ContainsPrimeId(List<SocialPlayer> players, string primeId)
        {
            foreach (SocialPlayer player in players)
                if (player.PrimeId.Equals(primeId, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool SocialMatches(SocialUiRow row, string search)
        {
            if (String.IsNullOrWhiteSpace(search))
                return true;
            return row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.PrimeId.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.Activity.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.Detail.Contains(search, StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikePrimeId(string value)
        {
            if (value.Length != 27 || !value.StartsWith("PP-", StringComparison.OrdinalIgnoreCase))
                return false;
            for (int i = 3; i < value.Length; i++)
            {
                if (i is 7 or 12 or 17 or 22)
                {
                    if (value[i] != '-') return false;
                    continue;
                }
                char ch = value[i];
                if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f')
                    || (ch >= 'A' && ch <= 'F')))
                    return false;
            }
            return true;
        }

        private static string ActivityLabel(string activity) => activity switch
        {
            "menu" => "MAIN MENU",
            "lobby" => "IN LOBBY",
            "in_match" => "IN MATCH",
            "spectating" => "SPECTATING",
            _ => "ONLINE"
        };

        private static string SocialRoomLabel(string? roomKey)
        {
            if (String.IsNullOrWhiteSpace(roomKey))
                return "";
            if (Metadata.RoomMetadata.TryGetValue(roomKey, out RoomMetadata? metadata)
                && !String.IsNullOrWhiteSpace(metadata.InGameName))
                return metadata.InGameName!.ToUpperInvariant();
            return roomKey.ToUpperInvariant();
        }

        private static string SocialMutationStatus(string status) => status switch
        {
            "request_sent" => "FRIEND REQUEST SENT",
            "request_pending" => "FRIEND REQUEST ALREADY PENDING",
            "friends" => "FRIEND ADDED",
            "already_friends" => "ALREADY FRIENDS",
            "request_declined" => "FRIEND REQUEST DECLINED",
            "request_cancelled" => "FRIEND REQUEST CANCELLED",
            "friend_removed" => "FRIEND REMOVED",
            "blocked" => "PLAYER BLOCKED",
            "unblocked" => "PLAYER UNBLOCKED",
            _ => status.Replace('_', ' ').ToUpperInvariant()
        };

        private static string InviteMutationStatus(string status) => status switch
        {
            "invite_sent" => "GAME INVITE SENT",
            "invite_pending" => "GAME INVITE ALREADY PENDING",
            "invite_active" => "GAME INVITE ALREADY ACTIVE",
            "invite_declined" => "GAME INVITE DECLINED",
            "invite_cancelled" => "GAME INVITE CANCELLED",
            _ => status.Replace('_', ' ').ToUpperInvariant()
        };

        private static string PartyMutationStatus(string status) => status switch
        {
            "party_created_and_invited" => "PARTY CREATED // INVITE SENT",
            "party_invite_sent" => "PARTY INVITE SENT",
            "party_invite_pending" => "PARTY INVITE ALREADY PENDING",
            "party_joined" => "PARTY JOINED",
            "party_invite_declined" => "PARTY INVITE DECLINED",
            "party_invite_cancelled" => "PARTY INVITE CANCELLED",
            "party_left" => "PARTY LEFT",
            "party_disbanded" => "PARTY DISBANDED",
            "party_member_removed" => "PARTY MEMBER REMOVED",
            "party_leader_promoted" => "PARTY LEADER PROMOTED",
            "party_travel_declined" => "PARTY TRAVEL DECLINED",
            "party_travel_joined" => "PARTY REGROUPED",
            "party_travel_published" => "PARTY TRAVEL READY",
            _ => status.Replace('_', ' ').ToUpperInvariant()
        };

        private static string ShortSocialError(Exception ex)
        {
            string text = ex.Message.Trim().Replace('\n', ' ').Replace('\r', ' ');
            if (text.Length == 0) text = ex.GetType().Name;
            return text.Length > 52 ? text[..52].ToUpperInvariant() : text.ToUpperInvariant();
        }

        private static void SeedSocialCapture()
        {
            _socialDrawerOpen = true;
            SetBool("social_open", true);
            SetBool("social_loading", false);
            SetText("social_status", "SOCIAL READY // CAPTURE FIXTURE");
            SetText("social_online_count", "6 ONLINE");
            SetText("social_friend_count", "2 ONLINE");
            SetText("social_request_count", "2 REQUESTS");
            SetText("social_invite_count", "1 ACTIVE");
            SetText("social_party_count", "2/8 MEMBERS");
            SetText("social_recent_count", "4 RECENT");
            SetText("social_dnd_label", "DND OFF");
            SetText("social_badge", "3");
            SetText("social_notice", "GAME INVITE // SYLUX MAIN // SANCTORUS");
            NativeSocialClear();
            NativeSocialAddRow("PP-7A1C-5D91-44B2-8E31-9F20", "TRACE MAIN",
                "IN LOBBY", "SANCTORUS", "FRIEND", "", 1, 1, 0, 1, 0, 0, 0, 0);
            NativeSocialAddRow("PP-0D72-3F1A-4B8C-91E0-6A2B", "KANDEN",
                "IN MATCH", "FUEL STACK", "FRIEND", "", 1, 1, 0, 0, 0, 0, 0, 0);
            NativeSocialAddRow("PP-991A-B732-4FD1-87C0-122E", "WEAVEL FAN",
                "OFFLINE", "", "FRIEND", "", 0, 0, 0, 0, 0, 0, 0, 0);
            NativeSocialAddRow("PP-AB22-01CE-4DA7-82E1-7F04", "NOXUS",
                "OFFLINE", "", "FRIEND", "", 0, 0, 0, 0, 0, 0, 0, 0);
            NativeSocialAddHomeFriend("PP-7A1C-5D91-44B2-8E31-9F20",
                "TRACE MAIN", "IN LOBBY", "SANCTORUS");
            NativeSocialAddHomeFriend("PP-0D72-3F1A-4B8C-91E0-6A2B",
                "KANDEN", "IN MATCH", "FUEL STACK");
            NativeSocialCommit();
        }

        private static void HandleGamepad(UiAction action)
        {
            if (!_active) return;
            if (action == UiAction.Back)
            {
                if (NativeBack() == 0)
                    _commands.Enqueue("route:news");
                return;
            }
            int key = action switch
            {
                UiAction.Up => 5,
                UiAction.Down => 6,
                UiAction.Left => 7,
                UiAction.Right => 8,
                UiAction.Accept => 2,
                UiAction.PreviousTab => 1,
                UiAction.NextTab => 1,
                _ => 0
            };
            if (key == 0) return;
            int modifiers = action == UiAction.PreviousTab ? 1 : 0;
            NativeKey(key, 1, modifiers);
            NativeKey(key, 0, modifiers);
        }

        private static int TranslateKey(Keys key) => key switch
        {
            Keys.Tab => 1,
            Keys.Enter or Keys.KeyPadEnter => 2,
            Keys.Escape => 3,
            Keys.Space => 4,
            Keys.Up => 5,
            Keys.Down => 6,
            Keys.Left => 7,
            Keys.Right => 8,
            Keys.Home => 9,
            Keys.End => 10,
            Keys.PageUp => 11,
            Keys.PageDown => 12,
            _ => 0
        };

        private static int Modifiers(KeyboardKeyEventArgs e)
        {
            int modifiers = 0;
            if (e.Shift) modifiers |= 1;
            if (e.Control) modifiers |= 2;
            if (e.Alt) modifiers |= 4;
            if (e.Command) modifiers |= 8;
            return modifiers;
        }

        private static string? CaptureDirectory()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i].Equals("-rmluipocshot", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        private static bool SocialCaptureRequested()
            => Array.Exists(Environment.GetCommandLineArgs(),
                value => value.Equals("-rmluisocial", StringComparison.OrdinalIgnoreCase));

        private static Vector2i? CaptureSize()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (!args[i].Equals("-rmluisize", StringComparison.OrdinalIgnoreCase))
                    continue;
                string[] parts = args[i + 1].Split('x', 'X');
                if (parts.Length == 2
                    && Int32.TryParse(parts[0], out int width)
                    && Int32.TryParse(parts[1], out int height)
                    && width is >= 640 and <= 7680
                    && height is >= 360 and <= 4320)
                    return new Vector2i(width, height);
            }
            return null;
        }

        private static unsafe void ReadSize(RenderWindow window, out int width, out int height, out float density)
        {
            width = Math.Max(window.FramebufferSize.X, 1);
            height = Math.Max(window.FramebufferSize.Y, 1);

            // Do not use NativeWindow.ClientSize for the Retina ratio here.
            // On macOS/OpenTK it can already reflect framebuffer-sized pixels,
            // which makes a 2x Retina window look like 1x and shrinks every
            // density-independent RmlUi control by half on screen.
            //
            // GLFW window size is explicitly in screen coordinates while the
            // framebuffer size is in pixels. Their ratio is therefore the
            // authoritative conversion for both RmlUi dp and pointer input.
            int windowWidth = 0, windowHeight = 0;
            float framebufferScaleX = 1f, framebufferScaleY = 1f;
            try
            {
                GLFW.GetWindowSize(window.WindowPtr, out windowWidth, out windowHeight);
                if (windowWidth > 0)
                    framebufferScaleX = Math.Max(1f, width / (float)windowWidth);
                if (windowHeight > 0)
                    framebufferScaleY = Math.Max(1f, height / (float)windowHeight);
            }
            catch
            {
                // Keep the 1x fallback and let content scale below provide a
                // platform DPI answer if GLFW window-size lookup is unavailable.
            }

            _pointerScaleX = framebufferScaleX;
            _pointerScaleY = framebufferScaleY;

            float contentScale = 1f;
            try
            {
                GLFW.GetWindowContentScale(window.WindowPtr, out float xScale, out float yScale);
                if (float.IsFinite(xScale) && float.IsFinite(yScale))
                    contentScale = Math.Max(1f, Math.Max(xScale, yScale));
            }
            catch
            {
                // Framebuffer/window ratio above remains the primary answer.
            }

            density = Math.Max(
                Math.Max(framebufferScaleX, framebufferScaleY),
                contentScale);

            float forced = DensityOverride();
            if (forced > 0)
                density = forced;
        }

        private static float DensityOverride()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (!args[i].Equals("-rmluidensity", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (float.TryParse(args[i + 1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float value)
                    && float.IsFinite(value))
                    return Math.Clamp(value, 0.75f, 4f);
            }
            return 0;
        }

        private static void SetText(string name, string value)
        {
            if (_active) NativeSetText(name, value ?? string.Empty);
        }

        private static void SetBool(string name, bool value)
        {
            if (_active) NativeSetBool(name, value ? 1 : 0);
        }

        private static void Fail(string message, Exception ex)
        {
            bool wasActive = _active;
            _failed = true;
            _active = false;
            if (wasActive)
            {
                try { NativeShutdown(); }
                catch (Exception shutdown) { Mods.DebugLog.Exception("rmlui", shutdown); }
            }
            _commands.Clear();
            _gamepad.Reset();
            _captureDirectory = null;
            _captureFrames = 0;
            _stageRefreshCancel?.Cancel();
            _stageRefreshCancel = null;
            _stageRefresh = null;
            _stageRefreshRoom = "";
            _socialCancel?.Cancel();
            _socialCancel?.Dispose();
            _socialCancel = null;
            _socialLoad = null;
            _socialMutation = null;
            _socialLookup = null;
            _socialInviteMutation = null;
            _socialPartyMutation = null;
            _socialPartyGameInvite = null;
            _socialJoin = null;
            ClearVerifiedSocialJoins();
            _socialSnapshot = null;
            _socialLookupPlayer = null;
            _socialPendingAction = "";
            _socialSearch = "";
            _socialFingerprint = "";
            _socialDrawerOpen = false;
            _socialFixture = false;
            LauncherHunter.Reset();
            GamepadContexts.MenuVisible = false;
            Mods.DebugLog.Line("rmlui", $"POC {message}: {ex.Message}; falling back to Avalonia");
            Mods.DebugLog.Exception("rmlui", ex);
        }

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_initialize")]
        private static extern int NativeInitialize(int width, int height, float density,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string assetRoot);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_shutdown")]
        private static extern void NativeShutdown();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_update")]
        private static extern void NativeUpdate();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_render")]
        private static extern void NativeRender(int width, int height);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_resize")]
        private static extern void NativeResize(int width, int height, float density);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_mouse_move")]
        private static extern int NativeMouseMove(int x, int y, int modifiers);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_mouse_button")]
        private static extern int NativeMouseButton(int button, int down, int modifiers);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_mouse_wheel")]
        private static extern int NativeMouseWheel(float deltaY, int modifiers);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_key")]
        private static extern int NativeKey(int key, int down, int modifiers);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_text")]
        private static extern int NativeText(uint codepoint);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_set_text")]
        private static extern void NativeSetText(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_set_bool")]
        private static extern void NativeSetBool(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int value);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_social_clear")]
        private static extern void NativeSocialClear();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_social_add_row")]
        private static extern void NativeSocialAddRow(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string primeId,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string activity,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string detail,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string relation,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string inviteId,
            int online, int friendOnline, int canInvite, int canJoin,
            int canAcceptInvite, int canDeclineInvite, int canCancelInvite,
            int canPartyInvite);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_social_add_home_friend")]
        private static extern void NativeSocialAddHomeFriend(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string primeId,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string activity,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string detail);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_social_commit")]
        private static extern void NativeSocialCommit();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_back")]
        private static extern int NativeBack();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_focus")]
        private static extern int NativeFocus(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string id);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_take_action")]
        private static extern int NativeTakeAction([Out] byte[] buffer, int capacity);
    }
}
#endif
