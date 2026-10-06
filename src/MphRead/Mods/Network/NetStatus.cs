using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;

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

        public SessionPhase Phase { get; init; }
        public MatchFormat Format { get; init; }
        public bool LobbyEnabled { get; init; }
        public bool AllowJoinInProgress { get; init; }
        public bool Online { get; init; }
        /// <summary>
        /// Server-instance identity published in MatchState. It changes when a
        /// dedicated authority process restarts and is used by social invites
        /// to reject stale endpoint reuse.
        /// </summary>
        public ulong AuthorityEpoch { get; init; }
        public string RoomKey
        {
            get => _roomKey ?? "";
            init => _roomKey = value;
        }
        public GameMode Mode { get; init; }
        public MatchModifierFlags Rules { get; init; }
        public int Players { get; init; }
        /// <summary>0 when the server did not say -- an older build answering the join probe.</summary>
        public int MaxPlayers { get; init; }
        public bool WaitlistSupported { get; init; }
        public int WaitlistCount { get; init; }
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
        /// A server refuses a Hello from a different version in silence, which
        /// from the client's side is indistinguishable from a server that is
        /// not there -- except that this packet still comes back, and carries
        /// the number. See <see cref="NetLaunch.DescribeJoinFailure"/>.
        /// </summary>
        public int Protocol { get; init; }

        /// <summary>
        /// Whether this server will open a *new* match for you, on a port of
        /// its own, beside the one it is already running.
        ///
        /// False for a server that did not say, and that is the right default:
        /// hosting is off unless an admin passed <c>-hostports</c>, so silence
        /// and no are the same answer -- unlike the directory's own flag,
        /// where hosting is on by default and silence means "too old to say".
        /// </summary>
        public bool CanHost { get; init; }

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
    /// Two paths on purpose. <see cref="PacketType.StatusQuery"/> is the
    /// cheap one and takes no slot, so it can be repeated while the screen is
    /// open. A server built before that packet existed ignores it, and rather
    /// than reporting a live server as dead we fall back to the join probe --
    /// a Hello followed immediately by a Bye, which does claim a slot for a
    /// moment. The fallback is opt-in per call so the polling loop can use it
    /// rarely and the "test this address" button can use it always.
    /// </summary>
    public static class NetStatus
    {
        public static ServerStatus Query(string address, int port, bool allowJoinProbe,
            int timeoutMs = 1200, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            timeoutMs = Math.Max(1, timeoutMs);
            if (String.IsNullOrWhiteSpace(address))
            {
                return ServerStatus.Offline("No server address.");
            }
            IPEndPoint endPoint;
            try
            {
                IPAddress[] resolved = Dns.GetHostAddressesAsync(address, cancellationToken).GetAwaiter().GetResult();
                IPAddress? ipv4 = Array.Find(resolved,
                    a => a.AddressFamily == AddressFamily.InterNetwork);
                if (ipv4 == null)
                {
                    return ServerStatus.Offline($"Cannot find {address}.");
                }
                endPoint = new IPEndPoint(ipv4, port);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                return ServerStatus.Offline($"Cannot find {address}.");
            }

            using var socket = new UdpClient(AddressFamily.InterNetwork);
            try
            {
                using var receiveDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                receiveDeadline.CancelAfter(timeoutMs);
                // The clock starts on the send and stops on the reply, so the
                // number a browser shows is one round trip over the same path
                // a match would use -- not an ICMP ping, which routers are
                // free to treat differently, and not the server's own idea of
                // anything.
                var clock = Stopwatch.StartNew();
                socket.Send(new byte[]
                {
                    (byte)PacketType.StatusQuery, NetConfig.ProtocolVersion
                }, 2, endPoint);
                while (clock.ElapsedMilliseconds < timeoutMs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    UdpReceiveResult received = socket.ReceiveAsync(receiveDeadline.Token).AsTask().GetAwaiter().GetResult();
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] reply = received.Buffer;
                    if (!received.RemoteEndPoint.Equals(endPoint)) continue;
                    if (reply.Length >= 1 + ServerStatusPacket.Size
                        && reply[0] == (byte)PacketType.StatusReply)
                    {
                        return Describe(ServerStatusPacket.Read(reply.AsSpan(1)), legacy: false,
                            latency: (int)clock.ElapsedMilliseconds);
                    }
                }
            }
            catch (OperationCanceledException) { cancellationToken.ThrowIfCancellationRequested(); }
            catch (SocketException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Timed out or the host refused the datagram; either way the
                // fallback below is the next thing to try.
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ServerStatus.Offline($"Cannot reach {address}: {ex.Message}");
            }

            return allowJoinProbe
                ? JoinProbe(socket, endPoint, address, timeoutMs, cancellationToken)
                : ServerStatus.Offline($"No answer from {address}:{port}.");
        }

        /// <summary>
        /// The old way: say hello, read what comes back, say goodbye.
        ///
        /// Kept because the running server may predate StatusQuery, and a
        /// launcher that showed "offline" for a server people are playing on
        /// would be worse than a probe that borrows a slot for 200 ms.
        /// </summary>
        private static ServerStatus JoinProbe(UdpClient socket, IPEndPoint endPoint,
            string address, int timeoutMs, CancellationToken cancellationToken)
        {
            bool welcomed = false, helloSent = false;
            try
            {
                using var receiveDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                receiveDeadline.CancelAfter(timeoutMs);
                // 0xFF asks for any free slot rather than a particular one.
                socket.Send(new byte[]
                {
                    (byte)PacketType.Hello, NetConfig.ProtocolVersion, 0xFF
                }, 3, endPoint);
                helloSent = true;
                var clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < timeoutMs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    UdpReceiveResult received = socket.ReceiveAsync(receiveDeadline.Token).AsTask().GetAwaiter().GetResult();
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] reply = received.Buffer;
                    if (!received.RemoteEndPoint.Equals(endPoint)) continue;
                    if (reply.Length >= 1 && reply[0] == (byte)PacketType.Welcome)
                    {
                        welcomed = true;
                        continue;
                    }
                    if (reply.Length >= 1 + MatchStatePacket.Size
                        && reply[0] == (byte)PacketType.MatchState)
                    {
                        MatchStatePacket match = MatchStatePacket.Read(reply.AsSpan(1));
                        // The probe is in the roster while it asks, so the
                        // count it is told includes itself. Reporting one
                        // player on an empty server is worse than reporting
                        // none: it is the difference between "somebody is on"
                        // and "nobody is on".
                        match.PlayerCount = (byte)Math.Max(0, match.PlayerCount - 1);
                        return Describe(new ServerStatusPacket
                        {
                            Match = match,
                            MaxPlayers = 0,
                            ServerName = ""
                        }, legacy: true, latency: -1);
                    }
                }
            }
            catch (Exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Timeouts and unreachable hosts return the partial legacy
                // answer, if Welcome was already received.
            }
            finally
            {
                // Return the borrowed legacy seat even if the caller cancels
                // while waiting, or the reply naming it is lost.
                if (helloSent)
                    try { socket.Send(new byte[] { (byte)PacketType.Bye }, 1, endPoint); }
                    catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
            }
            if (welcomed)
                return new ServerStatus { Online = true, Latency = -1, Legacy = true,
                    Message = "Online · the server did not say what is running." };
            return ServerStatus.Offline($"No answer from {address}. It may be off, "
                + "or a firewall may be blocking UDP.");
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
                ? $"{room} \u00B7 {ModeName(mode)} \u00B7 {MatchTypeCatalog.MatchupLabel(mode, status.Format)} \u00B7 {players}"
                : players;
            string modifiers = new MatchDefinition { InstaGib = status.Rules.HasFlag(MatchModifierFlags.InstaGib),
                LowTier = status.Rules.HasFlag(MatchModifierFlags.LowTier), NoImperialist = status.Rules.HasFlag(MatchModifierFlags.NoImperialist) }.ModifierSummary;
            if (modifiers.Length > 0) message += " · " + modifiers;
            if (!String.IsNullOrEmpty(status.ServerName))
            {
                // The name first: it is what the player recognises, and the
                // rest of the line is what it happens to be doing right now.
                message = $"{status.ServerName} \u00B7 {message}";
            }
            return new ServerStatus
            {
                Online = true,
                AuthorityEpoch = match.AuthorityEpoch,
                RoomKey = match.RoomKey,
                ServerName = status.ServerName ?? "",
                Mode = mode, Rules = status.Rules,
                Players = match.PlayerCount,
                MaxPlayers = status.MaxPlayers,
                WaitlistSupported = status.WaitlistSupported, WaitlistCount = status.WaitlistCount,
                TimeRemaining = match.TimeRemaining,
                Latency = latency,
                Legacy = legacy,
                Protocol = status.Protocol,
                Phase = status.Phase, Format = status.Format, LobbyEnabled = status.LobbyEnabled,
                AllowJoinInProgress = status.AllowJoinInProgress,
                CanHost = (status.Flags & ServerStatusPacket.FlagCanHost) != 0,
                Message = message
            };
        }

        /// <summary>Player-facing game type name. Team organization is displayed separately.</summary>
        public static string ModeName(GameMode mode) => MatchTypeCatalog.BaseLabel(mode);
    }
}
