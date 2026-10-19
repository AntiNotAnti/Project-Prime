using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class RangeImeAcceptance
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException(label); }
        Check(Marshal.SizeOf<RmlUiNativeTextEditRequest>() == 80 && Marshal.SizeOf<RmlUiNativeTextEditState>() == 88
            && Marshal.SizeOf<RmlUiNativeTextInputState>() == 64, "optional packet layouts preserve old state64");
        var native = new FakeBridge { EditEnabled = true };
        using var host = new RmlUiHost(native);
        host.Initialize(640, 360, 1, "."); host.FocusDocument(host.HomeDocument, "name");
        Check(host.TryGetTextEditState(out var scope) && scope.Revision == 1 && scope.Bounds.Width == 0,
            "copied actual Host accepts true zero-width caret position with positive actual line height");
        var api = new CocoaApi();
        using var ime = new RmlUiCocoaIme(host, 1, () => true, api);
        var callbacks = api.Callback!;
        native.EditSelectionLength = 0;
        Check(callbacks.CandidateForRange!(new(2, 0)) is { ExactCaret: true, Bounds.Width: 0 }, "range-aware candidate uses actual zero-width caret only at matching collapsed insertion");
        Check(callbacks.CandidateForRange!(new(0, 1)) is { ExactCaret: false, Bounds.Width: 260, ActualRange.Location: 0, ActualRange.Length: 12 },
            "unrelated character range receives honest field bounds and actual whole-field range");
        native.EditSelectionLength = 3;
        Check(callbacks.CandidateForRange!(new(2, 3)) is { ExactCaret: false }, "selected range with unknown active direction never guesses a caret");
        Check(callbacks.MarkedWithReplacement!("日😀本", new(3, 1), new(2, 3)), "AppKit replacement callback consumed");
        Check(native.LastEdit.Start == 2 && native.LastEdit.Length == 3 && native.LastEdit.SelectedStart == 3
            && native.LastEdit.SelectedLength == 1 && native.LastPayload == "日😀本" && native.EditCalls == 1,
            "copied Cocoa adapter forwards exact UTF16 selected/replacement ranges instead of dropping or scalar-flooring");
        Check(callbacks.MarkedRange() == new RmlUiCocoaRange(2, 4) && callbacks.Selection() == new RmlUiCocoaRange(5, 1),
            "selection/marked query comes from current numeric native state");
        Check(callbacks.InsertWithReplacement!("日本", RmlUiCocoaRange.None) && native.LastEdit.Kind == RmlUiTextEditKind.Commit
            && native.EditCalls == 2 && !callbacks.Owns(), "payload commit uses the same owner once and releases composition authority");
        Check(!callbacks.InsertWithReplacement("ordinary", RmlUiCocoaRange.None), "ordinary no-range typing stays with existing GLFW owner");
        // Literal SDK ABI value: do not obtain the expected sentinel from the
        // production constant, which hid NSIntegerMax/NSUIntegerMax drift.
        var sdkNotFound = new RmlUiCocoaRange(0x7fffffffffffffffUL, 0);
        Check(RmlUiCocoaRange.None == sdkNotFound, "Cocoa NSNotFound is literal 64-bit NSIntegerMax");
        int literalEdits = native.EditCalls;
        Check(!callbacks.InsertWithReplacement!("a", sdkNotFound) && native.EditCalls == literalEdits && !callbacks.Owns(),
            "literal AppKit ordinary insertion falls through to original GLFW without consuming or editing");
        Check(callbacks.MarkedRange() == new RmlUiCocoaRange(0x7fffffffffffffffUL, 0),
            "unmarked query returns literal AppKit NSNotFound rather than unsigned maximum");
        Check(callbacks.InsertWithReplacement!("invalid unsigned sentinel", new(0xffffffffffffffffUL, 0))
            && native.EditCalls == literalEdits && !callbacks.Owns(),
            "NSUIntegerMax remains an invalid consumed replacement and never reaches native edits");
        Check(callbacks.MarkedWithReplacement!("ime", new(3, 0), new(0x7fffffffffffffffUL, 0)),
            "literal AppKit no-replacement marker starts an owned composition");
        Check(callbacks.InsertWithReplacement!("ime", new(0x7fffffffffffffffUL, 0))
            && native.LastEdit.Kind == RmlUiTextEditKind.Commit && !callbacks.Owns(),
            "literal AppKit composition result commits once and releases its original owner");
        Check(callbacks.InsertWithReplacement("x", new(1, 2)) && native.LastEdit.Kind == RmlUiTextEditKind.Replace
            && native.LastEdit.Start == 1 && native.LastEdit.Length == 2, "explicit unmarked replacement reaches optional real bridge seam");
        ulong first = native.LastEdit.Sequence;
        Check(callbacks.InsertWithReplacement("x", new(1, 2)) && native.LastEdit.Sequence > first,
            "equal consecutive text is not deduplicated by text or time");
        callbacks.MarkedWithReplacement("kept", new(4, 0), RmlUiCocoaRange.None);
        callbacks.Unmark();
        Check(native.LastEdit.Kind == RmlUiTextEditKind.Commit && native.LastEdit.Flags == 1 && native.LastPayload == ""
            && !callbacks.Owns(), "AppKit unmark uses exact owner CommitCurrentMarked with no managed preedit reinsertion");
        callbacks.Unmark();
        callbacks.MarkedWithReplacement!("cancel-owned", new(12, 0), RmlUiCocoaRange.None);
        ime.Cancel(); // fake API synchronously reenters Unmark as NSInputContext may
        int canceledCalls = native.EditCalls;
        Check(callbacks.Owns() && callbacks.InsertWithReplacement!("late canceled result", RmlUiCocoaRange.None)
            && native.EditCalls == canceledCalls, "synchronous unmark during discard cannot release canceled insertion into GLFW");
        callbacks.NewKey();
        callbacks.MarkedWithReplacement!("stale-unmark-revision", new(21, 0), RmlUiCocoaRange.None);
        native.EditRevision++;
        int unmarkCalls = native.EditCalls;
        callbacks.Unmark();
        Check(callbacks.Owns() && callbacks.InsertWithReplacement!("stale", RmlUiCocoaRange.None) && native.EditCalls == unmarkCalls,
            "FIRST stale Unmark after revision change retains discard authority without a preceding refresh");
        callbacks.NewKey();
        callbacks.MarkedWithReplacement!("stale-unmark-focus", new(18, 0), RmlUiCocoaRange.None);
        host.FocusDocument(host.HomeDocument, "apply"); host.FocusDocument(host.HomeDocument, "name");
        unmarkCalls = native.EditCalls;
        callbacks.Unmark();
        Check(callbacks.Owns() && callbacks.InsertWithReplacement!("stale", RmlUiCocoaRange.None) && native.EditCalls == unmarkCalls,
            "FIRST stale Unmark after focus change retains discard authority without a preceding refresh");
        callbacks.NewKey();
        int before = native.EditCalls;
        callbacks.MarkedWithReplacement("new", new(3, 0), RmlUiCocoaRange.None);
        native.EditRevision++; // intervening real owner selection/value mutation
        Check(callbacks.InsertWithReplacement("stale", RmlUiCocoaRange.None) && native.EditCalls == before + 1,
            "new range callback cannot adopt an intervening revision or restore old bytes");
        callbacks.NewKey();
        callbacks.MarkedWithReplacement("focus", new(5, 0), RmlUiCocoaRange.None);
        host.FocusDocument(host.HomeDocument, "apply"); host.FocusDocument(host.HomeDocument, "name");
        before = native.EditCalls;
        Check(callbacks.InsertWithReplacement("late", RmlUiCocoaRange.None) && native.EditCalls == before,
            "late payload cannot enter a later focus lifetime on the same field");
        callbacks.NewKey();
        before = native.EditCalls;
        Check(callbacks.MarkedWithReplacement("bad", new(ulong.MaxValue, 1), RmlUiCocoaRange.None)
            && native.EditCalls == before, "unsigned invalid ranges are not clamped/wrapped before native mutation");
        Check(callbacks.InsertWithReplacement!("must stay discarded", RmlUiCocoaRange.None) && native.EditCalls == before,
            "rejected marked callback still owns/discards its later insert instead of leaking into GLFW");
        callbacks.NewKey();
        Check(host.TryGetTextEditState(out var old), "state before background transition");
        var modal = host.OpenDocument("pages/dialog.rml", RmlUiDocumentLayer.Modal);
        Check(!host.TryGetTextEditState(out _) && host.TextEdit(old, 999, RmlUiTextEditKind.Replace, "forbidden")
            != RmlUiTextEditResult.Accepted, "copied Host rejects native metadata from background document");
        host.CloseDocument(modal);
        var filter = native.FilterEditPacket;
        native.FilterEditPacket = packet => { packet.SelectionLength = int.MaxValue; return packet; };
        Check(!host.TryGetTextEditState(out _), "malformed native lengths fail closed without overflow");
        native.FilterEditPacket = filter;
        native.FilterEditPacket = packet => { packet.Flags = 1; return packet; };
        Check(host.TryGetTextEditState(out var protectedState) && !JsonSerializer.Serialize(protectedState).Contains("stale", StringComparison.Ordinal),
            "protected state contains numeric ranges and geometry only");
        native.FilterEditPacket = filter;
        before = native.EditCalls;
        Check(host.TryGetTextEditState(out var fresh) && host.TextEdit(fresh, 1000, RmlUiTextEditKind.Replace, "\ud800") == RmlUiTextEditResult.Invalid && native.EditCalls == before,
            "malformed UTF16 never reaches native mutation");
        bool ownerThread = Task.Run(() => {
            try { host.TryGetTextEditState(out _); return false; } catch (InvalidOperationException) { return true; }
        }).GetAwaiter().GetResult();
        Check(ownerThread, "optional queries retain actual Host owner thread guard");

        var oldNative = new FakeBridge();
        using var oldHost = new RmlUiHost(oldNative); oldHost.Initialize(640, 360, 1, "."); oldHost.FocusDocument(oldHost.HomeDocument, "name");
        var oldApi = new CocoaApi(); using var oldIme = new RmlUiCocoaIme(oldHost, 2, () => true, oldApi);
        Check(!oldHost.TryGetTextEditState(out _) && oldApi.Callback!.MarkedWithReplacement!("old", new(3, 0), RmlUiCocoaRange.None),
            "older native bridge keeps its existing supported no-range composition");
        oldApi.Callback!.Insert("old");
        before = oldNative.CompositionCalls;
        Check(oldApi.Callback.InsertWithReplacement!("range", new(0, 1)) && oldNative.CompositionCalls == before
            && oldIme.LastFailure.Contains("unsupported", StringComparison.Ordinal), "older bridge cannot silently ignore an explicit replacement range");
        int oldCommits = oldNative.CompositionStages.Count(stage => stage == 2);
        Check(oldApi.Callback.MarkedWithReplacement!("unsupported preedit", new(1, 0), new(0, 1))
            && oldApi.Callback.InsertWithReplacement!("unsupported result", RmlUiCocoaRange.None)
            && oldNative.CompositionStages.Count(stage => stage == 2) == oldCommits, "unsupported ranged marking cannot later fall through as an ordinary GLFW insertion");
        Console.WriteLine($"PASS {checks} managed Host/Cocoa range seam checks; synthetic native and AppKit boundaries only");
    }
    private sealed class CocoaApi : IRmlUiCocoaImeApi
    {
        internal RmlUiCocoaCallbacks? Callback;
        public bool Attach(nint view, RmlUiCocoaCallbacks callbacks) { Callback = callbacks; return true; }
        public bool Detach() => true;
        public void DiscardMarkedText() { Callback?.Unmark(); }
        public void InvalidateCandidatePosition() { }
    }
}
