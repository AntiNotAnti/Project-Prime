using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using ProjectPrime.PayloadEscort;
namespace MphRead.Mods.HeroModes;

/// <summary>Native-world adapter. Player movement and hero balance stay with their existing owners.</summary>
internal sealed class PayloadRuntime
{
    private EscortMatch? _match;
    private MapDefinition? _definition;
    private PayloadMap? _map;
    private PayloadEntity? _cargo;
    private int _intermission;
    private readonly List<CtzSpawnRoomDefinition> _roomCache=new(2);
    private int _roomStage=-1,_roomAttacker=-1;
    private BotFlankPlanner? _botPlanner;
    private readonly Dictionary<PlayerKey,BotDirective> _botGoals=new();
    private readonly List<PlayerSample> _occupants=new(8);
    private readonly Dictionary<PlayerKey,PlayerEntity> _players=new(8);
    private readonly Guid _offlineMatchId=Guid.NewGuid();
    internal PayloadFrame? Frame { get; private set; }
    internal PayloadMap? Map=>_map;
    internal static readonly EscortRules Rules=EscortProfiles.PayloadDistrictV12; // Objective pacing only; hero definitions are unchanged.
    internal void Reset() { _match=null;_map=null;_definition=null;_cargo=null;_intermission=0;Frame=null;_roomCache.Clear();_roomStage=_roomAttacker=-1;_occupants.Clear();_players.Clear();_botPlanner=null;_botGoals.Clear(); }
    internal void Ensure(Scene scene)
    {
        if(!HeroRules.IsPayload(scene.GameState.Mode))return;
        var definition=CustomRooms.Definitions.FirstOrDefault(d=>d.Name==scene.Room?.Meta.Name);
        if(definition==null)return;
        if(_definition!=definition)
        {
            Reset();_definition=definition;_map=PayloadMap.Get(definition);
            _match=new EscortMatch(_offlineMatchId,_map.Route,Rules);
            UpdateFrame();
        }
        if(_cargo==null)
        {
            _cargo=new PayloadEntity(scene,this,_map!);
            scene.AddEntity(_cargo);
        }
    }
    internal static void Tick(Scene scene)
    {
        if(!HeroRules.IsPayload(scene.GameState.Mode))return;
        var self=scene.Payload;self.Ensure(scene);
        if(self._map==null || self._match==null || scene.Services.IsReplica || NetObjectiveSync.IsClient(scene))return;
        if(scene.GameState.MatchState!=MatchState.InProgress)return;
        var match=self._match;var current=match.Current;
        if(match.Outcome!=null){self.ApplyScores(scene);return;}
        if(current.Phase==RoundPhase.Finished)
        {
            if(++self._intermission<300)return;
            self._intermission=0;match.AdvanceAfterResults();self.UpdateFrame();
            if(match.Outcome!=null){self.ApplyScores(scene);scene.GameState.MatchTime=0;return;}
            // Destroy old-round bombs while their owners still have the old-life links.
            // Respawn clears SyluxBombs; leaving the linked native entities alive made
            // the first post-swap draw dereference a null link and close the client.
            self.ClearRoundCombat(scene);
            foreach(var p in scene.GetPlayerEntities())
                if(p.ModIsInPlay && p.LoadFlags.TestFlag(LoadFlags.Active))p.ModZoneRoundRespawn();
            return;
        }
        self._occupants.Clear();self._players.Clear();
        var cart=self.Position;
        foreach(var p in scene.GetPlayerEntities())
        {
            if(p.TeamIndex is <0 or >1 || p.Health<=0 || !p.ModIsInPlay || !p.LoadFlags.TestFlag(LoadFlags.Spawned))continue;
            var key=Key(p);self._players[key]=p;
            bool protectedPlayer=p.ModZoneSpawnProtected || CtzSpawnRooms.InsideFriendly(p);
            bool visible=Visible(scene,p.Position.AddY(.65f),cart.AddY(.65f));
            self._occupants.Add(new(key,(ProjectPrime.PayloadEscort.Team)p.TeamIndex,p.IsAltForm?BodyKind.AlternateForm:BodyKind.Biped,
                new(p.Position.X,p.Position.Y,p.Position.Z),Protected:protectedPlayer,ObjectiveVisible:visible,
                Health:p.Health,MaximumHealth:HeroRules.Definition(p.Hunter).MaxHealth));
        }
        if(current.Phase==RoundPhase.Running)
            foreach(var sample in self._occupants)
                if(!sample.Protected && sample.ObjectiveVisible && System.Numerics.Vector3.DistanceSquared(sample.Position,new(cart.X,cart.Y,cart.Z))<=Rules.PresenceRadius*Rules.PresenceRadius
                    && Math.Abs(sample.Position.Y-cart.Y)<=Rules.PresenceHalfHeight && self._players.TryGetValue(sample.Key,out var occupant))
                    scene.GameState.CtzStats.Objective(occupant.SlotIndex,secondary:(int)sample.Team!=(int)current.Attacker);
        current.Step(current.Tick+1,self._occupants);
        foreach(var ev in current.DrainEvents())
            if(ev.Kind==EventKind.HealRequested && ev.Player is {} key && self._players.TryGetValue(key,out var player)
                && Key(player)==key)player.ModGainCtzHealth(ev.Amount);
        if(current.Tick%15==0 || self._botGoals.Count==0)
        {
            self._botPlanner??=new BotFlankPlanner(self._map.Route,self._map.Flanks);
            self._botGoals.Clear();foreach(var directive in self._botPlanner.PlanGoals(current,self._occupants))self._botGoals[directive.Player]=directive;
        }
        self.UpdateFrame();self.ApplyScores(scene);
    }
    private void ClearRoundCombat(Scene scene)
    {
        // This runs at the mode-state boundary, before the native entity update pass.
        // Use an owned list: Destroy returns pooled bombs and removes owner links.
        var bombs = new List<BombEntity>();
        foreach (var entity in scene.Entities)
        {
            if (entity is BeamProjectileEntity beam) beam.Lifespan = 0;
            else if (entity is BombEntity bomb) bombs.Add(bomb);
        }
        foreach (var bomb in bombs)
        {
            bomb.Destroy(); // Publishes canonical Removed (never a detonation) on hosts.
            scene.RemoveEntity(bomb);
        }
        _botGoals.Clear(); _occupants.Clear(); _players.Clear();
        _roomStage = _roomAttacker = -1;
    }
    private static PlayerKey Key(PlayerEntity p)=>new(new Guid(p.SlotIndex+1,0,0,new byte[8]),NetPlayerLifecycle.Generation(p.SlotIndex),NetPlayerLifecycle.Get(p.SlotIndex));
    internal Vector3 BotGoal(PlayerEntity player)
    {
        if(Frame is not {} state)return player.Position;
        if(state.Phase==0)return player.TeamIndex==state.Attacker?player.Position:Position+Forward*8;
        if(_botGoals.TryGetValue(Key(player),out var directive) && directive.Task==BotTask.Flank)
            return new(directive.Target.X,directive.Target.Y,directive.Target.Z);
        // Stop outside the physical hull rather than steering into its center.
        return Position+Forward*(player.TeamIndex==state.Attacker?-3.1f:3.1f);
    }
    internal static bool Visible(Scene scene,Vector3 from,Vector3 to)
    {
        // Static world segment, not cargo collision: the cart itself must not occlude its escort volume.
        var result=new CollisionResult();
        return !CollisionDetection.CheckBetweenPoints(from,to,TestFlags.Scan | TestFlags.Players,scene,ref result);
    }
    internal Vector3 Position
    {
        get{if(_map==null)return Vector3.Zero;var p=_map.Route.Pose(Frame?.Progress??0).Position;return new(p.X,p.Y,p.Z);}
    }
    internal Vector3 Forward
    {
        get{if(_map==null)return Vector3.UnitZ;var p=_map.Route.Pose(Frame?.Progress??0).Forward;return new(p.X,p.Y,p.Z);}
    }
    internal static bool Locked(PlayerEntity player)
    {
        var scene=player.OwningScene;
        if(HeroRules.IsZone(scene.GameState.Mode))return CtzRuntime.Locked(scene);
        if(!HeroRules.IsPayload(scene.GameState.Mode))return false;
        var state=scene.Payload.Frame;
        return state==null || state.Value.Phase==2 || (state.Value.Phase==0 && player.TeamIndex==state.Value.Attacker);
    }
    internal List<CtzSpawnRoomDefinition> Rooms(Scene scene)
    {
        EnsureMap(scene);if(_map==null)return _roomCache;
        int stage=Math.Clamp(Frame?.Checkpoints??0,0,2),attacker=Frame?.Attacker??0;
        if(stage!=_roomStage || attacker!=_roomAttacker) {
            _roomStage=stage;_roomAttacker=attacker;_roomCache.Clear();
            for(int team=0;team<2;team++){var r=_map.ForTeam(stage,attacker,team);_roomCache.Add(new CtzSpawnRoomDefinition{Min=r.Min,Max=r.Max,Side=team});}
        }
        return _roomCache;
    }
    private void EnsureMap(Scene scene)
    {
        if(_map!=null)return;
        var d=CustomRooms.Definitions.FirstOrDefault(d=>d.Name==scene.Room?.Meta.Name);
        if(d!=null){_definition=d;_map=PayloadMap.Get(d);_match=new(_offlineMatchId,_map.Route,Rules);UpdateFrame();}
    }
    internal bool ValidSpawn(Scene scene,int team,Vector3 position)
    {
        EnsureMap(scene);return _map!=null && team is >=0 and <=1 && PayloadMap.Contains(_map.ForTeam(Frame?.Checkpoints??0,Frame?.Attacker??0,team),position);
    }
    internal void Apply(Scene scene,PayloadFrame value)
    {
        EnsureMap(scene);
        if(!value.Valid || _map==null || value.Length!=_map.Route.Length)return;
        if(Frame is {} previous && (value.Round<previous.Round || value.Round==previous.Round && value.Tick<previous.Tick))return;
        Frame=value;ApplyScores(scene);
    }
    private void UpdateFrame()
    {
        var match=_match!;var s=match.Current;var history=match.History;
        long best(int team)=>history.LastOrDefault(h=>(int)h.Attacker==team)?.BestDistance??0;
        int points(int team)=>history.Where(h=>(int)h.Attacker==team).Sum(h=>h.Checkpoints);
        long a=best(0),b=best(1);int ap=points(0),bp=points(1);
        // Current finished result is already in History only after match continuation/results.
        if(match.Outcome==null){if((int)s.Attacker==0){a=s.BestProgress;ap+=s.CapturedCheckpoints+(s.Progress==_map!.Route.Length?1:0);}else{b=s.BestProgress;bp+=s.CapturedCheckpoints+(s.Progress==_map!.Route.Length?1:0);}}
        Frame=new(s.RoundId,match.PairIndex,(int)s.Attacker,(byte)s.Phase,s.SetupRemaining,s.RemainingTicks,s.Tick,s.Progress,s.BestProgress,
            _map!.Route.Length,s.CapturedCheckpoints,s.HoldRemaining,s.Overtime,s.FuseRemaining,s.Contested,s.Escorts,s.Target??-1,
            a,b,ap,bp,match.AlphaBank,match.BravoBank,match.Outcome?.Winner is {} winner?(int)winner:-1,match.Outcome!=null);
        if(!Frame.Value.Valid)throw new InvalidOperationException("Native payload state exceeded its wire budget.");
    }
    internal void ApplyScores(Scene scene)
    {
        if(Frame is not {} f)return;
        scene.GameState.TeamPoints[0]=f.AlphaPoints;scene.GameState.TeamPoints[1]=f.BravoPoints;
        // End standings reflect objective victory, not unrelated kill totals or rounded distance.
        if(f.Finished){scene.GameState.TeamPoints[0]=f.Winner<0?0:f.Winner==0?1:0;scene.GameState.TeamPoints[1]=f.Winner==1?1:0;}
    }
}
