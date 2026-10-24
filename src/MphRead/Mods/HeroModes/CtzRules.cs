using System;
using System.IO;
using System.Linq;
using OpenTK.Mathematics;
using MphRead.Entities;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

namespace MphRead.Mods.HeroModes;

public static class CtzRules
{
    public const uint Capability = 0x355a5443; // CTZ5: authoritative full-match support/damage scoreboard totals.
    public const int RulesetVersion = 5, CaptureLimit = 2;
    public const int RoundStartBannerTicks = 3 * 60;
    public const ushort HealedAllyRevealTicks = 120;
    public const ushort MaximumHealedAllyRevealTicks = HealingAuraTicks + HealedAllyRevealTicks;
    public static bool IsSupportHunter(Hunter hunter) => hunter is Hunter.Noxus or Hunter.Weavel or Hunter.Spire;
    public const ushort NoxusFreezeCooldownTicks = 30 * 60;
    public const ushort HealingAuraTicks = 150 * 2; // Native multiplayer Magmaul burn duration.
    public const int NoxusBodyHeal = 9, NoxusHeadHeal = 15, NoxusFreezeHeal = 30;
    public const int WeavelDirectHeal = 4, WeavelSplashHeal = 3;
    public const int SpireNormalHeal = 3, SpireTotalHeal = 20;
    public const byte SpireAuraPulses = 5;
    public const int SpireHealPerPulse = SpireTotalHeal / SpireAuraPulses;
    public const float BattlehammerDamageMultiplier = .75f;
    public static ushort RespawnTicks(Hunter hunter) => hunter switch
    {
        Hunter.Trace => 20 * 60,
        Hunter.Kanden or Hunter.Samus => 10 * 60,
        Hunter.Spire or Hunter.Weavel or Hunter.Sylux or Hunter.Noxus => 5 * 60,
        _ => throw new ArgumentOutOfRangeException(nameof(hunter))
    };
    public const int BurstRounds = 3, BurstSpacingTicks = 4, BurstRecoveryTicks = 16;
    public const int BurstPeriodTicks = 2 * BurstSpacingTicks + BurstRecoveryTicks;
    public const int CountdownTicks = 180, UnlockTicks = 600, CaptureTicks = 300;
    public const int WinningScoreTicks = 6000, IntermissionTicks = 300;
    public static uint ReducedDamage(uint damage, double reduction) => damage == 0 ? 0
        : (uint)Math.Max(1, Math.Ceiling(damage * (1 - Math.Clamp(reduction, 0, .95))));
    public static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
}

public enum CtzPhase : byte { Countdown, Active, Intermission, Finished }

/// <summary>One immutable, bounded authority fact. No player transform or client-owned state.</summary>
public readonly record struct CtzSnapshot(CtzPhase Phase, int Round, int Owner, int Capturing, int Capture,
    int Red, int Blue, int RedWins, int BlueWins, int Remaining, int Unlock, bool Contested, int RoundElapsedTicks = 0)
{
    public bool Valid => Enum.IsDefined(Phase) && Round is >= 1 and <= 3 && Owner is >= -1 and <= 1
        && Capturing is >= -1 and <= 1 && Capture is >= 0 and <= CtzRules.CaptureTicks
        && Red is >= 0 and <= CtzRules.WinningScoreTicks && Blue is >= 0 and <= CtzRules.WinningScoreTicks
        && RedWins is >= 0 and <= 2 && BlueWins is >= 0 and <= 2 && RedWins + BlueWins <= 3
        && Remaining is >= 0 and <= CtzRules.IntermissionTicks && Unlock is >= 0 and <= CtzRules.UnlockTicks
        && RoundElapsedTicks >= 0;
    public void Write(BinaryWriter w)
    {
        if (!Valid) throw new InvalidDataException("Invalid CTZ authority state.");
        w.Write((byte)Phase); w.Write((byte)Round); w.Write((sbyte)Owner); w.Write((sbyte)Capturing);
        w.Write((ushort)Capture); w.Write((ushort)Red); w.Write((ushort)Blue);
        w.Write((byte)RedWins); w.Write((byte)BlueWins); w.Write((ushort)Remaining); w.Write((ushort)Unlock); w.Write(Contested); w.Write(RoundElapsedTicks);
    }
    public static CtzSnapshot Read(BinaryReader r, bool hasRoundClock = true)
    {
        var v = new CtzSnapshot((CtzPhase)r.ReadByte(), r.ReadByte(), r.ReadSByte(), r.ReadSByte(),
            r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16(), r.ReadByte(), r.ReadByte(), r.ReadUInt16(), r.ReadUInt16(), r.ReadBoolean(),
            hasRoundClock ? r.ReadInt32() : 0);
        if (!v.Valid) throw new InvalidDataException("Invalid CTZ authority state.");
        return v;
    }
}

public sealed class CtzState
{
    public CtzSnapshot State { get; private set; } = Initial();
    private static CtzSnapshot Initial() => new(CtzPhase.Countdown,1,-1,-1,0,0,0,0,0,CtzRules.CountdownTicks,CtzRules.UnlockTicks,false);
    public void Reset() => State = Initial();
    public void Apply(CtzSnapshot v) { if (!v.Valid) throw new ArgumentException("Invalid CTZ state."); State = v; }
    public void Tick(int red, int blue)
    {
        if (red < 0 || blue < 0 || red > 4 || blue > 4) throw new ArgumentOutOfRangeException();
        var s=State;
        if (s.Phase==CtzPhase.Finished) return;
        if (s.Phase==CtzPhase.Countdown)
        {
            int t=Math.Max(0,s.Remaining-1);
            State=s with { Remaining=t, Phase=t==0 ? CtzPhase.Active : CtzPhase.Countdown }; return;
        }
        if (s.Phase==CtzPhase.Intermission)
        {
            int t=Math.Max(0,s.Remaining-1);
            State=t>0 ? s with { Remaining=t } : Initial() with { Round=s.Round+1, RedWins=s.RedWins, BlueWins=s.BlueWins };
            return;
        }
        // Authority-owned elapsed ROUND time: independent of occupancy and rendering.
        // It freezes in intermission, resets for each round and travels in objective snapshots.
        s=s with { RoundElapsedTicks=s.RoundElapsedTicks == int.MaxValue ? int.MaxValue : s.RoundElapsedTicks+1 };
        if (s.Unlock>0) { State=s with { Unlock=s.Unlock-1, Contested=false }; return; }
        bool contested=red>0 && blue>0;
        s=s with { Contested=contested };
        if (contested) { State=s; return; }
        int team=red>0 ? 0 : blue>0 ? 1 : -1;
        if (team<0) { State=s with { Capturing=-1,Capture=0 }; return; }
        if (s.Owner!=team)
        {
            int capture=s.Capturing==team ? s.Capture+1 : 1;
            s=s with { Capturing=team,Capture=capture };
            if (capture>=CtzRules.CaptureTicks) s=s with { Owner=team,Capturing=-1,Capture=0 };
            State=s; return; // Scoring begins on the next fixed step, never on the capture step.
        }
        s=s with { Capturing=-1,Capture=0,Red=s.Red+(team==0?1:0),Blue=s.Blue+(team==1?1:0) };
        if (s.Red>=CtzRules.WinningScoreTicks || s.Blue>=CtzRules.WinningScoreTicks)
        {
            int rw=s.RedWins+(team==0?1:0),bw=s.BlueWins+(team==1?1:0);
            s=s with { RedWins=rw,BlueWins=bw,Phase=rw>=2||bw>=2 ? CtzPhase.Finished : CtzPhase.Intermission,Remaining=CtzRules.IntermissionTicks };
        }
        State=s;
    }
}

public static class CtzRuntime
{
    private sealed class DefinitionCache { public ulong Frame=ulong.MaxValue; public string? Room; public CaptureZoneDefinition? Value; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Scene,DefinitionCache> Definitions=new();
    public static CaptureZoneDefinition? Definition(Scene scene)
    {
        var cached=Definitions.GetValue(scene,static _=>new DefinitionCache());
        var room=scene.Room?.Meta.Name;
        if(cached.Frame!=scene.FrameCount || cached.Room!=room)
        {
            cached.Frame=scene.FrameCount;cached.Room=room;
            cached.Value=CustomRooms.Definitions.FirstOrDefault(m=>m.Name==room)?.CaptureZone;
        }
        return cached.Value;
    }
    public static bool Locked(Scene scene) => HeroRules.IsZone(scene.GameState.Mode)
        && scene.CaptureZone.State.Phase != CtzPhase.Active;
    public static void Tick(Scene scene)
    {
        if (!HeroRules.IsZone(scene.GameState.Mode) || scene.Services.IsReplica || NetObjectiveSync.IsClient(scene)) return;
        var def=Definition(scene);
        if (def?.Valid != true) throw new InvalidOperationException("Capture the Zone requires an authored capture volume.");
        int red=0,blue=0;
        foreach (var p in scene.GetPlayerEntities())
        {
            if (p.Health<=0 || !p.ModIsInPlay || !p.LoadFlags.TestFlag(LoadFlags.Spawned)
                || p.ModZoneSpawnProtected || !def.Contains(p.Position)) continue;
            if (p.TeamIndex==0) red++; else if (p.TeamIndex==1) blue++;
        }
        if(scene.CaptureZone.State.Phase==CtzPhase.Active && scene.CaptureZone.State.Unlock==0)
            foreach(var player in scene.GetPlayerEntities())
                if(player.Health>0 && player.ModIsInPlay && player.LoadFlags.TestFlag(LoadFlags.Spawned) && !player.ModZoneSpawnProtected && def.Contains(player.Position))
                    scene.GameState.CtzStats.Objective(player.SlotIndex,secondary:red>0&&blue>0);
        int oldRound=scene.CaptureZone.State.Round;
        scene.CaptureZone.Tick(red,blue);
        var s=scene.CaptureZone.State;
        if (s.Round!=oldRound)
        {
            foreach(var beam in scene.GetBeamProjectileEntities()) beam.Lifespan=0;
            foreach(var p in scene.GetPlayerEntities())
                if(p.LoadFlags.TestFlag(LoadFlags.Active) && p.ModIsInPlay) p.ModZoneRoundRespawn();
        }
        scene.GameState.TeamPoints[0]=s.RedWins; scene.GameState.TeamPoints[1]=s.BlueWins;
        if(s.Phase==CtzPhase.Finished) scene.GameState.MatchTime=0;
    }
}
