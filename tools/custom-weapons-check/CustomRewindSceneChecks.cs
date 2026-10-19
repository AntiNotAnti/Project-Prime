using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Asset-backed scope seam only. Launch/certificate/resource records are explicit
// test fixtures, not network admission, historical source proof or trade proof.
// No custom live projectile is authorized; native projectile Process exercises
// the common explicit-pool catch-up path separately.
internal static class CustomRewindSceneChecks
{
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool value,string label)
    {
        if (!value) throw new InvalidOperationException("Custom rewind scene: " + label);
        _checks++; Console.WriteLine("CUSTOM REWIND PASS " + label);
    }
    private static T Field<T>(Type type, string name, object? instance = null)
        => (T)type.GetField(name,instance == null ? StaticHidden : Hidden)!.GetValue(instance)!;
    private static void Frame(uint value)
        => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null,value);
    private static bool Idle() => !Field<bool>(typeof(NetUnlagged),"_inProgress")
        && !Field<bool>(typeof(NetUnlagged),"_reconciled") && Field<object?>(typeof(NetUnlagged),"_shooter") == null
        && Field<int>(typeof(NetUnlagged),"_rewind") == 0 && Field<object?>(typeof(NetUnlagged),"_shotBeams") == null
        && Field<object?>(typeof(NetUnlagged),"_customTiming") == null;
    private readonly record struct Context(bool Active, bool Reconciled, object? Shooter, int Depth,
        double Target, object? Pool, object? Timing);
    private static Context ContextNow() => new(Field<bool>(typeof(NetUnlagged),"_inProgress"),
        Field<bool>(typeof(NetUnlagged),"_reconciled"),Field<object?>(typeof(NetUnlagged),"_shooter"),
        Field<int>(typeof(NetUnlagged),"_rewind"),Field<double>(typeof(NetUnlagged),"_shotTargetFrame"),
        Field<object?>(typeof(NetUnlagged),"_shotBeams"),Field<object?>(typeof(NetUnlagged),"_customTiming"));
    private sealed record World(HistoricalCollisionState Shooter, HistoricalCollisionState Target,
        (INetRewindableGeometry Object,NetGeometryState State)[] Geometry);
    private readonly record struct Flight(Vector3 Position, Vector3 Velocity, float Age, float Life,
        BeamFlags Flags, ShotKey Key, uint Launch);
    private static Flight FlightNow(BeamProjectileEntity beam)
        => new(beam.Position,beam.Velocity,beam.Age,beam.Lifespan,beam.Flags,beam.ModLaunchKey,beam.ModLaunchFrame);

    private sealed class Fixture : IDisposable
    {
        private readonly ServerSim _sim = new();
        private readonly List<BeamProjectileEntity[]> _privatePools = new();
        private uint _sequence;
        internal readonly Scene Scene;
        internal readonly PlayerEntity Shooter, Target;
        internal readonly Vector3 Base, TargetBase;
        internal readonly WeaponDefinition Definition = RiftlineM7.Create();
        internal readonly BeamProjectileEntity[] CustomPool;
        internal Fixture()
        {
            var roster = RosterPacket.Create();
            roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
            for (byte slot=0; slot<2; slot++)
            {
                roster.Slots[slot] = slot; roster.Generations[slot] = 1;
                roster.Hunters[slot] = (byte)Hunter.Samus; roster.Names[slot] = "Custom rewind fixture " + slot;
            }
            try
            {
                Check(_sim.Start("MP3 PROVING GROUND",GameMode.Battle,2,_=>{},()=>{},roster:roster),"real asset ServerSim starts");
                NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                    RoomKey = "MP3 PROVING GROUND", Mode = (byte)GameMode.Battle },false);
                NetSession.ApplyRoster(roster);
                Scene = _sim.EvidenceScene!;
                Scene.GameState.MatchTime = -1; Scene.GameState.PointGoal = 10000;
                Scene.GameState.EnhancedHunters = false; Scene.GameState.BalancedMode = false; Scene.GameState.PrimeHunter = -1;
                foreach (var item in Scene.GetItemInstanceEntities()) item.DespawnTimer = 0;
                for (int i=0; i<120; i++) _sim.Step();
                Shooter = Scene.Players.Items[0]; Target = Scene.Players.Items[1];
                Base = Shooter.Position; TargetBase = Base+new Vector3(0,0,-4);
                Check(NetSession.IsAuthority && NetSession.LocalSlot == -1 && !Scene.Services.IsReplica
                    && !Shooter.IsBot && Shooter.ModIsInPlay && Target.ModIsInPlay
                    && ShotKey.For(Shooter.SlotIndex,0) is { Generation: >0, LifeId: >0 },
                    "genuine dedicated remote shooter and target have current engine lives");
                CustomPool = NewPool(8);
            }
            catch { _sim.Stop(); NetSession.Stop(); throw; }
        }
        internal BeamProjectileEntity[] NewPool(int size)
        {
            var pool = new BeamProjectileEntity[size];
            for (int i=0; i<size; i++) pool[i] = new(Scene);
            _privatePools.Add(pool); return pool;
        }
        internal void Prepare()
        {
            NetUnlagged.AbortShot();
            foreach (var pool in _privatePools)
                foreach (var beam in pool) { beam.Destroy(); Scene.RemoveEntity(beam); }
            foreach (var beam in Shooter.EquipInfo.Beams) { beam.Destroy(); Scene.RemoveEntity(beam); }
            NetUnlagged.Reset(); NetFireEvents.Reset();
            NetUnlagged.Enabled = true; NetUnlagged.MaxRewindFrames = 45; NetDynamicGeometryHistory.ProductionEnabled = true;
            Shooter.Health = Target.Health = 999; Shooter.ModForceForm(false); Target.ModForceForm(false);
            Shooter.Controls.ClearAll(); Target.Controls.ClearAll(); Shooter.ModPlaceAt(Base);
            for (uint frame=1000; frame<1010; frame++)
            {
                Frame(frame); Target.ModPlaceAt(Historical(frame));
                NetUnlagged.Record(frame);
            }
            Frame(1010); Target.ModPlaceAt(TargetBase+new Vector3(20,0,0));
            var key = ShotKey.For(Shooter.SlotIndex,0);
            NetSession.RemoteIntents[Shooter.SlotIndex] = new IntentPacket { MatchId = key.MatchId, AuthorityEpoch = key.AuthorityEpoch,
                SlotGeneration = key.Generation, LifeId = key.LifeId, Frame = 1010, AckFrame = 1008,
                Position = Shooter.Position, Aim = Vector3.UnitX, Buttons = IntentButtons.InPlayState };
            NetSession.RemoteIntentValid[Shooter.SlotIndex] = true;
            Check(Idle() && !NetFireEvents.TryTiming(Shooter,out _) && NetFireEvents.CaptureLedger(Shooter.SlotIndex).Pending == 0,
                "scope fixture has no native FireEvent and a deliberately different remote ACK");
        }
        internal Vector3 Historical(double frame) => TargetBase+new Vector3((float)((frame-1000)*.25),0,0);
        internal (CustomAcceptedLaunch Launch,EquipInfo Equip) Launch(uint requested = 1004, byte fraction = 0)
        {
            var key = ShotKey.For(Shooter.SlotIndex,0);
            var owner = new WeaponOwnerContext(key.MatchId,key.AuthorityEpoch,(byte)key.ShooterSlot,key.Generation,key.LifeId);
            uint sequence = ++_sequence;
            var bootstrap = new WorldBootstrapIdentity(new(key.MatchId,key.AuthorityEpoch,1),1,key.Generation,1010,1,1);
            var manifest = new CustomWeaponManifest(new[] { Definition });
            var action = new CustomWeaponAction(bootstrap,manifest.Digest,owner.Slot,owner.Life,new(10),sequence,1010,
                requested,fraction,CustomWeaponActionKind.Fire,0,Shooter.Position+Vector3.UnitY*.5f,Vector3.UnitX);
            // Deliberate trusted-admission stub. The scope is not a proof producer.
            var source = new CustomSourceCertificate(action,1010,requested,fraction,Shooter.Position,Vector3.UnitX,true,false,false);
            var state = new WeaponRuntimeState(Definition,owner,1010); state.SetEquipped(1010,true);
            var before = state.Snapshot();
            Check(state.TryFire(1010,sequence,true) == WeaponActionResult.Accepted,"fixture supplies exact4-to3 accepted resource transition");
            var launch = new CustomAcceptedLaunch(Definition,source,before,state.Snapshot());
            var equip = new EquipInfo(CustomPrecisionAdapter.Build(Definition.Mechanics),CustomPool)
                { InfiniteAmmo = true, CustomShot = CustomShotSource.FromLaunch(launch) };
            return (launch,equip);
        }
        internal World Capture()
        {
            var room = Field<NetDynamicGeometryHistory>(typeof(NetDynamicGeometryHistory),"_room");
            var objects = Field<INetRewindableGeometry[]>(typeof(NetDynamicGeometryHistory),"_objects",room);
            return new(Shooter.ModCaptureCollisionState(),Target.ModCaptureCollisionState(),
                objects.Select(o=>(o,o.CaptureNetworkCollisionState())).ToArray());
        }
        internal bool Unchanged(World world) => Shooter.ModCaptureCollisionState() == world.Shooter
            && Target.ModCaptureCollisionState() == world.Target
            && world.Geometry.All(g=>g.Object.CaptureNetworkCollisionState() == g.State);
        internal void RemoveHistory(uint frame, bool geometry)
        {
            if (geometry)
            {
                var room = Field<NetDynamicGeometryHistory>(typeof(NetDynamicGeometryHistory),"_room");
                Field<bool[]>(typeof(NetDynamicGeometryHistory),"_valid",room)[frame%NetDynamicGeometryHistory.Depth] = false;
            }
            else Field<uint[]>(typeof(NetUnlagged),"_stamp")[frame%NetUnlagged.HistoryFrames] = 0;
        }
        public void Dispose()
        {
            try { NetUnlagged.AbortShot(); }
            finally
            {
                try
                {
                    foreach (var pool in _privatePools)
                        foreach (var beam in pool) { beam.Destroy(); Scene.RemoveEntity(beam); }
                }
                finally { try { _sim.Stop(); } finally { NetSession.Stop(); } }
            }
        }
    }

    internal static int Run(string data)
    {
        _checks = 0;
        string directory = Directory.GetCurrentDirectory();
        bool enabled = NetUnlagged.Enabled, pressAge = NetUnlagged.PressAgeEnabled;
        int maximum = NetUnlagged.MaxRewindFrames, players = PlayerEntity.MaxPlayers;
        bool geometry = NetDynamicGeometryHistory.ProductionEnabled, shadow = NetDynamicGeometryHistory.ShadowEnabled;
        bool force = MapAudit.ForceEveryone, script = NetTestScript.Enabled;
        var proof = NetShotProof.Mode; var timed = NetTimedShotProof.Mode;
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data)); Paths.UpdatePaths(); Paths.ChooseMphPath();
            Headless.Enter(); MapAudit.ForceEveryone = true; NetTestScript.Enabled = false;
            NetShotProof.Mode = ShotProofMode.Off; NetTimedShotProof.Mode = TimedGrantProofMode.Off;
            NetDynamicGeometryHistory.ProductionEnabled = true; NetDynamicGeometryHistory.ShadowEnabled = false;
            Check(ServerSim.Available(out string reason),"real game assets and synthetic input available: " + reason);
            using var fixture = new Fixture();
            TimingAndRestoration(fixture); MissingHistory(fixture); InvalidAndLifecycle(fixture); NativePrivateCatchUp(fixture);
            NativeJoinIsolation(fixture);
            Console.WriteLine($"PASS {_checks} asset-backed custom rewind scope checks.");
            Console.WriteLine("SCOPE: real ServerSim bodies/collision history/rewind restoration and native private-pool Process; custom certificates and native-join metadata/payment receipts explicitly seeded. NOT_RUN: custom live Spawn, transport/readiness, historical source admission, actual multiplayer damage/trade/payment claims, replay or GL/audio.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("CUSTOM REWIND FAIL " + error); return 1; }
        finally
        {
            try { NetUnlagged.AbortShot(); }
            finally
            {
                NetSession.Stop(); NetUnlagged.Enabled = enabled; NetUnlagged.PressAgeEnabled = pressAge;
                NetUnlagged.MaxRewindFrames = maximum; PlayerEntity.MaxPlayers = players;
                NetDynamicGeometryHistory.ProductionEnabled = geometry; NetDynamicGeometryHistory.ShadowEnabled = shadow;
                NetTestScript.Enabled = script; NetShotProof.Mode = proof; NetTimedShotProof.Mode = timed;
                MapAudit.ForceEveryone = force; Directory.SetCurrentDirectory(directory);
            }
        }
    }
    private static void TimingAndRestoration(Fixture f)
    {
        foreach (var test in new[] { (Frame:1004u,Sub:(byte)0,Ceiling:45,Effective:1004d),
            (Frame:1004u,Sub:(byte)128,Ceiling:45,Effective:1004.5d),
            (Frame:1002u,Sub:(byte)128,Ceiling:3,Effective:1007d) })
        {
            f.Prepare(); NetUnlagged.MaxRewindFrames = test.Ceiling;
            var supplied = f.Launch(test.Frame,test.Sub); var before = f.Capture();
            Check(NetUnlagged.TryBeginCustomShot(f.Shooter,supplied.Launch,supplied.Equip,out var timing),"explicit integer/fractional/clamped custom scope begins");
            try
            {
                Check(timing.AuthorityFrame == 1010 && timing.RequestedFrame == test.Frame && timing.RequestedSubFrame == test.Sub
                    && timing.EffectiveFrame == test.Effective && timing.RewindFrames == 1010-test.Effective
                    && NetUnlagged.LaunchFrameFor(f.Shooter) == test.Frame,"requested launch and effective collision timing remain distinct");
                Check((f.Target.Position-f.Historical(test.Effective)).LengthSquared < .000001f
                    && f.Shooter.ModCaptureCollisionState() == before.Shooter
                    && ReferenceEquals(ContextNow().Pool,supplied.Equip.Beams),"real historical target moves while shooter and private pool remain exact");
                Check(!NetFireEvents.TryTiming(f.Shooter,out _) && NetSession.RemoteIntents[f.Shooter.SlotIndex].AckFrame == 1008,
                    "custom timing needs no fabricated native event and ignores differing latest remote ACK");
                var active = ContextNow(); var target = f.Target.ModCaptureCollisionState(); var nested = f.Launch(1006);
                Check(!NetUnlagged.TryBeginCustomShot(f.Shooter,nested.Launch,nested.Equip,out var refused)
                    && refused == default && ContextNow() == active && f.Target.ModCaptureCollisionState() == target,
                    "nested custom refusal preserves active timing/pool and exact rewound collision state");
                Check(BeamProjectileEntity.Spawn(f.Shooter,supplied.Equip,supplied.Launch.Source.Action.Origin,
                    Vector3.UnitX,BeamSpawnFlags.NoMuzzle,f.Shooter.NodeRef,f.Scene) == BeamResultFlags.NoSpawn
                    && supplied.Equip.Beams.All(b=>b.Lifespan == 0),"scope seam does not bypass existing custom live Spawn refusal");
            }
            finally { NetUnlagged.EndShot(f.Shooter); }
            Check(Idle() && f.Unchanged(before),"EndShot restores exact player and mutable geometry state and releases context");
        }
        foreach (bool disabled in new[] { false,true })
        {
            f.Prepare(); NetUnlagged.Enabled = !disabled;
            var supplied = f.Launch(disabled ? 1004u : 1010u); var before = f.Capture();
            Check(NetUnlagged.TryBeginCustomShot(f.Shooter,supplied.Launch,supplied.Equip,out var timing)
                && timing.EffectiveFrame == 1010 && timing.RewindFrames == 0
                && NetUnlagged.LaunchFrameFor(f.Shooter) == (disabled ? 1004u : 1010u)
                && f.Unchanged(before),"zero-rewind/disabled scope still stamps requested launch without moving bodies");
            NetUnlagged.EndShot(f.Shooter);
            Check(Idle() && f.Unchanged(before),"zero-rewind scope releases context through EndShot");
        }
    }
    private static void MissingHistory(Fixture f)
    {
        foreach (bool geometry in new[] { false,true })
        foreach (var sample in new[] { (Frame:1004u,Fraction:(byte)0,Hole:1004u,Label:"initial"),
            (Frame:1004u,Fraction:(byte)0,Hole:1006u,Label:"intermediate"),
            (Frame:1004u,Fraction:(byte)128,Hole:1005u,Label:"fractional upper") })
        {
            f.Prepare(); var supplied = f.Launch(sample.Frame,sample.Fraction); var before = f.Capture();
            f.RemoveHistory(sample.Hole,geometry);
            Check(!NetUnlagged.TryBeginCustomShot(f.Shooter,supplied.Launch,supplied.Equip,out var timing)
                && timing == default && Idle() && f.Unchanged(before),"missing " + sample.Label + (geometry ? " dynamic" : " player") + " history refuses before changing world");
        }
        f.Prepare(); var upperNow = f.Launch(1009,128); var unchanged = f.Capture();
        Check(!NetUnlagged.TryBeginCustomShot(f.Shooter,upperNow.Launch,upperNow.Equip,out _)
            && Idle() && f.Unchanged(unchanged),"fractional upper equal current frame still requires published history");
    }
    private static void InvalidAndLifecycle(Fixture f)
    {
        f.Prepare(); var supplied = f.Launch(); var original = f.Capture();
        Check(!NetUnlagged.TryBeginCustomShot(f.Shooter,default,supplied.Equip,out var absent)
            && absent == default && Idle() && f.Unchanged(original),"default launch rejects without throwing or changing context");
        foreach (string fault in new[] { "owner","epoch","generation","resource debit","accepted sequence" })
        {
            var launch = supplied.Launch; var action = launch.Source.Action;
            if (fault == "owner") action = action with { Slot = (byte)f.Target.SlotIndex };
            else if (fault == "epoch") action = action with { Bootstrap = action.Bootstrap with
                { Start = action.Bootstrap.Start with { AuthorityEpoch = action.Bootstrap.Start.AuthorityEpoch+1 } } };
            else if (fault == "generation") action = action with { Bootstrap = action.Bootstrap with
                { SlotGeneration = (ushort)(action.Bootstrap.SlotGeneration+1) } };
            else if (fault == "resource debit") launch = launch with { After = launch.After with { Magazine = launch.Before.Magazine } };
            else launch = launch with { After = launch.After with { LastAcceptedShotId = action.Sequence+1 } };
            launch = launch with { Source = launch.Source with { Action = action } };
            Check(!NetUnlagged.TryBeginCustomShot(f.Shooter,launch,supplied.Equip,out _) && Idle() && f.Unchanged(original),
                "invalid " + fault + " cannot enter custom scope");
        }
        var unsafePools = new List<(string Label,BeamProjectileEntity[] Pool)>
        {
            ("shooter native array",f.Shooter.EquipInfo.Beams),
            ("shooter native array copy",f.Shooter.EquipInfo.Beams.ToArray()),
            ("shooter native8-entry subset",f.Shooter.EquipInfo.Beams.Take(8).ToArray()),
            ("other player native8-entry subset",f.Target.EquipInfo.Beams.Take(8).ToArray()),
            ("duplicate private object",new[] { supplied.Equip.Beams[0],supplied.Equip.Beams[0] }),
            ("empty",Array.Empty<BeamProjectileEntity>()),("oversized",f.NewPool(9))
        };
        if (f.Scene.EnemyBeams is { Length: >0 } enemies)
            unsafePools.Add(("scene enemy native subset",enemies.Take(8).ToArray()));
        else Console.WriteLine("CUSTOM REWIND SKIP enemy pool alias: no native enemy pool in fixture room.");
        if (f.Scene.PlatformBeams is { Length: >0 } platforms)
            unsafePools.Add(("scene platform native subset",platforms.Take(8).ToArray()));
        else Console.WriteLine("CUSTOM REWIND SKIP platform pool alias: no native platform pool in fixture room.");
        Check(f.Shooter.EquipInfo.Beams.Length >= 8 && f.Target.EquipInfo.Beams.Length >= 8,
            "player native alias probes contain8 real objects within custom capacity bound");
        foreach (var candidate in unsafePools)
        {
            var unsafeEquip = new EquipInfo(supplied.Equip.Weapon,candidate.Pool) { InfiniteAmmo = true, CustomShot = supplied.Equip.CustomShot };
            Check(!NetUnlagged.TryBeginCustomShot(f.Shooter,supplied.Launch,unsafeEquip,out _) && Idle() && f.Unchanged(original),
                candidate.Label + " pool refuses before context mutation");
        }
        foreach (string exit in new[] { "AbortShot","Record","DisabledRecord","Reset" })
        {
            f.Prepare(); supplied = f.Launch(); original = f.Capture();
            if (exit == "DisabledRecord") NetUnlagged.Enabled = false;
            Check(NetUnlagged.TryBeginCustomShot(f.Shooter,supplied.Launch,supplied.Equip,out _),"scope begins before " + exit);
            if (exit == "AbortShot") NetUnlagged.AbortShot();
            else if (exit is "Record" or "DisabledRecord") NetUnlagged.Record(1010);
            else NetUnlagged.Reset();
            Check(Idle() && f.Unchanged(original),exit + " during scope restores exact world and releases all retained context");
            NetUnlagged.EndShot(f.Shooter);
            Check(Idle() && f.Unchanged(original),"EndShot after " + exit + " safely remains inert");
        }
    }
    private static void NativePrivateCatchUp(Fixture f)
    {
        f.Prepare();
        var nativePool = f.Shooter.EquipInfo.Beams;
        var nativeEquip = new EquipInfo(f.Scene.WeaponRules[(int)BeamType.PowerBeam],nativePool) { InfiniteAmmo = true };
        var privatePool = f.NewPool(8);
        var privateEquip = new EquipInfo(f.Scene.WeaponRules[(int)BeamType.PowerBeam],privatePool) { InfiniteAmmo = true };
        var far = f.Shooter.Position+new Vector3(0,10000,0);
        Check(BeamProjectileEntity.Spawn(f.Shooter,nativeEquip,far+new Vector3(20,0,0),Vector3.UnitY,
            BeamSpawnFlags.NoMuzzle,f.Shooter.NodeRef,f.Scene) != BeamResultFlags.NoSpawn,"existing native pool owns genuine native projectile");
        var existing = nativePool.Single(b=>b.Lifespan > 0); var existingState = FlightNow(existing);
        var before = f.Capture(); long steps = NetUnlagged.CatchUpSteps;
        BeamProjectileEntity? emitted = null;
        try
        {
            NetUnlagged.BeginShot(f.Shooter,far,Vector3.UnitY,privateEquip);
            Check(ContextNow().Active && ReferenceEquals(ContextNow().Pool,privatePool)
                && !ReferenceEquals(ContextNow().Pool,nativePool),"native acceptedEquip captures supplied distinct private pool");
            Check(BeamProjectileEntity.Spawn(f.Shooter,privateEquip,far,Vector3.UnitY,
                BeamSpawnFlags.NoMuzzle,f.Shooter.NodeRef,f.Scene) != BeamResultFlags.NoSpawn,"distinct private pool emits genuine native projectile");
            emitted = privatePool.Single(b=>b.Lifespan > 0);
            Check(emitted.Age == 0 && FlightNow(existing) == existingState,"pre-catch-up native projectiles have explicit unprocessed state");
            NetUnlagged.EndShot(f.Shooter);
        }
        finally { NetUnlagged.AbortShot(); }
        Check(emitted != null && emitted.Age > 0 && NetUnlagged.CatchUpSteps > steps
            && FlightNow(existing) == existingState && Idle() && f.Unchanged(before),
            "real native Process catches up only supplied private pool and restores world while native pool projectile remains untouched");
    }
    private static void NativeJoinIsolation(Fixture f)
    {
        f.Prepare();
        bool claims = NetHitClaims.Enabled, impacts = NetConfirmedImpacts.Enabled;
        var timed = NetTimedShotProof.Mode;
        var before = f.Capture(); int shooterHealth = f.Shooter.Health, targetHealth = f.Target.Health;
        // Explicit metadata-only objects owned by this real scene. Neither beam
        // is emitted, processed, collided or passed to TakeDamage in this test.
        var objects = f.NewPool(2); var native = objects[0]; var custom = objects[1];
        var nativeKey = ShotKey.For(f.Shooter.SlotIndex,1);
        var payment = new ShotPaymentKey(nativeKey,(byte)f.Target.SlotIndex,
            NetPlayerLifecycle.Generation(f.Target.SlotIndex),NetPlayerLifecycle.Get(f.Target.SlotIndex),1,1);
        void NativeShape(BeamProjectileEntity beam)
        {
            beam.Owner = f.Shooter; beam.Beam = beam.BeamKind = BeamType.PowerBeam;
            beam.ModLaunchKey = nativeKey; beam.ModShotId = nativeKey.ShotId; beam.ModComponentId = 1;
            beam.ModLaunchMatch = nativeKey.MatchId; beam.ModLaunchAuthority = nativeKey.AuthorityEpoch;
            beam.ModLaunchGeneration = nativeKey.Generation; beam.ModLaunchLife = nativeKey.LifeId; beam.ModLaunchFrame = 1004;
            beam.Lifespan = 1; beam.Flags = BeamFlags.SurfaceCollision; beam.EnhancedDirectHit = true;
            beam.Position = beam.SpawnPosition = f.Shooter.Position+Vector3.UnitY*.5f;
            beam.BackPosition = beam.Position+Vector3.UnitY;
        }
        NativeShape(native); NativeShape(custom);
        var supplied = f.Launch();
        var action = supplied.Launch.Source.Action with { Sequence = 1 };
        var launch = supplied.Launch with { Source = supplied.Launch.Source with { Action = action },
            After = supplied.Launch.After with { LastAcceptedShotId = 1 } };
        var source = CustomShotSource.FromLaunch(launch);
        typeof(BeamProjectileEntity).GetProperty(nameof(BeamProjectileEntity.ModCustomShot),Hidden)!.SetValue(custom,source);
        var fact = new ConfirmedImpact(nativeKey,1,0,payment.Victim,payment.Generation,payment.Life,1,
            NetSession.NetFrame,(byte)BeamType.PowerBeam,ReplayShotFactFlags.Direct,ImpactProvenance.NativePoint,f.Target.Position);
        var nativeOutcome = new CombatAckEntry { ShotId = 1, Result = (byte)CombatAckResult.Applied,
            VictimSlot = payment.Victim, VictimGeneration = payment.Generation, VictimLife = payment.Life,
            DamageApplied = 7, HealthAfter = 123, HalfturretHealthAfter = 17, DamageSequence = 77,
            Flags = CombatAckFlags.OutcomePresent };
        var forgedOutcome = nativeOutcome; forgedOutcome.DamageApplied = 999; forgedOutcome.HealthAfter = 0;
        void DenyCustom(string label)
        {
            using var reservation = NetCombatPayments.Begin(f.Target,custom,DamageFlags.None);
            Check(!reservation.Allowed,label);
            // Even an erroneous caller invoking Commit on a denied fixture
            // reservation cannot overwrite the separate native receipt.
            reservation.Commit(forgedOutcome);
        }
        try
        {
            NetConfirmedImpacts.Enabled = true; NetTimedShotProof.Mode = TimedGrantProofMode.Shadow;
            Check(source.Sequence == nativeKey.ShotId && custom.ModCustomShot == source
                && custom.ModLaunchKey == native.ModLaunchKey && custom.ModShotId == native.ModShotId
                && custom.Beam == native.Beam && fact.Valid,
                "declared custom metadata deliberately forges identical valid native key/sequence/component/weapon fields");
            string oracleBefore = System.Text.Json.JsonSerializer.Serialize(NetCampaignHitOracle.Capture());
            NetCampaignHitOracle.ObserveSegment(custom,custom.SpawnPosition,custom.SpawnPosition+Vector3.UnitX*4);
            Check(System.Text.Json.JsonSerializer.Serialize(NetCampaignHitOracle.Capture()) == oracleBefore,
                "typed custom first segment cannot consume native oracle rows or change its coverage/overflow despite forged native metadata; timeline="
                + NetCampaignEvidence.TimelineEnabled);
            Check(NetConfirmedImpacts.Describe(f.Target,f.Shooter,native,null,1,DamageFlags.None).HasValue
                && NetConfirmedImpacts.Describe(f.Target,f.Shooter,custom,null,1,DamageFlags.None) == null
                && NetConfirmedImpacts.Matches(fact,native) && !NetConfirmedImpacts.Matches(fact,custom),
                "typed custom beam cannot describe or pair native confirmed impact despite forged matching native fields");
            foreach (bool enabled in new[] { true,false })
            {
                NetHitClaims.Enabled = enabled;
                Check(NetDamage.Suppress(f.Target,custom,DamageFlags.None)
                    && !NetDamage.Suppress(f.Target,native,DamageFlags.None),
                    "custom live damage is suppressed while ordinary native metadata remains eligible; claims=" + enabled);
                Check(!NetDamage.Suppress(f.Target,null,DamageFlags.None),
                    "ordinary source-less authority damage keeps existing suppression behavior; claims=" + enabled);
                using (var ordinary = NetCombatPayments.Begin(f.Target,null,DamageFlags.None))
                    Check(ordinary.Allowed,"ordinary source-less payment adapter keeps existing behavior; claims=" + enabled);
                foreach (bool nativeFirst in new[] { false,true })
                {
                    NetCombatPayments.Reset();
                    if (!nativeFirst)
                    {
                        DenyCustom("custom-first same-number reservation refused; claims=" + enabled);
                        Check(!NetCombatPayments.TryGet(payment,(byte)BeamType.PowerBeam,out _),
                            "custom-first attempt cannot manufacture a native payment receipt");
                    }
                    using (var reservation = NetCombatPayments.Begin(payment,(byte)BeamType.PowerBeam))
                    {
                        Check(reservation.Allowed,"explicit native fixture reservation remains available after custom attempts");
                        if (!nativeFirst) DenyCustom("custom attempt cannot enter an in-progress native reservation; claims=" + enabled);
                        using (var occupied = NetCombatPayments.Begin(payment,(byte)BeamType.PowerBeam))
                            Check(!occupied.Allowed,"native reservation remains exact through pending custom-attempt chronology");
                        reservation.Commit(nativeOutcome);
                    }
                    Check(NetCombatPayments.TryGet(payment,(byte)BeamType.PowerBeam,out var retained)
                        && retained.Equals(nativeOutcome) && NetCombatPayments.Payments == 1,
                        "explicit native fixture commits exactly its declared receipt, without actual HP removal");
                    DenyCustom("custom-after-native same-number reservation refused; claims=" + enabled);
                    Check(NetCombatPayments.TryGet(payment,(byte)BeamType.PowerBeam,out retained)
                        && retained.Equals(nativeOutcome) && NetCombatPayments.Payments == 1,
                        "custom same-number attempt cannot consume or overwrite native tombstone; native-first=" + nativeFirst + "; claims=" + enabled);
                    using var duplicate = NetCombatPayments.Begin(payment,(byte)BeamType.PowerBeam);
                    Check(!duplicate.Allowed,"native fixture tombstone still rejects its own exact duplicate after custom attempts");
                }
            }
            foreach (bool modeOff in new[] { false,true })
            {
                NetTimedShotProof.Mode = TimedGrantProofMode.Shadow;
                var start = native.SpawnPosition; var end = start+Vector3.UnitX*4;
                using var nativeSegment = new NetNativeImpactWitness.SegmentScope(native,start,end);
                using var nativeWitness = NetNativeImpactWitness.Enter(f.Target,native,1,DamageFlags.None,f.Target.Position);
                Check(NetNativeImpactWitness.TryCapture(out var outer) && outer.Payment == payment
                    && outer.SegmentStart == start && outer.SegmentEnd == end,
                    "explicit native metadata fixture opens exact native witness and segment scope");
                object? segment = Field<object?>(typeof(NetNativeImpactWitness),"_segment");
                if (modeOff) NetTimedShotProof.Mode = TimedGrantProofMode.Off;
                using (var customSegment = new NetNativeImpactWitness.SegmentScope(custom,start,end))
                {
                    Check(Field<object?>(typeof(NetNativeImpactWitness),"_segment") == null,
                        "custom scope clears outer native segment even with optional policy Off=" + modeOff);
                    using (var customWitness = NetNativeImpactWitness.Enter(f.Target,custom,1,DamageFlags.None,f.Target.Position))
                        Check(!NetNativeImpactWitness.TryCapture(out _),
                            "custom scope cannot borrow outer native witness even with optional policy Off=" + modeOff);
                    Check(NetNativeImpactWitness.TryCapture(out var restored) && restored == outer,
                        "custom witness disposal restores exact outer native metadata witness");
                }
                Check(Equals(Field<object?>(typeof(NetNativeImpactWitness),"_segment"),segment),
                    "custom segment disposal restores exact prior native segment");
            }
            Check(f.Shooter.Health == shooterHealth && f.Target.Health == targetHealth && f.Unchanged(before),
                "declared native-join fixtures never apply multiplayer HP damage or alter collision world");
        }
        finally
        {
            NetCombatPayments.Reset(); NetConfirmedImpacts.Reset();
            NetHitClaims.Enabled = claims; NetConfirmedImpacts.Enabled = impacts; NetTimedShotProof.Mode = timed;
            custom.Destroy(); native.Destroy();
        }
    }
}
