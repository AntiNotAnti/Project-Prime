using System.Net;
using System.Net.Sockets;
using MphRead.Mods.Network;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL " + message);
    checks++; Console.WriteLine("WAITLIST PASS " + message);
}
LobbyQueueConnection Owner(int number) => new("127.0.0.1", 10000 + number, (ulong)number);
var queue = new LobbyWaitlist(capacity: 3);
queue.Update(0, 2, 3, true, 1, 10); // humans/bots already occupy both seats
Check(queue.TryJoin(Owner(1), 1, 0, out var first), "full lobby accepts queue entry");
Check(!queue.TryJoin(Owner(1) with { ConnectionId = 99 }, 99, 0, out _), "replacement incarnation cannot duplicate same endpoint entry");
Check(queue.TryJoin(Owner(2), 1, 0, out var second), "client nonce is not a global priority or identity");
Check(queue.TryJoin(Owner(3), 3, 0, out var third), "FIFO capacity reaches bound");
Check(!queue.TryJoin(Owner(4), 4, 0, out _), "capacity fails closed");
Check(!queue.TryJoin(Owner(1), 99, 0, out _), "duplicate owner rejected regardless of nonce");
Check(queue.TryGetState(Owner(2), second.QueueId, out var secondState) && secondState.Position == 2, "server sequence owns FIFO order");
Check(!queue.TryGetState(Owner(4), first.QueueId, out _), "queue ID alone cannot inspect another entry");
Check(!queue.CanDirectJoin(0), "bots and humans occupy ordinary capacity");
queue.Update(1, 2, 2, true, 1, 10);
Check(queue.TryGetState(Owner(1), first.QueueId, out var offered) && offered.Offer?.Slot == 0, "first waiting connection receives free seat");
var offer = offered.Offer!.Value;
Check(!queue.CanDirectJoin(0) && queue.ReservedSlots == 1, "direct join cannot steal reservation");
Check(!queue.TryAccept(Owner(2), first.QueueId, offer.OfferId, 1, 10, 1, _ => throw new Exception()), "wrong connection cannot accept known offer");
Check(!queue.TryAccept(Owner(1) with { Port = 9999 }, first.QueueId, offer.OfferId, 1, 10, 1, _ => throw new Exception()), "changed endpoint cannot claim connection ID");
Check(!queue.TryAccept(Owner(1), first.QueueId, offer.OfferId + 1, 1, 10, 1, _ => true), "wrong offer ID rejected");
Check(!queue.TryAccept(Owner(1), first.QueueId, offer.OfferId, 2, 10, 1, _ => true), "wrong match rejected");
Check(!queue.TryAccept(Owner(1), first.QueueId, offer.OfferId, 1, 11, 1, _ => true), "wrong epoch rejected");
Check(!queue.TryAccept(Owner(1), first.QueueId, offer.OfferId, 1, 10, 1, _ => false) && queue.Count == 3 && queue.ReservedSlots == 1,
    "normal admission rejection preserves offer");
try { queue.TryAccept(Owner(1), first.QueueId, offer.OfferId, 1, 10, 1, _ => throw new IOException()); }
catch(IOException) { Check(queue.Count == 3 && queue.ReservedSlots == 1, "admission exception preserves offer"); }
try { queue.TryAccept(Owner(1), first.QueueId, offer.OfferId, 1, 10, 1, _ => { queue.Clear(); return true; }); }
catch(InvalidOperationException) { Check(queue.Count == 3, "admission callback cannot reenter queue"); }
Check(queue.TryAccept(Owner(1), first.QueueId, offer.OfferId, 1, 10, 2, slot => slot == 0), "validated admission consumes exact reserved seat");
Check(!queue.CanDirectJoin(0) && queue.Count == 2 && queue.Metrics.Accepted == 1 && queue.Metrics.MeanWaitSeconds == 2,
    "accepted seat immediately occupied and bounded metrics updated");
Check(!queue.TryAccept(Owner(1), first.QueueId, offer.OfferId, 1, 10, 2, _ => true), "duplicate accept cannot admit twice");
queue.Update(3, 2, 0, true, 1, 10);
Check(queue.ReservedSlots == 3, "two simultaneous vacancies reserve distinct seats");
queue.TryGetState(Owner(2), second.QueueId, out offered); offer = offered.Offer!.Value;
Check(queue.Decline(Owner(2), second.QueueId, offer.OfferId, 1, 10, 3), "decline removes offered entry");
Check(queue.CanDirectJoin(0), "declined reservation releases seat when nobody else waits");
Check(queue.Disconnect(Owner(3), third.QueueId, 3), "disconnect retains queue and reservation briefly");
Check(!queue.Resume(Owner(9), third.QueueId, 4), "different connection cannot resume by queue ID");
Check(queue.Resume(Owner(3), third.QueueId, 4), "same established connection resumes within grace");
queue.Update(4, 2, 0, false, 1, 10);
Check(queue.ReservedSlots == 0 && queue.TryGetState(Owner(3), third.QueueId, out var waiting) && waiting.State == LobbyWaitlistState.NextMatch,
    "JIP disabled retains entry as NEXT MATCH and revokes offers");
queue.Update(5, 2, 0, true, 2, 11);
queue.TryGetState(Owner(3), third.QueueId, out offered); offer = offered.Offer!.Value;
Check(offer.MatchId == 2 && offer.AuthorityEpoch == 11, "next lobby epoch issues a fresh fenced offer");
queue.Update(20, 2, 0, true, 2, 11);
Check(queue.Count == 0 && queue.ReservedSlots == 0 && queue.Metrics.Expired == 1, "offer expires exactly at deadline");
queue.TryJoin(Owner(1), 9, 20, out first);
queue.Disconnect(Owner(1), first.QueueId, 20);
queue.Disconnect(Owner(1), first.QueueId, 25);
queue.Update(30, 2, 0, true, 2, 11);
Check(queue.Count == 0 && queue.Metrics.DisconnectedExpired == 1, "repeated disconnect does not extend bounded grace");
queue.TryJoin(Owner(2), 3, 30, out first); queue.Clear();
Check(queue.Count == 0 && queue.ReservedSlots == 0, "shutdown clears all entries and reservations");
queue.Update(30, 2, 0, true, 3, 12); queue.TryJoin(Owner(2), 3, 30, out second);
Check(second.QueueId != first.QueueId, "clear never reuses queue identifiers");
Exception? offThread = null;
var thread = new Thread(() => { try { queue.Clear(); } catch(Exception ex) { offThread=ex; } });
thread.Start(); thread.Join();
Check(offThread is InvalidOperationException && queue.Count == 1, "mutations restricted to lobby owner thread");
try { queue.Update(double.NaN, 2, 0, true, 3, 12); Check(false,"reject NaN"); }
catch(ArgumentOutOfRangeException) { Check(queue.Count == 1,"nonfinite time rejected before mutation"); }
try { queue.Update(29,2,0,true,3,12); Check(false,"reject reversed time"); }
catch(ArgumentOutOfRangeException) { Check(queue.Count == 1,"server time cannot move backward"); }

var expiryQueue = new LobbyWaitlist(2, offerLifetimeSeconds: 1, resumeGraceSeconds: 2);
expiryQueue.Update(0, 1, 1, true, 1, 1);
expiryQueue.TryJoin(Owner(1), 1, 0, out var expiringFirst);
expiryQueue.TryJoin(Owner(2), 2, 0, out var expirySecond);
expiryQueue.Update(0, 1, 0, true, 1, 1);
expiryQueue.TryGetState(Owner(1), expiringFirst.QueueId, out var oldState);
expiryQueue.Update(1, 1, 0, true, 1, 1);
Check(expiryQueue.TryGetState(Owner(2), expirySecond.QueueId, out var nextOffer) && nextOffer.Offer != null
    && nextOffer.Offer.Value.OfferId != oldState.Offer!.Value.OfferId, "expiry immediately advances offer to next FIFO owner");
Check(!expiryQueue.TryAccept(Owner(1), expiringFirst.QueueId, oldState.Offer.Value.OfferId, 1, 1, 1, _ => true), "expired offer cannot be accepted");
var staleOffer = nextOffer.Offer!.Value;
expiryQueue.Update(1, 1, 0, true, 2, 2);
Check(!expiryQueue.TryAccept(Owner(2), expirySecond.QueueId, staleOffer.OfferId, 1, 1, 1, _ => true), "transition invalidates old offer identity");
try { _ = new LobbyWaitlist(257); Check(false, "reject oversized capacity"); }
catch(ArgumentOutOfRangeException) { Check(true, "hard capacity capped at 256"); }

var graceQueue = new LobbyWaitlist(2, resumeGraceSeconds: 1);
graceQueue.Update(0, 1, 1, true, 1, 1);
graceQueue.TryJoin(Owner(1), 1, 0, out var absentFirst);
graceQueue.TryJoin(Owner(2), 2, 0, out var behindAbsent);
graceQueue.Disconnect(Owner(1), absentFirst.QueueId, 0);
graceQueue.Update(0, 1, 0, true, 1, 1);
Check(!graceQueue.CanDirectJoin(0) && graceQueue.ReservedSlots == 0 && graceQueue.TryGetState(Owner(2), behindAbsent.QueueId, out var behindState)
    && behindState.Offer == null, "disconnect grace preserves FIFO precedence");
graceQueue.Update(1, 1, 0, true, 1, 1);
Check(graceQueue.TryGetState(Owner(2), behindAbsent.QueueId, out behindState) && behindState.Offer != null,
    "bounded disconnect grace releases FIFO head");

// Generated synthetic queue schema checks. No live packet IDs are assigned.
void Golden(byte[] actual, string expected, Func<byte[], bool> decode)
{
    Check(actual.AsSpan().SequenceEqual(Convert.FromHexString(expected)), "golden packet " + actual[0]);
    Check(decode(actual), "round trip packet " + actual[0]);
    for(int i=0;i<actual.Length;i++) Check(!decode(actual[..i]), "truncation " + actual[0] + " at " + i);
    Check(!decode(actual.Concat(new byte[]{0}).ToArray()), "trailing byte " + actual[0]);
    var wrong=(byte[])actual.Clone();wrong[0]++;Check(!decode(wrong),"wrong packet kind " + actual[0]);
}
byte[] EncodeJoin(ulong nonce) { var p=new QueueJoin(nonce);var b=new byte[p.EncodedSize];p.Write(b);return b; }
byte[] EncodeLeave(ulong id) { var p=new QueueLeave(id);var b=new byte[p.EncodedSize];p.Write(b);return b; }
byte[] EncodeAccept(ulong id,ulong offerId,uint match,ulong epoch) { var p=new QueueAccept(id,offerId,match,epoch);var b=new byte[p.EncodedSize];p.Write(b);return b; }
Golden(EncodeJoin(1),"C90100000000000000",b=>QueueJoin.TryRead(b,out _));
Golden(EncodeLeave(1),"CA0100000000000000",b=>QueueLeave.TryRead(b,out _));
var statePacket=new QueueState(1,2,3,QueueStatus.Waiting);byte[] stateBytes=new byte[statePacket.EncodedSize];statePacket.Write(stateBytes);
Golden(stateBytes,"CB01000000000000000200030000",b=>QueueState.TryRead(b,out _));
var offerPacket=new QueueOffer(1,2,3,4,60);byte[] offerBytes=new byte[offerPacket.EncodedSize];offerPacket.Write(offerBytes);
Golden(offerBytes,"CC010000000000000002000000000000000300000004000000000000003C000000",b=>QueueOffer.TryRead(b,out _));
Golden(EncodeAccept(1,2,3,4),"CD01000000000000000200000000000000030000000400000000000000",b=>QueueAccept.TryRead(b,out _));
var declinePacket=new QueueDecline(1,2,3,4);byte[] declineBytes=new byte[declinePacket.EncodedSize];declinePacket.Write(declineBytes);
Golden(declineBytes,"CE01000000000000000200000000000000030000000400000000000000",b=>QueueDecline.TryRead(b,out _));
stateBytes[^1]=255;Check(!QueueState.TryRead(stateBytes,out _),"bad queue enum rejected");
stateBytes[^1]=0;stateBytes[9]=0;stateBytes[10]=0;Check(!QueueState.TryRead(stateBytes,out _),"zero position rejected");
offerBytes[^1]=255;Check(!QueueOffer.TryRead(offerBytes,out _),"oversized offer expiry rejected");

// Real loopback datagrams exercise the core with fixture-established endpoint /
// connection identities. This is NOT NetTransport bootstrap or live server E2E.
using var server=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
using var clientA=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
using var clientB=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
using var attacker=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
foreach(var socket in new[]{server,clientA,clientB,attacker}) socket.Client.ReceiveTimeout=2000;
var endpoint=(IPEndPoint)server.Client.LocalEndPoint!;
LobbyQueueConnection SocketOwner(UdpClient socket,ulong id) => LobbyQueueConnection.FromEstablished((IPEndPoint)socket.Client.LocalEndPoint!,id);
var identities=new Dictionary<string,LobbyQueueConnection> {
    [clientA.Client.LocalEndPoint!.ToString()!]=SocketOwner(clientA,101),
    [clientB.Client.LocalEndPoint!.ToString()!]=SocketOwner(clientB,102),
    [attacker.Client.LocalEndPoint!.ToString()!]=SocketOwner(attacker,103)
};
var udpQueue=new LobbyWaitlist();udpQueue.Update(0,1,1,true,1,1);
byte[] Exchange(UdpClient client,byte[] request)
{
    client.Send(request,endpoint); IPEndPoint sender=new(IPAddress.Any,0);byte[] received=server.Receive(ref sender);
    var owner=identities[sender.ToString()];byte[] reply={0};
    if(QueueJoin.TryRead(received,out var join) && udpQueue.TryJoin(owner,join.ClientNonce,0,out var entry)) {
        var state=new QueueState(entry.QueueId,(ushort)entry.Position,(ushort)entry.QueueLength,QueueStatus.Waiting);
        reply=new byte[state.EncodedSize];state.Write(reply);
    } else if(QueueLeave.TryRead(received,out var leave) && udpQueue.Leave(owner,leave.QueueId,0)) reply=new byte[]{1};
    else if(QueueAccept.TryRead(received,out var accept) && udpQueue.TryAccept(owner,accept.QueueId,accept.OfferId,
        accept.MatchId,accept.AuthorityEpoch,0,_=>true)) reply=new byte[]{1};
    server.Send(reply,sender);IPEndPoint from=new(IPAddress.Any,0);return client.Receive(ref from);
}
Check(QueueState.TryRead(Exchange(clientA,EncodeJoin(1)),out var a) && a.Position==1,"UDP queue-only synthetic join");
Check(QueueState.TryRead(Exchange(clientB,EncodeJoin(2)),out var b) && b.Position==2,"UDP FIFO ordering");
Check(Exchange(clientA,EncodeJoin(3))[0]==0,"UDP duplicate join rejected");
Check(Exchange(attacker,EncodeLeave(a.QueueId))[0]==0 && udpQueue.Count==2,"UDP attacker cannot leave victim entry");
Check(Exchange(clientA,EncodeJoin(1)[..^1])[0]==0,"UDP truncated queue packet rejected");
udpQueue.Update(0,1,0,true,1,1);udpQueue.TryGetState(SocketOwner(clientA,101),a.QueueId,out offered);offer=offered.Offer!.Value;
Check(!udpQueue.CanDirectJoin(0),"UDP fixture offer reserves seat");
Check(Exchange(attacker,EncodeAccept(a.QueueId,offer.OfferId,1,1))[0]==0,"UDP wrong endpoint cannot accept seat");
Check(Exchange(clientA,EncodeAccept(a.QueueId,offer.OfferId,1,1))[0]==1,"UDP rightful acceptance");
Check(Exchange(clientA,EncodeAccept(a.QueueId,offer.OfferId,1,1))[0]==0,"UDP duplicate acceptance rejected");
Check(Exchange(clientB,EncodeLeave(b.QueueId))[0]==1 && udpQueue.Count==0,"UDP leave frees queue");
Console.WriteLine($"WAITLIST PASS {checks} assertions; policy, generated synthetic wire, real loopback datagrams. Live NetTransport admission is not integrated.");
