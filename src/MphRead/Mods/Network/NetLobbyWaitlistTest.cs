using System;
using System.Linq;
using System.Net;
using System.Threading;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    public static int RunWaitlist()
    {
        try
        {
            QueuePacketChecks();
            {
            using var rig = new Rig();
            for (uint id = 910; id < 918; id++) rig.Add(id);
            using var queued = new LobbyQueueClient(new IPEndPoint(IPAddress.Loopback, rig.Server.BoundPort), 1001);
            using var second = new LobbyQueueClient(new IPEndPoint(IPAddress.Loopback, rig.Server.BoundPort), 1002);
            rig.Wait(() => { queued.Poll(); second.Poll(); return queued.Position == 1 && second.Position == 2; }, "full server queues two proven connections FIFO");
            Check(rig.Server.PeerCount == 8 && !queued.Admitted && !second.Admitted, "queue-only sessions consume no player slots");
            using var direct = new Client(rig.Server.BoundPort, 1003);
            rig.Wait(() => { direct.Drain(); queued.Poll(); second.Poll(); return direct.Refused; }, "full server retains ordinary refusal");
            var victim = rig.Clients[^1]; rig.Clients.RemoveAt(rig.Clients.Count - 1); victim.Dispose();
            rig.Wait(() => { queued.Poll(); second.Poll(); return queued.SeatAvailable; }, "departing player offers first queued seat");
            Check(!second.SeatAvailable, "only FIFO head receives one vacancy");
            direct.Refused = false; direct.Hello();
            rig.Wait(() => { direct.Drain(); queued.Poll(); second.Poll(); return direct.Refused; }, "ordinary join cannot steal reserved seat");
            var offer = queued.Offer!.Value;
            var stolen = new QueueAcceptPacket(offer.QueueId, offer.OfferId, offer.MatchId, offer.AuthorityEpoch);
            byte[] bytes = new byte[QueueAcceptPacket.Size]; stolen.Write(bytes);
            second.Transport.Send(second.Server, PacketType.QueueAccept, bytes);
            Thread.Sleep(100); second.Poll(); queued.Poll();
            Check(!second.Admitted && rig.Server.PeerCount == 7, "another authenticated queue connection cannot claim known offer");
            var stale = new QueueAcceptPacket(offer.QueueId, offer.OfferId + 1, offer.MatchId, offer.AuthorityEpoch);
            stale.Write(bytes); queued.Transport.Send(queued.Server, PacketType.QueueAccept, bytes);
            Thread.Sleep(100); queued.Poll();
            Check(!queued.Admitted, "wrong offer rejected on real server");
            // A queue transport must never deliver gameplay or forged role promotion to the server.
            byte[] fakeWelcome = new byte[17]; fakeWelcome[0] = 0;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(fakeWelcome.AsSpan(1), second.ClientId);
            second.Transport.Send(second.Server, PacketType.Welcome, fakeWelcome);
            second.Transport.Send(second.Server, PacketType.Intent, new byte[IntentPacket.FullSize]);
            Thread.Sleep(100); second.Poll();
            Check(rig.Server.PeerCount == 7 && !second.Admitted, "queue-only inbound allowlist rejects promotion and gameplay");
            Check(queued.DeclineSeat(), "decline request sent");
            rig.Wait(() => { queued.Poll(); second.Poll(); return second.SeatAvailable; }, "decline advances real server reservation FIFO");
            Check(second.AcceptSeat(), "accept request sent");
            rig.Wait(() => { second.Poll(); return second.Admitted && rig.Server.PeerCount == 8; }, "normal Welcome promotes queue on the same transport");
            Check(ReplayIdentityCompatibility.Supports(34), "protocol34 recordings remain supported after protocol35 bump");
            byte[] match = new byte[1 + MatchStatePacket.Size]; match[0] = (byte)PacketType.MatchState;
            rig.Clients[0].Match.Write(match.AsSpan(1));
            Check(ReplayIdentityCompatibility.Convert(match, 34).SequenceEqual(match), "protocol34 gameplay packets retain byte-compatible replay adapter");
            }
            WaitlistExpiryAndBotScenario();
            WaitlistJipScenario();
            WaitlistSessionHandoffScenario();
            WaitlistSimultaneousVacanciesScenario();
            WaitlistContinuousFirstAdmissionScenario();
            WaitlistBurstScenario();
            Console.WriteLine($"[waitlist] PASS {_checks} assertions; real server transport bootstrap, FIFO, refusal/reservation, wrong-owner/offer, decline and same-connection admission.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("[waitlist] FAIL " + ex); return 1; }
        finally { NetSession.Stop(); }
    }
    private static void WaitlistExpiryAndBotScenario()
    {
        using var rig = new Rig(maxPlayers: 2, waitlistOfferSeconds: .5, waitlistResumeSeconds: .3);
        var owner = rig.Add(1101);
        rig.Expect(owner, owner.Command(LobbyCommandType.AddBot, hunter: 0, level: 1), LobbyResultCode.Ok);
        using var first = new LobbyQueueClient(new IPEndPoint(IPAddress.Loopback, rig.Server.BoundPort), 1102);
        using var second = new LobbyQueueClient(new IPEndPoint(IPAddress.Loopback, rig.Server.BoundPort), 1103);
        rig.Wait(() => { first.Poll(); second.Poll(); return first.Position == 1 && second.Position == 2; }, "bot capacity queues humans without evicting bot");
        Check(rig.Server.PeerCount == 1 && Enumerable.Range(0, owner.Roster.Count).Any(i => owner.Roster.IsBot(i)) && !first.SeatAvailable, "queued humans never evict bots");
        first.Transport.Send(first.Server, PacketType.Bye, ReadOnlySpan<byte>.Empty);
        Thread.Sleep(60); first.Poll();
        first.Transport.Send(first.Server, PacketType.Ping, ReadOnlySpan<byte>.Empty);
        rig.Wait(() => { first.Poll(); second.Poll(); return first.State?.State == LobbyQueueWireState.Waiting; }, "same established queue connection resumes after transient disconnect");
        rig.Expect(owner, owner.Command(LobbyCommandType.RemoveBot, target: 1), LobbyResultCode.Ok);
        rig.Wait(() => { first.Poll(); second.Poll(); return first.SeatAvailable; }, "explicit bot removal creates ordinary seat offer");
        rig.Expect(owner, owner.Command(LobbyCommandType.AddBot, hunter: 0, level: 1), LobbyResultCode.ServerBusy);
        Check(!Enumerable.Range(0, owner.Roster.Count).Any(i => owner.Roster.IsBot(i)), "bot addition cannot steal an outstanding human reservation");
        var expired = first.Offer!.Value;
        rig.Wait(() => { first.Poll(); second.Poll(); return second.SeatAvailable && first.State?.State == LobbyQueueWireState.Expired; }, "offer expiry advances FIFO over production UDP");
        var stale = new QueueAcceptPacket(expired.QueueId, expired.OfferId, expired.MatchId, expired.AuthorityEpoch);
        byte[] bytes = new byte[QueueAcceptPacket.Size]; stale.Write(bytes);
        first.Transport.Send(first.Server, PacketType.QueueAccept, bytes);
        Thread.Sleep(40); first.Poll();
        Check(!first.Admitted, "expired connection cannot accept stale seat");
        second.Transport.Send(second.Server, PacketType.QueueAccept, bytes.AsSpan(0, bytes.Length - 1));
        Thread.Sleep(40); second.Poll();
        Check(!second.Admitted, "malformed queue packet cannot bypass real admission");
        second.Dispose();
        Thread.Sleep(80);
        using var grace = new LobbyQueueClient(new IPEndPoint(IPAddress.Loopback, rig.Server.BoundPort), 1104);
        rig.Wait(() => { grace.Poll(); return grace.SeatAvailable; }, "leave releases reservation for another connection");
        grace.Transport.Send(grace.Server, PacketType.Bye, ReadOnlySpan<byte>.Empty);
        Thread.Sleep(450); grace.Poll();
        rig.Wait(() => { grace.Poll(); return grace.State?.State == LobbyQueueWireState.Expired; }, "disconnect grace expiry clears live queue entry");
    }

    private static void WaitlistJipScenario()
    {
        using var rig = new Rig(maxPlayers: 2, allowJoinInProgress: false);
        var owner = rig.Add(1201); var guest = rig.Add(1202);
        rig.ReadyAll(); rig.Expect(owner, owner.Command(LobbyCommandType.StartMatch), LobbyResultCode.Ok);
        owner.Loaded(); guest.Loaded();
        rig.Wait(() => owner.State?.Phase == SessionPhase.InMatch, "waitlist JIP fixture enters a real control-plane match");
        using var queued = new LobbyQueueClient(new IPEndPoint(IPAddress.Loopback, rig.Server.BoundPort), 1203);
        rig.Wait(() => { queued.Poll(); return queued.State?.State == LobbyQueueWireState.NextMatch; }, "JIP-disabled queue reports NEXT MATCH");
        rig.Clients.Remove(guest); guest.Dispose();
        rig.Wait(() => { queued.Poll(); return rig.Server.PeerCount == 1; }, "JIP-disabled player departure observed");
        Check(!queued.SeatAvailable, "JIP-disabled vacancy is not offered during match");
        rig.EndMatchForTest();
        rig.Wait(() => { queued.Poll(); return queued.SeatAvailable && owner.State?.Phase == SessionPhase.Lobby; },
            "return to lobby offers retained queue entry", PostMatchWaitMilliseconds);
        rig.Server.Stop();
        rig.Wait(() => { queued.Poll(); return queued.Error != null; }, "server shutdown closes queue connection");
    }

    private static void WaitlistSessionHandoffScenario()
    {
        using var rig = new Rig(maxPlayers: 2);
        rig.Add(1301); var guest = rig.Add(1302);
        using var queued = new LobbyQueueClient(new IPEndPoint(IPAddress.Loopback, rig.Server.BoundPort));
        rig.Wait(() => { queued.Poll(); return queued.Position == 1; }, "UI queue uses persistent local client identity");
        var transport = queued.Transport;
        rig.Clients.Remove(guest); guest.Dispose();
        rig.Wait(() => { queued.Poll(); return queued.SeatAvailable; }, "UI queue receives offered seat");
        queued.AcceptSeat();
        rig.Wait(() => { queued.Poll(); return queued.Admitted; }, "UI queue receives normal Welcome");
        Check(NetLaunch.Connect("127.0.0.1", rig.Server.BoundPort, "QueuedHero", Hunter.Trace, queuedAdmission: queued),
            "accepted queue handoff completes normal identity/roster/session admission");
        var active = typeof(NetSession).GetField("_transport", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null);
        Check(ReferenceEquals(transport, active) && NetSession.LocalSlot == 1, "game session adopts same socket and reserved player slot");
        NetSession.Stop();
    }

    private static void WaitlistSimultaneousVacanciesScenario()
    {
        using var rig = new Rig(maxPlayers: 3);
        rig.Add(1401); var guest1 = rig.Add(1402); var guest2 = rig.Add(1403);
        using var first = new LobbyQueueClient(new IPEndPoint(IPAddress.Loopback, rig.Server.BoundPort), 1404);
        using var second = new LobbyQueueClient(new IPEndPoint(IPAddress.Loopback, rig.Server.BoundPort), 1405);
        using var third = new LobbyQueueClient(new IPEndPoint(IPAddress.Loopback, rig.Server.BoundPort), 1406);
        void Poll() { first.Poll(); second.Poll(); third.Poll(); }
        rig.Wait(() => { Poll(); return first.Position == 1 && second.Position == 2 && third.Position == 3; }, "three clients enter real FIFO");
        rig.Clients.Remove(guest1); rig.Clients.Remove(guest2); guest1.Dispose(); guest2.Dispose();
        rig.Wait(() => { Poll(); return first.SeatAvailable && second.SeatAvailable; }, "two simultaneous vacancies create two offers");
        Check(!third.SeatAvailable && first.Offer!.Value.OfferId != second.Offer!.Value.OfferId, "only first two receive distinct reservations");
        second.AcceptSeat(); first.AcceptSeat();
        rig.Wait(() => { Poll(); return first.Admitted && second.Admitted && rig.Server.PeerCount == 3; }, "reverse acceptance order preserves two reserved seats");
        Check(first.Offer!.Value.QueueId != second.Offer!.Value.QueueId && !third.Admitted, "capacity cannot over-admit third queued client");
    }

    private static void WaitlistContinuousFirstAdmissionScenario()
    {
        using var rig = new Rig(ServerSessionPolicy.Continuous, maxPlayers: 2, allowJoinInProgress: false);
        var owner = rig.Add(1501);
        using var queued = new LobbyQueueClient(new IPEndPoint(IPAddress.Loopback, rig.Server.BoundPort), 1502);
        rig.Wait(() => { queued.Poll(); return queued.Status == "NEXT MATCH"; }, "continuous active match still blocks JIP");
        rig.Clients.Remove(owner); owner.Dispose();
        rig.Wait(() => { queued.Poll(); return queued.SeatAvailable; }, "empty continuous server can offer first seat with JIP disabled");
        queued.AcceptSeat();
        rig.Wait(() => { queued.Poll(); return queued.Admitted && rig.Server.PeerCount == 1; }, "first queued player starts fresh continuous match with JIP disabled");
    }
    private static void WaitlistBurstScenario()
    {
        using var rig = new Rig(maxPlayers: 2);
        rig.Add(1601); rig.Add(1602);
        var clients = new System.Collections.Generic.List<LobbyQueueClient>();
        double oldLoss = NetLag.LossPercent;
        try
        {
            NetLag.ConfigureLoss("5");
            for (uint id = 1700; id < 1764; id++)
                clients.Add(new LobbyQueueClient(new IPEndPoint(IPAddress.Loopback, rig.Server.BoundPort), id));
            void Poll() { foreach (var client in clients) client.Poll(); }
            rig.Wait(() => { Poll(); return clients.All(c => c.Position > 0 || c.Error != null); }, "bounded bootstrap resolves 64 simultaneous attempts under loss", 14000);
            Check(clients.All(c => c.Error == null), "64 queue clients survive bootstrap and revision publication under loss: " + string.Join(";", clients.Where(c => c.Error != null).Select(c => c.Error)));
            rig.Wait(() => { Poll(); return clients.All(c => c.QueueLength == 64); }, "all 64 queue positions converge under loss", 6000);
            Check(clients.Select(c => c.Position).Distinct().Count() == 64 && rig.Server.PeerCount == 2, "burst queue remains bounded and occupies no player seats");
            for (int i = 0; i < 48; i++) clients[i].Dispose();
            clients.RemoveRange(0,48);
            rig.Wait(() => { Poll(); return clients.All(c => c.QueueLength == 16 && c.Error == null); }, "burst leaves converge without reliable queue exhaustion", 18000);
        }
        finally
        {
            NetLag.ConfigureLoss(oldLoss.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var client in clients) client.Dispose();
        }
    }

    private static void QueuePacketChecks()
    {
        void Fixture(int size, Action<byte[]> write, Func<byte[],bool> read, string golden)
        {
            byte[] bytes = new byte[size]; write(bytes);
            Check(bytes.AsSpan().SequenceEqual(Convert.FromHexString(golden)) && read(bytes), "production queue golden wire " + bytes[0]);
            for (int n = 0; n < size; n++) Check(!read(bytes[..n]), "production queue truncation " + bytes[0] + ":" + n);
            Check(!read(bytes.Concat(new byte[]{0}).ToArray()), "production queue trailing bytes " + bytes[0]);
            bytes[0] = 255; Check(!read(bytes), "production queue wrong kind");
        }
        string protocolHex = NetConfig.ProtocolVersion.ToString("X2", System.Globalization.CultureInfo.InvariantCulture);
        Fixture(QueueHelloPacket.Size, b => new QueueHelloPacket(NetConfig.ProtocolVersion,1,2).Write(b), b => QueueHelloPacket.TryRead(b,out _), "39" + protocolHex + "010000000200000000000000");
        Fixture(QueueWelcomePacket.Size, b => new QueueWelcomePacket(NetConfig.ProtocolVersion,1,2).Write(b), b => QueueWelcomePacket.TryRead(b,out _), "3A" + protocolHex + "010000000200000000000000");
        byte[] oldHello = new byte[QueueHelloPacket.Size];
        new QueueHelloPacket(NetConfig.ProtocolVersion,1,2).Write(oldHello);
        oldHello[1] = (byte)(NetConfig.ProtocolVersion - 1);
        Check(!QueueHelloPacket.TryRead(oldHello, out _), "queue rejects an obsolete protocol before admission");
        Fixture(QueueJoinPacket.Size, b => new QueueJoinPacket(2).Write(b), b => QueueJoinPacket.TryRead(b,out _), "3B0200000000000000");
        Fixture(QueueLeavePacket.Size, b => new QueueLeavePacket(2).Write(b), b => QueueLeavePacket.TryRead(b,out _), "3C0200000000000000");
        Fixture(QueueStatePacket.Size, b => new QueueStatePacket(1,2,1,3,LobbyQueueWireState.Waiting).Write(b), b => QueueStatePacket.TryRead(b,out _), "3D0100000002000000000000000100030000");
        Fixture(QueueSeatOfferPacket.Size, b => new QueueSeatOfferPacket(1,2,3,4,5,60).Write(b), b => QueueSeatOfferPacket.TryRead(b,out _), "3E0100000002000000000000000300000000000000040005000000000000003C000000");
        Fixture(QueueAcceptPacket.Size, b => new QueueAcceptPacket(2,3,4,5).Write(b), b => QueueAcceptPacket.TryRead(b,out _), "3F0200000000000000030000000000000004000500000000000000");
        Fixture(QueueDeclinePacket.Size, b => new QueueDeclinePacket(2,3,4,5).Write(b), b => QueueDeclinePacket.TryRead(b,out _), "400200000000000000030000000000000004000500000000000000");
        Check(!new QueueJoinPacket(0).Validate() && !new QueueAcceptPacket(1,1,1,0).Validate(), "zero nonces and authority identities reject before wire mutation");
        Check(!new QueueStatePacket(1,1,2,1,LobbyQueueWireState.Waiting).Validate(), "impossible queue position rejects at generated boundary");
        Check(new QueueSeatOfferPacket(1,ulong.MaxValue,ulong.MaxValue,ushort.MaxValue,ulong.MaxValue,18000).Validate(), "queue identities preserve complete ulong domain");
        const string unicodeServerName = "Rémëlle ー's lobby";
        var status = new ServerStatusPacket
        {
            WaitlistSupported = true,
            WaitlistCount = 256,
            ReservedSlots = 3,
            ServerName = unicodeServerName
        };
        byte[] statusBytes = new byte[ServerStatusPacket.SizeWithReservations];
        status.Write(statusBytes);
        var decoded = ServerStatusPacket.Read(statusBytes);
        Check(decoded.WaitlistSupported && decoded.WaitlistCount == 256
            && decoded.ReservedSlots == 3
            && decoded.ServerName == unicodeServerName,
            "bounded discovery queue/reservation capacity and Unicode server name round trip");
        Check(ServerStatusPacket.Read(
                statusBytes.AsSpan(0, ServerStatusPacket.SizeWithWaitlist)).ReservedSlots == 0,
            "pre-protocol-43 discovery reply defaults reserved capacity to zero");
        Check(!ServerStatusPacket.Read(
                statusBytes.AsSpan(0, ServerStatusPacket.SizeWithFlags)).WaitlistSupported,
            "old discovery reply defaults queue capability off");
    }

}
