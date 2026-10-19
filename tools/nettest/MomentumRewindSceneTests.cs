using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Actual offline rule-selected Momentum input/collision trajectories sampled into
// the existing history/query seam. Record is called explicitly by this test;
// this does NOT prove BroadcastSnapshot/ACK binding or live UDP acceptance.
internal static class MomentumRewindSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
        Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static void Near(Vector3 actual, Vector3 expected, string label)
        => Check(float.IsFinite(actual.X) && float.IsFinite(actual.Y) && float.IsFinite(actual.Z)
            && (actual - expected).Length <= .00001f, label + $": {actual} != {expected}");
    private readonly record struct Sample(uint Frame, Vector3 Position, bool AltForm);
    private static float[] V(Vector3 v) => new[] { v.X, v.Y, v.Z };

    internal static int Run(string? outputPath)
    {
        int originalWindow = NetUnlagged.MaxRewindFrames;
        bool originalEnabled = NetUnlagged.Enabled;
        var results = new List<object>();
        string? failure = null;
        try
        {
            _checks = 0;
            Check(!NetSession.Active && !DemoPlayback.IsActive, "offline non-replay history fixture");
            Check(ServerSim.Available(out string reason), reason);
            Headless.Enter(); NetUnlagged.Enabled = true;
            foreach (Hunter hunter in Hunters)
            {
                results.Add(Trajectory(hunter, morph: false));
                results.Add(Trajectory(hunter, morph: true));
            }
            Check(NetUnlagged.MaxRewindFrames == originalWindow, "test preserves existing rewind window");
            Console.WriteLine($"MOMENTUM REWIND SCENE PASS {_checks} assertions");
            return 0;
        }
        catch (Exception ex) { failure = ex.ToString(); Console.Error.WriteLine("MOMENTUM REWIND SCENE FAIL " + ex); return 1; }
        finally
        {
            NetUnlagged.Reset(); NetPlayerLifecycle.Reset(); NetUnlagged.Enabled = originalEnabled;
            if (outputPath != null)
            {
                string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { checks = _checks, failure,
                    scope = "asset-backed offline native 60Hz earned Momentum trajectory -> explicit NetUnlagged.Record -> historical geometry queries; no BroadcastSnapshot/ACK/UDP proof",
                    conditions = new { hunters = 7, simulationHz = 60, historyFrames = NetUnlagged.HistoryFrames,
                        rewindWindow = originalWindow, lifecycle = "explicit synthetic generation/life transitions through production lifecycle API; no network admission claim" }, results },
                    new JsonSerializerOptions { WriteIndented = true }));
            }
        }
    }

    private static object Trajectory(Hunter hunter, bool morph)
    {
        NetUnlagged.Reset(); NetPlayerLifecycle.Reset();
        Scene scene = MomentumSceneTests.Load(hunter, true, out var keyboard, matchRule: true);
        try
        {
            var p = scene.Players.Items[0];
            Check(scene.GameState.MovementModel == MovementModel.Momentum && scene.MomentumOwnsPlayer(p), "ordinary offline match rule owns real local player");
            var set = typeof(KeyboardState).GetMethod("SetKeyState", Hidden | BindingFlags.Public)!
                .CreateDelegate<Action<KeyboardState, Keys, bool>>();
            Sample Record()
            {
                uint frame = checked((uint)scene.FrameCount);
                NetUnlagged.Record(frame);
                Check(NetUnlagged.HistoryAvailable(frame), "actual post-simulation frame stamped in existing ring");
                Check(NetUnlagged.TryHistoricalPose(p, frame, out var pose), "recorded actual player pose query succeeds");
                Near(pose.Position, p.Position, "exact sample timing/position");
                Check(pose.AltForm == p.IsAltForm, "actual native form captured");
                return new(frame, p.Position, p.IsAltForm);
            }
            Sample previous = Record();
            var trace = new List<object>();
            bool airborne = false, nativeMorphStarted = false, boundary = false;
            int interpolated = 0, bodyHits = 0, headHits = 0, airInputDirections = 0;
            var directions = new HashSet<string>();
            long steps = scene.DeveloperMomentumSteps;
            for (int tick = 0; tick < 90; tick++)
            {
                set(keyboard, Keys.Space, tick == 0);
                set(keyboard, Keys.W, tick < 18);
                set(keyboard, Keys.D, tick is >= 18 and < 36);
                set(keyboard, Keys.S, tick is >= 36 and < 54);
                if (morph && tick == 20)
                {
                    Check(p.Speed.Y > 0 || !p.Flags1.TestFlag(PlayerFlags1.Standing), "native form request begins in actual jump flight");
                    p.ModStartFormSwitch();
                    Check(p.IsMorphing, "actual native midair morph begins"); nativeMorphStarted = true;
                }
                ulong beforeFrame = scene.FrameCount;
                scene.OnSimulationFrame();
                Check(scene.FrameCount == beforeFrame + 1, "one native fixed 60Hz simulation tick per history sample");
                Sample current = Record();
                Check(current.Frame == previous.Frame + 1, "history labels adjacent actual simulation ticks");
                bool flying = !p.Flags1.TestFlag(PlayerFlags1.Standing);
                airborne |= flying;
                if (flying && p.ModMomentumEligible)
                {
                    if (p.Controls.MoveUp.IsDown) directions.Add("forward");
                    if (p.Controls.MoveRight.IsDown) directions.Add("right");
                    if (p.Controls.MoveDown.IsDown) directions.Add("back");
                }
                Check(NetUnlagged.TryHistoricalPose(p, previous.Frame + .5, out var half), "fractional frame historical query");
                bool continuous = previous.AltForm == current.AltForm && (current.Position - previous.Position).LengthSquared <= 16;
                Vector3 expected = continuous ? Vector3.Lerp(previous.Position, current.Position, .5f) : previous.Position;
                Near(half.Position, expected, "native history fractional interpolation/discontinuity policy");
                Check(half.AltForm == previous.AltForm, "fractional historical form belongs to lower sample");
                if (previous.AltForm != current.AltForm) boundary = true;
                if (!previous.AltForm)
                {
                    Check(NetUnlagged.TryHistoricalBiped(p, previous.Frame + .5, out var biped), "historical lower biped remains queryable");
                    Near(biped, expected, "biped history agrees with existing pose interpolation");
                    var body = NetHistoricalTrace.Body(p, half);
                    Check(body.Type == HistoricalBodyType.BipedCylinder, "existing native historical biped cylinder selected");
                    float middle = (body.Bottom + body.Top) / 2;
                    Vector3 center = body.Position + Vector3.UnitY * middle;
                    Check(NetHistoricalTrace.Intersect(body, center - Vector3.UnitX * 2, center + Vector3.UnitX * 2, 0, out _), "actual historical biped body ray hits"); bodyHits++;
                    Vector3 head = body.Position + Vector3.UnitY * (body.Top - .1f);
                    var hit = NetHistoricalTrace.Trace(new[] { body }, head - Vector3.UnitX * 2, head + Vector3.UnitX * 2, 0);
                    Check(hit.Available && hit.Slot == p.SlotIndex && hit.Head, "existing historical head band ray hits selected slot"); headHits++;
                    Vector3 miss = body.Position + Vector3.UnitY * (body.Top + .2f);
                    Check(!NetHistoricalTrace.Intersect(body, miss - Vector3.UnitX * 2, miss + Vector3.UnitX * 2, 0, out _), "ray above historical top misses");
                    interpolated++;
                }
                else Check(!NetUnlagged.TryHistoricalBiped(p, previous.Frame + .5, out _), "actual alt-form lower sample refuses biped-only query");
                trace.Add(new { tick, current.Frame, position = V(current.Position), speed = V(p.Speed), current.AltForm,
                    morphing = p.IsMorphing, historicalHalf = V(half.Position) });
                previous = current;
            }
            airInputDirections = directions.Count;
            Check(airborne && scene.DeveloperMomentumSteps > steps && interpolated > 0, "actual earned Momentum jump and historical body queries activated");
            if (morph) Check(nativeMorphStarted && boundary && p.IsAltForm, "native midair form transition reaches recorded alt form without interpolation across boundary");
            else Check(airInputDirections == 3, "actual airborne Momentum input visits forward/right/back directions");
            if (!morph) TeleportAndLifecycle(scene, p, Record);
            Console.WriteLine($"MOMENTUM REWIND SCENE PASS {hunter}/{(morph ? "midair-form" : "earned-air-directions")}");
            return new { hunter = hunter.ToString(), scenario = morph ? "midair-form" : "earned-air-directions",
                interpolated, bodyHits, headHits, airInputDirections, boundary, trace };
        }
        finally { MomentumSceneTests.Release(scene); }
    }

    private static void TeleportAndLifecycle(Scene scene, PlayerEntity player, Func<Sample> record)
    {
        Sample before = record();
        // Exercise the native finalized teleport endpoint; this is explicitly
        // not a teleporter-volume or remotely accepted teleport event test.
        player.Teleport(player.Position + Vector3.UnitX * 8, player.FacingVector, player.NodeRef);
        scene.OnSimulationFrame();
        Sample after = record();
        Check(after.Frame == before.Frame + 1 && (after.Position - before.Position).Length > 4,
            "native teleport endpoint creates real adjacent-frame discontinuity");
        Check(NetUnlagged.TryHistoricalPose(player, before.Frame + .5, out var pose), "teleport lower history remains available");
        Near(pose.Position, before.Position, "existing >4-unit fence does not blend teleport path");
        Check(NetUnlagged.TryHistoricalBiped(player, before.Frame + .5, out var biped), "teleport biped lower history available");
        Near(biped, before.Position, "biped teleport fence holds lower sample");

        // These are isolated lifecycle identity fixtures through production APIs,
        // not network admission or an earned death/respawn claim.
        int slot = player.SlotIndex;
        ushort generation = NetLifecycleTracker.Next(NetPlayerLifecycle.Generation(slot));
        NetPlayerLifecycle.SetOccupant(slot, generation);
        Check(!NetUnlagged.TryHistoricalPose(player, after.Frame, out _), "occupant generation boundary retires old history");
        scene.OnSimulationFrame(); Sample waitingIdentity = record();
        ushort oldLife = NetPlayerLifecycle.Get(slot);
        ushort nextLife = NetLifecycleTracker.Next(oldLife);
        var life = new PlayerState { SlotIndex = (byte)slot, SlotGeneration = generation, LifeId = nextLife,
            Health = (ushort)player.Health, Flags = (byte)(PlayerState.FlagActive | PlayerState.FlagSpawned),
            Position = player.Position, Speed = player.Speed, Facing = player.FacingVector };
        Check(NetPlayerLifecycle.AcceptState(life, waitingIdentity.Frame + 1), "production lifecycle accepts explicit newer-life fixture");
        Check(NetUnlagged.PositionAt(slot, waitingIdentity.Frame, generation, oldLife, out _), "old-life cell exists under original explicit identity");
        Check(!NetUnlagged.PositionAt(slot, waitingIdentity.Frame, generation, nextLife, out _)
            && !NetUnlagged.TryHistoricalPose(player, waitingIdentity.Frame, out _), "new life cannot query surviving old-life geometry");
        scene.OnSimulationFrame(); Sample newLife = record();
        Check(NetUnlagged.PositionAt(slot, newLife.Frame, generation, nextLife, out _), "newly recorded actual body belongs to current life");
        NetPlayerLifecycle.SetOccupant(slot, NetLifecycleTracker.Next(generation));
        Check(!NetUnlagged.TryHistoricalPose(player, newLife.Frame, out _), "replacement generation refuses previous occupant body");
    }
}
