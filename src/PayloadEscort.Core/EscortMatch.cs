namespace ProjectPrime.PayloadEscort;
public sealed record MatchOutcome(Team? Winner,bool Draw);
public sealed record BankPair(int Alpha,int Bravo,Team FirstAttacker);
public sealed class EscortMatch {
    private readonly PayloadRoute _route;
    private readonly EscortRules _rules;
    private readonly List<RoundResult> _history=new();
    private RoundResult? _first;
    private bool _second;
    private int _alphaBank,_bravoBank;
    public Guid MatchId { get; }
    public EscortRound Current { get; private set; }
    public int PairIndex { get; private set; }
    public int AlphaBank=>_alphaBank;
    public int BravoBank=>_bravoBank;
    public MatchOutcome? Outcome { get; private set; }
    public IReadOnlyList<RoundResult> History=>_history.AsReadOnly();
    public EscortMatch(Guid matchId,PayloadRoute route,EscortRules rules){
        MatchId=matchId;_route=route;_rules=rules;
        Current=new(matchId,0,Team.Alpha,route,rules);
    }
    public static BankPair NormalizeBanks(int alpha,int bravo,EscortRules rules){
        if(alpha<0||bravo<0)throw new ArgumentException("Negative time bank.");rules.Validate();
        int min=Math.Min(alpha,bravo);
        int delta=min<rules.MinimumBankTicks?rules.MinimumBankTicks-min:
            min>rules.MaximumLowerBankTicks?rules.MaximumLowerBankTicks-min:0;
        return new(checked(alpha+delta),checked(bravo+delta),alpha<=bravo?Team.Alpha:Team.Bravo);
    }
    // Call once world reset/results lifecycle is ready. Never called from client input.
    public bool AdvanceAfterResults(){
        if(Outcome is not null)return false;
        var result=Current.Result??throw new InvalidOperationException("Round is still running.");
        _history.Add(result);
        if(!_second){
            _first=result;_second=true;
            Team next=result.Attacker==Team.Alpha?Team.Bravo:Team.Alpha;
            int? bank=PairIndex==0?null:next==Team.Alpha?_alphaBank:_bravoBank;
            Current=new(MatchId,Current.RoundId+1,next,_route,_rules,bank,PairIndex>0,result.Delivered?null:result.BestDistance);
            return true;
        }
        var first=_first!;
        if(first.Delivered && result.Delivered){
            int alpha=first.Attacker==Team.Alpha?first.RemainingTicks:result.RemainingTicks;
            int bravo=first.Attacker==Team.Bravo?first.RemainingTicks:result.RemainingTicks;
            var bank=NormalizeBanks(alpha,bravo,_rules);_alphaBank=bank.Alpha;_bravoBank=bank.Bravo;
            PairIndex++;_second=false;_first=null;
            Current=new(MatchId,Current.RoundId+1,bank.FirstAttacker,_route,_rules,
                bank.FirstAttacker==Team.Alpha?bank.Alpha:bank.Bravo,true);
            return true;
        }
        // Any difference below the same winning margin is a draw; comparison matches live target check.
        long difference=result.BestDistance-first.BestDistance;
        int checkpointDifference=result.Checkpoints-first.Checkpoints;
        Outcome=checkpointDifference!=0?new(checkpointDifference>0?result.Attacker:first.Attacker,false):
            Math.Abs(difference)<_rules.WinningMargin?new(null,true):
            new(difference>0?result.Attacker:first.Attacker,false);
        return true;
    }
}
