using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

internal static class Program
{
    // The existing ScreenCapture gate counts pixels with any RGB component > 8.
    private const double MinimumLitFraction = .01;
    private static int _checks;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize(int width, int height, float density, [MarshalAs(UnmanagedType.LPUTF8Str)] string assets);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Empty();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Render(int width, int height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Key(int key, int down, int modifiers);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Move(int x, int y, int modifiers);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Mouse(int button, int down, int modifiers);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Take([Out] byte[] buffer, int capacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetBool([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Bounds(out float x, out float y, out float width, out float height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ElementBounds(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string id, out float x, out float y, out float width, out float height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetField(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadField(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string id, [Out] byte[] buffer, int capacity);
    private static readonly GLFWCallbacks.ErrorCallback Error = (code, message) => Console.Error.WriteLine($"GLFW {code}: {message}");

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args.Length % 2 != 1)
            throw new ArgumentException("Usage: rmlui-route-check <native-bridge> [--assets <directory>] [--capture <directory>]");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string bridge = Path.GetFullPath(args[0]);
        string assets = Path.Combine(AppContext.BaseDirectory, "rmlui");
        string? captureDirectory = null;
        for (int argument = 1; argument < args.Length; argument += 2)
        {
            if (args[argument] == "--assets") assets = Path.GetFullPath(args[argument + 1]);
            else if (args[argument] == "--capture") captureDirectory = Path.GetFullPath(args[argument + 1]);
            else throw new ArgumentException("Unknown option: " + args[argument]);
        }
        if (!File.Exists(bridge) || !File.Exists(Path.Combine(assets, "prime_home.rml")))
            throw new FileNotFoundException("Build the optional native bridge and supply the shipped RmlUi assets before running this display-dependent check.");

        Console.WriteLine("Native RmlUi regression: creating a compatibility OpenGL window.");
        GLFWProvider.SetErrorCallback(Error);
        GLFW.InitHint(InitHintBool.CocoaChdirResources, false);
        GLFW.InitHint(InitHintBool.CocoaMenubar, false);
        GLFWProvider.EnsureInitialized();
        GLFW.DefaultWindowHints();
        using var window = new NativeWindow(new NativeWindowSettings
        {
            ClientSize = new(640, 360), Title = "Project Prime native RmlUi regression",
            StartVisible = true, StartFocused = false, API = ContextAPI.OpenGL,
            APIVersion = new(2, 1), Profile = ContextProfile.Any, Flags = ContextFlags.Default
        });
        window.MakeCurrent();
        GL.LoadBindings(new GLFWBindingsContext());
        NativeWindow.ProcessWindowEvents(false);
        double scaleX = window.FramebufferSize.X / (double)window.ClientSize.X;
        double scaleY = window.FramebufferSize.Y / (double)window.ClientSize.Y;
        Console.WriteLine($"Native RmlUi regression: current context {GL.GetString(StringName.Version)}, framebuffer scale {scaleX}x{scaleY}.");
        nint module = NativeLibrary.Load(bridge);
        T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
        var initialize = Load<Initialize>("pp_rmlui_initialize");
        var shutdown = Load<Empty>("pp_rmlui_shutdown");
        var update = Load<Empty>("pp_rmlui_update");
        var render = Load<Render>("pp_rmlui_render");
        var key = Load<Key>("pp_rmlui_key");
        var move = Load<Move>("pp_rmlui_mouse_move");
        var mouse = Load<Mouse>("pp_rmlui_mouse_button");
        var take = Load<Take>("pp_rmlui_take_action");
        var set = Load<SetBool>("pp_rmlui_set_bool");
        var bounds = Load<Bounds>("pp_rmlui_studio_bounds");
        var elementBounds = Load<ElementBounds>("pp_rmlui_element_bounds");
        var setField = Load<SetField>("pp_rmlui_set_field");
        var readField = Load<ReadField>("pp_rmlui_read_field");
        byte[] actionBuffer = new byte[128];
        void Input(int code, int modifiers = 0)
        {
            key(code, 1, modifiers); key(code, 0, modifiers); update();
        }
        void Click(string element, float density, string action)
        {
            Require(elementBounds(element, out float x, out float y,
                out float width, out float height) == 1
                && width > 0 && height > 0,
                $"{element} is visible and has real native hit bounds");
            move((int)(x + width / 2), (int)(y + height / 2), 0);
            mouse(0, 1, 0);
            mouse(0, 0, 0);
            update();
            Action($"{element} click", density, action);
        }

        void Action(string gesture, float density, string expected)
        {
            Array.Clear(actionBuffer);
            int count = take(actionBuffer, actionBuffer.Length);
            Require(count > 0 && count <= actionBuffer.Length
                && Encoding.UTF8.GetString(actionBuffer, 0, count) == expected,
                $"{gesture} density={density} emits {expected} through the actual native DOM");
            Require(take(actionBuffer, actionBuffer.Length) == 0,
                $"{gesture} density={density} emits exactly one action");
        }

        try
        {
            int index = 0;
            foreach (var test in new[]
            {
                (Physical: new Vector2i(1280, 720), Density: 1f),
                (Physical: new Vector2i(2560, 1440), Density: 2f),
                (Physical: new Vector2i(2560, 1440), Density: 1f)
            })
            {
                window.ClientSize = new((int)Math.Round(test.Physical.X / scaleX), (int)Math.Round(test.Physical.Y / scaleY));
                NativeWindow.ProcessWindowEvents(false);
                Vector2i physical = window.FramebufferSize;
                Require(physical == test.Physical, $"measured framebuffer matches requested {test.Physical}, density={test.Density}");
                Require(initialize(physical.X, physical.Y, test.Density, assets) == 1,
                    $"current real OpenGL context initializes the shipped document at {physical}, density={test.Density}");
                set("reduce_motion", 1); update();
                GL.Viewport(0, 0, physical.X, physical.Y);
                GL.ClearColor(0, 0, 0, 0);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
                render(physical.X, physical.Y);
                byte[] rgb = new byte[physical.X * physical.Y * 3];
                GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
                GL.ReadPixels(0, 0, physical.X, physical.Y, PixelFormat.Rgb, PixelType.UnsignedByte, rgb);
                int lit = 0;
                for (int pixel = 0; pixel < rgb.Length; pixel += 3)
                    if (rgb[pixel] > 8 || rgb[pixel + 1] > 8 || rgb[pixel + 2] > 8) lit++;
                double fraction = lit / (double)(physical.X * physical.Y);
                // Preserve a failed framebuffer too, so CI artifacts show actual evidence.
                if (captureDirectory is not null)
                {
                    Directory.CreateDirectory(captureDirectory);
                    WritePng(Path.Combine(captureDirectory, $"native-rmlui-{index++}-{physical.X}x{physical.Y}-density{test.Density}.png"), physical.X, physical.Y, rgb);
                }
                Require(fraction >= MinimumLitFraction,
                    $"real RGB framebuffer {physical}, density={test.Density} exceeds unchanged 1% lit gate: {fraction:P3}");
                window.Context.SwapBuffers();

                // Exercise the real lobby-mode data binding and READY action,
                // not a synthetic command. Switching modes requests focus on
                // #lobby_ready after the native model update.
                set("lobby_mode", 1);
                set("slot0_occupied", 1);
                set("slot0_local", 1);
                set("lobby_owner", 1);
                update();
                Input(2);
                Action("live lobby READY Enter", test.Density, "lobby:ready");
                set("lobby_mode", 0);
                update();

                // Home focus is restored to activity_selector. Native Shift-Tab
                // reaches profile then nav_studio; Enter dispatches its DOM callback.
                Input(1, 1); Input(1, 1); Input(2);
                Action("keyboard Enter", test.Density, "studio:open");
                Require(bounds(out float x, out float y, out float width, out float height) == 1
                    && float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(width) && float.IsFinite(height)
                    && width > 0 && height > 0 && x >= 0 && y >= 0 && x + width <= physical.X && y + height <= physical.Y,
                    "bounded actual STUDIO control fits the framebuffer");
                move((int)(x + width / 2), (int)(y + height / 2), 0);
                mouse(0, 1, 0); mouse(0, 0, 0); update(); Action("mouse click", test.Density, "studio:open");

                // The migration must have real pointer-editable inputs and
                // dispatched Create/Join actions in the native DOM. Network
                // allocation is deliberately not performed by this pure UI gate.
                set("multiplayer_mode", 1);
                set("play_browser_mode", 1);
                update();
                Click("play_create_open", test.Density, "play:create-open");
                setField("play_create_name", "RML Native Lobby");
                update();
                byte[] fieldBuffer = new byte[256];
                int fieldLength = readField("play_create_name", fieldBuffer, fieldBuffer.Length);
                Require(fieldLength > 0
                    && Encoding.UTF8.GetString(fieldBuffer, 0, fieldLength) == "RML Native Lobby",
                    "native Create Lobby text field retains user input");
                Click("play_create_submit", test.Density, "play:create");
                Click("play_create_cancel", test.Density, "play:browse");
                setField("play_join_address", "127.0.0.1:27888");
                update();
                fieldLength = readField("play_join_address", fieldBuffer, fieldBuffer.Length);
                Require(fieldLength > 0
                    && Encoding.UTF8.GetString(fieldBuffer, 0, fieldLength) == "127.0.0.1:27888",
                    "native Direct Connect field returns the edited endpoint");
                Click("play_join", test.Density, "play:join");
                Click("play_back", test.Density, "play:cancel");
                shutdown(); Require(take(actionBuffer, actionBuffer.Length) == 0, "native shutdown clears pending action ownership");
            }
        }
        finally { shutdown(); NativeLibrary.Free(module); }
        Console.WriteLine($"Native RmlUi checks passed: {_checks}. OpenGL: {GL.GetString(StringName.Version)}.");
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++; Console.WriteLine("RML NATIVE PASS " + message);
    }

    private static void WritePng(string path, int width, int height, byte[] bottomUpRgb)
    {
        using var output = File.Create(path);
        output.Write(new byte[] {137, 80, 78, 71, 13, 10, 26, 10});
        void Chunk(string type, byte[] bytes)
        {
            byte[] length = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length); output.Write(length);
            byte[] name = Encoding.ASCII.GetBytes(type); output.Write(name); output.Write(bytes);
            uint crc = 0xffffffff;
            foreach (byte value in name.Concat(bytes))
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
            }
            BinaryPrimitives.WriteUInt32BigEndian(length, ~crc); output.Write(length);
        }
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 2; Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            for (int y = height - 1; y >= 0; y--) { zlib.WriteByte(0); zlib.Write(bottomUpRgb, y * width * 3, width * 3); }
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", Array.Empty<byte>());
    }
}
