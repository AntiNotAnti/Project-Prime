using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Threading;
using MphRead.Mods.Network;

// Source-linked LobbyQueueClient with a closed transport facade, never UDP.
// Facade surface: QueueIncoming(ReceivedPacket), Sent tuples(Type, Payload),
// Disposed. Production methods exercised here are the actual linked source.
internal static class QueueClientAcceptance
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception("Queue client: " + label); ++checks; }
        var server = new IPEndPoint(IPAddress.Loopback, 27015);
        const uint client = 1234;
        const ulong connection = 4567;
        string Id() => Guid.NewGuid().ToString("D");
        var request = new PartyReservationRequestPacket(1, PartyReservationPurpose.MemberAttach, 88, client, 0,
            Id(), Id(), 12, Id(), 3, 42, 777, "pp1." + new string('x', 764));
        ReceivedPacket Received(PacketType type, byte[] payload, ulong owner = connection)
        {
            var bytes = new byte[payload.Length + 1]; bytes[0] = (byte)type; payload.CopyTo(bytes, 1);
            return new ReceivedPacket(server, bytes, bytes.Length, connectionId: owner, sequence: 11);
        }
        void Establish(LobbyQueueClient queue)
        {
            var transport = queue.Transport;
            Check(QueueHelloPacket.TryRead(transport.Sent.First(p => p.Type == PacketType.QueueHello).Payload, out var hello), "constructor sends normal bounded Hello");
            var welcome = new QueueWelcomePacket(NetConfig.ProtocolVersion, client, hello.ClientNonce);
            var bytes = new byte[QueueWelcomePacket.Size]; welcome.Write(bytes);
            transport.QueueIncoming(Received(PacketType.QueueWelcome, bytes)); queue.Poll();
        }
        PartyReservationStatePacket State(uint revision = 1, PartyReservationWireState state = PartyReservationWireState.Reserved, uint ticks = 900)
            => new(1, request.OperationNonce, revision, request.RequestId, "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
                request.PartyRevision, request.TravelId, request.TravelRevision, request.MatchId, request.AuthorityEpoch,
                state, 3, 1, ticks, 999, 100, 1, 1);
        void Publish(LobbyQueueClient queue, PartyReservationStatePacket state, ulong owner = connection)
        {
            var bytes = new byte[state.EncodedSize]; state.Write(bytes);
            queue.Transport.QueueIncoming(Received(PacketType.PartyReservationState, bytes, owner)); queue.Poll();
        }
        ReceivedPacket Welcome(byte slot = 3, uint identity = client, ushort match = 42, ulong epoch = 777, ulong owner = connection)
        {
            var bytes = new byte[17]; bytes[0] = slot;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1), identity);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(5), match);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(7), epoch);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(15), 9);
            return Received(PacketType.Welcome, bytes, owner);
        }
        using (var solo = new LobbyQueueClient(server, client))
        {
            Establish(solo);
            Check(!solo.IsPartyReservation && solo.PartyState == null, "default mode remains solo");
            var sent = solo.Transport.Sent.Last();
            Check(sent.Type == PacketType.QueueJoin && QueueJoinPacket.TryRead(sent.Payload, out _), "solo Welcome sends ordinary QueueJoin bytes");
            Check(!solo.AcceptPartySeat() && !solo.DeclinePartySeat(), "solo cannot issue Party control");
        }
        foreach (var invalid in new[] { request with { Purpose = PartyReservationPurpose.LeaderCreate }, request with { ClientId = client + 1 }, request with { OperationNonce = 0 } })
        {
            bool threw = false;
            try { using var rejected = new LobbyQueueClient(server, client, invalid); }
            catch (ArgumentException) { threw = true; }
            Check(threw, "constructor rejects wrong purpose, account or operation before socket ownership");
        }
        using (var queue = new LobbyQueueClient(server, client, request))
        {
            Establish(queue);
            var transport = queue.Transport;
            var sent = transport.Sent.Last();
            Check(sent.Type == PacketType.PartyReservationRequest && sent.Payload.Length == 929, "max ticket has sufficient request buffer");
            Check(PartyReservationRequestPacket.TryRead(sent.Payload, out var attached) && attached.QueueNonce != 0
                && attached.OperationNonce == request.OperationNonce && attached.ClientId == client, "attach uses current bootstrap nonce and captured account");
            Check(!transport.Sent.Any(p => p.Type == PacketType.QueueJoin), "group never silently enters solo FIFO");
            Check(!queue.AcceptPartySeat() && !queue.DeclinePartySeat(), "no action before proven Reserved state");
            Publish(queue, State(1, PartyReservationWireState.Waiting));
            Check(!queue.AcceptPartySeat() && !queue.DeclinePartySeat(), "Pending group cohort is leader-owned and has no member seat action");
            // Start a higher revision for the actual Reserved publication.
            var state = State(2);
            foreach (var wrong in new[] { state with { OperationNonce = 89 }, state with { RequestId = Id() },
                state with { PartyRevision = 13 }, state with { TravelId = Id() }, state with { TravelRevision = 4 },
                state with { MatchId = 43 }, state with { AuthorityEpoch = 778 } })
            { Publish(queue, wrong); Check(queue.PartyState?.State == PartyReservationWireState.Waiting, "mismatched immutable witness cannot publish current reservation"); }
            Publish(queue, state, connection + 1); Check(queue.PartyState?.State == PartyReservationWireState.Waiting, "wrong connection incarnation rejected");
            Publish(queue, state);
            Check(queue.SeatAvailable && queue.PartyState == state && !queue.Admitted && queue.RemainingPartySeconds > 0, "Reserved is visible but never admission");
            Publish(queue, state with { RemainingTicks = 3600 });
            Check(queue.PartyState == state, "duplicate state revision cannot renew countdown");
            queue.Transport.QueueIncoming(Welcome()); queue.Poll(); Check(!queue.Admitted, "unsolicited actual Welcome cannot auto-accept group seat");
            Check(queue.AcceptPartySeat() && !queue.AcceptPartySeat(), "explicit Accept issues one action");
            Check(PartyReservationActionPacket.TryRead(transport.Sent.Last().Payload, out var accept)
                && accept.Action == PartyReservationWireAction.Accept && accept.ExpectedRevision == state.Revision
                && accept.MemberLeaseNonce == state.MemberLeaseNonce, "Accept binds exact revision and member lease");
            foreach (var wrong in new[] { Welcome(slot: 4), Welcome(identity: client + 1), Welcome(match: 43), Welcome(epoch: 778), Welcome(owner: connection + 1) })
            { transport.QueueIncoming(wrong); queue.Poll(); Check(!queue.Admitted, "actual admission must match accepted slot, account, match, epoch and connection"); }
            var actual = Welcome(); transport.QueueIncoming(actual); queue.Poll();
            Check(queue.Admitted && queue.PartyState?.State == PartyReservationWireState.Reserved, "Welcome may overtake Admitting after explicit Accept");
            var admission = queue.TakeAdmission(); queue.Dispose();
            Check(ReferenceEquals(admission.Transport, transport) && !transport.Disposed, "TakeAdmission transfers same transport without disposal");
            Check(admission.Welcome.Data.SequenceEqual(actual.Data) && admission.Welcome.ConnectionId == actual.ConnectionId
                && admission.Welcome.Sequence == actual.Sequence, "handoff preserves exact bootstrap bytes and connection evidence");
            transport.Dispose();
        }
        using (var queue = new LobbyQueueClient(server, client, request))
        {
            Establish(queue); Publish(queue, State()); Check(queue.AcceptPartySeat(), "second journey accepts explicitly");
            Publish(queue, State(2, PartyReservationWireState.Admitting));
            Check(!queue.Admitted && queue.PartyState?.State == PartyReservationWireState.Admitting, "Admitting alone is never Joined");
            Publish(queue, State(1)); Check(queue.PartyState?.Revision == 2, "older state cannot replace current Admitting");
            queue.Transport.QueueIncoming(Welcome()); queue.Poll(); Check(queue.Admitted, "matching actual Welcome after Admitting admits");
        }
        using (var queue = new LobbyQueueClient(server, client, request))
        {
            Establish(queue); Publish(queue, State()); Check(queue.DeclinePartySeat() && !queue.AcceptPartySeat(), "self decline cannot become Accept");
            Check(PartyReservationActionPacket.TryRead(queue.Transport.Sent.Last().Payload, out var decline)
                && decline.Action == PartyReservationWireAction.DeclineSelf, "decline never becomes leader CancelGroup");
        }
        foreach (var terminal in new[] { PartyReservationWireState.Expired, PartyReservationWireState.Rejected, PartyReservationWireState.Cancelled })
        {
            using var queue = new LobbyQueueClient(server, client, request); Establish(queue); Publish(queue, State(1, terminal, 0));
            Check(queue.Ended && !queue.Admitted && !queue.AcceptPartySeat() && !queue.DeclinePartySeat(), "terminal state exits visibly and cannot act");
        }
        using (var queue = new LobbyQueueClient(server, client, request))
        {
            Establish(queue); Publish(queue, State(uint.MaxValue)); Publish(queue, State(1));
            Check(queue.PartyState?.Revision == uint.MaxValue, "Party state revisions never wrap");
        }
        using (var queue = new LobbyQueueClient(server, client, request))
        {
            Establish(queue); var oneTick = State(ticks: 1); Publish(queue, oneTick);
            Publish(queue, oneTick with { RemainingTicks = 3600 });
            Check(SpinWait.SpinUntil(() => queue.RemainingPartySeconds == 0, 1000), "duplicate publication cannot refresh fixed monotonic deadline");
            Publish(queue, oneTick with { Revision = 2, RemainingTicks = 3600 });
            Check(queue.Ended && queue.PartyState?.Revision == 1 && !queue.AcceptPartySeat(), "delayed newer state cannot resurrect elapsed local lease");
        }
        var disposed = new LobbyQueueClient(server, client, request); Establish(disposed); Publish(disposed, State());
        var owned = disposed.Transport; disposed.Dispose();
        Check(owned.Disposed && PartyReservationActionPacket.TryRead(owned.Sent.Last().Payload, out var leaving)
            && leaving.Action == PartyReservationWireAction.DeclineSelf, "unadmitted Dispose best-effort declines exact member lease");
        return checks;
    }
}
