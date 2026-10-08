using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

static class Program
{
    private static int _checks;
    static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    static void Wait(Func<bool> done, Fixture fixture, bool tick = true)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < deadline) { if (tick) fixture.Controller.Tick(); Thread.Sleep(2); }
        if (tick) fixture.Controller.Tick();
        Check(done(), "operation completed within deadline");
    }
    static bool Released(PartyReservedAdmission admission)
    {
        try { var taken = admission.Take(); taken.Transport.Dispose(); return false; }
        catch (InvalidOperationException) { return true; }
    }
    static void Main(string[] args)
    {
        if (args.Contains("--live-queue")) { LiveQueueChecks.Run(); return; }
        using (var f = new Fixture())
        {
            f.Service.Party = new("party-A", "leader", true, 3);
            f.Controller.Open(true); f.Controller.QuickPlay();
            Wait(() => f.Connected == 1, f);
            Check(f.Service.RequiredSlots == 3 && f.Service.LobbyOnly, "party search uses whole-party capacity and lobby phase");
            Check(f.Service.Prepares == 1 && f.Service.Joins == 1, "one reservation precedes one join");
            Check(f.Service.FindCalls == 1, "busy Quick Play cannot start duplicate connection");
            Check(ReferenceEquals(f.Service.PreparedAdmission, f.Service.JoinAdmission), "actual prepared admission is transferred");
            Check(f.Service.TravelNotes == 1, "party travel publication follows accepted join");
            Check(f.Service.Canceled.Count == 0, "accepted party keeps its member reservation");
        }
        using (var f = new Fixture())
        {
            f.Controller.Open(true); Wait(() => f.Connected == 1, f);
            Check(f.Service.RequiredSlots == 1 && !f.Service.LobbyOnly && f.Service.Prepares == 0, "solo Quick Play preserves normal matchmaking");
        }
        using (var f = new Fixture())
        {
            f.Service.Party = new("party-A", "leader", true, 3); f.Service.DiscoveryHold = new();
            f.Controller.Open(false); f.Controller.Tick(); f.Controller.JoinSelected(0, spectate: true);
            Wait(() => f.Connected == 1, f);
            Check(f.Service.Spectate && f.Service.Prepares == 0, "spectator joining never consumes whole-party combatant reservation");
            f.Service.DiscoveryHold.SetResult(new(true, 1, 1, "Done"));
        }
        using (var f = new Fixture())
        {
            f.Service.Party = new("party-A", "leader", true, 3); f.Service.DiscoveryHold = new();
            f.Controller.Open(false); f.Controller.Tick(); f.Controller.JoinSelected(0);
            Wait(() => f.Connected == 1, f);
            Check(f.Service.Prepares == 1, "a live row can reserve while discovery is still running");
            f.Service.DiscoveryHold.SetResult(new(true, 1, 1, "Done"));
        }
        using (var f = new Fixture())
        {
            f.Service.Party = new("party-A", "leader", true, 3);
            f.Service.Entry = f.Service.Entry with { Status = f.Service.Entry.Status with { ReservedSlots = 6 } };
            f.Controller.Open(false); f.Controller.Tick(); f.Controller.JoinSelected(0); f.Controller.Tick();
            Check(f.Service.Prepares == 0 && f.Service.Joins == 0, "reserved seats are excluded from available party capacity");
            Check(f.Text["play_status"].Contains("ENOUGH OPEN SLOTS"), "capacity refusal stays visible");
        }
        using (var f = new Fixture())
        {
            f.Service.Party = new("party-A", "leader", true, 3);
            f.Controller.Open(false); f.Controller.Tick(); f.Controller.JoinEndpoint("localhost:12345", false);
            Wait(() => f.Connected == 1, f);
            Check(f.Service.Probes == 1 && f.Service.Prepares == 1, "manual party join verifies lobby before reservation");
        }
        using (var f = new Fixture())
        {
            f.Service.Party = new("party-A", "leader", true, 3); f.Service.SearchHold = new();
            f.Controller.Open(true); f.Service.Party = new("party-A", "leader", true, 4);
            f.Service.SearchHold.SetResult(new(true, f.Service.Entry, default, "Found"));
            Wait(() => f.Text.TryGetValue("play_status", out string? value) && value.Contains("PARTY CHANGED"), f);
            Check(f.Service.Prepares == 0 && f.Service.Joins == 0, "membership changes invalidate searched party witness");
        }
        using (var f = new Fixture())
        {
            f.Service.Party = new("party-A", "leader", true, 3); f.Service.PrepareHold = new();
            f.Controller.Open(true); Wait(() => f.Service.Prepares == 1, f); f.Controller.Cancel();
            var admission = FakeServices.Admission();
            f.Service.PrepareHold.SetResult(new(true, "", admission, "owned-A"));
            Wait(() => f.Service.Canceled.Contains("owned-A"), f);
            Check(f.Service.Joins == 0 && Released(admission), "canceled preparation releases admission before joining");
            Check(f.Service.Canceled.SequenceEqual(new[] { "owned-A" }), "cleanup cancels exact owned request once");
        }
        using (var f = new Fixture())
        {
            f.Service.Party = new("party-A", "leader", true, 3); f.Service.PrepareFailure = true;
            f.Service.ThrowOnCancel = true; f.Controller.Open(true);
            Wait(() => f.Service.Canceled.Contains("owned-A"), f);
            Check(f.Service.Joins == 0, "failed reservation refuses direct join fallback");
            f.Service.Party = null; f.Controller.Cancel(); f.Controller.Open(true);
            Wait(() => f.Connected == 1, f);
            Check(f.Service.Joins == 1, "failed cleanup releases serialization gate for next operation");
        }
        using (var f = new Fixture())
        {
            var admission = FakeServices.Admission(); var request = new SocialJoinRequest("localhost", 12345, "friend lobby", "arena", admission, 999);
            string? failure = null; f.Controller.JoinSocial(request, error => failure = error);
            Wait(() => failure != null, f);
            Check(failure!.Contains("authority changed") && f.Connected == 0, "social epoch mismatch reports failure and refuses handoff");
            Check(request.IsDisposed && Released(admission) && f.Service.Stops == 1, "failed social join retires request and only owned session");
            Check(f.Controller.Visible && f.Service.FindCalls == 0 && f.Service.DiscoverCalls == 0, "social join preserves route without directory browse");
        }
        using (var f = new Fixture())
        {
            var admission = FakeServices.Admission(); var request = new SocialJoinRequest("localhost", 12345, "friend lobby", "arena", admission, 123);
            f.Controller.JoinSocial(request, _ => throw new Exception("unexpected failure"));
            Wait(() => f.Connected == 1, f);
            Check(request.IsDisposed && ReferenceEquals(admission, f.Service.JoinAdmission), "social admission transfers once and request retires");
            Check(f.Plan.Lobby?.ServerName == "friend lobby", "verified social lobby title reaches handoff");
        }
        using (var f = new Fixture())
        {
            f.Service.JoinHold = new(); var request = new SocialJoinRequest("localhost", 12345, "lobby", "arena", authorityEpoch: 123);
            f.Controller.JoinSocial(request, _ => { }); Wait(() => f.Service.Joins == 1, f);
            var refusedAdmission = FakeServices.Admission(); var refused = new SocialJoinRequest("localhost", 12345, "other", "arena", refusedAdmission);
            int failures = 0; f.Controller.JoinSocial(refused, _ => failures++);
            Check(failures == 1 && refused.IsDisposed && Released(refusedAdmission), "busy join releases incoming request without replacing current operation");
            f.Controller.Cancel(); f.Service.JoinHold.SetResult(true);
            Wait(() => request.IsDisposed, f);
            Check(f.Connected == 0 && f.Service.Stops == 1, "late completed canceled social join releases owned connection");
        }
        using (var f = new Fixture())
        {
            f.Controller.Open(true); Wait(() => f.Service.Joins == 1, f, tick: false);
            f.Controller.Cancel(); var newer = new object(); f.Service.Connection = newer; f.Controller.Tick();
            Check(f.Connected == 0 && f.Service.Stops == 0 && ReferenceEquals(newer, f.Service.Connection), "stale completed join cannot stop newer connection with same authority epoch");
        }
        using (var f = new Fixture())
        {
            f.Controller.Open(true); Wait(() => f.Service.Joins == 1, f, tick: false); f.Controller.Dispose();
            Check(f.Connected == 0 && f.Service.Stops == 1, "retiring queued completion invokes connection cleanup");
        }
        using (var f = new Fixture())
        {
            f.Service.JoinHold = new(); var request = new SocialJoinRequest("localhost", 12345, "lobby", "arena");
            f.Controller.JoinSocial(request, _ => { }); Wait(() => f.Service.Joins == 1, f);
            f.Controller.Dispose(); f.Service.JoinHold.SetResult(true); Wait(() => request.IsDisposed, f, tick: false);
            Check(f.Service.Stops == 1 && f.Connected == 0, "late callback after presenter disposal cleans up without publication");
        }
        using (var f = new Fixture())
        {
            f.Service.ThrowJoin = true; string? failure = null;
            f.Controller.JoinSocial(new("localhost", 12345, "lobby", "arena"), error => failure = error);
            Wait(() => failure != null, f);
            Check(!failure!.Contains("raw-secret"), "raw service exception never reaches native status or social callback");
        }
        using (var f = new Fixture())
        {
            f.Service.RefuseJoin = true;
            f.Service.Entry = f.Service.Entry with { Status = f.Service.Entry.Status with { Players = 8, WaitlistSupported = true } };
            f.Controller.Open(false); f.Controller.Tick(); f.Controller.JoinSelected(0); f.Controller.Tick();
            Check(f.Controller.QueueSnapshot.Visible && f.Controller.QueueSnapshot.CanJoin && f.Service.QueueOpens == 0,
                "full server offers opt-in waitlist before opening transport");
            f.Controller.QueueJoin(); f.Controller.QueueJoin(); f.Controller.Tick();
            Check(f.Service.QueueOpens == 1 && !f.Controller.QueueSnapshot.CanJoin, "queue join creates one owned client");
            var queue = f.Service.Queue!; queue.SeatAvailable = true; f.Controller.Tick();
            Check(f.Controller.QueueSnapshot.CanAccept && f.Service.QueuedJoins == 0, "ready seat requires explicit user acceptance");
            f.Controller.QueueAccept(); f.Controller.QueueAccept(); f.Controller.Tick();
            Check(queue.Accepts == 1 && !f.Controller.QueueSnapshot.CanAccept, "duplicate accept is suppressed for current offer");
            queue.Admitted = true; f.Controller.Tick(); Wait(() => f.Connected == 1, f);
            Check(f.Service.QueuedJoins == 1 && ReferenceEquals(queue, f.Service.JoinedQueue), "ready client itself transfers to gameplay admission");
            Check(queue.Disposals == 1 && !f.Controller.QueueSnapshot.Visible, "accepted client retires once after ownership transfer");
        }
        using (var f = new Fixture())
        {
            f.Service.RefuseJoin = true;
            f.Service.Entry = f.Service.Entry with { Status = f.Service.Entry.Status with { Players = 8, WaitlistSupported = true } };
            f.Controller.Open(false); f.Controller.Tick(); f.Controller.JoinSelected(0); f.Controller.Tick();
            f.Controller.QueueJoin(); f.Controller.Tick(); var queue = f.Service.Queue!;
            queue.SeatAvailable = true; f.Controller.Tick(); f.Controller.QueueDecline(); f.Controller.Tick();
            Check(queue.Declines == 1 && queue.Disposals == 1 && !f.Controller.QueueSnapshot.Visible,
                "decline leaves offered queue and releases owned client");
        }
        using (var f = new Fixture())
        {
            f.Service.RefuseJoin = true; f.Service.QueueOpenHold = new();
            f.Service.Entry = f.Service.Entry with { Status = f.Service.Entry.Status with { Players = 8, WaitlistSupported = true } };
            f.Controller.Open(false); f.Controller.Tick(); f.Controller.JoinSelected(0); f.Controller.Tick();
            f.Controller.QueueJoin(); f.Controller.QueueLeave(); var late = new FakeQueue();
            f.Service.QueueOpenHold.SetResult(late);
            Wait(() => late.Disposals == 1, f);
            Check(f.Service.QueuedJoins == 0 && !f.Controller.QueueSnapshot.Visible, "late queue-open response after leave is retired");
        }
        using (var f = new Fixture())
        {
            f.Service.RefuseJoin = true;
            f.Service.Entry = f.Service.Entry with { Status = f.Service.Entry.Status with { Players = 8, WaitlistSupported = true } };
            f.Controller.Open(false); f.Controller.Tick(); f.Controller.JoinSelected(0); f.Controller.Tick();
            f.Controller.QueueJoin(); f.Controller.Tick(); var queue = f.Service.Queue!;
            queue.SeatAvailable = true; queue.OfferEpoch = 999; f.Controller.Tick(); f.Controller.QueueAccept(); f.Controller.Tick();
            Check(queue.Accepts == 0 && queue.Disposals == 1 && !f.Controller.QueueSnapshot.Visible,
                "changed authority offer is rejected before seat acceptance");
        }
        using (var f = new Fixture())
        {
            f.Service.RefuseJoin = true;
            f.Service.Entry = f.Service.Entry with { Status = f.Service.Entry.Status with { Players = 8, WaitlistSupported = true } };
            f.Controller.Open(false); f.Controller.Tick(); f.Controller.JoinSelected(0); f.Controller.Tick();
            f.Controller.QueueJoin(); f.Controller.Tick(); var queue = f.Service.Queue!;
            queue.Error = "Server closed queue."; f.Controller.Tick();
            Check(queue.Disposals == 1 && f.Controller.QueueSnapshot.Visible && f.Controller.QueueSnapshot.CanJoin,
                "ended waitlist leaves visible error and allows explicit retry");
            Check(f.Controller.QueueSnapshot.Status == "Server closed queue.", "queue refusal persists in modal snapshot");
        }
        using (var f = new Fixture())
        {
            f.Service.RefuseJoin = true; f.Service.JoinHold = new();
            f.Service.Entry = f.Service.Entry with { Status = f.Service.Entry.Status with { Players = 8, WaitlistSupported = true } };
            f.Controller.Open(false); f.Controller.Tick(); f.Controller.JoinSelected(0); f.Controller.Tick();
            f.Controller.QueueJoin(); f.Controller.Tick(); var queue = f.Service.Queue!;
            queue.SeatAvailable = true; f.Controller.QueueAccept(); queue.Admitted = true; f.Controller.Tick();
            Wait(() => f.Service.QueuedJoins == 1, f); f.Controller.QueueLeave(); f.Service.JoinHold.SetResult(true);
            Wait(() => queue.Disposals == 1, f);
            Check(f.Service.Stops == 1 && f.Connected == 0 && !f.Controller.QueueSnapshot.Visible,
                "canceling admission transfer releases late ready session without handoff");
        }
        using (var f = new Fixture())
        {
            f.Service.RefuseJoin = true;
            f.Service.Entry = f.Service.Entry with { Status = f.Service.Entry.Status with { Players = 8, WaitlistSupported = true } };
            f.Controller.Open(false); f.Controller.Tick(); f.Controller.JoinSelected(0); f.Controller.Tick();
            f.Controller.QueueJoin(); f.Controller.Tick(); var queue = f.Service.Queue!;
            f.Controller.OpenCreate(); f.Controller.Tick();
            Check(queue.Disposals == 1 && !f.Controller.QueueSnapshot.Visible,
                "opening a host form retires existing waitlist ownership");
        }
        using (var f = new Fixture())
        {
            f.Service.RefuseJoin = true; f.Service.QueueOpenHold = new();
            f.Service.Entry = f.Service.Entry with { Status = f.Service.Entry.Status with { Players = 8, WaitlistSupported = true } };
            f.Controller.Open(false); f.Controller.Tick(); f.Controller.JoinSelected(0); f.Controller.Tick();
            f.Controller.QueueJoin(); f.Controller.OpenCreate(); var late = new FakeQueue();
            f.Service.QueueOpenHold.SetResult(late); Wait(() => late.Disposals == 1, f);
            Check(!f.Controller.QueueSnapshot.Visible && f.Service.QueuedJoins == 0,
                "late waitlist preparation cannot reopen modal after a host-form switch");
        }
        using (var f = new Fixture())
        {
            var field = typeof(RmlMultiplayerController).GetField("_requiredMap", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var pin = new CommunityHostRequest("fixture", MapContentIdentity.BuiltIn("fixture"));
            field.SetValue(f.Controller, pin); f.Controller.Open(false);
            Check(field.GetValue(f.Controller) == null, "normal Open clears prior community map pin");
            field.SetValue(f.Controller, pin); f.Controller.OpenCreate();
            Check(field.GetValue(f.Controller) == null, "normal Create form clears prior community pin");
            field.SetValue(f.Controller, pin); f.Controller.Cancel();
            Check(field.GetValue(f.Controller) == null, "Cancel clears prior community pin");
            CustomRooms.MapDirectory = Path.Combine(Path.GetTempPath(), "prime-rmlui-map-check-empty-" + Guid.NewGuid());
            CustomRooms.InstallSnapshot(new MapDefinition { Name = "NATIVE-STUDIO-FIXTURE", MapId = Guid.NewGuid() });
            int discoveries = f.Service.DiscoverCalls;
            Check(f.Controller.OpenCreateForRoom("native-studio-fixture"), "actual newly published Studio source opens native host form");
            f.Controller.Tick();
            Check(f.Text["play_create_map"] == "NATIVE-STUDIO-FIXTURE" && f.Service.DiscoverCalls == discoveries, "Studio map selected without network discovery");
            Check(!f.Controller.OpenCreateForRoom("invented-map-name"), "unknown Studio map name is rejected");
        }
        Console.WriteLine($"Native multiplayer contracts: {_checks} assertions passed.");
    }
}

sealed class Fixture : IDisposable
{
    public readonly FakeServices Service = new();
    public readonly Dictionary<string, string> Text = new();
    public readonly RmlMultiplayerController Controller;
    public int Connected;
    public LaunchPlan Plan;
    public Fixture()
    {
        Controller = new(new[] { "arena" }, (id, value) => Text[id] = value, (_, _) => { },
            (_, _) => { }, _ => "Test Player", Service);
        Controller.Connected += plan => { Plan = plan; Connected++; };
    }
    public void Dispose() => Controller.Dispose();
}

sealed class FakeServices : IRmlMultiplayerServices
{
    public SocialPartyView? Party;
    public SocialPartyView? CurrentParty => Party;
    public ulong AuthorityEpoch => 123;
    public object? Connection;
    public object? ConnectionIdentity => Connection;
    public readonly ConcurrentQueue<string> Canceled = new();
    public int FindCalls, DiscoverCalls, Prepares, Joins, Probes, Stops, TravelNotes, RequiredSlots;
    public bool LobbyOnly, Spectate, PrepareFailure, ThrowOnCancel, ThrowJoin;
    public bool RefuseJoin;
    public int QueueOpens, QueuedJoins;
    public FakeQueue? Queue;
    public TaskCompletionSource<IRmlLobbyQueue>? QueueOpenHold;
    public IRmlLobbyQueue? JoinedQueue;
    public PartyReservedAdmission? PreparedAdmission, JoinAdmission;
    public TaskCompletionSource<ServerDiscoveryResult>? DiscoveryHold;
    public TaskCompletionSource<QuickPlaySearchResult>? SearchHold;
    public TaskCompletionSource<SocialLeaderAdmissionResult>? PrepareHold;
    public TaskCompletionSource<bool>? JoinHold;
    public ServerBrowserEntry Entry = new(new MasterListing { Address = "localhost", Port = 12345, ServerName = "fixture" },
        new ServerStatus { Online = true, LobbyEnabled = true, Phase = SessionPhase.Lobby,
            Protocol = NetConfig.ProtocolVersion, AuthorityEpoch = 123, Players = 1, MaxPlayers = 8 });
    public Task<ServerDiscoveryResult> DiscoverAsync(Action<ServerBrowserEntry> onEntry, CancellationToken token)
    { DiscoverCalls++; onEntry(Entry); return DiscoveryHold?.Task ?? Task.FromResult(new ServerDiscoveryResult(true, 1, 1, "Done")); }
    public Task<QuickPlaySearchResult> FindBestAsync(int requiredSlots, bool lobbyOnly, CancellationToken token)
    { FindCalls++; RequiredSlots = requiredSlots; LobbyOnly = lobbyOnly; return SearchHold?.Task ?? Task.FromResult(new QuickPlaySearchResult(true, Entry, default, "Found")); }
    public Task<ServerStatus> ProbeAsync(string host, int port, CancellationToken token)
    { Probes++; return Task.FromResult(Entry.Status); }
    public Task<SocialLeaderAdmissionResult> PrepareReservationAsync(ServerBrowserEntry entry, CancellationToken token)
    {
        Prepares++;
        if (PrepareHold != null) return PrepareHold.Task;
        if (PrepareFailure) return Task.FromResult(new SocialLeaderAdmissionResult(false, "Reservation refused", null, "owned-A"));
        PreparedAdmission = Admission(); return Task.FromResult(new SocialLeaderAdmissionResult(true, "", PreparedAdmission, "owned-A"));
    }
    public Task CancelReservationAsync(string requestId)
    { Canceled.Enqueue(requestId); return ThrowOnCancel ? Task.FromException(new IOException("offline")) : Task.CompletedTask; }
    public async Task<OnlineJoinResult> JoinAsync(string host, int port, string player, Hunter hunter, int suit,
        bool spectate, PartyReservedAdmission? admission, CancellationToken token)
    {
        Joins++; Spectate = spectate; JoinAdmission = admission;
        if (ThrowJoin) throw new IOException("raw-secret-auth-response");
        if (RefuseJoin) return new(false, default, "Server full.");
        if (JoinHold != null) await JoinHold.Task;
        if (admission != null) { var taken = admission.Take(); taken.Transport.Dispose(); }
        Connection = new object();
        return new(true, new LaunchPlan { Kind = LaunchKind.Online, PlayerName = player, Hunter = hunter, Port = port, Spectate = spectate }, "");
    }
    public Task<IRmlLobbyQueue> OpenQueueAsync(string host, int port, CancellationToken token)
    {
        QueueOpens++;
        return QueueOpenHold?.Task ?? Task.FromResult<IRmlLobbyQueue>(Queue ??= new FakeQueue());
    }
    public async Task<OnlineJoinResult> JoinQueuedAsync(string host, int port, string player, Hunter hunter, int suit,
        IRmlLobbyQueue queue, CancellationToken token)
    {
        QueuedJoins++; JoinedQueue = queue;
        if (JoinHold != null) await JoinHold.Task;
        Connection = new object();
        return new(true, new LaunchPlan { Kind = LaunchKind.Online, PlayerName = player, Hunter = hunter, Port = port }, "");
    }
    public void StopOwnedConnection(object? identity)
    { if (identity != null && ReferenceEquals(identity, Connection)) { Stops++; Connection = null; } }
    public void NotePartyTravel() => TravelNotes++;
    public static PartyReservedAdmission Admission()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 12345);
        return new(new NetTransport(0, playbackOnly: true), endpoint, 42,
            new ReceivedPacket(endpoint, new byte[] { (byte)PacketType.Welcome }, 1));
    }
}

sealed class FakeQueue : IRmlLobbyQueue
{
    public bool Admitted { get; set; }
    public bool Ended { get; set; }
    public bool SeatAvailable { get; set; }
    public int Position { get; set; } = 1;
    public int QueueLength { get; set; } = 2;
    public int RemainingOfferSeconds { get; set; } = 10;
    public ulong OfferId { get; set; } = 1;
    public ulong OfferEpoch { get; set; } = 123;
    public string? Error { get; set; }
    public string Status => SeatAvailable ? "PLAYER SLOT AVAILABLE" : "WAITING FOR PLAYER SLOT";
    public int Accepts, Declines, Disposals;
    public void Poll() { }
    public bool AcceptSeat() { Accepts++; return SeatAvailable; }
    public bool DeclineSeat() { Declines++; return SeatAvailable; }
    public void Dispose() { Disposals++; }
}
