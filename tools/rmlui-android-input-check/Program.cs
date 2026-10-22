using MphRead.Droid;
using MphRead.Mods.Launcher.RmlUi.Host;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
var native = new FakeBridge();
using var host = new RmlUiHost(native);
string assetRoot = Directory.GetCurrentDirectory();
Check(host.Initialize(1280, 720, 2, assetRoot, RmlUiRenderBackend.DrawList), "fake draw-list host initializes");
host.FocusDocument(host.HomeDocument, "field");
var queue = new RmlUiAndroidInputQueue(capacity: 4);
queue.Resume(host);
var first = queue.Current;
Check(first.Active && first.Document == host.HomeDocument && first.HasTextInput, "owner publishes active document and text focus");
var down = new RmlUiPlatformInputEvent(first.Document, RmlUiPlatformInputKind.PointerDown,
    RmlUiInputDevice.Touch, X: 101, Y: 203);
Check(Task.Run(() => queue.TryEnqueue(first, down)).GetAwaiter().GetResult(), "UI worker enqueues copied touch without native calls");
Check(native.Buttons == 0, "native pointer remains untouched before EGL-owner drain");
queue.Drain(host);
Check(native.Buttons == 1 && native.Pointer == (101, 203), "framebuffer pixels cross exactly once, without density multiplication");

Check(queue.TryEnqueue(first, new(first.Document, RmlUiPlatformInputKind.KeyDown, Code: 1)), "keyboard press queued");
queue.Drain(host);
Check(native.Keys != 0, "keyboard is held before cancellation");
Task.Run(queue.Suspend).GetAwaiter().GetResult();
Check(!queue.Current.Active && !queue.TryEnqueue(first, down), "UI suspend rejects old surface witness immediately");
Check(native.Buttons != 0 && native.Keys != 0, "UI suspend never calls native");
queue.Drain(host);
Check(native.Buttons == 0 && native.Keys == 0 && native.Releases == 1, "owner flush releases stranded touch and key");
queue.Resume(host);
Check(queue.Current.Active && queue.Current.SurfaceGeneration != first.SurfaceGeneration
    && !queue.TryEnqueue(first, down), "resume publishes new surface lifetime");

host.FocusDocument(host.HomeDocument, "field"); queue.Refresh(host);
var ime = queue.Current;
Check(queue.TryEnqueue(ime, new(ime.Document, RmlUiPlatformInputKind.CompositionBegin,
    RmlUiInputDevice.InputMethod, ime.TextInput.FocusEpoch)), "IME begin retains connection focus witness");
queue.Drain(host);
Check(native.Composing, "owner begins composition");
host.FocusDocument(host.HomeDocument, "other-field"); queue.Refresh(host);
Check(!queue.TryEnqueue(ime, new(ime.Document, RmlUiPlatformInputKind.CompositionCommit,
    RmlUiInputDevice.InputMethod, ime.TextInput.FocusEpoch, Text: "private input")), "stale IME connection cannot commit into a newly focused field");
var currentIme = queue.Current;
Check(queue.TryEnqueue(currentIme, new(currentIme.Document, RmlUiPlatformInputKind.TextCommitted,
    RmlUiInputDevice.InputMethod, currentIme.TextInput.FocusEpoch, Text: "A😀")), "fresh IME commit queued");
queue.Drain(host);
Check(native.Text.SequenceEqual(new uint[] { 'A', 0x1f600 }), "fresh UTF-16 commit reaches Unicode scalar codec once");

// Queued text may become stale before the owner drains it; Dispatch checks the
// actual native focus, rather than trusting the UI's previous snapshot.
currentIme = queue.Current;
Check(queue.TryEnqueue(currentIme, new(currentIme.Document, RmlUiPlatformInputKind.TextCommitted,
    RmlUiInputDevice.InputMethod, currentIme.TextInput.FocusEpoch, Text: "stale")), "queued IME captures original epoch");
host.FocusDocument(host.HomeDocument, "next-field");
queue.Drain(host);
Check(native.Text.Count == 2, "focus change before drain rejects stale queued text");

var deletion = queue.Current;
Check(queue.TryEnqueue(deletion, new(deletion.Document, RmlUiPlatformInputKind.KeyDown,
    RmlUiInputDevice.InputMethod, deletion.TextInput.FocusEpoch, Code: 5)), "IME deletion key retains focus witness");
queue.Drain(host);
host.FocusDocument(host.HomeDocument, "replacement-field"); queue.Refresh(host);
Check(!queue.TryEnqueue(deletion, new(deletion.Document, RmlUiPlatformInputKind.KeyUp,
    RmlUiInputDevice.InputMethod, deletion.TextInput.FocusEpoch, Code: 5)), "old IME key-up rejected after focus replacement");
queue.Drain(host);
Check(native.Keys == 0 && queue.Current.HasTextInput, "rejected focus-scoped key-up releases stranded key while retaining current field focus");

var beforeModal = queue.Current;
Check(queue.TryEnqueue(beforeModal, down with { Document = beforeModal.Document }), "pointer before modal queued");
queue.Drain(host);
Check(native.Buttons == 1, "pointer held while route changes");
var modal = host.OpenDocument("pages/unit.rml", RmlUiDocumentLayer.Modal);
host.FocusDocument(modal, "modal-field");
queue.Refresh(host);
Check(native.Buttons == 0 && queue.Current.Document == modal && queue.Current.HasTextInput,
    "modal replacement releases prior hold and preserves newly authored focus with a fresh epoch");
Check(!queue.TryEnqueue(beforeModal, down), "still-alive underlying page cannot receive old queued events");

var full = queue.Current;
Check(queue.TryEnqueue(full, new(full.Document, RmlUiPlatformInputKind.PointerDown, X: 2, Y: 3)), "overflow precondition press queued");
queue.Drain(host);
for (int i = 0; i < 4; i++) Check(queue.TryEnqueue(full,
    new(full.Document, RmlUiPlatformInputKind.Wheel, Delta: i)), "bounded non-coalescible burst queued");
Check(!queue.TryEnqueue(full, new(full.Document, RmlUiPlatformInputKind.PointerUp, X: 4, Y: 2))
    && queue.Metrics.Pending == 0 && queue.Metrics.Overflows == 1, "overflow discards burst and reserves owner release instead of losing only pointer-up");
int releases = native.Releases;
queue.Drain(host);
Check(native.Buttons == 0 && native.Releases == releases + 1, "overflow guarantees one owner-thread release");
Check(!queue.TryEnqueue(full, down with { Document = full.Document }), "overflow retires cached input witness");
Check(!queue.TryEnqueue(queue.Current, new(queue.Current.Document, RmlUiPlatformInputKind.TextCommitted,
    Text: new string('x', RmlUiAndroidInputQueue.MaximumTextLength + 1))), "text payload is bounded before it enters queue");

Exception? wrongThread = null;
Task.Run(() => { try { queue.Drain(host); } catch (Exception error) { wrongThread = error; } }).GetAwaiter().GetResult();
Check(wrongThread is InvalidOperationException, "native drain rejects a different thread");
wrongThread = null;
Task.Run(() => { try { queue.Refresh(host); } catch (Exception error) { wrongThread = error; } }).GetAwaiter().GetResult();
Check(wrongThread is InvalidOperationException, "native ownership publication rejects a different thread");

queue.Suspend(); queue.Drain(host); host.Shutdown(); queue.Refresh(host);
Check(!queue.Current.Active && queue.Current.Document == default && queue.Metrics.Pending == 0,
    "shutdown leaves no document/input ownership");

// Link the real Android session orchestration, replacing only native and GLES.
var sessionNative = new FakeBridge();
var sessionHost = new RmlUiHost(sessionNative);
using (var session = new RmlUiAndroidDrawListSession(sessionHost))
{
    Check(session.Initialize(800, 600, 1.5f, assetRoot), "production Android session initializes input foundation");
    var scope = session.Input.Current;
    Check(session.Input.TryEnqueue(scope, new(scope.Document, RmlUiPlatformInputKind.PointerDown, X: 12, Y: 24)), "production session receives queued UI touch");
    session.Render(800, 600, 1.5f);
    Check(sessionNative.Buttons == 1 && sessionNative.InputPrecededUpdate,
        "production session drains on owner before native update/render");
    Check(session.Input.TryEnqueue(scope, new(scope.Document, RmlUiPlatformInputKind.PointerMove, X: 99, Y: 88)),
        "pre-resize move retains old contact/layout witness");
    session.Render(900, 600, 1.5f);
    Check(sessionNative.ButtonsAtResize == 0 && sessionNative.Pointer == (12, 24),
        "size-only reflow releases held contact before Resize and discards old queued coordinates");
    Check(session.Input.Current.Active && session.Input.Current.SurfaceGeneration != scope.SurfaceGeneration
        && !session.Input.TryEnqueue(scope, new(scope.Document, RmlUiPlatformInputKind.PointerUp)),
        "size-only reflow resumes fresh ownership and rejects pre-resize pointer-up");
    sessionHost.FocusDocument(sessionHost.HomeDocument, "field"); session.Input.Refresh(sessionHost);
    var reflowIme = session.Input.Current;
    Check(session.Input.TryEnqueue(reflowIme, new(reflowIme.Document, RmlUiPlatformInputKind.CompositionBegin,
        RmlUiInputDevice.InputMethod, reflowIme.TextInput.FocusEpoch)), "IME begins before density-only reflow");
    session.Render(900, 600, 1.5f);
    Check(sessionNative.Composing && session.Input.TryEnqueue(reflowIme,
        new(reflowIme.Document, RmlUiPlatformInputKind.TextCommitted, RmlUiInputDevice.InputMethod,
            reflowIme.TextInput.FocusEpoch, Text: "must not cross reflow")), "queued IME commit retains old layout/focus lifetime");
    session.Render(900, 600, 2);
    Check(!sessionNative.Composing && sessionNative.Text.Count == 0 && session.Input.Current.Active
        && !session.Input.TryEnqueue(reflowIme, new(reflowIme.Document, RmlUiPlatformInputKind.CompositionCommit,
            RmlUiInputDevice.InputMethod, reflowIme.TextInput.FocusEpoch, Text: "stale")),
        "density-only reflow retires preedit and queued/delayed stale IME without replaying text");
    session.SuspendInput();
    session.Render(1000, 600, 2);
    Check(!session.Input.Current.Active && sessionNative.Buttons == 0, "session flushes input before EGL surface release");
    Check(!session.Input.Current.Active, "reflow of a suspended/unfocused route does not force input resume");
    session.ResumeInput();
    Check(session.Input.Current.Active && !session.Input.TryEnqueue(scope,
        new(scope.Document, RmlUiPlatformInputKind.PointerUp)), "session resume refuses pre-suspend contact");
    sessionNative.OnResize = () => Task.Run(session.Input.Suspend).Wait();
    session.Render(1100, 600, 2);
    Check(!session.Input.Current.Active, "UI suspension during Resize wins over conditional owner layout resume");
    sessionNative.OnResize = null; session.ResumeInput();
    session.Shutdown();
    Check(!session.Active && !session.Input.Current.Active && session.Input.Metrics.Pending == 0,
        "production session shutdown retires host and input together");
}
var failedNative = new FakeBridge();
using (var failed = new RmlUiAndroidDrawListSession(new RmlUiHost(failedNative)))
{
    Check(failed.Initialize(640, 360, 1, assetRoot), "release-failure fixture initializes");
    int released = RmlUiAndroidGlesCompositor.ReleaseCalls;
    failedNative.ThrowOnRelease = true;
    bool rejected = false;
    try { failed.Shutdown(); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected && !failed.Active && !failed.Input.Current.Active
        && RmlUiAndroidGlesCompositor.ReleaseCalls == released + 1,
        "input release failure still retires native host and runs GLES cleanup");
    failedNative.ThrowOnRelease = false;
}
Check(native.WrongThreadCalls == 0 && sessionNative.WrongThreadCalls == 0, "no native call escaped owner thread");
PointerBurstAcceptance.Run(Check);
Console.WriteLine($"RMLUI ANDROID INPUT PASS {checks} checks; production queue/session with fake native/GLES, no APK/startup/touch/IME/device acceptance.");
