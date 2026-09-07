using System;
using System.Diagnostics;
using System.IO;
using MphRead.Entities;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace MphRead.Mods.Network
{
    /// <summary>Rendered authoritative movement check using the shipping scene hooks.</summary>
    public sealed class AuthoritativeCheck : GameWindow
    {
        private readonly AuthoritativePlay _play;
        private readonly Scene _scene;
        private readonly double _seconds;
        private readonly Stopwatch _clock = new();
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

        private AuthoritativeCheck(AuthoritativePlay play, Hunter hunter, double seconds,
            string? shotDirectory, int width, int height, double spectateAt, double rejoinAt)
            : base(new GameWindowSettings { UpdateFrequency = 60 }, new NativeWindowSettings
            {
                ClientSize = new Vector2i(width, height), Title = "Prime Hunters authoritative check",
                Profile = ContextProfile.Compatability, Flags = ContextFlags.Default,
                APIVersion = new Version(3, 2), StartVisible = false
            })
        {
            _play = play;
            _seconds = seconds;
            _shotDirectory = shotDirectory;
            _spectateAt = spectateAt;
            _rejoinAt = rejoinAt;
            _scene = new Scene(preserveNicknames: NetSession.Active) { Services = new ClientSceneServices() };
            _ = new ScenePresentation(_scene, Size, KeyboardState, MouseState, _ => { }, Close);
            play.BuildPlayers(_scene, hunter, 0);
            _scene.AddRoom(play.Client.Accepted.Room, play.Client.Accepted.Mode,
                playerCount: NetConfig.RoomPlayerCount);
            play.ScriptInput = Drive;
        }

        private void Drive(PlayerEntity player, uint tick)
        {
            double elapsed = _clock.Elapsed.TotalSeconds;
            if (_spectateAt >= 0 && !_spectateRequested && elapsed >= _spectateAt)
            {
                SpectatorMode.Start();
                _spectateRequested = true;
            }
            if (_rejoinAt >= 0 && !_rejoinRequested && elapsed >= _rejoinAt)
            {
                SpectatorMode.Rejoin();
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

        protected override void OnLoad()
        {
            ScenePresentation.Get(_scene).OnLoad();
            GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
            ScenePresentation.Get(_scene).OnResize();
            _clock.Start();
            base.OnLoad();
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            ScenePresentation.Get(_scene).OnUpdateFrame();
            if (ScenePresentation.Get(_scene).OnRenderFrame())
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
                if (_frames % 120 == 0)
                {
                    _lit |= ScreenCapture.NonBlackFraction(_scene) > 0.01;
                    if (_shotDirectory != null)
                    {
                        ScreenCapture.Save(_scene, Path.Combine(_shotDirectory,
                            $"slot{_play.LocalSlot}-{_shots++:000}.png"));
                    }
                }
                SwapBuffers();
                ScenePresentation.Get(_scene).OnFramePresented();
                ScenePresentation.Get(_scene).AfterRenderFrame();
            }
            base.OnRenderFrame(args);
            // A duration boundary may land during a legitimate map load.
            // Finish once that match has a complete state, with a bounded grace.
            if (_clock.Elapsed.TotalSeconds >= _seconds
                && (_play.Client.State == NetConnectionState.Playing && _play.HasWorldState
                    || _clock.Elapsed.TotalSeconds >= _seconds + 10)) { Close(); }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs args)
        {
            ScenePresentation.Get(_scene).DoCleanup();
            base.OnClosing(args);
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
            bool recordDemo = false, double spectateAt = -1, double rejoinAt = -1)
        {
            if (!Double.IsFinite(seconds) || seconds < 10 || seconds > 300) { return 2; }
            hunter = Launcher.Hunters.Resolve(hunter);
            using var play = new AuthoritativePlay(host, port, name, hunter);
            play.Join();
            if (shotDirectory != null) { Directory.CreateDirectory(shotDirectory); }
            if (recordDemo && !DemoRecorder.Start()) { throw new ProgramException("Could not start demo recording."); }
            try
            {
                using var window = new AuthoritativeCheck(play, hunter, seconds,
                    shotDirectory, width, height, spectateAt, rejoinAt);
                window.Run();
                return window.Report();
            }
            finally { if (recordDemo) { DemoRecorder.Stop(); } }
        }
    }
}
