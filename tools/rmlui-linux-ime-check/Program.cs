using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Host;

if (args.Length < 2 || args[0] != "--native") throw new ArgumentException("Usage: --native LIBRARY [--ibus]");
bool realBus = args.Contains("--ibus");
if (realBus && !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The actual IBus fixture requires Linux.");
nint module = NativeLibrary.Load(Path.GetFullPath(args[1]));
NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly, (name, _, _) => name == "ProjectPrime.RmlUi.Native" ? module : 0);
string root = Path.Combine(AppContext.BaseDirectory, "rmlui");
File.WriteAllText(Path.Combine(root, "linux-ime.rml"), """
<rml><head><title>Linux input contract</title><style>
body { font-family: Rajdhani; font-weight: 600; font-size: 24px; }
input { display: block; width: 400px; height: 50px; tab-index: auto; }
</style></head><body><input id="name" type="text" value="" />
<input id="other" type="text" value="" /><input id="password" type="password" value="" /></body></rml>
""");
int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new InvalidOperationException(message); }
using var host = new RmlUiHost();
Check(host.Initialize(1280, 720, 1, root, RmlUiRenderBackend.DrawList), "Native host failed to initialize");
bool visible = true;
var fake = new FakeBus();
using (var ime = new RmlUiLinuxIme(host, () => visible, b => new((int)b.X, (int)b.Y, (int)b.Width, (int)b.Height), fake))
{
    var doc = host.OpenDocument("linux-ime.rml", RmlUiDocumentLayer.Modal);
    host.SetField(doc, "name", "λ😀"); host.Update(); host.FocusDocument(doc, "name");
    host.Input.Key(10, true, RmlUiInputModifiers.Control); host.Input.Key(10, false, RmlUiInputModifiers.Control);
    ime.Pump(); long first = fake.Session;
    Check(host.TryGetTextInputState(out var firstScope), "Initial text lifetime missing");
    fake.Emit(RmlUiLinuxImeSignalKind.Preedit, "日本😀", 2); ime.Pump();
    Check(host.ReadField(doc, "name") == "λ😀日本😀", "Preedit did not render at the native selection");
    fake.Emit(RmlUiLinuxImeSignalKind.Preedit, "日", 1); ime.Pump();
    Check(host.ReadField(doc, "name") == "λ😀日", "Repeated preedit appended instead of replacing");
    fake.Emit(RmlUiLinuxImeSignalKind.Commit, "日本"); ime.Pump();
    Check(host.ReadField(doc, "name") == "λ😀日本", "Composition commit was not inserted exactly once");
    Check(ime.ProcessKey('a', 30, 0) && ime.SuppressCharacterCallback(), "Accepted key did not own following GLFW text");
    fake.Reply = RmlUiLinuxImeReply.Declined;
    Check(!ime.ProcessKey('e', 18, 0) && !ime.SuppressCharacterCallback(), "Declined key suppressed committed Unicode fallback");
    fake.Reply = RmlUiLinuxImeReply.Accepted;
    fake.Emit(RmlUiLinuxImeSignalKind.Preedit, "取消", 2); ime.Pump(); ime.Cancel();
    Check(host.ReadField(doc, "name") == "λ😀日本", "Cancel lost committed draft");
    host.FocusDocument(doc, "other"); ime.Pump();
    host.SetField(doc, "other", "λ😀"); host.Update();
    Check(host.Input.Dispatch(new(firstScope.Document, RmlUiPlatformInputKind.KeyDown, RmlUiInputDevice.InputMethod,
        firstScope.FocusEpoch, Code: 13)) == RmlUiInputResult.StaleFocus && host.ReadField(doc, "other") == "λ😀",
        "Stale input-method deletion key affected a different field in the same document");
    host.SetField(doc, "other", "");
    fake.EmitAt(first, RmlUiLinuxImeSignalKind.Commit, "late"); ime.Pump();
    Check(host.ReadField(doc, "other") == "", "Delayed bus commit entered a different field");
    fake.Emit(RmlUiLinuxImeSignalKind.Preedit, "draft", 5); ime.Pump();
    visible = false; ime.Pump(); Check(host.ReadField(doc, "other") == "" && fake.Session == 0, "Hidden menu kept composition or bus focus");
    visible = true; host.FocusDocument(doc, "password"); ime.Pump();
    Check(fake.Protected, "Protected field metadata did not select private password input purpose");
    Check(host.TryGetTextInputState(out var secret) && (secret.Capabilities & RmlUiTextInputCapabilities.Protected) != 0,
        "Native password capability metadata was unavailable");
    fake.Emit(RmlUiLinuxImeSignalKind.Preedit, "protected", 9); ime.Pump();
    long protectedSession = fake.Session;
    fake.Reply = RmlUiLinuxImeReply.Failed;
    Check(!ime.ProcessKey('q', 16, 0), "Failed bus request did not release GLFW fallback");
    fake.EmitAt(protectedSession, RmlUiLinuxImeSignalKind.Commit, "late"); ime.Pump();
    Check(host.ReadField(doc, "password") == "", "Timed-out bus context later committed text");
    Check(!new RmlUiLinuxImeSignal(1, RmlUiLinuxImeSignalKind.Commit, "protected").ToString().Contains("protected"), "Input signal diagnostics exposed contents");
    host.FocusDocument(doc, "name"); ime.Pump(); fake.Emit(RmlUiLinuxImeSignalKind.Preedit, "cancel", 6); ime.Pump();
    fake.Status = RmlUiLinuxImeStatus.Unavailable; ime.Pump();
    Check(host.ReadField(doc, "name") == "λ😀日本" && !ime.Available && !ime.SuppressCharacterCallback(),
        "Unavailable transport retained preedit or blocked committed Unicode fallback");
    fake.Status = RmlUiLinuxImeStatus.Available;
    Check(Task.Run(() => { try { ime.Pump(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult(), "Worker called native host input");
    fake.Reply = RmlUiLinuxImeReply.Accepted;
    host.CloseDocument(doc); ime.Pump();
    for (int i = 0; i < 100; i++)
    {
        doc = host.OpenDocument("linux-ime.rml", RmlUiDocumentLayer.Modal); host.FocusDocument(doc, "name"); ime.Pump();
        long session = fake.Session; fake.Emit(RmlUiLinuxImeSignalKind.Preedit, "loop", 4); ime.Pump();
        Check(host.ReadField(doc, "name") == "loop", "Repeated context failed to compose");
        host.CloseDocument(doc); ime.Pump(); fake.EmitAt(session, RmlUiLinuxImeSignalKind.Commit, "late"); ime.Pump();
        Check(!host.IsAlive(doc), "Retired context was revived by late bus event");
    }
}
Check(fake.Disposed, "Bus adapter did not retire its worker ownership");
if (realBus)
{
    using var api = new RmlUiIbusApi("prime-rmlui-test");
    using var ime = new RmlUiLinuxIme(host, () => true, b => new((int)b.X, (int)b.Y, (int)b.Width, (int)b.Height), api);
    var doc = host.OpenDocument("linux-ime.rml", RmlUiDocumentLayer.Modal); host.FocusDocument(doc, "name");
    void Wait(Func<bool> condition, string message)
    {
        var timer = Stopwatch.StartNew();
        bool succeeded = condition();
        while (!succeeded && timer.ElapsedMilliseconds < 4000)
        { ime.Pump(); host.Update(); Thread.Sleep(8); succeeded = condition(); }
        Check(succeeded, message);
    }
    Wait(() => ime.Available, "System IBus daemon did not connect");
    Wait(() => ime.ProcessKey('a', 30, 0), "Fixture engine did not accept first key");
    Wait(() => host.ReadField(doc, "name") == "日本😀", "Real D-Bus Unicode preedit failed");
    Check(ime.SuppressCharacterCallback(), "Real accepted key did not suppress duplicate GLFW text");
    Check(ime.ProcessKey('b', 48, 0), "Fixture update key rejected");
    Wait(() => host.ReadField(doc, "name") == "日", "Real preedit replacement failed");
    Check(ime.ProcessKey('c', 46, 0), "Fixture commit key rejected");
    Wait(() => host.ReadField(doc, "name") == "日本", "Real preedit commit was duplicated");
    Check(ime.ProcessKey('d', 32, 0), "Fixture cancellation key rejected");
    Wait(() => host.ReadField(doc, "name") == "日本取消", "Real cancellation preedit failed");
    ime.Cancel(); Check(host.ReadField(doc, "name") == "日本", "Real reset lost committed text");
    host.FocusDocument(doc, "other"); ime.Pump();
    Wait(() => ime.ProcessKey('x', 45, 0), "Second bus context failed to focus");
    Wait(() => host.ReadField(doc, "other") == "λ😀", "Real direct Unicode commit failed");
    Check(!ime.ProcessKey('e', 18, 0) && !ime.SuppressCharacterCallback(), "Real declined key broke GLFW fallback");
    host.CloseDocument(doc); ime.Pump();
}
Console.WriteLine($"PASS: {checks} real RmlUi/Linux input lifetime checks" + (realBus ? " including system IBus D-Bus composition." : "; real IBus transport requires Linux --ibus fixture."));

sealed class FakeBus : IRmlUiLinuxImeApi
{
    readonly ConcurrentQueue<RmlUiLinuxImeSignal> _signals = new();
    public RmlUiLinuxImeStatus Status { get; set; } = RmlUiLinuxImeStatus.Available;
    public long Session; public bool Protected, Disposed;
    public RmlUiLinuxImeReply Reply = RmlUiLinuxImeReply.Accepted;
    public void Focus(long session, bool protectedField, RmlUiLinuxImeRectangle rectangle) { Session = session; Protected = protectedField; }
    public void Move(long session, RmlUiLinuxImeRectangle rectangle) { }
    public RmlUiLinuxImeReply ProcessKey(long session, uint symbol, uint scanCode, uint modifiers) => Reply;
    public void Emit(RmlUiLinuxImeSignalKind kind, string text = "", int cursor = 0) => EmitAt(Session, kind, text, cursor);
    public void EmitAt(long session, RmlUiLinuxImeSignalKind kind, string text = "", int cursor = 0) => _signals.Enqueue(new(session, kind, text, cursor));
    public bool TryTake(out RmlUiLinuxImeSignal signal) => _signals.TryDequeue(out signal);
    public void Dispose() { Disposed = true; }
}
