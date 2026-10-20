using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.MatchEvents;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Real authority emission/collision/health/payment integration through an explicit
// developer seam. Source certificates are deliberately injected trusted fixtures:
// no authenticated ingress, source-history/readiness producer or transport is tested.
internal static class CustomAuthoritySceneChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool condition,string label)
    {
        if (!condition) throw new InvalidOperationException("Custom authority scene: " + label);
        _checks++; Console.WriteLine("CUSTOM AUTHORITY PASS " + label);
    }
    private static void Frame(uint frame)
        => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null,frame);
    private static void Field(PlayerEntity player,string name,object value)
        => typeof(PlayerEntity).GetField(name,Hidden)!.SetValue(player,value);
    private static T DamageField<T>(string name)
        => (T)typeof(NetDamage).GetField(name,StaticHidden)!.GetValue(null)!;
    private sealed class TrustedFixtureOracle : ICustomSourceAdmission
    {
        internal readonly Dictionary<CustomWeaponAction,CustomSourceCertificate> Supplied = new();
        public CustomSourceDecision Inspect(in CustomWeaponAction action,out CustomSourceCertificate source)
            => Supplied.TryGetValue(action,out source) ? CustomSourceDecision.Verified : CustomSourceDecision.Rejected;
    }
    private sealed class Fixture : IDisposable
    {
        private readonly ServerSim _sim = new();
        internal readonly Scene Scene;
        internal readonly PlayerEntity Shooter, Victim;
        internal readonly TrustedFixtureOracle Oracle = new();
        internal readonly CustomAuthorityCombatAdapter Root;
        internal readonly WeaponOwnerContext Owner;
        internal readonly BeamProjectileEntity[] Pool;
        private uint _sequence;
        internal Fixture()
        {
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
            for (byte slot=0; slot<2; slot++)
            {
                roster.Slots[slot] = slot; roster.Generations[slot] = 1;
                roster.Hunters[slot] = (byte)Hunter.Weavel; roster.Names[slot] = "Custom authority fixture " + slot;
            }
            try
            {
                Check(_sim.Start("MP3 PROVING GROUND",GameMode.Battle,2,_=>{},()=>{},roster:roster),"asset-backed dedicated ServerSim starts");
                NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                    RoomKey = "MP3 PROVING GROUND",Mode = (byte)GameMode.Battle },false);
                NetSession.ApplyRoster(roster); Scene = _sim.EvidenceScene!;
                Scene.GameState.MatchTime = -1; Scene.GameState.PointGoal = 10000;
                Scene.GameState.DamageLevel = 1; // Explicit stock 1.0 damage modifier.
                Scene.GameState.EnhancedHunters = Scene.GameState.BalancedMode = false; Scene.GameState.PrimeHunter = -1;
                foreach (var item in Scene.GetItemInstanceEntities()) item.DespawnTimer = 0;
                for (int i=0; i<120; i++) _sim.Step();
                Shooter = Scene.Players.Items[0]; Victim = Scene.Players.Items[1];
                Check(NetSession.IsAuthority && NetSession.LocalSlot == -1 && Shooter.ModIsInPlay && Victim.ModIsInPlay,
                    "real authority actors own actual spawned lifecycle identities");
                Shooter.ModForceForm(false); Victim.ModForceForm(false); Shooter.Controls.ClearAll(); Victim.Controls.ClearAll();
                Clean(Shooter,200); Clean(Victim,200);
                // Spawn points can face a wall within three units. Choose a
                // measured clear body AND head segment using the real room mesh.
                bool clear = false; Vector3 basePosition = Shooter.Position;
                float headHeight = Fixed.ToFloat(Victim.Values.MaxPickupHeight)-.15f;
                foreach (int height in new[] { 0,2,5 })
                {
                    foreach (var direction in new[] { Vector3.UnitZ,Vector3.UnitX,-Vector3.UnitX,-Vector3.UnitZ })
                    {
                        var start = basePosition+Vector3.UnitY*height; var end = start+direction*3;
                        var muzzle = start+Vector3.UnitY*.4f;
                        if (BeamProjectileEntity.TryBeamObstacle(muzzle,end+Vector3.UnitY*.2f,Scene,out _,out _)
                            || BeamProjectileEntity.TryBeamObstacle(muzzle,end+Vector3.UnitY*headHeight,Scene,out _,out _)) continue;
                        Shooter.ModPlaceAt(start); Victim.ModPlaceAt(end); clear = true; break;
                    }
                    if (clear) break;
                }
                Check(clear,"native room provides measured unobstructed body/head fixture rays");
                NetUnlagged.AbortShot(); NetUnlagged.Reset(); NetFireEvents.Reset();
                NetUnlagged.Enabled = true; NetUnlagged.MaxRewindFrames = 45;
                NetDynamicGeometryHistory.ProductionEnabled = true;
                for (uint world=990; world<=1000; world++) { Frame(world); NetUnlagged.Record(world); }
                bool refused = false;
                try { Scene.EnableDeveloperCustomCombat(false,Oracle); }
                catch (InvalidOperationException) { refused = true; }
                Check(refused && Scene.CustomCombat == null && Scene.CustomWeapons == null,
                    "missing explicit developer opt-in refuses without installing runtime or registry");
                Root = Scene.EnableDeveloperCustomCombat(true,Oracle);
                var key = ShotKey.For(Shooter.SlotIndex,0);
                Owner = new(key.MatchId,key.AuthorityEpoch,(byte)key.ShooterSlot,key.Generation,key.LifeId);
                Check(Root.BindLife(Shooter,1000,true),"explicit trusted birth binds unequipped life inventory");
                Check(!Root.BindLife(Shooter,1000,true),"same-life bind cannot recreate or refill current inventory");
                var slots = (Array)typeof(CustomAuthorityCombatAdapter).GetField("_slots",Hidden)!.GetValue(Root)!;
                var slot = slots.GetValue(Shooter.SlotIndex)!;
                Pool = ((EquipInfo)slot.GetType().GetField("Equip",Hidden)!.GetValue(slot)!).Beams;
                Check(Pool.Length == 8 && !ReferenceEquals(Pool,Shooter.EquipInfo.Beams)
                    && Pool.All(b=>!Shooter.EquipInfo.Beams.Contains(b) && !Victim.EquipInfo.Beams.Contains(b)),
                    "authority owns eight private projectiles outside native pools");
                NetDamage.ForgetSlot(Shooter.SlotIndex); NetDamage.ForgetSlot(Victim.SlotIndex);
                Check(Root.TryState(Shooter,out var initial) && initial.Phase == CustomWeaponPhase.Unequipped
                    && initial.Magazine == 4,"real journal begins unequipped with canonical magazine");
                Check(Process(Action(CustomWeaponActionKind.Equip)).Result == WeaponActionResult.Accepted,"source-admitted equip seq1");
                Check(Process(Action(CustomWeaponActionKind.Optic,optic:1)).Result == WeaponActionResult.Accepted,"source-admitted scope seq2");
            }
            catch { _sim.Stop(); NetSession.Stop(); throw; }
        }
        internal static void Clean(PlayerEntity player,int health)
        {
            player.Health = health; Field(player,"_spawnInvulnTimer",(ushort)0); Field(player,"_damageInvulnTimer",(ushort)0);
            Field(player,"_frozenTimer",(ushort)0);
            player.ModReleaseSpawnProtectionFromNetwork();
        }
        internal Vector3 Aim(bool head = false)
            => (Victim.Position+Vector3.UnitY*(head ? Fixed.ToFloat(Victim.Values.MaxPickupHeight)-.15f : .2f)
                -(Shooter.Position+Vector3.UnitY*.4f)).Normalized();
        internal CustomWeaponAction Action(CustomWeaponActionKind kind,bool head = false,byte? optic = null,
            uint? target = null,Vector3? aim = null)
        {
            uint now = NetSession.NetFrame;
            var bootstrap = new WorldBootstrapIdentity(new(Owner.MatchId,Owner.AuthorityEpoch,1),1,Owner.Generation,1000,1,1);
            bool fire = kind == CustomWeaponActionKind.Fire;
            var direction = aim ?? Aim(head);
            var action = new CustomWeaponAction(bootstrap,Root.Manifest.Digest,Owner.Slot,Owner.Life,new(10),++_sequence,now,
                fire ? target ?? now : 0,0,kind,optic ?? (fire ? (byte)1 : (byte)0),fire ? Shooter.Position+Vector3.UnitY*.5f : Vector3.Zero,
                fire ? direction : Vector3.Zero);
            Oracle.Supplied.Add(action,new(action,now,fire ? target ?? now : now,0,Shooter.Position,direction,true,false,false));
            Check(Root.ObserveSource(Owner,now,true),"explicit oracle observation is bound to exact source frame and owner");
            return action;
        }
        internal CustomActionReceipt Process(CustomWeaponAction action)
        {
            Check(Root.Offer(action) == CustomQueueResult.Queued,"custom action queues in owned ordered journal");
            Check(Root.TryProcess(Shooter,out var receipt) && receipt.Action == action,"owned action produces immutable ordered receipt");
            return receipt;
        }
        internal BeamProjectileEntity Fire(bool head = false,int victimHealth = 200,bool process = true)
        {
            Clean(Victim,victimHealth);
            var receipt = Process(Action(CustomWeaponActionKind.Fire,head));
            Check(receipt.Result == WeaponActionResult.Accepted && receipt.Emission == CustomEmissionDisposition.Spawned
                && receipt.Action.Optic == 1 && Root.AcceptedEmissions == 1,"real scoped M7 accepted fire emits through native projectile factory exactly once");
            var beam = Pool.Single(b=>b.ModCustomShot?.Sequence == receipt.Action.Sequence);
            Check(beam.Beam == BeamType.None && beam.ModLaunchKey == default && beam.ModShotId == 0
                && beam.ModLaunchMatch == 0 && beam.ModLaunchAuthority == 0 && beam.ModLaunchGeneration == 0
                && beam.ModLaunchLife == 0 && beam.Equip?.Zoomed == true
                && beam.ModCustomShot?.Action == CustomActionIdentity.For(receipt.Action),
                "real authority projectile retains typed provenance with all native identity fields clear");
            void Diagnostic(string phase) => Console.WriteLine($"CUSTOM AUTHORITY DIAG {phase}: hp={Victim.Health} shooter={Shooter.Position} target={Victim.Position} spawn={beam.SpawnPosition} pos={beam.Position} back={beam.BackPosition} vel={beam.Velocity} flags={beam.Flags} life={beam.Lifespan} age={beam.Age} contacts={Root.TerminalContacts} payments={Root.PositivePayments} owned={Root.OwnsFlight(beam)} health={Root.LastOutcome?.Health} head={Root.LastOutcome?.Contact.Headshot}");
            Diagnostic("before Process");
            if (process) for (int i=0; i<2 && Root.TerminalContacts == 0; i++)
            { beam.Process(); Diagnostic("after Process " + (i+1)); }
            return beam;
        }
        internal void KillShooterWithNativeProjectile(uint? declaredLaunchWorld = null)
        {
            // The killing beam is created by the native factory. Direct TakeDamage
            // isolates canonical death/trade timing; no death tombstone is seeded.
            var equip = new EquipInfo(Scene.WeaponRules[(int)BeamType.PowerBeam],Victim.EquipInfo.Beams) { InfiniteAmmo = true };
            Vector3 origin = Victim.Position+new Vector3(0,20,0);
            try
            {
                NetUnlagged.BeginShot(Victim,origin,Vector3.UnitY,equip);
                Check(BeamProjectileEntity.Spawn(Victim,equip,origin,Vector3.UnitY,BeamSpawnFlags.NoMuzzle,Victim.NodeRef,Scene)
                    != BeamResultFlags.NoSpawn,"native killing-source projectile is genuinely emitted");
                NetUnlagged.EndShot(Victim);
            }
            finally { NetUnlagged.AbortShot(); }
            var beam = equip.Beams.First(b=>b.Lifespan > 0 && b.Owner == Victim);
            Check(NetPlayerLifecycle.CurrentProjectile(beam) && beam.ModLaunchFrame == NetSession.NetFrame,
                "native killing source carries actual current launch-world stamp");
            if (declaredLaunchWorld.HasValue)
            {
                // Explicit timing-only fixture override on a genuinely emitted
                // native source; actual TakeDamage/death observation stays real.
                beam.ModLaunchFrame = declaredLaunchWorld.Value;
                Check(NetHitClaims.CanonicalDamageWorld(Victim,beam,DamageFlags.None) == declaredLaunchWorld.Value,
                    "declared native timing seam supplies exact earlier killing world");
            }
            Clean(Shooter,100);
            Shooter.TakeDamage(999,DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln,null,beam);
            Check(Shooter.Health == 0 && NetDamage.Sequence(Shooter.SlotIndex) != 0,
                "native canonical TakeDamage actually kills shooter and allocates native damage event");
            beam.Destroy(); Scene.RemoveEntity(beam);
        }
        public void Dispose()
        { try { NetUnlagged.AbortShot(); Root.Dispose(); } finally { try { _sim.Stop(); } finally { NetSession.Stop(); } } }
    }
    internal static int Run(string data)
    {
        _checks = 0;
        string directory = Directory.GetCurrentDirectory();
        bool enabled = NetUnlagged.Enabled, pressAge = NetUnlagged.PressAgeEnabled;
        int maximum = NetUnlagged.MaxRewindFrames, players = PlayerEntity.MaxPlayers;
        bool geometry = NetDynamicGeometryHistory.ProductionEnabled, shadow = NetDynamicGeometryHistory.ShadowEnabled;
        bool force = MapAudit.ForceEveryone, script = NetTestScript.Enabled, claims = NetHitClaims.Enabled;
        var proof = NetShotProof.Mode; var timed = NetTimedShotProof.Mode;
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data)); Paths.UpdatePaths(); Paths.ChooseMphPath();
            Headless.Enter(); MapAudit.ForceEveryone = true; NetTestScript.Enabled = false;
            NetShotProof.Mode = ShotProofMode.Off; NetTimedShotProof.Mode = TimedGrantProofMode.Off;
            NetDynamicGeometryHistory.ProductionEnabled = true; NetDynamicGeometryHistory.ShadowEnabled = false;
            Check(ServerSim.Available(out string reason),"real assets/synthetic input available: " + reason);
            DamageAndSequence(); HeadAndScore(); ProtectedContact(); TurretContact(); MissingHistoryAndCapacity();
            Trade(false); Trade(true); RespawnAndReplacement(); LateImpactTrade(); SharedCounterBoundaries(); CanonicalReplayFrontier();
            RulesRetirement(); StreamRetirement(); SceneCleanup(); AdvanceAllocation(); RetainedLifeBudget();
            Console.WriteLine($"PASS {_checks} real-scene custom authority combat checks.");
            Console.WriteLine("SCOPE: actual dedicated M7 emission/Process/TakeDamage/health/payment, native-death trade, and seeded canonical snapshot damage-history replay seams; source certificates explicitly injected. NOT_RUN: authenticated transport/readiness/source-pose producer, custom replay reconstruction, presentation or GL/audio.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("CUSTOM AUTHORITY FAIL " + error); return 1; }
        finally
        {
            try { NetUnlagged.AbortShot(); }
            finally
            {
                NetSession.Stop(); NetUnlagged.Enabled = enabled; NetUnlagged.PressAgeEnabled = pressAge;
                NetUnlagged.MaxRewindFrames = maximum; PlayerEntity.MaxPlayers = players;
                NetDynamicGeometryHistory.ProductionEnabled = geometry; NetDynamicGeometryHistory.ShadowEnabled = shadow;
                NetTestScript.Enabled = script; NetShotProof.Mode = proof; NetTimedShotProof.Mode = timed;
                NetHitClaims.Enabled = claims; MapAudit.ForceEveryone = force; Directory.SetCurrentDirectory(directory);
            }
        }
    }
    private static void DamageAndSequence()
    {
        using var f = new Fixture(); ushort before = NetDamage.Sequence(f.Victim.SlotIndex);
        var beam = f.Fire();
        Check(f.Victim.Health == 135 && f.Root.LastOutcome is { Health.BodyRemoved:65,Health.TurretRemoved:0,Lethal:false }
            && f.Root.TerminalContacts == 1 && f.Root.PositivePayments == 1,"actual body collision pays exactly65 through TakeDamage");
        var outcome = f.Root.LastOutcome!.Value;
        Check(outcome.Health.DamageEventId == NetLifecycleTracker.Next(before) && NetDamage.Sequence(f.Victim.SlotIndex) == outcome.Health.DamageEventId
            && f.Root.TryGetOutcome(outcome.Contact.Key,out var retained) && retained == outcome,
            "typed immutable outcome joins exactly one canonical victim damage-event identity");
        PlayerState state = new() { SlotIndex = (byte)f.Victim.SlotIndex,SlotGeneration = NetPlayerLifecycle.Generation(f.Victim.SlotIndex),
            LifeId = NetPlayerLifecycle.Get(f.Victim.SlotIndex),Health = (ushort)f.Victim.Health,CurrentWeapon = (byte)f.Victim.CurrentWeapon };
        NetDamage.Write(f.Victim.SlotIndex,ref state);
        Check(state.DamageEventId == outcome.Health.DamageEventId && state.AttackerSlot == 255 && state.DamageBeam == 255
            && state.DamageFlags == 0 && state.HitDirection == Vector3.Zero
            && Enumerable.Range(0,PlayerState.DamageHistory).All(i=>state.EventAt(i).EventId == 0),
            "custom event advances shared sequence while native history and fallback attribution stay empty/neutral");
        var bytes = new byte[PlayerState.Size]; var again = new byte[PlayerState.Size]; state.Write(bytes);
        var decoded = PlayerState.Read(bytes); decoded.Write(again);
        Check(bytes.SequenceEqual(again) && decoded.DamageEventId == state.DamageEventId && decoded.Damage0.EventId == 0,
            "unchanged native PlayerState wire layout round-trips custom sequence with empty native history");
        foreach (bool claims in new[] { true,false })
        {
            NetHitClaims.Enabled = claims;
            int health = f.Victim.Health;
            f.Victim.TakeDamage(65,DamageFlags.NoDmgInvuln,null,beam);
            f.Root.ApplyContact(f.Victim,beam,65,DamageFlags.NoDmgInvuln,null,f.Victim.Position);
            Check(f.Victim.Health == health && f.Root.TerminalContacts == 1 && f.Root.PositivePayments == 1,
                "spent/direct custom source cannot reenter damage outside exact reserved contact, claims=" + claims);
        }
        var forged = new BeamProjectileEntity(f.Scene) { Owner = f.Shooter,Beam = BeamType.None,ModComponentId = 1,Lifespan = 1 };
        try
        {
            // Explicitly forged metadata on an object the authority never emitted
            // or placed in its private pool, even retaining the genuine source ref.
            typeof(BeamProjectileEntity).GetProperty(nameof(BeamProjectileEntity.ModCustomShot),Hidden)!.SetValue(forged,beam.ModCustomShot);
            forged.ModLaunchKey = ShotKey.For(f.Shooter.SlotIndex,3); forged.ModShotId = 3;
            int health = f.Victim.Health; ushort sequence = NetDamage.Sequence(f.Victim.SlotIndex);
            f.Victim.TakeDamage(999,DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln,null,forged);
            f.Root.ApplyContact(f.Victim,forged,999,DamageFlags.None,null,f.Victim.Position);
            Check(f.Victim.Health == health && NetDamage.Sequence(f.Victim.SlotIndex) == sequence && f.Root.PositivePayments == 1,
                "forged foreign beam/native fields cannot borrow genuine custom source or current contact");
        }
        finally { forged.Destroy(); }
        var originalAction = beam.ModCustomShot!.Certificate!.Value.Action;
        Check(f.Root.Offer(originalAction) == CustomQueueResult.Duplicate && f.Root.PositivePayments == 1,
            "duplicate accepted fire action retains its receipt without another emission/payment");
        ushort native = NetDamage.Note(f.Victim,f.Shooter,BeamType.PowerBeam,DamageFlags.None,Vector3.UnitX,1,
            launchFrame:1000,launchKey:ShotKey.For(f.Shooter.SlotIndex,500),componentId:1,damageKind:1);
        NetDamage.Write(f.Victim.SlotIndex,ref state);
        Check(native == NetLifecycleTracker.Next(outcome.Health.DamageEventId) && state.EventAt(PlayerState.DamageHistory-1).EventId == native
            && Enumerable.Range(0,PlayerState.DamageHistory-1).All(i=>state.EventAt(i).EventId == 0),
            "subsequent ordinary native Note uses next sequence and records only its native event");
        Check(f.Root.TryState(f.Shooter,out var snapshot) && snapshot.Magazine == 3,"real source journal debits one custom round");
    }
    private static void HeadAndScore()
    {
        using (var f = new Fixture())
        {
            f.Fire(head:true);
            Check(f.Victim.Health == 50 && f.Root.LastOutcome is { Health.BodyRemoved:150,Contact.Headshot:true,Lethal:false },
                "actual scoped native head-band collision pays150");
        }
        using (var f = new Fixture())
        {
            int kills = f.Scene.GameState.Kills[f.Shooter.SlotIndex], deaths = f.Scene.GameState.Deaths[f.Victim.SlotIndex];
            var beam = f.Fire(head:true,victimHealth:100);
            Check(f.Victim.Health == 0 && f.Root.LastOutcome is { Health.BodyRemoved:100,Lethal:true }
                && f.Scene.GameState.Kills[f.Shooter.SlotIndex] == kills+1 && f.Scene.GameState.Deaths[f.Victim.SlotIndex] == deaths+1,
                "actual lethal custom collision routes canonical death and score once");
            f.Victim.TakeDamage(150,DamageFlags.NoDmgInvuln,null,beam); beam.Process();
            Check(f.Root.PositivePayments == 1 && f.Scene.GameState.Kills[f.Shooter.SlotIndex] == kills+1
                && f.Scene.GameState.BeamKills[f.Shooter.SlotIndex,(int)BeamType.Imperialist] == 0,
                "duplicate contact cannot repeat health/score or create native Imperialist statistics");
        }
    }
    private static void ProtectedContact()
    {
        using var f = new Fixture(); Fixture.Clean(f.Victim,200); Field(f.Victim,"_spawnInvulnTimer",(ushort)120);
        var action = f.Action(CustomWeaponActionKind.Fire); var receipt = f.Process(action);
        Check(receipt.Emission == CustomEmissionDisposition.Spawned,"protected target does not reject source emission");
        var beam = f.Pool.Single(b=>b.ModCustomShot?.Sequence == action.Sequence); beam.Process(); beam.Process();
        Check(f.Victim.Health == 200 && f.Root.TerminalContacts == 1 && f.Root.PositivePayments == 0
            && f.Root.LastOutcome is { Health.BodyRemoved:0,Health.TurretRemoved:0,Health.DamageEventId:0 },
            "actual spawn-protected contact settles terminal zero without allocating damage event");
        Fixture.Clean(f.Victim,200); f.Root.ApplyContact(f.Victim,beam,65,DamageFlags.None,null,f.Victim.Position);
        Check(f.Victim.Health == 200 && f.Root.TerminalContacts == 1,"protection removal cannot revive spent component");
    }
    private static void TurretContact()
    {
        using var f = new Fixture(); f.Victim.CreateHalfturret(); f.Victim.Halfturret.Health = 100; f.Victim.Health = 1;
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!.SetValue(f.Victim,f.Victim.Flags2 | PlayerFlags2.Halfturret);
        var action = f.Action(CustomWeaponActionKind.Fire,aim:Vector3.UnitY); var receipt = f.Process(action);
        Check(receipt.Emission == CustomEmissionDisposition.Spawned,"turret seam owns genuinely emitted custom projectile");
        var beam = f.Pool.Single(b=>b.ModCustomShot?.Sequence == action.Sequence);
        var collision = new CollisionResult { Position = f.Victim.Position,Plane = new Vector4(0,1,0,f.Victim.Position.Y) };
        // Controlled native collision result isolates turret routing; ordinary
        // body/head tests above exercise actual scene geometry and intersection.
        typeof(BeamProjectileEntity).GetMethod("ResolveCollision",Hidden)!.Invoke(beam,new object?[] { collision,f.Victim.Halfturret,true,.5f,true });
        Check(f.Victim.Health == 1 && f.Victim.Halfturret.Health == 68 && f.Root.LastOutcome is
            { Health.BodyRemoved:0,Health.TurretRemoved:32,Contact.Key.Channel:CustomImpactChannel.Halfturret,Lethal:false },
            "pre-split capture preserves exact turret-only removal at one body HP");
    }
    private static void MissingHistoryAndCapacity()
    {
        using (var f = new Fixture())
        {
            NetUnlagged.Reset(); var receipt = f.Process(f.Action(CustomWeaponActionKind.Fire,target:999));
            Check(receipt.Emission == CustomEmissionDisposition.NoSpawn && f.Root.AcceptedEmissions == 0
                && f.Root.TerminalContacts == 0 && f.Victim.Health == 200 && f.Pool.All(b=>b.Lifespan <= 0)
                && f.Root.TryState(f.Shooter,out var state) && state.Magazine == 4,
                "missing historical target is definite pre-emission refusal with exact custom ammo refund");
            Check(f.Root.Offer(receipt.Action) == CustomQueueResult.Duplicate && !f.Root.TryProcess(f.Shooter,out _),
                "refunded no-spawn retains denied action sequence");
        }
        using (var f = new Fixture())
        {
            // Deliberate engine-pool pressure injection, not eight shots that the
            // four-round/75-tick/six-tick rifle could organically keep in flight.
            foreach (var beam in f.Pool) beam.Lifespan = 1;
            var receipt = f.Process(f.Action(CustomWeaponActionKind.Fire));
            Check(receipt.Result == WeaponActionResult.CapacityUnavailable && f.Root.AcceptedEmissions == 0
                && f.Pool.All(b=>b.Lifespan == 1) && f.Root.TryState(f.Shooter,out var state) && state.Magazine == 4,
                "eight occupied private slots refuse without debit, emission or eviction");
        }
        using (var f = new Fixture())
        {
            var beam = f.Fire(process:false);
            Check(f.Root.OwnsFlight(beam),"accepted private flight is owned before lifecycle retirement");
            f.Shooter.ModSetSpectating(true); f.Root.Advance();
            Check(!f.Root.OwnsFlight(beam) && !f.Root.TryState(f.Shooter,out _) && f.Pool.All(b=>b.Lifespan <= 0),
                "authoritative spectator retirement removes admitted flights and current source inventory");
            Check(f.Root.Offer(f.Oracle.Supplied.Keys.Last()) == CustomQueueResult.Foreign,
                "retired source cannot offer another same-life action");
        }
    }
    private static void Trade(bool later)
    {
        using var f = new Fixture(); Fixture.Clean(f.Victim,200);
        // Freeze explicit oracle body/aim/permission while the shooter is alive.
        // Future-world variant is deliberately offered only when authority reaches
        // that world; it must still fail the real canonical death-world fence.
        var action = f.Action(CustomWeaponActionKind.Fire,target:later ? 1001u : 1000u);
        f.KillShooterWithNativeProjectile();
        if (later) Frame(1001);
        var receipt = f.Process(action);
        if (later)
            Check(receipt.Disposition == CustomJournalDisposition.SourceDenied && f.Root.AcceptedEmissions == 0
                && f.Victim.Health == 200 && f.Root.PositivePayments == 0,
                "predeath permission cannot authorize custom launch later than actual native killing world");
        else
        {
            Check(receipt.Emission == CustomEmissionDisposition.Spawned,"predeath source at equal native killing world remains trade-eligible");
            var beam = f.Pool.Single(b=>b.ModCustomShot?.Sequence == action.Sequence); beam.Process(); beam.Process();
            Check(f.Shooter.Health == 0 && f.Victim.Health == 135 && f.Root.PositivePayments == 1,
                "actual native death followed by equal-world custom collision pays admitted trade once");
        }
    }
    private static PlayerState State(Fixture f)
        => new() { SlotIndex = (byte)f.Victim.SlotIndex,SlotGeneration = NetPlayerLifecycle.Generation(f.Victim.SlotIndex),
            LifeId = NetPlayerLifecycle.Get(f.Victim.SlotIndex),Health = (ushort)f.Victim.Health,
            CurrentWeapon = (byte)f.Victim.CurrentWeapon,AttackerSlot = 255,DamageBeam = 255 };
    private static void RespawnAndReplacement()
    {
        using (var f = new Fixture())
        {
            var beam = f.Fire(process:false); var source = beam.ModCustomShot!;
            var oldOwner = source.Owner;
            Check(f.Root.OwnsFlight(beam),"real admitted old-life flight exists before native death/respawn");
            f.KillShooterWithNativeProjectile();
            Frame(1001);
            f.Shooter.Spawn(f.Shooter.Position,Vector3.UnitZ,Vector3.UnitY,f.Shooter.NodeRef,respawn:true);
            Fixture.Clean(f.Shooter,200); f.Root.Advance();
            ushort life = NetPlayerLifecycle.Get(f.Shooter.SlotIndex);
            Check(life != oldOwner.Life && f.Shooter.ModIsInPlay && f.Root.OwnsFlight(beam)
                && beam.ModCustomShot?.Owner == oldOwner && !f.Root.TryState(f.Shooter,out _),
                "real native respawn advances life and retires source journal while preserving admitted old flight");
            Check(f.Root.BindLife(f.Shooter,1001,true) && f.Root.TryState(f.Shooter,out var fresh)
                && fresh.Owner.Life == life && fresh.Magazine == 4 && fresh.LastAcceptedShotId == 0
                && fresh.Phase == CustomWeaponPhase.Unequipped,
                "real replacement life receives fresh independent unequipped resources");
            beam.Process(); beam.Process();
            Check(f.Victim.Health == 135 && f.Root.PositivePayments == 1 && f.Root.LastOutcome is { } outcome
                && outcome.Contact.Key.Action.Owner == oldOwner && outcome.Health.BodyRemoved == 65,
                "old admitted flight pays65 once after native respawn without borrowing new firing life");
            var paid = f.Root.LastOutcome!.Value;
            Check(f.Root.TryGetOutcome(paid.Contact.Key,out var retained) && retained == paid
                && f.Root.TryState(f.Shooter,out fresh) && fresh.Owner.Life == life && fresh.Magazine == 4
                && fresh.LastAcceptedShotId == 0,
                "old flight receipt remains keyed to old life and cannot debit fresh-life resources");
            var assists = typeof(SceneMatchEvents).GetField("_assists",Hidden)!.GetValue(f.Scene.MatchEvents)!;
            var actors = (MatchSemanticActor[,])typeof(MatchAssistTracker).GetField("_actors",Hidden)!.GetValue(assists)!;
            var contributor = actors[f.Victim.SlotIndex,f.Shooter.SlotIndex];
            Check(contributor.Slot == oldOwner.Slot && contributor.SlotGeneration == oldOwner.Generation
                && contributor.Life == oldOwner.Life && contributor.Life != life,
                "generic assist contribution retains actual old firing life without native key conversion");
            ushort generation = NetPlayerLifecycle.Generation(f.Shooter.SlotIndex);
            NetPlayerLifecycle.SetOccupant(f.Shooter.SlotIndex,NetLifecycleTracker.Next(generation)); f.Root.Advance();
            Check(!f.Root.TryGetOutcome(paid.Contact.Key,out _) && !f.Root.TryState(f.Shooter,out _),
                "actual occupant generation replacement retires old receipts and current custom inventory");
        }
        using (var f = new Fixture())
        {
            var beam = f.Fire(process:false);
            NetPlayerLifecycle.SetOccupant(f.Shooter.SlotIndex,NetLifecycleTracker.Next(f.Owner.Generation)); f.Root.Advance();
            Check(!f.Root.OwnsFlight(beam) && beam.Lifespan <= 0 && f.Victim.Health == 200 && f.Root.PositivePayments == 0,
                "actual occupant generation replacement destroys genuinely admitted unpaid flight");
        }
    }
    private static void LateImpactTrade()
    {
        using var f = new Fixture(); var beam = f.Fire(process:false); var source = beam.ModCustomShot!;
        Check(source.Certificate?.TargetWorldFrame == 1000 && f.Root.TryState(f.Shooter,out var before) && before.Magazine == 3,
            "late-impact test actually emits and spends round before native killing contact");
        f.KillShooterWithNativeProjectile(declaredLaunchWorld:999);
        ushort sequence = NetDamage.Sequence(f.Victim.SlotIndex);
        beam.Process(); beam.Process();
        Check(f.Victim.Health == 200 && f.Root.TerminalContacts == 1 && f.Root.PositivePayments == 0
            && NetDamage.Sequence(f.Victim.SlotIndex) == sequence && f.Root.LastOutcome is
            { Disposition:CustomContactDisposition.TradeDenied,Health.BodyRemoved:0,Health.TurretRemoved:0,Health.DamageEventId:0 },
            "first physical impact after earlier-world native death settles immutable TradeDenied zero rather than paying");
        var denied = f.Root.LastOutcome!.Value;
        Check(f.Root.TryState(f.Shooter,out var after) && after.Magazine == 3
            && f.Root.TryGetOutcome(denied.Contact.Key,out var retained) && retained == denied,
            "late trade denial retains terminal outcome and never refunds actual emission");
        var lives = (Array)typeof(CustomAuthorityCombatAdapter).GetField("_lives",Hidden)!.GetValue(f.Root)!;
        var life = lives.Cast<object?>().First(l=>l != null
            && (WeaponOwnerContext)l.GetType().GetField("Owner",Hidden)!.GetValue(l)! == source.Owner)!;
        var payments = (CustomLaunchPayments)life.GetType().GetField("Payments",Hidden)!.GetValue(life)!;
        // Inspect the real life store after the real physical denial. These
        // alternate identity probes do not pretend another collision occurred.
        foreach (var changed in new[] { denied.Contact.Key with { Channel = CustomImpactChannel.Halfturret },
            denied.Contact.Key with { Victim = new((byte)f.Shooter.SlotIndex,f.Owner.Generation,f.Owner.Life) } })
            Check(payments.TryBeginContact(source,denied.Contact with { Key = changed },1000,out _,out _) == CustomContactDecision.Consumed
                && !f.Root.TryGetOutcome(changed,out _),"trade-denied component cannot revive through changed target/channel");
        f.Root.ApplyContact(f.Victim,beam,65,DamageFlags.Halfturret,null,f.Victim.Position);
        Check(f.Victim.Health == 200 && f.Root.TerminalContacts == 1 && f.Root.PositivePayments == 0,
            "repeated actual adapter contact cannot revive terminal trade denial");
    }
    private static void SharedCounterBoundaries()
    {
        foreach (var test in new[] { (Counter:ushort.MaxValue,Next:(ushort)1,Keep:false),
            (Counter:(ushort)32767,Next:(ushort)32768,Keep:true),(Counter:(ushort)32768,Next:(ushort)32769,Keep:false) })
        {
            using var f = new Fixture(); int slot = f.Victim.SlotIndex;
            // Only counter/history age is injected. Event allocation still occurs
            // inside the actual emitted projectile's reserved canonical contact.
            DamageField<ushort[]>("_sequence")[slot] = test.Counter;
            var history = DamageField<DamageEvent[,]>("_history");
            var lives = DamageField<ushort[,]>("_replayAttackerLives");
            history[slot,0] = new DamageEvent { EventId = 1,AttackerSlot = (byte)f.Shooter.SlotIndex,
                AttackerGeneration = NetPlayerLifecycle.Generation(f.Shooter.SlotIndex),Beam = (byte)BeamType.PowerBeam,
                Damage = 7,Direction = Vector3.UnitX };
            lives[slot,0] = 77;
            DamageField<byte[]>("_attacker")[slot] = (byte)f.Shooter.SlotIndex;
            DamageField<byte[]>("_beam")[slot] = (byte)BeamType.PowerBeam;
            DamageField<byte[]>("_flags")[slot] = 1;
            DamageField<Vector3[]>("_direction")[slot] = Vector3.UnitX;
            ushort receiveFrontier = DamageField<ushort[]>("_lastSeen")[slot];
            f.Fire();
            Check(f.Victim.Health == 135 && f.Root.LastOutcome?.Health.DamageEventId == test.Next
                && NetDamage.Sequence(slot) == test.Next,"actual custom contact allocates seeded wrap/half-range victim counter");
            Check(history[slot,0].EventId == (test.Keep ? 1 : 0) && lives[slot,0] == (test.Keep ? 77 : 0),
                "custom allocator retains only serial-older native history and clears paired replay life on alias/half-range expiry");
            var snapshot = State(f); NetDamage.Write(slot,ref snapshot);
            Check(snapshot.AttackerSlot == 255 && snapshot.DamageBeam == 255 && snapshot.DamageFlags == 0
                && snapshot.HitDirection == Vector3.Zero,"custom allocation clears scalar native fallback attribution even with seeded history");
            byte[] bytes = new byte[PlayerState.Size]; snapshot.Write(bytes); var decoded = PlayerState.Read(bytes);
            Check(decoded.DamageEventId == test.Next && decoded.EventAt(0).EventId == (test.Keep ? 1 : 0),
                "native snapshot layout retains custom frontier and exact pruned/retained native history");
            if (!test.Keep)
                Check(decoded.AttackerSlot == 255 && decoded.DamageBeam == 255 && decoded.DamageFlags == 0
                    && decoded.HitDirection == Vector3.Zero,"wrapped/pruned native wire metadata remains neutral without aliasing custom event");
            Check(f.Root.TryGetOutcome(f.Root.LastOutcome!.Value.Contact.Key,out _)
                && DamageField<ushort[]>("_lastSeen")[slot] == receiveFrontier,
                "custom payment/outcome lookup never advances native receive frontier; receipt transport is outside this fixture");
        }
    }
    private static void CanonicalReplayFrontier()
    {
        using var f = new Fixture(); Fixture.Clean(f.Victim,200); int slot = f.Victim.SlotIndex;
        var baseline = State(f); baseline.DamageEventId = 32768;
        NetDamage.BeginLife(slot,baseline);
        Check(DamageField<ushort[]>("_lastSeen")[slot] == 32768,"receiver fixture begins from exact canonical lifecycle snapshot frontier");
        var customHole = baseline; customHole.DamageEventId = 32769;
        int replayed = NetDamage.Replayed[slot];
        NetDamage.Replay(f.Victim,customHole);
        Check(DamageField<ushort[]>("_lastSeen")[slot] == 32769 && f.Victim.Health == 200
            && NetDamage.Replayed[slot] == replayed,
            "canonical custom-only snapshot closes empty native history hole without replaying health damage");
        NetDamage.Replay(f.Victim,customHole);
        Check(f.Victim.Health == 200 && NetDamage.Replayed[slot] == replayed,
            "repeated canonical custom-only snapshot cannot create native fallback damage");
        var native = customHole; native.DamageEventId = 32770; native.Health = 199;
        native.Damage3 = new DamageEvent { EventId = 32770,AttackerSlot = (byte)f.Shooter.SlotIndex,
            AttackerGeneration = NetPlayerLifecycle.Generation(f.Shooter.SlotIndex),Beam = (byte)BeamType.PowerBeam,Damage = 1 };
        NetDamage.Replay(f.Victim,native);
        Check(DamageField<ushort[]>("_lastSeen")[slot] == 32770 && f.Victim.Health == 199
            && NetDamage.Replayed[slot] == replayed+1,
            "subsequent exact native event replays once after canonical custom-only frontier");
        NetDamage.Replay(f.Victim,native);
        Check(f.Victim.Health == 199 && NetDamage.Replayed[slot] == replayed+1,
            "repeated native event remains deduplicated after custom snapshot hole");
    }
    private static void ExcludedRule(Fixture f,int rule,bool enabled)
    {
        switch (rule)
        {
            case 0: f.Scene.GameState.EnhancedHunters = enabled; break;
            case 1: f.Scene.GameState.BalancedMode = enabled; break;
            case 2: f.Scene.GameState.InstaGib = enabled; break;
            case 3: f.Scene.GameState.OneInTheChamber = enabled; break;
            case 4: f.Scene.GameState.NoImperialist = enabled; break;
            case 5: f.Scene.GameState.Mode = enabled ? GameMode.Capture : GameMode.Battle; break;
            default: throw new ArgumentOutOfRangeException(nameof(rule));
        }
    }
    private static void RulesRetirement()
    {
        string[] names = { "Enhanced Hunters","Balanced","Insta-Gib","One in the Chamber","No Imperialist","non-Battle mode" };
        for (int rule=0; rule<names.Length; rule++)
        {
            using var f = new Fixture(); var beam = f.Fire(process:false);
            var action = beam.ModCustomShot!.Certificate!.Value.Action;
            ExcludedRule(f,rule,true);
            Check(!f.Root.IsEligible && !f.Root.OwnsFlight(beam) && f.Root.Offer(action) == CustomQueueResult.Foreign
                && !f.Root.TryProcess(f.Shooter,out _),"actual rule change immediately refuses custom source/flight: " + names[rule]);
            f.Root.Advance();
            Check(f.Pool.All(b=>b.Lifespan <= 0 && b.ModCustomShot == null) && f.Victim.Health == 200
                && f.Root.PositivePayments == 0,"Advance retires unpaid private flight on rule change: " + names[rule]);
            ExcludedRule(f,rule,false); f.Root.Advance();
            Check(!f.Root.IsEligible && !f.Root.TryState(f.Shooter,out _) && !f.Root.BindLife(f.Shooter,1000,true),
                "restoring rule cannot revive disposed custom runtime or refill life: " + names[rule]);
        }
    }
    private static void StreamRetirement()
    {
        foreach (bool epoch in new[] { false,true })
        {
            using var f = new Fixture(); var beam = f.Fire(process:false);
            var source = beam.ModCustomShot!;
            var action = source.Certificate!.Value.Action;
            ushort match = epoch ? (ushort)1 : (ushort)2; ulong authority = epoch ? 2UL : 1UL;
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = match,AuthorityEpoch = authority,
                RoomKey = "MP3 PROVING GROUND",Mode = (byte)GameMode.Battle },false);
            Check(NetSession.CurrentMatchId == match && NetSession.AuthorityEpoch == authority,
                "real authoritative match control changes requested stream dimension; epoch=" + epoch);
            Check(!f.Root.IsEligible && !f.Root.OwnsFlight(beam) && f.Root.Offer(action) == CustomQueueResult.Foreign,
                "old runtime cannot cross actual authoritative match/epoch boundary; epoch=" + epoch);
            f.Root.Advance();
            Check(f.Pool.All(b=>b.Lifespan <= 0 && b.ModCustomShot == null) && f.Root.PositivePayments == 0,
                "actual stream replacement destroys old unpaid flight without payment; epoch=" + epoch);
            f.Root.Dispose(); f.Root.Advance();
            Check(!f.Root.BindLife(f.Shooter,1000,true),"stream-retired runtime remains unavailable under repeated disposal; epoch=" + epoch);
        }
    }
    private static void SceneCleanup()
    {
        using var f = new Fixture(); var beam = f.Fire(process:false); var runtime = f.Root;
        var registry = f.Scene.CustomWeapons!;
        f.Scene.DoCleanup();
        Check(f.Scene.CustomCombat == null && f.Scene.CustomWeapons == null && registry.IsDisposed
            && !runtime.IsEligible && !runtime.OwnsFlight(beam) && f.Pool.All(b=>b.Lifespan <= 0 && b.ModCustomShot == null),
            "actual scene cleanup releases custom runtime/registry and admitted private flight");
        runtime.Advance(); runtime.Dispose();
        Check(runtime.AcceptedEmissions == 1 && runtime.PositivePayments == 0,
            "retained runtime reference is inert after actual scene cleanup; no post-teardown actor damage invoked");
    }
    private static void AdvanceAllocation()
    {
        foreach (bool settled in new[] { false,true })
        {
            using var f = new Fixture(); var beam = f.Fire(process:settled);
            Check(f.Root.TryState(f.Shooter,out var before),"allocation fixture captures real source resources");
            var outcome = f.Root.LastOutcome; var position = beam.Position; float age = beam.Age, life = beam.Lifespan;
            ushort sequence = NetDamage.Sequence(f.Victim.SlotIndex); ulong contacts = f.Root.TerminalContacts;
            for (int i=0; i<10000; i++) f.Root.Advance();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i=0; i<100000; i++) f.Root.Advance();
            allocated = GC.GetAllocatedBytesForCurrentThread()-allocated;
            Check(allocated == 0,"100000 warmed fixed-frame Advance calls allocate zero bytes with actual retained " + (settled ? "outcome" : "flight"));
            Check(f.Root.TryState(f.Shooter,out var after) && after == before && f.Root.LastOutcome == outcome
                && f.Root.TerminalContacts == contacts && NetDamage.Sequence(f.Victim.SlotIndex) == sequence
                && beam.Position == position && beam.Age == age && beam.Lifespan == life,
                "fixed-frame Advance measurement preserves exact real retained state; settled=" + settled);
            Console.WriteLine($"CUSTOM AUTHORITY ALLOCATION 100000 fixed-frame Advance calls, retained {(settled ? "outcome" : "flight")}: {allocated} bytes.");
        }
    }
    private static void RetainedLifeBudget()
    {
        using var f = new Fixture(); var initial = f.Fire(); var first = f.Root.LastOutcome!.Value;
        Vector3 shooterBase = f.Shooter.Position, victimBase = f.Victim.Position;
        // Retire already-collided presentation objects so this store-retention
        // fixture isolates life budget, rather than artificial projectile pressure.
        initial.Destroy(); f.Scene.RemoveEntity(initial);
        CustomActionReceipt AdmitCurrent(CustomWeaponActionKind kind,uint sequence,byte optic)
        {
            var key = ShotKey.For(f.Shooter.SlotIndex,0); uint world = NetSession.NetFrame;
            var owner = new WeaponOwnerContext(key.MatchId,key.AuthorityEpoch,(byte)key.ShooterSlot,key.Generation,key.LifeId);
            bool fire = kind == CustomWeaponActionKind.Fire;
            var bootstrap = new WorldBootstrapIdentity(new(owner.MatchId,owner.AuthorityEpoch,1),1,owner.Generation,world,1,1);
            var aim = f.Aim();
            var action = new CustomWeaponAction(bootstrap,f.Root.Manifest.Digest,owner.Slot,owner.Life,new(10),sequence,world,
                fire ? world : 0,0,kind,optic,fire ? f.Shooter.Position+Vector3.UnitY*.5f : Vector3.Zero,fire ? aim : Vector3.Zero);
            f.Oracle.Supplied.Add(action,new(action,world,world,0,f.Shooter.Position,aim,true,false,false));
            Check(f.Root.ObserveSource(owner,world,true) && f.Root.Offer(action) == CustomQueueResult.Queued,
                "budget fixture supplies exact current-life source to real journal");
            Check(f.Root.TryProcess(f.Shooter,out var receipt) && receipt.Result == WeaponActionResult.Accepted,
                "budget fixture executes actual current-life custom action");
            return receipt;
        }
        for (int retained=1; retained<CustomAuthorityCombatAdapter.LifeCapacity; retained++)
        {
            uint world = 1000u+(uint)retained; Frame(world);
            f.Shooter.Spawn(shooterBase,Vector3.UnitZ,Vector3.UnitY,f.Shooter.NodeRef,respawn:true);
            // Native Spawn offsets its supplied position upward. Restore the
            // originally measured clear standing pair after the real lifecycle
            // transition, rather than accumulating height and hitting heads.
            f.Shooter.ModPlaceAt(shooterBase); f.Victim.ModPlaceAt(victimBase);
            Fixture.Clean(f.Shooter,200); Fixture.Clean(f.Victim,200); f.Root.Advance();
            Check(f.Root.BindLife(f.Shooter,world,true),"bounded store retains another actual native respawn life");
            AdmitCurrent(CustomWeaponActionKind.Equip,1,0); AdmitCurrent(CustomWeaponActionKind.Optic,2,1);
            var receipt = AdmitCurrent(CustomWeaponActionKind.Fire,3,1);
            Check(receipt.Emission == CustomEmissionDisposition.Spawned,"budget fixture emits genuine M7 in each retained life");
            var beam = f.Pool.Single(b=>b.Lifespan > 0 && !b.Flags.TestFlag(BeamFlags.Collided) && b.ModCustomShot?.Owner.Life == NetPlayerLifecycle.Get(f.Shooter.SlotIndex));
            beam.Process(); beam.Process();
            if (f.Victim.Health != 135)
                Console.WriteLine($"CUSTOM AUTHORITY BUDGET DIAG retained={retained} world={world} hp={f.Victim.Health} shooter={f.Shooter.Position} victim={f.Victim.Position} beam={beam.Position} spawn={beam.SpawnPosition} flags={beam.Flags} age={beam.Age} lifespan={beam.Lifespan} owned={f.Root.OwnsFlight(beam)} contacts={f.Root.TerminalContacts} positive={f.Root.PositivePayments} outcome={f.Root.LastOutcome}");
            Check(f.Victim.Health == 135 && f.Root.LastOutcome?.Contact.Headshot == false,
                "each retained life owns an actual terminal65 body payment");
            beam.Destroy(); f.Scene.RemoveEntity(beam);
        }
        Frame(1032); f.Shooter.Spawn(shooterBase,Vector3.UnitZ,Vector3.UnitY,f.Shooter.NodeRef,respawn:true);
        f.Shooter.ModPlaceAt(shooterBase); f.Victim.ModPlaceAt(victimBase);
        Fixture.Clean(f.Shooter,200); f.Root.Advance();
        Check(!f.Root.BindLife(f.Shooter,1032,true) && f.Root.TryGetOutcome(first.Contact.Key,out var original) && original == first,
            "33rd real life fails bounded retention admission without evicting first paid outcome");
        Frame(1256); f.Root.Advance();
        Check(!f.Root.BindLife(f.Shooter,1256,true) && f.Root.TryGetOutcome(first.Contact.Key,out _),
            "root life budget preserves first outcome through inclusive256 retention boundary");
        Frame(1257); f.Root.Advance();
        Check(f.Root.BindLife(f.Shooter,1257,true) && !f.Root.TryGetOutcome(first.Contact.Key,out _)
            && f.Root.TryState(f.Shooter,out var fresh) && fresh.Magazine == 4 && fresh.Phase == CustomWeaponPhase.Unequipped,
            "expired retained life releases capacity for previously refused native life without premature eviction");
    }
}
