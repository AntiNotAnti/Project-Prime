using System;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Network;

/// <summary>Independent proof from repeated physical controls. Missing or conflicting samples
/// never become held input. Charge uses authority resources; scope needs an observed reset.</summary>
internal static class NetShotInputProof
{
    internal const int Capacity = 512;
    private struct Sample { internal bool Live, Conflict; internal uint Frame; internal byte Bits; }
    private struct Scope
    {
        internal bool Known, Zoomed, PreviousZoom;
        internal int Weapon;
        internal void Step(byte bits)
        {
            int weapon = bits & 15;
            bool down = (bits & ShotInputHistory.ZoomBit) != 0;
            bool eligible = (bits & ShotInputHistory.EligibleBit) != 0;
            if (weapon != (int)BeamType.Imperialist || Weapon >= 0 && Weapon != weapon)
            { Known = true; Zoomed = false; }
            if (!eligible && weapon == (int)BeamType.Imperialist) Known = false;
            if (eligible && weapon == (int)BeamType.Imperialist && down && !PreviousZoom)
                Zoomed = !Zoomed;
            Weapon = weapon; PreviousZoom = down;
        }
        internal void Missing() { Known = false; Weapon = -1; PreviousZoom = false; }
    }
    private sealed class Life
    {
        internal ShotKey Key;
        internal uint First, Frontier, Birth, ChargeStart;
        internal bool Seen, Poisoned;
        internal Scope Prefix = new() { Weapon = -1 };
        internal readonly Sample[] Samples = new Sample[Capacity];
    }
    private static readonly Life?[,] Lives = new Life?[8, 8];
    private static bool Active => NetShotProof.SourceObserving;
    private static Life? Get(ShotKey key) => (uint)key.ShooterSlot < 8
        && Lives[key.ShooterSlot, key.LifeId % 8] is { } life
        && life.Key == (key with { ShotId = 0 }) ? life : null;
    internal static void Seed(PlayerEntity player)
    {
        if (!NetShotProof.SourceInterested(player) || (uint)player.SlotIndex >= 8) return;
        var key = ShotKey.For(player.SlotIndex, 0);
        Lives[key.ShooterSlot, key.LifeId % 8] = new Life { Key = key, Birth = NetSession.NetFrame };
    }
    private static bool Read(Life life, uint frame, out byte bits)
    {
        var sample = life.Samples[frame % Capacity]; bits = sample.Bits;
        return sample.Live && !sample.Conflict && sample.Frame == frame;
    }
    internal static void Observe(int slot, in IntentPacket intent)
    {
        if (!Active || intent.ShotInputCount == 0 || !intent.ShotInputs.Valid(intent.ShotInputCount)) return;
        var key = new ShotKey(intent.AuthorityEpoch, intent.MatchId, slot, intent.SlotGeneration, intent.LifeId, 0);
        var life = Get(key); if (life == null) return;
        uint oldest = intent.Frame - intent.ShotInputCount + 1;
        if (!life.Seen) { life.Seen = true; life.First = oldest; life.Frontier = intent.Frame; life.ChargeStart = intent.Frame; }
        if (unchecked((int)(intent.Frame - life.Frontier)) > 0)
        {
            // Preserve scope toggles beyond the raw ring without trusting a reported ZoomedState.
            uint floor = intent.Frame - (Capacity - 1);
            int discard = unchecked((int)(floor - life.First));
            if (discard > Capacity) { life.Prefix.Missing(); life.First = floor; }
            else for (int i = 0; i < discard; i++, life.First++)
            {
                if (Read(life, life.First, out byte old)) life.Prefix.Step(old);
                else life.Prefix.Missing();
            }
            life.Frontier = intent.Frame;
        }
        for (int i = intent.ShotInputCount - 1; i >= 0; i--)
        {
            uint frame = intent.Frame - (uint)i;
            if (unchecked((int)(frame - life.Frontier)) <= -Capacity) continue;
            if (unchecked((int)(frame - life.First)) < 0)
            {
                // Backfill can extend only an uncompacted beginning. A previous scope
                // checkpoint cannot safely be retroactively changed.
                if (unchecked(life.Frontier - frame) < Capacity && life.Prefix.Weapon == -1)
                    life.First = frame;
                else continue;
            }
            ref var sample = ref life.Samples[frame % Capacity];
            if (sample.Live && sample.Frame == frame)
            {
                if (sample.Bits != intent.ShotInputs[i]) { sample.Conflict = true; life.Poisoned = true; }
            }
            else sample = new Sample { Live = true, Frame = frame, Bits = intent.ShotInputs[i] };
        }
    }
    internal static ShotProofReason Inspect(PlayerEntity player, ShotKey key, in FireEvent fire, bool charged)
    {
        // Authority-owned native simulation supplies these inputs directly.
        if (player.IsBot || player.SlotIndex == NetSession.LocalSlot) return ShotProofReason.Verified;
        var life = Get(key);
        if (life == null || !life.Seen || life.Poisoned || !Read(life, fire.SourceFrame, out byte current))
            return ShotProofReason.SourceInputUnavailable;
        if ((current & 15) != fire.Weapon || (current & ShotInputHistory.EligibleBit) == 0)
            return ShotProofReason.InvalidCharge;
        if (fire.Weapon == (byte)BeamType.Imperialist)
        {
            var scope = life.Prefix;
            int count = unchecked((int)(fire.SourceFrame - life.First));
            if (count < 0 || count >= Capacity) return ShotProofReason.SourceInputUnavailable;
            for (int i = 0; i <= count; i++)
            {
                if (Read(life, life.First + (uint)i, out byte bits)) scope.Step(bits);
                else scope.Missing();
            }
            if (!scope.Known) return ShotProofReason.SourceInputUnavailable;
            if (fire.ScopedAtFire != scope.Zoomed) return ShotProofReason.ModifierMismatch;
        }
        if (!charged)
        {
            if (fire.Kind is not (FireEventKind.PressFire or FireEventKind.AutomaticFire))
                return ShotProofReason.InvalidCharge;
            if (!Read(life, fire.SourceFrame - 1, out byte previous)) return ShotProofReason.SourceInputUnavailable;
            if (fire.Weapon == (byte)BeamType.VoltDriver && fire.Kind == FireEventKind.PressFire)
            {
                // Native buffered taps fire on the first legal cooldown frame,
                // when the trigger may already be up. Prove a real rising edge
                // in the bounded window rather than trusting a delayed press
                // marker. The independent source journal still enforces ammo
                // and the full cooldown between committed shots.
                if ((current & ShotInputHistory.ShootBit) != 0)
                    return (previous & ShotInputHistory.ShootBit) == 0
                        ? ShotProofReason.Verified : ShotProofReason.InvalidCharge;
                int weapon = (int)BeamType.VoltDriver;
                if (NetShotSourceJournal.TryState(key, fire.SourceFrame, out var source)
                    && Weapons.GetAffinityBeam(source.Hunter) == BeamType.VoltDriver)
                    weapon += 9;
                int maxDelay = VoltDriverTapBuffer.MaxDelayFrames(
                    player.OwningScene.WeaponRules[weapon].ShotCooldown * 2);
                for (int age = 1; age <= maxDelay; age++)
                {
                    uint frame = unchecked(fire.SourceFrame - (uint)age);
                    if (!Read(life, frame, out byte past) || !Read(life, frame - 1, out byte before))
                        return ShotProofReason.SourceInputUnavailable;
                    if ((past & 15) != fire.Weapon || (past & ShotInputHistory.EligibleBit) == 0)
                        return ShotProofReason.InvalidCharge;
                    if ((past & ShotInputHistory.ShootBit) != 0
                        && (before & ShotInputHistory.ShootBit) == 0)
                        return ShotProofReason.Verified;
                }
                return ShotProofReason.InvalidCharge;
            }
            if ((current & ShotInputHistory.ShootBit) == 0
                || fire.Kind == FireEventKind.PressFire && (previous & ShotInputHistory.ShootBit) != 0
                || fire.Kind == FireEventKind.AutomaticFire && (previous & ShotInputHistory.ShootBit) == 0)
                return ShotProofReason.InvalidCharge;
            return ShotProofReason.Verified;
        }
        // The sender cannot turn backfilled frames into time lived before authority spawn.
        if (unchecked(NetSession.NetFrame - life.Birth) < fire.Charge)
            return ShotProofReason.InvalidCharge;
        if (fire.Kind != FireEventKind.ReleaseFire) return ShotProofReason.InvalidCharge;
        // A carrier's backfill is not a spawn-time receipt. Establish the earliest
        // provable charge frame from the first carrier actually observed in this life.
        if (unchecked((int)(fire.SourceFrame - life.ChargeStart)) < 0)
            return ShotProofReason.SourceInputUnavailable;
        int held = 0;
        bool reset = false;
        for (int age = 1; age < NetShotSourceJournal.ReorderFrames; age++)
        {
            uint sourceFrame = fire.SourceFrame - (uint)age;
            if (unchecked((int)(sourceFrame - life.ChargeStart)) < 0
                || !Read(life, sourceFrame, out byte bits)) break;
            if ((bits & 15) != fire.Weapon || (bits & ShotInputHistory.ShootBit) == 0)
            { reset = true; break; }
            if ((bits & ShotInputHistory.EligibleBit) == 0) return ShotProofReason.SourceInputUnavailable;
            held++;
        }
        if (held == 0) return ShotProofReason.SourceInputUnavailable;
        int charge = 0;
        for (int age = held; age >= 0; age--)
        {
            uint frame = fire.SourceFrame - (uint)age;
            if (!NetShotSourceJournal.TryState(key, frame, out var state)) return ShotProofReason.SourceHistoryIncomplete;
            bool affinity = Weapons.GetAffinityBeam(state.Hunter) == (BeamType)fire.Weapon;
            var weapon = player.OwningScene.WeaponRules[fire.Weapon + (affinity ? 9 : 0)];
            if ((state.Charges & (1 << fire.Weapon)) == 0 || !weapon.Flags.TestFlag(WeaponFlags.CanCharge)
                || fire.Charge < weapon.MinCharge * 2)
                return ShotProofReason.InvalidCharge;
            int ammo = weapon.AmmoType == 0 ? state.Ammo0 : state.Ammo1;
            bool down = age > 0 || (current & ShotInputHistory.ShootBit) != 0;
            bool release = !down || !state.Infinite && ammo < weapon.ChargeCost;
            if (age == 0)
            {
                if (!reset && charge < weapon.FullCharge * 2) return ShotProofReason.SourceInputUnavailable;
                return release && charge == fire.Charge ? ShotProofReason.Verified : ShotProofReason.InvalidCharge;
            }
            if (release) { charge = 0; reset = true; continue; }
            if (charge < weapon.FullCharge * 2)
            {
                charge++;
                int min = weapon.MinCharge * 2, full = weapon.FullCharge * 2;
                if (charge > min && full > min)
                {
                    int cost = (weapon.MinChargeCost * 2
                        + (weapon.ChargeCost - weapon.MinChargeCost) * 2 * (charge - min) / (full - min)) / 2;
                    if (!state.Infinite && ammo < cost) charge--;
                }
            }
        }
        return ShotProofReason.SourceInputUnavailable;
    }
    internal static void Reset() => Array.Clear(Lives);
}
