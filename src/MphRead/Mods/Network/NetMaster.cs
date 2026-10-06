using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Where a dedicated server announces itself, and where a launcher asks
    /// who is up.
    ///
    /// A player with a server address can already join it; what they could
    /// not do was find one. The directory is deliberately the smallest thing
    /// that fixes that: servers say "I am here" every few seconds over UDP, a
    /// list is kept in memory, entries that stop talking are forgotten, and a
    /// launcher gets the list back in one datagram. No database, no accounts,
    /// no HTTP -- it runs beside the game server on the same Raspberry Pi and
    /// survives a restart by simply being repopulated within a few seconds.
    ///
    /// Nothing about a match passes through here. The master never sees a
    /// player, a position or a shot: a launcher that has the list talks to
    /// each server directly, which is also how it measures a latency worth
    /// showing -- the master's own round trip to a server is not the number
    /// the person reading the screen cares about.
    /// </summary>
    public static class NetMasterConfig
    {
        /// <summary>
        /// The directory a server reports to unless told otherwise.
        ///
        /// A hostname rather than an address, on purpose and unlike the
        /// default game server: this one is a service the project runs, and
        /// pointing it somewhere else has to be possible without shipping a
        /// new build to every server operator.
        /// </summary>
        public const string DefaultHost = "51.161.113.128";

        /// <summary>
        /// Beside the game port rather than on it: a machine can then run
        /// both the directory and a server people play on, which is exactly
        /// the arrangement this was written for.
        /// </summary>
        public const ushort DefaultPort = 27889;

        /// <summary>Seconds between heartbeats.</summary>
        public const double HeartbeatSeconds = 15.0;

        /// <summary>
        /// How long a server stays listed after its last heartbeat. Several
        /// heartbeats' worth, so a server does not vanish from the list
        /// because one datagram went missing.
        /// </summary>
        public const double ExpirySeconds = 50.0;

        /// <summary>
        /// How many servers fit in one reply. The packet cap is 1024 bytes
        /// and an entry is 82, so this is what the datagram holds rather than
        /// a policy about how many servers may exist.
        /// </summary>
        public static int EntriesPerPacket =>
            (NetConfig.MaxPacketSize - 1 - 2) / MasterEntryPacket.Size;
    }

    /// <summary>
    /// The dedicated server's end of the arrangement: one datagram every few
    /// seconds saying what this server is running.
    ///
    /// Failure here is never allowed to matter. A directory that is down, a
    /// name that does not resolve, a network that drops the packet -- none of
    /// it should touch a match in progress, so everything is swallowed and
    /// the next beat simply tries again. The one thing worth reporting is the
    /// first failure, once, so an operator who expected to be listed can see
    /// why they are not.
    /// </summary>
    public sealed class MasterReporter : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private UdpClient? _socket;
        private IPEndPoint? _endPoint;
        private double _lastBeat = Double.NegativeInfinity;
        private double _lastResolve = Double.NegativeInfinity;
        private readonly CancellationTokenSource _resolveCancellation = new();
        private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolveAddresses;
        private Task<IPAddress[]>? _resolution;
        private bool _disposed;
        private readonly byte[] _datagram = new byte[1 + MasterHeartbeatPacket.Size];
        private bool _complained;
        private readonly byte[] _scratch = new byte[MasterHeartbeatPacket.Size];

        public MasterReporter(string host, int port)
        {
            _host = host;
            _port = port;
            _resolveAddresses = (name, cancel) => Dns.GetHostAddressesAsync(name, cancel);
        }

        internal MasterReporter(string host, int port,
            Func<string, CancellationToken, Task<IPAddress[]>> resolver) : this(host, port)
            => _resolveAddresses = resolver;

        /// <summary>
        /// Where this one reports, so another can be made pointing at the same
        /// directory.
        ///
        /// A reporter owns a socket and its own heartbeat clock, so a match a
        /// server opens for somebody needs one of its own rather than a share
        /// of this one -- it is a different server, on a different port, with
        /// a different name.
        /// </summary>
        public string Host => _host;
        public int Port => _port;

        /// <summary>Announce, if enough time has passed since the last one.</summary>
        public void Beat(double now, string serverName, ushort port, byte players,
            byte maxPlayers, byte mode, string roomKey)
        {
            if (_disposed) return;
            try { Resolve(now); } // Poll detached DNS even between heartbeat deadlines.
            catch (Exception ex) { Complain(ex.Message); }
            if (now - _lastBeat < NetMasterConfig.HeartbeatSeconds)
            {
                return;
            }
            try
            {
                if (_endPoint == null)
                {
                    return;
                }
                var beat = new MasterHeartbeatPacket
                {
                    Protocol = NetConfig.ProtocolVersion,
                    Port = port,
                    Players = players,
                    MaxPlayers = maxPlayers,
                    Mode = mode,
                    ServerName = serverName,
                    RoomKey = roomKey
                };
                beat.Write(_scratch);
                _datagram[0] = (byte)PacketType.MasterHeartbeat;
                _scratch.CopyTo(_datagram, 1);
                _socket!.Send(_datagram, _datagram.Length, _endPoint);
                _lastBeat = now;
            }
            catch (Exception ex)
            {
                Complain(ex.Message);
            }
        }

        /// <summary>
        /// Tell the directory this server is going away.
        ///
        /// Without it the only way a directory learns a server is gone is
        /// fifty seconds of missing heartbeats, so a server somebody stopped
        /// on purpose stays on everyone's list for the best part of a minute
        /// -- offered, unreachable, and looking exactly like a broken one.
        /// A small idempotent burst is sent without waiting for an acknowledgement;
        /// if all copies go missing, the normal silence expiry still handles it.
        /// </summary>
        public void Farewell(ushort port)
        {
            if (_socket == null || _endPoint == null)
            {
                return;
            }
            try
            {
                var datagram = new byte[3];
                datagram[0] = (byte)PacketType.Bye;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                    datagram.AsSpan(1), port);
                // Removal is idempotent and this packet is deliberately not
                // acknowledged. Send a tiny burst so one lost UDP datagram does
                // not leave an unreachable server advertised until expiry.
                for (int i = 0; i < 3; i++)
                {
                    _socket.Send(datagram, datagram.Length, _endPoint);
                }
            }
            catch (Exception)
            {
                // Shutting down; there is nobody left to tell.
            }
        }

        /// <summary>
        /// Resolve the directory's name, and do it again now and then.
        ///
        /// Not once at startup: a server is expected to run for weeks, and
        /// the whole reason the default is a hostname is that where it points
        /// can change. An hour is far more often than that happens and far
        /// less often than it would cost anything.
        /// </summary>
        private void Resolve(double now)
        {
            if (_resolution?.IsCompleted == true)
            {
                Task<IPAddress[]> resolution = _resolution;
                _resolution = null;
                try
                {
                    IPAddress? ipv4 = Array.Find(resolution.GetAwaiter().GetResult(),
                        a => a.AddressFamily == AddressFamily.InterNetwork);
                    if (ipv4 == null) Complain($"{_host} has no IPv4 address");
                    else
                    {
                        _endPoint = new IPEndPoint(ipv4, _port);
                        _socket ??= new UdpClient(AddressFamily.InterNetwork);
                        _complained = false;
                    }
                }
                catch (Exception ex) { Complain(ex.Message); }
            }
            double retry = _endPoint == null ? NetMasterConfig.HeartbeatSeconds : 3600;
            if (_resolution != null || now - _lastResolve < retry) return;
            _lastResolve = now;
            if (IPAddress.TryParse(_host, out IPAddress? literal))
            {
                if (literal.AddressFamily != AddressFamily.InterNetwork)
                { Complain($"{_host} has no IPv4 address"); return; }
                _endPoint = new IPEndPoint(literal, _port);
                _socket ??= new UdpClient(AddressFamily.InterNetwork);
                return;
            }
            try
            {
                // The worker owns only a hostname/token, never match or Scene state.
                _resolution = _resolveAddresses(_host, _resolveCancellation.Token);
                // Disposal never waits for DNS; faults are still observed if no next Beat occurs.
                _ = _resolution.ContinueWith(task => { _ = task.Exception; },
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted
                    | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            catch (Exception ex) { Complain(ex.Message); }
        }

        private void Complain(string message)
        {
            if (_complained)
            {
                return;
            }
            _complained = true;
            Console.WriteLine($"[master] not listed on {_host}:{_port} -- {message}");
            Console.WriteLine("[master] the server is running normally; "
                + "pass -nomaster to stop trying, or -master HOST to point elsewhere");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _resolveCancellation.Cancel();
            _resolveCancellation.Dispose();
            _socket?.Dispose();
            _socket = null;
        }
    }

    /// <summary>
    /// The directory itself: <c>MphRead -master</c>.
    ///
    /// Deliberately part of this binary rather than a separate tool. The
    /// machine that runs it already has the server on it, the two speak the
    /// same packet definitions, and one file to deploy is the difference
    /// between a service somebody keeps running and one they mean to set up.
    /// </summary>
    public sealed class MasterServer
    {
        private readonly int _port;
        private readonly MasterDirectory _directory = new();
        private List<MasterDirectory.Entry> _entries => _directory.Entries;
        private readonly byte[] _scratch = new byte[NetConfig.MaxPacketSize];
        private NetTransport? _transport;
        private volatile bool _running;
        private readonly HostedMapRequests _mapRequests = new();
        private readonly HostPool _hosts;
        private readonly System.Diagnostics.Stopwatch _clock = new();
        private uint _publicAddress;
        private string _publicName = "";
        private bool _hostingReady = true;
        private string _hostingUnavailable = "";

        /// <summary>
        /// The range of ports this directory may start games on.
        ///
        /// A range rather than one port because each game is an ordinary
        /// dedicated server with its own listener, which is what lets a player
        /// join it with no client changes at all: as far as their launcher is
        /// concerned it is simply a server at an address. The operator opens
        /// the range once.
        /// </summary>
        public void SetHostPorts(int first, int last) => _hosts.SetPorts(first, last);

        public bool CanHost => _hosts.CanHost && _hostingReady;

        public MasterServer(int port = NetMasterConfig.DefaultPort)
        {
            _port = port;
            _hosts = new HostPool
            {
                Log = Log,
                ListingTarget = () => ("127.0.0.1", _port),
                // The directory owns the authoritative list and can remove a
                // hosted child synchronously when its pool reaps it.
                OnStopped = Unlist
            };
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
            try
            {
                _transport = new NetTransport(_port);
                _transport.EnableDirectoryFarewells();
                _running = true;
                Log($"listening on UDP {_transport.LocalPort}");
                Log($"servers are dropped after {NetMasterConfig.ExpirySeconds:0} s of silence");
                if (_publicAddress != 0)
                {
                    Log($"servers on this machine are listed as {_publicName}");
                }
                if (_hosts.CanHost)
                {
                    string? gameProblem = Launcher.GameFiles.Problem();
                    _hostingReady = LocalServer.Ready && gameProblem == null;
                    if (!_hostingReady)
                    {
                        _hostingUnavailable = !LocalServer.Ready
                            ? "no dedicated-server executable is available"
                            : gameProblem ?? "authoritative game files are unavailable";
                        Log("hosted lobby allocation disabled: " + _hostingUnavailable);
                    }
                }
                Log(CanHost ? _hosts.Describe()
                    : _hosts.CanHost
                        ? "not starting games: " + _hostingUnavailable
                        : _hosts.Describe());
                _clock.Restart();
                double lastReport = 0;
                double lastHostReap = double.NegativeInfinity;
                double lastMapPump = double.NegativeInfinity;
                double lastUpdateCheck = double.NegativeInfinity;
                while (_running && !cancel.IsCancellationRequested)
                {
                    double now = _clock.Elapsed.TotalSeconds;
                    foreach (ReceivedPacket packet in _transport.Drain())
                    {
                        Handle(packet, now);
                    }
                    Expire(now);
                    if (now - lastHostReap >= 1.0)
                    {
                        lastHostReap = now;
                        _hosts.Reap(now);
                    }
                    if (now - lastMapPump >= 0.05)
                    {
                        lastMapPump = now;
                        _mapRequests.Pump(now,
                            (request, sender, time, packages) => _hosts.Start(request, sender, time, packages),
                            SendHostReply);
                    }
                    // The directory keeps itself current too, and waits on the
                    // matches it is running rather than on the servers it lists:
                    // a listed server re-announces every fifteen seconds, so the
                    // list rebuilds itself within a restart, but a hosted match
                    // lives in this process and a restart ends it.
                    if (now - lastUpdateCheck >= 1.0)
                    {
                        lastUpdateCheck = now;
                        if (Update.ServerUpdate.ShouldRestart(_hosts.Count + _mapRequests.ActiveCount))
                        {
                            Log("shutting down to come back on the new build");
                            _running = false;
                            break;
                        }
                    }
                    if (now - lastReport >= 60)
                    {
                        lastReport = now;
                        Log($"{_entries.Count} server(s) listed"
                            + (_hosts.Count > 0 ? $", {_hosts.Count} started here" : ""));
                    }
                    // UDP receive owns its blocking worker. Sleep this lightweight
                    // allocator until traffic arrives, with a bounded maintenance
                    // deadline for map completions, reaping and update checks.
                    _transport.WaitForActivity(50);
                }
            }
            finally
            {
                _running = false;
                Log("shutting down");
                try { _mapRequests.Dispose(); }
                catch (Exception ex) { Log("cleanup map requests: " + ex.Message); }
                try { _hosts.StopAll("the directory is shutting down"); }
                catch (Exception ex) { Log("cleanup hosted children: " + ex.Message); }
                try { _transport?.Dispose(); }
                finally { _transport = null; }
            }
        }

        private void Handle(ReceivedPacket packet, double now)
        {
            if (packet.Type == PacketType.MasterHeartbeat)
            {
                HandleHeartbeat(packet, now);
            }
            else if (packet.Type == PacketType.MasterQuery)
            {
                if (_directory.AllowQuery(packet.Sender.Address, now)) SendList(packet.Sender);
            }
            else if (packet.Type == PacketType.HostChallenge)
            {
                HandleHostChallenge(packet, now);
            }
            else if (packet.Type == PacketType.HostChallengeReply)
            {
                if (HostChallengeReplyPacket.TryRead(packet.Payload, out var proof))
                    _directory.Complete(packet.Sender, proof.Nonce, now);
            }
            else if (packet.Type == PacketType.HostRequest)
            {
                HandleHostRequest(packet, now);
            }
            else if (packet.Type == PacketType.Bye)
            {
                HandleFarewell(packet);
            }
        }

        private void HandleHostChallenge(ReceivedPacket packet, double now)
        {
            if (!HostChallengePacket.TryRead(packet.Payload, out var challenge)
                || challenge.Protocol != NetConfig.ProtocolVersion)
                return;
            HostChallengeReplyPacket reply =
                HostRequestGuard.Challenge(packet.Sender, challenge, now);
            reply.Write(_scratch);
            _transport?.Send(packet.Sender, PacketType.HostChallengeReply,
                _scratch.AsSpan(0, HostChallengeReplyPacket.Size));
        }

        /// <summary>A server saying it is stopping. Take it off the list now.</summary>
        private void HandleFarewell(ReceivedPacket packet)
        {
            if (packet.Payload.Length < 2)
            {
                return;
            }
            ushort port = System.Buffers.Binary.BinaryPrimitives
                .ReadUInt16LittleEndian(packet.Payload);
            _directory.Farewell(packet.Sender, port);
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
            if (packet.Payload.Length < HostRequestPacket.Size)
            {
                reply.Reason = "malformed request";
            }
            else if (packet.Payload[0] != NetConfig.ProtocolVersion)
            {
                reply.Reason = $"this directory speaks protocol {NetConfig.ProtocolVersion}, "
                    + $"your build speaks {packet.Payload[0]}";
            }
            else
            {
                HostRequestPacket request = HostRequestPacket.Read(packet.Payload);
                if (request.Protocol != NetConfig.ProtocolVersion)
                {
                    reply.Reason = "malformed request";
                }
                else if (!CanHost)
                {
                    reply.Reason = "this directory does not start games";
                }
                else if (!HostRequestGuard.Validate(packet.Sender, request, now,
                    out string admissionReason))
                {
                    reply.Reason = admissionReason;
                }
                else
                {
                    try
                    {
                        if (request.RequiresMapPreparation)
                        {
                            _mapRequests.Enqueue(request, packet.Sender, now, SendHostReply);
                            return;
                        }
                        reply = _hosts.Start(request, packet.Sender, now);
                    }
                    catch (Exception ex) { reply.Reason = ex.Message; }
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

        private void SendHostReply(IPEndPoint sender, HostReplyPacket reply)
        {
            reply.Write(_scratch);
            _transport?.Send(sender, PacketType.HostReply, _scratch.AsSpan(0, HostReplyPacket.Size));
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
            if (packet.Payload.Length < MasterHeartbeatPacket.Size)
            {
                return;
            }
            MasterHeartbeatPacket beat = MasterHeartbeatPacket.Read(packet.Payload);
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
            if (beat.Protocol != NetConfig.ProtocolVersion || port == 0
                || beat.MaxPlayers is < 1 or > 8 || beat.Players > beat.MaxPlayers
                || !Enum.IsDefined(typeof(GameMode), beat.Mode)) return;
            var entry = new MasterDirectory.Entry
            {
                Key = key, Reporter = packet.Sender, Address = address, Port = port,
                Players = beat.Players, MaxPlayers = beat.MaxPlayers, Mode = beat.Mode,
                Protocol = beat.Protocol, ServerName = beat.ServerName, RoomKey = beat.RoomKey
            };
            if (!_directory.Begin(entry, now, out ulong nonce)) return;
            new HostChallengePacket(NetConfig.ProtocolVersion, nonce).Write(_scratch);
            _transport?.Send(key, PacketType.HostChallenge,
                _scratch.AsSpan(0, HostChallengePacket.Size));
        }

        private void Expire(double now) => _directory.Expire(now);

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
                int offset = 2;
                for (int i = 0; i < count; i++)
                {
                    MasterDirectory.Entry entry = _entries[sent + i];
                    var wire = new MasterEntryPacket
                    {
                        Address = entry.Address,
                        Port = entry.Port,
                        Players = entry.Players,
                        MaxPlayers = entry.MaxPlayers,
                        Mode = entry.Mode,
                        Protocol = entry.Protocol,
                        ServerName = entry.ServerName,
                        RoomKey = entry.RoomKey
                    };
                    wire.Write(_scratch.AsSpan(offset));
                    offset += MasterEntryPacket.Size;
                }
                // What this directory can do, after the entries rather than
                // in front of them. A launcher built before the byte existed
                // stops reading once it has taken `count` entries and never
                // sees it, so nothing needed a protocol bump -- and a launcher
                // that does read it can tell "no directory answered" from "the
                // directory answered and does not start games", which are the
                // same empty list and completely different problems.
                _scratch[offset++] = (byte)(CanHost ? MasterFlags.CanHost : 0);
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

    /// <summary>
    /// One row of a server browser, before its latency is measured.
    ///
    /// Null-safe strings for the same reason <see cref="ServerStatus"/> has
    /// them: this is a struct, so a caller can be holding a default one.
    /// </summary>
    public readonly struct MasterListing
    {
        private readonly string? _address;
        private readonly string? _serverName;
        private readonly string? _roomKey;

        public string Address
        {
            get => _address ?? "";
            init => _address = value;
        }
        public int Port { get; init; }
        public string ServerName
        {
            get => _serverName ?? "";
            init => _serverName = value;
        }
        public string RoomKey
        {
            get => _roomKey ?? "";
            init => _roomKey = value;
        }
        public GameMode Mode { get; init; }
        public int Players { get; init; }
        public int MaxPlayers { get; init; }
        public int Protocol { get; init; }

        public string Endpoint => Port == NetConfig.DefaultPort
            ? Address
            : $"{Address}:{Port}";
    }

    /// <summary>
    /// What a directory said, and whether it said anything at all.
    ///
    /// The difference matters on screen: "nobody is hosting right now" and
    /// "your launcher cannot reach the directory" are the same empty list and
    /// completely different problems, and only one of them is the player's to
    /// do something about.
    /// </summary>
    public readonly struct MasterListResult
    {
        public IReadOnlyList<MasterListing> Servers { get; init; }
        public bool Answered { get; init; }

        /// <summary>
        /// Whether this directory will start a game for somebody who cannot
        /// open a port: true or false when it said, and **null when it did
        /// not** -- a directory built before the flags byte existed.
        ///
        /// Three states rather than two, and the third is the one that
        /// matters. Folding "did not say" into false was the conservative
        /// reading and it was wrong: hosting is on by default and has to be
        /// turned *off* with <c>-hostports none</c>, so every directory
        /// deployed in the world hosts, and a launcher that reads silence as
        /// "no" offers nothing to anybody until every one of them is
        /// redeployed. A launcher reads it as "probably" instead -- see
        /// <see cref="WillHost"/> -- because the cost of being wrong is a
        /// clear refusal from the directory at the moment the player presses
        /// the button, against a row that says "nobody" and explains nothing.
        /// </summary>
        public bool? CanHost { get; init; }

        /// <summary>
        /// Whether to offer this directory as a host: it said yes, or it is
        /// too old to have said anything. Only an explicit no is a no.
        /// </summary>
        public bool WillHost => Answered && CanHost != false;
    }

    /// <summary>What a directory says about itself, in the byte after its list.</summary>
    public static class MasterFlags
    {
        public const byte CanHost = 1;
    }

    /// <summary>What came back from asking the directory to start a game.</summary>
    public readonly struct HostedGame
    {
        public Guid OwnerToken { get; init; }
        private readonly string? _host;
        private readonly string? _reason;

        public bool Started { get; init; }
        /// <summary>The directory's own address -- the game runs there.</summary>
        public string Host
        {
            get => _host ?? "";
            init => _host = value;
        }
        public int Port { get; init; }
        public string Reason
        {
            get => _reason ?? "";
            init => _reason = value;
        }
    }

    /// <summary>
    /// A machine that could run a match for somebody, and what it said when
    /// it was asked.
    /// </summary>
    public readonly struct HostCandidate
    {
        private readonly string? _label;
        private readonly string? _host;

        /// <summary>What to call it on a list -- a server's own name where there is one.</summary>
        public string Label
        {
            get => _label ?? "";
            init => _label = value;
        }

        public string Host
        {
            get => _host ?? "";
            init => _host = value;
        }

        public int Port { get; init; }

        /// <summary>Whether a directory answered here at all.</summary>
        public bool Answered { get; init; }

        /// <summary>True, false, or null when nothing answered at all.</summary>
        public bool? CanHost { get; init; }

        /// <summary>Milliseconds to the answer, or -1.</summary>
        public int Latency { get; init; }

        public bool WillHost => Answered && CanHost != false;

        /// <summary>
        /// What to put against this row, in a player's words.
        ///
        /// It used to say "no directory here", which is true and useless:
        /// nobody opening this screen has an opinion about directories, they
        /// want to know whether they can put their game on this server. The
        /// answer is the ping when they can and the reason when they cannot.
        /// </summary>
        public string Describe()
        {
            if (!Answered)
            {
                return "not answering";
            }
            string ping = Latency >= 0
                ? Latency.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms"
                : "-- ms";
            return CanHost == false ? $"{ping} -- cannot open new games" : ping;
        }
    }

    /// <summary>The launcher's end: ask the directory who is up.</summary>
    public static class NetMasterClient
    {
        /// <summary>
        /// Every machine that might run a match, asked one by one.
        ///
        /// The list is derived rather than configured: the directory already
        /// names every server that is up, and a box running a server is the
        /// obvious box to also be running a directory -- so each listed
        /// address is asked on the directory port as well, and whatever
        /// answers goes on the list under that server's own name. A player
        /// picking "Project Prime - Japan" to host on is then picking a place
        /// they can already see and have already pinged.
        ///
        /// Nothing here needs deploying to *this* build. The moment a box in
        /// the fleet gets a directory on a reachable 27889 it appears, and
        /// until then it appears greyed with the reason -- which is a far
        /// better answer than leaving it off the list, since "Japan is not
        /// offered" and "Japan cannot host" are the same absence otherwise.
        /// </summary>
        /// <summary>
        /// Ask who can open a match for you, and hand each answer over **as it
        /// arrives**.
        ///
        /// The servers themselves are asked, on the port the browser already
        /// pings them on -- not a directory beside each one. A server that
        /// was started with <c>-hostports</c> says so in the flags byte of its
        /// status reply, and it is the machine that would run the match, so
        /// asking anything else was a layer of indirection with nothing in it.
        /// The first shape of this put a second directory on every box: they
        /// listed nothing (every relay reports to the one real directory) and
        /// existed purely to be asked, which is a component invented to
        /// satisfy a mistake.
        ///
        /// There is still exactly one directory in the world. It answers "who
        /// is up"; each server answers "can you open me a game".
        ///
        /// Nothing here waits for anything, and that took three goes. Asking
        /// one after another made the wait the slowest timeout *times* the
        /// number of boxes; asking at once but returning a finished list made
        /// it the slowest of them. A box that does not answer costs the full
        /// timeout, so any design where an answer waits on a silence is always
        /// as slow as its worst member. The server browser was the model: it
        /// posts a row the moment that server replies.
        /// </summary>
        /// <param name="onFound">
        /// Called once per machine, on a thread pool thread, in whatever order
        /// they reply. A UI caller marshals it itself.
        /// </param>
        /// <param name="onDone">Called once, after the last one.</param>
        public static void FindHosts(string masterHost, int masterPort,
            Action<HostCandidate> onFound, Action? onDone = null, int timeoutMs = 1500)
        {
            Task.Run(() =>
            {
                try
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    MasterListResult listing = Query(masterHost, masterPort, timeoutMs);
                    clock.Stop();
                    // The directory itself is a candidate too, and not as a
                    // special case: it is a machine with a port range that
                    // will open a match for you, which is the whole
                    // definition. It is simply the only one that was ever
                    // *asked* before -- and it is also the one that can do it
                    // today, since a relay needs a build carrying HostPool.
                    onFound(new HostCandidate
                    {
                        Label = masterHost,
                        Host = masterHost,
                        Port = masterPort,
                        Answered = listing.Answered,
                        CanHost = listing.CanHost,
                        Latency = listing.Answered ? (int)clock.ElapsedMilliseconds : -1
                    });
                    var jobs = new List<Task>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string masterMachine = Resolve(masterHost);
                    foreach (MasterListing server in listing.Servers
                        ?? Array.Empty<MasterListing>())
                    {
                        if (server.Address.Length == 0
                            // When the always-on master itself can allocate
                            // lobbies, probing every dynamic child on that same
                            // machine only adds work and duplicate picker rows.
                            || listing.CanHost == true && String.Equals(
                                Resolve(server.Address), masterMachine,
                                StringComparison.OrdinalIgnoreCase)
                            || !seen.Add($"{server.Address}:{server.Port}"))
                        {
                            continue;
                        }
                        MasterListing row = server;
                        jobs.Add(Task.Run(() =>
                        {
                            var clock = System.Diagnostics.Stopwatch.StartNew();
                            ServerStatus status;
                            try
                            {
                                status = NetStatus.Query(row.Address, row.Port,
                                    allowJoinProbe: false);
                            }
                            catch (Exception)
                            {
                                status = default;
                            }
                            clock.Stop();
                            onFound(new HostCandidate
                            {
                                Label = row.ServerName.Length > 0
                                    ? row.ServerName : row.Endpoint,
                                Host = row.Address,
                                Port = row.Port,
                                Answered = status.Online,
                                CanHost = status.Online ? status.CanHost : null,
                                Latency = status.Online
                                    ? (status.Latency >= 0
                                        ? status.Latency : (int)clock.ElapsedMilliseconds)
                                    : -1
                            });
                        }));
                    }
                    Task.WhenAll(jobs).ContinueWith(_ => Safely(onDone));
                }
                catch (Exception)
                {
                    // Every probe swallows its own failure, so this is only
                    // reached if something upstream threw. The caller still
                    // has to be told the asking is over, or its screen sits
                    // on "asking..." with no way out.
                    Safely(onDone);
                }
            });
        }

        /// <summary>
        /// Add a candidate to a list, or replace the one already standing for
        /// the same machine.
        ///
        /// One row per machine, not per port. The Pi reaches a host list twice
        /// -- as the directory on 27889 and as a relay on 27888 -- and they are
        /// one box: offering both is offering a choice that is not one. The
        /// row that can actually open a game wins, since that is the only
        /// difference a player would ever see.
        ///
        /// Shared by the screen and by <c>-hosts</c> so the diagnostic cannot
        /// drift from the picture it exists to check.
        /// </summary>
        public static void Merge(List<HostCandidate> into, HostCandidate candidate)
        {
            string machine = Resolve(candidate.Host);
            for (int i = 0; i < into.Count; i++)
            {
                if (!String.Equals(Resolve(into[i].Host), machine,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (candidate.WillHost && !into[i].WillHost)
                {
                    into[i] = candidate;
                }
                return;
            }
            into.Add(candidate);
        }

        private static void Safely(Action? action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception)
            {
                // A caller's own failure is not this one's to report.
            }
        }

        /// <summary>
        /// Ask one machine whether it runs a directory, and what it knows.
        ///
        /// Never throws: every failure is an unanswered candidate, which is a
        /// row on the screen rather than a screen that never finishes.
        /// </summary>
        private static (HostCandidate, MasterListResult) Probe(string host, int port,
            int timeoutMs, string label)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            MasterListResult answer;
            try
            {
                answer = Query(host, port, timeoutMs);
            }
            catch (Exception)
            {
                answer = new MasterListResult
                {
                    Servers = Array.Empty<MasterListing>(),
                    Answered = false
                };
            }
            clock.Stop();
            return (new HostCandidate
            {
                Label = label,
                Host = host,
                Port = port,
                Answered = answer.Answered,
                CanHost = answer.CanHost,
                Latency = answer.Answered ? (int)clock.ElapsedMilliseconds : -1
            }, answer);
        }

        /// <summary>
        /// The IPv4 behind a name, or the name itself when it has none.
        ///
        /// Only ever used as a key for telling two candidates apart. The
        /// probe resolves again on its own, so a name that starts pointing
        /// somewhere else between the two is a wrong row, not a wrong
        /// connection.
        /// </summary>
        private static string Resolve(string host)
        {
            try
            {
                IPAddress[] found = Dns.GetHostAddresses(host);
                IPAddress? ipv4 = Array.Find(found,
                    a => a.AddressFamily == AddressFamily.InterNetwork);
                return ipv4?.ToString() ?? host;
            }
            catch (Exception)
            {
                return host;
            }
        }

        /// <summary>
        /// Ask a machine to open a game, and get back where it is.
        ///
        /// Addressed to whoever will run the match: the directory on its own
        /// port, or -- since servers answer this too -- a game server on the
        /// port the browser already pings it on. The caller picks; this only
        /// sends.
        ///
        /// This is hosting for somebody whose router will not forward a port,
        /// which is most people: the match runs on somebody else's machine,
        /// and the person who asked for it joins by connecting outwards like
        /// everybody else. Nothing has to reach into their network at all.
        /// </summary>
        public static HostedGame RequestGame(string masterHost, int masterPort,
            string roomKey, GameMode mode, float timeLimit, int pointGoal,
            int maxPlayers, string serverName, int timeoutMs = 6000,
            IReadOnlyList<(string RoomKey, GameMode Mode)>? rotation = null,
            ServerSessionPolicy policy = ServerSessionPolicy.Continuous)
        {
            IPEndPoint endPoint;
            try
            {
                IPAddress[] resolved = Dns.GetHostAddresses(masterHost);
                IPAddress? ipv4 = Array.Find(resolved,
                    a => a.AddressFamily == AddressFamily.InterNetwork);
                if (ipv4 == null)
                {
                    return new HostedGame { Reason = $"{masterHost} has no IPv4 address" };
                }
                endPoint = new IPEndPoint(ipv4, masterPort);
            }
            catch (Exception ex)
            {
                return new HostedGame { Reason = $"cannot find {masterHost}: {ex.Message}" };
            }
            try
            {
                using var socket = new UdpClient(AddressFamily.InterNetwork);
                ulong hostNonce;
                Span<byte> nonceBytes = stackalloc byte[8];
                do
                {
                    RandomNumberGenerator.Fill(nonceBytes);
                    hostNonce = System.Buffers.Binary.BinaryPrimitives
                        .ReadUInt64LittleEndian(nonceBytes);
                } while (hostNonce == 0);

                var challenge = new HostChallengePacket(
                    (byte)NetConfig.ProtocolVersion, hostNonce);
                byte[] challengeWire = new byte[1 + HostChallengePacket.Size];
                challengeWire[0] = (byte)PacketType.HostChallenge;
                challenge.Write(challengeWire.AsSpan(1));
                socket.Send(challengeWire, challengeWire.Length, endPoint);

                var challengeFrom = new IPEndPoint(IPAddress.Any, 0);
                socket.Client.ReceiveTimeout = 250;
                HostChallengeReplyPacket challengeReply = default;
                bool challenged = false;
                var challengeClock = System.Diagnostics.Stopwatch.StartNew();
                int challengeTimeout = Math.Clamp(timeoutMs, 250, 1500);
                while (challengeClock.ElapsedMilliseconds < challengeTimeout)
                {
                    socket.Client.ReceiveTimeout = Math.Max(1, Math.Min(250,
                        challengeTimeout - (int)challengeClock.ElapsedMilliseconds));
                    try
                    {
                        byte[] answer = socket.Receive(ref challengeFrom);
                        if (challengeFrom.Equals(endPoint)
                            && answer.Length == 1 + HostChallengeReplyPacket.Size
                            && answer[0] == (byte)PacketType.HostChallengeReply
                            && HostChallengeReplyPacket.TryRead(answer.AsSpan(1),
                                out challengeReply)
                            && challengeReply.Nonce == hostNonce)
                        {
                            challenged = true;
                            break;
                        }
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode
                        == SocketError.TimedOut)
                    {
                        if (challengeClock.ElapsedMilliseconds < challengeTimeout)
                        {
                            socket.Send(challengeWire, challengeWire.Length, endPoint);
                            continue;
                        }
                        break;
                    }
                }
                if (!challenged)
                {
                    return new HostedGame
                    {
                        Reason = $"{masterHost}:{masterPort} did not complete the "
                            + "protected lobby-allocation challenge"
                    };
                }

                IReadOnlyList<HostRotationEntry>? hostedRotation = rotation?
                    .Take(HostRequestPacket.MaxRotation)
                    .Select(entry => HostRotationEntry.ForRoom(entry.RoomKey, entry.Mode))
                    .ToArray();
                var request = new HostRequestPacket
                {
                    Protocol = NetConfig.ProtocolVersion,
                    MaxPlayers = (byte)Math.Clamp(maxPlayers, 2,
                        MphRead.Entities.PlayerEntity.SlotCapacity),
                    Mode = (byte)mode,
                    TimeLimit = (ushort)Math.Clamp((int)timeLimit, 0, UInt16.MaxValue),
                    PointGoal = (ushort)Math.Clamp(pointGoal, 0, UInt16.MaxValue),
                    RoomKey = roomKey, MapIdentity = NetworkMapIdentity.ForRoom(roomKey),
                    ServerName = serverName,
                    Policy = policy, AllowJoinInProgress = true, RequireReady = false,
                    Rotation = hostedRotation,
                    HostNonce = hostNonce, HostCookie = challengeReply.Cookie
                };
                var datagram = new byte[1 + request.Length];
                datagram[0] = (byte)PacketType.HostRequest;
                request.Write(datagram.AsSpan(1));
                socket.Send(datagram, datagram.Length, endPoint);
                var from = new IPEndPoint(IPAddress.Any, 0);
                int requestTimeout = request.RequiresMapPreparation ? Math.Max(timeoutMs, 150_000) : timeoutMs;
                socket.Client.ReceiveTimeout = request.RequiresMapPreparation ? 1000 : timeoutMs;
                var requestClock = System.Diagnostics.Stopwatch.StartNew();
                while (requestClock.ElapsedMilliseconds < requestTimeout)
                {
                    socket.Client.ReceiveTimeout = Math.Max(1, Math.Min(request.RequiresMapPreparation ? 1000 : Math.Max(1, timeoutMs),
                        requestTimeout - (int)requestClock.ElapsedMilliseconds));
                    byte[] reply;
                    try { reply = socket.Receive(ref from); }
                    catch (SocketException ex) when (request.RequiresMapPreparation && ex.SocketErrorCode == SocketError.TimedOut)
                    { socket.Send(datagram, datagram.Length, endPoint); continue; }
                    if (reply.Length < 1 + HostReplyPacket.Size
                        || reply[0] != (byte)PacketType.HostReply || !from.Equals(endPoint))
                    {
                        continue;
                    }
                    HostReplyPacket answer = HostReplyPacket.Read(reply.AsSpan(1));
                    if (!answer.Started)
                    {
                        return new HostedGame
                        {
                            Started = false,
                            OwnerToken = answer.OwnerToken,
                            Host = masterHost,
                            Port = answer.Port,
                            Reason = answer.Reason
                        };
                    }

                    // Process.Start is not readiness. A host service can spawn a
                    // child that immediately dies on bind/startup and, before
                    // this check, the launcher was told "started" and only
                    // learned the truth from an eight-second join timeout.
                    // StatusQuery proves the actual game endpoint is answering.
                    var readyClock = System.Diagnostics.Stopwatch.StartNew();
                    int readyTimeoutMs = request.RequiresMapPreparation ? 120_000 : 30_000;
                    while (readyClock.ElapsedMilliseconds < readyTimeoutMs)
                    {
                        int remaining = readyTimeoutMs - (int)readyClock.ElapsedMilliseconds;
                        ServerStatus status = NetStatus.Query(masterHost, answer.Port,
                            allowJoinProbe: false, timeoutMs: Math.Clamp(remaining, 20, 250));
                        if (status.Online)
                        {
                            if (status.Protocol > 0
                                && status.Protocol != NetConfig.ProtocolVersion)
                            {
                                return new HostedGame
                                {
                                    Started = false,
                                    OwnerToken = answer.OwnerToken,
                                    Host = masterHost,
                                    Port = answer.Port,
                                    Reason = $"{masterHost}:{answer.Port} started with protocol "
                                        + $"{status.Protocol}; this build speaks "
                                        + NetConfig.ProtocolVersion
                                };
                            }
                            return new HostedGame
                            {
                                Started = true,
                                OwnerToken = answer.OwnerToken,
                                Host = masterHost,
                                Port = answer.Port,
                                Reason = ""
                            };
                        }
                        remaining = readyTimeoutMs - (int)readyClock.ElapsedMilliseconds;
                        if (remaining > 0)
                        {
                            Thread.Sleep(Math.Min(50, remaining));
                        }
                    }
                    return new HostedGame
                    {
                        Started = false,
                        OwnerToken = answer.OwnerToken,
                        Host = masterHost,
                        Port = answer.Port,
                        Reason = $"{masterHost} started a server on UDP {answer.Port}, "
                            + "but it never became reachable"
                    };
                }
                return new HostedGame { Reason = $"{masterHost} did not answer" };
            }
            catch (SocketException)
            {
                return new HostedGame
                {
                    Reason = $"no answer from {masterHost}:{masterPort} -- "
                        + "it may be down, or UDP may not reach it"
                };
            }
            catch (Exception ex)
            {
                return new HostedGame { Reason = ex.Message };
            }
        }

        public static MasterListResult Query(string host,
            int port = NetMasterConfig.DefaultPort, int timeoutMs = 1500,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            timeoutMs = Math.Max(1, timeoutMs);
            var found = new List<MasterListing>();
            bool answered = false;
            bool? canHost = null;
            IPEndPoint endPoint;
            try
            {
                IPAddress[] resolved = Dns.GetHostAddressesAsync(host, cancellationToken).GetAwaiter().GetResult();
                IPAddress? ipv4 = Array.Find(resolved,
                    a => a.AddressFamily == AddressFamily.InterNetwork);
                if (ipv4 == null)
                {
                    return new MasterListResult { Servers = found, Answered = false };
                }
                endPoint = new IPEndPoint(ipv4, port);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                return new MasterListResult { Servers = found, Answered = false };
            }
            try
            {
                using var socket = new UdpClient(AddressFamily.InterNetwork);
                using var receiveDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                receiveDeadline.CancelAfter(timeoutMs);
                socket.Send(new byte[]
                {
                    (byte)PacketType.MasterQuery, NetConfig.ProtocolVersion
                }, 2, endPoint);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                int total = -1;
                while (clock.ElapsedMilliseconds < timeoutMs && (total < 0 || found.Count < total))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    UdpReceiveResult received = socket.ReceiveAsync(receiveDeadline.Token).AsTask().GetAwaiter().GetResult();
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] reply = received.Buffer;
                    if (reply.Length < 3 || reply[0] != (byte)PacketType.MasterList)
                    {
                        continue;
                    }
                    answered = true;
                    int count = reply[1];
                    total = reply[2];
                    int offset = 3;
                    for (int i = 0; i < count; i++)
                    {
                        if (offset + MasterEntryPacket.Size > reply.Length)
                        {
                            break;
                        }
                        MasterEntryPacket entry = MasterEntryPacket.Read(reply.AsSpan(offset));
                        offset += MasterEntryPacket.Size;
                        found.Add(new MasterListing
                        {
                            Address = new IPAddress(new[]
                            {
                                (byte)(entry.Address >> 24), (byte)(entry.Address >> 16),
                                (byte)(entry.Address >> 8), (byte)entry.Address
                            }).ToString(),
                            Port = entry.Port,
                            ServerName = entry.ServerName,
                            RoomKey = entry.RoomKey,
                            Mode = Enum.IsDefined(typeof(GameMode), entry.Mode)
                                ? (GameMode)entry.Mode
                                : GameMode.Battle,
                            Players = entry.Players,
                            MaxPlayers = entry.MaxPlayers,
                            Protocol = entry.Protocol
                        });
                    }
                    // The flags byte, when this directory is new enough to
                    // have written one. Left null when it is not, which is a
                    // third answer and not a no -- see MasterListResult.CanHost.
                    if (offset < reply.Length)
                    {
                        canHost = (reply[offset] & MasterFlags.CanHost) != 0;
                    }
                    if (total == 0)
                    {
                        break;
                    }
                }
            }
            catch (SocketException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Timed out with nothing, or with part of the list. Part of a
                // list is still a list.
            }
            catch (Exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            return new MasterListResult
            {
                Servers = found,
                Answered = answered,
                CanHost = canHost
            };
        }
    }
}
