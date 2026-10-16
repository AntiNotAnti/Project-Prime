using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

static void Check(bool pass, string name)
{
    if (!pass) throw new InvalidOperationException(name);
    Console.WriteLine("RML03 PASS " + name);
}
static bool Flag(RmlUiMenuSnapshot snapshot, string key)
    => snapshot.Bindings.TryGetValue(key, out var value)
       && value.Kind == RmlUiBindingKind.Boolean && value.Boolean;
static string Text(RmlUiMenuSnapshot snapshot, string key)
    => snapshot.Bindings.TryGetValue(key, out var value) ? value.Text : "";
static RmlUiMenuSnapshot Wait(RmlUiMenuController controller, Func<RmlUiMenuSnapshot, bool> predicate)
{
    for (int i = 0; i < 150; i++)
    {
        var snapshot = controller.Tick();
        if (predicate(snapshot)) return snapshot;
        Thread.Sleep(10);
    }
    throw new TimeoutException("Timed out waiting for a versioned native presentation result.");
}

var services = new Stub();
using var controller = new RmlUiMenuController(services);
var home = controller.Tick();
Check(Text(home, "player_name") == "Pilot" && Text(home, "hunter_name") == "SAMUS"
    && !Flag(home, "lobby_mode") && !Flag(home, "multiplayer_mode"),
    "Home uses presentation-neutral current player and no fabricated session");
Check(controller.Tick().Revision == home.Revision,
    "unchanged native snapshots retain revision");

services.BrowsePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Dispatch(RmlUiIntentKind.PlayBrowse), "browser starts");
Check(controller.Busy && Flag(controller.Tick(), "play_busy"), "search status is visible but nonblocking");
services.BrowsePending.SetResult(new(true, "2 servers answered.", new[]
{
    new RmlUiServerCandidate("Test Arena", "127.0.0.1:27888", 2, 8, 35, true),
    new RmlUiServerCandidate("Old Protocol", "127.0.0.2:27888", 1, 8, 5, false)
}));
var directory = Wait(controller, s => Flag(s, "play_server0_present"));
Check(!controller.Busy && Text(directory, "play_server_count") == "1 LIVE"
    && Text(directory, "play_server0_name") == "Test Arena"
    && !Flag(directory, "play_server1_present"),
    "only compatible live directory rows are shown, with truthful counts");

services.JoinPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Dispatch(RmlUiIntentKind.PlayServer, 0, playerName: "Pilot"),
    "selected server starts authoritative join");
Check(controller.Joining && !controller.Dispatch(RmlUiIntentKind.PlayQuick),
    "duplicate admission is refused while connection is pending");
services.JoinPending.SetResult(new(true,
    new LaunchPlan { Kind = LaunchKind.Online, PlayerName = "Pilot", Hunter = Hunter.Samus },
    ""));
var joined = Wait(controller, s => services.Accepted == 1);
Check(services.Accepted == 1 && !controller.Joining,
    "admitted plan is handed off exactly once on the owner thread");
services.LobbyView = new(true, false, true, true, false, true, true,
    "Lobby", "MP1 SANCTORUS", "BATTLE", "FFA", "WAITING FOR HUNTERS", 8,
    new[]
    {
        new RmlUiLobbyPlayer("Pilot", "SAMUS", false, true, false),
        new RmlUiLobbyPlayer("Other", "TRACE", true, false, false)
    });
var lobby = Wait(controller, s => Flag(s, "lobby_mode"));
Check(Flag(lobby, "lobby_owner") && Flag(lobby, "lobby_require_ready")
    && Text(lobby, "lobby_ready_count") == "1 READY"
    && Text(lobby, "slot1_name") == "Other"
    && !Flag(lobby, "multiplayer_mode"),
    "native lobby projects only authoritative rules and roster");
Check(controller.Dispatch(RmlUiIntentKind.LobbyReady) && services.ReadyCalls == 1,
    "ready sends server command without client-side acknowledgement");
Check(!Flag(controller.Tick(), "lobby_local_ready"),
    "local ready state remains false until server confirms it");
services.LobbyView = services.LobbyView with { LocalReady = true };
Check(Flag(Wait(controller, s => Flag(s, "lobby_local_ready")), "lobby_local_ready"),
    "server response updates ready projection");
Check(controller.Dispatch(RmlUiIntentKind.LobbyStart) && services.StartCalls == 1,
    "owner match start delegated to existing service");
Check(controller.Dispatch(RmlUiIntentKind.LobbyLeave) && services.LeaveCalls == 1,
    "leave delegates to existing lobby lifecycle");
services.LobbyView = Stub.EmptyLobby;
Check(!Flag(Wait(controller, s => !Flag(s, "lobby_mode")), "lobby_mode"),
    "closing session resets native lobby without local invented state");

services.BrowsePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
controller.Dispatch(RmlUiIntentKind.PlayBrowse);
controller.Dispatch(RmlUiIntentKind.PlayCancel);
services.BrowsePending.SetResult(new(true, "STALE DATA",
    new[] { new RmlUiServerCandidate("Stale", "127.0.0.3:27888", 1, 8, 9, true) }));
Thread.Sleep(50);
Check(Text(controller.Tick(), "play_status") == "READY TO DEPLOY",
    "late cancelled discovery cannot overwrite current page");
Check(!controller.Dispatch(RmlUiIntentKind.PlayServer, 8),
    "invalid ABI server index rejected before touching services");

Exception? wrongThread = null;
Task.Run(() =>
{
    try { controller.Tick(); }
    catch (Exception error) { wrongThread = error; }
}).GetAwaiter().GetResult();
Check(wrongThread is InvalidOperationException,
    "native state projection rejects worker-thread writes");
Console.WriteLine("RML03 owner-thread launcher service contracts passed.");

sealed class Stub : IRmlUiMenuServices
{
    public static readonly RmlUiLobbyView EmptyLobby = new(false, false, false, false,
        false, false, false, "", "", "", "", "", 8, Array.Empty<RmlUiLobbyPlayer>());
    public TaskCompletionSource<RmlUiDirectory> BrowsePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<RmlUiJoinResult> JoinPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public RmlUiLobbyView LobbyView = EmptyLobby;
    public int Accepted, ReadyCalls, StartCalls, LeaveCalls;
    public HubSnapshot Hub() => new("Pilot", Hunter.Samus, Hunter.Samus, 0, true, "Desktop");
    public string BuildVersion() => "0.1.99";
    public RmlUiLobbyView Lobby() => LobbyView;
    public Task<RmlUiDirectory> Browse(CancellationToken _) => BrowsePending.Task;
    public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken _) =>
        Task.FromResult<RmlUiServerCandidate?>(null);
    public Task<RmlUiJoinResult> Join(string _, string __, CancellationToken ___) => JoinPending.Task;
    public bool AcceptJoin(LaunchPlan _) { Accepted++; return true; }
    public bool OpenCreateLobby() => true;
    public bool NavigateTo(RmlUiRouteArgument _) => true;
    public bool OpenStudio() => true;
    public bool SetReady() { ReadyCalls++; return true; }
    public bool StartMatch() { StartCalls++; return true; }
    public bool LeaveLobby() { LeaveCalls++; LobbyView = EmptyLobby; return true; }
    public bool NextHunter() => true;
    public bool NextSuit() => true;
    public bool OpenLobbyDetails() => true;
    public void Quit() { }
}
