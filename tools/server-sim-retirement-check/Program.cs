using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Diagnostics;
using MphRead.Mods.Launcher;
using MphRead.Mods.MapGen;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

internal static class Program
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);++checks;}
    private static Scene Scene(ServerSim sim)=>(Scene)typeof(ServerSim).GetField("_scene",Hidden)!.GetValue(sim)!;
    private static readonly string[] Rooms={"MP1 SANCTORUS","MP2 HARVESTER","MP3 PROVING GROUND","MP5 FUEL SLUICE","MP7 PROCESSOR CORE","MP9 CRYOCHASM"};
    private static bool Start(ServerSim sim,string room,ushort match)
    {
        var roster=RosterPacket.Create();roster.MatchId=match;roster.AuthorityEpoch=8001;roster.Revision=1;roster.SessionRevision=1;roster.Count=2;
        for(int i=0;i<2;++i){roster.Slots[i]=(byte)i;roster.Generations[i]=1;roster.Hunters[i]=(byte)i;roster.Teams[i]=-1;roster.Names[i]="OWNED FIXTURE";}
        var definition=new MatchDefinition{RoomKey=room,Mode=GameMode.Battle,TimeLimitSeconds=0,PointGoal=0,SpawnProtection=true,FriendlyFire=true};
        var session=new SessionStatePacket{MatchId=match,AuthorityEpoch=8001,Revision=1,Phase=SessionPhase.InMatch,Policy=ServerSessionPolicy.Continuous,
            MaxPlayers=8,Match=definition,WorldProfile=MatchWorldProfile.Resolve(8)};
        return sim.Start(room,GameMode.Battle,8,(ReadOnlySpan<byte> _)=>{},()=>{},roster,session);
    }
    private static void Complete(Scene scene,SceneLifetimeDiagnostics.Observation observation)
    {
        long generation=scene.LifetimeGeneration;
        foreach(var stage in new[]{SceneLifetimeStage.CleanupStarted,SceneLifetimeStage.CleanupComplete,SceneLifetimeStage.VisualResourcesReleased,SceneLifetimeStage.CompatibilityReleased})
            Check(observation.Snapshots.Any(s=>s.Generation==generation&&s.Stage==stage),"exact authority generation reaches "+stage);
        Check(!ReferenceEquals(GameState.Current,scene.GameState)&&!ReferenceEquals(PlayerEntity.LegacyRegistry,scene.Players)
            &&!ReferenceEquals(Rng.Current,scene.Random),"eligible retired world releases exact compatibility facades");
        Check(!MapRuntimeUsage.IsInUse(Metadata.GetRoomById(scene.RoomId)!.Name),"retired world releases package reader lease");
    }
    private static int Main(string[] args)
    {
        using var observation=SceneLifetimeDiagnostics.Begin(4096);Exception? failure=null;
        try
        {
            string userdata=Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA")??"";
            Check(Path.IsPathFullyQualified(userdata)&&userdata==Directory.GetCurrentDirectory()
                &&userdata==MphRead.Mods.Platform.AppPaths.UserDataDirectory
                &&!userdata.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library/Application Support"),StringComparison.Ordinal),"explicit private absolute cwd/userdata boundary");
            Check(GameFiles.Problem()==null,"private readonly actual assets available");Headless.Enter();
            var sim=new ServerSim();
            for(ushort i=0;i<Rooms.Length;++i)
            {
                Check(Start(sim,Rooms[i],(ushort)(100+i)),"actual native authority starts room "+i);var scene=Scene(sim);
                sim.Step();Check(sim.Frames==1&&sim.StepFailures==0,"one actual native step; no pacing/campaign claim");
                sim.Stop(preserveRoomPrewarm:i%2==0);Complete(scene,observation);
                int count=observation.Snapshots.Count;sim.Stop();Check(observation.Snapshots.Count==count,"repeated Stop is owner-idempotent");
                Check(!sim.Running&&!NetSession.Active&&GameState.Current.Owner==null,"ordinary stopped authority leaves no live runtime/root");
            }
            var partial=new ServerSim();int before=observation.Snapshots.Count;
            Check(!Start(partial,"CPP05 OWNED MISSING ROOM",200),"real room construction failure returns false");
            var created=observation.Snapshots.Skip(before).First(s=>s.Role==SceneLifetimeRole.HeadlessLive&&s.Stage==SceneLifetimeStage.Constructed);
            Check(observation.Snapshots.Any(s=>s.Generation==created.Generation&&s.Stage==SceneLifetimeStage.CleanupComplete)
                &&observation.Snapshots.Any(s=>s.Generation==created.Generation&&s.Stage==SceneLifetimeStage.VisualResourcesReleased),"partial construction candidate is actually cleaned and visually retired");
            Check(!partial.Running&&!NetSession.Active&&GameState.Current.Owner==null,"partial construction retires its own authority/facades");

            // StreamWriter opening a directory throws UnauthorizedAccessException
            // on macOS. This is a real existing post-publication initialization
            // failure, not a production test flag or callback injection seam.
            string blockedNetlog=Path.Combine(Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA")!,"netlog-server.txt");
            Directory.CreateDirectory(blockedNetlog);
            var debugWriter=typeof(DebugLog).GetField("_writer",BindingFlags.Static|BindingFlags.NonPublic)!;
            object? previousWriter=debugWriter.GetValue(null);
            using(var diagnostic=new StreamWriter(Path.Combine(Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA")!,"owned-startup-diagnostic.txt")))
            {
                debugWriter.SetValue(null,diagnostic);
                try
                {
                    var authorityFailure=new ServerSim();int sceneCount=observation.Snapshots.Count;
                    Check(!Start(authorityFailure,Rooms[0],201),"real post-publication diagnostic failure returns false");
                    Check(!authorityFailure.Running&&!NetSession.Active&&!NetSession.IsAuthority,"failed authority publication retires exact owned runtime before Scene construction");
                    Check(observation.Snapshots.Count==sceneCount,"post-publication failure precedes any Scene construction");
                }
                finally{debugWriter.SetValue(null,previousWriter);Directory.Delete(blockedNetlog);}
            }

            var old=new ServerSim();var newer=new ServerSim();Check(Start(old,Rooms[0],300),"old owner starts");var oldScene=Scene(old);
            Check(Start(newer,Rooms[1],301),"replacement owner starts");var current=Scene(newer);old.Stop();
            Check(newer.Running&&NetSession.IsServer&&NetSession.IsAuthority&&NetSession.CurrentMatchId==301
                &&ReferenceEquals(GameState.Current,current.GameState)&&ReferenceEquals(PlayerEntity.LegacyRegistry,current.Players)
                &&ReferenceEquals(Rng.Current,current.Random),"stale Stop cannot retire replacement authority or facades");
            Check(observation.Snapshots.Any(s=>s.Generation==oldScene.LifetimeGeneration&&s.Stage==SceneLifetimeStage.CleanupComplete)
                &&observation.Snapshots.Any(s=>s.Generation==oldScene.LifetimeGeneration&&s.Stage==SceneLifetimeStage.VisualResourcesReleased)
                &&!observation.Snapshots.Any(s=>s.Generation==oldScene.LifetimeGeneration&&s.Stage==SceneLifetimeStage.CompatibilityReleased),"stale candidate still cleans itself without releasing replacement facades");
            newer.Step();Check(newer.StepFailures==0&&newer.Frames==1,"replacement world still steps after stale Stop");newer.Stop();

            // A real CTS callback can throw during cleanup. It also publishes a
            // replacement on this SAME owner thread, testing exception ordering
            // and exact lifecycle incarnation without any synthetic NetSession.
            var failing=new ServerSim();var replacement=new ServerSim();Check(Start(failing,Rooms[2],400),"throwing cleanup owner starts");var failingScene=Scene(failing);
            var cts=(CancellationTokenSource)failingScene.Room!.GetType().GetField("_cts",Hidden)!.GetValue(failingScene.Room)!;
            using var callback=cts.Token.Register(()=>{if(!Start(replacement,Rooms[3],401))throw new Exception("replacement fixture did not start");throw new InvalidOperationException("controlled owned cancellation failure");});
            bool threw=false;try{failing.Stop();}catch(Exception error){threw=error.ToString().Contains("controlled owned cancellation failure",StringComparison.Ordinal);}
            Check(threw,"original owned cleanup exception remains visible");var replacementScene=Scene(replacement);
            Check(!failing.Running&&observation.Snapshots.Any(s=>s.Generation==failingScene.LifetimeGeneration&&s.Stage==SceneLifetimeStage.VisualResourcesReleased),"throwing cleanup still detaches exact old runtime and attempts visual release");
            Check(replacement.Running&&NetSession.IsServer&&NetSession.IsAuthority&&NetSession.CurrentMatchId==401
                &&ReferenceEquals(GameState.Current,replacementScene.GameState),"throwing stale cleanup preserves newly published replacement authority");
            Check(!observation.Snapshots.Any(s=>s.Generation==failingScene.LifetimeGeneration&&s.Stage==SceneLifetimeStage.CompatibilityReleased),"partial failed cleanup is never falsely marked facade-eligible");
            replacement.Step();Check(replacement.StepFailures==0,"replacement survives old cleanup exception");replacement.Stop();

            var reentrant=new ServerSim();Check(Start(reentrant,Rooms[4],500),"same-instance cleanup owner starts");var detached=Scene(reentrant);
            var reentrantCts=(CancellationTokenSource)detached.Room!.GetType().GetField("_cts",Hidden)!.GetValue(detached.Room)!;
            using var reentrantCallback=reentrantCts.Token.Register(()=>{if(!Start(reentrant,Rooms[5],501))throw new Exception("same-instance replacement did not start");});
            reentrant.Stop();var reentrantScene=Scene(reentrant);
            Check(reentrant.Running&&!ReferenceEquals(reentrantScene,detached)&&NetSession.CurrentMatchId==501
                &&ReferenceEquals(GameState.Current,reentrantScene.GameState),"same-instance callback replacement remains owned after outer Stop");
            reentrant.Step();Check(reentrant.Frames==1&&reentrant.StepFailures==0,"same-instance callback replacement still steps");reentrant.Stop();
            Check(!Directory.Exists("Savedata"),"no-save server teardown writes no story save");
            Console.WriteLine($"PASS {checks} actual asset-backed ServerSim retirement checks;6 real room Start/Stop cycles, partial construction, idempotence, replacement and cleanup exception; no UDP/GL/backend/physical devices");return 0;
        }
        catch(Exception error){failure=error;Console.Error.WriteLine(error);return 1;}
        finally{NetSession.Stop();File.WriteAllText(args[0],JsonSerializer.Serialize(new{status=failure==null?"PASS":"FAIL",error=failure?.ToString(),checks,lifetime=observation.Snapshots},new JsonSerializerOptions{WriteIndented=true})+"\n");}
    }
}
