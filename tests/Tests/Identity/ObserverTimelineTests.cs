using System;
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
        Assert.Equal(53, bytes.Length); Assert.True(JoinPacket.TryRead(bytes, out var parsed)); Assert.True(parsed.Observer);
        bytes[JoinPacket.Size] = 2; Assert.False(JoinPacket.TryRead(bytes, out _));
        bytes[JoinPacket.Size] = 0; Assert.False(JoinPacket.TryRead(bytes, out _));
        bytes[JoinPacket.Size] = 1; bytes[JoinPacket.Size + 1] = 1; Assert.False(JoinPacket.TryRead(bytes, out _));
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
        Assert.Null(history.Baseline(69, 60));
        var old = history.Baseline(70, 60)!;
        Assert.Equal(1, old.Value.Snapshot![0]);
        history.BeginMatch(2); history.Commit(71, 2, rules);
        Assert.Same(old, history.Baseline(100, 60));
        Assert.Equal(1u, history.Baseline(100, 0)!.Value.MatchId);
    }
    [Fact]
    public void RetentionEvictsCursorsAndHandlesTickWrap()
    {
        var history = new ObserverTimeline();
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS");
        history.BeginMatch(1); history.Snapshot(new byte[] { 1 }); history.WorldBatch(new byte[] { 2 }); history.Roster(new byte[] { 3 }, 1);
        history.Commit(0, 1, rules); var cursor = history.Baseline(0, 0)!;
        for (uint tick = 1; tick <= 3602; tick++) history.Commit(tick, 1, rules);
        Assert.False(history.Owns(cursor)); Assert.True(history.Count <= 3601);
        Assert.True(history.RetainedBytes <= ObserverTimeline.MaxBytes);
        Assert.True(ObserverTimeline.Due(20, UInt32.MaxValue - 39, 60));
        Assert.False(ObserverTimeline.Due(20, 21, 0));
    }
}
