using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Diagnostics;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

// Full production Run/ServerSim/Scene, original 60Hz pacing and intermission.
// Thin local peers echo baseline identity headers; they do not construct client worlds.
internal static class Program
{
    private static readonly string[] Rooms={"MP1 SANCTORUS","MP2 HARVESTER","MP3 PROVING GROUND","MP5 FUEL SLUICE","MP7 PROCESSOR CORE","MP9 CRYOCHASM"};
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);++checks;}
    private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Hidden)!.GetValue(owner)!;
    private static object? Call(object owner,string name,params object?[] args)=>owner.GetType().GetMethod(name,Hidden)!.Invoke(owner,args);
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed record Round(string Room,ushort Match,ulong Epoch,uint Start,int RosterOccupancy,int SnapshotPackets,
        MatchDefinition Frozen,byte ExpectedMask,byte LoadedMask,byte ReadyMask,double PostSeenAt);
    private static int Main(string[] args)
    {
        if(args[0]=="--revalidate")return Revalidate(args[1],args[2]);
        int humans=int.Parse(args[0]),bots=int.Parse(args[1]);bool idle=args[2]=="idle";string output=Path.GetFullPath(args[3]);
        bool requireRetirement=args.Length>4&&args[4]=="--require-retirement";
        var rounds=new List<Round>();var peers=new List<Peer>();var seen=new HashSet<ushort>();var checkedFloors=new HashSet<ushort>();
        SceneLifetimeSnapshot[] lifetime=Array.Empty<SceneLifetimeSnapshot>();Exception? failure=null,serverError=null;
        int dropped=0;using var cancel=new CancellationTokenSource();DedicatedServer? server=null;Thread? thread=null;
        var timer=Stopwatch.StartNew();int wrapLoads=0;
        try
        {
            Check(OperatingSystem.IsMacOS(),"Mac runtime scope");
            Check(Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA")==Directory.GetCurrentDirectory(),"fresh private cwd and userdata agree");
            Check(String.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROJECT_PRIME_CAREER_SERVER_KEY")),"reporter disabled; no external account/service calls");
            Check(GameFiles.Problem()==null&&Paths.MphKey=="AMHE1","actual AMHE1 asset lease is available");
            Check(Path.IsPathFullyQualified(Paths.Export)&&Paths.Export.StartsWith(Path.GetDirectoryName(output)!,StringComparison.Ordinal),"derived replay Export is inside private run directory");
            var rotation=MapRotation.FromList(Rooms.Select(r=>(r,GameMode.Battle)).ToArray(),3,0);
            server=new DedicatedServer(0,8,rotation){SessionPolicy=ServerSessionPolicy.Continuous,
                Reporter=null,PartyReservationsEnabled=false,FriendlyFire=true,AffinityWeapons=true,SpawnProtection=true,
                ReplayPolicy=new ServerReplayPolicy(Enabled:true,StorageLimitGb:0,RetentionDays:0,KeepLast:100)};
            Check(!Field<bool>(server,"_controlPlaneOnlyForTests"),"asset-free test seam is never enabled");
            for(int i=0;i<bots;++i)
            {
                object?[] configure={new LobbyCommandPacket{Hunter=(byte)(i==0?Hunter.Sylux:Hunter.Trace),BotLevel=1,TeamIndex=-1},false,null};
                Check((LobbyResultCode)Call(server,"ConfigureBot",configure)! ==LobbyResultCode.Ok,"actual server bot configuration before Run");
            }
            thread=new Thread(()=>
            {
                using var observation=SceneLifetimeDiagnostics.Begin(4096);
                try{server.Run(cancel.Token);}catch(Exception error){serverError=error;}
                finally{lifetime=observation.Snapshots.ToArray();dropped=observation.DroppedRecords;}
            }){IsBackground=true};thread.Start();
            while(!server.Listening&&timer.Elapsed.TotalSeconds<45&&serverError==null)Thread.Sleep(10);
            Check(server.Listening&&serverError==null,"real DedicatedServer.Run opens assigned ephemeral UDP port");
            if(idle)
            {
                Thread.Sleep(1250);Check(server.PeerCount==0&&!server.Simulating,"true zero-connected Continuous server keeps no authority Scene");
                cancel.Cancel();thread.Join(15000);Check(!thread.IsAlive&&serverError==null,"idle server exits cleanly");
                Check(lifetime.All(s=>s.Stage!=SceneLifetimeStage.Constructed),"true zero-connected idle constructs zero Scenes");
            }
            else
            {
                int connections=humans==0?1:humans;var endpoint=new IPEndPoint(IPAddress.Loopback,server.BoundPort);
                for(int i=0;i<connections;++i)peers.Add(new(endpoint,(uint)(101+i),spectator:humans==0));
                bool stopping=false;
                while(timer.Elapsed.TotalSeconds<225&&serverError==null)
                {
                    foreach(var peer in peers)peer.Poll();
                    var latest=peers[0].State;
                    if(latest is { } state&&server.Simulating)
                    {
                        if(state.Phase==SessionPhase.Starting&&rounds.Count>0&&checkedFloors.Add(state.MatchId))
                        {
                            Round old=rounds[^1];
                            Check(timer.Elapsed.TotalSeconds-old.PostSeenAt>=20.5,"original21sec post-match floor is preserved before next map");
                        }
                        if(state.Phase==SessionPhase.PostMatch&&!seen.Contains(state.MatchId))
                        {
                            Check(rounds.Count<6&&state.Match.RoomKey==Rooms[rounds.Count],"actual completed room follows six-map rotation");
                            Check(state.AuthorityEpoch!=0&&peers.All(p=>p.State?.MatchId==state.MatchId&&p.State?.AuthorityEpoch==state.AuthorityEpoch),"all actual wire observers agree on authoritative match/epoch");
                            Check(peers.All(p=>p.Roster.MatchId==state.MatchId&&p.Roster.AuthorityEpoch==state.AuthorityEpoch),"actual wire rosters agree with authority generation");
                            Check(peers.All(p=>p.Roster.Count==connections+bots)&&peers[0].Roster.ContainsBots==(bots>0),"actual wire roster publishes exact human/observer/bot occupancy");
                            Check(peers.All(p=>p.Snapshots.GetValueOrDefault(state.MatchId)>0),"each actual wire observer receives native authority snapshots");
                            Check(peers.All(p=>p.Ready.Contains(new(state.MatchId,state.AuthorityEpoch,state.StartGeneration))),"thin handshake observers received exact WorldReady confirmation");
                            if(rounds.Count>0)Check(state.MatchId==NetLifecycleTracker.Next(rounds[^1].Match)&&state.AuthorityEpoch==rounds[^1].Epoch
                                &&state.StartGeneration!=rounds[^1].Start,"rotation advances match/start under same server epoch");
                            rounds.Add(new(state.Match.RoomKey,state.MatchId,state.AuthorityEpoch,state.StartGeneration,peers[0].Roster.Count,
                                peers[0].Snapshots.GetValueOrDefault(state.MatchId),state.Match,state.ExpectedParticipants,state.LoadedParticipants,state.WorldReadyParticipants,
                                timer.Elapsed.TotalSeconds));seen.Add(state.MatchId);
                            Console.WriteLine($"WORLD ROUND {rounds.Count}/6 room={state.Match.RoomKey} match={state.MatchId} epoch={state.AuthorityEpoch} snapshots={peers[0].Snapshots.GetValueOrDefault(state.MatchId)} humans={humans} observers={(humans==0?1:0)} bots={bots}");
                        }
                        // Let the sixth original intermission really complete.
                        // The seventh/wrapped loaded Scene proves six rotations;
                        // its shutdown segment is explicitly not a seventh match.
                        if(rounds.Count==6&&state.MatchId!=rounds[^1].Match&&state.Phase==SessionPhase.InMatch)
                        {wrapLoads=1;Check(timer.Elapsed.TotalSeconds-rounds[^1].PostSeenAt>=20.5,"sixth original intermission completes before shutdown");stopping=true;break;}
                    }
                    Thread.Sleep(5);
                }
                Check(stopping&&rounds.Count==6,"six complete actual-world matches and wrapped reconstruction finish within225sec");
                cancel.Cancel();thread.Join(20000);Check(!thread.IsAlive&&serverError==null,"actual-world server shuts down normally");
                Check(!Field<bool>(server,"_controlPlaneOnlyForTests"),"production test flag remained disabled for entire campaign");
                VerifyRetired(rounds,lifetime,dropped,Environment.GetEnvironmentVariable("PRIME_NET2_EVIDENCE")!,requireRetirement);
                var archives=InspectArchives(rounds);
                Write("PASS",null,archives);
            }
            if(idle)Write("PASS",null,Array.Empty<object>());
            Console.WriteLine($"WORLD ROTATION PASS {checks} checks; completed{rounds.Count}, wrapLoads{wrapLoads}, humans{humans}, observers{(!idle&&humans==0?1:0)}, bots{bots}; original60Hz/21sec; no GL/client-world/device claims");return 0;
        }
        catch(Exception error){failure=error;Console.Error.WriteLine(error);return 1;}
        finally
        {
            cancel.Cancel();if(thread?.IsAlive==true)thread.Join(20000);
            foreach(var peer in peers)peer.Dispose();
            if(failure!=null)Write("FAIL",failure.ToString(),Array.Empty<object>());
        }
        void Write(string status,string? error,object[] archives)
        {
            File.WriteAllText(output,JsonSerializer.Serialize(new{status,error,checks,humans,bots,observerConnections=!idle&&humans==0?1:0,idle,
                scope="actual server Run/ServerSim/native Scene; thin UDP baseline observers, no client worlds/GL/backend/devices",
                assemblySha256=Hash(File.ReadAllBytes(typeof(DedicatedServer).Assembly.Location)),elapsedSeconds=timer.Elapsed.TotalSeconds,
                completedMatches=rounds.Count,wrappedLoadedWorlds=wrapLoads,rounds,lifetime,dropped,archives,
                clients=peers.Select(p=>new{p.Id,p.Slot,p.Spectator,p.SnapshotCount,p.BootstrapCount,p.StaleReadySent,p.ReadyCount}),serverError=serverError?.ToString()},new JsonSerializerOptions{WriteIndented=true})+"\n");
        }
    }
    private static void VerifyRetired(List<Round> rounds,SceneLifetimeSnapshot[] lifetime,int dropped,string evidencePath,bool requireRetirement=false)
    {
        // Canonical replay capture legitimately owns Replica scenes on this
        // thread too. They do not enter the authority-world denominator.
        var authority=lifetime.Where(s=>s.Stage==SceneLifetimeStage.Loaded&&s.Role==SceneLifetimeRole.HeadlessLive).ToArray();
        Check(authority.Length>=7&&dropped==0,"owner-thread diagnostic log contains all seven genuine loaded authority worlds");
        Check(authority.All(s=>s.Entities>8),"each loaded authority owns actual native world entities");
        Check(authority.Select(s=>s.Generation).Distinct().Count()>=7,"six rotations reconstruct separate native Scene generations");
        if(requireRetirement)
        {
            foreach(var world in authority)
                foreach(var stage in new[]{SceneLifetimeStage.CleanupStarted,SceneLifetimeStage.CleanupComplete,
                    SceneLifetimeStage.VisualResourcesReleased,SceneLifetimeStage.CompatibilityReleased})
                    Check(lifetime.Any(s=>s.Generation==world.Generation&&s.Stage==stage),"each exact loaded authority retires through "+stage+" including shutdown");
        }
        using var evidence=JsonDocument.Parse(File.ReadAllText(evidencePath));
        var segments=evidence.RootElement.GetProperty("ServerSegments").EnumerateArray().ToArray();
        foreach(var round in rounds)Check(segments.Any(s=>s.GetProperty("CurrentMatchId").GetUInt16()==round.Match
            &&s.GetProperty("AuthorityEpoch").GetUInt64()==round.Epoch&&s.GetProperty("Room").GetString()==round.Room
            &&s.GetProperty("Frames").GetInt64()>=60&&s.GetProperty("StepFailures").GetInt64()==0),
            "owner-thread retired segment proves exact match/epoch native steps and no swallowed failure");
    }
    private static int Revalidate(string source,string output)
    {
        // Retired evidence only. Preserve the original failure and process exit;
        // this result cannot rewrite or promote that original executable run.
        try
        {
            using var original=JsonDocument.Parse(File.ReadAllText(source));var root=original.RootElement;
            Check(root.GetProperty("completedMatches").GetInt32()==6&&root.GetProperty("wrappedLoadedWorlds").GetInt32()==1
                &&root.GetProperty("serverError").ValueKind==JsonValueKind.Null,"source runtime completed six matches and shutdown without server error");
            Check(GameFiles.Problem()==null,"existing private readonly asset paths resolve");
            MphRead.Mods.Headless.Enter();
            var rounds=JsonSerializer.Deserialize<List<Round>>(root.GetProperty("rounds").GetRawText())!;
            var lifetime=JsonSerializer.Deserialize<SceneLifetimeSnapshot[]>(root.GetProperty("lifetime").GetRawText())!;
            VerifyRetired(rounds,lifetime,root.GetProperty("dropped").GetInt32(),Path.Combine(Path.GetDirectoryName(source)!,"server-evidence.json"));
            var archives=InspectArchives(rounds);
            File.WriteAllText(output,JsonSerializer.Serialize(new{status="PASS",checks,source,sourceSha256=Hash(File.ReadAllBytes(source)),
                originalStatus=root.GetProperty("status").GetString(),originalError=root.GetProperty("error").GetString(),archives,
                scope="post-retirement validation only; original runtime failure/exit preserved",assemblySha256=Hash(File.ReadAllBytes(typeof(Scene).Assembly.Location))},new JsonSerializerOptions{WriteIndented=true})+"\n");
            Console.WriteLine($"RETIRED WORLD VALIDATION PASS {checks}; original runtime result preserved");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    private static object[] InspectArchives(List<Round> rounds)
    {
        var receipts=new List<object>();
        foreach(string path in Directory.GetFiles(ServerReplayRecorder.ReplayDirectory,"*"+DemoFile.Extension))
        {
            using var reader=DemoReader.Open(path,out var open)??throw new InvalidDataException("Canonical server archive did not open");Check(open==ReplayOpenResult.Success,"canonical server archive opens");
            var metadata=reader.Metadata!;using var capsule=ReplayWorldCheckpoint.FromBytes(metadata.WorldCheckpoint,metadata.BuildId);
            var decoder=new ReplayReplicaState();decoder.RestoreCheckpoint(capsule.ConstructionState());
            SessionStatePacket session=decoder.Configuration??throw new InvalidDataException("Canonical capsule has no session");
            Check(decoder.Match is { } match&&match.MatchId==session.MatchId&&match.AuthorityEpoch==session.AuthorityEpoch
                &&match.RoomKey==session.Match.RoomKey&&match.Mode==(byte)session.Match.Mode,"capsule Match and Configuration identities agree");
            foreach(var checkpoint in reader.Checkpoints)
            {
                using var saved=ReplayWorldCheckpoint.FromBytes(reader.ReadCheckpoint(checkpoint),metadata.BuildId);
                Check(saved.Frame==metadata.OriginRecordingFrame+checkpoint.Frame,"durable checkpoint preserves original recording clock");
            }
            int packets=0;while(reader.ReadNext()!=null)++packets;
            Check(metadata.Integrity==ReplayIntegrity.Healthy&&reader.LastResult==ReplayOpenResult.Success,"canonical chunks/footer/checkpoints verify completely");
            bool completed=metadata.Events.Any(e=>e.Type==ReplayEventType.MatchEnded);
            if(completed)
            {
                Round round=rounds.Single(r=>r.Match==session.MatchId&&r.Epoch==session.AuthorityEpoch);
                Check(metadata.RoomKey==round.Room&&session.Match==round.Frozen&&metadata.MapHash!=0&&metadata.WorldCheckpoint.Length>0
                    &&reader.DurationFrames>=60&&packets>0&&reader.Checkpoints.Count>0,"completed canonical archive binds real room/rules/generation and positive native timeline");
                // Run has fully joined. This existing passive owner restores the
                // authority's immutable capsule on this thread; no live Scene is
                // sampled or hashed from a foreign thread.
                using var replica=new PassiveReplayScene(path,new Vector2i(256,192));
                Check(replica.Session.LastWarning==null,"same-build canonical native world restored without compatibility fallback");
                var archived=replica.Scene.GameState;
                Check(replica.Scene.RoomId==Metadata.GetRoomByName(round.Room).Item2&&replica.Scene.Room!=null
                    &&archived.FriendlyFire==round.Frozen.FriendlyFire&&archived.AffinityWeapons==round.Frozen.AffinityWeapons
                    &&archived.SpawnProtection==round.Frozen.SpawnProtection&&archived.PointGoal==round.Frozen.PointGoal,
                    "post-retirement native capsule restores actual room and frozen initial rules");
            }
            receipts.Add(new{path,sha256=Hash(File.ReadAllBytes(path)),bytes=new FileInfo(path).Length,metadata.RoomKey,session.MatchId,session.AuthorityEpoch,
                metadata.MapHash,metadata.DurationFrames,integrity=metadata.Integrity.ToString(),completed,packets,checkpoints=reader.Checkpoints.Count,
                initialWorldSha256=Hash(metadata.WorldCheckpoint)});
        }
        Check(receipts.Count>=6,"canonical recorder finalized six completed matches");
        Check(rounds.All(r=>receipts.Any(x=>(ushort)x.GetType().GetProperty("MatchId")!.GetValue(x)! ==r.Match
            &&(bool)x.GetType().GetProperty("completed")!.GetValue(x)!)),"all six authoritative identities have completed replay witnesses");
        return receipts.ToArray();
    }
    private sealed class Peer:IDisposable
    {
        internal readonly uint Id;internal readonly bool Spectator;internal int Slot=-1;internal SessionStatePacket? State;
        internal RosterPacket Roster=RosterPacket.Create();internal readonly HashSet<MatchStartIdentity> Ready=new();
        internal int SnapshotCount,BootstrapCount,StaleReadySent;internal int ReadyCount=>Ready.Count;
        internal readonly Dictionary<ushort,int> Snapshots=new();
        private readonly NetTransport wire=new(0);private readonly IPEndPoint endpoint;private readonly Stopwatch clock=Stopwatch.StartNew();
        private double lastHello=-1,lastPing=-1,lastLoaded=-1;private MatchStartIdentity? loaded,old;
        internal Peer(IPEndPoint endpoint,uint id,bool spectator){this.endpoint=endpoint;Id=id;Spectator=spectator;wire.AnswerPingsImmediately();Hello();}
        private void Send(PacketType type,ReadOnlySpan<byte> bytes)=>wire.Send(endpoint,type,bytes);
        private void Hello(){var b=new byte[23];b[0]=NetConfig.ProtocolVersion;b[1]=255;BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(2),Id);b[22]=(byte)(Spectator?1:0);Send(PacketType.Hello,b);lastHello=clock.Elapsed.TotalSeconds;}
        internal void Poll()
        {
            double now=clock.Elapsed.TotalSeconds;if(Slot<0&&now-lastHello>=.5)Hello();
            if(Slot>=0&&now-lastPing>=.5){lastPing=now;Send(PacketType.Ping,ReadOnlySpan<byte>.Empty);}
            foreach(var p in wire.Drain())
            {
                if(p.Type==PacketType.Welcome)Slot=p.Payload[0];
                if(p.Type==PacketType.Refused)throw new InvalidOperationException("Actual world observer refused "+RefusedPacket.Read(p.Payload).Describe("loopback"));
                if(p.Type==PacketType.SessionState&&SessionStatePacket.TryRead(p.Payload,out var s)
                    &&(State==null||s.Revision==State.Value.Revision||SessionStatePacket.IsNewer(s.Revision,State.Value.Revision)))State=s;
                if(p.Type==PacketType.Roster&&RosterPacket.TryRead(p.Payload,out var roster)
                    &&(roster.Revision==Roster.Revision||NetLifecycleTracker.Newer(roster.Revision,Roster.Revision)))Roster=roster;
                if(p.Type is PacketType.Snapshot or PacketType.SnapshotFast&&p.Payload.Length>=SnapshotHeader.Size)
                {++SnapshotCount;ushort match=SnapshotHeader.Read(p.Payload).MatchId;Snapshots[match]=Snapshots.GetValueOrDefault(match)+1;}
                if(p.Type==PacketType.WorldBootstrap&&WorldBootstrapIdentity.TryRead(p.Payload,out var bootstrap))
                {var b=new byte[WorldBootstrapIdentity.Size];bootstrap.Write(b);Send(PacketType.WorldReady,b);++BootstrapCount;}
                if(p.Type==PacketType.WorldReady&&WorldBootstrapIdentity.TryRead(p.Payload,out var ready))Ready.Add(ready.Start);
            }
            if(State is { } state&&Slot>=0)
            {
                var identity=new MatchStartIdentity(state.MatchId,state.AuthorityEpoch,state.StartGeneration);
                if(loaded!=identity)
                {
                    old=loaded;loaded=identity;lastLoaded=-1;
                    if(old is { } stale){var b=new byte[MatchLoadedPacket.Size];new MatchLoadedPacket(stale.MatchId,stale.AuthorityEpoch,stale.StartGeneration).Write(b);Send(PacketType.MatchLoaded,b);++StaleReadySent;}
                }
                if(!Ready.Contains(identity)&&state.Phase is SessionPhase.Starting or SessionPhase.InMatch&&now-lastLoaded>=.3)
                {lastLoaded=now;var b=new byte[MatchLoadedRolePacket.Size];new MatchLoadedRolePacket(identity.MatchId,identity.AuthorityEpoch,identity.StartGeneration,Spectator).Write(b);Send(PacketType.MatchLoaded,b);}
            }
        }
        public void Dispose(){Send(PacketType.Bye,ReadOnlySpan<byte>.Empty);wire.Dispose();}
    }
}
