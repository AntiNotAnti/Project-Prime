using System.Runtime.InteropServices;
using System.Text;
using MphRead.Mods.Launcher.RmlUi.Host;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Require(Marshal.SizeOf<RmlUiNativeIntent>() == RmlUiIntentRegistry.NativeIntentSize, "ABI structure size changed");
var protocolProbe = new FakeBridge();
protocolProbe.Initialize(640, 360, 1, ".");
protocolProbe.Queue(new(protocolProbe.Generation(), protocolProbe.HomeDocument()), RmlUiIntentKind.Quit, 1);
var invalidRequest = new RmlUiNativeIntent { Size = RmlUiIntentRegistry.NativeIntentSize, Version = 0 };
Require(protocolProbe.TakeIntent(ref invalidRequest) == 0, "Native protocol probe accepted request version zero");
invalidRequest.Version = RmlUiIntentRegistry.ProtocolVersion;
Require(protocolProbe.TakeIntent(ref invalidRequest) == 1, "Invalid request drained the pending event");
foreach (RmlUiIntentKind kind in Enum.GetValues<RmlUiIntentKind>())
{
    int count = kind switch
    {
        RmlUiIntentKind.Navigate => 10,
        RmlUiIntentKind.StageSelect or RmlUiIntentKind.StagePreview => 4,
        RmlUiIntentKind.PlayServer => 8,
        RmlUiIntentKind.LobbyRulesToggle => 16,
        _ => 1
    };
    for (int argument = 0; argument < count; argument++)
    {
        var intent = new RmlUiIntent(kind, argument, new(1, 1), 1);
        string action = RmlUiIntentRegistry.ToLegacy(intent);
        Require(RmlUiIntentRegistry.TryParseLegacy(action, intent.Document, intent.Sequence, out var parsed)
            && parsed == intent, $"Legacy registry roundtrip failed: {action}");
    }
    Require(!RmlUiIntentRegistry.IsValid(kind, count), $"Out-of-range argument accepted for {kind}");
}
foreach (string bad in new[] { "play:server:-1", "play:server:08", "play:server:8", "lobby:rules-toggle:16", "route:unexpected", "quit:extra" })
    Require(!RmlUiIntentRegistry.TryParseLegacy(bad, new(1, 1), 1, out _), "Invalid legacy intent accepted");

var native = new FakeBridge();
using var host = new RmlUiHost(native);
Require(host.Initialize(1920, 1080, 1.5f, "."), "Host initialization failed");
RmlUiDocumentToken initial = host.HomeDocument;
var source = new Dictionary<string, RmlUiBindingValue> { ["player_name"] = RmlUiBindingValue.FromText("Hunter λ") };
var snapshot = new RmlUiBindingSnapshot(initial, 1, source);
source["player_name"] = RmlUiBindingValue.FromText("mutated source");
Require(host.Present(snapshot) && native.Texts[(initial.DocumentId, "player_name")] == "Hunter λ", "Snapshot aliases mutable source");
int writes = native.TextWrites;
Require(!host.Present(snapshot), "Duplicate revision accepted");
Require(host.Present(new(initial, 2, snapshot.Bindings)) && native.TextWrites == writes, "Unchanged snapshot dirtied native DOM");

Task.Run(() => host.EnqueueSnapshot(new(initial, 3, new Dictionary<string, RmlUiBindingValue>
{
    ["player_name"] = RmlUiBindingValue.FromText("worker completion")
}))).GetAwaiter().GetResult();
Require(native.Texts[(initial.DocumentId, "player_name")] == "Hunter λ", "Worker called native bridge");
host.Update();
Require(native.Texts[(initial.DocumentId, "player_name")] == "worker completion", "Owner did not apply queued snapshot");
bool guarded = Task.Run(() =>
{
    try { host.SetText(initial, "player_name", "unsafe worker"); return false; }
    catch (InvalidOperationException) { return true; }
}).GetAwaiter().GetResult();
Require(guarded, "Wrong-thread native call was permitted");

host.SetField(initial, "draft", "locally edited");
host.Present(new(initial, 4, snapshot.Bindings));
Require(host.ReadField(initial, "draft") == "locally edited", "Snapshot overwrote an input draft");
using (var cancelled = new CancellationTokenSource())
{
    host.EnqueueSnapshot(new(initial, 5, source), cancelled.Token);
    cancelled.Cancel();
    host.Update();
    Require(native.Texts[(initial.DocumentId, "player_name")] == "Hunter λ", "Cancelled work reached native DOM");
}

for (int i = 0; i < 100; i++)
{
    var modal = host.OpenDocument("pages/rules.rml", RmlUiDocumentLayer.Modal);
    CancellationToken lifetime = host.DocumentCancellation(modal);
    host.EnqueueSnapshot(new(modal, 1, source));
    Require(host.ShowDocument(modal, true) && host.FocusDocument(modal, "apply"), "Document focus/visibility failed");
    Require(host.CloseDocument(modal) && lifetime.IsCancellationRequested, "Close did not retire document work");
    host.Update();
    Require(!native.Texts.ContainsKey((modal.DocumentId, "player_name")), "Retired modal received stale completion");
    native.Queue(modal, RmlUiIntentKind.LobbyRulesApply, sequence: (ulong)i + 1);
    Require(!host.TryTakeIntent(out _), "Retired document dispatched an intent");
}
Require(native.Documents.Count == 1, "Document resources leaked across open/close cycles");
var visibleModal = host.OpenDocument("pages/visible.rml", RmlUiDocumentLayer.Modal);
var hiddenModal = host.OpenDocument("pages/hidden.rml", RmlUiDocumentLayer.Modal);
CancellationToken visibleLifetime = host.DocumentCancellation(visibleModal);
host.ShowDocument(hiddenModal, false);
host.EnqueueSnapshot(new(visibleModal, 1, source));
Require(host.Back() && visibleLifetime.IsCancellationRequested && !host.IsAlive(visibleModal)
    && host.IsAlive(hiddenModal), "Back did not retire the topmost visible modal lifetime");
host.Update();
Require(!native.Texts.ContainsKey((visibleModal.DocumentId, "player_name")), "Back allowed a late modal completion");
Require(host.ShowDocument(hiddenModal, true) && host.Back() && !host.IsAlive(hiddenModal),
    "Shown modal did not regain Back priority");
var firstModal = host.OpenDocument("pages/first.rml", RmlUiDocumentLayer.Modal);
var secondModal = host.OpenDocument("pages/second.rml", RmlUiDocumentLayer.Modal);
Require(host.ShowDocument(firstModal, true) && host.Back() && !host.IsAlive(firstModal)
    && host.IsAlive(secondModal), "Showing a prior modal did not bring it to the top of the Back stack");
Require(host.Back() && !host.IsAlive(secondModal), "Remaining modal did not regain Back priority");
var throwingModal = host.OpenDocument("pages/throwing.rml", RmlUiDocumentLayer.Modal);
CancellationToken throwingLifetime = host.DocumentCancellation(throwingModal);
ulong generationBeforeClose = native.Generation();
using (throwingLifetime.Register(() =>
{
    Require(!host.IsAlive(throwingModal), "Retired document still appears live inside cancellation callback");
    throw new InvalidOperationException("intentional cancellation callback failure");
}))
using (throwingLifetime.Register(() => host.Reinitialize()))
{
    Require(host.CloseDocument(throwingModal), "Throwing cancellation aborted document close");
    Require(!host.IsAlive(throwingModal) && host.LastCleanupError != null,
        "Throwing callback retained document ownership or was not reported");
    Require(native.Generation() == generationBeforeClose, "Cancellation callback reentered native lifetime");
}
try { host.OpenDocument("../outside.rml", RmlUiDocumentLayer.Page); throw new Exception("Traversal accepted"); }
catch (ArgumentException) { }

native.Queue(initial, RmlUiIntentKind.LobbyReady, sequence: 101, argument: 1);
native.Queue(initial, RmlUiIntentKind.LobbyReady, sequence: 102);
Require(host.TryTakeIntent(out var ready) && ready.Kind == RmlUiIntentKind.LobbyReady && ready.Sequence == 102,
    "Malformed event prevented valid typed action dispatch");
native.Queue(initial, RmlUiIntentKind.LobbyStart, sequence: 102);
Require(!host.TryTakeIntent(out _), "Duplicate native sequence dispatched");
host.SetBool(initial, "multiplayer_mode", false);
native.Bools[(initial.DocumentId, "multiplayer_mode")] = true; // DOM callback owns its local state.
native.Queue(initial, RmlUiIntentKind.PlayCancel, sequence: 103);
Require(host.TryTakeIntent(out _), "Play cancel intent missing");
host.SetBool(initial, "multiplayer_mode", false);
Require(!native.Bools[(initial.DocumentId, "multiplayer_mode")], "Input changed native state but managed cache suppressed restoration");

host.Input.SetFramebufferScale(1.5f, 2f);
host.Input.PointerButton(0, -10.5, 100.25, true);
Require(native.Pointer == (-16, 201), "Pointer was scaled twice or clamped inside the viewport");
host.Input.Text("λ😀");
Require(native.Codepoints.SequenceEqual(new uint[] { 955, 0x1f600 }), "Unicode text was split into UTF-16 surrogates");
host.ReleaseInput();
Require(native.FocusReleaseCount == 1, "Hidden UI did not release input focus");
host.SetClipboard("копия λ😀");
Require(host.ReadClipboard() == "копия λ😀", "Unicode clipboard roundtrip failed");

for (int i = 0; i < 100; i++)
{
    var retired = host.HomeDocument;
    CancellationToken lifetime = host.DocumentCancellation(retired);
    Require(host.Reinitialize() && lifetime.IsCancellationRequested, "Reinitialize did not cancel prior runtime lifetime");
    Require(!host.IsAlive(retired), "Old runtime generation remains valid");
    host.EnqueueSnapshot(new(retired, 10, source));
    native.Queue(retired, RmlUiIntentKind.Quit, sequence: 1);
    host.Update();
    Require(!host.TryTakeIntent(out _), "Old runtime dispatched into new generation");
}
Require(native.Documents.Count == 1, "Reinitialization leaked native documents");
var shutdownHome = host.HomeDocument;
var shutdownModal = host.OpenDocument("pages/throwing.rml", RmlUiDocumentLayer.Modal);
CancellationToken shutdownHomeLifetime = host.DocumentCancellation(shutdownHome);
CancellationToken shutdownModalLifetime = host.DocumentCancellation(shutdownModal);
Action<Exception> throwingReporter = _ => throw new InvalidOperationException("intentional reporting failure");
host.CleanupFailed += throwingReporter;
using (shutdownHomeLifetime.Register(() => throw new InvalidOperationException("intentional home cancellation failure")))
using (shutdownModalLifetime.Register(() => throw new InvalidOperationException("intentional modal cancellation failure")))
{
    host.Shutdown();
    Require(!host.Active && host.HomeDocument == default && !host.IsAlive(shutdownHome)
        && !host.IsAlive(shutdownModal) && shutdownHomeLifetime.IsCancellationRequested
        && shutdownModalLifetime.IsCancellationRequested && native.Documents.Count == 0,
        "Throwing callback or reporter interrupted runtime teardown");
}
host.CleanupFailed -= throwingReporter;
Require(host.Initialize(1920, 1080, 1.5f, "."), "Throwing teardown callback prevented reinitialization");
host.DeviceLost();
Require(!host.Active && native.Documents.Count == 0, "Device-loss teardown retained resources");
host.Shutdown();
Require(host.Initialize(640, 360, 1, "."), "Device-loss recovery failed");

using var legacy = new RmlUiHost(new FakeBridge { Legacy = true });
Require(legacy.Initialize(640, 360, 1, "."), "Legacy bridge compatibility initialization failed");
Require(legacy.ProtocolVersion == 0, "Legacy ABI negotiation failed");
Console.WriteLine("RmlUi runtime checks passed: typed ABI, owner thread, immutable revisioned snapshots, cancellation, document lifecycle, DPI, Unicode, clipboard, and 100 reinitializations.");
if (args.Length != 0)
{
    if (args.Length != 3 || args[0] != "--native")
        throw new ArgumentException("Usage: rmlui-runtime-check [--native <bridge-library> <rmlui-assets-directory>]");
    ManagedHostCheck.Run(args[1], args[2]);
}

sealed class FakeBridge : IRmlUiNativeBridge
{
    public bool Legacy;
    private ulong _generation, _nextDocument, _home;
    private string _clipboard = "";
    public readonly HashSet<ulong> Documents = new();
    public readonly Dictionary<(ulong, string), string> Texts = new();
    public readonly Dictionary<(ulong, string), bool> Bools = new();
    private readonly Dictionary<(ulong, string), string> _fields = new();
    private readonly Queue<RmlUiNativeIntent> _intents = new();
    public readonly List<uint> Codepoints = new();
    public (int, int) Pointer;
    public int TextWrites, FocusReleaseCount;
    public uint ProtocolVersion() => Legacy ? throw new EntryPointNotFoundException() : 1u;
    public ulong Generation() => _generation;
    public ulong HomeDocument() => _home;
    public int Initialize(int width, int height, float density, string root) => InitializeBackend(width, height, density, root, 0);
    public int InitializeBackend(int width, int height, float density, string root, int backend)
    {
        _generation++;
        _home = ++_nextDocument;
        Documents.Add(_home);
        return 1;
    }
    public void Shutdown() { Documents.Clear(); Texts.Clear(); Bools.Clear(); _fields.Clear(); _intents.Clear(); }
    public void Resize(int width, int height, float density) { }
    public void Update() { }
    public void Render(int width, int height) { }
    public int TakeIntent(ref RmlUiNativeIntent packet)
    {
        if (packet.Size != RmlUiIntentRegistry.NativeIntentSize
            || packet.Version != RmlUiIntentRegistry.ProtocolVersion) return 0;
        if (!_intents.TryDequeue(out var queued)) return 0;
        packet = queued;
        return 1;
    }
    public int TakeAction(byte[] buffer, int capacity) => 0;
    public void Queue(RmlUiDocumentToken document, RmlUiIntentKind kind, ulong sequence, int argument = 0) =>
        _intents.Enqueue(new() { Size = 40, Version = 1, Kind = (uint)kind, Argument = argument,
            Generation = document.Generation, DocumentId = document.DocumentId, Sequence = sequence });
    public ulong OpenDocument(string path, int layer) { ulong id = ++_nextDocument; Documents.Add(id); return id; }
    public int CloseDocument(ulong document) => Documents.Remove(document) ? 1 : 0;
    public int ShowDocument(ulong document, int show) => Documents.Contains(document) ? 1 : 0;
    public int FocusDocument(ulong document, string element) => Documents.Contains(document) ? 1 : 0;
    public void SetText(string name, string value) => DocumentSetText(_home, name, value);
    public void SetBool(string name, int value) => DocumentSetBool(_home, name, value);
    public void SetField(string id, string value) => DocumentSetField(_home, id, value);
    public int ReadField(string id, byte[] buffer, int capacity) => DocumentReadField(_home, id, buffer, capacity);
    public int DocumentSetText(ulong document, string name, string value) { Texts[(document, name)] = value; TextWrites++; return 1; }
    public int DocumentSetBool(ulong document, string name, int value) { Bools[(document, name)] = value != 0; return 1; }
    public int DocumentSetField(ulong document, string id, string value) { _fields[(document, id)] = value; return 1; }
    public int DocumentReadField(ulong document, string id, byte[] buffer, int capacity) =>
        Copy(_fields.GetValueOrDefault((document, id), ""), buffer, capacity);
    public void SetLobbyAnchor(int slot, float x, float y) { }
    public int Back() => 0;
    public int MouseMove(int x, int y, int modifiers) { Pointer = (x, y); return 1; }
    public int MouseButton(int button, int down, int modifiers) => 1;
    public int MouseWheel(float delta, int modifiers) => 1;
    public int Key(int key, int down, int modifiers) => 1;
    public int Text(uint codepoint) { Codepoints.Add(codepoint); return 1; }
    public void FocusLost() => FocusReleaseCount++;
    public int TextInputActive() => 1;
    public int FocusedElement(byte[] buffer, int capacity) => Copy("apply", buffer, capacity);
    public void SetClipboard(string text) => _clipboard = text;
    public int ReadClipboard(byte[] buffer, int capacity) => Copy(_clipboard, buffer, capacity);
    private static int Copy(string value, byte[] buffer, int capacity)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        int length = Math.Min(bytes.Length, capacity - 1);
        Array.Copy(bytes, buffer, length);
        buffer[length] = 0;
        return length;
    }
}
