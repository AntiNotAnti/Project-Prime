using System.Collections;
using System.Reflection;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;

// Reuse the existing actual UDP fixture; no production listener or auth endpoint is contacted.
static class LiveQueueChecks
{
    private static int _checks;
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); _checks++; }
    internal static void Run()
    {
        var oldTickets = NetSession.IdentityTicketSourceForChecks;
        NetSession.IdentityTicketSourceForChecks = NoTicket;
        try
        {
            using var rig = new LiveRig();
            object owner = rig.Add(76001), guest = rig.Add(76002);
            var service = new LiveServices(rig.Server.BoundPort);
            var controller = new RmlMultiplayerController(new[] { "MP1 SANCTORUS" }, (_, _) => { },
                (_, _) => { }, (_, _) => { }, _ => "NativeQueue", service);
            using (controller)
            {
                int connected = 0; controller.Connected += _ => connected++;
                controller.Open(false); controller.JoinEndpoint("127.0.0.1:" + rig.Server.BoundPort, false);
                rig.Wait(() => { controller.Tick(); return controller.QueueSnapshot.Visible; }, "native full lobby offers queue", 10000);
                Check(rig.Server.PeerCount == 2 && service.Queue == null, "queue requires explicit opt-in and consumes no gameplay seat");
                controller.QueueJoin();
                rig.Wait(() => { controller.Tick(); return service.Queue?.Position == 1; }, "native queue reaches real server FIFO");
                Check(rig.Server.PeerCount == 2 && !controller.QueueSnapshot.CanAccept, "full queue keeps both existing combatants");
                controller.QueueLeave(); controller.Tick();
                rig.Wait(() => NetStatus.Query("127.0.0.1", rig.Server.BoundPort, allowJoinProbe: false).WaitlistCount == 0,
                    "native leave releases real FIFO entry");
                Check(!controller.QueueSnapshot.Visible, "native queue modal closes after leave");

                controller.JoinEndpoint("127.0.0.1:" + rig.Server.BoundPort, false);
                rig.Wait(() => { controller.Tick(); return controller.QueueSnapshot.Visible; }, "queue can reopen after leave", 10000);
                controller.QueueJoin();
                rig.Wait(() => { controller.Tick(); return service.Queue?.Position == 1; }, "replacement queue receives fresh identity");
                rig.Remove(guest);
                rig.Wait(() => { controller.Tick(); return controller.QueueSnapshot.CanAccept; }, "real vacancy offers native ready control");
                Check(connected == 0 && !NetSession.Active && rig.Server.PeerCount == 1, "server offer does not launch gameplay before accept");
                object queueTransport = service.Queue!.Client.Transport;
                controller.QueueAccept(); controller.QueueAccept();
                rig.Wait(() => { controller.Tick(); return connected == 1; }, "native accepts real reserved seat and hands off", 10000);
                Check(NetSession.Active && NetSession.LocalSlot == 1 && rig.Server.PeerCount == 2,
                    "ready transfer completes actual network identity and authoritative slot");
                Check(ReferenceEquals(queueTransport, NetSession.ConnectionIdentity), "gameplay adopts the exact queued transport");
                Check(service.QueuedJoins == 1 && !controller.QueueSnapshot.Visible, "ready transfer emits one launch and closes modal");
                NetSession.Stop(); rig.Wait(() => rig.Server.PeerCount == 1, "queue gameplay departure releases occupied seat");

                guest = rig.Add(76003);
                controller.JoinEndpoint("127.0.0.1:" + rig.Server.BoundPort, false);
                rig.Wait(() => { controller.Tick(); return controller.QueueSnapshot.Visible; }, "promotion-race fixture opens queue", 10000);
                controller.QueueJoin();
                rig.Wait(() => { controller.Tick(); return service.Queue?.Position == 1; }, "promotion-race fixture waits FIFO");
                rig.Remove(guest);
                rig.Wait(() => { controller.Tick(); return controller.QueueSnapshot.CanAccept; }, "promotion-race receives offer");
                controller.QueueAccept();
                // Deliberately keep the UI from reading Welcome after the server promotes the connection.
                rig.Wait(() => rig.Server.PeerCount == 2, "server promoted before next frontend poll");
                Check(!service.Queue!.Admitted, "frontend has not observed promoted Welcome");
                controller.QueueLeave(); controller.Tick();
                rig.Wait(() => rig.Server.PeerCount == 1, "cancel after promotion releases seat before peer timeout", 3000);
                Check(connected == 1 && !NetSession.Active, "promotion cancellation emits no second gameplay handoff");
            }
            Console.WriteLine($"Native multiplayer real queue: {_checks} assertions passed; actual dedicated server, leave, ready and same-transport handoff.");
        }
        finally { NetSession.Stop(); NetSession.IdentityTicketSourceForChecks = oldTickets; }
    }
    private static bool NoTicket(uint id, out string ticket) { ticket = ""; return false; }
}

sealed class LiveRig : IDisposable
{
    private const BindingFlags Methods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly object _rig;
    private readonly Type _type;
    public DedicatedServer Server { get; }
    public LiveRig(int maxPlayers = 2)
    {
        _type = typeof(NetLobbyTest).GetNestedType("Rig", BindingFlags.NonPublic)!;
        _rig = _type.GetConstructors(Methods).Single().Invoke(new object?[]
            { ServerSessionPolicy.Lobby, Guid.Empty, false, null, null, maxPlayers, 15d, 10d, true, null });
        Server = (DedicatedServer)_type.GetField("Server", Methods)!.GetValue(_rig)!;
    }
    public object Add(uint id) => _type.GetMethod("Add", Methods)!.Invoke(_rig, new object[] { id, Guid.Empty, false })!;
    public void Remove(object client)
    {
        ((IList)_type.GetField("Clients", Methods)!.GetValue(_rig)!).Remove(client);
        ((IDisposable)client).Dispose();
    }
    public void Wait(Func<bool> condition, string label, int timeout = 5000)
    {
        try { _type.GetMethod("Wait", Methods)!.Invoke(_rig, new object[] { condition, label, timeout }); }
        catch (TargetInvocationException error) { throw error.InnerException!; }
    }
    public void EndMatch() => _type.GetMethod("EndMatchForTest", Methods)!.Invoke(_rig, null);
    public void Dispose() => ((IDisposable)_rig).Dispose();
}

sealed class LiveServices : IRmlMultiplayerServices
{
    private readonly RmlMultiplayerServices _actual = new();
    private readonly int _port;
    public RmlLobbyQueue? Queue;
    public int QueuedJoins;
    public LiveServices(int port) => _port = port;
    public SocialPartyView? CurrentParty => null;
    public ulong AuthorityEpoch => _actual.AuthorityEpoch;
    public object? ConnectionIdentity => _actual.ConnectionIdentity;
    public Task<ServerDiscoveryResult> DiscoverAsync(Action<ServerBrowserEntry> onEntry, CancellationToken token)
        => Task.FromResult(new ServerDiscoveryResult(true, 0, 0, "Local fixture"));
    public Task<QuickPlaySearchResult> FindBestAsync(int slots, bool lobbyOnly, CancellationToken token)
        => throw new InvalidOperationException("Live fixture uses explicit loopback endpoint.");
    public Task<ServerStatus> ProbeAsync(string host, int port, CancellationToken token) => _actual.ProbeAsync(host, port, token);
    public Task<SocialLeaderAdmissionResult> PrepareReservationAsync(ServerBrowserEntry entry, CancellationToken token)
        => throw new InvalidOperationException("Solo fixture cannot request party service.");
    public Task CancelReservationAsync(string requestId) => throw new InvalidOperationException("Solo fixture has no party reservation.");
    public Task<OnlineJoinResult> JoinAsync(string host, int port, string player, Hunter hunter, int suit,
        bool spectate, PartyReservedAdmission? admission, CancellationToken token) => Join(player, hunter, suit, token);
    public async Task<IRmlLobbyQueue> OpenQueueAsync(string host, int port, CancellationToken token)
    { Queue = (RmlLobbyQueue)await _actual.OpenQueueAsync(host, port, token); return Queue; }
    public Task<OnlineJoinResult> JoinQueuedAsync(string host, int port, string player, Hunter hunter, int suit,
        IRmlLobbyQueue queue, CancellationToken token)
    { QueuedJoins++; return Join(player, hunter, suit, token, ((RmlLobbyQueue)queue).Client); }
    private Task<OnlineJoinResult> Join(string player, Hunter hunter, int suit, CancellationToken token, LobbyQueueClient? queue = null)
        => Task.Run(() =>
        {
            bool joined = NetLaunch.Connect("127.0.0.1", _port, player, hunter,
                color: suit, cancellationToken: token, queuedAdmission: queue);
            if (!joined) NetSession.Stop();
            return new OnlineJoinResult(joined, new LaunchPlan { Kind = LaunchKind.Online, PlayerName = player, Hunter = hunter, Port = _port }, NetLaunch.LastJoinError);
        }, token);
    public void StopOwnedConnection(object? identity) => _actual.StopOwnedConnection(identity);
    public void NotePartyTravel() => throw new InvalidOperationException("Solo fixture has no party travel.");
}
