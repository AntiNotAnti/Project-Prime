using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using MphRead.Mods.Launcher.RmlUi.Host;

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
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Resize(int w, int h, float density);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int layer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Close(ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Show(ulong id, int visible);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Focus(ulong id, [MarshalAs(UnmanagedType.LPUTF8Str)] string element);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetText(ulong id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetBool(ulong id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadField(ulong id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [Out] byte[] buffer, int capacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SemanticSnapshot(ulong id, [Out] byte[] buffer, int capacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SemanticSetText(ulong generation, ulong document, ulong revision, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Take(ref Intent intent);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Status(ref UpdateStatus status);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadBytes([Out] byte[] buffer, int capacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Key(int code, int down, int modifiers);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MouseMove(int x, int y, int modifiers);
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
    [StructLayout(LayoutKind.Sequential)] private struct UpdateStatus
    {
        public uint Size, Version; public ulong Generation, Revision; public double Delay; public uint Flags, Reserved;
        public static UpdateStatus New() => new() { Size = 40, Version = 1 };
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
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Bounds(ulong doc, [MarshalAs(UnmanagedType.LPUTF8Str)] string id, out float x, out float y, out float width, out float height);
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
            button, input, textarea { display: block; width: 260px; height: 40px; tab-index: auto; }
            #canvas { position: absolute; left: 0px; top: 0px; width: 40px; height: 30px; }
            </style></head><body><div id="message">TEXT</div><input id="name" type="text" value="" />
            <textarea id="payload"/><div id="canvas"/>
            <button id="submit" data-action="route:settings">SETTINGS</button></body></rml>
            """);
        File.WriteAllText(Path.Combine(root, "focus-policy.rml"), """
            <rml><head><style>
            body { font-family: Rajdhani; font-size: 20px; }
            #viewport { position: absolute; left: 20px; top: 30px; width: 220px; height: 100px; overflow: auto; }
            scrollbarvertical { width: 16px; } scrollbarhorizontal { height: 16px; }
            #policy_canvas { display: block; width: 200px; height: 180px; margin-top: 60px; }
            #far { display: block; width: 120px; height: 40px; margin-top: 300px; tab-index: auto; }
            </style></head><body><div id="viewport"><div id="policy_canvas" tabindex="0" data-focus-scroll="none"/>
            <button id="far" data-action="route:home">FAR CONTROL</button></div></body></rml>
            """);
        var actions = Enum.GetValues<RmlUiIntentKind>().SelectMany(kind => Enumerable.Range(-1, 66)
            .Where(argument => RmlUiIntentRegistry.IsValid(kind, argument))
            .Select(argument => new RmlUiIntent(kind, argument, new(1, 1), 1))).ToArray();
        string buttons = String.Join("", actions.Select((action, index) => $"<button id='action{index}' data-action='{RmlUiIntentRegistry.ToLegacy(action)}'>ACTION {index}</button>"));
        File.WriteAllText(Path.Combine(root, "registry.rml"), "<rml><head><style>body{font-family:Rajdhani;font-size:16px;}button,input{tab-index:auto;width:200px;height:30px;display:block;}</style></head><body>" + buttons + "<input id='seek' type='range' min='0' max='1000' step='1' value='500' data-action='replay:seek'/><img id='thumbnail' width='40' height='40'/></body></rml>");
        byte[] image = new byte[34]; image[2] = 2; image[12] = image[14] = 2; image[16] = 32; image[17] = 40;
        for (int index = 18; index < image.Length; index += 4) { image[index] = 20; image[index + 1] = 40; image[index + 2] = 200; image[index + 3] = 255; }
        File.WriteAllBytes(Path.Combine(root, "local.tga"), image);
        nint module = NativeLibrary.Load(Path.GetFullPath(args[0]));
        T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
        var version = Load<Version>("pp_rmlui_protocol_version"); var generation = Load<Id>("pp_rmlui_generation");
        var status = Load<Status>("pp_rmlui_update_status");
        var home = Load<Id>("pp_rmlui_home_document"); var initialize = Load<Initialize>("pp_rmlui_initialize_backend");
        var shutdown = Load<Empty>("pp_rmlui_shutdown"); var update = Load<Empty>("pp_rmlui_update"); var render = Load<Render>("pp_rmlui_render");
        var resize = Load<Resize>("pp_rmlui_resize"); var mouseMove = Load<MouseMove>("pp_rmlui_mouse_move"); var mouseButton = Load<Key>("pp_rmlui_mouse_button");
        var open = Load<Open>("pp_rmlui_document_open"); var close = Load<Close>("pp_rmlui_document_close"); var focus = Load<Focus>("pp_rmlui_document_focus");
        var show = Load<Show>("pp_rmlui_document_show");
        var bounds = Load<Bounds>("pp_rmlui_document_element_bounds");
        var setText = Load<SetText>("pp_rmlui_document_set_text"); var setBool = Load<SetBool>("pp_rmlui_document_set_bool"); var read = Load<ReadField>("pp_rmlui_document_read_field");
        var setField = Load<SetText>("pp_rmlui_document_set_field");
        var semanticSnapshot = Load<SemanticSnapshot>("pp_rmlui_document_accessibility_snapshot");
        var semanticSetText = Load<SemanticSetText>("pp_rmlui_accessibility_set_text");
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
        UpdateStatus State() { UpdateStatus state = UpdateStatus.New(); Check(status(ref state) == 1, "Optional native update status is owner-valid"); return state; }
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
            ulong styled = open("contract.rml", 1);
            Check(setText(styled, "rect:canvas", "13,17,90,40") == 1 && bounds(styled, "canvas", out float cx, out float cy, out float cw, out float ch) == 1
                && Math.Abs(cx - 13) < .1f && Math.Abs(cy - 17) < .1f && Math.Abs(cw - 90) < .1f && Math.Abs(ch - 40) < .1f, "Bound finite HUD geometry controls actual DOM layout");
            foreach (string invalid in new[] { "nan,0,1,1", "0,0,-1,1", "0,0,100001,1", "0,0,1,1;left:0px", "0,0,1", "0,0,1,1,1" })
                Check(setText(styled, "rect:canvas", invalid) == 0, "Malformed HUD rectangles rejected before mutation");
            Check(setText(styled, "opacity:canvas", "0.5") == 1 && setText(styled, "opacity:canvas", "1.01") == 0 && setText(styled, "opacity:canvas", "nan") == 0, "HUD opacity finite and bounded");
            Check(setText(styled, "color:canvas", "#12345678") == 1 && setText(styled, "ink:message", "#abcdef") == 1 && setText(styled, "ink:message", "#ffffzz") == 0, "Authored color bindings validate hex without CSS injection");
            Check(setText(styled, "font-size:message", "32") == 1 && setText(styled, "font-size:message", "1001") == 0, "HUD text size is bounded");
            Check(setBool(styled, "class:@document:high-contrast", 1) == 1, "Shared document theme policy applies to root");
            Check(setText(styled, "action:submit", "results:map:4095") == 1 && setText(styled, "action:submit", "results:map:4096") == 0
                && setText(styled, "action:submit", "javascript:injected") == 0, "Dynamic authored actions accept only bounded registered intents");
            Check(focus(styled, "submit") == 1, "Bound action control focuses"); Press(2); Intent catalog = Intent.New();
            Check(take(ref catalog) == 1 && catalog.Kind == (uint)RmlUiIntentKind.ResultsMap && catalog.Argument == 4095, "Bound catalog identity is captured by real native listener");
            string payload = "日本語🚀\n" + new string('x', 4096);
            Check(setField(styled, "payload", payload) == 1 && read(styled, "payload", buffer, buffer.Length) == -Encoding.UTF8.GetByteCount(payload) - 1 && buffer[0] == 0, "Textarea reports required UTF8 capacity without leaking truncated payload");
            byte[] full = new byte[Encoding.UTF8.GetByteCount(payload) + 1]; int copiedPayload = read(styled, "payload", full, full.Length);
            Check(copiedPayload == full.Length - 1 && Encoding.UTF8.GetString(full, 0, copiedPayload) == payload, "Textarea Unicode and newlines roundtrip through field ABI");
            Check(focus(styled, "payload") == 1 && inputActive() == 1, "Textarea owns native edit context");
            Check(close(styled) == 1, "Bound geometry/textarea lifetime closes");
            ulong policy = open("focus-policy.rml", 1);
            float canvasBefore = 0, canvasAfter = 0, scrolledCanvas = 0, preservedCanvas = 0;
            Check(policy != 0 && bounds(policy, "policy_canvas", out _, out canvasBefore, out _, out _) == 1,
                "Scrollable canvas fixture opens with real bounds");
            Check(focus(policy, "policy_canvas") == 1 && bounds(policy, "policy_canvas", out _, out canvasAfter, out _, out _) == 1
                && Math.Abs(canvasBefore - canvasAfter) < .1f, "Authored canvas focus preserves gesture coordinate space");
            Check(focus(policy, "far") == 1 && bounds(policy, "policy_canvas", out _, out scrolledCanvas, out _, out _) == 1
                && scrolledCanvas < canvasAfter - 100, "Ordinary keyboard focus still scrolls distant control into view");
            Check(focus(policy, "policy_canvas") == 1 && bounds(policy, "policy_canvas", out _, out preservedCanvas, out _, out _) == 1
                && Math.Abs(scrolledCanvas - preservedCanvas) < .1f, "Authored canvas refocus preserves current scrolled position");
            Check(close(policy) == 1, "Canvas focus policy document closes");
            ulong cached = open("contract.rml", 1);
            Check(cached != 0 && show(home(), 0) == 1, "Static capture fixture opens with unrelated Home animations hidden");
            loseFocus(); update(); render(1280, 720);
            // The preceding pointer/keyboard checks may leave finite hover
            // transitions active on Home. Let those authored transitions end.
            Thread.Sleep(200); update(); render(1280, 720);
            UpdateStatus idle = State();
            Check(Marshal.SizeOf<UpdateStatus>() == 40 && idle.Generation == gen && idle.Flags == 2 && idle.Delay > 0,
                "Captured static frame is clean and reusable with a valid timer interval");
            int cachedCommands = drawCount();
            for (int frame = 0; frame < 100; ++frame) { update(); render(1280, 720); }
            UpdateStatus unchanged = State();
            Check(unchanged.Revision == idle.Revision && unchanged.Flags == 2 && drawCount() == cachedCommands,
                "One hundred idle updates/renders reuse real native draw resources without revising the frame");
            UpdateStatus invalidStatus = UpdateStatus.New(); invalidStatus.Size--;
            Check(status(ref invalidStatus) == 0, "Update status rejects wrong struct size");
            invalidStatus = UpdateStatus.New(); invalidStatus.Version = 2;
            Check(status(ref invalidStatus) == 0, "Update status rejects unknown capability version");
            Check(setText(cached, "message", "VISIBLE MUTATION") == 1, "DOM mutation accepted for capture invalidation");
            UpdateStatus mutated = State();
            Check(mutated.Revision > idle.Revision && mutated.Flags == 1 && mutated.Delay == 0,
                "Actual DOM mutation invalidates deferred frame before another capture");
            update(); render(1280, 720); UpdateStatus recaptured = State();
            Check(recaptured.Revision > mutated.Revision && recaptured.Flags == 2, "Mutated frame is updated and recaptured");
            Check(setText(cached, "message", "VISIBLE MUTATION") == 1 && State().Revision == recaptured.Revision,
                "Unchanged text binding preserves captured frame revision");
            Check(focus(cached, "name") == 1, "Caret timer fixture receives real native focus");
            update(); render(1280, 720); UpdateStatus caret = State();
            Check(double.IsFinite(caret.Delay) && caret.Delay > 0 && caret.Delay <= 1, "Focused text widget schedules its real blink deadline");
            Thread.Sleep(25); UpdateStatus later = State();
            Check(later.Delay < caret.Delay - .01 && later.Revision == caret.Revision,
                "Native interval accounts for elapsed wall time without a premature context update");
            Thread.Sleep((int)Math.Ceiling(later.Delay * 1000) + 10);
            update(); render(1280, 720); UpdateStatus blink = State();
            Check(blink.Revision > caret.Revision && blink.Flags == 2, "Caret deadline updates and recaptures the native frame");
            Check(close(cached) == 1 && (State().Flags & 2) == 0, "Retiring real geometry invalidates retained deferred frame");
            Check(show(home(), 1) == 1, "Home visibility restores after the isolated capture fixture");
            ulong registry = open("registry.rml", 1);
            Check(registry != 0, "Full additive intent registry document opens");
            for (int index = 0; index < actions.Length; index++)
            {
                Check(focus(registry, "action" + index) == 1, "Every authored registry action focuses through the real DOM");
                Press(2); Intent packet = Intent.New();
                Check(take(ref packet) == 1 && packet.Kind == (uint)actions[index].Kind && packet.Argument == actions[index].Argument
                    && packet.Document == registry && packet.Generation == gen, "Native and managed additive action maps agree");
                Check(take(ref packet) == 0, "Single native action packet per keyboard activation");
            }
            Check(setBool(registry, "disabled:action0", 1) == 1 && focus(registry, "action0") == 0, "Disabled binding blocks native focus");
            Check(setBool(registry, "disabled:action0", 0) == 1 && focus(registry, "action0") == 1, "Enabled binding restores native focus");
            Check(setBool(registry, "class:action0:selected", 1) == 1, "Authored class binding supported");
            Check(setBool(registry, "visible:action0", 0) == 1 && bounds(registry, "action0", out _, out _, out _, out _) == 0 && focus(registry, "action0") == 0, "Visible binding removes layout and focus");
            Check(setBool(registry, "visible:action0", 1) == 1 && bounds(registry, "action0", out _, out _, out float bw, out float bh) == 1 && bw > 0 && bh > 0, "Document-specific bounds restored");
            Check(setText(registry, "image:thumbnail", "https://example.invalid/remote.png") == 0, "Image binding rejects network sources");
            Check(setText(registry, "image:thumbnail", Path.Combine(root, "local.tga")) == 1, "Image binding assigns a local attribute without parsing markup");
            update(); render(1280, 720);
            bool thumbnailLoaded = false;
            for (int index = 0; index < textureCount(); index++) {
                Texture t = new() { Size = (uint)Marshal.SizeOf<Texture>() };
                if (texture(index, ref t) == 1 && t.Width == 2 && t.Height == 2) {
                    byte[] rgba = new byte[16]; Marshal.Copy(t.Pixels, rgba, 0, rgba.Length);
                    thumbnailLoaded = rgba[0] == 200 && rgba[1] == 40 && rgba[2] == 20 && rgba[3] == 255;
                }
            }
            Check(thumbnailLoaded, "Absolute local thumbnail resolves and captures checked RGBA pixels");
            Intent authoritativeSeek = Intent.New();
            for (int frame = 0; frame < 120; frame++)
            {
                Check(setField(registry, "seek", frame.ToString()) == 1, "Authoritative replay position binds each playback frame");
                update(); render(1280, 720);
                Check(take(ref authoritativeSeek) == 0, "Continuous presenter range updates never emit user seek commands");
            }
            UpdateStatus rangeIdle = State();
            Check(setField(registry, "seek", "119") == 1, "Range integer bindings compare with RmlUi's formatted numeric value");
            UpdateStatus rangeUnchanged = State();
            Check(rangeUnchanged.Revision == rangeIdle.Revision && rangeUnchanged.Flags == 2,
                "Unchanged authoritative range position preserves the captured native frame");
            Check(focus(registry, "seek") == 1, "Range input obtains native keyboard focus");
            Press(8); Intent seek = Intent.New();
            Check(take(ref seek) == 1 && seek.Kind == 170 && seek.Argument == 9 && seek.Document == registry, "Range keyboard change emits typed replay seek");
            Check(take(ref seek) == 0, "Physical range edit dispatches exactly one seek");
            byte[] semanticBytes = new byte[1024 * 1024 + 1024];
            int semanticLength = semanticSnapshot(registry, semanticBytes, semanticBytes.Length);
            Check(semanticLength > 0, "Real semantic snapshot exposes authored range control");
            using (JsonDocument semantics = JsonDocument.Parse(semanticBytes.AsMemory(0, semanticLength)))
            {
                JsonElement seekNode = semantics.RootElement.GetProperty("nodes").EnumerateArray()
                    .Single(node => node.GetProperty("id").GetString() == "seek");
                Check(seekNode.GetProperty("role").GetString() == "slider" && (seekNode.GetProperty("actions").GetInt32() & 8) != 0,
                    "Accessible native slider advertises actual SetText capability");
                Check(semanticSetText(gen, registry, semantics.RootElement.GetProperty("revision").GetUInt64(),
                    seekNode.GetProperty("key").GetString()!, "125") == 1, "Accessible slider applies one authoritative user value");
                Intent accessibleSeek = Intent.New();
                Check(take(ref accessibleSeek) == 1 && accessibleSeek.Kind == 170 && accessibleSeek.Argument == 9 && accessibleSeek.Document == registry,
                    "Accessible slider edit dispatches a real typed seek");
                Check(take(ref accessibleSeek) == 0, "A single accessible slider edit dispatches exactly one seek");
            }
            Check(close(registry) == 1, "Registry document closes");
            foreach (float density in new[] { 1f, 1.25f, 2f })
            {
                resize(1280, 720, density);
                ulong queue = open("pages/play/queue.rml", 1);
                Check(queue > 0, "Actual authored waitlist modal opens at density " + density);
                Check(setText(queue, "play_queue_status", "WAITING FOR SERVER SLOT") == 1
                    && setText(queue, "play_queue_position", "POSITION // 2") == 1
                    && setText(queue, "play_queue_seconds", "15") == 1, "Waitlist facts bind to authored status, position and expiry fields");
                update(); render(1280, 720);
                Check(bounds(queue, "play_queue_status", out _, out _, out float statusWidth, out float statusHeight) == 1
                    && statusWidth > 0 && statusHeight > 0, "Waitlist status has usable native layout");
                Check(bounds(queue, "play_queue_offer", out _, out _, out float offerWidth, out float offerHeight) == 1
                    && offerWidth > 0 && offerHeight > 0, "Waitlist expiry has usable native layout");
                foreach (string id in new[] { "play_queue_join", "play_queue_accept", "play_queue_decline" })
                    Check(focus(queue, id) == 0, "Waitlist action is authored unavailable until authoritative state enables it");
                string[] ids = { "play_queue_join", "play_queue_accept", "play_queue_decline", "play_queue_leave" };
                for (int argument = 0; argument < ids.Length; argument++)
                {
                    string id = ids[argument];
                    Check(setBool(queue, "disabled:" + id, 0) == 1 && focus(queue, id) == 1, "Waitlist permission enables actual control");
                    update(); render(1280, 720);
                    Check(bounds(queue, id, out float x, out float y, out float w, out float h) == 1
                        && w > 0 && h > 0 && x >= 0 && y >= 0 && x + w <= 1280 && y + h <= 720, "Waitlist control stays inside the framebuffer");
                    mouseMove((int)(x + w / 2), (int)(y + h / 2), 0); mouseButton(0, 1, 0); mouseButton(0, 0, 0); update();
                    Intent packet = Intent.New();
                    Check(take(ref packet) == 1 && packet.Kind == 20 && packet.Argument == argument && packet.Document == queue && packet.Generation == gen,
                        "Real waitlist mouse action carries bounded kind20 and current modal identity");
                    Check(take(ref packet) == 0, "Waitlist mouse emits exactly one typed packet");
                }
                Check(close(queue) == 1 && count() == 1, "Waitlist retirement restores native shell lifetime");
            }
            resize(1280, 720, 1);
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
