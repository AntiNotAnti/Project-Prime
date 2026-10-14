using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Network;

// Isolated friend assembly. No authority scene writes: configuration travels
// through the ordinary authenticated lobby command and production transport.
internal static class Program
{
    private static readonly Hunter[] ExpectedHunters = { Hunter.Samus, Hunter.Kanden, Hunter.Trace, Hunter.Sylux,
        Hunter.Noxus, Hunter.Spire, Hunter.Weavel, Hunter.Samus };
    private static void RequireOrdinaryRoster(bool[] occupied, bool[] bots, Hunter[] hunters, string[] names)
    {
        if (occupied.Length != 8 || bots.Length != 8 || hunters.Length != 8 || names.Length != 8
            || occupied.Any(x => !x) || bots.Any(x => x)
            || !hunters.SequenceEqual(ExpectedHunters)
            || !names.SequenceEqual(Enumerable.Range(0, 8).Select(x => "peer-" + x)))
            throw new InvalidOperationException("Actual ordinary slot mask, bot flags, hunter/name roster differs from the fixed route.");
    }
    private static void RequireMatch(SessionStatePacket state, string room, bool beforeStart)
    {
        if (state.Policy != ServerSessionPolicy.Lobby || state.MaxPlayers != 8
            || state.Match.RoomKey != room || state.Match.Mode != GameMode.Battle || state.Match.DisablePowerups
            || (beforeStart ? state.Phase != SessionPhase.Lobby
                : state.Phase is not SessionPhase.Starting and not SessionPhase.InMatch || state.ExpectedParticipants != 255))
            throw new InvalidOperationException("Actual server-accepted match flags, phase or participant mask differs from the route.");
    }
    private static object SelfCheck()
    {
        int checks = 0;
        void Reject(Action action)
        {
            try { action(); } catch (InvalidOperationException) { checks++; return; }
            throw new Exception("A falsifying roster/match was accepted.");
        }
        var occupied = Enumerable.Repeat(true, 8).ToArray();
        var bots = new bool[8]; var hunters = ExpectedHunters.ToArray();
        var names = Enumerable.Range(0, 8).Select(x => "peer-" + x).ToArray();
        RequireOrdinaryRoster(occupied, bots, hunters, names); checks++;
        occupied[7] = false; Reject(() => RequireOrdinaryRoster(occupied, bots, hunters, names)); occupied[7] = true;
        bots[7] = true; Reject(() => RequireOrdinaryRoster(occupied, bots, hunters, names)); bots[7] = false;
        hunters[1] = Hunter.Samus; Reject(() => RequireOrdinaryRoster(occupied, bots, hunters, names)); hunters[1] = Hunter.Kanden;
        names[1] = "wrong-peer"; Reject(() => RequireOrdinaryRoster(occupied, bots, hunters, names)); names[1] = "peer-1";
        Reject(() => RequireOrdinaryRoster(new bool[9], bots, hunters, names));
        var state = new SessionStatePacket { Policy = ServerSessionPolicy.Lobby, Phase = SessionPhase.Lobby,
            MaxPlayers = 8, Match = new MatchDefinition { RoomKey = "MP5 FUEL SLUICE", Mode = GameMode.Battle } };
        RequireMatch(state, state.Match.RoomKey, true); checks++;
        var bad = state; bad.Match = bad.Match with { DisablePowerups = true }; Reject(() => RequireMatch(bad, state.Match.RoomKey, true));
        bad = state; bad.Policy = ServerSessionPolicy.Continuous; Reject(() => RequireMatch(bad, state.Match.RoomKey, true));
        bad = state; bad.MaxPlayers = 7; Reject(() => RequireMatch(bad, state.Match.RoomKey, true));
        Reject(() => RequireMatch(state, "wrong-room", true));
        state.Phase = SessionPhase.Starting; state.ExpectedParticipants = 255;
        RequireMatch(state, state.Match.RoomKey, false); checks++;
        bad = state; bad.ExpectedParticipants = 127; Reject(() => RequireMatch(bad, state.Match.RoomKey, false));
        Reject(() => RequireMatch(state, state.Match.RoomKey, true));
        var type = typeof(NetSession);
        foreach (string method in new[] { "SendLobbyCommand", "Pump" })
        { if (type.GetMethod(method) == null) throw new MissingMethodException(method); checks++; }
        if (typeof(NetLaunch).GetMethod("Connect") == null) throw new MissingMethodException("Connect"); checks++;
        return new { status = "PASS", checks, scope = "Device-free API presence and roster/match falsifiers; no UDP, assets, native bodies, grant or pacing coverage" };
    }
    private static int Main(string[] args)
    {
        string? Value(string key)
        {
            int index = Array.IndexOf(args, "-" + key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
        bool Flag(string key) => args.Contains("-" + key);
        bool recording = false;
        bool liveAttempt = false;
        try
        {
            var client = typeof(NetSession).Assembly;
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(client.Location))).ToLowerInvariant();
            if (hash != Environment.GetEnvironmentVariable("PRIME_PACING_EXPECTED_CLIENT_SHA256"))
                throw new InvalidOperationException("Loaded client differs from the declared frozen SHA256.");
            string receipt = Environment.GetEnvironmentVariable("PRIME_PACING_LOBBY_RECEIPT")
                ?? throw new ArgumentException("Missing lobby receipt path.");
            if (Flag("self-check"))
            {
                var result = new { schema = 1, loadedClient = new { path = client.Location, sha256 = hash,
                    mvid = client.ManifestModule.ModuleVersionId,
                    protocol = client.GetType("MphRead.Mods.Network.NetConfig")!.GetField("ProtocolVersion")!.GetRawConstantValue() },
                    selfCheck = SelfCheck() };
                using var file = new FileStream(receipt, FileMode.CreateNew, FileAccess.Write);
                JsonSerializer.Serialize(file, result, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine("[pacing-lobby] device-free self-check PASS");
                return 0;
            }
            if (!Flag("nographics") || !Flag("ai-net-client") || Flag("hitrig") || Flag("spectate") || Flag("rejoin"))
                throw new ArgumentException("Pacing lobby clients require native AI and -nographics; scripted/observer roles are unsupported.");
            string room = Value("pacing-room") ?? throw new ArgumentException("Missing -pacing-room.");
            int players = int.Parse(Value("pacing-players") ?? "8", CultureInfo.InvariantCulture);
            if (players != 8) throw new ArgumentException("This matched route requires eight actual clients.");
            bool owner = Flag("pacing-owner");
            Guid token = owner ? Guid.Parse(Value("pacing-token")!) : Guid.Empty;
            string name = Value("name") ?? throw new ArgumentException("Missing client name.");
            Hunter hunter = Enum.Parse<Hunter>(Value("hunter")!, true);
            double seconds = double.Parse(Value("seconds")!, CultureInfo.InvariantCulture);
            if (Value("mapdir") is string mapDirectory)
                MphRead.Mods.MapGen.CustomRooms.MapDirectory = Path.GetFullPath(mapDirectory);
            ConsoleSetup.Run();
            Paths.UpdatePaths(); Paths.ChooseMphPath(); Paths.ChooseFhPath();
            if (string.IsNullOrEmpty(Paths.FileSystem) || !Directory.Exists(Paths.FileSystem))
                throw new InvalidOperationException("Normal startup did not resolve the declared native asset directory.");
            if (!NetLag.Configure(Value("netlag")) || !NetLag.ConfigureLoss(Value("netloss"))
                || !NetLag.ConfigureReorder(Value("netreorder"))
                || !NetLag.ConfigureDuplicate(Value("netduplicate")) || !NetLag.ConfigureSeed(Value("netseed")))
                throw new ArgumentException("Missing or invalid explicit fault configuration.");
            if (!NetAiClient.Configure(true, Value("ai-net-client"), Value("ai-seed"), out string aiError))
                throw new ArgumentException(aiError);
            liveAttempt = true;
            if (!NetLaunch.Connect(Value("netcheck")!, int.Parse(Value("port")!), name, hunter,
                timeoutMs: 30000, ownerToken: token)) throw new InvalidOperationException(NetLaunch.LastJoinError);

            var clock = Stopwatch.StartNew();
            void Wait(Func<bool> condition, string stage)
            {
                while (!condition())
                {
                    NetSession.Pump();
                    if (!NetSession.Active || NetSession.Refused || NetSession.SessionTimedOut || clock.Elapsed.TotalSeconds > 90)
                        throw new InvalidOperationException("Lobby setup failed at " + stage + ": " + NetSession.LobbyMessage);
                    Thread.Sleep(10);
                }
            }
            var before = NetSession.ServerSession ?? throw new InvalidOperationException("No actual server session.");
            if (before.Policy != ServerSessionPolicy.Lobby || before.Match.RoomKey != room)
                throw new InvalidOperationException("Actual lobby policy/room differs from the route.");
            if (owner)
            {
                Wait(() => NetSession.LocalIsLobbyOwner && NetSession.SlotOccupied.Count(x => x) == players, "owner and actual roster");
                // Identity announcements advance the server revision. Wait for
                // one stable interval before requesting the ordinary rule edit.
                ushort revision = NetSession.SessionRevision;
                double stableSince = clock.Elapsed.TotalSeconds;
                Wait(() =>
                {
                    if (revision != NetSession.SessionRevision)
                    { revision = NetSession.SessionRevision; stableSince = clock.Elapsed.TotalSeconds; }
                    return clock.Elapsed.TotalSeconds - stableSince >= 1;
                }, "stable lobby revision");
                var configuration = NetSession.ServerSession!.Value;
                configuration.Match = configuration.Match with { DisablePowerups = false };
                configuration.RuleFlags &= ~LobbyRuleFlags.RequireReady;
                if (!NetSession.SendLobbyCommand(LobbyCommandType.UpdateMatch, configuration: configuration))
                    throw new InvalidOperationException("Production lobby command was not sent.");
                Wait(() => !NetSession.LobbyCommandPending && NetSession.ActiveMatchDefinition is { DisablePowerups: false }, "server-accepted powerup rule");
                RequireMatch(NetSession.ServerSession!.Value, room, beforeStart: true);
                RequireOrdinaryRoster(NetSession.SlotOccupied, NetSession.SlotIsBot, NetSession.SlotHunter, GameState.Nicknames);
                if (!NetSession.SendLobbyCommand(LobbyCommandType.StartMatch))
                    throw new InvalidOperationException("Production start command was not sent.");
            }
            Wait(() => NetSession.ShouldLoadMatch, "actual server start barrier");
            var accepted = NetSession.ServerSession!.Value;
            RequireMatch(accepted, room, beforeStart: false);
            RequireOrdinaryRoster(NetSession.SlotOccupied, NetSession.SlotIsBot, NetSession.SlotHunter, GameState.Nicknames);
            var identity = new
            {
                schema = 1, scope = "Actual authenticated lobby state before native simulation; no fabricated grant denominator",
                loadedClient = new { path = client.Location, sha256 = hash, mvid = client.ManifestModule.ModuleVersionId,
                    protocol = client.GetType("MphRead.Mods.Network.NetConfig")!.GetField("ProtocolVersion")!.GetRawConstantValue() },
                owner, slot = NetSession.LocalSlot, name, requestedHunter = hunter,
                acceptedHunter = NetSession.SlotHunter[NetSession.LocalSlot],
                occupiedSlots = NetSession.SlotOccupied.ToArray(), acceptedHunters = NetSession.SlotHunter.ToArray(),
                acceptedNames = GameState.Nicknames.ToArray(), actualBots = NetSession.SlotIsBot.ToArray(),
                before = new { before.Policy, before.Phase, before.Revision, before.Match.DisablePowerups },
                accepted = new { accepted.Policy, accepted.Phase, accepted.Revision, accepted.MatchId, accepted.AuthorityEpoch,
                    accepted.StartGeneration, accepted.MaxPlayers, accepted.ExpectedParticipants, accepted.Match.RoomKey,
                    accepted.Match.Mode, accepted.Match.Rules, accepted.Match.DisablePowerups },
                aiLevel = NetAiClient.Level, aiSeed = NetAiClient.Seed,
                actualAssets = new { Paths.MphKey, Paths.FileSystem, Paths.Export },
                actualFaults = new { NetLag.RoundTripMs, NetLag.JitterMs, NetLag.LossPercent,
                    NetLag.ReorderRate, NetLag.DuplicateRate, NetLag.Seed },
                setupSeconds = clock.Elapsed.TotalSeconds
            };
            using (var file = new FileStream(receipt, FileMode.CreateNew, FileAccess.Write))
                JsonSerializer.Serialize(file, identity, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine("[pacing-lobby] actual eight-client frozen match accepted with native powerups enabled");
            NetLaunch.DisableCheatsForMatch();
            NetTestScript.Reset(); NetTestScript.Enabled = false;
            Headless.Enter();
            if (Flag("recorddemo")) recording = DemoRecorder.Start();
            if (Flag("recorddemo") && !recording) throw new InvalidOperationException("Recording did not start.");
            int resultCode = NetCheckSimulation.Run(room, accepted.Match.Mode, hunter, seconds);
            // Native bodies only exist after the real start/load path. A dead
            // body remains a body; health/alive are observations, not a demand
            // to alter gameplay or fabricate a spawn.
            var bodies = PlayerEntity.Players.Select(player => new { slot = player.SlotIndex,
                hunter = player.Hunter, player.LoadFlags, player.Health,
                generation = NetPlayerLifecycle.Generation(player.SlotIndex), life = NetPlayerLifecycle.Get(player.SlotIndex) }).ToArray();
            if (bodies.Length != 8 || !bodies.Select(x => x.slot).OrderBy(x => x).SequenceEqual(Enumerable.Range(0, 8))
                || bodies.Any(x => x.generation == 0 || x.hunter != ExpectedHunters[x.slot]))
                throw new InvalidOperationException("Actual post-start native body/slot generation proof is incomplete.");
            using (var file = new FileStream(receipt + ".native-bodies.json", FileMode.CreateNew, FileAccess.Write))
                JsonSerializer.Serialize(file, new { schema = 1, resultCode, bodies,
                    scope = "Actual foreground native client bodies after simulation/drain; not pre-start bodies or all-alive proof" },
                    new JsonSerializerOptions { WriteIndented = true });
            return resultCode;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 2; }
        finally
        {
            if (liveAttempt)
            {
                try { NetCampaignEvidence.Write("simulation-client"); }
                catch (Exception ex) { Console.Error.WriteLine("[pacing-lobby] evidence capture failed after route failure: " + ex); }
                finally
                {
                    if (recording) DemoRecorder.Stop();
                    NetSession.Stop(); NetLog.Close();
                }
            }
        }
    }
}
