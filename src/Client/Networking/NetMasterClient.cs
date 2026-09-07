using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Network
{
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
        public NetWireFamily Family { get; init; }
        public bool Compatible => NetWireIdentity.IsCompatible(Family, Protocol);
        public string IncompatibilityReason => NetWireIdentity.IncompatibilityReason(Family, Protocol);

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
        public NetWireFamily Family { get; init; }
        public int Protocol { get; init; }
        public bool Compatible => Answered && NetWireIdentity.IsCompatible(Family, Protocol);
        public string IncompatibilityReason => !Answered ? "Directory did not answer." : NetWireIdentity.IncompatibilityReason(Family, Protocol);
    }

    /// <summary>What came back from asking the directory to start a game.</summary>
    public readonly struct HostedGame
    {
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
        public Guid RequestNonce { get; init; }
        /// <summary>
        /// One-use secret capability for the launcher's first owner admission.
        /// </summary>
        public Guid OwnerToken { get; init; }
        public string Reason
        {
            get => _reason ?? "";
            init => _reason = value;
        }
    }

    /// <summary>The launcher's end: ask the directory who is up.</summary>
    public static class NetMasterClient
    {
        /// <summary>
        /// Ask the directory to run a game, and get back where it is.
        ///
        /// This is hosting for somebody whose router will not forward a port,
        /// which is most people: the match runs on the directory's machine,
        /// and the person who asked for it joins by connecting outwards like
        /// everybody else. Nothing has to reach into their network at all.
        /// </summary>
        public static HostedGame RequestGame(string masterHost, int masterPort,
            string roomKey, GameMode mode, float timeLimit, int pointGoal,
            int maxPlayers, string serverName, int timeoutMs = 35000)
        {
            int capacity = Math.Clamp(maxPlayers, 2, MphRead.Entities.PlayerEntity.SlotCapacity);
            MatchRules rules;
            try
            {
                rules = MapRotation.SingleMatch(roomKey, mode, timeLimit, pointGoal)
                    .Current.ToMatchRules(capacity, friendlyFire: false);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return new HostedGame { Reason = ex.Message };
            }
            return RequestGame(masterHost, masterPort, rules, LobbyPolicy.PrivateHosted,
                botMinimumParticipants: 0, botSkill: 1, maxObservers: 4,
                observerDelaySeconds: 0, serverName, practice: false, timeoutMs);
        }

        public static HostedGame RequestGame(string masterHost, int masterPort,
            MatchRules rules, LobbyPolicy lobbyPolicy, int botMinimumParticipants,
            int botSkill, int maxObservers, int observerDelaySeconds,
            string serverName, bool practice = false, int timeoutMs = 35000)
        {
            if (rules == null) return new HostedGame { Reason = "Match rules are required." };
            if (lobbyPolicy == null) return new HostedGame { Reason = "Lobby policy is required." };
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
            // Probe the resolved endpoint we will mutate; a second hostname lookup
            // must not switch to an unverified address between discovery and hosting.
            MasterListResult directory = Query(endPoint.Address.ToString(), endPoint.Port, Math.Min(timeoutMs, 1500));
            if (!directory.Compatible) { return new HostedGame { Reason = directory.IncompatibilityReason }; }
            try
            {
                using var socket = new UdpClient(AddressFamily.InterNetwork);
                socket.Client.ReceiveTimeout = timeoutMs;
                Guid requestNonce = CreateNonce();
                var request = new HostRequestPacket
                {
                    Version = HostRequestPacket.CurrentVersion,
                    Protocol = NetConfig.ProtocolVersion,
                    Family = NetWireIdentity.Family,
                    Practice = practice,
                    LobbyPolicy = lobbyPolicy.Kind,
                    ReadyRequired = lobbyPolicy.ReadyRequired,
                    HostMayForceStart = lobbyPolicy.HostMayForceStart,
                    MinimumPlayers = lobbyPolicy.MinimumPlayers,
                    BotMinimumParticipants = checked((byte)botMinimumParticipants),
                    BotSkill = checked((byte)botSkill),
                    MaxObservers = checked((byte)maxObservers),
                    ObserverDelaySeconds = checked((byte)observerDelaySeconds),
                    RequestNonce = requestNonce,
                    Rules = rules,
                    ServerName = serverName
                };
                var datagram = new byte[1 + HostRequestPacket.Size];
                datagram[0] = (byte)PacketType.HostRequest;
                request.Write(datagram.AsSpan(1));
                socket.Send(datagram, datagram.Length, endPoint);
                var from = new IPEndPoint(IPAddress.Any, 0);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < timeoutMs)
                {
                    socket.Client.ReceiveTimeout = Math.Max(1, timeoutMs - (int)clock.ElapsedMilliseconds);
                    byte[] reply = socket.Receive(ref from);
                    if (!from.Equals(endPoint) || reply.Length != 1 + HostReplyPacket.Size
                        || reply[0] != (byte)PacketType.HostReply
                        || !HostReplyPacket.TryRead(reply.AsSpan(1), out HostReplyPacket answer)
                        || answer.RequestNonce != requestNonce
                        || !NetWireIdentity.IsCompatible(answer.Family, answer.Protocol))
                    {
                        continue;
                    }
                    var hosted = new HostedGame
                    {
                        Started = answer.Started,
                        // Keep the launch endpoint pinned to the address that produced
                        // the nonce-bound reply so the capability is never redirected
                        // through a second DNS resolution.
                        Host = endPoint.Address.ToString(),
                        Port = answer.Port,
                        RequestNonce = answer.RequestNonce,
                        OwnerToken = answer.OwnerToken,
                        Reason = answer.Reason
                    };
                    CryptographicOperations.ZeroMemory(reply.AsSpan(1 + 22, 16));
                    return hosted;
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

        private static Guid CreateNonce()
        {
            Span<byte> bytes = stackalloc byte[16];
            RandomNumberGenerator.Fill(bytes);
            return new Guid(bytes);
        }

        public static MasterListResult Query(string host,
            int port = NetMasterConfig.DefaultPort, int timeoutMs = 1500)
        {
            var found = new List<MasterListing>();
            bool answered = false;
            NetWireFamily resultFamily = NetWireFamily.Unknown;
            byte resultProtocol = 0;
            IPEndPoint endPoint;
            try
            {
                IPAddress[] resolved = Dns.GetHostAddresses(host);
                IPAddress? ipv4 = Array.Find(resolved,
                    a => a.AddressFamily == AddressFamily.InterNetwork);
                if (ipv4 == null)
                {
                    return new MasterListResult { Servers = found, Answered = false };
                }
                endPoint = new IPEndPoint(ipv4, port);
            }
            catch (Exception)
            {
                return new MasterListResult { Servers = found, Answered = false };
            }
            try
            {
                using var socket = new UdpClient(AddressFamily.InterNetwork);
                socket.Client.ReceiveTimeout = timeoutMs;
                socket.Send(new byte[]
                {
                    (byte)PacketType.MasterQuery, NetConfig.ProtocolVersion
                }, 2, endPoint);
                var from = new IPEndPoint(IPAddress.Any, 0);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var entries = new MasterEntryPacket[NetMasterConfig.EntriesPerPacket];
                var endpoints = new HashSet<string>(StringComparer.Ordinal);
                int total = -1;
                while (clock.ElapsedMilliseconds < timeoutMs && (total < 0 || found.Count < total))
                {
                    socket.Client.ReceiveTimeout = Math.Max(1, timeoutMs - (int)clock.ElapsedMilliseconds);
                    byte[] reply = socket.Receive(ref from);
                    if (!from.Equals(endPoint) || reply.Length < 3 || reply[0] != (byte)PacketType.MasterList
                        || !MasterListPacket.TryRead(reply.AsSpan(1), entries, out int count, out int advertisedTotal,
                            out NetWireFamily family, out byte protocol)
                        || (answered && (advertisedTotal != total || family != resultFamily || protocol != resultProtocol)))
                    {
                        continue;
                    }
                    answered = true;
                    total = advertisedTotal;
                    resultFamily = family;
                    resultProtocol = protocol;
                    for (int i = 0; i < count; i++)
                    {
                        MasterEntryPacket entry = entries[i];
                        string address = new IPAddress(new[]
                        {
                            (byte)(entry.Address >> 24), (byte)(entry.Address >> 16),
                            (byte)(entry.Address >> 8), (byte)entry.Address
                        }).ToString();
                        if (found.Count >= total || !endpoints.Add(address + ":" + entry.Port)) { continue; }
                        found.Add(new MasterListing
                        {
                            Address = address, Port = entry.Port, ServerName = entry.ServerName,
                            RoomKey = entry.RoomKey, Mode = (GameMode)entry.Mode, Players = entry.Players,
                            MaxPlayers = entry.MaxPlayers, Protocol = entry.Protocol, Family = entry.Family
                        });
                    }
                }
            }
            catch (SocketException)
            {
                // Timed out with nothing, or with part of the list. Part of a
                // list is still a list.
            }
            catch (Exception)
            {
            }
            return new MasterListResult { Servers = found, Answered = answered, Family = resultFamily, Protocol = resultProtocol };
        }
    }
}
