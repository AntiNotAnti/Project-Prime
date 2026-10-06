using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Bounded authority evidence for owner-authored attacks. Admission does
/// not require the authority to have spawned or collided with the shot: delayed
/// pre-death events can still rescue a valid hit. Native launches and rescue share
/// one resource reservation, so neither packet retries nor the two paths pay twice.</summary>
internal static class NetAcceptedAttacks
{
    internal const int Capacity = 1024, LifetimeFrames = 512;
    private struct Attack
    {
        public bool Valid, Paid, Launched, EmitAttempted, DoubleDamage, Prime, Charged;
        public FireEvent Fire;
        public WeaponInfo Weapon;
        public int Cost, Pool;
        public uint Arrived;
        public float Charge;
        public int CoilRamp, MicroCount;
        public bool RequestedDouble;
        public NetTargetIdentity Target;
    }
    private sealed class Slot
    {
        public ShotKey Fence;
        public readonly Attack[] Attacks = new Attack[Capacity];
        public readonly Dictionary<uint, int> Index = new(Capacity);
        public uint CoilStart;
        public bool CoilHeld;
        public readonly uint[] BurnFrames = new uint[8];
        public readonly NativeBomb[] NativeBombs = new NativeBomb[32];
        public readonly BombSample[,] BombHistory = new BombSample[128, 3];
        public readonly Attack[] Deferred = new Attack[NetFireEvents.Capacity];
        public readonly int[] Emissions = new int[NetFireEvents.Capacity];
        public int EmissionCount;
        public readonly Powerup[] Powerups = new Powerup[128];
        public readonly SourceBody[] SourceBodies = new SourceBody[128];
        public int Cursor, Charge, Weapon = -1;
        public uint LastId, LastFire, LastTurretFire, LastSource, LastArrival, SourceOrigin, ArrivalOrigin;
        public bool SeenId, SeenFire, SeenTurretFire, SeenIntent, Holding;
        public readonly int[] Reserved = new int[3];
        public readonly ushort[,] Outcomes = new ushort[Capacity, 8];
        public readonly uint[] Components = new uint[Capacity * 8 * 16];
        public readonly IntentPacket[] AltIntents = new IntentPacket[128];
        public readonly uint[] AltArrivals = new uint[128];
        public readonly uint[] AltGroups = new uint[128], PaidGroups = new uint[128];
        public readonly ushort[] AltPaid = new ushort[128];
        public readonly bool[] BombEmitted = new bool[128];
        public readonly uint[,] BombLineFrames = new uint[32, 8];
        public readonly HistoricalAltAttackState[] AltContacts = new HistoricalAltAttackState[128];
        public readonly bool[] BombPaid = new bool[128], GroupBomb = new bool[128], AltDouble = new bool[128];
        public readonly uint[] GroupArrived = new uint[128];
        public uint AltStart, LastBombSource;
        public bool AltHolding, BoostHolding;
        public int ReservedBombs;
    }
    private struct NativeBomb
    {
        public int Id;
        public uint Born, Seen, SourceFrame;
        public byte Paid;
    }
    private readonly record struct BombSample(int Id, uint Born, uint Frame, uint TravelFrames, Vector3 Position, float Radius, int Damage, int Index, bool Connections);
    private readonly record struct Powerup(uint Frame, bool DoubleDamage, bool Prime);
    private readonly record struct SourceBody(uint Frame, Vector3 Position, Vector3 Up, byte Flags);
    private static readonly Slot[] Slots = Create();
    public static long Accepted, Refused, ResourceRefused, ClaimsWithoutEvidence, GeometryRefused;
    internal static Vector3 LastResolvedImpact { get; private set; }
    internal static Vector3 LastResolvedDirection { get; private set; }
    private static Slot[] Create()
    { var slots = new Slot[8]; for (int i = 0; i < slots.Length; i++) slots[i] = new(); return slots; }
    private static Slot For(int slot)
    {
        Slot state = Slots[slot];
        ShotKey fence = ShotKey.For(slot, 0);
        if (state.Fence != fence)
        {
            state.Fence = fence; state.Index.Clear(); state.CoilHeld = false;
            Array.Clear(state.BurnFrames); Array.Clear(state.NativeBombs); Array.Clear(state.BombHistory);
            Array.Clear(state.Attacks); Array.Clear(state.Reserved); Array.Clear(state.Outcomes); Array.Clear(state.Components);
            Array.Clear(state.Deferred); Array.Clear(state.Powerups); Array.Clear(state.SourceBodies);
            Array.Clear(state.AltIntents); Array.Clear(state.AltArrivals); Array.Clear(state.AltPaid);
            Array.Clear(state.AltGroups); Array.Clear(state.PaidGroups); Array.Clear(state.BombPaid); Array.Clear(state.GroupArrived); Array.Clear(state.GroupBomb); Array.Clear(state.AltDouble); Array.Clear(state.BombEmitted); Array.Clear(state.BombLineFrames); Array.Clear(state.AltContacts);
            state.ReservedBombs = 0; state.AltStart = state.LastBombSource = 0;
            state.AltHolding = state.BoostHolding = false;
            state.SeenId = state.SeenFire = state.SeenTurretFire = state.SeenIntent = state.Holding = false;
            state.Cursor = state.Charge = 0; state.Weapon = -1;
            state.EmissionCount = 0;
            state.LastId = state.LastFire = state.LastTurretFire = state.LastSource = state.LastArrival = 0;
        }
        return state;
    }
    internal static void Reset()
    {
        NetAttackPaths.Reset();
        foreach (Slot state in Slots)
        {
            state.Fence = default; state.Index.Clear(); state.CoilHeld = false;
            Array.Clear(state.BurnFrames); Array.Clear(state.NativeBombs); Array.Clear(state.BombHistory);
            Array.Clear(state.Attacks); Array.Clear(state.Deferred);
            Array.Clear(state.Reserved); Array.Clear(state.Outcomes); Array.Clear(state.Components); Array.Clear(state.Powerups); Array.Clear(state.SourceBodies);
            Array.Clear(state.AltIntents); Array.Clear(state.AltArrivals); Array.Clear(state.AltPaid);
            Array.Clear(state.AltGroups); Array.Clear(state.PaidGroups); Array.Clear(state.BombPaid); Array.Clear(state.GroupArrived); Array.Clear(state.GroupBomb); Array.Clear(state.AltDouble); Array.Clear(state.BombEmitted); Array.Clear(state.BombLineFrames); Array.Clear(state.AltContacts);
            state.SeenIntent = state.SeenId = state.SeenFire = state.SeenTurretFire = state.Holding = false;
            state.ReservedBombs = 0; state.AltHolding = state.BoostHolding = false;
            state.EmissionCount = 0;
        }
        Accepted = Refused = ResourceRefused = ClaimsWithoutEvidence = GeometryRefused = 0;
    }
    internal static int AllowedCharge(int slot) => (uint)slot < 8 ? For(slot).Charge : 0;
    private static float ChargePercent(WeaponInfo weapon, int charge)
    {
        if (!weapon.Flags.TestFlag(WeaponFlags.CanCharge)) return 0;
        if (!weapon.Flags.TestFlag(WeaponFlags.PartialCharge)) return charge >= weapon.FullCharge * 2 ? 1 : 0;
        if (charge < weapon.MinCharge * 2) return 0;
        return Math.Clamp((charge - weapon.MinCharge * 2f) /
            Math.Max(1, weapon.FullCharge * 2f - weapon.MinCharge * 2f), 0, 1);
    }
    private static int Amount(int uncharged, int minimum, int charged, float pct)
        => pct <= 0 ? uncharged : (int)(minimum + (charged - minimum) * pct);
    private static WeaponInfo Weapon(PlayerEntity player, BeamType beam, bool turret)
    {
        if (turret) return player.ModTurretAttackWeapon;
        bool affinity = beam == Weapons.GetAffinityBeam(player.Hunter)
            && !(player.OwningScene.GameState.BalancedMode && player.Hunter == Hunter.Weavel
                && beam == BeamType.Battlehammer);
        bool prime = player.OwningScene.GameState.PrimeHunter == player.SlotIndex;
        return player.OwningScene.WeaponRules[(int)beam + (affinity || prime ? 9 : 0)];
    }
    internal static void AcceptIntent(int slot, in IntentPacket intent)
    {
        if (!NetSession.IsAuthority || (uint)slot >= 8 || slot >= PlayerEntity.Players.Count) return;
        PlayerEntity player = PlayerEntity.Players[slot]; Slot state = For(slot);
        uint now = NetSession.NetFrame;
        if (!state.SeenIntent) { state.SourceOrigin = intent.Frame; state.ArrivalOrigin = now; }
        uint sourceAdvance = state.SeenIntent ? unchecked(intent.Frame - state.LastSource) : 0;
        uint arrivalAdvance = state.SeenIntent ? unchecked(now - state.LastArrival) : 0;
        int elapsed = (int)Math.Min(sourceAdvance, Math.Min(arrivalAdvance + NetFireEvents.RetentionFrames, 255));
        int charge = state.Weapon == intent.WeaponSelect && state.Holding ? state.Charge + elapsed : elapsed;
        charge = Math.Clamp(charge, 0, 255);
        // Source counters are independent clocks, but cannot accelerate authority
        // cadence indefinitely. Allow the existing recovery history as bounded slack.
        bool coil = intent.WeaponSelect == (byte)BeamType.ShockCoil && intent.Buttons.HasFlag(IntentButtons.Shoot);
        if (coil && !state.CoilHeld) state.CoilStart = intent.Frame;
        state.CoilHeld = coil;
        bool clock = unchecked(intent.Frame - state.SourceOrigin)
            <= unchecked(now - state.ArrivalOrigin) + NetFireEvents.RetentionFrames;
        for (int i = 0; i < intent.FireEventCount; i++)
        {
            FireEvent fire = intent.FireEvents[i];
            if (state.SeenId && !NetLifecycleTracker.Newer(fire.ShotId, state.LastId)) continue;
            state.LastId = fire.ShotId; state.SeenId = true;
            BeamType beam = (BeamType)fire.Weapon;
            bool turret = fire.Kind == FireEventKind.TurretFire;
            if (!clock || !fire.HasPose
                || !LagCompensationPolicy.TryAdmitTime(now, fire.AckFrame, fire.AckSubFrame, out _)
                || !SourcePoseSupported(player, state, fire)
                || !WeaponResourceRules.AllowsBeam(beam, player.OwningScene.GameState.InstaGib,
                    player.OwningScene.GameState.NoImperialist)
                || turret && player.Hunter != Hunter.Weavel)
            { Refused++; continue; }
            WeaponInfo weapon = Weapon(player, beam, turret);
            // Older same-protocol clients stamped their selected human weapon on
            // turret events. The native turret owns its own weapon/charge state.
            if (turret) { beam = weapon.Beam; fire = fire with { Weapon = (byte)beam, Charge = 0 }; }
            float pct = ChargePercent(weapon, fire.Charge);
            bool charged = weapon.Flags.TestFlag(WeaponFlags.CanCharge)
                && fire.Charge >= (weapon.Flags.TestFlag(WeaponFlags.PartialCharge) ? weapon.MinCharge : weapon.FullCharge) * 2;
            int sourceCharge = Math.Max(0, charge - (int)unchecked(intent.Frame - fire.SourceFrame));
            int cooldown = weapon.Flags.TestFlag(WeaponFlags.Continuous) ? 1 : weapon.ShotCooldown * 2;
            if (turret) cooldown = Math.Max(1, (int)MathF.Floor(weapon.ShotCooldown * 1.4f)); // Native damaged-turret minimum factor is .7.
            if (fire.Kind == FireEventKind.AutomaticFire) cooldown = Math.Max(cooldown, weapon.AutofireCooldown * 2);
            bool seenFire = turret ? state.SeenTurretFire : state.SeenFire;
            uint lastFire = turret ? state.LastTurretFire : state.LastFire;
            if (charged && (!player.ModOwnsAttackCharge(beam) || fire.Charge > sourceCharge)
                || fire.Kind == FireEventKind.ReleaseFire && !charged
                || fire.Kind == FireEventKind.AutomaticFire && (charged || !weapon.Flags.TestFlag(WeaponFlags.RepeatFire))
                || seenFire && (!NetLifecycleTracker.Newer(fire.SourceFrame, lastFire)
                    || unchecked(fire.SourceFrame - lastFire) < cooldown)
                || fire.Kind == FireEventKind.ContinuousTick != weapon.Flags.TestFlag(WeaponFlags.Continuous)
                || fire.Kind == FireEventKind.ContinuousTick && fire.ContinuousPhase == 0)
            { Refused++; continue; }
            int cost = Amount(weapon.AmmoCost, weapon.MinChargeCost, weapon.ChargeCost, pct);
            if (weapon.Flags.TestFlag(WeaponFlags.Continuous)) cost = ContinuousWeaponPhase.Amount(cost, fire.ContinuousPhase, false);
            if (player.EquipInfo.InfiniteAmmo || turret) cost = 0;
            int pool = player.ModAttackAmmoPool(beam, weapon);
            Powerup historical = state.Powerups[fire.AckFrame % 128];
            var candidate = new Attack { Valid = true, Fire = fire, Weapon = weapon,
                Cost = cost, Pool = pool, Arrived = now, Charge = pct, Charged = charged,
                CoilRamp = fire.Kind == FireEventKind.ContinuousTick
                    ? Math.Min(4, Math.Max(player.ShockCoilTimer / 60,
                        NetLifecycleTracker.Newer(fire.SourceFrame, state.CoilStart)
                            ? (int)(unchecked(fire.SourceFrame - state.CoilStart) / 60) : 0)) : 0,
                MicroCount = Mods.EnhancedHunters.EnhancedHunters.UsingAffinity(player, beam)
                    && player.Hunter == Hunter.Samus && pct >= 1 ? Math.Min(3, (int)player.EnhancedState.ValueA) : 0,
                RequestedDouble = (intent.ShotFlags & IntentPacket.FlagDoubleDamage) != 0,
                Target = fire.SourceFrame == intent.Frame ? intent.Target
                    : state.AltIntents[fire.SourceFrame % 128].Frame == fire.SourceFrame
                        ? state.AltIntents[fire.SourceFrame % 128].Target : default,
                DoubleDamage = historical.Frame == fire.AckFrame ? historical.DoubleDamage : player.DoubleDamage,
                Prime = historical.Frame == fire.AckFrame ? historical.Prime : player.OwningScene.GameState.PrimeHunter == slot };
            if (!Admit(player, state, candidate))
            {
                // Owner position is applied on the simulation step. A legitimate
                // pickup at that position may therefore become authority-owned just
                // after this packet. Preserve the validated event, never grant from
                // owner ammo/weapon bits, and retry after native pickups run.
                int deferred = (int)(fire.ShotId % NetFireEvents.Capacity);
                if (!state.Deferred[deferred].Valid
                    || now - state.Deferred[deferred].Arrived > NetFireEvents.RetentionFrames)
                    state.Deferred[deferred] = candidate;
                Refused++; ResourceRefused++;
            }
            if (turret) { state.LastTurretFire = fire.SourceFrame; state.SeenTurretFire = true; }
            else { state.LastFire = fire.SourceFrame; state.SeenFire = true; if (charged) charge = 0; }
        }
        state.Charge = intent.WeaponSelect == state.Weapon && state.Holding ? charge : Math.Min(charge, elapsed);
        state.Holding = intent.Buttons.HasFlag(IntentButtons.Shoot);
        if (!state.Holding) state.Charge = 0;
        state.Weapon = intent.WeaponSelect; state.LastSource = intent.Frame; state.LastArrival = now; state.SeenIntent = true;
        if (!clock || !LagCompensationPolicy.TryAdmitTime(now, intent.AckFrame, intent.AckSubFrame, out _)) return;
        bool alt = intent.Buttons.HasFlag(IntentButtons.AltFormState);
        bool altHeld = alt && intent.Buttons.HasFlag(IntentButtons.AltAttack);
        bool boost = alt && player.Hunter == Hunter.Samus
            && (intent.ShotFlags & IntentPacket.FlagBoosting) != 0 && intent.BoostDamage > 0;
        uint edgeFrame = 0;
        for (int edge = 0; edge < InputEdgeHistory.Capacity; edge++)
        {
            ushort entry = intent.Presses[edge];
            if (InputEdgeHistory.Action(entry) != 10) continue; // AltAttack bit9, encoded as action+1.
            uint source = unchecked(intent.Frame - (uint)InputEdgeHistory.Age(entry));
            if (edgeFrame == 0 || NetLifecycleTracker.Newer(source, edgeFrame)) edgeFrame = source;
        }
        bool recovered = edgeFrame != 0 && (state.AltStart == 0 || NetLifecycleTracker.Newer(edgeFrame, state.AltStart));
        // A recovered press carries no historical alt pose. It can rescue an
        // admitted alt phase, never arm contact from a human-form carrier.
        recovered &= alt;
        bool rising = altHeld && !state.AltHolding || boost && !state.BoostHolding || recovered;
        if (rising) state.AltStart = recovered ? edgeFrame : intent.Frame;
        uint group = state.AltStart;
        bool bomber = player.Hunter is Hunter.Samus or Hunter.Kanden or Hunter.Sylux;
        if (bomber && !boost)
        {
            int cooldown = unchecked((ushort)(player.Values.BombCooldown * 2));
            if (player.OwningScene.GameState.BalancedMode && player.Hunter == Hunter.Kanden)
                cooldown = BalancedHunterAbilityRules.KandenCooldownFrames(cooldown);
            if (!rising || !player.ModCanAuthorBomb || player.ModBombAmmo <= state.ReservedBombs && !(player.Hunter == Hunter.Sylux && player.SyluxBombCount >= 3)
                || state.LastBombSource != 0 && unchecked(intent.Frame - state.LastBombSource) < cooldown)
                group = 0;
            else { state.LastBombSource = group; if (!(player.Hunter == Hunter.Sylux && player.SyluxBombCount >= 3)) state.ReservedBombs++; }
        }
        if (!alt || !altHeld && !boost && !recovered) group = 0;
        if (player.Hunter == Hunter.Noxus && unchecked(intent.Frame - state.AltStart) < (player.OwningScene.GameState.BalancedMode
            ? BalancedHunterAbilityRules.NoxusStartupFrames(player.Values.AltAttackStartup * 2) : player.Values.AltAttackStartup * 2))
            group = 0;
        if (player.Hunter == Hunter.Spire && altHeld)
            group = 1 + intent.Frame / (uint)Math.Max(1, player.Values.DamageInvuln * 2);
        state.AltHolding = altHeld; state.BoostHolding = boost;
        int at = (int)(intent.Frame % 128);
        state.AltIntents[at] = intent; state.AltArrivals[at] = now; state.AltGroups[at] = group;
        Powerup altPower = state.Powerups[intent.AckFrame % 128];
        state.AltDouble[at] = altPower.Frame == intent.AckFrame ? altPower.DoubleDamage : player.DoubleDamage;
        var contact = player.ModCaptureContactState();
        var ownVolume = PlayerEntity.PlayerVolumes[(int)player.Hunter, 2];
        Vector3 center = intent.Position + ownVolume.SpherePosition;
        ContactAttackKind kind = player.Hunter switch
        {
            Hunter.Samus when boost => ContactAttackKind.Boost, Hunter.Spire => ContactAttackKind.Spire,
            Hunter.Noxus => ContactAttackKind.Noxus, Hunter.Trace => ContactAttackKind.Trace,
            Hunter.Weavel => ContactAttackKind.Weavel, _ => ContactAttackKind.None
        };
        Vector3 translation = center - contact.Center;
        var acceptedContact = contact with { Kind = kind, AltForm = true, Center = center,
            PreviousCenter = center, Radius = ownVolume.SphereRadius,
            LeftRock = contact.LeftRock + translation, RightRock = contact.RightRock + translation,
            InPlay = group != 0 };
        var priorContact = state.AltContacts[(intent.Frame - 1) % 128];
        bool consecutive = state.AltIntents[(intent.Frame - 1) % 128].Frame == intent.Frame - 1;
        state.AltContacts[at] = NetContactLagComp.WithSweep(acceptedContact, priorContact, consecutive);
        if (group != 0)
        {
            int paidAt = (int)(group % 128);
            if (state.PaidGroups[paidAt] != group)
            {
                state.PaidGroups[paidAt] = group; state.AltPaid[paidAt] = 0; state.BombPaid[paidAt] = false;
                state.BombEmitted[paidAt] = false;
                state.GroupArrived[paidAt] = now; state.GroupBomb[paidAt] = bomber && !boost;
            }
        }
    }
    private static bool SourcePoseSupported(PlayerEntity player, Slot state, in FireEvent fire)
    {
        if (!player.ModSupportsFireSource(fire, out Vector3 sightOrigin)) return false;
        if (fire.Kind != FireEventKind.TurretFire)
        {
            SourceBody body = state.SourceBodies[fire.SourceFrame % 128];
            if (body.Frame == fire.SourceFrame
                && ((body.Position - fire.SourcePosition).LengthSquared > .0001f
                    || (body.Up - fire.SourceUp).LengthSquared > .0001f
                    || body.Flags != fire.SourceFlags)) return false;
            // Firing occurs after native movement, while the carrier body is
            // sampled before movement. Admit this separate mid-step movement
            // report once; later carriers/events cannot rewrite its source body.
            // This is muzzle coupling, not server speed/teleport authority.
        }
        bool clear = NetDynamicGeometryHistory.TryTraceDistanceAtFrame(player.OwningScene,
            sightOrigin, fire.Origin, fire.AckFrame + fire.AckSubFrame / 256.0, out float world)
            && world >= .999f;
        if (clear && fire.Kind != FireEventKind.TurretFire)
            state.SourceBodies[fire.SourceFrame % 128] = new(fire.SourceFrame, fire.SourcePosition,
                fire.SourceUp, fire.SourceFlags);
        return clear;
    }
    private static bool Admit(PlayerEntity player, Slot state, in Attack candidate)
    {
        if (state.EmissionCount >= state.Emissions.Length) return false;
        bool turret = candidate.Fire.Kind == FireEventKind.TurretFire;
        if ((!turret && !player.ModOwnsAttackWeapon((BeamType)candidate.Fire.Weapon))
            || candidate.Cost > player.ModAttackAmmo((BeamType)candidate.Fire.Weapon, candidate.Weapon)
                - state.Reserved[candidate.Pool]) return false;
        ref Attack replace = ref state.Attacks[state.Cursor];
        if (replace.Valid && unchecked(NetSession.NetFrame - replace.Arrived) <= LifetimeFrames) return false;
        if (replace.Valid && !replace.Paid) state.Reserved[replace.Pool] -= replace.Cost;
        for (int victim = 0; victim < 8; victim++) state.Outcomes[state.Cursor, victim] = 0;
        Array.Clear(state.Components, state.Cursor * 8 * 16, 8 * 16);
        if (replace.Valid) state.Index.Remove(replace.Fire.ShotId);
        replace = candidate; state.Index[candidate.Fire.ShotId] = state.Cursor;
        state.Emissions[state.EmissionCount++] = state.Cursor;
        state.Reserved[candidate.Pool] += candidate.Cost;
        state.Cursor = (state.Cursor + 1) % Capacity; Accepted++;
        return true;
    }
    internal static void AfterNativePickups(uint frame)
    {
        if (!NetSession.IsAuthority) return;
        for (int slot = 0; slot < PlayerEntity.Players.Count && slot < 8; slot++)
        {
            PlayerEntity player = PlayerEntity.Players[slot]; Slot state = For(slot);
            for (int i = 0; i < state.GroupArrived.Length; i++)
                if (state.GroupArrived[i] != 0 && unchecked(frame - state.GroupArrived[i]) > 124
                    && state.GroupBomb[i] && !state.BombPaid[i])
                { state.ReservedBombs = Math.Max(0, state.ReservedBombs - 1); state.BombPaid[i] = true; }
            for (int i = 0; i < state.Attacks.Length; i++)
            {
                ref Attack expired = ref state.Attacks[i];
                if (expired.Valid && unchecked(frame - expired.Arrived) > LifetimeFrames)
                {
                    if (!expired.Paid) state.Reserved[expired.Pool] -= expired.Cost;
                    state.Index.Remove(expired.Fire.ShotId); expired.Valid = false;
                }
                if (expired.Valid && !expired.Launched && !expired.EmitAttempted
                    && !LagCompensationPolicy.TryAdmitTime(frame, expired.Fire.AckFrame, expired.Fire.AckSubFrame, out _))
                {
                    if (!expired.Paid) state.Reserved[expired.Pool] -= expired.Cost;
                    state.Index.Remove(expired.Fire.ShotId); expired.Valid = false;
                }
                if (expired.Valid && unchecked(frame - expired.Arrived) <= 1
                    && expired.RequestedDouble && player.DoubleDamage) expired.DoubleDamage = true;
            }
            int bombs = 0;
            int historyAt = (int)(frame % 128);
            for (int i = 0; i < 3; i++) state.BombHistory[historyAt, i] = default;
            foreach (BombEntity bomb in player.OwningScene.GetBombEntities())
            {
                if (bomb.Owner != player || bombs >= 3) continue;
                for (int i = 0; i < state.NativeBombs.Length; i++)
                    if (state.NativeBombs[i].Born != 0 && state.NativeBombs[i].Id == bomb.Id)
                    {
                        state.NativeBombs[i].Seen = frame;
                        state.BombHistory[historyAt, bombs++] = new(bomb.Id, state.NativeBombs[i].Born, frame, unchecked(frame - state.NativeBombs[i].Born),
                            bomb.Position, bomb.Radius, bomb.Damage, bomb.BombIndex, bomb.ModLockjawConnectionsActive); break;
                    }
            }
            if (player.ModIsInPlay)
                state.Powerups[frame % 128] = new(frame, player.DoubleDamage,
                    player.OwningScene.GameState.PrimeHunter == slot);
            for (int i = 0; i < state.Deferred.Length; i++)
            {
                ref Attack candidate = ref state.Deferred[i];
                if (!candidate.Valid) continue;
                if (unchecked(frame - candidate.Arrived) > NetFireEvents.RetentionFrames
                    || !LagCompensationPolicy.TryAdmitTime(frame, candidate.Fire.AckFrame, candidate.Fire.AckSubFrame, out _))
                { candidate.Valid = false; continue; }
                if (Admit(player, state, candidate)) candidate.Valid = false;
            }
        }
    }
    private static int Find(Slot state, uint id)
        => state.Index.TryGetValue(id, out int at) && state.Attacks[at].Valid
            && unchecked(NetSession.NetFrame - state.Attacks[at].Arrived) <= LifetimeFrames ? at : -1;
    internal static bool TryAcceptedFire(int slot, uint id, out FireEvent fire)
        => TryAcceptedContext(slot, id, out fire, out _);
    internal static bool TryAcceptedContext(int slot, uint id, out FireEvent fire, out NetTargetIdentity target)
    {
        fire = default; target = default;
        if ((uint)slot >= 8) return false;
        Slot state = For(slot); int at = Find(state, id);
        if (at < 0) return false;
        fire = state.Attacks[at].Fire;
        target = state.Attacks[at].Target;
        return LagCompensationPolicy.TryAdmitTime(NetSession.NetFrame, fire.AckFrame, fire.AckSubFrame, out _);
    }
    internal static bool Authorized(int slot, uint id)
        => (uint)slot < 8 && id != 0 && Find(For(slot), id) >= 0;
    internal static void Launched(PlayerEntity player)
    {
        if (!player.ModAuthorityOwnsResources) return;
        Slot state = For(player.SlotIndex); int index = Find(state, NetFireEvents.ActiveShotId(player));
        if (index < 0) return;
        ref Attack attack = ref state.Attacks[index];
        attack.Launched = true;
        if (attack.Paid) return;
        attack.Paid = true; state.Reserved[attack.Pool] -= attack.Cost;
    }
    /// <summary>Admitted successful owner events can arrive after native input
    /// skipped a dead shooter's fire. Emit each unlaunched event once through
    /// native mechanics before snapshot assembly, preserving its source timing
    /// and resource ownership. This does not require an authority victim hit.</summary>
    internal static void EmitPending()
    {
        if (!NetSession.IsAuthority) return;
        for (int slot = 0; slot < PlayerEntity.Players.Count && slot < 8; slot++)
        {
            PlayerEntity player = PlayerEntity.Players[slot]; Slot state = For(slot);
            for (int i = 0; i < state.EmissionCount; i++)
            {
                int at = state.Emissions[i]; ref Attack attack = ref state.Attacks[at];
                if (!attack.Valid || attack.Launched || attack.EmitAttempted) continue;
                attack.EmitAttempted = true;
                FireEvent fire = attack.Fire; WeaponInfo weapon = attack.Weapon;
                if (!LagCompensationPolicy.TryAdmitTime(NetSession.NetFrame, fire.AckFrame, fire.AckSubFrame, out _))
                {
                    if (!attack.Paid) state.Reserved[attack.Pool] -= attack.Cost;
                    state.Index.Remove(fire.ShotId); attack.Valid = false;
                    continue;
                }
                // A turret's native owner type controls speed/affinity rules.
                EntityBase owner = fire.Kind == FireEventKind.TurretFire
                    ? player.Halfturret ?? new HalfturretEntity(player, player.OwningScene) : player;
                bool paid = attack.Paid;
                var equip = new EquipInfo(weapon, player.EquipInfo.Beams)
                {
                    ChargeLevel = fire.Charge, Zoomed = fire.ScopedAtFire,
                    InfiniteAmmo = paid || attack.Cost == 0,
                    GetAmmo = () => player.ModAttackAmmo(weapon.Beam, weapon),
                    SetAmmo = remaining => player.ModConsumeAttackAmmo(weapon.Beam, weapon,
                        Math.Max(0, player.ModAttackAmmo(weapon.Beam, weapon) - remaining))
                };
                var previousWeapon = player.EquipInfo.Weapon;
                ushort previousCharge = player.EquipInfo.ChargeLevel;
                IntentPacket previousIntent = NetSession.RemoteIntents[slot];
                var exactIntent = previousIntent;
                exactIntent.Target = attack.Target;
                exactIntent.Frame = fire.SourceFrame;
                exactIntent.ContinuousFireTick = fire.ContinuousPhase;
                exactIntent.HasContinuousFireTick = fire.Kind == FireEventKind.ContinuousTick;
                BeamSpawnFlags flags = BeamSpawnFlags.NoMuzzle;
                if (attack.DoubleDamage) flags |= BeamSpawnFlags.DoubleDamage;
                else if (attack.Prime) flags |= BeamSpawnFlags.PrimeHunter;
                try
                {
                    NetSession.RemoteIntents[slot] = exactIntent;
                    player.EquipInfo.Weapon = weapon; player.EquipInfo.ChargeLevel = fire.Charge;
                    using var acceptedScope = NetFireEvents.BeginAcceptedEmission(player, fire, attack.Target);
                    if (fire.Kind != FireEventKind.TurretFire) NetFireEvents.Begin(player);
                    NetUnlagged.BeginShot(player, fire.Origin, fire.Direction);
                    var result = BeamProjectileEntity.Spawn(owner, equip, fire.Origin,
                        fire.Direction.Normalized(), flags, player.NodeRef, player.OwningScene);
                    if (result != BeamResultFlags.NoSpawn)
                    {
                        Launched(player);
                        // Continuous weapons reuse one pool beam. Resolve this
                        // admitted pulse before a recovered older/newer pulse
                        // can replace it; EndShot ignores already-stepped beams.
                        if (fire.Kind == FireEventKind.ContinuousTick)
                            foreach (var beam in equip.Beams)
                                if (beam.ModLaunchKey.ShotId == fire.ShotId && beam.Age == 0 && beam.Lifespan > 0) beam.Process();
                    }
                    NetUnlagged.EndShot(player);
                }
                finally
                {
                    NetUnlagged.AbortShot();
                    player.EquipInfo.Weapon = previousWeapon; player.EquipInfo.ChargeLevel = previousCharge;
                    NetSession.RemoteIntents[slot] = previousIntent;
                }
            }
            state.EmissionCount = 0;
            for (int i = 0; i < state.AltIntents.Length; i++)
            {
                uint group = state.AltGroups[i]; int at = (int)(group % 128);
                if (group == 0 || state.PaidGroups[at] != group || !state.GroupBomb[at]
                    || state.BombPaid[at] || state.BombEmitted[at]
                    || unchecked(NetSession.NetFrame - state.GroupArrived[at]) > NetFireEvents.RetentionFrames) continue;
                if (!LagCompensationPolicy.TryAdmitTime(NetSession.NetFrame,
                    state.AltIntents[i].AckFrame, state.AltIntents[i].AckSubFrame, out _))
                { state.BombPaid[at] = true; state.ReservedBombs = Math.Max(0, state.ReservedBombs - 1); continue; }
                state.BombEmitted[at] = true;
                if (player.ModSpawnAcceptedBomb(state.AltIntents[i].Position, state.AltDouble[i], group)
                    && !state.BombPaid[at])
                { state.BombPaid[at] = true; state.ReservedBombs = Math.Max(0, state.ReservedBombs - 1); }
            }
        }
    }
    internal static void BombLaunched(PlayerEntity player, BombEntity bomb, uint sourceGroup = 0)
    {
        if (!player.ModAuthorityOwnsResources) return;
        Slot state = For(player.SlotIndex); uint group = sourceGroup != 0 ? sourceGroup : state.LastBombSource;
        for (int i = 0; i < state.NativeBombs.Length; i++)
            if (state.NativeBombs[i].Born == 0 || state.NativeBombs[i].Id == bomb.Id
                || unchecked(NetSession.NetFrame - state.NativeBombs[i].Seen) > 124)
            {
                state.NativeBombs[i] = new NativeBomb { Id = bomb.Id,
                    Born = Math.Max(NetSession.NetFrame, 1), Seen = NetSession.NetFrame, SourceFrame = group };
                for (int victim = 0; victim < 8; victim++) state.BombLineFrames[i, victim] = 0;
                break;
            }
        int at = (int)(group % 128);
        if (group != 0 && state.PaidGroups[at] == group && !state.BombPaid[at])
        { state.BombPaid[at] = true; state.ReservedBombs = Math.Max(0, state.ReservedBombs - 1); }
    }
    internal static bool ValidateClaim(int slot, in HitClaimPacket claim, bool pay = false)
        => ValidateClaimCore(slot, claim, pay, admittedTime: false);
    private static bool ValidateClaimCore(int slot, in HitClaimPacket claim, bool pay, bool admittedTime)
    {
        if (pay) LastResolvedImpact = LastResolvedDirection = default;
        if ((uint)slot >= 8 || slot >= PlayerEntity.Players.Count) return false;
        if ((uint)claim.VictimSlot >= PlayerEntity.Players.Count
            || !admittedTime && !LagCompensationPolicy.TryAdmitTime(NetSession.NetFrame,
                claim.AckFrame, claim.AckSubFrame, out _)) return false;
        Slot clockState = For(slot);
        if (!SourceTimeSupported(clockState, claim.Frame)) return false;
        if (claim.Beam == HitClaimPacket.NoBeam) return ValidateAlt(slot, claim, pay);
        Slot state = For(slot); int at = Find(state, claim.ShotId);
        if (at < 0) { ClaimsWithoutEvidence++; return false; }
        ref Attack attack = ref state.Attacks[at]; FireEvent fire = attack.Fire;
        if (fire.Weapon != claim.Beam || claim.LaunchFrame != fire.AckFrame
            || claim.AckFrame + claim.AckSubFrame / 256.0 < fire.AckFrame + fire.AckSubFrame / 256.0
            || fire.Kind == FireEventKind.ContinuousTick && (claim.Frame != fire.ContinuousPhase
                || (claim.Flags & HitClaimPacket.FlagContinuousTick) == 0)
            || fire.Kind != FireEventKind.ContinuousTick && (claim.Flags & HitClaimPacket.FlagContinuousTick) != 0) return false;
        PlayerEntity shooter = PlayerEntity.Players[slot], victim = PlayerEntity.Players[claim.VictimSlot];
        Span<uint> components = state.Components.AsSpan((at * 8 + claim.VictimSlot) * 16, 16);
        if (!ValidateGeometry(shooter, victim, attack, claim, components, out uint component, out Vector3 resolvedPoint, out Vector3 resolvedDirection)) { GeometryRefused++; return false; }
        bool head = (claim.Flags & HitClaimPacket.FlagHeadshot) != 0;
        if (head && (fire.Kind == FireEventKind.TurretFire || claim.Beam == (byte)BeamType.ShockCoil
            || !NetUnlagged.TryHistoricalBiped(victim, claim.AckFrame + claim.AckSubFrame / 256.0, out _))) return false;
        byte afflictions = NetHitClaims.AfflictionClaimFlags(attack.Weapon.Afflictions[attack.Charged ? 1 : 0]);
        const byte mask = HitClaimPacket.FlagFrozen | HitClaimPacket.FlagBurning | HitClaimPacket.FlagDisrupted;
        if ((claim.Flags & mask & ~afflictions) != 0) return false;
        int damage = MaximumDamage(shooter, victim, attack, head, claim.HitPoint);
        int pellets = Math.Max(1, Amount(attack.Weapon.Projectiles,
            attack.Weapon.MinChargeProjectiles, attack.Weapon.ChargedProjectiles, attack.Charge));
        int outcomes = (pellets + attack.MicroCount) * (attack.Weapon.SplashDamage > 0
            || attack.Weapon.ChargedSplashDamage > 0 ? 2 : 1);
        if (shooter.OwningScene.GameState.BalancedMode && attack.Weapon.Beam == BeamType.Battlehammer)
            outcomes = Math.Max(outcomes, 12); // Parent, terrain clusters and manual airburst.
        if (claim.Damage > damage || state.Outcomes[at, claim.VictimSlot] >= outcomes) return false;
        if (pay && !attack.Paid)
        {
            if (shooter.ModAttackAmmo((BeamType)claim.Beam, attack.Weapon) < attack.Cost) return false;
            shooter.ModConsumeAttackAmmo((BeamType)claim.Beam, attack.Weapon, attack.Cost);
            attack.Paid = true; state.Reserved[attack.Pool] -= attack.Cost;
        }
        if (component != 0 && state.Outcomes[at, claim.VictimSlot] >= components.Length) return false;
        if (pay)
        {
            if (component != 0) components[state.Outcomes[at, claim.VictimSlot]] = component;
            state.Outcomes[at, claim.VictimSlot]++; LastResolvedImpact = resolvedPoint; LastResolvedDirection = resolvedDirection;
        }
        return true;
    }
    internal static bool ConsumeClaim(int slot, in HitClaimPacket claim) => ValidateClaim(slot, claim, pay: true);
    /// <summary>Reserve the proven native component and its resource payment
    /// once, while historical geometry is available. Pending arbitration owns
    /// the immutable witness; no geometry or time budget is re-read after grace.</summary>
    internal static bool TryReserveClaim(int slot, in HitClaimPacket claim, out Vector3 point, out Vector3 direction)
    {
        point = direction = default;
        if (!ValidateClaimCore(slot, claim, pay: true, admittedTime: true)) return false;
        point = LastResolvedImpact; direction = LastResolvedDirection;
        return true;
    }
    private static bool ValidateGeometry(PlayerEntity shooter, PlayerEntity victim,
        in Attack attack, in HitClaimPacket claim, ReadOnlySpan<uint> used, out uint component, out Vector3 resolvedPoint, out Vector3 resolvedDirection)
    {
        component = 0; resolvedPoint = resolvedDirection = default;
        if (!attack.Fire.HasPose || !NetIntentPolicy.Sane(claim.HitPoint)) return false;
        // Derived native trajectories own their actual reach. Parent-table
        // estimates cannot replace or impose another envelope on that witness.
        return NetAttackPaths.Supports(shooter, victim, claim, attack.Fire, used,
            out component, out resolvedPoint, out resolvedDirection);
    }
    private static int MaximumDamage(PlayerEntity shooter, PlayerEntity victim, in Attack attack, bool head, Vector3 hitPoint)
    {
        WeaponInfo weapon = attack.Weapon; float pct = attack.Charge;
        int direct = head ? Amount(weapon.HeadshotDamage, weapon.MinChargeHeadshotDamage, weapon.ChargedHeadshotDamage, pct)
            : Amount(weapon.UnchargedDamage, weapon.MinChargeDamage, weapon.ChargedDamage, pct);
        int splash = Amount(weapon.SplashDamage, weapon.MinChargeSplashDamage, weapon.ChargedSplashDamage, pct);
        if (shooter.OwningScene.GameState.BalancedMode)
        {
            if (weapon.Beam == BeamType.Battlehammer) { direct = Math.Max(direct, 14); splash = Math.Max(splash, 6); }
            direct = (int)MathF.Round(BalancedModeRules.ScaleDirectHitDamage(weapon.Beam, direct, false), MidpointRounding.AwayFromZero);
            splash = (int)MathF.Round(BalancedModeRules.ScaleSplashDamage(weapon.Beam, splash), MidpointRounding.AwayFromZero);
        }
        int multiplier = attack.DoubleDamage ? 2 : 1;
        direct *= multiplier; splash *= multiplier;
        if (!attack.DoubleDamage && attack.Prime) { direct = direct * 150 / 100; splash = splash * 150 / 100; }
        if (Features.HalfDamageUnscoped && weapon.Beam == BeamType.Imperialist && !attack.Fire.ScopedAtFire) direct /= 2;
        if (weapon.Flags.TestFlag(WeaponFlags.Continuous))
        {
            direct = ContinuousWeaponPhase.Amount(direct, attack.Fire.ContinuousPhase, true);
            // The native Coil adds its target-contact ramp after phase rounding.
            // Accepted held duration is an upper bound; a different target can only reset it.
            if (attack.Fire.ContinuousPhase % 2 == 0) direct += attack.CoilRamp;
        }
        if (shooter.OwningScene.GameState.BalancedMode && BalancedModeRules.HasRangeDamageCurve(weapon.Beam))
        {
            float range = attack.Fire.HasPose ? Vector3.Distance(attack.Fire.Origin, hitPoint) : 8;
            float scale = attack.Fire.HasPose ? BalancedModeRules.RangeDamageMultiplier(weapon.Beam, range) : 1.2f;
            direct = (int)MathF.Ceiling(direct * scale); splash = (int)MathF.Ceiling(splash * scale);
        }
        if (Mods.EnhancedHunters.EnhancedHunters.UsingAffinity(shooter, weapon.Beam) && shooter.Hunter == Hunter.Spire)
            splash = (int)MathF.Ceiling(splash * 1.15f); // Native third ricochet explosion ceiling.
        if (Cheats.QuadrupleDamage) { direct *= 4; splash *= 4; }
        return FinalDamage(shooter, victim, Math.Max(direct, splash), weapon.Beam, head);
    }
    internal static int FinalDamage(PlayerEntity shooter, PlayerEntity victim, float raw, BeamType? beam, bool head)
    {
        uint damage = (uint)Math.Clamp(raw, 0, int.MaxValue);
        if (damage == 0) return 0; // A zero-damage continuous phase cannot mint a one-point claim.
        if (beam.HasValue)
        {
            float effect = Metadata.DamageMultipliers[(int)victim.BeamEffectiveness[(int)beam.Value]];
            if (effect == 0) return 0;
            damage = Math.Max(1u, (uint)(damage * effect));
        }
        damage = Math.Max(1u, (uint)(damage * Metadata.DamageLevels[shooter.OwningScene.GameState.DamageLevel]));
        byte reduction = NetSession.SlotDamageReduction[victim.SlotIndex];
        if (reduction > 0) damage = PlayerHandicap.ScaleDamage(damage, reduction);
        if (shooter.OwningScene.GameState.BalancedMode)
        {
            damage = BalancedModeRules.ScaleIncomingDamage(victim.Hunter, damage, head);
            // Weavel's transition shield only reduces this ceiling. The claim
            // names historical contact; current morph state cannot reduce it.
        }
        return (int)Math.Min(damage, int.MaxValue);
    }
    private static bool SourceTimeSupported(Slot state, uint frame)
        => state.SeenIntent && (!NetLifecycleTracker.Newer(frame, state.LastSource)
            || unchecked(frame - state.LastSource) <= unchecked(NetSession.NetFrame - state.LastArrival) + NetFireEvents.RetentionFrames);
    private static bool ValidateAlt(int slot, in HitClaimPacket claim, bool pay)
    {
        PlayerEntity shooter = PlayerEntity.Players[slot], victim = PlayerEntity.Players[claim.VictimSlot];
        Slot state = For(slot);
        if (pay) LastResolvedDirection = Vector3.Zero;
        double claimTime = claim.AckFrame + claim.AckSubFrame / 256.0;
        if (!NetIntentPolicy.Sane(claim.HitPoint)
            || !NetUnlagged.TryHistoricalPose(victim, claimTime, out var pose)
            || (claim.HitPoint - pose.Position).LengthSquared > NetHitClaims.ClaimRadius * NetHitClaims.ClaimRadius
            || !SourceTimeSupported(state, claim.Frame)) return false;
        var volume = PlayerEntity.PlayerVolumes[(int)victim.Hunter, pose.AltForm ? 2 : 0];
        HistoricalBody body = new(victim.SlotIndex, pose.Position + volume.SpherePosition,
            volume.SphereRadius, 0, 0, HistoricalBodyType.AltSphere);
        if ((claim.Flags & HitClaimPacket.FlagHalfturret) != 0)
        {
            if (!NetUnlagged.TryHistoricalHalfturretPosition(victim.SlotIndex, claimTime,
                claim.VictimGeneration, claim.VictimLifeId, out Vector3 turret)) return false;
            body = body with { Position = turret, Radius = .45f };
        }
        const byte bad = HitClaimPacket.FlagHeadshot | HitClaimPacket.FlagBurning
            | HitClaimPacket.FlagDisrupted | HitClaimPacket.FlagContinuousTick;
        if ((claim.Flags & bad) != 0) return false;
        bool frozen = (claim.Flags & HitClaimPacket.FlagFrozen) != 0;
        if (!frozen)
        {
            // Native bomb/contact rules deliberately have no victim LOS test.
            // Use their actual contact sphere, damage and resource-backed native
            // placement, never a HitPoint-radius envelope or free alt intent.
            for (int row = 0; row < 128; row++) for (int i = 0; i < 3; i++)
            {
                BombSample sample = state.BombHistory[row, i];
                if (sample.Born == 0 || unchecked(NetSession.NetFrame - sample.Frame) > 124) continue;
                for (int j = 0; j < state.NativeBombs.Length; j++)
                {
                    ref NativeBomb bomb = ref state.NativeBombs[j];
                    uint elapsed = unchecked(claim.Frame - bomb.SourceFrame);
                    if (bomb.Id != sample.Id || bomb.Born != sample.Born || elapsed > 1800
                        || Math.Abs((long)sample.TravelFrames - elapsed) > 2) continue;
                    int bombDamage = sample.Damage;
                    if (shooter.Hunter == Hunter.Sylux && sample.Connections)
                    {
                        BombSample zero = default, one = default, two = default;
                        for (int trap = 0; trap < 3; trap++)
                        {
                            BombSample point = state.BombHistory[row, trap];
                            if (point.Born == 0 || !point.Connections || point.Frame != sample.Frame) continue;
                            if (point.Index == 0) zero = point; else if (point.Index == 1) one = point; else if (point.Index == 2) two = point;
                        }
                        Vector3 snarePoint = (claim.Flags & HitClaimPacket.FlagHalfturret) != 0 ? body.Position : pose.Position;
                        if (zero.Born != 0 && one.Born != 0 && two.Born != 0
                            && BombEntity.ModLockjawSnareContains(zero.Position, one.Position, two.Position, snarePoint)) bombDamage = 60;
                    }
                    bool blast = (body.Position - sample.Position).LengthSquared <= sample.Radius * sample.Radius;
                    if (blast && (bomb.Paid & (1 << claim.VictimSlot)) == 0
                        && claim.Damage <= FinalDamage(shooter, victim, bombDamage, null, false))
                    {
                        if (pay) { bomb.Paid |= (byte)(1 << claim.VictimSlot); LastResolvedImpact = sample.Position; }
                        return true;
                    }
                    if (shooter.Hunter != Hunter.Sylux || !sample.Connections || sample.Index == 0 || claim.Frame == 0
                        || state.BombLineFrames[j, claim.VictimSlot] != 0
                            && !NetLifecycleTracker.Newer(claim.Frame, state.BombLineFrames[j, claim.VictimSlot])
                        || claim.Damage > FinalDamage(shooter, victim, 20, null, false)) continue;
                    for (int other = 0; other < 3; other++)
                    {
                        BombSample peer = state.BombHistory[row, other];
                        if (peer.Born == 0 || !peer.Connections || peer.Index >= sample.Index || peer.Id == sample.Id || peer.Frame != sample.Frame
                            || (peer.Position - sample.Position).LengthSquared >= 100) continue;
                        HistoricalBody lineBody = pose.AltForm ? body : body with { Position = pose.Position,
                            Bottom = Fixed.ToFloat(victim.Values.MinPickupHeight), Top = Fixed.ToFloat(victim.Values.MaxPickupHeight),
                            Type = HistoricalBodyType.BipedCylinder };
                        if ((claim.Flags & HitClaimPacket.FlagHalfturret) != 0) lineBody = body;
                        if (!NetHistoricalTrace.Intersect(lineBody, sample.Position, peer.Position, 0, out var contact)) continue;
                        if (pay) { state.BombLineFrames[j, claim.VictimSlot] = claim.Frame; LastResolvedImpact = contact.Position; }
                        return true;
                    }
                }
            }
            if (victim.ModBurning && victim.BurnedBy == shooter)
            {
                int ticks = shooter.OwningScene.GameState.BalancedMode && shooter.Hunter == Hunter.Spire
                    ? BalancedModeRules.SpireBurnTickFrames : 16;
                uint prior = state.BurnFrames[claim.VictimSlot];
                if (claim.Frame != 0 && (prior == 0 || NetLifecycleTracker.Newer(claim.Frame, prior)
                    && unchecked(claim.Frame - prior) >= ticks)
                    && claim.Damage <= FinalDamage(shooter, victim, shooter.DoubleDamage ? 2 : 1, null, false))
                { if (pay) { state.BurnFrames[claim.VictimSlot] = claim.Frame; LastResolvedImpact = pose.Position; } return true; }
            }
        }
        for (int i = 0; i < state.AltIntents.Length; i++)
        {
            IntentPacket intent = state.AltIntents[i]; uint group = state.AltGroups[i]; int at = (int)(group % 128);
            if (state.AltArrivals[i] == 0 || unchecked(NetSession.NetFrame - state.AltArrivals[i]) > 124
                || unchecked(claim.Frame - intent.Frame) > 2
                || intent.AckFrame + intent.AckSubFrame / 256.0 > claimTime
                || claim.AckFrame - intent.AckFrame > 32 || group == 0 || state.PaidGroups[at] != group
                || state.GroupBomb[at] || (state.AltPaid[at] & (1 << claim.VictimSlot)) != 0
                || frozen && shooter.Hunter != Hunter.Noxus) continue;
            HistoricalAltAttackState contact = state.AltContacts[i];
            if (!NetContactLagComp.Intersects(contact, body, true)) continue;
            int raw = contact.Kind == ContactAttackKind.Boost ? Math.Min(intent.BoostDamage, shooter.Values.AltAttackDamage)
                : shooter.Values.AltAttackDamage;
            if (claim.Damage > FinalDamage(shooter, victim, raw * (state.AltDouble[i] ? 2 : 1), null, false)) continue;
            if (pay) { state.AltPaid[at] |= (ushort)(1 << claim.VictimSlot); LastResolvedImpact = pose.Position; }
            return true;
        }
        return false;
    }
}
