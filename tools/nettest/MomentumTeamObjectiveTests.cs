using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Movement;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal sealed record MomentumTeamObjectiveOptions(bool LobbyOwner = false, GameMode Mode = GameMode.NodesTeams);

// Observes actual accepted objective worlds; never creates objective/player facts.
internal sealed class MomentumTeamObjectiveTests : IDisposable
{
    private readonly MomentumOnlineAcceptance _lobby;
    private readonly GameMode _mode;
    private readonly List<object> _samples = new();
    private readonly List<object> _diagnostics = new();
    private int _diagnosticOverflow;
    private uint? _lastTick;
    private object? _validatedConfiguration;
    private int _worlds, _occupancy, _capture, _score, _clientScore, _overflow;
    private object? _firstOccupancy, _firstCapture, _firstAuthorityScore, _firstClientScore;
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    internal static string SelectRoom(GameMode mode = GameMode.NodesTeams)
    {
        Require(mode is GameMode.NodesTeams or GameMode.DefenderTeams, "explicit supported objective test mode");
        const string room = "MP3 PROVING GROUND";
        Require(Metadata.RoomMetadata.TryGetValue(room, out var native) && native.EntityPath != null && native.NodePath != null,
            "predeclared prior-navigation arena has native entity/navigation data");
        Require(MapModeCapabilities.Supports(room, mode, MatchWorldProfile.Resolve(4), out var reason), reason);
        return room;
    }
    internal MomentumTeamObjectiveTests(MomentumTeamObjectiveOptions options, string latency, string loss,
        string reorder, string duplicate, int seed)
    {
        Require(options.Mode is GameMode.NodesTeams or GameMode.DefenderTeams, "supported objective options mode");
        _mode = options.Mode;
        _lobby = new(new MomentumOnlineAcceptanceOptions(MinimumHumans: 2, MinimumBots: 2,
            Latency: latency, Loss: loss, Reorder: reorder, Duplicate: duplicate, Seed: seed,
            LobbyOwner: options.LobbyOwner, LobbyBots: 2, LobbyHumans: 2, LobbyBotLevel: 3));
    }
    internal void PrepareLobby() => _lobby.PrepareLobby();
    internal void Validate(Scene scene, MatchDefinition definition)
    {
        Require(definition.Mode == _mode && GameState.IsTeamMode(definition.Mode), "actual requested team objective frozen mode");
        Require(MapModeCapabilities.Supports(definition.RoomKey, definition.Mode, MatchWorldProfile.Resolve(4), out var reason), reason);
        var occupied = Enumerable.Range(0, NetSession.SlotOccupied.Length).Where(slot => NetSession.SlotOccupied[slot]).ToArray();
        Require(occupied.Length == 4 && occupied.Count(slot => NetSession.SlotIsBot[slot]) == 2, "genuine two-human/two-bot cohort");
        Require(occupied.Where(slot => NetSession.SlotIsBot[slot]).All(slot => NetSession.SlotBotLevel[slot] == 3),
            "accepted server bot difficulty matches requested native level three");
        Require(occupied.All(slot => NetSession.SlotTeamIndex[slot] is 0 or 1)
            && occupied.Count(slot => NetSession.SlotTeamIndex[slot] == 0) == 2
            && occupied.Count(slot => NetSession.SlotTeamIndex[slot] == 1) == 2, "accepted roster balanced two versus two");
        Require(scene.GameState.TeamCount == 2 && scene.Players.Main.TeamIndex == NetSession.SlotTeamIndex[NetSession.LocalSlot], "native owner adopts accepted team");
        bool hasNode = false;
        foreach (var node in scene.GetNodeDefenseEntities()) { hasNode = true; break; }
        Require(hasNode, "real map contains native objective entities");
        Require(Metadata.RoomMetadata.TryGetValue(definition.RoomKey, out var native) && native.EntityPath != null,
            "native catalog map uses production BotMapAvailable native admission branch");
        _validatedConfiguration = new { room = definition.RoomKey, mode = definition.Mode.ToString(),
            model = definition.MovementModel.ToString(), epoch = NetSession.AuthorityEpoch, match = NetSession.CurrentMatchId,
            roster = occupied.Select(slot => new { slot, team = NetSession.SlotTeamIndex[slot],
                isBot = NetSession.SlotIsBot[slot], botLevel = NetSession.SlotBotLevel[slot], hunter = NetSession.SlotHunter[slot].ToString(),
                generation = NetPlayerLifecycle.Generation(slot) }).ToArray() };
    }
    internal void Observe(Scene scene)
    {
        var world = ReplayCapture.LatestAuthorityWorld;
        if (world == null || world.Tick == _lastTick || !NetSession.WorldIsReady || NetSession.FreezeGameplay) return;
        Require(world.MatchId == NetSession.CurrentMatchId && world.Epoch == NetSession.AuthorityEpoch
            && NetSession.ObjectiveTickIsCurrent(world.Tick), "received objective world matches admitted stream");
        _lastTick = world.Tick; _worlds++;
        if (_worlds == 1 || _worlds % 10 == 0)
        {
            if (_diagnostics.Count < 128)
            {
                var owner = scene.Players.Main;
                object? ReadAi(string name)
                {
                    var field = owner.AiData.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                    var value = field?.GetValue(owner.AiData);
                    return value is EntityBase entity ? new { entity.Id, type = entity.GetType().Name }
                        : value is bool or byte or ushort or int or uint or float ? value : value == null ? null : "present";
                }
                float[] V(OpenTK.Mathematics.Vector3 v) => new[] { v.X, v.Y, v.Z };
                var nodes = new List<object>();
                foreach (var node in scene.GetNodeDefenseEntities()) nodes.Add(new { node.Id, position = V(node.Position),
                    node.CurrentTeam, node.OccupyingTeam, node.Progress, node.Contested, node.InProgress });
                _diagnostics.Add(new { world.Epoch, world.MatchId, world.Tick, world.NodeCount,
                    rawNodes = world.Nodes.Take(world.NodeCount < 0 ? world.Nodes.Length : world.NodeCount).ToArray(),
                    sceneNodes = nodes, ownerPosition = V(owner.Position), nativeSpeed = V(owner.Speed), owner.Health,
                    owner.IsAltForm, owner.IsMorphing, owner.IsUnmorphing, owner.BotLevel,
                    authorityCaptures = world.NodesCaptured.ToArray(), clientCaptures = scene.GameState.NodesCaptured.ToArray(),
                    authorityPoints = world.TeamPoints.ToArray(), clientPoints = scene.GameState.TeamPoints.ToArray(),
                    authorityObjectiveStats = world.ObjectiveStats.ToArray(), clientTeamTime = scene.GameState.TeamTime.Take(2).ToArray(),
                    ai = new { nodeData = ReadAi("_nodeData"), strategicNode = ReadAi("_botStrategicNode"),
                        strategicTicks = ReadAi("_botStrategicTicks"), strategicCooldown = ReadAi("_botStrategicCooldown"),
                        tacticalTarget = ReadAi("_tacticalTarget"), tacticalMovementActive = ReadAi("_tacticalMovementActive") },
                    scope = "read-only first/every tenth distinct accepted authority world, including neutral/empty worlds; private field absence is null; no AI method invocation" });
            }
            else _diagnosticOverflow++;
        }
        if (_mode == GameMode.NodesTeams ? scene.GameState.TeamPoints.Take(2).Any(value => value > 0)
            : scene.GameState.TeamTime.Take(2).Any(value => value > 0))
        {
            _clientScore++;
            _firstClientScore ??= new { world.Epoch, world.MatchId, objectiveWorldTick = world.Tick,
                appliedSnapshotFrame = NetSession.AppliedSnapshotFrame, points = scene.GameState.TeamPoints.Take(2).ToArray(), teamTime = scene.GameState.TeamTime.Take(2).ToArray(),
                scope = "independently replicated client score: Nodes points or Defender TeamTime; not assumed same-frame as objective world" };
        }
        if (_mode == GameMode.NodesTeams ? world.TeamPoints.Take(2).Any(value => value > 0)
            : Enumerable.Range(0, 8).Any(slot => world.ObjectiveStats[slot * 4] > 0))
        {
            _score++;
            _firstAuthorityScore ??= new { world.Epoch, world.MatchId, world.Tick, points = world.TeamPoints.Take(2).ToArray(), objectiveStats = world.ObjectiveStats.ToArray(),
                scope = "Nodes authority TeamPoints or Defender authority per-player integer ObjectiveSeconds; no TeamTime field exists in objective world" };
        }
        Require(world.NodesCaptured.SequenceEqual(scene.GameState.NodesCaptured), "client adopts exact authority node capture counters");
        foreach (var node in world.Nodes.Take(world.NodeCount < 0 ? world.Nodes.Length : world.NodeCount))
        {
            Require(scene.TryGetEntity(node.Id, out var entity) && entity is NodeDefenseEntity, "authority objective entity exists in native scene");
            var actual = ((NodeDefenseEntity)entity!).CaptureReplayAuthority();
            var expectedNode = node with { Capturer = ReplayActorRef.Capture(NetObjectiveSync.Resolve(scene, node.Capturer)) };
            Require(actual == expectedNode, "native objective entity exactly adopts received authority node fact");
            bool occupied = _mode == GameMode.NodesTeams ? node.Occupants != 0 : node.Team is 0 or 1 && !node.Contested, captured = node.Team is 0 or 1 && world.NodesCaptured.Any(value => value > 0);
            bool scored = _mode == GameMode.NodesTeams ? world.TeamPoints.Take(2).Any(value => value > 0)
                : Enumerable.Range(0, 8).Any(slot => world.ObjectiveStats[slot * 4] > 0);
            if (occupied)
            {
                _occupancy++;
                _firstOccupancy ??= new { world.Epoch, world.MatchId, world.Tick, node, captures = world.NodesCaptured.ToArray() };
            }
            if (captured)
            {
                _capture++;
                _firstCapture ??= new { world.Epoch, world.MatchId, world.Tick, node, captures = world.NodesCaptured.ToArray() };
            }
            if (occupied || captured || scored)
            {
                if (_samples.Count < 128) _samples.Add(new { world.Epoch, world.MatchId, world.Tick, node,
                    captures = world.NodesCaptured.ToArray(), authorityObjectiveStats = world.ObjectiveStats.ToArray(), clientTeamTime = scene.GameState.TeamTime.Take(2).ToArray(), authorityPoints = world.TeamPoints.Take(2).ToArray(), clientPoints = scene.GameState.TeamPoints.Take(2).ToArray(), scope = "received authority world and exact native client adoption; captures are objective counters, not combat kills" });
                else _overflow++;
            }
        }
    }
    internal object Report => new { validatedConfiguration = _validatedConfiguration,
        diagnostics = _diagnostics, diagnosticOverflow = _diagnosticOverflow,
        sampleScope = "At most128 representative positive node facts; sampleOverflow counts omitted observations, not lossless trace coverage.", worlds = _worlds, occupancySamples = _occupancy, capturedSamples = _capture,
        authorityScoreSamples = _score, clientScoreSamples = _clientScore,
        firstOccupancy = _firstOccupancy, firstCapture = _firstCapture, firstAuthorityScore = _firstAuthorityScore, firstClientScore = _firstClientScore, boundedSamples = _samples, sampleOverflow = _overflow,
        mode = _mode.ToString(),
        scoreScope = "Nodes requires authoritative points and independently replicated client points plus actual capture; Defender requires uncontested authority owning team, authoritative per-player integer ObjectiveSeconds, and independent client TeamTime. Neither claims same-frame equality across score streams.",
        scope = "Team objective actual UDP two human owners plus two server bots; production AI controls; received authoritative node occupancy/capture/counters and exact client adoption. No forced placements, captures, health, team or readiness." };
    internal void Complete()
    {
        Require(_worlds > 0 && _occupancy > 0 && (_mode != GameMode.NodesTeams || _capture > 0) && _score > 0 && _clientScore > 0,
            "positive mode-specific actual authority objective and independently replicated native score evidence required");
    }
    public void Dispose() => _lobby.Dispose();
}
