using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
            Hunter sourceHunter = Hunter.Samus, TimedGrantProofMode timedMode = TimedGrantProofMode.Enforce)
        {
            NetShotProof.Mode = ShotProofMode.Shadow; NetTimedShotProof.Mode = timedMode;
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
        internal byte FullChargeFrames(BeamType weapon)
        {
            bool affinity = Weapons.GetAffinityBeam(Owner.Hunter) == weapon;
            return checked((byte)(Scene.WeaponRules[(int)weapon + (affinity ? 9 : 0)].FullCharge * 2));
        }
        internal (TimedShotCertificate Shot, BeamProjectileEntity Beam, HitClaimPacket Claim) Emit(
            BeamType weapon = BeamType.Imperialist, byte charge = 0, bool rewind = true,
            bool malformedDoubleHint = false, bool requireTrusted = true, Action? beforeAcceptedDrain = null)
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
            if (malformedDoubleHint) modifiers |= FireEvent.DoubleStateBit;
            var fire = new FireEvent(++_shot, source, ack, 0, charge == 0 ? FireEventKind.PressFire : FireEventKind.ReleaseFire, (byte)weapon,
                charge, modifiers, FireEvent.FlagPose, Base + Vector3.UnitY * .5f,
                Direction, Direction, Direction, SourceLife: NetPlayerLifecycle.Get(0));
            Submit(Packet(fire, source));
            beforeAcceptedDrain?.Invoke();
            NetHooks.TryApplyRemoteInput(Owner, 0);
            var key = ShotKey.For(0, fire.ShotId);
            Check(NetFireEvents.CaptureLedger(0).TryGet(key, out var admitted, out var disposition, out _)
                && admitted.Fire == fire && disposition == ShotDisposition.Fired,
                "actual private admitted drain owns native terminal");
            var beam = Owner.EquipInfo.Beams.Single(b => b.ModLaunchKey == key && b.ModComponentId != 0);
            Check(NetTimedShotProof.TryGet(key, fire.SourceFrame, out var shot) && (!requireTrusted || shot.TrustedSource)
                && shot.Components == 1, requireTrusted
                    ? "actual completed certificate retains verified native source and component"
                    : "actual completed certificate retains a component without requiring supported source proof");
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

    private sealed record FastArm(string Case, CombatFastMode Mode, int Before, int After,
        long Payments, uint Rng1Before, uint Rng2Before, uint Rng1After, uint Rng2After,
        string Scores, string Semantics, string[] Replay, CombatFastRow[] Rows, object FastCapture);
    private static string Digest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static FastArm FastSceneArm(string room, string control, CombatFastMode mode)
    {
        NetCombatFastPath.Mode = mode; NetCombatFastPath.ResetEvidenceForTests();
        // Both words are set before constructing/loading either arm. Do not
        // reset after setup: setup and shot RNG differences must stay visible.
        // LoadGame otherwise resets ONLY the first arm to native defaults when
        // the console save-slot sentinel still differs. Declare no save-slot
        // transition in either arm; Scene's constructor copies these words into
        // its owned Random before BuildPlayers/AddRoom/OnLoad consumes them.
        Menu.PreviousSaveSlot = Menu.SaveSlot;
        Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
        using var f = new Fixture(room, Hunter.Samus, balanced: control == "balanced",
            head: control == "head", timedMode: TimedGrantProofMode.Shadow);
        if (control == "health-one") f.Victim.Health = 1;
        if (control == "spawn-immunity")
            typeof(PlayerEntity).GetField("_spawnInvulnTimer", Hidden)!.SetValue(f.Victim, (ushort)20);
        if (control == "ordinary-timer")
            typeof(PlayerEntity).GetField("_damageInvulnTimer", Hidden)!.SetValue(f.Victim, (ushort)20);
        if (control == "alt") f.Victim.ModForceForm(true);
        // A declared native flag at the contact seam, with no AI controller or
        // bot update, isolates exclusion of bot damage rules from this subset.
        if (control == "bot") f.Victim.IsBot = true;
        int before = f.Victim.Health;
        uint rng1 = f.Scene.Random.Rng1, rng2 = f.Scene.Random.Rng2;
        int semanticStart = f.Scene.MatchEvents.Bus.Events.Count;
        long payments = NetCombatPayments.LifetimePayments;
        var replay = new List<string>();
        void Capture(ReplayTimelineRecord record) => replay.Add(JsonSerializer.Serialize(new {
            record.RecordingFrame, record.ServerTick, record.Kind, record.Marker,
            Bytes = Convert.ToHexString(record.Payload) }));
        ReplayCapture.Recorder.Accepted += Capture;
        try
        {
            Action? beforeAcceptedDrain = null;
            if (control == "current-alt-after-history")
                beforeAcceptedDrain = () => {
                    Check(!f.Victim.IsAltForm && NetUnlagged.TryHistoricalPose(f.Victim, 1001, out var pose)
                        && !pose.AltForm,
                        "form control starts with actual retained frame1001 biped before accepted catch-up");
                    // Declared current-form change only. The ordinary native
                    // rewind must recover the earlier biped without changing
                    // the live alt flag; no history cells or poses are injected.
                    f.Victim.ModForceForm(true);
                    Check(f.Victim.IsAltForm && !f.Victim.IsMorphing && !f.Victim.IsUnmorphing,
                        "form control enters actual current alt before the accepted drain");
                };
            var emitted = f.Emit(malformedDoubleHint: control == "malformed-double",
                requireTrusted: control != "balanced", beforeAcceptedDrain: beforeAcceptedDrain);
            Check(emitted.Shot.Damage.FirstSegmentKnown && emitted.Shot.Damage.FirstCollisionFrame == 1001,
                control + "/" + mode + " uses actual accepted historical first segment");
            if (control == "malformed-double")
                Check(emitted.Shot.DoubleHintAgrees == false,
                    "malformed modifier control actually disagrees with independently retained funding");
            if (control == "current-alt-after-history")
            {
                bool retained = NetUnlagged.TryHistoricalPose(f.Victim, 1001, out var pose);
                Console.WriteLine("FAST_SHADOW_FORM_CONTROL " + JsonSerializer.Serialize(new {
                    Mode = mode, CurrentAlt = f.Victim.IsAltForm, HistoricalFrame = 1001,
                    HistoricalPoseAvailable = retained, HistoricalAlt = retained ? (bool?)pose.AltForm : null,
                    Before = before, After = f.Victim.Health,
                    Scope = "Declared current form change after actual native biped history; no injected history or controller." }));
                Check(f.Victim.IsAltForm && retained && !pose.AltForm,
                    "native catch-up restores current alt while exact frame1001 remains biped");
            }
            string scores = JsonSerializer.Serialize(new {
                f.Scene.GameState.Points, f.Scene.GameState.TeamPoints, f.Scene.GameState.Kills,
                f.Scene.GameState.TeamKills, f.Scene.GameState.Deaths, f.Scene.GameState.TeamDeaths,
                f.Scene.GameState.ShotsFired, f.Scene.GameState.ShotsHit,
                f.Scene.GameState.MatchDamageDealt, f.Scene.GameState.MatchDamageTaken,
                f.Scene.GameState.KillStreak, f.Scene.GameState.HeadshotKills });
            string semantics = JsonSerializer.Serialize(f.Scene.MatchEvents.Bus.Events.Skip(semanticStart).ToArray());
            return new(control, mode, before, f.Victim.Health,
                NetCombatPayments.LifetimePayments - payments, rng1, rng2,
                f.Scene.Random.Rng1, f.Scene.Random.Rng2, scores, semantics, replay.ToArray(),
                NetCombatFastPath.CaptureRows(), NetCombatFastPath.Capture());
        }
        finally { ReplayCapture.Recorder.Accepted -= Capture; }
    }

    internal static int RunFastShadowScene(string room)
    {
        var oldFast = NetCombatFastPath.Mode; var oldMode = NetTimedShotProof.Mode;
        var oldProof = NetShotProof.Mode;
        bool oldPlayback = NetFireEvents.AcceptedPlaybackEnabled, oldScript = NetTestScript.Enabled;
        uint oldRng1 = Rng.Rng1, oldRng2 = Rng.Rng2;
        int oldPreviousSaveSlot = Menu.PreviousSaveSlot;
        try
        {
            _checks = 0;
            foreach (string control in new[] { "body", "head", "health-one", "ordinary-timer",
                "spawn-immunity", "balanced", "alt", "bot", "malformed-double", "current-alt-after-history" })
            {
                var off = FastSceneArm(room, control, CombatFastMode.Off);
                var shadow = FastSceneArm(room, control, CombatFastMode.Shadow);
                // Persist diagnostic observations to stdout before assertions so
                // a missing positive seam is visible even in a failing run.
                Console.WriteLine("FAST_SHADOW_SCENE " + JsonSerializer.Serialize(new {
                    Schema = 1, Control = control, LegacyProof = "Shadow", TimedGrantProof = "Shadow",
                    Seeds = new uint[] { 8128, 0x8128a5a5 },
                    SeedScope = "Before Scene construction; identical no-save-slot-transition setup; no post-setup reseed.",
                    Off = new { off.Before, off.After, off.Payments, off.Rng1Before, off.Rng2Before,
                        off.Rng1After, off.Rng2After, ScoreSha256 = Digest(off.Scores),
                        SemanticSha256 = Digest(off.Semantics), ReplayRecords = off.Replay.Length,
                        ReplaySha256 = Digest(string.Join("\n", off.Replay)), off.FastCapture },
                    Shadow = new { shadow.Before, shadow.After, shadow.Payments,
                        shadow.Rng1Before, shadow.Rng2Before, shadow.Rng1After, shadow.Rng2After,
                        ScoreSha256 = Digest(shadow.Scores), SemanticSha256 = Digest(shadow.Semantics),
                        ReplayRecords = shadow.Replay.Length,
                        ReplaySha256 = Digest(string.Join("\n", shadow.Replay)), shadow.FastCapture },
                    Scope = "Controlled asset fixture with declared source inventory, health, placements and negative flags; actual canonical carrier/private drain/native collision/TakeDamage. No UDP or early claim settlement certificate." }));
                Check(off.Before == shadow.Before && off.After == shadow.After && off.Payments == shadow.Payments,
                    control + " exact Off/Shadow native health and component-payment parity");
                Check(off.Rng1Before == shadow.Rng1Before && off.Rng2Before == shadow.Rng2Before
                    && off.Rng1After == shadow.Rng1After && off.Rng2After == shadow.Rng2After,
                    control + " both seeded gameplay RNG words agree before and after real native execution");
                Check(off.Scores == shadow.Scores && off.Semantics == shadow.Semantics,
                    control + " exact score/counter arrays and semantic events agree");
                Check(off.Replay.Length > 0 && off.Replay.SequenceEqual(shadow.Replay),
                    control + " emitted canonical replay record metadata and payload bytes agree exactly");
                Check(off.Rows.Length == 0, control + " disabled fast capture observes no candidate rows");
                Check(NetCombatFastPath.Overflow == 0 && NetCombatFastPath.Invalid == 0,
                    control + " shadow capture is bounded and complete for this controlled contact");
                var native = shadow.Rows.Where(row => row.Stage == FastProbeStage.NativePreDamage).ToArray();
                Check(native.Length == 1 && native[0].Key.Shot.ShotId != 0 && native[0].Key.Victim == 1,
                    control + " actual native seam produces one exact selected-victim contact observation");
                var row = native[0];
                if (control is "body" or "head" or "health-one" or "ordinary-timer")
                {
                    Check(row.Decision.State == FastDecisionState.VerifiedNative && row.Decision.WouldPay
                        && row.Compared && row.Agrees && !row.Corrected && row.Payments == 1
                        && shadow.Payments == 1 && row.Decision.Damage.HealthDebit == (uint)(shadow.Before - shadow.After)
                        && row.ActualDebit == shadow.Before - shadow.After,
                        control + " positive verified native would-pay matches actual debit and exactly one commit");
                    Check(row.Decision.Damage.Head == (control == "head"),
                        control + " actual native body/head flag remains exact");
                    if (control == "health-one")
                        Check(row.Decision.Damage.NominalDamage > 1 && row.Decision.Damage.HealthDebit == 1
                            && row.Decision.Damage.Lethal && shadow.After == 0,
                            "actual one-health native death preserves distinct nominal and paid damage");
                }
                else if (control == "spawn-immunity")
                    Check(row.Decision.WouldReject && row.Decision.Damage.Reason == FastDamageReason.SpawnProtected
                        && row.Compared && row.Agrees && row.Payments == 0 && shadow.Payments == 0
                        && shadow.Before == shadow.After,
                        "actual native spawn-protection call independently predicts refusal without payment");
                else if (control == "current-alt-after-history")
                    Check(row.NativeEvidence.SourceVerified && row.NativeEvidence.GeometryVerified
                        && !row.NativeEvidence.VictimGates.Biped
                        && row.Decision.State == FastDecisionState.LegacyRequired
                        && row.Decision.Reason == FastDecisionReason.DamageUnavailable
                        && row.Decision.Damage.Status == FastDamageAvailability.Unavailable
                        && row.Decision.Damage.Reason == FastDamageReason.UnsupportedContext
                        && !row.Decision.Eligible && !row.Decision.WouldPay && !row.Decision.WouldReject
                        && shadow.Before > shadow.After && shadow.Payments == 1 && row.Payments == 1,
                        "verified historical biped contact still pays natively but current alt cannot acquire a fast certificate");
                else
                    Check(!row.Decision.Eligible && !row.Decision.WouldPay && !row.Decision.WouldReject,
                        control + " unsupported or unfunded context falls back without a legal-damage certificate");
            }
            Console.WriteLine($"PASS: {_checks} controlled asset fast Off/Shadow native assertions; no all-fallback positive acceptance.");
            Console.WriteLine("SCOPE: declared asset fixture, actual accepted carrier and first native historical collision. Exact observed native health/payments, score arrays, semantic events, both RNG words and emitted replay records compared. NOT_RUN: UDP, graphics/audio, posthumous source lifecycle, cross-shooter trade/frontier, fresh claim fast settlement, later flight and other weapon/effect families.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            NetCombatFastPath.Mode = oldFast; NetTimedShotProof.Mode = oldMode; NetShotProof.Mode = oldProof;
            NetFireEvents.AcceptedPlaybackEnabled = oldPlayback; NetTestScript.Enabled = oldScript;
            Menu.PreviousSaveSlot = oldPreviousSaveSlot;
            Rng.SetRng1(oldRng1); Rng.SetRng2(oldRng2);
        }
    }

    private sealed record PaidReceiptArm(string Control, CombatFastMode Mode, int Before,
        int AfterNative, int AfterClaims, uint NativeSequence, uint FinalSequence,
        long Payments, long FastFirst, long FastRetry, long Corrections,
        CombatAckEntry NativeOutcome, CombatAckEntry FinalOutcome, byte[] FirstAck, byte[] RetryAck,
        uint Rng1Before, uint Rng2Before, uint Rng1After, uint Rng2After,
        string Scores, string Semantics, string[] Replay, CombatFastRow[] Rows, object FastCapture);

    private static byte[] AckBytes(in CombatAckEntry outcome)
    { var bytes = new byte[CombatAckEntry.Size]; outcome.Write(bytes); return bytes; }

    private static object ExportPaidArm(PaidReceiptArm arm) => new {
        arm.Control, arm.Mode, arm.Before, arm.AfterNative, arm.AfterClaims,
        arm.NativeSequence, arm.FinalSequence, arm.Payments, arm.FastFirst, arm.FastRetry, arm.Corrections,
        NativeAck = Convert.ToHexString(AckBytes(arm.NativeOutcome)),
        FinalAck = Convert.ToHexString(AckBytes(arm.FinalOutcome)),
        FirstAck = Convert.ToHexString(arm.FirstAck), RetryAck = Convert.ToHexString(arm.RetryAck),
        arm.Rng1Before, arm.Rng2Before, arm.Rng1After, arm.Rng2After,
        ScoreSha256 = Digest(arm.Scores), SemanticSha256 = Digest(arm.Semantics),
        ReplayRecords = arm.Replay.Length, ReplaySha256 = Digest(string.Join("\n", arm.Replay)),
        arm.FastCapture
    };

    private static CombatAckEntry ReceivePaidClaim(in HitClaimPacket claim)
    {
        ushort id = claim.ClaimId;
        var answers = new List<CombatAckEntry>();
        var saved = NetHitClaims.CombatAckSink;
        NetHitClaims.CombatAckSink = (slot, entries) => {
            foreach (var entry in entries)
                if (slot == 0 && entry.ClaimId == id) answers.Add(entry);
        };
        try
        {
            var bytes = new byte[1 + HitClaimPacket.Size]; bytes[0] = 1; claim.Write(bytes.AsSpan(1));
            NetHitClaims.Receive(0, bytes);
            for (int i = 0; i <= 2 * NetHitClaims.MaxGraceFrames + 2 && answers.Count == 0; i++)
            { NetHitClaims.Tick(); if (answers.Count == 0) Frame(unchecked(NetSession.NetFrame + 1)); }
            Check(answers.Count == 1 && answers[0].ShotId == claim.ShotId,
                "serialized paid claim produces exactly one correlated full terminal ACK");
            return answers[0];
        }
        finally { NetHitClaims.CombatAckSink = saved; }
    }

    private static PaidReceiptArm PaidReceiptSceneArm(string room, string control,
        BeamType weapon, bool nativeHead, bool claimHead, CombatFastMode mode)
    {
        NetCombatFastPath.Mode = mode; NetCombatFastPath.ResetEvidenceForTests();
        Menu.PreviousSaveSlot = Menu.SaveSlot;
        Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
        using var f = new Fixture(room, Hunter.Samus, balanced: false, head: nativeHead,
            timedMode: TimedGrantProofMode.Shadow);
        Check(NetHitClaims.Arbitrating, control + " real authority arbitrates paid receipts");
        int before = f.Victim.Health;
        uint rng1 = f.Scene.Random.Rng1, rng2 = f.Scene.Random.Rng2;
        long payments = NetCombatPayments.LifetimePayments;
        long corrections = NetHitClaims.HeadshotCorrections;
        int semanticStart = f.Scene.MatchEvents.Bus.Events.Count;
        var replay = new List<string>();
        void Capture(ReplayTimelineRecord record) => replay.Add(JsonSerializer.Serialize(new {
            record.RecordingFrame, record.ServerTick, record.Kind, record.Marker,
            Bytes = Convert.ToHexString(record.Payload) }));
        ReplayCapture.Recorder.Accepted += Capture;
        try
        {
            var emitted = f.Emit(weapon);
            // Imperialist catches up inside the accepted drain. Power Beam is
            // allowed to execute its actual stock Process until collision; no
            // damage call, payment reservation, ACK or physical witness is seeded.
            for (int i = 0; i < 60 && f.Victim.Health == before && emitted.Beam.Lifespan > 0; i++)
                emitted.Beam.Process();
            var claim = emitted.Claim; claim.ClaimId = 1;
            var key = NetShotProof.PaymentKey(0, claim);
            Check(f.Victim.Health < before && NetCombatPayments.LifetimePayments - payments == 1
                && NetCombatPayments.TryGet(key, claim.Beam, out _),
                control + " actual accepted stock collision pays the exact native component once");
            Check(NetCombatPayments.TryGet(key, claim.Beam, out var native)
                && native.Accepted && native.DamageApplied == before - f.Victim.Health
                && native.DamageSequence != 0 && native.HealthAfter == f.Victim.Health
                && native.Flags.HasFlag(CombatAckFlags.Headshot) == nativeHead,
                control + " native ledger supplies material body/head outcome, health and sequence");
            int afterNative = f.Victim.Health;
            uint nativeSequence = NetDamage.Sequence(1);
            Check(nativeSequence == native.DamageSequence,
                control + " immutable native outcome names the actual damage sequence");
            // The claim is a declared claimant input, including deliberate
            // mismatches. Frame1001 is real placed-body history in this fixture;
            // the accepted Imperialist launch/ACK stamp remains unchanged.
            claim.AckFrame = weapon == BeamType.Imperialist ? 1001u : claim.AckFrame;
            claim.Damage = nativeHead == claimHead ? native.DamageApplied
                : checked((ushort)(claimHead ? emitted.Beam.HeadshotDamage : emitted.Beam.Damage));
            if (claimHead) claim.Flags |= HitClaimPacket.FlagHeadshot;
            else claim.Flags &= unchecked((byte)~HitClaimPacket.FlagHeadshot);
            Check(NetUnlagged.PositionAt(1, claim.AckFrame, claim.VictimGeneration,
                claim.VictimLifeId, out var historical)
                && NetHitClaims.WithinClaimRadius(historical, claim.HitPoint, claim.Beam),
                control + " claimant point uses actual retained eligible victim history");
            Console.WriteLine("FAST_PAID_NATIVE " + JsonSerializer.Serialize(new {
                Control = control, Mode = mode, Weapon = weapon, NativeHead = nativeHead,
                ClaimHead = claimHead, Before = before, After = afterNative,
                NativeAck = Convert.ToHexString(AckBytes(native)), NativeSequence = nativeSequence,
                Scope = "Actual accepted stock beam Process and native payment; claimant fields are declared fixture inputs." }));
            long fastBefore = NetCombatFastPath.PaidReceipts;
            var first = ReceivePaidClaim(claim);
            long fastFirst = NetCombatFastPath.PaidReceipts - fastBefore;
            int afterFirst = f.Victim.Health;
            uint firstSequence = NetDamage.Sequence(1);
            Check(NetCombatPayments.TryGet(key, claim.Beam, out var final),
                control + " exact paid ledger remains available after first claim");
            var expected = final; expected.ClaimId = claim.ClaimId; expected.Result = first.Result;
            Check(AckBytes(first).SequenceEqual(AckBytes(expected)),
                control + " first terminal copies every immutable paid material field");
            var retry = ReceivePaidClaim(claim);
            long fastRetry = NetCombatFastPath.PaidReceipts - fastBefore;
            Check(AckBytes(retry).SequenceEqual(AckBytes(first))
                && f.Victim.Health == afterFirst && NetDamage.Sequence(1) == firstSequence
                && NetCombatPayments.TryGet(key, claim.Beam, out var afterRetry)
                && AckBytes(afterRetry).SequenceEqual(AckBytes(final)),
                control + " byte-exact cached retry preserves material, health, sequence and ledger");
            Check(NetCombatPayments.LifetimePayments - payments == 1,
                control + " claims and retry never create another component payment");
            if (nativeHead == claimHead)
                Check(first.Result == HitVerdictPacket.ResultDuplicate
                    && AckBytes(native).SequenceEqual(AckBytes(final))
                    && afterFirst == afterNative && firstSequence == nativeSequence
                    && NetHitClaims.HeadshotCorrections == corrections,
                    control + " matching paid receipt only acknowledges existing native state");
            else if (claimHead)
                Check(first.Result == (byte)CombatAckResult.Corrected
                    && final.Flags.HasFlag(CombatAckFlags.Headshot)
                    && final.DamageApplied == claim.Damage && before - afterFirst == claim.Damage
                    && afterFirst < afterNative && firstSequence != nativeSequence
                    && NetHitClaims.HeadshotCorrections - corrections == 1,
                    control + " body-to-head fallback retains exactly one legacy difference correction");
            else
                Check(first.Result == HitVerdictPacket.ResultDuplicate
                    && AckBytes(native).SequenceEqual(AckBytes(final))
                    && afterFirst == afterNative && firstSequence == nativeSequence
                    && NetHitClaims.HeadshotCorrections == corrections,
                    control + " head-to-body fallback preserves original native head payment");
            string scores = JsonSerializer.Serialize(new {
                Health = new[] { f.Owner.Health, f.Victim.Health, f.Other.Health },
                f.Scene.GameState.Points, f.Scene.GameState.TeamPoints, f.Scene.GameState.Kills,
                f.Scene.GameState.TeamKills, f.Scene.GameState.Deaths, f.Scene.GameState.TeamDeaths,
                f.Scene.GameState.ShotsFired, f.Scene.GameState.ShotsHit,
                f.Scene.GameState.MatchDamageDealt, f.Scene.GameState.MatchDamageTaken,
                f.Scene.GameState.KillStreak, f.Scene.GameState.HeadshotKills });
            return new(control, mode, before, afterNative, f.Victim.Health, nativeSequence,
                NetDamage.Sequence(1), NetCombatPayments.LifetimePayments - payments, fastFirst, fastRetry,
                NetHitClaims.HeadshotCorrections - corrections, native, final, AckBytes(first), AckBytes(retry),
                rng1, rng2, f.Scene.Random.Rng1, f.Scene.Random.Rng2, scores,
                JsonSerializer.Serialize(f.Scene.MatchEvents.Bus.Events.Skip(semanticStart).ToArray()),
                replay.ToArray(), NetCombatFastPath.CaptureRows(), NetCombatFastPath.Capture());
        }
        finally { ReplayCapture.Recorder.Accepted -= Capture; }
    }

    internal static int RunPaidReceiptScene(string profile, string room)
    {
        var oldFast = NetCombatFastPath.Mode; var oldTimed = NetTimedShotProof.Mode;
        var oldProof = NetShotProof.Mode; bool oldEnabled = NetHitClaims.Enabled;
        bool oldPlayback = NetFireEvents.AcceptedPlaybackEnabled, oldScript = NetTestScript.Enabled;
        uint oldRng1 = Rng.Rng1, oldRng2 = Rng.Rng2;
        int oldPreviousSaveSlot = Menu.PreviousSaveSlot;
        try
        {
            _checks = 0; NetHitClaims.Enabled = true;
            var controls = new[] {
                ("powerbeam-body", BeamType.PowerBeam, false, false),
                ("powerbeam-head", BeamType.PowerBeam, true, true),
                ("imperialist-body", BeamType.Imperialist, false, false),
                ("imperialist-head", BeamType.Imperialist, true, true),
                ("imperialist-body-claimed-head", BeamType.Imperialist, false, true),
                ("imperialist-head-claimed-body", BeamType.Imperialist, true, false) };
            foreach (var (control, weapon, nativeHead, claimHead) in controls)
            {
                var off = PaidReceiptSceneArm(room, control, weapon, nativeHead, claimHead, CombatFastMode.Off);
                var paid = PaidReceiptSceneArm(room, control, weapon, nativeHead, claimHead, CombatFastMode.Paid);
                Console.WriteLine("FAST_PAID_SCENE " + JsonSerializer.Serialize(new {
                    Schema = 1, Control = control, Weapon = weapon, NativeHead = nativeHead,
                    ClaimHead = claimHead, Profile = profile,
                    Off = ExportPaidArm(off), Paid = ExportPaidArm(paid),
                    Scope = "Six declared actual native stock-contact fixtures; Off/Paid serialized claim and exact cached retry parity. No UDP, early unpaid settlement, timing or all-family certificate." }));
                Check(off.Before == paid.Before && off.AfterNative == paid.AfterNative
                    && off.AfterClaims == paid.AfterClaims && off.NativeSequence == paid.NativeSequence
                    && off.FinalSequence == paid.FinalSequence && off.Payments == 1 && paid.Payments == 1,
                    control + " exact Off/Paid health, sequence and single native payment parity");
                Check(AckBytes(off.NativeOutcome).SequenceEqual(AckBytes(paid.NativeOutcome))
                    && AckBytes(off.FinalOutcome).SequenceEqual(AckBytes(paid.FinalOutcome))
                    && off.FirstAck.SequenceEqual(paid.FirstAck) && off.RetryAck.SequenceEqual(paid.RetryAck),
                    control + " all23 paid outcome and terminal ACK bytes agree across modes");
                Check(off.Rng1Before == paid.Rng1Before && off.Rng2Before == paid.Rng2Before
                    && off.Rng1After == paid.Rng1After && off.Rng2After == paid.Rng2After,
                    control + " both gameplay RNG words agree across full native and claim execution");
                Check(off.Scores == paid.Scores && off.Semantics == paid.Semantics
                    && off.Replay.Length > 0 && off.Replay.SequenceEqual(paid.Replay),
                    control + " all player health, score arrays, semantic events and nonempty replay bytes agree");
                long expectedFast = nativeHead == claimHead ? 1 : 0;
                Check(off.FastFirst == 0 && off.FastRetry == 0 && paid.FastFirst == expectedFast
                    && paid.FastRetry == expectedFast && off.Corrections == paid.Corrections,
                    control + " only matching first receipts bypass; retry cache and mismatches never increment bypass");
                var ready = paid.Rows.Where(row => row.Stage == FastProbeStage.ClaimReady).ToArray();
                Check(off.Rows.Length == 0 && ready.Length == 1,
                    control + " disabled capture is empty and admitted first claim has one exact ready row");
                Check(nativeHead == claimHead
                    ? ready[0].Decision.State == FastDecisionState.VerifiedPaid
                        && ready[0].Compared && ready[0].Agrees
                    : ready[0].Decision.State == FastDecisionState.LegacyRequired
                        && ready[0].Decision.Reason == FastDecisionReason.HeadMismatch,
                    control + " admitted decision distinguishes exact paid material from strict head mismatch");
                Check(NetCombatFastPath.Overflow == 0 && NetCombatFastPath.Invalid == 0
                    && NetCombatFastPath.UnjoinedTerminal == 0,
                    control + " finite capture has no overflow, invalid identity or unjoined terminal");
            }
            Console.WriteLine($"PASS: {_checks} actual native paid-receipt Off/Paid assertions across six controls.");
            Console.WriteLine("SCOPE: accepted stock Power Beam/Imperialist body/head Process, native immutable paid ledger, serialized first claims and byte-identical retries; declared resource/health/placement/head-mismatch inputs. NOT_RUN: authenticated UDP, fresh unpaid rescue fast path, posthumous/trade/all-family certification, GL/audio or performance.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            NetCombatFastPath.Mode = oldFast; NetTimedShotProof.Mode = oldTimed;
            NetShotProof.Mode = oldProof; NetHitClaims.Enabled = oldEnabled;
            NetFireEvents.AcceptedPlaybackEnabled = oldPlayback; NetTestScript.Enabled = oldScript;
            Menu.PreviousSaveSlot = oldPreviousSaveSlot;
            Rng.SetRng1(oldRng1); Rng.SetRng2(oldRng2);
        }
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
            foreach (var (source, balanced) in new[] {
                (Hunter.Kanden, true), (Hunter.Kanden, false), (Hunter.Samus, true) })
            {
                using var f = new Fixture(room, Hunter.Samus, balanced, false, sourceHunter: source);
                // The native F30 metadata's FullCharge is converted to F60;
                // the input proof correctly refuses a larger claimed charge.
                var emitted = f.Emit(BeamType.VoltDriver, f.FullChargeFrames(BeamType.VoltDriver));
                bool disrupt = source == Hunter.Kanden;
                bool affinity = disrupt && balanced;
                var policy = !disrupt ? TimedDisruptionPolicy.None
                    : affinity ? TimedDisruptionPolicy.BalancedKanden : TimedDisruptionPolicy.Ordinary;
                Check(emitted.Beam.Flags.TestFlag(BeamFlags.Charged)
                    && emitted.Beam.ModOwnerHunter == source
                    && emitted.Beam.Afflictions.TestFlag(Affliction.Disrupt) == disrupt
                    && emitted.Shot.Damage.DisruptionPolicy == policy
                    && ((emitted.Shot.Damage.EffectsFor(true) & HitClaimPacket.FlagDisrupted) != 0) == disrupt,
                    "actual charged native Volt retains its source row and native affliction policy");
                var claim = emitted.Claim; claim.Flags |= HitClaimPacket.FlagDisrupted;
                Check(NetTimedDamageProfile.TryGet(1, claim.VictimGeneration, claim.VictimLifeId,
                    claim.AckFrame, out var effectProfile) && effectProfile.BalancedDisruptImmunity == 0,
                    "actual pre-impact profile captures zero native immunity");
                f.Owner.ModSetHunter(source == Hunter.Kanden ? Hunter.Samus : Hunter.Kanden);
                int before = f.Victim.Health;
                for (int step = 0; step < 60 && f.Victim.Health == before && emitted.Beam.Lifespan > 0; step++) emitted.Beam.Process();
                int control = !disrupt ? 0 : affinity ? 75 : 120;
                int immunity = affinity ? 195 : 0;
                Check(f.Victim.Health < before && f.Victim.ModDisruptedTimer == control
                    && f.Victim.ModBalancedDisruptImmunityTimer == immunity,
                    "actual native disruption uses captured source policy after later owner mutation");
                f.Victim.ModResetBalancedAffinityState(); f.Victim.ModSetDisrupted(false);
                Check(NetTimedShotEffects.ApplyDisruption(f.Victim, emitted.Shot, claim, effectProfile) == disrupt
                    && f.Victim.ModDisruptedTimer == control
                    && f.Victim.ModBalancedDisruptImmunityTimer == immunity,
                    "declared same-clock rescue matches native duration or refuses an absent native effect");
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
