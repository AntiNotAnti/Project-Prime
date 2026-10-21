using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
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
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Actual ordinary client, separate dedicated process, real package workers and
// native loading/UDP world acquisition. Only authored spawn4 placement is setup.
internal static class WeaponPackageOnlineGlChecks
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly Action<KeyboardState,Keys,bool> Key=typeof(KeyboardState).GetMethod("SetKeyState",Hidden|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
    internal static int Run(string data,string output,string serverDll,bool localHost=false,string? remoteHost=null,bool captureCustomFacts=false)
    {
        string cwd=Directory.GetCurrentDirectory();string? oldStore=Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE");
        bool force=MapAudit.ForceEveryone,script=NetTestScript.Enabled,scope=InputSettings.CustomScopeHold;
        bool focus=GamepadContexts.Focused,menu=GamepadContexts.MenuVisible,capture=GamepadContexts.Capturing,text=GamepadContexts.TextEntryActive,stylus=PointerInput.StylusMode;
        Scene? scene=null;NativeWindow? window=null;DesktopGraphicsSession? graphics=null;Process? server=null;
        var logs=new StringBuilder();object logGate=new();int checks=0;var watch=Stopwatch.StartNew();var pictures=new List<object>();double nextStep=0;bool observeShotPose=false,sawShotPose=false,sawCwmDuringStarting=false;
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks++;Console.WriteLine("ONLINE PACKAGE GL PASS "+label);}
        var recorder=ReplayCapture.Recorder;
        var acceptedFacts=new List<(byte[] Payload,ReplayCustomWeaponContext? Context,uint RecordingFrame,uint ServerTick)>();
        var acceptedSceneFacts=new List<(byte[] Payload,ReplayFactKind Kind,uint Frame)>();long sceneCaptureBytes=0;int latestMatchIndex=-1,frozenMatchIndex=-1;
        byte[]? latestMatch=null,frozenMatch=null;uint latestMatchFrame=0,frozenMatchFrame=0;
        bool captureOverflow=false;long capturedBytes=0;
        void CaptureFact(ReplayTimelineRecord record)
        {
            if(record.Kind is ReplayFactKind.Match or ReplayFactKind.Roster or ReplayFactKind.Snapshot or ReplayFactKind.Intent or ReplayFactKind.AuthorityWorld or ReplayFactKind.Presentation or ReplayFactKind.CustomWeapon)
            {if(acceptedSceneFacts.Count>=16384||sceneCaptureBytes+record.Payload.Length>64L*1024*1024){captureOverflow=true;return;}acceptedSceneFacts.Add((record.Payload.ToArray(),record.Kind,record.RecordingFrame));sceneCaptureBytes+=record.Payload.Length;}
            if(record.Kind==ReplayFactKind.Match&&record.Payload.Length>0&&record.Payload[0]==(byte)PacketType.MatchState){if(record.Payload.Length>64*1024){captureOverflow=true;return;}latestMatch=record.Payload.ToArray();latestMatchFrame=record.RecordingFrame;latestMatchIndex=acceptedSceneFacts.Count-1;return;}
            if(record.Kind!=ReplayFactKind.CustomWeapon)return;
            if(acceptedFacts.Count==0){frozenMatch=latestMatch?.ToArray();frozenMatchFrame=latestMatchFrame;frozenMatchIndex=latestMatchIndex;}
            if(acceptedFacts.Count>=8192||capturedBytes+record.Payload.Length>32L*1024*1024){captureOverflow=true;return;}
            byte[] bytes=record.Payload.ToArray();capturedBytes+=bytes.Length;
            acceptedFacts.Add((bytes,recorder.CustomWeaponContext,record.RecordingFrame,record.ServerTick));
        }
        if(captureCustomFacts)recorder.Accepted+=CaptureFact;
        try
        {
            data=Path.GetFullPath(data);output=Path.GetFullPath(output);serverDll=Path.GetFullPath(serverDll);Directory.CreateDirectory(output);
            Directory.SetCurrentDirectory(data);Paths.UpdatePaths();Paths.ChooseMphPath();
            Check(!Headless.Active&&!NetSession.Active,"fresh ordinary desktop client owns genuine graphics context");
            var reference=WeaponPackage.Parse(WeaponPackageReference.BuildRiftlineM7());var resources=reference.ResourcePaths.Where(x=>x!="weapon.json").ToDictionary(x=>x,x=>reference.CopyResource(x),StringComparer.Ordinal);
            var rules=JsonNode.Parse(resources[reference.Project.MechanicsPath])!;rules["damage"]!["body"]=37;rules["damage"]!["head"]=83;rules["magazineCapacity"]=6;rules["initialReserve"]=23;rules["maximumReserve"]=40;rules["shotCooldownTicks"]=24;rules["reloadTicks"]=42;rules["opticsMilli"]=new JsonArray(1500,2500,4000,6000);resources[reference.Project.MechanicsPath]=Encoding.UTF8.GetBytes(rules.ToJsonString());
            var model=JsonNode.Parse(resources[reference.Project.ModelPath])!;model["palette"]![0]=new JsonArray(.95f,.08f,.55f);resources[reference.Project.ModelPath]=Encoding.UTF8.GetBytes(model.ToJsonString());
            foreach(string path in reference.Project.AudioPaths){var wave=resources[path];for(int i=44;i<wave.Length;i+=2)BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(i),(short)(BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(i))/2));}
            var project=JsonNode.Parse(reference.Project.ToCanonicalBytes())!;project["contentId"]="12c012c0-1122-3344-5566-778899aabbcc";project["name"]="Network creator four-optic six-round";
            var package=WeaponPackage.Parse(WeaponPackageBuilder.Build(WeaponProject.Parse(Encoding.UTF8.GetBytes(project.ToJsonString())),resources));
            string serverStorePath=Path.Combine(output,"server-store"),clientStorePath=Path.Combine(output,"client-store");var serverStore=new WeaponPackageStore(serverStorePath);var clientStore=new WeaponPackageStore(clientStorePath);serverStore.Install(package.CopyArchive());clientStore.Install(package.CopyArchive());File.WriteAllBytes(Path.Combine(output,"variant.ppweapon"),package.CopyArchive());
            string rotation=Path.Combine(output,"rotation.txt");MapRotation.WriteList(rotation,new[]{("MP3 PROVING GROUND",GameMode.Battle)},0,10000);
            int port;using(var reserved=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))port=((IPEndPoint)reserved.Client.LocalEndPoint!).Port;
            string dotnet=Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".dotnet","dotnet.exe");if(!File.Exists(dotnet))dotnet="dotnet";
            var start=new ProcessStartInfo(dotnet){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=data};start.ArgumentList.Add(serverDll);
            foreach(string arg in new[]{"-server","-port",port.ToString(),"-players","2","-lobby","-nomaster","-noserverreplays","-rotation",rotation,"-weaponstore",serverStorePath,"-weaponarchive",package.PackageHash})start.ArgumentList.Add(arg);
            start.Environment["PROJECT_PRIME_USER_DATA"]=data;start.Environment["DOTNET_PROCESSOR_COUNT"]="2";
            if(remoteHost!=null)
            {
                if(remoteHost is not ("directory" or "dedicated"))throw new ArgumentException("Expected directory or dedicated allocator.");
                Check(LocalServer.Available() is {} available&&Path.GetFullPath(available.WorkingDirectory)==Path.GetDirectoryName(serverDll),"remote allocator uses explicitly staged matching dedicated build");
                int first=Enumerable.Range(42000,2000).First(candidate=>Enumerable.Range(candidate,4).All(LocalServer.PortAvailable));
                start.ArgumentList.Clear();start.ArgumentList.Add(serverDll);
                foreach(string arg in new[]{remoteHost=="directory"?"-masterserver":"-server","-port",port.ToString(),"-hostports",first+"-"+(first+3),"-hostweaponstore",serverStorePath,"-noupdate","-noautoupdate"})start.ArgumentList.Add(arg);
                if(remoteHost=="dedicated")foreach(string arg in new[]{"-lobby","-nomaster","-noserverreplays","-rotation",rotation})start.ArgumentList.Add(arg);
            }
            Guid ownerToken=default;
            if(localHost)
            {
                Check(LocalServer.Available() is {} available&&Path.GetFullPath(available.WorkingDirectory)==Path.GetDirectoryName(serverDll),"local allocator resolves explicitly staged matching dedicated build");
                int hosted=LocalServer.Start("Exact package local GL",new[]{("MP3 PROVING GROUND",GameMode.Battle)},2,0,10000,
                    "127.0.0.1",1,false,requestedPort:port,weaponPackage:new(serverStorePath,package.PackageHash));
                Check(hosted==port,"actual LocalServer allocator starts exact installed package child");
                ownerToken=LocalServer.OwnerToken;server=LocalServer.DetachRunning();
                Check(ownerToken!=Guid.Empty&&server!=null,"package allocator generates owner token and transfers exact child handle");
            }
            else
            {
                server=new Process{StartInfo=start};void Log(object sender,DataReceivedEventArgs line){if(line.Data!=null)lock(logGate){if(logs.Length<512*1024)logs.AppendLine(line.Data);}}
                server.OutputDataReceived+=Log;server.ErrorDataReceived+=Log;server.Start();server.BeginOutputReadLine();server.BeginErrorReadLine();
            }
            var activeServer=server??throw new InvalidOperationException("The dedicated child handle was not transferred.");
            void Bound(){if(watch.Elapsed.TotalSeconds>180)throw new TimeoutException("Online package GL exceeded180seconds.");if(activeServer.HasExited)throw new InvalidOperationException("Dedicated process exited: "+activeServer.ExitCode+" "+logs);}
            void Step(int count=1){for(int i=0;i<count;i++){Bound();if(scene==null)NetSession.Pump();else scene.OnSimulationFrame();sawCwmDuringStarting|=NetSession.IsStarting&&NetSession.CustomWeaponReadinessConfirmed;if(observeShotPose&&scene!=null){var pose=scene.CustomRiflePose(scene.Players.Main);sawShotPose|=pose.Kick>0||pose.Bolt>0||pose.Flash>0;}nextStep=Math.Max(nextStep+1000d/60,watch.Elapsed.TotalMilliseconds);int wait=(int)(nextStep-watch.Elapsed.TotalMilliseconds);if(wait>0)Thread.Sleep(wait);}}
            void Until(Func<bool> condition,string label,int seconds=20){var deadline=watch.Elapsed.TotalSeconds+seconds;while(!condition()){if(watch.Elapsed.TotalSeconds>deadline)throw new TimeoutException(label+"; "+NetSession.LastError+"; "+NetSession.WeaponPackagePreparationMessage+"; receipt="+NetSession.LastCustomWeaponReceipt?.Receipt+"; denial="+NetSession.LastCustomControlDenied+"; state="+NetSession.CustomWeaponPresentationState+"; localFrame="+NetSession.NetFrame);Step();}Check(true,label);}
            if(!localHost)Until(()=>{lock(logGate)return logs.ToString().Contains("listening on UDP",StringComparison.Ordinal);},"actual separate dedicated process listens with exact configured package");
            if(remoteHost!=null)
            {
                var hosted=NetMasterClient.RequestGame("127.0.0.1",port,"MP3 PROVING GROUND",GameMode.Battle,0,10000,2,
                    "Exact remote package GL",weaponArchiveHash:package.PackageHash);
                Check(hosted.Started&&hosted.OwnerToken!=Guid.Empty,"actual "+remoteHost+" allocator verifies archive and starts reachable dedicated child: "+hosted.Reason);
                port=hosted.Port;ownerToken=hosted.OwnerToken;
            }
            Environment.SetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE",clientStorePath);
            Check(NetLaunch.Connect("127.0.0.1",port,"CreatorPackageGL",Hunter.Samus,ownerToken:ownerToken,
                weaponPackage:localHost||remoteHost!=null?new(clientStorePath,package.PackageHash):null),"ordinary NetLaunch client joins actual package lobby");
            Until(()=>NetSession.RequiredWeaponPackagesReady&&NetSession.LocalIsLobbyOwner,"actual asynchronous package preparation and authority confirmation finish in lobby");
            Check(clientStore.Uninstall(package.PackageHash)&&clientStore.Prune()==0,"prepared lobby client retains exact archive after marker uninstall before match transition");
            Check(NetSession.SendLobbyCommand(LobbyCommandType.SetReady,ready:true),"ordinary client sends lobby ready");Until(()=>!NetSession.LobbyCommandPending&&NetSession.SlotLobbyReady[NetSession.LocalSlot],"ordinary lobby ready settles");
            Check(NetSession.SendLobbyCommand(LobbyCommandType.StartMatch),"ordinary client requests package match start");Until(()=>NetSession.IsStarting&&!NetSession.LobbyCommandPending&&NetSession.RequiredWeaponPackagesReady,"package match enters native load barrier with current preparation complete");
            MapAudit.ForceEveryone=true;NetTestScript.Enabled=false;GamepadManager.ClearAll();GamepadContexts.Focused=true;GamepadContexts.MenuVisible=GamepadContexts.Capturing=GamepadContexts.TextEntryActive=false;PointerInput.StylusMode=false;PointerDevice.Reset();InputSettings.CustomScopeHold=false;
            var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new(960,720);settings.StartVisible=true;settings.Title="Project Prime ordinary online package client acceptance";
            window=new NativeWindow(settings);graphics=new DesktopGraphicsSession(window);GLFW.SwapInterval(0);string renderer=GL.GetString(StringName.Renderer),version=GL.GetString(StringName.Version);
            var keyboard=SyntheticInput.CreateKeyboard();scene=new(window.FramebufferSize,keyboard,SyntheticInput.CreateMouse(),_=>{},()=>{});NetLaunch.BuildPlayers(scene,Hunter.Samus,0);scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:NetLaunch.RoomPlayerCount);scene.OnLoad();scene.OnResize();NetSession.MarkMatchLoaded();
            var mode=typeof(Scene).GetField("_inputMode",Hidden)!;mode.SetValue(scene,Enum.Parse(mode.FieldType,"All"));var player=scene.Players.Main;player.IsBot=false;
            foreach(var button in player.Controls.All){button.Type=ButtonType.Key;button.Key=Keys.Unknown;}player.Controls.Shoot.Key=Keys.Enter;player.Controls.MoveUp.Key=Keys.W;player.Controls.CustomWeapon.Key=Keys.C;player.Controls.Zoom.Key=Keys.Z;player.Controls.CustomOptic.Key=Keys.O;player.Controls.CustomReload.Key=Keys.R;player.Controls.MouseAim=player.Controls.KeyboardAim=false;
            void Press(Keys key,int after=3){Key(keyboard,key,true);Step();Console.WriteLine($"ONLINE INPUT key={key} shoot={player.Controls.Shoot.IsDown}/{player.Controls.Shoot.IsPressed} context={GamepadContexts.Current} revision={GamepadContexts.Revision}");Key(keyboard,key,false);Step(after);}
            Until(()=>NetSession.IsPlaying&&NetSession.WorldAdmissionConfirmed&&NetSession.CustomWeaponReadinessConfirmed&&NetSession.CustomWeaponReplicaReady,"ordinary actual scene/native world/custom readiness completes without synthetic bootstrap",30);
            Check(sawCwmDuringStarting&&NetSession.CustomWeaponReplicaReady,"CWM1 confirmation during Starting eventually yields actual recipient baseline and InMatch");
            Key(keyboard,Keys.Enter,true);Until(()=>player.Health>0&&player.ModIsInPlay,"ordinary online local hunter spawns");Key(keyboard,Keys.Enter,false);scene.CameraSequences.Current?.End();Step(60);scene.CameraSequences.Current?.End();
            Check(scene.LocalWeaponPackage?.PackageHash==package.PackageHash&&scene.CustomRifleModel?.Color(0)==new Vector4(.95f,.08f,.55f,1),"ordinary client scene owns exact prepared creator model before drawing");for(int cue=0;cue<4;cue++)Check(scene.CustomRifleWave(cue).SequenceEqual(WeaponPackage.DecodeWave(package.CopyResource(package.Project.AudioPaths[cue]))),"ordinary client owns exact prepared package PCM "+cue);
            Check(NetSession.CustomWeaponPresentationState is {HasWeapon:false}&&scene.OfflineWeapons==null&&scene.CustomCombat==null,"ordinary network preparation grants no local custom weapon or authority runtime");
            PlayerSpawnEntity? spawn=null;foreach(var entry in scene.GetPlayerSpawnEntities())if(entry.Id==4)spawn=entry;if(spawn==null)throw new InvalidOperationException("Missing authored approach spawn4.");var from=spawn.Position;CollisionResult floor=default;Check(CollisionDetection.CheckBetweenPoints(from+Vector3.UnitY*2,from-Vector3.UnitY*6,TestFlags.Players,scene,ref floor),"online authored approach uses native extracted floor");from.Y=floor.Position.Y;
            var center=StockRiftlinePickups.ForRoom("MP3 PROVING GROUND",package.Definition)[0].Position;var walk=(center-from).WithY(0).Normalized();player.Reposition(from.AddY(.5f),walk,scene.Room!.GetNodeRefByPosition(from));player.ModSetAim(walk);player.Speed=Vector3.Zero;Step(15);
            void Draw(string name){scene.OnDrawFrame();Check(scene.OnRenderFrame(),"actual ordinary client GL renders "+name);Check(GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,"no native GL error "+name);string native=Path.Combine(output,name+"-window.png"),world=Path.Combine(output,name+"-world.png");Check(ScreenCapture.SaveWindow(scene,native)&&ScreenCapture.Save(scene,world),"actual window/world images captured "+name);pictures.Add(new{name,native,world,sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(native)))});DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();}
            Draw("online-before-pickup");Key(keyboard,Keys.W,true);Until(()=>NetSession.CustomWeaponPresentationState is {HasWeapon:true},"ordinary W movement reaches native authoritative package world pickup",8);Key(keyboard,Keys.W,false);Step(8);
            Check(NetSession.CustomWeaponPresentationState is {State.Magazine:6,State.Reserve:23},"authority funds actual client with selected package six/twentythree");Press(Keys.C);Until(()=>NetSession.CustomWeaponInputProjection is {Selected:true},"ordinary custom selection equips acquired package");Press(Keys.Z);
            Until(()=>NetSession.CustomWeaponPresentationState is {State.Optic:1},"ordinary scope input reaches authority");for(int i=2;i<=4;i++){Press(Keys.O);int expected=i;Until(()=>NetSession.CustomWeaponPresentationState?.State.Optic==expected,"ordinary optic cycling reaches selected package optic"+i);}Draw("online-optic4");Step(2);
            observeShotPose=true;Press(Keys.Enter);Until(()=>NetSession.CustomWeaponPresentationState is {State.Magazine:5},"ordinary online fire spends one authority-owned creator round");Check(sawShotPose,"ordinary client accepted shot drives creator cosmetic clock");observeShotPose=false;Step(30);Press(Keys.R);Until(()=>NetSession.CustomWeaponPresentationState is {State.Magazine:6,State.Reserve:22,State.Phase:CustomWeaponPhase.Ready},"ordinary online reload completes exact package resource transition");Step(30);Draw("online-package-reloaded");
            if(captureCustomFacts)
            {
                Check(!captureOverflow&&!recorder.CustomWeaponCaptureFailed,"accepted custom fact capture remains healthy and within fixture bounds");
                var context=recorder.CustomWeaponContext??throw new InvalidOperationException("Ordinary client has no accepted custom replay context.");
                Check(context.Bootstrap.Packages.Rows.Count==1&&context.Bootstrap.Packages.Rows[0].Archive.ToString()==package.PackageHash,
                    "accepted ordinary client context pins the actual creator archive");
                Check(acceptedFacts.Count>0&&acceptedFacts.Count==recorder.AcceptedCustomWeaponFacts,"subscription copied every accepted ordinary client custom fact");
                var counts=new Dictionary<ReplayCustomWeaponFactKind,int>();var receipt=new List<object>();ulong expectedSequence=1;
                var linear=new ReplayCustomWeaponState(context);
                var parsedFacts=new List<(ReplayCustomWeaponFact Fact,uint Frame)>();var completePoints=new List<int>();
                bool recordedEmpty=false,recordedPickup=false,recordedSpent=false,recordedReloading=false,recordedReloaded=false,recordedOptic4=false;
                var recordedSlots=new CustomReplicaSlot[8];var recordedFlights=new CustomReplicaFlight[64];
                using(var stream=new FileStream(Path.Combine(output,"accepted-facts.bin"),FileMode.Create,FileAccess.Write,FileShare.None))
                using(var writer=new BinaryWriter(stream))
                {
                    foreach(var captured in acceptedFacts)
                    {
                        Check(captured.Context?.Digest==context.Digest&&ReplayCustomWeaponFact.TryRead(captured.Payload,context,out _),
                            "copied accepted custom fact validates against its exact context");
                        if(!ReplayCustomWeaponFact.TryRead(captured.Payload,context,out var fact))throw new InvalidDataException("Invalid accepted custom fact.");
                        Check(fact!.Sequence==expectedSequence++,"accepted custom fact sequence is contiguous");
                        if(!linear.Accept(fact,captured.RecordingFrame))throw new InvalidDataException("Actual accepted fact was unexpectedly duplicated.");
                        parsedFacts.Add((fact,captured.RecordingFrame));
                        if(linear.CompleteForRestore)completePoints.Add(parsedFacts.Count-1);
                        counts[fact.Kind]=counts.GetValueOrDefault(fact.Kind)+1;
                        if(fact.Kind is ReplayCustomWeaponFactKind.Baseline or ReplayCustomWeaponFactKind.State)
                        {
                            if(!fact.CopyTable(context,recordedSlots,recordedFlights,out _))throw new InvalidDataException("Accepted custom table cannot be decoded.");
                            var local=recordedSlots[NetSession.LocalSlot];var state=local.State;
                            recordedEmpty|=!local.HasWeapon;
                            recordedPickup|=local.HasWeapon&&state.Magazine==6&&state.Reserve==23;
                            recordedSpent|=local.HasWeapon&&state.Magazine==5&&state.Reserve==23;
                            recordedReloading|=local.HasWeapon&&state.Phase==CustomWeaponPhase.Reloading;
                            recordedReloaded|=local.HasWeapon&&state.Magazine==6&&state.Reserve==22&&state.Phase==CustomWeaponPhase.Ready;
                            recordedOptic4|=local.HasWeapon&&state.Optic==4;
                        }
                        writer.Write(captured.Payload.Length);writer.Write(captured.Payload);
                        receipt.Add(new{kind=fact.Kind.ToString(),sequence=fact.Sequence,world=fact.World,captured.RecordingFrame,captured.ServerTick,
                            bytes=captured.Payload.Length,sha256=Convert.ToHexStringLower(SHA256.HashData(captured.Payload))});
                    }
                }
                Check(counts.GetValueOrDefault(ReplayCustomWeaponFactKind.Bootstrap)==1&&counts.GetValueOrDefault(ReplayCustomWeaponFactKind.Baseline)==1,
                    "ordinary client capture contains exactly one bootstrap and baseline");
                Check(counts.GetValueOrDefault(ReplayCustomWeaponFactKind.State)>=1&&counts.GetValueOrDefault(ReplayCustomWeaponFactKind.Launch)>=1
                    &&counts.GetValueOrDefault(ReplayCustomWeaponFactKind.Pickup)>=2,"ordinary capture contains state launch and pickup facts");
                Check(counts.GetValueOrDefault(ReplayCustomWeaponFactKind.Clock)>=1,"ordinary accepted snapshots publish genuine custom Clock facts");
                Check(recordedEmpty,"accepted tables preserve initial absence of a granted weapon");
                Check(recordedPickup,"accepted tables preserve actual pickup resources six/twentythree");
                Check(recordedSpent,"accepted tables preserve authority-paid five/twentythree");
                Check(recordedReloading,"accepted tables preserve actual reloading phase");
                Check(recordedReloaded,"accepted tables preserve completed reload six/twentytwo");
                Check(recordedOptic4,"accepted tables preserve fourth creator optic");
                Check(linear.CompleteForRestore&&!linear.Invalidated&&completePoints.Count>0,
                    "actual accepted facts produce complete passive custom state");
                Check(recorder.TryCaptureCustomWeaponState(out var recorderDescriptor,out var recorderCheckpoint),"recorder exposes complete immutable current custom state");
                Check(recorderDescriptor!=null&&BinaryPrimitives.ReadUInt16LittleEndian(recorderDescriptor.CopyBytes().AsSpan(4))==2&&recorderDescriptor.InspectContext().Digest==context.Digest,"recorder current descriptor is exact inspection V2");
                Check(recorderCheckpoint.AsSpan().SequenceEqual(linear.CaptureCheckpointBytes()),"recorder current CWRS equals actual accepted linear state including clocks");
                Console.WriteLine($"CUSTOM CLOCK FACTS {counts.GetValueOrDefault(ReplayCustomWeaponFactKind.Clock)} TOTAL FACTS {parsedFacts.Count} COMPLETE POINTS {completePoints.Count}; restore prefix work is quadratic with bounded live snapshots");
                var leftFlights=new CustomReplicaFlight[ReplayCustomWeaponState.FlightCapacity];
                var rightFlights=new CustomReplicaFlight[ReplayCustomWeaponState.FlightCapacity];
                bool SameState(ReplayCustomWeaponState left,ReplayCustomWeaponState right)
                {
                    if(left.Invalidated||right.Invalidated||!left.CompleteForRestore||!right.CompleteForRestore
                        ||left.Sequence!=right.Sequence||left.World!=right.World||left.RecordingFrame!=right.RecordingFrame)return false;
                    for(int slot=0;slot<8;slot++)
                        if(!left.TrySlot(slot,out var slotLeft)||!right.TrySlot(slot,out var slotRight)||slotLeft!=slotRight)return false;
                    for(int index=0;index<context.Layout.Count;index++)
                        if(!left.TryPickup(index,out var pickupLeft)||!right.TryPickup(index,out var pickupRight)||pickupLeft!=pickupRight)return false;
                    int leftCount=left.CopyFlights(leftFlights),rightCount=right.CopyFlights(rightFlights);
                    return leftCount==rightCount&&leftFlights.AsSpan(0,leftCount).SequenceEqual(rightFlights.AsSpan(0,rightCount));
                }
                Check(frozenMatch!=null&&frozenMatch[0]==(byte)PacketType.MatchState&&frozenMatchFrame<=acceptedFacts[0].RecordingFrame,"actual native Match precedes first custom bootstrap");
                var inspectionDescriptor=ReplayCustomWeaponDescriptor.ForInspection(context);
                File.WriteAllBytes(Path.Combine(output,"inspection-descriptor.bin"),inspectionDescriptor.CopyBytes());
                File.WriteAllBytes(Path.Combine(output,"inspection-match.bin"),frozenMatch!);
                ReplayReplicaState InspectionOwner(){var owner=new ReplayReplicaState();owner.EnableCustomInspection(inspectionDescriptor);owner.Accept(frozenMatch!,frozenMatchFrame);return owner;}
                var inspectionFinal=InspectionOwner();uint inspectionRng1=inspectionFinal.Rng1,inspectionRng2=inspectionFinal.Rng2;
                foreach(var item in parsedFacts)inspectionFinal.Accept(item.Fact.CopyBytes(),item.Frame);
                var finalReplica=inspectionFinal.CaptureCheckpoint();
                var fullInspection=new ReplayReplicaState();fullInspection.EnableCustomInspection(inspectionDescriptor);
                int scopeStart=-1,nativeRosters=0,nativeSnapshots=0,nativeConfigurations=0;var expectedStart=context.Bootstrap.World.Start;
                for(int i=0;i<=frozenMatchIndex;i++)
                {
                    var item=acceptedSceneFacts[i];if(item.Payload.Length!=1+MatchStatePacket.Size||item.Payload[0]!=(byte)PacketType.MatchState)continue;
                    var match=MatchStatePacket.Read(item.Payload.AsSpan(1));
                    if(match.MatchId==expectedStart.MatchId&&match.AuthorityEpoch==expectedStart.AuthorityEpoch&&StringComparer.OrdinalIgnoreCase.Equals(match.RoomKey,inspectionDescriptor.Map.RoomKey))
                    {scopeStart=i;break;}
                }
                Check(scopeStart>=0,"ordered actual native stream contains exact custom match scope");
                for(int i=scopeStart;i<acceptedSceneFacts.Count;i++)
                {
                    var item=acceptedSceneFacts[i];
                    if(item.Payload.Length>0&&item.Payload[0]==(byte)PacketType.SessionState)
                    {
                        if(!SessionStatePacket.TryRead(item.Payload.AsSpan(1),out var configuration,allowRetiredEnhanced:true)||configuration.MatchId!=expectedStart.MatchId||configuration.AuthorityEpoch!=expectedStart.AuthorityEpoch||configuration.StartGeneration!=expectedStart.StartGeneration)continue;
                        nativeConfigurations++;
                    }
                    fullInspection.Accept(item.Payload,item.Frame);
                    if(item.Kind==ReplayFactKind.Roster)nativeRosters++;if(item.Kind==ReplayFactKind.Snapshot)nativeSnapshots++;
                }
                Check(nativeRosters>0&&nativeSnapshots>0&&nativeConfigurations>0,"full inspection consumes genuine native roster snapshot and configuration");
                Check(fullInspection.Occupant(NetSession.LocalSlot).Generation!=0&&fullInspection.TryGetPlayer(NetSession.LocalSlot,out _),"full inspection checkpoint contains actual ordinary client actor");
                var fullReplica=fullInspection.CaptureCheckpoint();File.WriteAllBytes(Path.Combine(output,"full-inspection-replica-checkpoint.bin"),fullReplica.Bytes.ToArray());
                Check(fullInspection.CustomWeapons!.CaptureCheckpointBytes().AsSpan().SequenceEqual(linear.CaptureCheckpointBytes()),"full native stream preserves exact accepted custom lane");
                Console.WriteLine($"FULL INSPECTION records={acceptedSceneFacts.Count-scopeStart} roster={nativeRosters} snapshot={nativeSnapshots} configuration={nativeConfigurations}");
                var replayStore=new WeaponPackageStore(Path.Combine(output,"isolated-replay-store"));replayStore.Install(package.CopyArchive());
                int preparationThread=0;
                using(var preparedReplay=Task.Run(()=>{preparationThread=Environment.CurrentManagedThreadId;return PreparedReplayWeaponPackage.Prepare(inspectionDescriptor,replayStore);}).GetAwaiter().GetResult())
                {
                    Check(preparationThread!=Environment.CurrentManagedThreadId,"historical CPU preparation completes on worker before Scene construction");
                    byte[] beforeScene=fullReplica.Bytes.ToArray();uint liveRng1=scene.Random.Rng1,liveRng2=scene.Random.Rng2;
                    using(var passive=PassiveReplayScene.CreateCustomInspection(fullReplica,preparedReplay,ReplayMapIdentity.Compute(fullInspection.Match!.Value.RoomKey),new Vector2i(640,480)))
                    {
                        Check(!ReferenceEquals(passive.Scene.GameState,scene.GameState)&&!ReferenceEquals(passive.Scene.Random,scene.Random),"custom inspection owns isolated GameState and RNG");
                        Check(passive.Scene.Players.PlayersCreated==8&&passive.Scene.Room!=null,"custom inspection loads room with all eight placeholders");
                        Check(passive.Scene.HasReplayWeaponPackage(inspectionDescriptor)&&passive.Scene.LocalWeaponPackage?.PackageHash==package.PackageHash&&passive.Scene.CustomRifleModel!=null,"custom inspection binds exact prepared package model resources");
                        Check(passive.Scene.OfflineWeapons==null&&passive.Scene.CustomCombat==null,"passive resource binding creates no gameplay authority");
                        Check(passive.State.CaptureCheckpoint().Bytes.SequenceEqual(beforeScene)&&scene.Random.Rng1==liveRng1&&scene.Random.Rng2==liveRng2,"passive bind preserves exact checkpoint and live RNG");
                        void RefuseWorldCapture(string label)
                        {
                            bool refused=false;try{using var unsupported=MphRead.Mods.Replay.ReplayWorldCheckpoint.Capture(passive);}catch(InvalidOperationException){refused=true;}
                            Check(refused,label);
                        }
                        RefuseWorldCapture("active custom passive Scene refuses native world capture");
                        preparedReplay.Dispose();Check(replayStore.Uninstall(package.PackageHash)&&replayStore.Prune()==0,"independent passive Scene lease survives prepared disposal uninstall and prune");
                        passive.Session.Stop();
                        Check(passive.State.CustomWeapons==null,"passive session Stop retires custom decoder scope");
                        RefuseWorldCapture("stopped custom resource Scene still refuses native world capture");
                        Check(passive.Scene.HasReplayWeaponPackage(inspectionDescriptor)&&replayStore.Prune()==0,"session Stop preserves independently leased passive Scene resources");
                    }
                    Check(replayStore.Prune()==1,"passive Scene disposal releases isolated historical archive lease");
                }
                Check(inspectionFinal.Rng1==inspectionRng1&&inspectionFinal.Rng2==inspectionRng2,"accepted custom inspection leaves native RNG unchanged");
                File.WriteAllBytes(Path.Combine(output,"final-replica-checkpoint.bin"),finalReplica.Bytes.ToArray());
                var descriptorBytes=new ReplayCustomWeaponDescriptor(context).CopyBytes();
                File.WriteAllBytes(Path.Combine(output,"descriptor.bin"),descriptorBytes);
                File.WriteAllBytes(Path.Combine(output,"final-checkpoint.bin"),linear.CaptureCheckpointBytes());
                int forwardRestores=0,backwardRestores=0;long suffixFactsApplied=0;
                // Rebuild one prefix at a time instead of retaining every large state
                // snapshot. Test memory stays bounded independently of fact count.
                foreach(bool backwards in new[]{false,true})
                foreach(int index in backwards?completePoints.AsEnumerable().Reverse():completePoints)
                {
                    var prefix=new ReplayCustomWeaponState(context);
                    for(int n=0;n<=index;n++)prefix.Accept(parsedFacts[n].Fact,parsedFacts[n].Frame);
                    var replicaPrefix=InspectionOwner();for(int n=0;n<=index;n++)replicaPrefix.Accept(parsedFacts[n].Fact.CopyBytes(),parsedFacts[n].Frame);
                    var replicaBytes=replicaPrefix.CaptureCheckpoint();
                    if(!backwards&&index==completePoints[0])
                    {
                        string archivePath=Path.Combine(output,"actual-custom"+DemoFile.Extension);
                        var match=replicaPrefix.Match!.Value;uint origin=replicaPrefix.RecordingFrame;
                        var metadata=new ReplayMetadata{FormatVersion=7,ProtocolVersion=83,CustomWeapons=inspectionDescriptor,
                            RoomKey=match.RoomKey,Mode=(GameMode)match.Mode,OriginRecordingFrame=origin,
                            Bootstrap=new ReplayBootstrap{Packets=new[]{ReplayTimelineArchive.Construction(replicaPrefix)}}};
                        using(var writer=new ReplayWriterV3(archivePath,metadata))
                        {
                            for(int n=index+1;n<parsedFacts.Count;n++)
                            {
                                var item=parsedFacts[n];var record=new ReplayTimelineRecord(item.Frame,item.Fact.World,ReplayFactKind.CustomWeapon,item.Fact.CopyBytes());
                                try{ReplayTimelineArchive.Write(writer,record,origin);}finally{record.Release();}
                            }
                            writer.WriteRecord(checked(inspectionFinal.RecordingFrame-origin),new byte[]{255});
                        }
                        using var reader=DemoReader.Open(archivePath)??throw new InvalidDataException("Actual custom archive did not reopen.");
                        Check(reader.Metadata is {FormatVersion:7,CustomWeapons:not null}&&reader.Metadata.CustomWeapons.CopyBytes().AsSpan().SequenceEqual(inspectionDescriptor.CopyBytes()),"closed format7 archive reopens exact descriptor");
                        var reopened=ReplayFormatV3.ValidateCustomArchiveMetadata(reader.Metadata!)!;int archiveFacts=0;
                        while(reader.ReadNext() is { } record)
                        {if(record.Data[0]==255)continue;reopened.Accept(record.Data,checked(record.Frame+reader.Metadata!.OriginRecordingFrame));archiveFacts++;}
                        Check(reader.LastResult==ReplayOpenResult.Success&&reopened.CaptureCheckpoint().Bytes.SequenceEqual(finalReplica.Bytes),"actual format7 archive close reopen and accepted suffix reproduce final replica15");
                        Console.WriteLine($"CUSTOM ARCHIVE {archivePath} SUFFIX FACTS {archiveFacts}");
                    }
                    Check(BinaryPrimitives.ReadUInt16LittleEndian(replicaBytes.Bytes[4..])==15,"explicit custom replica checkpoint version fifteen");
                    ReplayReplicaState.InspectCustomCheckpointLayout(replicaBytes);
                    var replicaRestored=new ReplayReplicaState();replicaRestored.RestoreCustomCheckpoint(replicaBytes);
                    Check(replicaRestored.CaptureCheckpoint().Bytes.SequenceEqual(replicaBytes.Bytes)&&replicaRestored.CustomWeapons!.CaptureCheckpointBytes().AsSpan().SequenceEqual(prefix.CaptureCheckpointBytes()),"explicit replica prefix canonical custom lane parity");
                    for(int n=index+1;n<parsedFacts.Count;n++)replicaRestored.Accept(parsedFacts[n].Fact.CopyBytes(),parsedFacts[n].Frame);
                    Check(replicaRestored.CaptureCheckpoint().Bytes.SequenceEqual(finalReplica.Bytes)&&replicaRestored.CustomWeapons!.CaptureCheckpointBytes().AsSpan().SequenceEqual(linear.CaptureCheckpointBytes())&&replicaRestored.Rng1==inspectionRng1&&replicaRestored.Rng2==inspectionRng2,"explicit replica actual suffix canonical parity and unchanged RNG");
                    var point=prefix.CaptureRestorePoint();
                    var freshContext=new ReplayCustomWeaponContext(context.Bootstrap,context.Definition,context.Layout);
                    var restored=point.Restore(freshContext);
                    Check(ReplayCustomWeaponDescriptor.TryRead(descriptorBytes,out var decodedDescriptor),"persisted descriptor decodes");
                    var durableContext=decodedDescriptor!.Bind(context.Definition);
                    byte[] checkpoint=prefix.CaptureCheckpointBytes();
                    if(!backwards&&index==completePoints[0])File.WriteAllBytes(Path.Combine(output,"first-complete-checkpoint.bin"),checkpoint);
                    var durable=ReplayCustomWeaponState.RestoreCheckpointBytes(checkpoint,durableContext);
                    Check(SameState(prefix,durable),"byte checkpoint independently reproduces actual prefix");
                    Check(ReferenceEquals(restored.Context,freshContext)&&SameState(prefix,restored),
                        "typed custom restore independently reproduces captured slots pickups and sorted flights");
                    for(int n=index+1;n<parsedFacts.Count;n++)
                    {restored.Accept(parsedFacts[n].Fact,parsedFacts[n].Frame);durable.Accept(parsedFacts[n].Fact,parsedFacts[n].Frame);suffixFactsApplied++;}
                    restored.Advance(linear.World,linear.RecordingFrame);
                    durable.Advance(linear.World,linear.RecordingFrame);
                    Check(SameState(linear,durable)&&linear.CaptureCheckpointBytes().AsSpan().SequenceEqual(durable.CaptureCheckpointBytes()),"byte checkpoint actual suffix matches final canonical state");
                    Check(SameState(linear,restored),backwards
                        ?"backward typed restore plus actual suffix matches final linear custom state"
                        :"forward typed restore plus actual suffix matches final linear custom state");
                    if(backwards)backwardRestores++;else forwardRestores++;
                }
                File.WriteAllText(Path.Combine(output,"accepted-facts-results.json"),JsonSerializer.Serialize(new{count=acceptedFacts.Count,capturedBytes,
                    package.PackageHash,nativeInspection=new{records=acceptedSceneFacts.Count-scopeStart,nativeRosters,nativeSnapshots,nativeConfigurations},customArchive="actual-custom"+DemoFile.Extension,inspectionMatchFrame=frozenMatchFrame,replicaCheckpointBytes=finalReplica.Bytes.Length,
                    replicaCheckpointSha256=Convert.ToHexStringLower(SHA256.HashData(finalReplica.Bytes)),context=context.Digest.ToString(),checkpointSha256=Convert.ToHexString(SHA256.HashData(linear.CaptureCheckpointBytes())),counts=counts.ToDictionary(pair=>pair.Key.ToString(),pair=>pair.Value),facts=receipt,
                    observations=new{recordedEmpty,recordedPickup,recordedSpent,recordedReloading,recordedReloaded,recordedOptic4},
                    passiveRestore=new{completePoints=completePoints.Count,forwardRestores,backwardRestores,suffixFactsApplied,
                        finalSequence=linear.Sequence,finalWorld=linear.World,finalRecordingFrame=linear.RecordingFrame},
                    scope="Copied accepted ordinary-client custom lane and actual native roster/snapshot/configuration build an isolated inspection Scene with worker-prepared historical resources and independent leases. CWRD1/2, CWRS and explicit replica15 cold restores preserve canonical bytes and RNG across actual suffixes. No actual contact capture expected from this single-client wall shot. Retained detached format7 archive close/reopen reconstructs the actual custom suffix. No whole Scene checkpoint restore, custom presentation, Demo playback or archive seek qualification."},new JsonSerializerOptions{WriteIndented=true}));
            }
            Check(clientStore.Prune()==0,"ordinary live session and scene retain archive uninstalled before match transition");NetSession.Stop();Check(clientStore.Prune()==0&&scene.LocalWeaponPackage?.PackageHash==package.PackageHash,"session Stop preserves independently retained ordinary scene package owner");scene.DoCleanup();scene=null;Check(clientStore.Prune()==1,"ordinary scene cleanup releases final retained client archive");
            File.WriteAllText(Path.Combine(output,"online-package-gl-results.json"),JsonSerializer.Serialize(new{checks,renderer,version,sawCwmDuringStarting,package.PackageHash,contentId=package.Project.ContentId,seconds=watch.Elapsed.TotalSeconds,pictures,scope="Actual separate dedicated process and ordinary NetSession client/native GL; genuine package preparation, native world admission, W world pickup,scope4,fire,reload. Authored approach placement is declared; no synthetic snapshots, grant or client authority."},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"PASS {checks} ordinary online package client GL checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("ONLINE PACKAGE GL FAIL "+error);return 1;}
        finally
        {
            if(captureCustomFacts)recorder.Accepted-=CaptureFact;
            NetSession.Stop();scene?.DoCleanup();MphRead.Sound.AudioLifetime.Shutdown();graphics?.Dispose();window?.Dispose();
            if(server!=null){if(!server.HasExited){server.Kill(true);server.WaitForExit(10000);}server.Dispose();}if(Directory.Exists(output)){lock(logGate)File.WriteAllText(Path.Combine(output,"dedicated-process.log"),logs.ToString());}
            Environment.SetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE",oldStore);MapAudit.ForceEveryone=force;NetTestScript.Enabled=script;InputSettings.CustomScopeHold=scope;GamepadContexts.Focused=focus;GamepadContexts.MenuVisible=menu;GamepadContexts.Capturing=capture;GamepadContexts.TextEntryActive=text;PointerInput.StylusMode=stylus;PointerDevice.Reset();GamepadManager.ClearAll();Directory.SetCurrentDirectory(cwd);
        }
    }
}
