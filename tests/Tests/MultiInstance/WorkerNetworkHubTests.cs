using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class WorkerNetworkHubTests
{
    private static readonly IPEndPoint Endpoint = new(IPAddress.Loopback, 50000);

    [Fact]
    public void NetworkWakeIsPublishedAfterPhysicalAndOutboundTransitions()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        Assert.False(physical.AutoPongEnabled);
        using var match = hub.RegisterMatch(1, queueCapacity: 4, drainBudget: 4, criticalReserve: 0);
        ulong id = match.AllocateConnectionId();
        int wakeups = 0;
        hub.SetNetworkWake(() => wakeups++);

        physical.Enqueue(new(Endpoint, new byte[] { 1 }, 1));
        Assert.Equal(1, wakeups);
        Assert.True(hub.HasReadyNetworkWork);
        hub.Pump();
        Assert.False(hub.HasReadyNetworkWork);

        match.SendDatagram(Endpoint, Packet(NetMessageType.Ack, id));
        Assert.Equal(2, wakeups);
        Assert.True(hub.HasReadyNetworkWork);
    }

    [Fact]
    public void PumpReportsResidualReadyWorkForImmediateRepumpOnly()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter(),
            maximumDatagramsPerPump: 1);
        using var match = hub.RegisterMatch(1, queueCapacity: 4, drainBudget: 4, criticalReserve: 0);
        ulong id = match.AllocateConnectionId();
        match.SendDatagram(Endpoint, Packet(NetMessageType.Ack, id, 1));
        match.SendDatagram(Endpoint, Packet(NetMessageType.Ack, id, 2));

        WorkerNetworkPumpResult first = hub.PumpOnce();
        Assert.True(first.FlushBudgetExhausted);
        Assert.True(first.CanImmediateRepump);
        Assert.True(hub.HasReadyNetworkWork);

        WorkerNetworkPumpResult second = hub.PumpOnce();
        Assert.False(second.CanImmediateRepump);
        Assert.False(hub.HasReadyNetworkWork);
        Assert.Equal(2, physical.Sent.Count);
    }

    [Fact]
    public void NetworkAgeSamplesUseArrivalAndEnqueueDeltas()
    {
        var physical = new MemoryTransport();
        var router = new TestRouter();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), router);
        using var match = hub.RegisterMatch(1, queueCapacity: 4, drainBudget: 4, criticalReserve: 0);
        ulong id = match.AllocateConnectionId();
        router.Route = new(1, id, false);
        long arrival = Stopwatch.GetTimestamp() - Stopwatch.Frequency / 1000;
        ReceivedPacket captured = new(Endpoint, new byte[] { 1 }, 1, arrival);
        Assert.Equal(arrival, captured.ReceivedAt);
        physical.Incoming.Enqueue(captured);

        hub.Pump();
        match.SendDatagram(Endpoint, Packet(NetMessageType.Ack, id));
        hub.Pump();

        WorkerNetworkLoopSnapshot snapshot = hub.NetworkLoopDiagnostics;
        Assert.Equal(1, snapshot.ReceiveToRouteAgeMilliseconds.TotalCount);
        Assert.InRange(snapshot.ReceiveToRouteAgeMilliseconds.P50, 0, 100);
        Assert.Equal(1, snapshot.OutboundEnqueueToSendAgeMilliseconds.TotalCount);
        Assert.InRange(snapshot.OutboundEnqueueToSendAgeMilliseconds.P50, 0, 100);
    }

    [Fact]
    public void KeepAliveDeadlineBecomesReadyAndIsAdvancedAfterFlush()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter(),
            maximumDatagramsPerPump: 1);
        using var match = hub.RegisterMatch(1, queueCapacity: 4, drainBudget: 4, criticalReserve: 0);
        ulong id = match.AllocateConnectionId();
        byte[] keepAlive = new byte[NetHeader.Size];
        new NetHeader(NetMessageType.KeepAlive, NetHeaderFlags.Unsequenced, id, 0, 0, 0).Write(keepAlive);
        match.SetKeepAlive(Endpoint, keepAlive);

        long now = Stopwatch.GetTimestamp();
        Assert.True(hub.HasReadyNetworkWork);
        Assert.InRange(hub.NextNetworkDeadlineTimestamp, now, now + Stopwatch.Frequency);
        WorkerNetworkPumpResult result = hub.PumpOnce();
        Assert.False(result.CanImmediateRepump);
        Assert.False(hub.HasReadyNetworkWork);
        Assert.True(hub.NextNetworkDeadlineTimestamp > Stopwatch.GetTimestamp());
    }

    [Fact]
    public void RoutesAreBoundedIsolatedAndRemovedWithoutClosingSocket()
    {
        var physical = new MemoryTransport(); var router = new TestRouter();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), router);
        using var first = hub.RegisterMatch(1, 2, 2); using var second = hub.RegisterMatch(2, 2, 2);
        ulong firstId = first.AllocateConnectionId(), secondId = second.AllocateConnectionId();
        Assert.NotEqual(firstId, secondId);
        router.Route = new(1, firstId, false);
        for (int i = 0; i < 3; i++) physical.Incoming.Enqueue(new(Endpoint, new byte[] { 1 }, 1));
        hub.Pump(); Assert.Equal(2, first.QueuedPackets); Assert.Equal(0, second.QueuedPackets); Assert.Equal(1, first.PacketsDropped);
        router.Route = new(2, firstId, false); physical.Incoming.Enqueue(new(Endpoint, new byte[] { 2 }, 1));
        hub.Pump(); Assert.Equal(1, hub.Metrics.PacketsRejected); Assert.Equal(0, second.QueuedPackets);
        first.Dispose(); Assert.False(physical.Disposed);
        router.Route = new(0, firstId, false); physical.Incoming.Enqueue(new(Endpoint, new byte[] { 3 }, 1));
        hub.Pump(); Assert.Equal(2, hub.Metrics.PacketsRejected);
        Assert.Throws<InvalidOperationException>(() => first.AllocateConnectionId());
        router.Route = new(2, secondId, false); physical.Incoming.Enqueue(new(Endpoint, new byte[] { 4 }, 1));
        hub.Pump(); Assert.Equal(4, Assert.Single(second.Drain()).Data[0]);
        second.RemoveConnection(secondId); Assert.NotEqual(secondId, second.AllocateConnectionId());
    }

    [Fact]
    public void QueuePressureEvictsUpdatesForReliableControlAndOwnsBytes()
    {
        var physical = new MemoryTransport(); using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        using var match = hub.RegisterMatch(1, 2, 2); ulong id = match.AllocateConnectionId();
        byte[] update = Packet(NetMessageType.Snapshot, id);
        match.SendDatagram(Endpoint, update); match.SendDatagram(Endpoint, update);
        byte[] control = Packet(NetMessageType.Ack, id); match.SendDatagram(Endpoint, control); control[2] = 255;
        Assert.Equal(2, match.HeldOutgoingPackets); Assert.Equal(0, match.PacketsDropped);
        Assert.Equal(2, match.UpdateEnqueues); Assert.Equal(0, match.UpdateDrops);
        Assert.Equal(1, match.SnapshotsSuperseded);
        Assert.Equal(1, match.ControlEnqueues); Assert.Equal(0, match.ControlDrops);
        Assert.Equal(2, match.OutboundQueueHighWater);
        hub.Pump();
        Assert.Equal(NetMessageType.Ack, ReadHeader(physical.Sent[0]).Type);
        Assert.Equal(NetMessageType.Snapshot, ReadHeader(physical.Sent[1]).Type);
        Assert.Equal(2, match.Metrics.PacketsSent);
        Assert.Equal(1, match.PacketsSent(MatchTrafficClass.CriticalReliable));
        Assert.Equal(1, match.PacketsSent(MatchTrafficClass.Snapshot));
        Assert.Equal(1, match.FlushCount);
        Assert.Equal(1, match.FlushDurationPercentiles.Count);
        Assert.Equal(2, hub.Metrics.QueueHighWater);
        Assert.Equal(1, hub.PumpDurationPercentiles.Count);
    }

    [Fact]
    public void EvictableProductionCarriersDoNotClaimDurableSubmission()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        using var match = hub.RegisterMatch(1, queueCapacity: 2, drainBudget: 2, criticalReserve: 0);
        ulong id = match.AllocateConnectionId();

        Assert.False(match.TrySendDatagram(Endpoint, Packet(NetMessageType.Snapshot, id), NetDeliveryClass.State));
        Assert.False(match.TrySendDatagram(Endpoint, Packet(NetMessageType.Debug, id, 2), NetDeliveryClass.BestEffort));
        Assert.Equal(2, match.HeldOutgoingPackets);

        // Critical admission evicts update/best-effort slots; the accepted
        // result is only durable for the non-evictable reliable class.
        Assert.True(match.TrySendDatagram(Endpoint, Packet(NetMessageType.Ack, id, 3), NetDeliveryClass.Critical));
        Assert.Equal(1, match.ClassPacketsDropped(MatchTrafficClass.Snapshot)
            + match.ClassPacketsDropped(MatchTrafficClass.BestEffort));
        Assert.Equal(2, match.HeldOutgoingPackets);
    }

    [Fact]
    public void CriticalReserveRemainsUsableWhenOrdinaryCapacityIsFull()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter(),
            maximumDatagramsPerPump: 4);
        using var match = hub.RegisterMatch(1, queueCapacity: 4, drainBudget: 4,
            criticalReserve: 1);
        ulong id = match.AllocateConnectionId();

        for (uint sequence = 1; sequence <= 3; sequence++)
            match.SendDatagram(Endpoint, Packet(NetMessageType.Event, id, sequence));
        Assert.Equal(3, match.HeldOutgoingPackets);
        Assert.Equal(0, match.CriticalReserveInUse);

        match.SendDatagram(Endpoint, Packet(NetMessageType.Ack, id, 4));
        Assert.Equal(4, match.HeldOutgoingPackets);
        Assert.Equal(1, match.CriticalReserveInUse);
        Assert.Equal(1, match.CriticalReserveUses);
        Assert.Equal(1, match.CriticalReserveHighWater);

        // Ordinary reliable traffic cannot consume the reserved slot, and a
        // critical packet cannot evict normal reliable traffic to do so.
        match.SendDatagram(Endpoint, Packet(NetMessageType.Event, id, 5));
        Assert.Equal(4, match.HeldOutgoingPackets);
        Assert.Equal(1, match.ClassPacketsDropped(MatchTrafficClass.NormalReliable));

        match.SendDatagram(Endpoint, Packet(NetMessageType.Ack, id, 6));
        Assert.Equal(4, match.HeldOutgoingPackets);
        Assert.Equal(1, match.CriticalTransportDrops);
        Assert.Equal(1, match.CriticalReserveExhaustions);

        hub.Pump();
        Assert.Equal(4, physical.Sent.Count);
        Assert.Equal(0, match.CriticalReserveInUse);
        Assert.True(match.MaximumCriticalReserveQueueAgeMilliseconds >= 0);
        Assert.True(match.OutboundQueueHighWater >= 4);
    }

    [Fact]
    public void CriticalSurvivesSnapshotFloodAndSnapshotsStillCoalesce()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter(),
            maximumDatagramsPerPump: 4);
        using var match = hub.RegisterMatch(1, queueCapacity: 4, drainBudget: 4,
            maxConnections: 4, criticalReserve: 1);
        ulong first = match.AllocateConnectionId();
        ulong second = match.AllocateConnectionId();
        ulong third = match.AllocateConnectionId();
        ulong fourth = match.AllocateConnectionId();

        match.SendDatagram(Endpoint, Packet(NetMessageType.Snapshot, first, 1));
        match.SendDatagram(Endpoint, Packet(NetMessageType.Snapshot, second, 2));
        match.SendDatagram(Endpoint, Packet(NetMessageType.Snapshot, third, 3));
        match.SendDatagram(Endpoint, Packet(NetMessageType.Ack, first, 4));
        Assert.Equal(4, match.HeldOutgoingPackets);
        Assert.Equal(1, match.CriticalReserveInUse);

        match.SendDatagram(Endpoint, Packet(NetMessageType.Snapshot, fourth, 5));
        Assert.Equal(1, match.ClassPacketsDropped(MatchTrafficClass.Snapshot));
        Assert.Equal(4, match.HeldOutgoingPackets);

        // Replacing an already queued snapshot does not consume another slot
        // or disturb the reserved critical packet.
        match.SendDatagram(Endpoint, Packet(NetMessageType.Snapshot, first, 6));
        Assert.Equal(4, match.HeldOutgoingPackets);
        Assert.Equal(1, match.SnapshotsSuperseded);
        hub.Pump();

        Assert.Contains(physical.Sent, bytes => ReadHeader(bytes).Type == NetMessageType.Ack);
        Assert.Contains(physical.Sent, bytes => ReadHeader(bytes).Type == NetMessageType.Snapshot
            && ReadHeader(bytes).Sequence == 6);
        Assert.Equal(4, physical.Sent.Count);
    }

    [Fact]
    public void WorkerSendBudgetServicesMatchesRoundRobinAndCountsFailedAttempts()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter(),
            matchLimit: 3, maximumDatagramsPerPump: 3);
        using var first = hub.RegisterMatch(1, queueCapacity: 8, drainBudget: 8, criticalReserve: 0);
        using var second = hub.RegisterMatch(2, queueCapacity: 8, drainBudget: 8, criticalReserve: 0);
        using var third = hub.RegisterMatch(3, queueCapacity: 8, drainBudget: 8, criticalReserve: 0);
        ulong firstId = first.AllocateConnectionId();
        ulong secondId = second.AllocateConnectionId();
        ulong thirdId = third.AllocateConnectionId();
        foreach (MatchDatagramTransport match in new[] { first, second, third })
        {
            ulong id = match == first ? firstId : match == second ? secondId : thirdId;
            match.SendDatagram(Endpoint, Packet(NetMessageType.Ack, id, 1));
            match.SendDatagram(Endpoint, Packet(NetMessageType.Ack, id, 2));
        }

        hub.Pump();
        Assert.Equal(3, physical.Sent.Count);
        Assert.Equal(new ulong[] { firstId, secondId, thirdId },
            physical.Sent.Select(bytes => ReadHeader(bytes).ConnectionId));
        Assert.Equal(3, hub.DatagramsAttempted);
        Assert.Equal(1, hub.DatagramBudgetExhaustions);

        hub.Pump();
        Assert.Equal(6, physical.Sent.Count);
        Assert.Equal(new ulong[] { firstId, secondId, thirdId },
            physical.Sent.Skip(3).Select(bytes => ReadHeader(bytes).ConnectionId));
        Assert.Equal(6, hub.DatagramsAttempted);

        physical.FailSends = true;
        first.SendDatagram(Endpoint, Packet(NetMessageType.Ack, firstId, 3));
        hub.Pump();
        Assert.Equal(7, hub.DatagramsAttempted);
        Assert.Equal(1, hub.Metrics.SendErrors);
        Assert.Equal(3L, first.FlushDatagramsAttempted);
    }

    [Fact]
    public void KeepAlivesConsumeTheWorkerBudgetAsDatagrams()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter(),
            maximumDatagramsPerPump: 1);
        using var match = hub.RegisterMatch(1, queueCapacity: 4, drainBudget: 4, criticalReserve: 0);
        ulong id = match.AllocateConnectionId();
        byte[] keepAlive = new byte[NetHeader.Size];
        new NetHeader(NetMessageType.KeepAlive, NetHeaderFlags.Unsequenced, id, 0, 0, 0).Write(keepAlive);
        match.SetKeepAlive(Endpoint, keepAlive);

        hub.Pump();

        Assert.Single(physical.Sent);
        Assert.Equal(NetMessageType.KeepAlive, ReadHeader(physical.Sent[0]).Type);
        Assert.Equal(1, hub.DatagramsAttempted);
        Assert.Equal(1, hub.DatagramBudgetExhaustions);
    }

    [Fact]
    public void DisablingGlobalBudgetRestoresIndependentMatchFlushBudgets()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter(),
            matchLimit: 2, maximumDatagramsPerPump: 1,
            workerGlobalNetworkBudgetEnabled: false);
        using var first = hub.RegisterMatch(1, queueCapacity: 4, drainBudget: 2, criticalReserve: 0);
        using var second = hub.RegisterMatch(2, queueCapacity: 4, drainBudget: 2, criticalReserve: 0);
        ulong firstId = first.AllocateConnectionId();
        ulong secondId = second.AllocateConnectionId();
        first.SendDatagram(Endpoint, Packet(NetMessageType.Ack, firstId, 1));
        first.SendDatagram(Endpoint, Packet(NetMessageType.Ack, firstId, 2));
        second.SendDatagram(Endpoint, Packet(NetMessageType.Ack, secondId, 1));
        second.SendDatagram(Endpoint, Packet(NetMessageType.Ack, secondId, 2));

        hub.Pump();

        Assert.Equal(4, physical.Sent.Count);
        Assert.Equal(4, hub.DatagramsAttempted);
        Assert.Equal(0, hub.DatagramBudgetExhaustions);
    }

    [Fact]
    public void SnapshotQueueKeepsOnlyTheNewestUnsentStatePerConnection()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        using var match = hub.RegisterMatch(1, 4, 4);
        ulong id = match.AllocateConnectionId();

        match.SendDatagram(Endpoint, Packet(NetMessageType.Snapshot, id, sequence: 10));
        match.SendDatagram(Endpoint, Packet(NetMessageType.Snapshot, id, sequence: 11));
        match.SendDatagram(Endpoint, Packet(NetMessageType.Snapshot, id, sequence: 12));

        Assert.Equal(1, match.HeldOutgoingPackets);
        Assert.Equal(2, match.SnapshotsSuperseded);
        hub.Pump();
        NetHeader sent = ReadHeader(Assert.Single(physical.Sent));
        Assert.Equal(12u, sent.Sequence);
        Assert.Equal(0, match.HeldOutgoingPackets);
    }

    [Fact]
    public void WeightedSchedulingGivesSnapshotAndWorldTrafficBoundedOpportunities()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        using var match = hub.RegisterMatch(1, 32, 8);
        ulong id = match.AllocateConnectionId();
        for (uint sequence = 1; sequence <= 20; sequence++)
            match.SendDatagram(Endpoint, Packet(NetMessageType.Ack, id, sequence));
        match.SendDatagram(Endpoint, Packet(NetMessageType.Snapshot, id, sequence: 100));
        match.SendDatagram(Endpoint, Packet(NetMessageType.World, id, sequence: 200));

        hub.Pump();

        Assert.Equal(8, physical.Sent.Count);
        Assert.Contains(physical.Sent, bytes => ReadHeader(bytes).Type == NetMessageType.Snapshot);
        Assert.Contains(physical.Sent, bytes => ReadHeader(bytes).Type == NetMessageType.World);
        Assert.True(match.HeldOutgoingPackets > 0);
        Assert.Equal(1, match.PacketsSent(MatchTrafficClass.Snapshot));
        Assert.Equal(1, match.PacketsSent(MatchTrafficClass.World));
    }

    [Fact]
    public void WorldBatchesRemainFifoAndAreNeverSnapshotCoalesced()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        using var match = hub.RegisterMatch(1, 8, 8);
        ulong id = match.AllocateConnectionId();
        match.SendDatagram(Endpoint, Packet(NetMessageType.World, id, sequence: 30));
        match.SendDatagram(Endpoint, Packet(NetMessageType.World, id, sequence: 31));
        match.SendDatagram(Endpoint, Packet(NetMessageType.World, id, sequence: 32));

        Assert.Equal(3, match.HeldOutgoingPackets);
        hub.Pump();
        Assert.Equal(new uint[] { 30, 31, 32 },
            physical.Sent.Select(bytes => ReadHeader(bytes).Sequence));
    }

    [Fact]
    public void ReplacingAQueuedSnapshotAllocatesNoManagedMemoryAfterWarmup()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        using var match = hub.RegisterMatch(1, 4, 4);
        ulong id = match.AllocateConnectionId();
        byte[] snapshot = Packet(NetMessageType.Snapshot, id);
        match.SendDatagram(Endpoint, snapshot);
        for (int i = 0; i < 100; i++) match.SendDatagram(Endpoint, snapshot);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) match.SendDatagram(Endpoint, snapshot);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(1, match.HeldOutgoingPackets);
    }

    [Fact]
    public void QueueV2RollbackDisablesSnapshotSuperseding()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        using var match = hub.RegisterMatch(1, 4, 4, queueV2Enabled: false);
        ulong id = match.AllocateConnectionId();
        match.SendDatagram(Endpoint, Packet(NetMessageType.Snapshot, id, 10));
        match.SendDatagram(Endpoint, Packet(NetMessageType.Snapshot, id, 11));

        Assert.Equal(2, match.HeldOutgoingPackets);
        Assert.Equal(0, match.SnapshotsSuperseded);
        hub.Pump();
        Assert.Equal(new uint[] { 10, 11 }, physical.Sent.Select(ReadHeader).Select(header => header.Sequence));
    }

    [Fact]
    public void HubPumpAndEmptyMatchFlushAllocateNoManagedMemoryAfterWarmup()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        using var match = hub.RegisterMatch(1, 4, 4);
        for (int i = 0; i < 100; i++) hub.Pump();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) hub.Pump();

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public async Task PhysicalSendRunsOutsideTheMatchQueueLock()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        using var match = hub.RegisterMatch(1, 4, 4);
        ulong id = match.AllocateConnectionId();
        int reentered = 0;
        physical.Sending = () =>
        {
            if (Interlocked.Exchange(ref reentered, 1) == 0)
                match.SendDatagram(Endpoint, Packet(NetMessageType.Debug, id, 2));
        };
        match.SendDatagram(Endpoint, Packet(NetMessageType.Ack, id));

        Task pump = Task.Run(hub.Pump);

        await pump.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, physical.Sent.Count);
        Assert.Equal(0, match.HeldOutgoingPackets);
    }

    [Fact]
    public async Task MatchCloseDuringPhysicalSendDoesNotDeadlockOrRepublishRoute()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        MatchDatagramTransport match = hub.RegisterMatch(1, 4, 4);
        ulong id = match.AllocateConnectionId();
        physical.Sending = match.Dispose;
        match.SendDatagram(Endpoint, Packet(NetMessageType.Ack, id));

        Task pump = Task.Run(hub.Pump);
        await pump.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(physical.Disposed is false);
        Assert.Throws<InvalidOperationException>(() => match.AllocateConnectionId());
        match.Dispose();
    }

    [Fact]
    public void SingleReaderAndConcurrentProducersStayBounded()
    {
        var physical = new MemoryTransport(); using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter { Route = new(1, 0, true) });
        using var match = hub.RegisterMatch(1, 8, 8);
        byte[] control = Packet(NetMessageType.Ack, 1);
        Parallel.For(0, 1000, i => match.SendDatagram(Endpoint, control));
        Assert.Equal(8, match.HeldOutgoingPackets); Assert.Equal(992, match.PacketsDropped);
        physical.Incoming.Enqueue(new(Endpoint, new byte[] { 1 }, 1)); physical.Incoming.Enqueue(new(Endpoint, new byte[] { 2 }, 1)); hub.Pump();
        using var reader = match.Drain().GetEnumerator(); Assert.True(reader.MoveNext());
        Assert.Throws<InvalidOperationException>(() => match.Drain().ToArray());
    }

    [Fact]
    public void LegacyJoinIsExplicitlySingleMatchAndReliableWelcomeCompletesThroughVirtualTransport()
    {
        using var physical = new NetTransport(0);
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new LegacySingleMatchRouter(1));
        using var match = hub.RegisterMatch(1);
        Assert.Throws<InvalidOperationException>(() => hub.RegisterMatch(2));
        var server = new ServerNetwork(match, "MP1 SANCTORUS", GameMode.Battle);
        using var clientTransport = new NetTransport(0);
        using var client = new NetClient(clientTransport, new(IPAddress.Loopback, hub.LocalPort), "virtual", Hunter.Samus);
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < 5 && client.Connection == null)
        { client.Poll(); hub.Pump(); server.Poll(1); hub.Pump(); Thread.Sleep(1); }
        Assert.NotNull(client.Connection); Assert.Equal(1, server.Count);
        Assert.True(client.Ready(client.Accepted.MatchId));
        while (timer.Elapsed.TotalSeconds < 5 && server.Peers[0]!.Connection.State != NetConnectionState.Ready)
        { client.Poll(); hub.Pump(); server.Poll(2); hub.Pump(); Thread.Sleep(1); }
        Assert.Equal(NetConnectionState.Ready, server.Peers[0]!.Connection.State);
        ulong id = client.Connection!.Id; server.Remove(0); Assert.Null(server.Find(id));
        Assert.True(match.Metrics.PacketsReceived > 0); Assert.True(match.Metrics.PacketsSent > 0);
    }

    [Fact]
    public void ReliableRetransmissionPreservesPayloadAfterOutboundDrop()
    {
        var physical = new MemoryTransport(); using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        using var match = hub.RegisterMatch(1, 1, 1); ulong id = match.AllocateConnectionId();
        var connection = new NetConnection(id, Endpoint, 1, 0);
        connection.Reliable.TryEnqueue(ReliableEventType.Disconnect, ReadOnlySpan<byte>.Empty, out uint eventId);
        match.SendDatagram(Endpoint, Packet(NetMessageType.Ack, id)); // Occupy the one control slot.
        connection.FlushReliable(match, 0); hub.Pump(); Assert.Equal(1, match.PacketsDropped);
        connection.FlushReliable(match, 10); hub.Pump();
        Assert.True(ReliableEventPacket.TryRead(physical.Sent[^1].AsSpan(NetHeader.Size), out uint resentId, out var type, out _));
        Assert.Equal(eventId, resentId); Assert.Equal(ReliableEventType.Disconnect, type);
        NetHeader sent = ReadHeader(physical.Sent[^1]);
        connection.TryReceive(new NetHeader(NetMessageType.Ack, NetHeaderFlags.HasAck, id, 1, sent.Sequence, 0), Endpoint, 11, out _);
        Assert.Equal(0, connection.Reliable.PendingCount);
    }

    [Fact]
    public void ActualRoutedJoinFramingPreservesFullWidthIdsAndRejectsUnknownOrLegacyRoutes()
    {
        var physical = new MemoryTransport(); using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new RoutedMatchDatagramRouter());
        using var first = hub.RegisterMatch(70000); using var second = hub.RegisterMatch(80000);
        foreach (uint wire in new uint[] { 70000, 80000, 90000, 0 })
        {
            var join = new JoinPacket(NetHeader.Version, 42, Hunter.Samus, "routed", WireMatchId: wire);
            byte[] bytes = new byte[NetHeader.Size + join.EncodedSize];
            new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(bytes);
            join.Write(bytes.AsSpan(NetHeader.Size)); physical.Incoming.Enqueue(new(Endpoint, bytes, bytes.Length));
        }
        hub.Pump();
        Assert.Single(first.Drain()); Assert.Single(second.Drain()); Assert.Equal(2, hub.Metrics.PacketsRejected);
    }

    [Fact]
    public void AdmissionRoutesArePerMatchBoundedAndExpiredLeasesCanBeReplaced()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new RoutedMatchDatagramRouter(),
            matchLimit: 1, udpAuthenticationEnabled: true);
        using var match = hub.RegisterMatch(1);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (int i = 0; i < WorkerNetworkHub.AdmissionRouteLimitPerMatch; i++)
            Assert.True(match.RegisterAdmissionId(Guid.NewGuid(), now + 1, out bool created) && created);
        Assert.False(match.RegisterAdmissionId(Guid.NewGuid(), now + 1, out _));

        Thread.Sleep(1200);
        Assert.True(match.RegisterAdmissionId(Guid.NewGuid(), DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60,
            out bool replacementCreated) && replacementCreated);
    }

    [Fact]
    public void AutoClassificationUsesTheCanonicalReliablePolicyAndDropsMalformedEvents()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        using var match = hub.RegisterMatch(1, queueCapacity: 32, drainBudget: 32,
            criticalReserve: 4);
        ulong id = match.AllocateConnectionId();
        ReliableEventType[] critical =
        {
            ReliableEventType.Welcome, ReliableEventType.ClientReady,
            ReliableEventType.MapTransition, ReliableEventType.Disconnect,
            ReliableEventType.MatchState, ReliableEventType.Kill,
            ReliableEventType.WorldEvent, ReliableEventType.ObserverTransition,
            ReliableEventType.IntermissionBallot, ReliableEventType.TimingProfile,
            ReliableEventType.TimingProfileApplied
        };
        for (uint i = 0; i < critical.Length; i++)
            match.SendDatagram(Endpoint, SignedEvent(id, i + 1, critical[i]));
        match.SendDatagram(Endpoint, SignedEvent(id, 100, ReliableEventType.Combat));
        match.SendDatagram(Endpoint, SignedEvent(id, 101, ReliableEventType.Chat));

        byte[] malformed = new byte[NetHeader.Size];
        new NetHeader(NetMessageType.Event, NetHeaderFlags.None, id, 200, 0, 0).Write(malformed);
        match.SendDatagram(Endpoint, malformed);

        Assert.Equal(critical.Length + 2, match.HeldOutgoingPackets);
        Assert.Equal(1, match.Metrics.PacketsRejected);
        hub.Pump();
        Assert.Equal(critical.Length, match.PacketsSent(MatchTrafficClass.CriticalReliable));
        Assert.Equal(2, match.PacketsSent(MatchTrafficClass.NormalReliable));
    }

    [Fact]
    public void SignedCriticalAndReliableTrafficRespectReserveSaturation()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        using var match = hub.RegisterMatch(1, queueCapacity: 4, drainBudget: 4,
            criticalReserve: 1);
        ulong id = match.AllocateConnectionId();
        match.SendDatagram(Endpoint, SignedEvent(id, 1, ReliableEventType.Combat));
        match.SendDatagram(Endpoint, SignedEvent(id, 2, ReliableEventType.Chat));
        match.SendDatagram(Endpoint, SignedEvent(id, 3, ReliableEventType.Combat));
        match.SendDatagram(Endpoint, SignedEvent(id, 4, ReliableEventType.Kill));
        Assert.Equal(4, match.HeldOutgoingPackets);
        Assert.Equal(1, match.CriticalReserveInUse);

        match.SendDatagram(Endpoint, SignedEvent(id, 5, ReliableEventType.Combat));
        Assert.Equal(1, match.ClassPacketsDropped(MatchTrafficClass.NormalReliable));
        match.SendDatagram(Endpoint, SignedEvent(id, 6, ReliableEventType.Kill));
        Assert.Equal(1, match.CriticalTransportDrops);
        Assert.Equal(1, match.CriticalReserveExhaustions);
    }

    [Fact]
    public void StateHintDoesNotCoalesceInputDatagrams()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        using var match = hub.RegisterMatch(1, queueCapacity: 8, drainBudget: 8);
        ulong id = match.AllocateConnectionId();
        match.SendDatagram(Endpoint, Packet(NetMessageType.Input, id, 1),
            NetDeliveryClass.State);
        match.SendDatagram(Endpoint, Packet(NetMessageType.Input, id, 2),
            NetDeliveryClass.State);

        Assert.Equal(2, match.HeldOutgoingPackets);
        Assert.Equal(0, match.SnapshotsSuperseded);
    }

    [Fact]
    public void PartialDrainDoesNotRemoveAQueuedSnapshotIndexForAnInputSlot()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter(),
            maximumDatagramsPerPump: 1);
        using var match = hub.RegisterMatch(1, queueCapacity: 8, drainBudget: 8);
        ulong id = match.AllocateConnectionId();

        // The schedule takes the Input slot first. A later Snapshot must still
        // find and supersede the snapshot slot that remains queued.
        match.SendDatagram(Endpoint, Packet(NetMessageType.Input, id, 1), NetDeliveryClass.State);
        match.SendDatagram(Endpoint, Packet(NetMessageType.Snapshot, id, 2));
        hub.Pump();
        Assert.Equal(1, match.HeldOutgoingPackets);

        match.SendDatagram(Endpoint, Packet(NetMessageType.Snapshot, id, 3));
        Assert.Equal(1, match.HeldOutgoingPackets);
        Assert.Equal(1, match.SnapshotsSuperseded);
    }

    [Fact]
    public void ExplicitDeliveryRejectsInvalidEnumAndMessageClassMismatch()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        using var match = hub.RegisterMatch(1, queueCapacity: 8, drainBudget: 8);
        ulong id = match.AllocateConnectionId();

        match.SendDatagram(Endpoint, Packet(NetMessageType.Ack, id),
            (NetDeliveryClass)255);
        match.SendDatagram(Endpoint, Packet(NetMessageType.Ack, id), NetDeliveryClass.State);

        Assert.Equal(0, match.HeldOutgoingPackets);
        Assert.Equal(2, match.Metrics.PacketsRejected);
    }

    [Fact]
    public async Task CriticalTransportDropsRemainMonotonicAcrossRetireAndHubDispose()
    {
        var physical = new MemoryTransport();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new TestRouter());
        using var first = hub.RegisterMatch(1, queueCapacity: 1, drainBudget: 1);
        ulong firstId = first.AllocateConnectionId();
        first.SendDatagram(Endpoint, Packet(NetMessageType.Ack, firstId));
        for (int i = 0; i < 64; i++)
            first.SendDatagram(Endpoint, Packet(NetMessageType.Ack, firstId, (uint)i + 2));
        long firstExpected = first.CriticalTransportDrops;
        Assert.True(firstExpected > 0);

        var samples = new ConcurrentQueue<long>();
        using var stop = new CancellationTokenSource();
        Task reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested) samples.Enqueue(hub.CriticalTransportDrops);
            samples.Enqueue(hub.CriticalTransportDrops);
        });
        first.Dispose();
        Assert.Equal(firstExpected, hub.CriticalTransportDrops);

        using var second = hub.RegisterMatch(2, queueCapacity: 1, drainBudget: 1);
        ulong secondId = second.AllocateConnectionId();
        second.SendDatagram(Endpoint, Packet(NetMessageType.Ack, secondId));
        for (int i = 0; i < 32; i++)
            second.SendDatagram(Endpoint, Packet(NetMessageType.Ack, secondId, (uint)i + 100));
        long expected = firstExpected + second.CriticalTransportDrops;
        hub.Dispose();
        stop.Cancel();
        await reader.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(expected, hub.CriticalTransportDrops);
        long previous = 0;
        foreach (long sample in samples)
        {
            Assert.True(sample >= previous);
            previous = sample;
        }
    }

    [Fact]
    public void EstablishedIngressHasBoundedPerRouteQueueAndReleasesReservations()
    {
        var physical = new MemoryTransport();
        var router = new TestRouter();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), router);
        using var match = hub.RegisterMatch(1, queueCapacity: 128, drainBudget: 128);
        ulong id = match.AllocateConnectionId();
        router.Route = new(1, id, false);
        for (int i = 0; i < 80; i++)
            physical.Incoming.Enqueue(new(Endpoint, new byte[] { 1 }, 1));

        hub.Pump();

        Assert.Equal(64, match.QueuedPackets);
        Assert.Equal(64, hub.MaximumConnectionIngressDepth);
        Assert.Equal(16, hub.PerConnectionQuotaDrops);
        int drained = match.Drain(Span<ReceivedPacket>.Empty);
        Assert.Equal(0, drained);
        int count = 0;
        foreach (ReceivedPacket _ in match.Drain()) count++;
        Assert.Equal(64, count);

        physical.Incoming.Enqueue(new(Endpoint, new byte[] { 2 }, 1));
        hub.Pump();
        Assert.Single(match.Drain());
    }

    [Fact]
    public void AbusiveEstablishedPeerCannotStarveHealthyPeerOnAnotherMatch()
    {
        var physical = new MemoryTransport();
        var router = new ConnectionRouter();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), router,
            matchLimit: 2, maximumDatagramsPerPump: 128);
        using var abusiveMatch = hub.RegisterMatch(1, queueCapacity: 128, drainBudget: 64);
        using var healthyMatch = hub.RegisterMatch(2, queueCapacity: 8, drainBudget: 8);
        ulong abusiveId = abusiveMatch.AllocateConnectionId();
        ulong healthyId = healthyMatch.AllocateConnectionId();
        router.Add(abusiveId, new(1, abusiveId, false));
        router.Add(healthyId, new(2, healthyId, false));

        // One established peer exhausts only its own bounded ingress
        // reservation. The healthy peer's packet must still reach its match
        // in the same deterministic pump; no timing window or sleep is used.
        for (uint sequence = 1; sequence <= 80; sequence++)
            physical.Incoming.Enqueue(new(Endpoint, Packet(NetMessageType.Ack, abusiveId, sequence), NetHeader.Size));
        physical.Incoming.Enqueue(new(Endpoint, Packet(NetMessageType.Ack, healthyId, 1), NetHeader.Size));

        hub.Pump();

        Assert.Equal(64, abusiveMatch.QueuedPackets);
        Assert.Single(healthyMatch.Drain());
        Assert.Equal(16, hub.PerConnectionQuotaDrops);
        Assert.Equal(0, hub.EstablishedIngressDrops);
    }

    [Fact]
    public void FailedMatchEnqueueRestoresHubDropAccountingAndReleasesRouteReservation()
    {
        var physical = new MemoryTransport();
        var router = new TestRouter();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), router);
        using var match = hub.RegisterMatch(1, queueCapacity: 1, drainBudget: 1);
        ulong id = match.AllocateConnectionId();
        router.Route = new(1, id, false);
        physical.Incoming.Enqueue(new(Endpoint, Packet(NetMessageType.Ack, id), NetHeader.Size));
        physical.Incoming.Enqueue(new(Endpoint, Packet(NetMessageType.Ack, id), NetHeader.Size));

        hub.Pump();

        Assert.Equal(1, match.QueuedPackets);
        Assert.Equal(1, match.Metrics.QueueDrops);
        Assert.Equal(1, hub.Metrics.QueueDrops);
        Assert.Single(match.Drain());
        physical.Incoming.Enqueue(new(Endpoint, Packet(NetMessageType.Ack, id), NetHeader.Size));
        hub.Pump();
        Assert.Single(match.Drain());
    }

    [Fact]
    public void JoinSourceLimiterDropsAbuseBeforeAdmissionRouting()
    {
        var physical = new MemoryTransport();
        var router = new TestRouter();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), router,
            udpAuthenticationEnabled: true);
        using var match = hub.RegisterMatch(1, queueCapacity: 128, drainBudget: 128);
        Guid admissionId = Guid.NewGuid();
        Assert.True(match.RegisterAdmissionId(admissionId,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60, out _));
        router.Route = new(1, 0, true, admissionId);
        for (int i = 0; i < 60; i++)
            physical.Incoming.Enqueue(new(Endpoint, new byte[] { 1 }, 1));

        hub.Pump();

        Assert.Equal(40, match.QueuedPackets);
        Assert.Equal(20, hub.AdmissionIngressDrops);
    }

    [Fact]
    public void GlobalRoutingBudgetBoundsUnknownFloodWithoutAllocatingRoutes()
    {
        var physical = new MemoryTransport(); using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), new RoutedMatchDatagramRouter());
        using var match = hub.RegisterMatch(1, maxConnections: 32);
        for (int i = 0; i < 1000; i++) physical.Incoming.Enqueue(new(Endpoint, Packet(NetMessageType.Ack, (ulong)(i + 1)), NetHeader.Size));
        hub.Pump(); Assert.Equal(WorkerNetworkHub.MaxRoutingAttemptsPerPump, hub.Metrics.PacketsRejected);
        Assert.Equal(1, hub.RoutingBudgetExhaustions); Assert.Equal(0, match.QueuedPackets);
        for (int i = 0; i < 32; i++) match.AllocateConnectionId();
        Assert.Throws<InvalidOperationException>(() => match.AllocateConnectionId());
    }

    private static byte[] Packet(NetMessageType type, ulong id, uint sequence = 1)
    {
        int bodyLength = type == NetMessageType.Event ? ReliableEventPacket.HeaderSize : 0;
        byte[] result = new byte[NetHeader.Size + bodyLength];
        new NetHeader(type, NetHeaderFlags.None, id, sequence, 0, 0).Write(result);
        if (type == NetMessageType.Event)
            ReliableEventPacket.Write(result.AsSpan(NetHeader.Size), 1,
                ReliableEventType.Combat, ReadOnlySpan<byte>.Empty);
        return result;
    }

    private static byte[] SignedEvent(ulong id, uint sequence, ReliableEventType type)
    {
        byte[] payload = new byte[ReliableEventPacket.HeaderSize];
        ReliableEventPacket.Write(payload, sequence, type, ReadOnlySpan<byte>.Empty);
        byte[] datagram = new byte[NetAuthentication.AuthenticatedSize(payload.Length)];
        NetAuthentication.Sign(new byte[NetAuthentication.KeySize], NetAuthDirection.ServerToClient,
            new NetHeader(NetMessageType.Event, NetHeaderFlags.None, id, sequence, 0, 0),
            payload, datagram);
        return datagram;
    }
    private static NetHeader ReadHeader(byte[] bytes) { Assert.True(NetHeader.TryRead(bytes, out var header)); return header; }
    private sealed class TestRouter : IWorkerDatagramRouter
    {
        public WorkerDatagramRoute Route;
        public bool TryRoute(ReadOnlySpan<byte> datagram, out WorkerDatagramRoute route) { route = Route; return true; }
    }
    private sealed class ConnectionRouter : IWorkerDatagramRouter
    {
        private readonly Dictionary<ulong, WorkerDatagramRoute> _routes = [];
        public void Add(ulong connectionId, WorkerDatagramRoute route) => _routes.Add(connectionId, route);
        public bool TryRoute(ReadOnlySpan<byte> datagram, out WorkerDatagramRoute route)
        {
            route = default;
            return NetHeader.TryRead(datagram, out NetHeader header)
                && _routes.TryGetValue(header.ConnectionId, out route);
        }
    }
    private sealed class MemoryTransport : INetTransport
    {
        private Action? _networkWake;
        public ConcurrentQueue<ReceivedPacket> Incoming = new(); public List<byte[]> Sent = new(); public bool Disposed;
        public bool FailSends; public bool AutoPongEnabled;
        public Action? Sending;
        public int LocalPort => 50001; public long PacketsDropped => 0; public int QueuedPackets => Incoming.Count;
        public int HeldIncomingPackets => 0; public int HeldOutgoingPackets => 0; public NetTrafficMetrics Metrics { get; } = new();
        public void SetNetworkWake(Action? signal) { _networkWake = signal; if (signal != null && !Incoming.IsEmpty) signal(); }
        public void Enqueue(ReceivedPacket packet) { bool empty = Incoming.IsEmpty; Incoming.Enqueue(packet); if (empty) _networkWake?.Invoke(); }
        public IEnumerable<ReceivedPacket> Drain() { while (Incoming.TryDequeue(out var item)) yield return item; }
        public int Drain(Span<ReceivedPacket> destination)
        {
            int count = 0;
            while (count < destination.Length && Incoming.TryDequeue(out ReceivedPacket item))
                destination[count++] = item;
            return count;
        }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> bytes)
        {
            Sending?.Invoke();
            if (FailSends) throw new SocketException((int)SocketError.NoBufferSpaceAvailable);
            Sent.Add(bytes.ToArray());
        }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> bytes, long extraHoldTicks) => SendDatagram(target, bytes);
        public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> bytes, long extraHoldTicks = 0) => throw new NotSupportedException();
        public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> bytes = default) { }
        public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries) { }
        public void AnswerPingsImmediately() => AutoPongEnabled = true;
        public void EnqueueForPlayback(byte[] data, int length) => throw new NotSupportedException();
        public void Dispose() => Disposed = true;
    }
}
