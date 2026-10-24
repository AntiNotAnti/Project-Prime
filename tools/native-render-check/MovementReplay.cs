using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Editor;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods.MapGen;
using MphRead.Utility;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Diagnostic input replay. Default mode replaces fixture collision; package mode
// verifies and retains the installed map collision. Neither claims visual fidelity.
sealed partial class CheckWindow
{
    private string? _movementReport;
    private bool _movementPackageRoom, _movementPackageCollisionVerified;
    private MapDefinition? _movementSourceDefinition;
    private bool _movementSourceTeleport;
    private int _movementSourceExitFrames;
    private bool _movementSourceExitImpulse;
    private bool _movementSourceLaunch, _movementSourceApproachReady;
    private Vector2? _movementSourceInitialVelocity;
    private float _movementSourceHorizontalChange;
    private int _movementSourceAirFrames, _movementSourceAirInputFrames;
    private float _movementSourcePeak;
    private bool _movementRequireClearance,_movementBodySaturated;
    private float _movementWallHeight=8;
    private float _movementInputSpeed=.12f;
    private bool _movementStableArrivalRequested, _movementFullInputTrial;
    private Vector3? _movementStableArrival;
    private bool _movementCrest, _movementCrestObserved;
    private bool _movementSettleCoast, _movementAnalogTrial;
    private bool _movementFormRefinementPrototype;
    private bool _movementNonWalkable, _movementJumpTrial, _movementJumpObserved;
    private int _movementJumpFrame=100;
    private int _movementBodyOverlaps,_movementClearanceFrames;
    private int _movementContactOverflowStart,_movementContactOverflows;
    private float _movementMaximumPenetration;
    private int? _movementSpawnIndex;
    private bool _movementSpawnCoastObserved;
    private Vector3? _movementSpawnPosition;
    private Vector3 _movementSpawnFacing;
    private byte[]? _movementCollision;
    private string? _movementRoute;
    private Vector3 _movementStart, _movementGoal;
    private Vector3[]? _movementWaypoints;
    private Vector3[]? _movementContactPath;
    private int _movementContactIndex;
    private int _movementWaypointIndex=1, _movementWaypointHold;
    private Vector3? _movementSupportedStart;
    private Vector3 _movementInputForward,_movementInputRight;
    private ulong _movementBegin, _movementFocusReleaseUntil;
    private int _movementArrived, _movementInputFrames, _movementPostFocusInputFrames;
    private bool _movementFocusControl, _movementFocusControlInjected;
    private bool _movementStarted, _movementFinished, _movementBraking, _movementWall;
    private float _movementClosest=float.MaxValue, _movementPulseX, _movementPulseY;
    private int _movementFinalHealth, _movementAltFrames;
    private bool _movementAlt, _movementMorphObserved, _movementFinalAlt, _movementAltCoasting;
    private string? _movementHunter;
    private readonly List<object> _movementFrames = new();
    private const BindingFlags ReplayPrivate = BindingFlags.Instance|BindingFlags.NonPublic;
    private void ConfigureMovementReplay(string[] args)
    {
        int index=Array.IndexOf(args,"-movement-report");
        if(index<0)
        {
            if(args.Contains("-movement-certified-jump") || args.Contains("-movement-jump-route") || args.Contains("-movement-package-jump-probe")
                || args.Contains("-movement-contact-diagnosis") || args.Contains("-movement-input-until") || args.Contains("-movement-layer-barrier"))
                throw new ArgumentException("Certified jump selection requires -movement-report.");
            return;
        }
        if(args.Contains("-movement-jump-route") && !args.Contains("-movement-certified-jump"))
            throw new ArgumentException("A certified jump route requires -movement-certified-jump.");
        if(args.Contains("-movement-layer-barrier") && args.Any(a => a is "-movement-certified-jump"
            or "-movement-package-jump-probe" or "-movement-local-fixture" or "-movement-spawn" or "-movement-witness"))
            throw new ArgumentException("Layer barrier cannot be combined with another movement selector.");
        if(index+1>=args.Length || !args.Contains("-idle") || PlayerEntity.PlayerCount!=1 || _camera!=null || _shots!=null || args.Contains("-resourcestress") || _windowCycle)
            throw new ArgumentException("Movement replay requires -movement-report file, -idle, -players 1 and no camera/shots.");
        // This replay owns a synthetic keyboard. A real connected controller
        // must not change its startup, movement, or natural morph requests.
        typeof(Scene).Assembly.GetType("MphRead.Mods.Input.GamepadDesktop")!
            .GetField("_unavailable",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,true);
        MphRead.Mods.Input.GamepadManager.ClearAll();
        _movementWall=args.Contains("-movement-wall");
        _movementPackageRoom=args.Contains("-movement-package-room");
        if (_movementPackageRoom && _movementWall) throw new ArgumentException("Package-room controls must use the package's unchanged collision.");
        _movementNonWalkable=args.Contains("-movement-nonwalkable-link");
        _movementSourceLaunch=args.Contains("-movement-source-launch");
        _movementSourceTeleport=args.Contains("-movement-source-teleport");
        if((_movementSourceLaunch||_movementSourceTeleport) && (!_movementPackageRoom || _movementWall || !args.Contains("-movement-require-clearance") || _movementNonWalkable || args.Contains("-movement-jump-trial") || _movementSourceLaunch&&_movementSourceTeleport))
            throw new ArgumentException("Source launch replay requires unchanged packaged collision.");
        _movementJumpTrial=args.Contains("-movement-jump-trial");
        ConfigureBipedTransit(args);
        int jumpAt=Array.IndexOf(args,"-movement-jump-frame");
        if(jumpAt>=0)
        {
            if(!_movementJumpTrial||jumpAt+1>=args.Length||!int.TryParse(args[jumpAt+1],out _movementJumpFrame)||_movementJumpFrame<30||_movementJumpFrame>300)
                throw new ArgumentException("Jump timing requires an explicit jump trial and a frame from30through300.");
        }
        _movementSettleCoast=args.Contains("-movement-settle-coast");
        _movementFormRefinementPrototype=args.Contains("-movement-form-refinement-prototype");
        _movementStableArrivalRequested=args.Contains("-movement-stable-arrival");
        _movementAnalogTrial=args.Contains("-movement-analog-trial");
        _movementFullInputTrial=args.Contains("-movement-full-input-trial");
        int speedAt=Array.IndexOf(args,"-movement-input-speed");
        if(speedAt>=0){if(speedAt+1>=args.Length)throw new ArgumentException("Missing input speed");_movementInputSpeed=float.Parse(args[speedAt+1],System.Globalization.CultureInfo.InvariantCulture);if(!float.IsFinite(_movementInputSpeed)||_movementInputSpeed<=0||_movementInputSpeed>.24f)throw new ArgumentOutOfRangeException("input speed");}
        int wallHeightAt=Array.IndexOf(args,"-movement-wall-height");
        if(wallHeightAt>=0)
        {
            if(!_movementWall||wallHeightAt+1>=args.Length)throw new ArgumentException("Wall height requires a blocking control and value.");
            _movementWallHeight=float.Parse(args[wallHeightAt+1],System.Globalization.CultureInfo.InvariantCulture);
            if(!float.IsFinite(_movementWallHeight)||_movementWallHeight<8||_movementWallHeight>128)throw new ArgumentOutOfRangeException("wall height");
        }
        _movementRequireClearance=args.Contains("-movement-require-clearance");
        ConfigureContactDiagnosis(args);
        _movementAlt=args.Contains("-movement-alt");
        _movementCrest=args.Contains("-movement-crest");
        int hunterAt=Array.IndexOf(args,"-hunter");
        if(_movementCrest&&(!_movementWall||!_movementAlt||_movementWallHeight!=8||hunterAt<0||hunterAt+1>=args.Length||args[hunterAt+1]!="Spire"||!_movementRequireClearance))
            throw new ArgumentException("Crest control requires Spire alternate form, height-eight wall and strict clearance.");
        _movementFocusControl=args.Contains("-movement-synthetic-focus-loss");
        _movementReport=Path.GetFullPath(args[index+1]);
        using var input=JsonDocument.Parse(File.ReadAllText(_movementReport));
        var root=input.RootElement;
        if(args.Contains("-movement-package-jump-probe")){ ConfigurePackageJumpProbe(args,root); return; }
        if(args.Contains("-movement-certified-jump")){ ConfigureCertifiedJumpReplay(args,root); return; }
        if(args.Contains("-movement-local-fixture")){ ConfigureLocalInputFixture(args,root); return; }
        int spawnAt=Array.IndexOf(args,"-movement-spawn");
        if(spawnAt>=0)
        {
            if(spawnAt+1>=args.Length||_movementWall||args.Contains("-movement-reverse")||args.Contains("-movement-witness"))
                throw new ArgumentException("Compiled spawn replay needs an index and cannot combine wall/reverse/witness selection.");
            int spawnIndex=int.Parse(args[spawnAt+1]);int directionAt=Array.IndexOf(args,"-movement-spawn-direction");
            int direction=directionAt<0?3:int.Parse(args[directionAt+1]);if(direction is <0 or >15)throw new ArgumentOutOfRangeException("spawn direction");
            var source=MapDefinition.Load(root.GetProperty("project").GetString()!);
            _movementSourceDefinition=source;
            var compiled=MapCompiler.Compile(source);var map=compiled.Map??throw new InvalidDataException("Compiled spawn geometry unavailable.");
            if(map.AltFormCertification?.Failures.Any(f=>f.Detail.StartsWith("Spawn"))!=false)throw new InvalidDataException("Spawn replay requires passing mandatory spawn certification.");
            var spawns=map.Entities.OfType<PlayerSpawnEntityEditor>().ToArray();
            if(spawns.Length!=map.Definition.Spawns.Count||(uint)spawnIndex>=spawns.Length)throw new ArgumentOutOfRangeException("spawn index");
            var spawn=spawns[spawnIndex];var position=map.Definition.Spawns[spawnIndex].Position;
            if((spawn.Position-new Vector3(position[0],position[1],position[2])).Length>.0001f)throw new InvalidDataException("Runtime spawn differs from certified output.");
            _movementSpawnIndex=spawnIndex;_movementSpawnPosition=spawn.Position;_movementSpawnFacing=spawn.Facing;
            _movementStart=spawn.Position+Vector3.UnitY*.45f;
            float angle=direction*MathF.Tau/16;_movementGoal=_movementStart+new Vector3(MathF.Cos(angle),0,MathF.Sin(angle))*.525f;
            _movementRoute=$"Compiled spawn {spawnIndex}, radial direction {direction}";
            _movementCollision=(byte[])typeof(MapPacker).GetMethod("BuildCollision",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{map})!;
            return;
        }
        int kindAt=Array.IndexOf(args,"-movement-kind");
        if(kindAt>=0&&kindAt+1>=args.Length)throw new ArgumentException("Missing movement failure kind.");
        string kind=kindAt>=0?args[kindAt+1]:"BodyObstruction";
        bool selectWitness=args.Contains("-movement-witness");
        if(_movementStableArrivalRequested&&(!selectWitness||kind!="Finite contact traversal"||!_movementAlt||!_movementRequireClearance))throw new ArgumentException("Stable arrival requires a strict alternate-form finite contact witness.");
        if((_movementNonWalkable||_movementJumpTrial)&&(!_movementNonWalkable||_movementAlt||_movementWall||selectWitness||kind!="NavigationBarrierProbe"||!_movementRequireClearance))
            throw new ArgumentException("Nonwalking link control requires a biped barrier probe, no artificial wall, and strict clearance.");
        var failure=selectWitness
            ?root.GetProperty("playerForms").GetProperty("walkingWitnesses").EnumerateArray().First(w=>w.GetProperty("Kind").GetString()==kind)
            :root.GetProperty("playerForms").GetProperty("failures").EnumerateArray().First(f=>f.GetProperty("Kind").GetString()==kind);
        bool contactPath=args.Contains("-movement-contact-path");
        if(contactPath)
        {
            if(!selectWitness||kind!="Finite contact traversal"||!_movementRequireClearance||args.Contains("-movement-reverse"))
                throw new ArgumentException("Contact steering requires a forward strict saved contact witness.");
            int contactHunterAt=Array.IndexOf(args,"-hunter");
            string form=_movementAlt?args[contactHunterAt+1]+" alt form":"Biped";
            failure=root.GetProperty("playerForms").GetProperty("walkingWitnesses").EnumerateArray()
                .First(w=>w.GetProperty("Kind").GetString()==kind&&w.GetProperty("Form").GetString()==form);
        }
        Vector3 V(JsonElement v)=>new(v[0].GetSingle(),v[1].GetSingle(),v[2].GetSingle());
        // Fine graph node Y is floor + .5; a biped origin is floor + .5.
        _movementStart=V(failure.GetProperty("start"));
        _movementGoal=V(failure.GetProperty("end"));
        _movementRoute=failure.GetProperty(selectWitness?"Route":"Detail").GetString();
        if(contactPath)
        {
            _movementContactPath=failure.GetProperty("feet").EnumerateArray().Select(v=>V(v)+Vector3.UnitY*.5f).ToArray();
            if(_movementContactPath.Length<2||_movementContactPath.Length>8192||_movementContactPath.Any(v=>!float.IsFinite(v.LengthSquared))
                ||(_movementContactPath[0]-_movementStart).Xz.Length>.001f||(_movementContactPath[^1]-_movementGoal).Xz.Length>.1f)
                throw new InvalidDataException("Invalid saved contact steering path.");
        }
        if(args.Contains("-movement-waypoints"))
        {
            if(!selectWitness||kind!="Verified walking detour"||!_movementRequireClearance||_movementNonWalkable||_movementJumpTrial||args.Contains("-movement-reverse"))
                throw new ArgumentException("Waypoint replay requires a forward verified walking detour and strict clearance.");
            _movementWaypoints=failure.GetProperty("waypoints").EnumerateArray().Select(V).ToArray();
            if(_movementWaypoints.Length<3||_movementWaypoints.Length>5||(_movementWaypoints[0]-_movementStart).Length>.0005f||(_movementWaypoints[^1]-_movementGoal).Length>.0005f)
                throw new InvalidDataException("Invalid certified waypoint endpoints.");
        }
        if(args.Contains("-movement-reverse")){(_movementStart,_movementGoal)=(_movementGoal,_movementStart);_movementRoute+=" [reverse]";}
        if(args.Contains("-movement-supported-start"))
        {
            if(!selectWitness||args.Contains("-movement-reverse"))throw new ArgumentException("Supported start needs a forward saved witness.");
            // Graph nodes carry centre-ray floor height, which can put the
            // capsule inside a finite ridge. Use the verified biped stance for
            // this initial diagnostic placement, then natural physics/morphing.
            var biped=root.GetProperty("playerForms").GetProperty("walkingWitnesses").EnumerateArray()
                .First(w=>w.GetProperty("Route").GetString()==_movementRoute&&w.GetProperty("Form").GetString()=="Biped");
            var initial=V(biped.GetProperty("feet")[0])+Vector3.UnitY*.5f;
            if(!float.IsFinite(initial.LengthSquared)||(initial.Xz-_movementStart.Xz).Length>.001f)throw new InvalidDataException("Invalid supported witness start.");
            _movementSupportedStart=initial;
        }
        var definition=MapDefinition.Load(root.GetProperty("project").GetString()!);
        _movementSourceDefinition=definition;
        if(_movementSourceLaunch && (kind!="SourceLaunchProbe" || definition.Q3Triggers?.Any(t=>t.Kind=="push"&&t.UsePlayerBounds)!=true))
            throw new InvalidDataException("Source launch replay requires an authored source pad fixture.");
        if(_movementSourceTeleport && (kind!="SourceTeleportProbe" || definition.Q3Triggers?.Count(t=>t.Kind=="teleport"&&t.UsePlayerBounds&&t.TeleportExitSpeed>0)!=1))
            throw new InvalidDataException("Source teleport replay requires one authored source exit fixture.");
        var built=definition.Import!=null?Q3Import.Build(definition,false):MapBuilder.Build(definition); MapPacker.ApplyCollision(built,definition,false);
        ConfigureLayerBarrier(args,definition);
        var graph=MapNodePacker.AnalyzeFineForCertification(built.Solid,sourceUnitsPerUnit:definition.Import?.UnitsPerUnit,sourceStepHeight:definition.StepHeight);
        int Closest(Vector3 v)=>Enumerable.Range(0,graph.Positions.Length).MinBy(i=>(graph.Positions[i]-v).LengthSquared);
        int from=Closest(_movementStart),to=Closest(_movementGoal);
        bool linked=graph.Neighbours[from].Contains(to);
        bool validDetour=_movementWaypoints!=null&&graph.Detours.Any(d=>d.From==from&&d.To==to
            &&d.Nodes.Select(n=>graph.Positions[n]).SequenceEqual(_movementWaypoints));
        if((graph.Positions[from]-_movementStart).Length>.01f || (graph.Positions[to]-_movementGoal).Length>.01f
            ||(_movementWaypoints!=null?!validDetour||linked:_movementNonWalkable?linked:!_movementSourceLaunch&&!_movementSourceTeleport&&!linked))
            throw new InvalidDataException("Movement report endpoints or expected navigation link status do not match current graph.");
        _movementCollision=(byte[])typeof(MapPacker).GetMethod("BuildCollision",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{built})!;
    }
    private void PrepareMovementReplay()
    {
        if(_movementReport==null)return;
        // Arm synthetic input ownership during warmup as well as movement;
        // the hidden window's OS focus must not prevent normal initialization.
        if(Scene.FrameCount<240){MphRead.Mods.Input.GamepadContexts.Focused=true;return;}

        if(_movementFocusControl&&_movementStarted&&!_movementFocusControlInjected&&Scene.FrameCount-_movementBegin>=180)
        {
            MphRead.Mods.Input.GamepadContexts.Focused=false;
            typeof(Scene).GetMethod("NoteHardwareFocusLost",ReplayPrivate)!.Invoke(Scene,null);
            _movementFocusControlInjected=true;
        }
        // This diagnostic owns a synthetic keyboard; OS focus gates hardware,
        // not its controlled test stream. Production focus logic is unchanged.
        if(!MphRead.Mods.Input.GamepadContexts.Focused)
        {
            MphRead.Mods.Input.GamepadContexts.Focused=true;
            _movementFocusReleaseUntil=Scene.FrameCount+2;
        }
        var player=Scene.Players.Main;
        if(!_movementStarted)
        {
            if(player.Health<=0 || (player.LoadFlags&LoadFlags.Spawned)==0)
            {
                if(Scene.FrameCount<480)return;
                throw new InvalidOperationException($"Movement replay requires a live initialized spawned hunter: health={player.Health}, flags={player.LoadFlags}, position={player.Position}, frame={Scene.FrameCount}.");
            }
            if(player.IsAltForm || player.Hunter<Hunter.Samus || player.Hunter>Hunter.Weavel)
                throw new InvalidOperationException("Replay requires an initial biped belonging to one of the seven playable hunters.");
            _movementHunter=player.Hunter.ToString();
            // These are in-memory diagnostic bindings only; preferences are never saved.
            void Bind(Keybind binding,Keys key){binding.Type=ButtonType.Key;binding.Key=key;}
            Bind(player.Controls.MoveUp,Keys.W);Bind(player.Controls.RollUp,Keys.W);
            Bind(player.Controls.MoveDown,Keys.S);Bind(player.Controls.RollDown,Keys.S);
            Bind(player.Controls.MoveLeft,Keys.A);Bind(player.Controls.RolltLeft,Keys.A);
            Bind(player.Controls.MoveRight,Keys.D);Bind(player.Controls.RollRight,Keys.D);
            Bind(player.Controls.Morph,Keys.F);
            Bind(player.Controls.Jump,Keys.Space);
            var room=(RoomEntity)typeof(Scene).GetField("_room",ReplayPrivate)!.GetValue(Scene)!;
            if(_localFixtureKillHeight is float fixtureKillHeight&&fixtureKillHeight<room.Meta.KillHeight)
            {
                // The diagnostic swaps geometry, so its host-room kill plane
                // must also lie below that geometry. Keep source coordinates
                // and ordinary death handling; clone metadata for this room.
                var localMeta=typeof(object).GetMethod("MemberwiseClone",ReplayPrivate)!.Invoke(room.Meta,null)!;
                localMeta.GetType().GetField("<KillHeight>k__BackingField",ReplayPrivate)!.SetValue(localMeta,fixtureKillHeight);
                typeof(RoomEntity).GetField("_meta",ReplayPrivate)!.SetValue(room,localMeta);
            }
            var info=Collision.ReadMphCollision(Read.ReadStruct<CollisionHeader>(_movementCollision!),_movementCollision!,-1);
            var collisions=(List<CollisionInstance>)room.RoomCollision;
            if (_movementPackageRoom)
            {
                var definition = _movementSourceDefinition ?? throw new InvalidDataException("Missing source package definition.");
                var installed = CustomRooms.Definitions.Single(d => d.Name == room.Meta.Name);
                // Packing may reorder faces and shared vertices. Compare exact geometry,
                // flags and every grid cell's resolved faces, preserving duplicates.
                string FaceKey(MphCollisionInfoBase data, int i)
                {
                    var f = data.RuntimeData[i];
                    var vertices = Enumerable.Range(f.PointStartIndex, f.PointIndexCount)
                        .Select(v => data.Points[data.RuntimePointIndices[v]].ToString()).OrderBy(v => v, StringComparer.Ordinal);
                    return $"{data.Planes[f.PlaneIndex]}|{f.Counter}|{f.Flags}|{f.LayerMask}|{f.PaddingA}|" + string.Join(";",vertices);
                }
                bool Equivalent(MphCollisionInfoBase actual)
                {
                    if (actual.MinPosition != info.MinPosition || actual.Portals.Count != 0 || info.Portals.Count != 0
                        || actual.RuntimeEntries.Count != info.RuntimeEntries.Count) return false;
                    var actualFaces = Enumerable.Range(0,actual.RuntimeData.Count).Select(i => FaceKey(actual,i)).ToArray();
                    var expectedFaces = Enumerable.Range(0,info.RuntimeData.Count).Select(i => FaceKey(info,i)).ToArray();
                    if (!actualFaces.OrderBy(v=>v,StringComparer.Ordinal).SequenceEqual(expectedFaces.OrderBy(v=>v,StringComparer.Ordinal))) return false;
                    for (int cell=0;cell<actual.RuntimeEntries.Count;cell++)
                    {
                        var a=actual.RuntimeEntries[cell]; var e=info.RuntimeEntries[cell];
                        var af=Enumerable.Range(a.DataStartIndex,a.DataCount).Select(i=>actualFaces[actual.RuntimeDataIndices[i]]).OrderBy(v=>v,StringComparer.Ordinal);
                        var ef=Enumerable.Range(e.DataStartIndex,e.DataCount).Select(i=>expectedFaces[info.RuntimeDataIndices[i]]).OrderBy(v=>v,StringComparer.Ordinal);
                        if (!af.SequenceEqual(ef)) return false;
                    }
                    return true;
                }
                if (room.Meta.Name != definition.Name || installed.BundlePath == null
                    || installed.WorldGravity != definition.WorldGravity || installed.StepHeight != definition.StepHeight
                    || JsonSerializer.Serialize(installed.Q3Triggers) != JsonSerializer.Serialize(definition.Q3Triggers)
                    || JsonSerializer.Serialize(installed.Q3PlayableSpace) != JsonSerializer.Serialize(definition.Q3PlayableSpace)
                    || collisions.Count != 1 || !collisions[0].Active || collisions[0].Translation != Vector3.Zero
                    || collisions[0].Info is not MphCollisionInfoBase actual || !Equivalent(actual))
                    throw new InvalidDataException("Loaded package physics/collision does not match the source fixture.");
                _movementPackageCollisionVerified = true;
                VerifyLoadedCertifiedJump(room);
            }
            else { collisions.Clear();collisions.Add(new CollisionInstance("diagnostic-authored-map-collision",info,false)); }
            if(_movementWall)
            {
                var mid=(_movementStart+_movementGoal)/2;
                var normal=(_movementStart-_movementGoal);normal.Y=0;normal=normal.Normalized();
                var side=Vector3.Cross(normal,Vector3.UnitY)*8;
                var wall=new CollisionDataEditor{Plane=new(normal,Vector3.Dot(normal,mid)),LayerMask=4};
                wall.Points.AddRange(new[]{mid-side-Vector3.UnitY*_movementWallHeight,mid-side+Vector3.UnitY*_movementWallHeight,mid+side+Vector3.UnitY*_movementWallHeight,mid+side-Vector3.UnitY*_movementWallHeight});
                var bytes=MapCollisionPacker.Pack(new[]{wall});
                var block=Collision.ReadMphCollision(Read.ReadStruct<CollisionHeader>(bytes),bytes,-1);
                collisions.Add(new CollisionInstance("diagnostic-blocking-control",block,false));
            }
            if(_movementSpawnPosition is { } actualSpawn)
            {
                var hunter=player.Hunter;
                player.Spawn(actualSpawn,_movementSpawnFacing,Vector3.UnitY,player.NodeRef,respawn:true);
                if(player.Hunter!=hunter)throw new InvalidOperationException("Unexpected hunter change during diagnostic respawn.");
            }
            else
            {
                player.Position=_movementSupportedStart??(_movementStart+Vector3.UnitY*.025f);
                player.PrevPosition=player.Position;player.Speed=Vector3.Zero;player.PrevSpeed=Vector3.Zero;
            }
            _movementContactOverflowStart=(int)typeof(PlayerEntity).GetProperty("ModCollisionContactOverflows",ReplayPrivate)!.GetValue(player)!;
            _movementBegin=Scene.FrameCount;_movementStarted=true;
            Console.WriteLine("MOVEMENTREPLAY start "+_movementRoute);
        }
        typeof(Scene).GetField("_frameAdvanceOn",ReplayPrivate)!.SetValue(Scene,true);
        typeof(Scene).GetField("_advanceOneFrame",ReplayPrivate)!.SetValue(Scene,true);
        var keyboard=(KeyboardState)typeof(Scene).GetField("_keyboardState",ReplayPrivate)!.GetValue(Scene)!;
        var setKey=typeof(KeyboardState).GetMethod("SetKeyState",ReplayPrivate|BindingFlags.Public)!;
        foreach(Keys key in Enum.GetValues<Keys>())if(key>Keys.Unknown && (int)key<=348)setKey.Invoke(keyboard,new object[]{key,false});
        // Select an actual supported, body-clear stance inside the unchanged
        // destination radius. Only the input aim changes; the original goal,
        // native physics and sixty standing frames still decide acceptance.
        if(_movementStableArrivalRequested&&player.IsAltForm&&!player.IsMorphing&&_movementStableArrival==null)
        {
            int arrivalRayHits=0,arrivalFloorHits=0,arrivalClear=0;var arrivalProbes=new List<object>();
            foreach(float radius in new[]{0f,.0625f,.125f,.2f,.3f})
            {
                for(int direction=0;direction<8;direction++)
                {
                    float angle=direction*MathF.Tau/8;var at=_movementGoal+new Vector3(MathF.Cos(angle),0,MathF.Sin(angle))*radius;
                    CollisionResult hit=default;
                    bool arrivalHit=CollisionDetection.CheckBetweenPoints(at+Vector3.UnitY*.5f,at-Vector3.UnitY*1.2f,TestFlags.Players,Scene,ref hit);
                    if(!arrivalHit)continue;arrivalRayHits++;
                    if(hit.Plane.Y<.7f||MathF.Abs(hit.Position.Y-(_movementGoal.Y-.5f))>.5f){if(arrivalProbes.Count<8)arrivalProbes.Add(new{kind="floor-rejected",at=new[]{at.X,at.Y,at.Z},normal=new[]{hit.Plane.X,hit.Plane.Y,hit.Plane.Z},hit=new[]{hit.Position.X,hit.Position.Y,hit.Position.Z}});continue;}
                    arrivalFloorHits++;
                    var stance=new Vector3(at.X,hit.Position.Y+Fixed.ToFloat(player.Values.AltColRadius)/hit.Plane.Y-Fixed.ToFloat(player.Values.AltColYPos)+.025f,at.Z);
                    object?[] probe={stance,true,default(CollisionResult),false};
                    bool arrivalBlocked=(bool)typeof(PlayerEntity).GetMethod("FindDeepestPenetration",ReplayPrivate)!.Invoke(player,probe)!;
                    if(arrivalBlocked||(bool)probe[3]!){if(arrivalProbes.Count<8){var blocker=(CollisionResult)probe[2]!;arrivalProbes.Add(new{kind="body-rejected",at=new[]{stance.X,stance.Y,stance.Z},depth=blocker.Field14,normal=new[]{blocker.Plane.X,blocker.Plane.Y,blocker.Plane.Z}});}continue;}
                    arrivalClear++;_movementStableArrival=stance;break;
                }
                if(_movementStableArrival!=null)break;
            }
            if(_movementStableArrival==null){Console.WriteLine("ARRIVAL_DIAGNOSTIC "+JsonSerializer.Serialize(new{arrivalRayHits,arrivalFloorHits,arrivalClear,arrivalProbes}));throw new InvalidOperationException("No body-clear supported stance inside the existing destination tolerance.");}
        }
        var target=_movementStableArrival??_movementGoal;
        if(_movementContactPath!=null)
        {
            int limit=Math.Min(_movementContactIndex+32,_movementContactPath.Length-1);
            while(_movementContactIndex<limit&&(_movementContactPath[_movementContactIndex+1]-player.Position).Xz.Length<.18f)_movementContactIndex++;
            target=_movementContactIndex==_movementContactPath.Length-1?_movementGoal:
                _movementContactPath[Math.Min(_movementContactIndex+12,_movementContactPath.Length-1)];
        }
        if(_movementWaypoints!=null)
        {
            target=_movementWaypoints[_movementWaypointIndex];
            float targetY=target.Y+(_movementAlt?Fixed.ToFloat(player.Values.AltColRadius-player.Values.AltColYPos)-.5f:0);
            if(_movementWaypointIndex<_movementWaypoints.Length-1)
            {
                if((target-player.Position).Xz.Length<.1f&&Math.Abs(player.Position.Y-targetY)<.65f&&(player.Flags1&PlayerFlags1.Standing)!=0)_movementWaypointHold++;else _movementWaypointHold=0;
                if(_movementWaypointHold>=8){_movementWaypointIndex++;_movementWaypointHold=0;_movementAltCoasting=false;_movementBraking=false;}
            }
            target=_movementWaypoints[_movementWaypointIndex];
            // The requested flat arrival stance must also steer the final
            // waypoint. Keep intermediate points, the original destination,
            // strict body clearance and sixty standing frames unchanged.
            if(_movementWaypointIndex==_movementWaypoints.Length-1&&_movementStableArrival is {} flatArrival)target=flatArrival;
        }
        target=TransitTarget(player,target);
        var delta=target-player.Position;delta.Y=0;
        // A brief lateral request proves air steering, then returns to the unchanged landing goal.
        if(_movementSourceLaunch && _movementSourceAirFrames is >= 10 and < 25) delta.Z += 3;
        float Field(string name)=>(float)typeof(PlayerEntity).GetField(name,ReplayPrivate)!.GetValue(player)!;
        // These are the production walk/strafe axes, including its safe
        // fallback when aiming vertically. A projected aim vector can be zero.
        var forward=new Vector3(Field("_field70"),0,Field("_field74"));
        var right=-new Vector3(Field("_field78"),0,Field("_field7C"));
        if(player.IsAltForm && player.Values.AltFormStrafe==0)
        {
            var rollForward=new Vector3(Field("_altRollFbX"),0,Field("_altRollFbZ"));
            var rollRight=-new Vector3(Field("_altRollLrX"),0,Field("_altRollLrZ"));
            // Native rolling refreshes the camera-relative basis inside input
            // processing. Read the basis it will use, not last frame's values.
            ushort morphCamera=(ushort)typeof(PlayerEntity).GetField("_timeSinceMorphCamera",ReplayPrivate)!.GetValue(player)!;
            if(morphCamera>20 && (player.Flags1&PlayerFlags1.AltDirOverride)==0)
            {
                var cameraForward=new Vector3(player.CameraInfo.Field48,0,player.CameraInfo.Field4C);
                var cameraRight=-new Vector3(player.CameraInfo.Field50,0,player.CameraInfo.Field54);
                if(cameraForward.LengthSquared>.01f&&cameraRight.LengthSquared>.01f){rollForward=cameraForward;rollRight=cameraRight;}
            }
            if(rollForward.LengthSquared>.01f && rollRight.LengthSquared>.01f){forward=rollForward.Normalized();right=rollRight.Normalized();}
        }
        if(!float.IsFinite(forward.LengthSquared)||forward.LengthSquared<.01f||!float.IsFinite(right.LengthSquared)||right.LengthSquared<.01f)
            throw new InvalidOperationException("Production movement basis is unavailable for diagnostic input.");
        void Press(Keybind bind,bool down)
        {
            if(bind.Type!=ButtonType.Key || bind.Key==Keys.Unknown)throw new InvalidOperationException("Movement replay requires bound keyboard movement.");
            setKey.Invoke(keyboard,new object[]{bind.Key,down});
        }
        if(!ReplayDrivingAlt)
        {
            float friction=Math.Clamp(Fixed.ToFloat(player.Values.StandSpeedFactor),0,.999f);
            var speed=new Vector3(player.Speed.X,0,player.Speed.Z);
            bool predictedStop=(delta-speed*(friction/(2*(1-friction)))).Length<.18f;
            _movementBraking=predictedStop||(_movementBraking&&delta.Length<.4f);
        }
        if(ReplayDrivingAlt && (player.Hunter==Hunter.Trace||player.Hunter==Hunter.Weavel) && delta.Length<.15f && new Vector2(player.Speed.X,player.Speed.Z).Length<.025f)_movementAltCoasting=true;
        // Weavel's walking pulses can keep correcting inside the existing
        // arrival region and repeatedly unground him on a slope. Release keys
        // once close and nearly stopped, then let native gravity/friction settle.
        // The same 60 consecutive standing frames are still required.
        if(_movementSpawnPosition!=null && _movementAlt && player.Hunter==Hunter.Weavel
            && _movementInputFrames>0 && (player.Flags1&PlayerFlags1.Standing)!=0 && delta.Length<.3f && player.Speed.Xz.Length<.04f)
        {
            _movementAltCoasting=true;
            _movementSpawnCoastObserved=true;
        }
        // Diagnostic control experiment: release the keys inside the existing
        // arrival region and allow native friction/contact to settle. The same
        // strict clearance and 60 consecutive standing frames remain required.
        if(_movementSettleCoast&&ReplayDrivingAlt&&((player.Flags1&PlayerFlags1.Standing)!=0||player.Hunter==Hunter.Spire)
            &&delta.Length<(_movementStableArrivalRequested?.04f:.3f)&&player.Speed.Xz.Length<(_movementStableArrivalRequested?.025f:.025f))_movementAltCoasting=true;
        // A biped following waypoints otherwise keeps counter-steering after
        // arrival. Release ordinary input once nearly stopped, as the existing
        // settle trial already does for walking alternate forms. Intermediate
        // waypoints still clear this flag below; all arrival checks stay strict.
        if(_movementSettleCoast&&!ReplayDrivingAlt&&_movementInputFrames>0
            &&(player.Flags1&PlayerFlags1.Standing)!=0&&delta.Length<.1f&&player.Speed.Xz.Length<.025f)
            _movementAltCoasting=true;
        if(delta.Length>(_movementStableArrivalRequested?.08f:.38f))_movementAltCoasting=false;
        if(ReplayDrivingAlt && (player.Hunter==Hunter.Trace||player.Hunter==Hunter.Weavel) && delta.Length<.35f)
        {
            float friction=Math.Clamp(Fixed.ToFloat(player.Values.AltGroundSpeedFactor),0,.999f);
            var speed=new Vector3(player.Speed.X,0,player.Speed.Z);
            if((delta-speed*(friction/(2*(1-friction)))).Length<.12f)_movementAltCoasting=true;
        }
        if(_movementWaypoints!=null&&_movementWaypointIndex<_movementWaypoints.Length-1||_movementContactPath!=null&&_movementContactIndex<_movementContactPath.Length-1)_movementAltCoasting=false;
        // Rolling forms drift under native slope gravity when their stick is
        // released. Keep the existing input-only gravity compensation active
        // on a measured incline; do not repeatedly coast out of the arrival
        // area and steer back. Flat-floor coasting remains unchanged.
        if(_movementSettleCoast&&ReplayDrivingAlt&&player.Values.AltFormStrafe==0
            &&(player.Flags1&PlayerFlags1.Standing)!=0)
        {
            var support=(Vector3)typeof(PlayerEntity).GetField("_fieldC0",ReplayPrivate)!.GetValue(player)!;
            if(float.IsFinite(support.LengthSquared)&&support.LengthSquared>.001f)
            {
                support.Normalize();
                if(support.Y>.5f&&support.Y<.99999f)_movementAltCoasting=false;
            }
        }
        float targetStandingY=target.Y+((_bipedTransit?player.IsAltForm:_movementAlt)?Fixed.ToFloat(player.Values.AltColRadius-player.Values.AltColYPos)-.5f:0);
        if(Math.Abs(player.Position.Y-targetStandingY)>=.65f)_movementAltCoasting=false;
        ulong elapsed=Scene.FrameCount-_movementBegin;
        Press(player.Controls.Jump,_movementJumpTrial&&(!_bipedTransit||_transitPhase==TransitPhase.Flight)&&elapsed>=(ulong)_movementJumpFrame&&elapsed<(ulong)_movementJumpFrame+2);
        // A short initial pulse can be rejected while the native player is
        // still settling. Retry ordinary key presses at bounded intervals;
        // never force a morph, move the player, or toggle back out of alt form.
        bool morphPulse=elapsed>=40&&elapsed<42 || elapsed>=100&&elapsed<400&&(elapsed-100)%30<2;
        Press(player.Controls.Morph,Scene.FrameCount>=_movementFocusReleaseUntil && TransitMorphPulse(player,elapsed,_movementAlt
            && !player.IsAltForm&&!player.IsMorphing&&!player.IsUnmorphing&&morphPulse));
        // Drive as soon as the natural morph finishes; an extra idle wait lets
        // a rolling form leave its starting support before the route test begins.
        // Clearance is observed from that same first stable alternate-form frame.
        if((_movementSourceLaunch||_movementSourceTeleport) && elapsed>40 && (player.Flags1&(PlayerFlags1.Standing|PlayerFlags1.StandingPrevious))!=0 && MathF.Abs(player.Speed.Y)<.001f
            && (!_movementAlt || player.IsAltForm&&!player.IsMorphing&&!player.IsUnmorphing)) _movementSourceApproachReady=true;
        bool moving=(!(_movementSourceLaunch||_movementSourceTeleport)||_movementSourceApproachReady) && Scene.FrameCount>=_movementFocusReleaseUntil && !_movementFinished && !_movementAltCoasting && (!_movementInputUntil.HasValue || elapsed<(ulong)_movementInputUntil.Value) && (_movementAlt||_movementJumpTrial||_movementWaypoints!=null||_movementContactPath!=null||!_movementBraking) && elapsed>(ulong)(_movementAlt?40:30)
            && TransitMovementAllowed && (!_movementAlt || _bipedTransit&&!TransitDrivingAlt || player.IsAltForm&&!player.IsMorphing&&!player.IsUnmorphing);
        _movementInputForward=forward;_movementInputRight=right;
        float x=Vector3.Dot(delta,right),y=Vector3.Dot(delta,forward);
        bool pulseX=true,pulseY=true;
        if(_movementAlt||_movementJumpTrial||_movementWaypoints!=null||_movementContactPath!=null)
        {
            // Control only keys: approach slowly and counter-steer against real
            // rolling momentum. Never write the production velocity/position.
            // Limit the desired velocity in world space first. Limiting its
            // camera-axis components independently rotates the requested travel
            // direction as the camera turns, steering a ridge traversal downhill.
            var wanted=delta*.75f;
            if(wanted.Length>_movementInputSpeed)wanted=wanted.Normalized()*_movementInputSpeed;
            x=Math.Clamp((Vector3.Dot(wanted,right)-Vector3.Dot(player.Speed,right))*(_movementAnalogTrial&&_movementContactPath==null?5:20),-1,1);
            y=Math.Clamp((Vector3.Dot(wanted,forward)-Vector3.Dot(player.Speed,forward))*(_movementAnalogTrial&&_movementContactPath==null?5:20),-1,1);
            if(player.IsAltForm && player.Values.AltFormStrafe==0 && (player.Flags1&PlayerFlags1.Standing)!=0)
            {
                // Compensate the native rolling gravity with keyboard input.
                // The world-space horizontal impulse g*nXZ/nY keeps a resting
                // sphere tangent to its measured support. This changes only
                // key duty cycles; velocity and collision remain native.
                var ground=(Vector3)typeof(PlayerEntity).GetField("_fieldC0",ReplayPrivate)!.GetValue(player)!;
                float traction=Fixed.ToFloat(player.Values.RollAltTraction);
                if(float.IsFinite(ground.LengthSquared)&&ground.LengthSquared>.001f&&traction>0)
                {
                    ground.Normalize();
                    if(ground.Y>.5f)
                    {
                        var cancel=ground*(Fixed.ToFloat(player.Values.AltGroundGravity)*.5f/(ground.Y*traction));
                        cancel.Y=0;x=Math.Clamp(x+Vector3.Dot(cancel,right),-1,1);y=Math.Clamp(y+Vector3.Dot(cancel,forward),-1,1);
                    }
                }
            }
            // Test ordinary full directional input away from the arrival region.
            // Only the controller request changes; native acceleration, collision,
            // health and the original arrival/clearance assertions remain intact.
            if(_movementFullInputTrial && delta.Length>.6f)
            {
                var direction=delta.Normalized();
                x=Vector3.Dot(direction,right); y=Vector3.Dot(direction,forward);
            }
            _movementPulseX+=MathF.Abs(x);_movementPulseY+=MathF.Abs(y);
            pulseX=_movementPulseX>=1;pulseY=_movementPulseY>=1;
            if(pulseX)_movementPulseX-=1;if(pulseY)_movementPulseY-=1;
        }
        if(_movementAnalogTrial)
        {
            // Publish a virtual device through the ordinary gamepad pipeline.
            // This supplies input only; all movement and contact remain native.
            var stick=moving?new Vector2(x,y):Vector2.Zero;
            float length=stick.Length;
            if(length>0)
            {
                float inner=MphRead.Mods.Input.GamepadOptions.LeftInner;
                float outer=MphRead.Mods.Input.GamepadOptions.LeftOuter;
                stick*= (inner+Math.Min(length,1)*(1-inner-outer))/length;
            }
            MphRead.Mods.Input.GamepadManager.UpdateDevice("diagnostic-route-stick",
                new MphRead.Mods.Input.GamepadState { Connected=true,Name="Diagnostic route stick",LeftX=stick.X,LeftY=stick.Y },true);
            moving=false; // no digital movement may override the stick magnitude
        }
        float deadzone=_movementAlt||_movementJumpTrial||_movementWaypoints!=null?0:.08f;
        Press(player.Controls.MoveUp,moving&&pulseY&&y>deadzone);
        Press(player.Controls.MoveDown,moving&&pulseY&&y<-deadzone);
        Press(player.Controls.MoveRight,moving&&pulseX&&x>deadzone);
        Press(player.Controls.MoveLeft,moving&&pulseX&&x<-deadzone);
    }
    private void ObserveMovementReplay()
    {
        if(!_movementStarted||_movementFinished)return;
        var p=Scene.Players.Main;
        // Diagnostic only: simulate a bounded static refinement at the exact
        // form-change boundary. This flag is explicitly recorded and never
        // enabled by normal map acceptance or ordinary input fixtures.
        if(_movementFormRefinementPrototype&&p.IsAltForm&&!_movementFinalAlt)
            typeof(PlayerEntity).GetMethod("RefineStaticBodyContacts",ReplayPrivate)!.Invoke(p,null);
        var d=_movementGoal-p.Position;d.Y=0;
        _movementClosest=Math.Min(_movementClosest,d.Length);_movementFinalHealth=p.Health;
        _movementFinalAlt=p.IsAltForm;
        if(p.IsMorphing)_movementMorphObserved=true;
        if(p.IsAltForm&&!p.IsMorphing&&!p.IsUnmorphing)_movementAltFrames++;
        bool standing=(p.Flags1&PlayerFlags1.Standing)!=0;
        ObserveLayerBarrier(p,standing);
        if(_movementJumpTrial&&p.Controls.Jump.IsDown&&!standing&&p.Speed.Y>.1f)_movementJumpObserved=true;
        bool driven=p.Controls.MoveUp.IsDown||p.Controls.MoveDown.IsDown||p.Controls.MoveLeft.IsDown||p.Controls.MoveRight.IsDown;
        if(driven)_movementInputFrames++;
        if(_movementSourceTeleport && (int)typeof(PlayerEntity).GetField("_q3TeleportFrictionFrames",ReplayPrivate)!.GetValue(p)!>0)
        {
            _movementSourceExitFrames++;
            var trigger=_movementSourceDefinition!.Q3Triggers!.Single(t=>t.Kind=="teleport");
            var direction=Q3Traversal.ExitDirection(trigger);
            if(_movementSourceExitFrames==1)
                // The sampled velocity can already include the next native input update.
                // The first integrated horizontal displacement proves the applied exit impulse.
                _movementSourceExitImpulse=(p.Position.Xz-Q3Traversal.ToVector(trigger.Destination).Xz-direction.Xz*(trigger.TeleportExitSpeed/60)).Length<.001f
                    && Vector3.Dot(p.FacingVector,direction)>.99f;
        }
        if(_movementSourceLaunch && (int)typeof(PlayerEntity).GetField("_q3LaunchFrames",ReplayPrivate)!.GetValue(p)!>0 && !standing)
        {
            _movementSourceAirFrames++;
            _movementSourceInitialVelocity ??= p.Speed.Xz;
            _movementSourceHorizontalChange=Math.Max(_movementSourceHorizontalChange,(p.Speed.Xz-_movementSourceInitialVelocity.Value).Length);
            if(driven || p.Controls.AnalogMoveActive && (Math.Abs(p.Controls.AnalogMoveX)+Math.Abs(p.Controls.AnalogMoveY))>.01f) _movementSourceAirInputFrames++;
            _movementSourcePeak=Math.Max(_movementSourcePeak,p.Position.Y-_movementStart.Y);
        }
        if(driven&&_movementFocusControlInjected&&Scene.FrameCount>=_movementFocusReleaseUntil)_movementPostFocusInputFrames++;
        float expectedY=_movementGoal.Y+(_movementAlt?Fixed.ToFloat(p.Values.AltColRadius-p.Values.AltColYPos)-.5f:0);
        bool correctForm=_movementAlt?p.IsAltForm&&!p.IsMorphing&&!p.IsUnmorphing:!p.IsAltForm;
        if(OwnTurretTransitSupport(p,out float turretSupportedY)){standing=true;expectedY=turretSupportedY;_transitOwnTurretSupportFrames++;}
        if(_movementCrest&&correctForm)
        {
            var mid=(_movementStart+_movementGoal)/2;
            var normal=_movementStart-_movementGoal;normal.Y=0;normal=normal.Normalized();
            var center=p.Position+Vector3.UnitY*Fixed.ToFloat(p.Values.AltColYPos);
            float side=Vector3.Dot(center-mid,normal);
            if(side<0&&side>-1&&center.Y>mid.Y+_movementWallHeight)
                _movementCrestObserved=true;
        }
        bool bodyOverlap=false;
        // Measure every movement/settling frame after the natural morph and
        // initial spawn settle, using the production full-body recovery query.
        if((correctForm||_bipedTransit)&&Scene.FrameCount-_movementBegin>(ulong)(_movementAlt?40:30))
        {
            object?[] probe={p.Position,p.IsAltForm,default(CollisionResult),false};
            bodyOverlap=(bool)typeof(PlayerEntity).GetMethod("FindDeepestPenetration",ReplayPrivate)!.Invoke(p,probe)!;
            _movementContactOverflows=Math.Max(_movementContactOverflows,(int)typeof(PlayerEntity).GetProperty("ModCollisionContactOverflows",ReplayPrivate)!.GetValue(p)!-_movementContactOverflowStart);
            _movementClearanceFrames++;if(bodyOverlap)_movementBodyOverlaps++;
            _movementBodySaturated|=(bool)probe[3]!;
            _movementMaximumPenetration=Math.Max(_movementMaximumPenetration,((CollisionResult)probe[2]!).Field14);
        }
        if(d.Length<.4f && standing && correctForm && MathF.Abs(p.Position.Y-expectedY)<.65f)_movementArrived++;else _movementArrived=0;
        float[] V(Vector3 v)=>new[]{v.X,v.Y,v.Z};
        _movementFrames.Add(new{frame=Scene.FrameCount-_movementBegin,position=V(p.Position),speed=V(p.Speed),health=p.Health,
            contactDiagnosis=ObserveContactDiagnosis(p,checked((int)(Scene.FrameCount-_movementBegin))),
            standing,driven,bodyOverlap,windowFocused=IsFocused,syntheticInputFocused=MphRead.Mods.Input.GamepadContexts.Focused,facing=V(p.FacingVector),flags=p.Flags1.ToString(),alt=p.IsAltForm,morphing=p.IsMorphing,morphKey=p.Controls.Morph.IsDown,jumpKey=p.Controls.Jump.IsDown,
            controls=new{analogActive=p.Controls.AnalogMoveActive,analogX=p.Controls.AnalogMoveX,analogY=p.Controls.AnalogMoveY,predictedForward=V(_movementInputForward),predictedRight=V(_movementInputRight),rollForward=new[]{(float)typeof(PlayerEntity).GetField("_altRollFbX",ReplayPrivate)!.GetValue(p)!,0f,(float)typeof(PlayerEntity).GetField("_altRollFbZ",ReplayPrivate)!.GetValue(p)!},rollRight=new[]{-(float)typeof(PlayerEntity).GetField("_altRollLrX",ReplayPrivate)!.GetValue(p)!,0f,-(float)typeof(PlayerEntity).GetField("_altRollLrZ",ReplayPrivate)!.GetValue(p)!},up=p.Controls.RollUp.IsDown,down=p.Controls.RollDown.IsDown,left=p.Controls.RolltLeft.IsDown,right=p.Controls.RollRight.IsDown,ground=V((Vector3)typeof(PlayerEntity).GetField("_fieldC0",ReplayPrivate)!.GetValue(p)!)},
            kandenTail=p.Hunter==Hunter.Kanden?p.KandenSegPos.Select(V).ToArray():null,
            spireRocks=p.Hunter==Hunter.Spire&&p.IsAltForm?new[]{V((Vector3)typeof(PlayerEntity).GetField("_spireRockPosL",ReplayPrivate)!.GetValue(p)!),V((Vector3)typeof(PlayerEntity).GetField("_spireRockPosR",ReplayPrivate)!.GetValue(p)!)}:null,horizontalDistance=d.Length});
        if(_movementArrived>=60 || Scene.FrameCount-_movementBegin>=(ulong)(_movementCrest?900:(_movementAlt?(_movementWall?600:660):360)) || p.Health<=0)
        {
            _movementFinished=true;
            Console.WriteLine("MOVEMENTREPLAY finished arrived="+_movementArrived+" inputFrames="+_movementInputFrames);
        }
    }
    internal bool MovementReplayFinished=>_movementReport!=null&&_movementFinished;
    internal bool MovementReplayAccepted=>_movementReport==null || (_movementFinished&&_movementFinalHealth>0&&(!_movementSourceTeleport||_movementSourceExitFrames>=8&&_movementSourceExitImpulse)&&(!_movementSourceLaunch||_movementSourceAirFrames>=20&&_movementSourceAirInputFrames>=10&&_movementSourcePeak>1&&_movementSourceHorizontalChange>.005f)&&(!_movementRequireClearance||_movementClearanceFrames>0&&_movementBodyOverlaps==0&&!_movementBodySaturated&&_movementContactOverflows==0)&&(!_movementFocusControl||_movementFocusControlInjected&&_movementPostFocusInputFrames>=30)&&(!_movementAlt || (_bipedTransit&&_movementWall?_transitStartAltFrames>=60&&_transitUnmorphObserved:_movementFinalAlt&&_movementMorphObserved&&_movementAltFrames>=60))&&(!_movementCrest||_movementCrestObserved)&&(!_movementJumpTrial||_movementJumpObserved)&&TransitAccepted&&(_movementContactPath==null||_movementWall||_movementContactIndex==_movementContactPath.Length-1)&&(_movementWaypoints==null||_movementWall||_movementWaypointIndex==_movementWaypoints.Length-1)&&(_layerBarrier
        ?LayerBarrierAccepted:_movementNonWalkable&&!_movementJumpTrial
        ?_movementArrived==0&&_movementClosest>=.4f&&_movementInputFrames>60
        :_movementWall&&!_movementCrest
        ?_movementArrived==0&&_movementClosest>.8f&&_movementInputFrames>60
        :_movementArrived>=60&&_movementInputFrames>0));
    private object? MovementReplayResult()=>_movementReport==null?null:new{
        scope=_movementPackageRoom ? "Actual packaged map in the OpenGL client, unchanged and verified package collision/physics, live animated hunter and synthetic input. Covers the recorded route; general ability/tail acceptance remains separate." : "Actual OpenGL client simulation, initialized live animated hunter and synthetic keyboard/controller input with optional natural morph; diagnostic-only authored collision swap in native room (window may be hidden). Covers recorded route only, not map visuals or general ability/tail acceptance.",
        sourceTeleportTrial=_movementSourceTeleport,sourceExitFrames=_movementSourceExitFrames,sourceExitImpulseVerified=_movementSourceExitImpulse,
        sourceLaunchTrial=_movementSourceLaunch,sourceApproachReady=_movementSourceApproachReady,sourceHorizontalVelocityChange=_movementSourceHorizontalChange,sourceAirFrames=_movementSourceAirFrames,sourceAirInputFrames=_movementSourceAirInputFrames,sourcePeakAboveStart=_movementSourcePeak,
        packageRoom=_movementPackageRoom,packageCollisionVerified=_movementPackageCollisionVerified,
        certifiedJump=CertifiedJumpResult(),
        manualPackageJump=PackageJumpProbeResult(),
        pressureReleaseFrame=_movementInputUntil,
        layerBarrier=LayerBarrierResult(),
        contactPathSteering=_movementContactPath!=null,contactPathIndex=_movementContactIndex,contactPathPoints=_movementContactPath?.Length,
        fullDirectionalInputTrial=_movementFullInputTrial,stableArrivalInputTarget=_movementStableArrival is {} arrival?new[]{arrival.X,arrival.Y,arrival.Z}:null,requestedInputSpeed=_movementInputSpeed,waypointReplay=_movementWaypoints!=null,waypointIndex=_movementWaypointIndex,waypoints=_movementWaypoints?.Select(v=>new[]{v.X,v.Y,v.Z}).ToArray(),nonWalkingLinkControl=_movementNonWalkable,jumpTrial=_movementJumpTrial,jumpStartFrame=_movementJumpFrame,jumpObserved=_movementJumpObserved,analogInputTrial=_movementAnalogTrial,settlingCoastTrial=_movementSettleCoast,crestControl=_movementCrest,crestObserved=_movementCrestObserved,requireClearance=_movementRequireClearance,clearanceFrames=_movementClearanceFrames,contactOverflows=_movementContactOverflows,bodyOverlaps=_movementBodyOverlaps,bodySaturated=_movementBodySaturated,maximumBodyPenetration=_movementMaximumPenetration,
        syntheticFocusControl=_movementFocusControl,syntheticFocusLossInjected=_movementFocusControlInjected,postFocusLossInputFrames=_movementPostFocusInputFrames,harnessMvid=typeof(CheckWindow).Module.ModuleVersionId,hunter=_movementHunter,bipedTransit=_bipedTransit,bipedTransitPhase=_transitPhase.ToString(),bipedTransitStartAltFrames=_transitStartAltFrames,bipedTransitUnmorphObserved=_transitUnmorphObserved,bipedTransitFlightFrames=_transitBipedFlightFrames,bipedTransitEndAltFrames=_transitEndAltFrames,bipedTransitOwnTurretSupportedFrames=_transitOwnTurretSupportFrames,kandenRestDistances=_movementHunter=="Kanden"?PlayerEntity.KandenAltNodeDistances:null,requestedAlt=_movementAlt,morphTransitionObserved=_movementMorphObserved,stableAltFrames=_movementAltFrames,
        compiledSpawnIndex=_movementSpawnIndex,compiledSpawnPosition=_movementSpawnPosition is { } sp?new[]{sp.X,sp.Y,sp.Z}:null,
        supportedWitnessStart=_movementSupportedStart is { } ws?new[]{ws.X,ws.Y,ws.Z}:null,actualRespawnInvoked=_movementSpawnPosition!=null,spawnSettlingCoastObserved=_movementSpawnCoastObserved,
        supportPrototype=_inputSupportPrototype,
        formRefinementPrototype=_movementFormRefinementPrototype,
        independentInputDiagnostic=_independentInputDiagnostic,
        localFixtureKillHeight=_localFixtureKillHeight,
        finalSupportProbe=ProbeFinalInputSupport(),
        hardwareGamepadSuppressed=true,sourceReport=_movementReport,route=_movementRoute,collisionSha256=Convert.ToHexString(SHA256.HashData(_movementCollision!)),
        started=_movementStarted,finished=_movementFinished,passed=MovementReplayAccepted,blockingControl=_movementWall,blockingWallHalfHeight=_movementWall?(float?)_movementWallHeight:null,closestDestinationDistance=_movementClosest,
        keyboardInputFrames=_movementInputFrames,standingDestinationFrames=_movementArrived,frames=_movementFrames};
}
