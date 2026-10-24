using System;
using System.IO;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Mods.Network;

namespace MphRead.Mods.Oddball;

/// <summary>Fixed-60Hz team possession rules. Test v1 is deliberately offline-only.</summary>
public sealed class OddballMatch
{
    public const string RoomKey = "MP3 PROVING GROUND";
    public const int Rate = 60, Goal = 100, Wins = 2, RoundTicks = 300 * Rate;
    public string EventText { get; private set; } = "";
    public int EventTicks { get; private set; }
    private bool _announced;
    private int _lastLead;
    internal void Present(Scene scene, string text, string? cue = null, bool priority = false)
    {
        EventText = text; EventTicks = 180;
        if (cue != null) OddballAudio.Queue(scene, cue, priority);
    }
    public int[] ScoreTicks { get; } = new int[2];
    public int[] RoundsWon { get; } = new int[2];
    public int[] HoldTicks { get; } = new int[8];
    public int RoundNumber { get; private set; } = 1;
    public int RemainingTicks { get; private set; } = RoundTicks;
    public int SpawnTicks { get; private set; } = 180;
    public int PauseTicks { get; private set; }
    public int Winner { get; private set; } = -1;
    public int LastRoundWinner { get; private set; } = -1;
    public bool Overtime { get; private set; }
    public bool IsIntermission => PauseTicks > 0 || Winner >= 0;
    public bool CanPickUp => SpawnTicks == 0 && !IsIntermission;
    public int Score(int team) => (uint)team < 2 ? ScoreTicks[team] / Rate : 0;
    internal byte[] CaptureSnapshot()
    {
        using var stream=new MemoryStream();using var w=new BinaryWriter(stream);
        w.Write(RoundNumber);w.Write(RemainingTicks);w.Write(SpawnTicks);w.Write(PauseTicks);w.Write(Winner);w.Write(LastRoundWinner);w.Write(Overtime);
        foreach(int n in ScoreTicks)w.Write(n);foreach(int n in RoundsWon)w.Write(n);foreach(int n in HoldTicks)w.Write(n);
        return stream.ToArray();
    }
    internal void ApplySnapshot(byte[] bytes)
    {
        using var stream=new MemoryStream(bytes,false);using var reader=new BinaryReader(stream);
        int round=reader.ReadInt32(),remaining=reader.ReadInt32(),spawn=reader.ReadInt32(),pause=reader.ReadInt32(),winner=reader.ReadInt32(),last=reader.ReadInt32();bool overtime=reader.ReadBoolean();
        int[] score=[reader.ReadInt32(),reader.ReadInt32()],wins=[reader.ReadInt32(),reader.ReadInt32()];var holds=new int[8];for(int i=0;i<8;i++)holds[i]=reader.ReadInt32();
        if(stream.Position!=stream.Length || round is <1 or >3 || remaining is <0 or >RoundTicks || spawn is <0 or >180 || pause is <0 or >300 || winner is <-1 or >1 || last is <-1 or >1
            || Array.Exists(score,n=>n<0||n>Goal*Rate) || Array.Exists(wins,n=>n<0||n>Wins) || Array.Exists(holds,n=>n<0))throw new InvalidDataException("Invalid Oddball authority state.");
        RoundNumber=round;RemainingTicks=remaining;SpawnTicks=spawn;PauseTicks=pause;Winner=winner;LastRoundWinner=last;Overtime=overtime;
        score.CopyTo(ScoreTicks,0);wins.CopyTo(RoundsWon,0);holds.CopyTo(HoldTicks,0);
    }
    public int CompareTeams(int a, int b) => (uint)a >= 2 || (uint)b >= 2 ? 0
        : RoundsWon[a] != RoundsWon[b] ? RoundsWon[a].CompareTo(RoundsWon[b]) : ScoreTicks[a].CompareTo(ScoreTicks[b]);

    /// <returns>true only at the next-round reset boundary.</returns>
    public bool Step(int carrierSlot, int carrierTeam)
    {
        if (Winner >= 0) return false;
        if (PauseTicks > 0)
        {
            if (--PauseTicks != 0) return false;
            Array.Clear(ScoreTicks); RemainingTicks = RoundTicks; SpawnTicks = 180;
            Overtime = false; RoundNumber++; return true;
        }
        if (SpawnTicks > 0) SpawnTicks--;
        if (carrierSlot >= 0 && carrierSlot < 8 && carrierTeam >= 0 && carrierTeam < 2 && SpawnTicks == 0)
        {
            HoldTicks[carrierSlot]++; ScoreTicks[carrierTeam]++;
            if (Score(carrierTeam) >= Goal) FinishRound(carrierTeam);
            else if (Overtime && Score(0) != Score(1)) FinishRound(Score(0) > Score(1) ? 0 : 1);
        }
        else if (RemainingTicks > 0)
        {
            RemainingTicks--;
            if (RemainingTicks == 0)
            {
                if (Score(0) != Score(1)) FinishRound(Score(0) > Score(1) ? 0 : 1);
                else Overtime = true; // Explicit sudden-death test rule, not certified Infinite parity.
            }
        }
        return false;
    }

    private void FinishRound(int team)
    {
        LastRoundWinner = team;
        if (++RoundsWon[team] >= Wins) Winner = team;
        else PauseTicks = 300;
    }

    public void Tick(Scene scene)
    {
        if (scene.Services.IsReplica || NetObjectiveSync.IsClient(scene)) return;
        OctolithFlagEntity? skull = null;
        foreach (var flag in scene.GetOctolithFlagEntities()) { skull = flag; break; }
        var carrier = skull?.Carrier;
        carrier?.ModOddballLogCarrier();
        if (carrier != null && (carrier.Health == 0 || carrier.IsAltForm || carrier.IsMorphing
            || !carrier.LoadFlags.TestFlag(LoadFlags.Active) || (uint)carrier.TeamIndex >= 2))
        { skull!.OddballDrop(); carrier = null; }
        if (EventTicks > 0) EventTicks--;
        if (!_announced) { _announced = true; Present(scene, "ODDBALL - HOLD THE BALL TO SCORE", "oddball"); }
        int oldSpawn = SpawnTicks, oldClock = RemainingTicks;
        bool oldOvertime = Overtime;
        int oldRound = RoundsWon[0] + RoundsWon[1];
        bool reset = Step(carrier?.SlotIndex ?? -1, carrier?.TeamIndex ?? -1);
        if (oldRound != RoundsWon[0] + RoundsWon[1])
        {
            // Retire offensive entities before the intermission ends. Snapshot the
            // bomb list because Destroy unlinks each bomb from its scene registry.
            var bombs = new List<BombEntity>();
            foreach (var bomb in scene.GetBombEntities()) bombs.Add(bomb);
            foreach (var bomb in bombs) bomb.Destroy();
            foreach (var beam in scene.GetBeamProjectileEntities()) beam.Lifespan = 0;
            skull?.OddballReset();
            Present(scene, $"{(LastRoundWinner == 0 ? "RED" : "BLUE")} WINS {(Winner >= 0 ? "THE MATCH" : "THE ROUND")}",
                Winner >= 0 ? "game_over" : "round_over", priority: true);
        }
        if (oldSpawn > 0 && SpawnTicks == 0 && !IsIntermission)
            Present(scene, "PLAY BALL", "play_ball");
        if (!oldOvertime && Overtime) Present(scene, "SUDDEN DEATH - NEXT POINT WINS");
        if (!IsIntermission && !reset && oldClock != RemainingTicks)
        {
            string? warning = RemainingTicks switch { 3600 => "one_minute_remaining", 1800 => "thirty_seconds_remaining", 600 => "ten_seconds_remaining", _ => null };
            if (warning != null) OddballAudio.Queue(scene, warning);
        }
        int lead = Math.Sign(Score(0) - Score(1));
        if (lead != 0 && lead != _lastLead && !IsIntermission && !reset)
        {
            bool winning = lead > 0 ? scene.Players.Main.TeamIndex == 0 : scene.Players.Main.TeamIndex == 1;
            OddballAudio.Queue(scene, winning ? "gained_the_lead" : "lost_the_lead");
            _lastLead = lead;
        }
        OddballAudio.Tick(scene);
        if (reset)
        {
            _lastLead = 0;
            Present(scene, $"ROUND {RoundNumber} - GET READY");
            skull?.OddballReset();
            foreach (var player in scene.GetPlayerEntities())
                if (player.LoadFlags.TestFlag(LoadFlags.Active)) player.ModOddballRoundRespawn();
        }
        for (int i = 0; i < HoldTicks.Length; i++) scene.GameState.Time[i] = HoldTicks[i] / (float)Rate;
        scene.GameState.MatchTime = Winner >= 0 ? 0 : Math.Max(1f / Rate, RemainingTicks / (float)Rate);
    }
}
