using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

internal static class OfflineSceneChecks
{
    internal static int Run(string data)
    {
        int checks = 0;
        void Check(bool value,string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
        Scene? scene = null; bool force = MapAudit.ForceEveryone;
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data)); Paths.UpdatePaths(); Paths.ChooseMphPath();
            Headless.Enter(); MapAudit.ForceEveryone = true;
            scene = new(new Vector2i(256,192),MphRead.Mods.Input.SyntheticInput.CreateKeyboard(),
                MphRead.Mods.Input.SyntheticInput.CreateMouse(),_=>{},()=>{});
            scene.AddPlayer(Hunter.Weavel); scene.AddPlayer(Hunter.Samus);
            scene.Players.PlayerCount = 2; scene.Players.MainPlayerIndex = 0;
            foreach (var player in scene.Players.Items) player.IsBot = false;
            scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:2,nodeLayerMask:0);
            scene.OnLoad(); scene.GameState.MatchTime = -1;
            var shooter = scene.Players.Items[1]; var victim = scene.Players.Items[0];
            void Step(int count)
            {
                for (int i=0; i<count; i++) { NetTestScript.Rest(shooter,wantBiped:true); scene.OnSimulationFrame(); }
            }
            Step(240);
            Check(shooter.Health > 0 && victim.Health > 0,"real asset-backed players spawned");
            var runtime = scene.EnableDeveloperRifle(true);
            Check(runtime.Grant(shooter),"developer M7 grants without a native slot");
            Check(runtime.State(shooter)!.Definition.Identity.Kind == WeaponIdentityKind.Custom
                && runtime.State(shooter)!.Definition.Identity == RiftlineM7.Create().Identity,"bundled reference definition has exact custom identity");
            Check(RiftlineM7.Create().Identity.DefinitionHash == "3ee854525a9737a3acdaf6f994dd8da75e229b5f87d820c96d98f17c59512228","production reference matches canonical fixture");
            BeamType original = shooter.CurrentWeapon; int nativeAmmo = shooter.EquipInfo.Ammo;
            float headHeight = Fixed.ToFloat(victim.Values.MaxPickupHeight)-.15f;
            uint sequence = 0;
            void CleanVictim(int health)
            {
                victim.Health = health;
                typeof(PlayerEntity).GetField("_spawnInvulnTimer",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(victim,(ushort)0);
                typeof(PlayerEntity).GetField("_damageInvulnTimer",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(victim,(ushort)0);
            }
            Vector3 Aim(float height) => victim.Position+new Vector3(0,height,0)-(shooter.Position+new Vector3(0,.4f,0));
            victim.ModPlaceAt(shooter.Position+new Vector3(0,0,-3)); CleanVictim(100);
            Check(runtime.Optic(shooter,1,1) == WeaponActionResult.Accepted,"scoped precision selected independently");
            uint fired = (uint)scene.FrameCount;
            Check(runtime.Fire(shooter,++sequence,Aim(.2f)) == WeaponActionResult.Accepted,"accepted body shot emits once");
            BeamProjectileEntity? firstProjectile = null;
            foreach (var beam in scene.GetBeamProjectileEntities())
                if (beam.ModCustomShot?.Key.ShotId == sequence && beam.Owner == shooter) firstProjectile = beam;
            Check(firstProjectile is { InitialSpeed:200, MaxDistance:1000, ModComponentId:1 }
                && firstProjectile.CylinderRadius == 41/4096f && firstProjectile.Lifespan == 6/60f,
                "native emitter receives exact 200-unit speed, 1000-unit range, radius, six-tick lifetime and one component");
            Step(2);
            Check(victim.Health == 35 && runtime.LastImpact is { Headshot:false, Damage:65 },"canonical collision/TakeDamage pays 65 body damage");
            Check(runtime.State(shooter)!.Snapshot().Magazine == 3 && shooter.CurrentWeapon == original
                && shooter.EquipInfo.Ammo == nativeAmmo,"custom magazine payment leaves native inventory and ammo intact");
            while ((uint)scene.FrameCount < fired+74) Step(1);
            Check(runtime.Fire(shooter,++sequence,Aim(headHeight)) == WeaponActionResult.Cooldown,"actual scene rejects early bolt fire");
            Step(1); CleanVictim(100);
            Check(runtime.Fire(shooter,++sequence,Aim(headHeight)) == WeaponActionResult.Accepted,"75-tick scene cadence boundary");
            Step(2);
            Check(victim.Health == 0 && runtime.LastImpact is { Headshot:true,Lethal:true },"lethal head hit uses canonical death and typed local attribution");
            Check(scene.GameState.BeamKills[shooter.SlotIndex,(int)BeamType.Imperialist] == 0,"M7 kill never increments Imperialist/native weapon statistics");
            Check(runtime.LastImpact!.Value.Weapon == RiftlineM7.Create().Identity,"kill fact retains custom revision, never a native alias");
            // Recreate the victim through its real spawn path, then isolate subsequent probes.
            victim.Spawn(victim.Position,Vector3.UnitZ,Vector3.UnitY,victim.NodeRef,respawn:true); CleanVictim(200);
            while ((uint)scene.FrameCount < fired+150) Step(1);
            var slots = (Array)typeof(OfflineCustomWeaponRuntime).GetField("_slots",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runtime)!;
            object slot = slots.GetValue(shooter.SlotIndex)!;
            var equip = (EquipInfo)slot.GetType().GetField("Equip",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(slot)!;
            foreach (var beam in equip.Beams) beam.Lifespan = 1;
            int magazine = runtime.State(shooter)!.Snapshot().Magazine; ulong accepted = runtime.AcceptedShots;
            Check(runtime.Fire(shooter,++sequence,Aim(.2f)) == WeaponActionResult.CapacityUnavailable
                && runtime.State(shooter)!.Snapshot().Magazine == magazine && runtime.AcceptedShots == accepted,"full private pool refuses before payment/emission/eviction");
            foreach (var beam in equip.Beams) { beam.Destroy(); scene.RemoveEntity(beam); }
            Check(runtime.Reload(shooter,1) == WeaponActionResult.Accepted,"actual scene starts reload");
            uint reloadAt = (uint)scene.FrameCount; Step(137);
            Check(runtime.State(shooter)!.Snapshot().Magazine == magazine,"scene reload cannot complete early");
            Step(1); runtime.Step();
            Check(runtime.State(shooter)!.Snapshot().Magazine == 4 && (uint)scene.FrameCount == reloadAt+138,"scene reload transfers at 138 ticks");
            Check(runtime.Optic(shooter,2,1) == WeaponActionResult.Accepted,"reload clears scope and permits explicit precision re-entry");
            // Find a real unobstructed >15-unit line without replacing room collision.
            Vector3 basePosition = shooter.Position; bool found = false; Vector3 from = default,to = default;
            foreach (int height in new[] { 0,2,5,10 })
            {
                foreach (var dir in new[] { Vector3.UnitX,-Vector3.UnitX,Vector3.UnitZ,-Vector3.UnitZ })
                {
                    var a = basePosition+new Vector3(0,height,0); var b = a+dir*20;
                    if (!BeamProjectileEntity.TryBeamObstacle(a+new Vector3(0,.4f,0),b+new Vector3(0,headHeight,0),scene,out _,out _))
                    { from=a; to=b; found=true; break; }
                }
                if (found) break;
            }
            Check(found,"native room provides measured unobstructed long-range probe");
            shooter.ModPlaceAt(from); victim.ModPlaceAt(to); CleanVictim(200);
            Check(runtime.Fire(shooter,++sequence,Aim(headHeight)) == WeaponActionResult.Accepted,"long-range precision shot"); Step(1);
            Check(victim.Health == 50 && runtime.LastImpact is { Headshot:true, Damage:150, Lethal:false },"beyond-15-unit headshot pays 150 through original head band");
            // A real world obstruction must win before any victim behind/off the ray.
            while ((uint)scene.FrameCount < runtime.State(shooter)!.Snapshot().FireReadyTick) Step(1);
            victim.ModPlaceAt(shooter.Position+new Vector3(5,0,0)); CleanVictim(200);
            var origin = shooter.Position+new Vector3(0,.4f,0);
            Check(BeamProjectileEntity.TryBeamObstacle(origin,origin-Vector3.UnitY*100,scene,out _,out _),"native floor obstacle present");
            Check(runtime.Fire(shooter,++sequence,-Vector3.UnitY) == WeaponActionResult.Accepted,"wall/floor miss is accepted fire"); Step(1);
            Check(victim.Health == 200,"occluded/missed shot cannot damage unrelated target");
            int beforeBad = runtime.State(shooter)!.Snapshot().Magazine;
            runtime.Fire(shooter,++sequence,new(float.NaN,0,0));
            Check(runtime.State(shooter)!.Snapshot().Magazine == beforeBad,"nonfinite aim cannot debit");
            void Ready()
            {
                while ((uint)scene.FrameCount < runtime.State(shooter)!.Snapshot().FireReadyTick) Step(1);
            }
            // The shared TakeDamage path remains responsible for team/protection rules.
            Ready(); victim.ModPlaceAt(shooter.Position+new Vector3(0,0,-3)); CleanVictim(200);
            scene.GameState.Teams = true; scene.GameState.FriendlyFire = false;
            int victimTeam = victim.TeamIndex; victim.TeamIndex = shooter.TeamIndex;
            Check(runtime.Fire(shooter,++sequence,Aim(.2f)) == WeaponActionResult.Accepted,"friendly-fire-off attempt emits normally");
            Step(2); Check(victim.Health == 200,"canonical team protection prevents custom damage");
            scene.GameState.Teams = false; victim.TeamIndex = victimTeam;
            Ready(); CleanVictim(200);
            typeof(PlayerEntity).GetField("_spawnInvulnTimer",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(victim,(ushort)120);
            Check(runtime.Fire(shooter,++sequence,Aim(.2f)) == WeaponActionResult.Accepted,"protected target attempt emits normally");
            Step(2); Check(victim.Health == 200,"canonical spawn protection prevents custom damage");
            Ready(); int empty = runtime.State(shooter)!.Snapshot().Magazine;
            Check(empty == 0 && runtime.Fire(shooter,++sequence,Aim(.2f)) == WeaponActionResult.Empty
                && runtime.State(shooter)!.Snapshot().Magazine == 0,"empty magazine refuses without emission or payment");
            Check(runtime.Reload(shooter,2) == WeaponActionResult.Accepted,"empty scene reload starts"); Step(138);
            // Exercise the real collision dispatch at the two unsupported/native-index seams.
            // The controlled collision result isolates routing; ordinary rays above exercised geometry.
            CleanVictim(200); victim.CreateHalfturret(); victim.Health = 1;
            victim.Halfturret.Health = 100; typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!.SetValue(victim,victim.Flags2 | PlayerFlags2.Halfturret);
            victim.ModPlaceAt(shooter.Position+new Vector3(5,0,0));
            Check(runtime.Fire(shooter,++sequence,Vector3.UnitY) == WeaponActionResult.Accepted,"turret probe owns a genuine accepted custom projectile");
            var probe = equip.Beams.Single(b=>b.ModCustomShot?.Key.ShotId == sequence);
            var collision = new CollisionResult { Position = victim.Position, Plane = new Vector4(0,1,0,victim.Position.Y) };
            typeof(BeamProjectileEntity).GetMethod("ResolveCollision",BindingFlags.Instance|BindingFlags.NonPublic)!
                .Invoke(probe,new object?[] { collision,victim.Halfturret,true,.5f,true });
            Check(victim.Health == 1 && victim.Halfturret.Health == 68
                && runtime.LastImpact is { Damage:32,TurretDamage:32,HitTurret:true,Lethal:false },"turret-only damage at one body HP retains exact typed outcome");
            typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!.SetValue(victim,victim.Flags2 & ~PlayerFlags2.Halfturret);
            Ready();
            Check(runtime.Fire(shooter,++sequence,Vector3.UnitY) == WeaponActionResult.Accepted,"dynamic enemy probe owns a genuine accepted projectile");
            probe = equip.Beams.Single(b=>b.ModCustomShot?.Key.ShotId == sequence);
            var enemy = new EnemyInstanceEntity(new(0,shooter),shooter.NodeRef,scene);
            ushort enemyHealth = enemy.Health;
            typeof(BeamProjectileEntity).GetMethod("ResolveCollision",BindingFlags.Instance|BindingFlags.NonPublic)!
                .Invoke(probe,new object?[] { collision,enemy,true,.5f,false });
            Check(enemy.Health == enemyHealth && probe.Flags.TestFlag(BeamFlags.Collided),"enemy constructed after activation blocks custom shot without native effectiveness indexing");
            Ready();
            var savedShooter = shooter.Position; var savedVictim = victim.Position;
            shooter.ModPlaceAt(new(0,10000,0)); victim.ModPlaceAt(new(0,10000,-1001)); CleanVictim(200);
            var rangeOrigin = shooter.Position+new Vector3(0,.4f,0);
            Check(!BeamProjectileEntity.TryBeamObstacle(rangeOrigin,victim.Position+new Vector3(0,headHeight,0),scene,out _,out _),
                "range probe has no world occluder");
            Check(runtime.Optic(shooter,3,1) == WeaponActionResult.Accepted,"range probe selects precision optic");
            var rangeFire = runtime.Fire(shooter,++sequence,Aim(headHeight).Normalized());
            Check(rangeFire == WeaponActionResult.Accepted,"range-limited accepted precision shot: " + rangeFire);
            probe = equip.Beams.Single(b=>b.ModCustomShot?.Key.ShotId == sequence);
            Step(6);
            Check(victim.Health == 200 && probe.Flags.TestFlag(BeamFlags.Collided)
                && Vector3.Distance(probe.SpawnPosition,probe.Position) < 1000.1f,
                "real flight clips at 1000 units before an unobstructed target beyond range");
            Step(12); Check(probe.Lifespan == 0,"finite flight and collision presentation retire within a bounded lifetime");
            shooter.ModPlaceAt(savedShooter); victim.ModPlaceAt(savedVictim);
            var oldLife = runtime.State(shooter)!; shooter.Health = 0; runtime.Step();
            Check(oldLife.Snapshot().Phase == CustomWeaponPhase.Invalid
                && runtime.Fire(shooter,++sequence,Vector3.UnitY) == WeaponActionResult.InvalidLife,"death cancels resources and refuses new custom fire");
            shooter.Spawn(shooter.Position,Vector3.UnitZ,Vector3.UnitY,shooter.NodeRef,respawn:true); runtime.Step();
            Check(runtime.State(shooter)!.Owner.Life == 2 && runtime.State(shooter)!.Snapshot().Magazine == 4,
                "real respawn grants a fresh life without reviving the retired state");
            var escaped = runtime.State(shooter)!;
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null,NetRole.Client);
            runtime.Step();
            Check(!runtime.IsEligible && escaped.Snapshot().Phase == CustomWeaponPhase.Invalid,"transition to live role invalidates prototype before native transport");
            Check(equip.Beams.All(b=>b.Lifespan == 0 && b.ModCustomDefinition == null),"live refusal retires every private projectile and definition reference");
            Console.WriteLine($"PASS {checks} real-scene custom rifle checks; developer-only, no custom replay/online support");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { NetSession.Stop(); scene?.DoCleanup(); MapAudit.ForceEveryone=force; }
    }
}
