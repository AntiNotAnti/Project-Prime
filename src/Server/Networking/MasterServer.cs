using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Network
{
    public sealed class MasterServer
    {
        private sealed class Entry
        {
            public IPEndPoint Key = null!;
            public uint Address;
            public ushort Port;
            public byte Players;
            public byte MaxPlayers;
            public byte Mode;
            public byte Protocol;
            public NetWireFamily Family;
            public string ServerName = "";
            public string RoomKey = "";
            public double LastSeen;
        }

        /// <summary>A game this directory is running on somebody else's behalf.</summary>
        private sealed class Hosted
        {
            public ServerProcessHost? Server;
            public Task<ServerProcessHost> Startup = null!;
            public CancellationTokenSource Cancel = null!;
            public int Port;
            public string Name = "";
            /// <summary>Who asked for it, so a second request replaces it rather than piling up.</summary>
            public IPEndPoint Asker = new(IPAddress.None, 0);
            public double StartedAt;
            /// <summary>When it last had anybody in it, so an abandoned game can be reaped.</summary>
            public double LastOccupied;
        }

        private readonly int _port;
        private readonly List<Entry> _entries = new();
        private NetRateLimit _queries = new(5, 10, 0);
        private NetRateLimit _heartbeats = new(64, 128, 0);
        private NetRateLimit _hostRequests = new(2, 8, 0);
        private readonly List<Hosted> _hosted = new();
        private readonly byte[] _scratch = new byte[NetConfig.MaxPacketSize];
        private UdpTransport? _transport;
        private volatile bool _running;
        private bool _admissionClosed;
        public Update.ServerUpdateRuntime? Updates { get; init; }
        private readonly System.Diagnostics.Stopwatch _clock = new();
        private uint _publicAddress;
        private string _publicName = "";
        private int _hostPortFirst;
        private int _hostPortLast = -1;
        private string? _hostData;
        private string _hostVersion = "AMHE1";
        /// <summary>Ports just given up, and when. See <see cref="FreeHostPort"/>.</summary>
        private readonly Dictionary<int, double> _cooling = new();

        /// <summary>
        /// How long a port sits idle after a game on it ends.
        ///
        /// Two reasons, and both are races that only show up when somebody
        /// quits and immediately hosts again -- which is exactly what a player
        /// does when the first attempt did not go how they wanted. The socket
        /// takes a moment to come back after the server lets go of it, and the
        /// old server's goodbye is still in flight and would otherwise unlist
        /// its successor on the same port. With twenty ports there is no
        /// reason to be in a hurry.
        /// </summary>
        private const double PortCooldownSeconds = 5;

        /// <summary>
        /// How long a game the directory started may sit empty *before anybody
        /// has ever joined it*.
        ///
        /// Generous, because the reason it is empty is that the person who
        /// asked for it is still loading the map -- which on a cold cache is
        /// not fast -- and shutting it down underneath them would be worse
        /// than holding a port for a few minutes.
        /// </summary>
        private const double HostedStartupSeconds = 180;

        /// <summary>
        /// And how long once it *has* been played and everyone has left.
        ///
        /// Much shorter, because at that point the answer is known: the match
        /// is over. Not instant, though -- a client loading the next room
        /// sends nothing while it does, and the server drops a silent peer
        /// after NetConfig.TimeoutSeconds, so a player mid-load can briefly
        /// leave the game reading as empty. This has to outlast that plus the
        /// client's own re-announce, or a slow load would end the match.
        /// </summary>
        private const double HostedEmptySeconds = 45;

        /// <summary>
        /// The range of ports this directory may start games on.
        ///
        /// A range rather than one port because each game is an ordinary
        /// dedicated server with its own listener, which is what lets a player
        /// join it with no client changes at all: as far as their launcher is
        /// concerned it is simply a server at an address. The operator opens
        /// the range once.
        /// </summary>
        public void SetHostPorts(int first, int last)
        {
            if (first < 1 || last < first || last > UInt16.MaxValue || last - first >= 64)
            {
                throw new ArgumentOutOfRangeException(nameof(first), "Use a range of 1–64 valid UDP host ports.");
            }
            _hostPortFirst = first;
            _hostPortLast = last;
        }

        /// <summary>
        /// Explicit operator content for child matches. Directory-only operation
        /// neither loads this content nor requires a local game installation.
        /// </summary>
        public void SetHostContent(string directory, string version)
        {
            if (String.IsNullOrWhiteSpace(directory) || !System.IO.Directory.Exists(directory))
            {
                throw new ArgumentException("Hosted games require an extracted game directory or server content package.", nameof(directory));
            }
            if (version is not ("AMHE0" or "AMHE1" or "AMHP0" or "AMHP1" or "AMHJ0" or "AMHJ1" or "AMHK0"))
            {
                throw new ArgumentException("Unsupported hosted game data version: " + version, nameof(version));
            }
            _hostData = System.IO.Path.GetFullPath(directory);
            _hostVersion = version;
        }

        public bool CanHost => _hostPortLast >= _hostPortFirst && _hostPortFirst > 0 && _hostData != null;
        public int BoundPort => _transport?.LocalPort ?? 0;

        public MasterServer(int port = NetMasterConfig.DefaultPort)
        {
            _port = port;
        }

        /// <summary>
        /// The address to publish for servers that register from this machine
        /// or from the same private network.
        ///
        /// The address in a listing is normally the one the heartbeat arrived
        /// from, which is what makes a server behind a router announce the
        /// address players can actually reach. That rule gets the answer
        /// exactly backwards for the server sharing a box with the directory:
        /// its heartbeat arrives from 127.0.0.1, and a list handing out
        /// 127.0.0.1 sends every player to their own machine. The directory is
        /// the one party that can be told the truth here once, by whoever set
        /// it up, so it is.
        /// </summary>
        public bool SetPublicAddress(string host)
        {
            try
            {
                IPAddress[] resolved = Dns.GetHostAddresses(host);
                IPAddress? ipv4 = Array.Find(resolved,
                    a => a.AddressFamily == AddressFamily.InterNetwork);
                if (ipv4 == null)
                {
                    Log($"cannot publish local servers as \"{host}\": no IPv4 address");
                    return false;
                }
                _publicAddress = ToUInt32(ipv4);
                _publicName = $"{host} ({ipv4})";
                return true;
            }
            catch (Exception ex)
            {
                Log($"cannot publish local servers as \"{host}\": {ex.Message}");
                return false;
            }
        }

        private static uint ToUInt32(IPAddress address)
        {
            byte[] octets = address.GetAddressBytes();
            return ((uint)octets[0] << 24) | ((uint)octets[1] << 16)
                | ((uint)octets[2] << 8) | octets[3];
        }

        /// <summary>
        /// Whether an address is one only this machine or this LAN can reach,
        /// and therefore one no listing should ever carry.
        /// </summary>
        private static bool IsLocal(IPAddress address)
        {
            if (IPAddress.IsLoopback(address))
            {
                return true;
            }
            byte[] o = address.GetAddressBytes();
            return o[0] == 10
                || (o[0] == 172 && o[1] >= 16 && o[1] <= 31)
                || (o[0] == 192 && o[1] == 168)
                || (o[0] == 169 && o[1] == 254);
        }

        public void Stop() => _running = false;

        public void Run(CancellationToken cancel = default)
        {
            _transport = new UdpTransport(_port);
            _running = true;
            Log($"listening on UDP {_transport.LocalPort}");
            Log($"servers are dropped after {NetMasterConfig.ExpirySeconds:0} s of silence");
            if (_publicAddress != 0)
            {
                Log($"servers on this machine are listed as {_publicName}");
            }
            Log(CanHost
                ? $"can start games on ports {_hostPortFirst}-{_hostPortLast} "
                    + "for players who cannot open one of their own"
                : "directory only: hosted games require a host port range and explicit game content");
            _clock.Restart();
            double lastReport = 0;
            try
            {
                while (_running && !cancel.IsCancellationRequested)
                {
                    double now = _clock.Elapsed.TotalSeconds;
                    foreach (ReceivedPacket packet in _transport.Drain())
                    {
                        Handle(packet, now);
                    }
                    Expire(now);
                    ReapHosted(now);
                    if (Updates?.PollIdle(() => _hosted.Count == 0,
                        () => _admissionClosed = true, () => _admissionClosed = false) == true)
                    {
                        _running = false;
                        break;
                    }
                    if (now - lastReport >= 60)
                    {
                        lastReport = now;
                        Log($"{_entries.Count} server(s) listed"
                            + (_hosted.Count > 0 ? $", {_hosted.Count} started here" : ""));
                    }
                    // Nothing here is time-critical: a heartbeat every fifteen
                    // seconds and a query whenever somebody opens a launcher.
                    Thread.Sleep(20);
                }
            }
            finally
            {
                Log("shutting down");
                for (int i = _hosted.Count - 1; i >= 0; i--)
                {
                    StopHosted(_hosted[i], "the directory is shutting down");
                }
                _transport.Dispose();
                _transport = null;
            }
        }

        private void Handle(ReceivedPacket packet, double now)
        {
            if (packet.Type == PacketType.MasterHeartbeat)
            {
                if (_heartbeats.Take(now)) { HandleHeartbeat(packet, now); }
            }
            else if (packet.Type == PacketType.MasterQuery)
            {
                if (packet.Payload.Length == 1 && _queries.Take(now)) { SendList(packet.Sender); }
            }
            else if (packet.Type == PacketType.HostRequest)
            {
                if (!_admissionClosed && _hostRequests.Take(now)) { HandleHostRequest(packet, now); }
            }
            else if (packet.Type == PacketType.Bye)
            {
                HandleFarewell(packet);
            }
        }

        /// <summary>A server saying it is stopping. Take it off the list now.</summary>
        private void HandleFarewell(ReceivedPacket packet)
        {
            if (packet.Payload.Length != 2)
            {
                return;
            }
            ushort port = System.Buffers.Binary.BinaryPrimitives
                .ReadUInt16LittleEndian(packet.Payload);
            var key = new IPEndPoint(packet.Sender.Address, port);
            Entry? entry = _entries.Find(e => e.Key.Equals(key));
            if (entry != null)
            {
                Log($"- {entry.Key} \"{entry.ServerName}\" (said goodbye)");
                _entries.Remove(entry);
            }
        }

        /// <summary>
        /// Start a game here, on this machine's reachable port, and tell the
        /// asker where it is.
        ///
        /// Everything about the resulting server is ordinary -- it registers
        /// itself in the listing, answers status queries, rotates its own
        /// single map, and is joined by a client that has no idea it was
        /// started this way. The only thing that makes it special is where it
        /// is running, which is the entire point: the host reaches it by
        /// connecting outwards, so their router never has to let anything in.
        /// </summary>
        private void HandleHostRequest(ReceivedPacket packet, double now)
        {
            var reply = new HostReplyPacket();
            if (!HostRequestPacket.TryRead(packet.Payload, out HostRequestPacket request))
            {
                reply.Reason = "malformed request";
            }
            else
            {
                if (!NetWireIdentity.IsCompatible(request.Family, request.Protocol))
                {
                    reply.Reason = NetWireIdentity.IncompatibilityReason(request.Family, request.Protocol);
                }
                else if (!CanHost)
                {
                    reply.Reason = _hostData == null
                        ? "hosting is disabled: the directory has no configured game content"
                        : "this directory does not start games";
                }
                else
                {
                    HostReplyPacket? result = StartHosted(request, packet.Sender, now);
                    if (result == null) { return; } // Reply when the child is ready; keep serving the directory.
                    reply = result.Value;
                }
            }
            reply.Write(_scratch);
            _transport?.Send(packet.Sender, PacketType.HostReply,
                _scratch.AsSpan(0, HostReplyPacket.Size));
            if (!reply.Started)
            {
                Log($"refused a game for {packet.Sender}: {reply.Reason}");
            }
        }

        private HostReplyPacket? StartHosted(HostRequestPacket request, IPEndPoint asker, double now)
        {
            // One game per host. Somebody who quits and asks again is asking
            // for a *replacement*, not a second one -- and the old one is
            // sitting there empty, holding a port and a row on everybody's
            // list. Two attempts used to leave two of them.
            //
            // Only if it is empty, though: two people behind one router share
            // an address, and the second of them starting a game must not
            // throw the first out of theirs.
            for (int i = _hosted.Count - 1; i >= 0; i--)
            {
                Hosted previous = _hosted[i];
                if (previous.Asker.Address.Equals(asker.Address))
                {
                    if (previous.Server == null)
                    {
                        return new HostReplyPacket { Reason = "a game for this address is still starting" };
                    }
                    if (previous.Server.PeerCount == 0 && now - previous.LastOccupied >= HostedEmptySeconds)
                    {
                        StopHosted(previous, "the same player asked for another game");
                    }
                }
            }
            int port = FreeHostPort(now);
            if (port < 0)
            {
                return new HostReplyPacket
                {
                    Reason = $"all {_hostPortLast - _hostPortFirst + 1} game slots are busy"
                };
            }
            GameMode mode = Enum.IsDefined(typeof(GameMode), request.Mode)
                ? (GameMode)request.Mode
                : GameMode.Battle;
            string name = request.ServerName.Length > 0 ? request.ServerName : "Hosted game";
            var rotation = MapRotation.SingleMatch(request.RoomKey, mode,
                request.TimeLimit, request.PointGoal);
            var cancel = new CancellationTokenSource();
            int directoryPort = _transport!.LocalPort;
            var entry = new Hosted
            {
                Cancel = cancel,
                Port = port,
                Name = name,
                Asker = asker,
                StartedAt = now,
                LastOccupied = now,
                Startup = ServerProcessHost.StartAsync(_hostData!, _hostVersion, rotation, port,
                    Math.Clamp((int)request.MaxPlayers, 2, MphRead.Entities.PlayerEntity.SlotCapacity),
                    friendlyFire: false, listing: ("127.0.0.1", directoryPort, name), cancel: cancel.Token)
            };
            _hosted.Add(entry); // Reserve the port before processing another request.
            Log($"starting \"{name}\" on port {port} for {asker.Address} ({request.RoomKey}, {mode})");
            return null;
        }

        private int FreeHostPort(double now)
        {
            for (int port = _hostPortFirst; port <= _hostPortLast; port++)
            {
                bool taken = false;
                for (int i = 0; i < _hosted.Count; i++)
                {
                    if (_hosted[i].Port == port)
                    {
                        taken = true;
                        break;
                    }
                }
                if (taken)
                {
                    continue;
                }
                if (_cooling.TryGetValue(port, out double freedAt))
                {
                    if (now - freedAt < PortCooldownSeconds)
                    {
                        continue;
                    }
                    _cooling.Remove(port);
                }
                return port;
            }
            return -1;
        }

        /// <summary>
        /// Shut down games nobody is playing. Without this a directory left
        /// running for a week is a directory with every port allocated to a
        /// match that ended on Tuesday.
        /// </summary>
        private void ReapHosted(double now)
        {
            for (int i = _hosted.Count - 1; i >= 0; i--)
            {
                Hosted entry = _hosted[i];
                if (entry.Server == null)
                {
                    if (!entry.Startup.IsCompleted) { continue; }
                    HostReplyPacket reply;
                    try
                    {
                        entry.Server = entry.Startup.GetAwaiter().GetResult();
                        entry.LastOccupied = now;
                        reply = new HostReplyPacket { Started = true, Port = (ushort)entry.Server.Port, Reason = "" };
                        Log($"started \"{entry.Name}\" on port {entry.Server.Port}");
                    }
                    catch (Exception ex)
                    {
                        reply = new HostReplyPacket { Reason = "server startup failed: " + ex.Message };
                        Log($"game on {entry.Port} failed: {ex.Message}");
                    }
                    reply.Write(_scratch);
                    _transport?.Send(entry.Asker, PacketType.HostReply, _scratch.AsSpan(0, HostReplyPacket.Size));
                    if (entry.Server == null)
                    {
                        StopHosted(entry, "startup failed");
                        continue;
                    }
                }
                if (!entry.Server.Running)
                {
                    StopHosted(entry, "the server process exited");
                    continue;
                }
                if (entry.Server.PeerCount > 0)
                {
                    entry.LastOccupied = now;
                    continue;
                }
                bool played = entry.Server.EverOccupied;
                double grace = played ? HostedEmptySeconds : HostedStartupSeconds;
                if (now - entry.LastOccupied > grace)
                {
                    StopHosted(entry, played ? "everyone left" : "nobody joined");
                }
            }
        }

        private void StopHosted(Hosted entry, string why)
        {
            Log($"stopping \"{entry.Name}\" on port {entry.Port}: {why}");
            entry.Cancel.Cancel();
            try
            {
                // Cancellation wakes a pending startup immediately; it owns and
                // shuts down its child before completing the task.
                ServerProcessHost server = entry.Server ?? entry.Startup.GetAwaiter().GetResult();
                server.Dispose();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log($"game on {entry.Port} stopped: {ex.Message}"); }
            entry.Cancel.Dispose();
            _hosted.Remove(entry);
            _cooling[entry.Port] = _clock.Elapsed.TotalSeconds;
            Unlist(entry.Port);
        }

        /// <summary>Drop the listing for a server on this machine's port.</summary>
        private void Unlist(int port)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Port == port && IPAddress.IsLoopback(_entries[i].Key.Address))
                {
                    _entries.RemoveAt(i);
                }
            }
        }

        private void HandleHeartbeat(ReceivedPacket packet, double now)
        {
            if (!MasterHeartbeatPacket.TryRead(packet.Payload, out MasterHeartbeatPacket beat))
            {
                return;
            }
            if (packet.Sender.Address.AddressFamily != AddressFamily.InterNetwork)
            {
                return;
            }
            // The address a player has to dial is the one this datagram came
            // from, not the one the server believes it has: a server behind a
            // router knows only its private address, and a directory full of
            // 192.168 entries would be a list of servers nobody can reach.
            uint address = ToUInt32(packet.Sender.Address);
            if (_publicAddress != 0 && IsLocal(packet.Sender.Address))
            {
                // See SetPublicAddress: a server sharing this box announces
                // itself over the loopback, and nobody else can dial that.
                address = _publicAddress;
            }
            ushort port = beat.Port != 0 ? beat.Port : (ushort)packet.Sender.Port;
            var key = new IPEndPoint(packet.Sender.Address, port);
            Entry? entry = _entries.Find(e => e.Key.Equals(key));
            if (entry == null)
            {
                if (_entries.Count >= 255) { return; }
                entry = new Entry { Key = key };
                _entries.Add(entry);
                Log($"+ {key} \"{beat.ServerName}\"");
            }
            entry.Address = address;
            entry.Port = port;
            entry.Players = beat.Players;
            entry.MaxPlayers = beat.MaxPlayers;
            entry.Mode = beat.Mode;
            entry.Protocol = beat.Protocol;
            entry.Family = beat.Family;
            entry.ServerName = beat.ServerName;
            entry.RoomKey = beat.RoomKey;
            entry.LastSeen = now;
        }

        private void Expire(double now)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (now - _entries[i].LastSeen > NetMasterConfig.ExpirySeconds)
                {
                    Log($"- {_entries[i].Key} \"{_entries[i].ServerName}\"");
                    _entries.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Answer a query, in as many datagrams as the list needs.
        ///
        /// Each carries how many servers are in it and how many there are
        /// altogether, so a launcher knows whether it has the lot without the
        /// master having to keep any per-asker state -- which is what lets it
        /// answer a query from a client it will never hear from again.
        /// </summary>
        private void SendList(IPEndPoint sender)
        {
            int perPacket = NetMasterConfig.EntriesPerPacket;
            int total = Math.Min(_entries.Count, 255);
            int sent = 0;
            do
            {
                int count = Math.Min(perPacket, total - sent);
                _scratch[0] = (byte)count;
                _scratch[1] = (byte)total;
                _scratch[2] = (byte)NetWireIdentity.Family;
                _scratch[3] = NetHeader.Version;
                int offset = 4;
                for (int i = 0; i < count; i++)
                {
                    Entry entry = _entries[sent + i];
                    var wire = new MasterEntryPacket
                    {
                        Address = entry.Address,
                        Port = entry.Port,
                        Players = entry.Players,
                        MaxPlayers = entry.MaxPlayers,
                        Mode = entry.Mode,
                        Protocol = entry.Protocol,
                        Family = entry.Family,
                        ServerName = entry.ServerName,
                        RoomKey = entry.RoomKey
                    };
                    wire.Write(_scratch.AsSpan(offset));
                    offset += MasterEntryPacket.Size;
                }
                _transport?.Send(sender, PacketType.MasterList, _scratch.AsSpan(0, offset));
                sent += count;
            }
            while (sent < total);
        }

        private static void Log(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [master] {message}");
        }
    }

}
