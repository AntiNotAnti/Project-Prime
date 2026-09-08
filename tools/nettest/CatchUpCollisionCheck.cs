using System;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest
{
    /// <summary>Retail geometry and ordinary swept collision decide every hit in this fixture.</summary>
    internal static class CatchUpCollisionCheck
    {
        private const uint Tick = 100, ActionTick = 85;

        public static void RunCases(string data, string version = "AMHE1", string room = "MP1 SANCTORUS")
        {
            ServerContent.Open(data, version);
            foreach (string name in new[] { "moving-history", "current-only-miss", "wall-before-player",
                "player-before-wall", "replacement-life", "replacement-connection", "replacement-current-endpoint",
                "single-kill", "historical-splash", "splash-wall-los" })
            {
                Scene scene = Scene.CreateHeadless();
                try { scene.LoadServerRoom(room, GameMode.Battle, players: 2); Run(scene, name); }
                finally { scene.CloseHeadless(); }
            }
            foreach (bool later in new[] { false, true })
            {
                Scene scene = Scene.CreateHeadless();
                try { scene.LoadServerRoom(room, GameMode.Battle, players: 2); RunChild(scene, later); }
                finally { scene.CloseHeadless(); }
            }
        }

        private static void RunChild(Scene scene, bool later)
        {
            PlayerEntity shooter = scene.Players[0], target = scene.Players[1];
            shooter.ServerActivate(100, Hunter.Samus, 0);
            target.ServerActivate(200, Hunter.Kanden, 1);
            scene.Players.ActiveCount = 2;
            ExpireSpawnProtection(scene, target);
            Vector3 direction = FindRay(scene, target.Position.AddY(0.5f), wall: true, out Vector3 wall);
            Vector3 origin = wall - direction * 2;
            Vector3 side = Vector3.Cross(direction, Vector3.UnitY);
            foreach (PlayerEntity player in new[] { shooter, target })
                player.Teleport(origin + side * (5 + player.SlotIndex * 2), -direction, scene.GetNodeRefByPosition(origin));
            // One physical slot forces the real child Spawn to reuse the parent while its collision stack is active.
            var beam = new BeamProjectileEntity(scene);
            var impactObserver = new ImpactObserver(scene);
            var equip = new EquipInfo(Weapons.Current[(int)BeamType.Judicator], new[] { beam }) { InfiniteAmmo = true };
            var combat = new ServerCombat();
            for (uint tick = ActionTick; tick < Tick; tick++)
                foreach (PlayerEntity player in new[] { shooter, target })
                {
                    CombatActor actor = player.ServerCombatIdentity;
                    combat.History.Record(tick, player, actor.ConnectionId, actor.Life);
                }
            using (var services = new CombatSceneScope(scene, combat))
            combat.BeginTick(Tick);
            {
                combat.SetCommand(0, new(1, Tick, later ? Tick - 1 : ActionTick, 0, 0, direction, (byte)BeamType.Judicator), 250);
                BeamProjectileEntity.Spawn(shooter, equip, origin, direction,
                    BeamSpawnFlags.NoMuzzle, scene.GetNodeRefByPosition(origin), scene);
                beam.Owner = impactObserver;
                scene.StepHeadlessFrame(advanceMatch: false);
                combat.CatchUp.Drain();
            }
            if (later)
            {
                Require(beam.Generation == 1 && combat.CatchUp.Steps == 1, "Later-child parent collided before its ordinary frame.");
                combat.BeginTick(Tick + 1);
                {
                    scene.StepHeadlessFrame(advanceMatch: false);
                    Require(combat.CatchUp.Pending == 0, "A child born in ordinary simulation restarted historical catch-up.");
                    combat.CatchUp.Drain();
                }
                Require(combat.CatchUp.Steps == 1 && beam.Age <= 1 / 60f + 0.00001f,
                    "A child born after catch-up received historical steps.");
            }
            else
            {
                Require(combat.CatchUp.ProjectilesCaughtUp == 2 && combat.CatchUp.Steps == 15,
                    "The recycled child did not receive exactly the parent's remaining interval.");
                Require(beam.Age > 0 && beam.Age < 15 / 60f, "Catch-up child age includes the parent's elapsed steps.");
                Require(combat.CatchUp.Collisions == 1, "Recycled parent collision was lost or counted more than once.");
            }
            Require(beam.Generation == 2 && beam.RicochetWeapon == null && beam.Lifespan > 0
                && !beam.Flags.TestFlag(BeamFlags.Collided), "Parent stack corrupted the recycled child.");
            Require(combat.ShotsConsidered == 1 && combat.CatchUp.QueueDrops == 0, "Child inflated root timing or queue metrics.");
            int impacts = impactObserver.Count;
            Require(impacts == 1 && impactObserver.Generation == 1,
                "Parent impact must dispatch exactly once before slot reuse.");
            Require(combat.CatchUp.Pending == 0 && beam.CombatShot.CommandSequence == 1
                && beam.CombatShot.ActionServerTick == (later ? Tick - 1 : ActionTick), "Child lost immutable timing or remained queued.");
            Console.WriteLine($"CATCHUP-CHILD later={later} generation={beam.Generation} age={beam.Age:F6} steps={combat.CatchUp.Steps} collisions={combat.CatchUp.Collisions} impacts={impacts} PASS");
        }

        private sealed class ImpactObserver : EntityBase
        {
            public int Count { get; private set; }
            public uint Generation { get; private set; }
            public ImpactObserver(Scene scene) : base(EntityType.Object, scene) { }
            public override void HandleMessage(MessageInfo info)
            {
                if (info.Message == Message.Impact)
                {
                    Count++;
                    Generation = ((BeamProjectileEntity)info.Sender).Generation;
                }
            }
        }

        private static void Run(Scene scene, string name)
        {
            PlayerEntity shooter = scene.Players[0], target = scene.Players[1];
            shooter.ServerActivate(100, Hunter.Samus, 0);
            target.ServerActivate(200, Hunter.Kanden, 1);
            scene.Players.ActiveCount = 2;
            // Let ordinary player processing expire spawn protection; collision must not bypass invulnerability.
            shooter.Spawn(shooter.Position, shooter.FacingVector, Vector3.UnitY, shooter.NodeRef, respawn: false);
            target.Spawn(target.Position, target.FacingVector, Vector3.UnitY, target.NodeRef, respawn: false);
            ExpireSpawnProtection(scene, target);
            Vector3 origin = target.Position.AddY(0.5f);
            bool splash = name is "historical-splash" or "splash-wall-los";
            bool wallCase = splash || name is "wall-before-player" or "player-before-wall";
            Vector3 direction = FindRay(scene, origin, wallCase, out Vector3 endpoint);
            if (wallCase) origin = endpoint - direction * 2;
            Vector3 historical = (wallCase ? endpoint + direction * (name == "wall-before-player" ? 1 : -0.8f)
                : origin + direction * 2).AddY(-0.5f);
            Vector3 side = Vector3.Cross(direction, Vector3.UnitY);
            if (name == "replacement-current-endpoint") historical = (origin + direction * 3.5f).AddY(-0.5f);
            if (splash) historical = (endpoint + direction * (name == "historical-splash" ? -0.3f : 0.8f) + side * 1.2f).AddY(-0.5f);
            Vector3 current = historical + side * 3;
            if (name == "current-only-miss") (historical, current) = (current, historical);
            target.Teleport(historical, -direction, scene.GetNodeRefByPosition(historical));
            shooter.Teleport(origin.AddY(-0.5f), direction, scene.GetNodeRefByPosition(origin));
            if (name == "single-kill") target.Health = 1;
            var combat = new ServerCombat();
            CombatActor identity = target.ServerCombatIdentity;
            for (uint tick = ActionTick; tick < Tick; tick++)
            {
                LagCompensationState state = LagCompensationState.Capture(target, identity.ConnectionId, identity.Life);
                if (name == "moving-history")
                {
                    Vector3 offset = side * ((int)tick - (int)ActionTick - 1) * 0.2f;
                    state = state with { Position = state.Position + offset, SpherePosition = state.SpherePosition + offset };
                }
                combat.History.Record(tick, state);
                CombatActor source = shooter.ServerCombatIdentity;
                combat.History.Record(tick, shooter, source.ConnectionId, source.Life);
            }
            if (name == "replacement-life")
                target.Spawn(historical, -direction, Vector3.UnitY, target.NodeRef, respawn: false);
            if (name is "replacement-connection" or "replacement-current-endpoint")
            {
                target.ServerActivate(201, Hunter.Kanden, 1);
                target.Spawn(historical, -direction, Vector3.UnitY, target.NodeRef, respawn: false);
            }
            if (name is "replacement-life" or "replacement-connection" or "replacement-current-endpoint") ExpireSpawnProtection(scene, target);
            // Replacement remains on the old ray: falling back to its live body would be observable.
            if (name is "replacement-life" or "replacement-connection" or "replacement-current-endpoint") current = historical;
            target.Teleport(current, -direction, scene.GetNodeRefByPosition(current));
            int initialHealth = target.Health;
            BeamType weapon = splash ? BeamType.Magmaul : BeamType.PowerBeam;
            var equip = new EquipInfo(Weapons.Current[(int)weapon], shooter.EquipInfo.Beams) { InfiniteAmmo = true };
            if (splash) equip.ChargeLevel = (ushort)(equip.Weapon.FullCharge * 2);
            using (var services = new CombatSceneScope(scene, combat))
            combat.BeginTick(Tick);
            {
                combat.SetCommand(0, new(1, Tick, name == "replacement-current-endpoint" ? Tick - 2 : ActionTick, 0, 0, direction, (byte)weapon), 250);
                BeamProjectileEntity.Spawn(shooter, equip, origin, direction,
                    BeamSpawnFlags.NoMuzzle, shooter.NodeRef, scene);
                Require(combat.CatchUp.Pending == 1, name + ": expected one queued retail Power Beam.");
                scene.StepHeadlessFrame(advanceMatch: false);
                combat.CatchUp.Drain();
                long steps = combat.CatchUp.Steps;
                combat.CatchUp.Drain();
                Require(combat.CatchUp.Steps == steps, name + ": second drain advanced a completed shot.");
            }
            int damage = 0, deaths = 0, damageAmount = 0, shots = 0;
            Span<CombatEvent> events = stackalloc CombatEvent[CombatEventBatch.MaxCount];
            while (combat.Count > 0)
            {
                int count = combat.CopyPending(events);
                foreach (CombatEvent value in events[..count])
                {
                    if (value.Kind == CombatEventKind.Shot) shots++;
                    if (value.Target.Slot != target.SlotIndex) continue;
                    if (value.Kind == CombatEventKind.Damage) { damage++; damageAmount += value.Amount; }
                    if (value.Kind == CombatEventKind.Death) deaths++;
                }
                combat.Consume(count);
            }
            Require(shots == 1 && combat.ShotsConsidered == 1, name + ": fixture produced an extra root shot.");
            if (splash)
            {
                BeamProjectileEntity beam = equip.Beams[0];
                Require(Vector3.Distance(beam.Position, historical) < beam.SplashRadius, name + ": historical body is outside splash radius.");
                CollisionResult wall = default;
                bool blocked = CollisionDetection.CheckBetweenPoints(beam.Position, historical, TestFlags.Beams, scene, ref wall);
                Require(blocked == (name == "splash-wall-los"), name + ": expected retail splash LOS was not established.");
                Require(damageAmount == (blocked ? 0 : equip.ChargedSplashDamage), name + ": damage was not exactly the retail splash amount.");
            }
            if (name == "replacement-current-endpoint")
                Require(combat.History.Missing > 0 && combat.CatchUp.Steps == 2, name + ": did not test a historical miss before the current endpoint.");
            bool hit = name is "moving-history" or "player-before-wall" or "single-kill" or "replacement-current-endpoint" or "historical-splash";
            Require(damage == (hit ? 1 : 0), $"{name}: expected {(hit ? 1 : 0)} damage event, got {damage}.");
            Require(deaths == (name == "single-kill" ? 1 : 0), name + ": unexpected death event count.");
            Require(hit ? target.Health < initialHealth : target.Health == initialHealth, name + ": health disagrees with collision.");
            Require(combat.CatchUp.QueueDrops == 0 && combat.CatchUp.Pending == 0, name + ": incomplete catch-up queue.");
            Console.WriteLine($"CATCHUP-COLLISION case={name} damage={damage} deaths={deaths} steps={combat.CatchUp.Steps} history={combat.History.Queries} PASS");
        }

        private static void ExpireSpawnProtection(Scene scene, PlayerEntity target)
        {
            for (int frame = 0; frame <= target.Values.SpawnInvulnerability * 2; frame++)
                scene.StepHeadlessFrame(advanceMatch: false);
        }

        private static Vector3 FindRay(Scene scene, Vector3 origin, bool wall, out Vector3 endpoint)
        {
            for (int bearing = 0; bearing < 64; bearing++)
            {
                float angle = bearing * MathF.PI / 32;
                Vector3 direction = new(MathF.Sin(angle), 0, MathF.Cos(angle));
                CollisionResult collision = default;
                endpoint = origin + direction * 8;
                bool blocked = CollisionDetection.CheckBetweenPoints(origin, endpoint, TestFlags.Beams, scene, ref collision);
                if (!wall && !blocked) return direction;
                if (wall && blocked && Vector3.Distance(origin, collision.Position) > 1
                    && MathF.Abs(collision.Plane.Y) < 0.1f
                    && MathF.Abs(Vector3.Dot(collision.Plane.Xyz, direction)) > 0.9f)
                {
                    endpoint = collision.Position;
                    return direction;
                }
            }
            throw new InvalidOperationException("No suitable retail collision ray for catch-up fixture.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
