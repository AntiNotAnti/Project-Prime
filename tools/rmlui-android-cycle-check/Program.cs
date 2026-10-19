using MphRead.Droid;

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
void WrongOwner(Action action)
{
    Check(Task.Run(() => { try { action(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult(), "owner operation rejects UI worker");
}
var cycle = new RmlUiAndroidLocalCycle<Home>();
Guid view = Guid.NewGuid(), context = Guid.NewGuid();
var previous = new List<RmlUiAndroidLocalWitness>();
int scenes = 0, peakScenes = 0, retired = 0, nativeHomes = 1;
for (int iteration = 0; iteration < 128; iteration++)
{
    Check(cycle.Current.Phase == RmlUiAndroidLocalPhase.Home, "Home is an explicit phase");
    Check(cycle.Current.Cycle == iteration + 1, "fresh Home generation");
    Check(!cycle.TryTakeHome(out _), "fresh Home cannot replay prior ack");
    Check(cycle.TryBeginMatch(out var loading), "one copied plan begins match");
    Check(!cycle.TryBeginMatch(out _), "loading rejects duplicate plan");
    scenes++; peakScenes = Math.Max(peakScenes, scenes);
    Check(scenes == 1, "only one fake scene is live");
    Check(!cycle.TryRequestReturn(loading), "loading cannot be retired before ready");
    Check(cycle.MarkMatchReady(loading) && !cycle.MarkMatchReady(loading), "ready acknowledgement is exact and one-shot");
    var match = cycle.Current;
    foreach (var old in previous)
    {
        Check(!cycle.TryRequestReturn(old), "old match callback cannot return later match");
        Check(!cycle.TryAcknowledgeHome(old, new(view, context, -1)), "old UI ack cannot alter later match");
    }
    Check(Task.Run(() => cycle.TryRequestReturn(match)).GetAwaiter().GetResult(), "UI can submit copied exact match return");
    Check(!cycle.TryRequestReturn(match), "double Leave cannot retire twice");
    var events = new List<string>();
    var owner = new Retirement(events) { OnCleanup = () => { scenes--; retired++; } };
    Check(cycle.TryRetire(() => RmlUiAndroidLocalRetirement.Run(owner, _ => throw new InvalidOperationException("unexpected failure")), out var returned), "EGL completes real retirement pipeline");
    Check(events.SequenceEqual(new[] { "input", "native", "capture", "cleanup", "unload", "commit", "foreground", "reset" }), "exact native/scene/commit/foreground release order");
    Check(scenes == 0 && owner.Held == 0 && owner.Pixels == 0, "retirement clears fake held input and result pixels");
    Check(!cycle.TryBeginMatch(out _) && !cycle.TryTakeHome(out _), "no implicit build while UI reset is pending");
    Check(!cycle.TryRetire(() => throw new InvalidOperationException("must not execute twice"), out _), "retirement pipeline runs once");
    Check(!cycle.TryAcknowledgeHome(returned with { Cycle = returned.Cycle + 1 }, new(view, context, 0)), "copied but non-identical return ack is rejected");
    // A surface/background interval supplies no ack. No host worker is added.
    Check(cycle.Current.Phase == RmlUiAndroidLocalPhase.AwaitingHomeAck && scenes == 0 && events.Count == 8,
        "background/surface wait retains no fake scene and repeats no retirement actions");
    var home = new Home(view, context, iteration + 1);
    Check(Task.Run(() => cycle.TryAcknowledgeHome(returned, home)).GetAwaiter().GetResult(), "UI reset publishes detached Home options");
    Check(!cycle.TryAcknowledgeHome(returned, home), "UI reset cannot acknowledge twice");
    Check(cycle.TryTakeHome(out var copied) && ReferenceEquals(copied, home) && copied.View == view && copied.Context == context,
        "next Home uses the same fake View/context identity");
    nativeHomes++;
    Check(!cycle.TryTakeHome(out _), "new Home options consumed once");
    previous.Add(match); previous.Add(returned);
}
Check(peakScenes == 1 && scenes == 0 && retired == 128 && nativeHomes == 129,
    "many cycles retain one fake owner and retire every scene");
WrongOwner(() => cycle.TryBeginMatch(out _));
WrongOwner(() => cycle.TryTakeHome(out _));
WrongOwner(() => cycle.MarkMatchReady(cycle.Current));
WrongOwner(() => cycle.TryRetire(() => true, out _));

foreach (string fail in new[] { "input", "native", "capture", "cleanup", "unload", "commit", "foreground", "reset" })
{
    var failed = Ready(); var match = failed.Current; var events = new List<string>(); int errors = 0;
    Check(failed.TryRequestReturn(match), "failure case requested: " + fail);
    Check(!failed.TryRetire(() => RmlUiAndroidLocalRetirement.Run(new Retirement(events) { Fail = fail }, _ => errors++), out var fault), "failed stage never permits new Home: " + fail);
    Check(errors == 1 && fault.Phase == RmlUiAndroidLocalPhase.Faulted, "failure is observed and terminal: " + fail);
    Check(events.Contains("cleanup") && events.Contains("unload") && events.Last() == "reset", "failure still attempts scene and presentation cleanup: " + fail);
    Check(events.Contains("commit") == (fail != "capture"), "commit requires captured payload but survives later cleanup failure: " + fail);
    Check(events.Contains("foreground") == (fail is not ("capture" or "cleanup" or "unload" or "commit")), "foreground release requires cleanup/visual/commit success: " + fail);
    Check(!failed.TryAcknowledgeHome(fault, new(view, context, 1)) && !failed.TryTakeHome(out _) && !failed.TryBeginMatch(out _), "failure cannot be revived by UI or another plan");
}
foreach (bool nativeFailure in new[] { true, false })
{
    var failed = Ready(); failed.TryRequestReturn(failed.Current); int errors = 0;
    Check(!failed.TryRetire(() => RmlUiAndroidLocalRetirement.Run(new Retirement(new()) { NativeSuccess = !nativeFailure, ForegroundSuccess = nativeFailure }, _ => errors++), out _)
        && errors == 1, "false retirement/foreground result prevents Home");
}
var aggregate = new Retirement(new()) { FailAll = true }; int aggregateErrors = 0;
Check(!RmlUiAndroidLocalRetirement.Run(aggregate, _ => aggregateErrors++) && aggregateErrors == 6,
    "independent failures aggregate across input/native/capture/cleanup/unload/reset");
Check(aggregate.Events.SequenceEqual(new[] { "input", "native", "capture", "cleanup", "unload", "reset" }), "failed capture never invents a new commit");

foreach (var phase in Enum.GetValues<RmlUiAndroidLocalPhase>())
{
    if (phase == RmlUiAndroidLocalPhase.Retiring) continue; // Covered inside the actual callback below.
    var stopped = At(phase);
    var old = stopped.Current;
    stopped.Stop();
    Check(stopped.Current.Phase == RmlUiAndroidLocalPhase.Stopped, "Stop wins from phase " + phase);
    Check(!stopped.TryAcknowledgeHome(old, new(view, context, 1)) && !stopped.TryBeginMatch(out _)
        && !stopped.TryRequestReturn(old) && !stopped.TryTakeHome(out _), "Stop rejects all late owner/UI work: " + phase);
    stopped.Fault(); Check(stopped.Current.Phase == RmlUiAndroidLocalPhase.Stopped, "fault cannot revive stopped owner");
}
var stopDuring = Ready(); stopDuring.TryRequestReturn(stopDuring.Current);
Check(!stopDuring.TryRetire(() =>
    {
        Check(stopDuring.Current.Phase == RmlUiAndroidLocalPhase.Retiring, "retirement callback owns actual Retiring phase");
        Check(!stopDuring.TryAcknowledgeHome(stopDuring.Current, new(view, context, 0)), "retiring cannot accept UI Home ack");
        stopDuring.Stop(); return true;
    }, out var stoppedWitness)
    && stoppedWitness.Phase == RmlUiAndroidLocalPhase.Stopped, "Stop during retirement cannot publish Home");
var throwing = Ready(); throwing.TryRequestReturn(throwing.Current);
try { throwing.TryRetire(() => throw new InvalidOperationException("injected"), out _); Check(false, "throw expected"); }
catch (InvalidOperationException) { Check(throwing.Current.Phase == RmlUiAndroidLocalPhase.Faulted, "uncaught retirement error is terminal"); }
var foreign = Ready();
Check(!foreign.TryRequestReturn(previous[0]), "another View lifetime cannot leave this match");
Check(!foreign.TryAcknowledgeHome(previous[1], new(view, context, 1)), "another View lifetime cannot acknowledge Home");
// Execute the actual cleanup binding algorithm against a fake EGL boundary.
foreach (string scenario in new[] { "current", "surfaceless", "pbuffer", "foreign", "surface-fails", "create-fails", "wrong-identity", "unbind-throws", "destroy-throws" })
{
    var api = new CleanupContext
    {
        Matches = scenario == "current", Foreign = scenario == "foreign",
        Surfaceless = scenario != "pbuffer" && scenario != "surface-fails" && scenario != "create-fails",
        Create = scenario != "create-fails", Bind = scenario != "surface-fails",
        Identity = scenario != "wrong-identity",
        Throw = scenario == "unbind-throws" ? "unbind" : scenario == "destroy-throws" ? "destroy" : ""
    };
    int errors = 0, retires = 0; bool allowedGl = false;
    bool success = RmlUiAndroidCleanupContext.Run(api, valid => { retires++; allowedGl = valid; api.Events.Add("retire"); return valid; }, _ => errors++);
    bool expected = scenario is "current" or "surfaceless" or "pbuffer";
    Check(success == expected && retires == 1, "cleanup binding executes exactly one retirement even on failure: " + scenario);
    Check(allowedGl == (expected || scenario is "unbind-throws" or "destroy-throws"), "Unload permission requires exact context identity: " + scenario);
    if (scenario == "current")
        Check(api.Events.SequenceEqual(new[] { "retire" }), "already current window context is never rebound or unbound");
    else if (scenario == "foreign")
        Check(api.Events.SequenceEqual(new[] { "retire" }) && errors == 1, "foreign context is never rebound or unbound");
    else
        Check(api.Events.TakeLast(2).SequenceEqual(new[] { "unbind", "destroy" }), "temporary surface destruction is always attempted after unbind: " + scenario);
    Check(api.Events.Contains("create") == (scenario is "pbuffer" or "surface-fails" or "create-fails"), "pbuffer is bounded fallback only after surfaceless failure");
    Check(errors == (expected ? 0 : 1), "binding/identity/release failure blocks fresh Home: " + scenario);
}
foreach (string throwingStage in new[] { "surfaceless", "create", "bind" })
{
    var api = new CleanupContext { Surfaceless = throwingStage == "surfaceless", Throw = throwingStage };
    int errors = 0, retire = 0;
    Check(!RmlUiAndroidCleanupContext.Run(api, valid => { Check(!valid, "throwing binding cannot authorize GL"); retire++; return false; }, _ => errors++)
        && retire == 1 && errors == 1, "throwing binding still executes non-GL retirement once: " + throwingStage);
    Check(api.Events.TakeLast(2).SequenceEqual(new[] { "unbind", "destroy" }), "throwing partial create/bind still destroys temporary surface");
}
var throwingRetire = new CleanupContext(); int retirementErrors = 0;
Check(!RmlUiAndroidCleanupContext.Run(throwingRetire, _ => throw new InvalidOperationException("retire"), _ => retirementErrors++)
    && retirementErrors == 1 && throwingRetire.Events.TakeLast(2).SequenceEqual(new[] { "unbind", "destroy" }), "throwing scene retirement still releases temporary binding/surface");
Console.WriteLine($"RMLUI ANDROID CYCLE PASS {checks} checks; actual phase/retirement source, fake scene/native/UI boundaries; no Android/EGL/device acceptance.");

static RmlUiAndroidLocalCycle<Home> Ready()
{
    var result = new RmlUiAndroidLocalCycle<Home>();
    if (!result.TryBeginMatch(out var loading) || !result.MarkMatchReady(loading)) throw new InvalidOperationException("fixture setup");
    return result;
}
static RmlUiAndroidLocalCycle<Home> At(RmlUiAndroidLocalPhase phase)
{
    var result = new RmlUiAndroidLocalCycle<Home>();
    if (phase == RmlUiAndroidLocalPhase.Home) return result;
    result.TryBeginMatch(out var loading);
    if (phase == RmlUiAndroidLocalPhase.Loading) return result;
    result.MarkMatchReady(loading);
    if (phase == RmlUiAndroidLocalPhase.Match) return result;
    result.TryRequestReturn(result.Current);
    if (phase == RmlUiAndroidLocalPhase.ReturnRequested || phase == RmlUiAndroidLocalPhase.Retiring) return result;
    result.TryRetire(() => true, out var returned);
    if (phase == RmlUiAndroidLocalPhase.AwaitingHomeAck) return result;
    result.TryAcknowledgeHome(returned, new(Guid.NewGuid(), Guid.NewGuid(), 1));
    if (phase == RmlUiAndroidLocalPhase.HomeAccepted) return result;
    if (phase == RmlUiAndroidLocalPhase.Faulted) result.Fault(); else result.Stop();
    return result;
}
sealed record Home(Guid View, Guid Context, int RulesRevision);
sealed class Retirement(List<string> events) : IRmlUiAndroidLocalRetirement
{
    internal List<string> Events => events;
    internal string Fail = ""; internal bool FailAll, NativeSuccess = true, ForegroundSuccess = true;
    internal int Held = 3, Pixels = 1; internal Action? OnCleanup;
    void Stage(string name) { events.Add(name); if (FailAll || Fail == name) throw new InvalidOperationException(name); }
    public void ReleaseInput() { Stage("input"); Held = 0; }
    public bool RetireNative() { Stage("native"); return NativeSuccess; }
    public void CaptureCommit() => Stage("capture");
    public void CleanupScene() { Stage("cleanup"); OnCleanup?.Invoke(); }
    public void UnloadScene() => Stage("unload");
    public void Commit() => Stage("commit");
    public bool ReleaseForeground() { Stage("foreground"); return ForegroundSuccess; }
    public void ResetPresentation() { Stage("reset"); Held = Pixels = 0; }
}

sealed class CleanupContext : IRmlUiAndroidCleanupContext
{
    internal readonly List<string> Events = new();
    internal bool Matches, Foreign, Surfaceless = true, Create = true, Bind = true, Identity = true;
    internal string Throw = "";
    void Stage(string stage) { Events.Add(stage); if (Throw == stage) throw new InvalidOperationException(stage); }
    public bool CurrentMatchesOwner => Matches;
    public bool HasForeignCurrentContext => Foreign;
    public bool TryBindSurfaceless() { Stage("surfaceless"); if (Surfaceless) Matches = Identity; return Surfaceless; }
    public bool TryCreatePbuffer() { Stage("create"); return Create; }
    public bool TryBindPbuffer() { Stage("bind"); if (Bind) Matches = Identity; return Bind; }
    public void UnbindTemporary() { Stage("unbind"); Matches = false; }
    public void DestroyPbuffer() => Stage("destroy");
}
