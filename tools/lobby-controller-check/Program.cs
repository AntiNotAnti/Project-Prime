using System.Collections.Immutable;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;

int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException(name);
    checks++;
}

// Each driver represents one rendering owner without a transport, renderer or game assets.
var backend = new FakeLobby();
using (var controller = new LobbySessionController(backend))
{
    Check(controller.PumpOnce(LobbyPumpOwner.Legacy, 10), "first owner tick pumps");
    Check(!controller.PumpOnce(LobbyPumpOwner.Legacy, 10), "duplicate token does not pump");
    Check(!controller.PumpOnce(LobbyPumpOwner.Legacy, 9), "stale token does not pump");
    Check(!controller.PumpOnce(LobbyPumpOwner.Native, 11), "detached owner does not pump");
    backend.OnPump = () => Check(!controller.PumpOnce(LobbyPumpOwner.Legacy, 12), "reentrant callback does not pump");
    Check(controller.PumpOnce(LobbyPumpOwner.Legacy, 11), "next token pumps");
    backend.OnPump = null;
    Check(backend.Pumps == 2, "exactly one network update per accepted tick");
    controller.TransferPumpOwnership(LobbyPumpOwner.Native);
    Check(!controller.PumpOnce(LobbyPumpOwner.Legacy, 12), "old presenter loses ownership");
    Check(controller.PumpOnce(LobbyPumpOwner.Native, 12), "native presenter inherits session");
    var a = controller.Snapshot();
    var b = controller.Snapshot();
    Check(ReferenceEquals(a, b) && a.Version == b.Version, "equal snapshots preserve version and identity");
    backend.State = backend.State with { Players = backend.State.Players.SetItem(0,
        backend.State.Players[0] with { Name = "Updated" }) };
    var c = controller.Snapshot();
    Check(c.Version == a.Version + 1 && a.Players[0].Name == "Owner", "snapshot values are immutable and structural");
    bool wrongThreadRejected = Task.Run(() =>
    {
        try { controller.Dispatch(a is not null ? new LobbyIntent(a.Lifetime, a.SessionRevision, LobbyIntentKind.ToggleReady) : default); }
        catch (InvalidOperationException) { return true; }
        return false;
    }).GetAwaiter().GetResult();
    Check(wrongThreadRejected, "workers cannot mutate the owner-thread lobby");
}
Check(backend.Stops == 1, "disposal stops session once");

backend = new FakeLobby { LoadMatch = true };
backend.State = backend.State with { Phase = SessionPhase.InMatch };
using (var controller = new LobbySessionController(backend))
{
    controller.TransferPumpOwnership(LobbyPumpOwner.Native);
    controller.PumpOnce(LobbyPumpOwner.Native, 1);
    int starts = 0;
    LaunchPlan plan = default;
    controller.MatchRequested += (_, request) => { starts++; plan = request; };
    controller.PumpOnce(LobbyPumpOwner.Native, 2);
    Check(starts == 1 && plan.Kind == LaunchKind.Online && plan.RoomKey == "test_arena", "late listener receives the deferred match handoff");
    controller.PumpOnce(LobbyPumpOwner.Native, 3);
    Check(starts == 1 && backend.Pumps == 3, "late join keeps pumping throughout scene prewarm");
    backend.State = backend.State with { Phase = SessionPhase.Lobby };
    backend.LoadMatch = false;
    controller.PumpOnce(LobbyPumpOwner.Native, 4);
    Check(!controller.IsSuspended && !controller.Snapshot().ShouldLoadMatch, "server abort before local scene rearms handoff");
    backend.State = backend.State with { Phase = SessionPhase.Starting, StartGeneration = 2 };
    backend.LoadMatch = true;
    controller.PumpOnce(LobbyPumpOwner.Native, 5);
    Check(starts == 2 && controller.Snapshot().StartGeneration == 2, "later server start emits exactly one new handoff");
    controller.YieldPumpToGameplay();
    Check(!controller.PumpOnce(LobbyPumpOwner.Native, 6) && backend.Pumps == 5, "local scene handoff yields exactly to gameplay");
    backend.State = backend.State with { Phase = SessionPhase.Lobby };
    backend.LoadMatch = false;
    controller.Resume();
    controller.PumpOnce(LobbyPumpOwner.Native, 7);
    backend.State = backend.State with { Phase = SessionPhase.Starting };
    backend.LoadMatch = true;
    controller.PumpOnce(LobbyPumpOwner.Native, 8);
    Check(starts == 3, "return to lobby resets one-shot for next match");
}

backend = new FakeLobby();
using (var controller = new LobbySessionController(backend))
{
    MatchDefinition draft = backend.State.Match!.Value with { PointGoal = 12 };
    var submit = controller.Intent(LobbyIntentKind.UpdateRules) with { Match = draft, DraftVersion = 8 };
    Check(controller.Dispatch(submit).Accepted, "rule draft sends authoritative command");
    Check(controller.Dispatch(controller.Intent(LobbyIntentKind.StartMatch)).Accepted, "start queues behind submitted draft");
    backend.State = backend.State with { CommandPending = false, Message = "" };
    controller.PumpOnce(LobbyPumpOwner.Legacy, 1);
    Check(backend.Commands.Count == 1 && controller.Snapshot().RulesPending, "command acknowledgement alone cannot confirm rules or start");
    backend.State = backend.State with { Message = "Server rejected the selected arena." };
    controller.PumpOnce(LobbyPumpOwner.Legacy, 2);
    Check(!controller.Snapshot().RulesPending && controller.Snapshot().RulesError.Contains("rejected")
        && backend.Commands.Count == 1, "server rejection preserves reason and cancels queued start");
    Check(controller.Dispatch(submit).Accepted, "rejected rule draft can be retried");
    controller.Dispatch(controller.Intent(LobbyIntentKind.StartMatch));
    uint confirmedVersion = 0;
    controller.RulesConfirmed += (version, _) => confirmedVersion = version;
    backend.State = backend.State with { Match = draft, CommandPending = false, Message = "", SessionRevision = 2 };
    controller.PumpOnce(LobbyPumpOwner.Legacy, 3);
    Check(confirmedVersion == 8 && backend.AcceptedRules == 1, "exact authoritative rules confirm draft version once");
    Check(backend.Commands.Count == 3 && backend.Commands[^1].Kind == LobbyIntentKind.StartMatch,
        "confirmed rules release one queued start");
    controller.PumpOnce(LobbyPumpOwner.Legacy, 4);
    Check(backend.AcceptedRules == 1 && backend.Commands.Count == 3, "confirmation and queued start are one-shot");
}

backend = new FakeLobby();
LobbyIntent oldIntent;
using (var first = new LobbySessionController(backend))
{
    oldIntent = first.Intent(LobbyIntentKind.UpdateRules) with { Match = backend.State.Match, DraftVersion = 42 };
    first.Dispatch(oldIntent);
}
backend = new FakeLobby();
using (var second = new LobbySessionController(backend))
{
    Check(!second.Snapshot().RulesPending && second.Snapshot().RulesError.Length == 0, "new lobby inherits no pending draft");
    Check(!second.Dispatch(oldIntent).Accepted && backend.Commands.Count == 0, "completion from prior lobby lifetime cannot publish");
    LobbyIntent stale = second.Intent(LobbyIntentKind.StartMatch);
    backend.State = backend.State with { SessionRevision = 2 };
    Check(!second.Dispatch(stale).Accepted, "stale authoritative revision cannot issue owner commands");
    Check(!second.Dispatch(second.Intent((LobbyIntentKind)999)).Accepted, "unknown lobby action cannot reach transport");
    backend.State = backend.State with { OwnerSlot = 1 };
    Check(!second.Dispatch(second.Intent(LobbyIntentKind.StartMatch)).Accepted, "guest cannot start match");
    backend.Refused = true;
    string closedReason = "";
    second.Closed += (_, reason) => closedReason = reason;
    second.PumpOnce(LobbyPumpOwner.Legacy, 1);
    Check(second.IsClosed && closedReason == "Server refused the connection." && backend.Stops == 1, "server refusal closes with reason");
    Check(!second.PumpOnce(LobbyPumpOwner.Legacy, 2), "closed lobby never pumps");
}

backend = new FakeLobby();
using (var controller = new LobbySessionController(backend))
{
    LobbyActionResult rejection = controller.Dispatch(controller.Intent((LobbyIntentKind)999));
    var failed = controller.Snapshot();
    Check(!rejection.Accepted && failed.CommandError == rejection.Message,
        "local command rejection persists in the presentation snapshot");
    Check(ReferenceEquals(failed, controller.Snapshot()), "snapshot refresh does not erase command rejection");
    Check(controller.Dispatch(controller.Intent(LobbyIntentKind.SendChat) with { Text = "hello" }).Accepted
        && controller.Snapshot().CommandError.Length == 0, "next accepted action clears local command rejection");
    MatchDefinition draft = backend.State.Match!.Value with { PointGoal = 12 };
    controller.Dispatch(controller.Intent(LobbyIntentKind.UpdateRules) with { Match = draft });
    controller.Dispatch(controller.Intent(LobbyIntentKind.StartMatch));
    backend.RejectStart = true;
    backend.State = backend.State with { Match = draft, CommandPending = false, Message = "" };
    controller.PumpOnce(LobbyPumpOwner.Legacy, 1);
    Check(controller.Snapshot().CommandError.Contains("busy") && !controller.Snapshot().RulesPending,
        "rejected deferred start remains visible after rules confirmation");
    controller.Retire();
    Check(controller.Snapshot().CommandError.Length == 0, "retirement clears command error");
}

backend = new FakeLobby();
using (var retired = new LobbySessionController(backend, new LobbyContext("Lobby A", "a:1")))
using (var current = new LobbySessionController(backend, new LobbyContext("Lobby B", "b:2")))
{
    retired.Dispatch(retired.Intent(LobbyIntentKind.UpdateRules) with { Match = backend.State.Match });
    retired.Retire();
    Check(retired.IsClosed && !retired.Snapshot().RulesPending && backend.Stops == 0,
        "retiring old presentation invalidates drafts without stopping a replaced connection");
    Check(current.PumpOnce(LobbyPumpOwner.Legacy, 1) && current.Snapshot().Context?.ServerName == "Lobby B",
        "new lobby keeps its context and pump after old controller retirement");
    retired.Dispose();
    Check(backend.Stops == 0, "retired presenter disposal cannot disconnect new lobby");
}

var lifetimes = new HashSet<Guid>();
for (int cycle = 0; cycle < 50; cycle++)
{
    var cycleBackend = new FakeLobby();
    using var controller = new LobbySessionController(cycleBackend);
    var snapshot = controller.Snapshot();
    if (!lifetimes.Add(snapshot.Lifetime)) throw new InvalidOperationException("lobby lifetimes must be unique");
    controller.PumpOnce(LobbyPumpOwner.Legacy, 1);
    controller.Leave();
    controller.Leave();
    if (cycleBackend.Stops != 1 || controller.PumpOnce(LobbyPumpOwner.Legacy, 2))
        throw new InvalidOperationException("repeated lobby create/leave must stop once and release pumping");
}
Check(lifetimes.Count == 50, "50 create/leave cycles retain no lifetime or pump state");

Console.WriteLine($"Lobby controller checks passed ({checks} contracts).");

sealed class FakeLobby : ILobbySessionBackend
{
    public LobbySnapshot State = new()
    {
        Active = true, Persistent = true, Phase = SessionPhase.Lobby, SessionRevision = 1,
        MaxPlayers = 8, OwnerSlot = 0, LocalSlot = 0, LocalHunter = Hunter.Samus,
        PlayerName = "Owner", RequiredMapReady = true,
        Match = new MatchDefinition { RoomKey = "test_arena", Mode = GameMode.Battle, PointGoal = 7 },
        Players = ImmutableArray.Create(new LobbyPlayerSnapshot(0, "Owner", Hunter.Samus, 0, -1,
            false, 0, false, false, 1, 0, MapAvailabilityState.Ready))
    };
    public int Pumps, Stops, AcceptedRules;
    public bool LoadMatch;
    public bool RejectStart;
    public Action? OnPump;
    public List<LobbyIntent> Commands { get; } = new();
    public double Clock { get; set; }
    public bool ShouldLoadMatch => LoadMatch;
    public bool Refused { get; set; }
    public bool TimedOut { get; set; }
    public string RefusedMessage => "Server refused the connection.";
    public LobbySnapshot Capture() => State with
    {
        Players = State.Players.ToArray().ToImmutableArray(),
        Chat = State.Chat.ToArray().ToImmutableArray()
    };
    public void Pump() { Pumps++; OnPump?.Invoke(); }
    public void Stop() { Stops++; State = State with { Active = false }; }
    public bool SendCommand(LobbyIntent intent)
    {
        Commands.Add(intent);
        if (RejectStart && intent.Kind == LobbyIntentKind.StartMatch) return false;
        State = State with { CommandPending = true, Message = "Waiting for server..." };
        return true;
    }
    public void Identify(Hunter hunter, byte color) => State = State with { LocalHunter = hunter, LocalColor = color };
    public void SetSpectator(bool spectator) => State = State with { PreferSpectator = spectator };
    public void SendChat(string text) => State = State with { Chat = State.Chat.Add(text) };
    public void RetryMap() { }
    public LobbyActionResult ValidateRules(MatchDefinition match) => LobbyActionResult.Ok;
    public void RulesAccepted(MatchDefinition match) => AcceptedRules++;
}
