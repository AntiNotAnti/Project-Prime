using System;
using System.IO;
using System.Linq;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Actual passive services and CPU scenes; no room/assets, playback stepping or GL.
internal static class MomentumReplayRulesTests
{
    private static int _checks;
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static void Refused(Action action, string message)
    {
        bool refused = false;
        try { action(); } catch (InvalidDataException) { refused = true; }
        Check(refused, message);
    }
    private static byte[] Match(MovementModel model)
    {
        byte[] bytes = new byte[1 + MatchStatePacket.Size]; bytes[0] = (byte)PacketType.MatchState;
        new MatchStatePacket { MatchId = 7, AuthorityEpoch = 23, RoomKey = "MP3 PROVING GROUND",
            Mode = (byte)GameMode.Battle, TimeRemaining = 240,
            RuleBits = (uint)MovementModelRules.Modifier(model) }.Write(bytes.AsSpan(1));
        return bytes;
    }
    private static byte[] Configuration(MovementModel model, SessionPhase phase = SessionPhase.InMatch)
    {
        byte[] bytes = new byte[1 + SessionStatePacket.Size]; bytes[0] = (byte)PacketType.SessionState;
        new SessionStatePacket { MatchId = 7, AuthorityEpoch = 23, Revision = 1,
            Phase = phase, MaxPlayers = 8, WorldProfile = MatchWorldProfile.Resolve(8),
            Match = new MatchDefinition { RoomKey = "MP3 PROVING GROUND", Mode = GameMode.Battle,
                TimeLimitSeconds = 300, MovementModel = model } }.Write(bytes.AsSpan(1));
        return bytes;
    }
    private static void Contradictions()
    {
        foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum, MovementModel.Quake3 })
        {
            MovementModel other = model == MovementModel.Native ? MovementModel.Momentum : MovementModel.Native;
            foreach (bool configurationFirst in new[] { false, true })
            {
                var state = new ReplayReplicaState();
                state.Accept(configurationFirst ? Configuration(model) : Match(model), 0);
                byte[] before = state.CaptureCheckpoint().Bytes.ToArray();
                Refused(() => state.Accept(configurationFirst ? Match(other) : Configuration(other), 1),
                    "frozen contradiction refused in either arrival order: " + model + "/" + configurationFirst);
                Check(state.CaptureCheckpoint().Bytes.SequenceEqual(before), "contradictory packet leaves decoder unchanged");
                state.Accept(configurationFirst ? Match(model) : Configuration(model), 1);
                Check(state.Match.HasValue && state.Configuration.HasValue, "matching frozen pair accepted");

                byte[] valid = state.CaptureCheckpoint().Bytes.ToArray();
                byte[] configuration = Configuration(model);
                int configurationAt = valid.AsSpan().IndexOf(configuration.AsSpan(1));
                Check(configurationAt >= 0, "exact configuration body located in actual checkpoint");
                byte[] invalid = valid.ToArray();
                int modifierAt = configurationAt + SessionStatePacket.Protocol28Size;
                invalid[modifierAt + 1] ^= 0x80;
                Refused(() => state.RestoreCheckpoint(new(invalid)), "contradictory checkpoint refused: " + model);
                Check(state.CaptureCheckpoint().Bytes.SequenceEqual(valid), "contradictory checkpoint restore transactional");
            }
            foreach (bool configurationFirst in new[] { false, true })
            {
                var mutable = new ReplayReplicaState();
                mutable.Accept(configurationFirst ? Configuration(other, SessionPhase.Lobby) : Match(model), 0);
                mutable.Accept(configurationFirst ? Match(model) : Configuration(other, SessionPhase.Lobby), 1);
                var restored = new ReplayReplicaState(); restored.RestoreCheckpoint(mutable.CaptureCheckpoint());
                Check(restored.Configuration?.Phase == SessionPhase.Lobby && restored.Match.HasValue,
                    "mutable lobby model may differ from provisional match in packets and checkpoint");
            }
            var rotation = new ReplayReplicaState(); rotation.Accept(Match(model), 0); rotation.Accept(Configuration(model), 0);
            byte[] nextMatch = Match(other); var match = MatchStatePacket.Read(nextMatch.AsSpan(1));
            match.MatchId++; match.AuthorityEpoch++; match.Write(nextMatch.AsSpan(1)); rotation.Accept(nextMatch, 1);
            byte[] nextConfiguration = Configuration(other);
            Check(SessionStatePacket.TryReadArchived(nextConfiguration.AsSpan(1), out var config), "rotation fixture configuration valid");
            config.MatchId++; config.AuthorityEpoch++; config.Write(nextConfiguration.AsSpan(1)); rotation.Accept(nextConfiguration, 1);
            Check(rotation.Configuration?.Match.MovementModel == other && rotation.Match?.MatchId == 8,
                "new identity may select a different frozen model");
        }
    }

    internal static int Run()
    {
        Headless.Enter(); _checks = 0;
        try
        {
            Contradictions();
            var foreground = GameState.Current; var registry = PlayerEntity.LegacyRegistry;
            foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum, MovementModel.Quake3 })
            foreach (bool configured in new[] { false, true })
            {
                var host = new PassiveReplaySessionHost();
                using var session = new ReplayPlaybackSession(host);
                host.Inject(Match(model), 0);
                if (configured) host.Inject(Configuration(model), 0);
                var services = new ReplaySceneServices(session, host.State);
                var scene = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                    _ => { }, () => { }, services, initializeRuntime: false);
                try
                {
                    Check(scene.Services.IsReplica && services.SuppressDamage, "real passive replica policy");
                    Check(scene.GameState.MovementModel == MovementModel.Native, "fresh CPU scene defaults Native");
                    services.ApplyRules(scene, 60);
                    Check(scene.GameState.MovementModel == model && scene.GameState.MatchTime == 239,
                        "accepted rules apply model and recorded clock: " + model + "/" + configured);
                    scene.GameState.MovementModel = model == MovementModel.Native ? MovementModel.Momentum : MovementModel.Native;
                    scene.GameState.MatchTime = 123.5f;
                    services.RestoreMatchModifiers(scene);
                    Check(scene.GameState.MovementModel == model && scene.GameState.MatchTime == 123.5f,
                        "restore reapplies model without changing restored clock");
                    Check(!scene.DeveloperMomentumActive && scene.DeveloperMomentumSteps == 0
                        && !scene.TryEnableDeveloperMomentum(), "replica never admits developer owner solver");
                    var checkpoint = host.State.CaptureCheckpoint();
                    var restored = new ReplayReplicaState(); restored.RestoreCheckpoint(checkpoint);
                    var restoredServices = new ReplaySceneServices(session, restored);
                    scene.GameState.MovementModel = MovementModel.Native;
                    restoredServices.RestoreMatchModifiers(scene);
                    Check(scene.GameState.MovementModel == model && restored.Match?.RuleBits == host.State.Match?.RuleBits
                        && restored.Configuration?.Match.MovementModel == host.State.Configuration?.Match.MovementModel,
                        "decoder checkpoint retains both rule sources");
                    byte[] before = restored.CaptureCheckpoint().Bytes.ToArray();
                    foreach (byte protocol in new byte[] { 56, 57, 58 })
                    {
                        byte[] historical = ReplayProtocol58Tests.HistoricalCheckpoint(checkpoint.Bytes.ToArray(), protocol);
                        // The historical field cannot carry bit 16. Exercise its
                        // reserved movement bit rather than truncating Q3 to Classic.
                        if (model == MovementModel.Quake3)
                            historical[47 + 104] |= 0x80;
                        if (model != MovementModel.Native)
                        {
                            Refused(() => restored.RestoreCheckpoint(new(historical)), "old checkpoint reserved bit refused: " + protocol);
                            Check(restored.CaptureCheckpoint().Bytes.SequenceEqual(before), "old malformed restore is transactional");
                        }
                        else
                        {
                            restored.RestoreCheckpoint(new(historical));
                            restoredServices.RestoreMatchModifiers(scene);
                            Check(scene.GameState.MovementModel == MovementModel.Native, "old checkpoint remains Native: " + protocol);
                        }
                    }
                    byte[] future = checkpoint.Bytes.ToArray(); future[6] = 61;
                    Refused(() => restored.RestoreCheckpoint(new(future)), "future checkpoint protocol refused");
                }
                finally { scene.DoCleanup(); scene.UnloadGl(); }
                Check(ReferenceEquals(GameState.Current, foreground) && ReferenceEquals(PlayerEntity.LegacyRegistry, registry),
                    "private CPU construction and cleanup preserve foreground owners");
            }
            Console.WriteLine($"MOMENTUM REPLAY RULES PASS {_checks} assertions; actual decoder/services/CPU replicas, no world playback or visual claim");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("MOMENTUM REPLAY RULES FAIL " + error); return 1; }
    }
}
