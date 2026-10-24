using System.Numerics;
namespace ProjectPrime.PayloadEscort;
public enum BotTask { Escort, Contest, Intercept, Flank, WaitForRelease, HoldResults }
public sealed record BotDirective(PlayerKey Player,BotTask Task,Vector3 Target);
public static class BotObjectives {
    // Goal selection only. Native navigation/weapon AI are deliberately deferred to the final client's adapter.
    public static IReadOnlyList<BotDirective> Plan(EscortRound round,PayloadRoute route,IEnumerable<PlayerSample> samples){
        var living=samples.Where(p=>p.Connected&&p.Alive&&p.Health>0&&!p.Spectator&&p.Body!=BodyKind.DetachedEntity)
            .OrderBy(p=>p.Key.OccupantId).ToArray();
        var cart=route.Pose(round.Progress).Position;
        var attackers=living.Where(p=>p.Team==round.Attacker).OrderBy(p=>Vector3.DistanceSquared(p.Position,cart)).ToArray();
        var primary=attackers.FirstOrDefault();var result=new List<BotDirective>();
        foreach(var p in living){
            if(round.Phase==RoundPhase.Preparation){result.Add(new(p.Key,BotTask.WaitForRelease,p.Position));continue;}
            if(round.Phase==RoundPhase.Finished){result.Add(new(p.Key,BotTask.HoldResults,p.Position));continue;}
            bool escort=primary?.Key==p.Key;
            long offset=p.Team==round.Attacker?(escort?0:8*EscortRules.DistanceScale):4*EscortRules.DistanceScale;
            BotTask task=p.Team==round.Attacker?(escort?BotTask.Escort:BotTask.Intercept):(round.Contested?BotTask.Contest:BotTask.Intercept);
            result.Add(new(p.Key,task,route.Pose(round.Progress+offset).Position));
        }
        return result.AsReadOnly();
    }
}
