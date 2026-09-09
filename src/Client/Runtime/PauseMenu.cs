using System;
using OpenTK.Mathematics;

namespace MphRead.Mods
{
    /// <summary>
    /// The host surface the pause menu needs between frames. Keeping this
    /// independent of an OpenTK window host lets SDL service the same Avalonia
    /// menu.
    /// </summary>
    public interface IPauseMenuHost
    {
        Vector2i ClientLocation { get; }
        Vector2i ClientSize { get; }
        void Focus();
        void Close();
        void SyncTopmost(bool menuOpen);
        void ToggleFullscreen();
    }

    /// <summary>
    /// The menu Escape opens during a match: let go of the mouse, offer the
    /// way out, and put the settings within reach without leaving the game.
    ///
    /// The menu is an Avalonia window on the game's own thread, and talks to
    /// the game through the flags below. It has to be that thread: native
    /// window calls -- closing it, changing its border -- belong to the thread
    /// that created it, macOS accepts windows only on the main one, and Avalonia has
    /// a single UI thread per process in any case. So the menu asks, and
    /// <see cref="Poll"/> does the window work between frames.
    ///
    /// The cost of sharing the thread is that the toolkit only runs when the
    /// game lets it: <see cref="Poll"/> hands it the tail of every frame while
    /// the menu is up, which ties the menu's responsiveness to the frame rate
    /// and is why the match keeps drawing behind it.
    /// </summary>
    public static class PauseMenu
    {
        private static volatile bool _open;
        private static volatile bool _leave;
        private static volatile bool _quit;
        private static volatile bool _toggleFullscreen;
        private static volatile bool _refocus;
        /// <summary>Open right now: the cursor is free and the player is not driving.</summary>
        public static bool Open => _open;

        /// <summary>The player asked to leave the match but not the program.</summary>
        public static bool LeftMatch { get; private set; }

        /// <summary>The player asked to close the program outright.</summary>
        public static bool QuitProgram { get; private set; }

        /// <summary>Where the game window is, so the menu can open over it.</summary>
        public static int WindowX { get; private set; }
        public static int WindowY { get; private set; }
        public static int WindowWidth { get; private set; }
        public static int WindowHeight { get; private set; }

        /// <summary>
        /// True when the game window has moved or been resized since the menu
        /// last laid itself out over it. Cleared by whoever acts on it.
        /// </summary>
        internal static bool WindowMoved { get; set; }

        /// <summary>
        /// Take the game window's client rectangle.
        ///
        /// Called when the menu opens and then once a frame while it is up.
        /// The menu is a borderless window laid over the game rather than
        /// something drawn inside it -- Avalonia cannot render into the GL
        /// context -- so "part of the window" is a thing it has to keep being:
        /// a rectangle sampled once at open time detaches the moment anybody
        /// drags the game window, and what is left behind is exactly the
        /// floating popup this stopped being.
        /// </summary>
        private static void TakeWindowRect(Vector2i location, Vector2i size)
        {
            int x = location.X;
            int y = location.Y;
            int width = size.X;
            int height = size.Y;
            if (x == WindowX && y == WindowY
                && width == WindowWidth && height == WindowHeight)
            {
                return;
            }
            WindowX = x;
            WindowY = y;
            WindowWidth = width;
            WindowHeight = height;
            WindowMoved = true;
        }

        /// <summary>Open/close the pause menu from the active window host.</summary>
        public static bool HandleEscape(IPauseMenuHost host, Scene scene)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            TakeWindowRect(host.ClientLocation, host.ClientSize);
            if (!Launcher.Gui.GuiLauncher.EnsureSetup()) return false;
            if (_open)
            {
                Close();
                return true;
            }
            OpenMenu(scene);
            return true;
        }

        /// <summary>Called once a frame by the active window host.</summary>
        public static void Poll(IPauseMenuHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            PollCore(host);
        }

        private static void PollCore(IPauseMenuHost host)
        {
            if (_open)
            {
                // Before the toolkit's slice, so a drag that happened since
                // the last frame is laid out in this one rather than the next.
                TakeWindowRect(host.ClientLocation, host.ClientSize);
                if (WindowMoved)
                {
                    WindowMoved = false;
                    Launcher.Gui.PauseMenuWindow.FollowGameWindow();
                }
                // The menu's share of this frame. Everything it decided lands
                // in the flags below before they are read.
                Launcher.Gui.GuiLauncher.Pump();
            }
            if (_refocus)
            {
                _refocus = false;
                // Give the keyboard back to the game.
                //
                // Closing the menu does not reliably make the game window the
                // foreground one again -- which window manager decides that,
                // and on what grounds, is a per-platform matter -- and an
                // unfocused game window receives no keys and cannot grab the
                // pointer. The game goes on simulating, so nothing looks
                // crashed: the mouse still moves, the picture still draws, and
                // nothing the player presses arrives. That reads as a freeze.
                try
                {
                    host.Focus();
                }
                catch (Exception)
                {
                    // Not worth losing the match over.
                }
            }
            // The game window floats above the shell while it is fullscreen,
            // and stands down while this menu is up. Here rather than in
            // OpenMenu/Close because both of those are called from the menu's
            // own event handlers, and the native window attribute belongs to
            // the host thread -- which is this one, between frames.
            host.SyncTopmost(_open);
            if (_toggleFullscreen)
            {
                _toggleFullscreen = false;
                host.ToggleFullscreen();
            }
            if (_quit)
            {
                _quit = false;
                QuitProgram = true;
                Close();
                host.Close();
            }
            else if (_leave)
            {
                _leave = false;
                LeftMatch = true;
                Close();
                host.Close();
            }
        }

        /// <summary>Forget what the last match asked for.</summary>
        public static void Reset()
        {
            LeftMatch = false;
            QuitProgram = false;
            _leave = false;
            _quit = false;
        }

        internal static void RequestLeave() => _leave = true;

        internal static void RequestQuit() => _quit = true;

        internal static void RequestFullscreenToggle() => _toggleFullscreen = true;

        internal static void MarkClosed()
        {
            _open = false;
            // Asked for here, done in Poll: this runs inside the toolkit's
            // teardown for the menu window, and handing focus to the game
            // window in the middle of that is how a window manager is given two
            // contradictory instructions in one turn.
            _refocus = true;
        }

        private static void OpenMenu(Scene scene)
        {
            _open = Launcher.Gui.PauseMenuWindow.Open(scene);
        }

        private static void Close()
        {
            Launcher.Gui.PauseMenuWindow.CloseIfOpen();
            _open = false;
        }
    }
}
