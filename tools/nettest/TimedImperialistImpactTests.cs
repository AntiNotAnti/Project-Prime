using System;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// The scalar arm is constructed geometry. The asset arm uses real accepted
// carriers, native Spawn/Process/TakeDamage and actual published profiles.
// Source resources, health and placements are declared; no grant is fabricated.
internal static class TimedImperialistImpactTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException("Imperialist impact: " + label); _checks++; }
    private static void Frame(uint value)
        => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, value);
    internal static int Run()
    {
        try
        {
            _checks = 0;
            var body = new HistoricalBody(1, Vector3.Zero, .5f, 0, 2);
            var collision = new CollisionResult { Position = new Vector3(0, .5f, 5), Plane = new Vector4(0, 0, -1, 0) };
            var template = new TimedShotDamageTemplate(72.9f, 200.9f, 0, Vector3.Zero, Vector3.UnitZ,
                .1f, 100, 200, 3, false, 0, true);
            Check(NetTimedImperialistImpact.TryChannel(template, body, collision, out uint channel,
                out bool head, out var resolved) && channel == 72 && !head
                && resolved == collision.Position + collision.Plane.Xyz * Fixed.ToFloat(204),
                "constructed body uses native offset and truncation");
            collision.Position.Y = 1.9f;
            Check(NetTimedImperialistImpact.TryChannel(template, body, collision, out channel,
                out head, out resolved) && channel == 200 && head, "constructed biped head uses native threshold");
            Check(NetTimedImperialistImpact.TryChannel(template with { Head = template.Body }, body,
                collision, out channel, out head, out _) && channel == 72 && !head,
                "equal native channels do not invent head armour bypass");
            Check(NetTimedImperialistImpact.TryChannel(template, body with { Type = HistoricalBodyType.AltSphere },
                collision, out channel, out head, out _) && channel == 72 && !head,
                "alternate body never invents native head");
            foreach (int curve in new[] { 0, 1, 2, 3 })
            {
                var curved = template with { Curve = curve };
                Check(NetTimedImperialistImpact.TryChannel(curved, body, collision, out channel, out head, out resolved)
                    && channel == (uint)Math.Clamp(BeamProjectileEntity.GetInterpolatedValue(curve, curved.Head, 0,
                        Vector3.Distance(resolved, curved.Origin) / curved.Reach), 0, int.MaxValue),
                    "constructed channel reuses native interpolation");
            }
            Check(!NetTimedImperialistImpact.TryChannel(template with { Reach = 0 }, body, collision, out _, out _, out _)
                && !NetTimedImperialistImpact.TryChannel(template with { Body = float.NaN }, body, collision, out _, out _, out _)
                && !NetTimedImperialistImpact.TryChannel(template with { Curve = 4 }, body, collision, out _, out _, out _),
                "unknown geometry or arithmetic cannot manufacture a channel");
            // The archive deliberately retains channels, not these live proofs.
            Check(!default(TimedShotDamageTemplate).FirstSegmentKnown
                && default(TimedShotDamageTemplate).DisruptionPolicy == TimedDisruptionPolicy.None,
                "absent archived geometry/effect policy is explicitly unavailable");
            var key = new ShotKey(1, 1, 0, 1, 1, 1);
            var fire = new FireEvent(1, 1, 1, 0, FireEventKind.PressFire, (byte)BeamType.Imperialist,
                0, 0, FireEvent.FlagPose, Vector3.Zero, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, SourceLife: 1);
            var fixture = new TimedShotCertificate(key, fire, 42, 1, TimedShotSource.AcceptedFire,
                new(ShotProofReason.Verified, ShotProofReason.Verified, true, 0, 1, 1),
                new(TimedGrantKnowledge.NotGranted), TimedGrantKnowledge.NotGranted, null,
                TimedShotUnavailable.None, 1, template with { NativeVelocity = Vector3.UnitZ * 100,
                    FirstSegmentKnown = true, DisruptionPolicy = TimedDisruptionPolicy.BalancedKanden });
            var archived = ReplayTimedShotFact.From(fixture, TimedGrantProofMode.Shadow);
            var differentLiveProof = ReplayTimedShotFact.From(fixture with { Damage = fixture.Damage with {
                NativeVelocity = Vector3.UnitX, FirstSegmentKnown = false,
                DisruptionPolicy = TimedDisruptionPolicy.Ordinary } }, TimedGrantProofMode.Shadow);
            Check(archived.Packet().AsSpan().SequenceEqual(differentLiveProof.Packet())
                && ReplayTimedShotFact.TryRead(archived.Packet(), out var reopened) && reopened == archived,
                "fixed216 archive remains channel observations only and never serializes new live geometry/effect policy");
            Console.WriteLine($"PASS: {_checks} constructed first-segment arithmetic assertions; no native admission proof.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServerSim _sim = new();
        internal readonly Scene Scene;
        internal readonly PlayerEntity Owner, Victim, Other;
        internal readonly Vector3 Base, Direction;
        private uint _shot;
        internal Fixture(string room, Hunter hunter, bool balanced, bool head, int competitor = 0,
            Hunter sourceHunter = Hunter.Samus)
        {
            NetShotProof.Mode = ShotProofMode.Shadow; NetTimedShotProof.Mode = TimedGrantProofMode.Enforce;
            NetFireEvents.AcceptedPlaybackEnabled = true; NetTestScript.Enabled = false;
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1;
            roster.Revision = 1; roster.Count = 3;
            for (byte i = 0; i < 3; i++)
            { roster.Slots[i] = i; roster.Generations[i] = 1; roster.Hunters[i] = (byte)(i == 0 ? sourceHunter : i == 1 ? hunter : Hunter.Samus); roster.Names[i] = "Impact fixture " + i; }
            try
            {
                Check(_sim.Start(room, GameMode.Battle, 3, _ => { }, () => { }, roster: roster), "real asset authority starts");
                NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                    RoomKey = room, Mode = (byte)GameMode.Battle, PlayerCount = 3 }, false);
                NetSession.ApplyRoster(roster);
                Scene = (Scene)typeof(ServerSim).GetField("_scene", Hidden)!.GetValue(_sim)!;
                Scene.GameState.MatchTime = -1; Scene.GameState.PointGoal = 10000;
                Scene.GameState.EnhancedHunters = false; Scene.GameState.BalancedMode = balanced;
                Scene.GameState.PrimeHunter = -1;
                foreach (var item in Scene.GetItemInstanceEntities()) item.DespawnTimer = 0;
                for (int i = 0; i < 120; i++) _sim.Step();
                Check(_sim.StepFailures == 0, "normal setup has no simulation failures");
                Owner = Scene.Players.Items[0]; Victim = Scene.Players.Items[1]; Other = Scene.Players.Items[2]; Base = Owner.Position;
                Check(Owner.ModIsInPlay && Victim.ModIsInPlay && Other.ModIsInPlay
                    && !Owner.IsBot && !Victim.IsBot && !Other.IsBot && NetSession.IsAuthority,
                    "actual live authority occupants have real life identities");
                Frame(1000);
                foreach (var player in new[] { Owner, Victim, Other })
                {
                    player.Health = 10000; player.Controls.ClearAll(); player.ModForceForm(false);
                    player.ModReleaseSpawnProtection();
                    typeof(PlayerEntity).GetField("_spawnInvulnTimer", Hidden)!.SetValue(player, (ushort)0);
                    typeof(PlayerEntity).GetField("_damageInvulnTimer", Hidden)!.SetValue(player, (ushort)0);
                }
                NetSession.BroadcastSnapshot();
                foreach (var dir in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
                    if (NetDynamicGeometryHistory.TryTraceDistanceAtFrame(Scene, Base + Vector3.UnitY * .5f,
                        Base + dir * 4 + Vector3.UnitY * .5f, NetSession.NetFrame, out float wall) && wall == 1)
                    { Direction = dir; break; }
                Check(Direction.LengthSquared == 1, "actual room supplies a clear near lane");
                float offset = head ? .5f - Fixed.ToFloat(Victim.Values.MaxPickupHeight) + .15f : 0;
                Victim.ModPlaceAt(Base + Direction * 3 + Vector3.UnitY * offset);
                Other.ModPlaceAt(Base + (competitor == 0 ? -Direction * 3 : Direction * (competitor < 0 ? 1.5f : 3.8f)));
                // Explicit source resource setup. The normal carrier and private
                // admitted drain still own the complete production shot identity.
                NetShotProof.Seed(Owner, new ShotProofSource(0x1FF, 0x1FF, 10000, 10000,
                    false, Owner.Hunter, false, false));
                Submit(Packet(null, 1000)); NetHooks.TryApplyRemoteInput(Owner, 0);
            }
            catch { _sim.Stop(); NetSession.Stop(); throw; }
        }
        private IntentPacket Packet(FireEvent? fire, uint frame, BeamType holdingWeapon = BeamType.PowerBeam, bool down = false)
        {
            var life = ShotKey.For(0, 0);
            var packet = new IntentPacket { MatchId = life.MatchId, AuthorityEpoch = life.AuthorityEpoch,
                SlotGeneration = life.Generation, LifeId = life.LifeId, Frame = frame, AckFrame = fire?.AckFrame ?? frame,
                Position = Owner.Position, Aim = Direction, WeaponSelect = fire?.Weapon ?? (byte)holdingWeapon,
                Buttons = IntentButtons.InPlayState, HasFireEvents = true, ShotInputCount = 1 };
            packet.ShotInputs[0] = ShotInputHistory.Sample(fire?.Weapon ?? (byte)holdingWeapon,
                fire.HasValue ? fire.Value.Charge == 0 : down, false, true);
            if (fire is { } value)
            { packet.FireEventCount = 1; packet.FireEvents[0] = value; if (value.Charge == 0) packet.Buttons |= IntentButtons.Shoot; }
            if (!fire.HasValue && down) packet.Buttons |= IntentButtons.Shoot;
            return packet;
        }
        private void Submit(in IntentPacket packet)
            => Check(NetSession.AcceptSlotIntent(0, packet), "actual source carrier intake succeeds");
        internal (TimedShotCertificate Shot, BeamProjectileEntity Beam, HitClaimPacket Claim) Emit(
            BeamType weapon = BeamType.Imperialist, byte charge = 0, bool rewind = true)
        {
            for (uint i = 1; i <= charge; i++)
            { Frame(1000 + i); Submit(Packet(null, NetSession.NetFrame, weapon, down: true)); NetHooks.TryApplyRemoteInput(Owner, 0); }
            if (weapon == BeamType.Imperialist)
            {
                for (uint i = 1; i <= 2; i++)
                { Frame(1000 + i); NetSession.BroadcastSnapshot(); Submit(Packet(null, NetSession.NetFrame)); NetHooks.TryApplyRemoteInput(Owner, 0); }
            }
            uint source = weapon == BeamType.Imperialist ? 1003u : 1001u + charge;
            uint ack = weapon == BeamType.Imperialist && rewind ? 1000u : source;
            Frame(source); NetSession.BroadcastSnapshot();
            uint modifiers = Weapons.GetAffinityBeam(Owner.Hunter) == weapon ? FireEvent.AffinityStateBit : 0;
            var fire = new FireEvent(++_shot, source, ack, 0, charge == 0 ? FireEventKind.PressFire : FireEventKind.ReleaseFire, (byte)weapon,
                charge, modifiers, FireEvent.FlagPose, Base + Vector3.UnitY * .5f,
                Direction, Direction, Direction, SourceLife: NetPlayerLifecycle.Get(0));
            Submit(Packet(fire, source)); NetHooks.TryApplyRemoteInput(Owner, 0);
            var key = ShotKey.For(0, fire.ShotId);
            Check(NetFireEvents.CaptureLedger(0).TryGet(key, out var admitted, out var disposition, out _)
                && admitted.Fire == fire && disposition == ShotDisposition.Fired,
                "actual private admitted drain owns native terminal");
            var beam = Owner.EquipInfo.Beams.Single(b => b.ModLaunchKey == key && b.ModComponentId != 0);
            Check(NetTimedShotProof.TryGet(key, fire.SourceFrame, out var shot) && shot.TrustedSource
                && shot.Components == 1, "actual completed certificate retains verified native source and component");
            var claim = new HitClaimPacket { MatchId = key.MatchId, AuthorityEpoch = key.AuthorityEpoch,
                ShooterGeneration = key.Generation, ShooterLifeId = key.LifeId, VictimSlot = 1,
                VictimGeneration = NetPlayerLifecycle.Generation(1), VictimLifeId = NetPlayerLifecycle.Get(1),
                ShotId = key.ShotId, ComponentId = beam.ModComponentId, Beam = (byte)weapon,
                Frame = source, AckFrame = ack, LaunchFrame = ack, Flags = HitClaimPacket.FlagDirect,
                HitPoint = Victim.Position, Damage = ushort.MaxValue };
            return (shot, beam, claim);
        }
        public void Dispose() { _sim.Stop(); NetSession.Stop(); }
    }

    internal static int RunScene(string profile, string room)
    {
        var oldMode = NetTimedShotProof.Mode; var oldProof = NetShotProof.Mode;
        bool oldPlayback = NetFireEvents.AcceptedPlaybackEnabled, oldScript = NetTestScript.Enabled;
        bool oldImpacts = NetConfirmedImpacts.Enabled;
        var oldSink = NetSession.ConfirmedImpactSink;
        try
        {
            _checks = 0; NetConfirmedImpacts.Enabled = true;
            foreach (var hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Spire, Hunter.Noxus,
                Hunter.Trace, Hunter.Sylux, Hunter.Weavel })
            foreach (bool balanced in new[] { false, true }) foreach (bool head in new[] { false, true })
            {
                using var f = new Fixture(room, hunter, balanced, head, sourceHunter: hunter);
                ConfirmedImpact? fact = null;
                NetSession.ConfirmedImpactSink = value => { if (value.Weapon == (byte)BeamType.Imperialist && value.Victim == 1) fact = value; };
                int before = f.Victim.Health;
                var emitted = f.Emit(); var claim = emitted.Claim;
                if (head) claim.Flags |= HitClaimPacket.FlagHeadshot;
                Check(NetTimedImperialistImpact.TryResolve(emitted.Shot, claim, out var impact),
                    "actual native source resolves complete first-segment nearest body " + hunter + "/" + balanced + "/" + head);
                Check(NetTimedDamageProfile.TryGet(1, claim.VictimGeneration, claim.VictimLifeId, 1001, out var victimProfile),
                    "actual published victim profile is retained");
                var forged = claim; forged.HitPoint += new Vector3(50, 50, 50);
                Check(NetTimedImperialistImpact.TryResolve(emitted.Shot, forged, out var unchanged) && unchanged == impact,
                    "claim body origin cannot move the independently resolved collision");
                Check(emitted.Shot.Damage.FirstSegmentKnown && emitted.Shot.Damage.FirstCollisionFrame == 1001
                    && f.Victim.Health < before, "actual historical catch-up first Process pays before the private drain returns");
                Check(fact is { Provenance: ImpactProvenance.NativePoint } native && native.Point == impact.Point
                    && ((native.Flags & ReplayShotFactFlags.Headshot) != 0) == impact.NativeHead,
                    "actual native collision point and head flag equal historical first segment");
                Check(before - f.Victim.Health == victimProfile.CompleteDamage(impact.NativeChannel,
                    BeamType.Imperialist, impact.NativeHead, false),
                    "actual native TakeDamage debit equals immutable profile cap for all seven hunters");
                Frame(1200);
                Check(!NetTimedImperialistImpact.TryResolve(emitted.Shot, claim, out _)
                    && NetTimedShotModifiers.TryClaimLimit(0, claim, victimProfile, out uint limit, out _, impact)
                    && limit == victimProfile.CompleteDamage(impact.NativeChannel, BeamType.Imperialist, impact.NativeHead, false),
                    "frozen admission geometry/profile survive grace after live history is unavailable");
            }
            foreach (int competitor in new[] { -1, 1 })
            {
                using var f = new Fixture(room, Hunter.Samus, false, false, competitor);
                int victim = f.Victim.Health, other = f.Other.Health;
                var emitted = f.Emit(); var claim = emitted.Claim;
                bool resolved = NetTimedImperialistImpact.TryResolve(emitted.Shot, claim, out _);
                Check(resolved == (competitor > 0), "nearest competing player is selected before the named victim");
                Check(competitor < 0 ? f.Victim.Health == victim && f.Other.Health < other
                    : f.Victim.Health < victim && f.Other.Health == other,
                    "actual native winner agrees with competing body selection");
            }
            using (var f = new Fixture(room, Hunter.Samus, false, false, -1))
            {
                f.Scene.GameState.Teams = true; f.Scene.GameState.TeamCount = 2;
                f.Scene.GameState.FriendlyFire = false;
                f.Owner.TeamIndex = f.Other.TeamIndex = 0; f.Victim.TeamIndex = 1;
                Check(MphRead.Mods.Multiplayer.TeamRules.AreAllies(f.Owner.TeamIndex, f.Other.TeamIndex)
                    && !MphRead.Mods.Multiplayer.TeamRules.AreAllies(f.Owner.TeamIndex, f.Victim.TeamIndex),
                    "declared valid two-team native contract identifies the nearer ally and farther enemy");
                int victim = f.Victim.Health, other = f.Other.Health;
                var emitted = f.Emit();
                Check(!NetTimedImperialistImpact.TryResolve(emitted.Shot, emitted.Claim, out _),
                    "nearer friendly body remains the native collision winner before team damage rules");
                Check(f.Victim.Health == victim && f.Other.Health == other,
                    "native friendly damage refusal does not let the ray pay a farther enemy");
            }
            using (var f = new Fixture(room, Hunter.Samus, false, false))
            {
                var start = f.Base + Vector3.UnitY * .5f;
                Check(BeamProjectileEntity.TryBeamObstacle(start, start + f.Direction * 100, f.Scene,
                    out var wall, out _) && wall.Distance < .95f && wall.Distance > .05f,
                    "actual room has a native wall within the first segment");
                f.Victim.ModPlaceAt(start + f.Direction * (wall.Distance * 100 + 2) - Vector3.UnitY * .5f);
                int before = f.Victim.Health;
                var emitted = f.Emit();
                Check(!NetTimedImperialistImpact.TryResolve(emitted.Shot, emitted.Claim, out _),
                    "native historical wall refuses the named body behind it");
                Check(f.Victim.Health == before, "actual native surface winner prevents behind-wall damage");
            }
            using (var f = new Fixture(room, Hunter.Samus, false, false))
            {
                var emitted = f.Emit(rewind: false); var claim = emitted.Claim;
                claim.Flags |= HitClaimPacket.FlagHeadshot;
                Check(!emitted.Shot.Damage.FirstSegmentKnown
                    && !NetTimedImperialistImpact.TryResolve(emitted.Shot, claim, out _),
                    "actual current-world spawn cannot invent a historical first-segment proof");
                Check(NetTimedDamageProfile.TryGet(1, claim.VictimGeneration, claim.VictimLifeId,
                    claim.AckFrame, out var bodyProfile)
                    && NetTimedShotModifiers.TryClaimLimit(0, claim, bodyProfile, out uint limit, out bool head)
                    && !head && limit == bodyProfile.CompleteDamage(emitted.Shot.Damage.Body, BeamType.Imperialist, false, false),
                    "unknown exact geometry retains ordinary body cap without claimed head amount or bypass");
            }
            int oldCeiling = NetUnlagged.MaxRewindFrames;
            try
            {
                NetUnlagged.MaxRewindFrames = 1;
                using var f = new Fixture(room, Hunter.Samus, false, false);
                var emitted = f.Emit();
                Check(!emitted.Shot.Damage.FirstSegmentKnown
                    && !NetTimedImperialistImpact.TryResolve(emitted.Shot, emitted.Claim, out _),
                    "actually clamped target does not relabel requested ACK history as the served collision");
            }
            finally { NetUnlagged.MaxRewindFrames = oldCeiling; }
            foreach (Hunter source in new[] { Hunter.Kanden, Hunter.Samus })
            {
                using var f = new Fixture(room, Hunter.Samus, true, false, sourceHunter: source);
                var emitted = f.Emit(BeamType.VoltDriver, 180);
                Check(emitted.Beam.Flags.TestFlag(BeamFlags.Charged)
                    && emitted.Beam.ModOwnerHunter == source,
                    "actual charged native Volt retains original source hunter");
                var claim = emitted.Claim; claim.Flags |= HitClaimPacket.FlagDisrupted;
                Check(NetTimedDamageProfile.TryGet(1, claim.VictimGeneration, claim.VictimLifeId,
                    claim.AckFrame, out var effectProfile) && effectProfile.BalancedDisruptImmunity == 0,
                    "actual pre-impact profile captures zero native immunity");
                f.Owner.ModSetHunter(source == Hunter.Kanden ? Hunter.Samus : Hunter.Kanden);
                int before = f.Victim.Health;
                for (int step = 0; step < 60 && f.Victim.Health == before && emitted.Beam.Lifespan > 0; step++) emitted.Beam.Process();
                bool affinity = source == Hunter.Kanden;
                Check(f.Victim.Health < before && f.Victim.ModDisruptedTimer == (affinity ? 75 : 120)
                    && f.Victim.ModBalancedDisruptImmunityTimer == (affinity ? 195 : 0),
                    "actual native disruption uses captured source policy after later owner mutation");
                f.Victim.ModResetBalancedAffinityState();
                Check(NetTimedShotEffects.ApplyDisruption(f.Victim, emitted.Shot, claim, effectProfile)
                    && f.Victim.ModDisruptedTimer == (affinity ? 75 : 120)
                    && f.Victim.ModBalancedDisruptImmunityTimer == (affinity ? 195 : 0),
                    "declared same-clock rescue helper matches the real native applier duration");
                if (affinity)
                    Check(!NetTimedShotEffects.ApplyDisruption(f.Victim, emitted.Shot, claim, effectProfile)
                        && f.Victim.ModBalancedDisruptImmunityTimer == 195,
                        "same-clock immunity retry cannot refresh native control");
                f.Victim.ModResetBalancedAffinityState(); Frame(unchecked(claim.AckFrame + 1));
                Check(!NetTimedShotEffects.ApplyDisruption(f.Victim, emitted.Shot, claim, effectProfile),
                    "late additional disruption is refused while base payment remains independently owned");
            }
            Console.WriteLine($"PASS: {_checks} actual admitted native first-segment/profile assertions ({profile}).");
            Console.WriteLine("SCOPE: declared source resources/health/placements, real canonical carrier/private admission/native Spawn/Process/TakeDamage and published profiles. NOT_RUN: authenticated UDP, graphics/audio, temporal affliction rescue, later flight, homing/ricochet/splash/continuous/turret, full canary acceptance.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            NetSession.ConfirmedImpactSink = oldSink; NetConfirmedImpacts.Enabled = oldImpacts;
            NetTimedShotProof.Mode = oldMode; NetShotProof.Mode = oldProof;
            NetFireEvents.AcceptedPlaybackEnabled = oldPlayback; NetTestScript.Enabled = oldScript;
        }
    }
}
