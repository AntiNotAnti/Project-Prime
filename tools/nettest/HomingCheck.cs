using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest
{
    /// <summary>Real Spawn and Process physics with a deterministic moving target; no renderer or sockets.</summary>
    internal static class HomingCheck
    {
        private const uint Start = 100, Now = 109;
        private static readonly Vector3 Origin = new(0, 500, 0);

        public static int Run(string[] args)
        {
            if (args.Length is < 2 or > 3) { Console.Error.WriteLine("Usage: nettest --homing DATA [VERSION]"); return 2; }
            try
            {
                ServerContent.Open(args[1], args.Length == 3 ? args[2] : "AMHE1");
                using var simulation = new ServerSimulation(new RotationEntry { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle });
                Scene scene = simulation.Scene;
                var owner = PlayerEntity.Players[0];
                var target = PlayerEntity.Players[1];
                owner.ServerActivate(100, Hunter.Samus, 0);
                target.ServerActivate(200, Hunter.Kanden, 1);
                PlayerEntity.PlayerCount = 2;
                scene.StepHeadlessFrame(false);
                target.Health = 100;
                foreach (var variant in new (int Index, ushort Charge)[] { (9, 37), (9, 48), (9, 60), (10, 120), (11, 90) })
                    Progression(scene, owner, target, variant.Index, variant.Charge);
                Acquisition(scene, owner, target);
                HistoricalHit(scene, owner, target);
                Steering(scene, owner, target);
                ZeroRewind(scene, owner, target);
                TargetPoints(scene, target);
                RetractedTurret(scene, owner, target);
                UncompensatedSources(scene, owner, target);
                Console.WriteLine("HOMING PASS variants=5 acquisition=historical steering=historical identity=bound zeroRewind=ordinary");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine("HOMING FAIL " + error); return 1; }
        }

        private static EquipInfo Equipment(Scene scene, int index = 9, ushort charge = 60)
            => new(Weapons.WeaponsMP[index], [new BeamProjectileEntity(scene)]) { InfiniteAmmo = true, ChargeLevel = charge };

        private static BeamProjectileEntity Spawn(Scene scene, PlayerEntity owner, EquipInfo equip, ServerCombat combat, uint tick, uint view)
        {
            using var scope = combat.Enter(tick);
            combat.SetCommand(owner.SlotIndex, new(1, tick, view, 0, 0, Vector3.UnitZ, InputCommand.NoWeapon), 150);
            BeamProjectileEntity.Spawn(owner, equip, Origin, Vector3.UnitZ, BeamSpawnFlags.NoMuzzle, owner.NodeRef, scene);
            return equip.Beams[0];
        }

        private static Vector3 TargetPosition(uint tick) => new(1 + (tick - Start) * .12f, 500, 20);

        private static void Record(ServerCombat combat, PlayerEntity target, uint tick)
        {
            var actor = target.ServerCombatIdentity;
            combat.History.Record(tick, target, actor.ConnectionId, actor.Life);
        }

        private static void Clean(Scene scene, params EquipInfo[] equipment)
        {
            foreach (var equip in equipment)
                foreach (var beam in equip.Beams) { beam.Destroy(); scene.RemoveEntity(beam); }
        }

        private static void Progression(Scene scene, PlayerEntity owner, PlayerEntity target, int index, ushort charge)
        {
            var control = new ServerCombat(projectileCatchUpEnabled: false);
            var delayed = new ServerCombat();
            var controlEquip = Equipment(scene, index, charge);
            var delayedEquip = Equipment(scene, index, charge);
            var offEquip = Equipment(scene, index, charge);
            target.Position = TargetPosition(Start);
            Record(delayed, target, Start);
            var expected = Spawn(scene, owner, controlEquip, control, Start, Start);
            Require(expected.Target == target, "Control did not acquire the moving player.");
            for (uint tick = Start + 1; tick <= Now; tick++)
            {
                target.Position = TargetPosition(tick);
                if (tick < Now) Record(delayed, target, tick);
                using var scope = control.Enter(tick);
                Require(expected.Process(), "Control expired before parity boundary.");
            }
            var actual = Spawn(scene, owner, delayedEquip, delayed, Now, Start);
            Require(actual.Target == target && actual.CatchUpPending, "Delayed homing acquisition/catch-up was not enabled.");
            Vector3 livePosition = target.Position;
            using (delayed.Enter(Now)) delayed.CatchUp.Drain();
            Require(expected.Position == actual.Position && expected.Velocity == actual.Velocity
                && expected.Age == actual.Age && expected.Lifespan == actual.Lifespan, "Historical homing trajectory differs from timely control.");
            Require(actual.Velocity.X > 0 && target.Position == livePosition, "Homing did not steer or mutated the live target.");
            Require(delayed.CatchUp.Steps == 9 && delayed.CatchUp.MaxSteps == 9 && delayed.CatchUp.QueueDrops == 0,
                "Homing catch-up exceeded or omitted its bounded interval.");
            var uncompensated = Spawn(scene, owner, offEquip, control, Now, Start);
            using (control.Enter(Now)) uncompensated.Process();
            Require(uncompensated.Position != expected.Position && uncompensated.Age == scene.FrameTime,
                "OFF control concealed delayed projectile travel.");
            target.Position = TargetPosition(Now + 1);
            using (control.Enter(Now + 1)) expected.Process();
            using (delayed.Enter(Now + 1)) actual.Process();
            Require(expected.Position == actual.Position && expected.Velocity == actual.Velocity && expected.Age == actual.Age,
                "Post-catch-up steering failed to return to the current target.");
            Console.WriteLine($"HOMING parity={index} charge={charge} steps=9 position=bitExact velocity=bitExact age=bitExact PASS");
            Clean(scene, controlEquip, delayedEquip, offEquip);
        }

        private static void Acquisition(Scene scene, PlayerEntity owner, PlayerEntity target)
        {
            foreach (string variant in new[] { "historical-only", "current-only", "missing", "wrong-life", "wrong-connection", "dead", "spectating" })
            {
                var combat = new ServerCombat();
                var equip = Equipment(scene);
                target.Position = TargetPosition(Start);
                var identity = target.ServerCombatIdentity;
                var state = LagCompensationState.Capture(target, identity.ConnectionId, identity.Life);
                state = variant switch
                {
                    "current-only" => state with { Position = Origin - Vector3.UnitZ * 20 },
                    "wrong-life" => state with { LifeId = state.LifeId + 1 },
                    "wrong-connection" => state with { ConnectionId = state.ConnectionId + 1 },
                    "dead" => state with { Alive = false },
                    "spectating" => state with { Spectating = true },
                    _ => state
                };
                if (variant != "missing") combat.History.Record(Start, state);
                if (variant == "historical-only") target.Position = Origin - Vector3.UnitZ * 20;
                var beam = Spawn(scene, owner, equip, combat, Now, Start);
                Require((beam.Target == target) == (variant == "historical-only"), "Acquisition used live or invalid historical player: " + variant);
                combat.CatchUp.Clear();
                Clean(scene, equip);
                Console.WriteLine($"HOMING acquisition={variant} PASS");
            }
        }

        private static void Steering(Scene scene, PlayerEntity owner, PlayerEntity target)
        {
            foreach (string variant in new[] { "missing", "wrong-life", "wrong-connection", "dead", "spectating", "replacement-life", "replacement-connection" })
            {
                var combat = new ServerCombat();
                var equip = Equipment(scene);
                target.Position = TargetPosition(Start);
                target.Health = 100;
                var identity = target.ServerCombatIdentity;
                var state = LagCompensationState.Capture(target, identity.ConnectionId, identity.Life);
                combat.History.Record(Start, state);
                for (uint tick = Start + 1; tick < Now; tick++)
                {
                    var value = state;
                    if (tick == Start + 1)
                        value = variant switch
                        {
                            "wrong-life" => value with { LifeId = value.LifeId + 1 },
                            "wrong-connection" => value with { ConnectionId = value.ConnectionId + 1 },
                            "dead" => value with { Alive = false },
                            "spectating" => value with { Spectating = true },
                            _ => value
                        };
                    if (tick != Start + 1 || variant != "missing") combat.History.Record(tick, value);
                }
                var beam = Spawn(scene, owner, equip, combat, Now, Start);
                Require(beam.Target == target, "Steering fixture failed historical acquisition.");
                if (variant == "replacement-life") target.Spawn(target.Position, Vector3.UnitZ, Vector3.UnitY, target.NodeRef, respawn: true);
                if (variant == "replacement-connection") target.ServerActivate(identity.ConnectionId + 1, Hunter.Kanden, 1);
                using (combat.Enter(Now)) combat.CatchUp.Drain();
                Require(beam.Target == null && beam.Velocity.X == 0, "Invalid steering state fell back to the current target: " + variant);
                Clean(scene, equip);
                target.Health = 100;
                Console.WriteLine($"HOMING steering={variant} targetLost=once PASS");
            }
        }

        private static void HistoricalHit(Scene scene, PlayerEntity owner, PlayerEntity target)
        {
            Span<CombatEvent> events = stackalloc CombatEvent[16];
            foreach (var variant in new (int Index, ushort Charge)[] { (9, 60), (10, 120), (11, 90) })
            {
                // Expire the real spawn/damage invulnerability timers through
                // ordinary processing; do not bypass the damage policy.
                for (int frame = 0; frame <= target.Values.SpawnInvulnerability * 2; frame++) scene.StepHeadlessFrame(false);
                target.Health = 100;
                var combat = new ServerCombat();
                var equip = Equipment(scene, variant.Index, variant.Charge);
                for (uint tick = Start; tick < Start + 15; tick++)
                {
                    target.Teleport(new(.5f + (tick - Start) * .03f, 499.5f, 4), Vector3.UnitZ, target.NodeRef);
                    Record(combat, target, tick);
                }
                Vector3 currentPosition = new(20, 499.5f, 4);
                target.Teleport(currentPosition, Vector3.UnitZ, target.NodeRef);
                using (combat.Enter(Start + 15))
                {
                    combat.SetCommand(0, new(1, Start + 15, Start, 0, 0, Vector3.UnitZ, InputCommand.NoWeapon), 250);
                    BeamProjectileEntity.Spawn(owner, equip, Origin, Vector3.UnitZ, BeamSpawnFlags.NoMuzzle, owner.NodeRef, scene);
                    Require(equip.Beams[0].Target == target, "Historical-hit fixture did not acquire target.");
                    combat.CatchUp.Drain();
                }
                Require(target.Health < 100 && target.Position == currentPosition && combat.CatchUp.Collisions == 1
                    && combat.CatchUp.Steps < 15, "Homing projectile failed to hit the moving historical player before the current endpoint.");
                int damageEvents = 0;
                int count = combat.CopyPending(events);
                foreach (var value in events[..count]) if (value.Kind == CombatEventKind.Damage && value.Target.Slot == target.SlotIndex) damageEvents++;
                Require(damageEvents == 1, "Historical homing impact dispatched duplicate damage.");
                Console.WriteLine($"HOMING movingHistoricalHit={variant.Index} steps={combat.CatchUp.Steps} damageEvents=1 liveTarget=unchanged PASS");
                Clean(scene, equip);
            }
        }

        private static void ZeroRewind(Scene scene, PlayerEntity owner, PlayerEntity target)
        {
            target.Position = TargetPosition(Start);
            var on = new ServerCombat();
            var off = new ServerCombat(projectileCatchUpEnabled: false);
            var onEquip = Equipment(scene);
            var offEquip = Equipment(scene);
            var actual = Spawn(scene, owner, onEquip, on, Now, Now);
            var expected = Spawn(scene, owner, offEquip, off, Now, Now);
            using (on.Enter(Now)) { actual.Process(); on.CatchUp.Drain(); }
            using (off.Enter(Now)) expected.Process();
            Require(actual.Target == target && expected.Target == target && actual.Position == expected.Position
                && actual.Velocity == expected.Velocity && actual.Age == scene.FrameTime && on.CatchUp.Steps == 0,
                "Zero rewind altered ordinary homing acquisition, steering or step count.");
            Clean(scene, onEquip, offEquip);
            Console.WriteLine("HOMING zeroRewind=on/off position=bitExact velocity=bitExact oneNormalStep=PASS");
        }

        private static void TargetPoints(Scene scene, PlayerEntity target)
        {
            var combat = new ServerCombat();
            using var scope = combat.Enter(Now);
            var identity = target.ServerCombatIdentity;
            var state = LagCompensationState.Capture(target, identity.ConnectionId, identity.Life);
            foreach (bool alt in new[] { false, true })
            {
                combat.History.Record(Start, state with { Position = Origin, AltForm = alt });
                Require(HistoricalHomingTarget.TryGet(combat, target, Start, identity, out var position, out var actor)
                    && actor == identity && position == Origin.AddY(alt ? 0 : .5f), "Historical player target point changed form offset.");
            }
            var turretPosition = Origin + Vector3.UnitX;
            combat.History.Record(Start, state with { HasHalfturret = true, HalfturretPosition = turretPosition });
            Require(HistoricalHomingTarget.TryGet(combat, target.Halfturret, Start, identity, out var turret, out _) && turret == turretPosition,
                "Historical turret point did not use its owner-bound snapshot.");
            combat.History.Record(Start, state with { HasHalfturret = false });
            Require(!HistoricalHomingTarget.TryGet(combat, target.Halfturret, Start, identity, out _, out _), "Absent historical turret was targetable.");
            Console.WriteLine("HOMING targetPoints=upright/alt/turret absentTurret=rejected PASS");
        }

        private static void UncompensatedSources(Scene scene, PlayerEntity owner, PlayerEntity target)
        {
            target.Position = TargetPosition(Start);
            foreach (bool inherited in new[] { false, true })
            {
                var combat = new ServerCombat();
                var equip = Equipment(scene);
                using var scope = combat.Enter(Now);
                EntityBase source = inherited ? owner : new WorldSource(scene);
                CombatShot? parent = inherited ? new CombatShot(owner.ServerCombatIdentity, 1, Now, Start, Start, 9,
                    LagCompensationMode.HomingProjectileCatchUp) : null;
                BeamProjectileEntity.Spawn(source, equip, Origin, Vector3.UnitZ, BeamSpawnFlags.NoMuzzle,
                    owner.NodeRef, scene, inheritedShot: parent);
                var beam = equip.Beams[0];
                Require(beam.Target == target && !beam.CatchUpPending, "Uncompensated source lost ordinary live acquisition.");
                if (inherited) Require(beam.TimingMode == LagCompensationMode.None, "Unverified homing child was enabled.");
                else Require(!beam.CombatShot.IsValid, "World source unexpectedly has player attribution.");
                beam.Process();
                Require(beam.Velocity.X > 0 && combat.History.Queries == 0, "Uncompensated source read player history for steering.");
                Clean(scene, equip);
            }
            Console.WriteLine("HOMING invalidActor=ordinary unverifiedChild=excluded historyQueries=0 PASS");
        }

        private static void RetractedTurret(Scene scene, PlayerEntity owner, PlayerEntity target)
        {
            scene.ClearMessageQueue();
            target.ServerActivate(200, Hunter.Weavel, 1);
            target.Position = Origin - Vector3.UnitZ * 20; // Only its turret is in the aiming cone.
            scene.AddEntity(target.Halfturret);
            target.Halfturret.Position = TargetPosition(Start);
            var control = new ServerCombat(projectileCatchUpEnabled: false);
            var delayed = new ServerCombat();
            var controlEquip = Equipment(scene);
            var delayedEquip = Equipment(scene);
            RecordTurret(Start);
            var expected = Spawn(scene, owner, controlEquip, control, Start, Start);
            Require(expected.Target == target.Halfturret, "Timely control did not acquire the only turret in cone.");
            for (uint tick = Start + 1; tick < Now; tick++)
            {
                target.Halfturret.Position = TargetPosition(tick);
                RecordTurret(tick);
                using var scope = control.Enter(tick);
                expected.Process();
            }
            // Reproduce the renderer removal contract: current N still has the
            // Destroyed message queued, but the turret is absent from Entities.
            scene.RemoveEntity(target.Halfturret);
            scene.SendMessage(Message.Destroyed, target.Halfturret, null, 0, 0, delay: 1);
            expected.CatchUpPending = true;
            scene.StepHeadlessFrame(false);
            expected.CatchUpPending = false;
            Require(System.Linq.Enumerable.Any(scene.MessageQueue, message => message.Message == Message.Destroyed
                && message.Sender == target.Halfturret && message.ExecuteFrame == scene.FrameCount),
                "Fixture did not retain the current-frame Destroyed message.");
            target.Position = Origin - Vector3.UnitZ * 20;
            using (control.Enter(Now)) expected.Process();
            var actual = Spawn(scene, owner, delayedEquip, delayed, Now, Start);
            Require(actual.Target == target.Halfturret, "Historical turret absent from current Entities was not acquired.");
            using (delayed.Enter(Now)) delayed.CatchUp.Drain();
            Require(actual.Target == null && expected.Target == null && actual.Position == expected.Position
                && actual.Velocity == expected.Velocity && actual.Age == expected.Age,
                "Current Destroyed message prematurely ended historical turret steering.");
            Require(actual.Velocity.X > 0 && delayed.CatchUp.Steps == 9,
                "Retracted turret did not receive its complete historical steering interval.");
            Clean(scene, controlEquip, delayedEquip);
            scene.ClearMessageQueue();
            Console.WriteLine("HOMING retractedTurret=historical-acquisition currentDestroyed=deferred position=bitExact velocity=bitExact PASS");

            void RecordTurret(uint tick)
            {
                var identity = target.ServerCombatIdentity;
                // Prescribe historical presence alongside the fixture's target
                // motion; current owner state deliberately has no turret flag.
                delayed.History.Record(tick, LagCompensationState.Capture(target, identity.ConnectionId, identity.Life)
                    with { HasHalfturret = true });
            }
        }

        private sealed class WorldSource : EntityBase
        {
            internal WorldSource(Scene scene) : base(EntityType.Object, scene) { }
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
