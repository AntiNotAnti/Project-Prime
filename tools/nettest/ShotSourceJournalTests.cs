using System;
using System.Collections.Generic;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Exercises the production resource journal without assets, playback projectiles or
// client inventory. Reflection supplies only the session clock/role as in the other
// packet-only fixtures. These checks do not establish honest pickup equivalence.
internal static class ShotSourceJournalTests
{
    private static int _checks;
    private static PlayerEntity _player = null!;
    private static ShotKey _life;
    private static void Check(bool ok, string label)
    { _checks++; if (!ok) throw new InvalidOperationException(label); }
    private static void Frame(uint frame)
        => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, frame);
    private static ShotProofSource Source(int ammo = 60, int cap = 100, ushort weapons = 0x1FF,
        bool infinite = false, bool supported = true)
        => new(weapons, weapons, ammo, ammo, infinite, Hunter.Samus, false, false, supported,
            AmmoMax0: cap, AmmoMax1: cap);
    private static void Prepare(ShotProofSource? source = null)
    {
        HealthShotTests.Session();
        typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Host);
        NetHitClaims.Enabled = true; NetShotProof.Mode = ShotProofMode.Shadow;
        NetShotSourceJournal.Reset(); Frame(10);
        _player = HealthShotTests.Player(0); _life = ShotKey.For(0, 0);
        NetShotSourceJournal.Seed(_player, source ?? Source());
    }
    private static ShotKey Key(uint id) => _life with { ShotId = id };
    private static FireEvent Fire(uint id, uint frame, BeamType weapon = BeamType.Missile)
        => new(id, frame, frame, 0, FireEventKind.PressFire, (byte)weapon, 0, 0,
            FireEvent.FlagPose, Vector3.Zero, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
            SourceLife: _life.LifeId);
    private static ShotProofReason Inspect(uint id, uint frame, int cost, int cooldown = 0,
        BeamType weapon = BeamType.Missile)
        => NetShotSourceJournal.Inspect(Key(id), Fire(id, frame, weapon), 1, cost, cooldown);
    private static void Spend(uint id, uint frame, int cost, int cooldown = 0,
        BeamType weapon = BeamType.Missile)
    {
        var fire = Fire(id, frame, weapon);
        Check(NetShotSourceJournal.Inspect(Key(id), fire, 1, cost, cooldown) == ShotProofReason.Verified,
            $"legal expenditure {id}/{frame}");
        NetShotSourceJournal.Fired(Key(id), fire, 1, cost, cooldown);
    }
    private static ShotProofSource State(uint frame)
    {
        Check(NetShotSourceJournal.TryState(_life, frame, out var state), $"available source frame {frame}");
        return state;
    }
    private static void Observe(uint frame)
    {
        var intent = new IntentPacket { AuthorityEpoch = _life.AuthorityEpoch, MatchId = _life.MatchId,
            SlotGeneration = _life.Generation, LifeId = _life.LifeId, Frame = frame };
        NetShotSourceJournal.Observe(0, intent);
    }
    private static void Ammo(uint frame, int amount)
    { Frame(frame - 1); NetShotSourceJournal.GrantAmmo(_player, 1, amount); }
    private static void Weapon(uint frame, BeamType weapon)
    { Frame(frame - 1); NetShotSourceJournal.GrantWeapon(_player, weapon, 1, 60); }

    private static void NativeGrants()
    {
        Prepare(); Ammo(100, 50);
        Check(State(99).Ammo1 == 60 && State(100).Ammo1 == 100, "native ammo grant uses boundary and cap");
        // A forged native reserve cannot change the independent operation amount.
        HealthShotTests.Field(_player, "_ammo", new[] { 99999, 99999 }); Ammo(101, 100);
        Check(State(101).Ammo1 == 100, "reported reserve cannot fund or exceed journal cap");
        Prepare(Source(ammo: 10, weapons: 1)); Weapon(100, BeamType.Missile);
        Check((State(99).Weapons & (1 << (int)BeamType.Missile)) == 0 && State(99).Ammo1 == 10,
            "pickup does not retroactively grant weapon or refill");
        Check(State(100).Ammo1 == 60 && (State(100).Weapons & (1 << (int)BeamType.Missile)) != 0,
            "resolved native weapon pickup grants ownership and refill toward60");
        Weapon(101, BeamType.Missile); Check(State(101).Ammo1 == 60, "repeat weapon pickup does not add60");
        Prepare(Source(ammo: 90)); Weapon(100, BeamType.Missile);
        Check(State(100).Ammo1 == 90, "weapon refill preserves higher reserve");
        Prepare(Source(ammo: 0, infinite: true)); Spend(1, 100, 500);
        Check(State(101).Ammo1 == 0, "authority infinite reserve is not debited");
    }
    private static void Chronology()
    {
        Prepare(Source(cap: 60)); Spend(2, 100, 40); Ammo(150, 50); Spend(3, 200, 50);
        Check(Inspect(1, 80, 30) == ShotProofReason.Ammunition,
            "older expenditure cannot overdraft before a future grant");
        Check(State(201).Ammo1 == 10, "refused earlier expenditure changes no later reserve");
        Spend(1, 80, 10);
        Check(State(149).Ammo1 == 10 && State(150).Ammo1 == 60 && State(201).Ammo1 == 10,
            "late legal expenditure replays cap-sensitive grants chronologically");
        Check(State(200).Ammo1 == 60, "source snapshot precedes same-frame expenditure");
        Prepare(Source(ammo: 50)); Spend(2, 100, 30); Spend(1, 100, 20);
        Check(Inspect(3, 100, 1) == ShotProofReason.Ammunition && State(101).Ammo1 == 0,
            "same-frame authored IDs share one reserve");
        Prepare(); Spend(1, 100, 20, 10);
        Check(Inspect(2, 99, 1, 10) == ShotProofReason.FireRate
            && Inspect(2, 109, 1, 10) == ShotProofReason.FireRate
            && Inspect(2, 110, 1, 10) == ShotProofReason.Verified,
            "reordered and forward expenditure respect exact cooldown boundary");
    }
    private static void DeduplicationAndOmega()
    {
        Prepare(); Spend(1, 100, 20);
        var fire = Fire(1, 100);
        NetShotSourceJournal.Fired(Key(1), fire, 1, 20, 0);
        Check(Inspect(1, 100, 20) == ShotProofReason.Verified && State(101).Ammo1 == 40,
            "same full key does not debit twice");
        Prepare(); Spend(2, 200, 0, 10, BeamType.OmegaCannon);
        Check(Inspect(1, 100, 0, 10, BeamType.OmegaCannon) == ShotProofReason.UnownedWeapon,
            "late older Omega cannot invalidate already fired later ownership");
        Check((State(201).Weapons & (1 << (int)BeamType.OmegaCannon)) == 0,
            "successful Omega expenditure consumes independent ownership");
        Check(Inspect(3, 220, 0, 10, BeamType.OmegaCannon) == ShotProofReason.UnownedWeapon,
            "consumed Omega cannot fire again without grant");
        Weapon(250, BeamType.OmegaCannon); Spend(3, 260, 0, 10, BeamType.OmegaCannon);
        Check((State(251).Weapons & (1 << (int)BeamType.OmegaCannon)) != 0,
            "later trusted pickup provides a distinct Omega grant");
    }
    private static void Compaction()
    {
        Prepare(Source(ammo: 100)); Spend(1, 343, 40, 10); Observe(600);
        Check(!NetShotSourceJournal.TryState(_life, 343, out _), "older than retained source window is unavailable");
        Check(State(344).Ammo1 == 60, "checkpoint preserves compacted expenditure");
        Check(Inspect(2, 344, 1, 10) == ShotProofReason.FireRate,
            "checkpoint preserves cooldown crossing retained floor");
        Check(Inspect(1, 343, 40, 10) == ShotProofReason.SourceHistoryIncomplete,
            "expired expenditure cannot be recertified or refunded");
        Spend(2, 353, 60, 10); Observe(700);
        Check(State(444).Ammo1 == 0 && Inspect(3, 500, 1) == ShotProofReason.Ammunition,
            "repeated compaction preserves permanent resource debit");
        Prepare(); Spend(1, 343, 0, 10, BeamType.OmegaCannon); Observe(600);
        Check(Inspect(2, 400, 0, 10, BeamType.OmegaCannon) == ShotProofReason.UnownedWeapon,
            "checkpoint preserves consumed Omega ownership");
        Observe(500); Check(!NetShotSourceJournal.TryState(_life, 343, out _),
            "reordered intent never moves retention floor backwards");
    }
    private static void IdentityAndUnavailable()
    {
        Prepare();
        foreach (var wrong in new[] { _life with { AuthorityEpoch = _life.AuthorityEpoch + 1 },
            _life with { MatchId = (ushort)(_life.MatchId + 1) },
            _life with { Generation = (ushort)(_life.Generation + 1) },
            _life with { LifeId = (ushort)(_life.LifeId + 1) } })
            Check(!NetShotSourceJournal.TryState(wrong, 100, out _), "source key fences authority/match/generation/life");
        var next = HealthShotTests.State((ushort)(_life.LifeId + 1), 99); next.SlotIndex = 0; next.SlotGeneration = _life.Generation;
        Check(NetPlayerLifecycle.AcceptState(next, 20), "fixture accepts actual next life");
        NetShotSourceJournal.Seed(_player, Source(ammo: 10));
        Check(State(100).Ammo1 == 60
            && NetShotSourceJournal.TryState(ShotKey.For(0, 0), 100, out var fresh) && fresh.Ammo1 == 10,
            "respawn source does not overwrite retained prior-life resources");
        Prepare();
        NetShotSourceJournal.Invalidate(_player);
        Check(!NetShotSourceJournal.TryState(_life, 100, out _)
            && Inspect(1, 100, 0) == ShotProofReason.SourceChanged, "unsupported fact invalidates source explicitly");
        Prepare(Source(supported: false));
        Check(!NetShotSourceJournal.TryState(_life, 100, out _)
            && Inspect(1, 100, 0) == ShotProofReason.SourceUnavailable, "unsupported seed cannot certify resource state");
        Prepare(); typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, -1);
        NetSession.RemoteIntentValid[0] = false;
        NetShotSourceJournal.GrantAmmo(_player, 1, 50);
        Check(!NetShotSourceJournal.TryState(_life, 100, out _), "unknown owner clock cannot place grant in history");
        Prepare();
        for (uint i = 0; i < NetShotSourceJournal.Capacity + 1; i++) Ammo(100 + i, 0);
        Check(!NetShotSourceJournal.TryState(_life, 1000, out _), "bounded operation overflow never verifies truncated history");
    }
    private static void PrimeTransitions()
    {
        Prepare(); var scene = _player.OwningScene;
        var entities = (LinkedList<EntityBase>)typeof(Scene).GetField("_entities",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scene)!;
        var node = entities.AddLast(_player); int saved = scene.GameState.PrimeHunter;
        try
        {
            Frame(99); scene.GameState.PrimeHunter = 0; NetShotSourceJournal.PrimeChanged(scene);
            Check(!State(99).Prime && State(100).Prime, "canonical Prime transition has independent source boundary");
            Frame(100); scene.GameState.PrimeHunter = 1; NetShotSourceJournal.PrimeChanged(scene);
            Check(State(100).Prime && !State(101).Prime, "canonical Prime transfer preserves prior modifier history");
        }
        finally { entities.Remove(node); scene.GameState.PrimeHunter = saved; }
    }
    private static void TimedModifier()
    {
        Prepare(); Frame(99); NetShotSourceJournal.SetDouble(_player, 2);
        NetShotSourceJournal.Tick(_player);
        Check(!State(99).Double && State(100).Double, "double grant starts after its native source boundary");
        // Clearing native timer via client shot state must not stop authority expiry.
        HealthShotTests.Field(_player, "_doubleDmgTimer", (ushort)0);
        Frame(100); NetShotSourceJournal.Tick(_player);
        Check(State(100).Double && !State(101).Double,
            "independent native ticks expire double at exact boundary");
        Frame(199); NetShotSourceJournal.SetDouble(_player, 2);
        _player.Health = 0; Frame(200); NetShotSourceJournal.Tick(_player);
        Check(!State(201).Double, "death clears authority double state");
    }
    public static int Run()
    {
        bool enabled = NetHitClaims.Enabled; var mode = NetShotProof.Mode;
        try
        {
            NativeGrants(); Chronology(); DeduplicationAndOmega(); Compaction();
            IdentityAndUnavailable(); PrimeTransitions(); TimedModifier();
            Console.WriteLine($"PASS: {_checks} shot source journal assertions (production code, no assets)");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { NetSession.Stop(); NetShotSourceJournal.Reset(); NetHitClaims.Enabled = enabled; NetShotProof.Mode = mode; }
    }
}
