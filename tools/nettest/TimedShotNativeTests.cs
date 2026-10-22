using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Asset-backed, device-free native local-body fixture. Host/local0, keyboard
// bindings, monotonic tick pacing and fixture item placement are explicit inputs.
// This does not prove UDP authentication, natural AI or rendered/audio fidelity.
internal static class TimedShotNativeTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("Timed native shot: " + label);
        _checks++;
    }
    private static void Session(string property, object value)
        => typeof(NetSession).GetProperty(property)!.SetValue(null, value);

    internal readonly record struct Outcome(float Damage, float Lifespan, int Ammo, int Fired, int Semantic,
        FireEventKind Kind, byte Weapon, byte Charge, string IntentBytes, string ReplayFacts,
        int ReplayWeaponFired, int ReplayShotFacts);
    private readonly record struct RecordedFact(ReplayFactKind Kind, uint RecordingFrame,
        uint ServerTick, ReplayMarker? Marker, string Payload);
    internal readonly record struct NativeShot(ShotKey Key, FireEvent Fire, Outcome Outcome);

    internal sealed class Fixture : IDisposable
    {
        private readonly ServerSim _sim = new();
        private readonly KeyboardState _keyboard;
        private readonly Action<KeyboardState, Keys, bool> _setKey;
        private long _allowedStepFailures;
        internal readonly Scene Scene;
        internal readonly PlayerEntity Owner;
        // Optional declared body placement for asset-dependent timer tours.
        // It cannot grant, admit or emit; those remain the normal player phases.
        internal Action? BeforeStep { get; set; }
        internal Fixture(string room, ShotProofMode oldMode, TimedGrantProofMode newMode, Hunter hunter = Hunter.Samus)
        {
            NetShotProof.Mode = oldMode;
            NetTimedShotProof.Mode = newMode;
            NetTestScript.Enabled = false;
            Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
            var roster = RosterPacket.Create();
            roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 1;
            roster.Slots[0] = 0; roster.Generations[0] = 1;
            roster.Hunters[0] = (byte)hunter; roster.Names[0] = "Native timed source";
            try
            {
                Check(_sim.Start(room, GameMode.Battle, 2, _ => { }, () => { }, roster: roster), "native room starts");
                NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                    RoomKey = room, Mode = (byte)GameMode.Battle }, false);
                NetSession.ApplyRoster(roster);
                Scene = (Scene)typeof(ServerSim).GetField("_scene", Hidden)!.GetValue(_sim)!;
                Scene.Players.MainPlayerIndex = 0;
                Scene.GameState.MatchTime = -1; Scene.GameState.PointGoal = 10000;
                Scene.GameState.EnhancedHunters = false; Scene.GameState.BalancedMode = false;
                Scene.GameState.PrimeHunter = -1;
                NetSlotManager.Sync();
                Session(nameof(NetSession.Role), NetRole.Host);
                Session(nameof(NetSession.LocalSlot), 0);
                var input = typeof(Scene).GetField("_inputMode", Hidden)!;
                input.SetValue(Scene, Enum.Parse(input.FieldType, "All"));
                Owner = Scene.Players.Items[0];
                Check(!Owner.IsBot && NetSession.IsAuthority
                    && !Scene.Services.IsReplica && MphRead.Mods.Headless.Active,
                    "declared headless native authority local-body fixture");
                foreach (var control in Owner.Controls.All)
                { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
                Owner.Controls.Shoot.Key = Keys.Space;
                Owner.Controls.Missile.Key = Keys.M;
                Owner.Controls.PowerBeam.Key = Keys.P;
                Owner.Controls.MouseAim = false;
                _keyboard = (KeyboardState)typeof(Scene).GetField("_keyboardState", Hidden)!.GetValue(Scene)!;
                _setKey = typeof(KeyboardState).GetMethod("SetKeyState", Hidden | BindingFlags.Public)!
                    .CreateDelegate<Action<KeyboardState, Keys, bool>>();
                // Exclude unrelated map contacts; positive grant below uses a
                // real native item factory and the complete player phase.
                foreach (var item in Scene.GetItemInstanceEntities()) item.DespawnTimer = 0;
                Steps(120);
                Check(Owner.Hunter == hunter && Owner.ModIsInPlay && Owner.Health > 0 && Owner.CurrentWeapon == BeamType.PowerBeam
                    && NetPlayerLifecycle.Generation(0) != 0 && NetPlayerLifecycle.Get(0) != 0,
                    "native initial spawn owns ordinary PowerBeam and a real admitted life");
                Check(Scene.CameraMode == CameraMode.Roam && NetHooks.LocalSlot == 0,
                    "same synthetic-keyboard local body seam as MovementTour");
            }
            catch { _sim.Stop(); NetSession.Stop(); throw; }
        }
        internal void Step(bool shoot = false, bool missile = false, bool powerBeam = false,
            bool expectedInjectedFailure = false)
        {
            _setKey(_keyboard, Keys.Space, shoot);
            _setKey(_keyboard, Keys.M, missile);
            _setKey(_keyboard, Keys.P, powerBeam);
            BeforeStep?.Invoke();
            uint prior = NetSession.NetFrame;
            long failures = _sim.StepFailures;
            Check(failures == _allowedStepFailures, "no unaccounted native simulation failures before step");
            Session(nameof(NetSession.NetFrame), unchecked(prior + 1));
            _sim.Step();
            long expected = expectedInjectedFailure ? 1 : 0;
            Check(_sim.StepFailures - failures == expected && NetSession.NetFrame == unchecked(prior + 1),
                expectedInjectedFailure ? "exactly one declared injected native failure" : "one complete native step without a new exception");
            _allowedStepFailures += expected;
        }
        internal void Steps(int count) { for (int i = 0; i < count; i++) Step(); }
        internal void Until(uint frame)
        {
            while (unchecked((int)(frame - NetSession.NetFrame)) > 0) Step();
            Check(Owner.Health > 0 && Owner.ModIsInPlay, "native actor remains alive through bounded timer steps");
        }
        internal NativeShot Fire(Action? afterNativePress = null)
        {
            int ammo = Owner.EquipInfo.Ammo;
            int fired = NetDamage.Fired[0];
            int semantic = Scene.MatchEvents.Bus.Events.Count(e => e.Type == MphRead.Mods.MatchEvents.MatchSemanticEventType.WeaponFired);
            uint expected = unchecked(NetSession.NetFrame + 1);
            Check(!_keyboard.IsKeyDown(Keys.Space) && !Owner.Controls.Shoot.IsDown,
                "released keyboard/control baseline precedes actual native press");
            var recorded = new List<RecordedFact>();
            int replayWeaponFired = 0, replayShotFacts = 0;
            void Accepted(ReplayTimelineRecord record)
            {
                Check(recorded.Count < 32, "bounded real replay callback capture");
                var bytes = record.Payload;
                recorded.Add(new(record.Kind, record.RecordingFrame, record.ServerTick,
                    record.Marker, Convert.ToHexString(bytes)));
                if (bytes.Length > 0 && bytes[0] == (byte)PacketType.ReplayShotFact) replayShotFacts++;
                if (bytes.Length > 0 && bytes[0] == (byte)PacketType.MatchSemanticEvent
                    && MphRead.Mods.MatchEvents.MatchSemanticEventPacket.TryRead(bytes, out var fact)
                    && fact.Type == MphRead.Mods.MatchEvents.MatchSemanticEventType.WeaponFired)
                    replayWeaponFired++;
            }
            ReplayCapture.Recorder.Accepted += Accepted;
            try
            {
                Step(shoot: true);
                afterNativePress?.Invoke();
                Check(Owner.Controls.Shoot.IsPressed && Owner.Controls.Shoot.IsDown
                    && !Owner.Controls.Shoot.IsReleased && _keyboard.IsKeyDown(Keys.Space),
                    "real sampled rising keyboard edge");
                Check(NetFireEvents.TryActiveAttempt(Owner, out var key, out var fire)
                    && fire.SourceFrame == expected && key.ShotId != 0 && fire.SourceLife == key.LifeId,
                    "native Begin retains exact full key and actual source frame");
                int firedDelta = NetDamage.Fired[0] - fired;
                int semanticDelta = Scene.MatchEvents.Bus.Events.Count(e => e.Type == MphRead.Mods.MatchEvents.MatchSemanticEventType.WeaponFired) - semantic;
                Check(firedDelta == 1 && semanticDelta == 1, "native PlayerInput successful Spawn/Commit semantic denominator is one");
                var beam = Owner.EquipInfo.Beams.Single(b => b.ModLaunchKey == key && b.ModComponentId != 0);
                Check(beam.Owner == Owner && beam.Damage > 0, "actual production pooled component has positive native damage");
                // Capture the actual actor, then apply the normal SendIntent stamp.
                // A default Frame=0 would incorrectly prune the real new shot as old.
                var intent = NetPlayerBridge.CaptureIntent(Owner);
                intent.Frame = NetSession.NetFrame;
                intent.MatchId = NetSession.CurrentMatchId;
                intent.AuthorityEpoch = NetSession.AuthorityEpoch;
                intent.SlotGeneration = NetPlayerLifecycle.Generation(0);
                intent.LifeId = NetPlayerLifecycle.Get(0);
                NetFireEvents.Fill(ref intent, 0);
                Check(Enumerable.Range(0, intent.FireEventCount).Any(i => intent.FireEvents[i] == fire),
                    "actual successful native Commit retains exact outgoing fire");
                var intentBytes = new byte[IntentPacket.TouchCanonicalSize];
                intent.Write(intentBytes);
                float damage = beam.Damage, lifespan = beam.Lifespan;
                int spent = ammo - Owner.EquipInfo.Ammo;
                Step();
                Check(replayWeaponFired == 1 && recorded.Count > 0,
                    "real production ReplayRecorder callback accepts one native WeaponFired semantic fact");
                var outcome = new Outcome(damage, lifespan, spent, firedDelta, semanticDelta,
                    fire.Kind, fire.Weapon, fire.Charge, Convert.ToHexString(intentBytes),
                    JsonSerializer.Serialize(recorded), replayWeaponFired, replayShotFacts);
                return new(key, fire, outcome);
            }
            finally { ReplayCapture.Recorder.Accepted -= Accepted; }
        }
        public void Dispose() { _sim.Stop(); NetSession.Stop(); }
    }

    internal static TimedShotCertificate Certificate(NativeShot shot, TimedGrantKnowledge knowledge)
    {
        Check(NetTimedShotProof.TryGet(shot.Key, shot.Fire.SourceFrame, out var value), "actual native emitted certificate retained");
        Check(value.Key == shot.Key && value.Fire == shot.Fire && value.Components > 0
            && value.Lane == TimedShotSource.NativeInput && value.TrustedSource,
            "certificate binds real input, seeded source and completed component");
        Check(value.Knowledge == knowledge && value.Unavailable == TimedShotUnavailable.None,
            "native exact source ownership is " + knowledge + " (source=" + value.Source + ")");
        return value;
    }

    private static Outcome Ordinary(string room, ShotProofMode oldMode, TimedGrantProofMode newMode)
    {
        using var fixture = new Fixture(room, oldMode, newMode);
        var shot = fixture.Fire();
        if (newMode != TimedGrantProofMode.Off || NetCombatFastPath.ShadowEnabled)
        {
            var value = Certificate(shot, TimedGrantKnowledge.NotGranted);
            Check(!value.AuthorizedDouble && value.Funding == null && NetTimedShotProof.Stored == 1
                && NetTimedShotProof.NotGranted == 1, "unfunded native shot never authorizes Double");
            Check(value.CaptureMode == newMode && newMode != TimedGrantProofMode.Enforce
                && !NetTimedShotModifiers.Enforcing,
                "explicit observation retains the actual timed capture mode, including Off, without enabling enforcement");
        }
        else Check(NetTimedShotProof.Stored == 0 && !NetTimedShotProof.TryGet(shot.Key, shot.Fire.SourceFrame, out _),
            "both timed proof and fast observation Off retain no certificate while native semantic emission succeeds");
        Console.WriteLine("TIMED NATIVE ARM " + JsonSerializer.Serialize(new { oldMode, newMode, shot.Key, shot.Fire.SourceFrame, shot.Outcome }));
        return shot.Outcome;
    }

    private static void NativeGrant(string room)
    {
        using var fixture = new Fixture(room, ShotProofMode.Off, TimedGrantProofMode.Shadow);
        var owner = fixture.Owner;
        var item = ItemSpawnEntity.SpawnItem(ItemType.DoubleDamage, owner.Position, owner.NodeRef, 6000, fixture.Scene)
            ?? throw new InvalidOperationException("Native Double factory refused fixture item.");
        Check(NetPowerUpLedger.TrySpawnKey(item, out var spawn), "real initialized native Double incarnation registers");
        fixture.Step(); // Actual contact after the real input phase, no direct pickup callback.
        uint contact = NetSession.NetFrame;
        var ledger = NetPowerUpLedger.Ledger!;
        var grant = ledger.History.Single(g => g.Spawn == spawn);
        var life = ShotKey.For(owner.SlotIndex, 0);
        Check(item.DespawnTimer == 0 && grant.Life == life && grant.ContactFrame == contact
            && grant.SourceContactFrame == contact && grant.EffectiveFrame == unchecked(contact + 1),
            "complete native item contact records genuine source chronology");
        Check(ledger.TryReadObservation(life, contact, TimedPowerUp.DoubleDamage, out var atContact, out var observed)
            && observed == contact && atContact.Knowledge == TimedGrantKnowledge.NotGranted,
            "automatic contact-frame input stays NotGranted; no fabricated observation");
        var early = Certificate(fixture.Fire(), TimedGrantKnowledge.Granted);
        Check(early.Fire.SourceFrame == unchecked(contact + 1) && early.AuthorizedDouble
            && early.Funding is { } funding && funding.Sequence == grant.Sequence && funding.Spawn == spawn
            && funding.ContactFrame == contact && funding.SourceContactFrame == contact,
            "C+1 actual native fire uses exact native funding incarnation");
        // The early certificate correctly expires before the 1800-frame grant.
        // Emit a late real shot so its immutable receipt survives grant expiry.
        fixture.Until(unchecked(contact + 1700));
        var lateShot = fixture.Fire();
        var late = Certificate(lateShot, TimedGrantKnowledge.Granted);
        fixture.Until(unchecked(grant.ExpiresAtFrame + 2));
        Check(grant.EndReason == TimedGrantEnd.Expired && !owner.DoubleDamage,
            "actual native timer reaches expiry without backdating or timer assignment");
        Check(NetTimedShotProof.TryGet(lateShot.Key, lateShot.Fire.SourceFrame, out var retained) && retained == late
            && retained.AuthorizedDouble, "delayed retrieval after grant expiry preserves exact immutable late fire funding");
        Certificate(fixture.Fire(), TimedGrantKnowledge.NotGranted);
    }

    private static void NonadmittedHelpers(string room)
    {
        using var fixture = new Fixture(room, ShotProofMode.Off, TimedGrantProofMode.Shadow);
        var owner = fixture.Owner;
        long stored = NetTimedShotProof.Stored;
        var origin = owner.Position + Vector3.UnitY;
        NetFireEvents.Begin(owner, origin, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY);
        Check(NetFireEvents.TryActiveAttempt(owner, out var naked, out var nakedFire), "naked helper produces an attempt for negative fixture");
        NetFireEvents.Commit(owner);
        Check(NetTimedShotProof.Stored == stored && !NetTimedShotProof.TryGet(naked, nakedFire.SourceFrame, out _),
            "naked Begin/Commit cannot create successful input/emission proof");
        bool emitted = (bool)typeof(PlayerEntity).GetMethod("TryFireWeapon", Hidden)!.Invoke(owner, null)!;
        Check(emitted && NetFireEvents.TryActiveAttempt(owner, out var direct, out var directFire)
            && NetTimedShotProof.Stored == stored && !NetTimedShotProof.TryGet(direct, directFire.SourceFrame, out _),
            "actual direct TryFireWeapon emission outside sampled native scope cannot certify");
        NetFireEvents.Begin(owner, origin, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY);
        var result = BeamProjectileEntity.Spawn(owner, owner.EquipInfo, origin, Vector3.UnitY,
            BeamSpawnFlags.NoMuzzle, owner.NodeRef, fixture.Scene);
        if (result != BeamResultFlags.NoSpawn) NetFireEvents.Commit(owner);
        Check(result != BeamResultFlags.NoSpawn && NetTimedShotProof.Stored == stored,
            "real direct helper projectile does not manufacture sampled source admission");
        var key = ShotKey.For(0, 90000);
        var fire = new FireEvent(key.ShotId, NetSession.NetFrame, NetSession.NetFrame, 0,
            FireEventKind.PressFire, (byte)BeamType.PowerBeam, 0, 0, FireEvent.FlagPose,
            origin, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, SourceLife: key.LifeId);
        Check(!NetFireEvents.CaptureLedger(0).TryGet(key, out _, out _, out _), "direct accepted helper has no pending admission");
        var accepted = NetFireEvents.PlayAccepted(owner, key, fire);
        Check(accepted != BeamResultFlags.NoSpawn && NetTimedShotProof.Stored == stored
            && !NetTimedShotProof.TryGet(key, fire.SourceFrame, out _),
            "actual nonadmitted accepted-helper beam never certifies remote admission");
    }

    private static void EmptyAmmo(string room)
    {
        using var fixture = new Fixture(room, ShotProofMode.Off, TimedGrantProofMode.Shadow);
        Check(fixture.Owner.AvailableWeapons[BeamType.Missile], "native spawn owns negative-fixture Missile without synthetic weapon grant");
        fixture.Step(missile: true); fixture.Steps(60);
        Check(fixture.Owner.CurrentWeapon == BeamType.Missile, "normal sampled weapon selection reaches Missile");
        fixture.Owner.ModSetAmmo(0, 0); // Explicit negative fault input; never positive source seeding.
        int fired = NetDamage.Fired[0]; long stored = NetTimedShotProof.Stored;
        long empty = NetShotDiagnostics.Outcomes[(int)BeamType.Missile, (int)ShotAttemptResult.NoAmmo];
        fixture.Step(shoot: true);
        string state = AmmoState(fixture.Owner, fired, stored, empty);
        // Production PlayerProcess auto-selects an available weapon before its
        // native input scope when the current weapon has insufficient base ammo.
        // This is a natural selection check, not a Missile Spawn refusal.
        Check(fixture.Owner.CurrentWeapon == BeamType.PowerBeam
            && NetShotDiagnostics.Outcomes[(int)BeamType.Missile, (int)ShotAttemptResult.NoAmmo] == empty,
            "natural zero-bank input switches from Missile before its firing gate; " + state);
        if (NetFireEvents.TryActiveAttempt(fixture.Owner, out var key, out var fire))
        {
            Check(fire.Weapon != (byte)BeamType.Missile, "natural zero-bank transition never certifies a Missile; " + state);
            if (NetDamage.Fired[0] != fired)
            {
                var value = Certificate(new(key, fire, default), TimedGrantKnowledge.NotGranted);
                Check(NetDamage.Fired[0] == fired + 1 && NetTimedShotProof.Stored == stored + 1
                    && value.Fire.Weapon == (byte)BeamType.PowerBeam,
                    "any actual replacement PowerBeam emission retains its truthful native certificate; " + state);
            }
            else Check(NetTimedShotProof.Stored == stored, "unfired natural weapon transition has no certificate; " + state);
        }
        else Check(NetDamage.Fired[0] == fired && NetTimedShotProof.Stored == stored,
            "natural gun transition did not emit an unidentified shot; " + state);
        Console.WriteLine("TIMED NATIVE ZERO BANK " + state);
    }

    private static string AmmoState(PlayerEntity owner, int fired, long stored, long empty)
        => JsonSerializer.Serialize(new {
            FiredDelta = NetDamage.Fired[0] - fired, StoredDelta = NetTimedShotProof.Stored - stored,
            MissileNoAmmoDelta = NetShotDiagnostics.Outcomes[(int)BeamType.Missile, (int)ShotAttemptResult.NoAmmo] - empty,
            CurrentWeapon = owner.CurrentWeapon, Charge = owner.EquipInfo.ChargeLevel,
            ShotAge = typeof(PlayerEntity).GetField("_timeSinceShot", Hidden)!.GetValue(owner),
            AutofireCooldown = typeof(PlayerEntity).GetField("_autofireCooldown", Hidden)!.GetValue(owner),
            GunAnimation = owner.GunAnimation,
            Outcomes = Enumerable.Range(0, Enum.GetValues<ShotAttemptResult>().Length)
                .Select(i => new { Result = ((ShotAttemptResult)i).ToString(),
                    Missile = NetShotDiagnostics.Outcomes[(int)BeamType.Missile, i],
                    PowerBeam = NetShotDiagnostics.Outcomes[(int)BeamType.PowerBeam, i] }).ToArray()
        });

    private static void MidFrameAmmoFault(string room)
    {
        using var fixture = new Fixture(room, ShotProofMode.Off, TimedGrantProofMode.Shadow);
        fixture.Step(missile: true); fixture.Steps(60);
        var owner = fixture.Owner;
        Check(owner.CurrentWeapon == BeamType.Missile && !owner.EquipInfo.InfiniteAmmo,
            "controlled negative starts with a naturally owned finite-ammo Missile");
        var getter = owner.EquipInfo.GetAmmo
            ?? throw new InvalidOperationException("Native Missile equipment lacks its actual ammo getter.");
        int actualAmmo = getter();
        Check(actualAmmo >= owner.EquipInfo.Weapon.AmmoCost && owner.EquipInfo.Weapon.AmmoCost > 0
            && actualAmmo >= owner.EquipInfo.Weapon.ChargeCost,
            "controlled negative real bank funds automatic selection and the biped charge precheck");
        int fired = NetDamage.Fired[0]; long stored = NetTimedShotProof.Stored;
        long empty = NetShotDiagnostics.Outcomes[(int)BeamType.Missile, (int)ShotAttemptResult.NoAmmo];
        var reads = new List<(int Value, string[] Calls)>();
        // Explicit fault input: automatic selection reads the raw bank through
        // ModAmmoForWeapon. The first equipment getter read is ProcessBiped's
        // charge precheck inside ProcessInput, before TryFireWeapon. Preserve
        // that real bank value, then require the first zero at the spawn kernel.
        // This is not a natural ammo race or positive funding/source proof.
        owner.EquipInfo.GetAmmo = () => {
            Check(reads.Count < 64, "controlled ammo fault read capture remains bounded");
            int value = reads.Count == 0 ? getter() : 0;
            string[] calls = new StackTrace().GetFrames().Select(f => f.GetMethod()?.Name ?? "unknown").ToArray();
            reads.Add((value, calls));
            return value;
        };
        try { fixture.Step(shoot: true); }
        finally { owner.EquipInfo.GetAmmo = getter; }
        string state = AmmoState(owner, fired, stored, empty);
        int spawnRead = reads.FindIndex(r => r.Calls.Contains("SpawnWithEmission")
            && r.Calls.Contains("TryFireWeapon"));
        Check(reads.Count >= 2 && reads[0].Value == actualAmmo
            && reads[0].Calls.Contains("ProcessPlayer") && reads[0].Calls.Contains("ProcessBiped")
            && !reads[0].Calls.Contains("TryFireWeapon") && !reads[0].Calls.Contains("SpawnWithEmission")
            && spawnRead > 0 && reads[spawnRead].Value == 0
            && reads.FindIndex(r => r.Value == 0) == spawnRead,
            "controlled first zero reaches native Spawn only after the funded biped charge precheck; " + state
                + " reads=" + JsonSerializer.Serialize(reads.Select(r => new { r.Value, r.Calls })));
        Check(NetDamage.Fired[0] == fired && NetTimedShotProof.Stored == stored
            && NetShotDiagnostics.Outcomes[(int)BeamType.Missile, (int)ShotAttemptResult.NoAmmo] == empty + 1
            && getter() == actualAmmo && owner.CurrentWeapon == BeamType.Missile,
            "controlled mid-frame zero produces native NoAmmo with no spend/emission/certificate; " + state);
        Check(NetFireEvents.TryActiveAttempt(owner, out var key, out var fire)
            && fire.SourceFrame == NetSession.NetFrame && fire.Weapon == (byte)BeamType.Missile
            && !NetTimedShotProof.TryGet(key, fire.SourceFrame, out _)
            && !owner.EquipInfo.Beams.Any(b => b.ModLaunchKey == key && b.ModComponentId != 0),
            "actual refused native attempt has exact identity and no allocated component evidence; " + state);
        Console.WriteLine("TIMED NATIVE CONTROLLED AMMO FAULT " + state + " reads="
            + JsonSerializer.Serialize(reads.Select(r => new { r.Value, r.Calls })));
    }

    private sealed class PostSpendFault : Exception
    {
        internal PostSpendFault() : base("EXPECTED FIXTURE FAULT: real native ammo setter completed before deliberate throw") { }
    }

    private static void PostSpendException(string room)
    {
        using var fixture = new Fixture(room, ShotProofMode.Off, TimedGrantProofMode.Shadow);
        var priorShot = fixture.Fire();
        var priorCertificate = Certificate(priorShot, TimedGrantKnowledge.NotGranted);
        fixture.Step(missile: true); fixture.Steps(60);
        var owner = fixture.Owner;
        Check(owner.CurrentWeapon == BeamType.Missile && !owner.EquipInfo.InfiniteAmmo,
            "post-spend fault selects genuine native finite-ammo Missile");
        var setter = owner.EquipInfo.SetAmmo
            ?? throw new InvalidOperationException("Native Missile equipment lacks its actual ammo setter.");
        int bankBefore = owner.EquipInfo.Ammo, setterCalls = 0;
        Check(bankBefore >= owner.EquipInfo.Weapon.AmmoCost && owner.EquipInfo.Weapon.AmmoCost > 0,
            "post-spend exception begins with real native inventory");
        int fired = NetDamage.Fired[0]; long stored = NetTimedShotProof.Stored;
        string[] calls = Array.Empty<string>();
        owner.EquipInfo.SetAmmo = value => {
            setterCalls++;
            setter(value); // Actual native bank mutation precedes the fault.
            calls = new StackTrace().GetFrames().Select(f => f.GetMethod()?.Name ?? "unknown").ToArray();
            throw new PostSpendFault();
        };
        Console.WriteLine("TIMED NATIVE EXPECTED POST-SPEND FAULT: one declared StepFailure, not a natural native positive.");
        try { fixture.Step(shoot: true, expectedInjectedFailure: true); }
        finally { owner.EquipInfo.SetAmmo = setter; }
        string state = AmmoState(owner, fired, stored, 0);
        Check(setterCalls == 1 && calls.Contains("SpawnWithEmission") && owner.EquipInfo.Ammo < bankBefore
            && NetDamage.Fired[0] == fired && NetTimedShotProof.Stored == stored,
            "actual native post-spend exception mutates bank but cannot commit a shot/certificate; " + state);
        Check(NetFireEvents.TryActiveAttempt(owner, out var failedKey, out var failedFire)
            && failedFire.Weapon == (byte)BeamType.Missile && failedFire.SourceFrame == NetSession.NetFrame
            && !NetTimedShotProof.TryGet(failedKey, failedFire.SourceFrame, out _)
            && !owner.EquipInfo.Beams.Any(b => b.ModLaunchKey == failedKey && b.ModComponentId != 0),
            "thrown native attempt retains exact failed key and no fabricated component proof");
        Check(NetTimedShotProof.TryGet(priorShot.Key, priorShot.Fire.SourceFrame, out var unchanged)
            && unchanged == priorCertificate, "post-spend uncertainty leaves previously retained native certificate immutable");
        // Restore ordinary sampled input, switch by its normal bound key, and
        // allow native cooldown/animation to pass. No manual source repair.
        fixture.Step(); fixture.Step(powerBeam: true); fixture.Steps(60);
        Check(owner.CurrentWeapon == BeamType.PowerBeam, "normal keyboard selection restores a supported native weapon");
        var laterShot = fixture.Fire();
        Check(NetTimedShotProof.TryGet(laterShot.Key, laterShot.Fire.SourceFrame, out var later)
            && later.Components > 0 && later.Lane == TimedShotSource.NativeInput
            && later.Source.Source == ShotProofReason.SourceChanged
            && later.Unavailable == TimedShotUnavailable.Source && !later.TrustedSource && !later.AuthorizedDouble,
            "later genuine native emission truthfully retains private post-spend source uncertainty");
        Check(NetTimedShotProof.TryGet(priorShot.Key, priorShot.Fire.SourceFrame, out unchanged)
            && unchanged == priorCertificate, "later unavailable source cannot rewrite prior successful certificate");
        Console.WriteLine("TIMED NATIVE POST-SPEND RESULT " + JsonSerializer.Serialize(new {
            bankBefore, bankAfter = owner.ModAmmo.Missiles, setterCalls, calls,
            failedKey, failedFire.SourceFrame, laterShot.Key, Source = later.Source.Source,
            later.Unavailable, ExpectedStepFailures = 1
        }));
    }

    internal static int Run(string room)
    {
        _checks = 0;
        var oldProof = NetShotProof.Mode; var oldTimed = NetTimedShotProof.Mode;
        bool script = NetTestScript.Enabled;
        try
        {
            Check(ServerSim.Available(out string reason), reason);
            var baseline = Ordinary(room, ShotProofMode.Off, TimedGrantProofMode.Off);
            foreach (var mode in new[] { (ShotProofMode.Off, TimedGrantProofMode.Shadow),
                (ShotProofMode.Shadow, TimedGrantProofMode.Off), (ShotProofMode.Shadow, TimedGrantProofMode.Shadow) })
                Check(Ordinary(room, mode.Item1, mode.Item2) == baseline,
                    "old/new mode combination preserves actual native damage/lifespan/ammo/semantic, full canonical intent and all accepted replay bytes " + mode);
            NativeGrant(room);
            NonadmittedHelpers(room);
            EmptyAmmo(room);
            MidFrameAmmoFault(room);
            PostSpendException(room);
            Console.WriteLine($"PASS: {_checks} genuine native timed-shot assertions.");
            Console.WriteLine("SCOPE: actual headless keyboard/player/spawn/contact/commit and unmodified canonical intent/accepted replay bytes; fixture Host/local0 and item placement. No-target firing may have zero impact ReplayShotFacts; actual WeaponFired replay fact is required. NOT_RUN: GL/audio, natural AI, authenticated UDP, special-family acceptance.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("TIMED NATIVE FAIL " + error);
            Console.Error.WriteLine(JsonSerializer.Serialize(new { _checks, NetSession.NetFrame,
                NetTimedShotProof.Stored, NetTimedShotProof.Granted, NetTimedShotProof.NotGranted,
                NetTimedShotProof.Unavailable, NetTimedShotProof.NoEmission, NetTimedShotProof.Unsupported }));
            return 1;
        }
        finally
        {
            NetSession.Stop(); NetShotProof.Mode = oldProof; NetTimedShotProof.Mode = oldTimed;
            NetTestScript.Enabled = script;
        }
    }
}
