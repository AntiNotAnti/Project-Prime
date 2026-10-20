using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Network;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;

// Synthetic accepted native archive -> actual writer/reader/replica and
// actual transport seek. Never a live damage/source-proof or GL acceptance.
// Headless remains true throughout actual replica simulation. Only the private
// cosmetic CPU dispatch/sample seam temporarily enables its graphical-role gate.
internal static class HeadHitReplayChecks
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private const string Room="MP3 PROVING GROUND";
    private const ushort Match=7,Generation=2;
    private const ulong Epoch=0x0102030405060708;
    private const uint HitAt=12,NextHitAt=44,Last=70;
    private static readonly MethodInfo Dispatch=typeof(Scene).GetMethod("StepHeadHitPresentation",Hidden)!;
    private static readonly PropertyInfo HeadlessFlag=typeof(Headless).GetProperty(nameof(Headless.Active))!;
    private static int _checks;
    private static void Check(bool value,string label)
    {if(!value)throw new InvalidOperationException("Head-hit CPU replay: "+label);_checks++;Console.WriteLine("HEAD HIT REPLAY PASS "+label);}
    private static byte[] Packet(PacketType type,int size,Action<byte[]> write)
    {var body=new byte[size];write(body);return new[]{(byte)type}.Concat(body).ToArray();}
    private static ushort Life(uint frame)=>(ushort)(frame>=40?2:1);
    private static byte[] Snapshot(uint frame,bool facts)
    {
        int healthAt=SnapshotHeader.Size+2*PlayerState.Size+NetMatchTimeSync.Size;
        return Packet(PacketType.Snapshot,healthAt+NetHealthSync.HeaderSize,body=>
        {
            new SnapshotHeader{MatchId=Match,AuthorityEpoch=Epoch,PlayerCount=2,Frame=1000+frame}.Write(body);
            for(byte slot=0;slot<2;slot++)
            {
                ushort hp=(ushort)(slot==1&&facts&&(frame>=HitAt&&frame<40||frame>=NextHitAt)?50:200);
                new PlayerState{SlotIndex=slot,SlotGeneration=Generation,LifeId=Life(frame),Health=hp,
                    DamageEventId=(ushort)(slot==1&&facts?(frame>=NextHitAt?2:frame>=HitAt&&frame<40?1:0):0),
                    Flags=PlayerState.FlagActive|PlayerState.FlagSpawned,Position=new(2+slot*4,5,8),Facing=Vector3.UnitZ,
                    Form=new PlayerFormState{Frame=1000+frame,StartedFrame=1000,Phase=FormPhase.Biped,BodyHealth=hp}}
                    .Write(body.AsSpan(SnapshotHeader.Size+slot*PlayerState.Size));
            }
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(healthAt),Match);
        });
    }
    private static ReplayShotFact Fact(uint resolved,ushort victimLife,uint shot,ushort damage,bool lethal=false)
        =>new(Match,Epoch,1000+resolved,1000+resolved,shot,damage,0,Generation,1,1,Generation,victimLife,
            (byte)BeamType.Imperialist,ReplayShotFactFlags.Headshot|ReplayShotFactFlags.Direct|(lethal?ReplayShotFactFlags.Lethal:0),
            150,(ushort)(lethal?0:50),0,0,new(6,6,8));
    private static string Write(string output,bool facts)
    {
        string path=Path.Combine(output,facts?"accepted-nonlethal-synthetic.ppdemo":"no-head-facts-synthetic.ppdemo");
        if(File.Exists(path))throw new IOException("Choose a new output directory; archive attempts are retained.");
        var match=new MatchStatePacket{MatchId=Match,AuthorityEpoch=Epoch,RoomKey=Room,NextRoomKey="",Mode=(byte)GameMode.Battle,
            PlayerCount=2,TimeRemaining=-1};
        var session=new SessionStatePacket{MatchId=Match,AuthorityEpoch=Epoch,MaxPlayers=8,Phase=SessionPhase.InMatch,
            WorldProfile=MatchWorldProfile.Resolve(2),Match=new MatchDefinition{RoomKey=Room,Mode=GameMode.Battle,TimeLimitSeconds=0,PointGoal=999}};
        var roster=RosterPacket.Create();roster.MatchId=Match;roster.AuthorityEpoch=Epoch;roster.Revision=1;roster.Count=2;
        for(byte slot=0;slot<2;slot++){roster.Slots[slot]=slot;roster.Generations[slot]=Generation;roster.Hunters[slot]=(byte)Hunter.Samus;roster.Teams[slot]=-1;roster.Names[slot]="Synthetic head-hit "+slot;}
        var bootstrap=new[]{Packet(PacketType.MatchState,MatchStatePacket.Size,b=>match.Write(b)),
            Packet(PacketType.SessionState,SessionStatePacket.Size,b=>session.Write(b)),Packet(PacketType.Roster,RosterPacket.Size,b=>roster.Write(b)),Snapshot(0,facts)};
        using(var writer=new ReplayWriterV3(path,new ReplayMetadata{FormatVersion=3,ProtocolVersion=NetConfig.ProtocolVersion,TickRate=60,
            RoomKey=Room,Mode=GameMode.Battle,MapHash=ReplayMapIdentity.Compute(Room),BuildId="synthetic-cpu-head-hit-fixture",
            BuildVersion="synthetic",RecordedAtUtc=new(2026,10,20,0,0,0,DateTimeKind.Utc),Bootstrap=new(){Packets=bootstrap}}))
        {
            for(uint frame=0;frame<=Last;frame++)
            {
                writer.WriteRecord(frame,Snapshot(frame,facts));
                if(facts&&(frame==HitAt+3||frame==NextHitAt+3||frame==46))
                {
                    // Delivered3ticks later than resolve; the real reader maps
                    // using archived authority snapshots, not fixture retagging.
                    var fact=frame==HitAt+3?Fact(HitAt,1,101,1):frame==46?Fact(46,1,103,3,true):Fact(NextHitAt,2,102,2);
                    var packet=Packet(PacketType.ReplayShotFact,ReplayShotFactPacket.Size,b=>ReplayShotFactPacket.Write(fact,b));
                    Check(ReplayShotFactPacket.TryRead(packet.AsSpan(1),out var decoded)&&decoded==fact,"writer fact is valid exact native accepted payload");
                    writer.WriteRecord(frame,packet);
                    if(frame==HitAt+3)writer.WriteRecord(frame,packet); // actual archive retransmit
                }
            }
            ReplayTimelineArchive.EndFrame(writer,Last+1);
        }
        return path;
    }
    private static HeadHitPose CosmeticDispatch(PassiveReplayScene replay,bool repeat=true)
    {
        Check(Headless.Active,"actual replica step completed with Headless still enabled");
        var scene=replay.Scene;var victim=scene.Players.Items[1];
        var before=replay.State.CaptureCheckpoint().Bytes.ToArray();int hp=victim.Health;
        var rng=(scene.Random.Rng1,scene.Random.Rng2);ulong frame=scene.FrameCount;
        var actor=(victim.Position,victim.Speed,victim.ModGunVector,victim.CameraInfo.Target);
        var models=(ModelInstance[])typeof(PlayerEntity).GetField("_bipedModelLods",Hidden)!.GetValue(victim)!;
        var bones=models.Select(m=>m.Model.Nodes.Select(n=>n.Animation).ToArray()).ToArray();
        HeadHitPose pose;
        try
        {
            HeadlessFlag.SetValue(null,false);Dispatch.Invoke(scene,null);pose=scene.ModHeadHitPose(victim);
            for(int n=0;repeat&&n<8;n++)
            {Dispatch.Invoke(scene,null);Check(scene.ModHeadHitPose(victim)==pose,"same recording-frame cosmetic redispatch/sample advances0");}
        }
        finally{HeadlessFlag.SetValue(null,true);}
        Check(victim.Health==hp&&frame==scene.FrameCount&&(scene.Random.Rng1,scene.Random.Rng2)==rng
            &&(victim.Position,victim.Speed,victim.ModGunVector,victim.CameraInfo.Target)==actor
            &&replay.State.CaptureCheckpoint().Bytes.SequenceEqual(before),"CPU cosmetic seam preserves actual actor health/pose/RNG/authority bytes");
        Check(models.Select((m,i)=>m.Model.Nodes.Select(n=>n.Animation).SequenceEqual(bones[i])).All(b=>b),
            "CPU cosmetic dispatch changes no native matrix before actual draw");
        return pose;
    }
    internal static int Run(string data,string output)
    {
        string cwd=Directory.GetCurrentDirectory();bool oldHeadless=Headless.Active,oldCosmetics=RenderOptions.ShowCustomCosmetics;
        var quality=RenderOptions.CosmeticQuality;var hud=HudProfiles.CopyCurrent();int cap=FrameTiming.FrameRateCap;
        _checks=0;
        try
        {
            data=Path.GetFullPath(data);output=Path.GetFullPath(output);Directory.CreateDirectory(output);
            Directory.SetCurrentDirectory(data);Paths.UpdatePaths();Paths.ChooseMphPath();
            Check(!NetSession.Active,"isolated test owns no authenticated live session");Headless.Enter();
            RenderOptions.ShowCustomCosmetics=true;RenderOptions.CosmeticQuality=CosmeticEffectQuality.High;
            var visible=hud.DeepClone();visible.ReduceMotion=false;HudProfiles.Publish(visible);
            FrameTiming.FrameRateCap=60;FrameTiming.Reset();
            string path=Write(output,true),fallback=Write(output,false);var digest=SHA256.HashData(File.ReadAllBytes(path));
            var fallbackDigest=SHA256.HashData(File.ReadAllBytes(fallback));
            var poses=new Dictionary<uint,HeadHitPose>();
            using(var replay=new PassiveReplayScene(path,new(256,192)))
            {
                while(replay.Step())
                {
                    uint frame=replay.Session.RecordingFrame;var pose=CosmeticDispatch(replay);poses[frame]=pose;
                    Check(frame<=Last+1,"linear archive traversal bounded by fixture end");
                    if(frame>=HitAt&&frame<HitAt+18)
                    {
                        int age=(int)(frame-HitAt);float expected=age<5?age/5f:(18-age)/13f;expected=expected*expected*(3-2*expected);
                        Check(MathF.Abs(pose.Amount-expected)<.00001f,"actual fact reader retains linear canonical age "+age);
                    }
                    if(frame<HitAt||frame>=HitAt+18&&frame<NextHitAt)Check(pose==default,"no event/expired/respawn window is neutral");
                    if(frame==HitAt+5)Check(pose.Amount==1&&replay.Scene.Players.Items[1].Health==50,"canonical150/200→50 nonlethal fact reaches peak without extra health removal");
                    if(frame==NextHitAt+5)Check(pose.Amount==1,"old-life lethal guard does not cancel new-life accepted head hit");
                }
                Check(replay.Scene.ReplayPoses!.ResolvedShotFactsAt(HitAt).Any(f=>f.ResolveTick==1000+HitAt&&f.HealthAfter==50),
                    "real recorded fact reader maps authority clock onto original resolve recording-frame");
            }
            using(var player=new PassiveReplayPlayer(path,new(256,192),new(EnableSeeking:true,EnableMemoryCheckpoints:false,EnableDurableCheckpoints:false)))
            {
                player.Stepped+=_=>CosmeticDispatch(player.Current,repeat:false);
                foreach(uint target in new uint[]{17,28,5,17,49,17,30,49})
                {
                    long generation=player.Transport.SeekGeneration;var priorScene=player.Current.Scene;player.Seek(target);int updates=0;
                    while(player.Transport.IsSeeking)
                    {Check(Headless.Active,"seek simulation always remains Headless");int n=player.Update();Check(n<=PassiveReplayPlayer.MaximumStepsPerUpdate&&++updates<100,"actual forward/backward seek is bounded");}
                    Check(player.Current.Session.RecordingFrame==target,"actual media seek reaches exact recording-frame "+target);
                    Check(player.Transport.SeekGeneration!=generation||!ReferenceEquals(player.Current.Scene,priorScene),"actual seek has new presentation generation");
                    Check(CosmeticDispatch(player.Current)==poses[target],"new seek generation reconstructs only last18 accepted recording frames "+target);
                }
            }
            using(var replay=new PassiveReplayScene(fallback,new(256,192)))
                while(replay.Step())Check(CosmeticDispatch(replay,repeat:false)==default,"archive without accepted head facts keeps native fallback");
            Check(SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(digest)&&SHA256.HashData(File.ReadAllBytes(fallback)).SequenceEqual(fallbackDigest),
                "linear/seek/fallback never writes or changes either source archive");
            File.WriteAllText(Path.Combine(output,"head-hit-replay-results.json"),JsonSerializer.Serialize(new{checks=_checks,
                gameDllSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Scene).Assembly.Location))),
                path,sourceSha256=Convert.ToHexStringLower(digest),fallback,frames=poses.Count,
                scope="Synthetic accepted native archive -> actual decoder/PassiveReplayScene/transport; explicit cosmetic CPU dispatch only; no live damage, GL, custom replay or historical protocol compatibility claim"},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"PASS: {_checks} CPU head-hit replica/fact/seek checks; no GL claim");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally
        {HeadlessFlag.SetValue(null,oldHeadless);RenderOptions.ShowCustomCosmetics=oldCosmetics;RenderOptions.CosmeticQuality=quality;
            HudProfiles.Publish(hud);FrameTiming.FrameRateCap=cap;FrameTiming.Reset();Directory.SetCurrentDirectory(cwd);}
    }
}
