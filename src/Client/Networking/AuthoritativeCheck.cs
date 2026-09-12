using System;
using System.IO;
using MphRead.Entities;
using OpenTK.Mathematics;
using ProjectPrime.Server.Shared;
using MapGen = MphRead.Mods.MapGen;

namespace MphRead.Mods.Network
{
    /// <summary>Rendered authoritative movement check using the shipping scene hooks.</summary>
    public sealed class AuthoritativeCheck : IRenderToolClient
    {
        private readonly IRenderToolHost _host;
        private readonly ScenePresentation _presentation;
        private readonly AuthoritativePlay _play;
        private readonly Scene _scene;
        private readonly double _seconds;
        private readonly int _durationFrames;
        private const int GraceFrames = 600;
        private readonly Vector3[] _last = new Vector3[8];
        private readonly bool[] _seen = new bool[8];
        private readonly double[] _travel = new double[8];
        private int _frames;
        private bool _lit;
        private readonly string? _shotDirectory;
        private readonly double _spectateAt;
        private readonly double _rejoinAt;
        private bool _spectateRequested;
        private bool _rejoinRequested;
        private bool _sawSpectator;
        private bool _sawRejoin;
        private uint _spectatorLife;
        private uint _spectatorMatch;
        private int _shots;

        private AuthoritativeCheck(AuthoritativePlay play, MatchClientContext match,
            Hunter hunter, double seconds,
            string? shotDirectory, int width, int height, double spectateAt, double rejoinAt,
            IRenderToolHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _play = play;
            _seconds = seconds;
            _durationFrames = checked((int)Math.Ceiling(seconds * 60));
            _shotDirectory = shotDirectory;
            _spectateAt = spectateAt;
            _rejoinAt = rejoinAt;
            _scene = new Scene(features: ClientMatchFeatures.Capture())
            { Services = new ClientSceneServices(match) };
            _presentation = host.CreatePresentation(_scene);
            play.BuildPlayers(_scene, hunter, 0);
            _scene.AddRoom(play.Client.Accepted.Room, play.Client.Accepted.Mode,
                playerCount: NetConfig.RoomPlayerCount);
            play.ScriptInput = Drive;
        }

        private void Drive(PlayerEntity player, uint tick)
        {
            double elapsed = _simulationFrames / 60d;
            if (_spectateAt >= 0 && !_spectateRequested && elapsed >= _spectateAt)
            {
                SpectatorMode.Start(_scene);
                _spectateRequested = true;
            }
            if (_rejoinAt >= 0 && !_rejoinRequested && elapsed >= _rejoinAt)
            {
                SpectatorMode.Rejoin(_scene);
                _rejoinRequested = true;
            }
            foreach (SnapshotPlayer state in _play.Client.SnapshotPlayers)
            {
                if (state.Slot != _play.LocalSlot) { continue; }
                if ((state.Flags & SnapshotPlayerFlags.Spectating) != 0)
                {
                    _sawSpectator = true;
                    _spectatorLife = state.Life;
                    _spectatorMatch = _play.Client.Accepted.MatchId;
                }
                if (_sawSpectator && _rejoinRequested && state.Health > 0
                    && (state.Life != _spectatorLife || _play.Client.Accepted.MatchId != _spectatorMatch))
                { _sawRejoin = true; }
            }
            if (SpectatorMode.IsSpectating)
            {
                player.Controls.ClearAll();
                return;
            }
            Vector3 aim = -Vector3.UnitZ;
            foreach (SnapshotPlayer other in _play.Client.SnapshotPlayers)
            {
                if (other.Slot == _play.LocalSlot || other.Health == 0) { continue; }
                Vector3 direction = other.Position - player.Position;
                if (direction.LengthSquared > 0.01f) { aim = direction.Normalized(); }
                break;
            }
            InputButtons movement = ((tick / 120 + (uint)_play.LocalSlot) % 4) switch
            {
                0 => InputButtons.Forward, 1 => InputButtons.Left,
                2 => InputButtons.Back, _ => InputButtons.Right
            };
            InputButtons held = movement;
            InputButtons pressed = tick % 90 == 0 ? InputButtons.Jump : InputButtons.None;
            if (tick % 20 < 6) { held |= InputButtons.Shoot; }
            if (tick % 20 == 0) { pressed |= InputButtons.Shoot; }
            player.ApplyNetworkInput(new InputCommand(tick, tick, 0, held, pressed, aim, InputCommand.NoWeapon));
        }

        private int _simulationFrames;

        public void OnLoad()
        {
            _presentation.Size = _host.Size;
            _presentation.OnLoad();
            _presentation.OnResize();
        }

        public void OnFrame()
        {
            _presentation.OnSimulationFrame();
            _simulationFrames++;
            RenderToolCapture? requestedCapture = (_frames + 1) % 120 == 0
                ? new RenderToolCapture(CaptureTargetKind.SceneTarget) : null;
            RenderToolFrameResult frame = _host.Render(_presentation, requestedCapture,
                acknowledgePresentation: true);
            // A capture may have completed after the picture that requested it.
            // Consume every returned result; sampling cadence only decides when
            // a new request is queued.
            ConsumeCapture(frame);
            if (frame.Submitted)
            {
                _frames++;
                foreach (PlayerEntity player in _scene.GetPlayerEntities())
                {
                    if (!player.ModIsInPlay) { continue; }
                    int slot = player.SlotIndex;
                    if (_seen[slot])
                    {
                        float distance = (player.Position - _last[slot]).Length;
                        if (distance < 5) { _travel[slot] += distance; }
                    }
                    _seen[slot] = true;
                    _last[slot] = player.Position;
                }
            }
            // A duration boundary may land during a legitimate map load.
            // Finish once that match has a complete state, with a bounded grace.
            if (_simulationFrames >= _durationFrames
                && (_play.Client.State == NetConnectionState.Playing && _play.HasWorldState
                    || _simulationFrames >= _durationFrames + GraceFrames))
            {
                _host.Close();
            }
        }

        private void ConsumeCapture(RenderToolFrameResult frame)
        {
            for (int i = 0; i < frame.Captures.Count; i++)
            {
                ConsumeCapture(frame.Captures[i]);
            }
        }

        private void ConsumeCapture(RenderCaptureResult capture)
        {
            if (capture.Target != CaptureTargetKind.SceneTarget)
            {
                return;
            }
            _lit |= RenderToolCaptureSupport.NonBlackFraction(capture) > 0.01;
            if (_shotDirectory != null)
            {
                RenderToolCaptureSupport.Save(capture, Path.Combine(_shotDirectory,
                    $"slot{_play.LocalSlot}-{_shots++:000}.png"));
            }
        }

        public void OnCapture(RenderCaptureResult capture)
        {
            if (capture.Target == CaptureTargetKind.SceneTarget)
            {
                ConsumeCapture(capture);
            }
        }

        public void OnClosing()
        {
            _presentation.DoCleanup();
        }

        private int Report()
        {
            if (_play.LocalSlot is < 0 or >= 8)
            {
                Console.WriteLine("AUTHCHECK result=FAIL reason=observer-role-no-local-player; player travel proof is unavailable.");
                return 1;
            }
            int movingRemotes = 0;
            for (int slot = 0; slot < 8; slot++)
            {
                if (slot != _play.LocalSlot && _seen[slot] && _travel[slot] > 1) { movingRemotes++; }
            }
            bool passed = _play.Client.State == NetConnectionState.Playing && _frames >= 300
                && _lit && movingRemotes > 0 && _travel[_play.LocalSlot] > 1
                && _play.HasWorldState && _play.CombatEvents > 0
                && (_spectateAt < 0 || _sawSpectator) && (_rejoinAt < 0 || _sawRejoin);
            Console.WriteLine(FormattableString.Invariant($"AUTHCHECK slot={_play.LocalSlot} frames={_frames} snapshots={_play.Client.SnapshotsReceived} localTravel={_travel[_play.LocalSlot]:F2} movingRemotes={movingRemotes} lit={_lit} result={(passed ? "PASS" : "FAIL")}"));
            ClientPrediction prediction = _play.Prediction;
            Console.WriteLine(FormattableString.Invariant($"PREDICTION samples={prediction.Error.Count} meanError={prediction.Error.Mean:F3} worstError={prediction.Error.Max:F3} corrections={prediction.Corrections} hard={prediction.HardCorrections} historyMisses={prediction.HistoryMisses}"));
            Console.WriteLine($"REPLICATION world={_play.HasWorldState} combatEvents={_play.CombatEvents} damageEvents={_play.DamageEvents}");
            Console.WriteLine($"SPECTATOR requested={_spectateRequested} observed={_sawSpectator} rejoined={_sawRejoin}");
            return passed ? 0 : 1;
        }

        public static int Run(string host, int port, string name, Hunter hunter, double seconds,
            string? shotDirectory = null, int width = 320, int height = 180,
            bool recordReplay = false, double spectateAt = -1, double rejoinAt = -1)
        {
            if (!Double.IsFinite(seconds) || seconds < 10 || seconds > 300) { return 2; }
            hunter = Launcher.Hunters.Resolve(hunter);
            using var runtime = new ClientOnlineRuntime();
            using var play = new AuthoritativePlay(host, port, name, hunter);
            play.Join();
            MatchClientContext match = runtime.AdoptMatch(play, Guid.NewGuid())!;
            if (shotDirectory != null) { Directory.CreateDirectory(shotDirectory); }
            try
            {
                MapGen.RoomContentPreparationResult preparation =
                    MapGen.MapPreparation.PrepareRoomAsync(
                        new MapGen.RoomContentRequest(play.Client.Accepted.Room, null,
                            MapGen.GameplayContentIdentity.Current(
                                BuildIdentity.Display, NetHeader.Version),
                            MapGen.RoomContentPurpose.Match),
                        System.Threading.CancellationToken.None)
                    .GetAwaiter().GetResult();
                MapGen.MapPreparation.RequirePreparedRoom(preparation);
                if (recordReplay && !ReplayRecorder.Start())
                    throw new ProgramException("Could not start replay recording.");
                bool sdl = RenderBackendSelection.Current == RenderBackendKind.Sdl;
                // This check is the one tool whose contract includes a real
                // presentation acknowledgement. Keep an SDL window visible
                // so a compositor/minimize race cannot turn a successful
                // offscreen encode into a false authoritative presentation.
                using IRenderToolHost hostAdapter = RenderToolHostFactory.Create(
                    new Vector2i(width, height), "Project Prime authoritative check",
                    updateFrequency: 60, visible: sdl, presentable: true);
                var check = new AuthoritativeCheck(play, match, hunter, seconds,
                    shotDirectory, width, height, spectateAt, rejoinAt, hostAdapter);
                hostAdapter.Run(check);
                return check.Report();
            }
            finally
            {
                if (recordReplay) { ReplayRecorder.Stop(); }
                ContentEnvironment.UnmountMap();
            }
        }
    }
}
