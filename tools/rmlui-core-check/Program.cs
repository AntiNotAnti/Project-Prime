using System.Runtime.InteropServices;
using System.Text;

// Uses real RmlUi core and DOM through the draw-list adapter: no display, GPU,
// game data, network session, fake presenter, or Avalonia dependency required.
internal static unsafe class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint Version();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong Id();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Empty();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize(int w, int h, float dpi, [MarshalAs(UnmanagedType.LPUTF8Str)] string root, int backend);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Render(int w, int h);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int layer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Close(ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Focus(ulong id, [MarshalAs(UnmanagedType.LPUTF8Str)] string element);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetText(ulong id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetBool(ulong id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadField(ulong id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [Out] byte[] buffer, int capacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Take(ref Intent intent);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadBytes([Out] byte[] buffer, int capacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Key(int code, int down, int modifiers);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Text(uint codepoint);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Clipboard([MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DrawCommand(int index, ref Command command);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DrawGeometry(ulong id, ref Geometry geometry);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DrawTexture(int index, ref Texture texture);
    [StructLayout(LayoutKind.Sequential)] private struct Intent
    {
        public uint Size, Version, Kind; public int Argument;
        public ulong Generation, Document, Sequence;
        public static Intent New() => new() { Size = 40, Version = 1 };
    }
    [StructLayout(LayoutKind.Sequential)] private struct Command
    {
        public uint Size, Kind; public ulong Geometry, Texture;
        public float X, Y; public int ScissorX, ScissorY, Width, Height;
        public uint Enabled, Operation; public fixed float Transform[16];
    }
    [StructLayout(LayoutKind.Sequential)] private struct Geometry
    {
        public uint Size, Vertices, Indices, Reserved; public nint VertexData, IndexData;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Texture
    {
        public uint Size, Width, Height, Reserved; public ulong Handle; public nint Pixels;
    }
    private static int _checks;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); _checks++; }
    private static int Main(string[] args)
    {
        if (args.Length is not (1 or 3) || (args.Length == 3 && args[1] != "--assets"))
            throw new ArgumentException("Usage: rmlui-core-check <bridge> [--assets <directory>]");
        string source = args.Length == 3 ? Path.GetFullPath(args[2]) : Path.Combine(AppContext.BaseDirectory, "rmlui");
        string root = Path.Combine(Path.GetTempPath(), "prime-rmlui-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string dest = Path.Combine(root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest);
        }
        File.WriteAllText(Path.Combine(root, "contract.rml"), """
            <rml><head><title>Runtime contract</title><style>
            body { font-family: Rajdhani; font-size: 24px; background-color: #102030; }
            button, input { display: block; width: 260px; height: 40px; tab-index: auto; }
            </style></head><body><div id="message">TEXT</div><input id="name" type="text" value="" />
            <button id="submit" data-action="route:settings">SETTINGS</button></body></rml>
            """);
        nint module = NativeLibrary.Load(Path.GetFullPath(args[0]));
        T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
        var version = Load<Version>("pp_rmlui_protocol_version"); var generation = Load<Id>("pp_rmlui_generation");
        var home = Load<Id>("pp_rmlui_home_document"); var initialize = Load<Initialize>("pp_rmlui_initialize_backend");
        var shutdown = Load<Empty>("pp_rmlui_shutdown"); var update = Load<Empty>("pp_rmlui_update"); var render = Load<Render>("pp_rmlui_render");
        var open = Load<Open>("pp_rmlui_document_open"); var close = Load<Close>("pp_rmlui_document_close"); var focus = Load<Focus>("pp_rmlui_document_focus");
        var setText = Load<SetText>("pp_rmlui_document_set_text"); var setBool = Load<SetBool>("pp_rmlui_document_set_bool"); var read = Load<ReadField>("pp_rmlui_document_read_field");
        var count = Load<Count>("pp_rmlui_document_count"); var take = Load<Take>("pp_rmlui_take_intent"); var legacy = Load<ReadBytes>("pp_rmlui_take_action");
        var key = Load<Key>("pp_rmlui_key"); var text = Load<Text>("pp_rmlui_text"); var clipboard = Load<Clipboard>("pp_rmlui_set_clipboard"); var copied = Load<ReadBytes>("pp_rmlui_read_clipboard");
        var inputActive = Load<Count>("pp_rmlui_text_input_active"); var loseFocus = Load<Empty>("pp_rmlui_focus_lost"); var focused = Load<ReadBytes>("pp_rmlui_focused_element");
        var liveGeometryCount = Load<Count>("pp_rmlui_draw_geometry_count");
        var drawCount = Load<Count>("pp_rmlui_draw_command_count"); var draw = Load<DrawCommand>("pp_rmlui_draw_command"); var geometry = Load<DrawGeometry>("pp_rmlui_draw_geometry");
        var textureCount = Load<Count>("pp_rmlui_draw_texture_count"); var texture = Load<DrawTexture>("pp_rmlui_draw_texture"); var features = Load<Version>("pp_rmlui_draw_features");
        byte[] buffer = new byte[256];
        string Field(ulong id) { int length = read(id, "name", buffer, buffer.Length); return Encoding.UTF8.GetString(buffer, 0, length); }
        string Copy() { int length = copied(buffer, buffer.Length); return Encoding.UTF8.GetString(buffer, 0, length); }
        void Press(int code, int mods = 0) { key(code, 1, mods); key(code, 0, mods); update(); }
        void Frame()
        {
            update(); render(1280, 720); Check(drawCount() > 3 && features() == 0, "Real draw-list frame uses supported commands");
            int visible = 0;
            for (int index = 0; index < drawCount(); index++)
            {
                Command c = new() { Size = 120 }; Check(draw(index, ref c) == 1 && c.Kind is >= 1 and <= 6, "Command layout valid");
                if (c.Kind is not (1 or 6)) continue;
                Geometry g = new() { Size = (uint)Marshal.SizeOf<Geometry>() };
                Check(geometry(c.Geometry, ref g) == 1 && g.Vertices > 0 && g.Indices > 0 && g.VertexData != 0 && g.IndexData != 0, "Geometry live");
                int[] indices = new int[g.Indices]; Marshal.Copy(g.IndexData, indices, 0, indices.Length);
                Check(indices.All(i => i >= 0 && i < g.Vertices), "Indices in bounds");
                byte[] vertices = new byte[checked((int)g.Vertices * 20)]; Marshal.Copy(g.VertexData, vertices, 0, vertices.Length); visible++;
            }
            Check(visible > 0 && textureCount() > 0, "Geometry and generated font textures exist");
            for (int index = 0; index < textureCount(); index++)
            {
                Texture t = new() { Size = (uint)Marshal.SizeOf<Texture>() };
                Check(texture(index, ref t) == 1 && t.Handle > 0 && t.Width > 0 && t.Height > 0 && t.Pixels != 0, "Texture live");
                byte[] pixels = new byte[checked((int)(t.Width * t.Height * 4))]; Marshal.Copy(t.Pixels, pixels, 0, pixels.Length);
                Check(pixels.Where((_, i) => i % 4 == 3).Any(a => a > 0), "Font texture contains alpha");
            }
        }
        try
        {
            Check(version() == 1 && Marshal.SizeOf<Intent>() == 40 && Marshal.SizeOf<Command>() == 120, "Stable ABI sizes/version");
            Check(initialize(1280, 720, 1, root, 99) == 0 && count() == 0, "Unknown backend rejected");
            Check(initialize(1280, 720, float.NaN, root, 1) == 0, "Invalid density rejected");
            Check(initialize(1280, 720, 1, Path.Combine(root, "missing"), 1) == 0 && count() == 0, "Failed initialization unwinds");
            Check(initialize(1280, 720, 1, root, 1) == 1 && count() == 1, "Reinitialization after failure succeeds");
            ulong gen = generation(), firstHome = home(), sequence = 0; setBool(firstHome, "reduce_motion", 1); Frame();
            Check(open("../contract.rml", 0) == 0 && open(Path.Combine(root, "contract.rml"), 0) == 0, "Asset path escape rejected");
            Check(open("missing.rml", 0) == 0 && count() == 1 && close(firstHome) == 0, "Document failure preserves owned shell");
            Check(Task.Run(() => focus(firstHome, "activity_selector")).GetAwaiter().GetResult() == 0, "Off-thread mutation rejected");
            int warmGeometryCount = 0, warmTextureCount = 0;
            for (int cycle = 0; cycle < 100; cycle++)
            {
                Check(focus(firstHome, "activity_selector") == 1, "Launching control focus");
                ulong doc = open("contract.rml", 1); Check(doc > firstHome && count() == 2, "Unique modal lifetime");
                Check(setText(doc, "message", "<button id='injected'>untrusted & text</button>") == 1 && focus(doc, "injected") == 0, "Bound text cannot inject controls");
                Check(focus(doc, "name") == 1 && inputActive() == 1, "Input owns text focus");
                text('A'); text(0x00e9); text(0x1f680); update(); Check(Field(doc) == "Aé🚀", "Unicode scalar roundtrip");
                Check(text(0xd800) == 0 && text(0x110000) == 0, "Invalid scalar rejected");
                if (cycle == 0)
                {
                    Press(32, 2); Press(34, 2); Check(Copy() == "Aé🚀", "Ctrl+A and copy Unicode");
                    clipboard("Paste 日本語"); Press(53, 2); Check(Field(doc) == "Paste 日本語", "Paste replaces selection");
                    Press(13); Check(Field(doc) == "Paste 日本", "Backspace removes one Unicode character");
                    if (OperatingSystem.IsMacOS()) { Press(32, 8); Press(34, 8); Check(Copy() == "Paste 日本", "Command select and copy"); }
                }
                update(); render(1280, 720); Check(drawCount() > 3, "Modal produces real native draw geometry");
                Check(focus(doc, "submit") == 1 && inputActive() == 0, "Button releases text ownership"); Press(2);
                Intent bad = Intent.New(); bad.Version = 2; Check(take(ref bad) == 0, "Wrong protocol cannot drain intent");
                Intent intent = Intent.New();
                Check(take(ref intent) == 1 && intent.Kind == 1 && intent.Argument == 6 && intent.Document == doc && intent.Generation == gen && intent.Sequence > sequence, "Actual DOM typed intent identity"); sequence = intent.Sequence;
                Check(take(ref intent) == 0 && legacy(buffer, buffer.Length) == 0, "Exactly one shared intent queue");
                Press(2); Check(close(doc) == 1 && close(doc) == 0 && count() == 1 && take(ref intent) == 0, "Close discards pending actions");
                int length = focused(buffer, buffer.Length); Check(Encoding.UTF8.GetString(buffer, 0, length) == "activity_selector", "Focus restored to launching control");
                Check(setText(doc, "message", "stale") == 0 && focus(doc, "submit") == 0, "Stale IDs rejected");
                update(); render(1280, 720);
                if (cycle % 25 == 0) Frame();
                if (cycle == 0) { warmGeometryCount = liveGeometryCount(); warmTextureCount = textureCount(); }
                else Check(liveGeometryCount() <= warmGeometryCount && textureCount() <= warmTextureCount, "Closed modal releases resources without growth");
            }
            loseFocus(); Check(inputActive() == 0 && focused(buffer, buffer.Length) == 0, "Focus loss releases text/element focus");
            focus(firstHome, "nav_studio"); Press(2); Check(legacy(new byte[2], 2) == -12, "Small buffer reports required capacity");
            Intent final = Intent.New(); Check(take(ref final) == 1 && final.Kind == 3, "Small buffer does not truncate or drain intent");
            shutdown(); shutdown(); Check(count() == 0 && generation() > gen && home() == 0 && drawCount() == 0 && take(ref final) == 0, "Deterministic shutdown");
            for (int reinit = 0; reinit < 100; reinit++)
            {
                Check(initialize(1280, 720, reinit % 2 + 1, root, 1) == 1 && home() > firstHome && setText(firstHome, "player_name", "stale") == 0, "Reinit IDs never alias");
                setBool(home(), "reduce_motion", 1); update(); render(1280, 720);
                Check(drawCount() > 3 && liveGeometryCount() > 0 && textureCount() > 0, "Reinitialized runtime produces native geometry/texture resources");
                shutdown();
                Check(count() == 0 && liveGeometryCount() == 0 && textureCount() == 0 && drawCount() == 0, "Reinit shutdown releases all native resources");
            }
            ManagedHostCheck.Run(module, root);
            Console.WriteLine($"Native RmlUi runtime passed: {_checks} assertions; 100 real modal cycles and 100 native reinitializations; ABI v1, Unicode/clipboard, owner thread, lifetime, draw-list capture."); return 0;
        }
        finally { shutdown(); NativeLibrary.Free(module); Directory.Delete(root, true); }
    }
}
