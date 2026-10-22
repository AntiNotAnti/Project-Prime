using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Actual client processes; production AI drives HUMAN owner controls. No bot slot,
// role/readiness mutation, fixture grants, placement, direct damage or hit rig.
internal static class MomentumCombatOnlineTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    internal static int Run(int port, MovementModel expected, string outputPath, int seconds = 60, uint seed = 1729, string latency = "200:20", string loss = "0",
        string reorder = "0", string duplicate = "0", int faultSeed = 1, Hunter hunter = Hunter.Samus, int minimumHumanOwners = 2, MomentumTeamObjectiveOptions? teamObjective = null)
    {
        Scene? scene = null; string output = Path.GetFullPath(outputPath);
        using var team = teamObjective == null ? null : new MomentumTeamObjectiveTests(teamObjective, latency, loss, reorder, duplicate, faultSeed);
        try
        {
            _checks = 0;
            Check(seconds is >= 45 and <= 120 && seed != 0 && minimumHumanOwners is >= 2 and <= 8
                && (int)hunter >= 0 && (int)hunter < Hunters.Playable, "bounded genuine encounter duration/AI seed/Hunter/human count");
            Check(!NetSession.Active && !DemoPlayback.IsActive, "fresh live client process");
            Check(ServerSim.Available(out string reason), reason); Headless.Enter();
            Check(NetCampaignEvidence.Enabled, "opt-in evidence required before actual session begins");
            Check(NetLag.Configure(latency) && NetLag.ConfigureLoss(loss) && NetLag.ConfigureReorder(reorder)
                && NetLag.ConfigureDuplicate(duplicate) && NetLag.ConfigureSeed(faultSeed.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                "explicit actual transport fault queues configured before Connect");
            Check(NetAiClient.Configure(true, "3", seed.ToString(System.Globalization.CultureInfo.InvariantCulture), out reason), reason);
            Check(NetLaunch.Connect("127.0.0.1", port, (teamObjective == null ? "CombatOwner-" : "MomentumCPU-") + Environment.ProcessId, hunter, timeoutMs: 30000),
                "actual UDP join: " + NetLaunch.LastJoinError);
            team?.PrepareLobby();
            var timer = Stopwatch.StartNew();
            void Connected() => Check(NetSession.IsClient && !NetSession.IsAuthority && NetSession.Active
                && !NetSession.ConnectionLost && !NetSession.SessionTimedOut && !NetSession.Refused, "actual connected nonauthority client");
            while (!NetSession.ShouldLoadMatch && timer.Elapsed.TotalSeconds < 45)
            { NetSession.Pump(); Connected(); Thread.Sleep(5); }
            Check(NetSession.ShouldLoadMatch && NetSession.ActiveMatchDefinition is { }, "real frozen match delivered");
            var definition = NetSession.ActiveMatchDefinition!.Value;
            Check(definition.MovementModel == expected, "server selected expected movement");
            int slot = NetSession.LocalSlot; ushort match = NetSession.CurrentMatchId; ulong epoch = NetSession.AuthorityEpoch;
            NetLaunch.DisableCheatsForMatch();
            scene = new Scene(new Vector2i(256, 192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { }, lifetimeOwned: value => scene = value);
            NetLaunch.BuildPlayers(scene, NetSession.LocalHunter, NetSession.LocalColor, teams: GameState.IsTeamMode(definition.Mode));
            scene.AddRoom(definition.RoomKey, definition.Mode, playerCount: NetLaunch.RoomPlayerCount);
            definition.ApplyInitialConfiguration(scene.GameState);
            typeof(Scene).GetField("_developerMomentumDiagnostics", Hidden)!.SetValue(scene, true);
            scene.OnLoad();
            var mode = typeof(Scene).GetField("_inputMode", Hidden)!; mode.SetValue(scene, Enum.Parse(mode.FieldType, "All"));
            var owner = scene.Players.Main;
            Check(NetAiClient.IsBoundTo(scene) && NetAiClient.Drives(owner) && !owner.IsBot && !NetSession.SlotIsBot[slot]
                && owner.SlotIndex == slot && owner.Hunter == hunter && !NetHooks.IsPuppet(owner), "production AI drives genuine human local owner");
            NetSession.MarkMatchLoaded();
            var clock = Stopwatch.StartNew(); double next = 0;
            void Step()
            {
                next += 1d / 60; double pause = next - clock.Elapsed.TotalSeconds;
                if (pause > .002) Thread.Sleep((int)((pause - .001) * 1000));
                while (clock.Elapsed.TotalSeconds < next) Thread.SpinWait(32);
                if (clock.Elapsed.TotalSeconds - next > .25) next = clock.Elapsed.TotalSeconds;
                scene.OnSimulationFrame(); Connected();
            }
            timer.Restart();
            while ((!NetSession.WorldIsReady || NetSession.FreezeGameplay || owner.Health == 0 || !owner.ModIsInPlay) && timer.Elapsed.TotalSeconds < 45) Step();
            Check(NetSession.WorldIsReady && NetRoomChange.GameplayReady && !NetSession.FreezeGameplay && owner.Health > 0 && owner.ModIsInPlay,
                "native spawn and real baseline witnesses release living owner");
            scene.CameraSequences.Current?.End();
            int ActiveHumanCount() => scene.Players.Items.Count(player => player.SlotIndex >= 0
                && player.SlotIndex < NetSession.SlotOccupied.Length && NetSession.SlotOccupied[player.SlotIndex]
                && !NetSession.SlotIsBot[player.SlotIndex] && !player.IsBot && player.LoadFlags.TestFlag(LoadFlags.Active));
            var arrivals = Stopwatch.StartNew();
            while ((ActiveHumanCount() < minimumHumanOwners || !NetSession.WorldIsReady || NetSession.FreezeGameplay)
                && arrivals.Elapsed.TotalSeconds < 60) Step();
            Check(ActiveHumanCount() >= minimumHumanOwners && NetSession.WorldIsReady && !NetSession.FreezeGameplay,
                "actual required human cohort arrived and real baseline reopened before measured encounter interval");
            team?.Validate(scene, definition);
            double cohortArrivalWaitSeconds = arrivals.Elapsed.TotalSeconds; int humansAtStart = ActiveHumanCount();
            long initialSteps = scene.DeveloperMomentumSteps; int ticks = 0, shooting = 0, remoteAlive = 0, healthChanges = 0;
            var previousHealth = new Dictionary<int, int>(); var humans = new HashSet<int>(); timer.Restart();
            while (timer.Elapsed.TotalSeconds < seconds)
            {
                Step(); ticks++;
                team?.Observe(scene);
                Check(NetSession.AuthorityEpoch == epoch && NetSession.CurrentMatchId == match && scene.GameState.MovementModel == expected, "same actual match and immutable movement rule");
                Check(Finite(owner.Position) && Finite(owner.Speed), "finite native owner body");
                if (minimumHumanOwners == 8)
                    Check(!NetSession.SlotOccupied.Where((occupied, id) => occupied && NetSession.SlotIsBot[id]).Any(), "eight-human fixture contains no authority bot slots");
                if (owner.Controls.Shoot.IsDown || owner.Controls.Shoot.IsPressed) shooting++;
                foreach (var player in scene.Players.Items)
                {
                    int id = player.SlotIndex;
                    if (id == slot || id >= NetSession.SlotOccupied.Length || !NetSession.SlotOccupied[id] || NetSession.SlotIsBot[id]) continue;
                    if (!player.LoadFlags.TestFlag(LoadFlags.Active)) continue;
                    humans.Add(id); Check(NetHooks.IsPuppet(player) && !scene.MomentumOwnsPlayer(player), "remote human never owns local solver");
                    if (player.Health > 0) remoteAlive++;
                    if (previousHealth.TryGetValue(id, out int health) && player.Health < health) healthChanges++;
                    previousHealth[id] = player.Health;
                }
            }
            var report = new { scope = "real UDP; production NetAiClient drives native human-owner controls/aim; CPU Roam/All native intro skip; no physical player, bot identity, grants, placements or direct damage",
                teamObjective = team?.Report,
                model = expected.ToString(), hunter = owner.Hunter.ToString(), minimumHumanOwners, cohortArrivalWaitSeconds, humansAtStart, localSlot = slot, epoch, match, seconds, seed, ticks, shooting, remoteAlive, healthChanges,
                humanSlots = humans.OrderBy(value => value).ToArray(), solverSteps = scene.DeveloperMomentumSteps - initialSteps,
                clientClaimsDeclared = NetHitClaims.Declared, clientClaimsApplied = NetHitClaims.Applied, ai = NetAiClient.Capture(),
                latency = new { NetLag.RoundTripMs, NetLag.JitterMs, NetLag.LossPercent, NetLag.ReorderRate, NetLag.DuplicateRate, NetLag.Seed },
                proofScope = "Worker activity alone is not authority payment/rewind acceptance. Verify requires completed authority evidence plus authoritative rewind metrics." };
            Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            team?.Complete();
            Check(NetAiClient.HasLocalActivity && shooting > 0 && remoteAlive > 0 && humans.Count >= minimumHumanOwners - 1, "native encounter produces real owner fire and observes every required active human opponent");
            Check(expected == MovementModel.Native ? scene.DeveloperMomentumSteps == initialSteps : scene.DeveloperMomentumSteps > initialSteps, "selected movement solver exercised by actual owner input");
            Console.WriteLine($"MOMENTUM COMBAT OWNER PASS {_checks} activity assertions; claims={NetHitClaims.Declared}; authority proof still required"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("MOMENTUM COMBAT OWNER FAIL " + error); return 1; }
        finally
        {
            NetCampaignEvidence.Write("simulation-client");
            try { if (scene != null) MomentumSceneTests.Release(scene); }
            finally { NetSession.Stop(); NetAiClient.Reset(); }
        }
    }

    // ShotRewind must be captured INSIDE the real authority process before
    // teardown by NetCampaignEvidence, not inferred from client ACK metadata.
    internal static int Verify(string serverEvidence, string firstWorker, string secondWorker, string outputPath)
        => VerifyFiles(serverEvidence, new[] { firstWorker, secondWorker }, outputPath, expectedOwners: 2);

    internal static int VerifyMany(string serverEvidence, string workersManifest, string outputPath)
    {
        try
        {
            string manifest = Path.GetFullPath(workersManifest);
            var paths = JsonSerializer.Deserialize<string[]>(File.ReadAllText(manifest))
                ?? throw new InvalidDataException("Worker manifest must be an array of all eight worker report paths.");
            return VerifyFiles(serverEvidence, paths.Select(path => Path.GetFullPath(path, Path.GetDirectoryName(manifest)!)).ToArray(), outputPath, expectedOwners: 8);
        }
        catch (Exception error) { Console.Error.WriteLine("MOMENTUM COMBAT PROOF FAIL " + error); return 1; }
    }
    private static int VerifyFiles(string serverEvidence, string[] workerPaths, string outputPath, int expectedOwners)
    {
        var documents = new List<JsonDocument>();
        try
        {
            _checks = 0;
            using var server = JsonDocument.Parse(File.ReadAllText(serverEvidence));
            Check(workerPaths.Length == expectedOwners && workerPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == expectedOwners,
                "manifest contains the entire required cohort with distinct report paths");
            foreach (string path in workerPaths) documents.Add(JsonDocument.Parse(File.ReadAllText(path)));
            var workers = documents.Select(document => document.RootElement).ToArray();
            var first = workers[0];
            var owners = workers.Select(worker => worker.GetProperty("localSlot").GetInt32()).ToHashSet();
            string model = first.GetProperty("model").GetString()!;
            Check(owners.Count == expectedOwners && owners.All(slot => slot is >= 0 and < 8) && workers.All(worker => worker.GetProperty("model").GetString() == model)
                && (model is "Native" or "Momentum"), "distinct actual owners share the same defined model arm");
            foreach (var worker in workers)
            {
                var ai = worker.GetProperty("ai"); int slot = worker.GetProperty("localSlot").GetInt32();
                Check(ai.GetProperty("Enabled").GetBoolean() && ai.GetProperty("ActivityPassed").GetBoolean()
                    && !ai.GetProperty("IsBot").GetBoolean() && !ai.GetProperty("RosterBot").GetBoolean()
                    && worker.GetProperty("shooting").GetInt32() > 0 && worker.GetProperty("remoteAlive").GetInt32() > 0,
                    "each actual human owner produced native AI control and fire activity");
                Check(model == "Native" ? worker.GetProperty("solverSteps").GetInt64() == 0 : worker.GetProperty("solverSteps").GetInt64() > 0,
                    "each owner exercised its selected solver policy");
                var seen = worker.GetProperty("humanSlots").EnumerateArray().Select(value => value.GetInt32()).ToHashSet();
                Check(owners.Where(other => other != slot).All(seen.Contains), "each owner observed every other active cohort human");
                if (expectedOwners == 8)
                {
                    Check(worker.GetProperty("minimumHumanOwners").GetInt32() == 8, "eight-owner worker used the full-cohort observation gate");
                    var faults = worker.GetProperty("latency");
                    Check(faults.GetProperty("RoundTripMs").GetInt32() == 200 && faults.GetProperty("JitterMs").GetInt32() == 20
                        && Math.Abs(faults.GetProperty("LossPercent").GetDouble() - 1) < .000000001
                        && Math.Abs(faults.GetProperty("ReorderRate").GetDouble() - .05) < .000000001
                        && Math.Abs(faults.GetProperty("DuplicateRate").GetDouble() - .01) < .000000001,
                        "each eight-human owner uses the declared moderate-latency loss/reorder/duplicate profile");
                }
            }
            if (expectedOwners == 8)
            {
                Check(workers.Select(worker => worker.GetProperty("hunter").GetString()).Distinct().Count() >= Hunters.Playable
                    && workers.All(worker => Enum.TryParse<Hunter>(worker.GetProperty("hunter").GetString(), out var hunter)
                        && (int)hunter >= 0 && (int)hunter < Hunters.Playable),
                    "all seven native Hunter identities represented in real owner cohort");
                Check(workers.Select(worker => worker.GetProperty("latency").GetProperty("Seed").GetInt32()).Distinct().Count() == 8,
                    "each owner uses an independent explicit transport fault seed");
            }
            Check(workers.Sum(worker => worker.GetProperty("clientClaimsDeclared").GetInt64()) > 0, "actual human-owner client declared native resolved hit claim");
            Check(server.RootElement.GetProperty("Role").GetString() == "server"
                && !server.RootElement.GetProperty("ConnectionLost").GetBoolean(), "completed genuine authority report required");
            Check(workers.All(worker => worker.GetProperty("epoch").GetUInt64() == first.GetProperty("epoch").GetUInt64()
                && worker.GetProperty("match").GetUInt16() == first.GetProperty("match").GetUInt16()), "owner workers share actual authority stream");
            ulong epoch = first.GetProperty("epoch").GetUInt64();
            ushort match = first.GetProperty("match").GetUInt16();
            bool InStream(JsonElement key) => key.GetProperty("AuthorityEpoch").GetUInt64() == epoch
                && key.GetProperty("MatchId").GetUInt16() == match
                && owners.Contains(key.GetProperty("ShooterSlot").GetInt32());
            Check(server.RootElement.GetProperty("Overflow").GetInt64() == 0
                && !server.RootElement.GetProperty("SemanticOverrun").GetBoolean()
                && server.RootElement.GetProperty("SemanticDisconnects").GetInt64() == 0,
                "authority evidence and semantic stream are complete without overrun");
            Check(workers.Sum(worker => worker.GetProperty("healthChanges").GetInt32()) > 0,
                "real human observers saw body health decrease during the encounter");
            var joins = server.RootElement.GetProperty("TimedCaseJoins");
            foreach (var budget in joins.GetProperty("Budgets").EnumerateObject())
                Check(budget.Value.GetProperty("Dropped").GetInt64() == 0,
                    "complete bounded authority join evidence: " + budget.Name);
            string Key(JsonElement key) => string.Join("/", new[] { "AuthorityEpoch", "MatchId", "ShooterSlot", "Generation", "LifeId", "ShotId" }.Select(name => key.GetProperty(name).GetRawText()));
            var certificates = joins.GetProperty("Certificates").EnumerateArray().Select(row => row.GetProperty("Shot"))
                .Where(shot => InStream(shot.GetProperty("Key"))
                    && shot.GetProperty("Unavailable").GetInt32() == 0 && shot.GetProperty("Components").GetUInt32() > 0)
                .Select(shot => Key(shot.GetProperty("Key"))).ToHashSet();
            var claims = joins.GetProperty("Claims").EnumerateArray().Where(row => InStream(row.GetProperty("Key")))
                .Select(row => Key(row.GetProperty("Key"))).ToHashSet();
            string PaymentKey(JsonElement key) => Key(key.GetProperty("Shot")) + "/" + string.Join("/",
                new[] { "Victim", "Generation", "Life", "Component", "Kind" }.Select(name => key.GetProperty(name).GetRawText()));
            var paid = joins.GetProperty("Payments").EnumerateArray()
                .Where(row => !row.GetProperty("Correction").GetBoolean() && InStream(row.GetProperty("Key").GetProperty("Shot"))).ToArray();
            Check(paid.Select(row => PaymentKey(row.GetProperty("Key"))).Distinct().Count() == paid.Length,
                "no duplicate native payment for full shot/victim incarnation/component/kind identity; valid multiple components remain distinct");
            var joined = paid.Where(row =>
            {
                var key = row.GetProperty("Key").GetProperty("Shot"); var outcome = row.GetProperty("Outcome");
                var payment = row.GetProperty("Key");
                int after = outcome.GetProperty("HealthAfter").GetInt32(), damage = outcome.GetProperty("DamageApplied").GetInt32();
                return InStream(key) && certificates.Contains(Key(key)) && claims.Contains(Key(key))
                    && owners.Contains(outcome.GetProperty("VictimSlot").GetInt32()) && damage > 0
                    && payment.GetProperty("Victim").GetInt32() == outcome.GetProperty("VictimSlot").GetInt32()
                    && payment.GetProperty("Generation").GetUInt16() == outcome.GetProperty("VictimGeneration").GetUInt16()
                    && payment.GetProperty("Life").GetUInt16() == outcome.GetProperty("VictimLife").GetUInt16()
                    && after + damage > after && outcome.GetProperty("DamageSequence").GetUInt32() > 0;
            }).ToArray();
            Check(joined.Length > 0, "full ShotKey joins native owner certificate, received claim and actual human-victim damage payment");
            long rewindSamples = server.RootElement.GetProperty("ShotRewind").GetProperty("SamplesByWeapon").EnumerateArray().Sum(value => value.GetInt64());
            long rewindFrames = server.RootElement.GetProperty("ShotRewind").GetProperty("FramesByWeapon").EnumerateArray().Sum(value => value.GetInt64());
            Check(rewindSamples > 0 && rewindFrames > 0, "real authority served positive historical rewind; not inferred from ACK age");
            string output = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(new { checks = _checks, model, owners, workerCount = workers.Length,
                hunters = workers.Select(worker => worker.TryGetProperty("hunter", out var hunter) ? hunter.GetString() : "not recorded in earlier two-owner report").ToArray(),
                joinedPayments = joined.Length, rewindSamples, rewindFrames,
                scope = "Exact worker epoch/match owner ShotKey certificate/claim/payment joins; unique full native payment keys; native committed DamageApplied + HealthAfter + DamageSequence establish paid body health reduction. No independently sampled HealthBefore exists; HealthAfter+DamageApplied is accounted prior health, not a second observation. Positive authority rewind aggregate does not establish per-shot depth", payments = joined }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"MOMENTUM COMBAT PROOF PASS {_checks} assertions, {joined.Length} joined payments"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("MOMENTUM COMBAT PROOF FAIL " + error); return 1; }
        finally { foreach (var document in documents) document.Dispose(); }
    }
}
