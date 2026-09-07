using System;
using System.Diagnostics;
using MphRead.Entities;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace MphRead.Mods.Network
{
    /// <summary>Bounded rendered recording check through the shipping passive playback hooks.</summary>
    public sealed class DemoPlaybackCheck : GameWindow
    {
        private readonly Scene _scene;
        private readonly double _seconds;
        private readonly Stopwatch _clock = new();
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

        private DemoPlaybackCheck(double seconds) : base(new GameWindowSettings { UpdateFrequency = 60 },
            new NativeWindowSettings
            {
                ClientSize = new Vector2i(320, 180), Title = "Fruity-Prime demo playback check",
                Profile = ContextProfile.Compatability, Flags = ContextFlags.Default,
                APIVersion = new Version(3, 2), StartVisible = false
            })
        {
            _seconds = seconds;
            _scene = new Scene(Size, KeyboardState, MouseState, _ => { }, Close);
            PlayerEntity.MaxPlayers = PlayerEntity.SlotCapacity;
            NetLaunch.BuildPlayers(_scene, Hunter.Samus, 0, localSlot: -1);
            var room = NetLaunch.ServerRoom() ?? throw new ProgramException("Demo has no room.");
            _scene.AddRoom(room.RoomKey, room.Mode, playerCount: NetLaunch.RoomPlayerCount);
        }

        protected override void OnLoad()
        {
            _scene.OnLoad();
            GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
            _scene.OnResize();
            _clock.Start();
            base.OnLoad();
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            GameState.ApplyPause();
            _scene.OnUpdateFrame();
            if (_scene.OnRenderFrame())
            {
                _frames++;
                ModernDemoState state = DemoPlayback.Modern;
                if (_match != state.Match.MatchId)
                {
                    _match = state.Match.MatchId;
                    Array.Clear(_seen); Array.Clear(_deaths);
                }
                foreach (SnapshotPlayer source in state.Players)
                {
                    PlayerEntity player = PlayerEntity.Players[source.Slot];
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
                if (_frames % 120 == 0) { _lit |= ScreenCapture.NonBlackFraction(_scene) > 0.01; }
                SwapBuffers();
                _scene.AfterRenderFrame();
                if (DemoPlayback.AtEnd) { _endFrames++; }
            }
            base.OnRenderFrame(args);
            if (_clock.Elapsed.TotalSeconds >= _seconds || _endFrames >= 60) { Close(); }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs args)
        {
            _scene.DoCleanup();
            base.OnClosing(args);
        }

        private int Report()
        {
            ModernDemoState state = DemoPlayback.Modern;
            int moving = 0;
            for (int slot = 0; slot < 8; slot++) { if (_travel[slot] > 1) { moving++; } }
            bool passed = _frames >= 300 && _lit && moving > 0 && state.SnapshotsReceived >= 300
                && state.WorldApplications > 0 && _stateMismatches == 0
                && AuthoritativePlay.Current == null && NetSession.LocalSlot == -1;
            Console.WriteLine($"DEMOCHECK frames={_frames} snapshots={state.SnapshotsReceived} "
                + $"moving={moving} lit={_lit} atEnd={DemoPlayback.AtEnd} matches={state.MatchesLoaded} "
                + $"worldApplications={state.WorldApplications} combatEvents={state.CombatEventsReceived} "
                + $"damageEvents={state.DamageEventsReceived} deaths={_deathsObserved} "
                + $"stateMismatches={_stateMismatches} result={(passed ? "PASS" : "FAIL")}");
            return passed ? 0 : 1;
        }

        public static int Run(string path, double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 10 || seconds > 300) { return 2; }
            if (!DemoPlayback.Join(path)) { Console.Error.WriteLine(DemoPlayback.LastError); return 1; }
            try
            {
                if (!DemoPlayback.IsModern)
                { Console.Error.WriteLine("Rendered demo check requires an authoritative recording."); return 2; }
                using var window = new DemoPlaybackCheck(seconds);
                window.Run();
                return window.Report();
            }
            finally { DemoPlayback.Stop(); }
        }
    }
}
