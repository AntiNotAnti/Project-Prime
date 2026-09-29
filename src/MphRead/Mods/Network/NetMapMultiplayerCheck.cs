using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    private static CommunityMap[] AcceptanceMaps(string root)=>JsonSerializer.Deserialize<CommunityMap[]>(File.ReadAllText(Path.Combine(root,"maps.json")),MapPackageReader.JsonOptions)!;
    private static MapContentIdentity AcceptanceIdentity(CommunityMap map)=>new(map.MapId,map.Name,MapHash256.Parse(map.ContentHash),MapHash256.Parse(map.Hash),true);
    private static void AcceptancePaths(string root,string role)
    {
        Headless.Enter();string folder=Path.Combine(root,role);Directory.CreateDirectory(folder);Paths.SetPath(Paths.MphKey,Path.Combine(folder,"runtime"));Directory.CreateDirectory(Paths.FileSystem);
        CustomRooms.UserMapDirectory=Path.Combine(folder,"installed");CustomRooms.MapDirectory=Path.Combine(folder,"maps");Directory.CreateDirectory(CustomRooms.MapDirectory);
        CustomRooms.RuntimeNamespace=role;
    }
    /// <summary>Real dedicated server and UDP peers in separate processes; the authority world is the existing asset-free fixture.</summary>
    public static void MapAcceptanceServer(string root,string address)
    {
        AcceptancePaths(root,"server");var maps=AcceptanceMaps(root);using var community=new MapCommunityClient(address);
        foreach(var map in maps.Take(2))
        {
            using var prepared=community.PrepareExactAsync(AcceptanceIdentity(map),default).GetAwaiter().GetResult();
            var installed=prepared.Commit(CustomRooms.UserMapDirectory);Metadata.RegisterDownloadedMap(installed);
            Check(Read.GetRoomModelInstance(installed.Name).Model.Meshes.Count>0,"server private build decodes");
        }
        var rotation=MapRotation.FromList(maps.Take(2).Select(m=>(m.Name,GameMode.Battle)).ToArray(),0,0);
        using var rig=new Rig(room:maps[0].Name,rotation:rotation);rig.Server.MapDownloadSource=address;
        AtomicFile.Write(Path.Combine(root,"server.port"),System.Text.Encoding.UTF8.GetBytes(rig.Server.BoundPort.ToString()));
        bool rotated=false;
        rig.Wait(()=>
        {
            if(!rotated&&File.Exists(Path.Combine(root,"client-a.first"))&&File.Exists(Path.Combine(root,"client-b.first")))
            {
                rig.EndMatchForTest();const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                double now=(double)typeof(DedicatedServer).GetField("_now",flags)!.GetValue(rig.Server)!;
                typeof(DedicatedServer).GetMethod("AdvanceMap",flags)!.Invoke(rig.Server,new object[]{now});rotated=true;
            }
            return File.Exists(Path.Combine(root,"client-a.done"))&&File.Exists(Path.Combine(root,"client-b.done"));
        },"two isolated clients start both maps",90000);
        Check(rotated,"second map preparation followed rotation");
    }
    public static void MapAcceptanceClient(string root,string address,string role)
    {
        AcceptancePaths(root,role);int port=int.Parse(File.ReadAllText(Path.Combine(root,"server.port")));using var client=new Client(port,role=="client-a"?4001u:4002u);
        void Wait(Func<bool> condition,string label,int milliseconds=30000)
        {var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<milliseconds){client.Drain();if(condition())return;Thread.Sleep(5);}throw new InvalidOperationException("Timed out: "+label);}
        Wait(()=>client.Slot>=0&&client.State!=null,"client admission");client.Identify();File.WriteAllText(Path.Combine(root,role+".admitted"),"");
        if(role=="client-disconnect")
        {
            var state=client.State!.Value;byte[] bytes=new byte[MapAvailabilityPacket.Size];new MapAvailabilityPacket(state.MatchId,state.AuthorityEpoch,state.Match.MapIdentity,MapAvailabilityState.Downloading,state.MapGeneration,1).Write(bytes);client.Send(PacketType.MapAvailability,bytes);
            Thread.Sleep(100);Check(!Directory.Exists(CustomRooms.UserMapDirectory),"disconnect during preparation publishes no package");return;
        }
        ushort previous=0;uint sequence=0;
        for(int round=0;round<2;round++)
        {
            Wait(()=>client.State is {} state&&state.Phase==SessionPhase.Lobby&&(round==0||state.MapGeneration!=previous),"next lobby generation");
            var current=client.State!.Value;previous=current.MapGeneration;var required=current.Match.MapIdentity.Content(current.Match.RoomKey);
            void Report(MapAvailabilityState state,uint? reportSequence=null,ushort? generation=null)
            {byte[] bytes=new byte[MapAvailabilityPacket.Size];new MapAvailabilityPacket(current.MatchId,current.AuthorityEpoch,current.Match.MapIdentity,state,generation??current.MapGeneration,reportSequence??++sequence).Write(bytes);client.Send(PacketType.MapAvailability,bytes);}
            Report(MapAvailabilityState.Downloading);
            using(var community=new MapCommunityClient(address))
            using(var prepared=community.PrepareExactAsync(required,default).GetAwaiter().GetResult())
            {var definition=prepared.Commit(CustomRooms.UserMapDirectory);Metadata.RegisterDownloadedMap(definition);Check(Read.GetRoomModelInstance(definition.Name).Model.Meshes.Count>0,"client private runtime prewarm decodes");}
            Check(CustomRooms.Installed.HasExact(required),"client installed exact identity");
            Report(MapAvailabilityState.Ready);var retry=Stopwatch.StartNew();Wait(()=>{if(client.State?.MapAvailability?[client.Slot]==MapAvailabilityState.Ready)return true;if(retry.ElapsedMilliseconds>=250){Report(MapAvailabilityState.Ready);retry.Restart();}return false;},"ready acknowledged");
            Thread.Sleep(160);Report(MapAvailabilityState.Failed,0);Thread.Sleep(160);client.Drain();Check(client.State?.MapAvailability?[client.Slot]==MapAvailabilityState.Ready,"stale readiness cannot regress ready");
            var ready=client.Command(LobbyCommandType.SetReady,true);Wait(()=>client.Results.ContainsKey(ready.CommandId),"ready command reply");
            if(client.Results[ready.CommandId].ResultCode==LobbyResultCode.StaleRevision){ready=client.Command(LobbyCommandType.SetReady,true);Wait(()=>client.Results.ContainsKey(ready.CommandId),"ready retry");}
            Wait(()=>client.Roster.Count>=2&&Enumerable.Range(0,client.Roster.Count).All(i=>client.Roster.LobbyReady[i]),"both players ready");
            if(client.State!.Value.OwnerSlot==client.Slot)
            {
                var start=client.Command(LobbyCommandType.StartMatch);Wait(()=>client.Results.ContainsKey(start.CommandId),"match start reply");
                if(client.Results[start.CommandId].ResultCode==LobbyResultCode.StaleRevision){start=client.Command(LobbyCommandType.StartMatch);Wait(()=>client.Results.ContainsKey(start.CommandId),"start retry");}
                Check(client.Results[start.CommandId].ResultCode==LobbyResultCode.Ok,"match start accepted after exact readiness");
            }
            Wait(()=>client.State?.Phase==SessionPhase.Starting,"load barrier");client.Loaded();Wait(()=>client.State?.Phase==SessionPhase.InMatch,"world-ready barrier");
            File.WriteAllText(Path.Combine(root,role+(round==0?".first":".done")),required.PackageHash.ToString());
        }
        // Keep the second peer connected until the host has observed both completions.
        Thread.Sleep(500);
    }
    public static void MapAcceptanceReplay(string root,string address)
    {
        AcceptancePaths(root,"replay");Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_COMMUNITY",address);var maps=AcceptanceMaps(root);var v1=maps[0];var v2=maps[2];
        using var community=new MapCommunityClient(address);
        using(var prepared=community.PrepareExactAsync(AcceptanceIdentity(v2),default).GetAwaiter().GetResult())Metadata.RegisterDownloadedMap(prepared.Commit(CustomRooms.UserMapDirectory));
        Check(!CustomRooms.Installed.HasExact(AcceptanceIdentity(v1)),"replay starts without v1 archive");
        var session=new SessionStatePacket{Phase=SessionPhase.Lobby,Policy=ServerSessionPolicy.Lobby,MaxPlayers=8,OwnerSlot=255,MatchId=1,AuthorityEpoch=1,MapGeneration=1,MapDownloadSource=address,
            Match=new MatchDefinition{RoomKey=v1.Name,Mode=GameMode.Battle,MapIdentity=new(v1.MapId,MapHash256.Parse(v1.ContentHash),MapHash256.Parse(v1.Hash),NetworkMapFlags.Custom)}};
        byte[] packet=new byte[1+SessionStatePacket.Size];packet[0]=(byte)PacketType.SessionState;session.Write(packet.AsSpan(1));
        var metadata=new ReplayMetadata{RoomKey=v1.Name,Bootstrap=new ReplayBootstrap{Packets=new[]{packet}}};
        string replay=Path.Combine(root,"historical-v1.ppdemo");
        using(var writer=new ReplayWriterV3(replay,metadata))writer.WriteRecord(0,new byte[]{(byte)PacketType.Ping,0,0});
        using var reader=DemoReader.Open(replay,out var opened);
        Check(opened==ReplayOpenResult.Success&&reader?.Metadata!=null,"historical replay file opens");
        var restored=reader!.Metadata!;ReplayMapIdentity.PrepareExactPackage(restored);
        Check(CustomRooms.Installed.HasExact(AcceptanceIdentity(v1))&&ReplayMapIdentity.Validate(restored)==ReplayOpenResult.Success,"historical replay fetches v1 after v2 publication");
        Check(!CustomRooms.Installed.TryGet(v1.MapId,out var installed)||installed.Identity.PackageHash==MapHash256.Parse(v1.Hash),"historical replay never substitutes v2");
        Console.WriteLine("Historical replay package resolution passed.");
    }
}
