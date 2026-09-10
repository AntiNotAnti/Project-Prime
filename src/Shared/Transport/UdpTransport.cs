using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// UDP receives run on a dedicated worker; the simulation polls a bounded
    /// inbox with a fixed packet budget. Sends use the socket directly unless
    /// the optional impairment worker holds them. Every retained queue is bounded.
    /// </summary>
    public sealed class UdpTransport : INetTransport
    {
        private readonly UdpClient _socket;
        private readonly Thread _worker;
        private readonly Queue<ReceivedPacket> _inbox = new();
        private int _disposed;
        private long _packetsDropped;
        private volatile bool _running;

        /// <summary>Hard capacity of each inbox and simulated-delay queue.</summary>
        public const int MaxQueuedPackets = 2048;

        /// <summary>Maximum packets returned by one poll, even under a continuous flood.</summary>
        public const int MaxPacketsPerDrain = 256;

        /// <summary>
        /// Bytes the OS may hold before the worker thread gets to them. The
        /// default (64 KB) is the other half of the same problem: the worker
        /// is quick but it is still one thread, and a scheduling hiccup of
        /// fifty milliseconds with eight clients talking is a lot of bytes.
        /// </summary>
        private const int SocketBufferBytes = 1 << 20;

        private volatile bool _autoPong;
        private sealed record KeepAlive(IPEndPoint Target, byte[] Datagram);
        private KeepAlive[] _keepAlives = Array.Empty<KeepAlive>();
        private long _keepAliveDue;
        public const int MaxKeepAlives = 24; // Eight players plus sixteen observers.

        /// <summary>
        /// A client liveness packet that continues during synchronous room loading.
        /// Passing null clears it. The transport owns copies of its endpoint and bytes.
        /// </summary>
        public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> datagram = default)
        {
            PublishKeepAlives(target == null ? Array.Empty<KeepAlive>() : [CopyKeepAlive(target, datagram)]);
        }

        /// <summary>
        /// Publish at most twenty-four prepared authoritative keepalives atomically. The
        /// receive worker reads no connection, ACK or simulation state while the
        /// owner loads a room. Republish when a peer changes its endpoint.
        /// </summary>
        public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries)
        {
            if (entries.Length > MaxKeepAlives)
            {
                throw new ArgumentOutOfRangeException(nameof(entries), "At most twenty-four keepalive endpoints are supported.");
            }
            var copies = entries.IsEmpty ? Array.Empty<KeepAlive>() : new KeepAlive[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                copies[i] = CopyKeepAlive(entries[i].Endpoint, entries[i].Datagram.Span);
            }
            PublishKeepAlives(copies);
        }

        private static KeepAlive CopyKeepAlive(IPEndPoint target, ReadOnlySpan<byte> datagram)
        {
            if (target == null || target.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new ArgumentException("Keepalive requires an IPv4 endpoint.", nameof(target));
            }
            if (datagram.Length != NetHeader.Size || !NetHeader.TryRead(datagram, out NetHeader header)
                || header.Type != NetMessageType.KeepAlive)
            {
                throw new ArgumentException("Expected an authoritative keepalive header.", nameof(datagram));
            }
            return new KeepAlive(new IPEndPoint(new IPAddress(target.Address.GetAddressBytes()), target.Port), datagram.ToArray());
        }

        private void PublishKeepAlives(KeepAlive[] entries)
        {
            lock (_heldLock)
            {
                if (_running) { Volatile.Write(ref _keepAlives, entries); }
            }
        }

        /// <summary>
        /// Packets held back because <see cref="NetLag"/> is on: arrivals
        /// waiting to be handed to the game loop, and sends waiting to leave.
        ///
        /// Both are plain FIFOs and both are drained from the head only while
        /// the head is due, so a jittered hold can delay a datagram but never
        /// overtake the one in front of it. Reordering is a different fault
        /// with different guards against it, and mixing the two would make a
        /// run that reproduced one impossible to read.
        /// </summary>
        private readonly Queue<(long DueAt, ReceivedPacket Packet)> _heldIn = new();
        private readonly Queue<(long DueAt, IPEndPoint Target, byte[] Data, int Length)> _heldOut = new();
        private readonly object _heldLock = new();
        private Thread? _lagWorker;

        /// <summary>
        /// Answer Ping on this thread instead of queueing it for the game
        /// loop. Opt-in: a session that has a frame to wait for wants this
        /// and a directory server, whose replies are part of its own
        /// bookkeeping, does not.
        /// </summary>
        public void AnswerPingsImmediately() => _autoPong = true;

        public int LocalPort { get; }
        public long PacketsDropped => Interlocked.Read(ref _packetsDropped);
        public int QueuedPackets { get { lock (_heldLock) { return _inbox.Count; } } }
        public int HeldIncomingPackets { get { lock (_heldLock) { return _heldIn.Count; } } }
        public int HeldOutgoingPackets { get { lock (_heldLock) { return _heldOut.Count; } } }
        public NetTrafficMetrics Metrics { get; } = new();

        /// <summary>
        /// Packets dropped by every transport in this process, for the test
        /// harness: "the other client never saw me turn" has two very
        /// different causes, and this is what tells them apart.
        /// </summary>
        public static long TotalPacketsDropped;

        /// <summary>
        /// Packets sent by any transport in this process. Used by the
        /// headless client tests to prove the authority is actually
        /// publishing rather than silently doing nothing.
        /// </summary>
        public static long TotalPacketsSent;

        public UdpTransport(int port) : this(port, null) { }

        public UdpTransport(int port, IPAddress? bindAddress)
        {
            _socket = new UdpClient(AddressFamily.InterNetwork);
            if (OperatingSystem.IsWindows())
            {
                // SIO_UDP_CONNRESET. Without it, a peer that vanishes makes
                // Windows raise ConnectionReset on the *next* receive, which
                // would kill the worker. The control code is Windows-only and
                // throws PlatformNotSupportedException elsewhere, so it is
                // guarded rather than swallowed.
                _socket.Client.IOControl(unchecked((int)0x9800000C), new byte[] { 0, 0, 0, 0 }, null);
            }
            try
            {
                _socket.Client.ReceiveBufferSize = SocketBufferBytes;
                _socket.Client.SendBufferSize = SocketBufferBytes;
            }
            catch (SocketException)
            {
                // A system that refuses the size keeps its default; the
                // session still works, it just tolerates less of a stall.
            }
            // Only so the worker notices _running going false; nothing waits
            // on this in normal operation.
            _socket.Client.ReceiveTimeout = 500;
            _socket.Client.Bind(new IPEndPoint(bindAddress ?? IPAddress.Any, port));
            LocalPort = ((IPEndPoint)_socket.Client.LocalEndPoint!).Port;
            _running = true;
            _worker = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "MphRead net"
            };
            _worker.Start();
            if (NetLag.Active)
            {
                // Only when asked for. A thread that wakes a thousand times a
                // second to look at an empty queue is not something a real
                // session should be paying for.
                _lagWorker = new Thread(LagLoop)
                {
                    IsBackground = true,
                    Name = "MphRead net lag"
                };
                _lagWorker.Start();
            }
        }

        /// <summary>
        /// Let out whatever the simulated line has finished holding.
        ///
        /// Only the send side needs a thread of its own: arrivals are promoted
        /// by <see cref="Drain"/>, which the game loop calls every frame
        /// anyway, and a send held until the next frame would put a frame of
        /// latency on top of the one being simulated.
        /// </summary>
        private void LagLoop()
        {
            while (_running)
            {
                long now = Stopwatch.GetTimestamp();
                for (int i = 0; i < MaxPacketsPerDrain && _running; i++)
                {
                    (long DueAt, IPEndPoint Target, byte[] Data, int Length) held;
                    lock (_heldLock)
                    {
                        if (_heldOut.Count == 0 || _heldOut.Peek().DueAt > now)
                        {
                            break;
                        }
                        held = _heldOut.Dequeue();
                    }
                    SendNow(held.Target, held.Data.AsSpan(0, held.Length));
                }
                Thread.Sleep(1);
            }
        }

        private void ReceiveLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    KeepAlive[] keepAlives = Volatile.Read(ref _keepAlives);
                    if (keepAlives.Length > 0)
                    {
                        long now = Stopwatch.GetTimestamp();
                        if (now >= _keepAliveDue)
                        {
                            foreach (KeepAlive keepAlive in keepAlives)
                            {
                                SendDatagram(keepAlive.Target, keepAlive.Datagram);
                            }
                            _keepAliveDue = now + Stopwatch.Frequency;
                        }
                    }
                    // Blocking, with a timeout only so shutdown is prompt.
                    //
                    // This used to poll Available and Thread.Sleep(1) between
                    // passes, which put an arrival delay on the front of every
                    // packet the session ever received -- a millisecond at
                    // best, and Sleep(1) is not a millisecond on Windows,
                    // where the scheduler's tick is 15.6 ms unless something
                    // in the process has raised the timer resolution. The
                    // cost showed up as ping: the number on the scoreboard is
                    // a round trip through two of these loops, so a server
                    // one millisecond away by ICMP was reported at rather
                    // more. Blocking costs nothing -- the thread exists for
                    // this and does nothing else -- and hands the packet over
                    // the moment the kernel has it.
                    IPEndPoint sender = any;
                    byte[] data;
                    try
                    {
                        data = _socket.Receive(ref sender);
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        continue;
                    }
                    Metrics.Received(data.Length);
                    if (data.Length == 0 || data.Length > NetConfig.MaxPacketSize)
                    {
                        Metrics.Reject();
                        continue;
                    }
                    // Answered here rather than from the game loop, when the
                    // session asked for it.
                    //
                    // A Pong needs no game state: it echoes the id back so the
                    // server can match the reply to the ping it sent. Waiting
                    // for the next frame to do that added anything up to a
                    // whole frame -- half of one on average, more when the
                    // frame ran long -- to a measurement whose entire purpose
                    // is to describe the network. Every player's ping read
                    // about a frame worse than their connection.
                    if (_autoPong && data.Length >= 1 && (PacketType)data[0] == PacketType.Ping)
                    {
                        // The reply carries *both* halves of a simulated line.
                        // This ping was answered here, on the transport
                        // thread, before the hold below ever looked at it --
                        // so without the extra half the round trip the server
                        // measures comes out at half what was asked for, and
                        // the one number a latency run is read by would be
                        // describing the instrument.
                        Send(sender, PacketType.Pong, data.AsSpan(1),
                            _lagWorker != null ? NetLag.HoldTicks() : 0);
                        continue;
                    }
                    // The worker rather than NetLag.Active, so the two
                    // halves cannot disagree: nothing may be held back unless
                    // there is something running that lets it out again.
                    if (_lagWorker != null)
                    {
                        if (NetLag.Drops())
                        {
                            Metrics.DropSimulated();
                            continue;
                        }
                        long holdFor = NetLag.HoldTicks();
                        if (holdFor > 0)
                        {
                            lock (_heldLock)
                            {
                                if (_running)
                                {
                                    MakeRoom(_heldIn);
                                    _heldIn.Enqueue((Stopwatch.GetTimestamp() + holdFor,
                                        new ReceivedPacket(sender, data, data.Length)));
                                }
                            }
                            continue;
                        }
                    }
                    Enqueue(new ReceivedPacket(sender, data, data.Length));
                }
                catch (SocketException)
                {
                    // Transient: an ICMP unreachable from a peer that left.
                    // Keep serving the peers that are still here.
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Return at most MaxPacketsPerDrain packets. The caller's simulation tick
        /// must finish even when the receive worker continuously fills the queue.
        /// </summary>
        public IEnumerable<ReceivedPacket> Drain()
        {
            if (_lagWorker != null)
            {
                PromoteHeldArrivals();
            }
            for (int i = 0; i < MaxPacketsPerDrain; i++)
            {
                ReceivedPacket packet;
                lock (_heldLock)
                {
                    if (!_inbox.TryDequeue(out packet))
                    {
                        yield break;
                    }
                }
                yield return packet;
            }
        }

        public int Drain(Span<ReceivedPacket> destination)
        {
            if (_lagWorker != null) PromoteHeldArrivals();
            int count = 0;
            int limit = Math.Min(destination.Length, MaxPacketsPerDrain);
            lock (_heldLock)
            {
                while (count < limit && _inbox.TryDequeue(out ReceivedPacket packet))
                    destination[count++] = packet;
            }
            return count;
        }

        // All queue mutations share one short lock. This keeps capacities exact
        // across the receiver, delayed-arrival promotion and replay playback.
        private void Enqueue(ReceivedPacket packet)
        {
            lock (_heldLock)
            {
                if (_running)
                {
                    MakeRoom(_inbox);
                    _inbox.Enqueue(packet);
                }
            }
        }

        private void MakeRoom<T>(Queue<T> queue)
        {
            if (queue.Count == MaxQueuedPackets)
            {
                // Prefer fresh state; reliable messages recover through retries.
                queue.Dequeue();
                Interlocked.Increment(ref _packetsDropped);
                Metrics.DropQueued();
                Interlocked.Increment(ref TotalPacketsDropped);
            }
        }

        private void PromoteHeldArrivals()
        {
            long now = Stopwatch.GetTimestamp();
            lock (_heldLock)
            {
                for (int i = 0; i < MaxPacketsPerDrain; i++)
                {
                    if (_heldIn.Count == 0 || _heldIn.Peek().DueAt > now)
                    {
                        break;
                    }
                    MakeRoom(_inbox);
                    _inbox.Enqueue(_heldIn.Dequeue().Packet);
                }
            }
        }

        private static readonly IPEndPoint _playbackSender = new(IPAddress.Loopback, 0);

        /// <summary>
        /// Feeds a packet read back from a replay file into the same queue a
        /// real receive would have used, so <see cref="Drain"/> and
        /// everything downstream of it (<c>NetSession.Handle</c> and every
        /// packet-type handler) runs completely unchanged during playback --
        /// the sender endpoint is never checked by any of it, only the
        /// bytes. The socket this transport opened is never used for real
        /// traffic in that mode; it exists only because the constructor
        /// always binds one.
        /// </summary>
        public void EnqueueForPlayback(byte[] data, int length)
        {
            if (length <= 0 || length > NetConfig.MaxPacketSize || length > data.Length)
            {
                Metrics.Reject();
                return;
            }
            Enqueue(new ReceivedPacket(_playbackSender, data, length));
        }

        /// <param name="extraHoldTicks">
        /// More simulated line to hold this datagram behind, on top of the
        /// outbound half. Only the automatic Pong uses it; see the call site.
        /// </param>
        public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> payload,
            long extraHoldTicks = 0)
        {
            if (payload.Length >= NetConfig.MaxPacketSize)
            {
                Metrics.Reject();
                return;
            }
            Span<byte> buffer = stackalloc byte[NetConfig.MaxPacketSize];
            buffer[0] = (byte)type;
            payload.CopyTo(buffer[1..]);
            SendDatagram(target, buffer[..(payload.Length + 1)], extraHoldTicks);
        }

        /// <summary>Send a complete datagram through the same UDP impairment path.</summary>
        void INetDatagramSink.SendDatagram(IPEndPoint endpoint, ReadOnlySpan<byte> datagram)
            => SendDatagram(endpoint, datagram);

        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram, long extraHoldTicks = 0)
        {
            if (datagram.Length == 0 || datagram.Length > NetConfig.MaxPacketSize)
            {
                Metrics.Reject();
                return;
            }
            if (!_running)
            {
                return;
            }
            if (_lagWorker != null)
            {
                if (NetLag.Drops())
                {
                    Metrics.DropSimulated();
                    return;
                }
                long holdFor = NetLag.HoldTicks() + extraHoldTicks;
                if (holdFor > 0)
                {
                    // Copied, because the caller's span is a scratch buffer it
                    // is about to write the next packet into.
                    lock (_heldLock)
                    {
                        if (_running)
                        {
                            MakeRoom(_heldOut);
                            byte[] copy = datagram.ToArray();
                            _heldOut.Enqueue((Stopwatch.GetTimestamp() + holdFor,
                                target, copy, copy.Length));
                        }
                    }
                    return;
                }
            }
            SendNow(target, datagram);
        }

        private void SendNow(IPEndPoint target, ReadOnlySpan<byte> datagram)
        {
            try
            {
                _socket.Send(datagram, target);
                Metrics.Sent(datagram.Length);
                Interlocked.Increment(ref TotalPacketsSent);
            }
            catch (SocketException)
            {
                Metrics.SendFailed();
                // Same rationale as above: one unreachable peer must not
                // take down the session for everyone else.
            }
            catch (ObjectDisposedException)
            {
                // The session ended while the simulated line still held this.
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }
            lock (_heldLock)
            {
                _running = false;
                Volatile.Write(ref _keepAlives, Array.Empty<KeepAlive>());
            }
            _socket.Dispose(); // Interrupt the blocking receive before joining it.
            _worker.Join();
            _lagWorker?.Join();
            lock (_heldLock)
            {
                _inbox.Clear();
                _heldIn.Clear();
                _heldOut.Clear();
            }
        }
    }
}
