#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using MphRead.Mods.Launcher.RmlUi.Host;
using OpenTK.Graphics.OpenGL;
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
    private static bool _attempted, _disabled;
    private static int _width, _height;
    private static float _density;

    internal static bool CapturesInput => !_disabled && _host is { Active: true };

    internal static void Draw(RenderWindow window)
    {
        if (!Requested || _disabled || window.FramebufferSize.X <= 0 || window.FramebufferSize.Y <= 0)
            return;
        if (!TryStart(window)) return;
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
            host.Update();

            // The GL2 backend modifies the fixed-function state and stencil.
            // Its native adapter scopes that state; this owner-thread guard
            // also restores the GLSL program, active texture and BOTH FBO
            // bindings, which fixed-function glPushAttrib does not cover.
            int drawFramebuffer = GL.GetInteger(GetPName.DrawFramebufferBinding);
            int readFramebuffer = GL.GetInteger(GetPName.ReadFramebufferBinding);
            int program = GL.GetInteger(GetPName.CurrentProgram);
            int activeTexture = GL.GetInteger(GetPName.ActiveTexture);
            try
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                GL.UseProgram(0);
                GL.ActiveTexture(TextureUnit.Texture0);
                host.Render(width, height);
            }
            finally
            {
                GL.ActiveTexture((TextureUnit)activeTexture);
                GL.UseProgram(program);
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, drawFramebuffer);
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, readFramebuffer);
            }

            // Only Quit is wired for this isolated smoke. The shipping shell,
            // match, and services own all other intents until their new
            // presentation-neutral controllers are migrated.
            for (int i = 0; i < 64 && host.TryTakeIntent(out RmlUiIntent intent); i++)
            {
                if (intent.Kind == RmlUiIntentKind.Quit)
                    window.Close();
            }
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
            Mods.DebugLog.Line("rmlui", "opt-in OpenGL smoke session active");
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
        => Forward(host => RmlUiDesktopInput.KeyDown(host, key));

    internal static bool KeyUp(KeyboardKeyEventArgs key)
        => Forward(host => RmlUiDesktopInput.KeyUp(host, key));

    internal static bool TextInput(string text)
        => Forward(host => host.Input.Text(text));

    private static bool Forward(Action<RmlUiHost> action)
    {
        RmlUiHost? host = _host;
        if (_disabled || host is not { Active: true }) return false;
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
