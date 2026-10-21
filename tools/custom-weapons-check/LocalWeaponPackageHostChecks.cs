using System.Diagnostics;
using System.Buffers.Binary;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.Platform;

// Fresh-process private-profile test. No retail profile/binary or game bytes are
// written. The actual installed Windows console server owns its own process.
internal static class LocalWeaponPackageHostChecks
{
    internal static int Run(string data,string output)
    {
        int checks=0;var watch=Stopwatch.StartNew();string cwd=Directory.GetCurrentDirectory();string? priorStore=Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE");
        LobbyWitness? witness=null;Process? packageChild=null,nativeChild=null;var receipts=new List<object>();
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks++;Console.WriteLine("LOCAL PACKAGE HOST PASS "+label);}
        void Reject(Action action,string label){try{action();throw new InvalidOperationException("Expected rejection: "+label);}catch(Exception error)when(error is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException){Check(true,label);}}
        try
        {
            output=Path.GetFullPath(output);data=Path.GetFullPath(data);Directory.CreateDirectory(output);
            string profile=Path.GetFullPath(AppPaths.UserDataDirectory),profilePrefix=output.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            Check(OperatingSystem.IsWindows()&&Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA")!=null
                &&(profile.Equals(output,StringComparison.OrdinalIgnoreCase)||profile.StartsWith(profilePrefix,StringComparison.OrdinalIgnoreCase)),"fresh Windows process owns explicit private profile inside output root");
            Check(Path.GetFullPath(LauncherPrefs.Directory)==profile&&Path.GetFullPath(GameFiles.Root)==profile,"profile was selected before launcher/game-file static initialization");
            Check(!NetSession.Active&&LocalServer.Running==null,"fresh harness owns no previous network session or child");
            Directory.SetCurrentDirectory(profile);
            byte[] archive=WeaponPackageReference.BuildRiftlineM7();var package=WeaponPackage.Parse(archive);string tests=Path.Combine(output,"selection-tests");Directory.CreateDirectory(tests);
            string absent=Path.Combine(tests,"missing space store");var selection=new LocalWeaponPackageSelection(absent,package.PackageHash);
            Check(selection.StoreDirectory==Path.GetFullPath(absent)&&selection.ArchiveHash==package.PackageHash&&!Directory.Exists(absent),"immutable selection validates canonical path/hash without creating store");
            foreach(string bad in new[]{"",new string('A',64),new string('a',63),new string('g',64),new string('0',64)})Reject(()=>new LocalWeaponPackageSelection(absent,bad),"selection rejects malformed archive hash");
            foreach(string bad in new[]{"",Path.Combine(tests,"file:stream"),Path.Combine(tests,"nul"),Path.Combine(tests,"line\nfeed")})Reject(()=>new LocalWeaponPackageSelection(bad,package.PackageHash),"selection rejects unsafe disk path");
            var arguments=new ProcessStartInfo("owned-server.exe");arguments.ArgumentList.Add("-prefix");arguments.ArgumentList.Add("untouched");selection.AppendArguments(arguments);
            Check(arguments.ArgumentList.SequenceEqual(new[]{"-prefix","untouched","-weaponstore",Path.GetFullPath(absent),"-weaponarchive",package.PackageHash}),"selection appends exact paired arguments preserving spaces and existing arguments");
            const BindingFlags statics=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
            var running=typeof(LocalServer).GetProperty(nameof(LocalServer.Running),statics)!;var token=typeof(LocalServer).GetProperty(nameof(LocalServer.OwnerToken),statics)!;var library=typeof(LocalServer).GetProperty("HostedLibrary",statics)!;
            object? oldRunning=running.GetValue(null),oldToken=token.GetValue(null),oldLibrary=library.GetValue(null);using var sentinel=new Process();Guid sentinelToken=Guid.NewGuid();string sentinelLibrary=Path.Combine(tests,"never-created-owned-library");
            string[] Rotations()=>Directory.Exists(LocalServer.Directory)?Directory.GetFiles(LocalServer.Directory,"maprotation-launcher-*.txt").Order(StringComparer.Ordinal).ToArray():Array.Empty<string>();
            void Preflight(LocalWeaponPackageSelection candidate,string label,CancellationToken cancellation=default,GameMode mode=GameMode.Battle)
            {
                string[] rotations=Rotations();int result=LocalServer.Start("preflight",new[]{("MP3 PROVING GROUND",mode)},2,0,10000,"127.0.0.1",1,false,cancel:cancellation,weaponPackage:candidate);
                Check(result==-1&&ReferenceEquals(LocalServer.Running,sentinel)&&LocalServer.OwnerToken==sentinelToken&&(string?)library.GetValue(null)==sentinelLibrary&&Rotations().SequenceEqual(rotations),label+" preserves exact Running/token/library and writes no rotation");
            }
            lock(LocalServer.StartLifecycleGate)
            {
                running.SetValue(null,sentinel);token.SetValue(null,sentinelToken);library.SetValue(null,sentinelLibrary);
                try
                {
                    Preflight(selection,"missing-store package preflight");Check(!Directory.Exists(absent),"missing preflight creates no store root");
                    string corruptPath=Path.Combine(tests,"corrupt");var corrupt=new WeaponPackageStore(corruptPath);corrupt.Install(archive);File.WriteAllBytes(Path.Combine(corruptPath,"archives",package.PackageHash+".ppweapon"),new byte[]{1,2,3});Preflight(new(corruptPath,package.PackageHash),"corrupt archive package preflight");
                    string missingArchivePath=Path.Combine(tests,"missing-archive");new WeaponPackageStore(missingArchivePath).Install(archive);File.Delete(Path.Combine(missingArchivePath,"archives",package.PackageHash+".ppweapon"));Preflight(new(missingArchivePath,package.PackageHash),"missing installed archive preflight");
                    string markerPath=Path.Combine(tests,"bad-marker");new WeaponPackageStore(markerPath).Install(archive);File.WriteAllText(Path.Combine(markerPath,"installed",package.PackageHash+".installed.json"),"{}");Preflight(new(markerPath,package.PackageHash),"corrupt marker package preflight");
                    string retainedPath=Path.Combine(tests,"retained");var retained=new WeaponPackageStore(retainedPath);retained.Install(archive);var retainedSelection=new LocalWeaponPackageSelection(retainedPath,package.PackageHash);
                    using(var lease=retainedSelection.Acquire(default)){Check(retained.Uninstall(package.PackageHash)&&retained.Prune()==0,"selection parent lease protects archive bytes after uninstall");Reject(()=>retained.AcquireInstalled(package.PackageHash).Dispose(),"uninstalled bytes cannot satisfy independent installed child acquisition");Preflight(retainedSelection,"uninstalled marker preflight");}Check(retained.Prune()==1,"parent selection disposal releases final retained archive");
                    using(var cancellation=new CancellationTokenSource()){cancellation.Cancel();Preflight(selection,"pre-cancelled package preflight",cancellation.Token);}
                    string validPath=Path.Combine(tests,"valid-mode");new WeaponPackageStore(validPath).Install(archive);Preflight(new(validPath,package.PackageHash),"incompatible package mode preflight",mode:GameMode.Survival);
                }
                finally{running.SetValue(null,oldRunning);token.SetValue(null,oldToken);library.SetValue(null,oldLibrary);}
            }
            var binary=LocalServer.Available();string expectedServerRoot=Path.GetFullPath(Path.Combine(profile,"server"));
            Check(binary is {} candidate&&candidate.Downloaded&&Path.GetFullPath(candidate.WorkingDirectory)==expectedServerRoot
                &&Path.GetFullPath(candidate.Executable).StartsWith(expectedServerRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"actual LocalServer selects private installed dedicated executable rather than beside-tool binary");
            Check(Directory.Exists(data)&&GameFiles.Problem()==null,"private versioned paths file points to operator game files");
            string serverStorePath=Path.Combine(output,"actual-server-store");var serverStore=new WeaponPackageStore(serverStorePath);serverStore.Install(archive);string defaultClientStore=NetSession.WeaponPackageStoreDirectory;
            int port=LocalServer.Start("Private package host proof",new[]{("MP3 PROVING GROUND",GameMode.Battle)},2,0,10000,"127.0.0.1",1,false,lobby:false,weaponPackage:new(serverStorePath,package.PackageHash));
            Check(port>0&&LocalServer.Running is {HasExited:false}&&LocalServer.OwnerToken!=Guid.Empty,"actual package Start launches independent child and forces token-owning persistent lobby");Guid ownerToken=LocalServer.OwnerToken;
            lock(LocalServer.StartLifecycleGate)packageChild=LocalServer.DetachRunning();
            Check(packageChild!=null&&packageChild.Id!=Environment.ProcessId&&LocalServer.Running==null,"exact newly launched process transfers ownership atomically without stopping child");
            var childArgs=packageChild!.StartInfo.ArgumentList;
            Check(childArgs.Count(x=>x=="-weaponstore")==1&&childArgs.Count(x=>x=="-weaponarchive")==1&&childArgs.Contains(serverStorePath)&&childArgs.Contains(package.PackageHash)&&childArgs.Contains("-lobby")&&childArgs.Contains(ownerToken.ToString("N")),"actual LocalServer child command line carries exact package and owner token once");
            void Until(Func<bool> condition,string label){var deadline=Stopwatch.StartNew();while(!condition()){if(deadline.Elapsed.TotalSeconds>15)throw new TimeoutException(label+" "+NetSession.WeaponPackagePreparationMessage);witness?.Pump();NetSession.Pump();Thread.Sleep(10);}Check(true,label);}
            Check(!Path.GetFullPath(defaultClientStore).Equals(serverStorePath,StringComparison.OrdinalIgnoreCase),"selected local host package store differs from unmodified client default");
            Check(NetLaunch.Connect("127.0.0.1",port,"WrongLocalPackageOwner",Hunter.Samus,ownerToken:ownerToken,weaponPackage:new(serverStorePath,new string('b',64))),"ordinary client can inspect lobby while explicit selected archive differs");
            Until(()=>NetSession.WeaponPackagePreparationMessage.Contains("different package",StringComparison.OrdinalIgnoreCase),"server offer mismatching explicit selection fails before package-ready");
            Check(!NetSession.RequiredWeaponPackagesReady&&!NetSession.CustomWeaponReadinessConfirmed&&Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE")==priorStore,"wrong selected archive cannot prepare and leaves global environment unchanged");
            witness=new LobbyWitness(port);Until(()=>witness.Joined,"independent UDP lobby witness joins before wrong owner disconnects");
            NetSession.Stop();Check(NetSession.WeaponPackageStoreDirectory==defaultClientStore,"Stop clears wrong connection selection and restores ordinary store resolution");
            Check(NetLaunch.Connect("127.0.0.1",port,"PrivateHostOwner",Hunter.Samus,ownerToken:ownerToken,weaponPackage:new(serverStorePath,package.PackageHash)),"ordinary client claims local package lobby using exact explicit nondefault store and owner token");
            Until(()=>NetSession.RequiredWeaponPackagesReady,"real ordinary client completes exact installed preparation and authority confirmation");
            witness.Dispose();witness=null;Until(()=>NetSession.LocalIsLobbyOwner,"departing UDP witness transfers lobby ownership to correct prepared client");
            Check(NetSession.WeaponPackageStoreDirectory==serverStorePath&&Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE")==priorStore,"connection-scoped selection resolves host store without changing environment defaults");
            Check(NetSession.ServerSession is {Phase:SessionPhase.Lobby,Match.PackagedWeaponAvailable:true},"actual child advertises packaged persistent lobby without granting gameplay");
            witness=new LobbyWitness(port);Until(()=>witness.Joined,"fresh independent UDP witness preserves process-owned lobby across prepared owner disconnect");
            Check(serverStore.Uninstall(package.PackageHash)&&serverStore.Prune()==0,"actual child independently retains package after parent launch lease has returned");NetSession.Stop();Check(serverStore.Prune()==0,"child binding remains alive after ordinary launcher client disconnect");Check(NetSession.WeaponPackageStoreDirectory==defaultClientStore,"Stop clears successful connection selection without altering fallback store");
            int nativePort=LocalServer.Start("Private native host proof",new[]{("MP3 PROVING GROUND",GameMode.Battle)},2,0,10000,"127.0.0.1",1,false,lobby:false,weaponPackage:null);nativeChild=LocalServer.Running;
            Check(nativePort>0&&nativeChild is {HasExited:false}&&LocalServer.OwnerToken==Guid.Empty&&!packageChild.HasExited,"native null selection preserves continuous tokenless hosting and cannot stop detached package child");
            Check(!nativeChild!.StartInfo.ArgumentList.Contains("-weaponstore")&&!nativeChild.StartInfo.ArgumentList.Contains("-weaponarchive")&&!nativeChild.StartInfo.ArgumentList.Contains("-lobby"),"native child command line has no package or forced lobby arguments");
            Check(!NetLaunch.Connect("127.0.0.1",nativePort,"ExplicitPackageNativeRefusal",Hunter.Samus,weaponPackage:new(serverStorePath,package.PackageHash))&&!NetSession.Active&&NetLaunch.LastJoinError.Contains("does not offer",StringComparison.OrdinalIgnoreCase),"explicit package selection cannot silently join native-only server");
            Check(NetSession.WeaponPackageStoreDirectory==defaultClientStore&&Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE")==priorStore,"native refusal clears connection override and preserves environment defaults");
            receipts.Add(new{packagePid=packageChild.Id,nativePid=nativeChild.Id,port,nativePort,executable=binary!.Value.Executable,assemblySha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(LocalServer).Assembly.Location))),ownerTokenPresent=ownerToken!=Guid.Empty});
            Stop(packageChild);packageChild=null;Check(serverStore.Prune()==1,"stopping exact owned child releases its independent archive lease");LocalServer.Stop();nativeChild=null;
            File.WriteAllText(Path.Combine(output,"local-package-host-results.json"),JsonSerializer.Serialize(new{checks,seconds=watch.Elapsed.TotalSeconds,receipts,scope="Actual LocalServer.Start and private installed Windows dedicated child, ordinary owner connection/package preparation, independent OS leases and native child regression. No scene grants or Android claim."},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"PASS {checks} local weapon package host checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("LOCAL PACKAGE HOST FAIL "+error);return 1;}
        finally{NetSession.Stop();witness?.Dispose();Stop(packageChild);if(nativeChild!=null&&ReferenceEquals(LocalServer.Running,nativeChild))LocalServer.Stop();else Stop(nativeChild);Directory.SetCurrentDirectory(cwd);}
    }
    // This peer only preserves ordinary process-owned lobby lifetime. It never
    // acknowledges package preparation, readiness, loading, or gameplay.
    private sealed class LobbyWitness : IDisposable
    {
        private readonly NetTransport _transport=new(0);
        private readonly IPEndPoint _server;
        private readonly uint _id=(uint)Random.Shared.Next(1,int.MaxValue);
        private readonly Stopwatch _retry=Stopwatch.StartNew();
        private int _slot=-1;
        private bool _state;
        internal bool Joined=>_slot>=0&&_state;
        internal LobbyWitness(int port){_server=new(IPAddress.Loopback,port);_transport.AnswerPingsImmediately();Hello();}
        private void Hello(){byte[] bytes=new byte[22];bytes[0]=NetConfig.ProtocolVersion;bytes[1]=_slot<0?(byte)255:(byte)_slot;BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(2),_id);_transport.Send(_server,PacketType.Hello,bytes);_retry.Restart();}
        internal void Pump()
        {
            foreach(var packet in _transport.Drain())
            {
                if(packet.Type==PacketType.Refused)throw new InvalidOperationException("UDP lobby witness refused");
                if(packet.Type==PacketType.Welcome&&packet.Payload.Length>0)
                {
                    _slot=packet.Payload[0];byte[] identity=new byte[2+RosterPacket.MaxNameBytes];
                    identity[0]=(byte)Hunter.Samus;PlayerNameCodec.TryEncode("LobbyWitness",identity.AsSpan(2),out int length);
                    _transport.Send(_server,PacketType.Identify,identity.AsSpan(0,2+length));
                }
                if(packet.Type==PacketType.SessionState&&SessionStatePacket.TryRead(packet.Payload,out var state))_state=state.Phase==SessionPhase.Lobby;
            }
            if(!Joined&&_retry.ElapsedMilliseconds>=500)Hello();
        }
        public void Dispose(){try{_transport.Send(_server,PacketType.Bye,Array.Empty<byte>());}finally{_transport.Dispose();}}
    }
    private static void Stop(Process? child){if(child==null)return;try{if(!child.HasExited){child.Kill(true);child.WaitForExit(5000);}}finally{child.Dispose();}}
}

