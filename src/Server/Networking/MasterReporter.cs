using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Network
{
    public sealed class MasterReporter : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private UdpClient? _socket;
        private IPEndPoint? _endPoint;
        private double _lastBeat = Double.NegativeInfinity;
        private double _lastResolve = Double.NegativeInfinity;
        private bool _complained;
        private readonly byte[] _scratch = new byte[MasterHeartbeatPacket.Size];

        public MasterReporter(string host, int port)
        {
            _host = host;
            _port = port;
        }

        /// <summary>Announce, if enough time has passed since the last one.</summary>
        public void Beat(double now, string serverName, ushort port, byte players,
            byte maxPlayers, byte mode, string roomKey, byte protocol = 0)
        {
            if (now - _lastBeat < NetMasterConfig.HeartbeatSeconds)
            {
                return;
            }
            _lastBeat = now;
            try
            {
                if (!Resolve(now))
                {
                    return;
                }
                var beat = new MasterHeartbeatPacket
                {
                    Protocol = protocol == 0 ? (byte)NetConfig.ProtocolVersion : protocol,
                    Family = NetWireIdentity.Family,
                    Port = port,
                    Players = players,
                    MaxPlayers = maxPlayers,
                    Mode = mode,
                    ServerName = serverName,
                    RoomKey = roomKey
                };
                beat.Write(_scratch);
                var datagram = new byte[1 + MasterHeartbeatPacket.Size];
                datagram[0] = (byte)PacketType.MasterHeartbeat;
                _scratch.CopyTo(datagram, 1);
                _socket!.Send(datagram, datagram.Length, _endPoint);
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
        /// One datagram, sent once, and never retried: if it goes missing the
        /// silence handles it.
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
                _socket.Send(datagram, datagram.Length, _endPoint);
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
        private bool Resolve(double now)
        {
            if (_endPoint != null && now - _lastResolve < 3600)
            {
                return true;
            }
            IPAddress[] addresses = Dns.GetHostAddresses(_host);
            IPAddress? ipv4 = Array.Find(addresses,
                a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4 == null)
            {
                Complain($"{_host} has no IPv4 address");
                return false;
            }
            _lastResolve = now;
            _endPoint = new IPEndPoint(ipv4, _port);
            _socket ??= new UdpClient(AddressFamily.InterNetwork);
            _complained = false;
            return true;
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
            _socket?.Dispose();
            _socket = null;
        }
    }

}
