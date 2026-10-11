using System;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ContinuousCombatTests
{
    private static PlayerEntity _owner = null!, _victim = null!, _other = null!;
    private static Vector3 _base, _direction;
    private static uint _frame, _phase, _shot;
    private static int _checks;
    private static void Check(bool ok, string name)
    { _checks++; if (!ok) throw new InvalidOperationException(name); Console.WriteLine($"CONTINUOUS COMBAT PASS {name}"); }
    private static void Frame(uint frame)
    { _frame = frame; typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, frame); }
    private static void Prepare()
    {
        foreach (var player in new[] { _owner, _victim, _other })
        {
            foreach (var beam in player.EquipInfo.Beams) beam.Lifespan = 0;
            if (player.Health == 0) player.Spawn(player.Position, Vector3.UnitZ, Vector3.UnitY, player.NodeRef, respawn: true);
            player.ModForceForm(false);
            typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!.SetValue(player, player.Flags2 & ~PlayerFlags2.Halfturret);
            player.Health = 99; player.TeamIndex = player.SlotIndex;
            player.Controls.ClearAll(); player.ModReleaseSpawnProtection(); player.Speed = Vector3.Zero;
            foreach (string field in new[] { "_spawnInvulnTimer", "_damageInvulnTimer", "_doubleDmgTimer", "_shockCoilTimer", "_autofireCooldown" })
                typeof(PlayerEntity).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(player, (ushort)0);
        }
        var state = _owner.OwningScene.GameState; state.PrimeHunter = -1; state.BalancedMode = false;
        state.Teams = false; state.EnhancedHunters = false; _owner.Hunter = Hunter.Samus; _victim.Hunter = Hunter.Samus;
        _owner.Health = 50; _owner.ModPlaceAt(_base); _victim.ModPlaceAt(_base + _direction * 3);
        _other.ModPlaceAt(_base - _direction * 3); Frame(_frame + 200);
        NetContinuousPayments.Reset(); NetHitClaims.Reset(); NetFireEvents.Reset();
        NetHitClaims.Tick(); NetUnlagged.Record(_frame); NetDynamicGeometryHistory.RecordWorld(_frame);
    }
    private static BeamProjectileEntity Beam(bool fixtureTarget = true, NetTargetIdentity? selected = null)
    {
        _owner.ModArmWeapon(BeamType.ShockCoil); _owner.EquipInfo.InfiniteAmmo = true;
        uint authoredPhase = ++_phase;
        NetSession.RemoteIntents[0] = new IntentPacket { MatchId = NetSession.CurrentMatchId,
            AuthorityEpoch = NetSession.AuthorityEpoch, SlotGeneration = NetPlayerLifecycle.Generation(0),
            LifeId = NetPlayerLifecycle.Get(0), Frame = _frame, AckFrame = _frame,
            Position = _owner.Position, Aim = _direction, WeaponSelect = (byte)BeamType.ShockCoil,
            Buttons = IntentButtons.InPlayState | IntentButtons.Shoot, HasState = true,
            Target = selected ?? NetTargetIdentity.ForSlot(1), HasContinuousFireTick = true, ContinuousFireTick = authoredPhase };
        NetSession.RemoteIntentValid[0] = true; NetSession.RemoteIntentArrived[0] = _frame;
        foreach (var beam in _owner.EquipInfo.Beams) beam.Lifespan = 0;
        var result = BeamProjectileEntity.Spawn(_owner, _owner.EquipInfo, _owner.Position + Vector3.UnitY * .5f,
            _direction, BeamSpawnFlags.NoMuzzle, _owner.NodeRef, _owner.OwningScene);
        var emitted = _owner.EquipInfo.Beams.FirstOrDefault(b => b.Beam == BeamType.ShockCoil && b.Lifespan > 0);
        Check(result != BeamResultFlags.NoSpawn && emitted != null, "actual asset Shock Coil spawn supplies native beam");
        emitted!.ModShotId = ++_shot; emitted.ModLaunchFrame = _frame;
        emitted.ModLaunchMatch = NetSession.CurrentMatchId; emitted.ModLaunchAuthority = NetSession.AuthorityEpoch;
        emitted.ModLaunchGeneration = NetPlayerLifecycle.Generation(0); emitted.ModLaunchLife = NetPlayerLifecycle.Get(0);
        emitted.ModLaunchKey = ShotKey.For(0, emitted.ModShotId);
        emitted.ModContinuousPhase = authoredPhase; emitted.ModHasSharedContinuousPhase = true;
        // Legacy payment fixtures author a known victim directly; target-guard tests use untouched Spawn metadata.
        if (fixtureTarget) { emitted.Target = _victim; emitted.ModContinuousTarget = NetTargetIdentity.ForSlot(1); }
        emitted.Flags |= BeamFlags.LifeDrain;
        return emitted;
    }
    private static ContinuousPaymentKey Key(BeamProjectileEntity beam, PlayerEntity victim, bool turret = false)
        => new(beam.ModLaunchKey with { ShotId = (uint)beam.ModContinuousPhase }, NetTargetIdentity.ForSlot(victim.SlotIndex),
            turret ? ContinuousPaymentKey.Turret : ContinuousPaymentKey.Body);
    private static void Native(BeamProjectileEntity beam, uint amount = 12, PlayerEntity? victim = null,
        DamageFlags flags = DamageFlags.NoDmgInvuln)
    {
        beam.EnhancedDirectHit = true;
        try { (victim ?? _victim).TakeDamage(amount, flags, Vector3.UnitY * .025f, beam); }
        finally { beam.EnhancedDirectHit = false; }
    }
    private static void NativePaymentOnce()
    {
        Prepare(); var beam = Beam(); long paid = NetContinuousPayments.LifetimePayments;
        Native(beam); Check(_victim.Health == 87 && _owner.Health == 62,
            "one native Shock Coil tick pays actual victim damage and owner heal");
        Check(NetContinuousPayments.TryGet(Key(beam, _victim), out var outcome, out int heal, out int turretHeal)
            && outcome.DamageApplied == 12 && outcome.HealthAfter == 87 && heal == 12 && turretHeal == 0
            && NetContinuousPayments.LifetimePayments == paid + 1, "exact tick captures immutable actual payment and heal");
        ushort sequence = NetDamage.Sequence(1); var speed = _victim.Speed;
        Native(beam); Native(beam);
        Check(_victim.Health == 87 && _owner.Health == 62 && NetDamage.Sequence(1) == sequence && _victim.Speed == speed,
            "repeated native owner phase cannot damage, heal or impulse twice");
        beam.ModShotId += 1000; beam.ModLaunchKey = beam.ModLaunchKey with { ShotId = beam.ModShotId };
        Native(beam, victim: _other);
        Check(_other.Health == 99 && _owner.Health == 62
            && !NetContinuousPayments.TryGet(Key(beam, _other), out _, out _, out _),
            "same owner phase with new shot ID and redirected target cannot redirect a paid tick");
        _victim.Spawn(_victim.Position, Vector3.UnitZ, Vector3.UnitY, _victim.NodeRef, respawn: true);
        NetContinuousPayments.ForgetSlot(1, preserveFlights: false);
        Native(beam, victim: _other);
        Check(_other.Health == 99 && _owner.Health == 62,
            "victim replacement cannot free another owner's paid phase for redirection");
        Check(NetContinuousPayments.TryCapturePhase(0, out uint newest, out ulong low, out _)
            && newest == beam.ModContinuousPhase && (low & 1) != 0, "paid tick bitmap includes committed native phase");
    }
    private static void SelectedTargetFence()
    {
        Prepare(); var beam = Beam(fixtureTarget: false);
        var selected = NetTargetIdentity.ForSlot(1);
        Check(beam.Target == _victim && beam.ModContinuousTarget == selected,
            "real continuous Spawn captures selected full victim identity");
        long payments = NetContinuousPayments.LifetimePayments;
        Native(beam, 1, _other);
        Check(_other.Health == 99 && _owner.Health == 50
            && NetContinuousPayments.LifetimePayments == payments,
            "unpaid valid pulse cannot damage incidental player ahead of authored victim");
        Native(beam, 1, flags: DamageFlags.NoDmgInvuln | DamageFlags.Halfturret);
        Check(_victim.Health == 99 && _owner.Health == 50
            && NetContinuousPayments.LifetimePayments == payments,
            "selected body cannot pay mismatched turret component");
        Native(beam, 1);
        Check(_victim.Health == 98 && _owner.Health == 51
            && NetContinuousPayments.LifetimePayments == payments + 1,
            "refused incidental collision leaves exact selected victim eligible once");

        Prepare(); beam = Beam(fixtureTarget: false, selected: NetTargetIdentity.None);
        Check(beam.Target == null && beam.ModContinuousTarget == NetTargetIdentity.None,
            "real explicit-none continuous Spawn retains no target");
        payments = NetContinuousPayments.LifetimePayments;
        Native(beam, 1); Native(beam, 1, _other);
        Check(_victim.Health == 99 && _other.Health == 99 && _owner.Health == 50
            && NetContinuousPayments.LifetimePayments == payments,
            "explicit-none pulse cannot damage or heal through incidental players");

        Prepare(); beam = Beam(fixtureTarget: false); selected = beam.ModContinuousTarget;
        Check(beam.Target == _victim && selected == NetTargetIdentity.ForSlot(1),
            "replacement-life fixture starts with actual selected target");
        _victim.Spawn(_victim.Position, Vector3.UnitZ, Vector3.UnitY, _victim.NodeRef, respawn: true);
        _victim.ModReleaseSpawnProtection(); int health = _victim.Health;
        payments = NetContinuousPayments.LifetimePayments; Native(beam, 1);
        Check(selected != NetTargetIdentity.ForSlot(1) && beam.Target == _victim
            && _victim.Health == health && _owner.Health == 50
            && NetContinuousPayments.LifetimePayments == payments,
            "same entity reference with replacement victim life cannot inherit selected pulse");
    }
    private static void DeclinedAndCapped()
    {
        Prepare(); var immune = Beam();
        typeof(PlayerEntity).GetField("_spawnInvulnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_victim, (ushort)1000);
        Native(immune); Check(_victim.Health == 99 && _owner.Health == 50
            && !NetContinuousPayments.TryGet(Key(immune, _victim), out _, out _, out _), "spawn-immune declined tick cannot phantom heal or become paid");
        Prepare(); var capped = Beam(); _victim.Health = 3; _owner.Health = _owner.HealthMax - 1;
        Native(capped); Check(_victim.Health == 0 && _owner.Health == _owner.HealthMax
            && NetContinuousPayments.TryGet(Key(capped, _victim), out var lethal, out int heal, out _)
            && lethal.DamageApplied == 3 && heal == 1, "lethal low-health tick pays removed health and capped actual heal");
        Prepare(); var prime = Beam(); _owner.OwningScene.GameState.PrimeHunter = 0;
        Native(prime); Check(_owner.Health == 50, "Prime owner cannot drain heal");
        Prepare(); var allied = Beam(); _owner.OwningScene.GameState.Teams = true; _owner.OwningScene.GameState.TeamCount = 2; _victim.TeamIndex = _owner.TeamIndex;
        Native(allied); Check(_owner.Health == 50, "allied target cannot create drain heal");
        Prepare(); var posthumous = Beam(); _owner.Health = 0;
        Native(posthumous); Check(_owner.Health == 0, "dead owner cannot be resurrected by drain");
    }
    private static void SplitAndBalanced()
    {
        Prepare(); _owner.Hunter = Hunter.Weavel; _owner.CreateHalfturret();
        _owner.Health = 40; _owner.Halfturret.Health = 40; _owner.ModRestoreHalfturretFlag();
        var beam = Beam(); Native(beam, 10);
        Check(_owner.Health == 45 && _owner.Halfturret.Health == 45
            && NetContinuousPayments.TryGet(Key(beam, _victim), out _, out int body, out int turret)
            && body == 5 && turret == 5, "native drain records actual Weavel body and turret heal split");
        Prepare(); _owner.Hunter = Hunter.Sylux; _owner.OwningScene.GameState.BalancedMode = true;
        var affinity = Beam(); Native(affinity, 10);
        int expected = BalancedModeRules.LifeDrainHealForActualDamage(10);
        Check(_owner.Health == 50 + expected
            && NetContinuousPayments.TryGet(Key(affinity, _victim), out _, out int heal, out _) && heal == expected,
            "Balanced Sylux applies actual-damage drain policy once");
    }
    private static void ActualCollision()
    {
        Prepare(); _owner.Hunter = Hunter.Sylux; var beam = Beam();
        // Reuse the actual metadata cadence at a phase that yields one point.
        int raw = _owner.OwningScene.WeaponRules[(int)BeamType.ShockCoil + 9].UnchargedDamage;
        uint phase = 64;
        while (ContinuousWeaponPhase.Amount(raw, phase, true) == 0) phase += 2;
        beam.ModContinuousPhase = phase;
        beam.Damage = ContinuousWeaponPhase.Amount(raw, phase, true);
        int victimBefore = _victim.Health, ownerBefore = _owner.Health;
        for (int i = 0; i < 60 && _victim.Health == victimBefore && beam.Lifespan > 0; i++) beam.Process();
        int removed = victimBefore - _victim.Health;
        Check(removed > 0 && _owner.Health - ownerBefore == removed,
            $"actual asset Shock Coil collision heals once for accepted damage removed={removed} heal={_owner.Health-ownerBefore} damage={beam.Damage} target={beam.Target?.Type} lifespan={beam.Lifespan} phase={beam.ModContinuousPhase}");
        Native(beam, (uint)removed);
        Check(_victim.Health == victimBefore - removed && _owner.Health == ownerBefore + removed,
            "native beam after real collision cannot pay or heal again");
    }
    private static void ReservationAndLifetime()
    {
        Prepare(); var beam = Beam(); var key = Key(beam, _victim);
        using (var reserve = NetContinuousPayments.Begin(key))
        {
            Check(reserve.Allowed, "exact tick reserves before effects");
            using var duplicate = NetContinuousPayments.Begin(key);
            Check(!duplicate.Allowed, "reentrant reservation pins owner phase before commit");
        }
        using (var again = NetContinuousPayments.Begin(key)) Check(again.Allowed, "declined uncommitted reservation releases without paid tombstone");
        long payments = NetContinuousPayments.LifetimePayments, turretDamage = NetContinuousPayments.LifetimeTurretDamage;
        using (var committed = NetContinuousPayments.Begin(key))
            committed.Commit(new CombatAckEntry { DamageApplied = 2, VictimSlot = 1,
                VictimGeneration = key.Victim.Generation, VictimLife = key.Victim.LifeId }, 0, 0, actualTurretDamage: 3);
        Check(NetContinuousPayments.LifetimePayments == payments + 1 && NetContinuousPayments.LifetimeTurretDamage == turretDamage + 3,
            "body/turret damage commit with zero heal still records paid tick and lifetime turret damage");
        long lifetime = NetContinuousPayments.LifetimePayments; NetContinuousPayments.Reset();
        Check(NetContinuousPayments.LifetimePayments == lifetime && !NetContinuousPayments.TryGet(key, out _, out _, out _),
            "teardown reset clears live payments and preserves process lifetime evidence");
    }
    private static ushort _claimId;
    private static CombatAckEntry _ack;
    private static HitClaimPacket JournalClaim(BeamProjectileEntity beam, bool publish = true, bool odd = false, bool blocked = false)
    {
        _phase = (_frame - 2) & ~1u;
        var weapon = _owner.OwningScene.WeaponRules[(int)BeamType.ShockCoil + (_owner.Hunter == Hunter.Sylux ? 9 : 0)];
        int raw = Math.Max(weapon.UnchargedDamage, Math.Max(weapon.MinChargeDamage, weapon.ChargedDamage));
        while (ContinuousWeaponPhase.Amount(raw * 2, _phase, true) == 0) _phase -= 2;
        if (odd) _phase++;
        beam.ModContinuousPhase = _phase;
        var target = NetTargetIdentity.ForSlot(1);
        Vector3 origin = _owner.Position + Vector3.UnitY * .5f, direction = _direction;
        if (blocked)
        {
            direction = -Vector3.UnitY;
            Check(NetDynamicGeometryHistory.TryTraceDistanceAtFrame(_owner.OwningScene, origin,
                origin + direction * 4, _frame, out float wall) && wall < .8f,
                "asset continuous LOS fixture crosses real floor geometry");
            _victim.ModPlaceAt(origin + direction * (4 * wall + .5f) - Vector3.UnitY * .5f);
            NetUnlagged.Record(_frame);
        }
        var fire = new FireEvent(beam.ModShotId, _phase, _frame, 0, FireEventKind.ContinuousTick,
            (byte)BeamType.ShockCoil, 0, _phase, FireEvent.FlagPose,
            origin, direction, direction, direction,
            SourceLife: NetPlayerLifecycle.Get(0), Target: target);
        var packet = NetSession.RemoteIntents[0]; packet.Frame = _phase; packet.AckFrame = _frame;
        packet.HasFireEvents = true; packet.FireEventCount = 1; packet.FireEvents[0] = fire;
        packet.ContinuousFireTick = _phase;
        if (publish) Check(NetFireEvents.Accept(0, packet), "continuous source carrier accepted into exact journal");
        return new HitClaimPacket { MatchId = NetSession.CurrentMatchId, AuthorityEpoch = NetSession.AuthorityEpoch,
            ShooterGeneration = NetPlayerLifecycle.Generation(0), ShooterLifeId = NetPlayerLifecycle.Get(0),
            VictimSlot = 1, VictimGeneration = target.Generation, VictimLifeId = target.LifeId,
            ClaimId = ++_claimId, ShotId = beam.ModShotId, ComponentId = beam.ModComponentId,
            Frame = _phase, AckFrame = _frame, LaunchFrame = beam.ModLaunchFrame,
            Beam = (byte)BeamType.ShockCoil, Flags = HitClaimPacket.FlagContinuousTick | HitClaimPacket.FlagDirect,
            Damage = 1, HitPoint = _victim.Position, Direction = Vector3.Zero };
    }
    private static byte Receive(in HitClaimPacket claim)
    {
        byte result = 255; ushort id = claim.ClaimId; var saved = NetHitClaims.CombatAckSink;
        NetHitClaims.CombatAckSink = (slot, entries) =>
        { foreach (var entry in entries) if (slot == 0 && entry.ClaimId == id) { result = entry.Result; _ack = entry; } };
        try
        {
            byte[] packet = new byte[1 + HitClaimPacket.Size]; packet[0] = 1; claim.Write(packet.AsSpan(1));
            NetHitClaims.Receive(0, packet);
            for (int i = 0; i <= 2 * NetHitClaims.MaxGraceFrames + 2 && result == 255; i++)
            { NetHitClaims.Tick(); if (result == 255) Frame(_frame + 1); }
            Check(result != 255, "continuous claim receives terminal acknowledgement"); return result;
        }
        finally { NetHitClaims.CombatAckSink = saved; }
    }
    private static byte[] Receipt(in CombatAckEntry value)
    { byte[] bytes = new byte[CombatAckEntry.Size]; value.Write(bytes); return bytes; }
    private static void ClaimAndNativeOrdering()
    {
        Prepare(); _owner.Hunter = Hunter.Sylux; var beam = Beam(); var claim = JournalClaim(beam);
        Check(Receive(claim) == HitVerdictPacket.ResultApplied && _victim.Health == 98 && _owner.Health == 51,
            $"journal-backed claim first rescues one damage and one actual heal result={_ack.Result} damage={_ack.DamageApplied} victim={_victim.Health} owner={_owner.Health} admission={NetContinuousAdmission.Validate(0, claim, out _)} phase={claim.Frame} ack={claim.AckFrame} frame={_frame}");
        byte[] terminal = Receipt(_ack); ushort sequence = NetDamage.Sequence(1);
        Native(beam, 1);
        Check(_victim.Health == 98 && _owner.Health == 51 && NetDamage.Sequence(1) == sequence,
            "late native collision after rescue cannot repeat damage or drain heal");
        Check(Receive(claim) == HitVerdictPacket.ResultApplied && Receipt(_ack).SequenceEqual(terminal),
            "exact rescued continuous terminal retry is byte-identical");
        claim.ClaimId = ++_claimId;
        Check(Receive(claim) == HitVerdictPacket.ResultDuplicate && _victim.Health == 98 && _owner.Health == 51,
            "new claim ID for paid continuous phase cannot repay damage or heal");
        Prepare(); _owner.Hunter = Hunter.Sylux; beam = Beam(); claim = JournalClaim(beam); Native(beam, 1);
        Check(Receive(claim) == HitVerdictPacket.ResultDuplicate && _ack.ShotId == beam.ModShotId
            && _ack.DamageApplied == 1 && _victim.Health == 98 && _owner.Health == 51,
            "native-first exact source claim returns original paid outcome without extra heal");
        terminal = Receipt(_ack);
        Check(Receive(claim) == HitVerdictPacket.ResultDuplicate && Receipt(_ack).SequenceEqual(terminal),
            "native-first continuous acknowledgement retry is byte-identical");
    }
    private static void LocalAuthoredClock()
    {
        Prepare(); _owner.Hunter = Hunter.Sylux; _owner.ModArmWeapon(BeamType.ShockCoil);
        _owner.EquipInfo.InfiniteAmmo = true;
        var role = NetSession.Role; bool authority = NetSession.IsAuthority;
        int local = NetSession.LocalSlot; uint applied = NetSession.AppliedSnapshotFrame;
        void Set(string name, object value) => typeof(NetSession).GetProperty(name)!.SetValue(null, value);
        try
        {
            Set(nameof(NetSession.Role), NetRole.Client); Set(nameof(NetSession.IsAuthority), false);
            Set(nameof(NetSession.LocalSlot), 0); NetSmoothing.Reset();
            uint authoredAck = _frame - 8;
            Set(nameof(NetSession.AppliedSnapshotFrame), authoredAck);
            var origin = _owner.Position + Vector3.UnitY * .5f;
            NetFireEvents.Begin(_owner, origin, _direction, _direction, _direction);
            uint shot = NetFireEvents.ActiveShotId(_owner);
            Set(nameof(NetSession.AppliedSnapshotFrame), authoredAck + 3);
            NetUnlagged.BeginShot(_owner, origin, _direction);
            var result = BeamProjectileEntity.Spawn(_owner, _owner.EquipInfo, origin, _direction,
                BeamSpawnFlags.NoMuzzle, _owner.NodeRef, _owner.OwningScene);
            NetUnlagged.EndShot(_owner);
            var beam = _owner.EquipInfo.Beams.FirstOrDefault(b => b.ModShotId == shot && b.Lifespan > 0);
            Check(result != BeamResultFlags.NoSpawn && beam != null && beam.ModHasSharedContinuousPhase,
                "local Begin/Spawn/EndShot creates exact shared continuous projectile");
            Check(beam!.ModLaunchFrame == authoredAck,
                "local continuous launch retains authored ACK despite newer spawn clock");
            beam.Process(); // Actual targeting records the phase used by the outgoing fire journal.
            NetFireEvents.Commit(_owner);
            var intent = new IntentPacket { Frame = _frame }; NetFireEvents.Fill(ref intent, 0);
            Check(intent.FireEventCount == 1 && intent.FireEvents[0].ShotId == shot
                && intent.FireEvents[0].AckFrame == authoredAck
                && intent.FireEvents[0].ContinuousPhase == beam.ModContinuousPhase,
                $"outgoing fire journal and spawned local beam retain identical authored timing count={intent.FireEventCount} shot={intent.FireEvents[0].ShotId}/{shot} ack={intent.FireEvents[0].AckFrame}/{authoredAck} phase={intent.FireEvents[0].ContinuousPhase}/{beam.ModContinuousPhase}");
            Set(nameof(NetSession.AppliedSnapshotFrame), authoredAck - 3);
            NetHitClaims.Reset();
            ushort id = NetHitClaims.Declare(_victim, _owner, BeamType.ShockCoil, 1,
                DamageFlags.NoDmgInvuln, false, _victim.Position, beam.ModLaunchFrame, Vector3.Zero,
                continuousPhase: (uint)beam.ModContinuousPhase, direct: true, shotId: shot,
                shotKey: beam.ModLaunchKey, componentId: beam.ModComponentId);
            byte[] bytes = new byte[2048]; int size = NetHitClaims.Compose(bytes);
            Check(id != 0 && size >= 1 + HitClaimPacket.Size && bytes[0] == 1,
                "post-EndShot local collision enters real claim outbox");
            var claim = HitClaimPacket.Read(bytes.AsSpan(1, HitClaimPacket.Size));
            Check(claim.AckFrame == authoredAck && claim.LaunchFrame == authoredAck
                && claim.Frame == beam.ModContinuousPhase && claim.ShotId == shot,
                "outbox never resamples older collision ACK or substitutes owner phase");
        }
        finally
        {
            Set(nameof(NetSession.Role), role); Set(nameof(NetSession.IsAuthority), authority);
            Set(nameof(NetSession.LocalSlot), local); Set(nameof(NetSession.AppliedSnapshotFrame), applied);
            NetFireEvents.Reset(); NetHitClaims.Reset();
        }
    }
    private static void TerminalDeathOrdering(bool rescueFirst)
    {
        Prepare(); _owner.Hunter = Hunter.Sylux;
        var beam = Beam(); var claim = JournalClaim(beam);
        byte[]? original = null; long payments = NetContinuousPayments.LifetimePayments;
        if (rescueFirst)
        {
            Check(Receive(claim) == HitVerdictPacket.ResultApplied,
                "rescue-before-death fixture pays exact queued phase");
            original = Receipt(_ack);
        }
        else
        {
            byte[] packet = new byte[1 + HitClaimPacket.Size]; packet[0] = 1; claim.Write(packet.AsSpan(1));
            NetHitClaims.Receive(0, packet);
        }
        int victimHealth = _victim.Health; _owner.Health = 0;
        NetFireEvents.DrainAccepted(_owner);
        Frame(_frame + (uint)NetHitClaims.MaxGraceFrames + 2); NetHitClaims.Tick();
        byte result = Receive(claim);
        if (rescueFirst)
            Check(result == HitVerdictPacket.ResultApplied && Receipt(_ack).SequenceEqual(original!)
                && NetContinuousPayments.LifetimePayments == payments + 1
                && NetContinuousAdmission.Validate(0, claim, out _),
                "paid rescue survives dead-owner native drain with byte-identical original receipt");
        else
            Check(result == HitVerdictPacket.ResultInvalidLaunch
                && !NetContinuousPayments.TryGet(Key(beam, _victim), out _, out _, out _)
                && NetContinuousPayments.LifetimePayments == payments,
                "pending alive-owner claim cannot rescue terminally rejected native pulse after death");
        Check(_owner.Health == 0 && _victim.Health == victimHealth,
            "dead-owner drain and grace cannot add damage or heal");
    }
    private static void RampAfterFractionalDither()
    {
        for (int ramp = 0; ramp <= 1; ramp++)
        {
            Prepare(); _owner.Hunter = Hunter.Sylux;
            Frame(160); NetUnlagged.Record(_frame); NetDynamicGeometryHistory.RecordWorld(_frame);
            var beam = Beam(); beam.ModContinuousPhase = 160;
            var weapon = _owner.OwningScene.WeaponRules[(int)BeamType.ShockCoil + 9];
            Check(ContinuousWeaponPhase.Amount(weapon.UnchargedDamage, 160, true) == 0,
                "phase 160 fixture has zero authored fractional base damage");
            var target = NetTargetIdentity.ForSlot(1);
            var fire = new FireEvent(beam.ModShotId, 160, _frame, 0, FireEventKind.ContinuousTick,
                (byte)BeamType.ShockCoil, 0, 160,
                (byte)(FireEvent.FlagPose | FireEvent.FlagContinuousContext | ramp << 4),
                _owner.Position + Vector3.UnitY * .5f, _direction, _direction, _direction,
                SourceLife: NetPlayerLifecycle.Get(0), Target: target);
            var packet = NetSession.RemoteIntents[0]; packet.Frame = 160; packet.AckFrame = _frame;
            packet.HasFireEvents = true; packet.FireEventCount = 1; packet.FireEvents[0] = fire;
            packet.ContinuousFireTick = 160;
            Check(NetFireEvents.Accept(0, packet), "zero-base ramp source accepted into exact journal");
            var claim = new HitClaimPacket { MatchId = NetSession.CurrentMatchId,
                AuthorityEpoch = NetSession.AuthorityEpoch, ShooterGeneration = NetPlayerLifecycle.Generation(0),
                ShooterLifeId = NetPlayerLifecycle.Get(0), VictimSlot = 1, VictimGeneration = target.Generation,
                VictimLifeId = target.LifeId, ClaimId = ++_claimId, ShotId = beam.ModShotId,
                ComponentId = beam.ModComponentId, Frame = 160, AckFrame = _frame,
                LaunchFrame = beam.ModLaunchFrame, Beam = (byte)BeamType.ShockCoil,
                Flags = HitClaimPacket.FlagContinuousTick | HitClaimPacket.FlagDirect, Damage = 1,
                HitPoint = _victim.Position, Direction = Vector3.Zero };
            long payments = NetContinuousPayments.LifetimePayments;
            byte result = Receive(claim);
            if (ramp == 0)
                Check(result == HitVerdictPacket.ResultInvalidLaunch && _victim.Health == 99
                    && _owner.Health == 50 && NetContinuousPayments.LifetimePayments == payments,
                    "zero fractional base with authored ramp zero refuses damage one");
            else
            {
                Check(result == HitVerdictPacket.ResultApplied && _victim.Health == 98
                    && _owner.Health == 51 && NetContinuousPayments.LifetimePayments == payments + 1,
                    "authored ramp one adds one after zero fractional base and rescues exactly once");
                byte[] receipt = Receipt(_ack); ushort sequence = NetDamage.Sequence(1);
                Native(beam, 1); Native(beam, 1);
                Check(_victim.Health == 98 && _owner.Health == 51 && NetDamage.Sequence(1) == sequence
                    && NetContinuousPayments.LifetimePayments == payments + 1,
                    "native collision after zero-base ramp rescue cannot repay damage or heal");
                Check(Receive(claim) == HitVerdictPacket.ResultApplied && Receipt(_ack).SequenceEqual(receipt),
                    "zero-base ramp terminal retry preserves original exact receipt");
            }
        }
    }
    private static void RefusedSources()
    {
        for (int variant = 0; variant < 6; variant++)
        {
            Prepare(); var beam = Beam(); var claim = JournalClaim(beam, publish: variant != 0, odd: variant == 1, blocked: variant == 5);
            if (variant == 2) claim.Frame += 2;
            if (variant == 3) claim.VictimLifeId++;
            if (variant == 4)
            { claim.VictimSlot = 2; claim.VictimGeneration = NetPlayerLifecycle.Generation(2);
                claim.VictimLifeId = NetPlayerLifecycle.Get(2); claim.HitPoint = _other.Position; }
            byte result = Receive(claim);
            Check(result != HitVerdictPacket.ResultApplied && result != HitVerdictPacket.ResultDuplicate
                && _victim.Health == 99 && _other.Health == 99 && _owner.Health == 50,
                $"unpaid continuous source refusal preserves health and heal variant={variant}");
        }
    }
    private static void RetainedCarrierEvent(bool latest = false, bool dead = false)
    {
        Prepare(); _owner.Hunter = Hunter.Sylux; _owner.ModArmWeapon(BeamType.ShockCoil); _owner.EquipInfo.InfiniteAmmo = true;
        uint phase = (_frame - 2) & ~1u, shot = ++_shot;
        var weapon = _owner.OwningScene.WeaponRules[(int)BeamType.ShockCoil + 9];
        for (int i = 0; i < 32 && ContinuousWeaponPhase.Amount(weapon.UnchargedDamage, phase, true) == 0; i++) phase -= 2;
        Check(ContinuousWeaponPhase.Amount(weapon.UnchargedDamage, phase, true) > 0,
            "retained fixture chooses authored even phase with positive native dither");
        var target = NetTargetIdentity.ForSlot(1);
        var fire = new FireEvent(shot, phase, _frame, 0, FireEventKind.ContinuousTick,
            (byte)BeamType.ShockCoil, 0, phase, (byte)(FireEvent.FlagPose | 0x80),
            _owner.Position + Vector3.UnitY * .5f, _direction, _direction, _direction,
            SourceLife: NetPlayerLifecycle.Get(0), Target: target);
        var carrier = new IntentPacket { MatchId = NetSession.CurrentMatchId, AuthorityEpoch = NetSession.AuthorityEpoch,
            SlotGeneration = NetPlayerLifecycle.Generation(0), LifeId = NetPlayerLifecycle.Get(0),
            Frame = _frame, AckFrame = _frame, Position = _owner.Position, Aim = -_direction,
            WeaponSelect = (byte)BeamType.ShockCoil, Buttons = IntentButtons.InPlayState | IntentButtons.Shoot,
            HasState = false, Target = NetTargetIdentity.ForSlot(2), HasContinuousFireTick = true,
            ContinuousFireTick = latest ? phase : phase + 2, HasFireEvents = true, FireEventCount = 1 };
        carrier.FireEvents[0] = fire; NetSession.RemoteIntents[0] = carrier; NetSession.RemoteIntentValid[0] = true; NetSession.RemoteIntentArrived[0] = _frame;
        Check(NetFireEvents.Accept(0, carrier), "older unique continuous event accepted in newer carrier");
        typeof(PlayerEntity).GetField("_doubleDmgTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_owner, (ushort)1000);
        typeof(PlayerEntity).GetField("_shockCoilTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_owner, (ushort)240);
        _owner.Controls.ClearAll();
        typeof(PlayerEntity).GetField("_autofireCooldown", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_owner, (ushort)1000);
        if (dead) _owner.Health = 0;
        long paymentsBefore = NetContinuousPayments.LifetimePayments;
        NetFireEvents.Prepare(_owner, carrier); NetFireEvents.DrainAccepted(_owner);
        if (dead)
        {
            Check(_owner.Health == 0 && _victim.Health == 99 && _other.Health == 99
                && NetContinuousPayments.LifetimePayments == paymentsBefore,
                "accepted continuous pulse cannot pay or heal dead owner at drain time");
            return;
        }
        var beam = _owner.EquipInfo.Beams.FirstOrDefault(b => b.ModLaunchKey == ShotKey.For(0, shot));
        Check(beam != null && beam.ModContinuousPhase == phase && beam.ModHasSharedContinuousPhase,
            "retained event spawns with authored phase rather than newer carrier phase");
        Check(beam!.Target == _victim && beam.Target != _other,
            "retained event keeps authored victim rather than future carrier target");
        int victimHealth = _victim.Health, ownerHealth = _owner.Health;
        Check(99 - victimHealth == ContinuousWeaponPhase.Amount(weapon.UnchargedDamage, phase, true),
            "retained authored no-double zero-ramp damage ignores later owner modifiers");
        Check(victimHealth < 99 && _other.Health == 99 && ownerHealth - 50 == 99 - victimHealth,
            "retained older event pays authored target and drain exactly once");
        Check(NetFireEvents.Accept(0, carrier), "repeated newer carrier remains admissible");
        Frame(_frame + 1); NetFireEvents.Prepare(_owner, carrier); NetFireEvents.DrainAccepted(_owner); Native(beam, 1);
        Check(_victim.Health == victimHealth && _owner.Health == ownerHealth && _other.Health == 99,
            "repeated retained event cannot repay or transfer to future target");
        _victim.Spawn(_victim.Position, Vector3.UnitZ, Vector3.UnitY, _victim.NodeRef, respawn: true);
        _victim.ModReleaseSpawnProtection(); int replacementHealth = _victim.Health;
        Native(beam, 1);
        Check(_victim.Health == replacementHealth && _owner.Health == ownerHealth,
            "paid retained phase cannot transfer to victim replacement life");
    }
    private static void ExpiryCallback()
    {
        var history = new NetIncomingFireHistory(); int count = 0; FireEvent expired = default;
        history.OnExpired = fire => { count++; expired = fire; };
        var fire = new FireEvent(1, 100, 100, 0, FireEventKind.ContinuousTick,
            (byte)BeamType.ShockCoil, 0, 100, FireEvent.FlagPose,
            Vector3.Zero, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
            SourceLife: 1, Target: NetTargetIdentity.ForSlot(1));
        var carrier = new IntentPacket { Frame = 100, HasFireEvents = true, FireEventCount = 1 };
        carrier.FireEvents[0] = fire;
        Check(history.Accept(carrier) && history.Pending == 1, "expiry fixture parks exact continuous fire");
        carrier.Frame = 165; carrier.FireEventCount = 0;
        Check(history.Accept(carrier) && history.Accept(carrier) && count == 1 && expired == fire
            && history.Pending == 0 && history.ExpiredPending == 1,
            "retention expiry reports exact authored fire once and retires pending pulse");
    }
    public static int Run(string profile, string room)
    {
        var mode = NetShotProof.Mode; bool accepted = NetFireEvents.AcceptedPlaybackEnabled; var sim = new ServerSim();
        if (!sim.Start(room, GameMode.Battle, 3, _ => { }, () => { })) return 1;
        try
        {
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1, RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 3;
            for (byte i = 0; i < 3; i++) { roster.Slots[i] = i; roster.Generations[i] = 1; }
            NetSession.ApplyRoster(roster); for (int i = 0; i < 120; i++) sim.Step();
            _owner = PlayerEntity.Players[0]; _victim = PlayerEntity.Players[1]; _other = PlayerEntity.Players[2];
            _base = _owner.Position; _frame = NetSession.NetFrame; NetFireEvents.AcceptedPlaybackEnabled = true; NetShotProof.Mode = ShotProofMode.Shadow;
            foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
                if (NetDynamicGeometryHistory.TryTraceDistanceAtFrame(_owner.OwningScene, _base + Vector3.UnitY * .5f,
                    _base + direction * 4 + Vector3.UnitY * .5f, _frame, out float wall) && wall == 1)
                { _direction = direction; break; }
            Check(_direction.LengthSquared == 1, "asset fixture provides clear continuous collision lane");
            NativePaymentOnce(); SelectedTargetFence(); DeclinedAndCapped(); SplitAndBalanced(); ActualCollision(); ReservationAndLifetime(); ClaimAndNativeOrdering(); LocalAuthoredClock(); TerminalDeathOrdering(false); TerminalDeathOrdering(true); RampAfterFractionalDither(); RefusedSources(); ExpiryCallback(); RetainedCarrierEvent(); RetainedCarrierEvent(latest: true); RetainedCarrierEvent(latest: true, dead: true);
            Check(sim.StepFailures == 0, "continuous asset simulation has no step failures");
            Console.WriteLine($"PASS: {_checks} continuous combat asset assertions ({profile})"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { sim.Stop(); NetSession.Stop(); NetFireEvents.AcceptedPlaybackEnabled = accepted; NetShotProof.Mode = mode; }
    }
}
