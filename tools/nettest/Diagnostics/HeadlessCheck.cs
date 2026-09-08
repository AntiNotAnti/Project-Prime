using System;
using System.Diagnostics;
using System.Threading;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.NetTest;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    public static class HeadlessCheck
    {
        public static int Run(string? data, string version, string room, int frames, int players,
            GameMode mode = GameMode.Battle, bool realtime = false)
        {
            if (data == null || frames < -1 || frames == 0 || frames > 216000 || players < 0 || players > 8
                || mode < GameMode.Battle || mode > GameMode.PrimeHunter || (frames == -1 && !realtime))
            {
                Console.WriteLine("Usage: -headlesscheck ROOM -data DIRECTORY -dataversion AMHE1 "
                    + "-frames 600 -players 8");
                return 2;
            }
            Scene? scene = null;
            try
            {
                ServerContent.Open(data, version);
                long loadStart = Stopwatch.GetTimestamp();
                scene = Scene.CreateHeadless();
                scene.LoadServerRoom(room, mode, players, bots: true);
                double loadMs = Stopwatch.GetElapsedTime(loadStart).TotalMilliseconds;
                var duration = new NetSample();
                var scheduler = new FixedTickScheduler();
                using var cancellation = new CancellationTokenSource();
                using var signals = new ConsoleShutdown(cancellation);
                int spawned = 0;
                int frame = 0;
                Console.WriteLine($"[headless] loaded {room} mode={mode}, {players} bots, realtime={realtime}");
                while (!cancellation.IsCancellationRequested && (frames == -1 || frame < frames))
                {
                    int due = realtime ? scheduler.TakeDue(Stopwatch.GetTimestamp()) : 1;
                    if (due == 0)
                    {
                        scheduler.Wait();
                        continue;
                    }
                    for (int step = 0; step < due && (frames == -1 || frame < frames); step++)
                    {
                        long start = Stopwatch.GetTimestamp();
                        scene.StepHeadlessFrame();
                        duration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                        foreach (PlayerEntity player in scene.GetPlayerEntities())
                        {
                            Vector3 position = player.Position;
                            if (!Single.IsFinite(position.X) || !Single.IsFinite(position.Y) || !Single.IsFinite(position.Z))
                            {
                                throw new ProgramException($"Non-finite player {player.SlotIndex} at frame {frame}.");
                            }
                            if (player.Health > 0) { spawned |= 1 << player.SlotIndex; }
                        }
                        frame++;
                    }
                }
                Console.WriteLine(FormattableString.Invariant($"[headless] room={room} frames={frame} players={players} spawned=0x{spawned:X2} loadMs={loadMs:F2} tickMeanMs={duration.Mean:F3} tickWorstMs={duration.Max:F3} driftMeanMs={scheduler.DriftMs.Mean:F3} driftWorstMs={scheduler.DriftMs.Max:F3} catchUp={scheduler.CatchUpTicks} dropped={scheduler.DroppedTicks} matchState={scene.Match.LegacyState}"));
                foreach (PlayerEntity player in scene.GetPlayerEntities())
                {
                    Console.WriteLine(FormattableString.Invariant($"[headless] slot={player.SlotIndex} pos={player.Position.X:F3},{player.Position.Y:F3},{player.Position.Z:F3} health={player.Health} kills={scene.Match.Players[player.SlotIndex].Kills} deaths={scene.Match.Players[player.SlotIndex].Deaths}"));
                }
                if (mode == GameMode.Nodes && players == 8)
                {
                    VerifyNodeSlots(scene);
                }
                return spawned == (1 << players) - 1 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[headless] " + ex);
                return 1;
            }
            finally
            {
                scene?.CloseHeadless();
            }
        }

        private static void VerifyNodeSlots(Scene scene)
        {
            foreach (NodeDefenseEntity node in scene.GetNodeDefenseEntities())
            {
                CollisionVolume volume = node.Volume;
                Vector3 center = volume.Type switch
                {
                    VolumeType.Sphere => volume.SpherePosition,
                    VolumeType.Cylinder => volume.CylinderPosition + volume.CylinderVector * volume.CylinderDot / 2,
                    _ => volume.BoxPosition + (volume.BoxVector1 * volume.BoxDot1
                        + volume.BoxVector2 * volume.BoxDot2 + volume.BoxVector3 * volume.BoxDot3) / 2
                };
                foreach (int slot in new[] { 4, 7 })
                {
                    foreach (PlayerEntity other in scene.GetPlayerEntities()) { other.Health = 0; }
                    node.Process();
                    PlayerEntity player = scene.Players[slot];
                    Vector3 old = player.Position;
                    player.Position = center - (player.Volume.SpherePosition - old);
                    player.ModRefreshNodeRef(old);
                    player.Health = 99;
                    for (int step = 0; step < 602; step++) { node.Process(); }
                    if (node.CurrentTeam != slot || node.CapturedPlayer != player)
                    {
                        throw new ProgramException($"Node {node.Id} did not attribute capture to slot {slot}.");
                    }
                    player.Health = 0;
                    node.Process();
                    if (node.IsOccupied)
                    {
                        throw new ProgramException($"Node {node.Id} retained stale occupancy for slot {slot}.");
                    }
                }
                Console.WriteLine($"[headless] node {node.Id}: slots 4 and 7 capture/clear PASS");
                return;
            }
            throw new ProgramException("Nodes mode loaded no defense node for the capture check.");
        }
    }
}
