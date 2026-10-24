using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Formats;
using MphRead.Mods.HeroModes;
using OpenTK.Mathematics;
namespace MphRead.Entities;
public partial class PlayerEntity
{
    public partial class PlayerAiData
    {
        private readonly List<Vector3> _payloadPath=new();
        private int _payloadPathIndex,_payloadRouteCooldown,_payloadRound=-1;
        private Vector3 _payloadDestination;
        private void ApplyPayloadObjective()
        {
            if(!HeroRules.IsPayload(_scene.GameState.Mode)||_player.Health<=0||!_player.ModInPlay)return;
            var runtime=_scene.Payload;var state=runtime.Frame;
            if(state==null||PayloadRuntime.Locked(_player)){_payloadPath.Clear();return;}
            Vector3 goal=runtime.BotGoal(_player);
            // The ordinary tactical layer may still seek health while critically wounded.
            if(_player.Health<_player.HealthMax/4 && _botStrategicItem!=null)return;
            if(_payloadRound!=state.Value.Round){_payloadRound=state.Value.Round;_payloadRouteCooldown=0;_payloadPath.Clear();}
            bool straight=Math.Abs(goal.Y-_player.Position.Y)<1.7f && Vector3.DistanceSquared(goal,_player.Position)<81
                && PayloadRuntime.Visible(_scene,_player.Position.AddY(.8f),goal.AddY(.8f))
                && !CtzSpawnRooms.BlocksMovement(_player,_player.Position,goal);
            Vector3 desired=goal;
            if(!straight)
            {
                if(--_payloadRouteCooldown<=0 || Vector3.DistanceSquared(goal,_payloadDestination)>9 || _payloadPathIndex>=_payloadPath.Count)
                {BuildPayloadPath(goal);_payloadDestination=goal;_payloadRouteCooldown=45+_player.SlotIndex*3;}
                while(_payloadPathIndex<_payloadPath.Count && (_payloadPath[_payloadPathIndex]-_player.Position).LengthSquared<3.24f)_payloadPathIndex++;
                if(_payloadPathIndex>=_payloadPath.Count)return;
                desired=_payloadPath[_payloadPathIndex];
                // Short, collision-checked lookahead avoids oscillating at sparse nav corners.
                for(int i=_payloadPathIndex+1;i<_payloadPath.Count;i++)
                {
                    var next=_payloadPath[i];if(Vector3.DistanceSquared(next,_player.Position)>81)break;
                    if(Math.Abs(next.Y-_player.Position.Y)>1.7f || !PayloadRuntime.Visible(_scene,_player.Position.AddY(.8f),next.AddY(.8f))
                        || CtzSpawnRooms.BlocksMovement(_player,_player.Position,next))break;
                    desired=next;_payloadPathIndex=i;
                }
            }
            // Keep normal aim, fire, reactions and form choices; replace only locomotion intent.
            _buttons.X.IsDown=_buttons.B.IsDown=_buttons.A.IsDown=_buttons.Y.IsDown=false;
            _tacticalMovementActive=true;_tacticalSteeringActive=true;
            if((desired-_player.Position).WithY(0).LengthSquared>.81f)BotFollowCombatGoal(desired);
        }
        private void BuildPayloadPath(Vector3 goal)
        {
            _payloadPath.Clear();_payloadPathIndex=0;
            if(_nodeList==null||_nodeList.Count==0)return;
            var nodes=_nodeList.Where(n=>n.NodeType!=NodeType.Hazard && n.NodeType!=NodeType.Aerial).ToDictionary(n=>n.Id);
            ushort? Closest(Vector3 position)
            {
                NodeData3? best=null;float distance=float.MaxValue;
                foreach(var node in nodes.Values)
                {
                    float d=Vector3.DistanceSquared(position,node.Position);
                    if(d>=distance||Math.Abs(position.Y-node.Position.Y)>2.5f)continue;
                    if(!PayloadRuntime.Visible(_scene,position.AddY(.8f),node.Position.AddY(.8f))||CtzSpawnRooms.BlocksMovement(_player,position,node.Position))continue;
                    best=node;distance=d;
                }
                return best?.Id;
            }
            var start=Closest(_player.Position);var target=Closest(goal);if(start==null||target==null)return;
            var costs=new Dictionary<ushort,float>{{start.Value,0}};var previous=new Dictionary<ushort,ushort>();
            var queue=new PriorityQueue<ushort,float>();queue.Enqueue(start.Value,0);var closed=new HashSet<ushort>();
            while(queue.TryDequeue(out ushort id,out _))
            {
                if(!closed.Add(id))continue;if(id==target.Value)break;var node=nodes[id];
                for(int i=0;i<node.Count2;i++)
                {
                    int at=node.Index2+i;if(at<0||at>=node.Values.Count)continue;ushort next=node.Values[at];
                    if(!nodes.TryGetValue(next,out var neighbor)||closed.Contains(next)||CtzSpawnRooms.BlocksMovement(_player,node.Position,neighbor.Position))continue;
                    float cost=costs[id]+Vector3.Distance(node.Position,neighbor.Position);
                    if(costs.TryGetValue(next,out float before)&&before<=cost)continue;
                    costs[next]=cost;previous[next]=id;queue.Enqueue(next,cost+Vector3.Distance(neighbor.Position,goal));
                }
            }
            if(!closed.Contains(target.Value))return;
            ushort cursor=target.Value;for(int guard=0;guard<=nodes.Count;guard++)
            {
                _payloadPath.Add(nodes[cursor].Position);if(cursor==start.Value)break;
                if(!previous.TryGetValue(cursor,out cursor)){_payloadPath.Clear();return;}
            }
            _payloadPath.Reverse();_payloadPath.Add(goal);
        }
    }
}
