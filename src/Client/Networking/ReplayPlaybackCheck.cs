using System;
using System.Collections.Generic;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>Bounded rendered recording check through the shipping passive playback hooks.</summary>
    public sealed class ReplayPlaybackCheck : IRenderToolClient
    {
        private readonly IRenderToolHost _host;
        private readonly ScenePresentation _presentation;
        private readonly Scene _scene;
        private readonly double _seconds;
        private readonly int _durationFrames;
        private readonly Vector3[] _last = new Vector3[8];
        private readonly bool[] _seen = new bool[8];
        private readonly double[] _travel = new double[8];
        private readonly int[] _deaths = new int[8];
        private int _frames;
        private int _endFrames;
        private uint _match;
        private bool _lit;
        private long _stateMismatches;
        private long _deathsObserved;

        private ReplayPlaybackCheck(double seconds, IRenderToolHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _seconds = seconds;
            _durationFrames = checked((int)Math.Ceiling(seconds * 60));
            _scene = new Scene(features: ClientMatchFeatures.Capture()) { Services = new ClientSceneServices() };
            _presentation = host.CreatePresentation(_scene);
            _scene.Players.MaxPlayers = PlayerEntity.SlotCapacity;
            NetLaunch.BuildPlayers(_scene, Hunter.Samus, 0, localSlot: -1);
            var room = NetLaunch.ServerRoom() ?? throw new ProgramException("Replay has no room.");
            _scene.AddRoom(room.RoomKey, room.Mode, playerCount: NetConfig.RoomPlayerCount);
        }

        public void OnLoad()
        {
            _presentation.Size = _host.Size;
            _presentation.OnLoad();
            _presentation.OnResize();
        }

        public void OnFrame()
        {
            _presentation.OnSimulationFrame();
            RenderToolCapture? requestedCapture = (_frames + 1) % 120 == 0
                ? new RenderToolCapture(CaptureTargetKind.SceneTarget) : null;
            RenderToolFrameResult frame = _host.Render(_presentation, requestedCapture);
            ConsumeCaptures(frame.Captures);
            if (frame.Submitted)
            {
                _frames++;
                ModernReplayState state = ReplayPlayback.Modern;
                if (_match != state.Match.MatchId)
                {
                    _match = state.Match.MatchId;
                    Array.Clear(_seen); Array.Clear(_deaths);
                }
                foreach (SnapshotPlayer source in state.Players)
                {
                    PlayerEntity player = _scene.Players[source.Slot];
                    int points = source.Points, kills = source.Kills, deaths = source.Deaths;
                    if (state.World.HasState && !Sequence32.IsNewer(state.Snapshot.ServerTick, state.World.ServerTick))
                    {
                        WorldRecord score = state.World.Records[1 + source.Slot * 2];
                        points = unchecked((int)score.A); kills = unchecked((int)score.B); deaths = unchecked((int)score.C);
                    }
                    if ((player.Position - source.Position).LengthSquared > 0.0001f
                        || player.Health != source.Health || _scene.Match.Players[source.Slot].Points != points
                        || _scene.Match.Players[source.Slot].Kills != kills || _scene.Match.Players[source.Slot].Deaths != deaths)
                    { _stateMismatches++; }
                    if (source.Deaths > _deaths[source.Slot]) { _deathsObserved += source.Deaths - _deaths[source.Slot]; }
                    _deaths[source.Slot] = source.Deaths;
                    if (!player.ModIsInPlay) { continue; }
                    int slot = source.Slot;
                    if (_seen[slot])
                    {
                        float distance = (player.Position - _last[slot]).Length;
                        if (distance < 5) { _travel[slot] += distance; }
                    }
                    _seen[slot] = true;
                    _last[slot] = player.Position;
                }
                if (ReplayPlayback.AtEnd) { _endFrames++; }
            }
            if (_frames >= _durationFrames || _endFrames >= 60) { _host.Close(); }
        }

        private void ConsumeCaptures(IReadOnlyList<RenderCaptureResult> captures)
        {
            for (int i = 0; i < captures.Count; i++)
            {
                OnCapture(captures[i]);
            }
        }

        public void OnCapture(RenderCaptureResult capture)
        {
            if (capture.Target == CaptureTargetKind.SceneTarget)
            {
                _lit |= RenderToolCaptureSupport.NonBlackFraction(capture) > 0.01;
            }
        }

        public void OnClosing()
        {
            _presentation.DoCleanup();
        }

        private int Report()
        {
            ModernReplayState state = ReplayPlayback.Modern;
            int moving = 0;
            for (int slot = 0; slot < 8; slot++) { if (_travel[slot] > 1) { moving++; } }
            bool passed = _frames >= 300 && _lit && moving > 0 && state.SnapshotsReceived >= 300
                && state.WorldApplications > 0 && _stateMismatches == 0
                && AuthoritativePlay.Current == null && NetSession.LocalSlot == -1;
            Console.WriteLine($"REPLAYCHECK frames={_frames} snapshots={state.SnapshotsReceived} "
                + $"moving={moving} lit={_lit} atEnd={ReplayPlayback.AtEnd} matches={state.MatchesLoaded} "
                + $"worldApplications={state.WorldApplications} combatEvents={state.CombatEventsReceived} "
                + $"damageEvents={state.DamageEventsReceived} deaths={_deathsObserved} "
                + $"stateMismatches={_stateMismatches} result={(passed ? "PASS" : "FAIL")}");
            return passed ? 0 : 1;
        }

        public static int Run(string path, double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 10 || seconds > 300) { return 2; }
            if (!ReplayPlayback.Join(path)) { Console.Error.WriteLine(ReplayPlayback.LastError); return 1; }
            try
            {
                if (!ReplayPlayback.IsModern)
                { Console.Error.WriteLine("Rendered replay check requires an authoritative recording."); return 2; }
                using IRenderToolHost host = RenderToolHostFactory.Create(
                    new Vector2i(320, 180), "Project Prime replay playback check",
                    updateFrequency: 60, visible: false, presentable: false);
                var check = new ReplayPlaybackCheck(seconds, host);
                host.Run(check);
                return check.Report();
            }
            finally { ReplayPlayback.Stop(); }
        }
    }
}
