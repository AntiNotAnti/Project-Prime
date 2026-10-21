using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using MphRead.Mods.CustomWeapons.Packages;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    // Real UDP production server dispatch and real local installed package leases.
    // Preparation deliberately remains in Lobby: CW12c owns online execution.
    private sealed class PackagePreparationProbe : IDisposable
    {
        readonly Client _client;readonly WeaponPackageStore _store;
        internal WeaponPackagePreparationKey? Key;
        internal WeaponMatchPackageBinding? Binding;
        internal int Offers,Confirmations,Rejected;
        internal bool WithholdConfirmation;
        internal PackagePreparationProbe(Client client,WeaponPackageStore store){_client=client;_store=store;client.PacketReceived=Receive;}
        void Receive(ReceivedPacket packet)
        {
            if(packet.Type==PacketType.WeaponPackageOffer)
            {
                Check(packet.Sender.Equals(_client.Server)&&packet.ConnectionId==_client.Transport.ConnectionStats(_client.Server)!.Value.ConnectionId,"package offer comes from exact admitted authority connection");
                Check(packet.Payload.Length==247&&WeaponPackagePreparationKey.TryRead(packet.Payload,out var key)
                    &&CustomWeaponPackageManifest.TryRead(packet.Payload[61..],out var manifest)&&manifest!.Rows.Count==1&&manifest.Digest==key.Manifest,"UDP offer carries exact61byte recipient key and canonical186byte singlepackage manifest");
                WeaponPackagePreparationKey.TryRead(packet.Payload,out key);CustomWeaponPackageManifest.TryRead(packet.Payload[61..],out var parsed);
                Check(key.Slot==_client.Slot&&key.Match==_client.State!.Value.MatchId&&key.Epoch==_client.State.Value.AuthorityEpoch
                    &&key.Connection==packet.ConnectionId,"offer binds match epoch slot and real transport incarnation");
                Offers++;if(Key==key)return;Binding?.Dispose();Binding=null;Key=key;
                try{Binding=WeaponMatchPackageBinding.BindInstalled(_store,parsed!);}catch(Exception error)when(error is IOException or InvalidDataException){Rejected++;}
            }
            if(packet.Type==PacketType.WeaponPackageConfirmed)
            {
                Check(packet.Payload.Length==61&&WeaponPackagePreparationKey.TryRead(packet.Payload,out var confirmed)&&confirmed==Key,"authority confirms exact locally bound package recipient key");
                if(!WithholdConfirmation)Confirmations++;
            }
        }
        internal byte[] Bytes(WeaponPackagePreparationKey? replacement=null){var bytes=new byte[61];(replacement??Key??throw new InvalidOperationException("No package offer.")).Write(bytes);return bytes;}
        internal void Send(){Check(Binding!=null,"probe ACK follows successful real installedpackage binding");_client.Send(PacketType.WeaponPackageReady,Bytes());}
        public void Dispose(){_client.PacketReceived=null;Binding?.Dispose();Binding=null;}
    }
    private static void PackagePreparationRuntimeClient(string root,byte[] archive)
    {
        NetSession.Stop();string? previous=Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE");
        string serverPath=Path.Combine(root,"runtime-server"),clientPath=Path.Combine(root,"runtime-client"),missingPath=Path.Combine(root,"runtime-missing");
        var serverStore=new WeaponPackageStore(serverPath);var package=serverStore.Install(archive);var clientStore=new WeaponPackageStore(clientPath);clientStore.Install(archive);
        try
        {
            using var rig=new Rig(weaponStore:serverPath,weaponArchive:package.PackageHash);
            void PumpUntil(Func<bool> condition,string label)=>rig.Wait(()=>{NetSession.Pump();return condition();},label,8000);
            const BindingFlags statics=BindingFlags.Static|BindingFlags.NonPublic;
            const BindingFlags instances=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            var stateField=typeof(NetSession).GetField("_weaponPreparation",statics)!;var workerField=typeof(NetSession).GetField("_weaponPackageWorker",statics)!;
            var authorityTransport=(NetTransport)typeof(DedicatedServer).GetField("_transport",instances)!.GetValue(rig.Server)!;
            void SendOffer(WeaponPackagePreparationKey key)
            {
                var manifest=new CustomWeaponPackageManifest(new[]{package});var payload=new byte[61+manifest.Size];key.Write(payload);manifest.Write(payload.AsSpan(61));
                Check(authorityTransport.TrySendSemantic(new IPEndPoint(IPAddress.Loopback,NetSession.ConnectionPort),PacketType.WeaponPackageOffer,payload),"actual authority transport enqueues explicit package offer guard probe");
            }
            Environment.SetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE",clientPath);
            using(var blocked=new FileStream(Path.Combine(clientPath,".store.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                Check(NetLaunch.Connect("127.0.0.1",rig.Server.BoundPort,"PackageWorker",Hunter.Samus),"actual NetSession client connects to selected package lobby");
                PumpUntil(()=>NetSession.ServerSession is {Match.PackagedWeaponAvailable:true},"production client receives package requirement before native loading");
                Check(!NetSession.RequiredWeaponPackagesReady,"production client readiness stays closed while exact local store gate blocks verification");
                PumpUntil(()=>stateField.GetValue(null) is {} candidate&&candidate.GetType().GetField("Work",instances)!.GetValue(candidate) is Task {IsCompleted:false},"production package worker is actually blocked on held store gate");
                var pinnedState=stateField.GetValue(null)!;var pinnedWork=workerField.GetValue(null);var pinnedKey=(WeaponPackagePreparationKey)pinnedState.GetType().GetField("Key",instances)!.GetValue(pinnedState)!;
                for(ulong change=1;change<=4;change++)SendOffer(pinnedKey with{Configuration=pinnedKey.Configuration+change});
                var observation=Stopwatch.StartNew();PumpUntil(()=>observation.ElapsedMilliseconds>=150,"contradictory offers reach real client while package parser waits on store");
                Check(ReferenceEquals(stateField.GetValue(null),pinnedState)&&ReferenceEquals(workerField.GetValue(null),pinnedWork)&&!NetSession.RequiredWeaponPackagesReady,"differing current-epoch configuration offers cannot replace pinned state or spawn overlapping package workers");
            }
            int slot=NetSession.LocalSlot;
            PumpUntil(()=>NetSession.RequiredWeaponPackagesReady&&rig.Server.IsWeaponPackagePrepared(slot),"actual asynchronous installed binding worker ACK and authority confirmation complete over UDP");
            Check(NetSession.WeaponPackagePreparationMessage.Length==0&&NetSession.ServerSession?.Phase==SessionPhase.Lobby,"production package confirmation clears waiting message and stays in lobby");
            Check(clientStore.Uninstall(package.PackageHash)&&clientStore.Prune()==0,"actual NetSession preparation binding retains installed archive lease after uninstall");
            NetSession.Stop();PumpUntil(()=>rig.Server.PeerCount==0,"actual production client disconnect retires package occupant");
            Check(clientStore.Prune()==1,"actual NetSession Stop releases retained package worker binding lease");
            Environment.SetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE",missingPath);
            var retiredWorker=new TaskCompletionSource<WeaponMatchPackageBinding>(TaskCreationOptions.RunContinuationsAsynchronously);workerField.SetValue(null,retiredWorker.Task);
            Check(NetLaunch.Connect("127.0.0.1",rig.Server.BoundPort,"MissingPackageWorker",Hunter.Samus),"actual production client reconnects with isolated missing package store");slot=NetSession.LocalSlot;
            PumpUntil(()=>NetSession.ServerSession is {Match.PackagedWeaponAvailable:true},"new production incarnation receives package session while prior parser fence is pending");
            var holdObservation=Stopwatch.StartNew();PumpUntil(()=>holdObservation.ElapsedMilliseconds>=650,"repeated real server offers traverse pending retired-worker fence");
            Check(stateField.GetValue(null)==null&&ReferenceEquals(workerField.GetValue(null),retiredWorker.Task)&&!NetSession.RequiredWeaponPackagesReady,"pending retired parser prevents every new preparation state and worker across reconnect");
            retiredWorker.SetCanceled();
            PumpUntil(()=>NetSession.WeaponPackagePreparationMessage.Contains("unavailable",StringComparison.OrdinalIgnoreCase),"actual missing-store worker produces explicit local preparation error");
            Check(!NetSession.RequiredWeaponPackagesReady&&!rig.Server.IsWeaponPackagePrepared(slot),"failed actual client worker cannot send package-ready or receive confirmed capability");
            NetSession.Stop();PumpUntil(()=>rig.Server.PeerCount==0,"failed production client disconnect clears package requirement");
            clientStore.Install(archive);Environment.SetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE",clientPath);
            Check(NetLaunch.Connect("127.0.0.1",rig.Server.BoundPort,"InstalledPackageWorker",Hunter.Samus),"production reconnect uses explicitly installed exact archive");slot=NetSession.LocalSlot;
            PumpUntil(()=>NetSession.RequiredWeaponPackagesReady&&rig.Server.IsWeaponPackagePrepared(slot),"production reconnect obtains fresh confirmation for new transport incarnation");
            NetSession.Stop();PumpUntil(()=>rig.Server.PeerCount==0,"last production client cleanly retires readiness");clientStore.Uninstall(package.PackageHash);Check(clientStore.Prune()==1,"reconnected production worker releases all retained archive handles");
        }
        finally{NetSession.Stop();Environment.SetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE",previous);}
    }
    public static int RunWeaponPackagePreparation()
    {
        int before=_checks;string root=Path.Combine(Path.GetTempPath(),"prime-package-udp-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            Check(NetConfig.ProtocolVersion==80&&(byte)PacketType.WeaponPackageOffer==99&&(byte)PacketType.WeaponPackageReady==100&&(byte)PacketType.WeaponPackageConfirmed==101,"protocol80 package semantic packet IDs remain exact");
            var archive=WeaponPackageReference.BuildRiftlineM7();var package=WeaponPackage.Parse(archive);var serverStore=new WeaponPackageStore(Path.Combine(root,"server"));serverStore.Install(archive);
            WeaponPackageStore ClientStore(string name){var store=new WeaponPackageStore(Path.Combine(root,name));store.Install(archive);return store;}
            using(var rig=new Rig(weaponStore:Path.Combine(root,"server"),weaponArchive:package.PackageHash))
            {
                var first=rig.Add(12800);var second=rig.Add(12801);
                using var a=new PackagePreparationProbe(first,ClientStore("first"));using var b=new PackagePreparationProbe(second,new WeaponPackageStore(Path.Combine(root,"missing")));
                rig.Wait(()=>a.Key.HasValue&&b.Key.HasValue,"two actual UDP peers receive package requirements before native load");
                Check(a.Binding!=null&&b.Binding==null&&b.Rejected==1,"only exact explicitly installed local archive resolves preparation");
                Check(first.State!.Value.Match.PackagedWeaponAvailable&&first.State.Value.Match.RiftlineM7Available&&first.State.Value.Phase==SessionPhase.Lobby,"operator selection advertises package flag in lobby without activating world");
                Check(serverStore.Uninstall(package.PackageHash)&&serverStore.Prune()==0,"running configured server retains package archive lease despite uninstall");
                rig.ReadyAll();var waiting=rig.Expect(first,first.Command(LobbyCommandType.StartMatch),LobbyResultCode.MapUnavailable);
                Check(waiting.Reason.Contains("verify",StringComparison.OrdinalIgnoreCase)&&first.State.Value.Phase==SessionPhase.Lobby,"ordinary lobbyReady cannot bypass exactpackage preparation before world loading");
                first.Send(PacketType.WeaponPackageReady,b.Bytes());
                foreach(var altered in new[]{a.Key!.Value with{Epoch=a.Key.Value.Epoch+1},a.Key.Value with{Configuration=a.Key.Value.Configuration+1},a.Key.Value with{Generation=(ushort)(a.Key.Value.Generation+1)},a.Key.Value with{Connection=a.Key.Value.Connection+1},a.Key.Value with{Manifest=MphRead.Mods.CustomWeapons.WeaponDigest.Read(new byte[]{1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0})}})
                    first.Send(PacketType.WeaponPackageReady,a.Bytes(altered));
                ObserveCustomReadiness(rig,150,"invalid crossslot/staleepoch/config/generation/connection/digest ACKs traverse actual UDP");
                Check(!rig.Server.IsWeaponPackagePrepared(first.Slot)&&!rig.Server.IsWeaponPackagePrepared(second.Slot),"forged readiness mutates neither authenticated occupant");
                var serverTransport=(NetTransport)typeof(DedicatedServer).GetField("_transport",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(rig.Server)!;
                long invalidBefore=serverTransport.Telemetry.Capture().Invalid;
                using(var stranger=new UdpClient(0))
                {
                    var payload=a.Bytes();var wire=new byte[NetHeader.Size+4+payload.Length];new NetHeader(PacketType.WeaponPackageReady,NetHeaderFlags.Reliable,a.Key!.Value.Connection,0x71000000,0,0).Write(wire);BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(NetHeader.Size),1);payload.CopyTo(wire,NetHeader.Size+4);stranger.Send(wire,wire.Length,first.Server);
                }
                rig.Wait(()=>serverTransport.Telemetry.Capture().Invalid>invalidBefore,"unadmitted endpoint copying validpackage key/connection is rejected by actual transport");
                Check(!rig.Server.IsWeaponPackagePrepared(first.Slot),"copied endpoint cannot prepare authenticated package occupant");
                a.WithholdConfirmation=true;a.Send();rig.Wait(()=>rig.Server.IsWeaponPackagePrepared(first.Slot),"valid locallybound ACK prepares only exact sender");
                Check(!rig.Server.IsWeaponPackagePrepared(second.Slot)&&a.Confirmations==0,"missing installed peer stays gated while first confirmation withheld");
                a.WithholdConfirmation=false;a.Send();a.Send();rig.Wait(()=>a.Confirmations>0,"duplicate exact readiness recovers application confirmation without rebind");
                var oldKey=b.Key!.Value;b.Dispose();var installedSecond=ClientStore("second");using var resolved=new PackagePreparationProbe(second,installedSecond);
                rig.Wait(()=>resolved.Key.HasValue&&resolved.Binding!=null,"explicit install and new local probe resolves repeated server offer");resolved.Send();rig.Wait(()=>rig.Server.IsWeaponPackagePrepared(second.Slot)&&resolved.Confirmations>0,"second installed peer independently confirms exactpackage binding");
                var unsupported=rig.Expect(first,first.Command(LobbyCommandType.StartMatch),LobbyResultCode.MapUnavailable);
                Check(unsupported.Reason.Contains("execution",StringComparison.OrdinalIgnoreCase)&&unsupported.Reason.Contains("not enabled",StringComparison.OrdinalIgnoreCase)&&first.State.Value.Phase==SessionPhase.Lobby,"allprepared remains explicitly blocked until online package runtime integration");
                var spectator=rig.Add(12802,initialSpectator:true);using var s=new PackagePreparationProbe(spectator,ClientStore("spectator"));
                rig.Wait(()=>s.Key.HasValue,"late spectator receives exact package requirement");Check(!rig.Server.IsWeaponPackagePrepared(spectator.Slot),"late spectator cannot inherit another peer readiness");s.Send();rig.Wait(()=>s.Confirmations>0,"late spectator independently verifies package before any load");
                int oldSlot=second.Slot;second.Send(PacketType.Bye,Array.Empty<byte>());rig.Wait(()=>!rig.Server.IsWeaponPackagePrepared(oldSlot),"disconnect retires exactpackage preparation");rig.Clients.Remove(second);second.Dispose();
                var replacement=rig.Add(12803);using var r=new PackagePreparationProbe(replacement,ClientStore("replacement"));rig.Wait(()=>r.Key.HasValue,"replacement occupant receives fresh package requirement");
                Check(replacement.Slot==oldSlot&&r.Key!.Value.Generation!=oldKey.Generation,"reused physical slot receives fresh occupant generation");replacement.Send(PacketType.WeaponPackageReady,r.Bytes(oldKey));ObserveCustomReadiness(rig,100,"stale predecessor key crosses real reused-slot connection");Check(!rig.Server.IsWeaponPackagePrepared(oldSlot),"replacement cannot inherit stale generation or connection readiness");r.Send();rig.Wait(()=>r.Confirmations>0,"replacement independently prepares retained exact package");
            }
            Check(serverStore.Prune()==1,"server shutdown releases final configured archive lease");
            PackagePreparationRuntimeClient(root,archive);
            using(var native=new Rig())
            {
                var owner=native.Add(12810);int offers=0;owner.PacketReceived=p=>{if(p.Type==PacketType.WeaponPackageOffer)offers++;};owner.Send(PacketType.WeaponPackageReady,new byte[61]);native.ReadyAll();native.Expect(owner,owner.Command(LobbyCommandType.StartMatch),LobbyResultCode.Ok);owner.Loaded();native.Wait(()=>owner.State?.Phase==SessionPhase.InMatch,"native-only start remains ordinary load barrier");
                Check(!owner.State!.Value.Match.PackagedWeaponAvailable&&offers==0&&!native.Server.IsWeaponPackagePrepared(owner.Slot),"native-only server neither offers packages nor consumes package ACKs");
            }
            Console.WriteLine($"PASS {_checks-before} real UDP package preparation checks (no online package execution claim)");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("PACKAGE PREPARATION UDP FAIL "+error);return 1;}
        finally{NetSession.Stop();try{Directory.Delete(root,true);}catch(Exception error){Console.Error.WriteLine("Package UDP cleanup: "+error.Message);}}
    }
}
