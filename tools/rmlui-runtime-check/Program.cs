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
        RmlUiIntentKind.Navigate => 11,
        RmlUiIntentKind.ResultsAction => 6,
        RmlUiIntentKind.ResultsMap => 4096,
        RmlUiIntentKind.AimResultsAction => 3,
        RmlUiIntentKind.SetupAction => 15,
        RmlUiIntentKind.SetupRelease => 30,
        RmlUiIntentKind.HudAction => 45,
        RmlUiIntentKind.HudElement => 14,
        RmlUiIntentKind.HudProperty => 8,
        RmlUiIntentKind.SocialAction => 53,
        RmlUiIntentKind.NewsAction => 12,
        RmlUiIntentKind.CommunityTab => 3,
        RmlUiIntentKind.CommunitySort => 3,
        RmlUiIntentKind.CommunityLifecycle => 4,
        RmlUiIntentKind.CommunityPage => 2,
        RmlUiIntentKind.CommunitySelect => 8,
        RmlUiIntentKind.CommunityDetailAction => 10,
        RmlUiIntentKind.CommunityRevisionPage => 2,
        RmlUiIntentKind.CommunitySelectRevision => 8,
        RmlUiIntentKind.CommunityCreatorAction => 5,
        RmlUiIntentKind.CommunityUpload => 2,
        RmlUiIntentKind.CommunityVisibility => 3,
        RmlUiIntentKind.CommunityReportReason => 6,
        RmlUiIntentKind.CommunityConflict => 5,

        RmlUiIntentKind.StageSelect or RmlUiIntentKind.StagePreview => 5,
        RmlUiIntentKind.PlayQueueAction => 4,
        RmlUiIntentKind.PlayServer => 8,
        RmlUiIntentKind.HunterRotate or RmlUiIntentKind.HunterZoom => 2,
        RmlUiIntentKind.InGameAction => 27,
        RmlUiIntentKind.LicenseAction => 21,
        RmlUiIntentKind.StudioAction => 5,
        RmlUiIntentKind.TheatreAction => 25,
        RmlUiIntentKind.TheatreEntry => 8,
        RmlUiIntentKind.TheatreThumbnail => 3,
        RmlUiIntentKind.ReplayAction => 13,
        RmlUiIntentKind.OfflineSelectArena => 16,
        RmlUiIntentKind.OfflineArenaPage => 2,
        RmlUiIntentKind.LobbyRulesToggle => 16,
        RmlUiIntentKind.LobbyPlayerSelect or RmlUiIntentKind.LobbySlotTeamNext => 8,
        RmlUiIntentKind.LobbyMapSelect => 6,
        RmlUiIntentKind.LobbyMapCategory => 2,
        RmlUiIntentKind.LobbyTeamSelect => 4,
        RmlUiIntentKind.HunterSelect => 7,
        RmlUiIntentKind.HunterSuit => 4,
        RmlUiIntentKind.HunterPreviewMode => 4,
        RmlUiIntentKind.SettingsCategory => 13,
        RmlUiIntentKind.SettingsAction => 63,
        RmlUiIntentKind.AdventureSelectSlot => 3,
        RmlUiIntentKind.OfflineChoice => 16,
        RmlUiIntentKind.OfflineRuleToggle => 12,
        RmlUiIntentKind.OfflineTrainingToggle => 5,
        _ => 1
    };
    int first = kind == RmlUiIntentKind.AdventureSelectSlot ? 1 : 0;
    for (int index = 0; index < count; index++)
    {
        int argument = kind is RmlUiIntentKind.OfflineArenaPage or RmlUiIntentKind.CommunityPage or RmlUiIntentKind.CommunityRevisionPage ? (index == 0 ? -1 : 1)
            : kind == RmlUiIntentKind.SettingsAction ? (index < 15 ? index : index + 1) : first + index;
        var intent = new RmlUiIntent(kind, argument, new(1, 1), 1);
        string action = RmlUiIntentRegistry.ToLegacy(intent);
        Require(RmlUiIntentRegistry.TryParseLegacy(action, intent.Document, intent.Sequence, out var parsed)
            && parsed == intent, $"Legacy registry roundtrip failed: {action}");
    }
    Require(!RmlUiIntentRegistry.IsValid(kind, (kind == RmlUiIntentKind.SettingsAction ? 256 : first + count)), $"Out-of-range argument accepted for {kind}");
}
foreach (string bad in new[] { "play:server:-1", "play:server:08", "play:server:8", "lobby:rules-toggle:16",
    "lobby:slot-team:-1", "lobby:slot-team:08", "lobby:slot-team:8",
    "lobby:map-select:6", "lobby:map-category:2", "route:unexpected", "quit:extra" })
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

string fullProfile = new string('x', 128 * 1024 - 8) + "λ😀ab";
host.SetField(initial, "full_profile", fullProfile);
Require(host.ReadField(initial, "full_profile", 128 * 1024) == fullProfile, "Maximum UTF-8 profile field was truncated");
try { host.ReadField(initial, "full_profile"); throw new Exception("Oversized default field was truncated silently"); }
catch (InvalidOperationException) { }
foreach (int invalid in new[] { 0, -1, 128 * 1024 + 1 })
{
    try { host.ReadField(initial, "full_profile", invalid); throw new Exception("Invalid field bound accepted"); }
    catch (ArgumentOutOfRangeException) { }
}
host.Input.SetFramebufferScale(1.5f, 2f);
host.Input.PointerButton(0, -10.5, 100.25, true);
Require(native.Pointer == (-16, 201), "Pointer was scaled twice or clamped inside the viewport");
host.Input.Text("λ😀");
Require(native.Codepoints.SequenceEqual(new uint[] { 955, 0x1f600 }), "Unicode text was split into UTF-16 surrogates");
host.ReleaseInput();
Require(native.FocusReleaseCount == 1, "Hidden UI did not release input focus");
host.SetClipboard("копия λ😀");
Require(host.ReadClipboard() == "копия λ😀", "Unicode clipboard roundtrip failed");
host.SetClipboard(fullProfile);
Require(host.ReadClipboard() == fullProfile, "Full profile clipboard was truncated");
host.SetClipboard(fullProfile + "x");
try { host.ReadClipboard(); throw new Exception("Oversized clipboard was truncated silently"); }
catch (InvalidOperationException) { }
Require(Marshal.SizeOf<RmlUiNativeTextInputState>() == 64, "Text input ABI structure size changed");
host.FocusDocument(initial, "name");
Require(host.TryGetTextInputState(out var textScope), "Text input context state was not captured");
host.Input.DiagnosticsEnabled = true;
var privateText = new RmlUiPlatformInputEvent(textScope.Document, RmlUiPlatformInputKind.TextCommitted,
    FocusEpoch: textScope.FocusEpoch, Text: "never expose this password");
Require(host.Input.Dispatch(privateText) == RmlUiInputResult.Accepted, "Typed commit did not reach its focused field");
Require(!privateText.ToString().Contains(privateText.Text) && !host.Input.Diagnostics.Display.Contains(privateText.Text),
    "Text/password content leaked into event diagnostics");
var composition = privateText with { Device = RmlUiInputDevice.InputMethod, Kind = RmlUiPlatformInputKind.CompositionBegin, Text = "" };
Require(host.Input.Dispatch(composition) == RmlUiInputResult.Accepted, "Composition begin was rejected");
Require(host.Input.Dispatch(composition with { Kind = RmlUiPlatformInputKind.CompositionUpdate, Text = "日本😀", Cursor = 2 })
    == RmlUiInputResult.Accepted, "Unicode preedit update was rejected");
Require(host.Input.Dispatch(privateText) == RmlUiInputResult.Invalid, "Committed character callback duplicated active preedit");
host.FocusDocument(initial, "apply");
host.FocusDocument(initial, "name");
Require(host.Input.Dispatch(composition with { Kind = RmlUiPlatformInputKind.CompositionCommit, Text = "日本" })
    == RmlUiInputResult.StaleFocus, "Delayed composition committed into a new field focus lifetime");
Require(host.TryGetTextInputState(out var newScope), "Replacement text input context state was not captured");
Require(host.Input.Dispatch(privateText with { FocusEpoch = newScope.FocusEpoch, Text = "\ud800" })
    == RmlUiInputResult.Invalid, "Invalid UTF-16 scalar was accepted");
host.ShowDocument(initial, false);
Require(host.Input.Dispatch(privateText with { FocusEpoch = newScope.FocusEpoch }) == RmlUiInputResult.HiddenDocument,
    "Hidden document accepted a delayed platform input event");
host.ShowDocument(initial, true);
var imeApi = new FakeImeWindowApi();
var ime = new RmlUiWindowsIme(host, (nint)123, () => true, imeApi);
Require(ime.Attached && imeApi.AttachCount == 1, "IME HWND subclass did not attach once");
Require(imeApi.Send(0x010d) == 0, "IME start was not consumed");
imeApi.Preedit = "A😀日本";
imeApi.CursorUtf16 = 3;
Require(imeApi.Send(0x010f, 0x0008) == 0 && native.CompositionCursor == 2,
    "IME UTF-16 cursor was not mapped to Unicode scalar offset");
imeApi.Result = "日本";
Require(imeApi.Send(0x010f, 0x0800) == 0 && imeApi.Send(0x0286) == 0 && imeApi.Send(0x0102) == 0,
    "IME result was duplicated through promoted character messages");
Require(imeApi.Send(0x010e) == 0, "IME end was not consumed after commit");
Require(imeApi.Send(0x010d) == 0, "Second IME sequence did not begin");
host.FocusDocument(initial, "apply");
host.FocusDocument(initial, "name");
int beforeDelayedFocusResult = native.CompositionCalls;
Require(imeApi.Send(0x010f, 0x0800) == 0 && native.CompositionCalls == beforeDelayedFocusResult
    && imeApi.Send(0x0102) == 0 && imeApi.Send(0x010e) == 0,
    "Delayed HWND composition result was redirected into a new field focus lifetime");
Require(imeApi.Send(0x010d) == 0, "IME sequence after focus retirement did not begin");
ime.Cancel();
int beforeDelayedCanceledResult = native.CompositionCalls;
Require(imeApi.Send(0x010f, 0x0800) == 0 && native.CompositionCalls == beforeDelayedCanceledResult
    && imeApi.Send(0x0102) == 0 && imeApi.Send(0x010e) == 0 && imeApi.Send(0x0102) == 99,
    "Canceled HWND sequence either committed a late result or retained normal character ownership");
int nativeCompositionCalls = native.CompositionCalls;
Task.Run(() => imeApi.Send(0x010d)).GetAwaiter().GetResult();
Require(native.CompositionCalls == nativeCompositionCalls && ime.LastFailure.Contains("owner thread"),
    "Off-thread window callback accessed the native text context");
Require(Task.Run(() =>
{
    try { ime.Dispose(); return false; }
    catch (InvalidOperationException) { return true; }
}).GetAwaiter().GetResult(), "IME hook detached from a different thread");
ime.Dispose(); ime.Dispose();
Require(!ime.Attached && imeApi.DetachCount == 1, "IME HWND subclass leaked or detached twice");
var destroyApi = new FakeImeWindowApi();
using var destroyedIme = new RmlUiWindowsIme(host, (nint)456, () => true, destroyApi);
destroyApi.Send(0x0082);
Require(!destroyedIme.Attached && destroyApi.DetachCount == 1, "Native HWND destruction retained the IME callback");
var failedDetachApi = new FakeImeWindowApi { DetachSucceeds = false };
var failedDetachIme = new RmlUiWindowsIme(host, (nint)789, () => true, failedDetachApi);
failedDetachIme.Dispose();
Require(failedDetachIme.Attached && failedDetachIme.LastFailure.Contains("rooted"),
    "Unconfirmed native subclass removal released its callback delegate");
failedDetachApi.Send(0x0082);
Require(!failedDetachIme.Attached, "Native HWND destruction did not release a retained failed-detach callback");

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
var cleanupBridge = new FakeBridge();
using (var cleanupHost = new RmlUiHost(cleanupBridge))
{
    Require(cleanupHost.Initialize(640, 360, 1, "."), "Cleanup fixture failed to initialize");
    var ownedDocument = cleanupHost.OpenDocument("page.rml", RmlUiDocumentLayer.Page);
    var lifetime = cleanupHost.DocumentCancellation(ownedDocument);
    cleanupBridge.ThrowOnShutdown = true;
    var cleanupOrder = new List<string>();
    RmlUiCleanup.Run(_ => throw new InvalidOperationException("Diagnostic sink failed"),
        () => { cleanupOrder.Add("presentation"); throw new InvalidOperationException("Foreign presenter failed"); },
        () => { cleanupOrder.Add("input"); throw new InvalidOperationException("Platform hook failed"); },
        () => { cleanupOrder.Add("pages"); throw new InvalidOperationException("Page disposer failed"); },
        () => { cleanupOrder.Add("gpu"); throw new InvalidOperationException("Graphics device unavailable"); },
        () => { cleanupOrder.Add("native"); cleanupHost.Shutdown(); },
        () => cleanupOrder.Add("flags"));
    Require(String.Join(",", cleanupOrder) == "presentation,input,pages,gpu,native,flags",
        "A failed disposer or diagnostic sink interrupted independent native teardown");
    Require(!cleanupHost.Active && !cleanupHost.IsAlive(ownedDocument) && lifetime.IsCancellationRequested,
        "Faulted presentation teardown retained native document lifetime");
    cleanupBridge.ThrowOnShutdown = false;
    Require(cleanupHost.Initialize(640, 360, 1, "."), "Faulted presentation teardown prevented reinitialization");
}
SchedulerCheck.Run();
Console.WriteLine("RmlUi runtime checks passed: typed ABI, owner thread, immutable revisioned snapshots, cancellation, document lifecycle, DPI, Unicode, clipboard, resilient teardown, and 100 reinitializations.");
if (args.Length != 0)
{
    if (args.Length != 3 || args[0] != "--native")
        throw new ArgumentException("Usage: rmlui-runtime-check [--native <bridge-library> <rmlui-assets-directory>]");
    ManagedHostCheck.Run(args[1], args[2]);
}

sealed class FakeBridge : IRmlUiNativeBridge
{
    public bool Legacy;
    public bool ThrowOnShutdown;
    public bool ScheduleSupported, ScheduleMissing, Dirty = true;
    public double NextUpdateDelay = double.PositiveInfinity;
    public int UpdateCount, RenderCount, StateQueryCount;
    public Func<RmlUiNativeUpdateState, RmlUiNativeUpdateState>? FilterUpdatePacket;
    private ulong _visualRevision = 1;
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
    private ulong _focusEpoch = 1, _focusedDocument;
    private string _focusedElement = "name";
    private bool _composing;
    public int CompositionCalls, CompositionCursor;
    public uint ProtocolVersion() => Legacy ? throw new EntryPointNotFoundException() : 1u;
    public ulong Generation() => _generation;
    public ulong HomeDocument() => _home;
    public int Initialize(int width, int height, float density, string root) => InitializeBackend(width, height, density, root, 0);
    public int InitializeBackend(int width, int height, float density, string root, int backend)
    {
        _generation++;
        _home = ++_nextDocument;
        Documents.Add(_home);
        Dirty = true;
        return 1;
    }
    public void Shutdown()
    {
        Documents.Clear(); Texts.Clear(); Bools.Clear(); _fields.Clear(); _intents.Clear();
        if (ThrowOnShutdown) throw new InvalidOperationException("Native device release failed");
    }
    public void Resize(int width, int height, float density) { Dirty = true; }
    public void Update() { UpdateCount++;Dirty = false;_visualRevision++; }
    public void Render(int width, int height) { RenderCount++; }
    public int UpdateState(ref RmlUiNativeUpdateState state)
    {
        StateQueryCount++;
        if (ScheduleMissing) throw new EntryPointNotFoundException();
        if (!ScheduleSupported || state.Size != 40 || state.Version != 1) return 0;
        state = new() { Size = 40, Version = 1, Generation = _generation, VisualRevision = _visualRevision,
            NextUpdateDelaySeconds = NextUpdateDelay, Flags = Dirty ? RmlUiUpdateFlags.Dirty : RmlUiUpdateFlags.None };
        if (FilterUpdatePacket != null) state = FilterUpdatePacket(state);
        return 1;
    }
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
    public int FocusDocument(ulong document, string element)
    {
        if (!Documents.Contains(document)) return 0;
        _focusedDocument = document; _focusedElement = element; _focusEpoch++; _composing = false;
        return 1;
    }
    public void SetText(string name, string value) => DocumentSetText(_home, name, value);
    public void SetBool(string name, int value) => DocumentSetBool(_home, name, value);
    public void SetField(string id, string value) => DocumentSetField(_home, id, value);
    public int ReadField(string id, byte[] buffer, int capacity) => DocumentReadField(_home, id, buffer, capacity);
    public int DocumentSetText(ulong document, string name, string value) { Dirty=true;Texts[(document, name)] = value; TextWrites++; return 1; }
    public int DocumentSetBool(ulong document, string name, int value) { Dirty=true;Bools[(document, name)] = value != 0; return 1; }
    public int DocumentSetField(ulong document, string id, string value) { Dirty=true;_fields[(document, id)] = value; return 1; }
    public int DocumentReadField(ulong document, string id, byte[] buffer, int capacity) =>
        Copy(_fields.GetValueOrDefault((document, id), ""), buffer, capacity);
    public void SetLobbyAnchor(int slot, float x, float y) { }
    public int Back() => 0;
    public int MouseMove(int x, int y, int modifiers) { Dirty=true;Pointer = (x, y); return 1; }
    public int MouseButton(int button, int down, int modifiers) => 1;
    public int MouseWheel(float delta, int modifiers) => 1;
    public int Key(int key, int down, int modifiers) => 1;
    public int Text(uint codepoint) { Codepoints.Add(codepoint); return 1; }
    public void FocusLost() { FocusReleaseCount++; _focusedElement = ""; _focusEpoch++; _composing = false; }
    public int TextInputActive() => _focusedElement == "name" ? 1 : 0;
    public int FocusedElement(byte[] buffer, int capacity) => Copy(_focusedElement, buffer, capacity);
    public int HoveredElement(byte[] buffer, int capacity) => Copy("submit", buffer, capacity);
    public int TextInputState(ref RmlUiNativeTextInputState state)
    {
        if (state.Size != 64 || state.Version != 1 || TextInputActive() == 0) return 0;
        state = new() { Size = 64, Version = 1, Generation = _generation,
            DocumentId = _focusedDocument == 0 ? _home : _focusedDocument, FocusEpoch = _focusEpoch,
            Width = 260, Height = 40, Composing = _composing ? 1 : 0,
            Capabilities = RmlUiTextInputCapabilities.Composition | RmlUiTextInputCapabilities.Bounds };
        return 1;
    }
    public int Composition(ulong generation, ulong document, ulong epoch, int stage, string text, int cursor, int selectionLength)
    {
        if (generation != _generation || !Documents.Contains(document) || epoch != _focusEpoch || TextInputActive() == 0) return 0;
        CompositionCalls++; CompositionCursor = cursor;
        if (stage == 0) _composing = true;
        else if (stage is 2 or 3) _composing = false;
        return 1;
    }
    public void SetClipboard(string text) => _clipboard = text;
    public int ReadClipboard(byte[] buffer, int capacity) => Copy(_clipboard, buffer, capacity);
    private static int Copy(string value, byte[] buffer, int capacity)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length >= capacity) return checked(-bytes.Length - 1);
        int length = bytes.Length;
        Array.Copy(bytes, buffer, length);
        buffer[length] = 0;
        return length;
    }
}

sealed class FakeImeWindowApi : IRmlUiImeWindowApi
{
    private RmlUiWindowSubclass? _callback;
    private nint _window;
    private nuint _id;
    public int AttachCount, DetachCount;
    public bool DetachSucceeds = true;
    public string Preedit = "", Result = "";
    public int CursorUtf16;
    public bool Attach(nint window, RmlUiWindowSubclass callback, nuint id)
    { _window = window; _callback = callback; _id = id; AttachCount++; return true; }
    public bool Detach(nint window, RmlUiWindowSubclass callback, nuint id)
    { if (DetachSucceeds) _callback = null; DetachCount++; return DetachSucceeds; }
    public nint Forward(nint window, uint message, nuint wParam, nint lParam) => 99;
    public string CompositionText(nint window, bool result) => result ? Result : Preedit;
    public int Cursor(nint window) => CursorUtf16;
    public void CancelComposition(nint window) { }
    public void PositionCandidate(nint window, RmlUiTextInputBounds bounds) { }
    public nint Send(uint message, nint flags = 0)
        => _callback?.Invoke(_window, message, 0, flags, _id, 0) ?? throw new InvalidOperationException("IME hook is detached");
}
