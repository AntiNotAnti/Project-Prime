using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Render;
using OpenTK.Windowing.Desktop;

internal static class CanvasProgram
{
    private static int Main(string[] args)
    {
        if (args.Length is < 1 or > 2) throw new ArgumentException("Pass runtime RmlUi assets and optional WIDTHxHEIGHT.");
        int width = 960, height = 600;
        if (args.Length == 2)
        {
            string[] size = args[1].Split('x');
            if (size.Length != 2 || !int.TryParse(size[0], out width) || !int.TryParse(size[1], out height)
                || width is < 320 or > 4096 || height is < 240 or > 4096) throw new ArgumentException("Invalid canvas viewport.");
        }
        Environment.SetEnvironmentVariable("PROJECT_PRIME_GL_PROFILE", "1");
        GraphicsBackendPolicy.Configure("opengl");
        var settings = DesktopGlContext.Settings(background: true); settings.ClientSize = new(width, height);
        using var window = new NativeWindow(settings);
        using var context = new DesktopGraphicsSession(window);
        width = window.FramebufferSize.X; height = window.FramebufferSize.Y;
        Console.WriteLine($"CANVAS GL FRAMEBUFFER {width}x{height}");
        using var host = new RmlUiHost();
        if (!host.Initialize(width, height, 1, Path.GetFullPath(args[0]))) throw new InvalidOperationException("Canvas GL host initialization failed.");
        CanvasGlAcceptance.Run(host, width, height, Path.GetFullPath(args[0]));
        return 0;
    }
}
