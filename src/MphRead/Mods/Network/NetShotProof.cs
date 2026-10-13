using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public enum ShotProofMode { Off, Shadow, Enforce }
public enum ShotProofStatus { Verified, Rejected, Unavailable }
public enum ShotProofFamily { Straight, Projectile, Splash, Ricochet, Homing, Child, Continuous, Turret, BombOrMelee }
public enum ShotProofReason
{
    Verified, UnknownShot, ShotNotFired, WrongLife, WeaponMismatch, InvalidRay, StaleAck,
    SourceUnavailable, SourceChanged, UnownedWeapon, InvalidCharge, FireRate, Ammunition,
    ModifierMismatch, SourcePoseUnavailable, MuzzleDistance, BlockedMuzzle, UnknownComponent,
    HistoryUnavailable, MissedBody, BlockedWorld, WrongHeadshot, Reach, PhysicalWitnessRequired,
    UnsupportedContinuous, UnsupportedTurret, UnsupportedBombOrMelee, DuplicateComponent,
    DamageMismatch, Capacity, ContextConflict, SourceInputUnavailable, SourceHistoryIncomplete,
    UnverifiedModifier, ClaimEffectsMismatch, Unexamined, SourceClock, SourceAimUnavailable
}
public readonly record struct ShotProofDecision(ShotProofStatus Status, ShotProofReason Reason,
    ShotProofFamily Family, ShotProofReason Source, ShotProofReason Geometry, bool PhysicalMatch = false);
public readonly record struct ShotProofSource(ushort Weapons, ushort Charges, int Ammo0, int Ammo1,
    bool Infinite, Hunter Hunter, bool Prime, bool Double, bool Supported = true,
    int AmmoMax0 = int.MaxValue, int AmmoMax1 = int.MaxValue);
public readonly record struct ShotPaymentKey(ShotKey Shot, byte Victim, ushort Generation,
    ushort Life, uint Component, byte Kind);

/// <summary>Bounded, read-only proof of authored shots and authority collision witnesses.
/// Does not own emission or damage. Shadow is the default; unavailable evidence is never
/// reported as verified. Source state starts at authority spawn, before client
/// inventory is applied, and advances through independently recorded grants and expenditure.</summary>
public static class NetShotProof
{
    public static ShotProofMode Mode { get; set; } = Environment.GetEnvironmentVariable("PRIME_SHOT_PROOF") switch
    { "off" => ShotProofMode.Off, "enforce" => ShotProofMode.Enforce, _ => ShotProofMode.Shadow };
    public const int ShotsPerSlot = 256, ComponentsPerSlot = 512, PaymentsPerSlot = 512;
    public const uint Retention = 1024;
    private struct Shot
    {
        public bool Live, Fired;
        public ShotKey Key;
        public FireEvent Fire;
        public uint Observed;
        public int Cost, AmmoType, Cooldown;
        public ShotProofReason Pose, Source;
    }
    private struct Pose
    { public ShotKey Life; public uint SourceFrame, WorldFrame; public Vector3 Body, Aim; public bool Live, ClockValid, Disrupted; }
    private struct SourceClock
    { public bool Live; public ShotKey Stream; public uint Source, World; }
    private static readonly SourceClock[] _clocks=new SourceClock[8];
    private struct Component
    {
        public bool Live;
        public ShotKey Key;
        public uint Id, Observed;
        public byte Weapon;
        public Vector3 Origin, Direction;
        public float Radius, Speed, Reach, Damage, HeadDamage;
        public ShotProofFamily Family;
        public NetBurnSource? Burn;
    }
    private struct Payment
    { public bool Live, Physical, Claimed; public ShotPaymentKey Key; public uint Frame; public int Damage; public bool Head; public byte Effects; public Vector3 Impulse; public CombatAckEntry Outcome; }
    private static readonly Shot[,] _shots = new Shot[8, ShotsPerSlot];
    private static readonly Pose[,] _poses = new Pose[8, 128];
    private static readonly Component[,] _components = new Component[8, ComponentsPerSlot];
    private static readonly Payment[,] _payments = new Payment[8, PaymentsPerSlot];
    private static readonly long[] _reasons = new long[Enum.GetValues<ShotProofReason>().Length];
    private static readonly List<object> _evidence = new();
    private static readonly List<object> _emissionEvidence = new();
    private static readonly long[] _emissionReasons = new long[Enum.GetValues<ShotProofReason>().Length];
    public static long EmissionVerified, EmissionRejected, EmissionUnavailable;
    public static long Verified, Rejected, Unavailable, LegacyAllowedRejected, LegacyAllowedUnavailable,
        CapacityRefused, EvidenceOverflow, PhysicalDuplicates, RadiusSamples;
    public static double RadiusSum;
    public static float RadiusMax;
    private static readonly long[] _radiusBuckets = new long[9];
    private static bool Active => Mode != ShotProofMode.Off && NetHitClaims.Arbitrating;
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool Expired(uint frame) => unchecked(NetSession.NetFrame - frame) > Retention;
    private static bool SameLife(ShotKey a, ShotKey b) => a with { ShotId = 0 } == b with { ShotId = 0 };
    internal static ShotKey Key(int slot, in HitClaimPacket c) => new(c.AuthorityEpoch, c.MatchId,
        slot, c.ShooterGeneration, c.ShooterLifeId, c.ShotId);
    internal static byte Kind(byte flags) => (byte)(((flags & HitClaimPacket.FlagDirect) != 0 ? 1 : 2)
        | ((flags & HitClaimPacket.FlagHalfturret) != 0 ? 4 : 0));
    internal static ShotPaymentKey PaymentKey(int slot, in HitClaimPacket c) => new(Key(slot,c),
        c.VictimSlot,c.VictimGeneration,c.VictimLifeId,c.ComponentId,Kind(c.Flags));
    private static int Find(in ShotKey key)
    {
        if ((uint)key.ShooterSlot >= 8) return -1;
        for (int i=0;i<ShotsPerSlot;i++)
            if (_shots[key.ShooterSlot,i].Live && _shots[key.ShooterSlot,i].Key == key
                && !Expired(_shots[key.ShooterSlot,i].Observed)) return i;
        return -1;
    }
    internal static void Seed(PlayerEntity player, in ShotProofSource state)
    {
        NetShotSourceJournal.Seed(player, state);
        NetShotInputProof.Seed(player);
    }
    internal static void SourceChanged(PlayerEntity player) => NetShotSourceJournal.Invalidate(player);
    internal static void ObservePose(PlayerEntity player, in IntentPacket intent)
    {
        if (!Active || (uint)player.SlotIndex>=8 || !player.ModIsInPlay) return;
        var stream=ShotKey.For(player.SlotIndex,0) with { LifeId=0 };
        ref var clock=ref _clocks[player.SlotIndex];
        if(!clock.Live || clock.Stream!=stream)
            clock=new SourceClock { Live=true,Stream=stream,Source=intent.Frame,World=NetSession.NetFrame };
        // Fixed arrival anchor: never follow a dishonest sender's increasing offset.
        // Allow one retained history of jitter/batching, not unbounded source-clock acceleration.
        long sourceElapsed=unchecked((int)(intent.Frame-clock.Source));
        long elapsed=unchecked((int)(NetSession.NetFrame-clock.World));
        bool clockValid=sourceElapsed<=elapsed+32;
        _poses[player.SlotIndex,intent.Frame%128]=new Pose { Live=true,
            Life=ShotKey.For(player.SlotIndex,0), SourceFrame=intent.Frame,
            WorldFrame=NetSession.NetFrame,Body=player.Position,Aim=intent.Aim,
            ClockValid=clockValid,Disrupted=player.ModDisrupted };
    }
    internal static void Admit(PlayerEntity player, in ShotKey key, in FireEvent fire)
    {
        if (!Active || (uint)key.ShooterSlot>=8 || key.ShotId==0 || Find(key)>=0) return;
        int free=-1;
        for(int i=0;i<ShotsPerSlot;i++)
            if (!_shots[key.ShooterSlot,i].Live || Expired(_shots[key.ShooterSlot,i].Observed)
                || !NetSession.MatchesStream(_shots[key.ShooterSlot,i].Key.MatchId,_shots[key.ShooterSlot,i].Key.AuthorityEpoch)
                || _shots[key.ShooterSlot,i].Key.Generation != key.Generation) { free=i;break; }
        if(free<0) { CapacityRefused++; return; }
        int weapon=Math.Min((int)fire.Weapon,8);
        bool known = NetShotSourceJournal.TryState(key, fire.SourceFrame, out var sourceState);
        bool affinity = known && (sourceState.Prime || Weapons.GetAffinityBeam(sourceState.Hunter) == (BeamType)weapon);
        var mechanics=player.OwningScene.WeaponRules[weapon+(affinity?9:0)];
        // Prime swaps the emission row only around Spawn; input charge and cooldown
        // still use the hunter's normally equipped weapon.
        var inputMechanics = player.OwningScene.WeaponRules[weapon
            + (known && Weapons.GetAffinityBeam(sourceState.Hunter) == (BeamType)weapon ? 9 : 0)];
        float charge=0;
        if(mechanics.Flags.TestFlag(WeaponFlags.CanCharge))
        {
            if(mechanics.Flags.TestFlag(WeaponFlags.PartialCharge) && fire.Charge>=mechanics.MinCharge*2)
                charge=(fire.Charge-mechanics.MinCharge*2)/(float)Math.Max(1,(mechanics.FullCharge-mechanics.MinCharge)*2);
            else if(fire.Charge>=mechanics.FullCharge*2) charge=1;
        }
        bool battlehammerBurst = BalancedModeRules.IsBattlehammerBurst(
            player.OwningScene.GameState.Multiplayer, player.OwningScene.GameState.BalancedMode, (BeamType)weapon);
        int cost = battlehammerBurst ? BalancedModeRules.BattlehammerBurstAmmoCost
            : (int)(charge<=0?mechanics.AmmoCost:mechanics.MinChargeCost+(mechanics.ChargeCost-mechanics.MinChargeCost)*charge);
        var poseReason=ShotProofReason.SourcePoseUnavailable;
        ref var pose=ref _poses[key.ShooterSlot,fire.SourceFrame%128];
        if(!fire.HasPose || !Finite(fire.Origin) || !Finite(fire.Direction)
            || Math.Abs(fire.Direction.LengthSquared-1)>0.02f) poseReason=ShotProofReason.InvalidRay;
        else if(pose.Live && pose.SourceFrame==fire.SourceFrame && SameLife(pose.Life,key))
        {
            // ACK is the opponents' read point, not the shooter's source clock.
            Vector3 body=pose.Body+Vector3.UnitY*.5f;
            poseReason=!pose.ClockValid ? ShotProofReason.SourceClock
                : !Finite(pose.Aim) || pose.Aim.LengthSquared<.001f || pose.Disrupted
                    ? ShotProofReason.SourceAimUnavailable
                : Vector3.Dot(fire.Direction.Normalized(),pose.Aim.Normalized())<.99f
                    ? ShotProofReason.InvalidRay
                : (fire.Origin-body).LengthSquared>9 ? ShotProofReason.MuzzleDistance
                : !NetDynamicGeometryHistory.TryTraceDistanceAtFrame(player.OwningScene,body,fire.Origin,pose.WorldFrame,out float wall)
                    ? ShotProofReason.HistoryUnavailable : wall<.999f?ShotProofReason.BlockedMuzzle:ShotProofReason.Verified;
        }
        int cooldown = battlehammerBurst ? BalancedModeRules.BattlehammerBurstCooldownFrames
            : (fire.Kind == FireEventKind.AutomaticFire
                ? Math.Max(inputMechanics.ShotCooldown, inputMechanics.AutofireCooldown) : inputMechanics.ShotCooldown) * 2;
        var sourceReason = NetShotSourceJournal.Inspect(key, fire, mechanics.AmmoType, cost, cooldown);
        bool charged = mechanics.Flags.TestFlag(WeaponFlags.CanCharge)
            && fire.Charge >= (mechanics.Flags.TestFlag(WeaponFlags.PartialCharge) ? mechanics.MinCharge : mechanics.FullCharge) * 2;
        if (sourceReason == ShotProofReason.Verified)
        {
            if (!known) sourceReason = ShotProofReason.SourceUnavailable;
            else if (fire.Charge > 0 && !inputMechanics.Flags.TestFlag(WeaponFlags.CanCharge)
                || fire.Charge > inputMechanics.FullCharge * 2 && inputMechanics.Flags.TestFlag(WeaponFlags.CanCharge)
                || charged && (sourceState.Charges & (1 << weapon)) == 0)
                sourceReason = ShotProofReason.InvalidCharge;
            else if (fire.HasEnhancedContext || fire.PrimeAtFire != sourceState.Prime || fire.DoubleAtFire != sourceState.Double
                || fire.AffinityAtFire != affinity) sourceReason = ShotProofReason.ModifierMismatch;
            // Selecting another affinity slot can consume Omega even without a shot.
            // Until those transitions have independent evidence, its source is unavailable.
            else if (fire.Weapon == (byte)BeamType.OmegaCannon) sourceReason = ShotProofReason.SourceUnavailable;
            else if (fire.Kind == FireEventKind.AutomaticFire && fire.Weapon == (byte)BeamType.PowerBeam)
                sourceReason = ShotProofReason.SourceInputUnavailable; // Native held-fire cadence ramps over time.
            else sourceReason = NetShotInputProof.Inspect(player, key, fire,
                charged || fire.Kind == FireEventKind.ReleaseFire);
        }
        _shots[key.ShooterSlot,free]=new Shot { Live=true,Key=key,Fire=fire,Observed=NetSession.NetFrame,
            Cost=cost,AmmoType=mechanics.AmmoType,Cooldown=cooldown,
            Pose=poseReason, Source=sourceReason };
    }
    internal static void Fired(in ShotKey key)
    {
        int i=Find(key); if(i<0)return;
        ref var shot=ref _shots[key.ShooterSlot,i];
        if (shot.Fired) return;
        shot.Fired=true;
        NetShotSourceJournal.Fired(key,shot.Fire,shot.AmmoType,shot.Cost,shot.Cooldown);
    }
    internal static ShotProofReason SourceReason(in ShotKey key)
    {
        int at=Find(key);
        // Admission certificates are immutable; later pickups and ring eviction cannot
        // retroactively fund, refund or invalidate the source of an existing shot.
        return at<0 ? ShotProofReason.UnknownShot : _shots[key.ShooterSlot,at].Source;
    }
    internal static ShotProofReason EmissionReason(in ShotKey key)
    {
        var source=SourceReason(key);if(source!=ShotProofReason.Verified)return source;
        int at=Find(key);return at<0?ShotProofReason.UnknownShot:_shots[key.ShooterSlot,at].Pose;
    }
    internal static ShotProofReason ObserveEmission(in ShotKey key)
    {
        if (!Active) return ShotProofReason.Unexamined;
        var reason = EmissionReason(key);
        var status = reason == ShotProofReason.Verified ? ShotProofStatus.Verified
            : IsReject(reason) ? ShotProofStatus.Rejected : ShotProofStatus.Unavailable;
        _emissionReasons[(int)reason]++;
        if (status == ShotProofStatus.Verified) EmissionVerified++;
        else if (status == ShotProofStatus.Rejected) EmissionRejected++;
        else EmissionUnavailable++;
        if (NetCampaignEvidence.Enabled)
        {
            if (_emissionEvidence.Count < 16384)
                _emissionEvidence.Add(new { Key = key, AuthorityFrame = NetSession.NetFrame,
                    Status = status.ToString(), Reason = reason.ToString() });
            else EvidenceOverflow++;
        }
        return reason;
    }
    internal static uint ComponentId(uint parent, int ordinal)
        => ordinal is >0 and <16 && parent<=0x0FFFFFFF ? (parent<<4)|(uint)ordinal : 0;
    internal static void Emitted(BeamProjectileEntity beam, BeamProjectileEntity? parent)
    {
        if(!Active || beam.ModComponentId==0 || (uint)beam.ModLaunchKey.ShooterSlot>=8)return;
        int slot=beam.ModLaunchKey.ShooterSlot,free=-1;
        for(int i=0;i<ComponentsPerSlot;i++)
        {
            ref var c=ref _components[slot,i];
            if(c.Live && c.Key==beam.ModLaunchKey && c.Id==beam.ModComponentId)return;
            if(free<0 && (!c.Live || Expired(c.Observed) || c.Key.Generation!=beam.ModLaunchKey.Generation
                || !NetSession.MatchesStream(c.Key.MatchId,c.Key.AuthorityEpoch)))free=i;
        }
        if(free<0){CapacityRefused++;return;}
        var family=beam.Flags.TestFlag(BeamFlags.Continuous)?ShotProofFamily.Continuous
            : beam.Owner is HalfturretEntity?ShotProofFamily.Turret
            : parent!=null?ShotProofFamily.Child
            : beam.Homing>0?ShotProofFamily.Homing
            : beam.Flags.TestFlag(BeamFlags.Ricochet) || beam.RicochetWeapon!=null || beam.EnhancedBounceCount>0?ShotProofFamily.Ricochet
            : beam.Beam==BeamType.Imperialist?ShotProofFamily.Straight:ShotProofFamily.Projectile;
        bool balancedBurn = beam.OwningScene.GameState.Multiplayer && beam.OwningScene.GameState.BalancedMode
            && beam.ModOwnerHunter == Hunter.Spire && beam.Beam == BeamType.Magmaul;
        _components[slot,free]=new Component { Live=true,Key=beam.ModLaunchKey,Id=beam.ModComponentId,
            Observed=NetSession.NetFrame,Weapon=(byte)beam.Beam,Origin=beam.Position,Direction=beam.Velocity.LengthSquared>0.000001f ? beam.Velocity.Normalized() : beam.Direction,
            Radius=beam.CylinderRadius,Speed=beam.Speed,Reach=beam.MaxDistance,Damage=beam.Damage,
            HeadDamage=beam.HeadshotDamage,Family=family,
            Burn=beam.Afflictions.TestFlag(Affliction.Burn) && (!balancedBurn || beam.Flags.TestFlag(BeamFlags.Charged))
                ? NetBurnSource.From(beam, 300, balancedBurn) : null };
    }
    // Immutable fire and emitted component evidence; a client cannot manufacture
    // an explosion or burn merely by naming an otherwise valid shot.
    internal static bool TryEmittedFire(in ShotKey key, uint component, byte weapon,
        out FireEvent fire, bool requireVerified = true)
    {
        fire = default;
        int at = Find(key);
        if (!Active || at < 0 || !_shots[key.ShooterSlot, at].Fired
            || !FindComponent(key, component, out var emitted) || emitted.Weapon != weapon
            || requireVerified && EmissionReason(key) != ShotProofReason.Verified) return false;
        fire = _shots[key.ShooterSlot, at].Fire;
        return true;
    }

    internal static bool TryBurnSource(int slot, in HitClaimPacket claim, out NetBurnSource source)
    {
        source = default;
        var key = Key(slot, claim);
        if (!TryEmittedFire(key, claim.ComponentId, claim.Beam, out var fire)
            || !FindComponent(key, claim.ComponentId, out var component) || component.Burn is not { } burn
            || burn.Balanced && ((claim.Flags & HitClaimPacket.FlagDirect) == 0 || fire.Charge == 0)) return false;
        source = burn; return true;
    }

    private static bool FindComponent(in ShotKey key, uint id, out Component component)
    {
        component = default;
        if ((uint)key.ShooterSlot >= 8 || id == 0) return false;
        for (int i = 0; i < ComponentsPerSlot; i++)
        {
            ref var candidate = ref _components[key.ShooterSlot, i];
            if (candidate.Live && candidate.Key == key && candidate.Id == id && !Expired(candidate.Observed))
            { component = candidate; return true; }
        }
        return false;
    }
    // Old-life acknowledgements need exact physical identity, not a new damage
    // admission using the shooter's present-life input or inventory.
    internal static bool TryPhysicalOutcome(int slot, in HitClaimPacket claim, out CombatAckEntry outcome)
    {
        outcome = default;
        if (!FindComponent(Key(slot, claim), claim.ComponentId, out var component)
            || component.Weapon != claim.Beam) return false;
        int at = FindPayment(PaymentKey(slot, claim));
        if (at < 0 || !_payments[slot, at].Physical) return false;
        outcome = _payments[slot, at].Outcome;
        return true;
    }

    private static int FindPayment(in ShotPaymentKey key)
    {
        if((uint)key.Shot.ShooterSlot>=8)return -1;
        for(int i=0;i<PaymentsPerSlot;i++)
            if(_payments[key.Shot.ShooterSlot,i].Live && _payments[key.Shot.ShooterSlot,i].Key==key
                && !Expired(_payments[key.Shot.ShooterSlot,i].Frame))return i;
        return -1;
    }
    private static int PaymentCell(in ShotPaymentKey key)
    {
        int found=FindPayment(key);if(found>=0)return found;
        for(int i=0;i<PaymentsPerSlot;i++)
        {
            ref var p=ref _payments[key.Shot.ShooterSlot,i];
            if(!p.Live || Expired(p.Frame) || p.Key.Shot.Generation!=key.Shot.Generation
                || !NetSession.MatchesStream(p.Key.Shot.MatchId,p.Key.Shot.AuthorityEpoch))
            {p=new Payment {Live=true,Key=key,Frame=NetSession.NetFrame};return i;}
        }
        CapacityRefused++;return -1;
    }
    internal static void Physical(PlayerEntity victim, BeamProjectileEntity beam, uint damage, DamageFlags flags, Vector3 impulse, int healthBefore)
    {
        if(!Active || NetDamage.ApplyingClaim || beam.ModComponentId==0 || damage==0
            || (uint)beam.ModLaunchKey.ShooterSlot>=8 || (uint)victim.SlotIndex>=8)return;
        byte kind=(byte)((beam.EnhancedDirectHit?1:2)|(flags.TestFlag(DamageFlags.Halfturret)?4:0));
        var key=new ShotPaymentKey(beam.ModLaunchKey,(byte)victim.SlotIndex,NetPlayerLifecycle.Generation(victim.SlotIndex),
            NetPlayerLifecycle.Get(victim.SlotIndex),beam.ModComponentId,kind);
        int at=PaymentCell(key);if(at<0)return;
        ref var p=ref _payments[key.Shot.ShooterSlot,at];
        if(p.Physical){PhysicalDuplicates++;return;}
        p.Physical=true;p.Damage=(int)damage;p.Head=flags.TestFlag(DamageFlags.Headshot);
        p.Effects=flags.TestFlag(DamageFlags.Halfturret)?(byte)0:NetHitClaims.AfflictionClaimFlags(beam.Afflictions);p.Impulse=impulse;
        p.Outcome = NetHitClaims.CaptureOutcome(victim, Math.Max(0, healthBefore-victim.Health),
            (flags.TestFlag(DamageFlags.Headshot) ? CombatAckFlags.Headshot : 0)
            | (flags.TestFlag(DamageFlags.Halfturret) ? CombatAckFlags.HalfturretAffected : 0));
        p.Outcome.ShotId = beam.ModShotId;
    }
    internal static bool MarkClaim(int slot,in HitClaimPacket claim)
    {
        if(!Active || claim.ComponentId==0 || (uint)slot>=8)return false;
        var key=PaymentKey(slot,claim);int at=PaymentCell(key);if(at<0)return false;
        ref var p=ref _payments[slot,at];if(p.Claimed)return false;p.Claimed=true;return true;
    }
    internal static bool WasClaimed(int slot,in HitClaimPacket claim)
    {int at=FindPayment(PaymentKey(slot,claim));return at>=0 && _payments[slot,at].Claimed;}
    public static ShotProofDecision Evaluate(int slot,in HitClaimPacket claim,bool final=false)
    {
        ShotProofFamily family=claim.Beam==HitClaimPacket.NoBeam?ShotProofFamily.BombOrMelee
            :(claim.Flags&HitClaimPacket.FlagContinuousTick)!=0?ShotProofFamily.Continuous:ShotProofFamily.Projectile;
        ShotProofDecision Result(ShotProofStatus status,ShotProofReason reason,ShotProofReason source=ShotProofReason.Unexamined,
            ShotProofReason geometry=ShotProofReason.Unexamined,bool physical=false)=>new(status,reason,family,source,geometry,physical);
        if((uint)slot>=8 || claim.VictimSlot>=8 || !NetSession.MatchesStream(claim.MatchId,claim.AuthorityEpoch)
            || claim.ShooterGeneration!=NetPlayerLifecycle.Generation(slot) || claim.ShooterLifeId==0
            || !NetPlayerLifecycle.Matches(claim.VictimSlot,claim.VictimGeneration,claim.VictimLifeId))
            return Result(ShotProofStatus.Rejected,ShotProofReason.WrongLife);
        if(claim.AckFrame==0 || claim.AckFrame>NetSession.NetFrame || NetSession.NetFrame-claim.AckFrame>NetUnlagged.HistoryFrames-4)
            return Result(ShotProofStatus.Rejected,ShotProofReason.StaleAck);
        if(claim.Beam==HitClaimPacket.NoBeam)return Result(ShotProofStatus.Unavailable,ShotProofReason.UnsupportedBombOrMelee);
        if(family==ShotProofFamily.Continuous)return Result(ShotProofStatus.Unavailable,ShotProofReason.UnsupportedContinuous);
        var key=Key(slot,claim);
        bool found=FindComponent(key,claim.ComponentId,out var component);
        if(found)
        {
            // Family comes from the emitted component. Client flags cannot turn
            // a continuous/turret result into an ordinary projectile or splash.
            family=component.Family;
            if(component.Weapon!=claim.Beam)return Result(ShotProofStatus.Rejected,ShotProofReason.WeaponMismatch);
            if(family==ShotProofFamily.Continuous)return Result(ShotProofStatus.Unavailable,ShotProofReason.UnsupportedContinuous);
            if(family==ShotProofFamily.Turret)return Result(ShotProofStatus.Unavailable,ShotProofReason.UnsupportedTurret);
        }
        int at=Find(key);
        if(at<0)return Result(final?ShotProofStatus.Rejected:ShotProofStatus.Unavailable,ShotProofReason.UnknownShot);
        ref var shot=ref _shots[slot,at];
        if(shot.Fire.Weapon!=claim.Beam)return Result(ShotProofStatus.Rejected,ShotProofReason.WeaponMismatch);
        if(claim.LaunchFrame!=shot.Fire.AckFrame || claim.LaunchFrame>claim.AckFrame)
            return Result(ShotProofStatus.Rejected,ShotProofReason.StaleAck);
        if(!shot.Fired)return Result(ShotProofStatus.Unavailable,ShotProofReason.ShotNotFired);
        if(WasClaimed(slot,claim))return Result(ShotProofStatus.Rejected,ShotProofReason.DuplicateComponent);
        if(!found)return Result(ShotProofStatus.Unavailable,ShotProofReason.UnknownComponent);
        family=(claim.Flags&HitClaimPacket.FlagDirect)==0?ShotProofFamily.Splash:component.Family;
        ShotProofReason source=SourceReason(key),geometry=shot.Pose;
        bool physical=false;
        int paid=FindPayment(PaymentKey(slot,claim));
        if(paid>=0 && _payments[slot,paid].Physical)
        {
            var p=_payments[slot,paid];physical=true;
            byte effects=(byte)(claim.Flags & (HitClaimPacket.FlagFrozen|HitClaimPacket.FlagBurning|HitClaimPacket.FlagDisrupted));
            geometry=effects!=p.Effects || (claim.Direction-p.Impulse).LengthSquared>0.000001f
                ? ShotProofReason.ClaimEffectsMismatch : claim.Damage>p.Damage?ShotProofReason.DamageMismatch
                :(claim.Flags&HitClaimPacket.FlagHeadshot)!=0 && !p.Head?ShotProofReason.WrongHeadshot:ShotProofReason.Verified;
        }
        else if(geometry==ShotProofReason.Verified)
        {
            if(family==ShotProofFamily.Straight)geometry=Trace(slot,claim,shot.Fire,component);
            else geometry=family==ShotProofFamily.Turret?ShotProofReason.UnsupportedTurret:ShotProofReason.PhysicalWitnessRequired;
        }
        // A physical collision cannot legitimize a forged muzzle or invalid source context.
        if(shot.Pose != ShotProofReason.Verified) geometry=shot.Pose;
        if(IsReject(source))return Result(ShotProofStatus.Rejected,source,source,geometry,physical);
        if(IsReject(geometry))return Result(ShotProofStatus.Rejected,geometry,source,geometry,physical);
        if(source!=ShotProofReason.Verified)return Result(ShotProofStatus.Unavailable,source,source,geometry,physical);
        if(geometry!=ShotProofReason.Verified)return Result(ShotProofStatus.Unavailable,geometry,source,geometry,physical);
        return Result(ShotProofStatus.Verified,ShotProofReason.Verified,source,geometry,physical);
    }
    private static bool IsReject(ShotProofReason reason)=>reason is ShotProofReason.UnownedWeapon or ShotProofReason.InvalidCharge
        or ShotProofReason.FireRate or ShotProofReason.Ammunition or ShotProofReason.ModifierMismatch or ShotProofReason.InvalidRay
        or ShotProofReason.MuzzleDistance or ShotProofReason.BlockedMuzzle or ShotProofReason.MissedBody
        or ShotProofReason.BlockedWorld or ShotProofReason.WrongHeadshot or ShotProofReason.Reach or ShotProofReason.DamageMismatch
        or ShotProofReason.ClaimEffectsMismatch or ShotProofReason.SourceClock;
    private static ShotProofReason Trace(int slot,in HitClaimPacket claim,in FireEvent fire,in Component component)
    {
        if(!Finite(component.Origin)||!Finite(component.Direction)||component.Direction.LengthSquared<.99f)return ShotProofReason.InvalidRay;
        if(claim.VictimSlot>=PlayerEntity.Players.Count)return ShotProofReason.HistoryUnavailable;
        var victim=PlayerEntity.Players[claim.VictimSlot];var scene=victim.OwningScene;
        double frame=fire.AckFrame+fire.AckSubFrame/256.0;
        if(!NetUnlagged.TryHistoricalPose(victim,frame,out var pose))return ShotProofReason.HistoryUnavailable;
        var body=NetHistoricalTrace.Body(victim,pose);
        float reach=component.Reach>0?Math.Min(component.Reach,component.Speed):component.Speed;
        if(!float.IsFinite(reach)||reach<=0)return ShotProofReason.Reach;
        Vector3 end=component.Origin+component.Direction.Normalized()*reach;
        if(!NetHistoricalTrace.Intersect(body,component.Origin,end,component.Radius,out var hit))return ShotProofReason.MissedBody;
        if(!NetDynamicGeometryHistory.TryTraceDistanceAtFrame(scene,component.Origin,end,frame,out float wall))return ShotProofReason.HistoryUnavailable;
        if(wall<=hit.Distance)return ShotProofReason.BlockedWorld;
        // Do not reinterpret HitPoint (body origin) or Direction (knockback) as a ray.
        bool head=body.Type==HistoricalBodyType.BipedCylinder && hit.Position.Y-body.Position.Y>=body.Top-.3f;
        if((claim.Flags&HitClaimPacket.FlagHeadshot)!=0 && !head)return ShotProofReason.WrongHeadshot;
        // Damage/effect rescue still needs an independently resolved result. A ray alone
        // cannot certify current armor, susceptibility or pickup modifiers.
        return ShotProofReason.PhysicalWitnessRequired;
    }
    internal static ShotProofDecision Observe(int slot,in HitClaimPacket claim,byte legacy,string stage,bool final=false)
    {
        if(!Active)return default;
        var d=Evaluate(slot,claim,final);_reasons[(int)d.Reason]++;
        if(d.Status==ShotProofStatus.Verified)Verified++;
        else if(d.Status==ShotProofStatus.Rejected){Rejected++;if(legacy==HitVerdictPacket.ResultApplied)LegacyAllowedRejected++;}
        else {Unavailable++;if(legacy==HitVerdictPacket.ResultApplied)LegacyAllowedUnavailable++;}
        float offset=-1;
        if(NetUnlagged.PositionAt(claim.VictimSlot,claim.AckFrame,claim.VictimGeneration,claim.VictimLifeId,out var body))
        {
            offset=(claim.HitPoint-body).Length;
            if(float.IsFinite(offset)){RadiusSamples++;RadiusSum+=offset;RadiusMax=Math.Max(RadiusMax,offset);
                _radiusBuckets[Math.Min(8,(int)(offset*4))]++;}
        }
        if(NetCampaignEvidence.Enabled)
        {
            object? witness = null;
            int paid = FindPayment(PaymentKey(slot, claim));
            if (paid >= 0 && _payments[slot, paid].Physical)
            {
                var p = _payments[slot, paid];
                witness = new { p.Damage, p.Head, p.Effects, ImpulseX=p.Impulse.X, ImpulseY=p.Impulse.Y, ImpulseZ=p.Impulse.Z };
            }
            if(_evidence.Count<16384)_evidence.Add(new { Stage=stage,Key=Key(slot,claim),claim.ClaimId,claim.ComponentId,
                claim.VictimSlot,claim.VictimGeneration,claim.VictimLifeId,claim.Beam,claim.Flags,claim.Damage,
                claim.AckFrame,claim.LaunchFrame,AuthorityFrame=NetSession.NetFrame,
                Legacy=legacy,Status=d.Status.ToString(),Reason=d.Reason.ToString(),Family=d.Family.ToString(),
                Source=d.Source.ToString(),Geometry=d.Geometry.ToString(),d.PhysicalMatch,BodyOffset=offset,
                ClaimImpulseX=claim.Direction.X,ClaimImpulseY=claim.Direction.Y,ClaimImpulseZ=claim.Direction.Z,Witness=witness });
            else EvidenceOverflow++;
        }
        return d;
    }
    internal static object Capture()=>new {Mode=Mode.ToString(),Verified,Rejected,Unavailable,LegacyAllowedRejected,
        LegacyAllowedUnavailable,CapacityRefused,EvidenceOverflow,PhysicalDuplicates,RadiusSamples,RadiusSum,RadiusMax,
        RadiusBuckets=_radiusBuckets,Reasons=_reasons,ReasonNames=Enum.GetNames<ShotProofReason>(),Rows=_evidence,
        EmissionVerified,EmissionRejected,EmissionUnavailable,EmissionReasons=_emissionReasons,EmissionRows=_emissionEvidence,
        SourceJournal=NetShotSourceJournal.Capture()};
    public static void Reset()
    {
        Array.Clear(_clocks);Array.Clear(_shots);NetShotSourceJournal.Reset();NetShotInputProof.Reset();Array.Clear(_poses);Array.Clear(_components);Array.Clear(_payments);
        // Campaign counters survive match teardown; start a fresh process for a new evidence run.
    }
}
