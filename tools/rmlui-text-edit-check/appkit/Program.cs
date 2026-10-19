using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Host;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Actual AppKit Objective-C callbacks and actual native RmlUi owner, with
// programmatically authored NSTextInputClient messages. No physical keyboard,
// input-method server, candidate panel, GL, game assets, account or network.
if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("This acceptance requires actual macOS AppKit.");
if (args.Length != 1) throw new ArgumentException("Pass the private review bundle's assets clone.");
string assets = Path.GetFullPath(args[0]);
if (!assets.StartsWith("/private/tmp/prime-rmlui-text-edit-", StringComparison.Ordinal)
    && !assets.StartsWith("/tmp/prime-rmlui-text-edit-", StringComparison.Ordinal))
    throw new ArgumentException("Private assets clone required; no source/user asset writes.");
// Resolve only the explicitly prepared synthetic test runtime, never replace
// the app's native resolver or load a user-supplied library into the game.
if (Environment.GetEnvironmentVariable("PRIME_RMLUI_TEXT_EDIT_NATIVE") is { Length: > 0 } library) {
    string native = Path.GetFullPath(library);
    if (!native.StartsWith("/private/tmp/prime-rmlui-text-edit-", StringComparison.Ordinal)
        && !native.StartsWith("/tmp/prime-rmlui-text-edit-", StringComparison.Ordinal))
        throw new ArgumentException("Native fixture copy must be private temporary input.");
    NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,
        (name, _, _) => name == "ProjectPrime.RmlUi.Native" ? NativeLibrary.Load(native) : 0);
}
GLFWCallbacks.ErrorCallback error = (code, _) => { try { Console.Error.WriteLine("GLFW numeric error=" + (int)code); } catch { } };
GLFWProvider.SetErrorCallback(error);
GLFW.InitHint(InitHintBool.CocoaMenubar, false);
GLFW.InitHint(InitHintBool.CocoaChdirResources, false);
GLFWProvider.EnsureInitialized();
unsafe { if (GLFW.GetPrimaryMonitor() == null) throw new InvalidOperationException("Actual macOS display required for the NoAPI Cocoa window."); }
GLFW.DefaultWindowHints();
using var window = new NativeWindow(new NativeWindowSettings {
    API = ContextAPI.NoAPI, ClientSize = new Vector2i(640, 360), StartVisible = false,
    StartFocused = false, Title = "PRIVATE PROGRAMMATIC APPKIT IME FIXTURE"
});
nint view;
unsafe { view = GLFW.GetCocoaView(window.WindowPtr); }
if (view == 0) throw new InvalidOperationException("Actual Cocoa GLFW NSView unavailable.");
nint originalClass = Obj.Class(view);
int width = window.FramebufferSize.X, height = window.FramebufferSize.Y;
if (width <= 0 || height <= 0) throw new InvalidOperationException("Actual NoAPI Cocoa view has invalid framebuffer dimensions.");
using var host = new RmlUiHost();
if (!host.Initialize(width, height, 1, assets, RmlUiRenderBackend.DrawList)) throw new InvalidOperationException("Actual CPU native owner failed to initialize.");
int checks = 0, characterCallbacks = 0;
void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException(label); Console.WriteLine("APPKIT MESSAGE PASS " + label); }
window.TextInput += input => {
    characterCallbacks++;
    if (host.TryGetTextInputState(out var scope) && !scope.Composing)
        host.Input.Dispatch(new(scope.Document, RmlUiPlatformInputKind.TextCommitted, RmlUiInputDevice.Keyboard,
            scope.FocusEpoch, Text: input.AsString));
};
string file = "private-appkit-" + Guid.NewGuid().ToString("N") + ".rml";
string pagePath = Path.Combine(assets, file);
using (var stream = new FileStream(pagePath, FileMode.CreateNew, FileAccess.Write))
using (var writer = new StreamWriter(stream)) writer.Write("""
    <rml><head><style>body {font-family:Rajdhani;font-size:24px;} input {display:block;width:240px;height:48px;margin:8px;}</style></head>
    <body><input id="field" type="text"/><input id="limited" type="text" maxlength="4"/><input id="other" type="text"/><input id="secret" type="password"/></body></rml>
    """);
RmlUiDocumentToken document = default;
try {
    document = host.OpenDocument(file, RmlUiDocumentLayer.Page);
    void Seed(string field, string text) { host.SetField(document, field, text); Check(host.FocusDocument(document, field), "real native field focus " + field); host.Update(); }
    void Attached(Action<RmlUiCocoaIme> body) {
        using (var ime = RmlUiCocoaIme.TryAttach(host, view, () => true)) {
            Check(ime is { Attached: true } && Obj.Class(view) != originalClass, "real per-view Objective-C subclass attached");
            body(ime!);
        }
        Check(Obj.Class(view) == originalClass, "real original NSView class restored after detach");
    }
    Seed("field", "A😀B");
    Attached(ime => {
        int before = characterCallbacks;
        Obj.Marked(view, "日😀", new(3, 0), new(1, 2), attributed: true);
        Check(host.ReadField(document, "field") == "A日😀B" && characterCallbacks == before,
            "actual attributed marked callback applies exact UTF16 replacement without GLFW characters");
        Check(Obj.Range(view, "markedRange") == new RmlUiCocoaRange(1, 3)
            && Obj.Range(view, "selectedRange") == new RmlUiCocoaRange(4, 0) && Obj.HasMarked(view),
            "actual NSTextInputClient marked and selected UTF16 queries match current widget");
        var (rectangle, actual) = Obj.Candidate(view, new(4, 0));
        Check(double.IsFinite(rectangle.X) && double.IsFinite(rectangle.Y) && double.IsFinite(rectangle.Width)
            && double.IsFinite(rectangle.Height) && rectangle.Width >= 0 && rectangle.Height > 0
            && actual.Location <= 4 && actual.Length <= 65536 && actual.Location + actual.Length >= 4,
            "actual firstRect callback returns finite native-to-AppKit bounds with bounded actual range");
        Obj.Insert(view, "日本", RmlUiCocoaRange.None);
        Check(host.ReadField(document, "field") == "A日本B" && characterCallbacks == before && !Obj.HasMarked(view),
            "actual composed insertion reaches native once and suppresses original GLFW insertion");
    });
    Seed("field", "A😀B");
    Attached(ime => {
        Obj.Marked(view, "日😀", new(3, 0), new(1, 2));
        int before = characterCallbacks;
        Obj.Void(view, "unmarkText");
        Check(host.ReadField(document, "field") == "A日😀B" && !Obj.HasMarked(view) && characterCallbacks == before,
            "actual AppKit unmark accepts current marked bytes without cancellation or reinsertion");
        Obj.Void(view, "unmarkText");
        Check(host.ReadField(document, "field") == "A日😀B", "actual unmark without marking is a no-op");
    });
    Seed("limited", "A😀Z");
    Attached(ime => {
        Obj.Marked(view, "日😀語文", new(3, 2), new(1, 2));
        Check(host.ReadField(document, "limited") == "A日😀語文Z", "actual temporary preedit may exceed authored maxlength");
        Obj.Void(view, "unmarkText");
        Check(host.ReadField(document, "limited") == "A日😀Z" && Obj.Range(view, "selectedRange") == new RmlUiCocoaRange(4, 0),
            "actual unmark truncates marked substring at supplementary scalar boundary and remaps selection");
    });
    Seed("field", "original");
    Attached(ime => {
        Obj.Marked(view, "temporary", new(9, 0), new(0, 8));
        ime.Cancel();
        int before = characterCallbacks;
        // Real NSInputContext discard may already call this synchronously. This
        // extra real message proves such reentry cannot release stale authority.
        Obj.Void(view, "unmarkText");
        Obj.Insert(view, "must be discarded", RmlUiCocoaRange.None);
        Check(host.ReadField(document, "field") == "original" && characterCallbacks == before,
            "actual discard then unmark keeps canceled late insert fenced from GLFW");
    });
    Seed("field", "source"); host.SetField(document, "other", "SAFE");
    Attached(ime => {
        Obj.Marked(view, "old", new(3, 0), new(0, 6));
        Check(host.FocusDocument(document, "other"), "actual native focus changes during marked sequence");
        ime.RefreshCandidatePosition();
        int before = characterCallbacks;
        Obj.Void(view, "unmarkText"); Obj.Insert(view, "stale payload", RmlUiCocoaRange.None);
        Check(host.ReadField(document, "other") == "SAFE" && characterCallbacks == before,
            "actual delayed composition never inserts into replacement native focus");
    });
    // No RefreshCandidatePosition before Unmark: this is the first stale callback.
    Seed("field", "source"); host.SetField(document, "other", "SAFE");
    Attached(ime => {
        Obj.Marked(view, "old", new(3, 0), new(0, 6));
        Check(host.FocusDocument(document, "other"), "actual focus changes before FIRST stale unmark");
        int before = characterCallbacks;
        Obj.Void(view, "unmarkText"); Obj.Insert(view, "must remain discarded", RmlUiCocoaRange.None);
        Check(host.ReadField(document, "other") == "SAFE" && characterCallbacks == before,
            "FIRST stale unmark after focus change cannot release a late insert into GLFW/new field");
    });
    Seed("field", "source");
    Attached(ime => {
        Obj.Marked(view, "old", new(3, 0), new(0, 6));
        host.SetField(document, "field", "AUTHORITATIVE");
        int before = characterCallbacks;
        Obj.Void(view, "unmarkText"); Obj.Insert(view, "must remain discarded", RmlUiCocoaRange.None);
        Check(host.ReadField(document, "field") == "AUTHORITATIVE" && characterCallbacks == before,
            "FIRST stale unmark after revision change cannot replace authoritative bytes");
    });
    Seed("field", "A😀B");
    Attached(ime => {
        int before = characterCallbacks;
        Obj.Insert(view, "x", new(1, 2));
        Check(host.ReadField(document, "field") == "AxB" && characterCallbacks == before,
            "actual explicit replacement outside composition uses optional owner instead of range-less GLFW fallback");
    });
    Seed("field", "");
    Attached(ime => {
        int before = characterCallbacks;
        Obj.Insert(view, "x", new RmlUiCocoaRange(0x7fffffffffffffffUL, 0));
        Obj.Insert(view, "x", new RmlUiCocoaRange(0x7fffffffffffffffUL, 0));
        Check(host.ReadField(document, "field") == "xx" && characterCallbacks == before + 2,
            "actual literal SDK NSNotFound ordinary repeated text remains GLFW-owned without text or time dedup");
        int unchanged = characterCallbacks;
        Obj.Insert(view, "invalid sentinel", new RmlUiCocoaRange(0xffffffffffffffffUL, 0));
        Check(host.ReadField(document, "field") == "xx" && characterCallbacks == unchanged,
            "actual malformed NSUIntegerMax replacement remains rejected without native or GLFW insertion");
    });
    Seed("secret", "synthetic-secret");
    Attached(ime => {
        Check(host.TryGetTextEditState(out var state) && (state.Flags & 1) != 0, "actual protected field state contains numeric protected flag");
        Obj.Marked(view, "秘密", new(2, 0), new(0, 16));
        ime.Cancel(); Obj.Void(view, "unmarkText");
        int before = characterCallbacks;
        Obj.Insert(view, "discarded synthetic secret", RmlUiCocoaRange.None);
        Check(host.ReadField(document, "secret") == "synthetic-secret" && characterCallbacks == before,
            "actual protected cancel preserves original synthetic bytes without a character callback");
    });
    Check(Obj.Class(view) == originalClass, "all actual callback owners detached before native host/window disposal");
    Console.WriteLine($"PASS {checks} actual macOS AppKit PROGRAMMATIC message checks; physical IME/candidate-server and GL remain unqualified");
} finally {
    if (host.IsAlive(document)) host.CloseDocument(document);
    File.Delete(pagePath);
}
GC.KeepAlive(error);

internal static class Obj
{
    private const string Lib = "/usr/lib/libobjc.A.dylib";
    internal static nint Class(nint value) => object_getClass(value);
    internal static void Void(nint value, string name) => SendVoid(value, Selector(name));
    internal static RmlUiCocoaRange Range(nint value, string name) => SendRange(value, Selector(name));
    internal static bool HasMarked(nint view) => SendBool(view, Selector("hasMarkedText")) != 0;
    internal static void Marked(nint view, string text, RmlUiCocoaRange selected, RmlUiCocoaRange replacement, bool attributed = false) {
        using var value = new StringObject(text);
        nint input = value.Value, attribute = 0;
        try {
            if (attributed) input = attribute = SendObjectArgument(SendObject(objc_getClass("NSAttributedString"), Selector("alloc")), Selector("initWithString:"), value.Value);
            SendMarked(view, Selector("setMarkedText:selectedRange:replacementRange:"), input, selected, replacement);
        } finally { if (attribute != 0) Void(attribute, "release"); }
    }
    internal static void Insert(nint view, string text, RmlUiCocoaRange replacement) {
        using var value = new StringObject(text); SendInsert(view, Selector("insertText:replacementRange:"), value.Value, replacement);
    }
    internal static (RmlUiCocoaRect, RmlUiCocoaRange) Candidate(nint view, RmlUiCocoaRange requested) {
        nint actual = Marshal.AllocHGlobal(Marshal.SizeOf<RmlUiCocoaRange>());
        try {
            Marshal.StructureToPtr(RmlUiCocoaRange.None, actual, false);
            var rectangle = SendRect(view, Selector("firstRectForCharacterRange:actualRange:"), requested, actual);
            return (rectangle, Marshal.PtrToStructure<RmlUiCocoaRange>(actual));
        } finally { Marshal.FreeHGlobal(actual); }
    }
    private sealed class StringObject : IDisposable {
        internal nint Value { get; }
        internal StringObject(string text) { Value = SendUtf8(SendObject(objc_getClass("NSString"), Selector("alloc")), Selector("initWithUTF8String:"), text); if (Value == 0) throw new InvalidOperationException("Synthetic NSString creation failed"); }
        public void Dispose() => Void(Value, "release");
    }
    private static nint Selector(string name) => sel_registerName(name);
    [DllImport(Lib)] private static extern nint object_getClass(nint value);
    [DllImport(Lib)] private static extern nint objc_getClass(string name);
    [DllImport(Lib)] private static extern nint sel_registerName(string name);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern void SendVoid(nint value, nint selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern nint SendObject(nint value, nint selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern nint SendObjectArgument(nint value, nint selector, nint argument);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern nint SendUtf8(nint value, nint selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern void SendMarked(nint value, nint selector, nint text, RmlUiCocoaRange selected, RmlUiCocoaRange replacement);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern void SendInsert(nint value, nint selector, nint text, RmlUiCocoaRange replacement);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern RmlUiCocoaRange SendRange(nint value, nint selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern byte SendBool(nint value, nint selector);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] private static extern RmlUiCocoaRect SendRect(nint value, nint selector, RmlUiCocoaRange requested, nint actual);
}
