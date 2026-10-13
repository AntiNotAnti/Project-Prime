using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Reflection;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using MphRead.Mods.MapEditor;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

sealed partial class CheckWindow
{
    internal bool ResourceStressEnabled { get; private set; }
    private string _resourceArm="combined";
    private ShaderSourceLedger? _resourceShaderSources;
    private int _quiescentSamples=6, _quiescentIntervalMs=1000;
    private int _resourceCycles = 4, _resourceFrames = 120, _resourceCycle;
    private bool _resourceCandidate, _resourceHit, _resourceDrain;
    private readonly List<AudioShutdownObservation> _audioShutdowns=new();
    private readonly List<object> _resourceDrainSamples=new();
    private string? _resourceDrainError;
    private sealed class AudioShutdownObservation
    {
        internal Task Task=null!;
        internal long CleanupStarted,ObservedAt,CompletedAt;
    }
    private string[] _resourceRooms = { "MP9 CRYOCHASM", "MP3 PROVING GROUND" };
    private string[] _failureStages = { "retained-promotion-after-vbo", "retained-vao-after-create", "retained-combined-after-vbo", "depth-snapshot-after-framebuffer", "shadow-after-framebuffer" };
    private byte[]? _referencePixels, _referenceDepth;
    private ulong _referenceFrame;
    private string _resourceEvidence="";
    private readonly List<object> _resourceRecords = new();
    private readonly List<long> _resourcePrivateBytes = new();
    private readonly List<object> _studioRecords = new();
    private readonly Dictionary<string,string> _rotationReferences = new();
    private int _resourcePlayers;
    private FieldInfo FailureCallback => typeof(Scene).Assembly.GetType("MphRead.Mods.Render.OpenGlFailureInjection")!
        .GetField("Callback", BindingFlags.NonPublic | BindingFlags.Static)!;
    private string ResourceRoom => _resourceRooms[_resourceCycle % _resourceRooms.Length];
    private string? FailureStage => _failureStages.Length == 0 ? null : _failureStages[_resourceCycle % _failureStages.Length];
    internal void ConfigureResourceStress(string[] args)
    {
        ResourceStressEnabled = args.Contains("-resourcestress");
        if (!ResourceStressEnabled) return;
        string Value(string key,string fallback) => Array.IndexOf(args,key) is int i && i>=0 && i+1<args.Length ? args[i+1] : fallback;
        _resourceEvidence=Path.GetFullPath(Value("-resource-evidence",Value("-output","native-render-result.json")+".resource-evidence"));
        Directory.CreateDirectory(_resourceEvidence);
        _resourceDrain=args.Contains("-resource-drain");
        _resourceArm=Value("-resource-arm","combined");
        if (_resourceArm is not ("combined" or "map-gl" or "studio"))
            throw new ArgumentException("Use -resource-arm combined|map-gl|studio|audio.");
        _quiescentSamples=int.Parse(Value("-resource-quiescent-samples","6"));
        _quiescentIntervalMs=int.Parse(Value("-resource-quiescent-ms","1000"));
        ResourceAttribution.ValidateQuiescence(_quiescentSamples,_quiescentIntervalMs);
        _resourceCycles=int.Parse(Value("-resourcecycles","4"));
        _resourceFrames=int.Parse(Value("-resourceframes","120"));
        _resourceRooms=Value("-resourcerooms","MP9 CRYOCHASM|MP3 PROVING GROUND").Split('|',StringSplitOptions.RemoveEmptyEntries);
        string stages=Value("-failure-stages",string.Join(',',_failureStages));
        _failureStages=stages=="none" ? Array.Empty<string>() : stages.Split(',',StringSplitOptions.RemoveEmptyEntries);
        if (_resourceCycles<1 || _resourceCycles>200 || _resourceFrames<30 || _resourceFrames>600 || _resourceRooms.Length==0)
            throw new ArgumentException("Resource cycles 1..200, frames 30..600, nonempty rooms required.");
        if (_windowCycle || _shots!=null || _bombCheck || _impactCheck || _outlineCheck)
            throw new ArgumentException("Resource stress is separate from capture, window-cycle and performance modes.");
        if (_resourceArm != "studio" && _failureStages.Contains("shadow-after-framebuffer") && _shadows==ShadowQuality.Off)
            throw new ArgumentException("Shadow allocation injection requires enabled -shadows.");
        if (args.Contains("-resource-shader-source-ledger")) _resourceShaderSources = new ShaderSourceLedger();
    }
    internal void ClearResourceFailureCallback()
    {
        if (ResourceStressEnabled) FailureCallback.SetValue(null,null);
        _resourceShaderSources?.Dispose();
    }
    private void StartResourceScene()
    {
        FailureCallback.SetValue(null,null);
        if (Scene != null) CleanupAndInspect();
        _resourceHit=false;
        if (_resourceCandidate && FailureStage is string stage)
            FailureCallback.SetValue(null,(Action<string>)(actual =>
            {
                if (_resourceHit || actual!=stage) return;
                _resourceHit=true;
                var exception=(Exception)Activator.CreateInstance(typeof(Scene).Assembly.GetType("MphRead.Mods.Render.OpenGlInjectedFailureException")!,
                    BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{stage},null)!;
                throw exception;
            }));
        BeginScene();
        typeof(Scene).GetField("_keyboardState",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(Scene,SyntheticInput.CreateKeyboard());
        typeof(Scene).GetField("_mouseState",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(Scene,SyntheticInput.CreateMouse());
        Scene!.Random.SetRng1(12345);Scene.Random.SetRng2(98765);
        PlayerEntity.MaxPlayers=_resourcePlayers;
        for(int i=0;i<_resourcePlayers;i++) AddPlayer(i==0?Hunter.Samus:Hunter.Spire);
        PlayerEntity.PlayerCount=_resourcePlayers;PlayerEntity.MainPlayerIndex=0;
        foreach(var player in PlayerEntity.Players) player.IsBot=false;
        if (_resourceArm == "map-gl") ResourceAttribution.AddRoomWithoutAudio(Scene!,
            () => AddRoom(ResourceRoom,GameMode.Battle,playerCount:_resourcePlayers));
        else AddRoom(ResourceRoom,GameMode.Battle,playerCount:_resourcePlayers);
        ApplyDiagnosticOptions();
        LoadScene();
        RenderOptions.PlayerOutline=PlayerOutlineStyle.Red;
        // FBO failure legitimately falls back to ordinary unshadowed rendering;
        // its reference explicitly uses that same fallback output.
        RenderOptions.Shadows=FailureStage=="shadow-after-framebuffer" && !_resourceCandidate ? ShadowQuality.Off : _shadows;
        Scene.GameState.PointGoal=0;Scene.GameState.MatchTime=-1;
        FrameTiming.Reset();FrameTiming.ResetDiagnostics();
    }
    private void ResourceStressTick()
    {
        if (_wall.Elapsed.TotalSeconds > _resourceCycles*_resourceFrames/30.0+180)
            throw new TimeoutException("Resource stress timed out.");
        if(Scene.FrameCount<(ulong)_resourceFrames) return;
        if(Scene.FrameCount!=(ulong)_resourceFrames) throw new InvalidOperationException("Resource fixture advanced beyond its exact requested simulation frame.");
        if(FailureStage=="depth-snapshot-after-framebuffer" && (!_resourceCandidate || !_resourceHit)) ProbeDepthSnapshotAllocation();
        Scene.OnDrawFrame();Scene.OnRenderFrame();
        byte[] pixels=Scene.ReadSceneTarget(out int width,out int height) ?? throw new InvalidOperationException("Missing world target.");
        if(!pixels.Any(v=>v!=0) || pixels.Distinct().Count()<4) throw new InvalidOperationException("Vacuous resource pixels.");
        int previous=GL.GetInteger(GetPName.ReadFramebufferBinding);
        int framebuffer=(int)typeof(Scene).GetMethod("GraphicsReadFramebuffer",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(Scene,null)!;
        pixels=new byte[width*height*4];
        int depthFramebuffer=(int)typeof(Scene).GetField("_frameBuffer",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(Scene)!;
        float[] depth=new float[width*height];
        try { GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,framebuffer);GL.ReadBuffer(ReadBufferMode.ColorAttachment0);GL.ReadPixels(0,0,width,height,PixelFormat.Rgba,PixelType.UnsignedByte,pixels);GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,depthFramebuffer);GL.ReadPixels(0,0,width,height,PixelFormat.DepthComponent,PixelType.Float,depth); }
        finally { GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,previous); }
        if(!depth.Any(d=>d<1) || depth.Any(d=>!float.IsFinite(d))) throw new InvalidOperationException("Vacuous or invalid depth.");
        byte[] depthBytes=new byte[depth.Length*4];System.Buffer.BlockCopy(depth,0,depthBytes,0,depthBytes.Length);
        if(_resourceCandidate)
        {
            if(FailureStage!=null && !_resourceHit) throw new InvalidOperationException("Requested failure stage not reached: "+FailureStage);
            if(!_referencePixels!.SequenceEqual(pixels) || !_referenceDepth!.SequenceEqual(depthBytes))
            {
                Directory.CreateDirectory(_resourceEvidence);
                string stem=Path.Combine(_resourceEvidence,$"cycle-{_resourceCycle}-{FailureStage}");
                File.WriteAllBytes(stem+"-reference.rgba",_referencePixels!);
                File.WriteAllBytes(stem+"-candidate.rgba",pixels);
                File.WriteAllBytes(stem+"-reference-depth-float32.bin",_referenceDepth!);
                File.WriteAllBytes(stem+"-candidate-depth-float32.bin",depthBytes);
                ScreenCapture.Save(Scene,stem+"-candidate.png");
                File.WriteAllText(stem+"-mismatch.json",JsonSerializer.Serialize(new { cycle=_resourceCycle,stage=FailureStage,room=ResourceRoom,width,height,
                    referenceSimulationFrame=_referenceFrame,candidateSimulationFrame=Scene.FrameCount,camera=Scene.CameraPosition.ToString(),
                    exactRgba=_referencePixels!.SequenceEqual(pixels),exactDepth=_referenceDepth!.SequenceEqual(depthBytes),injectionHit=_resourceHit },new JsonSerializerOptions{WriteIndented=true}));
                throw new InvalidOperationException("Injected fallback changed exact world pixels/depth: "+FailureStage+"; diagnostics "+stem);
            }
        }
        else
        {
            _referencePixels=pixels;_referenceDepth=depthBytes;_referenceFrame=Scene.FrameCount;
            Directory.CreateDirectory(_resourceEvidence);
            if(!ScreenCapture.Save(Scene,Path.Combine(_resourceEvidence,$"cycle-{_resourceCycle}-{FailureStage ?? "normal"}-reference.png")))
                throw new InvalidOperationException("Resource reference screenshot failed.");
            string key=ResourceRoom+":"+RenderOptions.Shadows;
            string hash=Convert.ToHexString(SHA256.HashData(pixels));
            if(_rotationReferences.TryGetValue(key,out string? prior) && prior!=hash)
                throw new InvalidOperationException("Repeated real room reload changed pixels: "+key);
            _rotationReferences[key]=hash;
        }
        FailureCallback.SetValue(null,null);
        long generationBefore=RetainedGeneration();
        ulong capturedFrame=Scene.FrameCount;
        long cleanupStarted=Stopwatch.GetTimestamp();
        object audioOwnedBefore=ResourceAttribution.AudioOwnedSample();
        object cleanup=CleanupAndInspect();
        ObserveAudioShutdown(cleanupStarted);
        long generationAfter=RetainedGeneration();
        if(generationAfter<=generationBefore) throw new InvalidOperationException("Scene teardown did not invalidate retained generation.");
        using var process=Process.GetCurrentProcess();process.Refresh();
        _resourcePrivateBytes.Add(process.PrivateMemorySize64);
        _resourceRecords.Add(new { cycle=_resourceCycle,room=ResourceRoom,candidate=_resourceCandidate,stage=FailureStage,depthSnapshotDedicatedFixture=FailureStage=="depth-snapshot-after-framebuffer",injectionHit=_resourceHit,
            width,height,simulationFrame=capturedFrame,pixelsSha256=Convert.ToHexString(SHA256.HashData(pixels)),depthSha256=Convert.ToHexString(SHA256.HashData(depthBytes)),
            generationBefore,generationAfter,cleanup,privateBytes=process.PrivateMemorySize64>0?(long?)process.PrivateMemorySize64:null,workingSetBytes=process.WorkingSet64,managedBytes=GC.GetTotalMemory(false),memory=ResourceAttribution.MemorySample(),audioOwnedBefore,audioShutdown=AudioShutdownSummary() });
        if(!_resourceCandidate){_resourceCandidate=true;StartResourceScene();return;}
        if (_resourceArm == "combined") RunStudioReload();
        _resourceCandidate=false;_resourceCycle++;
        if(FinishResourceCycle()) return;
        StartResourceScene();
    }
    private void ObserveAudioShutdown(long cleanupStarted)
    {
        Type sfx=typeof(Scene).Assembly.GetType("MphRead.Sound.Sfx")!;
        Task task=(Task)sfx.GetProperty("ShutdownCompletion",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        var entry=new AudioShutdownObservation { Task=task,CleanupStarted=cleanupStarted,ObservedAt=Stopwatch.GetTimestamp() };
        _audioShutdowns.Add(entry);
        if(task.IsCompleted) entry.CompletedAt=entry.ObservedAt;
        else _=task.ContinueWith(_=>Interlocked.Exchange(ref entry.CompletedAt,Stopwatch.GetTimestamp()),
            CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
    }
    private object AudioShutdownSummary() => AudioShutdownSummary(false);
    private object AudioShutdownSummary(bool includeTasks) => new
    {
        observations=_audioShutdowns.Count,distinctTasks=_audioShutdowns.Select(e=>e.Task).Distinct().Count(),
        completed=_audioShutdowns.Count(e=>e.Task.IsCompleted),remaining=_audioShutdowns.Count(e=>!e.Task.IsCompleted),
        faulted=_audioShutdowns.Count(e=>e.Task.IsFaulted),canceled=_audioShutdowns.Count(e=>e.Task.IsCanceled),
        tasks=includeTasks ? _audioShutdowns.Select((e,index)=>new { index,status=e.Task.Status.ToString(),
            observedAfterCleanupMs=Stopwatch.GetElapsedTime(e.CleanupStarted,e.ObservedAt).TotalMilliseconds,
            observedCompletionFromCleanupStartMs=Interlocked.Read(ref e.CompletedAt) is long end && end>0
                ? (double?)Stopwatch.GetElapsedTime(e.CleanupStarted,end).TotalMilliseconds : null }).ToArray() : null,
        durationMeaning="Observation from cleanup start; includes cleanup/GC and completion-observer scheduling. Already-completed tasks provide an upper bound, not native close duration."
    };
    private bool FinishResourceCycle()
    {
        Console.WriteLine($"NATIVERENDER resource arm={_resourceArm} cycle={_resourceCycle}/{_resourceCycles}");
        if (_resourceCycle != _resourceCycles) return false;
        if (_resourceDrain) DrainResources();
        Complete=true;Close();return true;
    }
    private void DrainResources()
    {
        _resourceDrainSamples.Add(new { phase="before-drain",memory=ResourceAttribution.MemorySample(),audio=AudioShutdownSummary() });
        Task[] tasks=_audioShutdowns.Select(e=>e.Task).Distinct().ToArray();
        long started=Stopwatch.GetTimestamp();bool allComplete=false;
        try {allComplete=Task.WaitAll(tasks,TimeSpan.FromSeconds(15));}
        catch(AggregateException ex){_resourceDrainError=ex.Flatten().ToString();allComplete=tasks.All(t=>t.IsCompleted);}
        _resourceDrainSamples.Add(new { phase="after-audio-wait",waitLimitSeconds=15,elapsedMs=Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            allComplete,memory=ResourceAttribution.MemorySample(),audio=AudioShutdownSummary() });
        if (!allComplete || tasks.Any(t=>t.IsFaulted || t.IsCanceled))
            throw new InvalidOperationException("Audio drain incomplete, faulted or canceled: "+_resourceDrainError);
        // Deliberately outside timing evidence: request one LOH compaction and
        // drain finalizers plus queued GL work to distinguish deferred release.
        GCSettings.LargeObjectHeapCompactionMode=GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration,GCCollectionMode.Forced,blocking:true,compacting:true);
        GC.WaitForPendingFinalizers();GL.Finish();
        _resourceDrainSamples.Add(new { phase="after-full-gc-finalizers-gl-finish",memory=ResourceAttribution.MemorySample(),audio=AudioShutdownSummary() });
        ResourceAttribution.QuiescentSamples(_resourceDrainSamples,_quiescentSamples,_quiescentIntervalMs,AudioShutdownSummary);
    }
    private void ProbeDepthSnapshotAllocation()
    {
        Type type=typeof(Scene).Assembly.GetType("MphRead.Mods.Render.DepthReplaySnapshot")!;
        object snapshot=Activator.CreateInstance(type,true)!;
        int source=(int)typeof(Scene).GetField("_frameBuffer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Scene)!;
        int previous=GL.GetInteger(GetPName.FramebufferBinding);
        long before=Convert.ToInt64(type.GetProperty("ResidentBytes",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null));
        try
        {
            bool captured=(bool)type.GetMethod("Capture",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(snapshot,new object[]{source,Scene.RenderSize.X,Scene.RenderSize.Y})!;
            if(_resourceCandidate)
            {
                if(captured || !_resourceHit) throw new InvalidOperationException("Injected depth snapshot did not return ordinary replay fallback.");
            }
            else if(!captured || Convert.ToInt64(type.GetProperty("ResidentBytes",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null))<=before)
                throw new InvalidOperationException("Depth allocation reference did not allocate/copy nonempty storage.");
        }
        finally
        {
            ((IDisposable)snapshot).Dispose();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer,previous);
        }
        if(Convert.ToInt64(type.GetProperty("ResidentBytes",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null))!=before)
            throw new InvalidOperationException("Partial depth snapshot allocation leaked owned bytes.");
    }
    private static long RetainedGeneration() => (long)typeof(Scene).Assembly.GetType("MphRead.Mods.Render.DesktopRetainedGeometry")!
        .GetField("_tokenGeneration",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
    private void RunStudioReload()
    {
        var create=typeof(Scene).GetMethod("CreateEditorRenderer",BindingFlags.Public|BindingFlags.Static)
            ?? throw new InvalidOperationException("Studio renderer unavailable in this client build.");
        var definition=MapTemplates.Create("Resource reload",true).Definition;
        var cache=new MapViewportCache();
        Scene editor=(Scene)create.Invoke(null,new object[]{new Vector2i(320,180)})!;
        var draw=typeof(Scene).GetMethod("DrawEditorFrame")!;
        byte[]? first=null;
        try
        {
            for(int reload=0;reload<3;reload++)
            {
                cache.Invalidate(definition,new(MapChangeDomain.Geometry|MapChangeDomain.Material));
                var frame=new MapRenderFrame(new(320,180),new(new System.Numerics.Vector3(12,10,12),System.Numerics.Vector3.Zero,true),
                    cache.Meshes,new HashSet<Guid>(),new Dictionary<Guid,System.Numerics.Matrix4x4>(),false,false);
                draw.Invoke(editor,new object[]{frame,new Vector2i(320,180),true});
                byte[] image=editor.ReadSceneTarget(out _,out _) ?? throw new InvalidOperationException("Studio target missing.");
                if(first!=null && !first.SequenceEqual(image)) throw new InvalidOperationException("Studio geometry/material reload changed pixels.");
                first=image;
            }
            bool spatialVariation=first!=null && Enumerable.Range(1,first.Length/3-1)
                .Any(i=>first[3*i]!=first[0] || first[3*i+1]!=first[1] || first[3*i+2]!=first[2]);
            int fbo=(int)typeof(Scene).GetField("_frameBuffer",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(editor)!;
            int previous=GL.GetInteger(GetPName.ReadFramebufferBinding);
            float[] depth=new float[320*180];
            try {GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,fbo);GL.ReadPixels(0,0,320,180,PixelFormat.DepthComponent,PixelType.Float,depth);}
            finally {GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,previous);}
            int uploads=(int)typeof(Scene).GetProperty("EditorMeshUploads")!.GetValue(editor)!;
            if(cache.GeometryRebuildCount!=3 || cache.Meshes.Count==0 || uploads<cache.Meshes.Count*3 || !spatialVariation || !depth.Any(d=>d<1))
            {
                ScreenCapture.Save(editor,Path.Combine(_resourceEvidence,$"studio-{_resourceCycle}-blank.png"));
                throw new InvalidOperationException($"Studio reload did not rebuild visible geometry: rebuilds={cache.GeometryRebuildCount}, meshes={cache.Meshes.Count}, uploads={uploads}, spatialVariation={spatialVariation}, covered={depth.Count(d=>d<1)}.");
            }
        }
        finally { editor.UnloadGl(); }
        int meshes=(int)typeof(Scene).GetProperty("EditorMeshCount")!.GetValue(editor)!;
        int textures=((HashSet<int>)typeof(Scene).GetField("_ownedTextures",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(editor)!).Count;
        int framebuffer=(int)typeof(Scene).GetField("_frameBuffer",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(editor)!;
        int program=(int)typeof(Scene).GetField("_shaderProgramId",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(editor)!;
        long retained=Convert.ToInt64(typeof(Scene).Assembly.GetType("MphRead.Mods.Render.DesktopRetainedGeometry")!
            .GetProperty("ResidentBytes",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null));
        if(meshes!=0 || textures!=0 || framebuffer!=0 || program!=0 || retained!=0 || GL.GetError()!=ErrorCode.NoError) throw new InvalidOperationException("Studio unload retained meshes or GL errors.");
        _studioRecords.Add(new { cycle=_resourceCycle,geometryRebuilds=cache.GeometryRebuildCount,meshCountAfter=meshes,textureCountAfter=textures,framebufferAfter=framebuffer,programAfter=program,retainedBytesAfter=retained,pixelsSha256=Convert.ToHexString(SHA256.HashData(first!)),memory=ResourceAttribution.MemorySample(),audio=ResourceAttribution.AudioOwnedSample() });
    }
    internal object ResourceStressResult() => new { schema=2,kind="same-context-resource-stress",arm=_resourceArm,manifest=ResourceAttribution.Manifest(),
        workload=new { fixedRng=new[]{12345,98765},players=_resourceArm=="studio"?0:_resourcePlayers,bots=false,simulationHz=60,studioRebuildsPerCycle=_resourceArm=="map-gl"?0:3,readback=true,audioEnabled=_resourceArm=="combined",
            shadows=_shadows.ToString(),requestedFramebuffer=new[]{_requestedPixels.X,_requestedPixels.Y},actualFramebuffer=new[]{FramebufferSize.X,FramebufferSize.Y},cap=_cap,
            vendor=_glVendor,renderer=_glRenderer,glVersion=_glVersion,quiescentSamples=_quiescentSamples,quiescentIntervalMs=_quiescentIntervalMs },assemblyMvid=typeof(Scene).Module.ModuleVersionId,
        cyclesRequested=_resourceCycles,cyclesCompleted=_resourceCycle,simulationFramesPerArm=_resourceArm=="studio"?0:_resourceFrames,rooms=_resourceArm=="studio"?Array.Empty<string>():_resourceRooms,failureStages=_resourceArm=="studio"?Array.Empty<string>():_failureStages,resourceDrainRequested=_resourceDrain,audioShutdown=AudioShutdownSummary(true),drainSamples=_resourceDrainSamples,drainError=_resourceDrainError,
        contextCount=1,windowEvents=_windowEvents,ownedResourcesAfterEveryUnloadZero=Complete,records=_resourceRecords,studioReloads=_studioRecords,privateMemoryAvailable=_resourcePrivateBytes.Any(v=>v>0),privateBytesFirst=_resourcePrivateBytes.FirstOrDefault()>0?(long?)_resourcePrivateBytes.FirstOrDefault():null,
        privateBytesLast=_resourcePrivateBytes.LastOrDefault()>0?(long?)_resourcePrivateBytes.LastOrDefault():null,complete=Complete,
        shaderSourceLedger=_resourceShaderSources?.Result(),
        limitations="Readback/GC/reloads are lifecycle evidence, never FPS evidence. Live managed bytes differ from GC committed/fragmented bytes and RSS. Memory is informational, not driver residency. Optional audio wait/full GC/finalizers/GL.Finish is diagnostic-only, never FPS evidence. Studio tests renderer/cache reload, not full editor UI." };
}
