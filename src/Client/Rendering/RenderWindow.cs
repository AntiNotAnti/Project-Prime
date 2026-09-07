using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Effects;
using MphRead.Entities;
using MphRead.Export;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using MphRead.Hud;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead
{
    public class RenderWindow : GameWindow
    {
        /// <summary>
        /// The loop is driven entirely from <see cref="OnRenderFrame"/>, which
        /// runs the simulation on its own fixed-step accumulator
        /// (<see cref="Mods.Render.FrameTiming"/>).
        ///
        /// OpenTK 4.9 no longer separates its update and render ticks -- the
        /// two callbacks fire together and RenderFrequency is deprecated -- so
        /// UpdateFrequency here is the *frame* rate, and 0 means "as fast as
        /// the window will go", which with VSync on is the display's rate.
        /// <see cref="ApplyFrameRateSettings"/> sets both from the player's
        /// choice.
        ///
        /// This used to be 60, and it was the whole frame rate: one call did a
        /// simulation step and a picture, so 60 steps a second and 60 pictures
        /// a second were the same number and neither could move without the
        /// other.
        /// </summary>
        private static readonly GameWindowSettings _gameWindowSettings = new GameWindowSettings()
        {
            UpdateFrequency = 0
        };

        private static readonly NativeWindowSettings _nativeWindowSettings = new NativeWindowSettings()
        {
            ClientSize = new Vector2i(1280, 768),
            Title = Mods.Branding.Name,
            Profile = ContextProfile.Compatability,
            Flags = ContextFlags.Default,
            APIVersion = new Version(3, 2),
            StartVisible = false
        };

        public Scene Scene { get; }
        public ScenePresentation Presentation { get; }
        private bool _startedHidden = true;

        /// <summary>
        /// The smallest the game window may be dragged to.
        ///
        /// Not an aesthetic floor: Escape's menu is laid over this window and
        /// its panel needs about 500 *device-independent* pixels of height, so
        /// a shorter window cut the panel's top and bottom off -- and on a
        /// display running at 150%, which is the ordinary Windows setting on a
        /// laptop, 500 of those are 750 real ones. The old 600-pixel floor was
        /// therefore short by a third before the player did anything wrong,
        /// which is why the menu came up trimmed however the window was sized.
        ///
        /// The menu also scales itself down to whatever it is given now (see
        /// <c>PauseMenuView</c>), so nothing depends on this any more; a floor
        /// this low simply is not a size anybody wants to play at either, and
        /// GLFW will honour a minimum where it will not honour a request to be
        /// sensible.
        /// </summary>
        private static readonly Vector2i _minimumSize = new Vector2i(1024, 720);

        /// <summary>
        /// True once <see cref="Scene"/> exists. <see cref="OnResize"/> can be
        /// called before it does, from inside this constructor.
        /// </summary>
        private bool _sceneReady;

        public RenderWindow() : base(_gameWindowSettings, _nativeWindowSettings)
        {
            // The scene first, and the size floor after it: applying size
            // limits to a window smaller than the floor makes GLFW resize it
            // on the spot, which calls the size callback -- and that reached
            // OnResize with Scene still null. OpenTK does not let the
            // exception out of the callback: it stashes it and rethrows it
            // from the first ProcessWindowEvents, so a machine whose screen
            // could not hold a 1024x720 window died with a null reference
            // inside Run() with the whole room already loaded and nothing
            // near the crash to explain it.
            Scene = new Scene(preserveNicknames: Mods.Network.NetSession.Active);
            Presentation = new ScenePresentation(Scene, Size, KeyboardState, MouseState, (string title) =>
            {
                Title = title;
            }, () =>
            {
                Close();
            });
            Presentation.EnableDesktopLook();
            _sceneReady = true;
            FitToScreen();
        }

        /// <summary>
        /// The size floor, against the screen the window opened on.
        ///
        /// <see cref="_minimumSize"/> is 720 tall, and the work area of a
        /// 1366x768 laptop panel is shorter than that once its taskbar and the
        /// window's own title bar are taken off. A floor taller than the
        /// screen is a window whose bottom is off the desktop and can never be
        /// dragged back on -- and it is the resize GLFW performs to enforce
        /// that floor which crashed the game outright before the guard in
        /// <see cref="OnResize"/>. Only ever trimmed, never raised, so a
        /// display with room for it gets exactly what it always got.
        ///
        /// The startup size is deliberately left alone: client sizes and a
        /// monitor's work area are the same unit on Windows and X11 but not
        /// on a Retina Mac, where the window is measured in pixels and the
        /// work area in points, and clamping one against the other there
        /// would halve a window that was never too big.
        /// </summary>
        private void FitToScreen()
        {
            Vector2i floor = _minimumSize;
            try
            {
                Box2i area = Monitors.GetMonitorFromWindow(this).WorkArea;
                // Room for the frame the window manager draws around the
                // client area. The exact figure does not matter: it only ever
                // applies to a screen that is already too small.
                var room = new Vector2i(Math.Max(320, area.Size.X - 16),
                    Math.Max(240, area.Size.Y - 64));
                floor = new Vector2i(Math.Min(floor.X, room.X), Math.Min(floor.Y, room.Y));
            }
            catch (Exception ex)
            {
                // No monitor to ask (a headless run, a display that went away
                // between creating the window and this line): the fixed floor
                // is what shipped for every release before this one.
                Mods.DebugLog.Line("window", $"could not size against the display: {ex.Message}");
            }
            MinimumSize = floor;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            Presentation.DoCleanup();
            base.OnClosing(e);
        }

        public void AddRoom(int id, GameMode mode = GameMode.None, int playerCount = 0,
            int nodeLayerMask = 0, int entityLayerId = -1)
        {
            RoomMetadata? meta = Metadata.GetRoomById(id);
            if (meta == null)
            {
                throw new ProgramException("No room with this ID is known.");
            }
            Presentation.AddRoom(meta.Name, mode, playerCount, nodeLayerMask, entityLayerId);
        }

        public void AddRoom(string name, GameMode mode = GameMode.None, int playerCount = 0,
            int nodeLayerMask = 0, int entityLayerId = -1)
        {
            Presentation.AddRoom(name, mode, playerCount, nodeLayerMask, entityLayerId);
        }

        public void AddModel(string name, int recolor = 0, bool firstHunt = false, MetaDir dir = MetaDir.Models, Vector3? pos = null)
        {
            Presentation.AddModel(name, recolor, firstHunt, dir, pos);
        }

        public void AddPlayer(Hunter hunter, int recolor = 0, int team = -1, Vector3? position = null)
        {
            Scene.AddPlayer(hunter, recolor, team, position);
        }

        protected override void OnLoad()
        {
            Presentation.OnLoad();
            base.OnLoad();
        }

        private int _appliedFrameRateCap = -1;

        /// <summary>
        /// Put the player's frame rate choice on the window, and only when it
        /// has changed: the settings window opens from the pause menu during a
        /// match, so this is asked every frame.
        ///
        /// A cap of <c>DisplayRate</c> means "whatever the monitor does",
        /// which is VSync and no cap of ours -- the right default, and the
        /// only one that produces a tear-free 144. Any explicit number turns
        /// VSync off, because asking for 120 on a 144 Hz screen with VSync on
        /// gets 72.
        /// </summary>
        private void ApplyFrameRateSettings()
        {
            int cap = Mods.Render.FrameTiming.FrameRateCap;
            if (cap == _appliedFrameRateCap)
            {
                return;
            }
            _appliedFrameRateCap = cap;
            if (cap == Mods.Render.FrameTiming.DisplayRate)
            {
                VSync = VSyncMode.On;
                UpdateFrequency = 0;
            }
            else
            {
                VSync = VSyncMode.Off;
                UpdateFrequency = cap;
            }
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            // The pause menu wants the pointer back.
            CursorState = (Presentation.CameraMode == CameraMode.Player || Presentation.IsFreeCam) && !Presentation.FrameAdvance
                && !Mods.PauseMenu.Open
                && !Presentation.ShowCursor
                ? CursorState.Grabbed
                : CursorState.Normal;
            ApplyFrameRateSettings();
            // The simulation runs at 60 Hz and the picture runs at the
            // display's rate, so this is 1 on a 60 Hz screen, 0 or 1 on a
            // faster one, and 2 or more only on a frame that took longer than
            // a step. Everything the game *is* -- input, the network session,
            // the world, the clock -- happens in here and exactly this often.
            int steps;
            if (Presentation.FrameAdvance)
            {
                // Stepping frames by hand is the one mode that must stay one
                // step to one picture: the request to advance is consumed
                // after the frame is drawn (AfterRenderFrame), so a picture
                // drawn without a step would eat it before the simulation ever
                // saw it, and the frame would never advance.
                Mods.Render.FrameTiming.Reset();
                steps = 1;
            }
            else
            {
                steps = Mods.Render.FrameTiming.Advance(args.Time);
            }
            for (int i = 0; i < steps; i++)
            {
                Presentation.OnSimulationFrame();
            }
            // Start, on a pad, is Escape. Consumed here rather than in the
            // scene because opening the menu is a window operation and the
            // window is this class -- the same reason the keyboard's Escape
            // is handled in OnKeyDown and not in the entity.
            if (Mods.Input.GamepadInput.TakeMenuPress()
                && (Presentation.CameraMode == CameraMode.Player || Presentation.IsFreeCam))
            {
                Mods.PauseMenu.HandleEscape(this);
            }
            Presentation.OnDrawFrame();
            if (!Presentation.OnRenderFrame())
            {
                return;
            }
            SwapBuffers();
            Presentation.OnFramePresented();
            if (_startedHidden)
            {
                IsVisible = true;
                _startedHidden = false;
                Mods.WindowMode.ApplyStartup(this);
            }
            // What the pause menu asked for, done on the thread that owns the
            // window: closing it and changing its border belong here.
            Mods.PauseMenu.Poll(this);
            Presentation.AfterRenderFrame();
            base.OnRenderFrame(args);
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            // GLFW can call this while the window is still being built, before
            // there is a scene to hand the new size to. Nothing is lost by
            // ignoring it: the window's real size is read again when the scene
            // is created, and the render target is sized at load.
            if (!_sceneReady)
            {
                return;
            }
            GL.Viewport(0, 0, e.Size.X, e.Size.Y);
            Presentation.Size = e.Size;
            Presentation.OnResize();
            base.OnResize(e);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.Button == MouseButton.Button1)
            {
                if (Mods.SpectatorMode.IsSpectating)
                {
                    Mods.SpectatorMode.CycleNext();
                }
                else
                {
                    Presentation.OnMouseClick(down: true);
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (e.Button == MouseButton.Button1)
            {
                Presentation.OnMouseClick(down: false);
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            if (IsFocused && CursorState == CursorState.Grabbed && Presentation.CanCaptureSimulationLook)
                Presentation.RenderLook?.Add(e.DeltaX, e.DeltaY);
            else Presentation.ResetRenderLook();
            Presentation.OnMouseMove(e.DeltaX, e.DeltaY);
            base.OnMouseMove(e);
        }

        protected override void OnFocusedChanged(FocusedChangedEventArgs e)
        {
            if (_sceneReady) Presentation.ResetRenderLook();
            base.OnFocusedChanged(e);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            Presentation.OnMouseWheel(e.OffsetY);
            base.OnMouseWheel(e);
        }

        /// <summary>
        /// One code point, from whatever layout the player actually types on.
        /// The key events above cannot answer this: GLFW reports physical
        /// keys, so reading a message out of them would spell it in US QWERTY
        /// whoever wrote it.
        /// </summary>
        protected override void OnTextInput(TextInputEventArgs e)
        {
            Mods.Chat.ChatBox.HandleText(e.Unicode);
            base.OnTextInput(e);
        }

        protected override void OnKeyDown(KeyboardKeyEventArgs e)
        {
            // First, before anything else claims a key. While the chat prompt
            // is up every key belongs to it: Escape closes the prompt rather
            // than the match, Space types a space rather than jumping, and
            // Enter sends. When it is down, this is only looking for the one
            // key that opens it.
            //
            // Not while a demo is playing: there is nobody to send to, and
            // whatever was said in that match is already in the file.
            if (Mods.Chat.ChatBox.HandleKeyDown(e,
                canOpen: !Mods.Network.DemoPlayback.IsActive
                    && (Presentation.CameraMode == CameraMode.Player || Presentation.IsFreeCam)))
            {
                base.OnKeyDown(e);
                return;
            }
            // F11 and Alt+Enter switch window modes.
            if (Mods.WindowMode.HandleKey(this, e))
            {
                base.OnKeyDown(e);
                return;
            }
            // Space, while watching rather than playing: the free no-clip
            // camera instead of riding along with whoever spectator mode has
            // the camera on, and back again. Not a real control in either
            // case -- a demo freezes every player's input for the whole
            // session, and a spectator's own input is dropped by
            // PlayerInput.ProcessInput -- so there is nothing this can
            // conflict with, and it is how a spectator gets back to the
            // overview they started in.
            if (e.Key == Keys.Space
                && (Mods.Network.DemoPlayback.IsActive || Mods.SpectatorMode.IsSpectating))
            {
                if (Mods.Network.DemoPlayback.IsActive)
                {
                    Presentation.ToggleFreeCamera();
                }
                else
                {
                    // Spectating a live match: the map or a player, never the
                    // hidden body you left behind. See SpectatorMode.
                    Mods.SpectatorMode.ToggleView();
                }
                base.OnKeyDown(e);
                return;
            }
            // Escape opens the pause menu while a player is being driven --
            // the mouse comes back, the window can be resized, and the
            // settings are reachable without leaving the match. Everywhere
            // else it still means "close this" -- except the spectator's free
            // camera, which is CameraMode.Roam and would otherwise fall
            // through to that and quit the game instead of pausing it, taking
            // "Rejoin match" with it.
            if (e.Key == Keys.Escape && (Presentation.CameraMode == CameraMode.Player || Presentation.IsFreeCam)
                && Mods.PauseMenu.HandleEscape(this))
            {
                base.OnKeyDown(e);
                return;
            }
            if (e.Key == Keys.Escape)
            {
                Presentation.DoCleanup();
                Close();
            }
            else
            {
                Presentation.OnKeyDown(e);
            }
            base.OnKeyDown(e);
        }
    }

}
