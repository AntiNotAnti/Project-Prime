using System;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render
{
#if MPHREAD_SERVER
    // RenderWindow remains in the server build, but it owns no UI raster or GPU.
    public static class UiOverlay
    {
        public static bool Visible { get; set; }
        public static bool HasFrame => false;
        public static void Upload(IntPtr pixels, int width, int height) { }
        public static void Draw(int width, int height) { }
        public static void DrawAlone(RenderWindow window, int width, int height) { }
        public static void Release() => Visible = false;
        internal static void ForgetRendererResources() => Visible = false;
    }
#else
    /// <summary>
    /// The launcher's own picture, drawn inside the game window.
    ///
    /// This is the GL half of the unified window and knows nothing about the
    /// toolkit that produced the pixels: it is handed a buffer of RGBA and
    /// puts it on the screen as one quad over whatever the frame already
    /// holds. <see cref="Launcher.Gui.UiSurface"/> is the other half.
    ///
    /// Why a texture at all: the front screen, the pause menu and the settings
    /// are Avalonia, and Avalonia cannot draw into a GL context the engine
    /// owns. They used to be real windows laid over the game -- borderless,
    /// topmost, chasing the game window on every drag -- which is a second
    /// thing on the desktop pretending to be part of the first, and reads as
    /// exactly that. Rendered offscreen and uploaded, they are part of the
    /// frame: they cannot be alt-tabbed away from the game, cannot be left
    /// behind when it moves, and go fullscreen with it because there is only
    /// one window to go fullscreen.
    ///
    /// Premultiplied alpha, because that is what Avalonia renders. Blending
    /// with SrcAlpha instead would darken every edge of every glyph against
    /// the match behind the pause menu.
    /// </summary>
    public static class UiOverlay
    {
        private static readonly FullscreenUiOverlay _overlay = new();

        /// <summary>Whether the last uploaded frame should be drawn.</summary>
        public static bool Visible { get; set; }

        /// <summary>True once a frame has been uploaded and not released.</summary>
        public static bool HasFrame => _overlay.HasFrame;

        /// <summary>
        /// Take a rendered UI frame. Tightly packed RGBA, top row first, which
        /// is what the headless surface hands over.
        /// </summary>
        public static void Upload(IntPtr pixels, int width, int height)
        {
            _overlay.Upload(pixels, width, height);
        }

        /// <summary>
        /// Put it on the screen, over everything drawn so far.
        ///
        /// Fixed function rather than the scene's shader: what this draws is
        /// one screen-filling quad in normalised coordinates, which is the one
        /// thing the fixed pipeline does without setting anything up, and the
        /// scene's programs all expect uniforms that belong to a world.
        /// </summary>
        public static void Draw(int width, int height)
        {
            if (Visible) _overlay.Draw(width, height);
        }

        /// <summary>
        /// A frame with no match behind it: the front screen is the whole
        /// picture, so there is nothing to compose it over.
        /// </summary>
        public static void DrawAlone(RenderWindow window, int width, int height)
        {
            GL.Viewport(0, 0, Math.Max(width, 1), Math.Max(height, 1));
            GL.ClearColor(0f, 0f, 0f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit
                | ClearBufferMask.StencilBufferBit);
#if MPHREAD_RMLUI_POC
            bool rmlStage = Mods.Launcher.Gui.RmlUiPrototype.Active;
            if (rmlStage)
            {
                // The RmlUi home now owns a purpose-built deployment chamber,
                // not a photographed gameplay room. Activity ambience and
                // Hunter identity feed this lightweight procedural stage.
                LauncherPhoto.StageFxEnabled = false;
                LauncherBackdropComposer.Enabled = true;
                LauncherStageFx.Enabled = true;
                LauncherBackdropComposer.Draw(width, height);

                // Grounding and atmosphere remain separate passes around the
                // real engine Hunter so the model keeps its actual materials.
                LauncherStageFx.DrawUnderHunter(width, height);
                LauncherHunter.CinematicLighting = true;
                try
                {
                    // Home draws one hero. The live lobby reuses the same real
                    // engine preview renderer for every occupied chevron slot,
                    // with the local Hunter remaining the largest/front anchor.
                    Mods.Launcher.Gui.RmlUiPrototype.DrawHunters(window, width, height);
                }
                finally
                {
                    LauncherHunter.CinematicLighting = false;
                    Scene.LauncherPreviewCinematicLighting = false;
                }
                LauncherStageFx.DrawOverHunter(width, height);

                // The fixed-function RmlUi renderer samples unit 0. The scene
                // and preview are free to leave a different unit active.
                GL.ActiveTexture(TextureUnit.Texture0);
                Mods.Launcher.Gui.RmlUiPrototype.Render(width, height);
                return;
            }
#endif
            LauncherBackdropComposer.Enabled = false;
            LauncherStageFx.Enabled = false;
            LauncherPhoto.StageFxEnabled = false;

            // The production Avalonia shell still uses its established room
            // photograph path. The deployment chamber is an RmlUi proof
            // concern until that renderer migration is explicitly promoted.
            LauncherPhoto.Draw(width, height);
#if MPHREAD_SHELL
            Mods.Launcher.Gui.UiSurface.Current?.DrawMapViewport(width, height);
#endif
            Draw(width, height);
            // The real hunter, *over* the screens rather than under them.
            //
            // Under was the obvious place and it does not work: the drawer the
            // stand sits in is an opaque panel, drawn by the screens, so a
            // model beneath the texture is a model behind a card. The stand
            // draws nothing at all where the model goes (see HunterStand), and
            // this fills that rectangle afterwards -- it clears it to its own
            // background and paints the hunter, so there is nothing for the
            // panel to have been covering.
            //
            // Only on this frame. With a match up, the results screen's own
            // pass owns the preview and draws it inside the world's target.
            LauncherHunter.Draw(window, width, height);
        }

        /// <summary>Give the texture back. The context has to be current.</summary>
        public static void Release()
        {
            try
            {
                _overlay.Release();
                LauncherStageFx.Release();
                LauncherBackdropComposer.Release();
                LauncherPhoto.StageFxEnabled = false;
            }
            finally { Visible = false; }
        }

        // A failed device reconstruction cannot service graphics-facade calls.
        // Its device teardown owns native destruction; clear context-local IDs
        // without touching Current so the next renderer starts with a full upload.
        internal static void ForgetRendererResources()
        {
            _overlay.Forget();
            LauncherStageFx.ForgetRendererResources();
            LauncherBackdropComposer.ForgetRendererResources();
            LauncherPhoto.StageFxEnabled = false;
            Visible = false;
        }
    }
#endif
}
