using System.Numerics;
namespace ProjectPrime.PayloadEscort;
public sealed record PresenceResult(IReadOnlyList<PlayerSample> Attackers,int DefenderCount) {
    public bool Contested=>DefenderCount>0;
}
public static class Presence {
    public static PresenceResult Evaluate(IEnumerable<PlayerSample> samples,Team attacker,Vector3 cart,EscortRules rules) {
        var input=samples.ToArray();
        if(input.Length>32)throw new ArgumentException("Unbounded occupancy batch.");
        // Detached entities are ignored before deduplication. A turret cannot hide a player body.
        var bodies=input.Where(p=>p.Body!=BodyKind.DetachedEntity).ToArray();
        if(bodies.Count(p=>p.Team==Team.Alpha)>4 || bodies.Count(p=>p.Team==Team.Bravo)>4)throw new ArgumentException("A team may contain at most four players.");
        if(bodies.Length>8)throw new ArgumentException("The agreed player cap is eight.");
        var seen=new HashSet<Guid>();var attackers=new List<PlayerSample>();int defenders=0;
        foreach(var p in bodies){
            if(p.Key.OccupantId==Guid.Empty || p.Key.SlotGeneration<0 || p.Key.LifeId<0
                || !Enum.IsDefined(p.Team) || !Enum.IsDefined(p.Body))throw new ArgumentException("Invalid authoritative body identity.");
            if(!seen.Add(p.Key.OccupantId))throw new ArgumentException("Duplicate occupant: use exactly one current body per player.");
            if(!float.IsFinite(p.Position.X)||!float.IsFinite(p.Position.Y)||!float.IsFinite(p.Position.Z)
                || p.MaximumHealth<=0 || p.Health<0 || p.Health>p.MaximumHealth)throw new ArgumentException("Invalid body sample.");
            if(!p.Connected||!p.Alive||p.Health==0||p.Spectator||p.Protected||!p.ObjectiveVisible)continue;
            Vector3 offset=p.Position-cart;
            if(Math.Abs(offset.Y)>rules.PresenceHalfHeight || offset.X*offset.X+offset.Z*offset.Z>rules.PresenceRadius*rules.PresenceRadius)continue;
            if(p.Team==attacker)attackers.Add(p);else defenders++;
        }
        return new PresenceResult(attackers.AsReadOnly(),defenders);
    }
}
