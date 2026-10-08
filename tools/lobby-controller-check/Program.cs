using System.Collections.Immutable;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Cosmetics;

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

backend = new FakeLobby();
backend.State = backend.State with { Players = backend.State.Players.Add(new(1, "Guest", Hunter.Kanden,
    1, -1, true, 21, false, false, 1, 0, MapAvailabilityState.Ready, 7)) };
using (var controller = new LobbySessionController(backend))
{
    LobbyIntent selected = controller.Intent(LobbyIntentKind.KickPlayer, 1);
    Check(selected.TargetGeneration == 7 && selected.ExpectedRosterRevision == 1,
        "selected player command retains slot generation and roster witness");
    backend.State = backend.State with { Players = backend.State.Players.SetItem(1, backend.State.Players[1] with { Generation = 8 }) };
    Check(!controller.Dispatch(selected).Accepted && backend.Commands.Count == 0,
        "slot reused within the same session revision cannot receive a destructive command");
    selected = controller.Intent(LobbyIntentKind.TransferOwner, 1);
    backend.State = backend.State with { RosterRevision = 2 };
    Check(!controller.Dispatch(selected).Accepted, "roster update invalidates a retained admin confirmation");
    Check(controller.Dispatch(controller.Intent(LobbyIntentKind.TransferOwner, 1)).Accepted,
        "owner can transfer to a current human participant");
    backend.State = backend.State with { CommandPending = false, OwnerSlot = 1, Message = "" };
    Check(!controller.Dispatch(controller.Intent(LobbyIntentKind.KickPlayer, 1)).Accepted,
        "ownership loss invalidates admin permissions immediately");
}

backend = new FakeLobby();
backend.State = backend.State with { Match = backend.State.Match!.Value with { Mode = GameMode.BattleTeams, Format = MatchFormat.OneVsOne },
    RuleFlags = LobbyRuleFlags.RequireReady,
    Players = ImmutableArray.Create(backend.State.Players[0] with { Team = 0, Ready = true },
        new LobbyPlayerSnapshot(1, "Bot", Hunter.Kanden, 0, 1, false, 0, true, false, 2, 0, MapAvailabilityState.Ready, 2),
        new LobbyPlayerSnapshot(2, "Observer", Hunter.Trace, 0, 0, false, 10, false, true, 1, 0, MapAvailabilityState.Ready, 3)) };
using (var controller = new LobbySessionController(backend))
{
    var view = LobbyPresentation.From(controller.Snapshot());
    Check(view.CombatantCount == 2 && view.SpectatorCount == 1 && view.BotCount == 1
        && view.Teams[0].Occupants == 1, "spectators do not occupy team seats or combatant counts");
    Check(view.CanStart && view.StartReason == "Ready to start.", "bots and spectators do not require ready for start");
    Check(!view.CanAssignTeam(0, 1) && view.CanAssignTeam(0, 0) && view.CanAssignTeam(0, -1),
        "team capabilities include capacity, current seat and Auto");
    Check(!controller.Dispatch(controller.Intent(LobbyIntentKind.SetTeam, 0) with { Team = 1 }).Accepted,
        "team command rejects an occupied team");
    Check(!controller.Dispatch(controller.Intent(LobbyIntentKind.SetTeam, 2) with { Team = 1 }).Accepted,
        "spectator cannot be assigned a combat team");
    Check(!controller.Dispatch(controller.Intent(LobbyIntentKind.TransferOwner, 1)).Accepted,
        "bot cannot become lobby owner");
    Check(view.Players[1].CanConfigureBot && view.Players[1].CanRemoveBot && !view.Players[2].CanChangeTeam,
        "row capabilities preserve bot and spectator roles");
    Check(!controller.Dispatch(controller.Intent(LobbyIntentKind.UpdateBot, 1) with { BotLevel = 4 }).Accepted,
        "bot configuration validates existing difficulty limits");
    Check(!controller.Dispatch(controller.Intent(LobbyIntentKind.UpdateBot, 1) with { DamageReduction = 1 }).Accepted,
        "bot configuration validates handicap steps");
    Check(controller.Dispatch(controller.Intent(LobbyIntentKind.UpdateBot, 1) with { Hunter = Hunter.Weavel, Team = 1, BotLevel = 3 }).Accepted,
        "owner can configure a current bot in its existing team seat");
    backend.State = backend.State with { CommandPending = false, OwnerSlot = 1, RuleFlags = LobbyRuleFlags.LockTeams };
    Check(!LobbyPresentation.From(controller.Snapshot()).CanAssignTeam(0, -1)
        && !controller.Dispatch(controller.Intent(LobbyIntentKind.SetTeam, 0) with { Team = -1 }).Accepted,
        "guest respects locked teams in model and command validation");
}

backend = new FakeLobby();
using (var controller = new LobbySessionController(backend))
{
    Check(controller.Dispatch(controller.Intent(LobbyIntentKind.Identify) with { Hunter = Hunter.Kanden, Color = 2 }).Accepted
        && controller.Snapshot().IdentityPending && controller.Snapshot().AcknowledgedLocalHunter == Hunter.Samus,
        "Hunter request remains distinct from server acknowledged identity");
    backend.State = backend.State with { LocalHunter = Hunter.Samus, LocalColor = 0 };
    backend.Clock = 1.1;
    controller.PumpOnce(LobbyPumpOwner.Legacy, 1);
    Check(backend.Identifies == 2 && controller.Snapshot().LocalHunter == Hunter.Kanden,
        "older roster identity does not lose requested Hunter and retries through service");
    backend.State = backend.State with { Players = backend.State.Players.SetItem(0,
        backend.State.Players[0] with { Hunter = Hunter.Kanden, Color = 2 }) };
    controller.PumpOnce(LobbyPumpOwner.Legacy, 2);
    Check(!controller.Snapshot().IdentityPending && controller.Snapshot().AcknowledgedLocalColor == 2,
        "exact Hunter and suit roster echo confirms selection");
    controller.Dispatch(controller.Intent(LobbyIntentKind.Identify) with { Hunter = Hunter.Trace, Color = 0 });
    backend.State = backend.State with { Match = backend.State.Match!.Value with { LowTier = true } };
    controller.PumpOnce(LobbyPumpOwner.Legacy, 3);
    Check(!controller.Snapshot().IdentityPending && controller.Snapshot().IdentityMessage.Contains("did not confirm"),
        "rule change cancels now disallowed Hunter with explicit feedback");
    controller.Dispatch(controller.Intent(LobbyIntentKind.ToggleSpectator));
    Check(controller.Snapshot().SpectatorPending && controller.Snapshot().PreferSpectator
        && controller.Snapshot().AcknowledgedSpectator == false, "spectator request waits for authoritative role echo");
    Check(!controller.Dispatch(controller.Intent(LobbyIntentKind.ToggleSpectator)).Accepted
        && !controller.Dispatch(controller.Intent(LobbyIntentKind.ToggleReady)).Accepted,
        "pending spectator role blocks duplicate role and ready commands");
    backend.State = backend.State with { Players = backend.State.Players.SetItem(0, backend.State.Players[0] with { IsSpectator = true }) };
    controller.PumpOnce(LobbyPumpOwner.Legacy, 4);
    Check(!controller.Snapshot().SpectatorPending && controller.Snapshot().AcknowledgedSpectator == true,
        "authoritative spectator role confirms request");
    controller.Dispatch(controller.Intent(LobbyIntentKind.ToggleSpectator));
    backend.Clock = 10;
    controller.PumpOnce(LobbyPumpOwner.Legacy, 5);
    Check(!controller.Snapshot().SpectatorPending && controller.Snapshot().SpectatorMessage.Contains("did not confirm"),
        "spectator timeout releases pending state with reason");
    Check(!controller.Dispatch(controller.Intent(LobbyIntentKind.SendChat) with { Text = new string('x', 97) }).Accepted,
        "chat rejects text exceeding existing protocol budget");
    Check(!controller.Dispatch(controller.Intent(LobbyIntentKind.SendChat) with { Text = "hello 🌌" }).Accepted
        && controller.Snapshot().CommandError.Contains("ASCII"), "unsupported chat receives explicit feedback");
    Check(controller.Dispatch(controller.Intent(LobbyIntentKind.SendChat) with { Text = new string('x', 96) }).Accepted,
        "chat accepts full existing protocol budget");
    backend.State = backend.State with { MaxPlayers = 1 };
    Check(!LobbyPresentation.From(controller.Snapshot()).CanAddBot
        && !controller.Dispatch(controller.Intent(LobbyIntentKind.AddBot)).Accepted,
        "full lobby disables and rejects bot addition");
}

var cosmetics = new FakeHunters();
using (var selection = new HunterSelectionController(backend: cosmetics))
{
    var initial = selection.Snapshot;
    selection.Pump();
    Check(ReferenceEquals(initial, selection.Snapshot) && initial.Skins.Length == 7
        && initial.ArmorEffects.Length == 21 && initial.DeathPresentations.Length == 8,
        "Hunter selector exposes complete actual cosmetic catalog with stable immutable snapshot");
    Check(!selection.SelectSkin("skin.trace.obsidian").Accepted && selection.Snapshot.CommandError.Length > 0,
        "Hunter-specific skin from another Hunter receives explicit rejection");
    selection.Pump();
    Check(selection.Snapshot.CommandError.Length > 0, "Hunter error remains visible across refreshes");
    cosmetics.Blocked = "armor.lightning";
    Check(!selection.SelectArmor("armor.lightning").Accepted
        && !selection.Snapshot.ArmorEffects.Single(value => value.Key == cosmetics.Blocked).Unlocked,
        "backend ownership gate is reflected in choices and command validation");
    cosmetics.Blocked = "";
    Check(selection.SelectSkin("skin.samus.obsidian").Accepted && selection.SelectArmor("armor.lightning").Accepted,
        "valid cosmetic draft changes clear errors");
    CosmeticLoadout samusDraft = selection.Snapshot.Draft;
    selection.SelectHunter(Hunter.Weavel);
    selection.SetPreviewMode(SkinContext.Halfturret);
    selection.SelectHunter(Hunter.Samus);
    Check(selection.Snapshot.Draft == samusDraft && selection.Snapshot.PreviewMode == SkinContext.Biped,
        "per-Hunter draft survives selection changes and incompatible preview resets");
    Check(!selection.SetPreviewMode(SkinContext.Halfturret).Accepted,
        "half-turret preview is restricted to Weavel");
    selection.Rotate(100); selection.Zoom(10); selection.SetLoopDeath(true);
    int requested = selection.Snapshot.DeathRequest;
    selection.PreviewDeath(); selection.CompareNative(true);
    Check(selection.Snapshot.PreviewLoadout == CosmeticLoadout.Default && selection.Snapshot.Draft == samusDraft
        && selection.Snapshot.DeathRequest == requested + 1 && selection.Snapshot.Zoom == 1,
        "native comparison and preview reset preserve draft and death trigger");
    selection.CompareNative(false);
    Check(selection.Equip().Accepted && selection.Snapshot.Saving && selection.Snapshot.Equipped == samusDraft
        && selection.Snapshot.PendingSync, "equip persists local loadout before asynchronous account result");
    Check(!selection.Equip().Accepted, "pending cosmetic save cannot submit duplicate request");
    cosmetics.Complete(false);
    selection.Pump();
    Check(!selection.Snapshot.Saving && selection.Snapshot.Equipped == samusDraft
        && selection.Snapshot.CommandError.Contains("NOT SYNCED"), "failed remote save retains local equip and visible status");
    selection.SelectSuit(3); selection.ApplyIdentity();
    Check(cosmetics.SavedHunter == Hunter.Samus && cosmetics.SavedColor == 3,
        "pre-lobby identity persists only through explicit apply");
    Check(selection.Equip().Accepted, "unsynced equip can be retried");
    cosmetics.Complete(true); selection.Pump();
    Check(!selection.Snapshot.PendingSync && !selection.Snapshot.Saving, "exact account acknowledgement clears sync pending");
    bool wrongThread = Task.Run(() => { try { selection.SelectSuit(0); } catch (InvalidOperationException) { return true; } return false; }).GetAwaiter().GetResult();
    Check(wrongThread, "worker cannot mutate Hunter selector");
}

backend = new FakeLobby();
using (var lobby = new LobbySessionController(backend))
using (var selection = new HunterSelectionController(lobby, new FakeHunters()))
{
    selection.SelectHunter(Hunter.Trace);
    backend.State = backend.State with { Match = backend.State.Match!.Value with { LowTier = true } };
    selection.Pump();
    Check(selection.Snapshot.Hunter == Hunter.Trace && selection.Snapshot.AllowedHunters.Length == 4
        && !selection.Snapshot.CanApplyIdentity && !selection.ApplyIdentity().Accepted,
        "live rule refresh preserves draft selection while disallowed identity receives explicit feedback");
    selection.SelectHunter(Hunter.Kanden); selection.SelectSuit(1);
    Check(selection.ApplyIdentity().Accepted && lobby.Snapshot().IdentityPending,
        "live Hunter apply uses shared lobby authority");
    selection.Pump();
    Check(selection.Snapshot.IdentityPending && selection.Snapshot.AcknowledgedHunter == Hunter.Samus,
        "native Hunter state presents pending and acknowledged identity independently");
    backend.State = backend.State with { Players = backend.State.Players.SetItem(0, backend.State.Players[0] with { Hunter = Hunter.Kanden, Color = 1 }) };
    lobby.PumpOnce(LobbyPumpOwner.Legacy, 1); selection.Pump();
    Check(!selection.Snapshot.IdentityPending && selection.Snapshot.Status.Contains("confirmed"),
        "native Hunter status reports server confirmation");
}

cosmetics = new FakeHunters { PreferredHunter = (Hunter)250 };
using (var selection = new HunterSelectionController(backend: cosmetics))
{
    Check(selection.Snapshot.CommandError.Contains("saved Hunter"), "invalid saved Hunter is explained rather than silently replaced");
    selection.Equip();
    selection.Dispose();
    cosmetics.Complete(true);
    selection.Pump();
    Check(!selection.Snapshot.CanEquip && !selection.Snapshot.CanApplyIdentity,
        "disposed Hunter selector ignores late save completion and releases actions");
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
        Active = true, Persistent = true, Phase = SessionPhase.Lobby, SessionRevision = 1, RosterRevision = 1,
        MaxPlayers = 8, OwnerSlot = 0, LocalSlot = 0, LocalHunter = Hunter.Samus,
        PlayerName = "Owner", RequiredMapReady = true,
        Match = new MatchDefinition { RoomKey = "test_arena", Mode = GameMode.Battle, PointGoal = 7 },
        Players = ImmutableArray.Create(new LobbyPlayerSnapshot(0, "Owner", Hunter.Samus, 0, -1,
            false, 0, false, false, 1, 0, MapAvailabilityState.Ready, 1))
    };
    public int Pumps, Stops, AcceptedRules, Identifies, SpectatorRequests;
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
    public void Identify(Hunter hunter, byte color) { Identifies++; State = State with { LocalHunter = hunter, LocalColor = color }; }
    public void SetSpectator(bool spectator) { SpectatorRequests++; State = State with { PreferSpectator = spectator }; }
    public void SendChat(string text) => State = State with { Chat = State.Chat.Add(text) };
    public void RetryMap() { }
    public LobbyActionResult ValidateRules(MatchDefinition match) => LobbyActionResult.Ok;
    public void RulesAccepted(MatchDefinition match) => AcceptedRules++;
}

sealed class FakeHunters : IHunterSelectionBackend
{
    private readonly Dictionary<Hunter, CosmeticLoadout> _equipped = new();
    private readonly HashSet<Hunter> _pending = new();
    private TaskCompletionSource<HunterEquipResult>? _work;
    private Hunter _savingHunter;
    public Hunter PreferredHunter { get; set; } = Hunter.Samus;
    public byte PreferredColor => 0;
    public Hunter SavedHunter;
    public byte SavedColor;
    public string Blocked = "";
    public void SaveIdentity(Hunter hunter, byte color) { SavedHunter = hunter; SavedColor = color; }
    public HunterSelectionProfile Capture(Hunter hunter) => new(
        _equipped.GetValueOrDefault(hunter, CosmeticLoadout.Default), _pending.Contains(hunter),
        ImmutableArray.Create(SkinContext.Biped, SkinContext.ViewModel), true, "Installed pack enabled.", "Effects visible.");
    public bool IsUnlocked(Hunter hunter, CosmeticDefinition definition, out string reason)
    { reason = definition.Key == Blocked ? "This cosmetic is locked by the test authority." : ""; return reason.Length == 0; }
    public Task<HunterEquipResult> EquipAsync(Hunter hunter, CosmeticLoadout loadout, CancellationToken cancellationToken)
    {
        _equipped[hunter] = loadout; _pending.Add(hunter); _savingHunter = hunter;
        _work = new(); return _work.Task;
    }
    public void Complete(bool synced)
    {
        if (synced) _pending.Remove(_savingHunter);
        _work!.SetResult(new(synced, synced ? "EQUIPPED / SYNCED" : "LOCAL / NOT SYNCED — server unavailable"));
    }
}
