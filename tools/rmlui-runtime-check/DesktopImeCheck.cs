using MphRead.Mods.Launcher.RmlUi.Host;

// Synthetic platform callbacks only: no Cocoa runtime, HWND, D-Bus or accounts.
static class DesktopImeCheck
{
    private static int _checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        _checks++;
    }

    public static void Run()
    {
        Cocoa();
        Ibus();
        Console.WriteLine($"Desktop IME mock acceptance passed: {_checks} assertions.");
    }

    private static void Cocoa()
    {
        var native = new FakeBridge();
        using var host = new RmlUiHost(native);
        Check(host.Initialize(640, 360, 1, "."), "Cocoa fixture host initializes");
        host.FocusDocument(host.HomeDocument, "name");
        bool visible = true;
        var api = new CocoaApi();
        using var ime = new RmlUiCocoaIme(host, 123, () => visible, api);
        var callback = api.Callback!;
        Check(ime.Attached && !callback.Insert("ordinary"), "Cocoa ordinary Unicode stays with GLFW");
        Check(callback.Marked("日😀本", 3, 1) && native.CompositionCalls == 2
            && native.CompositionCursor == 2, "Cocoa UTF16 preedit cursor converts to scalar offset");
        Check(callback.Owns() && callback.MarkedRange().Length == 4
            && callback.Selection().Location == 3 && callback.Selection().Length == 1,
            "Cocoa reports UTF16 marked range and selection without extracting text");
        Check(callback.Candidate() is { Width: 640, Height: 360 }, "Cocoa candidate query supplies framebuffer geometry");
        ime.RefreshCandidatePosition();
        Check(api.Invalidations == 1, "Cocoa candidate geometry refresh reaches platform");
        Check(callback.Insert("日本") && native.CompositionCalls == 3 && !callback.Owns(),
            "Cocoa composed commit is consumed exactly once");
        Check(!callback.Insert("ordinary"), "Cocoa later ordinary commit is not swallowed");

        callback.Marked("private", 7, 0);
        host.FocusDocument(host.HomeDocument, "apply");
        host.FocusDocument(host.HomeDocument, "name");
        int before = native.CompositionCalls;
        Check(callback.Insert("late private") && native.CompositionCalls == before,
            "Cocoa late result cannot enter replacement focus lifetime");
        callback.Marked("cancel", 6, 0);
        ime.Cancel();
        before = native.CompositionStages.Count(stage => stage == 2);
        Check(api.Discards == 1 && callback.Insert("late cancel") && native.CompositionStages.Count(stage => stage == 2) == before,
            "Cocoa canceled result is consumed without insertion");
        callback.NewKey();
        visible = false;
        Check(!callback.Marked("hidden", 6, 0), "Cocoa hidden host cannot begin composition");
        visible = true;
        callback.Marked("retired", 7, 0);
        visible = false;
        ime.RefreshCandidatePosition();
        before = native.CompositionStages.Count(stage => stage == 2);
        Check(callback.Insert("late hidden") && native.CompositionStages.Count(stage => stage == 2) == before,
            "Cocoa hiding cancels scope and rejects late results");
        visible = true;
        before = native.CompositionCalls;
        Task.Run(() => callback.Marked("offthread", 9, 0)).GetAwaiter().GetResult();
        Check(native.CompositionCalls == before && ime.LastFailure.Length > 0,
            "Cocoa callback owner-thread guard prevents native calls");
        ime.Dispose(); ime.Dispose();
        Check(!ime.Attached && api.Detaches == 1, "Cocoa adapter detaches once");

        var retained = new CocoaApi { DetachSucceeds = false };
        using var retainedIme = new RmlUiCocoaIme(host, 456, () => true, retained);
        retainedIme.Dispose();
        Check(retainedIme.Attached && retainedIme.LastFailure.Contains("rooted"),
            "Cocoa failed detach retains callback ownership");
        retained.Callback!.Destroyed();
        Check(!retainedIme.Attached, "Cocoa destruction retires retained callback ownership");
    }

    private static void Ibus()
    {
        var native = new FakeBridge();
        using var host = new RmlUiHost(native);
        Check(host.Initialize(640, 360, 1, "."), "IBus fixture host initializes");
        host.FocusDocument(host.HomeDocument, "name");
        bool visible = true;
        var api = new IbusApi();
        using var ime = new RmlUiLinuxIme(host, () => visible,
            bounds => new(11, 22, (int)bounds.Width, (int)bounds.Height), api);
        ime.Pump();
        long first = api.Session;
        Check(ime.Available && first > 0 && api.Rectangle is { X: 11, Y: 22, Width: 260 },
            "IBus focus captures native field lifetime and mapped geometry");
        api.Signals.Enqueue(new(first, RmlUiLinuxImeSignalKind.Preedit, "日😀", 2));
        ime.Pump();
        Check(native.CompositionCalls == 2 && native.CompositionCursor == 2,
            "IBus queued preedit reaches owner thread as scalar composition");
        api.Signals.Enqueue(new(first, RmlUiLinuxImeSignalKind.Commit, "日本"));
        ime.Pump();
        Check(native.CompositionCalls == 3, "IBus composed result commits once");
        Check(ime.ProcessKey(65, 30, 0) && ime.SuppressCharacterCallback(),
            "IBus accepted key suppresses duplicate GLFW characters");
        Check(ime.ProcessKey(65, 30, 0, released: true) && (api.Modifiers & (1u << 30)) != 0,
            "IBus release preserves platform release modifier");
        api.Reply = RmlUiLinuxImeReply.Declined;
        Check(!ime.ProcessKey(66, 31, 0) && !ime.SuppressCharacterCallback(),
            "IBus declined next key restores ordinary Unicode fallback");
        host.FocusDocument(host.HomeDocument, "apply");
        host.FocusDocument(host.HomeDocument, "name");
        api.Signals.Enqueue(new(first, RmlUiLinuxImeSignalKind.Commit, "late secret"));
        int before = native.Codepoints.Count;
        ime.Pump();
        Check(api.Session != first && native.Codepoints.Count == before,
            "IBus stale session commit cannot enter replacement focus");
        long current = api.Session;
        api.Signals.Enqueue(new(current, RmlUiLinuxImeSignalKind.Commit, "λ😀"));
        ime.Pump();
        Check(native.Codepoints.Skip(before).SequenceEqual(new uint[] { 955, 0x1f600 }),
            "IBus commit without preedit inserts Unicode scalars once");
        api.Signals.Enqueue(new(current, RmlUiLinuxImeSignalKind.Preedit, "hide", 4));
        ime.Pump();
        visible = false;
        ime.Pump();
        before = native.CompositionCalls;
        api.Signals.Enqueue(new(current, RmlUiLinuxImeSignalKind.Commit, "late hidden"));
        ime.Pump();
        Check(api.Session == 0 && native.CompositionCalls == before,
            "IBus hidden owner cancels scope and drains late results");
        visible = true; ime.Pump();
        api.Reply = RmlUiLinuxImeReply.Failed;
        Check(!ime.ProcessKey(65, 30, 0) && !ime.SuppressCharacterCallback()
            && ime.LastFailure.Length > 0, "IBus failed request restores Unicode fallback");
        api.Status = RmlUiLinuxImeStatus.Unavailable;
        before = api.Keys;
        Check(!ime.ProcessKey(65, 30, 0) && api.Keys == before,
            "IBus unavailable service never receives keys");
        bool rejected = false;
        Task.Run(() => { try { ime.Pump(); } catch (InvalidOperationException) { rejected = true; } })
            .GetAwaiter().GetResult();
        Check(rejected, "IBus pump enforces owner thread");
        Check(!new RmlUiLinuxImeSignal(1, RmlUiLinuxImeSignalKind.Commit, "private").ToString().Contains("private"),
            "IBus signal diagnostics omit composition contents");
        ime.Dispose(); ime.Dispose();
        Check(api.Disposals == 1, "IBus platform worker disposed once");
    }

    private sealed class CocoaApi : IRmlUiCocoaImeApi
    {
        public RmlUiCocoaCallbacks? Callback;
        public int Detaches, Discards, Invalidations;
        public bool DetachSucceeds = true;
        public bool Attach(nint view, RmlUiCocoaCallbacks callbacks) { Callback = callbacks; return true; }
        public bool Detach() { Detaches++; return DetachSucceeds; }
        public void DiscardMarkedText() => Discards++;
        public void InvalidateCandidatePosition() => Invalidations++;
    }

    private sealed class IbusApi : IRmlUiLinuxImeApi
    {
        public RmlUiLinuxImeStatus Status { get; set; } = RmlUiLinuxImeStatus.Available;
        public readonly Queue<RmlUiLinuxImeSignal> Signals = new();
        public long Session;
        public RmlUiLinuxImeRectangle Rectangle;
        public RmlUiLinuxImeReply Reply = RmlUiLinuxImeReply.Accepted;
        public uint Modifiers;
        public int Keys, Disposals;
        public void Focus(long session, bool protectedField, RmlUiLinuxImeRectangle rectangle)
        { Session = session; Rectangle = rectangle; }
        public void Move(long session, RmlUiLinuxImeRectangle rectangle) => Rectangle = rectangle;
        public RmlUiLinuxImeReply ProcessKey(long session, uint symbol, uint scanCode, uint modifiers)
        { Keys++; Modifiers = modifiers; return Reply; }
        public bool TryTake(out RmlUiLinuxImeSignal signal) => Signals.TryDequeue(out signal);
        public void Dispose() => Disposals++;
    }
}
