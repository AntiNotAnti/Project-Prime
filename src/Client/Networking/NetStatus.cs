using System;
using System.Net;
using System.Net.Sockets;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// What a server is doing right now, as far as one query could tell.
    ///
    /// Every string here reads as an empty one until it is set. This is a
    /// struct, so <c>default</c> is a perfectly ordinary value of it -- the
    /// server browser holds one for every row it has not probed yet -- and a
    /// plain auto-property would hand those callers a null to call .Length on.
    /// It did exactly that, on the first row of the first list anybody opened.
    /// </summary>
    public readonly struct ServerStatus
    {
        private readonly string? _roomKey;
        private readonly string? _serverName;
        private readonly string? _message;

        public Guid ServerId { get; init; }
        public bool RequiresTicket { get; init; }
        public RulesetPreset RulesetPreset { get; init; }
        public RankingEligibility RankingEligibility { get; init; }
        public int Observers { get; init; }
        public int MaxObservers { get; init; }
        public int Bots { get; init; }
        public int ObserverDelaySeconds { get; init; }
        public bool HasRules { get; init; }
        public bool FriendlyFire { get; init; }
        public bool PlayerRadar { get; init; }
        public SpawnPolicy SpawnPolicy { get; init; }
        public OvertimePolicy OvertimePolicy { get; init; }
        public LateJoinPolicy LateJoinPolicy { get; init; }
        public bool Online { get; init; }
        public string RoomKey
        {
            get => _roomKey ?? "";
            init => _roomKey = value;
        }
        public GameMode Mode { get; init; }
        public int Players { get; init; }
        /// <summary>0 when the server did not say -- an older build answering the join probe.</summary>
        public int MaxPlayers { get; init; }
        public float TimeRemaining { get; init; }
        /// <summary>What the server calls itself, or an empty string.</summary>
        public string ServerName
        {
            get => _serverName ?? "";
            init => _serverName = value;
        }
        /// <summary>
        /// Round trip to the server in milliseconds, measured by this
        /// machine, or -1 when nothing came back.
        ///
        /// Measured here rather than taken from anywhere else because it is a
        /// property of the path between this player and that server, and
        /// nobody else's measurement of it means anything to them.
        /// </summary>
        public int Latency { get; init; }
        /// <summary>One line, ready to put under a button.</summary>
        public string Message
        {
            get => _message ?? "";
            init => _message = value;
        }
        /// <summary>True when only the join probe answered, so the details are thin.</summary>
        public bool Legacy { get; init; }
        /// <summary>
        /// The protocol version the server speaks, or 0 when it did not say.
        ///
        /// Discovery reports the protocol independently of admission, so an
        /// incompatible server can be identified without taking a player slot.
        /// </summary>
        public int Protocol { get; init; }
        public NetWireFamily Family { get; init; }
        public bool Compatible => Online && NetWireIdentity.IsCompatible(Family, Protocol);
        public string IncompatibilityReason => NetWireIdentity.IncompatibilityReason(Family, Protocol);

        public static ServerStatus Offline(string message) => new()
        {
            RoomKey = "",
            ServerName = "",
            Latency = -1,
            Message = message
        };
    }

    /// <summary>
    /// Asks a server what is running, for a launcher to show before anybody
    /// commits to joining.
    ///
    /// Status queries are always read-only; incompatible legacy servers are
    /// never joined as a fallback probe.
    /// </summary>
    public static class NetStatus
    {
        public static ServerStatus Query(string address, int port, bool allowJoinProbe,
            int timeoutMs = 1200)
        {
            // Retained for existing launcher callsites; probes never join.
            _ = allowJoinProbe;
            if (String.IsNullOrWhiteSpace(address))
            {
                return ServerStatus.Offline("No server address.");
            }
            IPEndPoint endPoint;
            try
            {
                IPAddress[] resolved = Dns.GetHostAddresses(address);
                IPAddress? ipv4 = Array.Find(resolved,
                    a => a.AddressFamily == AddressFamily.InterNetwork);
                if (ipv4 == null)
                {
                    return ServerStatus.Offline($"Cannot find {address}.");
                }
                endPoint = new IPEndPoint(ipv4, port);
            }
            catch (Exception)
            {
                return ServerStatus.Offline($"Cannot find {address}.");
            }

            using var socket = new UdpClient(AddressFamily.InterNetwork);
            socket.Client.ReceiveTimeout = timeoutMs;
            try
            {
                // The clock starts on the send and stops on the reply, so the
                // number a browser shows is one round trip over the same path
                // a match would use -- not an ICMP ping, which routers are
                // free to treat differently, and not the server's own idea of
                // anything.
                var clock = System.Diagnostics.Stopwatch.StartNew();
                socket.Send(new byte[]
                {
                    (byte)PacketType.StatusQuery, (byte)(NetConfig.ProtocolVersion | ServerStatusPacket.RulesCapability)
                }, 2, endPoint);
                var from = new IPEndPoint(IPAddress.Any, 0);
                while (clock.ElapsedMilliseconds < timeoutMs)
                {
                    socket.Client.ReceiveTimeout = Math.Max(1, timeoutMs - (int)clock.ElapsedMilliseconds);
                    byte[] reply = socket.Receive(ref from);
                    if (from.Equals(endPoint) && reply.Length > 1
                        && reply[0] == (byte)PacketType.StatusReply
                        && ServerStatusPacket.TryRead(reply.AsSpan(1), out ServerStatusPacket status))
                    {
                        return Describe(status, legacy: status.Family == NetWireFamily.LegacyRelay,
                            latency: (int)clock.ElapsedMilliseconds);
                    }
                }
            }
            catch (SocketException)
            {
                // Timeout or refusal leaves discovery offline without admission.
            }
            catch (Exception ex)
            {
                return ServerStatus.Offline($"Cannot reach {address}: {ex.Message}");
            }

            return ServerStatus.Offline($"No answer from {address}:{port}.");
        }

        private static ServerStatus Describe(ServerStatusPacket status, bool legacy, int latency)
        {
            MatchStatePacket match = status.Match;
            GameMode mode = Enum.IsDefined(typeof(GameMode), match.Mode)
                ? (GameMode)match.Mode
                : GameMode.Battle;
            string room = match.RoomKey;
            if (room.Length > 0)
            {
                (RoomMetadata? meta, _) = Metadata.GetRoomByName(room);
                room = meta?.InGameName ?? room;
            }
            string players;
            if (status.MaxPlayers > 0)
            {
                players = $"{match.PlayerCount}/{status.MaxPlayers} players";
            }
            else if (match.PlayerCount == 0)
            {
                players = "nobody playing yet";
            }
            else
            {
                players = match.PlayerCount == 1 ? "1 player" : $"{match.PlayerCount} players";
            }
            // A middle dot rather than a dash: this string goes on a screen,
            // not into a log, and the rest of the launcher separates the same
            // way.
            string message = room.Length > 0
                ? $"{room} \u00B7 {ModeName(mode)} \u00B7 {players}"
                : players;
            if (!String.IsNullOrEmpty(status.ServerName))
            {
                // The name first: it is what the player recognises, and the
                // rest of the line is what it happens to be doing right now.
                message = $"{status.ServerName} \u00B7 {message}";
            }
            return new ServerStatus
            {
                RulesetPreset = status.RulesetPreset, RankingEligibility = status.RankingEligibility,
                Bots = status.Bots, Observers = status.Observers, MaxObservers = status.MaxObservers, ObserverDelaySeconds = status.ObserverDelaySeconds,
                ServerId = status.ServerId, RequiresTicket = status.RequiresTicket, HasRules = status.HasRules, FriendlyFire = status.FriendlyFire, PlayerRadar = status.PlayerRadar,
                SpawnPolicy = status.SpawnPolicy, OvertimePolicy = status.OvertimePolicy, LateJoinPolicy = status.LateJoinPolicy,
                Online = true,
                RoomKey = match.RoomKey,
                ServerName = status.ServerName ?? "",
                Mode = mode,
                Players = match.PlayerCount,
                MaxPlayers = status.MaxPlayers,
                TimeRemaining = match.TimeRemaining,
                Latency = latency,
                Legacy = legacy,
                Protocol = status.Protocol,
                Family = status.Family,
                Message = message
            };
        }

        /// <summary>"BattleTeams" -> "Battle Teams", for a screen rather than a log.</summary>
        public static string ModeName(GameMode mode)
        {
            string name = mode.ToString();
            var builder = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && Char.IsUpper(name[i]))
                {
                    builder.Append(' ');
                }
                builder.Append(name[i]);
            }
            return builder.ToString();
        }
    }
}
