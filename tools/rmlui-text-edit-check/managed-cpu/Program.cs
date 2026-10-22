using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Host;

// ACTUAL native CPU bridge + copied actual Host/Cocoa adapter. Only the Cocoa
// callback boundary is synthetic; no NSView/window/AppKit/GL/device is created.
if (args.Length != 1) throw new ArgumentException("Private asset clone required.");
string assets = Path.GetFullPath(args[0]);
if (!assets.StartsWith("/private/tmp/prime-rmlui-text-edit-", StringComparison.Ordinal)
    && !assets.StartsWith("/tmp/prime-rmlui-text-edit-", StringComparison.Ordinal))
    throw new ArgumentException("Private assets only.");
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
int checks = 0;
void Check(bool value, string label) {
    checks++; if (!value) throw new InvalidOperationException(label);
    Console.WriteLine("ACTUAL NATIVE / SYNTHETIC COCOA PASS " + label);
}
using var host = new RmlUiHost();
Check(host.Initialize(640, 360, 1, assets, RmlUiRenderBackend.DrawList), "actual native CPU initialization without a window");
string file = "private-cocoa-cpu-" + Guid.NewGuid().ToString("N") + ".rml";
string path = Path.Combine(assets, file);
using (var output = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write)))
    output.Write("<rml><head><style>body{font-family:Rajdhani;} input{display:block;width:240px;height:48px;}</style></head><body><input id=\"field\" type=\"text\"/><input id=\"other\" type=\"text\"/></body></rml>");
RmlUiDocumentToken document = default;
try {
    document = host.OpenDocument(file, RmlUiDocumentLayer.Page);
    host.SetField(document, "field", "A😀B");
    Check(host.FocusDocument(document, "field"), "actual first field focus"); host.Update();
    Check(host.TryGetTextEditState(out var first), "actual first native owner witness");
    var api = new SyntheticCocoaApi();
    using (var ime = new RmlUiCocoaIme(host, 1, () => true, api)) {
        var callback = api.Callback!;
        Check(ime.Attached && callback.MarkedWithReplacement!("日😀", new(3, 0), new(1, 2)), "first attachment marked callback consumed");
        Check(host.ReadField(document, "field") == "A日😀B" && callback.MarkedRange() == new RmlUiCocoaRange(1, 3),
            "first attachment uses actual native bytes and UTF16 marked range");
        callback.Unmark();
        Check(host.ReadField(document, "field") == "A日😀B" && callback.MarkedRange() == RmlUiCocoaRange.None && !callback.Owns(),
            "first attachment unmark preserves actual bytes and releases marker");
    }
    Check(api.DetachCalls == 1, "first callback boundary detached once");
    Check(host.TryGetTextEditState(out var afterFirst) && afterFirst.FocusEpoch == first.FocusEpoch,
        "adapter detach preserves actual focused native owner lifetime");
    Check(host.TextEdit(afterFirst, 1, RmlUiTextEditKind.Replace, "REPLAY", 0, 1) == RmlUiTextEditResult.Duplicate
        && host.ReadField(document, "field") == "A日😀B", "actual native consumed sequence remains rejected after detach");
    host.SetField(document, "field", "A😀B"); host.Update();
    Check(host.TryGetTextEditState(out var reseeded) && reseeded.FocusEpoch == first.FocusEpoch && reseeded.Revision > afterFirst.Revision,
        "actual same-field programmatic write advances revision without forcing a new focus owner");
    api = new SyntheticCocoaApi();
    using (var ime = new RmlUiCocoaIme(host, 1, () => true, api)) {
        var callback = api.Callback!;
        Check(callback.MarkedWithReplacement!("日😀", new(3, 0), new(1, 2)), "second attachment marked callback consumed");
        Check(host.ReadField(document, "field") == "A日😀B" && callback.MarkedRange() == new RmlUiCocoaRange(1, 3),
            "second attachment on SAME native focus must use a fresh operation sequence");
        callback.Unmark();
        Check(host.ReadField(document, "field") == "A日😀B" && callback.MarkedRange() == RmlUiCocoaRange.None && !callback.Owns(),
            "second attachment unmark preserves actual preedit without reinsertion");
        Check(host.TryGetTextEditState(out var second) && second.FocusEpoch == first.FocusEpoch
            && host.TextEdit(second, 1, RmlUiTextEditKind.Replace, "REPLAY", 0, 1) == RmlUiTextEditResult.Duplicate,
            "host allocation does not weaken actual native replay rejection");
    }
    Check(api.DetachCalls == 1, "second callback boundary detached once");
    host.SetField(document, "field", "source"); host.SetField(document, "other", "SAFE");
    api = new SyntheticCocoaApi();
    using (var ime = new RmlUiCocoaIme(host, 1, () => true, api)) {
        var callback = api.Callback!;
        callback.MarkedWithReplacement!("old", new(3, 0), new(0, 6));
        Check(host.ReadField(document, "field") == "old", "third attachment actual native preedit admitted");
        Check(host.FocusDocument(document, "other"), "actual native focus changes before first unmark"); host.Update();
        callback.Unmark();
        Check(callback.Owns() && callback.InsertWithReplacement!("LATE", RmlUiCocoaRange.None)
            && host.ReadField(document, "other") == "SAFE", "FIRST stale unmark fences late insertion without refresh");
    }
    Check(api.DetachCalls == 1, "stale composition boundary detached once");
    bool ownerRejected = Task.Run(() => {
        try { host.TryGetTextEditState(out _); return false; } catch (InvalidOperationException) { return true; }
    }).GetAwaiter().GetResult();
    Check(ownerRejected, "actual Host retains owner-thread guard");
    Console.WriteLine($"PASS {checks} actual native CPU Host/Cocoa lifetime checks; synthetic Cocoa callbacks, NO WINDOW/GL/APPKIT");
} finally {
    if (host.IsAlive(document)) host.CloseDocument(document);
    File.Delete(path);
}

internal sealed class SyntheticCocoaApi : IRmlUiCocoaImeApi {
    internal RmlUiCocoaCallbacks? Callback;
    internal int DetachCalls;
    public bool Attach(nint view, RmlUiCocoaCallbacks callbacks) { Callback = callbacks; return true; }
    public bool Detach() { DetachCalls++; return true; }
    public void DiscardMarkedText() => Callback?.Unmark();
    public void InvalidateCandidatePosition() { }
}
