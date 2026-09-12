namespace MphRead.Mods
{
    public enum WindowStartMode
    {
        Windowed,
        BorderlessFullscreen
    }

    /// <summary>
    /// Window-mode preference and the active fullscreen state exposed to the
    /// launcher and pause UI. The SDL host owns all native window operations;
    /// this type intentionally contains no platform window handle or API.
    /// </summary>
    public static class WindowMode
    {
        /// <summary>
        /// How the next window should open. Set by the launcher from its
        /// saved preference, or by -fullscreen on the command line.
        /// </summary>
        public static WindowStartMode Startup { get; set; } = WindowStartMode.Windowed;

        public static bool IsFullscreen { get; private set; }

        /// <summary>
        /// Called by the active platform host after a native mode transition.
        /// Keeping the state update here lets the launcher and pause UI expose
        /// the result without acquiring or retaining a native window handle.
        /// </summary>
        internal static void SetFullscreenState(bool fullscreen)
            => IsFullscreen = fullscreen;

        /// <summary>"borderless"/"fullscreen"/"windowed" from a settings file or a flag.</summary>
        public static WindowStartMode Parse(string? value, WindowStartMode fallback)
        {
            if (value == null)
            {
                return fallback;
            }
            string text = value.Trim().ToLowerInvariant();
            if (text is "borderless" or "fullscreen" or "borderless fullscreen" or "1" or "true")
            {
                return WindowStartMode.BorderlessFullscreen;
            }
            if (text is "windowed" or "window" or "0" or "false")
            {
                return WindowStartMode.Windowed;
            }
            return fallback;
        }
    }
}
