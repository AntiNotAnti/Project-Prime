using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Network;

static class Program
{
    private static int _checks;
    private static long _frame;
    private static uint _nextId = 77000;
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Usage: rmlui-lobby-live-check <native library>"); return 2; }
        var oldTicket = NetSession.IdentityTicketSourceForChecks;
        NetSession.IdentityTicketSourceForChecks = NoTicket;
        nint native = NativeLibrary.Load(Path.GetFullPath(args[0]));
        NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly, (name, _, _) => name == "ProjectPrime.RmlUi.Native" ? native : 0);
        try
        {
            using var host = new RmlUiHost();
            Check(host.Initialize(1280, 720, 1, Path.Combine(AppContext.BaseDirectory, "rmlui"), RmlUiRenderBackend.DrawList), "actual native host initializes");
            foreach (int size in new[] { 2, 4, 8 })
            {
                using var rig = new LiveRig(size);
                var clients = Add(rig, size);
                try
                {
                    Check(clients.All(c => c.Controller.Snapshot().Players.Length == size), $"{size} native controllers capture real authoritative roster");
                    RunMatch(rig, clients, host, $"{size}-client");
                    Console.WriteLine($"Native live lobby: {size} clients passed authoritative ready/start/load/return.");
                }
                finally { Retire(clients); }
            }
            using (var rig = new LiveRig(2))
            {
                for (int cycle = 0; cycle < 50; cycle++)
                {
                    var clients = Add(rig, 2);
                    try
                    {
                        ShowAndClick(host, clients[0], "lobby_ready", RmlUiIntentKind.LobbyReady, LobbyIntentKind.ToggleReady);
                        PumpUntil(rig, clients, () => clients[0].Controller.Snapshot().Players.Any(p => p.Slot == clients[0].Backend.Slot && p.Ready), "cycle native ready acknowledged");
                        if (cycle == 0)
                        {
                            // Reuse the same sessions/controllers through every return;
                            // fresh controllers would miss a consumed one-shot start bug.
                            for (int match = 0; match < 20; match++)
                            {
                                RunMatch(rig, clients, host, "return-" + match);
                                if ((match + 1) % 5 == 0)
                                    Console.WriteLine($"Native live lobby: {match + 1}/20 returns passed on the same persistent controllers.");
                            }
                        }
                        else Show(host, clients[1]);
                    }
                    finally { Retire(clients); }
                    rig.Wait(() => rig.Server.PeerCount == 0, "lobby cycle releases all actual client seats");
                    Check(!NetSession.Active, "wire fixture never starts a second global engine session");
                    if ((cycle + 1) % 10 == 0) Console.WriteLine($"Native live lobby: {cycle + 1}/50 join/leave cycles; 20/20 persistent match returns passed.");
                }
            }
            Console.WriteLine($"Native live lobby PASS: {_checks} assertions; real2/4/8 UDP clients,50 lobby cycles,20 persistent match returns, actual native DOM/input and shared controllers.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { NetSession.Stop(); NetSession.IdentityTicketSourceForChecks = oldTicket; NativeLibrary.Free(native); }
    }
    private static bool NoTicket(uint id, out string ticket) { ticket = ""; return false; }
    private sealed record Client(LiveLobbyBackend Backend, LobbySessionController Controller);
    private static List<Client> Add(LiveRig rig, int count)
    {
        var clients = new List<Client>();
        for (int i = 0; i < count; i++)
        {
            var backend = new LiveLobbyBackend(rig, rig.Add(++_nextId));
            var controller = new LobbySessionController(backend, new("Native fixture", "loopback"));
            controller.TransferPumpOwnership(LobbyPumpOwner.Native);
            controller.MatchRequested += (_, _) => { backend.LoadRequests++; backend.Loaded(); };
            clients.Add(new(backend, controller));
        }
        Pump(clients); return clients;
    }
    private static void Pump(List<Client> clients)
    { foreach (var client in clients) client.Controller.PumpOnce(LobbyPumpOwner.Native, ++_frame); }
    private static void PumpUntil(LiveRig rig, List<Client> clients, Func<bool> predicate, string label, int timeout = 15000)
        => rig.Wait(() => { Pump(clients); return predicate(); }, label, timeout);
    private static void Retire(List<Client> clients)
    { foreach (var client in clients) { client.Controller.Leave(); client.Controller.Dispose(); } }
    private static void RunMatch(LiveRig rig, List<Client> clients, RmlUiHost host, string label)
    {
        foreach (var client in clients)
        {
            if (!client.Controller.Snapshot().Players.Any(p => p.Slot == client.Backend.Slot && p.Ready))
                ShowAndClick(host, client, "lobby_ready", RmlUiIntentKind.LobbyReady, LobbyIntentKind.ToggleReady);
            PumpUntil(rig, clients, () => !client.Controller.Snapshot().CommandPending, "native ready result arrives");
        }
        PumpUntil(rig, clients, () => clients.All(c => c.Controller.Snapshot().Players.All(p => p.Ready)), "all real clients see ready roster");
        Client owner = clients.Single(c => c.Controller.Snapshot().IsOwner);
        int requests = clients.Sum(c => c.Backend.LoadRequests);
        ushort previousMatch = owner.Controller.Snapshot().MatchId;
        ulong previousGeneration = owner.Controller.Snapshot().StartGeneration;
        ShowAndClick(host, owner, "lobby_start", RmlUiIntentKind.LobbyStart, LobbyIntentKind.StartMatch);
        PumpUntil(rig, clients, () => clients.All(c => c.Controller.Snapshot().Phase == SessionPhase.InMatch), label + " match barrier releases", 20000);
        Check(clients.Sum(c => c.Backend.LoadRequests) == requests + clients.Count, "each real client issues one match-load handoff");
        Check(clients.All(c => c.Controller.Snapshot().MatchId != previousMatch
            && c.Controller.Snapshot().StartGeneration > previousGeneration), "next real match retains fresh authoritative start identity");
        foreach (var client in clients)
        {
            client.Controller.YieldPumpToGameplay();
            Check(!client.Controller.PumpOnce(LobbyPumpOwner.Native, ++_frame), "launcher pump yields only at explicit gameplay handoff");
        }
        rig.EndMatch();
        // Exercise the actual 21-second server results/intermission deadline.
        rig.Wait(() => clients.All(c => c.Backend.State.Phase == SessionPhase.Lobby), label + " server returns persistent clients", 30000);
        foreach (var client in clients) client.Controller.Resume();
        Pump(clients);
        Check(clients.All(c => !c.Controller.IsClosed && !c.Controller.IsSuspended && c.Controller.Snapshot().Players.Length == clients.Count),
            "match return keeps same controllers and live real client roster");
        Show(host, owner);
    }
    private static void Show(RmlUiHost host, Client client)
    {
        using var pages = new RmlUiLauncherPages(host);
        RmlUiLobbyBindings.Present(pages, client.Controller.Snapshot());
        pages.Flush(); host.Update(); pages.AfterUpdate(); host.Render(1280, 720);
        Check(host.TryGetElementBounds(pages.Manager.Page, "lobby_ready", out _, out _, out float width, out float height) && width > 0 && height > 0,
            "actual native lobby renders ready control from live state");
        Check(host.TryGetElementBounds(pages.Manager.Page, "lobby_brief", out _, out _, out float briefWidth, out float briefHeight)
            && briefWidth > 0 && briefHeight > 0,
            "actual live 2/4/8-player lobby exposes its squad readiness formation");
        Check(host.TryGetElementBounds(pages.Manager.Page, "lobby_brief_slot7", out _, out _, out float markerWidth, out _)
            && markerWidth > 0,
            "eighth readiness slot is present even before that seat is occupied");
        Check(host.TryGetElementBounds(pages.Manager.Page, "lobby_social_access", out _, out _, out float socialWidth, out float socialHeight)
            && socialWidth > 0 && socialHeight > 0,
            "actual native lobby exposes the authorized Invite Friends / Party route");
    }
    private static void ShowAndClick(RmlUiHost host, Client client, string id, RmlUiIntentKind expected, LobbyIntentKind command)
    {
        using var pages = new RmlUiLauncherPages(host);
        RmlUiLobbyBindings.Present(pages, client.Controller.Snapshot());
        pages.Flush(); host.Update(); pages.AfterUpdate(); host.Render(1280, 720);
        var document = pages.Manager.Page;
        Check(host.FocusDocument(document, id), "actual live lobby control can focus: " + id);
        Check(host.TryGetElementBounds(document, id, out float x, out float y, out float width, out float height) && width > 0 && height > 0,
            "actual live lobby action has usable bounds");
        host.Input.PointerMoved(x + width / 2, y + height / 2);
        host.Input.PointerButton(0, x + width / 2, y + height / 2, true);
        host.Input.PointerButton(0, x + width / 2, y + height / 2, false);
        host.Update();
        Check(host.TryTakeIntent(out var intent) && intent.Kind == expected && pages.Manager.Accept(intent), "real mouse yields current live document command");
        Check(client.Controller.Dispatch(client.Controller.Intent(command)).Accepted, "shared controller accepts live native intent");
        Check(!pages.Manager.Accept(intent), "same native packet cannot dispatch twice");
    }
}

sealed class LiveLobbyBackend : ILobbySessionBackend
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly LiveRig _rig;
    private readonly object _client;
    private readonly Type _type;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private uint? _pending;
    private bool _active = true;
    private string _message = "";
    public int LoadRequests;
    public LiveLobbyBackend(LiveRig rig, object client) { _rig = rig; _client = client; _type = client.GetType(); }
    public SessionStatePacket State => (SessionStatePacket)_type.GetField("State", Fields)!.GetValue(_client)!;
    public int Slot => (int)_type.GetField("Slot", Fields)!.GetValue(_client)!;
    private RosterPacket Roster => (RosterPacket)_type.GetField("Roster", Fields)!.GetValue(_client)!;
    public double Clock => _clock.Elapsed.TotalSeconds;
    public bool ShouldLoadMatch => _active && State.Phase == SessionPhase.Starting;
    public bool Refused => false;
    public bool TimedOut => false;
    public string RefusedMessage => "";
    public void Pump() { if (_active) _type.GetMethod("Drain", Fields)!.Invoke(_client, null); }
    public void Stop() { if (!_active) return; _active = false; _rig.Remove(_client); }
    public void Loaded() => _type.GetMethod("Loaded", Fields)!.Invoke(_client, new object?[] { null });
    public LobbySnapshot Capture()
    {
        SessionStatePacket state = State; RosterPacket roster = Roster;
        if (_pending is uint id && ((IDictionary)_type.GetField("Results", Fields)!.GetValue(_client)!).Contains(id))
        { _pending = null; _message = "Server acknowledged command."; }
        var players = ImmutableArray.CreateBuilder<LobbyPlayerSnapshot>(roster.Count);
        for (int i = 0; i < roster.Count; i++) players.Add(new(roster.Slots[i], roster.Names[i], (Hunter)roster.Hunters[i],
            roster.Colors[i], roster.Teams[i], roster.LobbyReady[i], roster.Pings[i], roster.IsBot(i), roster.IsSpectator(i),
            roster.BotLevels[i], roster.DamageReductions[i], MapAvailabilityState.Ready, roster.Generations[i]));
        return new()
        {
            Active = _active, Persistent = true, Phase = state.Phase, SessionRevision = state.Revision,
            RosterRevision = roster.Revision, MatchId = state.MatchId, AuthorityEpoch = state.AuthorityEpoch,
            StartGeneration = state.StartGeneration, StartStage = state.StartStage,
            ExpectedParticipants = state.ExpectedParticipants, LoadedParticipants = state.LoadedParticipants,
            WorldReadyParticipants = state.WorldReadyParticipants, OwnerSlot = state.OwnerSlot, MaxPlayers = state.MaxPlayers,
            LocalSlot = Slot, LocalHunter = Hunter.Samus, LocalColor = 0,
            PlayerName = roster.Names[Array.IndexOf(roster.Slots, (byte)Slot, 0, roster.Count)], Match = state.Match,
            RuleFlags = state.RuleFlags, RequiredMapReady = true, CommandPending = _pending != null,
            Message = _message, Players = players.MoveToImmutable()
        };
    }
    public bool SendCommand(LobbyIntent intent)
    {
        LobbyCommandType type = intent.Kind switch
        { LobbyIntentKind.ToggleReady => LobbyCommandType.SetReady, LobbyIntentKind.StartMatch => LobbyCommandType.StartMatch,
          _ => throw new InvalidOperationException("Live native fixture only sends ready/start commands.") };
        bool ready = !Capture().Players.Single(p => p.Slot == Slot).Ready;
        var packet = (LobbyCommandPacket)_type.GetMethod("Command", Fields)!.Invoke(_client,
            new object?[] { type, ready, null, byte.MaxValue, (sbyte)-1, intent.ExpectedRevision, (byte)0, (byte)1, (byte)0 })!;
        _pending = packet.CommandId; return true;
    }
    public void Identify(Hunter hunter, byte color) => throw new InvalidOperationException("Not used by live ready fixture.");
    public void SetSpectator(bool spectator) => throw new InvalidOperationException("Not used by live ready fixture.");
    public void SendChat(string text) => throw new InvalidOperationException("Live checks never send account messages.");
    public void RetryMap() => throw new InvalidOperationException("Built-in control-plane arena needs no download.");
    public LobbyActionResult ValidateRules(MatchDefinition match) => LobbyActionResult.Ok;
    public void RulesAccepted(MatchDefinition match) { }
}
