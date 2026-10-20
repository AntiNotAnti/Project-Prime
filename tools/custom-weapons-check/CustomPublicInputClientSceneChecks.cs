using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Public client boundary only: real assets, actor, NetSession and UDP transport.
// Native snapshots/private results/public baseline are explicit synthetic facts.
internal static class CustomPublicInputClientSceneChecks
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool ok,string label)
    {if(!ok)throw new InvalidOperationException(label);_checks++;Console.WriteLine("PUBLIC CLIENT PASS "+label);}
    private sealed record Observed(PacketType Type,ulong Connection,byte[] Payload);
    // A transparent loopback NAT/proxy preserves real transport admission while
    // recording exact outbound91/93 packets; it invents no application payload.
    private sealed class Proxy : IDisposable
    {
        private readonly UdpClient _socket=new(new IPEndPoint(IPAddress.Loopback,0));
        private readonly IPEndPoint _server;private IPEndPoint? _client;
        private readonly Thread _thread;private volatile bool _stop;
        internal readonly ConcurrentQueue<Observed> Sent=new();
        internal Exception? Error;internal bool Overflow;
        internal int Port=>((IPEndPoint)_socket.Client.LocalEndPoint!).Port;
        internal Proxy(int server)
        {
            _server=new(IPAddress.Loopback,server);_socket.Client.ReceiveTimeout=100;
            _thread=new(()=>
            {
                try{while(!_stop)
                {
                    IPEndPoint sender=new(IPAddress.Any,0);byte[] bytes;
                    try{bytes=_socket.Receive(ref sender);}catch(SocketException e)when(e.SocketErrorCode==SocketError.TimedOut){continue;}
                    if(sender.Equals(_server)){if(_client!=null)_socket.Send(bytes,bytes.Length,_client);continue;}
                    _client=sender;
                    if(NetHeader.TryRead(bytes,out var header)&&header.Type is PacketType.CustomWeaponControl or PacketType.CustomWeaponInput)
                    {
                        int offset=NetHeader.Size+((header.Flags&NetHeaderFlags.Reliable)!=0?4:0);
                        if(Sent.Count<256)Sent.Enqueue(new(header.Type,header.ConnectionId,bytes[offset..]));else Overflow=true;
                    }
                    _socket.Send(bytes,bytes.Length,_server);
                }}catch(Exception e)when(_stop&&(e is ObjectDisposedException or SocketException)){}catch(Exception e){Error=e;}
            }){IsBackground=true};_thread.Start();
        }
        public void Dispose(){_stop=true;_socket.Dispose();_thread.Join(1000);}
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly Scene Scene;
        internal readonly object Rig;
        internal readonly DedicatedServer Server;
        internal readonly Proxy Wire;
        internal readonly NetTransport Authority;
        internal readonly CustomReadinessWire Manifest;
        internal readonly WeaponDefinition Definition=RiftlineM7.Create();
        internal readonly PlayerEntity Player;
        internal readonly WorldBootstrapIdentity Bootstrap;
        private readonly MethodInfo _wait;
        private uint _sample=100;
        internal Fixture()
        {
            Scene=new(new Vector2i(256,192),SyntheticInput.CreateKeyboard(),SyntheticInput.CreateMouse(),_=>{},()=>{});
            Scene.AddPlayer(Hunter.Samus);Scene.AddPlayer(Hunter.Samus);Scene.Players.PlayerCount=2;Scene.Players.MainPlayerIndex=0;
            foreach(var actor in Scene.Players.Items)actor.IsBot=false;
            Scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:2,nodeLayerMask:0);Scene.OnLoad();Scene.GameState.MatchTime=-1;
            for(int i=0;i<240;i++)Scene.OnSimulationFrame();
            var rigType=typeof(NetLobbyTest).GetNestedType("Rig",BindingFlags.NonPublic)!;
            var ctor=rigType.GetConstructors(Hidden|BindingFlags.Public).Single();
            object?[] args={ServerSessionPolicy.Lobby,Guid.Empty,false,"MP3 PROVING GROUND",null,8,15d,10d,true,true,false,false};
            Rig=ctor.Invoke(args);Server=(DedicatedServer)rigType.GetField("Server")!.GetValue(Rig)!;
            _wait=rigType.GetMethod("Wait")!;Wire=new(Server.BoundPort);
            Check(NetLaunch.Connect("127.0.0.1",Wire.Port,"PublicInputFixture",Hunter.Samus),"real NetSession client joins through transparent actual UDP proxy");
            Until(()=>NetSession.LocalIsLobbyOwner&&NetSession.LobbyRoster().SessionRevision==NetSession.SessionRevision,"native client lobby converges");
            Check(NetSession.SendLobbyCommand(LobbyCommandType.SetReady,ready:true),"native ready command queues");
            Until(()=>!NetSession.LobbyCommandPending&&NetSession.SlotLobbyReady[NetSession.LocalSlot]
                &&NetSession.LobbyRoster().SessionRevision==NetSession.SessionRevision,"native ready command settles");
            Check(NetSession.SendLobbyCommand(LobbyCommandType.StartMatch),"control-plane fixture starts synthetic native bootstrap");
            Until(()=>NetSession.IsStarting&&!NetSession.LobbyCommandPending,"native load barrier arrives");NetSession.MarkMatchLoaded();
            Until(()=>NetSession.IsPlaying&&NetSession.WorldAdmissionConfirmed&&NetSession.CustomWeaponReadinessConfirmed,"real client completes native/custom capability handshake and native countdown");
            Bootstrap=NetSession.ConfirmedWorldBootstrap!.Value;Manifest=new(Bootstrap,(byte)NetSession.LocalSlot);
            Authority=(NetTransport)typeof(DedicatedServer).GetField("_transport",Hidden)!.GetValue(Server)!;
            Player=Scene.Players.Items[NetSession.LocalSlot];Scene.Players.MainPlayerIndex=NetSession.LocalSlot;
            Native(1,Bootstrap.AuthorityFrame+10);NetHooks.AfterInput(Scene);ReadyBody();
            State(1,1,0,false,0);
            var slots=new CustomReplicaSlot[8];var flights=new CustomReplicaFlight[64];
            for(byte i=0;i<8;i++)slots[i]=new(i,false,0,0,false,false,default,0,0,default);
            slots[Player.SlotIndex]=new((byte)Player.SlotIndex,true,Bootstrap.SlotGeneration,1,false,true,default,0,100,Snapshot(1,false,0));
            var page=new CustomReplicaPage(1,0,Bootstrap.AuthorityFrame+10,0,0);var bytes=new byte[page.Length];
            Check(CustomReplicaCodec.TryWritePage(Manifest,page,slots,flights,bytes,out _),"explicit synthetic public baseline encodes");
            Send(PacketType.CustomWeaponReplicaPage,bytes);Until(()=>NetSession.CustomWeaponReplicaReady,"real client durably commits synthetic baseline");
        }
        internal WeaponOwnerContext Owner(ushort life)=>new(Bootstrap.Start.MatchId,Bootstrap.Start.AuthorityEpoch,(byte)Player.SlotIndex,Bootstrap.SlotGeneration,life);
        internal WeaponStateSnapshot Snapshot(ushort life,bool selected,byte optic)
            =>new(Owner(life),Definition.Identity,100,selected?CustomWeaponPhase.Ready:CustomWeaponPhase.Unequipped,4,16,optic,0,0,0,0);
        internal void Until(Func<bool> condition,string label)
            =>_wait.Invoke(Rig,new object[]{(Func<bool>)(()=>{NetSession.Pump();if(Wire.Error!=null)throw Wire.Error;return condition();}),label,8000});
        internal void Send(PacketType type,byte[] bytes)
            =>Check(Authority.TrySendSemantic(new IPEndPoint(IPAddress.Loopback,Wire.Port),type,bytes),"synthetic authority fact enters authenticated actual UDP: "+type);
        internal void State(ushort life,uint revision,uint settled,bool selected,byte optic)
        {
            var value=new CustomStateMessage(Bootstrap,Manifest.Manifest.Digest,new(10),Bootstrap.AuthorityFrame+10,revision,settled,
                selected?new WeaponRuntimeId(10):default,100,Snapshot(life,selected,optic));
            var bytes=new byte[CustomStateMessage.Size];CustomCombatResults.Write(value,bytes,Manifest.Manifest,Definition);Send(PacketType.CustomWeaponState,bytes);
            Until(()=>NetSession.CustomWeaponPresentationState==value,"canonical synthetic private state reaches real client cache");
        }
        internal void Native(ushort life,uint world)
        {
            var state=new PlayerState {SlotIndex=(byte)NetSession.LocalSlot,SlotGeneration=Bootstrap.SlotGeneration,LifeId=life,Health=200,
                Position=Player.Position,Facing=Vector3.UnitZ,CurrentWeapon=(byte)BeamType.PowerBeam,Flags=PlayerState.FlagActive|PlayerState.FlagSpawned};
            var header=new SnapshotHeader {MatchId=Bootstrap.Start.MatchId,AuthorityEpoch=Bootstrap.Start.AuthorityEpoch,Frame=world,PlayerCount=1};
            var bytes=new byte[SnapshotHeader.Size+PlayerState.Size+NetMatchTimeSync.Size+NetHealthSync.HeaderSize];
            header.Write(bytes);state.Write(bytes.AsSpan(SnapshotHeader.Size));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length-NetHealthSync.HeaderSize),Bootstrap.Start.MatchId);
            Authority.Send(new IPEndPoint(IPAddress.Loopback,Wire.Port),PacketType.Snapshot,bytes);
            Until(()=>NetPlayerLifecycle.Get(NetSession.LocalSlot)==life&&NetSession.RemoteStateValid[NetSession.LocalSlot],"synthetic native snapshot is decoded before client actor adoption");
        }
        internal ushort AppliedLife=>((ushort[])typeof(PlayerReplicationBridge).GetField("_appliedLifeId",Hidden)!.GetValue(Scene.PlayerReplication)!)[Player.SlotIndex];
        internal void ReadyBody()
        {
            Player.ModForceForm(false);Player.Controls.ClearAll();Player.ModReleaseSpawnProtectionFromNetwork();
            foreach(string name in new[]{"_spawnInvulnTimer","_damageInvulnTimer","_frozenTimer","_field6D0"})
            {var field=typeof(PlayerEntity).GetField(name,Hidden)!;field.SetValue(Player,Convert.ChangeType(0,field.FieldType));}
            typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.GunAnimation))!.SetValue(Player,GunAnimation.Idle);
            Check(Scene.HardwareInputReady&&Player.ModTryCaptureCustomPrecombatFacts(Player.ModGunVector,out var facts)&&facts.CanUseWeapon,
                "actual asset client body is explicitly prepared for input-boundary fixture");
        }
        internal void Sample(Action<PlayerControls> controls)
        {
            Player.Controls.ClearAll();controls(Player.Controls);_sample+=2;
            typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null,_sample);
            NetHooks.AfterInput(Scene);NetHooks.AfterSimulation();NetSession.Pump();
        }
        internal CustomControlRequest Request(uint sequence,ushort life=1)
        {
            CustomControlRequest result=default;
            Until(()=>Wire.Sent.Any(p=>p.Type==PacketType.CustomWeaponControl&&CustomControlRequest.TryRead(p.Payload,out var r)
                &&r.Sequence==sequence&&r.Life==life),"public router emits actual reliable91 request "+sequence);
            foreach(var packet in Wire.Sent)if(packet.Type==PacketType.CustomWeaponControl&&CustomControlRequest.TryRead(packet.Payload,out var r)
                &&r.Sequence==sequence&&r.Life==life){result=r;break;}
            return result;
        }
        internal void Deny(CustomControlRequest request,ulong token)
        {var bytes=new byte[CustomControlDenied.Size];new CustomControlDenied(request,token,Bootstrap.AuthorityFrame+11,CustomControlDenialReason.SourceUnavailable).Write(bytes);Send(PacketType.CustomWeaponControlDenied,bytes);Until(()=>NetSession.LastCustomControlDenied?.Token==token,"synthetic no-source result consumed");}
        public void Dispose(){NetSession.Stop();Wire.Dispose();((IDisposable)Rig).Dispose();Scene.DoCleanup();}
    }
    internal static int Run(string data)
    {
        _checks=0;string cwd=Directory.GetCurrentDirectory();bool force=MapAudit.ForceEveryone,ready=NetSession.DeveloperCustomWeaponReadiness;
        bool focus=GamepadContexts.Focused;var context=GamepadContexts.Current;
        try
        {
            NetSession.Stop();Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();Headless.Enter();
            MapAudit.ForceEveryone=true;GamepadContexts.Focused=true;GamepadContexts.Current=GamepadContext.Gameplay;NetSession.DeveloperCustomWeaponReadiness=true;
            using var f=new Fixture();int health=f.Player.Health;
            f.Sample(c=>c.PrevWeapon.IsPressed=true);var equip=f.Request(1);
            Check(equip.Kind==CustomWeaponActionKind.Equip&&equip.Owner==f.Owner(1)&&equip.Manifest==f.Manifest.Manifest.Digest
                &&equip.Bootstrap==f.Bootstrap&&equip.Weapon.Value==10,"public acquired-weapon cycle derives complete canonical Equip identity/nonce1");
            f.Until(()=>f.Wire.Sent.Any(p=>p.Type==PacketType.CustomWeaponInput&&CustomInputCarrier.TryRead(p.Payload,out var r,out var i)
                &&r==equip&&i.Frame==102&&i.LifeId==1),"same public sample emits actual93 containing native source102 and exact pending Equip");
            // Canonical state stands in for accepted Equip with its application
            // receipt withheld. The pending retry remains until new-life reset.
            f.State(1,2,1,true,0);
            f.Sample(c=>{c.Zoom.IsPressed=true;c.Zoom.IsDown=true;c.Shoot.IsPressed=true;c.Shoot.IsDown=true;});
            var optic=f.Request(2);var fire=f.Request(3);
            Check(optic.Kind==CustomWeaponActionKind.Optic&&optic.Optic==1&&fire.Kind==CustomWeaponActionKind.Fire&&fire.Optic==1,
                "physical Zoom/Shoot produce ordered optic2/fire3 without caller-supplied identity or ray");
            f.Until(()=>f.Wire.Sent.Any(p=>p.Type==PacketType.CustomWeaponInput&&CustomInputCarrier.TryRead(p.Payload,out _,out var i)
                &&i.Frame==104&&(i.Buttons&IntentButtons.Shoot)!=0&&(i.Buttons&IntentButtons.Zoom)!=0),
                "actual93 preserves native physical Shoot/Zoom while custom owns public controls");
            f.Deny(optic,2);f.Deny(fire,3);f.Sample(c=>c.CustomReload.IsPressed=true);var reload=f.Request(4);
            Check(reload.Kind==CustomWeaponActionKind.Reload,"public custom reload produces next canonical control4");f.Deny(reload,4);
            f.Sample(c=>c.PowerBeam.IsPressed=true);var native=f.Request(5);
            Check(native.Kind==CustomWeaponActionKind.Unequip&&f.Scene.CustomInputSuppressesNative(f.Player)
                &&f.Player.CurrentWeapon==BeamType.PowerBeam,"selecting same native fallback submits Unequip5 and retains pending native fence");
            Check(f.Player.Health==health&&NetSession.CustomWeaponPresentationState?.State.Magazine==4
                &&NetSession.CustomWeaponPresentationState?.State.Reserve==16,"public input/UDP without authority combat results cannot change HP or canonical4/16 resources");
            // Native state similarly confirms Unequip while its receipt is
            // withheld; this never contradicts the prior accepted selection.
            f.State(1,3,5,false,0);f.Sample(_=>{});
            Check(!f.Scene.CustomInputSuppressesNative(f.Player),"explicit canonical native state clears old pending selection fence");
            f.Native(2,f.Bootstrap.AuthorityFrame+20);Check(f.AppliedLife==1,"nativeLife2 is queued while real actor still reflects adoptedLife1");
            f.Sample(c=>c.CustomWeapon.IsPressed=true);
            Check(f.AppliedLife==2&&NetSession.CustomWeaponInputProjection==null&&!f.Scene.CustomInputSuppressesNative(f.Player)
                &&!f.Wire.Sent.Any(p=>p.Type==PacketType.CustomWeaponControl&&CustomControlRequest.TryRead(p.Payload,out var r)&&r.Life==2),
                "AfterInput adopts actual new actor life before custom sampling and cannot publish from stale old-life canonical state");
            f.State(2,4,0,false,0);f.ReadyBody();f.Sample(c=>c.CustomWeapon.IsPressed=true);var reborn=f.Request(1,2);
            Check(reborn.Owner==f.Owner(2)&&reborn.Sequence==1,"fresh post-adoption custom edge derives new full-life nonce1");
            Check(!f.Wire.Overflow&&f.Wire.Error==null&&!f.Server.Simulating,"bounded transparent UDP recorder completes without pretending a remote engine exists");
            Console.WriteLine($"PASS {_checks} actual online public-input client boundary checks.");
            Console.WriteLine("SCOPE: actual asset-backed ordinary client actor, NetHooks.AfterInput/AfterSimulation public router, canonical control producer and real91/93UDP through a transparent loopback proxy. Remote native snapshots/private states/no-source results/public baseline and physical control edges are explicit synthetic fixtures. No remote engine/source admission, authoritative damage, UI devices or DedicatedServer combat E2E is claimed.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine("PUBLIC CLIENT INPUT FAIL "+e);return 1;}
        finally{NetSession.Stop();NetSession.DeveloperCustomWeaponReadiness=ready;MapAudit.ForceEveryone=force;GamepadContexts.Focused=focus;GamepadContexts.Current=context;Directory.SetCurrentDirectory(cwd);}
    }
}
