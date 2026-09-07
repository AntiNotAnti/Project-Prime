using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

/// <summary>
/// Headless observer capacity and delayed-history soak. The clients only
/// exercise the network protocol; no renderer or client scene is required.
/// </summary>
internal static class ObserverSoakCheck
{
    private const string FirstRoom = "MP1 SANCTORUS";
    private const string SecondRoom = "MP3 PROVING GROUND";
    private const int PlayerCount = 8;
    private const int ObserverCount = 16;
    private const int ObserverDelaySeconds = 3;
    private const double SetupTimeoutSeconds = 25;
    private const double FreshnessSlackSeconds = 10;

    public static int Run(string[] args)
    {
        if (args.Length != 4 || !Int32.TryParse(args[2], out int seconds)
            || seconds is < 10 or > 300)
        {
            Console.Error.WriteLine("Usage: nettest --observer-soak DATA_DIRECTORY SECONDS REPORT_JSON (10..300 seconds)");
            return 2;
        }

        string data = Path.GetFullPath(args[1]);
        string report = Path.GetFullPath(args[3]);
        string telemetry = report + ".telemetry-" + Guid.NewGuid().ToString("N");
        string replays = report + ".replays-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(report) ?? AppContext.BaseDirectory);
            Directory.CreateDirectory(telemetry);
            Directory.CreateDirectory(replays);
            using var server = new ChildServer(data, telemetry, replays);
            using var fixture = new Fixture(server, seconds);
            fixture.Run();
            server.StopAndVerify();
            fixture.WriteReport(report, telemetry, replays);
            Console.WriteLine($"OBSERVER SOAK PASS players={PlayerCount} observers={ObserverCount} seconds={seconds} report={report}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("OBSERVER SOAK FAIL " + ex);
            return 1;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ChildServer _server;
        private readonly int _seconds;
        private readonly List<Probe> _players = new(PlayerCount);
        private readonly List<Probe> _observers = new(ObserverCount);
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly InputCommand[] _input = new InputCommand[1];
        private double _nextInputAt;
        private double _maxObserverAgeSeconds;
        private uint _maxObserverAgeTicks;
        private bool _observerInputsNeverAccepted = true;
        private int _telemetryFiles;
        private int _telemetryEvents;
        private long? _firstServerWorkingSet;
        private long? _lastServerWorkingSet;
        private long _peakServerWorkingSet;
        private long? _firstServerPrivateMemory;
        private long? _lastServerPrivateMemory;
        private long? _peakServerPrivateMemory;
        private int _serverMemorySamples;
        private double _nextMemorySampleAt;
        private readonly HashSet<uint> _matchIds = new();
        private readonly HashSet<string> _rooms = new(StringComparer.Ordinal);

        public Fixture(ChildServer server, int seconds)
        {
            _server = server;
            _seconds = seconds;
            for (int i = 0; i < PlayerCount; i++)
                _players.Add(new Probe(server.Port, $"SOAK-P{i}", (Hunter)i, observer: false));
        }

        public void Run()
        {
            double setupDeadline = _clock.Elapsed.TotalSeconds + SetupTimeoutSeconds;
            while (_clock.Elapsed.TotalSeconds < setupDeadline && !_players.All(IsLivePlayer))
            {
                Pump(_players);
                CheckProcesses();
                Thread.Sleep(2);
            }
            Require(_players.All(IsLivePlayer), "Eight player streams did not reach Playing before the setup deadline.");

            // The delayed observer baseline needs three seconds of complete
            // history. Keep every player alive while the history warms.
            double warmUntil = _clock.Elapsed.TotalSeconds + ObserverDelaySeconds + 1;
            while (_clock.Elapsed.TotalSeconds < warmUntil)
            {
                Pump(_players);
                CheckProcesses();
                Thread.Sleep(2);
            }

            for (int i = 0; i < ObserverCount; i++)
                _observers.Add(new Probe(_server.Port, $"SOAK-O{i}", (Hunter)(i % 8), observer: true));

            setupDeadline = _clock.Elapsed.TotalSeconds + SetupTimeoutSeconds;
            while (_clock.Elapsed.TotalSeconds < setupDeadline && !_observers.All(IsLiveObserver))
            {
                Pump(_players);
                Pump(_observers);
                CheckProcesses();
                Thread.Sleep(2);
            }
            Require(_observers.All(IsLiveObserver), "Sixteen observer streams did not reach Playing before the setup deadline.");

            double soakUntil = _clock.Elapsed.TotalSeconds + _seconds;
            while (_clock.Elapsed.TotalSeconds < soakUntil)
            {
                Pump(_players);
                Pump(_observers);
                CheckProcesses();
                ObserveFreshness();
                Thread.Sleep(2);
            }

            VerifyLiveStreams();
        }

        private void Pump(IEnumerable<Probe> probes)
        {
            foreach (Probe probe in probes)
            {
                probe.Client.Poll();
                if (probe.Client.Failure != null)
                    throw new InvalidOperationException($"{probe.Name}: {probe.Client.Failure}");
                probe.EnsureReady();
                probe.Track(_clock.Elapsed.TotalSeconds, _matchIds, _rooms);
                while (probe.Client.TryDequeueEvent(out NetApplicationEvent applicationEvent))
                {
                    probe.Events++;
                    if (applicationEvent.MatchId != probe.Client.Accepted.MatchId)
                        throw new InvalidOperationException($"{probe.Name}: event crossed match boundary.");
                }
            }

            double now = _clock.Elapsed.TotalSeconds;
            if (now >= _nextInputAt)
            {
                _nextInputAt = now + 1.0 / 30;
                for (int i = 0; i < _players.Count; i++)
                {
                    Probe player = _players[i];
                    if (player.Client.State == NetConnectionState.Playing && player.World.HasState
                        && player.World.Phase == MatchPhase.Playing && player.World.PhaseRevision != 0)
                    {
                        uint sequence = player.NextInput++;
                        _input[0] = new InputCommand(sequence, sequence, player.Client.Snapshot.ServerTick,
                            InputButtons.None, InputButtons.None, -OpenTK.Mathematics.Vector3.UnitZ,
                            InputCommand.NoWeapon);
                        if (!player.Client.SendInputs(_input, player.World.PhaseRevision))
                            throw new InvalidOperationException($"{player.Name}: legal neutral input was refused.");
                    }
                }
            }

            foreach (Probe observer in _observers)
            {
                if (observer.Client.SendInputs(ReadOnlySpan<InputCommand>.Empty))
                    _observerInputsNeverAccepted = false;
            }
            SampleServerMemory();
        }

        private void ObserveFreshness()
        {
            uint liveTick = 0;
            uint liveMatch = 0;
            foreach (Probe player in _players)
            {
                if (player.Client.HasSnapshot && Sequence32.IsNewer(player.Client.Snapshot.ServerTick, liveTick))
                {
                    liveTick = player.Client.Snapshot.ServerTick;
                    liveMatch = player.Client.Accepted.MatchId;
                }
            }
            if (liveTick == 0) return;
            foreach (Probe observer in _observers)
            {
                if (!observer.World.HasState || observer.World.MatchId != liveMatch) continue;
                uint age = unchecked(liveTick - observer.World.ServerTick);
                if (age > Int32.MaxValue) continue;
                _maxObserverAgeTicks = Math.Max(_maxObserverAgeTicks, age);
                double ageSeconds = age / 60.0;
                _maxObserverAgeSeconds = Math.Max(_maxObserverAgeSeconds, ageSeconds);
                if (ageSeconds > ObserverDelaySeconds + FreshnessSlackSeconds)
                    throw new InvalidOperationException($"{observer.Name}: delayed world age {ageSeconds:0.0}s exceeded {ObserverDelaySeconds + FreshnessSlackSeconds:0.0}s.");
            }
        }

        private void VerifyLiveStreams()
        {
            Require(_observerInputsNeverAccepted, "Observer SendInputs unexpectedly succeeded.");
            Require(_matchIds.Count >= 2 && _rooms.Count >= 2,
                $"Rotation did not expose two maps (matches={_matchIds.Count}, rooms={_rooms.Count}).");
            // A rotation can legitimately leave every client in Loading for a
            // few owner ticks. Capacity is the connection invariant; setup
            // and the per-client stream counters prove that the session was
            // live before and during the transition.
            Require(_players.All(IsConnected), "A player connection was not alive at the end of the soak: "
                + String.Join(", ", _players.Where(p => !IsConnected(p)).Select(p => p.Status)));
            Require(_observers.All(IsConnected), "An observer connection was not alive at the end of the soak: "
                + String.Join(", ", _observers.Where(p => !IsConnected(p)).Select(p => p.Status)));

            int rejectionBudget = Math.Max(8, _matchIds.Count * 4);
            foreach (Probe probe in _players.Concat(_observers))
            {
                // A transition fence can discard a small number of already
                // queued packets from the completed match. Keep that bounded
                // and visible without treating the expected race as a queue
                // failure; transport drops and send errors remain fatal below.
                Require(probe.Client.Rejected <= rejectionBudget,
                    $"{probe.Name}: rejected datagrams={probe.Client.Rejected}, budget={rejectionBudget}.");
                Require(probe.Transport.PacketsDropped == 0 && probe.Transport.QueuedPackets == 0
                    && probe.Transport.Metrics.QueueDrops == 0 && probe.Transport.Metrics.SendErrors == 0,
                    $"{probe.Name}: transport queue failure/drop detected ({probe.Transport.Metrics.Describe()}).");
                Require(probe.WorldFrames > 0 && probe.Snapshots > 0,
                    $"{probe.Name}: no complete world/snapshot stream was observed.");
            }
            double now = _clock.Elapsed.TotalSeconds;
            foreach (Probe observer in _observers)
            {
                Require(now - observer.LastSnapshotAt <= ObserverDelaySeconds + FreshnessSlackSeconds,
                    $"{observer.Name}: snapshot stream was stale for {now - observer.LastSnapshotAt:0.0}s.");
                Require(now - observer.LastWorldAt <= ObserverDelaySeconds + FreshnessSlackSeconds,
                    $"{observer.Name}: world stream was stale for {now - observer.LastWorldAt:0.0}s.");
            }
        }

        private void CheckProcesses()
        {
            if (_server.Exited)
                throw new InvalidOperationException("Server exited unexpectedly: " + _server.Diagnostics);
        }

        private static bool IsLivePlayer(Probe probe) => probe.Client.State == NetConnectionState.Playing
            && probe.Client.HasSnapshot && probe.Client.Connection != null;

        private static bool IsConnected(Probe probe) => probe.Client.Connection != null
            && probe.Client.State != NetConnectionState.Disconnecting && probe.Client.Failure == null;

        private static bool IsLiveObserver(Probe probe) => probe.Client.IsObserver
            && probe.Client.State == NetConnectionState.Playing && probe.Client.HasSnapshot
            && probe.Client.Connection != null && probe.WorldFrames > 0;

        public void WriteReport(string path, string telemetryDirectory, string replayDirectory)
        {
            foreach (string file in Directory.EnumerateFiles(telemetryDirectory, "*.telemetry.json.gz"))
            {
                _telemetryFiles++;
                using var stream = new GZipStream(File.OpenRead(file), CompressionMode.Decompress);
                using JsonDocument document = JsonDocument.Parse(stream);
                JsonElement root = document.RootElement;
                _telemetryEvents += root.GetProperty("Events").GetArrayLength();
                Require(root.GetProperty("DroppedEvents").GetInt32() == 0, $"Telemetry dropped events in {file}.");
            }
            Require(_telemetryFiles >= _matchIds.Count - 1 && _telemetryEvents > 0,
                $"Telemetry exports were incomplete (files={_telemetryFiles}, events={_telemetryEvents}, matches={_matchIds.Count}).");
            ReplaySummary replay = ValidateReplays(replayDirectory);
            Require(replay.Files >= _matchIds.Count - 1,
                $"Replay exports were incomplete (files={replay.Files}, matches={_matchIds.Count}).");

            var result = new
            {
                DurationSeconds = _seconds,
                Players = PlayerCount,
                Observers = ObserverCount,
                ObserverDelaySeconds,
                MatchCount = _matchIds.Count,
                Rooms = _rooms.OrderBy(value => value).ToArray(),
                MaxObserverAgeSeconds = _maxObserverAgeSeconds,
                MaxObserverAgeTicks = _maxObserverAgeTicks,
                TelemetryFiles = _telemetryFiles,
                TelemetryEvents = _telemetryEvents,
                ReplayFiles = replay.Files,
                ReplayRecords = replay.Records,
                ReplayIndexEntries = replay.IndexEntries,
                FirstServerWorkingSetBytes = _firstServerWorkingSet,
                LastServerWorkingSetBytes = _lastServerWorkingSet,
                PeakServerWorkingSetBytes = _peakServerWorkingSet,
                FirstServerPrivateMemoryBytes = _firstServerPrivateMemory,
                LastServerPrivateMemoryBytes = _lastServerPrivateMemory,
                PeakServerPrivateMemoryBytes = _peakServerPrivateMemory,
                ServerMemorySamples = _serverMemorySamples,
                PlayerStreams = _players.Select(Report).ToArray(),
                ObserverStreams = _observers.Select(Report).ToArray(),
                TelemetryDirectory = telemetryDirectory,
                ReplayDirectory = replayDirectory
            };
            File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static ReplaySummary ValidateReplays(string directory)
        {
            int files = 0, records = 0, indexEntries = 0;
            Type readerType = Type.GetType("MphRead.Mods.Network.DemoReader, FruityPrime.Replay")
                ?? throw new InvalidOperationException("Replay reader assembly is unavailable.");
            MethodInfo open = readerType.GetMethod("Open", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("Replay reader Open method is unavailable.");
            PropertyInfo format = readerType.GetProperty("FormatVersion", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Replay format property is unavailable.");
            PropertyInfo canSeek = readerType.GetProperty("CanSeek", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Replay seek property is unavailable.");
            PropertyInfo recoveredTail = readerType.GetProperty("RecoveredTail", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Replay recovery property is unavailable.");
            PropertyInfo index = readerType.GetProperty("Index", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Replay index property is unavailable.");
            MethodInfo readNext = readerType.GetMethod("ReadNext", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Replay reader ReadNext method is unavailable.");
            foreach (string path in Directory.EnumerateFiles(directory, "*.fpdemo"))
            {
                object? reader = open.Invoke(null, new object[] { path });
                Require(reader != null, $"Replay file could not be opened: {path}");
                try
                {
                    Require((byte)format.GetValue(reader)! == 3, $"Replay file is not indexed format 3: {path}");
                    Require((bool)canSeek.GetValue(reader)!, $"Replay file is not seekable: {path}");
                    Require(!(bool)recoveredTail.GetValue(reader)!, $"Replay file has a recovered tail: {path}");
                    int fileIndexEntries = ((System.Collections.ICollection)index.GetValue(reader)!).Count;
                    Require(fileIndexEntries > 0, $"Replay file has no indexed checkpoints: {path}");
                    indexEntries += fileIndexEntries;
                    int fileRecords = 0;
                    while (readNext.Invoke(reader, null) != null) fileRecords++;
                    Require(fileRecords > 0, $"Replay file has no readable records: {path}");
                    records += fileRecords;
                    files++;
                }
                finally { ((IDisposable)reader!).Dispose(); }
            }
            return new ReplaySummary(files, records, indexEntries);
        }

        private readonly record struct ReplaySummary(int Files, int Records, int IndexEntries);

        private static object Report(Probe probe) => new
        {
            probe.Name,
            probe.Snapshots,
            probe.WorldPackets,
            probe.WorldFrames,
            probe.Events,
            Matches = probe.MatchIds.OrderBy(value => value).ToArray(),
            probe.Client.Rejected,
            QueueDrops = probe.Transport.Metrics.QueueDrops,
            SendErrors = probe.Transport.Metrics.SendErrors,
            probe.MaxQueuedPackets,
            probe.MaxHeldIncomingPackets,
            probe.MaxHeldOutgoingPackets
        };

        private void SampleServerMemory()
        {
            foreach (Probe probe in _players.Concat(_observers)) probe.ObserveQueueDepth();
            double now = _clock.Elapsed.TotalSeconds;
            if (now < _nextMemorySampleAt) return;
            _nextMemorySampleAt = now + 1;
            if (_server.TryGetMemory(out long workingSet, out long privateMemory))
            {
                _serverMemorySamples++;
                _firstServerWorkingSet ??= workingSet;
                _lastServerWorkingSet = workingSet;
                _peakServerWorkingSet = Math.Max(_peakServerWorkingSet, workingSet);
                if (privateMemory > 0)
                {
                    _firstServerPrivateMemory ??= privateMemory;
                    _lastServerPrivateMemory = privateMemory;
                    _peakServerPrivateMemory = Math.Max(_peakServerPrivateMemory ?? 0, privateMemory);
                }
            }
        }

        public void Dispose()
        {
            foreach (Probe probe in _players) probe.Dispose();
            foreach (Probe probe in _observers) probe.Dispose();
        }
    }

    private sealed class Probe : IDisposable
    {
        private uint _readyMatch;
        private long _lastSnapshots;
        private readonly bool _observer;
        public readonly string Name;
        public readonly NetTransport Transport;
        public readonly NetClient Client;
        public readonly ClientWorldState World = new();
        public readonly HashSet<uint> MatchIds = new();
        public long Snapshots;
        public long WorldPackets;
        public long WorldFrames;
        public long Events;
        public uint NextInput;
        public double LastSnapshotAt;
        public double LastWorldAt;
        public int MaxQueuedPackets;
        public int MaxHeldIncomingPackets;
        public int MaxHeldOutgoingPackets;
        private long _lastWorldFrames;

        public Probe(int serverPort, string name, Hunter hunter, bool observer)
        {
            Name = name;
            _observer = observer;
            Transport = new NetTransport(0);
            Client = new NetClient(Transport, new IPEndPoint(IPAddress.Loopback, serverPort), name, hunter, observer: observer);
            Client.WorldPacketValidator = ValidateWorld;
            Client.WorldPacketReceived = ReceiveWorld;
        }

        private bool ValidateWorld(ReadOnlySpan<byte> body, uint match)
        {
            if (match == 0) return false;
            if (World.MatchId != match) World.Reset(match);
            return WorldPacket.TryValidate(body, match);
        }

        private void ReceiveWorld(ReadOnlySpan<byte> body)
        {
            uint match = Client.Connection?.MatchId ?? Client.Accepted.MatchId;
            if (World.MatchId != match) World.Reset(match);
            WorldPackets++;
            if (World.Receive(body))
            {
                WorldFrames++;
            }
        }

        public void EnsureReady()
        {
            if (Client.Connection == null || Client.Connection.State != NetConnectionState.Loading) return;
            uint match = Client.Accepted.MatchId;
            if (match == 0 || match == _readyMatch) return;
            if (!_observer)
            {
                if (!Client.Ready(match)) throw new InvalidOperationException($"{Name}: player Ready was refused.");
            }
            else
            {
                Span<byte> payload = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(payload, match);
                if (!Client.Connection.Reliable.TryEnqueue(ReliableEventType.ClientReady, payload, out _)
                    || !Client.Connection.Ready(match))
                    throw new InvalidOperationException($"{Name}: observer Ready was refused.");
            }
            _readyMatch = match;
        }

        public void Track(double now, HashSet<uint> allMatches, HashSet<string> rooms)
        {
            if (Client.Connection != null && Client.Accepted.MatchId != 0)
            {
                uint match = Client.Accepted.MatchId;
                if (MatchIds.Count > 0 && !MatchIds.Contains(match))
                {
                    uint previous = MatchIds.OrderByDescending(value => value).First();
                    if (!Sequence32.IsNewer(match, previous))
                        throw new InvalidOperationException($"{Name}: match identity moved backwards {previous}->{match}.");
                }
                MatchIds.Add(match); allMatches.Add(match); rooms.Add(Client.Accepted.Room);
            }
            if (Client.SnapshotsReceived > _lastSnapshots)
            {
                Snapshots += Client.SnapshotsReceived - _lastSnapshots;
                _lastSnapshots = Client.SnapshotsReceived;
                LastSnapshotAt = now;
            }
            if (WorldFrames > _lastWorldFrames)
            {
                _lastWorldFrames = WorldFrames;
                LastWorldAt = now;
            }
        }

        public void ObserveQueueDepth()
        {
            MaxQueuedPackets = Math.Max(MaxQueuedPackets, Transport.QueuedPackets);
            MaxHeldIncomingPackets = Math.Max(MaxHeldIncomingPackets, Transport.HeldIncomingPackets);
            MaxHeldOutgoingPackets = Math.Max(MaxHeldOutgoingPackets, Transport.HeldOutgoingPackets);
        }

        public string Status => $"{Name}: state={Client.State} failure={Client.Failure ?? "none"} snapshots={Snapshots} worlds={WorldFrames}";

        public void Dispose()
        {
            Client.Dispose();
            Transport.Dispose();
        }
    }

    private sealed class ChildServer : IDisposable
    {
        private readonly Process _process;
        private readonly string _temporary;
        private readonly ConcurrentQueue<string> _lines = new();
        private readonly ManualResetEventSlim _ready = new();
        public int Port { get; private set; }
        public bool Exited => _process.HasExited;
        public string Diagnostics => String.Join(Environment.NewLine, _lines);

        public bool TryGetMemory(out long workingSet, out long privateMemory)
        {
            workingSet = privateMemory = 0;
            try
            {
                if (_process.HasExited) return false;
                _process.Refresh();
                workingSet = _process.WorkingSet64;
                privateMemory = _process.PrivateMemorySize64;
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }

        public ChildServer(string data, string telemetryDirectory, string replayDirectory)
        {
            _temporary = Path.Combine(Path.GetTempPath(), "fruity-observer-soak-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporary);
            string rotation = Path.Combine(_temporary, "rotation.txt");
            File.WriteAllLines(rotation, new[]
            {
                FirstRoom + " | Battle | 0.1 | 0",
                SecondRoom + " | Battle | 0.1 | 0"
            });
            string executable = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet" ? Environment.ProcessPath! : "dotnet");
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            foreach (string argument in new[]
            {
                Path.Combine(AppContext.BaseDirectory, "FruityPrimeServer.dll"), "-server", "-port", "0",
                "-data", Path.GetFullPath(data), "-dataversion", "AMHE1", "-rotation", rotation,
                "-players", PlayerCount.ToString(), "-spectators", ObserverCount.ToString(),
                "-spectatordelay", ObserverDelaySeconds.ToString(), "-parent-stdin", "-nomaster", "-noupdate"
            }) start.ArgumentList.Add(argument);
            start.Environment["PRIME_TELEMETRY_DIRECTORY"] = telemetryDirectory;
            start.Environment["PRIME_SERVER_REPLAY_DIRECTORY"] = replayDirectory;
            _process = new Process { StartInfo = start, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => Observe(e.Data);
            _process.ErrorDataReceived += (_, e) => Observe(e.Data);
            _process.Exited += (_, _) => _ready.Set();
            try
            {
                if (!_process.Start()) throw new InvalidOperationException("Could not start server child.");
                _process.BeginOutputReadLine(); _process.BeginErrorReadLine();
                if (!_ready.Wait(TimeSpan.FromSeconds(20)) || Port == 0)
                    throw new InvalidOperationException("Server did not bind. " + Diagnostics);
            }
            catch { Dispose(); throw; }
        }

        private void Observe(string? line)
        {
            if (line == null) return;
            _lines.Enqueue(line);
            while (_lines.Count > 100) _lines.TryDequeue(out _);
            const string marker = "listening on UDP ";
            int index = line.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
            {
                ReadOnlySpan<char> value = line.AsSpan(index + marker.Length);
                int end = value.IndexOf(';');
                if (end > 0 && Int32.TryParse(value[..end], out int port))
                {
                    Port = port; _ready.Set();
                }
            }
            if (line.Contains("[server] match=", StringComparison.Ordinal)) Console.WriteLine(line);
        }

        public void StopAndVerify()
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(10000))
                {
                    _process.Kill(entireProcessTree: true); _process.WaitForExit();
                    throw new InvalidOperationException("Server did not stop when its parent closed stdin.");
                }
            }
            if (_process.ExitCode != 0)
                throw new InvalidOperationException("Server shutdown failed: " + Diagnostics);
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.StandardInput.Close();
                    if (!_process.WaitForExit(10000)) { _process.Kill(entireProcessTree: true); _process.WaitForExit(); }
                }
            }
            catch (InvalidOperationException) { }
            _process.Dispose(); _ready.Dispose();
            if (Directory.Exists(_temporary)) Directory.Delete(_temporary, recursive: true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
