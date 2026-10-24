namespace ProjectPrime.PayloadEscort;

/// <summary>Authority-only 60 Hz objective domain. No player movement, weapons, HUD, or respawn code.</summary>
public sealed class EscortRound {
    private readonly EscortRules _rules;
    private readonly PayloadRoute _route;
    private readonly Dictionary<PlayerKey,int> _heal=new();
    private readonly List<DomainEvent> _events=new();
    private long _lastTouch=long.MinValue/2;
    private int _abandonedTicks;
    private int _sequence;
    public Guid MatchId { get; }
    public int RoundId { get; }
    public Team Attacker { get; }
    public bool ExtraRound { get; }
    public long? Target { get; }
    public long Tick { get; private set; }
    public RoundPhase Phase { get; private set; }
    public int SetupRemaining { get; private set; }
    public int RemainingTicks { get; private set; }
    public long Progress { get; private set; }
    public long BestProgress { get; private set; }
    public int CapturedCheckpoints { get; private set; }
    public int HoldRemaining { get; private set; }
    public bool Overtime { get; private set; }
    public long OvertimeAge { get; private set; }
    public int FuseRemaining { get; private set; }
    public bool Contested { get; private set; }
    public int Escorts { get; private set; }
    public RoundResult? Result { get; private set; }
    public long RollbackFloor=>CapturedCheckpoints==0?0:_route.Checkpoints[CapturedCheckpoints-1].Distance;
    public EscortRound(Guid matchId,int roundId,Team attacker,PayloadRoute route,EscortRules rules,
        int? allottedTicks=null,bool extraRound=false,long? target=null) {
        rules.Validate();
        if(matchId==Guid.Empty||roundId<0||!Enum.IsDefined(attacker)||allottedTicks is <=0
            || allottedTicks>24*60*60*60 || target is <0 || target>=route.Length)throw new ArgumentException("Invalid round start.");
        MatchId=matchId;RoundId=roundId;Attacker=attacker;_route=route;_rules=rules;ExtraRound=extraRound;Target=target;
        RemainingTicks=allottedTicks??rules.InitialTicks;SetupRemaining=rules.PreparationTicks;
        Phase=SetupRemaining>0?RoundPhase.Preparation:RoundPhase.Running;
    }
    public IReadOnlyList<DomainEvent> DrainEvents(){var copy=_events.ToArray();_events.Clear();return Array.AsReadOnly(copy);}
    private void Emit(EventKind kind,PlayerKey? player=null,int amount=0)=>_events.Add(new(MatchId,RoundId,Tick,_sequence++,kind,player,amount));
    public bool Step(long expectedTick,IEnumerable<PlayerSample> players){
        if(expectedTick!=Tick+1 || Phase==RoundPhase.Finished)return false;
        // Validate the entire input before mutating state; malformed samples fail atomically.
        var presence=Presence.Evaluate(players,Attacker,_route.Pose(Progress).Position,_rules);
        Tick=expectedTick;
        if(Phase==RoundPhase.Preparation){
            if(--SetupRemaining==0){Phase=RoundPhase.Running;Emit(EventKind.Released);}
            return true;
        }
        bool touched=presence.Attackers.Count>0;
        Escorts=presence.Attackers.Count;Contested=presence.Contested;
        if(touched){_lastTouch=Tick;_abandonedTicks=0;}else if(_abandonedTicks<int.MaxValue)_abandonedTicks++;
        int nextClock=Math.Max(0,RemainingTicks-1);
        if(HoldRemaining>0)HoldRemaining--;
        else if(touched&&!Contested){
            long next=Math.Min(_route.Length,Progress+_rules.Speed(Escorts));
            if(CapturedCheckpoints<2 && next>=_route.Checkpoints[CapturedCheckpoints].Distance){
                var checkpoint=_route.Checkpoints[CapturedCheckpoints++];
                next=checkpoint.Distance;HoldRemaining=checkpoint.HoldTicks;
                if(!ExtraRound)nextClock=checked(nextClock+checkpoint.AddedTicks);
                Emit(EventKind.CheckpointReached,amount:CapturedCheckpoints);
                if(nextClock>0&&Overtime){Overtime=false;OvertimeAge=0;FuseRemaining=0;Emit(EventKind.OvertimeEnded);}
            }
            Progress=next;BestProgress=Math.Max(BestProgress,Progress);
        }
        else if(!touched && _abandonedTicks>_rules.RollbackDelayTicks)
            Progress=Math.Max(RollbackFloor,Progress-_rules.RollbackPerTick);
        RemainingTicks=nextClock;
        if(Progress==_route.Length){Finish(EndReason.Delivered);return true;}
        if(Target is long target && (BestProgress>=target+_rules.WinningMargin || CapturedCheckpoints>_route.Checkpoints.Count(c=>c.Distance<=target))){Finish(EndReason.TargetBeaten);return true;}
        if(RemainingTicks==0){
            if(!Overtime){
                if(Tick-_lastTouch>_rules.OvertimeTouchGraceTicks){Finish(EndReason.TimeExpired);return true;}
                Overtime=true;OvertimeAge=0;FuseRemaining=_rules.OvertimeFuseTicks;Emit(EventKind.OvertimeStarted);
            }
            OvertimeAge++;
            int capacity=_rules.FuseAt(OvertimeAge);
            FuseRemaining=touched?capacity:Math.Min(FuseRemaining,capacity)-1;
            if(FuseRemaining<=0){FuseRemaining=0;Finish(EndReason.OvertimeExpired);return true;}
        }
        Heal(presence.Attackers);
        return true;
    }
    private void Heal(IReadOnlyList<PlayerSample> attackers){
        var eligible=attackers.Where(p=>p.Health<p.MaximumHealth).ToArray();
        var keys=eligible.Select(p=>p.Key).ToHashSet();
        foreach(var stale in _heal.Keys.Where(k=>!keys.Contains(k)).ToArray())_heal.Remove(stale);
        foreach(var p in eligible){
            int ticks=_heal.GetValueOrDefault(p.Key)+1;
            if(ticks>=_rules.HealIntervalTicks){Emit(EventKind.HealRequested,p.Key,1);ticks=0;}
            _heal[p.Key]=ticks;
        }
    }
    private void Finish(EndReason reason){
        Phase=RoundPhase.Finished;
        Result=new(Attacker,BestProgress,CapturedCheckpoints+(reason==EndReason.Delivered?1:0),
            reason==EndReason.Delivered,RemainingTicks,reason);
        _heal.Clear();Emit(EventKind.RoundFinished,amount:(int)reason);
    }
}
