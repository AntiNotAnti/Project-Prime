using System;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest
{
    internal static class CatchUpCheck
    {
        public static int Run(string[] args)
        {
            if (args.Length is < 2 or > 3) { Console.Error.WriteLine("Usage: nettest --catch-up DATA [VERSION]"); return 2; }
            try
            {
                ServerContent.Open(args[1], args.Length == 3 ? args[2] : "AMHE1");
                using (var simulation = new ServerSimulation(new RotationEntry { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle }))
                {
                    Scene scene = simulation.Scene;
                    // This fixture drives the scene directly instead of the
                    // lifecycle owner, so opt into the phase where gameplay is
                    // legal before exercising projectile timing.
                    scene.Match.Phase = MatchPhase.Playing;
                    PlayerEntity owner = scene.Players[0];
                    owner.ServerActivate(100, Hunter.Samus, 0);
                    scene.Players.ActiveCount = 1;
                    // Establish the ordinary engine frame duration; fixtures then
                    // spawn the control AFTER completed T, first advancing T+1.
                    scene.StepHeadlessFrame(advanceMatch: false);
                    foreach (BeamType type in new[] { BeamType.PowerBeam, BeamType.VoltDriver, BeamType.Missile,
                        BeamType.Battlehammer, BeamType.Judicator, BeamType.Magmaul, BeamType.OmegaCannon })
                        Progression(scene, owner, type);
                    Progression(scene, owner, BeamType.Judicator, charged: true);
                    ZeroRewind(scene, owner);
                    ExpiryAndRecycling(scene, owner);
                    MaxBudget(scene, owner);
                    QueueBound(scene, owner);
                }
                CatchUpCollisionCheck.RunCases(args[1], args.Length == 3 ? args[2] : "AMHE1");
                Console.WriteLine("CATCHUP PASS boundary=completed-T firstStep=T+1 maxHistoricalSteps=15");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine("CATCHUP FAIL " + error); return 1; }
        }

        private static EquipInfo Equipment(Scene scene, BeamType type, int pool = 8)
            => new(Weapons.Current[(int)type], Enumerable.Range(0, pool).Select(_ => new BeamProjectileEntity(scene)).ToArray())
            { InfiniteAmmo = true };
        private static InputCommand Command(uint tick, uint view)
            => new(tick, tick, view, InputButtons.Shoot, InputButtons.Shoot, Vector3.UnitZ, InputCommand.NoWeapon);
        private static BeamProjectileEntity Spawn(Scene scene, PlayerEntity owner, EquipInfo equip, ServerCombat combat, uint tick, uint view)
        {
            using var services = new CombatSceneScope(scene, combat);
            combat.BeginTick(tick);
            combat.SetCommand(owner.SlotIndex, Command(tick, view), 150);
            BeamProjectileEntity.Spawn(owner, equip, new Vector3(0, 500, 0), Vector3.UnitZ, BeamSpawnFlags.NoMuzzle, owner.NodeRef, scene);
            return equip.Beams.First(beam => beam.Owner == owner);
        }
        private static void Clean(Scene scene, EquipInfo equip)
        {
            foreach (BeamProjectileEntity beam in equip.Beams)
                if (beam.Owner != null) { beam.Destroy(); scene.RemoveEntity(beam); beam.Lifespan = 0; }
        }
        private static void Progression(Scene scene, PlayerEntity owner, BeamType type, bool charged = false)
        {
            var controlEquip = Equipment(scene, type);
            var delayedEquip = Equipment(scene, type);
            var uncompensatedEquip = Equipment(scene, type);
            if (charged) controlEquip.ChargeLevel = delayedEquip.ChargeLevel = uncompensatedEquip.ChargeLevel = (ushort)(controlEquip.Weapon.FullCharge * 2);
            var control = new ServerCombat(projectileCatchUpEnabled: false);
            var delayed = new ServerCombat();
            const uint start = 100, now = 109; //150ms at60Hz, measured RTT budget permits9.
            scene.Random.SetRng2(123);
            BeamProjectileEntity expected = Spawn(scene, owner, controlEquip, control, start, start);
            for (uint tick = start + 1; tick < now; tick++)
            {
                using var services = new CombatSceneScope(scene, control);
                control.BeginTick(tick);
                scene.StepHeadlessFrame(advanceMatch: false);
            }
            scene.Random.SetRng2(123);
            BeamProjectileEntity actual = Spawn(scene, owner, delayedEquip, delayed, now, start);
            scene.Random.SetRng2(123);
            var uncompensated = Spawn(scene, owner, uncompensatedEquip, control, now, start);
            using (var services = new CombatSceneScope(scene, delayed))
            delayed.BeginTick(now);
            {
                scene.StepHeadlessFrame(advanceMatch: false);
                Require(actual.Age == 0, "Pending beam received an extra scene step.");
                delayed.CatchUp.Drain();
            }
            Require(expected.Position == actual.Position && expected.Velocity == actual.Velocity && expected.Age == actual.Age
                && expected.Lifespan == actual.Lifespan, $"{type} delayed/control progression differs.");
            Require(uncompensated.Age == scene.FrameTime && uncompensated.Position != expected.Position,
                "Catch-up OFF did not expose the delayed-control progression difference.");
            int pellets = charged ? 3 : 1;
            for (int pellet = 0; pellet < pellets; pellet++)
                Require(controlEquip.Beams[pellet].Velocity == delayedEquip.Beams[pellet].Velocity
                    && controlEquip.Beams[pellet].Position == delayedEquip.Beams[pellet].Position, "Pellet progression differs.");
            Require(delayed.CatchUp.Steps == 9 * pellets && delayed.CatchUp.MaxSteps == 9, "Incorrect catch-up step count.");
            using (var services = new CombatSceneScope(scene, delayed))
            delayed.BeginTick(now + 1); scene.StepHeadlessFrame(advanceMatch: false);
            Require(expected.Position == actual.Position && expected.Age == actual.Age, "Post-catch-up normal progression differs.");
            Console.WriteLine($"CATCHUP parity={type} charged={charged} pellets={pellets} stepsPerProjectile=9 position=bitExact velocity=bitExact age=bitExact PASS");
            Clean(scene, controlEquip); Clean(scene, delayedEquip); Clean(scene, uncompensatedEquip);
        }
        private static void ZeroRewind(Scene scene, PlayerEntity owner)
        {
            foreach (bool enabled in new[] { false, true })
            {
                var combat = new ServerCombat(projectileCatchUpEnabled: enabled);
                var equip = Equipment(scene, BeamType.PowerBeam);
                var beam = Spawn(scene, owner, equip, combat, 200, 200);
                using (var services = new CombatSceneScope(scene, combat))
                combat.BeginTick(200); { scene.StepHeadlessFrame(false); combat.CatchUp.Drain(); }
                Require(beam.Age == scene.FrameTime && combat.CatchUp.Steps == 0, "Zero rewind changed the ordinary one-step spawn.");
                Clean(scene, equip);
            }
            Console.WriteLine("CATCHUP zeroRewind=on/off oneNormalStep=PASS");
        }
        private static void ExpiryAndRecycling(Scene scene, PlayerEntity owner)
        {
            var combat = new ServerCombat();
            var equip = Equipment(scene, BeamType.PowerBeam, 1);
            var beam = Spawn(scene, owner, equip, combat, 300, 285);
            uint generation = beam.Generation;
            Spawn(scene, owner, equip, combat, 300, 285);
            Require(beam.Generation != generation && combat.CatchUp.Pending == 2, "Fixture failed to recycle queued generation.");
            beam.Lifespan = scene.FrameTime * 2;
            using (var services = new CombatSceneScope(scene, combat))
            combat.BeginTick(300); { scene.StepHeadlessFrame(false); combat.CatchUp.Drain(); }
            Require(combat.CatchUp.ProjectilesCaughtUp == 1 && combat.CatchUp.Steps == 2 && combat.CatchUp.Collisions == 1,
                "Expired/recycled beam advanced or collided twice.");
            Clean(scene, equip);
            Console.WriteLine("CATCHUP expiry=2steps recycledGeneration=ignored terminalCollision=once PASS");
        }
        private static void MaxBudget(Scene scene, PlayerEntity owner)
        {
            var combat = new ServerCombat();
            var equip = Equipment(scene, BeamType.PowerBeam);
            using (var services = new CombatSceneScope(scene, combat))
            combat.BeginTick(500);
            {
                combat.SetCommand(owner.SlotIndex, Command(500, 1), 250);
                BeamProjectileEntity.Spawn(owner, equip, new Vector3(0, 500, 0), Vector3.UnitZ,
                    BeamSpawnFlags.NoMuzzle, owner.NodeRef, scene);
                scene.StepHeadlessFrame(false);
                combat.CatchUp.Drain();
            }
            Require(combat.ShotsClamped == 1 && combat.CatchUp.Steps == 15 && combat.CatchUp.MaxSteps == 15,
                "Ancient view did not clamp to exactly15 catch-up steps.");
            Clean(scene, equip);
            Console.WriteLine("CATCHUP ancientView=clamped maximum=15steps PASS");
        }
        private static void QueueBound(Scene scene, PlayerEntity owner)
        {
            var combat = new ServerCombat();
            var equip = Equipment(scene, BeamType.PowerBeam, 1);
            for (int i = 0; i <= ProjectileCatchUp.Capacity; i++) Spawn(scene, owner, equip, combat, 400, 385);
            Require(combat.CatchUp.Pending == ProjectileCatchUp.Capacity && combat.CatchUp.QueueDrops == 1, "Catch-up queue is not bounded.");
            using (var services = new CombatSceneScope(scene, combat))
            combat.BeginTick(400); { scene.StepHeadlessFrame(false); combat.CatchUp.Drain(); }
            Require(combat.CatchUp.Pending == 0 && combat.CatchUp.Steps <= ProjectileCatchUp.Capacity * 15,
                "Catch-up drain exceeded work bound.");
            Clean(scene, equip);
            Console.WriteLine("CATCHUP queueCapacity=512 overflow=observable work=bounded PASS");
        }
        private static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }
    }

    /// <summary>Temporarily routes scene-owned combat callbacks to one fixture authority.</summary>
    internal sealed class CombatSceneScope : IDisposable
    {
        private readonly Scene _scene;
        private readonly ISceneServices _previous;

        public CombatSceneScope(Scene scene, ServerCombat combat)
        {
            _scene = scene;
            _previous = scene.Services;
            scene.Services = new ServerSceneServices(combat);
        }

        public void Dispose() => _scene.Services = _previous;
    }
}
