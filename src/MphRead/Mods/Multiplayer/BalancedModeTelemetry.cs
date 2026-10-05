using System;
using System.Text;
using MphRead.Entities;
using MphRead.Mods.MatchEvents;
using MphRead.Mods.Network;
using MphRead.Mods.Network.Telemetry;
using OpenTK.Mathematics;

namespace MphRead.Mods.Multiplayer;

internal enum BalancedModeMetric
{
    HunterPick,
    HunterSecond,
    HunterKill,
    HunterDeath,
    HunterDamageDealt,
    HunterDamageTaken,
    WeaponShot,
    WeaponHit,
    WeaponKill,
    WeaponDamage,
    DirectHit,
    DirectDamage,
    SplashHit,
    SplashDamage,
    RangeCloseHit,
    RangeMidHit,
    RangeFarHit,
    RangeCloseDamage,
    RangeMidDamage,
    RangeFarDamage,
    BattlehammerParentDirectHit,
    BattlehammerTerrainImpact,
    BattlehammerChildrenSpawned,
    BattlehammerChildHit,
    BattlehammerChildDamage,
    BattlehammerChildKill,
    KandenDisruptApplied,
    KandenDisruptRejected,
    NoxusFreezeApplied,
    NoxusFreezeRejected,
    SpireBurnApplied,
    SpireBurnDamage,
    SpireBurnKill,
    SyluxDrainRequested,
    SyluxDrainActual,
    SyluxDrainCapped,
    ImperialistShotsAcquired,
    ImperialistShotsFired,
    ImperialistShotsWasted,
    ImperialistBodyKill,
    ImperialistHeadshotKill
}

/// <summary>
/// Authority-owned aggregate balance measurements for Balanced Mode.
///
/// No identity strings are retained. Hunter and weapon dimensions are bounded
/// enum indexes; range buckets use the same 12/24-unit thresholds as the live
/// damage rules. The aggregate is flushed into the existing anonymous network
/// telemetry writer at match teardown.
/// </summary>
internal static class BalancedModeTelemetry
{
    internal const int Hunters = 7;
    internal const int Weapons = 9;
    internal const int RangeBuckets = 3;
    internal const int BattlehammerMetrics = 6;
    internal const int AffinityMetrics = 11;
    internal const int ImperialistMetrics = 5;

    private static readonly long[] _hunterPicks = new long[Hunters];
    private static readonly long[] _hunterSeconds = new long[Hunters];
    private static readonly long[] _hunterKills = new long[Hunters];
    private static readonly long[] _hunterDeaths = new long[Hunters];
    private static readonly long[] _hunterDamageDealt = new long[Hunters];
    private static readonly long[] _hunterDamageTaken = new long[Hunters];

    private static readonly long[] _weaponShots = new long[Weapons];
    private static readonly long[] _weaponHits = new long[Weapons];
    private static readonly long[] _weaponKills = new long[Weapons];
    private static readonly long[] _weaponDamage = new long[Weapons];
    private static readonly long[] _directHits = new long[Weapons];
    private static readonly long[] _directDamage = new long[Weapons];
    private static readonly long[] _splashHits = new long[Weapons];
    private static readonly long[] _splashDamage = new long[Weapons];
    private static readonly long[] _rangeHits = new long[Weapons * RangeBuckets];
    private static readonly long[] _rangeDamage = new long[Weapons * RangeBuckets];

    // parent-direct, terrain-impact, children-spawned, child-hit, child-damage, child-kill
    private static readonly long[] _battlehammer = new long[BattlehammerMetrics];
    // Kanden applied/rejected, Noxus applied/rejected, Spire burn applied/damage/kill,
    // Sylux requested/actual/capped, reserved
    private static readonly long[] _affinity = new long[AffinityMetrics];
    // acquired, fired, wasted-on-death, body-kill, headshot-kill
    private static readonly long[] _imperialist = new long[ImperialistMetrics];

    private static readonly bool[,] _picked = new bool[PlayerEntity.SlotCapacity, Hunters];
    private static ulong _lastSecondFrame = UInt64.MaxValue;
    private static bool _flushed;

    internal static void Reset()
    {
        foreach (long[] values in new[] { _hunterPicks, _hunterSeconds, _hunterKills, _hunterDeaths,
            _hunterDamageDealt, _hunterDamageTaken, _weaponShots, _weaponHits, _weaponKills,
            _weaponDamage, _directHits, _directDamage, _splashHits, _splashDamage,
            _rangeHits, _rangeDamage, _battlehammer, _affinity, _imperialist })
        {
            Array.Clear(values);
        }
        Array.Clear(_picked);
        _lastSecondFrame = UInt64.MaxValue;
        _flushed = false;
    }

    private static bool CanRecord(Scene scene)
        => scene.GameState.Multiplayer && scene.GameState.BalancedMode
            && !scene.Services.IsReplica && (!NetSession.Active || NetSession.IsAuthority);

    private static bool HunterIndex(Hunter hunter, out int index)
    {
        index = (int)hunter;
        return (uint)index < Hunters;
    }

    private static bool WeaponIndex(BeamType beam, out int index)
    {
        index = (int)beam;
        return (uint)index < Weapons;
    }

    private static void Add(long[] values, int index, long amount = 1)
    {
        if ((uint)index >= values.Length || amount <= 0) return;
        values[index] = Math.Min(Int64.MaxValue, values[index] + amount);
    }

    internal static void NoteHunterSpawn(PlayerEntity player)
    {
        Scene scene = player.OwningScene;
        if (!CanRecord(scene) || !HunterIndex(player.Hunter, out int hunter)
            || (uint)player.SlotIndex >= PlayerEntity.SlotCapacity) return;
        if (!_picked[player.SlotIndex, hunter])
        {
            _picked[player.SlotIndex, hunter] = true;
            Add(_hunterPicks, hunter);
        }
    }

    internal static void Tick(Scene scene)
    {
        if (!CanRecord(scene) || scene.FrameCount == _lastSecondFrame
            || scene.FrameCount % 60 != 0) return;
        _lastSecondFrame = scene.FrameCount;
        foreach (PlayerEntity player in scene.GetPlayerEntities())
        {
            if (!player.LoadFlags.TestFlag(LoadFlags.Active)
                || player.Flags2.TestFlag(PlayerFlags2.Spectating)
                || !HunterIndex(player.Hunter, out int hunter)) continue;
            Add(_hunterSeconds, hunter);
        }
    }

    internal static void NoteShot(Scene scene, in MatchSemanticEvent fact)
    {
        if (!CanRecord(scene) || !WeaponIndex((BeamType)fact.Weapon, out int weapon)) return;
        Add(_weaponShots, weapon);
        if ((BeamType)fact.Weapon == BeamType.Imperialist) Add(_imperialist, 1);
    }

    internal static void NoteDamage(PlayerEntity victim, PlayerEntity attacker,
        BeamProjectileEntity? beam, BeamType weapon, uint applied)
    {
        Scene scene = victim.OwningScene;
        if (!CanRecord(scene) || applied == 0) return;

        if (HunterIndex(attacker.Hunter, out int attackerHunter))
            Add(_hunterDamageDealt, attackerHunter, applied);
        if (HunterIndex(victim.Hunter, out int victimHunter))
            Add(_hunterDamageTaken, victimHunter, applied);
        if (!WeaponIndex(weapon, out int weaponIndex)) return;

        Add(_weaponHits, weaponIndex);
        Add(_weaponDamage, weaponIndex, applied);

        if (beam != null)
        {
            bool direct = beam.EnhancedDirectHit;
            if (direct)
            {
                Add(_directHits, weaponIndex);
                Add(_directDamage, weaponIndex, applied);
            }
            else if (beam.SplashDamage > 0)
            {
                Add(_splashHits, weaponIndex);
                Add(_splashDamage, weaponIndex, applied);
            }

            float distance = Vector3.Distance(beam.Position, beam.SpawnPosition);
            int range = distance < BalancedModeRules.MidRange ? 0
                : distance < BalancedModeRules.FarRange ? 1 : 2;
            Add(_rangeHits, weaponIndex * RangeBuckets + range);
            Add(_rangeDamage, weaponIndex * RangeBuckets + range, applied);

            if (weapon == BeamType.Battlehammer)
            {
                if (beam.BattlehammerClusterChild)
                {
                    Add(_battlehammer, 3);
                    Add(_battlehammer, 4, applied);
                    if (applied >= victim.Health) Add(_battlehammer, 5);
                }
                else if (direct)
                {
                    Add(_battlehammer, 0);
                }
            }
        }
    }

    internal static void NoteKill(Scene scene, in MatchSemanticEvent fact)
    {
        if (!CanRecord(scene) || !fact.Actor.IsPlayer || !fact.Target.IsPlayer) return;
        if ((uint)fact.Actor.Slot < PlayerEntity.Players.Count)
        {
            PlayerEntity attacker = PlayerEntity.Players[fact.Actor.Slot];
            if (HunterIndex(attacker.Hunter, out int hunter)) Add(_hunterKills, hunter);
        }
        if ((uint)fact.Target.Slot < PlayerEntity.Players.Count)
        {
            PlayerEntity victim = PlayerEntity.Players[fact.Target.Slot];
            if (HunterIndex(victim.Hunter, out int hunter)) Add(_hunterDeaths, hunter);
        }
        if (WeaponIndex((BeamType)fact.Weapon, out int weapon)) Add(_weaponKills, weapon);
        if ((BeamType)fact.Weapon == BeamType.Imperialist)
        {
            bool headshot = (fact.Flags & MatchSemanticEventFlags.Headshot) != 0;
            Add(_imperialist, headshot ? 4 : 3);
        }
    }

    internal static void NoteBattlehammerTerrainImpact(PlayerEntity owner, int children)
    {
        if (!CanRecord(owner.OwningScene)) return;
        Add(_battlehammer, 1);
        Add(_battlehammer, 2, children);
    }

    internal static void NoteKandenDisrupt(PlayerEntity victim, bool applied)
    {
        if (!CanRecord(victim.OwningScene)) return;
        Add(_affinity, applied ? 0 : 1);
    }

    internal static void NoteNoxusFreeze(PlayerEntity victim, bool applied)
    {
        if (!CanRecord(victim.OwningScene)) return;
        Add(_affinity, applied ? 2 : 3);
    }

    internal static void NoteSpireBurn(PlayerEntity victim)
    {
        if (CanRecord(victim.OwningScene)) Add(_affinity, 4);
    }

    internal static void NoteSpireBurnDamage(PlayerEntity victim, bool lethal)
    {
        if (!CanRecord(victim.OwningScene)) return;
        Add(_affinity, 5);
        if (lethal) Add(_affinity, 6);
    }

    internal static void NoteSyluxDrain(PlayerEntity player, int requested, int actual)
    {
        if (!CanRecord(player.OwningScene)) return;
        Add(_affinity, 7, requested);
        Add(_affinity, 8, actual);
        Add(_affinity, 9, Math.Max(0, requested - actual));
    }

    internal static void NoteImperialistAcquired(PlayerEntity player, int shots)
    {
        if (CanRecord(player.OwningScene)) Add(_imperialist, 0, shots);
    }

    internal static void NoteImperialistDeath(PlayerEntity player, int unusedShots)
    {
        if (CanRecord(player.OwningScene)) Add(_imperialist, 2, unusedShots);
    }

    internal static BalancedModeTelemetrySummary Capture()
        => new(BalancedModeRules.BalanceRevision,
            (long[])_hunterPicks.Clone(), (long[])_hunterSeconds.Clone(),
            (long[])_hunterKills.Clone(), (long[])_hunterDeaths.Clone(),
            (long[])_hunterDamageDealt.Clone(), (long[])_hunterDamageTaken.Clone(),
            (long[])_weaponShots.Clone(), (long[])_weaponHits.Clone(),
            (long[])_weaponKills.Clone(), (long[])_weaponDamage.Clone(),
            (long[])_directHits.Clone(), (long[])_directDamage.Clone(),
            (long[])_splashHits.Clone(), (long[])_splashDamage.Clone(),
            (long[])_rangeHits.Clone(), (long[])_rangeDamage.Clone(),
            (long[])_battlehammer.Clone(), (long[])_affinity.Clone(),
            (long[])_imperialist.Clone());

    private static void Emit(BalancedModeMetric metric, long value,
        int hunter = 255, int weapon = 255)
    {
        if (value <= 0 || !ProductionTelemetry.Enabled) return;
        ProductionTelemetry.Emit(new(TelemetryEventType.BalancedMode, NetSession.NetFrame,
            Player: (byte)hunter, Weapon: (byte)weapon, Result: (int)metric, A: value,
            Id: (uint)BalancedModeRules.BalanceRevision));
    }

    internal static void FlushToProductionTelemetry()
    {
        if (_flushed) return;
        _flushed = true;
        for (int h = 0; h < Hunters; h++)
        {
            Emit(BalancedModeMetric.HunterPick, _hunterPicks[h], hunter: h);
            Emit(BalancedModeMetric.HunterSecond, _hunterSeconds[h], hunter: h);
            Emit(BalancedModeMetric.HunterKill, _hunterKills[h], hunter: h);
            Emit(BalancedModeMetric.HunterDeath, _hunterDeaths[h], hunter: h);
            Emit(BalancedModeMetric.HunterDamageDealt, _hunterDamageDealt[h], hunter: h);
            Emit(BalancedModeMetric.HunterDamageTaken, _hunterDamageTaken[h], hunter: h);
        }
        for (int w = 0; w < Weapons; w++)
        {
            Emit(BalancedModeMetric.WeaponShot, _weaponShots[w], weapon: w);
            Emit(BalancedModeMetric.WeaponHit, _weaponHits[w], weapon: w);
            Emit(BalancedModeMetric.WeaponKill, _weaponKills[w], weapon: w);
            Emit(BalancedModeMetric.WeaponDamage, _weaponDamage[w], weapon: w);
            Emit(BalancedModeMetric.DirectHit, _directHits[w], weapon: w);
            Emit(BalancedModeMetric.DirectDamage, _directDamage[w], weapon: w);
            Emit(BalancedModeMetric.SplashHit, _splashHits[w], weapon: w);
            Emit(BalancedModeMetric.SplashDamage, _splashDamage[w], weapon: w);
            Emit(BalancedModeMetric.RangeCloseHit, _rangeHits[w * 3], weapon: w);
            Emit(BalancedModeMetric.RangeMidHit, _rangeHits[w * 3 + 1], weapon: w);
            Emit(BalancedModeMetric.RangeFarHit, _rangeHits[w * 3 + 2], weapon: w);
            Emit(BalancedModeMetric.RangeCloseDamage, _rangeDamage[w * 3], weapon: w);
            Emit(BalancedModeMetric.RangeMidDamage, _rangeDamage[w * 3 + 1], weapon: w);
            Emit(BalancedModeMetric.RangeFarDamage, _rangeDamage[w * 3 + 2], weapon: w);
        }
        BalancedModeMetric[] bh = { BalancedModeMetric.BattlehammerParentDirectHit,
            BalancedModeMetric.BattlehammerTerrainImpact, BalancedModeMetric.BattlehammerChildrenSpawned,
            BalancedModeMetric.BattlehammerChildHit, BalancedModeMetric.BattlehammerChildDamage,
            BalancedModeMetric.BattlehammerChildKill };
        for (int i = 0; i < bh.Length; i++) Emit(bh[i], _battlehammer[i]);

        BalancedModeMetric[] affinity = { BalancedModeMetric.KandenDisruptApplied,
            BalancedModeMetric.KandenDisruptRejected, BalancedModeMetric.NoxusFreezeApplied,
            BalancedModeMetric.NoxusFreezeRejected, BalancedModeMetric.SpireBurnApplied,
            BalancedModeMetric.SpireBurnDamage, BalancedModeMetric.SpireBurnKill,
            BalancedModeMetric.SyluxDrainRequested, BalancedModeMetric.SyluxDrainActual,
            BalancedModeMetric.SyluxDrainCapped };
        for (int i = 0; i < affinity.Length; i++) Emit(affinity[i], _affinity[i]);

        BalancedModeMetric[] imp = { BalancedModeMetric.ImperialistShotsAcquired,
            BalancedModeMetric.ImperialistShotsFired, BalancedModeMetric.ImperialistShotsWasted,
            BalancedModeMetric.ImperialistBodyKill, BalancedModeMetric.ImperialistHeadshotKill };
        for (int i = 0; i < imp.Length; i++) Emit(imp[i], _imperialist[i]);
    }

    internal static string Describe()
    {
        var text = new StringBuilder($"Balanced r{BalancedModeRules.BalanceRevision}");
        long shots = 0, hits = 0, damage = 0;
        for (int i = 0; i < Weapons; i++)
        {
            shots += _weaponShots[i]; hits += _weaponHits[i]; damage += _weaponDamage[i];
        }
        text.Append($" shots={shots} hits={hits} damage={damage}");
        text.Append($" BH={_battlehammer[0]}direct/{_battlehammer[1]}terrain/{_battlehammer[3]}childHits");
        text.Append($" affinity=K{_affinity[0]}/{_affinity[1]} N{_affinity[2]}/{_affinity[3]}");
        text.Append($" Imp={_imperialist[0]}acq/{_imperialist[1]}fired/{_imperialist[2]}wasted");
        return text.ToString();
    }
}
