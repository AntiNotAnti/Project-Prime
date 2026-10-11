using System;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Asset-backed native damage and arbitration regressions. Fixtures emit through
// accepted fire playback; native repeats use the actual public TakeDamage hook.
internal static class SelfSplashTests
{
    private static int _checks;
    private static PlayerEntity _shooter = null!, _victim = null!;
    private static Vector3 _base, _direction;
    private static uint _id, _frame;
    private static ushort _claimId;
    private static ShotKey _physicalShot;
    private static uint _physicalComponent;
    private static CombatAckEntry _lastAck;
    private static void Check(bool ok, string label)
    { _checks++; if (!ok) throw new InvalidOperationException(label); Console.WriteLine($"SELF SPLASH PASS {label}"); }
    private static void Frame(uint frame)
    { _frame = frame; typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, frame); }
    private static void Record()
    { NetUnlagged.Record(_frame); NetDynamicGeometryHistory.RecordWorld(_frame); }
    private static ShotProofSource Source(ushort weapons = 0x1FF, int ammo0 = 10000, int ammo1 = 10000,
        bool infinite = false, ushort charges = 0x1FF, bool supported = true)
        => new(weapons, charges, ammo0, ammo1, infinite, _shooter.Hunter, false, false, supported);
    private static void Prepare(ShotProofSource? source = null)
    {
        foreach (var beam in _shooter.EquipInfo.Beams) beam.Lifespan = 0;
        NetFireEvents.Reset(); NetHitClaims.Reset(); _physicalShot = default; _physicalComponent = 0;
        Frame(_frame + 200);
        foreach (var player in new[] { _shooter, _victim })
        {
            player.Health = 999; player.ModForceForm(false); player.Controls.ClearAll();
            player.ModReleaseSpawnProtection(); player.ModSetFrozen(false); player.ModSetBurning(false); player.ModSetDisrupted(false); player.Speed = Vector3.Zero;
            typeof(PlayerEntity).GetField("_spawnInvulnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(player, (ushort)0);
            typeof(PlayerEntity).GetField("_damageInvulnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(player, (ushort)0);
        }
        _shooter.ModPlaceAt(_base); _victim.ModPlaceAt(_base + _direction * 3);
        NetHitClaims.Tick(); Record();
        NetShotProof.Seed(_shooter, source ?? Source());
    }
    private static int NativeWeaponIndex(BeamType weapon)
        => (int)weapon + (Weapons.GetAffinityBeam(_shooter.Hunter) == weapon ? 9 : 0);
    private static BeamProjectileEntity Emit(BeamType weapon = BeamType.Imperialist, byte charge = 0,
        uint modifiers = 0, Vector3? ray = null, bool preserve = false, Vector3? ownerAim = null, uint? sourceFrame = null,
        bool? affinity = null)
    {
        if (!preserve) foreach (var old in _shooter.EquipInfo.Beams) old.Lifespan = 0;
        var direction = ray ?? _direction;
        uint source = sourceFrame ?? _frame;
        var aim = ownerAim ?? direction.Normalized();
        if (affinity ?? Weapons.GetAffinityBeam(_shooter.Hunter) == weapon)
            modifiers |= FireEvent.AffinityStateBit;
        else if (affinity.HasValue) modifiers &= ~FireEvent.AffinityStateBit;
        var fire = new FireEvent(++_id, source, _frame, 0, FireEventKind.PressFire,
            (byte)weapon, charge, modifiers, FireEvent.FlagPose, _shooter.Position + Vector3.UnitY * .5f,
            direction, aim, aim, SourceLife: NetPlayerLifecycle.Get(_shooter.SlotIndex));
        var packet = new IntentPacket { MatchId = NetSession.CurrentMatchId, AuthorityEpoch = NetSession.AuthorityEpoch,
            SlotGeneration = NetPlayerLifecycle.Generation(_shooter.SlotIndex), LifeId = NetPlayerLifecycle.Get(_shooter.SlotIndex),
            Frame = source, AckFrame = _frame, Position = _shooter.Position, Aim = aim, WeaponSelect = (byte)weapon,
            Buttons = IntentButtons.InPlayState, HasFireEvents = true, FireEventCount = 1 };
        if (weapon == BeamType.Imperialist || charge <= 1)
        {
            // Ordinary fixtures provide a real press edge and a weapon reset for scope.
            // Charged Missile fixtures deliberately omit the unproven hold history.
            packet.ShotInputCount = 2;
            packet.ShotInputs[1] = ShotInputHistory.Sample((byte)BeamType.PowerBeam,
                shoot: false, physicalZoom: false, eligible: true);
            packet.ShotInputs[0] = ShotInputHistory.Sample((byte)weapon,
                shoot: true, physicalZoom: fire.ScopedAtFire, eligible: true);
            packet.Buttons |= IntentButtons.Shoot;
            if (fire.ScopedAtFire) packet.Buttons |= IntentButtons.Zoom;
        }
        packet.FireEvents[0] = fire;
        NetShotProof.ObservePose(_shooter, packet);
        Check(NetFireEvents.Accept(_shooter.SlotIndex, packet), "actual carrier admitted");
        NetFireEvents.DrainAccepted(_shooter);
        var key = ShotKey.For(_shooter.SlotIndex, fire.ShotId);
        bool found = NetFireEvents.CaptureLedger(_shooter.SlotIndex).TryGet(key, out _, out var disposition, out var dispositionReason);
        Check(found && disposition == ShotDisposition.Fired,
            $"actual accepted event emitted once found={found} disposition={disposition} reason={dispositionReason}");
        var beam = _shooter.EquipInfo.Beams.FirstOrDefault(b => b.ModLaunchKey == key && b.ModComponentId != 0);
        Check(beam != null, "production projectile has shot and component identity");
        return beam!;
    }
    private static HitClaimPacket Claim(BeamProjectileEntity beam, bool direct = true, ushort damage = 1)
    {
        PlayerState state = default; NetDamage.Write(_victim.SlotIndex, ref state);
        return new HitClaimPacket
        {
        MatchId = beam.ModLaunchKey.MatchId, AuthorityEpoch = beam.ModLaunchKey.AuthorityEpoch,
        ShooterGeneration = beam.ModLaunchKey.Generation, ShooterLifeId = beam.ModLaunchKey.LifeId,
        VictimSlot = (byte)_victim.SlotIndex, VictimGeneration = NetPlayerLifecycle.Generation(_victim.SlotIndex), VictimLifeId = NetPlayerLifecycle.Get(_victim.SlotIndex),
        ClaimId = ++_claimId, ShotId = beam.ModShotId, ComponentId = beam.ModComponentId,
        Frame = _frame, AckFrame = _frame, LaunchFrame = beam.ModLaunchFrame,
        Beam = (byte)beam.Beam, Flags = direct ? HitClaimPacket.FlagDirect : (byte)0,
        Damage = damage, HitPoint = _victim.Position,
        Direction = _physicalShot == beam.ModLaunchKey && _physicalComponent == beam.ModComponentId ? state.HitDirection : Vector3.Zero
        };
    }
    private static byte Receive(in HitClaimPacket claim)
    {
        byte result = 255; _lastAck = default; ushort claimId = claim.ClaimId; var saved = NetHitClaims.CombatAckSink;
        NetHitClaims.CombatAckSink = (slot, entries) =>
        { foreach (var entry in entries) if (slot == 0 && entry.ClaimId == claimId) { result = entry.Result; _lastAck = entry; } };
        try
        {
            byte[] packet = new byte[1 + HitClaimPacket.Size]; packet[0] = 1; claim.Write(packet.AsSpan(1));
            NetHitClaims.Receive(0, packet);
            for (int i = 0; i <= 2 * NetHitClaims.MaxGraceFrames + 2 && result == 255; i++)
            { NetHitClaims.Tick(); if (result == 255) Frame(_frame + 1); }
            Check(result != 255, "claim receives terminal acknowledgement"); return result;
        }
        finally { NetHitClaims.CombatAckSink = saved; }
    }
    private static byte[] Bytes(in CombatAckEntry ack)
    { byte[] bytes = new byte[CombatAckEntry.Size]; ack.Write(bytes); return bytes; }
    private static HitClaimPacket SelfClaim(BeamProjectileEntity beam)
    {
        var claim = Claim(beam, direct: false); claim.VictimSlot = 0;
        claim.VictimGeneration = NetPlayerLifecycle.Generation(0); claim.VictimLifeId = NetPlayerLifecycle.Get(0);
        claim.HitPoint = _shooter.Position; return claim;
    }
    private static void SupportedPolicy()
    {
        foreach (BeamType weapon in Enum.GetValues<BeamType>())
            Check(NetSelfSplash.Supports(weapon) == (weapon is BeamType.Missile or BeamType.Magmaul
                or BeamType.Judicator or BeamType.OmegaCannon), $"constrained self policy weapon {weapon}");
    }
    private static void ForgedClaims()
    {
        foreach (string variant in new[] { "no-witness", "missing-shot", "wrong-component", "remote-offset", "direct", "NoBeam", "burn", "turret" })
        {
            Prepare(); var beam = Emit(BeamType.Missile, ray: -Vector3.UnitY); var claim = SelfClaim(beam);
            if (variant != "no-witness")
            {
                Explode(beam);
                Check(NetSelfSplash.Validate(0, claim, out var expected), "negative fixture starts from admitted source and actual terminal witness");
                var exaggerated = claim; exaggerated.Damage = ushort.MaxValue; exaggerated.Direction = Vector3.One * 100;
                Check(NetSelfSplash.Validate(0, exaggerated, out var derived) && derived == expected,
                    "self outcome derives damage and impulse from authority witness rather than claimant values");
            }
            if (variant == "missing-shot") claim.ShotId += 10000;
            if (variant == "wrong-component") claim.ComponentId += 10000;
            if (variant == "remote-offset") claim.HitPoint += Vector3.UnitX * 20;
            if (variant == "direct") claim.Flags |= HitClaimPacket.FlagDirect;
            if (variant == "NoBeam") claim.Beam = HitClaimPacket.NoBeam;
            if (variant == "burn") claim.Flags |= HitClaimPacket.FlagBurning;
            if (variant == "turret") claim.Flags |= HitClaimPacket.FlagHalfturret;
            int health = _shooter.Health; var speed = _shooter.Speed;
            Check(!NetSelfSplash.Validate(0, claim, out _), $"self claim rejects forged {variant}");
            Check(_shooter.Health == health && _shooter.Speed == speed, "self validation cannot author health or impulse");
        }
    }
    private static void Explode(BeamProjectileEntity beam, bool protectedOwner = false)
    {
        int before = _shooter.Health;
        for (int i = 0; i < 90 && beam.Lifespan > 0; i++) beam.Process();
        Check(beam.Lifespan <= 0, $"actual {beam.Beam} floor explosion reaches terminal collision");
        Check(protectedOwner ? _shooter.Health == before : _shooter.Health < before,
            $"actual {beam.Beam} owner splash respects protection={protectedOwner}");
    }
    private static void ExplodeAtRealFloor(BeamProjectileEntity beam)
    {
        CollisionResult floor = default;
        Check(CollisionDetection.CheckBetweenPoints(beam.Position, beam.Position - Vector3.UnitY * 4,
            TestFlags.Beams, _shooter.OwningScene, ref floor),
            "charged explosive fixture resolves actual room floor collision");
        Check(beam.SplashDamage > 0 && beam.SplashRadius > 0,
            $"{beam.Beam} asset row supplies native splash damage={beam.SplashDamage} radius={beam.SplashRadius} flags={beam.Flags}");
        int before = _shooter.Health;
        // Drive the native terminal collision with the real geometry result; no damage is fabricated.
        beam.BackPosition = beam.Position; beam.Position = floor.Position + Vector3.UnitY * .05f;
        beam.OnCollision(floor, colWith: null);
        Check(_shooter.Health < before, $"actual charged {beam.Beam} floor splash damages owner");
    }
    private static void ActualExplosions()
    {
        foreach (var variant in new[] { (BeamType.Missile, false), (BeamType.Missile, true),
            (BeamType.Magmaul, false), (BeamType.Magmaul, true),
            (BeamType.Judicator, false), (BeamType.Judicator, true), (BeamType.OmegaCannon, false) })
        {
            foreach (bool claimFirst in new[] { false, true })
            {
                Prepare(); byte charge = variant.Item2
                    ? (byte)(_shooter.OwningScene.WeaponRules[NativeWeaponIndex(variant.Item1)].FullCharge * 2) : (byte)0;
                var beam = Emit(variant.Item1, charge: charge, ray: -Vector3.UnitY);
                bool directOnly = !variant.Item2 && variant.Item1 == BeamType.Magmaul;
                if (variant.Item1 == BeamType.Judicator)
                {
                    CollisionResult floor = default;
                    Check(CollisionDetection.CheckBetweenPoints(beam.Position, beam.Position - Vector3.UnitY * 4,
                        TestFlags.Beams, _shooter.OwningScene, ref floor), "tiny Judicator splash fixture resolves real floor");
                    // Native Judicator splash radius is only 0.25 units; put the actual owner body inside it.
                    _shooter.ModPlaceAt(floor.Position + Vector3.UnitY * .1f);
                    Record();
                }
                var claim = SelfClaim(beam); if (directOnly) claim.Flags |= HitClaimPacket.FlagDirect;
                Check(!directOnly || !NetSelfSplash.Validate(0, claim, out _),
                    "returning native self-direct remains acknowledgement-only before authority payment");
                int before = _shooter.Health;
                if (claimFirst)
                {
                    byte[] packet = new byte[1 + HitClaimPacket.Size]; packet[0] = 1; claim.Write(packet.AsSpan(1));
                    NetHitClaims.Receive(0, packet);
                    Check(NetHitClaims.ClaimsPendingCurrent == 1 && _shooter.Health == before,
                        "self claim waits for native authority explosion without speculative authority damage");
                }
                if (variant.Item1 == BeamType.Judicator || variant.Item2 && variant.Item1 == BeamType.Magmaul)
                    ExplodeAtRealFloor(beam);
                else Explode(beam);
                int health = _shooter.Health; var speed = _shooter.Speed;
                ushort sequence = NetDamage.Sequence(0);
                bool splashPaid = NetCombatPayments.TryGet(NetShotProof.PaymentKey(0, claim), claim.Beam, out var paid);
                var directClaim = claim; directClaim.Flags |= HitClaimPacket.FlagDirect;
                bool directPaid = NetCombatPayments.TryGet(NetShotProof.PaymentKey(0, directClaim), claim.Beam, out var directOutcome);
                Check(splashPaid,
                    $"actual {variant} explosion records exact owner component expectedKind={(directOnly ? "direct" : "splash")} payment splashPaid={splashPaid} directPaid={directPaid} directDamage={directOutcome.DamageApplied} totalNativeDamage={before-health} splash={beam.SplashDamage} radius={beam.SplashRadius}");
                byte result = Receive(claim);
                Check(result == HitVerdictPacket.ResultDuplicate && _lastAck.VictimSlot == 0
                    && _lastAck.ShotId == paid.ShotId && _lastAck.HealthAfter == paid.HealthAfter
                    && _lastAck.DamageApplied == paid.DamageApplied && _lastAck.DamageSequence == paid.DamageSequence
                    && _shooter.Health == health && _shooter.Speed == speed,
                    $"native exact self payment returns authoritative outcome {variant} claimFirst={claimFirst} result={result} ackhealth={_lastAck.HealthAfter}/{paid.HealthAfter} ackdamage={_lastAck.DamageApplied}/{paid.DamageApplied} totalNativeDamage={before-health} seq={_lastAck.DamageSequence}/{paid.DamageSequence}");
                byte[] receipt = Bytes(_lastAck);
                Check(Receive(claim) == HitVerdictPacket.ResultDuplicate && Bytes(_lastAck).SequenceEqual(receipt)
                    && _shooter.Health == health && _shooter.Speed == speed && NetDamage.Sequence(0) == sequence,
                    "self terminal retry preserves health, knockback and immutable receipt");
            }
        }
    }
    private static void JudicatorAffinityPolicy()
    {
        Prepare(); _shooter.Hunter = Hunter.Noxus;
        var weapon = _shooter.OwningScene.WeaponRules[NativeWeaponIndex(BeamType.Judicator)];
        Check(weapon.SplashDamage > 0 && weapon.ChargedSplashDamage == 0
            && weapon.Flags.TestFlag(WeaponFlags.AoeCharged),
            "actual Noxus affinity row has uncharged splash and charged ice wave with no splash damage");
        var beam = Emit(BeamType.Judicator, charge: (byte)(weapon.FullCharge * 2), ray: -Vector3.UnitY);
        var claim = SelfClaim(beam); int health = _shooter.Health;
        Check(!NetSelfSplash.Validate(0, claim, out _) && _shooter.Health == health,
            "affinity charged Judicator cannot invent self splash rescue from zero-damage ice-wave profile");
        _shooter.Hunter = Hunter.Samus;
    }
    private static void ProtectedAndReplacement()
    {
        Prepare(); var beam = Emit(BeamType.Missile, ray: -Vector3.UnitY); var claim = SelfClaim(beam);
        typeof(PlayerEntity).GetField("_spawnInvulnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_shooter, (ushort)1000);
        Explode(beam, protectedOwner: true);
        Check(!NetSelfSplash.Validate(0, claim, out _), "native terminal witness cannot override self spawn protection");
        typeof(PlayerEntity).GetField("_spawnInvulnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_shooter, (ushort)0);
        Check(!NetSelfSplash.Validate(0, claim, out _), "waiting out protection cannot convert immune witnessed explosion into self rescue");
        Prepare(); beam = Emit(BeamType.Missile, ray: -Vector3.UnitY); claim = SelfClaim(beam);
        _shooter.Spawn(_shooter.Position, Vector3.UnitZ, Vector3.UnitY, _shooter.NodeRef, respawn: true);
        _shooter.ModReleaseSpawnProtection(); int health = _shooter.Health;
        Check(!NetSelfSplash.Validate(0, claim, out _) && _shooter.Health == health,
            "old self shot cannot rescue damage onto replacement owner life");
    }
    private static void MovingJumpAndSelfKill()
    {
        Prepare(); var beam = Emit(BeamType.Missile, ray: -Vector3.UnitY);
        _shooter.ModPlaceAt(_shooter.Position + _direction * .15f);
        _shooter.Speed = _direction * .05f;
        var claim = SelfClaim(beam); Explode(beam);
        int health = _shooter.Health; var speed = _shooter.Speed;
        Check(speed.Y > 0, "moving downward Missile explosion supplies upward rocket-jump impulse");
        Check(Receive(claim) == HitVerdictPacket.ResultDuplicate && _shooter.Health == health
            && _shooter.Speed == speed, "moving self splash reconciles without health or impulse replay");
        Prepare(); beam = Emit(BeamType.Missile, ray: -Vector3.UnitY); claim = SelfClaim(beam);
        _shooter.Health = 1; Explode(beam);
        Check(_shooter.Health == 0, "near-floor low-health owner is killed by actual native self splash");
        Check(Receive(claim) == HitVerdictPacket.ResultDuplicate && _lastAck.HealthAfter == 0,
            "self-kill exact receipt remains available after owner death");
    }
    private static void ChainedAndTeamSplash()
    {
        Prepare(); var first = Emit(BeamType.Missile, ray: -Vector3.UnitY);
        var second = Emit(BeamType.Missile, ray: -Vector3.UnitY, preserve: true);
        Check(first.ModLaunchKey != second.ModLaunchKey, "two explosive emissions retain distinct shot identities");
        Explode(first); int health = _shooter.Health; Explode(second);
        Check(_shooter.Health < health, "independent chained explosions each retain legal self payment");
        Prepare(); var state = _shooter.OwningScene.GameState;
        bool teams = state.Teams; int victimTeam = _victim.TeamIndex;
        try
        {
            state.Teams = true; state.TeamCount = 2; _victim.TeamIndex = _shooter.TeamIndex;
            var beam = Emit(BeamType.Missile, ray: -Vector3.UnitY); var claim = SelfClaim(beam);
            Explode(beam); health = _shooter.Health;
            Check(Receive(claim) == HitVerdictPacket.ResultDuplicate && _shooter.Health == health,
                "team-mode self splash preserves native owner payment without extra claim damage");
        }
        finally { state.Teams = teams; _victim.TeamIndex = victimTeam; }
    }
    private static void SplitNativeHookRescue()
    {
        Prepare(); var beam = Emit(BeamType.Missile, ray: -Vector3.UnitY);
        CollisionResult floor = default;
        Check(CollisionDetection.CheckBetweenPoints(beam.Position, beam.Position - Vector3.UnitY * 4,
            TestFlags.Beams, _shooter.OwningScene, ref floor), "split-hook fixture captures real floor collision");
        beam.BackPosition = beam.Position; beam.Position = floor.Position + Vector3.UnitY * .05f;
        Vector3 origin = BeamProjectileEntity.ModSplashOrigin(_shooter.OwningScene, beam.Beam, beam.Position, floor.Position);
        // Deliberately isolate the native pre-owner-loop witness hook. This is not a claimed UDP miss.
        NetSelfSplash.Observe(beam, origin);
        var claim = SelfClaim(beam); claim.Damage = 1;
        Check(NetSelfSplash.Validate(0, claim, out var expected) && expected.Damage > claim.Damage,
            "split native pre-payment hook supplies strict source and server-derived splash outcome");
        var perturbed = claim; perturbed.HitPoint += Vector3.UnitY * .01f;
        Check(NetSelfSplash.Validate(0, perturbed, out var same) && same == expected,
            "inside-envelope client point cannot change canonical server-body damage or impulse");
        int before = _shooter.Health; var speed = _shooter.Speed;
        Check(Receive(claim) == HitVerdictPacket.ResultApplied && _lastAck.DamageApplied == expected.Damage
            && _shooter.Health == before - expected.Damage,
            "split-hook unpaid self rescue applies server-derived damage rather than claimed one");
        Check(Vector3.Distance(_shooter.Speed, speed + expected.Impulse) < .000001f,
            "split-hook unpaid self rescue applies server-derived impulse");
        int health = _shooter.Health; speed = _shooter.Speed; ushort sequence = NetDamage.Sequence(0);
        beam.OnCollision(floor, colWith: null);
        Check(_shooter.Health == health && _shooter.Speed == speed && NetDamage.Sequence(0) == sequence,
            "later actual native floor splash cannot repay split-hook rescued component");
    }
    private static void BurnOwnership()
    {
        Prepare(); var beam = Emit(BeamType.Magmaul);
        var source = NetBurnSource.From(beam, 100, balanced: false);
        _victim.ModStartBurn(source);
        Check(_victim.ModBurnSource is { } captured && captured.Key == beam.ModLaunchKey
            && captured.Component == beam.ModComponentId && captured.LaunchFrame == beam.ModLaunchFrame,
            "burn captures original projectile shot, component and launch ownership");
        var timer = typeof(PlayerEntity).GetField("_burnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        timer.SetValue(_victim, (ushort)40); _victim.ModStartBurn(source);
        Check((ushort)timer.GetValue(_victim)! == 40,
            "same shot and component cannot refresh active burn duration");
        typeof(PlayerEntity).GetField("_doubleDmgTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_shooter, (ushort)1000);
        beam.Lifespan = 0; int health = _victim.Health;
        source.Apply(_victim);
        Check(_victim.Health == health - source.Damage && _victim.ModBurnSource == source,
            "burn tick retains captured damage after projectile removal and later owner double damage");
        ushort life = source.Key.LifeId;
        _shooter.Spawn(_shooter.Position, Vector3.UnitZ, Vector3.UnitY, _shooter.NodeRef, respawn: true);
        Check(_victim.ModBurnSource!.Value.Key.LifeId == life
            && NetPlayerLifecycle.Get(0) != life,
            "owner respawn cannot rewrite active burn launch life");
        Frame(_frame + 16); health = _victim.Health; source.Apply(_victim);
        PlayerState state = default; state.SlotIndex = 1;
        state.SlotGeneration = NetPlayerLifecycle.Generation(1); state.LifeId = NetPlayerLifecycle.Get(1);
        NetDamage.Write(1, ref state);
        Check(_victim.Health == health - source.Damage
            && NetDamage.ReplayAttackerLife(state, source.Key.Generation) == life,
            "burn after owner respawn retains original launch life in replay attribution");
        NetPlayerLifecycle.SetOccupant(0, (ushort)(source.Key.Generation + 1));
        Check(source.ResolveOwner(_victim.OwningScene) == null,
            "disconnected original owner cannot resolve to replacement occupant for burn credit");
        Frame(_frame + 16); _victim.Health = 1;
        int points = GameState.Points[0], kills = GameState.Kills[0]; source.Apply(_victim);
        var lastHit = (uint[])typeof(NetHitClaims).GetField("_lastHitFire", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Check(lastHit[1] == _frame && lastHit[1] != source.LaunchFrame,
            "lethal burn uses current authority damage time rather than backdating death to original shot");
        Check(_victim.Health == 0 && GameState.Points[0] == points && GameState.Kills[0] == kills,
            "burn survives owner replacement but cannot credit replacement occupant for lethal tick");
    }
    public static int Run(string profile, string room)
    {
        bool accepted = NetFireEvents.AcceptedPlaybackEnabled; var mode = NetShotProof.Mode; bool selfEnabled = NetSelfSplash.Enabled; NetSelfSplash.Enabled = true;
        var sim = new ServerSim();
        if (!sim.Start(room, GameMode.Battle, 2, _ => { }, () => { })) return 1;
        try
        {
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1, RoomKey = room,
                Mode = (byte)GameMode.Battle }, false);
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
            for (byte i = 0; i < 2; i++) { roster.Slots[i] = i; roster.Generations[i] = 1; }
            NetSession.ApplyRoster(roster);
            for (int i = 0; i < 120; i++) sim.Step();
            _shooter = PlayerEntity.Players[0]; _victim = PlayerEntity.Players[1]; _base = _shooter.Position;
            _shooter.OwningScene.GameState.EnhancedHunters = false;
            _shooter.OwningScene.GameState.BalancedMode = false;
            NetFireEvents.AcceptedPlaybackEnabled = true; NetShotProof.Mode = ShotProofMode.Shadow; _frame = NetSession.NetFrame;
            foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
                if (NetDynamicGeometryHistory.TryTraceDistanceAtFrame(_shooter.OwningScene,
                    _base + Vector3.UnitY * .5f, _base + direction * 4 + Vector3.UnitY * .5f, _frame, out float wall) && wall == 1)
                { _direction = direction; break; }
            Check(_direction.LengthSquared == 1, "asset fixture provides clear production collision lane");

            SupportedPolicy(); ForgedClaims(); ActualExplosions(); JudicatorAffinityPolicy(); ProtectedAndReplacement(); MovingJumpAndSelfKill(); ChainedAndTeamSplash(); SplitNativeHookRescue(); BurnOwnership();
            long lifetimePayments = NetCombatPayments.LifetimePayments;
            long lifetimeSuppressed = NetCombatPayments.LifetimeSuppressed;
            Check(lifetimePayments > 0, "process evidence captures actual payments and suppressed repeats");
            NetHitClaims.Reset();
            Check(NetCombatPayments.LifetimePayments == lifetimePayments && NetCombatPayments.LifetimeSuppressed == lifetimeSuppressed
                && NetCombatPayments.Payments == 0 && NetCombatPayments.Suppressed == 0,
                "room teardown clears live ledger without erasing process payment evidence");
            Check(sim.StepFailures == 0, "no simulation failures");
            Console.WriteLine($"PASS: {_checks} self splash asset assertions ({profile})"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { sim.Stop(); NetSession.Stop(); NetFireEvents.AcceptedPlaybackEnabled = accepted; NetShotProof.Mode = mode; NetSelfSplash.Enabled = selfEnabled; }
    }
}
