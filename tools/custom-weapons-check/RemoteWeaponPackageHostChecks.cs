using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Network;
using MphRead.Mods.Launcher;
using MphRead.Mods.Platform;

internal static class RemoteWeaponPackageHostChecks
{
    internal static int Run(string data,string output,string serverDll,string route)
    {
        string cwd=Directory.GetCurrentDirectory();int checks=0;Process? parent=null;var logs=new StringBuilder();object logGate=new();string? previous=Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE");var receipts=new List<object>();
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks++;Console.WriteLine("REMOTE HOST PASS "+label);}
        void Until(Func<bool> done,string label,int seconds=25){var clock=Stopwatch.StartNew();while(!done()){if(parent?.HasExited==true)throw new InvalidOperationException("Owned allocator exited "+parent.ExitCode);if(clock.Elapsed.TotalSeconds>seconds)throw new TimeoutException(label);NetSession.Pump();Thread.Sleep(20);}Check(true,label);}
        void Stop(){if(parent==null)return;try{if(!parent.HasExited){parent.Kill(true);parent.WaitForExit(8000);}}finally{parent.Dispose();parent=null;}}
        try
        {
            output=Path.GetFullPath(output);data=Path.GetFullPath(data);serverDll=Path.GetFullPath(serverDll);Directory.CreateDirectory(output);
            Check(route is "directory" or "dedicated","explicit real allocator route");
            string profile=Path.GetFullPath(AppPaths.UserDataDirectory);Check(Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA")!=null&&profile.StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"fresh allocator proof owns private profile beneath output");
            Check(!NetSession.Active&&LocalServer.Available() is {} candidate&&Path.GetFullPath(candidate.WorkingDirectory)==Path.GetDirectoryName(serverDll),"private installed child candidate matches explicitly supplied dedicated assembly directory");
            Directory.SetCurrentDirectory(data);
            Check(Directory.Exists(data)&&GameFiles.Problem()==null,"operator game files are available through private profile");
            var ports=new List<int>();var reservations=new List<UdpClient>();
            try
            {
                
                bool found=false;for(int trial=0;trial<200&&!found;trial++)
                {int start=20000+Random.Shared.Next(0,30000);try{for(int n=0;n<5;n++){reservations.Add(new(new IPEndPoint(IPAddress.Loopback,start+n)));}ports.AddRange(Enumerable.Range(start,5));found=true;}catch(SocketException){foreach(var socket in reservations)socket.Dispose();reservations.Clear();}}
                Check(found,"five private contiguous UDP ports reserved before allocator launch");
            }
            finally{foreach(var socket in reservations)socket.Dispose();}
            int allocatorPort=ports[0];int[] childPorts=ports.Skip(1).ToArray();
            string storePath=Path.Combine(output,"approved-store"),clientPath=Path.Combine(output,"client-store");var store=new WeaponPackageStore(storePath);var client=new WeaponPackageStore(clientPath);byte[] archive=WeaponPackageReference.BuildRiftlineM7();var package=store.Install(archive);client.Install(archive);
            // A metadata-only revision gives a distinct exact archive to corrupt.
            var resource=package.ResourcePaths.Where(p=>p!="weapon.json").ToDictionary(p=>p,p=>package.CopyResource(p));
            var project=System.Text.Json.Nodes.JsonNode.Parse(package.Project.ToCanonicalBytes())!;project["name"]="Corruption probe archive";
            string corruptStorePath=Path.Combine(output,"corrupt-approved-store");var corruptStore=new WeaponPackageStore(corruptStorePath);
            var alternate=corruptStore.Install(WeaponPackageBuilder.Build(WeaponProject.Parse(Encoding.UTF8.GetBytes(project.ToJsonString())),resource));File.WriteAllBytes(Path.Combine(corruptStorePath,"archives",alternate.PackageHash+".ppweapon"),new byte[]{1,2,3});
            string rotation=Path.Combine(output,"parent-rotation.txt");MapRotation.WriteList(rotation,new[]{("MP3 PROVING GROUND",GameMode.Battle)},0,10000);
            void Launch(bool approved,string? approvedStore=null)
            {
                int logStart;lock(logGate)logStart=logs.Length;
                string dotnet=Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".dotnet","dotnet.exe");
                var start=new ProcessStartInfo(dotnet){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=profile};start.ArgumentList.Add(serverDll);
                var args=new List<string>{route=="directory"?"-masterserver":"-server","-port",allocatorPort.ToString(),"-hostports",$"{childPorts[0]}-{childPorts[^1]}","-noupdate","-noautoupdate"};
                if(route=="dedicated")args.AddRange(new[]{"-lobby","-rotation",rotation,"-nomaster","-noserverreplays"});if(approved)args.AddRange(new[]{"-hostweaponstore",approvedStore??storePath});foreach(string arg in args)start.ArgumentList.Add(arg);
                start.Environment["PROJECT_PRIME_USER_DATA"]=profile;start.Environment["DOTNET_PROCESSOR_COUNT"]="2";parent=new(){StartInfo=start};
                void Log(object sender,DataReceivedEventArgs line){if(line.Data==null)return;lock(logGate){int left=128*1024-logs.Length;if(left>0)logs.Append(line.Data.AsSpan(0,Math.Min(left,line.Data.Length))).Append('\n');}}
                parent.OutputDataReceived+=Log;parent.ErrorDataReceived+=Log;parent.Start();parent.BeginOutputReadLine();parent.BeginErrorReadLine();
                Until(()=>{lock(logGate)return logs.ToString(logStart,logs.Length-logStart).Contains("listening",StringComparison.OrdinalIgnoreCase);},"owned "+route+" allocator listens",30);
            }
            bool Online(int port)=>NetStatus.Query("127.0.0.1",port,false,200).Online;
            void NoPorts(string label)=>Check(childPorts.All(p=>!Online(p)),label+" leaves all reserved child ports offline");
            HostedGame Request(string? hash)=>NetMasterClient.RequestGame("127.0.0.1",allocatorPort,"MP3 PROVING GROUND",GameMode.Battle,0,10000,2,"Exact remote host",timeoutMs:6000,weaponArchiveHash:hash);
            void Refused(HostedGame result,string label){Check(!result.Started&&result.Port==0&&!result.Reason.Contains(output,StringComparison.OrdinalIgnoreCase),label+" fails before allocation without private-path disclosure: "+result.Reason);NoPorts(label);}
            Launch(false);Refused(Request(package.PackageHash),"unapproved package");Stop();lock(logGate)logs.AppendLine("=== approved allocator ===");
            // Readiness for the replacement is proven by its genuine challenge,
            // not by a stale prior-process log line.
            Launch(true,corruptStorePath);
            using(var socket=new UdpClient(AddressFamily.InterNetwork))
            {
                socket.Client.ReceiveTimeout=3000;var endpoint=new IPEndPoint(IPAddress.Loopback,allocatorPort);var challenge=new HostChallengePacket(NetConfig.ProtocolVersion,0x12830001);byte[] request=new byte[1+HostChallengePacket.Size];request[0]=(byte)PacketType.HostChallenge;challenge.Write(request.AsSpan(1));socket.Send(request,request.Length,endpoint);var from=new IPEndPoint(IPAddress.Any,0);byte[] reply=socket.Receive(ref from);
                Check(from.Equals(endpoint)&&reply[0]==(byte)PacketType.HostChallengeReply&&HostChallengeReplyPacket.TryRead(reply.AsSpan(1),out var proof),"actual approved allocator completes return-path challenge");
                HostChallengeReplyPacket.TryRead(reply.AsSpan(1),out var cookie);var old=new HostRequestPacket{Protocol=82,MaxPlayers=2,Mode=(byte)GameMode.Battle,RoomKey="MP3 PROVING GROUND",ServerName="Old82",HostNonce=cookie.Nonce,HostCookie=cookie.Cookie};byte[] current=new byte[old.Length];old.Write(current);byte[] oldWire=new byte[1+current.Length-32];oldWire[0]=(byte)PacketType.HostRequest;current.AsSpan(0,current.Length-32).CopyTo(oldWire.AsSpan(1));socket.Send(oldWire,oldWire.Length,endpoint);reply=socket.Receive(ref from);
                Check(from.Equals(endpoint)&&reply[0]==(byte)PacketType.HostReply&&!HostReplyPacket.Read(reply.AsSpan(1)).Started,"genuine protocol82 request is refused by current allocator over UDP");NoPorts("protocol82 refusal");
            }
            Refused(Request(new string('b',64)),"missing exact archive");Refused(Request(alternate.PackageHash),"corrupt exact archive");
            Stop();Launch(true);
            var hosted=Request(package.PackageHash);Check(hosted.Started&&childPorts.Contains(hosted.Port)&&hosted.OwnerToken!=Guid.Empty,"real RequestGame challenge/reply/status barrier starts exact package child");
            Check(NetLaunch.Connect("127.0.0.1",hosted.Port,"RemotePackageOwner",Hunter.Samus,ownerToken:hosted.OwnerToken,weaponPackage:new(clientPath,package.PackageHash)),"ordinary owner joins actual allocated child with explicit client store");
            Until(()=>NetSession.LocalIsLobbyOwner&&NetSession.RequiredWeaponPackagesReady,"actual child confirms ordinary exact package preparation");
            Check(NetSession.ServerSession is {Match.PackagedWeaponAvailable:true}&&Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE")==previous,"child advertises package while global client environment remains unchanged");
            Check(store.Uninstall(package.PackageHash)&&store.Prune()==0,"actual allocated child owns independent archive lease after worker reply");NetSession.Stop();
            Until(()=>!Online(hosted.Port),"last owner disconnect retires process-owned package child",30);int removed=0;Until(()=>{removed+=store.Prune();return removed==1;},"archive prunes after child retirement while allocator and cached reply remain alive");Check(parent is {HasExited:false},"parent allocator remains alive after child lease release");
            receipts.Add(new{route,parentPid=parent!.Id,allocatorPort,childPorts,hostedPort=hosted.Port,hash=package.PackageHash,serverSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(serverDll)))});
            // Use a fresh owned allocator for the independent native regression;
            // native behavior is independent of the previous package child lifetime.
            Stop();Launch(true);var native=Request(null);Check(native.Started&&childPorts.Contains(native.Port),"native omission still allocates ordinary child");
            Check(NetLaunch.Connect("127.0.0.1",native.Port,"NativeRemoteOwner",Hunter.Samus,ownerToken:native.OwnerToken),"ordinary native client joins allocated native child");Check(NetSession.ServerSession is {Match.PackagedWeaponAvailable:false},"native omission carries no packaged availability");NetSession.Stop();
            File.WriteAllText(Path.Combine(output,"remote-host-results.json"),JsonSerializer.Serialize(new{checks,receipts},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception error){Console.Error.WriteLine("REMOTE PACKAGE HOST FAIL "+error);return 1;}
        finally{NetSession.Stop();Stop();Directory.SetCurrentDirectory(cwd);Directory.CreateDirectory(output);lock(logGate)File.WriteAllText(Path.Combine(output,"allocator-console.log"),logs.ToString());}
    }
}
