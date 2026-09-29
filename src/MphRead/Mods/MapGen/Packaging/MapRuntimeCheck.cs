using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

/// <summary>Asset-free portable package/download/runtime gate, also exercised by the Android diagnostic activity.</summary>
public static class MapRuntimeCheck
{
    public static async Task RunAsync(string root)
    {
        Directory.CreateDirectory(root);Headless.Enter();Paths.SetPath(Paths.MphKey,Path.Combine(root,"runtime"));Directory.CreateDirectory(Paths.FileSystem);
        CustomRooms.UserMapDirectory=Path.Combine(root,"installed");CustomRooms.MapDirectory=Path.Combine(root,"maps");Directory.CreateDirectory(CustomRooms.MapDirectory);
        using(var texture=new BinaryWriter(File.Create(Path.Combine(root,"tile.tex"))))
        {texture.Write(Encoding.ASCII.GetBytes("FPTX"));texture.Write((ushort)1);texture.Write((ushort)1);texture.Write((ushort)0);texture.Write((ushort)8);texture.Write((ushort)8);texture.Write((ushort)1);texture.Write((ushort)0);texture.Write((ushort)32767);texture.Write(new byte[64]);}
        var definition=new MapDefinition{FormatVersion=2,MapId=Guid.NewGuid(),Name="PORTABLE_MAP_CHECK",Version="1",BaseDirectory=root};
        definition.Materials.Add(new(){Texture="tile.tex"});definition.Assets.Add(new(){Path="tile.tex"});definition.Geometry.Add(new MapBox{Transform=new(){Position=new[]{0f,-1,0},Scale=new[]{8f,1,8}}});definition.Spawns.Add(new(){Position=new[]{0f,2,0}});
        string package=MapPackageBuilder.Build(definition,Path.Combine(root,"source.ppmap"));var identity=MapContentIdentity.FromPackage(package);
        using(var read=new MapPackageReader(package))if(read.Manifest?.MapId!=identity.MapId)throw new InvalidDataException("Package identity failed.");
        using var port=new TcpListener(IPAddress.Loopback,0);port.Start();string address="http://127.0.0.1:"+((IPEndPoint)port.LocalEndpoint).Port+"/";port.Stop();
        using var stop=new CancellationTokenSource();const string secret="portable-map-check-local-token";var server=MapCommunityServer.ServeAsync(address,Path.Combine(root,"community"),secret,stop.Token);
        try
        {
            using var client=new MapCommunityClient(address,secret);await client.UploadAsync(package,default);File.Delete(package);
            using var prepared=await client.PrepareExactAsync(identity,default);var installed=prepared.Commit(CustomRooms.UserMapDirectory);Metadata.RegisterDownloadedMap(installed);
            if(!CustomRooms.Installed.HasExact(identity)||CustomRooms.NeedsGenerating(installed)||Read.GetRoomModelInstance(installed.Name).Model.Meshes.Count==0)
                throw new InvalidDataException("Downloaded custom map did not register and decode runtime output.");
        }
        finally{stop.Cancel();try{await server;}catch(OperationCanceledException){}}
    }
}
