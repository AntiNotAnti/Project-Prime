using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest
{
    /// <summary>Actual server-process rotation, loading and late-join regression under seeded UDP impairment.</summary>
    internal static class MatchLifecycleCheck
    {
        private const string FirstRoom = "MP1 SANCTORUS";
        private const string SecondRoom = "MP3 PROVING GROUND";

        public static int Run(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage: nettest --match-lifecycle DATA_DIRECTORY (approximately 45 seconds)");
                return 2;
            }
            try
            {
                using var server = new ChildServer(args[1]);
                using var fixture = new Fixture(server);
                fixture.Run();
                server.StopAndVerify();
                Console.WriteLine("MATCHLIFECYCLE PASS");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("MATCHLIFECYCLE FAIL " + ex);
                return 1;
            }
        }

        private sealed class Participant : IDisposable
        {
            public readonly NetTransport Transport = new(0);
            public readonly NetClient Client;
            public readonly ClientWorldState World = new();
            public readonly ImpairedLink Link;
            public readonly bool[] CompleteEpoch = new bool[5];
            public readonly bool[] PlayingEpoch = new bool[5];
            public uint Epoch;
            public uint Sequence;
            public ulong Identity;
            public byte Slot;
            public double LoadUntil;
            public long Replayed;
            public int Frames;
            public int Epochs;
            public bool LateJoin;

            public Participant(int serverPort, int index, bool lateJoin)
            {
                Link = new ImpairedLink(serverPort, 8421 + index);
                Client = new NetClient(Transport, Link.Endpoint, "ROTATION" + index, (Hunter)index);
                Client.WorldPacketValidator = WorldPacket.TryValidate;
                Client.WorldPacketReceived = body =>
                {
                    if (World.Receive(body)) { Frames++; }
                };
                LateJoin = lateJoin;
            }
            public void Dispose() { Client.Dispose(); Transport.Dispose(); Link.Dispose(); }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly ChildServer _server;
            private readonly List<Participant> _players = new(3);
            private readonly Stopwatch _timer = Stopwatch.StartNew();
            private readonly FixedTickScheduler _scheduler = new();
            private readonly InputCommand[] _input = new InputCommand[1];
            private readonly byte[] _payload = new byte[InputBundle.MaxSize];
            private bool _lateJoined;

            public Fixture(ChildServer server)
            {
                _server = server;
                _players.Add(new Participant(server.Port, 0, false));
                _players.Add(new Participant(server.Port, 1, false));
            }

            public void Run()
            {
                while (_timer.Elapsed.TotalSeconds < 75)
                {
                    if (_server.Exited) { throw new InvalidOperationException("Server exited unexpectedly. " + _server.Diagnostics); }
                    if (_scheduler.TakeDue(Stopwatch.GetTimestamp()) == 0) { _scheduler.Wait(); continue; }
                    for (int i = 0; i < _players.Count; i++) { Step(_players[i], i); }
                    if (!_lateJoined && _players[0].CompleteEpoch[3] && _players[1].CompleteEpoch[3])
                    {
                        _players.Add(new Participant(_server.Port, 2, true));
                        _lateJoined = true;
                        Console.WriteLine("MATCHLIFECYCLE late join started during match3");
                    }
                    if (_lateJoined && Finished())
                    {
                        for (int i = 0; i < _players.Count; i++)
                        {
                            Participant player = _players[i];
                            if (player.Link.Replayed == 0 || player.Link.QueueDrops != 0 || player.Link.Failure != null)
                            { throw new InvalidOperationException("Impairment proxy failed to exercise bounded stale replay."); }
                            Console.WriteLine($"MATCHLIFECYCLE client={i} slot={player.Slot} epochs={player.Epochs} worldFrames={player.Frames} staleReplays={player.Link.Replayed} rejected={player.Client.Rejected} PASS");
                        }
                        return;
                    }
                }
                throw new TimeoutException("Rotation did not produce four valid epochs before75seconds. " + _server.Diagnostics);
            }

            private bool Finished()
            {
                for (int i = 0; i < _players.Count; i++)
                {
                    Participant player = _players[i];
                    int first = player.LateJoin ? 3 : 1;
                    for (int epoch = first; epoch <= 4; epoch++)
                    {
                        if (!player.CompleteEpoch[epoch] || !player.PlayingEpoch[epoch]) { return false; }
                    }
                    if (player.Epoch != 4 || player.Client.State != NetConnectionState.Playing
                        || !player.Client.Snapshot.HasProcessedInput || player.Client.Snapshot.LastProcessedInput < 5) { return false; }
                }
                return true;
            }

            private void Step(Participant player, int index)
            {
                if (player.Link.Failure != null) { throw new InvalidOperationException("Proxy failed.", player.Link.Failure); }
                double now = _timer.Elapsed.TotalSeconds;
                // During this interval there are no owner-thread polls. The
                // transport's independent keepalive continues while "loading".
                if (player.Epoch != 0 && player.Client.State == NetConnectionState.Loading && now < player.LoadUntil) { return; }
                player.Client.Poll();
                NetClient client = player.Client;
                if (client.Failure != null) { throw new InvalidOperationException($"Client{index}: {client.Failure}"); }
                if (client.Connection == null) { return; }
                uint epoch = client.Accepted.MatchId;
                if (epoch != player.Epoch)
                {
                    if (epoch > 4 || epoch < 1 || (player.Epoch != 0 && epoch != player.Epoch + 1))
                    { throw new InvalidOperationException($"Unexpected epoch {player.Epoch}->{epoch}."); }
                    if (player.Epoch == 0)
                    {
                        if (epoch != (player.LateJoin ? 3u : 1u)) { throw new InvalidOperationException("Late join entered the wrong match."); }
                        player.Identity = client.Connection.Id; player.Slot = client.Accepted.Slot;
                    }
                    else if (client.Connection.Id != player.Identity || client.Accepted.Slot != player.Slot)
                    { throw new InvalidOperationException("A map transition replaced the live connection identity."); }
                    string room = epoch == 3 ? SecondRoom : FirstRoom;
                    GameMode mode = epoch == 2 ? GameMode.Nodes : GameMode.Battle;
                    if (client.Accepted.Room != room || client.Accepted.Mode != mode || client.State != NetConnectionState.Loading
                        || client.HasSnapshot || client.SnapshotPlayers.Length != 0)
                    { throw new InvalidOperationException("Transition did not atomically reset client match state."); }
                    player.Epoch = epoch; player.Sequence = 0; player.Epochs++;
                    player.World.Reset(epoch);
                    player.LoadUntil = now + (epoch == 2 && index == 0 ? 2.5 : 0.5);
                    if (epoch > 1)
                    {
                        // Old commands carry fresh connection sequence numbers;
                        // rejection therefore depends on the match identity.
                        _input[0] = new InputCommand(1_000_000, 1_000_000, 0, InputButtons.Shoot,
                            InputButtons.Shoot, -Vector3.UnitZ, InputCommand.NoWeapon);
                        int length = InputBundle.Write(_payload, epoch - 1, _input,
                            player.World.HasState ? player.World.PhaseRevision : 0);
                        client.Connection.Send(player.Transport, NetMessageType.Input, _payload.AsSpan(0, length));
                        Span<byte> ready = stackalloc byte[4];
                        BinaryPrimitives.WriteUInt32LittleEndian(ready, epoch - 1);
                        if (!client.Connection.Reliable.TryEnqueue(ReliableEventType.ClientReady, ready, out _))
                        { throw new InvalidOperationException("Could not enqueue stale Ready fixture."); }
                        client.Connection.FlushReliable(player.Transport, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
                        player.Link.ReplayOldPackets();
                    }
                    Console.WriteLine($"MATCHLIFECYCLE client={index} epoch={epoch} loading room={room} mode={mode}");
                    return;
                }
                if (client.State == NetConnectionState.Loading)
                {
                    if (client.HasSnapshot) { throw new InvalidOperationException("Stale Ready activated a loading player."); }
                    if (!client.Ready(epoch)) { throw new InvalidOperationException("ClientReady failed for current epoch."); }
                }
                if (client.HasSnapshot)
                {
                    if (client.Snapshot.MatchId != epoch || client.Snapshot.LastProcessedInput >= 1_000_000)
                    { throw new InvalidOperationException("Stale input or snapshot crossed the match boundary."); }
                    foreach (SnapshotPlayer state in client.SnapshotPlayers)
                    {
                        if (state.Points != 0 || state.Kills != 0 || state.Deaths != 0)
                        { throw new InvalidOperationException("Unarmed neutral clients gained a score across the match reset."); }
                        if (state.Slot == player.Slot && state.ConnectionId != player.Identity)
                        { throw new InvalidOperationException("Snapshot changed the local session identity."); }
                    }
                }
                while (client.TryDequeueEvent(out NetApplicationEvent message))
                {
                    if (message.MatchId != epoch) { throw new InvalidOperationException("Old reliable event survived a transition."); }
                }
                if (player.World.HasState)
                {
                    WorldRecord match = player.World.Records[0];
                    if (player.World.MatchId != epoch || (GameMode)match.A != client.Accepted.Mode)
                    { throw new InvalidOperationException("World state crossed the match boundary."); }
                    if (player.World.Phase is MatchPhase.Countdown or MatchPhase.Playing && match.Position.X > 0)
                    { player.CompleteEpoch[epoch] = true; }
                }
                if (client.State == NetConnectionState.Playing)
                {
                    player.PlayingEpoch[epoch] = true;
                    if (epoch > 1 && player.Replayed != epoch)
                    {
                        player.Link.ReplayOldPackets(); player.Replayed = epoch;
                        _input[0] = new InputCommand(1_000_000, 1_000_000, 0, InputButtons.Shoot,
                            InputButtons.Shoot, -Vector3.UnitZ, InputCommand.NoWeapon);
                        int length = InputBundle.Write(_payload, epoch - 1, _input, player.World.PhaseRevision);
                        client.Connection!.Send(player.Transport, NetMessageType.Input, _payload.AsSpan(0, length));
                    }
                    if (player.World.HasState && player.World.Phase == MatchPhase.Playing)
                    {
                        uint sequence = player.Sequence++;
                        _input[0] = new InputCommand(sequence, sequence, client.Snapshot.ServerTick,
                            InputButtons.None, InputButtons.None, -Vector3.UnitZ, InputCommand.NoWeapon);
                        client.SendInputs(_input, player.World.PhaseRevision);
                    }
                }
            }

            public void Dispose()
            {
                foreach (Participant player in _players) { player.Dispose(); }
            }
        }

        private sealed class ChildServer : IDisposable
        {
            private readonly Process _process;
            private readonly string _temporary;
            private readonly ConcurrentQueue<string> _lines = new();
            private readonly ManualResetEventSlim _ready = new();
            private int _port;
            public int Port => Volatile.Read(ref _port);
            public bool Exited => _process.HasExited;
            public string Diagnostics => String.Join(Environment.NewLine, _lines);

            public ChildServer(string data)
            {
                _temporary = Path.Combine(Path.GetTempPath(), "fruity-match-check-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_temporary);
                string rotation = Path.Combine(_temporary, "rotation.txt");
                File.WriteAllLines(rotation, new[] { FirstRoom + " | Battle | 0.1 | 0", FirstRoom + " | Nodes | 0.1 | 0", SecondRoom + " | Battle | 0.1 | 0" });
                string executable = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                    ?? (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet" ? Environment.ProcessPath! : "dotnet");
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = AppContext.BaseDirectory };
                foreach (string argument in new[] { Path.Combine(AppContext.BaseDirectory, "FruityPrime.dll"), "-server", "-port", "0",
                    "-data", Path.GetFullPath(data), "-dataversion", "AMHE1", "-rotation", rotation, "-players", "3", "-parent-stdin", "-nomaster", "-noupdate" })
                { start.ArgumentList.Add(argument); }
                _process = new Process { StartInfo = start, EnableRaisingEvents = true };
                _process.OutputDataReceived += (_, e) => Observe(e.Data);
                _process.ErrorDataReceived += (_, e) => Observe(e.Data);
                _process.Exited += (_, _) => _ready.Set();
                try
                {
                    if (!_process.Start()) { throw new InvalidOperationException("Could not start server child."); }
                    _process.BeginOutputReadLine(); _process.BeginErrorReadLine();
                    if (!_ready.Wait(TimeSpan.FromSeconds(20)) || Port == 0)
                    { throw new InvalidOperationException("Server did not bind. " + Diagnostics); }
                }
                catch { Dispose(); throw; }
            }
            private void Observe(string? line)
            {
                if (line == null) { return; }
                _lines.Enqueue(line); while (_lines.Count > 80) { _lines.TryDequeue(out _); }
                const string marker = "listening on UDP ";
                int index = line.IndexOf(marker, StringComparison.Ordinal);
                if (index >= 0)
                {
                    ReadOnlySpan<char> value = line.AsSpan(index + marker.Length);
                    int end = value.IndexOf(';');
                    if (end > 0 && Int32.TryParse(value[..end], out int port)) { Volatile.Write(ref _port, port); _ready.Set(); }
                }
                if (line.Contains("[server] match=", StringComparison.Ordinal)) { Console.WriteLine(line); }
            }
            public void StopAndVerify()
            {
                if (!_process.HasExited)
                {
                    _process.StandardInput.Close();
                    if (!_process.WaitForExit(5000))
                    {
                        _process.Kill(entireProcessTree: true); _process.WaitForExit();
                        throw new InvalidOperationException("Server did not stop when its parent closed stdin.");
                    }
                }
                _process.WaitForExit();
                if (_process.ExitCode != 0) { throw new InvalidOperationException("Server shutdown failed: " + Diagnostics); }
            }
            public void Dispose()
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.StandardInput.Close();
                        if (!_process.WaitForExit(5000)) { _process.Kill(entireProcessTree: true); _process.WaitForExit(); }
                    }
                    _process.WaitForExit();
                }
                catch (InvalidOperationException) { }
                _process.Dispose(); _ready.Dispose();
                if (Directory.Exists(_temporary)) { Directory.Delete(_temporary, recursive: true); }
            }
        }

        private sealed class ImpairedLink : IDisposable
        {
            private readonly UdpClient _socket = new(new IPEndPoint(IPAddress.Loopback, 0));
            private readonly IPEndPoint _server;
            private readonly Thread _thread;
            private readonly Random _random;
            private readonly PriorityQueue<(byte[] Data, IPEndPoint Target), long> _pending = new();
            private readonly byte[]?[] _old = new byte[4][];
            private IPEndPoint? _client;
            private volatile bool _running = true;
            private int _replay;
            private long _replayed;
            private long _queueDrops;
            private Exception? _failure;
            public Exception? Failure => Volatile.Read(ref _failure);
            public long Replayed => Interlocked.Read(ref _replayed);
            public long QueueDrops => Interlocked.Read(ref _queueDrops);
            public IPEndPoint Endpoint { get; }

            public ImpairedLink(int serverPort, int seed)
            {
                Endpoint = (IPEndPoint)_socket.Client.LocalEndPoint!;
                _server = new IPEndPoint(IPAddress.Loopback, serverPort);
                _random = new Random(seed);
                _thread = new Thread(Run) { IsBackground = true, Name = "Match-check UDP impairment" };
                _thread.Start();
            }
            public void ReplayOldPackets() => Interlocked.Exchange(ref _replay, 1);
            private void Run()
            {
                try
                {
                    while (_running)
                    {
                        if (Interlocked.Exchange(ref _replay, 0) != 0 && _client != null)
                        {
                            for (int i = 0; i < _old.Length; i++)
                            {
                                if (_old[i] == null) { continue; }
                                Queue(_old[i]!, i == 0 ? _server : _client);
                                Interlocked.Increment(ref _replayed);
                            }
                        }
                        for (int count = 0; count < 128 && _socket.Available > 0; count++)
                        {
                            IPEndPoint from = new(IPAddress.Any, 0);
                            byte[] data = _socket.Receive(ref from);
                            bool outbound = !from.Equals(_server);
                            if (outbound) { _client = from; }
                            IPEndPoint? target = outbound ? _server : _client;
                            if (target == null) { continue; }
                            if (NetHeader.TryRead(data, out NetHeader header))
                            {
                                int cache = header.Type switch { NetMessageType.Input when outbound => 0,
                                    NetMessageType.Snapshot when !outbound => 1, NetMessageType.World when !outbound => 2,
                                    NetMessageType.Event when !outbound => 3, _ => -1 };
                                if (cache >= 0 && _old[cache] == null) { _old[cache] = data; }
                            }
                            if (_random.Next(100) < 3) { continue; }
                            Queue(data, target);
                            if (_random.Next(100) < 2) { Queue(data, target); }
                        }
                        long now = Stopwatch.GetTimestamp();
                        for (int count = 0; count < 128 && _pending.TryPeek(out _, out long due) && due <= now; count++)
                        {
                            var packet = _pending.Dequeue(); _socket.Send(packet.Data, packet.Target);
                        }
                        Thread.Sleep(1);
                    }
                }
                catch (Exception ex) { Volatile.Write(ref _failure, ex); }
            }
            private void Queue(byte[] data, IPEndPoint target)
            {
                if (_pending.Count == 4096) { Interlocked.Increment(ref _queueDrops); return; }
                // 250 +/-50ms RTT, independently impaired in both directions.
                long due = Stopwatch.GetTimestamp() + (long)((0.1 + _random.NextDouble() * 0.05) * Stopwatch.Frequency);
                _pending.Enqueue((data, target), due);
            }
            public void Dispose()
            {
                _running = false;
                if (!_thread.Join(3000)) { throw new InvalidOperationException("Impairment worker did not stop."); }
                _socket.Dispose();
            }
        }
    }
}
