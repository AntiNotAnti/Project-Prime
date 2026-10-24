using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.HeroModes;

// Real objective constructors, AI initialization and input processing in a
// controlled asset-free scene. Full personality/world execution is maptest's job.
internal static class CtfBotStartupChecks
{
    internal static void Run(Action<bool,string> check)
    {
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        var scene=new Scene(new(64,64),null!,null!,_=>{},()=>{},initializeRuntime:false);
        scene.GameState.ConfigureMatchMode(GameMode.CaptureTheFlag,true);
        CollisionDetection.Init();
        var room=new RoomEntity(scene);
        typeof(Scene).GetField("_room",hidden)!.SetValue(scene,room);
        var nodes=new NodeData(default,Array.Empty<ushort>(),new[]{new[]{new[]{new NodeData3(default(Vector3Fx))}}});
        // SetNodeData also loads a visualization model; this fixture only needs
        // the actual navigation data consumed by InitializeMain.
        typeof(RoomEntity).GetField("_nodeData",hidden)!.SetValue(room,nodes);
        T Data<T>(EntityType type,short id,int team,Vector3 position,int bytes) where T:struct
        {
            var data=new byte[bytes];var header=new EntityDataHeader((ushort)type,id,position,Vector3.UnitY,Vector3.UnitZ);
            MemoryMarshal.Write(data.AsSpan(),in header);data[40]=(byte)team;
            return Read.ReadStruct<T>(data);
        }
        var flags=Enumerable.Range(0,2).Select(t=>new OctolithFlagEntity(
            Data<OctolithFlagEntityData>(EntityType.OctolithFlag,(short)(100+t),t,new(t*6,0,0),41),scene)).ToArray();
        var bases=Enumerable.Range(0,2).Select(t=>new FlagBaseEntity(
            Data<FlagBaseEntityData>(EntityType.FlagBase,(short)(110+t),t,new(t*6,0,0),108),scene)).ToArray();
        foreach(var flag in flags)scene.InsertEntity(flag);
        foreach(var flagBase in bases)scene.InsertEntity(flagBase);
        scene.Players.Construct(scene);
        var aiType=typeof(PlayerEntity.PlayerAiData);
        object? Field(object ai,string name)=>aiType.GetField(name,hidden)!.GetValue(ai);
        object? Invoke(object ai,string name,params object?[] args)=>aiType.GetMethod(name,hidden)!.Invoke(ai,args);
        foreach(var player in scene.Players.Items)
        {
            player.TeamIndex=player.SlotIndex%2;player.IsBot=true;player.Health=100;player.LoadFlags=LoadFlags.Spawned;
            player.Position=new(player.TeamIndex*6,0,0);scene.InsertEntity(player);
            var ai=player.AiData;Invoke(ai,"InitializeMain");int own=player.TeamIndex,enemy=1-own;
            check(ReferenceEquals(Field(ai,"_octolithFlagCC"),flags[own])&&ReferenceEquals(Field(ai,"_octolithFlagD4"),flags[own])
                &&ReferenceEquals(Field(ai,"_octolithFlagDC"),flags[enemy])&&ReferenceEquals(Field(ai,"_flagBaseD0"),bases[own])
                &&ReferenceEquals(Field(ai,"_flagBaseD8"),bases[own])&&ReferenceEquals(Field(ai,"_flagBaseE0"),bases[enemy])
                &&!(bool)Field(ai,"_forceDisable")!,$"CTF bot slot {player.SlotIndex}: binds both teams' real flag/base references");
            check((int)Invoke(ai,"Func3_213BAF4",null,new AiPersonalityData5(4096*2,0))! ==1,
                $"CTF bot slot {player.SlotIndex}: first capture selector executes against its own flag");
        }
        var bot=scene.Players.Items[0];var brain=bot.AiData;
        bot.Position=new(6,0,0);brain.ProcessInput();
        check(bot.Controls.Zoom.IsPressed&&bot.Controls.Zoom.IsDown&&flags[1].Carrier==null,
            "CTF bot submits a normal Zoom pickup edge after production eligibility checks, without assigning ownership");
        brain.ProcessInput();
        check(!bot.Controls.Zoom.IsDown&&bot.Controls.Zoom.IsReleased,"CTF bot releases between pickup attempts");
        typeof(OctolithFlagEntity).GetField("_carrier",hidden)!.SetValue(flags[0],scene.Players.Items[1]);
        scene.Players.Items[1].Position=new(100,0,0);bot.Position=Vector3.Zero;
        scene.GameState.ConfigureMatchMode(GameMode.Bounty,true);
        check((int)Invoke(brain,"Func3_213B690",null,new AiPersonalityData5())! ==1,
            "The carrier-distance selector remains live outside team capture modes");
        foreach(var mode in new[]{GameMode.Capture,GameMode.CaptureTheFlag})
        {
            scene.GameState.ConfigureMatchMode(mode,true);
            check((int)Invoke(brain,"Func3_213B690",null,new AiPersonalityData5())! ==0,
                $"{mode}: capture-only selector excludes the bounty carrier-distance branch");
        }
        scene.RemoveEntity(flags[1]);brain.Reset();Invoke(brain,"InitializeMain");
        check((bool)Field(brain,"_forceDisable")!,"CTF map missing an opposing objective retains the existing AI disable safeguard");
        brain.Process();
        check(true,"Disabled incompatible-map bot processing exits before an uninitialized personality tree");
    }
}
