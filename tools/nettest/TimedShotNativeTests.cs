using System;
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

    private readonly record struct Outcome(int Damage, int Ammo, int Fired, int Semantic,
        FireEventKind Kind, byte Weapon, byte Charge);
    private readonly record struct NativeShot(ShotKey Key, FireEvent Fire, Outcome Outcome);

    private sealed class Fixture : IDisposable
    {
        private readonly ServerSim _sim = new();
        private readonly KeyboardState _keyboard;
        private readonly Action<KeyboardState, Keys, bool> _setKey;
        internal readonly Scene Scene;
        internal readonly PlayerEntity Owner;
        internal Fixture(string room, ShotProofMode oldMode, TimedGrantProofMode newMode)
        {
            NetShotProof.Mode = oldMode;
            NetTimedShotProof.Mode = newMode;
            NetTestScript.Enabled = false;
            Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
            var roster = RosterPacket.Create();
            roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 1;
            roster.Slots[0] = 0; roster.Generations[0] = 1;
            roster.Hunters[0] = (byte)Hunter.Samus; roster.Names[0] = "Native timed source";
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
                Check(!Owner.IsBot && Owner.Hunter == Hunter.Samus && NetSession.IsAuthority
                    && !Scene.Services.IsReplica && MphRead.Mods.Headless.Active,
                    "declared headless native authority local-body fixture");
                foreach (var control in Owner.Controls.All)
                { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
                Owner.Controls.Shoot.Key = Keys.Space;
                Owner.Controls.Missile.Key = Keys.M;
                Owner.Controls.MouseAim = false;
                _keyboard = (KeyboardState)typeof(Scene).GetField("_keyboardState", Hidden)!.GetValue(Scene)!;
                _setKey = typeof(KeyboardState).GetMethod("SetKeyState", Hidden | BindingFlags.Public)!
                    .CreateDelegate<Action<KeyboardState, Keys, bool>>();
                // Exclude unrelated map contacts; positive grant below uses a
                // real native item factory and the complete player phase.
                foreach (var item in Scene.GetItemInstanceEntities()) item.DespawnTimer = 0;
                Steps(120);
                Check(Owner.ModIsInPlay && Owner.Health > 0 && Owner.CurrentWeapon == BeamType.PowerBeam
                    && NetPlayerLifecycle.Generation(0) != 0 && NetPlayerLifecycle.Get(0) != 0,
                    "native initial spawn owns ordinary PowerBeam and a real admitted life");
                Check(Scene.CameraMode == CameraMode.Roam && NetHooks.LocalSlot == 0,
                    "same synthetic-keyboard local body seam as MovementTour");
            }
            catch { _sim.Stop(); NetSession.Stop(); throw; }
        }
        internal void Step(bool shoot = false, bool missile = false)
        {
            _setKey(_keyboard, Keys.Space, shoot);
            _setKey(_keyboard, Keys.M, missile);
            uint prior = NetSession.NetFrame;
            Session(nameof(NetSession.NetFrame), unchecked(prior + 1));
            _sim.Step();
            Check(_sim.StepFailures == 0 && NetSession.NetFrame == unchecked(prior + 1),
                "one complete native simulation step without exception");
        }
        internal void Steps(int count) { for (int i = 0; i < count; i++) Step(); }
        internal void Until(uint frame)
        {
            while (unchecked((int)(frame - NetSession.NetFrame)) > 0) Step();
            Check(Owner.Health > 0 && Owner.ModIsInPlay, "native actor remains alive through bounded timer steps");
        }
        internal NativeShot Fire()
        {
            int ammo = Owner.EquipInfo.Ammo;
            int fired = NetDamage.Fired[0];
            int semantic = Scene.MatchEvents.Bus.Events.Count(e => e.Type == MphRead.Mods.MatchEvents.MatchSemanticEventType.WeaponFired);
            uint expected = unchecked(NetSession.NetFrame + 1);
            Check(!_keyboard.IsKeyDown(Keys.Space) && !Owner.Controls.Shoot.IsDown,
                "released keyboard/control baseline precedes actual native press");
            Step(shoot: true);
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
            var intent = new IntentPacket(); NetFireEvents.Fill(ref intent, 0);
            Check(Enumerable.Range(0, intent.FireEventCount).Any(i => intent.FireEvents[i] == fire),
                "actual successful native Commit retains exact outgoing fire");
            var outcome = new Outcome((int)beam.Damage, ammo - Owner.EquipInfo.Ammo,
                firedDelta, semanticDelta, fire.Kind, fire.Weapon, fire.Charge);
            Step();
            return new(key, fire, outcome);
        }
        public void Dispose() { _sim.Stop(); NetSession.Stop(); }
    }

    private static TimedShotCertificate Certificate(NativeShot shot, TimedGrantKnowledge knowledge)
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
        if (newMode == TimedGrantProofMode.Shadow)
        {
            var value = Certificate(shot, TimedGrantKnowledge.NotGranted);
            Check(!value.AuthorizedDouble && value.Funding == null && NetTimedShotProof.Stored == 1
                && NetTimedShotProof.NotGranted == 1, "unfunded native shot never authorizes Double");
        }
        else Check(NetTimedShotProof.Stored == 0 && !NetTimedShotProof.TryGet(shot.Key, shot.Fire.SourceFrame, out _),
            "new Off retains no certificate while native semantic emission succeeds");
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
        Check(NetDamage.Fired[0] == fired && NetTimedShotProof.Stored == stored
            && NetShotDiagnostics.Outcomes[(int)BeamType.Missile, (int)ShotAttemptResult.NoAmmo] == empty + 1,
            "genuine sampled no-ammo attempt has no successful component/certificate");
        if (NetFireEvents.TryActiveAttempt(fixture.Owner, out var key, out var fire))
            Check(!NetTimedShotProof.TryGet(key, fire.SourceFrame, out _), "failed attempt exact key has no certificate");
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
                    "old/new mode combination preserves actual native damage/ammo/semantic outcome " + mode);
            NativeGrant(room);
            NonadmittedHelpers(room);
            EmptyAmmo(room);
            Console.WriteLine($"PASS: {_checks} genuine native timed-shot assertions.");
            Console.WriteLine("SCOPE: actual headless keyboard/player/spawn/contact/commit; fixture Host/local0 and item placement. NOT_RUN: GL/audio, natural AI, authenticated UDP, special-family acceptance.");
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
