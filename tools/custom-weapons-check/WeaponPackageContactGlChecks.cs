using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Sound;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Two ordinary clients live in distinct processes. Pose placement is declared
// deterministic fixture setup immediately BEFORE the owner's simulation frame.
// No server hook, snapshot mutation, synthetic intent, health or grant is used.
internal static class WeaponPackageContactGlChecks
{
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    private static readonly Action<KeyboardState,Keys,bool> Key=typeof(KeyboardState).GetMethod("SetKeyState",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
    private sealed record Config(string Data,string Output,string Store,string Hash,int Port);
    private sealed record Command(int Sequence,string Phase,float[]? Position=null,float[]? Facing=null);
    private sealed record Status(string Phase,int Slot,int Health,ushort Generation,ushort Life,ushort DamageEventId,uint Frame,float[] Position,int CommandSequence,string? Error=null,float MaxPickupHeight=0,float BipedTop=0);
    private static void Atomic<T>(string path,T value)
    {
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(value,Json);if(bytes.Length>16384)throw new InvalidDataException("Contact IPC exceeds bound.");
        string temporary=path+".tmp";File.WriteAllBytes(temporary,bytes);File.Move(temporary,path,overwrite:true);
    }
    private static T Read<T>(string path)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);if(stream.Length is <=0 or >16384)throw new InvalidDataException("Contact IPC exceeds bound.");
        return JsonSerializer.Deserialize<T>(stream)??throw new InvalidDataException("Invalid contact IPC.");
    }
    private static Vector3 Vec(float[] values)
    {if(values.Length!=3||values.Any(x=>!float.IsFinite(x)||MathF.Abs(x)>8192))throw new InvalidDataException("Invalid fixture vector.");return new(values[0],values[1],values[2]);}
    private static float[] Array(Vector3 value)=>new[]{value.X,value.Y,value.Z};
    private sealed class Client : IDisposable
    {
        internal Scene? Scene;internal PlayerEntity Player=>Scene!.Players.Main;internal Action? BeforeFrame;
        private NativeWindow? _window;private DesktopGraphicsSession? _graphics;private KeyboardState _keyboard=default!;
        private readonly Stopwatch _watch=Stopwatch.StartNew();private double _next;
        internal Client(string data,string store,string hash,int port,string name)
        {
            Directory.SetCurrentDirectory(data);Paths.UpdatePaths();Paths.ChooseMphPath();MapAudit.ForceEveryone=true;NetTestScript.Enabled=false;
            GamepadManager.ClearAll();GamepadContexts.Focused=true;GamepadContexts.MenuVisible=GamepadContexts.Capturing=GamepadContexts.TextEntryActive=false;PointerInput.StylusMode=false;InputSettings.CustomScopeHold=false;
            Environment.SetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE",store);
            if(!NetLaunch.Connect("127.0.0.1",port,name,Hunter.Samus,weaponPackage:new(store,hash)))throw new InvalidOperationException("Ordinary contact client connect refused.");
        }
        internal void Step(int count=1)
        {
            for(int i=0;i<count;i++)
            {
                if(_watch.Elapsed.TotalSeconds>240)throw new TimeoutException("Ordinary contact client exceeded240seconds.");
                if(Scene==null)NetSession.Pump();else{BeforeFrame?.Invoke();Scene.OnSimulationFrame();}
                _next=Math.Max(_next+1000d/60,_watch.Elapsed.TotalMilliseconds);int wait=(int)(_next-_watch.Elapsed.TotalMilliseconds);if(wait>0)Thread.Sleep(wait);
            }
        }
        internal void Until(Func<bool> condition,string label,int seconds=30)
        {double until=_watch.Elapsed.TotalSeconds+seconds;while(!condition()){if(_watch.Elapsed.TotalSeconds>until)throw new TimeoutException(label+"; "+NetSession.LastError+"; "+NetSession.WeaponPackagePreparationMessage);Step();}Console.WriteLine("CONTACT PASS "+label);}
        internal void Load()
        {
            var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new(960,720);settings.StartVisible=true;settings.Title="Ordinary package contact "+NetSession.LocalSlot;
            _window=new NativeWindow(settings);_graphics=new DesktopGraphicsSession(_window);GLFW.SwapInterval(0);_keyboard=SyntheticInput.CreateKeyboard();
            Scene=new(_window.FramebufferSize,_keyboard,SyntheticInput.CreateMouse(),_=>{},()=>{});NetLaunch.BuildPlayers(Scene,Hunter.Samus,0);Scene.AddRoom(NetSession.ServerMatch!.Value.RoomKey,GameMode.Battle,playerCount:NetLaunch.RoomPlayerCount);Scene.OnLoad();Scene.OnResize();NetSession.MarkMatchLoaded();
            var mode=typeof(Scene).GetField("_inputMode",BindingFlags.Instance|BindingFlags.NonPublic)!;mode.SetValue(Scene,Enum.Parse(mode.FieldType,"All"));
            foreach(var button in Player.Controls.All){button.Type=ButtonType.Key;button.Key=Keys.Unknown;}Player.Controls.Shoot.Key=Keys.Enter;Player.Controls.MoveUp.Key=Keys.W;Player.Controls.CustomWeapon.Key=Keys.C;Player.Controls.Zoom.Key=Keys.Z;Player.Controls.CustomReload.Key=Keys.R;Player.Controls.MouseAim=Player.Controls.KeyboardAim=false;Player.IsBot=false;
        }
        internal void Hold(Keys key,bool down)=>Key(_keyboard,key,down);
        internal void Press(Keys key){Hold(key,true);Step();Hold(key,false);Step(3);}
        internal void Spawn()
        {Hold(Keys.Enter,true);Until(()=>NetSession.IsPlaying&&NetSession.WorldAdmissionConfirmed&&NetSession.CustomWeaponReplicaReady&&Player.Health>0&&Player.ModIsInPlay,"ordinary keyboard spawn completes",40);Hold(Keys.Enter,false);Scene!.CameraSequences.Current?.End();Step(10);}
        internal void Picture(string path)
        {Scene!.OnDrawFrame();if(!Scene.OnRenderFrame()||GL.GetError()!=OpenTK.Graphics.OpenGL.ErrorCode.NoError||!ScreenCapture.SaveWindow(Scene,path))throw new InvalidOperationException("Contact scene render failed.");DesktopGraphicsSession.Present(_window!);Scene.AfterRenderFrame();}
        public void Dispose(){NetSession.Stop();Scene?.DoCleanup();AudioLifetime.Shutdown();_graphics?.Dispose();_window?.Dispose();}
    }
    internal static int Target(string configPath)
    {
        Config? config=null;
        try
        {
            config=Read<Config>(Path.GetFullPath(configPath));Directory.CreateDirectory(config.Output);using var client=new Client(config.Data,config.Store,config.Hash,config.Port,"ContactTarget");
            int commandSequence=0;Command command=new(0,"spawn");bool readySent=false;int ticks=0;
            client.BeforeFrame=()=>
            {
                // Declared owner pose setup occurs before every simulation frame.
                if(command.Position!=null&&command.Facing!=null&&client.Player.Health>0)
                {var position=Vec(command.Position);var facing=Vec(command.Facing).Normalized();client.Player.Reposition(position,facing,client.Scene!.Room!.GetNodeRefByPosition(position));client.Player.ModSetAim(facing);client.Player.Speed=Vector3.Zero;}
                client.Hold(Keys.Enter,client.Player.Health<=0&&command.Phase is "spawn" or "respawn");
            };
            while(true)
            {
                string commandPath=Path.Combine(config.Output,"command.json");if(File.Exists(commandPath)){var next=Read<Command>(commandPath);if(next.Sequence>=commandSequence){command=next;commandSequence=next.Sequence;}}if(command.Phase=="stop")break;
                if(NetSession.RequiredWeaponPackagesReady&&NetSession.IsInLobby&&!readySent){if(NetSession.SendLobbyCommand(LobbyCommandType.SetReady,ready:true))readySent=true;}
                if(NetSession.IsStarting&&client.Scene==null)client.Load();
                client.Step();
                if(++ticks%4==0)
                {
                    int slot=NetSession.LocalSlot;var native=slot>=0?NetSession.RemoteStates[slot]:default;var position=client.Scene==null?Vector3.Zero:client.Player.Position;
                    Atomic(Path.Combine(config.Output,"status.json"),new Status(client.Scene==null?"lobby":client.Player.Health>0?"alive":"dead",slot,client.Scene==null?0:client.Player.Health,NetPlayerLifecycle.Generation(slot),NetPlayerLifecycle.Get(slot),native.DamageEventId,NetSession.NetFrame,Array(position),commandSequence,MaxPickupHeight:client.Scene==null?0:Fixed.ToFloat(client.Player.Values.MaxPickupHeight),BipedTop:client.Scene==null?0:client.Player.ModBipedTop));
                }
            }
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine("CONTACT TARGET FAIL "+error);if(config!=null)Atomic(Path.Combine(config.Output,"status.json"),new Status("error",-1,0,0,0,0,0,Array(Vector3.Zero),0,error.ToString()));return 1;}
    }
    private static WeaponPackage Package()
    {
        var reference=WeaponPackage.Parse(WeaponPackageReference.BuildRiftlineM7());var resources=reference.ResourcePaths.Where(x=>x!="weapon.json").ToDictionary(x=>x,x=>reference.CopyResource(x),StringComparer.Ordinal);
        var mechanics=JsonNode.Parse(resources[reference.Project.MechanicsPath])!;mechanics["damage"]!["body"]=37;mechanics["damage"]!["head"]=50;mechanics["magazineCapacity"]=6;mechanics["initialReserve"]=23;mechanics["maximumReserve"]=40;mechanics["shotCooldownTicks"]=24;mechanics["reloadTicks"]=42;resources[reference.Project.MechanicsPath]=Encoding.UTF8.GetBytes(mechanics.ToJsonString());
        var project=JsonNode.Parse(reference.Project.ToCanonicalBytes())!;project["contentId"]="14c014c0-1122-3344-5566-778899aabbcc";project["name"]="Original ordinary contact capture fixture";return WeaponPackage.Parse(WeaponPackageBuilder.Build(WeaponProject.Parse(Encoding.UTF8.GetBytes(project.ToJsonString())),resources));
    }
    internal static int Source(string data,string output,string serverDll)
    {
        data=Path.GetFullPath(data);output=Path.GetFullPath(output);serverDll=Path.GetFullPath(serverDll);Directory.CreateDirectory(output);
        Process? server=null,target=null;var records=new List<(ReplayFactKind Kind,uint Frame,uint Tick,byte[] Bytes)>();var contacts=new List<(ulong Sequence,CustomContactOutcome Outcome)>();var launchDiagnostics=new List<object>();var shotDiagnostics=new List<object>();long bytes=0;bool overflow=false,sourceCompleted=false;ReplayCustomWeaponContext? context=null;int frozenMatchIndex=-1,latestMatchIndex=-1;
        void Capture(ReplayTimelineRecord record)
        {
            if(record.Kind==ReplayFactKind.World)return;
            byte[] copy=record.Kind==ReplayFactKind.Event?ReplayTimelineArchive.EncodeMarker(record.ServerTick,record.Marker??throw new InvalidDataException("Accepted Event has no actual marker.")):record.Payload.ToArray();
            if(records.Count>=32768||bytes+copy.Length>64L*1024*1024){overflow=true;return;}
            records.Add((record.Kind,record.RecordingFrame,record.ServerTick,copy));bytes+=copy.Length;
            if(copy.Length>0&&copy[0]==(byte)PacketType.MatchState)latestMatchIndex=records.Count-1;
            if(record.Kind!=ReplayFactKind.CustomWeapon)return;context=ReplayCapture.Recorder.CustomWeaponContext;if(frozenMatchIndex<0)frozenMatchIndex=latestMatchIndex;
            if(context!=null&&ReplayCustomWeaponFact.TryRead(copy,context,out var launchFact)&&launchFact!.Kind==ReplayCustomWeaponFactKind.Launch&&launchFact.TryLaunch(context,out var launch))
            {launchDiagnostics.Add(new{launchFact.Sequence,record.RecordingFrame,record.ServerTick,launch.World,origin=Array(launch.Origin),direction=Array(launch.Direction),body=Array(launch.Source.Body),aim=Array(launch.Source.Aim),optic=launch.Source.Action.Optic});Console.WriteLine($"ACTUAL LAUNCH seq={launchFact.Sequence} frame={record.RecordingFrame} world={launch.World} body={launch.Source.Body} aim={launch.Source.Aim} origin={launch.Origin} direction={launch.Direction} optic={launch.Source.Action.Optic}");}
            if(context!=null&&ReplayCustomWeaponFact.TryRead(copy,context,out var fact)&&fact!.Kind==ReplayCustomWeaponFactKind.Contact&&fact.TryContact(context,out _,out var outcome))
            {contacts.Add((fact.Sequence,outcome));Console.WriteLine($"ACTUAL CONTACT seq={fact.Sequence} head={outcome.Contact.Headshot} lethal={outcome.Lethal} HP={outcome.Health.BodyBefore}->{outcome.Health.BodyAfter} life={outcome.Contact.Key.Victim.Life} event={outcome.Health.DamageEventId}");}
        }
        void ExportBundle()
        {
            File.WriteAllText(Path.Combine(output,"replay-fixture-data.txt"),data);
            using(var stream=File.Create(Path.Combine(output,"accepted-full-stream.bin")))using(var writer=new BinaryWriter(stream))foreach(var record in records){writer.Write((byte)record.Kind);writer.Write(record.Frame);writer.Write(record.Tick);writer.Write(record.Bytes.Length);writer.Write(record.Bytes);}
            File.WriteAllText(Path.Combine(output,"accepted-launches.json"),JsonSerializer.Serialize(launchDiagnostics,Json));File.WriteAllText(Path.Combine(output,"shot-attempts.json"),JsonSerializer.Serialize(shotDiagnostics,Json));
            if(context!=null)File.WriteAllBytes(Path.Combine(output,"inspection-descriptor.bin"),ReplayCustomWeaponDescriptor.ForInspection(context).CopyBytes());
            File.WriteAllText(Path.Combine(output,"accepted-stream-results.json"),JsonSerializer.Serialize(new{version=1,records=records.Count,payloadBytes=bytes,frozenMatchIndex,latestMatchIndex,overflow,sourceCompleted,customCaptureFailed=ReplayCapture.Recorder.CustomWeaponCaptureFailed,context=context?.Digest.ToString(),recordLayout="kind:u8, recordingFrame:u32LE, serverTick:u32LE, payloadLength:i32LE, payload[exactLength]",eventEncoding="Event retains the actual record.Marker through existing ReplayTimelineArchive.EncodeMarker(ServerTick,Marker); all non-Event payloads are exact original bytes. No synthesized event.",limits=new{records=32768,payloadBytes=64*1024*1024}},Json));
        }
        ReplayCapture.Recorder.Accepted+=Capture;
        try
        {
            var package=Package();File.WriteAllBytes(Path.Combine(output,"variant.ppweapon"),package.CopyArchive());string serverStore=Path.Combine(output,"server-store"),sourceStore=Path.Combine(output,"source-store"),targetStore=Path.Combine(output,"target-store");foreach(string store in new[]{serverStore,sourceStore,targetStore})new WeaponPackageStore(store).Install(package.CopyArchive());
            int port;using(var socket=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))port=((IPEndPoint)socket.Client.LocalEndPoint!).Port;
            string host=Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".dotnet","dotnet.exe");string rotation=Path.Combine(output,"rotation.txt");MapRotation.WriteList(rotation,new[]{("MP3 PROVING GROUND",GameMode.Battle)},0,10000);
            Process Launch(string dll,string[] argv,string folder)
            {
                Directory.CreateDirectory(folder);var start=new ProcessStartInfo(host){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=data};start.ArgumentList.Add(dll);foreach(string arg in argv)start.ArgumentList.Add(arg);start.Environment["PROJECT_PRIME_USER_DATA"]=folder;start.Environment["DOTNET_PROCESSOR_COUNT"]="2";File.WriteAllText(Path.Combine(folder,"paths.txt"),"0.35.1.0\nAMHE1="+Paths.FileSystem+"\n");
                var process=new Process{StartInfo=start};process.OutputDataReceived+=(_,e)=>{if(e.Data!=null){string path=Path.Combine(folder,"stdout.log");if(!File.Exists(path)||new FileInfo(path).Length<1024*1024)File.AppendAllText(path,e.Data+Environment.NewLine);}};process.ErrorDataReceived+=(_,e)=>{if(e.Data!=null){string path=Path.Combine(folder,"stderr.log");if(!File.Exists(path)||new FileInfo(path).Length<1024*1024)File.AppendAllText(path,e.Data+Environment.NewLine);}};process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();return process;
            }
            Directory.SetCurrentDirectory(data);Paths.UpdatePaths();Paths.ChooseMphPath();
            server=Launch(serverDll,new[]{"-server","-port",port.ToString(),"-players","2","-lobby","-nomaster","-noserverreplays","-rotation",rotation,"-weaponstore",serverStore,"-weaponarchive",package.PackageHash},Path.Combine(output,"server-profile"));
            using var client=new Client(data,sourceStore,package.PackageHash,port,"ContactSource");client.Until(()=>NetSession.RequiredWeaponPackagesReady&&NetSession.LocalIsLobbyOwner,"source ordinary package lobby",40);
            string targetFolder=Path.Combine(output,"target");Directory.CreateDirectory(targetFolder);string configPath=Path.Combine(targetFolder,"config.json");Atomic(configPath,new Config(data,targetFolder,targetStore,package.PackageHash,port));target=Launch(typeof(WeaponPackageContactGlChecks).Assembly.Location,new[]{"--weapon-package-contact-target",configPath},Path.Combine(targetFolder,"profile"));
            Status TargetStatus(){if(target.HasExited)throw new InvalidOperationException("Target exited "+target.ExitCode);var status=Read<Status>(Path.Combine(targetFolder,"status.json"));if(status.Error!=null)throw new InvalidOperationException(status.Error);return status;}
            client.Until(()=>File.Exists(Path.Combine(targetFolder,"status.json"))&&TargetStatus().Slot>=0&&NetSession.SlotLobbyReady[TargetStatus().Slot],"target ordinary lobby preparation and ready",45);
            if(!NetSession.SendLobbyCommand(LobbyCommandType.SetReady,ready:true))throw new InvalidOperationException("Source ready failed.");client.Until(()=>!NetSession.LobbyCommandPending&&NetSession.SlotLobbyReady[NetSession.LocalSlot],"source ready settles");if(!NetSession.SendLobbyCommand(LobbyCommandType.StartMatch))throw new InvalidOperationException("Ordinary start failed.");client.Until(()=>NetSession.IsStarting,"ordinary match starts");client.Load();client.Spawn();client.Until(()=>TargetStatus().Phase=="alive","target ordinary native spawn",45);client.Step(420);
            bool beforePickupAbsent=NetSession.CustomWeaponPresentationState is {HasWeapon:false};if(!beforePickupAbsent)throw new InvalidOperationException("Ordinary spawn implicitly funded a package weapon before world acquisition.");Console.WriteLine("CONTACT PASS ordinary spawn has no package ownership before W world pickup");
            int scoreSourceSlot=NetSession.LocalSlot,scoreTargetSlot=TargetStatus().Slot;var initialSourceScore=NetSession.RemoteStates[scoreSourceSlot];var initialTargetScore=NetSession.RemoteStates[scoreTargetSlot];
            PlayerSpawnEntity? spawn=null;foreach(var candidate in client.Scene!.GetPlayerSpawnEntities())if(candidate.Id==4)spawn=candidate;
            if(spawn==null)throw new InvalidOperationException("Native pickup approach spawn4 absent.");
            var from=spawn.Position;CollisionResult floor=default;if(!CollisionDetection.CheckBetweenPoints(from+Vector3.UnitY*2,from-Vector3.UnitY*6,TestFlags.Players,client.Scene,ref floor))throw new InvalidOperationException("Native pickup approach floor absent.");from.Y=floor.Position.Y;var pickup=StockRiftlinePickups.ForRoom("MP3 PROVING GROUND",package.Definition)[0].Position;var walk=(pickup-from).WithY(0).Normalized();
            bool placeOnce=true;client.BeforeFrame=()=>{if(placeOnce){client.Player.Reposition(from.AddY(.5f),walk,client.Scene.Room!.GetNodeRefByPosition(from));client.Player.ModSetAim(walk);client.Player.Speed=Vector3.Zero;placeOnce=false;}};client.Step(15);client.Hold(Keys.W,true);client.Until(()=>NetSession.CustomWeaponPresentationState is {HasWeapon:true},"source acquires package through actual W world pickup",12);client.Hold(Keys.W,false);client.Press(Keys.C);client.Until(()=>NetSession.CustomWeaponInputProjection is {Selected:true},"source selects acquired package");
            Vector3 laneSource=client.Player.Position,laneTarget=default,laneFacing=default;bool laneFound=false;
            foreach(int height in new[]{0,2,5})foreach(var direction in new[]{Vector3.UnitZ,Vector3.UnitX,-Vector3.UnitX,-Vector3.UnitZ})
            {
                if(laneFound)break;var start=laneSource+Vector3.UnitY*height;var end=start+direction*8;
                if(BeamProjectileEntity.TryBeamObstacle(start+Vector3.UnitY*.4f,end+Vector3.UnitY*.2f,client.Scene,out _,out _)||BeamProjectileEntity.TryBeamObstacle(start+Vector3.UnitY*.4f,end+Vector3.UnitY*1.1f,client.Scene,out _,out _))continue;laneSource=start;laneTarget=end;laneFacing=direction;laneFound=true;
            }
            if(!laneFound)throw new InvalidOperationException("No clear declared eight-unit contact lane.");
            int commandSequence=1;Atomic(Path.Combine(targetFolder,"command.json"),new Command(commandSequence,"lane",Array(laneTarget),Array(laneFacing)));Vector3 aimPoint=laneTarget+Vector3.UnitY*.2f;
            client.BeforeFrame=()=>{if(client.Player.Health>0){client.Player.Reposition(laneSource,laneFacing,client.Scene.Room!.GetNodeRefByPosition(laneSource));client.Player.ModSetAim((aimPoint-(laneSource+Vector3.UnitY*.4f)).Normalized());client.Player.Speed=Vector3.Zero;}};
            int targetSlot=TargetStatus().Slot;client.Until(()=>TargetStatus().CommandSequence==commandSequence&&(NetSession.RemoteStates[targetSlot].Position-laneTarget).Length<.75f,"ordinary target pose converges through accepted native snapshot",30);client.Step(420);
            ushort firstLife=TargetStatus().Life,firstGeneration=TargetStatus().Generation;int initialHealth=TargetStatus().Health;if(initialHealth<=50)throw new InvalidOperationException("Actual initial health cannot cover nonlethal head fixture.");
            CustomContactOutcome Fire(bool head)
            {
                if(NetSession.CustomWeaponPresentationState is {State.Magazine:0}){client.Press(Keys.R);client.Until(()=>NetSession.CustomWeaponPresentationState is {State.Phase:CustomWeaponPhase.Ready,State.Magazine:>0},"keyboard reload before next contact");}
                if(head&&NetSession.CustomWeaponPresentationState is {State.Optic:0}){client.Press(Keys.Z);client.Until(()=>NetSession.CustomWeaponPresentationState is {State.Optic:>0},"ordinary Z input reaches accepted scoped optic before head shot");}
                aimPoint=laneTarget+Vector3.UnitY*(head?Fixed.ToFloat(client.Scene.Players.Items[targetSlot].Values.MaxPickupHeight)-.05f:.2f);client.Step(30);
                void Diagnose(string stage)
                {
                    var targetState=TargetStatus();var observed=NetSession.RemoteStates[targetSlot];var value=new{stage,head,frame=NetSession.NetFrame,sourcePosition=Array(client.Player.Position),aimPoint=Array(aimPoint),acceptedTargetPosition=Array(observed.Position),acceptedTargetHealth=observed.Health,observed.SlotGeneration,observed.LifeId,observed.DamageEventId,target=targetState,state=NetSession.CustomWeaponPresentationState?.ToString(),receipt=NetSession.LastCustomWeaponReceipt?.ToString(),denial=NetSession.LastCustomControlDenied?.ToString(),contacts=contacts.Count,launches=launchDiagnostics.Count};shotDiagnostics.Add(value);File.WriteAllText(Path.Combine(output,"shot-attempts.json"),JsonSerializer.Serialize(shotDiagnostics,Json));Console.WriteLine("SHOT ATTEMPT "+JsonSerializer.Serialize(value));
                }
                Diagnose("before-keyboard");int previous=contacts.Count;try{client.Press(Keys.Enter);client.Until(()=>contacts.Count>previous,"ordinary keyboard shot yields actual accepted contact",12);}finally{Diagnose("after-attempt");}var outcome=contacts[^1].Outcome;
                if(outcome.Contact.Headshot!=head||outcome.Contact.Key.Victim.Slot!=targetSlot||outcome.Health.BodyRemoved<=0)throw new InvalidOperationException("Actual contact geometry/payment differs from declared shot.");client.Until(()=>TargetStatus().Health==outcome.Health.BodyAfter&&NetSession.RemoteStates[targetSlot].Health==outcome.Health.BodyAfter,"target local and source native HP agree with accepted authority outcome",12);return outcome;
            }
            var body=Fire(false);if(body.Health.BodyBefore!=initialHealth||body.Health.BodyRemoved!=37||body.Lethal)throw new InvalidOperationException("Actual first body debit differs.");
            var firstHead=Fire(true);if(firstHead.Lethal||firstHead.Health.BodyRemoved!=50)throw new InvalidOperationException("Genuine nonlethal head contact absent.");int shots=0;var lethal=firstHead;while(!lethal.Lethal&&shots++<8)lethal=Fire(true);if(!lethal.Lethal)throw new InvalidOperationException("No genuine lethal head.");
            client.Until(()=>NetSession.RemoteStates[scoreSourceSlot].Kills==initialSourceScore.Kills+1&&NetSession.RemoteStates[scoreSourceSlot].Points==initialSourceScore.Points+1&&NetSession.RemoteStates[scoreTargetSlot].Deaths==initialTargetScore.Deaths+1,"actual accepted native kill point and death scores advance exactly once",12);
            Atomic(Path.Combine(targetFolder,"command.json"),new Command(++commandSequence,"respawn",Array(laneTarget),Array(laneFacing)));client.Until(()=>TargetStatus().Health>0&&TargetStatus().Life!=firstLife,"target keyboard respawn produces new native life",40);client.Step(420);ushort newLife=TargetStatus().Life,newGeneration=TargetStatus().Generation;var newContact=Fire(false);if(newContact.Contact.Key.Victim.Life!=newLife)throw new InvalidOperationException("New-life contact identity differs.");
            client.Picture(Path.Combine(output,"source-contact-window.png"));if(overflow||ReplayCapture.Recorder.CustomWeaponCaptureFailed||context==null)throw new InvalidOperationException("Accepted capture failed or exceeded budget.");
            using(var stream=File.Create(Path.Combine(output,"accepted-full-stream.bin")))using(var writer=new BinaryWriter(stream))foreach(var record in records){writer.Write((byte)record.Kind);writer.Write(record.Frame);writer.Write(record.Tick);writer.Write(record.Bytes.Length);writer.Write(record.Bytes);}
            var finalSourceScore=NetSession.RemoteStates[scoreSourceSlot];var finalTargetScore=NetSession.RemoteStates[scoreTargetSlot];
            File.WriteAllBytes(Path.Combine(output,"inspection-descriptor.bin"),ReplayCustomWeaponDescriptor.ForInspection(context).CopyBytes());Atomic(Path.Combine(output,"contact-results.json"),new{beforePickupAbsent,nativeScores=new{sourceSlot=scoreSourceSlot,targetSlot=scoreTargetSlot,initialSource=new{initialSourceScore.Points,initialSourceScore.Kills},finalSource=new{finalSourceScore.Points,finalSourceScore.Kills},initialTarget=new{initialTargetScore.Deaths},finalTarget=new{finalTargetScore.Deaths}},records=records.Count,bytes,frozenMatchIndex,package.PackageHash,initialHealth,firstLife,newLife,firstGeneration,newGeneration,laneSource=Array(laneSource),laneTarget=Array(laneTarget),contacts=contacts.Select(x=>new{x.Sequence,x.Outcome.AuthorityFrame,x.Outcome.Contact.Headshot,x.Outcome.Lethal,point=Array(x.Outcome.Contact.Point),victim=x.Outcome.Contact.Key.Victim,x.Outcome.Health}),scope="Two ordinary client processes and actual dedicated authority. Declared owner pose setup before simulation, actual keyboard acquisition/fire/reload/respawn and accepted contacts; no fabricated packets, HP, grants or tape."});
            Atomic(Path.Combine(targetFolder,"command.json"),new Command(++commandSequence,"stop"));if(!target.WaitForExit(10000))throw new TimeoutException("Target did not stop.");if(target.ExitCode!=0)throw new InvalidOperationException("Target failed shutdown.");Atomic(Path.Combine(output,"target-process.json"),new{pid=target.Id,actualExit=target.ExitCode,completed=true});sourceCompleted=true;Console.WriteLine($"PASS genuine body/nonlethal-head/lethal-head/new-life contact capture: {contacts.Count} contacts");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("CONTACT SOURCE FAIL "+error);return 1;}
        finally{ReplayCapture.Recorder.Accepted-=Capture;try{ExportBundle();}catch(Exception exportError){Console.Error.WriteLine("CONTACT BUNDLE EXPORT FAIL "+exportError);}foreach(var child in new[]{target,server}){if(child==null)continue;if(!child.HasExited){child.Kill(entireProcessTree:true);child.WaitForExit(10000);}child.Dispose();}}
    }
}
