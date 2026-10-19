using MphRead;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;

/// <summary>Private synthetic files only. Never invokes an OS picker, scene, network, encoder or GL.</summary>
internal static class ReplayProjectFilesAcceptance
{
    internal const string ChildArgument="--replay-project-files-isolated";
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"prime-project-files-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string executable=Environment.ProcessPath??throw new InvalidOperationException("Missing test executable.");
            var start=new ProcessStartInfo(executable){WorkingDirectory=root,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
            if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(ReplayProjectFilesAcceptance).Assembly.Location);
            start.ArgumentList.Add(ChildArgument);start.Environment["PROJECT_PRIME_USER_DATA"]=root;start.Environment["PROJECT_PRIME_PROJECT_FILES_ROOT"]=root;
            using var child=Process.Start(start)??throw new InvalidOperationException("Project fixture did not start.");
            var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();
            if(!child.WaitForExit(45000)){child.Kill(true);child.WaitForExit();throw new TimeoutException("Project fixture timed out.");}
            Console.Write(output.GetAwaiter().GetResult());if(child.ExitCode!=0)throw new InvalidOperationException("Project fixture failed: "+error.GetAwaiter().GetResult());
        }
        finally{Directory.Delete(root,true);}
    }
    internal static void RunIsolated()
    {
        string root=Environment.GetEnvironmentVariable("PROJECT_PRIME_PROJECT_FILES_ROOT")??"";
        if(!Path.IsPathFullyQualified(root)||Path.GetFileName(root).StartsWith("prime-project-files-",StringComparison.Ordinal)!=true
            ||Directory.GetFileSystemEntries(root).Length!=0)throw new InvalidOperationException("Fresh private fixture directory required.");
        Directory.SetCurrentDirectory(root);root=Directory.GetCurrentDirectory();Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",root);
        if(!Path.GetFullPath(DemoLibrary.Directory).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.Ordinal))throw new InvalidOperationException("Replay library escaped private test root.");
        int checks=0;void Check(bool ok,string label){if(!ok)throw new InvalidOperationException("PROJECT FILES: "+label);checks++;Console.WriteLine("PROJECT FILES PASS "+label);}
        var assembly=typeof(RmlUiTheatreIndex).Assembly;
        var saveType=assembly.GetType("MphRead.Mods.Launcher.RmlUi.Render.RmlUiReplayProjectSave",true)!;
        var prepare=saveType.GetMethod("Prepare",BindingFlags.Static|BindingFlags.NonPublic)!;
        var importer=assembly.GetType("MphRead.Mods.Launcher.RmlUi.Render.RmlUiReplayProjectLibrary",true)!.GetMethod("ImportAsync",BindingFlags.Static|BindingFlags.NonPublic)!;
        string outside=Path.Combine(root,"external");Directory.CreateDirectory(outside);
        string source=Path.Combine(outside,"recording.ppdemo");CreateRecording(source);byte[] original=File.ReadAllBytes(source);
        var annotations=new ReplayAnnotationDocument
        {
            Tags=new(){"practice"},Collections=new(){"project-fixture"},
            Bookmarks=new(){new(Guid.NewGuid(),40,"Middle",DateTime.UtcNow),new(Guid.NewGuid(),5,"Before",DateTime.UtcNow)},
            Highlights=new(){new(Guid.NewGuid(),10,50,"Crossing",DateTime.UtcNow)}
        };
        var reel=new ReplayReelDocument{SourceReplay=source,Segments=new(){new(Guid.NewGuid(),30,90,"Reel",ReplaySegmentCamera.Director)}};
        var camera=new ReplayCameraTrack();
        Check(camera.Put(new(0,Vector3.Zero,Quaternion.Identity,MathHelper.DegreesToRadians(60)))
            &&camera.Put(new(60,Vector3.One,Quaternion.Identity,MathHelper.DegreesToRadians(75))),
            "synthetic camera fixture accepts normalized rotations and radian FOVs");
        byte[] sourceHash=SHA256.HashData(original);
        string sourceWitness="fixture:SOURCE-SHA:"+Convert.ToHexString(sourceHash)+":PLAYBACK-SHA:"+Convert.ToHexString(sourceHash)+":RANGE:0:0";
        var audioSettings=new TheatreStudioAudioSettings(Enabled:false,GameEvents:false,CombatFeedback:true,GameGain:.4f,CombatGain:.6f,ReplayGain:.7f,MusicGain:.25f);
        var audioReview=ReplayStudioAudioSettingsStore.Read(source,sourceWitness);
        ReplayStudioAudioSettingsStore.Save(audioReview,sourceWitness,audioSettings,()=>{});
        var presentationSettings=new ReplayStudioPresentationPreferences(ReplayCameraMode.Overview,3,95,false,true,true,false,true);
        var presentationReview=ReplayStudioPresentationPreferencesStore.Read(source,sourceWitness);
        ReplayStudioPresentationPreferencesStore.SaveAsync(presentationReview,presentationSettings,()=>{}).GetAwaiter().GetResult();
        var transfer=assembly.GetType("MphRead.Mods.Launcher.RmlUi.Render.RmlUiReplayProjectPreferences",true)!
            .GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic)!;
        object? preferences=Invoke(transfer,null,source,sourceHash,(uint)0,(uint)0,CancellationToken.None);
        string saved=Path.Combine(outside,"selection.ppclip");
        using(var ticket=Prepare(saved,CancellationToken.None))
        {
            Check(!File.Exists(saved)&&!File.Exists(saved+ReplayAnnotations.Extension),"detached Save As exposes no project before owner publication");
            Commit(ticket);
            Check(ReplayVirtualClips.TryLoad(saved,out var clip)&&clip is{StartFrame:20,EndFrame:80}&&clip.SourceReplay==source,"Save As keeps old .ppclip schema and durable recording range");
            var rebased=ReplayAnnotations.ReadReviewed(ReplayReviewedMetadata.Read(saved+ReplayAnnotations.Extension),60);
            Check(rebased.Bookmarks.Count==1&&rebased.Bookmarks[0].Frame==20&&rebased.Highlights[0].StartFrame==0&&rebased.Highlights[0].EndFrame==30
                &&rebased.Tags.SequenceEqual(new[]{"practice"}),"Save As crops and rebases bookmarks/highlights while retaining organization");
            var savedReel=ReplayReels.ReadReviewed(ReplayReviewedMetadata.Read(saved+ReplayReels.Extension),saved,60);
            Check(savedReel.Segments.Single() is{StartFrame:10,EndFrame:60},"Save As rebases intersecting reel and binds new logical project");
            var savedCamera=new ReplayCameraTrack();bool loaded=savedCamera.Load(saved);
            Check(loaded&&savedCamera.Keys.Count>0,"Save As camera loads with destination and source-range identity: "+savedCamera.LastError);
            foreach(uint relative in new uint[]{0,20,60})
            {
                Check(camera.Sample(relative+20,out var expected)&&savedCamera.Sample(relative,out var actual)
                    &&Vector3.Distance(expected.Position,actual.Position)<0.0001f
                    &&Math.Abs(expected.Fov-actual.Fov)<0.0001f,
                    "Save As camera preserves original evaluator at rebased frame "+relative);
            }
            string wrongIdentity=Path.Combine(outside,"other-identity.ppclip");File.Copy(saved,wrongIdentity);
            File.Copy(saved+".camera",wrongIdentity+".camera");
            var rejectedCamera=new ReplayCameraTrack();
            Check(!rejectedCamera.Load(wrongIdentity)&&rejectedCamera.LastError?.Contains("different replay or clip range",StringComparison.Ordinal)==true,
                "persisted camera rejects another logical project identity with identical source/range");
            using var document=JsonDocument.Parse(File.ReadAllText(saved));
            Check(document.RootElement.GetProperty("SourceContentHash").GetString()==Convert.ToHexString(SHA256.HashData(original)),"saved project binds exact source bytes for old Studio compatibility");
            CheckPreferences(saved,20,80,"Save As");
            Throws(()=>Commit(ticket),"duplicate Save As publication is refused");
        }
        Check(File.ReadAllBytes(source).SequenceEqual(original),"Save As preserves original recording bytes");
        string abandoned=Path.Combine(outside,"abandoned.ppclip");using(var ticket=Prepare(abandoned,CancellationToken.None)){}
        Check(!File.Exists(abandoned)&&!Directory.EnumerateDirectories(outside,".prime-project-*").Any(),"discarded detached ticket removes only staging files");
        using(var cancel=new CancellationTokenSource())
        {cancel.Cancel();Throws(()=>{using var ticket=Prepare(Path.Combine(outside,"canceled.ppclip"),cancel.Token);},"canceled preparation leaves no project");}
        string collision=Path.Combine(outside,"collision.ppclip");
        using(var ticket=Prepare(collision,CancellationToken.None))
        {File.WriteAllText(collision,"preserve me");Throws(()=>Commit(ticket),"destination appearing after review prevents publication");Check(File.ReadAllText(collision)=="preserve me"&&!File.Exists(collision+".camera"),"publication collision preserves existing bytes and creates no sidecars");}
        string imported=Import(saved,CancellationToken.None);
        Check(ReplayVirtualClips.TryLoad(imported,out var importedClip)&&importedClip!=null&&Path.GetDirectoryName(importedClip.SourceReplay)==DemoLibrary.Directory
            &&File.ReadAllBytes(importedClip.SourceReplay).SequenceEqual(original),"external .ppclip Open imports and validates durable source into retained library");
        Check(ReplayReels.ReadReviewed(ReplayReviewedMetadata.Read(imported+ReplayReels.Extension),imported,60).Segments.Count==1
            &&new ReplayCameraTrack().Load(imported),"project import rebinds reel and camera to imported logical identity");
        CheckPreferences(imported,20,80,"project import");
        Check(Import(imported,CancellationToken.None)==imported,"already local project keeps its stable library identity");
        Write(source+ReplayAnnotations.Extension,annotations);Write(source+ReplayReels.Extension,reel);
        Check(camera.Save(source),"synthetic raw recording owns a readable camera sidecar");
        string raw=Import(source,CancellationToken.None);
        Check(raw.EndsWith(".ppdemo",StringComparison.OrdinalIgnoreCase)&&Path.GetDirectoryName(raw)==DemoLibrary.Directory&&File.ReadAllBytes(raw).SequenceEqual(original),"normal .ppdemo Open copies validated recording into retained library");
        Check(ReplayReels.ReadReviewed(ReplayReviewedMetadata.Read(raw+ReplayReels.Extension),raw,120).Segments.Count==1&&new ReplayCameraTrack().Load(raw),"normal recording import preserves and rebinds authored sidecars");
        CheckPreferences(raw,0,0,"recording import");
        Check(Import(raw,CancellationToken.None)==raw,"already local recording reuses accepted identity");
        string bad=Path.Combine(outside,"bad.ppclip");Write(bad,new{Version=1,SourceReplay=source,StartFrame=0,EndFrame=999999,CreatedUtc=DateTime.UtcNow,Name="Invalid"});
        int before=Directory.GetFiles(DemoLibrary.Directory).Length;
        Throws(()=>Import(bad,CancellationToken.None),"project range beyond synthetic source is rejected");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();Throws(()=>Import(source,cancel.Token),"canceled Open cannot publish library files");}
        Check(before==Directory.GetFiles(DemoLibrary.Directory).Length,"rejected or canceled Open leaves library inventory unchanged");
        Check(RmlUiReplayFileNames.IsPortableBundle("REPLAY.PPREPLAY.ZIP")&&RmlUiReplayFileNames.IsPortableBundle("replay.ppreplay")
            &&!RmlUiReplayFileNames.IsPortableBundle("replay.zip")&&RmlUiReplayFileNames.PortableBundleName=="replay.ppreplay.zip","portable extension policy retains old default and newer compatibility");
        string moveSource=Path.Combine(outside,"move-prefs.ppdemo"),moveTarget=Path.Combine(outside,"moved-prefs.ppdemo");File.WriteAllBytes(moveSource,original);
        ReplayStudioAudioSettingsStore.StageRebound(moveSource,ReplayStudioAudioSettingsStore.ReadForCopy(source,ReplayStudioAudioSettingsStore.CreateBinding(sourceHash,0,0)),ReplayStudioAudioSettingsStore.CreateBinding(sourceHash,0,0),CancellationToken.None);
        ReplayStudioPresentationPreferencesStore.StageRebound(moveSource,ReplayStudioPresentationPreferencesStore.ReadForCopy(source,ReplayStudioPresentationPreferencesStore.CreateBinding(sourceHash,0,0)),ReplayStudioPresentationPreferencesStore.CreateBinding(sourceHash,0,0),CancellationToken.None);
        ReplayArtifacts.Move(moveSource,moveTarget);CheckPreferences(moveTarget,0,0,"artifact move");
        ReplayArtifacts.DeleteAll(moveTarget);
        Check(!File.Exists(moveTarget+ReplayStudioAudioSettingsStore.Extension)&&!File.Exists(moveTarget+ReplayStudioPresentationPreferencesStore.Extension),"artifact deletion owns both durable preference sidecars");
        string stalePreferenceTarget=Path.Combine(outside,"stale-preferences.ppclip");
        using(var ticket=Prepare(stalePreferenceTarget,CancellationToken.None))
        {
            var currentAudio=ReplayStudioAudioSettingsStore.Read(source,sourceWitness);
            ReplayStudioAudioSettingsStore.Save(currentAudio,sourceWitness,audioSettings with{GameGain=.9f},()=>{});
            Throws(()=>Commit(ticket),"audio preferences changed after Save As review reject final publication");
            Check(!File.Exists(stalePreferenceTarget)&&!File.Exists(stalePreferenceTarget+ReplayStudioAudioSettingsStore.Extension),"stale preference CAS leaves no destination descriptor or preference sidecar");
        }
        Console.WriteLine("PROJECT FILES CPU PASS "+checks+" assertions; no OS picker, GL, network, map installation or real user data.");

        ITheatreProjectSave Prepare(string destination,CancellationToken token)
            =>((Task<ITheatreProjectSave>)Invoke(prepare,null,destination,outside,source,(uint)20,(uint)80,(uint)20,(uint)80,annotations,reel,camera.Crop(20,80),"fixture-camera",new List<FileStream>(),token,preferences)!).GetAwaiter().GetResult();
        string Import(string path,CancellationToken token)=>((Task<string>)Invoke(importer,null,path,token)!).GetAwaiter().GetResult();
        void Commit(ITheatreProjectSave ticket)=>Invoke(ticket.GetType().GetMethod("Commit",BindingFlags.Instance|BindingFlags.NonPublic)!,ticket);
        void CheckPreferences(string logical,uint start,uint end,string operation)
        {
            var audio=ReplayStudioAudioSettingsStore.ReadForCopy(logical,ReplayStudioAudioSettingsStore.CreateBinding(sourceHash,start,end)).Settings;
            var presentation=ReplayStudioPresentationPreferencesStore.ReadForCopy(logical,ReplayStudioPresentationPreferencesStore.CreateBinding(sourceHash,start,end)).Preferences;
            Check(!audio.Enabled&&!audio.GameEvents&&audio.CombatFeedback&&audio.GameGain==.4f&&audio.MusicGain==.25f,
                operation+" retains frozen audio flags/gains under new durable source/range binding");
            Check(presentation==presentationSettings,operation+" retains presentation preferences without frame retiming or stale cache witness");
        }
        void Throws(Action action,string label){bool rejected=false;try{action();}catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or OperationCanceledException or ArgumentException){rejected=true;}Check(rejected,label);}
    }
    private static object? Invoke(MethodInfo method,object? owner,params object?[] args)
    {try{return method.Invoke(owner,args);}catch(TargetInvocationException error)when(error.InnerException!=null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}}
    private static void Write(string path,object value)=>File.WriteAllText(path,JsonSerializer.Serialize(value));
    private static void CreateRecording(string path)
    {
        var match=new MatchStatePacket{RoomKey="MP1 SANCTORUS",NextRoomKey="",Mode=(byte)GameMode.Battle,TimeRemaining=300,Flags=MatchStatePacket.FlagInProgress,MatchId=1,AuthorityEpoch=1};
        byte[] matchBytes=new byte[1+MatchStatePacket.Size];matchBytes[0]=(byte)PacketType.MatchState;match.Write(matchBytes.AsSpan(1));
        var metadata=new ReplayMetadata{RoomKey=match.RoomKey,Mode=GameMode.Battle,Bootstrap=new ReplayBootstrap{Packets=new[]{matchBytes}}};
        using var writer=new ReplayWriterV3(path,metadata);byte[] packet={(byte)PacketType.Ping,17,42};writer.WriteRecord(0,packet);writer.WriteRecord(120,packet);
    }
}
