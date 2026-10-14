using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Network;

internal static class Program
{
    private static int Main(string[] args)
    {
        string Arg(string key) => Array.IndexOf(args, key) is int index && index >= 0 && index + 1 < args.Length
            ? args[index + 1] : throw new ArgumentException("Required " + key);
        string candidate = Path.GetFullPath(Arg("--binary-dir")), receipt = Path.GetFullPath(Arg("--receipt"));
        string profile = Path.Combine(Path.GetTempPath(), "prime-grant-udp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile); Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", profile);
        Directory.SetCurrentDirectory(profile);
        AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(candidate, name.Name + ".dll"))
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(candidate, name.Name + ".dll")) : null;
        return Fixture.Run(candidate, receipt, profile);
    }
}

// Socket relay controls loss/reorder/duplication on actual datagrams. It never
// injects application callbacks, backdates production clocks or alters the client.
internal sealed class Relay : IDisposable
{
    private readonly UdpClient _socket = new(new IPEndPoint(IPAddress.Loopback, 0));
    internal readonly IPEndPoint Server, Client;
    internal IPEndPoint Address => (IPEndPoint)_socket.Client.LocalEndPoint!;
    internal bool ReorderPair, DropAll;
    internal double DropAcksUntil;
    internal int DroppedAcks, DuplicateCopies, ReorderedPairs, GrantAttempts;
    internal readonly List<int> GrantLengths = new();
    internal byte[]? FirstGrant;
    private byte[]? _held;
    internal Relay(NetTransport server, NetTransport client)
    { Server = new(IPAddress.Loopback, server.LocalPort); Client = new(IPAddress.Loopback, client.LocalPort); }
    internal static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    internal void Pump()
    {
        while (_socket.Available > 0)
        {
            IPEndPoint source = new(IPAddress.Any, 0); byte[] bytes = _socket.Receive(ref source);
            bool authority = source.Equals(Server);
            if (!authority && !source.Equals(Client)) throw new InvalidOperationException("Unexpected relay endpoint.");
            bool header = NetHeader.TryRead(bytes, out var wire);
            if (authority && header && wire.Type == PacketType.TimedGrant)
            {
                GrantAttempts++; GrantLengths.Add(bytes.Length); FirstGrant ??= bytes.ToArray();
            }
            if (DropAll) continue;
            if (!authority && header && (wire.Flags & NetHeaderFlags.AckOnly) != 0 && Now < DropAcksUntil)
            { DroppedAcks++; continue; }
            if (authority && header && wire.Type == PacketType.TimedGrant && ReorderPair)
            {
                if (_held == null) { _held = bytes; continue; }
                Send(bytes, Client); Send(_held, Client); Send(_held, Client);
                _held = null; ReorderPair = false; DuplicateCopies++; ReorderedPairs++; continue;
            }
            Send(bytes, authority ? Client : Server);
        }
    }
    internal void ToClient(byte[] bytes) => Send(bytes, Client);
    private void Send(byte[] bytes, IPEndPoint target) => _socket.Send(bytes, bytes.Length, target);
    public void Dispose() => _socket.Dispose();
}

internal static class Fixture
{
    private static readonly List<string> Checks = new();
    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); Checks.Add(label); }
    private static void Session(string property, object value)
        => typeof(NetSession).GetProperty(property, BindingFlags.Public | BindingFlags.Static)!.SetValue(null, value);
    private static ShotKey Life(int slot) => new(11, 7, slot, 1, 1, 0);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string StateHash(TimedGrantPresentation state)
    { using var stream = new MemoryStream(); state.Write(new BinaryWriter(stream)); return Hash(stream.ToArray()); }
    private static void Authority(uint frame)
    { Session(nameof(NetSession.Role), NetRole.Server); Session(nameof(NetSession.IsAuthority), true); Session(nameof(NetSession.LocalSlot), -1); Session(nameof(NetSession.NetFrame), frame); }
    private static List<TimedGrantFact> Decode(ReadOnlySpan<byte> bytes)
    {
        Check(NetFactBatch.Validate(PacketType.TimedGrant, bytes), "production batched grant payload valid");
        var result = new List<TimedGrantFact>();
        for (int offset = 0; offset < bytes.Length; offset += TimedGrantFact.Size)
        {
            Check(TimedGrantFact.TryRead(bytes.Slice(offset, TimedGrantFact.Size), out var fact), "exact grant row decodes");
            result.Add(fact);
        }
        return result;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int Run(string candidate, string output, string profile)
    {
        var report = new Dictionary<string, object?> { ["schemaVersion"] = 1, ["status"] = "FAIL",
            ["scope"] = "synthetic authority-value grants; actual identity-bound reliable UDP, batching and bounded delivery",
            ["nativeContactGeometry"] = "NOT_RUN", ["naturalAIThreeTypeCoverage"] = "NOT_RUN",
            ["gpuWork"] = "NOT_RUN", ["protocol"] = NetConfig.ProtocolVersion,
            ["isolatedProfile"] = profile, ["checks"] = Checks };
        var watch = Stopwatch.StartNew();
        try
        {
            var engine = typeof(NetTransport).Assembly;
            string loaded = engine.Location, expected = Path.Combine(candidate, "ProjectPrime.dll");
            string loadedHash = Hash(File.ReadAllBytes(loaded)), expectedHash = Hash(File.ReadAllBytes(expected));
            report["diagnostic"] = new { path = typeof(Fixture).Assembly.Location,
                sha256 = Hash(File.ReadAllBytes(typeof(Fixture).Assembly.Location)),
                mvid = typeof(Fixture).Assembly.ManifestModule.ModuleVersionId,
                buildId = typeof(Fixture).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion };
            report["client"] = new { path = loaded, sha256 = loadedHash, expectedSha256 = expectedHash,
                mvid = engine.ManifestModule.ModuleVersionId, buildId = engine.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion };
            Check(loadedHash == expectedHash && NetConfig.ProtocolVersion == 56, "loaded physical candidate hash and protocol match frozen control");
            NetLag.Configure("0"); NetLag.ConfigureLoss("0"); NetLag.ConfigureReorder("0"); NetLag.ConfigureDuplicate("0");
            NetGrantNetwork.Reset(); Authority(100);
            Session(nameof(NetSession.ServerMatch), new MatchStatePacket { MatchId = 7, AuthorityEpoch = 11, RoomKey = "synthetic-grant-udp", Mode = (byte)GameMode.Battle });
            // Production pure reservation model with explicitly synthetic lives,
            // spawn incarnations, contact/source frame100, and expiry1900.
            var ledger = new NetTimedGrants(11, 7, 3, NetGrantNetwork.Changed);
            var records = new List<TimedGrantRecord>();
            for (int slot = 0; slot < 8; slot++)
            {
                ledger.BeginLife(Life(slot), 100); NetGrantNetwork.BeginLife(Life(slot), 3, 100);
                foreach (var type in Enum.GetValues<TimedPowerUp>())
                {
                    Check(ledger.RegisterSpawn(slot * 3 + (int)type + 10, type, out var spawn), "synthetic incarnation registered in production ledger");
                    Check(ledger.TryGrant(spawn, Life(slot), 100, 100, out var record) == TimedGrantResult.Granted,
                        "production pure ledger reserves synthetic contact once");
                    records.Add(record!);
                }
            }
            using var server = new NetTransport(0); using var healthy = new NetTransport(0);
            using var late = new NetTransport(0); using var stalled = new NetTransport(0);
            using var goodLink = new Relay(server, healthy); using var lateLink = new Relay(server, late); using var stalledLink = new Relay(server, stalled);
            var links = new[] { goodLink, lateLink, stalledLink };
            var latePresentation = new TimedGrantPresentation();
            int healthyPackets = 0, latePackets = 0, stalledPackets = 0;
            var healthyRows = new List<TimedGrantFact>(); var lateRows = new List<TimedGrantFact>();
            bool failureNotice = false;
            NetGrantNetwork.Peer? goodPeer = null;
            int healthyServicePumps = 0;
            void Pump()
            {
                foreach (var link in links) link.Pump();
                if (goodPeer != null)
                {
                    healthyServicePumps++;
                    if (!NetGrantNetwork.Pump(goodPeer, (type, bytes) => server.TrySendSemantic(goodLink.Address, type, bytes)))
                        throw new InvalidOperationException("Healthy grant peer failed during normal ongoing service.");
                }
                foreach (var packet in server.Drain())
                    if (packet.Type == PacketType.Bye && packet.Sender.Equals(stalledLink.Address)) failureNotice = true;
                foreach (var packet in healthy.Drain()) if (packet.Type == PacketType.TimedGrant)
                {
                    Check(packet.Sender.Equals(goodLink.Address), "healthy authority fact arrived from admitted endpoint");
                    healthyPackets++;
                    foreach (var fact in Decode(packet.Payload))
                    {
                        healthyRows.Add(fact); Session(nameof(NetSession.Role), NetRole.Client); Session(nameof(NetSession.IsAuthority), false);
                        Check(NetGrantNetwork.Receive(fact), "production client receive accepts fresh authority revision");
                    }
                }
                foreach (var packet in late.Drain()) if (packet.Type == PacketType.TimedGrant)
                { latePackets++; foreach (var fact in Decode(packet.Payload)) { lateRows.Add(fact); Check(latePresentation.Accept(fact, 900), "independent late peer accepts fresh authority revision"); } }
                foreach (var packet in stalled.Drain()) if (packet.Type == PacketType.TimedGrant) stalledPackets++;
                foreach (var link in links) link.Pump();
            }
            void Until(Func<bool> done, double seconds, string label)
            {
                var elapsed = Stopwatch.StartNew();
                while (!done() && elapsed.Elapsed.TotalSeconds < seconds) { Pump(); Thread.Sleep(2); }
                Pump(); Check(done(), label);
            }
            void Admit(NetTransport client, Relay link, uint id)
            {
                byte[] hello = new byte[6]; hello[0] = NetConfig.ProtocolVersion; hello[1] = 255;
                BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(2), id); client.Send(link.Address, PacketType.Hello, hello);
                byte[] welcome = new byte[17]; BinaryPrimitives.WriteUInt32LittleEndian(welcome.AsSpan(1), id);
                server.Send(link.Address, PacketType.Welcome, welcome);
                Until(() => client.ConnectionStats(link.Address).HasValue && server.ReliableStats(link.Address)?.Pending == 0,
                    3, "real Hello/Welcome connection identity binds and acknowledges " + id);
                Check(client.ConnectionStats(link.Address)!.Value.ConnectionId == server.ConnectionStats(link.Address)!.Value.ConnectionId,
                    "both UDP endpoints use the same admitted connection incarnation");
            }
            Admit(healthy, goodLink, 101); Admit(late, lateLink, 102); Admit(stalled, stalledLink, 103);
            Authority(700); goodPeer = NetGrantNetwork.Join();
            Check(goodPeer.Baseline.Count == 2, "24 current states require exactly two production bootstrap packets");
            goodLink.ReorderPair = true; goodLink.DropAcksUntil = Relay.Now + .8;
            Check(NetGrantNetwork.Pump(goodPeer, (type, bytes) => server.TrySendSemantic(goodLink.Address, type, bytes)), "healthy bootstrap admits into real reliable transport");
            Until(() => healthyRows.Count == 24 && server.ReliableStats(goodLink.Address)?.Pending == 0, 4,
                "lost ACK recovers all24 states with finite real retransmission and no duplicate application");
            int initialPackets = healthyPackets;
            Check(healthyPackets == 2 && goodLink.ReorderedPairs == 1 && goodLink.DuplicateCopies == 1 && goodLink.DroppedAcks > 0,
                "actual wire reorder, duplicate and ACK loss were exercised positively");
            var healthyStats = healthy.ConnectionStats(goodLink.Address)!.Value;
            var reliable = server.ReliableStats(goodLink.Address)!.Value;
            Check(reliable.Retransmissions > 0 && healthy.ReliableStats(goodLink.Address)!.Value.Duplicates > 0
                && healthyStats.Reordered > 0, "production reliable/window statistics observe retransmission and dedup/reorder");
            Check(goodLink.GrantLengths.All(length => length <= 1200) && goodLink.GrantLengths.Contains(1194)
                && goodLink.GrantLengths.Contains(134), "complete reliable datagrams are1194/134bytes within1200MTU");
            Check(healthyRows.Select(f => (f.Life.ShooterSlot, f.Type)).Distinct().Count() == 24
                && healthyRows.All(f => f.Resume && f.ContactFrame == 100 && f.EndFrame == 1900 && f.AuthorityFrame == 700),
                "24 immutable life/type grants preserve contact, expiry and quiet late-bootstrap clock");
            report["bootstrap"] = new { states = healthyRows.Count, applicationDatagrams = initialPackets,
                GrantLengths = goodLink.GrantLengths.ToArray(), goodLink.GrantAttempts, goodLink.DroppedAcks, goodLink.DuplicateCopies,
                goodLink.ReorderedPairs, reliable.Retransmissions, healthyStats.Reordered };
            Check(NetGrantNetwork.Presentation.View(Life(0), TimedPowerUp.DoubleDamage, 700).Remaining == 1200,
                "client projects only remaining1200 ticks instead of renewing grant");
            long invalid = healthy.Telemetry.Capture().Invalid; byte[] forged = goodLink.FirstGrant!.ToArray();
            Check(NetHeader.TryRead(forged, out var header), "fault injection uses actual captured authority header");
            (header with { ConnectionId = header.ConnectionId + 1 }).Write(forged); goodLink.ToClient(forged);
            using (var stranger = new UdpClient(0)) stranger.Send(goodLink.FirstGrant!, goodLink.FirstGrant!.Length, goodLink.Client);
            Until(() => healthy.Telemetry.Capture().Invalid >= invalid + 2, 2, "wrong connection incarnation and unknown UDP sender rejected");
            Check(healthyRows.Count == 24, "forged endpoint/incarnation cannot add a client grant");
            Authority(900); var latePeer = NetGrantNetwork.Join();
            Check(NetGrantNetwork.Pump(latePeer, (type, bytes) => server.TrySendSemantic(lateLink.Address, type, bytes))
                && NetGrantNetwork.Pump(goodPeer, (type, bytes) => server.TrySendSemantic(goodLink.Address, type, bytes)), "late join baseline and existing-peer refresh admit independently");
            Until(() => lateRows.Count == 24 && healthyRows.Count == 48 && server.ReliableStats(lateLink.Address)?.Pending == 0
                && server.ReliableStats(goodLink.Address)?.Pending == 0, 3, "real late join receives24 states without stealing healthy cursor");
            Check(latePackets == 2 && lateRows.All(f => f.ContactFrame == 100 && f.EndFrame == 1900 && f.AuthorityFrame == 900)
                && latePresentation.View(Life(7), TimedPowerUp.DeathAlt, 900).Remaining == 1000,
                "late join resumes original grants at1000 remaining ticks");
            // Occupy exactly the production semantic admission limit with real
            // accepted events. The relay cuts delivery, not a callback surrogate.
            stalledLink.DropAll = true;
            var fill = healthyRows[0];
            for (uint revision = 10000; revision < 10016; revision++)
            {
                byte[] payload = new byte[TimedGrantFact.Size]; (fill with { Revision = revision }).Write(payload);
                Check(server.TrySendSemantic(stalledLink.Address, PacketType.TimedGrant, payload), "stalled peer ordinary reliable event admitted");
            }
            Authority(900); var stalledPeer = NetGrantNetwork.Join();
            Check(NetGrantNetwork.Pump(goodPeer, (type, bytes) => server.TrySendSemantic(goodLink.Address, type, bytes)), "healthy receives only its own refreshed revisions");
            Until(() => healthyRows.Count == 72 && server.ReliableStats(goodLink.Address)?.Pending == 0, 3, "healthy continues while another UDP link is cut");
            string before = StateHash(NetGrantNetwork.Presentation);
            bool applicationFailed = false; var stalledWatch = Stopwatch.StartNew();
            ulong stalledCursor = stalledPeer.Cursor.Next;
            while (stalledWatch.Elapsed.TotalSeconds < 17 && (!applicationFailed || !failureNotice))
            {
                if (!NetGrantNetwork.Pump(stalledPeer, (type, bytes) => server.TrySendSemantic(stalledLink.Address, type, bytes))) applicationFailed = true;
                Pump(); Thread.Sleep(2);
            }
            Check(applicationFailed && failureNotice && server.ReliableStats(stalledLink.Address)!.Value.Failed,
                "real15-second application and reliable delivery deadlines fail stalled peer explicitly");
            Check(stalledPeer.Baseline.Count == 2 && stalledPeer.Cursor.Next == stalledCursor && stalledPackets == 0,
                "failed stalled bootstrap neither advances retained cursor nor invents client delivery");
            Check(StateHash(NetGrantNetwork.Presentation) == before && server.ReliableStats(goodLink.Address)!.Value.Pending == 0
                && !server.ReliableStats(goodLink.Address)!.Value.Failed && healthyRows.Count == 72,
                "stalled peer failure leaves healthy confirmed grant state and reliable channel unchanged");
            report["stalled"] = new { observedSeconds = stalledWatch.Elapsed.TotalSeconds, applicationFailed, failureNotice,
                reliable = server.ReliableStats(stalledLink.Address), baselinePacketsRetained = stalledPeer.Baseline.Count,
                healthyStateBefore = before, healthyStateAfter = StateHash(NetGrantNetwork.Presentation), healthyServicePumps,
                healthyUnchanged = true };
            Authority(950); records[0].EndFrame = 950; records[0].EndReason = TimedGrantEnd.Retired; NetGrantNetwork.Changed(records[0]);
            Check(NetGrantNetwork.Pump(goodPeer, (type, bytes) => server.TrySendSemantic(goodLink.Address, type, bytes)), "post-failure retained terminal admits for healthy peer");
            Until(() => healthyRows.Count == 73 && server.ReliableStats(goodLink.Address)?.Pending == 0, 3, "healthy receives actual retained terminal after other peer failed");
            Check(NetGrantNetwork.Presentation.View(Life(0), TimedPowerUp.DoubleDamage, 950).ConfirmedRemaining == 0,
                "received terminal ends only matching confirmed effect");
            report["status"] = "PASS"; report["states"] = 24; report["initialApplicationDatagrams"] = initialPackets;
            report["wire"] = new { mtu = 1200, goodLink.GrantLengths, goodLink.GrantAttempts, goodLink.DroppedAcks,
                goodLink.ReorderedPairs, goodLink.DuplicateCopies, reliable.Retransmissions, healthyStats.Reordered };
            report["lateJoin"] = new { datagrams = latePackets, states = lateRows.Count, remainingTicks = 1000 };
            report["ports"] = links.Select(link => new { relay = link.Address.ToString(), client = link.Client.ToString(), server = link.Server.ToString() }).ToArray();
        }
        catch (Exception error) { report["error"] = error.ToString(); }
        finally
        {
            report["elapsedSeconds"] = watch.Elapsed.TotalSeconds;
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            NetSession.Stop();
        }
        Console.WriteLine("GRANT UDP " + report["status"] + " checks=" + Checks.Count + " receipt=" + output);
        return Equals(report["status"], "PASS") ? 0 : 1;
    }
}
