using System.Net;
using MphRead.Mods.Network;
int failures = 0;
foreach (int traffic in new[] { 0, 100, 511, 512, 600, 2000 }) {
 var endpoint = new IPEndPoint(IPAddress.Loopback, 32125);
 var sender = new NetConnection(endpoint, 7);
 var receiver = new NetConnection(endpoint, 7);
 if (!sender.Reliable.TryQueue(PacketType.TimedGrant, new byte[] { 1 }, 0, out uint id)
  || !sender.Reliable.TrySend(0, out var transmission)) throw new Exception("actual reliable admission failed");
 var original = sender.Send(transmission.Type, 0, NetHeaderFlags.Reliable, transmission.EventId);
 receiver.Receive(original, 400);
 if (!receiver.Reliable.Receive(id)) throw new Exception("first actual event must be received");
 var ack = receiver.Send(0, 400, NetHeaderFlags.AckOnly);
 if ((ack.Flags & NetHeaderFlags.AckValid) == 0 || !NetReceiveWindow.Acknowledges(original.Sequence, ack.Ack, ack.AckBits))
  throw new Exception("actual receiver ACK does not cover original reliable datagram");
 for (int n = 0; n < traffic; n++) sender.Send(PacketType.Snapshot, 800.0 * (n+1) / (traffic+1));
 sender.Receive(ack, 800);
 int pending = sender.Reliable.Capture(800).Pending;
 bool passed = pending == 0;
 if (!passed) failures++;
 Console.WriteLine($"{(passed ? "PASS" : "FAIL")} real owner ACK-retention contract: datagramsDuringRTT={traffic} rttMs=800 receivedEventOnce=true exactAck=true remainingPending={pending} {sender.Capture()}");
}
Console.WriteLine($"{(failures == 0 ? "PASS" : "FAIL")} actual compiled NetConnection receipt retention, {failures} failures; controlled clocks/headers, no sockets/gameplay/assets/GL/backend");
int extended = RetentionMatrix.Run();
int bounds = ReceiptBounds.Run();
int udp = args.Length == 1 && args[0] == "--udp-saturation" ? UdpSaturation.Run() : 0;
return failures == 0 && extended == 0 && bounds == 0 && udp == 0 ? 0 : 1;
