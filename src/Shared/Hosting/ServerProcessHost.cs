using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Network
{
    internal sealed record HostedProcessOptions(MatchRules Rules, LobbyPolicy LobbyPolicy,
        int BotMinimumParticipants, int BotSkill, int MaxObservers, int ObserverDelaySeconds,
        Guid OwnerToken, IPAddress OwnerAddress)
    {
        public void Validate(bool practice)
        {
            if (Rules == null || LobbyPolicy == null) throw new ArgumentException("Hosted rules and lobby policy are required.");
            MatchLifecycle.ValidateRules(Rules);
            if (BotMinimumParticipants < 0 || BotMinimumParticipants > Rules.MaxPlayers || BotSkill is < 0 or > 2)
                throw new ArgumentOutOfRangeException(nameof(BotMinimumParticipants));
            if (MaxObservers is < 0 or > LobbyRuntime.MaximumObservers || ObserverDelaySeconds is < 0 or > 30)
                throw new ArgumentOutOfRangeException(nameof(MaxObservers));
            if (OwnerToken == Guid.Empty) throw new ArgumentException("A nonempty lobby owner token is required.", nameof(OwnerToken));
            if (OwnerAddress == null || OwnerAddress.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException("An IPv4 lobby owner source address is required.", nameof(OwnerAddress));
            if (practice && Rules.RankingEligibility != RankingEligibility.Unranked)
                throw new ArgumentException("Practice hosting must remain unranked.", nameof(Rules));
        }
    }

    /// <summary>
    /// Owns one authoritative server process. Scene and GameState are process-wide,
    /// so a local player and a hosted match must never share a simulation thread.
    /// The stdin pipe is the child lifetime contract; it also closes if this process dies.
    /// </summary>
    internal sealed class ServerProcessHost : IDisposable
    {
        public const int StartupTimeoutMs = 30000;
        private const int MaxLogLines = 64;
        private readonly Process _process = new();
        private readonly TaskCompletionSource<int> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Queue<string> _log = new();
        private readonly string _rotationFile;
        private bool _started;
        private int _disposed;
        private int _peerCount;
        private int _everOccupied;
        private int _exitCode = Int32.MinValue;
        private Guid _ownerCapability;

        public int Port { get; private set; }
        public int PeerCount => Volatile.Read(ref _peerCount);
        public bool EverOccupied => Volatile.Read(ref _everOccupied) != 0;
        public bool Running
        {
            get
            {
                if (!_started || Volatile.Read(ref _disposed) != 0) { return false; }
                try { return !_process.HasExited; }
                catch (InvalidOperationException) when (Volatile.Read(ref _disposed) != 0) { return false; }
            }
        }
        public int? ExitCode
        {
            get
            {
                int code = Volatile.Read(ref _exitCode);
                return code == Int32.MinValue ? null : code;
            }
        }
        public bool WasKilled { get; private set; }
        public string RecentLog { get { lock (_log) { return String.Join(Environment.NewLine, _log); } } }

        internal Guid TakeOwnerCapability()
        {
            Guid capability = _ownerCapability;
            _ownerCapability = Guid.Empty;
            return capability;
        }

        private ServerProcessHost(ProcessStartInfo start, string rotationFile)
        {
            _rotationFile = rotationFile;
            _process.StartInfo = start;
            _process.EnableRaisingEvents = true;
            _process.OutputDataReceived += (_, args) => Observe(args.Data);
            _process.ErrorDataReceived += (_, args) => Observe(args.Data);
            _process.Exited += (_, _) =>
            {
                _ready.TrySetResult(0);
            };
        }

        public static ServerProcessHost Start(string data, string version, MapRotation rotation,
            int port, int maxPlayers, bool friendlyFire,
            (string Host, int Port, string Name)? listing = null, CancellationToken cancel = default, bool practice = false,
            HostedProcessOptions? options = null)
            => StartAsync(data, version, rotation, port, maxPlayers, friendlyFire, listing, cancel, practice, options).GetAwaiter().GetResult();

        public static async Task<ServerProcessHost> StartAsync(string data, string version, MapRotation rotation,
            int port, int maxPlayers, bool friendlyFire,
            (string Host, int Port, string Name)? listing = null, CancellationToken cancel = default, bool practice = false,
            HostedProcessOptions? options = null)
        {
            if (String.IsNullOrWhiteSpace(data) || !Directory.Exists(data))
            {
                throw new ArgumentException("Hosting requires an extracted game directory or server content package.", nameof(data));
            }
            if (version is not ("AMHE0" or "AMHE1" or "AMHP0" or "AMHP1" or "AMHJ0" or "AMHJ1" or "AMHK0"))
            {
                throw new ArgumentException("Unsupported server data version: " + version, nameof(version));
            }
            if (port < 0 || port > UInt16.MaxValue || maxPlayers < 1 || maxPlayers > 8)
            {
                throw new ArgumentOutOfRangeException(nameof(port), "Use UDP port 0–65535 and 1–8 players.");
            }
            if (rotation.Entries.Count is < 1 or > 64)
            {
                throw new ArgumentException("Hosting requires 1–64 rotation entries.", nameof(rotation));
            }
            foreach (RotationEntry entry in rotation.Entries)
            {
                if (String.IsNullOrWhiteSpace(entry.RoomKey) || entry.RoomKey.IndexOfAny(['|', '#', '\r', '\n']) >= 0
                    || entry.Mode < GameMode.Battle || entry.Mode > GameMode.PrimeHunter
                    || !Single.IsFinite(entry.TimeLimit) || entry.TimeLimit < 0 || entry.TimeLimit > 86400
                    || entry.PointGoal < 0)
                {
                    throw new ArgumentException("Invalid hosted map or match rules.", nameof(rotation));
                }
                _ = entry.ToMatchRules(maxPlayers, friendlyFire);
            }
            MatchRules defaultRules = rotation.Current.ToMatchRules(maxPlayers, friendlyFire);
            options ??= new HostedProcessOptions(defaultRules, LobbyPolicy.PrivateHosted,
                practice ? Math.Min(4, maxPlayers) : 0, 1, 4, 0, CreateToken(), IPAddress.Loopback);
            options.Validate(practice);
            if (options.Rules.MaxPlayers != maxPlayers || options.Rules.RoomKey != rotation.Current.RoomKey
                || options.Rules.Mode.ToLegacyMode() != rotation.Current.Mode)
                throw new ArgumentException("Hosted rules must match the process room, mode, and capacity.", nameof(options));
            cancel.ThrowIfCancellationRequested();
            string rotationFile = Path.Combine(Path.GetTempPath(), "fruity-server-" + Guid.NewGuid().ToString("N") + ".rotation");
            ServerProcessHost? server = null;
            try
            {
                using (var file = new StreamWriter(new FileStream(rotationFile, FileMode.CreateNew, FileAccess.Write)))
                {
                    foreach (RotationEntry entry in rotation.Entries)
                    {
                        file.WriteLine(FormattableString.Invariant($"{entry.RoomKey} | {entry.Mode} | {entry.TimeLimit / 60:R} | {entry.PointGoal} | {entry.ObjectiveTimeGoal:R}"));
                    }
                }
                ProcessStartInfo start = CreateStartInfo(CustomRooms.MapDirectory);
                if (practice)
                {
                    if (listing != null) throw new ArgumentException("Practice cannot be listed.");
                    start.Environment["PRIME_PRACTICE"] = "1";
                    foreach (string key in new[] { "PRIME_REPORT_DIRECTORY", "PRIME_REPORT_URL", "PRIME_REPORT_CREDENTIAL", "PRIME_TICKET_BACKEND", "PRIME_TICKET_ISSUER", "PRIME_REQUIRE_TICKETS", "PRIME_SERVER_SECRET" }) start.Environment.Remove(key);
                }
                start.Environment["PRIME_BOT_FILL"] = options.BotMinimumParticipants.ToString(CultureInfo.InvariantCulture);
                start.Environment["PRIME_BOT_SKILL"] = options.BotSkill.ToString(CultureInfo.InvariantCulture);
                const string capabilityVariable = "PRIME_LOBBY_OWNER_CAPABILITY";
                const string addressVariable = "PRIME_LOBBY_OWNER_ADDRESS";
                start.Environment[capabilityVariable] = options.OwnerToken.ToString("N");
                start.Environment[addressVariable] = options.OwnerAddress.ToString();
                void Add(string key, string? value = null)
                {
                    start.ArgumentList.Add(key);
                    if (value != null) { start.ArgumentList.Add(value); }
                }
                Add("-authoritative-server", rotation.Current.RoomKey);
                Add("-data", Path.GetFullPath(data));
                Add("-dataversion", version);
                Add("-rotation", rotationFile);
                Add("-port", port.ToString(CultureInfo.InvariantCulture));
                Add("-players", maxPlayers.ToString(CultureInfo.InvariantCulture));
                Add("-friendlyfire", friendlyFire ? "true" : "false");
                Span<byte> rulesBytes = stackalloc byte[MatchRulesWire.Size];
                MatchRulesWire.Write(rulesBytes, options.Rules);
                Add("-hostrules", Convert.ToBase64String(rulesBytes));
                Add("-lobbypolicy", options.LobbyPolicy.Kind.ToString());
                Add("-readyrequired", options.LobbyPolicy.ReadyRequired ? "true" : "false");
                Add("-minplayers", options.LobbyPolicy.MinimumPlayers.ToString(CultureInfo.InvariantCulture));
                Add("-hostforce", options.LobbyPolicy.HostMayForceStart ? "true" : "false");
                Add("-spectators", options.MaxObservers.ToString(CultureInfo.InvariantCulture));
                Add("-spectatordelay", options.ObserverDelaySeconds.ToString(CultureInfo.InvariantCulture));
                Add("-parent-stdin");
                Add("-noupdate");
                if (listing is { } directory)
                {
                    Add("-master", directory.Host + ":" + directory.Port.ToString(CultureInfo.InvariantCulture));
                    Add("-name", directory.Name);
                }
                else { Add("-nomaster"); }
                server = new ServerProcessHost(start, rotationFile) { _ownerCapability = options.OwnerToken };
                try { server._started = server._process.Start(); }
                finally
                {
                    // Do not retain admission secrets in the long-lived parent process metadata.
                    start.Environment.Remove(capabilityVariable);
                    start.Environment.Remove(addressVariable);
                }
                if (!server._started) { throw new IOException("The authoritative server process did not start."); }
                server._process.BeginOutputReadLine();
                server._process.BeginErrorReadLine();
                try
                {
                    server.Port = await server._ready.Task.WaitAsync(TimeSpan.FromMilliseconds(StartupTimeoutMs), cancel).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    throw new TimeoutException("The authoritative server did not become ready within 30 seconds.\n" + server.RecentLog);
                }
                if (server.Port == 0 || server._process.HasExited)
                {
                    // Finish reading stderr after an early exit before reporting its reason.
                    server._process.WaitForExit();
                    throw new IOException("The authoritative server stopped during startup.\n" + server.RecentLog);
                }
                return server;
            }
            catch
            {
                if (server != null) { server.Dispose(); }
                else { File.Delete(rotationFile); }
                throw;
            }
        }

        private static Guid CreateToken()
        {
            Span<byte> bytes = stackalloc byte[16];
            System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
            return new Guid(bytes);
        }

        internal static ProcessStartInfo CreateStartInfo(string mapDirectory)
        {
            string? configured = Environment.GetEnvironmentVariable("FRUITY_SERVER_PATH");
            string? serverPath = null;
            if (!String.IsNullOrWhiteSpace(configured))
            {
                serverPath = Path.GetFullPath(configured);
                if (!File.Exists(serverPath)) throw new IOException("Cannot locate the configured server executable: " + serverPath);
            }
            else
            {
                string suffix = OperatingSystem.IsWindows() ? ".exe" : "";
                foreach (string directory in new[] { Path.Combine(AppContext.BaseDirectory, "server"), AppContext.BaseDirectory })
                {
                    foreach (string name in new[] { "FruityPrimeServer" + suffix, "FruityPrimeServer.dll" })
                    {
                        string candidate = Path.Combine(directory, name);
                        if (File.Exists(candidate)) { serverPath = candidate; break; }
                    }
                    if (serverPath != null) break;
                }
                if (serverPath == null)
                    throw new IOException("Cannot locate the standalone server beside the client. Install the server package or set FRUITY_SERVER_PATH.");
            }
            bool managed = Path.GetExtension(serverPath).Equals(".dll", StringComparison.OrdinalIgnoreCase);
            string executable = managed
                ? Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet" : serverPath;
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Environment.CurrentDirectory
            };
            if (managed) start.ArgumentList.Add(serverPath);
            start.ArgumentList.Add("-mapdir");
            start.ArgumentList.Add(Path.GetFullPath(mapDirectory));
            return start;
        }

        private void Observe(string? line)
        {
            if (line == null) { return; }
            if (line.Length > 1024) { line = line[..1024]; }
            lock (_log)
            {
                if (_log.Count == MaxLogLines) { _log.Dequeue(); }
                _log.Enqueue(line);
            }
            const string ready = "[server] listening on UDP ";
            if (line.StartsWith(ready, StringComparison.Ordinal))
            {
                int end = line.IndexOf(';', ready.Length);
                if (end > ready.Length && Int32.TryParse(line.AsSpan(ready.Length, end - ready.Length),
                    NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is > 0 and <= UInt16.MaxValue)
                {
                    _ready.TrySetResult(port);
                }
            }
            // The concise peer-change line includes active players and observers.
            // Periodic tick diagnostics retain their historical player-only count.
            if (line.StartsWith("[server] peers=", StringComparison.Ordinal))
            {
                int begin = line.IndexOf(" peers=", StringComparison.Ordinal);
                if (begin < 0) { return; }
                begin += 7;
                int end = line.IndexOf(' ', begin);
                if (end < 0) { end = line.Length; }
                if (Int32.TryParse(line.AsSpan(begin, end - begin), out int peers) && peers is >= 0 and <= 8)
                {
                    Volatile.Write(ref _peerCount, peers);
                    if (peers > 0) { Volatile.Write(ref _everOccupied, 1); }
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
            _ownerCapability = Guid.Empty;
            try
            {
                if (_started)
                {
                    try { _process.StandardInput.Close(); }
                    catch (IOException) { }
                    if (!_process.WaitForExit(3000))
                    {
                        try
                        {
                            _process.Kill(entireProcessTree: true);
                            WasKilled = true;
                        }
                        catch (InvalidOperationException) when (_process.HasExited) { }
                        catch (System.ComponentModel.Win32Exception) when (_process.HasExited) { }
                        if (!_process.WaitForExit(2000))
                        {
                            throw new IOException("The owned authoritative server did not exit after termination.");
                        }
                    }
                    _process.WaitForExit();
                    Volatile.Write(ref _exitCode, _process.ExitCode);
                }
            }
            finally
            {
                _process.Dispose();
                File.Delete(_rotationFile);
            }
        }
    }
}
