using System;
using System.Collections.Generic;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest
{
    /// <summary>Run in its own process with retail content; no renderer or graphics device.</summary>
    internal static class BombPoolCheck
    {
        public static int Run(string[] args)
        {
            if (args.Length is < 2 or > 3)
            {
                Console.Error.WriteLine("Usage: nettest --bomb-pool DATA_DIRECTORY [VERSION]");
                return 2;
            }
            try
            {
                ServerContent.Open(args[1], args.Length > 2 ? args[2] : "AMHE1");
                using var simulation = new ServerSimulation(new RotationEntry { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle });
                Scene scene = simulation.Scene;
                PlayerEntity owner = PlayerEntity.Players[0];
                owner.ServerActivate(100, Hunter.Samus, 0);
                PlayerEntity.PlayerCount = 1;
                var firstGeneration = new HashSet<BombEntity>();
                uint tick = 0;
                for (int generation = 0; generation < 2; generation++)
                {
                    var active = new HashSet<BombEntity>();
                    using (simulation.Combat.Enter(tick))
                    {
                        for (int i = 0; i < 32; i++)
                        {
                            BombEntity? bomb = BombEntity.Spawn(owner, owner.Transform, scene);
                            Require(bomb != null, $"Pool exhausted after {i} bombs in generation {generation}.");
                            Require(active.Add(bomb!), "Pool returned an already active object.");
                            if (generation == 0) firstGeneration.Add(bomb!);
                            else Require(firstGeneration.Contains(bomb!), "Pool allocated a replacement instead of reusing an expired bomb.");
                            Require(bomb!.Initialized && bomb.BombType == BombType.MorphBall && bomb.Countdown == 86,
                                "Spawn did not initialize the normal bomb lifetime.");
                            Require(bomb.Effect == null && bomb.GetModels().Count == 0, "Headless morph bomb allocated presentation resources.");
                            bomb.NodeRef = owner.NodeRef;
                        }
                        Require(BombEntity.Spawn(owner, owner.Transform, scene) == null, "Pool exceeded its existing capacity of 32.");
                    }
                    Require(Count(scene) == 32, "Spawned bombs missing from scene.");
                    for (int frame = 0; frame < 87; frame++)
                    {
                        using var scope = simulation.Combat.Enter(++tick);
                        owner.ApplyNetworkInput(new(tick, tick, tick, 0, 0, -Vector3.UnitZ, InputCommand.NoWeapon));
                        scene.StepHeadlessFrame(advanceMatch: false);
                        if (frame < 85) Require(Count(scene) == 32, "Bomb disappeared before its normal fuse expired.");
                    }
                    Require(Count(scene) == 0, "Expired bombs remain in the active entity list.");
                    foreach (BombEntity bomb in active) Require(bomb.Owner == null, "Recycled bomb retains its owner.");
                    Console.WriteLine($"BOMBPOOL generation={generation} spawned=32 expired=32 reuse={(generation == 1 ? 32 : 0)} PASS");
                }
                Console.WriteLine($"BOMBPOOL PASS ticks={tick} objects={firstGeneration.Count} capacity=32");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine("BOMBPOOL FAIL " + error); return 1; }
        }
        private static int Count(Scene scene)
        {
            int count = 0;
            foreach (BombEntity bomb in scene.GetBombEntities()) count++;
            return count;
        }
        private static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }
    }
}
