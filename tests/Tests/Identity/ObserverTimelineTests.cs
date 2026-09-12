using System;
using System.Buffers.Binary;
using System.Net;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using MphRead.Mods.Network;
using Xunit;
namespace MphRead.Tests;
public sealed class ObserverTimelineTests
{
    [Fact]
    public void ObserverJoinExtensionIsCanonicalAndStrict()
    {
        var join = new JoinPacket(NetHeader.Version, 123, Hunter.Samus, "WATCH", Observer: true);
        byte[] bytes = new byte[join.EncodedSize]; join.Write(bytes);
        Assert.Equal(37, bytes.Length); Assert.True(JoinPacket.TryRead(bytes, out var parsed)); Assert.True(parsed.Observer);
        bytes[34] = 2; Assert.False(JoinPacket.TryRead(bytes, out _));
        bytes[34] = 0; Assert.False(JoinPacket.TryRead(bytes, out _));
        bytes[34] = 1; bytes[35] = 1; Assert.False(JoinPacket.TryRead(bytes, out _));
        Assert.Equal(947, JoinPacket.MaxTicketBytes);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void ForcedObserverTransferChecksCapacityBeforeDetachingPlayer(int capacity, bool succeeds)
    {
        using var serverSocket = new NetTransport(0);
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS");
        var server = new ServerNetwork(serverSocket, rules, observers: new ObserverOptions(capacity, 0));
        using var socket = new NetTransport(0);
        using var client = new NetClient(socket, new IPEndPoint(IPAddress.Loopback, serverSocket.LocalPort), "PLAYER", Hunter.Samus);
        var timer = Stopwatch.StartNew(); uint tick = 1;
        while (!client.HasRoster && timer.ElapsedMilliseconds < 3000)
        { server.Poll(tick++); client.Poll(); Thread.Sleep(1); }
        Assert.True(client.HasRoster); var peer = server.Peers[client.Accepted.Slot]!;
        ulong connection = peer.Connection.Id;
        server.CaptureObserverSnapshot(new byte[] { 1 }); server.CaptureObserverWorld(new byte[] { 2 });
        server.CommitObserverTick(tick - 1);
        bool transferred = server.TryForceObserver(peer, out string reason);
        Assert.True(transferred == succeeds, reason);
        if (!succeeds)
        {
            Assert.Same(peer, server.Peers[peer.Slot]); Assert.Equal(1, server.Count); Assert.Equal(0, server.ObserverCount);
            return;
        }
        Assert.Equal(0, server.Count); Assert.Equal(1, server.ObserverCount);
        timer.Restart();
        while (!client.IsObserver && timer.ElapsedMilliseconds < 3000)
        { server.Poll(++tick); client.Poll(); Thread.Sleep(1); }
        Assert.True(client.IsObserver); Assert.Equal(connection, client.Connection!.Id);
        Assert.True(client.Accepted.IsObserver); Assert.Equal(1u, client.RoleRevision);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(8, 1)]
    [InlineData(8, 16)]
    public void ExplicitGuestObserverHandshakeOwnsNoPlayerSlotAndCannotSendInputs(int players, int observers)
    {
        using var serverSocket = new NetTransport(0);
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS");
        var server = new ServerNetwork(serverSocket, rules, observers: new ObserverOptions(observers, 1));
        var sockets = new List<NetTransport>(); var clients = new List<NetClient>();
        try
        {
        uint setupTick = 1;
        for (int i = 0; i < players; i++)
        {
            var playerSocket = new NetTransport(0); sockets.Add(playerSocket);
            var player = new NetClient(playerSocket, new IPEndPoint(IPAddress.Loopback, serverSocket.LocalPort), $"P{i}", Hunter.Samus); clients.Add(player);
            var joinTimer = Stopwatch.StartNew();
            while (!player.HasRoster && joinTimer.ElapsedMilliseconds < 3000)
            { server.Poll(setupTick++); foreach (var connected in clients) connected.Poll(); Thread.Sleep(1); }
            Assert.True(player.HasRoster);
        }
        server.Poll(setupTick);
        // Admission consumes only the complete historical metadata here; this
        // fixture deliberately never sends Ready or applies these opaque bodies.
        server.CaptureObserverSnapshot(new byte[] { 1 });
        server.CaptureObserverWorld(new byte[] { 2 });
        server.CommitObserverTick(setupTick);
        using var socket = new NetTransport(0);
        using var client = new NetClient(socket, new IPEndPoint(IPAddress.Loopback, serverSocket.LocalPort),
            "OBSERVER", Hunter.Samus, observer: true);
        var timer = Stopwatch.StartNew(); uint tick = setupTick + 60;
        while (!client.HasRoster && client.Failure == null && timer.ElapsedMilliseconds < 3000)
        { server.Poll(tick++); client.Poll(); Thread.Sleep(1); }
        Assert.Null(client.Failure); Assert.True(client.HasRoster);
        Assert.True(client.Accepted.IsObserver); Assert.Equal(setupTick, client.Accepted.ServerTick);
        for (int i = 1; i < observers; i++)
        {
            var extraSocket = new NetTransport(0); sockets.Add(extraSocket);
            var extra = new NetClient(extraSocket, new IPEndPoint(IPAddress.Loopback, serverSocket.LocalPort), $"WATCH{i}", Hunter.Samus, observer: true);
            clients.Add(extra); timer.Restart();
            while (!extra.HasRoster && timer.ElapsedMilliseconds < 3000)
            { server.Poll(tick++); foreach (var connected in clients) connected.Poll(); client.Poll(); Thread.Sleep(1); }
            Assert.True(extra.HasRoster); Assert.True(extra.Accepted.IsObserver);
        }
        Assert.Equal(players, server.Count); Assert.Equal(observers, server.ObserverCount);
        foreach (var peer in server.Peers) { if (players == 0) Assert.Null(peer); else Assert.NotNull(peer); }
        Assert.False(client.SendInputs(ReadOnlySpan<InputCommand>.Empty));
        }
        finally
        {
            foreach (var connected in clients) connected.Dispose();
            foreach (var playerSocket in sockets) playerSocket.Dispose();
        }
    }

    [Fact]
    public void DelayNeverFallsBackToLiveAndPreservesHistoricalMatch()
    {
        var history = new ObserverTimeline();
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS");
        history.BeginMatch(1);
        byte[] snapshot = { 1 }, world = { 2 }, roster = { 3 };
        history.Snapshot(snapshot); history.WorldBatch(world); history.Roster(roster, 1);
        history.Commit(10, 1, rules);
        snapshot[0] = 99; world[0] = 99; roster[0] = 99;
        Assert.Null(history.Baseline(new(69), new(60)));
        ObserverCursor old = history.Baseline(new(70), new(60))!.Value;
        Assert.True(history.TryGet(old, out ObserverFrame oldFrame));
        Assert.Equal(1, oldFrame.Snapshot![0]);
        history.BeginMatch(2); history.Commit(71, 2, rules);
        Assert.Equal(old, history.Baseline(new(100), new(60)));
        Assert.True(history.TryGet(history.Baseline(new(100), SimDuration.Zero)!.Value, out ObserverFrame retained));
        Assert.Equal(1u, retained.MatchId);
    }
    [Fact]
    public void RetentionEvictsCursorsAndHandlesTickWrap()
    {
        var history = new ObserverTimeline();
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS");
        history.BeginMatch(1); history.Snapshot(new byte[] { 1 }); history.WorldBatch(new byte[] { 2 }); history.Roster(new byte[] { 3 }, 1);
        history.Commit(0, 1, rules); ObserverCursor cursor = history.Baseline(new(0), SimDuration.Zero)!.Value;
        for (uint tick = 1; tick <= 3602; tick++) history.Commit(tick, 1, rules);
        Assert.False(history.Owns(cursor)); Assert.True(history.Count <= 3601);
        Assert.True(history.RetainedBytes <= ObserverTimeline.MaxBytes);
        Assert.True(ObserverTimeline.Due(new(20), new(UInt32.MaxValue - 39), new(60)));
        Assert.False(ObserverTimeline.Due(new(20), new(21), SimDuration.Zero));
    }

    [Fact]
    public void RetentionIsDemandDrivenAndCanBeDisabledForReplayOnlyCapture()
    {
        Assert.Equal(checked((int)ObserverTimeline.MinimumBaselineRetention.Ticks),
            new ObserverTimeline(new ObserverOptions(4, 0)).RetentionTicks);
        Assert.Equal(1920, new ObserverTimeline(new ObserverOptions(4, 30)).RetentionTicks);
        Assert.Equal(0, new ObserverTimeline(new ObserverOptions(0, 30)).RetentionTicks);

        var disabled = new ObserverTimeline(new ObserverOptions(0));
        disabled.BeginMatch(1);
        disabled.Snapshot(new byte[] { 1 }); disabled.WorldBatch(new byte[] { 2 }); disabled.Roster(new byte[] { 3 }, 1);
        disabled.Commit(1, 1, MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS"));
        Assert.Equal(0, disabled.Count);
        Assert.Null(disabled.Baseline(new(1), SimDuration.Zero));
    }

    [Fact]
    public void ReplayOnlyFrameCaptureDoesNotRetainSpectatorHistory()
    {
        using var transport = new NetTransport(0);
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS");
        var network = new ServerNetwork(transport, rules, observers: new ObserverOptions(0));
        ObserverFrame? captured = null;
        network.ObserverFrameCaptured = frame => captured = frame;
        network.CaptureObserverSnapshot(new byte[] { 1 });
        network.CaptureObserverWorld(new byte[] { 2 });
        network.CommitObserverTick(1);

        Assert.True(network.FrameCaptureRequired);
        Assert.False(network.SpectatorHistoryRequired);
        Assert.NotNull(captured);
        Assert.Equal(0, network.ObserverHistoryFrameCount);
        Assert.Equal(0, network.ObserverHistoryBytes);
    }

    [Fact]
    public void LateReplayCaptureDoesNotSeedPreviousMatchRoster()
    {
        using var transport = new NetTransport(0);
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS");
        var network = new ServerNetwork(transport, rules, matchId: 1,
            observers: new ObserverOptions(0));

        // Publish the empty match-one roster, then rotate before replay is
        // enabled. The callback setter must not copy that stale payload into
        // match two's first frame.
        network.Poll(1);
        network.ChangeMatch(2, rules, 2);

        ObserverFrame? captured = null;
        network.ObserverFrameCaptured = frame => captured = frame;
        network.CaptureObserverSnapshot(new byte[] { 1 });
        network.CaptureObserverWorld(new byte[] { 2 });
        network.CommitObserverTick(2);

        Assert.NotNull(captured);
        Assert.Null(captured!.Roster);

        // The new match roster is published on the next network poll and is
        // then eligible for the first complete replay frame.
        network.Poll(2);
        network.CaptureObserverSnapshot(new byte[] { 3 });
        network.CaptureObserverWorld(new byte[] { 4 });
        network.CommitObserverTick(3);

        Assert.NotNull(captured!.Roster);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(captured.Roster));
    }

    [Fact]
    public void OversizedSpectatorFrameClearsHistoryAfterReplayCapture()
    {
        using var transport = new NetTransport(0);
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS");
        var network = new ServerNetwork(transport, rules, observers: new ObserverOptions(4, 0));
        network.Poll(1);

        ObserverFrame? captured = null;
        network.ObserverFrameCaptured = frame => captured = frame;
        network.CaptureObserverSnapshot(new byte[] { 1 });
        network.CaptureObserverWorld(new byte[] { 2 });
        network.CommitObserverTick(1);
        Assert.Equal(1, network.ObserverHistoryFrameCount);

        // Observer history cannot retain this frame, but the replay sink must
        // still receive it before the spectator ring is detached.
        network.CaptureObserverSnapshot(new byte[ObserverTimeline.MaxBytes]);
        network.CommitObserverTick(2);

        Assert.NotNull(captured);
        Assert.Equal(2u, captured!.Tick);
        Assert.Equal(0, network.ObserverHistoryFrameCount);
        Assert.Equal(0, network.ObserverHistoryBytes);
    }
}
