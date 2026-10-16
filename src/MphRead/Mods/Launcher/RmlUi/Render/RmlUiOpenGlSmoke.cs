#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.Gui;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.RmlUi.Render;

/// <summary>
/// Explicit developer smoke of the historical native RmlUi GL2 renderer on the
/// *current* RenderWindow. The existing Avalonia shell remains authoritative.
/// This does not accept lobby, account, gameplay, or Community actions.
/// </summary>
internal static class RmlUiOpenGlSmoke
{
    private static readonly bool Requested =
        String.Equals(Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_GL_SMOKE"),
            "1", StringComparison.Ordinal);

    private static RmlUiHost? _host;
    private static RmlUiMenuController? _controller;
    private static long _lastPresentedRevision;
    private static bool _shown;
    private static bool _attempted, _disabled;
    private static int _width, _height;
    private static float _density;

    internal static bool CapturesInput => !_disabled && _shown && _host is { Active: true };

    /// <summary>
    /// Called before compositing the shell on a scene-free window. A positive
    /// result means RmlUi has initialized and will paint this frame; Avalonia's
    /// chrome must NOT also be drawn underneath its translucent page.
    /// </summary>
    internal static bool BeforeLauncherComposite(RenderWindow window)
    {
        if (!Requested || _disabled || window.HasScene
            || window.FramebufferSize.X <= 0 || window.FramebufferSize.Y <= 0
            || !Shell.RmlUiCanPresent)
        {
            if (_shown && _host is { Active: true } current) current.ReleaseInput();
            _shown = false;
            return false;
        }
        if (!TryStart(window)) { _shown = false; return false; }
        _shown = true;
        return true;
    }

    internal static void Draw(RenderWindow window)
    {
        if (!BeforeLauncherComposite(window)) return;
        RmlUiHost host = _host!;
        try
        {
            int width = window.FramebufferSize.X;
            int height = window.FramebufferSize.Y;
            float density = Density(window);
            if (_width != width || _height != height || _density != density)
            {
                host.Resize(width, height, density);
                _width = width;
                _height = height;
                _density = density;
            }
            host.Input.SetFramebufferScale(
                (float)width / Math.Max(1, window.ClientSize.X),
                (float)height / Math.Max(1, window.ClientSize.Y));
            // Native events are validated by the versioned ABI. Application
            // commands go to one owner-thread controller, not native callbacks
            // or a second matchmaking/network pump.
            RmlUiMenuController controller = _controller!;
            for (int i = 0; i < 64 && host.TryTakeIntent(out RmlUiIntent intent); i++)
            {
                string address = intent.Kind == RmlUiIntentKind.PlayJoin
                    ? host.ReadField(host.HomeDocument, "play_join_address", 160) : "";
                string player = intent.Kind is RmlUiIntentKind.PlayJoin or RmlUiIntentKind.PlayServer
                    ? host.ReadField(host.HomeDocument, "play_player_name", 128) : "";
                string ruleTime = intent.Kind == RmlUiIntentKind.LobbyRulesApply
                    ? host.ReadField(host.HomeDocument, "rules_time", 24) : "";
                string ruleGoal = intent.Kind == RmlUiIntentKind.LobbyRulesApply
                    ? host.ReadField(host.HomeDocument, "rules_goal", 24) : "";
                string createName = intent.Kind == RmlUiIntentKind.PlayCreate
                    ? host.ReadField(host.HomeDocument, "play_create_name", 128) : "";
                if (intent.Kind == RmlUiIntentKind.PlayCreate)
                    player = host.ReadField(host.HomeDocument, "play_create_player_name", 128);
                bool handled = controller.Dispatch(intent.Kind, intent.Argument,
                    address, player, ruleTime, ruleGoal, createName);
                if (handled && intent.Kind is RmlUiIntentKind.LobbyRulesOpen
                    or RmlUiIntentKind.LobbyRulesMode or RmlUiIntentKind.LobbyRulesFormat)
                {
                    if (controller.RuleInputs is { } fields)
                    {
                        host.SetField(host.HomeDocument, "rules_time", fields.Time);
                        host.SetField(host.HomeDocument, "rules_goal", fields.Goal);
                    }
                }
            }
            // A native navigation may have opened the existing setup/create
            // modal or legacy workspace within this very frame. Yield capture
            // immediately, not one presented frame later.
            if (!Shell.RmlUiCanPresent)
            {
                _shown = false;
                host.ReleaseInput();
                return;
            }
            RmlUiMenuSnapshot snapshot = controller.Tick();
            if (snapshot.Revision > _lastPresentedRevision)
            {
                host.Present(new RmlUiBindingSnapshot(host.HomeDocument,
                    snapshot.Revision, snapshot.Bindings));
                _lastPresentedRevision = snapshot.Revision;
            }
            host.Update();
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        catch (Exception error) when (Recoverable(error))
        {
            Fail(error);
        }
    }

    private static bool TryStart(RenderWindow window)
    {
        if (_host is { Active: true }) return true;
        if (_attempted) return false;
        _attempted = true;
        string root = Path.Combine(AppContext.BaseDirectory, "rmlui");
        if (!File.Exists(Path.Combine(root, "prime_home.rml")))
        {
            Fail(new FileNotFoundException("Opt-in RmlUi home document is missing.", root));
            return false;
        }

        try
        {
            var host = new RmlUiHost();
            _host = host;
            int width = window.FramebufferSize.X;
            int height = window.FramebufferSize.Y;
            float density = Density(window);
            if (!host.Initialize(width, height, density, root, RmlUiRenderBackend.OpenGl))
            {
                Fail(new InvalidOperationException("Native RmlUi refused the OpenGL session."));
                return false;
            }
            _width = width;
            _height = height;
            _density = density;
            _controller = new RmlUiMenuController(new RmlUiLiveMenuServices());
            _lastPresentedRevision = 0;
            string player = HubState.Capture().PlayerName;
            host.SetField(host.HomeDocument, "play_player_name", player);
            host.SetField(host.HomeDocument, "play_create_player_name", player);
            Mods.DebugLog.Line("rmlui", "opt-in native Home/Play/Lobby session active");
            return true;
        }
        catch (Exception error) when (Recoverable(error))
        {
            Fail(error);
            return false;
        }
    }

    private static float Density(RenderWindow window)
    {
        float value = (float)window.FramebufferSize.X / Math.Max(1, window.ClientSize.X);
        return Single.IsFinite(value) && value > 0 ? value : 1;
    }

    internal static bool PointerMoved(double x, double y)
        => Forward(host => host.Input.PointerMoved(x, y));

    internal static bool PointerButton(MouseButton button, double x, double y, bool down)
    {
        int code = button switch
        {
            MouseButton.Button1 => 0,
            MouseButton.Button2 => 1,
            MouseButton.Button3 => 2,
            _ => -1
        };
        return Forward(host =>
        {
            if (code >= 0) host.Input.PointerButton(code, x, y, down);
        });
    }

    internal static bool Wheel(double delta)
        => Forward(host => host.Input.PointerWheel(delta));

    internal static bool KeyDown(KeyboardKeyEventArgs key)
    {
        // Window-level F11 and Alt+Enter must remain operational while the
        // native menu has focus. The OpenTK window, not either UI toolkit,
        // owns fullscreen changes.
        if (key.Key == Keys.F11 || (key.Key == Keys.Enter && key.Alt))
            return false;
        return Forward(host => RmlUiDesktopInput.KeyDown(host, key));
    }

    internal static bool KeyUp(KeyboardKeyEventArgs key)
        => Forward(host => RmlUiDesktopInput.KeyUp(host, key));

    internal static bool TextInput(string text)
        => Forward(host => host.Input.Text(text));

    private static bool Forward(Action<RmlUiHost> action)
    {
        RmlUiHost? host = _host;
        if (_disabled || !_shown || host is not { Active: true }) return false;
        try
        {
            action(host);
            return true;
        }
        catch (Exception error) when (Recoverable(error))
        {
            Fail(error);
            return false;
        }
    }

    private static bool Recoverable(Exception error)
        => error is not OutOfMemoryException and not StackOverflowException
            and not AccessViolationException;

    private static void Fail(Exception error)
    {
        _disabled = true;
        try { Console.Error.WriteLine("[rmlui] developer GL smoke disabled: " + error.Message); }
        catch { }
        try { Mods.DebugLog.Line("rmlui", "developer GL smoke disabled: " + error.Message); }
        catch { }
        Shutdown();
    }

    internal static void Shutdown()
    {
        _shown = false;
        RmlUiMenuController? controller = _controller;
        _controller = null;
        if (controller != null)
        {
            try { controller.Dispose(); }
            catch (Exception error) when (Recoverable(error))
            {
                try { Mods.DebugLog.Line("rmlui", "controller teardown: " + error.Message); }
                catch { }
            }
        }
        var old = _host;
        _host = null;
        if (old == null) return;
        try { old.Dispose(); }
        catch (Exception error) when (Recoverable(error))
        {
            try { Mods.DebugLog.Line("rmlui", "native teardown: " + error.Message); }
            catch { }
        }
    }
}
#endif
