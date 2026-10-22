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
internal static partial class CustomAuthoritySceneChecks
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
    // Explicit trusted server configuration, not an authenticated readiness packet.
    private sealed class TrustedReadinessFixture : ICustomSourceReadiness
    {
        internal CustomSourceReadyContext? Context;
        public bool TryGet(WeaponOwnerContext owner,out CustomSourceReadyContext context)
        { context = Context.GetValueOrDefault(); return Context.HasValue && context.Ready.Slot == owner.Slot; }
    }
    // Explicit reservation/audience fixture. It supplies no pose, certificate,
    // health or resource authority and does not model packet delivery.
    private sealed class ExecutionDelivery : ICustomCombatDelivery
    {
        internal bool Allowed=true;
        internal int Prepared,Launches,Contacts;
        internal readonly List<CustomActionReceipt> Receipts=new();
        private readonly HashSet<CustomWeaponAction> _prepared=new();
        public bool MayExecute(in CustomWeaponAction action)=>Allowed;
        public bool HasActionReservation(in CustomWeaponAction action)=>true;
        public bool HasContactReservation(in CustomWeaponAction action)=>true;
        public bool HasControlWitness(in CustomWeaponAction action)=>false;
        public bool TryPrepareAudience(in CustomWeaponAction action){Prepared++;_prepared.Add(action);return true;}
        public bool HasPreparedAudience(in CustomWeaponAction action)=>_prepared.Contains(action);
        public bool Publish(in CustomActionReceipt receipt){Receipts.Add(receipt);return true;}
        public bool Publish(in CustomContactOutcome outcome){Contacts++;return true;}
        public bool PublishLaunch(in CustomReplicaLaunch launch){Launches++;return true;}
        public void SourceObserved(CustomAuthorityCombatAdapter root,in CustomSourceInput input){}
        public void AfterSourcePhase(CustomAuthorityCombatAdapter root){}
    }
    private sealed class Fixture : IDisposable
    {
        private readonly ServerSim _sim = new();
        internal readonly Scene Scene;
        internal readonly PlayerEntity Shooter, Victim;
        internal readonly TrustedFixtureOracle Oracle = new();
        internal readonly TrustedReadinessFixture Readiness = new();
        internal readonly CustomReadinessPeer? BoundaryPeer;
        internal readonly CustomPeerSourceBoundary? Boundary;
        internal readonly CustomAuthorityCombatAdapter Root;
        internal readonly WeaponOwnerContext Owner;
        internal BeamProjectileEntity[] Pool
        {
            get
            {
                var slots = (Array)typeof(CustomAuthorityCombatAdapter).GetField("_slots",Hidden)!.GetValue(Root)!;
                var slot = slots.GetValue(Shooter.SlotIndex);
                return slot == null ? Array.Empty<BeamProjectileEntity>()
                    : ((EquipInfo)slot.GetType().GetField("Equip",Hidden)!.GetValue(slot)!).Beams;
            }
        }
        private uint _sequence;
        internal long StepFailures => _sim.StepFailures;
        internal void Step() => _sim.Step();
        internal Fixture(bool engineSources = false,bool automaticSourceBoundary = false,ICustomCombatDelivery? delivery = null)
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
                var key = ShotKey.For(Shooter.SlotIndex,0);
                Owner = new(key.MatchId,key.AuthorityEpoch,(byte)key.ShooterSlot,key.Generation,key.LifeId);
                if (automaticSourceBoundary)
                {
                    BoundaryPeer = new(Bootstrap(),Owner.Slot,1,0);
                    Boundary = new(BoundaryPeer);
                    Root = Scene.EnableDeveloperEngineCustomCombat(true,Boundary,delivery);
                }
                else Root = engineSources ? Scene.EnableDeveloperEngineCustomCombat(true,Readiness,delivery)
                    : Scene.EnableDeveloperCustomCombat(true,Oracle);
                Readiness.Context = new(new(Bootstrap(),Root.Manifest.Digest,Owner.Slot,
                    WeaponMechanics.SchemaVersion,WeaponMechanics.BehaviorVersion,CustomWeaponReady.PrecisionCapability),1000,1000,1);
                if (!automaticSourceBoundary)
                {
                    Check(Root.BindLife(Shooter,1000,true),"explicit trusted birth binds unequipped life inventory");
                    Check(!Root.BindLife(Shooter,1000,true),"same-life bind cannot recreate or refill current inventory");
                    Check(Pool.Length == 8 && !ReferenceEquals(Pool,Shooter.EquipInfo.Beams)
                        && Pool.All(b=>!Shooter.EquipInfo.Beams.Contains(b) && !Victim.EquipInfo.Beams.Contains(b)),
                        "authority owns eight private projectiles outside native pools");
                    Check(Root.TryState(Shooter,out var initial) && initial.Phase == CustomWeaponPhase.Unequipped
                        && initial.Magazine == 4,"real journal begins unequipped with canonical magazine");
                }
                else Check(!Root.TryState(Shooter,out _) && Pool.Length == 0,
                    "automatic source-boundary mode has no manually granted life inventory or private pool");
                NetDamage.ForgetSlot(Shooter.SlotIndex); NetDamage.ForgetSlot(Victim.SlotIndex);
                if (!engineSources && !automaticSourceBoundary)
                {
                    Check(Process(Action(CustomWeaponActionKind.Equip)).Result == WeaponActionResult.Accepted,"source-admitted equip seq1");
                    Check(Process(Action(CustomWeaponActionKind.Optic,optic:1)).Result == WeaponActionResult.Accepted,"source-admitted scope seq2");
                }
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
        internal WorldBootstrapIdentity Bootstrap()
            => new(new(Owner.MatchId,Owner.AuthorityEpoch,1),1,Owner.Generation,1000,1,1);
        internal CustomWeaponAction EngineAction(CustomWeaponActionKind kind,uint source,uint ack = 0,byte optic = 0)
            => new(Bootstrap(),Root.Manifest.Digest,Owner.Slot,Owner.Life,new(10),++_sequence,source,ack,0,kind,optic,
                kind == CustomWeaponActionKind.Fire ? Shooter.Position+Vector3.UnitY*.4f : Vector3.Zero,
                kind == CustomWeaponActionKind.Fire ? Aim() : Vector3.Zero);
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
    internal static int Run(string data,bool sourceOnly = false,bool admissionOnly = false,bool readinessOnly = false,bool snapshotOnly = false,string? snapshotCase = null,bool nativeSelectionOnly = false)
    {
        _checks = 0;
        string directory = Directory.GetCurrentDirectory();
        bool enabled = NetUnlagged.Enabled, pressAge = NetUnlagged.PressAgeEnabled;
        int maximum = NetUnlagged.MaxRewindFrames, players = PlayerEntity.MaxPlayers;
        bool geometry = NetDynamicGeometryHistory.ProductionEnabled, shadow = NetDynamicGeometryHistory.ShadowEnabled;
        bool force = MapAudit.ForceEveryone, script = NetTestScript.Enabled, claims = NetHitClaims.Enabled;
        bool acceptedPlayback = NetFireEvents.AcceptedPlaybackEnabled;
        var proof = NetShotProof.Mode; var timed = NetTimedShotProof.Mode;
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data)); Paths.UpdatePaths(); Paths.ChooseMphPath();
            Headless.Enter(); MapAudit.ForceEveryone = true; NetTestScript.Enabled = false;
            NetShotProof.Mode = ShotProofMode.Off; NetTimedShotProof.Mode = TimedGrantProofMode.Off;
            NetDynamicGeometryHistory.ProductionEnabled = true; NetDynamicGeometryHistory.ShadowEnabled = false;
            Check(ServerSim.Available(out string reason),"real assets/synthetic input available: " + reason);
            if (nativeSelectionOnly)
            {
                NativeSelectionAccepted(); NativeSelectionReleased(); NativeSelectionDenied(); NativeSelectionFire(); NativeSelectionLifecycle(); NativeFireGateEngine();
                Console.WriteLine($"PASS {_checks} real engine native selection-history checks.");
                Console.WriteLine("SCOPE: actual asset-backed authority actors, native accepted-input placement/control phases and production engine custom source admission; trusted readiness configuration and manual ordered action ingress are explicit fixtures. Source-time selection ownership plus native gate intake and actual turret-emission claim lookup are tested through explicit readiness ingress seams; UDP, claim damage and rendering are not activated.");
                return 0;
            }
            if (snapshotOnly)
            {
                if (snapshotCase=="wall") ReplicaCaptureTerminal();
                else if (snapshotCase=="scope") ReplicaCaptureNativeScope();
                else { ReplicaCaptureEmpty(); ReplicaCaptureAtomic(); ReplicaCaptureFlight(); ReplicaCaptureTerminal(); ReplicaCaptureRespawn(); ReplicaCaptureNativeScope(); }
                Console.WriteLine($"PASS {_checks} real authority replica capture checks.");
                Console.WriteLine("SCOPE: read-only authority value capture of actual private custom projectiles and public state. Source oracle and native lifecycle/scene setup are explicit fixtures. No live fanout, late-join install, client rendering or transport claim.");
                return 0;
            }
            if (readinessOnly)
            {
                AutomaticReadinessBoundary(); RejectedReadinessBoundary(); ReadinessBoundaryLifecycle();
                Console.WriteLine($"PASS {_checks} real engine readiness/source-boundary checks.");
                Console.WriteLine("SCOPE: actual engine placement/control phases automatically open source boundary and life inventory, then production engine admission/emission/canonical damage. Network accepted-input publications and peer readiness handshake are explicit trusted fixtures; no UDP action or source-frontier transport claim.");
                return 0;
            }
            if (admissionOnly)
            {
                EngineAdmissionSuccess(false); EngineAdmissionSuccess(true); EngineAdmissionRejections(); EngineAdmissionReadiness();
                Console.WriteLine($"PASS {_checks} real engine source admission checks.");
                Console.WriteLine("SCOPE: real remote input placement/control/source/modifier observation through production custom admission, private M7 emission and canonical damage; readiness is explicit trusted fixture configuration. NOT_RUN: endpoint authentication, custom readiness/action transport, local/bot source parity, missing-input recovery or rendering.");
                return 0;
            }
            if (sourceOnly)
            {
                NetFireEvents.AcceptedPlaybackEnabled = true;
                SourceFacts(); SourceInputPhase(); AutomaticSourceInputPhase(); NativeSourceInputBarrier(false); NativeSourceInputBarrier(true);
                DisposeDuringSourceInputPhase(); RejectedSourceInputs(); FrozenSourceCancelsReload();
                Console.WriteLine($"PASS {_checks} real engine source observation checks.");
                Console.WriteLine("SCOPE: actual authority placement/control validation and post-control/pre-projectile observation barrier; native input carriers seeded through AcceptSlotIntent. NOT_RUN: endpoint authentication, custom action admission, missing-source recovery, modifiers, local/bot source parity or custom transport.");
                return 0;
            }
            ExecutionBarrier(); DamageAndSequence(); HeadAndScore(); ProtectedContact(); TurretContact(); MissingHistoryAndCapacity();
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
                NetHitClaims.Enabled = claims; NetFireEvents.AcceptedPlaybackEnabled = acceptedPlayback;
                MapAudit.ForceEveryone = force; Directory.SetCurrentDirectory(directory);
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

    private static IntentPacket SourcePacket(PlayerEntity player,uint source,Vector3 aim)
    {
        var life = ShotKey.For(player.SlotIndex,0);
        return new IntentPacket { MatchId = life.MatchId,AuthorityEpoch = life.AuthorityEpoch,
            SlotGeneration = life.Generation,LifeId = life.LifeId,Frame = source,AckFrame = source-1,
            Position = player.Position,Aim = aim,WeaponSelect = 255,Buttons = IntentButtons.InPlayState };
    }
    private static WeaponOwnerContext SourceOwner(PlayerEntity player)
    {
        var key = ShotKey.For(player.SlotIndex,0);
        return new(key.MatchId,key.AuthorityEpoch,(byte)key.ShooterSlot,key.Generation,key.LifeId);
    }
    private static IntentPacket BoundaryPacket(Fixture f,uint source,bool shoot = false)
    {
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.GunAnimation))!.SetValue(f.Shooter,GunAnimation.Idle);
        var input = SourcePacket(f.Shooter,source,f.Aim());
        if (shoot) input.Buttons |= IntentButtons.Shoot;
        return input;
    }
    private static void PublishBoundaryInput(Fixture f,in IntentPacket input,uint world,bool validReceipt = true)
    {
        Frame(world);
        var owner = new WeaponOwnerContext(input.MatchId,input.AuthorityEpoch,(byte)f.Shooter.SlotIndex,input.SlotGeneration,input.LifeId);
        // Explicit trusted ingress publication fixture. No UDP receive/authentication
        // or actual server source-frontier publication is asserted by this test.
        bool noted = f.Boundary!.NoteAcceptedInput(new(owner,input.Frame,input.AckFrame,input.AckSubFrame,
            input.Position,input.Aim,(uint)input.Buttons));
        Check(noted == validReceipt,"trusted fixture publishes exact accepted input receipt; valid=" + validReceipt);
        Check(NetSession.AcceptSlotIntent(f.Shooter.SlotIndex,input),"boundary fixture submits input through actual native lifecycle carrier intake");
        ConsumeBoundaryInput(f,world);
    }
    private static void ConsumeBoundaryInput(Fixture f,uint world)
    {
        Frame(world);
        Check(f.Root.BeginSourceInputPhase(),"automatic boundary starts actual engine source phase");
        NetHooks.TryApplyRemoteInput(f.Shooter,f.Shooter.SlotIndex);
        f.Root.CompleteSourceInputPhase(); NetUnlagged.Record(world);
    }
    private static void ConfirmBoundary(Fixture f)
    {
        var peer = f.BoundaryPeer!;
        Check(peer.Receive(peer.Wire.ReadyBytes,peer.ConnectionId,NetSession.NetFrame,0) == CustomReadinessResult.Accepted
            && f.Boundary!.ConfirmReadiness(),"explicit trusted peer fixture confirms exact readiness and freezes input receipt fence");
    }
    private static object GrantClock(Fixture f)
        => ((Array)typeof(CustomAuthorityCombatAdapter).GetField("_grantClocks",Hidden)!.GetValue(f.Root)!).GetValue(f.Shooter.SlotIndex)!;
    private static void AutomaticReadinessBoundary()
    {
        using var f = new Fixture(automaticSourceBoundary:true);
        var beforeReady = BoundaryPacket(f,1000);
        PublishBoundaryInput(f,beforeReady,1000);
        Check(!f.Root.TryState(f.Shooter,out _) && !f.Boundary!.TryGet(f.Owner,out _) && f.Pool.Length == 0,
            "consumed pre-readiness input cannot grant inventory or source context");
        ConfirmBoundary(f); ConsumeBoundaryInput(f,1000);
        Check(!f.Root.TryState(f.Shooter,out _) && !f.Boundary!.TryGet(f.Owner,out _),
            "reconsuming pre-ready carrier cannot manufacture post-ready source boundary");
        PublishBoundaryInput(f,BoundaryPacket(f,1001),1001);
        Check(f.Root.TryState(f.Shooter,out var initial) && initial.Magazine == 4 && initial.Reserve == 16
            && initial.Phase == CustomWeaponPhase.Unequipped && initial.Tick == 1001,
            "fresh engine-consumed carrier automatically grants initial4/16 unequipped inventory");
        Check(f.Root.TryGetSourceObservation(f.Owner,1001,out var first),"automatic initial life stores exact accepted source observation");
        Check(f.Boundary!.TryGet(f.Owner,out var context) && context.FirstSourceFrame == 1001
            && context.OpenedWorld == 1000 && context.PeerIncarnation == 1,
            "fresh accepted carrier consumed by engine automatically opens exact readiness/source context");
        Check(f.Pool.Length == 8 && f.Pool.All(b=>!f.Shooter.EquipInfo.Beams.Contains(b) && !f.Victim.EquipInfo.Beams.Contains(b)),
            "automatic boundary creates only owned private projectile pool after engine admission");
        Check(f.Process(f.EngineAction(CustomWeaponActionKind.Equip,1001)).Result == WeaponActionResult.Accepted
            && f.Process(f.EngineAction(CustomWeaponActionKind.Optic,1001,optic:1)).Result == WeaponActionResult.Accepted,
            "automatically sourced equip and optic use existing journal without supplied certificates");
        PublishBoundaryInput(f,BoundaryPacket(f,1002,shoot:true),1002);
        var fire = f.EngineAction(CustomWeaponActionKind.Fire,1002,1001,optic:1);
        var receipt = f.Process(fire);
        Check(receipt.Result == WeaponActionResult.Accepted && receipt.Emission == CustomEmissionDisposition.Spawned
            && f.Oracle.Supplied.Count == 0,"automatic boundary feeds production engine fire admission and real M7 factory");
        var beam = f.Pool.Single(b=>b.ModCustomShot?.Sequence == fire.Sequence);
        for (int step=0; step<2 && f.Root.TerminalContacts == 0; step++) beam.Process();
        Check(f.Victim.Health == 135 && f.Root.PositivePayments == 1
            && f.Root.TryState(f.Shooter,out var spent) && spent.Magazine == 3 && spent.Reserve == 16,
            "automatically bound real M7 source removes65 canonical health and consumes one custom round");
        Check(f.Boundary!.ConfirmReadiness(),"identical readiness confirmation preserves first source boundary");
        ConsumeBoundaryInput(f,1003);
        Check(f.Root.TryState(f.Shooter,out var repeated) && repeated.Magazine == 3 && repeated.Reserve == 16
            && repeated.LastAcceptedShotId == fire.Sequence && f.Root.TryGetSourceObservation(f.Owner,1001,out var unchanged)
            && unchanged == first && f.Boundary.TryGet(f.Owner,out var sameContext) && sameContext == context,
            "repeated same-life source and readiness cannot refill resources or revise first observed boundary");
    }
    private static void RejectedReadinessBoundary()
    {
        foreach (string reason in new[] { "invalid aim","nonfinite body","zero position","frozen placement" })
        {
            using var f = new Fixture(automaticSourceBoundary:true); ConfirmBoundary(f);
            var input = BoundaryPacket(f,1001);
            if (reason == "invalid aim") input.Aim = Vector3.Zero;
            if (reason == "nonfinite body") input.Position = new(float.NaN,0,0);
            if (reason == "zero position") input.Position = Vector3.Zero;
            if (reason == "frozen placement") Field(f.Shooter,"_frozenTimer",(ushort)60);
            PublishBoundaryInput(f,input,1001,validReceipt:reason is not ("invalid aim" or "nonfinite body"));
            Check(!f.Root.TryState(f.Shooter,out _) && !f.Boundary!.TryGet(f.Owner,out _) && f.Pool.Length == 0,
                "malformed or engine-rejected source cannot open boundary or grant inventory: " + reason);
            Field(f.Shooter,"_frozenTimer",(ushort)0);
            PublishBoundaryInput(f,BoundaryPacket(f,1002),1002);
            Check(f.Root.TryState(f.Shooter,out var state) && state.Magazine == 4 && state.Reserve == 16
                && state.Phase == CustomWeaponPhase.Unequipped && f.Boundary!.TryGet(f.Owner,out var context)
                && context.FirstSourceFrame == 1002,"fresh actual accepted pose opens once after earlier unavailable source: " + reason);
        }
    }
    private static void ReadinessBoundaryLifecycle()
    {
        using (var f = new Fixture(automaticSourceBoundary:true))
        {
            ConfirmBoundary(f); PublishBoundaryInput(f,BoundaryPacket(f,1000),1000);
            object clock = GrantClock(f); Vector3 body = f.Shooter.Position;
            f.Shooter.ModSetSpectating(true); f.Root.Advance();
            Check(!f.Root.TryState(f.Shooter,out _) && f.Pool.Length == 0,"actual spectator retirement releases automatic life inventory and pool");
            f.Shooter.ModSetSpectating(false); Fixture.Clean(f.Shooter,200);
            // Explicit same-life body restoration attempt. No native respawn is
            // claimed here; the clock fence must deny even an otherwise usable
            // restored body, rather than succeeding only because Spawned is off.
            f.Shooter.LoadFlags |= LoadFlags.Spawned;
            Check(f.Shooter.ModIsInPlay && SourceOwner(f.Shooter) == f.Owner,
                "same-life rejoin fixture restores usable body without advancing native lifecycle");
            PublishBoundaryInput(f,BoundaryPacket(f,1001),1001);
            Check(!f.Root.TryState(f.Shooter,out _) && f.Pool.Length == 0 && ReferenceEquals(GrantClock(f),clock),
                "same-life spectator rejoin cannot recreate resources and retains occupant source clock");
            // Explicit negative API probes cannot grant any life. The production
            // auto path above remains the only successful binding in this mode.
            for (int i=0; i<100; i++) f.Root.BindLife(f.Shooter,1001,true);
            long before = GC.GetAllocatedBytesForCurrentThread(); bool granted = false;
            for (int i=0; i<10000; i++) granted |= f.Root.BindLife(f.Shooter,1001,true);
            long allocated = GC.GetAllocatedBytesForCurrentThread()-before;
            Check(!granted && allocated == 0,"warmed denied same-life grant probes allocate zero bytes and cannot reopen inventory");
            Frame(1002); f.Shooter.Spawn(body,Vector3.UnitZ,Vector3.UnitY,f.Shooter.NodeRef,respawn:true);
            f.Shooter.ModPlaceAt(body); Fixture.Clean(f.Shooter,200); f.Root.Advance();
            var nextOwner = SourceOwner(f.Shooter);
            Check(nextOwner.Life != f.Owner.Life,"actual native respawn advances full owner life after spectator retirement");
            PublishBoundaryInput(f,BoundaryPacket(f,1002),1002);
            Check(f.Root.TryState(f.Shooter,out var fresh) && fresh.Owner == nextOwner && fresh.Magazine == 4
                && fresh.Reserve == 16 && fresh.Phase == CustomWeaponPhase.Unequipped
                && ReferenceEquals(GrantClock(f),clock),"real next life automatically binds once using retained occupant source clock");
            ConsumeBoundaryInput(f,1003);
            Check(f.Root.TryState(f.Shooter,out var same) && same.Owner == nextOwner && same.Magazine == 4
                && same.LastAcceptedShotId == 0,"reconsumed next-life source cannot create a second birth grant");
        }
        using (var f = new Fixture(automaticSourceBoundary:true))
        {
            ConfirmBoundary(f); PublishBoundaryInput(f,BoundaryPacket(f,1000),1000); object clock = GrantClock(f);
            f.Shooter.ModSetSpectating(true); f.Root.Advance(); f.Shooter.ModSetSpectating(false);
            Vector3 body = f.Shooter.Position; Frame(1001);
            f.Shooter.Spawn(body,Vector3.UnitZ,Vector3.UnitY,f.Shooter.NodeRef,respawn:true);
            f.Shooter.ModPlaceAt(body); Fixture.Clean(f.Shooter,200); f.Root.Advance();
            PublishBoundaryInput(f,BoundaryPacket(f,2000),1001);
            Check(!f.Root.TryState(f.Shooter,out _) && f.Pool.Length == 0 && ReferenceEquals(GrantClock(f),clock),
                "spectator retirement and actual next life cannot reset fixed source clock to accept accelerated source jump");
        }
    }
    private static IntentPacket CaptureEngineInput(Fixture f,uint frame,bool shoot = false,bool recordWorld = true,
        uint? ack = null,byte ackSub = 0)
    {
        Frame(frame);
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.GunAnimation))!.SetValue(f.Shooter,GunAnimation.Idle);
        var input = SourcePacket(f.Shooter,frame,f.Aim());
        if (ack.HasValue) { input.AckFrame = ack.Value; input.AckSubFrame = ackSub; }
        if (shoot) input.Buttons |= IntentButtons.Shoot;
        // Owner damage/Prime hints are deliberately false authority evidence.
        input.ShotFlags = IntentPacket.FlagDoubleDamage | IntentPacket.FlagPrimeHunter;
        Check(NetSession.AcceptSlotIntent(0,input) && f.Root.BeginSourceInputPhase(),"engine admission consumes real lifecycle source input " + frame);
        NetHooks.TryApplyRemoteInput(f.Shooter,0); f.Root.CompleteSourceInputPhase();
        if (recordWorld) NetUnlagged.Record(frame); // Actual sampled-world publication, not fabricated pose evidence.
        return input;
    }
    private static void EquipEngine(Fixture f)
    {
        CaptureEngineInput(f,1000);
        Check(f.Process(f.EngineAction(CustomWeaponActionKind.Equip,1000)).Result == WeaponActionResult.Accepted
            && f.Process(f.EngineAction(CustomWeaponActionKind.Optic,1000,optic:1)).Result == WeaponActionResult.Accepted,
            "engine-observed neutral source authorizes equip and optic with no supplied certificate");
    }
    private static void ExecutionBarrier()
    {
        var delivery=new ExecutionDelivery();using var f=new Fixture(engineSources:true,delivery:delivery);
        CaptureEngineInput(f,1000);Frame(1001);
        Check(f.Process(f.EngineAction(CustomWeaponActionKind.Equip,1000)).Result==WeaponActionResult.Accepted
            &&f.Process(f.EngineAction(CustomWeaponActionKind.Optic,1000,optic:1)).Result==WeaponActionResult.Accepted,
            "past engine-observed neutral source settles equip/optic behind explicit delivery reservation fixture");
        CaptureEngineInput(f,1001,shoot:true,ack:1001);Frame(1002);
        var fire=f.EngineAction(CustomWeaponActionKind.Fire,1001,1001,optic:1);
        Check(InspectEngine(f,fire,out var valid)==CustomSourceDecision.Verified&&valid.IsValidFor(fire)
            &&f.Oracle.Supplied.Count==0,"queued execution-barrier Fire has a genuinely engine-derived source before audience closes");
        uint settled=0,committed=0;
        Check(f.Root.TryState(f.Shooter,out var before)&&f.Root.TryStateMetadata(f.Shooter,out _,out settled,out committed),
            "execution-barrier fixture reads canonical state before queued Fire");
        var lives=(Array)typeof(CustomAuthorityCombatAdapter).GetField("_current",Hidden)!.GetValue(f.Root)!;
        var life=lives.GetValue(f.Owner.Slot)!;
        var payments=(CustomLaunchPayments)life.GetType().GetField("Payments",Hidden)!.GetValue(life)!;
        uint highest=payments.HighestSequence;int health=f.Victim.Health;ushort damage=NetDamage.Sequence(f.Victim.SlotIndex);
        Check(f.Root.Offer(fire)==CustomQueueResult.Queued,"execution barrier changes only after exact Fire is already queued");
        delivery.Allowed=false;
        Check(InspectEngine(f,fire,out var unavailable)==CustomSourceDecision.Rejected&&unavailable==default,
            "closed delivery barrier rejects before returning the otherwise verified engine certificate");
        Check(f.Root.TryProcess(f.Shooter,out var denied)&&denied.Action==fire
            &&denied.Disposition==CustomJournalDisposition.SourceDenied&&denied.Result==WeaponActionResult.InvalidLife
            &&denied.Emission==CustomEmissionDisposition.NotAttempted&&denied.Source==null&&denied.Before==null&&denied.After==null,
            "already-bound Fire settles terminal source denial instead of waiting for a favorable audience");
        Check(f.Root.TryState(f.Shooter,out var after)&&after==before&&after.Magazine==4&&after.Reserve==16
            &&after.LastAcceptedShotId==0&&payments.HighestSequence==highest&&!payments.IsQuarantined
            &&f.Root.AcceptedEmissions==0&&f.Root.TerminalContacts==0&&f.Root.PositivePayments==0
            &&delivery.Prepared==0&&delivery.Launches==0&&delivery.Contacts==0&&f.Pool.All(beam=>beam.Lifespan<=0)
            &&f.Victim.Health==health&&NetDamage.Sequence(f.Victim.SlotIndex)==damage,
            "closed audience prepares no observers, advances no resources/payment identity and cannot emit or change HP");
        Check(f.Root.TryStateMetadata(f.Shooter,out _,out uint terminal,out uint source)&&terminal==fire.Sequence
            &&terminal==settled+1&&source==committed&&delivery.Receipts.Last()==denied,
            "source denial advances exactly its nonce while leaving committed source and resources unchanged");
        delivery.Allowed=true;int receipts=delivery.Receipts.Count;
        Check(f.Root.Offer(fire)==CustomQueueResult.Duplicate&&!f.Root.TryProcess(f.Shooter,out _)
            &&f.Root.TryReceipt(fire,out var retry)&&retry==denied&&delivery.Receipts.Count==receipts
            &&f.Root.TryState(f.Shooter,out after)&&after==before,
            "exact denied nonce remains immutable after audience recovery and cannot re-offer or pay");
        CaptureEngineInput(f,1002);CaptureEngineInput(f,1003,shoot:true,ack:1003);Frame(1004);
        var fresh=f.EngineAction(CustomWeaponActionKind.Fire,1003,1003,optic:1);var accepted=f.Process(fresh);
        Check(accepted.Action.Sequence==fire.Sequence+1&&accepted.Result==WeaponActionResult.Accepted
            &&accepted.Emission==CustomEmissionDisposition.Spawned&&accepted.Source is {} certificate&&certificate.IsValidFor(fresh)
            &&delivery.Prepared==1&&delivery.Launches==1&&f.Root.AcceptedEmissions==1,
            "fresh released-to-pressed engine source and next nonce execute once after audience recovery");
        foreach(var beam in f.Pool.Where(beam=>beam.ModCustomShot?.Sequence==fresh.Sequence))
            for(int i=0;i<2&&f.Root.TerminalContacts==0;i++)beam.Process();
        Check(f.Root.PositivePayments==1&&f.Root.TerminalContacts==1&&delivery.Contacts==1&&f.Victim.Health==health-65
            &&f.Root.TryState(f.Shooter,out after)&&after.Magazine==3&&after.Reserve==16,
            "recovered new action makes exactly one real65HP canonical payment without repaying denied action");
        Console.WriteLine("EXECUTION BARRIER SCOPE: real engine source phases, source admission, journal, emission and canonical HP; execution permission and preallocated-result/audience obligations are explicit trusted delivery fixtures, not UDP readiness proof.");
    }
    private static CustomSourceDecision InspectEngine(Fixture f,CustomWeaponAction action,out CustomSourceCertificate source)
        => ((ICustomSourceAdmission)f.Root).Inspect(action,out source);
    private static void EngineAdmissionSuccess(bool doublePickup)
    {
        using var f = new Fixture(engineSources:true); EquipEngine(f);
        uint fireFrame = 1001;
        if (doublePickup)
        {
            var item = ItemSpawnEntity.SpawnItem(ItemType.DoubleDamage,f.Shooter.Position,f.Shooter.NodeRef,6000,f.Scene)
                ?? throw new InvalidOperationException("Native Double item factory refused engine admission fixture.");
            Check(NetPowerUpLedger.TrySpawnKey(item,out var spawn),"actual native Double item registers its immutable spawn incarnation");
            long failures = f.StepFailures; f.Step();
            var grant = NetPowerUpLedger.Ledger!.History.Single(g=>g.Spawn == spawn);
            Check(f.StepFailures == failures && item.DespawnTimer == 0 && grant.ContactFrame == NetSession.NetFrame
                && grant.Life.LifeId == f.Owner.Life && grant.SourceContactFrame == 1000,
                "real complete native player step contacts Double item and records exact grant chronology");
            CaptureEngineInput(f,1001); fireFrame = 1002;
        }
        Fixture.Clean(f.Victim,200); CaptureEngineInput(f,fireFrame,shoot:true);
        Check(f.Root.TryGetSourceObservation(f.Owner,fireFrame,out var observed)
            && observed.Modifiers.DoubleKnowledge == (doublePickup ? TimedGrantKnowledge.Granted : TimedGrantKnowledge.NotGranted),
            "first actual source phase freezes native grant knowledge despite spoofed owner modifier hints");
        var action = f.EngineAction(CustomWeaponActionKind.Fire,fireFrame,fireFrame-1,optic:1);
        Check(InspectEngine(f,action,out var certificate) == CustomSourceDecision.Verified
            && certificate.DoubleDamage == doublePickup && !certificate.PrimeHunter
            && certificate.Body == observed.Body && certificate.Aim == observed.Aim
            && certificate.ObservedWorldFrame == observed.ObservedWorldFrame && certificate.TargetWorldFrame == fireFrame-1,
            "production engine admission derives certificate from exact source ACK geometry and canonical modifiers");
        var receipt = f.Process(action);
        Check(receipt.Result == WeaponActionResult.Accepted && receipt.Emission == CustomEmissionDisposition.Spawned
            && receipt.Source == certificate && f.Oracle.Supplied.Count == 0,"real engine admission emits M7 without any injected source certificate");
        var beam = f.Pool.Single(b=>b.ModCustomShot?.Sequence == action.Sequence);
        for (int step=0; step<2 && f.Root.TerminalContacts == 0; step++) beam.Process();
        int damage = doublePickup ? 130 : 65;
        Check(f.Victim.Health == 200-damage && f.Root.LastOutcome?.Health.BodyRemoved == damage
            && f.Root.PositivePayments == 1 && f.Root.TerminalContacts == 1,
            "engine-admitted real M7 collision pays canonical body damage " + damage);
        var state = new PlayerState(); NetDamage.Write(f.Victim.SlotIndex,ref state);
        Check(state.DamageEventId != 0 && state.AttackerSlot == 255 && state.DamageBeam == 255
            && Enumerable.Range(0,PlayerState.DamageHistory).All(i=>state.EventAt(i).EventId == 0)
            && !NetFireEvents.CaptureLedger(0).TryGet(ShotKey.For(0,action.Sequence),out _,out _,out _),
            "engine admission and custom payment leave native shot ledger and native damage history empty");
        Check(InspectEngine(f,action,out var repeated) == CustomSourceDecision.Verified && repeated == certificate,
            "post-impact engine admission returns identical immutable source certificate");
    }
    private static void EngineAdmissionRejections()
    {
        foreach (string reason in new[] { "ACK","ACK subframe","future observed ACK","future observed subframe","aim","muzzle","held fire","missing prior","denied source","expired source","missing world","missing source" })
        {
            using var f = new Fixture(engineSources:true);
            if (reason != "missing prior") CaptureEngineInput(f,1000,shoot:reason == "held fire");
            if (reason == "denied source") Field(f.Shooter,"_frozenTimer",(ushort)60);
            if (reason != "missing source") CaptureEngineInput(f,1001,shoot:true,recordWorld:reason != "missing world",
                ack:reason == "future observed ACK" ? 1002u : reason == "future observed subframe" ? 1001u : null,
                ackSub:reason == "future observed subframe" ? (byte)1 : (byte)0);
            else Frame(1001);
            var action = f.EngineAction(CustomWeaponActionKind.Fire,1001,1000,optic:1);
            if (reason == "ACK") action = action with { AckFrame = 1001 };
            if (reason == "ACK subframe") action = action with { AckSubFrame = 1 };
            if (reason == "future observed ACK") { action = action with { AckFrame = 1002 }; Frame(1002); }
            if (reason == "future observed subframe") { action = action with { AckFrame = 1001,AckSubFrame = 1 }; Frame(1002); }
            if (reason == "aim") action = action with { Direction = -action.Direction };
            if (reason == "muzzle") action = action with { Origin = action.Origin+Vector3.UnitX };
            if (reason == "expired source") Frame(1066);
            var expected = reason is "missing prior" or "missing world" or "missing source" ? CustomSourceDecision.Pending : CustomSourceDecision.Rejected;
            Check(InspectEngine(f,action,out _) == expected && f.Root.AcceptedEmissions == 0 && f.Victim.Health == 200,
                "engine admission refuses unavailable or contradictory real source without gameplay: " + reason);
        }
    }
    private static void EngineAdmissionReadiness()
    {
        foreach (string reason in new[] { "bootstrap mismatch","manifest mismatch","first source boundary","opened boundary","replacement","invalid replacement" })
        {
            using var f = new Fixture(engineSources:true);
            var context = f.Readiness.Context!.Value;
            if (reason == "first source boundary") f.Readiness.Context = context with { FirstSourceFrame = 1002 };
            if (reason == "opened boundary") f.Readiness.Context = context with { OpenedWorld = 1002 };
            CaptureEngineInput(f,1000); CaptureEngineInput(f,1001,shoot:true);
            var action = f.EngineAction(CustomWeaponActionKind.Fire,1001,1000,optic:1);
            if (reason == "bootstrap mismatch") action = action with { Bootstrap = action.Bootstrap with { Revision = 2 } };
            if (reason == "manifest mismatch") f.Readiness.Context = context with { Ready = context.Ready with { Manifest = default } };
            if (reason is "replacement" or "invalid replacement")
                f.Readiness.Context = context with { PeerIncarnation = reason == "replacement" ? 2ul : 0ul };
            if (reason == "invalid replacement")
            {
                CaptureEngineInput(f,1002);
                f.Readiness.Context = context;
            }
            var expected = reason == "opened boundary" ? CustomSourceDecision.Pending : CustomSourceDecision.Rejected;
            Check(InspectEngine(f,action,out _) == expected && f.Root.AcceptedEmissions == 0
                && (reason != "opened boundary" || !f.Root.TryGetSourceObservation(f.Owner,1001,out _)),
                "engine source admission fences trusted readiness configuration: " + reason);
            if (reason is "replacement" or "invalid replacement")
            {
                f.Readiness.Context = context;
                Check(InspectEngine(f,action,out _) == CustomSourceDecision.Rejected,
                    "restoring replaced readiness cannot reopen invalidated occupant admission: " + reason);
            }
        }
    }
    private static void SourceFacts()
    {
        using var f = new Fixture(); var p = f.Shooter;
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.GunAnimation))!.SetValue(p,GunAnimation.Idle);
        Check(p.ModTryCaptureCustomPrecombatFacts(Vector3.UnitX*3,out var initial) && initial.CanUseWeapon
            && initial.Body == p.Position && initial.Aim == Vector3.UnitX,"source helper captures explicit normalized accepted aim without native cached aim");
        var originalLoad = p.LoadFlags; var flags1 = p.Flags1; var flags2 = p.Flags2;
        void Denied(string label)
            => Check(p.ModTryCaptureCustomPrecombatFacts(Vector3.UnitX,out var facts) && !facts.CanUseWeapon,
                "restricted source is valid geometry with explicit denied permission: " + label);
        p.LoadFlags &= ~LoadFlags.Active; Denied("inactive"); p.LoadFlags = originalLoad;
        p.LoadFlags &= ~LoadFlags.Spawned; Denied("not spawned"); p.LoadFlags = originalLoad;
        p.Health = 0; Denied("dead"); p.Health = 200;
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!.SetValue(p,flags2 | PlayerFlags2.Spectating);
        Denied("spectator"); typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!.SetValue(p,flags2);
        Field(p,"_frozenTimer",(ushort)1); Denied("frozen"); Field(p,"_frozenTimer",(ushort)0);
        Field(p,"_field6D0",true); Denied("input disabled"); Field(p,"_field6D0",false);
        foreach (var flag in new[] { PlayerFlags1.Morphing,PlayerFlags1.Unmorphing })
        { typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1))!.SetValue(p,flags1 | flag); Denied(flag.ToString()); }
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1))!.SetValue(p,flags1);
        p.ModForceForm(true); Denied("alt form"); p.ModForceForm(false);
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.ScanVisor))!.SetValue(p,true);
        Denied("scan visor"); typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.ScanVisor))!.SetValue(p,false);
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.GunAnimation))!.SetValue(p,GunAnimation.UpDown);
        Denied("gun transition"); typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.GunAnimation))!.SetValue(p,GunAnimation.Idle);
        Field(p,"_disruptedTimer",(ushort)1); Denied("disrupted"); Field(p,"_disruptedTimer",(ushort)0);
        typeof(SceneGameState).GetProperty(nameof(SceneGameState.MenuPause))!.SetValue(f.Scene.GameState,true);
        Denied("menu pause"); typeof(SceneGameState).GetProperty(nameof(SceneGameState.MenuPause))!.SetValue(f.Scene.GameState,false);
        typeof(SceneGameState).GetProperty(nameof(SceneGameState.DialogPause))!.SetValue(f.Scene.GameState,true);
        Denied("dialog pause"); typeof(SceneGameState).GetProperty(nameof(SceneGameState.DialogPause))!.SetValue(f.Scene.GameState,false);
        var match = f.Scene.GameState.MatchState; f.Scene.GameState.MatchState = MatchState.Ending;
        Denied("match not in progress"); f.Scene.GameState.MatchState = match;
        int ammo = p.EquipInfo.Ammo; ushort cooldown = p.TimeSinceShot; var weapon = p.CurrentWeapon;
        Vector3 cachedAim = p.ModGunVector;
        foreach (var aim in new[] { Vector3.Zero,new Vector3(float.NaN,0,0),new Vector3(float.PositiveInfinity,0,0) })
            Check(!p.ModTryCaptureCustomPrecombatFacts(aim,out _),"invalid source aim cannot become observation");
        Vector3 position = p.Position;
        try
        {
            p.Position = new(float.NaN,position.Y,position.Z);
            Check(!p.ModTryCaptureCustomPrecombatFacts(Vector3.UnitX,out _),"nonfinite actual body cannot become source observation");
        }
        finally { p.Position = position; }
        Check(p.ModTryCaptureCustomPrecombatFacts(Vector3.UnitX,out var restored) && restored.CanUseWeapon,
            "restoring source restrictions returns valid facts without mutating native resources");
        Check(p.EquipInfo.Ammo == ammo && p.TimeSinceShot == cooldown && p.CurrentWeapon == weapon && p.ModGunVector == cachedAim,
            "read-only source helper leaves native ammunition cooldown weapon and cached aim unchanged");
    }

    private static (ShotKey Key,IntentPacket Shooter,IntentPacket Victim) NativeSourceInput(Fixture f)
    {
        Frame(1001); Fixture.Clean(f.Shooter,200); Fixture.Clean(f.Victim,50);
        Check(f.Root.BindLife(f.Victim,1001,true),"native barrier fixture binds second independent source history");
        // Explicit native inventory fixture only. No natural pickup or source-proof
        // admission is claimed; the carrier, ledger, factory and impact are real.
        f.Shooter.ModArmWeapon(BeamType.Imperialist);
        foreach (var player in new[] { f.Shooter,f.Victim })
        {
            typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.GunAnimation))!.SetValue(player,GunAnimation.Idle);
            foreach (var beam in player.EquipInfo.Beams) { beam.Destroy(); f.Scene.RemoveEntity(beam); }
        }
        Vector3 aim = f.Aim();
        var shooter = SourcePacket(f.Shooter,1001,aim);
        shooter.WeaponSelect = (byte)BeamType.Imperialist;
        shooter.Buttons |= IntentButtons.Shoot | IntentButtons.ZoomedState;
        shooter.HasFireEvents = true; shooter.FireEventCount = 1;
        shooter.FireEvents[0] = new FireEvent(1,1001,1000,0,FireEventKind.PressFire,
            (byte)BeamType.Imperialist,0,FireEvent.ScopedStateBit,FireEvent.FlagPose,
            f.Shooter.Position+Vector3.UnitY*.4f,aim,aim,aim,SourceLife:NetPlayerLifecycle.Get(0));
        shooter.ShotInputCount = 2;
        shooter.ShotInputs[1] = ShotInputHistory.Sample((byte)BeamType.Imperialist,false,false,true);
        shooter.ShotInputs[0] = ShotInputHistory.Sample((byte)BeamType.Imperialist,true,true,true);
        var victim = SourcePacket(f.Victim,1001,-Vector3.UnitZ);
        Check(NetSession.AcceptSlotIntent(0,shooter) && NetSession.AcceptSlotIntent(1,victim),
            "actual lifecycle intake accepts native firing carrier and second actor input");
        return (ShotKey.For(0,1),shooter,victim);
    }

    private static void NativeSourceInputBarrier(bool abort)
    {
        using var f = new Fixture(); var input = NativeSourceInput(f);
        var ledger = NetFireEvents.CaptureLedger(0);
        Check(ledger.TryGet(input.Key,out _,out var disposition,out _) && disposition == ShotDisposition.Pending,
            "real native event is Pending before source input pass; abort=" + abort);
        Check(f.Root.BeginSourceInputPhase(),"native firing fixture begins source phase; abort=" + abort);
        NetHooks.TryApplyRemoteInput(f.Shooter,0);
        Check(ledger.TryGet(input.Key,out _,out disposition,out _) && disposition == ShotDisposition.Pending && f.Victim.Health == 50,
            "first actor's accepted native projectile remains Pending while later actor input is unobserved");
        NetHooks.TryApplyRemoteInput(f.Victim,1);
        Check(ledger.TryGet(input.Key,out _,out disposition,out _) && disposition == ShotDisposition.Pending
            && !f.Root.TryGetSourceObservation(f.Owner,1001,out _) && !f.Root.TryGetSourceObservation(SourceOwner(f.Victim),1001,out _),
            "complete real input pass retains native Pending and publishes no partial source history");
        f.Root.CompleteSourceInputPhase(capture: !abort);
        Check(ledger.TryGet(input.Key,out _,out disposition,out var reason) && disposition == ShotDisposition.Fired,
            "barrier completion drains actual native ledger to Fired; abort=" + abort + " reason=" + reason);
        Check(f.Shooter.EquipInfo.Beams.Any(b=>b.ModLaunchKey == input.Key && b.ModCustomShot == null && b.Equip != null
                && !ReferenceEquals(b.Equip,f.Shooter.EquipInfo) && !f.Pool.Contains(b)),
            "deferred native drain uses actual projectile factory and detached equipment outside custom pool");
        Check(f.Victim.Health == 0 && NetDamage.Sequence(f.Victim.SlotIndex) != 0 && f.Root.PositivePayments == 0,
            "actual deferred native precision impact kills target through canonical native damage only");
        var victimOwner = SourceOwner(f.Victim);
        if (abort)
        {
            Check(!f.Root.TryGetSourceObservation(f.Owner,1001,out _) && !f.Root.TryGetSourceObservation(victimOwner,1001,out _),
                "aborted source phase drains native fire without publishing either staged observation");
        }
        else
        {
            Check(f.Root.TryGetSourceObservation(f.Owner,1001,out var first) && first.Input.ReportedAim == input.Shooter.Aim
                && first.Body == input.Shooter.Position && first.CanUseWeapon,
                "native firing actor source freezes accepted body aim and permission before actual drain");
            Check(f.Root.TryGetSourceObservation(victimOwner,1001,out var second) && second.Body == input.Victim.Position
                && second.CanUseWeapon && !f.Victim.ModIsInPlay,
                "later actor source was frozen alive before lower-index native shot killed that actor");
            f.Shooter.ModPlaceAt(input.Shooter.Position+Vector3.UnitY); Field(f.Shooter,"_frozenTimer",(ushort)60);
            Check(f.Root.TryGetSourceObservation(f.Owner,1001,out var unchangedFirst) && unchangedFirst == first
                && f.Root.TryGetSourceObservation(victimOwner,1001,out var unchangedSecond) && unchangedSecond == second,
                "both pre-projectile observations stay immutable after native death movement and freeze");
        }
        Check(f.Root.BeginSourceInputPhase(),"completed or aborted barrier releases ownership for fresh phase");
        f.Root.CompleteSourceInputPhase(capture:false);
        Check(ledger.TryGet(input.Key,out _,out disposition,out _) && disposition == ShotDisposition.Fired,
            "empty subsequent abort cannot replay or erase completed native emission");
    }

    private static void DisposeDuringSourceInputPhase()
    {
        using var f = new Fixture(); var input = NativeSourceInput(f); var runtime = f.Root;
        var ledger = NetFireEvents.CaptureLedger(0);
        Check(runtime.BeginSourceInputPhase(),"scene disposal fixture begins pending source phase");
        NetHooks.TryApplyRemoteInput(f.Shooter,0); NetHooks.TryApplyRemoteInput(f.Victim,1);
        Check(ledger.TryGet(input.Key,out _,out var before,out _) && before == ShotDisposition.Pending,
            "scene disposal starts with real native emission still deferred");
        f.Scene.DoCleanup();
        runtime.CompleteSourceInputPhase();
        Check(f.Scene.CustomCombat == null && !runtime.IsEligible && !runtime.BeginSourceInputPhase()
            && !runtime.TryGetSourceObservation(f.Owner,1001,out _),
            "actual scene disposal discards staged source history and prevents phase restart");
        var accepted = (Array)typeof(CustomAuthorityCombatAdapter).GetField("_acceptedInputs",Hidden)!.GetValue(runtime)!;
        Check(!(bool)typeof(CustomAuthorityCombatAdapter).GetField("_inputPhase",Hidden)!.GetValue(runtime)!
            && (byte)typeof(CustomAuthorityCombatAdapter).GetField("_deferredNativeDrains",Hidden)!.GetValue(runtime)! == 0
            && accepted.Cast<object?>().All(value=>value == null),
            "disposed phase retains no actor tokens or deferred native ownership");
        Check(!ledger.TryGet(input.Key,out _,out var after,out _) || after != ShotDisposition.Fired,
            "post-disposal completion cannot emit the previously pending native shot");
    }
    private static void SourceInputPhase()
    {
        using var f = new Fixture(); Frame(1001);
        Check(f.Root.BindLife(f.Victim,1001,true),"second actual actor binds independent source history");
        var shooter = SourcePacket(f.Shooter,1001,Vector3.UnitX);
        var victim = SourcePacket(f.Victim,1001,-Vector3.UnitZ);
        Check(NetSession.AcceptSlotIntent(0,shooter) && NetSession.AcceptSlotIntent(1,victim),"actual lifecycle carrier intake accepts two source inputs");
        Check(f.Root.BeginSourceInputPhase() && !f.Root.BeginSourceInputPhase(),"source phase begins once without reentrant reset");
        Check(NetHooks.TryApplyRemoteInput(f.Shooter,0) && NetHooks.TryApplyRemoteInput(f.Victim,1),"actual remote placement and control paths own both actors");
        Check(!f.Root.TryGetSourceObservation(f.Owner,1001,out _),"staged input has no observation before complete phase barrier");
        f.Root.CompleteSourceInputPhase();
        var targetOwner = SourceOwner(f.Victim);
        Check(f.Root.TryGetSourceObservation(f.Owner,1001,out var first) && first.Input.ReportedAim == Vector3.UnitX
            && first.Aim == Vector3.UnitX && first.Body == shooter.Position && first.ObservedWorldFrame == 1001,
            "frozen source retains actual accepted position and explicit current input aim");
        Check(f.Root.TryGetSourceObservation(targetOwner,1001,out var target) && target.Aim == -Vector3.UnitZ,
            "complete phase freezes every accepted actor source");
        f.Shooter.ModPlaceAt(shooter.Position+Vector3.UnitY); Field(f.Shooter,"_frozenTimer",(ushort)60);
        f.Shooter.Health = 1;
        Check(f.Root.TryGetSourceObservation(f.Owner,1001,out var unchanged) && unchanged == first,
            "subsequent engine movement freeze and health changes cannot rewrite original source");
        Field(f.Shooter,"_frozenTimer",(ushort)0); Frame(1002);
        Check(f.Root.BeginSourceInputPhase(),"next source phase begins");
        NetHooks.TryApplyRemoteInput(f.Shooter,0); f.Root.CompleteSourceInputPhase();
        Check(f.Root.TryGetSourceObservation(f.Owner,1001,out var duplicate) && duplicate == first,
            "reconsumed same accepted input cannot refresh source age or recapture facts");
        Frame(1065); Check(f.Root.TryGetSourceObservation(f.Owner,1001,out _),"first source observation remains available at exact64-tick age");
        Frame(1066); Check(!f.Root.TryGetSourceObservation(f.Owner,1001,out _),"reconsumption cannot keep observation alive beyond64 ticks");
    }
    private static void AutomaticSourceInputPhase()
    {
        using var f = new Fixture(); Frame(1001);
        Check(f.Root.BindLife(f.Victim,1001,true),"automatic renderer fixture binds second actor source life");
        var shooter = SourcePacket(f.Shooter,1001,Vector3.UnitX);
        var victim = SourcePacket(f.Victim,1001,-Vector3.UnitZ);
        Check(NetSession.AcceptSlotIntent(0,shooter) && NetSession.AcceptSlotIntent(1,victim),
            "automatic renderer fixture submits actual accepted input carriers without opening a manual phase");
        long failures = f.StepFailures;
        f.Step();
        Check(f.StepFailures == failures,"actual ServerSim/Scene simulation step completes without swallowed step failure");
        Check(f.Root.TryGetSourceObservation(f.Owner,1001,out var first) && first.Body == shooter.Position
            && first.Aim == shooter.Aim && first.ObservedWorldFrame == NetSession.NetFrame
            && f.Root.TryGetSourceObservation(SourceOwner(f.Victim),1001,out var second)
            && second.Body == victim.Position && second.Aim == victim.Aim,
            "production Renderer hooks automatically freeze both remote inputs during the real simulation step");
        Check(f.Root.BeginSourceInputPhase(),"automatic renderer completion leaves no live input barrier");
        f.Root.CompleteSourceInputPhase(capture:false);
    }
    private static void RejectedSourceInputs()
    {
        foreach (string invalid in new[] { "zero position","nonfinite position","zero aim","wrong life","frozen placement","stale arrival" })
        {
            using var f = new Fixture(); Frame(1001); var packet = SourcePacket(f.Shooter,1001,Vector3.UnitX);
            if (invalid == "zero position") packet.Position = Vector3.Zero;
            if (invalid == "nonfinite position") packet.Position = new(float.NaN,0,0);
            if (invalid == "zero aim") packet.Aim = Vector3.Zero;
            if (invalid == "wrong life") packet.LifeId++;
            bool accepted = NetSession.AcceptSlotIntent(0,packet);
            if (invalid == "wrong life") Check(!accepted,"actual lifecycle intake refuses wrong source life");
            else Check(accepted,"source carrier reaches actual engine geometry guards: " + invalid);
            if (invalid == "frozen placement") Field(f.Shooter,"_frozenTimer",(ushort)1);
            if (invalid == "stale arrival") Frame(1032);
            Check(f.Root.BeginSourceInputPhase(),"rejection fixture opens actual source phase");
            NetHooks.TryApplyRemoteInput(f.Shooter,0); f.Root.CompleteSourceInputPhase();
            bool trustedDenial = invalid is "zero position" or "frozen placement";
            Check(!f.Root.TryGetSourceObservation(f.Owner,1001,out _) && f.Root.TryState(f.Shooter,out var state)
                && state.Tick == (trustedDenial ? 1001u : 1000u)
                && (!trustedDenial || state.Phase == CustomWeaponPhase.Unequipped),
                "unsuccessful source geometry never mints pose; only valid fresh raw input advances denied permission: " + invalid);
        }
    }

    private static void FrozenSourceCancelsReload()
    {
        using var f = new Fixture(); f.Fire(); Frame(1001);
        var reload = f.Process(f.Action(CustomWeaponActionKind.Reload));
        Check(reload.Result == WeaponActionResult.Accepted && f.Root.TryState(f.Shooter,out var reloading)
            && reloading.Phase == CustomWeaponPhase.Reloading && reloading.Magazine == 3 && reloading.Reserve == 16,
            "explicit trusted action fixture starts one-round reload before real frozen source interruption");
        Frame(1002); Field(f.Shooter,"_frozenTimer",(ushort)60);
        var frozen = SourcePacket(f.Shooter,1002,Vector3.UnitX);
        Check(NetSession.AcceptSlotIntent(0,frozen) && f.Root.BeginSourceInputPhase(),
            "real fresh lifecycle input enters frozen-placement source phase");
        NetHooks.TryApplyRemoteInput(f.Shooter,0); f.Root.CompleteSourceInputPhase();
        Check(!f.Root.TryGetSourceObservation(f.Owner,1002,out _) && f.Root.TryState(f.Shooter,out var interrupted)
            && interrupted.Phase == CustomWeaponPhase.Unequipped && interrupted.ReloadCompleteTick == 0
            && interrupted.Magazine == 3 && interrupted.Reserve == 16,
            "frozen placement supplies trusted negative permission that cancels reload without accepted pose or ammo transfer");
        Field(f.Shooter,"_frozenTimer",(ushort)0); Frame(1003);
        Check(f.Root.BeginSourceInputPhase(),"thaw fixture begins reconsumption phase");
        NetHooks.TryApplyRemoteInput(f.Shooter,0); f.Root.CompleteSourceInputPhase();
        Check(!f.Root.TryGetSourceObservation(f.Owner,1002,out _),
            "reconsuming thawed actor cannot promote previously denied source frame into accepted geometry");
        Frame(1140);
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.GunAnimation))!.SetValue(f.Shooter,GunAnimation.Idle);
        var resumed = SourcePacket(f.Shooter,1140,Vector3.UnitX);
        Check(NetSession.AcceptSlotIntent(0,resumed) && f.Root.BeginSourceInputPhase(),
            "new real biped source input resumes after original reload deadline");
        NetHooks.TryApplyRemoteInput(f.Shooter,0); f.Root.CompleteSourceInputPhase();
        Check(f.Root.TryGetSourceObservation(f.Owner,1140,out var source) && source.CanUseWeapon
            && f.Root.TryState(f.Shooter,out var projected) && projected.Magazine == 3 && projected.Reserve == 16
            && projected.ReloadCompleteTick == 0,
            "new accepted source preserves earlier freeze cancellation past reload deadline");
        var optic = f.Process(f.Action(CustomWeaponActionKind.Optic,optic:1));
        Check(optic.Result == WeaponActionResult.Accepted && f.Root.TryState(f.Shooter,out var committed)
            && committed.Magazine == 3 && committed.Reserve == 16 && committed.ReloadCompleteTick == 0,
            "later committed trusted action cannot complete reload canceled by actual frozen engine input");
    }
}
