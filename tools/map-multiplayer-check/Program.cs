using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

const string fixtureToken="map-multiplayer-local-fixture-token";
if(args.Length>0&&args[0]=="--worker")
{
    string role=args[1],folder=args[2],address=args[3];
    if(role=="community")
    {
        using var stop=new CancellationTokenSource();var service=MapCommunityServer.ServeAsync(address,Path.Combine(folder,"community"),fixtureToken,stop.Token);
        while(!File.Exists(Path.Combine(folder,"community.stop"))&&!service.IsCompleted)await Task.Delay(50);
        stop.Cancel();try{await service;}catch(OperationCanceledException){}
    }
    else if(role=="server")NetLobbyTest.MapAcceptanceServer(folder,address);
    else if(role=="replay")NetLobbyTest.MapAcceptanceReplay(folder,address);
    else NetLobbyTest.MapAcceptanceClient(folder,address,role);
    return;
}
string root=args.Length>0?Path.GetFullPath(args[0]):Path.Combine(Path.GetTempPath(),"prime-multiplayer-"+Guid.NewGuid().ToString("N"));
if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any())throw new IOException("Use an empty acceptance output folder.");Directory.CreateDirectory(root);
var children=new List<(Process Process,Task Log)>();
Process Spawn(string role,string folder,string address)
{
    var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
    if((Path.GetFileNameWithoutExtension(Environment.ProcessPath)??"").Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    foreach(string argument in new[]{"--worker",role,folder,address})start.ArgumentList.Add(argument);
    var process=Process.Start(start)??throw new IOException("Cannot start acceptance worker.");
    var log=Task.Run(async()=>{var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await Task.WhenAll(stdout,stderr);await File.WriteAllTextAsync(Path.Combine(folder,role+".log"),stdout.Result+stderr.Result);});
    children.Add((process,log));return process;
}
async Task Finished(Process process,string name)
{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(120));await children.Single(p=>p.Process==process).Log;if(process.ExitCode!=0)throw new Exception(name+" failed; inspect its log in "+root);}
async Task WaitFile(string path,Process process)
{var watch=Stopwatch.StartNew();while(!File.Exists(path)){if(process.HasExited){await Finished(process,"worker");throw new Exception("Worker exited before creating "+path);}if(watch.Elapsed.TotalSeconds>60)throw new TimeoutException(path);await Task.Delay(20);}}
void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS: "+message);}
using var port=new TcpListener(IPAddress.Loopback,0);port.Start();string hub="http://127.0.0.1:"+((IPEndPoint)port.LocalEndpoint).Port+"/";port.Stop();
Process? community=null;
try
{
    Headless.Enter();Paths.SetPath(Paths.MphKey,Path.Combine(root,"fixture-runtime"));Directory.CreateDirectory(Paths.FileSystem);
    CustomRooms.UserMapDirectory=Path.Combine(root,"fixture-library");CustomRooms.MapDirectory=Path.Combine(root,"fixture-maps");Directory.CreateDirectory(CustomRooms.MapDirectory);
    using(var texture=new BinaryWriter(File.Create(Path.Combine(root,"tile.tex"))))
    {texture.Write(Encoding.ASCII.GetBytes("FPTX"));texture.Write((ushort)1);texture.Write((ushort)1);texture.Write((ushort)0);texture.Write((ushort)8);texture.Write((ushort)8);texture.Write((ushort)1);texture.Write((ushort)0);texture.Write((ushort)32767);texture.Write(new byte[64]);}
    string Package(Guid id,string name,string version)
    {
        var definition=new MapDefinition{FormatVersion=2,MapId=id,Name=name,Version=version,BaseDirectory=root};definition.Materials.Add(new(){Texture="tile.tex"});definition.Assets.Add(new(){Path="tile.tex"});
        definition.Geometry.Add(new MapBox{Transform=new(){Position=new[]{0f,-1,0},Scale=new[]{8f,1,8}}});definition.Spawns.Add(new(){Position=new[]{0f,2,0}});definition.Spawns.Add(new(){Position=new[]{2f,2,0}});
        return MapPackageBuilder.Build(definition,Path.Combine(root,name+"-"+version+".ppmap"));
    }
    Guid identity=Guid.NewGuid();string v1=Package(identity,"ACCEPT_A","1"),second=Package(Guid.NewGuid(),"ACCEPT_B","1"),v2=Package(identity,"ACCEPT_A","2");
    community=Spawn("community",root,hub);
    using var http=new HttpClient{BaseAddress=new Uri(hub)};var waiting=Stopwatch.StartNew();
    while(true){try{using var response=await http.GetAsync("health");if(response.IsSuccessStatusCode)break;}catch(HttpRequestException){}if(waiting.Elapsed.TotalSeconds>15)throw new TimeoutException("Community fixture did not start.");await Task.Delay(50);}
    using var client=new MapCommunityClient(hub,fixtureToken);
    var maps=new[]{await client.UploadAsync(v1,default),await client.UploadAsync(second,default),await client.UploadAsync(v2,default)};
    string manifest=JsonSerializer.Serialize(maps,MapPackageReader.JsonOptions);File.WriteAllText(Path.Combine(root,"maps.json"),manifest);
    for(int round=0;round<2;round++)
    {
        string folder=Path.Combine(root,"run-"+round);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"maps.json"),manifest);
        var server=Spawn("server",folder,hub);await WaitFile(Path.Combine(folder,"server.port"),server);
        var a=Spawn("client-a",folder,hub);await WaitFile(Path.Combine(folder,"client-a.admitted"),a);await Finished(Spawn("client-disconnect",folder,hub),"Disconnect during preparation");var b=Spawn("client-b",folder,hub);
        await Task.WhenAll(Finished(a,"Client A"),Finished(b,"Client B"),Finished(server,"Dedicated server"));
        Check(File.Exists(Path.Combine(folder,"client-a.done"))&&File.Exists(Path.Combine(folder,"client-b.done")),round==0?"two isolated clients prepare, prewarm, start and rotate":"server restart repeats exact preparation and rotation");
    }
    var required=new MapContentIdentity(maps[0].MapId,maps[0].Name,MapHash256.Parse(maps[0].ContentHash),MapHash256.Parse(maps[0].Hash),true);
    foreach(var bad in new[]{required with{PackageHash=MapHash256.Parse(new string('e',64))},required with{ContentHash=MapHash256.Parse(new string('f',64))}})
    {bool rejected=false;try{using var prepared=await client.PrepareExactAsync(bad,default);}catch(Exception ex)when(ex is HttpRequestException or InvalidDataException or IOException){rejected=true;}Check(rejected,"incorrect package/content hash rejected");}
    byte[] archive=File.ReadAllBytes(v1);string truncated=Path.Combine(root,"truncated.ppmap");File.WriteAllBytes(truncated,archive[..(archive.Length/2)]);
    bool invalid=false;try{using var prepared=await MapPackageInstaller.PrepareAsync(truncated,required);}catch(Exception ex)when(ex is InvalidDataException or IOException){invalid=true;}Check(invalid,"truncated archive rejected");
    using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();bool stopped=false;try{using var prepared=await client.PrepareExactAsync(required,cancelled.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"pre-cancelled download cannot install");}
    // A real HTTP peer sends partial or substituted archive bytes to the production downloader.
    foreach(string fault in new[]{"truncated HTTP body","mid-stream cancellation","substituted version"})
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        string endpoint="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/";
        using var cancellation=new CancellationTokenSource();
        byte[] body=fault=="substituted version"?File.ReadAllBytes(v2):archive;
        var peer=Task.Run(async()=>
        {
            using var connection=await listener.AcceptTcpClientAsync();await using var stream=connection.GetStream();
            using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
            while(await reader.ReadLineAsync() is {Length:>0}){}
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n\r\n"));
            await stream.WriteAsync(body.AsMemory(0,fault=="substituted version"?body.Length:body.Length/2));await stream.FlushAsync();
            if(fault=="mid-stream cancellation")await Task.Delay(300);
        });
        using var faulty=new MapCommunityClient(endpoint);bool rejected=false;
        try{using var prepared=await faulty.PrepareExactAsync(required,cancellation.Token,progress:_=>{if(fault=="mid-stream cancellation")cancellation.Cancel();});}
        catch(Exception ex)when(ex is HttpRequestException or InvalidDataException or IOException or OperationCanceledException){rejected=true;}
        await peer.WaitAsync(TimeSpan.FromSeconds(10));Check(rejected,fault+" cannot publish a package");
    }
    var concurrent=await Task.WhenAll(client.PrepareExactAsync(required,default),client.PrepareExactAsync(required,default));foreach(var prepared in concurrent){Check(prepared.Identity.Matches(required),"duplicate concurrent download preserves identity");prepared.Dispose();}
    File.WriteAllText(Path.Combine(root,"community.stop"),"");await Finished(community,"Community");File.Delete(Path.Combine(root,"community.stop"));community=Spawn("community",root,hub);
    waiting.Restart();while(true){try{using var response=await http.GetAsync("health");if(response.IsSuccessStatusCode)break;}catch(HttpRequestException){}if(waiting.Elapsed.TotalSeconds>15)throw new TimeoutException("Community restart did not become ready.");await Task.Delay(50);}
    Check((await client.GetPackageAsync(maps[0].Hash,default))?.Hash==maps[0].Hash,"historical version survives Community restart");
    await Finished(Spawn("replay",root,hub),"Historical replay");
    Console.WriteLine("PASS: map multiplayer acceptance. Logs: "+root);
}
finally
{
    File.WriteAllText(Path.Combine(root,"community.stop"),"");
    if(community!=null&&!community.HasExited)try{await community.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}catch(TimeoutException){}
    foreach(var child in children){if(!child.Process.HasExited)child.Process.Kill(entireProcessTree:true);await child.Process.WaitForExitAsync();await child.Log;child.Process.Dispose();}
}
