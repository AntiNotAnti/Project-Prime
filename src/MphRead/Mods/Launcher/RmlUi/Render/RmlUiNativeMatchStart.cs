#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Network;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private static RmlUiDocumentToken _matchStartDocument;

    private static void DrawNativeMatchStart(RenderWindow window)
    {
        if (!BeforeLauncherComposite(window)) return;
        var host = _host!;
        try
        {
            int width = window.FramebufferSize.X, height = window.FramebufferSize.Y;
            float density = Density(window);
            if (_width != width || _height != height || _density != density)
            {
                NativeViewportChanged();
                host.Resize(width, height, density);
                _width = width; _height = height; _density = density;
            }
            if (!host.IsAlive(_matchStartDocument))
            {
                // A separate foreground document covers any open native sheet.
                // Never render launcher Hunter previews into the loaded world.
                ReleaseNativeInput(host);
                _matchStartDocument = _pages!.OpenModal(new("lobby-start", "pages/lobby/lobby.rml", ""));
            }
            _launcherPages?.PresentChrome(_matchStartDocument);
            RmlUiApplicationHeader.Apply(host, _matchStartDocument, MphRead.Mods.Launcher.Gui.Shell.StudioWindow, RmlUiHeaderTab.Play);
            host.SetBool(_matchStartDocument, "class:screen:lobby", true);
            host.SetBool(_matchStartDocument, "visible:lobby_countdown", true);
            var start = RmlUiLobbyStartPresentation.Read(NetSession.ServerSession, NetSession.StartCountdownRemainingSeconds);
            // The network can commit during the scene tick. Keep this final
            // native frame until BeforeFrame releases the shell loading owner.
            if (start.Number.Length == 0) start = new("READY", "DEPLOYING TO ARENA");
            host.SetText(_matchStartDocument, "lobby_countdown_number", start.Number);
            host.SetText(_matchStartDocument, "lobby_countdown_detail", start.Detail);
            host.SetText(_matchStartDocument, "lobby_countdown_map", NetSession.ServerSession?.Match.RoomKey ?? "");
            host.DiscardIntents();
            host.Update();
            RmlUiHunterStageRenderer.Hide();
            // Paint a complete native surface; neither the Avalonia bitmap nor
            // the frozen gameplay scene supplies the deployment screen.
            GL.Viewport(0, 0, width, height);
            GL.ClearColor(0.008f, 0.035f, 0.06f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        catch (Exception error) when (Recoverable(error)) { Fail(error); }
    }

    private static void CloseNativeMatchStart()
    {
        if (_matchStartDocument == default) return;
        if (_host?.IsAlive(_matchStartDocument) == true && _pages?.Top == _matchStartDocument)
            _pages.CloseModal();
        _matchStartDocument = default;
    }
}
#endif
