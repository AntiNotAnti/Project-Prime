using System;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
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
    private static void MalformedContinuousShape()
    {
        var savedMode = NetShotProof.Mode;
        // Include reservations and receipt IDs: refusal must neither allocate
        // ledger storage nor poison the valid claim with the same ID.
        object[] ArrayState(Type type, string field) => ((Array)type.GetField(field,
            BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Cast<object>().ToArray();
        void Refused(in HitClaimPacket malformed, string label)
        {
            int victim = _victim.Health, owner = _owner.Health;
            ushort sequence = NetDamage.Sequence(1); Vector3 speed = _victim.Speed;
            long payments = NetContinuousPayments.LifetimePayments;
            var counts = (NetHitClaims.ClaimsPendingCurrent, NetHitClaims.ResolvedLedgerCurrent, NetHitClaims.RescuedLedgerCurrent);
            var entries = ArrayState(typeof(NetContinuousPayments), "Entries");
            var windows = ArrayState(typeof(NetContinuousPayments), "Windows");
            var receipts = ArrayState(typeof(NetHitClaims), "_seenIds");
            byte[] packet = new byte[1 + HitClaimPacket.Size]; packet[0] = 1; malformed.Write(packet.AsSpan(1));
            Check(!NetHitClaims.ValidateWireClaims(packet), label + " wire rejects malformed continuous shape");
            Check(Receive(malformed) == HitVerdictPacket.ResultInvalidLaunch,
                label + " direct Receive refuses before paid or terminal receipt");
            Check(_victim.Health == victim && _owner.Health == owner && _victim.Speed == speed
                && NetDamage.Sequence(1) == sequence && NetContinuousPayments.LifetimePayments == payments,
                label + " refusal cannot damage, heal, impulse or advance damage sequence");
            Check(counts == (NetHitClaims.ClaimsPendingCurrent, NetHitClaims.ResolvedLedgerCurrent, NetHitClaims.RescuedLedgerCurrent)
                && entries.SequenceEqual(ArrayState(typeof(NetContinuousPayments), "Entries"))
                && windows.SequenceEqual(ArrayState(typeof(NetContinuousPayments), "Windows"))
                && receipts.SequenceEqual(ArrayState(typeof(NetHitClaims), "_seenIds")),
                label + " refusal allocates no payment, pending, resolved, rescue or receipt slot");
            NetHitClaims.ValidateLedgerCounters();
        }
        string[] variants = { "missing continuous flag", "zero frame", "odd frame", "missing direct flag",
            "headshot", "non-Shock continuous flag", "zero shot" };
        try
        {
            foreach (var mode in new[] { ShotProofMode.Off, ShotProofMode.Shadow, ShotProofMode.Enforce })
                for (int variant = 0; variant < variants.Length; variant++)
                {
                    NetShotProof.Mode = mode; Prepare(); _owner.Hunter = Hunter.Sylux;
                    var beam = Beam(); var valid = JournalClaim(beam); var malformed = valid;
                    switch (variant)
                    {
                        case 0: malformed.Flags = (byte)(malformed.Flags & ~HitClaimPacket.FlagContinuousTick); break;
                        case 1: malformed.Frame = 0; break;
                        case 2: malformed.Frame |= 1; break;
                        case 3: malformed.Flags = (byte)(malformed.Flags & ~HitClaimPacket.FlagDirect); break;
                        case 4: malformed.Flags |= HitClaimPacket.FlagHeadshot; break;
                        case 5: malformed.Beam = (byte)BeamType.PowerBeam; break;
                        case 6: malformed.ShotId = 0; break;
                    }
                    string label = mode + " " + variants[variant];
                    Refused(malformed, label + " before native payment");
                    Native(beam, 1);
                    Check(_victim.Health == 98 && _owner.Health == 51
                        && NetContinuousPayments.TryGet(Key(beam, _victim), out var paid, out _, out _)
                        && paid.DamageApplied == 1, label + " actual native tick remains eligible after malformed same-ID arrival");
                    Refused(malformed, label + " after native payment");
                    Check(Receive(valid) == HitVerdictPacket.ResultDuplicate && _ack.ShotId == valid.ShotId
                        && _ack.DamageApplied == 1 && _victim.Health == 98 && _owner.Health == 51,
                        label + " valid same-ID claim returns original native outcome");
                    byte[] receipt = Receipt(_ack);
                    Refused(malformed, label + " after valid terminal receipt");
                    Check(Receive(valid) == HitVerdictPacket.ResultDuplicate && Receipt(_ack).SequenceEqual(receipt),
                        label + " malformed same-ID retry cannot poison byte-identical valid terminal retry");
                }
        }
        finally { NetShotProof.Mode = savedMode; }
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
    private static void FractionalHistoricalTarget()
    {
        Prepare(); _owner.Hunter = Hunter.Sylux; var beam = Beam();
        uint first = _frame;
        Vector3 center = _base + _direction * 3;
        Vector3 side = Vector3.Cross(_direction, Vector3.UnitY).Normalized();
        var weapon = _owner.OwningScene.WeaponRules[(int)BeamType.ShockCoil + 9];
        Vector3 origin = _owner.Position + Vector3.UnitY * .5f;
        float offset = .1f;
        while (offset < 8 && NetContinuousTargeting.Evaluate(origin, _direction,
            center + side * offset + Vector3.UnitY * .5f, true, true, false, false, false,
            Fixed.ToFloat(weapon.HomingRange), Fixed.ToFloat(weapon.HomingTolerance), out _, out _, out _)
            == ContinuousTargetRejection.None) offset += .1f;
        Check(offset < 8, "moving historical fixture locates integer pose outside authored cone");
        _victim.ModPlaceAt(center + side * offset); NetUnlagged.Record(first);
        Frame(first + 1); _victim.ModPlaceAt(center - side * offset);
        NetUnlagged.Record(_frame); NetDynamicGeometryHistory.RecordWorld(_frame);
        Check(NetUnlagged.TryContinuousClaimPose(_victim, first + .5, out var half, out _, out _)
            && Vector3.Distance(half.Position, center) < .001f,
            "continuous claim pose samples fractional moving-target midpoint");
        var claim = JournalClaim(beam, publish: false); claim.AckFrame = first;
        claim.HitPoint = center - side * 1.9f;
        Check(Vector3.Distance(claim.HitPoint, center + side * offset) > 2
            && Vector3.Distance(claim.HitPoint, half.Position) < 2,
            "fractional midpoint hitpoint lies outside integer radius and inside captured fractional radius");
        var fire = new FireEvent(beam.ModShotId, claim.Frame, claim.AckFrame, 128,
            FireEventKind.ContinuousTick, (byte)BeamType.ShockCoil, 0, claim.Frame,
            FireEvent.FlagPose, origin, _direction, _direction, _direction,
            SourceLife: NetPlayerLifecycle.Get(0), Target: NetTargetIdentity.ForSlot(1));
        var carrier = NetSession.RemoteIntents[0]; carrier.HasFireEvents = true;
        carrier.FireEventCount = 1; carrier.FireEvents[0] = fire;
        Check(NetFireEvents.Accept(0, carrier), "fractional target source enters exact journal");
        Check(NetContinuousAdmission.Validate(0, claim, out _),
            "uncaptured admission world includes authored ACK fraction");
        NetContinuousAdmission.CaptureWorld(beam.ModLaunchKey, first);
        Check(!NetContinuousAdmission.Validate(0, claim, out _),
            "integer historical endpoint outside cone refuses claim");

        // A fresh source captures the fractional world once, independently of its later carrier.
        NetFireEvents.Reset(); NetHitClaims.Reset();
        Check(NetFireEvents.Accept(0, carrier), "fractional source re-admitted for immutable capture fixture");
        NetContinuousAdmission.CaptureWorld(beam.ModLaunchKey, first + .5);
        Check(NetContinuousAdmission.Validate(0, claim, out _),
            "fractional historical midpoint inside cone admits exact claim");
        Frame(first + 2); _victim.ModPlaceAt(center + side * (offset + 2));
        NetUnlagged.Record(_frame); NetDynamicGeometryHistory.RecordWorld(_frame);
        NetContinuousAdmission.CaptureWorld(beam.ModLaunchKey, _frame);
        Check(NetContinuousAdmission.Validate(0, claim, out _),
            "captured fractional world remains immutable after later frame and current pose change");
    }
    private static void PendingSourceSettlement()
    {
        foreach (bool nativeWins in new[] { false, true })
        {
            Prepare(); _owner.Hunter = Hunter.Sylux; var beam = Beam();
            var claim = JournalClaim(beam, publish: false);
            byte[] packet = new byte[1 + HitClaimPacket.Size]; packet[0] = 1; claim.Write(packet.AsSpan(1));
            NetHitClaims.Receive(0, packet);
            Check(NetHitClaims.ClaimsPendingCurrent == 1 && _victim.Health == 99 && _owner.Health == 50,
                "continuous missing source parks on arrival without damage or early terminal refusal");
            long payments = NetContinuousPayments.LifetimePayments;
            if (nativeWins) Native(beam, 1);
            byte result = Receive(claim);
            Check(nativeWins ? result == HitVerdictPacket.ResultDuplicate && _ack.DamageApplied == 1
                    && _victim.Health == 98 && _owner.Health == 51
                    && NetContinuousPayments.LifetimePayments == payments + 1
                : result == HitVerdictPacket.ResultInvalidLaunch && _victim.Health == 99
                    && _owner.Health == 50 && NetContinuousPayments.LifetimePayments == payments,
                nativeWins ? "valid native payment before rescue returns immutable outcome despite missing source"
                    : "still-invalid continuous source refuses at grace settlement without payment");
        }
    }
    private static void DeferredContinuousHitRadius()
    {
        foreach (bool nativeWins in new[] { true, false })
        {
            Prepare(); _owner.Hunter = Hunter.Sylux; var beam = Beam();
            var claim = JournalClaim(beam, publish: !nativeWins);
            claim.HitPoint = _victim.Position + Vector3.UnitY * 2.02f;
            byte[] packet = new byte[1 + HitClaimPacket.Size]; packet[0] = 1; claim.Write(packet.AsSpan(1));
            NetHitClaims.Receive(0, packet);
            Check(NetHitClaims.ClaimsPendingCurrent == 1 && _victim.Health == 99 && _owner.Health == 50,
                "continuous hitpoint 2.02 units outside integer radius parks until exact settlement");
            long payments = NetContinuousPayments.LifetimePayments;
            CombatAckEntry paid = default;
            if (nativeWins)
            {
                Native(beam, 1);
                Check(NetContinuousPayments.TryGet(Key(beam, _victim), out paid, out _, out _),
                    "deferred radius fixture has immutable native payment before grace");
            }
            byte result = Receive(claim);
            if (nativeWins)
                Check(result == HitVerdictPacket.ResultDuplicate && _ack.ShotId == paid.ShotId
                    && _ack.DamageSequence == paid.DamageSequence && _ack.HealthAfter == paid.HealthAfter
                    && _ack.DamageApplied == paid.DamageApplied && _victim.Health == 98 && _owner.Health == 51
                    && NetContinuousPayments.LifetimePayments == payments + 1,
                    "native-paid continuous hit returns original outcome despite integer hitpoint radius");
            else
                Check(result == HitVerdictPacket.ResultInvalidLaunch && _victim.Health == 99
                    && _owner.Health == 50 && NetContinuousPayments.LifetimePayments == payments
                    && !NetContinuousPayments.TryGet(Key(beam, _victim), out _, out _, out _),
                    "unpaid forged hitpoint outside captured history refuses rescue after grace");
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
    private static void CheckContinuousTrail(BeamProjectileEntity beam)
    {
        var scene = _owner.OwningScene;
        var items = (System.Collections.Generic.List<RenderItem>)typeof(Scene)
            .GetField("_nonDecalItems", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scene)!;
        int before = items.Count;
        beam.GetDrawInfo();
        Check(beam.DrawFuncId == 9 && items.Count == before + 1
            && items[before].Type == RenderItemType.TrailMulti && items[before].ItemCount == 40
            && items[before].Alpha > 0 && items[before].Points != null
            && items[before].Points!.Take(40).All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)),
            "accepted targeted Shock Coil submits actual finite visible Draw09 trail geometry");
    }
    private static void RetainedCarrierEvent(bool latest = false, bool dead = false, bool queued = false, bool observer = false)
    {
        Prepare(); _owner.Hunter = Hunter.Sylux; _owner.ModArmWeapon(BeamType.ShockCoil); _owner.EquipInfo.InfiniteAmmo = true;
        uint phase = (_frame - (queued ? 16u : 2u)) & ~1u, shot = ++_shot;
        var weapon = _owner.OwningScene.WeaponRules[(int)BeamType.ShockCoil + 9];
        for (int i = 0; i < 32 && ContinuousWeaponPhase.Amount(weapon.UnchargedDamage, phase, true) == 0; i++) phase -= 2;
        Check(ContinuousWeaponPhase.Amount(weapon.UnchargedDamage, phase, true) > 0,
            "retained fixture chooses authored even phase with positive native dither");
        var target = NetTargetIdentity.ForSlot(1);
        var fire = new FireEvent(shot, phase, _frame, 0, FireEventKind.ContinuousTick,
            (byte)BeamType.ShockCoil, 0, phase, (byte)(FireEvent.FlagPose | 0x80),
            _owner.Position + Vector3.UnitY * .5f, _direction, _direction, _direction,
            SourceLife: NetPlayerLifecycle.Get(0), Target: target);
        var firstFire = fire;
        int expectedDamage = ContinuousWeaponPhase.Amount(weapon.UnchargedDamage, phase, true);
        if (queued)
        {
            phase += 2; shot = ++_shot;
            while (ContinuousWeaponPhase.Amount(weapon.UnchargedDamage, phase, true) == 0) phase += 2;
            fire = fire with { ShotId = shot, SourceFrame = phase, ContinuousPhase = phase };
            expectedDamage += ContinuousWeaponPhase.Amount(weapon.UnchargedDamage, phase, true);
        }
        var carrier = new IntentPacket { MatchId = NetSession.CurrentMatchId, AuthorityEpoch = NetSession.AuthorityEpoch,
            SlotGeneration = NetPlayerLifecycle.Generation(0), LifeId = NetPlayerLifecycle.Get(0),
            Frame = _frame, AckFrame = _frame, Position = _owner.Position, Aim = -_direction,
            WeaponSelect = (byte)BeamType.ShockCoil, Buttons = IntentButtons.InPlayState | IntentButtons.Shoot,
            HasState = false, Target = NetTargetIdentity.ForSlot(2), HasContinuousFireTick = true,
            ContinuousFireTick = latest ? phase : phase + 2, HasFireEvents = true, FireEventCount = 1 };
        carrier.FireEvents[0] = queued ? firstFire : fire;
        if (queued) { carrier.FireEventCount = 2; carrier.FireEvents[1] = fire; }
        NetSession.RemoteIntents[0] = carrier; NetSession.RemoteIntentValid[0] = true; NetSession.RemoteIntentArrived[0] = _frame;
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
        if (observer)
        {
            Check(NetSession.Role == NetRole.Client && !NetSession.IsAuthority && NetSession.LocalSlot == 2,
                "accepted observer fixture runs as independent nonauthority client");
            int observerVictimHealth = _victim.Health, observerOwnerHealth = _owner.Health;
            long observerPayments = NetContinuousPayments.LifetimePayments;
            float observerAge = beam.Age;
            Check(beam.Process() && beam.Lifespan > 0 && beam.Age == observerAge,
                "actual client accepted spawn survives ordinary same-frame pass");
            CheckContinuousTrail(beam);
            Check(_victim.Health == observerVictimHealth && _owner.Health == observerOwnerHealth
                && NetContinuousPayments.LifetimePayments == observerPayments,
                "actual client accepted trail pass cannot resolve predicted pulse twice");
            var observerScene = _owner.OwningScene;
            var observerFrame = typeof(Scene).GetField("_frameCount", BindingFlags.NonPublic | BindingFlags.Instance)!;
            ulong at = observerScene.FrameCount;
            try
            {
                observerFrame.SetValue(observerScene, at + 1);
                Check(!beam.Process(), "actual client accepted trail retires without next-frame refresh");
            }
            finally { observerFrame.SetValue(observerScene, at); }
            return;
        }
        int victimHealth = _victim.Health, ownerHealth = _owner.Health;
        Check(99 - victimHealth == expectedDamage,
            "retained authored no-double zero-ramp damage ignores later owner modifiers");
        Check(victimHealth < 99 && _other.Health == 99 && ownerHealth - 50 == 99 - victimHealth,
            "retained older event pays authored target and drain exactly once");
        Check(NetContinuousPayments.LifetimePayments == paymentsBefore + (queued ? 2 : 1),
            "each accepted positive continuous pulse commits exactly one payment");
        float age = beam.Age, lifespan = beam.Lifespan;
        long payments = NetContinuousPayments.LifetimePayments;
        bool retainedInScene = false;
        foreach (var entity in _owner.OwningScene.Entities) retainedInScene |= entity == beam;
        Check(beam.Process() && beam.Lifespan == lifespan && beam.Age == age
            && beam.Target == _victim && !beam.Flags.TestFlag(BeamFlags.Collided)
            && retainedInScene,
            "accepted continuous beam survives normal same-frame pass with visible targeted trail");
        Check(_victim.Health == victimHealth && _owner.Health == ownerHealth
            && NetContinuousPayments.LifetimePayments == payments,
            "normal same-frame pass cannot resolve accepted pulses twice");
        CheckContinuousTrail(beam);
        var savedRole = NetSession.Role;
        bool savedAuthority = NetSession.IsAuthority;
        int savedLocal = NetSession.LocalSlot;
        try
        {
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Client);
            typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, false);
            typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, 2);
            Check(beam.Process() && beam.Lifespan == lifespan && beam.Age == age,
                "client observer also preserves accepted beam through normal same-frame pass");
            CheckContinuousTrail(beam);
            Check(_victim.Health == victimHealth && _owner.Health == ownerHealth
                && NetContinuousPayments.LifetimePayments == payments,
                "client observer rendering cannot repay accepted damage or healing");
        }
        finally
        {
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, savedRole);
            typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, savedAuthority);
            typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, savedLocal);
        }
        var scene = _owner.OwningScene;
        var frameField = typeof(Scene).GetField("_frameCount", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ulong sceneFrame = scene.FrameCount;
        try
        {
            frameField.SetValue(scene, sceneFrame + 1);
            Check(!beam.Process(), "accepted continuous beam retires next scene step without refresh");
        }
        finally { frameField.SetValue(scene, sceneFrame); }
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
    private static void ObserverAcceptedEvent()
    {
        var role = NetSession.Role; int local = NetSession.LocalSlot; bool authority = NetSession.IsAuthority;
        try
        {
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Client);
            typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, false);
            typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, 2);
            RetainedCarrierEvent(observer: true);
        }
        finally
        {
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, role);
            typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, authority);
            typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, local);
        }
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
            NativePaymentOnce(); SelectedTargetFence(); DeclinedAndCapped(); SplitAndBalanced(); ActualCollision(); ReservationAndLifetime(); ClaimAndNativeOrdering(); MalformedContinuousShape(); LocalAuthoredClock(); TerminalDeathOrdering(false); TerminalDeathOrdering(true); RampAfterFractionalDither(); FractionalHistoricalTarget(); PendingSourceSettlement(); DeferredContinuousHitRadius(); RefusedSources(); ExpiryCallback(); RetainedCarrierEvent(); RetainedCarrierEvent(latest: true); RetainedCarrierEvent(queued: true); RetainedCarrierEvent(latest: true, dead: true);
            Check(sim.StepFailures == 0, "continuous asset simulation has no step failures");
            ObserverAcceptedEvent();
            Console.WriteLine($"PASS: {_checks} continuous combat asset assertions ({profile})"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { sim.Stop(); NetSession.Stop(); NetFireEvents.AcceptedPlaybackEnabled = accepted; NetShotProof.Mode = mode; }
    }
}
