using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Render;
using OpenTK.Windowing.Desktop;

internal static class CanvasProgram
{
    private static int Main(string[] args)
    {
        if (args.Length == 4 && args[1] == "--character-source-owned")
        {
            string[] size = args[3].Split('x');
            if (size.Length != 2 || !int.TryParse(size[0], out int w) || !int.TryParse(size[1], out int h)
                || w is < 320 or > 4096 || h is < 240 or > 4096) throw new ArgumentException("Invalid source preview viewport.");
            return CharacterSourceGlAcceptance.Run(Path.GetFullPath(args[0]), Path.GetFullPath(args[2]), w, h);
        }
        if (args.Length is < 1 or > 4) throw new ArgumentException("Pass runtime RmlUi assets and optional WIDTHxHEIGHT, --draw-list, --credits [WIDTHxHEIGHT], or --credits-owned OWNED_ROOT [WIDTHxHEIGHT].");
        if (args.Length == 2 && args[1] == "--draw-list") return CanvasDrawListAcceptance.Run(Path.GetFullPath(args[0]));
        bool legacy = args.Length >= 2 && args[1] == "--ending-legacy";
        bool credits = args.Length >= 2 && args[1] is "--credits" or "--credits-owned";
        bool owned = credits && args[1] == "--credits-owned";
        if ((owned && args.Length < 3) || (!credits && !legacy && args.Length > 2) || ((credits && !owned || legacy) && args.Length > 3))
            throw new ArgumentException("Invalid credits acceptance arguments.");
        string? ownedRoot = owned ? args[2] : null;
        int sizeIndex = owned ? 3 : credits || legacy ? 2 : 1;
        int width = 960, height = 600;
        if (args.Length > sizeIndex)
        {
            string[] size = args[sizeIndex].Split('x');
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
        if (legacy) EndingGlAcceptance.Run(host, width, height);
        else if (credits) CreditsPresentationGlAcceptance.Run(host, width, height, Path.GetFullPath(args[0]), ownedRoot);
        else CanvasGlAcceptance.Run(host, width, height, Path.GetFullPath(args[0]));
        return 0;
    }
}
