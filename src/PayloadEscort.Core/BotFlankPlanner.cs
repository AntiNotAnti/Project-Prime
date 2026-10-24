using System.Numerics;
namespace ProjectPrime.PayloadEscort;

public sealed record FlankPath(string Name,IReadOnlyList<Vector3> Points);
/// <summary>Persistent objective goals only. Actual navigation/combat remains in the native bot AI.</summary>
public sealed class BotFlankPlanner {
    private sealed class Plan(FlankPath path) { public FlankPath Path { get; }=path; public int Index; }
    private readonly PayloadRoute _route;
    private readonly FlankPath[] _flanks;
    private readonly Dictionary<PlayerKey,Plan> _plans=new();
    private readonly Dictionary<PlayerKey,HashSet<string>> _completed=new();
    private (Guid Match,int Round)? _round;
    private PlayerKey? _escort;
    public BotFlankPlanner(PayloadRoute route,IEnumerable<FlankPath> flanks){
        _route=route;_flanks=flanks.Select(f=>new FlankPath(f.Name,Array.AsReadOnly(f.Points.ToArray()))).ToArray();
        if(_flanks.Length>128 || _flanks.Any(f=>string.IsNullOrWhiteSpace(f.Name)||f.Points.Count<2||f.Points.Count>128
            ||f.Points.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z))))throw new ArgumentException("Invalid flank paths.");
        if(_flanks.Select(f=>f.Name).Distinct(StringComparer.Ordinal).Count()!=_flanks.Length)throw new ArgumentException("Duplicate flank name.");
    }
    public IReadOnlyList<BotDirective> PlanGoals(EscortRound round,IEnumerable<PlayerSample> input){
        if(_round!=(round.MatchId,round.RoundId)){_plans.Clear();_completed.Clear();_escort=null;_round=(round.MatchId,round.RoundId);}
        var living=input.Where(p=>p.Connected&&p.Alive&&p.Health>0&&!p.Spectator&&p.Body!=BodyKind.DetachedEntity).OrderBy(p=>p.Key.OccupantId).ToArray();
        var valid=living.Select(p=>p.Key).ToHashSet();
        foreach(var key in _plans.Keys.Where(k=>!valid.Contains(k)).ToArray())_plans.Remove(key);
        foreach(var key in _completed.Keys.Where(k=>!valid.Contains(k)).ToArray())_completed.Remove(key);
        var cart=_route.Pose(round.Progress).Position;
        var attackers=living.Where(p=>p.Team==round.Attacker).ToArray();
        if(_escort is null || !attackers.Any(p=>p.Key==_escort))_escort=attackers.OrderBy(p=>Vector3.DistanceSquared(p.Position,cart)).FirstOrDefault()?.Key;
        var output=new List<BotDirective>();
        foreach(var p in living){
            if(round.Phase!=RoundPhase.Running){output.Add(new(p.Key,round.Phase==RoundPhase.Preparation?BotTask.WaitForRelease:BotTask.HoldResults,p.Position));continue;}
            if(p.Key==_escort){_plans.Remove(p.Key);output.Add(new(p.Key,BotTask.Escort,cart));continue;}
            if(p.Team!=round.Attacker){_plans.Remove(p.Key);output.Add(new(p.Key,BotTask.Contest,cart));continue;}
            if(!_completed.TryGetValue(p.Key,out var done)){done=new(StringComparer.Ordinal);_completed[p.Key]=done;}
            if(_plans.TryGetValue(p.Key,out var old) && _route.Project(old.Path.Points[^1])<round.Progress){_plans.Remove(p.Key);done.Add(old.Path.Name);}
            if(!_plans.ContainsKey(p.Key)){
                var selected=_flanks.Where(f=>!done.Contains(f.Name) && _route.Project(f.Points[^1])>=round.Progress+2*EscortRules.DistanceScale
                    && _route.Project(f.Points[0])<=round.Progress+35*EscortRules.DistanceScale
                    && _route.Project(f.Points[^1])<=round.Progress+70*EscortRules.DistanceScale)
                    .OrderBy(f=>Vector3.DistanceSquared(p.Position,f.Points[0])).ThenBy(f=>f.Name,StringComparer.Ordinal).FirstOrDefault();
                if(selected is not null)_plans[p.Key]=new Plan(selected);
            }
            if(_plans.TryGetValue(p.Key,out var plan)){
                while(plan.Index<plan.Path.Points.Count && Vector3.DistanceSquared(p.Position,plan.Path.Points[plan.Index])<2.25f)plan.Index++;
                if(plan.Index<plan.Path.Points.Count){output.Add(new(p.Key,BotTask.Flank,plan.Path.Points[plan.Index]));continue;}
                done.Add(plan.Path.Name);_plans.Remove(p.Key);
            }
            output.Add(new(p.Key,BotTask.Intercept,_route.Pose(round.Progress+8*EscortRules.DistanceScale).Position));
        }
        return output.AsReadOnly();
    }
}
