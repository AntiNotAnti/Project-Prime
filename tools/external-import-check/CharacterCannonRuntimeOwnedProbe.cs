using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Effects;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Import;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.Platform;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Owned first-person product path. Raw source never leaves the temporary import;
// optional gameplay screenshots need an explicit worktree-artifacts destination.
internal static class CharacterCannonRuntimeOwnedProbe
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> Key = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly Action<MouseState, Vector2> MousePosition = typeof(MouseState)
        .GetProperty("Position", Hidden | BindingFlags.Public)!.SetMethod!.CreateDelegate<Action<MouseState, Vector2>>();
    private static readonly Keys[] KeysUsed = [Keys.Enter, Keys.W, Keys.M, Keys.P, Keys.B];
    private sealed record Stage(string Name, int Steps);
    private static readonly Stage[] Script = [new("idle",20),new("fire",20),new("release",25),new("charge",110),
        new("charge-release",40),new("turn",30),new("walk",30),new("missile",50),new("power",50),
        new("morph",100),new("unmorph",100),new("settle",100)];
    private sealed record Result(string Identity, string[] Trace, object[] Stages, int CannonFrames,
        int FallbackFrames, int RepeatedDraws, double MaximumTipError, double MaximumPaletteError, object[] FractionalSchedules, object? Ball = null,
        string[]? TrailTrace = null, object? Trail = null);

    internal static int Run(string rvz, bool trooper = false, bool probeBall = false, bool probeTrail = false)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "prime-cannon-runtime-" + Guid.NewGuid().ToString("N"));
        string cwd = Environment.CurrentDirectory;
        string? oldData = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA"),
            native = Environment.GetEnvironmentVariable("PRIME_BODY_TEST_ASSETS"),
            capturesRoot = Environment.GetEnvironmentVariable("PRIME_CANNON_TEST_CAPTURE_DIR");
        string? sourceBefore = null, sourceAfter = null, nativeBefore = null, nativeAfter = null,
            failed = null, error = null, errorSha = null, renderer = null, version = null;
        bool oldHd = RenderOptions.CharacterModelReplacements, oldBright = RenderOptions.BrightSkins,
            oldCosmetics = RenderOptions.ShowCustomCosmetics, oldForce = MapAudit.ForceEveryone,
            oldScript = NetTestScript.Enabled, oldFps = RenderOptions.ShowFps, stylus = PointerInput.StylusMode;
        bool focus = GamepadContexts.Focused, menu = GamepadContexts.MenuVisible,
            capturing = GamepadContexts.Capturing, text = GamepadContexts.TextEntryActive;
        int oldFov = RenderOptions.FieldOfView, oldCap = FrameTiming.FrameRateCap;
        ShadowQuality oldShadow = RenderOptions.Shadows;
        var runs = new List<Result>(); var captures = new List<object>();
        var script=probeBall?Script.SelectMany(s=>s.Name=="morph"?new[]{s,new Stage("ball-roll",90),new Stage("ball-bomb",20),new Stage("ball-settle",50),
            new Stage("ball-death-respawn",420),new Stage("ball-after-life-morph",100),new Stage("ball-after-life-roll",60)}:new[]{s}).ToArray():Script;
        bool cleaned = false; int checks = 0;
        string coreSha = HashFile(typeof(Scene).Assembly.Location), toolSha = HashFile(typeof(CharacterCannonRuntimeOwnedProbe).Assembly.Location);
        var watch = Stopwatch.StartNew();
        void Check(bool condition, string label)
        { if (!condition) { failed = label; throw new InvalidDataException(label); } checks++; }
        void Bound() { if (watch.Elapsed.TotalMinutes > 10) throw new TimeoutException("Cannon runtime probe exceeded ten minutes."); }
        try
        {
            Check(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64
                && !Headless.Active && !NetSession.Active && !DemoPlayback.IsActive && !ReplayCapture.LiveRecordingActive,
                "fresh actual Windows x64 offline first-person process");
            Check(!probeTrail||probeBall&&!trooper,"source trail fixture uses both exact local Echoes ball selections");
            Check(!string.IsNullOrWhiteSpace(native) && Directory.Exists(native), "existing owned AMHE1 fixture required");
            native = Path.GetFullPath(native!); rvz = Path.GetFullPath(rvz);
            sourceBefore = HashFile(rvz); nativeBefore = Inventory(native);
            if (!string.IsNullOrWhiteSpace(capturesRoot))
            {
                capturesRoot = Path.GetFullPath(capturesRoot);
                Check(capturesRoot.StartsWith(Path.GetFullPath(Path.Combine(cwd,"artifacts")) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase), "optional cannon gameplay capture remains inside worktree artifacts");
                Directory.CreateDirectory(capturesRoot);
            }
            string user = Path.Combine(temporary,"user"), imports = Path.Combine(temporary,"imports");
            Directory.CreateDirectory(user);
            File.WriteAllText(Path.Combine(user,"paths.txt"), Ver.AMHE1 + "=" + native + "\nExport=" + Path.Combine(user,"Export") + "\n");
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",user); Directory.SetCurrentDirectory(user);
            ConsoleSetup.Run(); Paths.UpdatePaths(); Paths.SetPath(Ver.AMHE1,native); Paths.MphKey = Ver.AMHE1;
            GraphicsBackendPolicy.Configure("opengl");
            RenderOptions.CharacterModelReplacements=false; RenderOptions.BrightSkins=false; RenderOptions.ShowCustomCosmetics=true;
            RenderOptions.Shadows=ShadowQuality.Off; RenderOptions.FieldOfView=120; RenderOptions.ShowFps=false;
            MapAudit.ForceEveryone=true; NetTestScript.Enabled=false;
            GamepadManager.ClearAll(); GamepadContexts.Focused=true;
            GamepadContexts.MenuVisible=GamepadContexts.Capturing=GamepadContexts.TextEntryActive=false;
            PointerInput.StylusMode=false; PointerInput.Reset(); PointerDevice.Reset();
            Scene.LauncherPreview=false; Scene.PreviewWanted=false;
            string[] ids=trooper?["trooper-power","trooper-ice","trooper-wave","trooper-plasma"]
                :["darksamus-echoes","darksamus-overcharged-echoes"];
            RetroCharacterPreviewExporter.ExportFromOwnedGame(rvz,imports,selectedIds:ids);
            var selections=ids.Select(id=>CharacterSourcePreviewSelection.LoadForLocalBody(id,CancellationToken.None,imports)).ToArray();
            Check(selections.All(s=>s.HasCannon&&s.RuntimeCannon),
                "only the exact supported appearance owns its own source cannon capability");
            var canonicalByAppearance=new Dictionary<PrivateCharacterAppearance,CharacterSourceGxMaterials>();
            foreach(var selected in selections.Where(s=>s.RuntimeCannon))
            {
                Check(selected.Pack.TryResolvePrivate(selected.NativeHunter,selected.SelectedAppearance,CharacterModelPart.Biped,0,out var sourceBody),
                    "cannon comparison resolves its own canonical source body");
                Check(selected.Pack.TryCreateSourcePreviewLease(sourceBody,out var bodyLease)&&bodyLease.SourceMaterials!=null,
                    "cannon comparison captures its own canonical body GX archive");
                canonicalByAppearance.Add(selected.SelectedAppearance,bodyLease!.SourceMaterials!);
            }
            var settings=DesktopGlContext.Settings(background:true); settings.ClientSize=new(640,480);
            settings.StartVisible=true; settings.StartFocused=false; settings.Title="Project Prime actual owned source cannon runtime";
            using var window=new NativeWindow(settings); using var graphics=new DesktopGraphicsSession(window); GLFW.SwapInterval(0);
            renderer=GL.GetString(StringName.Renderer);version=GL.GetString(StringName.Version);
            foreach(var selection in new CharacterSourcePreviewSelection?[]{null}.Concat(selections))
            {
                Result result=Exercise(selection); runs.Add(result);
                if(runs.Count>1)
                {
                    Result baseline=runs[0];
                    Check(result.Trace.Length==baseline.Trace.Length,"first-person parity traces have matching lengths");
                    int mismatch=Enumerable.Range(0,result.Trace.Length).FirstOrDefault(i=>result.Trace[i]!=baseline.Trace[i],-1);
                    Check(mismatch<0,result.Identity+" native health/actor/ammo/aim/projectile/effect/RNG parity; first mismatch="+mismatch);
                    if(probeTrail)
                    {
                        Check(result.TrailTrace!.Length==baseline.TrailTrace!.Length,"native active trail history traces have matching completed-step grains");
                        int trailMismatch=Enumerable.Range(0,result.TrailTrace.Length).FirstOrDefault(i=>result.TrailTrace[i]!=baseline.TrailTrace[i],-1);
                        Check(trailMismatch<0,result.Identity+" native18-slot alpha/index/active-matrix history parity; first mismatch="+trailMismatch);
                    }
                }
            }

            Result Exercise(CharacterSourcePreviewSelection? selection)
            {
                bool final=selection?.SelectedAppearance.SkinSource=="MP2DarkSamusFinal";
                Bound();FrameTiming.Reset();PointerInput.Reset();PointerDevice.Reset();
                Rng.SetRng1(8128);Rng.SetRng2(0x8128a5a5);
                var keyboard=SyntheticInput.CreateKeyboard();var mouse=SyntheticInput.CreateMouse();Scene? scene=null;
                try
                {
                    scene=new(window.FramebufferSize,keyboard,mouse,_=>{},()=>{});
                    scene.Random.SetRng1(123456);scene.Random.SetRng2(654321);scene.AddPlayer(trooper?Hunter.Weavel:Hunter.Samus);
                    scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:1,nodeLayerMask:0);scene.OnLoad();scene.OnResize();
                    var inputMode=typeof(Scene).GetField("_inputMode",Hidden)!;inputMode.SetValue(scene,Enum.Parse(inputMode.FieldType,"All"));
                    scene.GameState.MatchTime=-1;scene.GameState.PointGoal=10000;scene.GameState.ThirdPersonCamera=false;
                    scene.GameState.SpawnProtection=false;scene.GameState.EnhancedHunters=false;scene.GameState.ShadowFreeze=false;
                    var player=scene.Players.Main;player.IsBot=false;player.IgnoreItemPickups=true;
                    foreach(var button in player.Controls.All){button.Type=ButtonType.Key;button.Key=Keys.Unknown;}
                    player.Controls.Shoot.Key=Keys.Enter;player.Controls.MoveUp.Key=Keys.W;
                    if(probeBall){player.Controls.AltAttack.Key=Keys.Enter;player.Controls.RollUp.Key=Keys.W;}
                    player.Controls.Missile.Key=Keys.M;player.Controls.PowerBeam.Key=Keys.P;player.Controls.Morph.Key=Keys.B;
                    player.Controls.MouseAim=true;player.Controls.KeyboardAim=false;
                    Key(keyboard,Keys.Enter,true);
                    for(int n=0;n<600&&!player.LoadFlags.TestFlag(LoadFlags.Spawned);n++)scene.OnSimulationFrame();
                    Key(keyboard,Keys.Enter,false);scene.CameraSequences.Current?.End();
                    for(int n=0;n<240;n++)scene.OnSimulationFrame();scene.CameraSequences.Current?.End();
                    Check(player.Health>0&&player.ModIsInPlay&&!player.IsAltForm,"native input reaches actual first-person matching Hunter life");
                    if(selection!=null)player.ModBindSourceBody(selection);
                    CharacterSourceBodyOwner? owner=player.ModSourceBody;CharacterWeightedRenderModel? cannon=owner?.CannonModel;
                    bool expectsCannon=selection?.RuntimeCannon==true;
                    bool expectsBall=selection?.RuntimeBall==true;
                    var sourceBall=owner?.BallModel;
                    var nativeBallLists=player.ModSourceNativeBall.Meshes.Select(m=>m.ListId).Where(id=>id>0).ToHashSet();
                    CharacterWeightedModelData? ballGeometry=null;Matrix4[]? ballSkin=null;Vector3 ballCenter=Vector3.Zero;double ballRadiusError=0;
                    if(probeBall)
                    {
                        Check(expectsBall?owner is {BallCurrent:true}&&sourceBall!=null:sourceBall==null,
                            "each local Echoes selection owns its exact original or composite source ball; native has no source ball");
                        if(expectsBall)
                        {
                            Check(sourceBall!.Joints.Count==2&&sourceBall.IndexCount==(final?8170:2220)*3&&sourceBall.SourceCannon==null
                                &&sourceBall.IsFinalSourceBall==final,"each source ball retains its own complete two-joint geometry without cannon cut");
                            ballGeometry=(CharacterWeightedModelData)typeof(CharacterWeightedRenderModel).GetField("_sourceGeometry",Hidden)!.GetValue(sourceBall)!;
                            Check(selection!.Pack.TryResolvePrivate(Hunter.Samus,selection.SelectedAppearance,CharacterModelPart.AlternateForm,0,out var ballAsset)
                                &&selection.Pack.TryCreateSourcePreviewLease(ballAsset,out _),"actual ball has its own admitted load-boundary lease");
                            selection.Pack.TryCreateSourcePreviewLease(ballAsset,out var ballLease);
                            var ready=CharacterSourceClip.Load(ballLease,ballAsset,ballGeometry.SourceRig!,1).Sample(0);
                            ballSkin=ballGeometry.SourceRig!.Evaluate(ready,ballGeometry.Joints);
                            var posed=new List<Vector3>();var minimum=new Vector3(float.PositiveInfinity);var maximum=new Vector3(float.NegativeInfinity);
                            foreach(var primitive in ballGeometry.Primitives.Where(p=>!final||p.SourceMaterialOrdinal==0))foreach(var vertex in primitive.Vertices)
                            {
                                var point=Vector3.Zero;var position=new Vector3(vertex.Position.X,vertex.Position.Y,vertex.Position.Z);
                                for(int influence=0;influence<4;influence++)if(vertex.Weights[influence]>0)
                                    point+=Vector3.TransformPosition(position,ballSkin[primitive.JointPalette![vertex.PackedJoints>>(5*influence)&31]])*vertex.Weights[influence];
                                posed.Add(point);minimum=Vector3.ComponentMin(minimum,point);maximum=Vector3.ComponentMax(maximum,point);
                            }
                            ballCenter=(minimum+maximum)*.5f;
                            float fittedRadius=posed.Max(point=>Vector3.TransformPosition(point,owner!.BallFit).Length);
                            ballRadiusError=Math.Abs(fittedRadius-Fixed.ToFloat(player.Values.AltColRadius));
                            Check(Vector3.TransformPosition(ballCenter,owner!.BallFit).Length<.00001&&ballRadiusError<.00001,
                                "all original ready-pose vertices fit the unchanged native collision sphere radius and centre");
                            Check(final?sourceBall.Segments.Count==6
                                &&sourceBall.Segments.Select(s=>s.SourceGx!.Program.DrawPass).SequenceEqual(new[]{CharacterSourceGxProgram.Pass.Alpha,
                                    CharacterSourceGxProgram.Pass.Additive,CharacterSourceGxProgram.Pass.Opaque,CharacterSourceGxProgram.Pass.Additive,
                                    CharacterSourceGxProgram.Pass.Additive,CharacterSourceGxProgram.Pass.Opaque})
                                &&sourceBall.Segments.Select(s=>s.SourceGx!.Program.DrawCopies).SequenceEqual(new[]{1,2,1,2,2,1})
                                &&sourceBall.Segments.Select(s=>s.SourceGx!.Program.FrontFacesOnly).SequenceEqual(new[]{false,true,false,false,false,false})
                                :sourceBall.Segments.All(s=>s.SourceGx is {} gx&&gx.Program.DrawPass==CharacterSourceGxProgram.Pass.Opaque),
                                "normal opaque and Final six-batch shell/vent/vessel/line/heart material policies remain distinct and complete");
                        }
                    }
                    var canonicalMaterials=expectsCannon?canonicalByAppearance[selection!.SelectedAppearance]:null;
                    Check(expectsCannon ? owner is {CannonCurrent:true}&&cannon!=null : cannon==null,
                        "only the requested qualified local-body selection owns a runtime cannon");
                    var gun=(ModelInstance)typeof(PlayerEntity).GetField("_gunModel",Hidden)!.GetValue(player)!;
                    var nativeGunLists=gun.Model.Meshes.Select(mesh=>mesh.ListId).Where(id=>id>0).ToHashSet();
                    Check(nativeGunLists.Count>0,"actual native gun owns positive mesh list identities");
                    CharacterWeightedModelData? geometry=cannon==null?null:(CharacterWeightedModelData)typeof(CharacterWeightedRenderModel)
                        .GetField("_sourceGeometry",Hidden)!.GetValue(cannon)!;
                    Matrix4[]? globals=null,skin=null;
                    if(cannon!=null)
                    {
                        Check(cannon.Joints.Count==(trooper?35:39)&&cannon.IndexCount==(trooper?206:final?793:392)*3&&geometry!.SourceAuthoringPose!=null
                            &&geometry.SourceRig!=null&&geometry.SourceCannon!=null,"fixed runtime cannon retains complete source cut/rig and original authoring pose");
                        int[] materialIds=geometry!.Primitives.Select(p=>p.SourceMaterialOrdinal).Distinct().Order().ToArray();
                        Check(trooper?materialIds.SequenceEqual(new[]{0,1,2})
                                &&new[]{0,1,2}.Select(m=>geometry.Primitives.Where(p=>p.SourceMaterialOrdinal==m).Sum(p=>p.Indices.Length)/3).SequenceEqual(new[]{10,32,164})
                            :final?materialIds.SequenceEqual(new[]{3,4,5,7})
                                &&new[]{3,4,5,7}.Select(m=>geometry.Primitives.Where(p=>p.SourceMaterialOrdinal==m).Sum(p=>p.Indices.Length)/3).SequenceEqual(new[]{392,66,314,21})
                            :materialIds.SequenceEqual(new[]{2,5})&&geometry.Primitives.Where(p=>p.SourceMaterialOrdinal==2).Sum(p=>p.Indices.Length)==66*3
                                &&geometry.Primitives.Where(p=>p.SourceMaterialOrdinal==5).Sum(p=>p.Indices.Length)==326*3,
                            "runtime cannon retains exact whole-triangle original material cut");
                        globals=Globals(geometry.SourceRig!,geometry.SourceAuthoringPose!);
                        skin=geometry.Joints.Select((joint,i)=>joint.InverseBind*globals[geometry.SourceRig!.JointNodes[i]]).ToArray();
                        for(int segment=0;segment<cannon.Segments.Count;segment++)
                        {
                            var batch=cannon.Segments[segment];var primitive=geometry.Primitives[segment];
                            var original=canonicalMaterials!.Programs[primitive.SourceMaterialOrdinal];
                            var expected=final?canonicalMaterials.ForFinalCannon().Programs[primitive.SourceMaterialOrdinal]:original;
                            Check(batch.SourceGx!=null&&batch.SourceGx.Program.Vertex120==original.Vertex120
                                &&batch.SourceGx.Program.Fragment120==expected.Fragment120
                                &&batch.SourceGx.Program.InitialRegister1==canonicalMaterials.Programs[primitive.SourceMaterialOrdinal].InitialRegister1,
                                "cannon material and C1 come from its exact original body GX program");
                            Check(!final||batch.SourceGx!.Program.PresentationAlphaScale==(original.DrawPass==CharacterSourceGxProgram.Pass.Alpha?.55f:1f),
                                "Final applies donor shell alpha only to its Alpha pass; raw body programs remain unmodified");
                        }
                    }
                    int sourceFrames=0,fallbackFrames=0,repeats=0,sourceFire=0,sourceCharge=0,sourceRelease=0;
                    int ballFrames=0,ballFallback=0,maxBombs=0,afterLifeBallFrames=0;double maxBallError=0;bool ballMoved=false,died=false,respawned=false;
                    Vector3? ballFirst=null;
                    double maxTip=0,maxPalette=0;bool fired=false,charged=false,morphed=false,missile=false;
                    var trace=new List<string>();var stages=new List<object>();var captured=new HashSet<string>();
                    var trailTrace=new List<string>();int trailPictures=0,blueTrailPictures=0,nativeTrailPictures=0,rollingTrailPictures=0,morphTrailGuards=0,maxTrailSegments=0;
                    var fractionalSchedules=new List<object>();
                    using var sourceLocks = selection == null ? null : new SourceFileLocks(imports);
                    Check(selection == null || sourceLocks!.Count >= ids.Length * 13,
                        "all imported component files remain exclusively locked through actual match steps and draws");
                    Picture(false);Rng.SetRng1(123456);Rng.SetRng2(654321);scene.Random.SetRng1(123456);scene.Random.SetRng2(654321);
                    foreach(Stage stage in script)
                    {
                        if(stage.Name=="ball-death-respawn")
                        {player.TakeDamage(10000,DamageFlags.Death|DamageFlags.IgnoreInvuln,null,null);died=player.Health==0;}
                        int beforeSource=sourceFrames;var animations=new HashSet<string>();
                        for(int age=0;age<stage.Steps;age++)
                        {
                            Bound();foreach(Keys key in KeysUsed)Key(keyboard,key,false);
                            if(stage.Name=="fire"&&age<2||stage.Name=="charge"||stage.Name=="ball-bomb"&&age==0
                                ||stage.Name=="ball-death-respawn"&&age%30==0)Key(keyboard,Keys.Enter,true);
                            if(stage.Name is "walk" or "ball-roll" or "ball-after-life-roll")Key(keyboard,Keys.W,true);
                            if(stage.Name=="missile"&&age==0)Key(keyboard,Keys.M,true);
                            if(stage.Name=="power"&&age==0)Key(keyboard,Keys.P,true);
                            if(stage.Name is "morph" or "unmorph" or "ball-after-life-morph"&&age==0)Key(keyboard,Keys.B,true);
                            if(stage.Name=="turn")MousePosition(mouse,mouse.Position+new Vector2(8,0));
                            FrameTiming.Reset();scene.OnSimulationFrame();Picture(true,stage.Name);
                            if(probeBall&&stage.Name=="ball-roll"&&age==0)
                                Console.WriteLine(JsonSerializer.Serialize(new{Probe="ball-admission-state",Identity=selection?.SelectedAppearance.SkinSource??"native",
                                    player.IsAltForm,player.IsMorphing,player.IsUnmorphing,player.Health,
                                    Common=typeof(PlayerEntity).GetProperty("SourcePresentationStateSupported",Hidden)!.GetValue(player),
                                    BallSupported=typeof(PlayerEntity).GetProperty("SourceBallStateSupported",Hidden)!.GetValue(player),
                                    owner?.Current,owner?.BallCurrent,owner?.BallReady,BallPackets=BallItems(scene).Count,
                                    player.ModCrouched,player.ModTucked,Crouch=typeof(PlayerEntity).GetProperty("ModCrouchVisualAmount",Hidden)!.GetValue(player),
                                    Alpha=typeof(PlayerEntity).GetField("_curAlpha",Hidden)!.GetValue(player),
                                    PaletteOverridePresent=typeof(EntityBase).GetProperty("PaletteOverride",Hidden)!.GetValue(player)!=null,
                                    CameraSequence=scene.CameraSequences.Current!=null,Bright=BrightSkins.ShouldApply(player),
                                    DamageTimer=typeof(PlayerEntity).GetField("_timeSinceDamage",Hidden)!.GetValue(player)}));
                            trace.Add(State(scene,player)+(probeBall?":"+BombState(scene,player):""));animations.Add(player.GunAnimation.ToString());
                            if(probeTrail)trailTrace.Add(TrailHistory(player,activeOnly:true)+":"+Rng.Rng1+":"+Rng.Rng2);
                            respawned|=died&&player.Health>0&&player.ModIsInPlay;
                            if(respawned&&BallItems(scene).Count>0)afterLifeBallFrames++;
                            if(probeBall)maxBombs=Math.Max(maxBombs,SceneBombs(scene).Count(b=>ReferenceEquals(b.Owner,player)));
                            fired|=player.EquipInfo.Beams.Any(beam=>beam.Lifespan>0&&ReferenceEquals(beam.Owner,player));
                            charged|=player.EquipInfo.ChargeLevel>=player.EquipInfo.Weapon.MinCharge*2&&player.EquipInfo.ChargeLevel>0;
                            morphed|=player.IsAltForm;missile|=player.CurrentWeapon==BeamType.Missile;
                            if(SourceItems(scene).Count>0)
                            {if(stage.Name=="fire")sourceFire++;if(stage.Name=="charge")sourceCharge++;if(stage.Name=="charge-release")sourceRelease++;}
                        }
                        byte[] pixels=scene.ReadSceneTarget(out int width,out int height)??throw new InvalidDataException("Actual Scene target read failed.");
                        var observation=new{stage.Name,stage.Steps,SourceFrames=sourceFrames-beforeSource,Animations=animations.Order().ToArray(),
                            PixelSha256=Sha(pixels),Width=width,Height=height,OwnerCurrent=owner?.CannonCurrent};
                        stages.Add(observation);Console.WriteLine(JsonSerializer.Serialize(new{Probe="cannon-stage",Identity=selection?.SelectedAppearance.SkinSource??"native",observation}));
                    }
                    Check(fired&&charged&&morphed&&missile,"native input earns shot, charge, missile selection and morph");
                    if(probeBall)Console.WriteLine(JsonSerializer.Serialize(new{Probe="ball-run-summary",Identity=selection?.SelectedAppearance.SkinSource??"native",maxBombs,ballFrames,ballFallback,ballMoved}));
                    if(probeBall)Check(maxBombs>0&&died&&respawned&&(!expectsBall||ballFrames>0&&ballMoved&&afterLifeBallFrames>0),
                        "actual native input earns bomb, injected death/native respawn and moving source ball in both lives where selected");
                    if(probeTrail)Check(rollingTrailPictures>0&&trailPictures>0&&morphTrailGuards>0
                        &&(expectsBall?blueTrailPictures>0:nativeTrailPictures>0),
                        "actual native W rolling emits real TrailStack packets, with selected source tint and guarded native morph presentation");
                    var ballSchedules=new List<object>();
                    if(probeBall&&expectsBall)
                    {
                        foreach(Keys key in KeysUsed)Key(keyboard,key,false);
                        Key(keyboard,Keys.B,true);scene.OnSimulationFrame();Key(keyboard,Keys.B,false);
                        for(int n=0;n<110;n++){scene.OnSimulationFrame();Picture(false);}
                        Check(player.IsAltForm&&!player.IsMorphing&&BallItems(scene).Count==sourceBall!.Segments.Count,
                            "post-trace ball contracts start from a genuine settled native morph");
                        if(probeTrail)
                        {
                            Key(keyboard,Keys.W,true);
                            for(int n=0;n<60&&TrailItems(scene).Count==0;n++){scene.OnSimulationFrame();Picture(false,"trail-guard-prime");}
                            Check(TrailItems(scene).Count>0&&BallItems(scene).Count>0,"native rolling input primes genuine active trail before pause/fallback guards");
                        }
                        double frozenClock=owner!.BallMaterialSeconds;string frozenSkin=RawPalette(sourceBall!);
                        string frozenTrail=probeTrail?TrailHistory(player,activeOnly:false):"";
                        scene.GameState.PauseMenu();
                        try
                        {
                            for(int n=0;n<4;n++){scene.OnSimulationFrame();Picture(true);}
                            Check(owner.BallMaterialSeconds==frozenClock&&RawPalette(sourceBall!)==frozenSkin,
                                "paused simulation and repeated draw preserve fixed ball raw skin and material clock");
                            if(probeTrail)Check(TrailHistory(player,activeOnly:false)==frozenTrail,"pause leaves all18 native trail alphas/index/matrices frozen without fabricated updates");
                        }
                        finally{scene.GameState.UnpauseMenu();}
                        var flagsProperty=typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!;var flags=player.Flags2;
                        flagsProperty.SetValue(player,flags|PlayerFlags2.Cloaking);
                        try{Picture(true);Check(BallItems(scene).Count==0&&WorldItems(scene).Any(i=>nativeBallLists.Contains(i.ListId)),"cloak guard draws full native sphere with zero source ball packets");}
                        finally{flagsProperty.SetValue(player,flags);}
                        RenderOptions.Shadows=ShadowQuality.Low;
                        try{Picture(true);Check(BallItems(scene).Count==0&&WorldItems(scene).Any(i=>nativeBallLists.Contains(i.ListId)),"dynamic-shadow guard draws full native sphere with zero source packets");}
                        finally{RenderOptions.Shadows=ShadowQuality.Off;}
                        if(probeTrail)
                        {
                            RenderOptions.ShowCustomCosmetics=false;
                            try{Picture(true);Check(BallItems(scene).Count==0&&TrailItems(scene).Count>0,"cosmetics-off draws actual active native-color trail with complete native ball fallback");}
                            finally{RenderOptions.ShowCustomCosmetics=true;}
                        }
                        Picture(false);
                        Check(BallItems(scene).Count==sourceBall!.Segments.Count&&owner.BallMaterialSeconds==frozenClock&&RawPalette(sourceBall)==frozenSkin,
                            "guard restoration resumes the complete captured sphere without simulation, IO or clock advance");
                        if(probeTrail)Check(TrailHistory(player,activeOnly:false)==frozenTrail&&TrailItems(scene).Count>0,
                            "restoring source admission recolors only current trail packets and leaves canonical history unchanged");
                        Key(keyboard,Keys.W,true);
                        foreach(int rate in new[]{90,120,240,540})
                        {
                            FrameTiming.FrameRateCap=rate>FrameTiming.MaxCap?FrameTiming.Unlimited:rate;
                            FrameTiming.Reset();int stepsTotal=0,zeroSteps=0,fractional=0,firstPictures=ballFrames;
                            double firstClock=owner.BallMaterialSeconds;ulong firstStep=scene.FrameCount;
                            for(int picture=0;picture<rate;picture++)
                            {
                                int steps=FrameTiming.Advance(1d/rate);stepsTotal+=steps;if(steps==0)zeroSteps++;
                                if(FrameTiming.PresentationAlpha>1e-6&&FrameTiming.PresentationAlpha<1-1e-6)fractional++;
                                for(int step=0;step<steps;step++)scene.OnSimulationFrame();
                                Picture(true);
                                Check(RawPalette(sourceBall)==frozenSkin&&Math.Abs(owner.BallMaterialSeconds-firstClock-stepsTotal/60d)<1e-9,
                                    "fractional native roll pictures never advance the fixed source pose or independent material clock");
                            }
                            Check(scene.FrameCount-firstStep==(ulong)stepsTotal&&ballFrames-firstPictures==rate&&zeroSteps>0&&fractional>0,
                                "source ball follows the native specialized draw owner for every fractional-rate picture");
                            ballSchedules.Add(new{Rate=rate,Pictures=rate,CompletedSteps=stepsTotal,ZeroStepPictures=zeroSteps,FractionalPictures=fractional,
                                MaterialClockAdvance=owner.BallMaterialSeconds-firstClock,NativeRollPolicyUnchanged=true});
                        }
                        Key(keyboard,Keys.W,false);FrameTiming.FrameRateCap=oldCap;FrameTiming.Reset();
                        Key(keyboard,Keys.B,true);scene.OnSimulationFrame();Key(keyboard,Keys.B,false);
                        for(int n=0;n<110;n++){scene.OnSimulationFrame();Picture(false);}
                    }
                    if(expectsCannon)
                    {
                        Check(sourceFrames>0&&sourceFire>0&&sourceCharge>0&&sourceRelease>0,
                            "actual source cannon submits during native fire, charging and charge release");
                        foreach(Keys key in KeysUsed)Key(keyboard,key,false);
                        for(int n=0;n<300&&SourceItems(scene).Count==0;n++){scene.OnSimulationFrame();Picture(false);}
                        Check(SourceItems(scene).Count==cannon!.Segments.Count,"fallback fixtures start from complete source cannon");
                        // This source-cannon phase follows the captured parity
                        // trace. Its additional steps/pictures are separately
                        // counted and never presented as part of that trace.
                        int savedSourceFrames=sourceFrames,savedFallbackFrames=fallbackFrames,savedRepeats=repeats;
                        // Classic moving-reticle mode deliberately follows its
                        // eased camera rather than late pointer input. Select
                        // the existing responsive native viewmodel fixture here.
                        bool phasePro=Features.ProHud;Features.ProHud=false;
                        bool phaseCrosshair=Features.FixedCrosshair,phaseWeapon=Features.FixedWeapon;
                        Features.FixedCrosshair=true;Features.FixedWeapon=true;
                        try
                        {
                        foreach(int rate in new[]{90,120,240,540})
                        {
                            FrameTiming.FrameRateCap=rate>FrameTiming.MaxCap?FrameTiming.Unlimited:rate;
                            FrameTiming.Reset();FrameTiming.ResetDiagnostics();
                            int rateSource=sourceFrames,rateFallback=fallbackFrames,rateRepeats=repeats;
                            int completedSteps=0,fractionalPictures=0,changedOrientations=0,zeroStepPictures=0;
                            ulong firstFrame=scene.FrameCount;double firstClock=owner!.MaterialSeconds;
                            Matrix4? previousGun=null;double minimumAlpha=1,maximumAlpha=0;
                            string fixedSkin=RawPalette(cannon);
                            for(int picture=0;picture<rate;picture++)
                            {
                                Bound();ulong priorFrame=scene.FrameCount;double priorClock=owner.MaterialSeconds;
                                int steps=FrameTiming.Advance(1d/rate);completedSteps+=steps;
                                if(steps==0)zeroStepPictures++;
                                for(int step=0;step<steps;step++)scene.OnSimulationFrame();
                                Check(scene.FrameCount-priorFrame==(ulong)steps
                                    &&Math.Abs(owner.MaterialSeconds-priorClock-steps/60d)<1e-9,
                                    "fractional schedule advances native frame and source clock only by returned completed steps");
                                double alpha=FrameTiming.PresentationAlpha;minimumAlpha=Math.Min(minimumAlpha,alpha);maximumAlpha=Math.Max(maximumAlpha,alpha);
                                if(alpha>1e-6&&alpha<1-1e-6)fractionalPictures++;
                                scene.ModSetLateAim((picture%7-3)*.2f,(picture%5-2)*.1f);
                                Picture(true);
                                Check(player.ModGetFirstPersonGunTransform(out var preparedGun),"fractional picture retains prepared native first-person pose");
                                if(previousGun is { } previous&&Distance(previous.ClearTranslation(),preparedGun.ClearTranslation())>1e-7)changedOrientations++;
                                previousGun=preparedGun;
                                Check(scene.FrameCount-priorFrame==(ulong)steps
                                    &&Math.Abs(owner.MaterialSeconds-priorClock-steps/60d)<1e-9&&RawPalette(cannon)==fixedSkin,
                                    "fractional late aim and repeated pictures preserve fixed raw cannon skin and do not advance source clocks");
                            }
                            var schedule=new{Rate=rate,Pictures=rate,CompletedSteps=completedSteps,ZeroStepPictures=zeroStepPictures,
                                FractionalPictures=fractionalPictures,ChangedPreparedOrientations=changedOrientations,
                                ResponsiveNativeAim=Features.ResponsiveAimCamera,StaticNativeWeapon=Features.FixedWeapon,
                                MinimumAlpha=minimumAlpha,MaximumAlpha=maximumAlpha,SourcePictures=sourceFrames-rateSource,
                                FallbackPictures=fallbackFrames-rateFallback,RepeatedDraws=repeats-rateRepeats,
                                MaterialClockAdvance=owner.MaterialSeconds-firstClock};
                            fractionalSchedules.Add(schedule);Console.WriteLine(JsonSerializer.Serialize(new{Probe="cannon-fractional-schedule",schedule}));
                            Check(fractionalPictures>0,"simulated "+rate+" Hz earns fractional presentation alpha");
                            Check(changedOrientations>0,"simulated "+rate+" Hz earns changed prepared native gun orientations");
                            Check(zeroStepPictures>0,"simulated "+rate+" Hz earns pictures between completed simulation steps");
                            Check(sourceFrames-rateSource>0,"simulated "+rate+" Hz draws visible complete source cannon packets");
                            Check(scene.FrameCount-firstFrame==(ulong)completedSteps&&FrameTiming.TotalSteps==completedSteps
                                &&Math.Abs(owner.MaterialSeconds-firstClock-completedSteps/60d)<1e-9,
                                "per-rate native and source clocks agree with the full completed-step inventory");
                            scene.ModSetLateAim(0,0);
                        }
                        }
                        finally
                        {
                            Features.FixedCrosshair=phaseCrosshair;Features.FixedWeapon=phaseWeapon;Features.ProHud=phasePro;
                            scene.ModSetLateAim(0,0);
                        }
                        FrameTiming.Reset();FrameTiming.FrameRateCap=oldCap;
                        sourceFrames=savedSourceFrames;fallbackFrames=savedFallbackFrames;repeats=savedRepeats;
                        // Declared presentation-guard fixtures only: native
                        // movement ability execution is qualified elsewhere.
                        foreach(string abilityFlag in new[]{"_modMantling","_modWallrunning"})
                        {
                            var abilityField=typeof(PlayerEntity).GetField(abilityFlag,Hidden)!;
                            bool priorAbility=(bool)abilityField.GetValue(player)!;
                            string priorState=State(scene,player),priorRaw=RawPalette(cannon);
                            double priorClock=owner!.MaterialSeconds;
                            abilityField.SetValue(player,true);
                            try
                            {
                                Picture(true);
                                Check(SourceItems(scene).Count==0&&WorldItems(scene).Any(item=>item.ViewModel&&nativeGunLists.Contains(item.ListId)),
                                    abilityFlag+" presentation guard retains native gun with zero source cannon packets");
                                Check(State(scene,player)==priorState&&RawPalette(cannon)==priorRaw&&owner.MaterialSeconds==priorClock,
                                    abilityFlag+" guard drawing preserves native state, fixed source skin and source clock");
                            }
                            finally{abilityField.SetValue(player,priorAbility);}
                            Picture(false);
                            Check(SourceItems(scene).Count==cannon.Segments.Count&&owner.MaterialSeconds==priorClock,
                                abilityFlag+" guard restoration resumes complete source cannon without simulation");
                        }
                        // Accepted presentation-state fixture; controller execution is separate.
                        var grappleField=typeof(PlayerEntity).GetField("_modGrapple",Hidden)!;
                        var savedGrapple=player.ModGrappleState;
                        var injectedGrapple=new PlayerGrappleState(true,1,player.Position+new Vector3(0,2,4),4);
                        Check(injectedGrapple.IsValid,"injected grapple presentation fixture is valid");
                        string grappleState=State(scene,player),grapplePalette=RawPalette(cannon);
                        double grappleClock=owner!.MaterialSeconds;
                        try
                        {
                            grappleField.SetValue(player,injectedGrapple);Picture(true);
                            Check(SourceItems(scene).Count==0&&WorldItems(scene).Any(item=>item.ViewModel&&nativeGunLists.Contains(item.ListId)),
                                "accepted grapple presentation falls back to complete native first-person gun");
                        }
                        finally{grappleField.SetValue(player,savedGrapple);}
                        Picture(false);
                        Check(SourceItems(scene).Count==cannon.Segments.Count&&State(scene,player)==grappleState
                            &&RawPalette(cannon)==grapplePalette&&owner.MaterialSeconds==grappleClock,
                            "grapple restoration resumes source cannon with unchanged native state, raw palette and clock");
                        var flagsProperty=typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!;var flags=player.Flags2;
                        flagsProperty.SetValue(player,flags|PlayerFlags2.Cloaking);
                        try{Picture(false);Check(SourceItems(scene).Count==0,"cloaking falls back before any source cannon packet");}
                        finally{flagsProperty.SetValue(player,flags);}
                        RenderOptions.Shadows=ShadowQuality.Low;
                        try{Picture(false);Check(SourceItems(scene).Count==0,"dynamic shadows fall back before any source cannon packet");}
                        finally{RenderOptions.Shadows=ShadowQuality.Off;}
                        ushort damageTimer=(ushort)typeof(PlayerEntity).GetField("_timeSinceDamage",Hidden)!.GetValue(player)!;
                        player.TakeDamage(10,DamageFlags.IgnoreInvuln|DamageFlags.NoDmgInvuln,null,null);
                        try{Picture(false);Check(SourceItems(scene).Count==0,"native damage flash falls back before any source cannon packet");}
                        finally{typeof(PlayerEntity).GetField("_timeSinceDamage",Hidden)!.SetValue(player,damageTimer);}
                        int[] lists=cannon.Segments.Select(segment=>segment.ListId).ToArray(),ballLists=sourceBall?.Segments.Select(s=>s.ListId).ToArray()??[];
                        CharacterModelRuntime.Release(scene);
                        Check(!owner!.CannonCurrent&&lists.All(id=>!GL.IsList(id)),"context retirement deletes cannon geometry and authority");
                        Check(sourceBall==null||!owner.BallCurrent&&ballLists.All(id=>!GL.IsList(id)),"context retirement deletes every source sphere batch and its authority");
                        scene.OnDrawFrame();Check(SourceItems(scene).Count==0&&!owner.CannonCurrent,"retired cannon never resurrects on draw");
                        if(probeBall&&expectsBall)
                        {
                            foreach(Keys key in KeysUsed)Key(keyboard,key,false);
                            Key(keyboard,Keys.B,true);scene.OnSimulationFrame();Key(keyboard,Keys.B,false);
                            for(int n=0;n<110;n++){scene.OnSimulationFrame();Picture(false);}
                            Check(player.IsAltForm&&!player.IsMorphing&&!owner.BallCurrent&&BallItems(scene).Count==0
                                &&WorldItems(scene).Any(i=>nativeBallLists.Contains(i.ListId)),
                                "native remorph after context retirement draws native sphere without IO or source resurrection");
                            if(probeTrail)
                            {
                                Key(keyboard,Keys.W,true);
                                for(int n=0;n<60&&TrailItems(scene).Count==0;n++){scene.OnSimulationFrame();Picture(true,"trail-retired-roll");}
                                Check(TrailItems(scene).Count>0&&BallItems(scene).Count==0&&!owner.BallCurrent,
                                    "retired source context still earns genuine native-color trail through real native rolling input");
                                Key(keyboard,Keys.W,false);
                            }
                        }
                    }
                    else Check(sourceFrames==0,"native first-person path retains native gun rendering");
                    return new(selection?.SelectedAppearance.SkinSource??"native",trace.ToArray(),stages.ToArray(),sourceFrames,fallbackFrames,repeats,maxTip,maxPalette,fractionalSchedules.ToArray(),
                        probeBall?new{BallFrames=ballFrames,AfterLifeBallFrames=afterLifeBallFrames,NativeBallFallbackPictures=ballFallback,Moving=ballMoved,
                            Died=died,Respawned=respawned,MaximumPaletteOrCenterError=maxBallError,CollisionRadiusFitError=ballRadiusError,
                            MaximumOwnedBombs=maxBombs,FractionalSchedules=ballSchedules}:null,
                        probeTrail?trailTrace.ToArray():null,probeTrail?new{TrailPictures=trailPictures,BlueTrailPictures=blueTrailPictures,NativeTrailPictures=nativeTrailPictures,
                            RollingTrailPictures=rollingTrailPictures,MorphGuardPictures=morphTrailGuards,MaximumSegments=maxTrailSegments,Slots=18,
                            TraceGrain="one post-step record per original1415-step script; all18 alphas/ring index, matrices only for alpha>0, plus native global RNG; inactive matrix storage intentionally excluded across Scenes"}:null);

                    void Picture(bool repeat,string? label=null)
                    {
                        string canonicalTrail=probeTrail?TrailHistory(player,activeOnly:false):"";uint rngBefore1=Rng.Rng1,rngBefore2=Rng.Rng2;
                        scene.OnDrawFrame();var items=SourceItems(scene);
                        if(probeTrail)
                        {
                            bool hasTrail=TrailItems(scene).Count>0;
                            if(canonicalTrail!=TrailHistory(player,activeOnly:false)||hasTrail&&(rngBefore1!=Rng.Rng1||rngBefore2!=Rng.Rng2))
                                Console.WriteLine(JsonSerializer.Serialize(new{Probe="trail-draw-canonical-diagnostic",Identity=selection?.SelectedAppearance.SkinSource??"native",Stage=label,
                                    SameCanonicalHistory=canonicalTrail==TrailHistory(player,activeOnly:false),RngBefore1=rngBefore1,RngAfter1=Rng.Rng1,RngBefore2=rngBefore2,RngAfter2=Rng.Rng2,
                                    player.IsAltForm,player.IsMorphing,player.IsUnmorphing,player.Health,SourceBallPackets=BallItems(scene).Count,TrailPackets=TrailItems(scene).Count}));
                            Check(canonicalTrail==TrailHistory(player,activeOnly:false)&&(!hasTrail||rngBefore1==Rng.Rng1&&rngBefore2==Rng.Rng2),
                                "draw-only trail presentation preserves every canonical slot/alpha/index/matrix and emitted-trail native RNG");
                            ObserveTrail(label,count:true);
                        }
                        if(probeBall)
                        {
                            var balls=BallItems(scene);
                            if(balls.Count>0)
                            {
                                Check(expectsBall&&owner!.BallCurrent&&owner.BallReady&&balls.Count==sourceBall!.Segments.Count
                                    &&balls.All(i=>!i.ViewModel&&i.SourceGx!.AlternateForm&&i.SourceGx.BodyOwner==owner),
                                    "actual source-ball packets are complete, world-projected and owned by the current local actor");
                                Check(WorldItems(scene).All(i=>!nativeBallLists.Contains(i.ListId)),"source ball replaces native sphere meshes completely");
                                player.ModGetDrawActorPose(out var drawPosition,out _);
                                var roll=player.ModGetAlternateFormDrawPose(drawPosition,out _);roll.Row3.Y+=Fixed.ToFloat(player.Values.AltColYPos);
                                var expected=owner!.BallFit*roll;var center=drawPosition.AddY(Fixed.ToFloat(player.Values.AltColYPos));
                                foreach(var item in balls)
                                {
                                    var segment=sourceBall!.Segments.Single(s=>s.ListId==item.ListId);
                                    Check(owner.BallMaterialSeconds==owner.BallCompletedSteps/60d,"ball material time and heartbeat share completed source step authority");
                                    double phase=(owner.BallCompletedSteps%48)/48d;
                                    double G(double c,double w)=>Math.Exp(-Math.Pow((phase-c)/w,2));
                                    float beat=(float)(1+.3*G(.26,.07)+.15*G(.55,.06)-.3*G(.10,.05)-.18*G(.42,.045));
                                    var heart=final&&segment.SourcePrimitiveOrdinal==5
                                        ?Matrix4.CreateTranslation(-ballCenter)*Matrix4.CreateScale(beat)*Matrix4.CreateTranslation(ballCenter):Matrix4.Identity;
                                    var expectedPart=heart*expected;
                                    double error=(Vector3.TransformPosition(ballCenter,item.Transform)-center).Length;
                                    maxBallError=Math.Max(maxBallError,error);
                                    Check(error<.0001&&Distance(item.Transform,expectedPart)<.0001,"source ball follows native roll and sphere centre; only Final heart receives independently evaluated pulse");
                                    for(int slot=0;slot<segment.SourceJointPalette!.Count;slot++)
                                    {
                                        var raw=ballSkin![segment.SourceJointPalette[slot]];double delta=Distance(ReadMatrix(item.MatrixStack,slot*16),raw*expectedPart);
                                        maxBallError=Math.Max(maxBallError,delta);
                                        Check(delta<.0001&&Distance(ReadMatrix(segment.SourceMatrixPalette!,slot*16),raw)<.00002,
                                            "fixed original two-joint source skin composes with native roll once without raw travel or native palette substitution");
                                    }
                                }
                                ballFirst??=player.Position;ballMoved|=(player.Position-ballFirst.Value).Length>.1f;ballFrames++;
                            }
                            else if(player.IsAltForm)ballFallback++;
                            if(player.IsMorphing||player.IsUnmorphing||!player.IsAltForm||!expectsBall)
                                Check(balls.Count==0,"both morph transitions and native choices retain complete native ball presentation");
                        }
                        Check(items.All(item=>item.ViewModel),"first-person source collection contains only viewmodel geometry");
                        if(items.Count>0)
                        {
                            Check(expectsCannon&&owner!.CannonCurrent&&cannon!=null&&items.Count==cannon.Segments.Count
                                &&items.All(item=>item.SourceGx is { } source&&source.BodyOwner==owner&&ReferenceEquals(source.Binding.Owner,scene)
                                    &&item.WeightedSkinning&&item.MatrixStackCount is >0 and <=32)
                                &&items.Select(item=>item.ListId).Order().SequenceEqual(cannon.Segments.Select(segment=>segment.ListId).Order()),
                                "actual cannon source packets are complete and retain current owner");
                            Check(WorldItems(scene).All(item=>item.SourceGx==null&&!nativeGunLists.Contains(item.ListId)),
                                "source cannon replaces native gun meshes without entering native multipass lists");
                            Check(player.ModGetFirstPersonGunTransform(out var gunTransform)&&player.ModGetFirstPersonEffectTransform(out _),
                                "native prepared first-person gun and effect transforms remain available");
                            player.ModGetFirstPersonEffectTransform(out var effect);
                            float scale=owner!.FeetFit.Row0.Xyz.Length*.7f;
                            Matrix4 caller=Matrix4.CreateScale(scale)*Matrix4.CreateTranslation(0,0,Fixed.ToFloat(player.Values.MuzzleOffset))*gunTransform;
                            Matrix4 expectedTransform=geometry!.SourceCannon!.ToCannon*caller;
                            int tip=Enumerable.Range(0,geometry.SourceRig!.Nodes.Count).Single(i=>geometry.SourceRig.Nodes[i].Name==(trooper?"source_bone_34_R_gun_LCTR":"source_bone_32_GUN_Particle_LCTR"));
                            Vector3 nativeTip=Vector3.TransformPosition(new(0,0,Fixed.ToFloat(player.Values.MuzzleOffset)),gunTransform);
                            foreach(string effectName in new[]{"_chargeEffect","_muzzleEffect"})
                                if(typeof(PlayerEntity).GetField(effectName,Hidden)!.GetValue(player) is EffectEntry active
                                    &&active.DrawTransformOverride is { } drawEffect)
                                    Check(active.DrawTransformUsesViewModel&&(drawEffect.Row3.Xyz-nativeTip).Length<.00002,
                                        "native one-picture muzzle/charge effect remains aligned under viewmodel projection");
                            foreach(var item in items)
                            {
                                double tipError=(Vector3.TransformPosition(globals![tip].Row3.Xyz,item.Transform)-nativeTip).Length;
                                maxTip=Math.Max(maxTip,tipError);
                                Check(tipError<.0002&&(nativeTip-effect.Row3.Xyz).Length<.00002,
                                    "independent raw source locator lands on unchanged native effect muzzle");
                                Check(Distance(item.Transform,expectedTransform)<.0002,"raw texgen transform includes exactly one source-to-cannon basis");
                                var segment=cannon!.Segments.Single(s=>s.ListId==item.ListId);
                                for(int slot=0;slot<segment.SourceJointPalette!.Count;slot++)
                                {
                                    Matrix4 expectedRaw=skin![segment.SourceJointPalette[slot]],expected=expectedRaw*geometry.SourceCannon.ToCannon*caller;
                                    double difference=Distance(ReadMatrix(item.MatrixStack,slot*16),expected);maxPalette=Math.Max(maxPalette,difference);
                                    Check(Distance(ReadMatrix(segment.SourceMatrixPalette!,slot*16),expectedRaw)<.00002&&difference<.0002,
                                        "independent fixed source skin and exactly-once viewmodel palette composition match production");
                                }
                            }
                            sourceFrames++;
                        }
                        else if(expectsCannon)fallbackFrames++;
                        if(player.IsAltForm||player.IsMorphing||player.IsUnmorphing||player.CurrentWeapon!=BeamType.PowerBeam)
                            Check(items.Count==0,"native form/weapon transitions have zero partial source cannon submissions");
                        Check(scene.OnRenderFrame(),"actual native Scene renders first-person cannon frame");
                        if(probeBall&&BallItems(scene).Count>0)
                        {
                            var worldProjection=(Matrix4)typeof(Scene).GetField("_perspectiveMatrix",Hidden)!.GetValue(scene)!;
                            foreach(var group in BallItems(scene).GroupBy(i=>i.SourceGx!.Binding.ProgramId))
                            {
                                int program=group.Key;float[] projectionValues=new float[16],skinValues=new float[16];
                                GL.GetUniform(program,GL.GetUniformLocation(program,"gx_projection"),projectionValues);
                                GL.GetUniform(program,GL.GetUniformLocation(program,"gx_skin[0]"),skinValues);
                                Check(Distance(ReadMatrix(projectionValues,0),worldProjection)<.00002
                                    &&Distance(ReadMatrix(skinValues,0),ReadMatrix(group.Last().MatrixStack,0))<.00002,
                                    "actual source-ball GPU uniforms retain world projection and latest part palette, including heart pulse");
                            }
                        }
                        if(probeBall&&BallItems(scene).Count>0)
                        {
                            Matrix4 projection=(Matrix4)typeof(Scene).GetField("_perspectiveMatrix",Hidden)!.GetValue(scene)!;
                            foreach(int program in BallItems(scene).Select(i=>i.SourceGx!.Binding.ProgramId).Distinct())
                            {
                                float[] actual=new float[16];GL.GetUniform(program,GL.GetUniformLocation(program,"gx_projection"),actual);
                                Check(Distance(ReadMatrix(actual,0),projection)<.00002,"actual source sphere uses world camera projection");
                            }
                            if(!string.IsNullOrWhiteSpace(capturesRoot)&&label is "ball-roll" or "ball-bomb"
                                &&(!probeTrail||TrailItems(scene).Any(trail=>trail.ItemCount>=12))&&captured.Add(label))
                            {
                                string path=Path.Combine(capturesRoot,selection!.SelectedAppearance.SkinSource+"-"+label+".png");
                                Check(ScreenCapture.SaveWindow(scene,path),"optional actual source ball frame saved");captures.Add(new{Path=path,Sha256=HashFile(path),SourcePackets=BallItems(scene).Count,
                                    TrailSegments=TrailItems(scene).Select(trail=>trail.ItemCount).ToArray()});
                            }
                        }
                        if(items.Count>0)
                        {
                            Matrix4 vm=(Matrix4)typeof(Scene).GetField("_viewModelPerspectiveMatrix",Hidden)!.GetValue(scene)!,
                                world=(Matrix4)typeof(Scene).GetField("_perspectiveMatrix",Hidden)!.GetValue(scene)!;
                            Check(Distance(vm,world)>.01,"fixture has distinct widened world and authored viewmodel projections");
                            foreach(int program in items.Select(item=>item.SourceGx!.Binding.ProgramId).Distinct())
                            {
                                float[] actual=new float[16];GL.GetUniform(program,GL.GetUniformLocation(program,"gx_projection"),actual);
                                Check(Distance(ReadMatrix(actual,0),vm)<.00002,"actual source GX program uses viewmodel FOV rather than widened world FOV");
                            }
                            if(!probeTrail&&!string.IsNullOrWhiteSpace(capturesRoot)&&label is "idle" or "fire"&&captured.Add(label))
                            {
                                string path=Path.Combine(capturesRoot,selection!.SelectedAppearance.SkinSource+"-cannon-"+label+".png");
                                Check(ScreenCapture.SaveWindow(scene,path),"explicit optional cannon gameplay PNG saved");
                                captures.Add(new{Path=path,Sha256=HashFile(path),SourcePackets=items.Count});
                            }
                        }
                        if(repeat)
                        {
                            string state=State(scene,player),raw=cannon==null?"":RawPalette(cannon);double? clock=owner?.MaterialSeconds;
                            string ballRaw=sourceBall==null?"":RawPalette(sourceBall),bombState=probeBall?BombState(scene,player):"";double? ballClock=owner?.BallMaterialSeconds;
                            string trailPackets=probeTrail?TrailPacketHash(scene):"",trailState=probeTrail?TrailHistory(player,activeOnly:false):"";bool hadTrail=probeTrail&&TrailItems(scene).Count>0;
                            uint trailRng1=Rng.Rng1,trailRng2=Rng.Rng2;
                            scene.OnDrawFrame();Check(scene.OnRenderFrame(),"repeated actual first-person frame renders");
                            if(probeTrail)
                            {
                                ObserveTrail(label,count:false);
                                Check(TrailPacketHash(scene)==trailPackets&&TrailHistory(player,activeOnly:false)==trailState&&(!hadTrail||Rng.Rng1==trailRng1&&Rng.Rng2==trailRng2),
                                    "repeated draw preserves native trail packet geometry/alpha/index/matrices/color, full canonical history and RNG");
                            }
                            Check(State(scene,player)==state&&owner?.MaterialSeconds==clock&&(cannon==null||RawPalette(cannon)==raw),
                                "repeated draw preserves native effects/projectiles/aim/ammo/RNG and fixed raw cannon/owner clock");repeats++;
                            Check(!probeBall||ballClock==owner?.BallMaterialSeconds&&(sourceBall==null||RawPalette(sourceBall)==ballRaw)&&BombState(scene,player)==bombState,
                                "repeated draw preserves fixed source sphere, its clock and native bombs");
                        }
                        Check(GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,"actual source/native first-person frame is GL-clean");
                        DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();
                    }

                    void ObserveTrail(string? label,bool count)
                    {
                        var trails=TrailItems(scene);bool submitted=BallItems(scene).Count>0;
                        if(count&&(player.IsMorphing||player.IsUnmorphing)){morphTrailGuards++;Check(!submitted,"morph/unmorph never grants source-blue trail authority");}
                        if(count&&trails.Count>0)
                        {trailPictures++;maxTrailSegments=Math.Max(maxTrailSegments,trails.Max(trail=>trail.ItemCount));if(submitted)blueTrailPictures++;else nativeTrailPictures++;if(label is "ball-roll" or "ball-after-life-roll")rollingTrailPictures++;}
                        var alphas=(float[,])typeof(PlayerEntity).GetField("_mbTrailAlphas",Hidden)!.GetValue(player)!;
                        var matrices=(Matrix4[,])typeof(PlayerEntity).GetField("_mbTrailMatrices",Hidden)!.GetValue(player)!;
                        var indices=(int[])typeof(PlayerEntity).GetField("_mbTrailIndices",Hidden)!.GetValue(player)!;int slot=player.SlotIndex,index=indices[slot];
                        Check(alphas.GetLength(1)==18&&matrices.GetLength(1)==18&&index is >=0 and <18,"native trail retains exact18-slot ring storage and bounded canonical cursor");
                        var expectedColor=submitted?new Vector3(90/255f,200/255f,1):new Vector3(1,27/31f,11/31f);
                        foreach(var trail in trails)
                        {
                            Check(trail.MatrixStackCount==18&&trail.Alpha==1&&trail.ItemCount is >0 and <=18
                                &&trail.TextureBindingId==(int)typeof(PlayerEntity).GetField("_trailBindingId1",Hidden)!.GetValue(player)!,
                                "actual native TrailStack retains its native texture,18-matrix stack, alpha and bounded geometry");
                            for(int i=0;i<18;i++)Check(Distance(ReadMatrix(trail.MatrixStack,i*16),matrices[slot,i])==0,"every submitted native trail matrix remains bit-exact canonical storage");
                            int pairs=0;
                            for(int i=0;i<18;i++)
                            {
                                int first=(index-1-i+18)%18,second=(first+17)%18;float a=alphas[slot,first],b=alphas[slot,second];
                                if(a<=0||b<=0)continue;pairs++;
                                float u1=(31-(int)(a*31))/32f,u2=(31-(int)(b*31))/32f;
                                Vector3[] points=[new(u1,0,first),new(0,.375f,0),new(u1,1,first),new(0,-.375f,0),
                                    new(u2,1,second),new(0,-.375f,0),new(u2,0,second),new(0,.375f,0)];
                                Check(trail.Points.AsSpan(i*8,8).SequenceEqual(points),"native ring alpha selects unchanged UV/fade, slot indices and ribbon vertices");
                            }
                            Check(trail.ItemCount==pairs,"submitted native trail segment count exactly matches real active ring pairs");
                            if(trail.Diffuse!=expectedColor)Console.WriteLine(JsonSerializer.Serialize(new{Probe="source-ball-trail-color",Identity=selection?.SelectedAppearance.SkinSource??"native",Stage=label,
                                SourceBallSubmitted=submitted,TrailSegments=trail.ItemCount,Slots=trail.MatrixStackCount,ActualRgb=new[]{trail.Diffuse.X,trail.Diffuse.Y,trail.Diffuse.Z},ExpectedRgb=new[]{expectedColor.X,expectedColor.Y,expectedColor.Z}}));
                            Check(trail.Diffuse==expectedColor,"actual native TrailStack is source-blue exactly when this picture submitted its complete current source ball; otherwise native gold");
                        }
                    }
                }
                finally{if(scene!=null)Release(scene);}
            }
        }
        catch(Exception exception){Console.Error.WriteLine("OWNED CANNON RUNTIME failure: "+exception);error=exception.GetType().Name;errorSha=Sha(Encoding.UTF8.GetBytes(exception.Message));}
        finally
        {
            try
            {
                Directory.SetCurrentDirectory(cwd);Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",oldData);
                string target=Path.GetFullPath(temporary);
                if(!target.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)
                    ||!Path.GetFileName(target).StartsWith("prime-cannon-runtime-",StringComparison.Ordinal))throw new InvalidDataException("Temporary cannon cleanup escaped root.");
                if(Directory.Exists(target))Directory.Delete(target,true);cleaned=!Directory.Exists(target);
                if(sourceBefore!=null)sourceAfter=HashFile(rvz);if(nativeBefore!=null)nativeAfter=Inventory(native!);
                Check(cleaned&&sourceBefore!=null&&sourceBefore==sourceAfter&&nativeBefore!=null&&nativeBefore==nativeAfter,"derived imports deleted and both owned sources unchanged");
            }
            catch(Exception exception){error??=exception.GetType().Name;errorSha??=Sha(Encoding.UTF8.GetBytes(exception.Message));}
            RenderOptions.CharacterModelReplacements=oldHd;RenderOptions.BrightSkins=oldBright;RenderOptions.ShowCustomCosmetics=oldCosmetics;
            RenderOptions.Shadows=oldShadow;RenderOptions.FieldOfView=oldFov;RenderOptions.ShowFps=oldFps;MapAudit.ForceEveryone=oldForce;
            NetTestScript.Enabled=oldScript;GamepadContexts.Focused=focus;GamepadContexts.MenuVisible=menu;
            GamepadContexts.Capturing=capturing;GamepadContexts.TextEntryActive=text;PointerInput.StylusMode=stylus;
            PointerInput.Reset();PointerDevice.Reset();FrameTiming.FrameRateCap=oldCap;FrameTiming.Reset();
            Console.WriteLine(JsonSerializer.Serialize(new{Pass=error==null,Checks=checks,FailedCheck=failed,ErrorType=error,ErrorMessageSha256=errorSha,
                CoreSha256=coreSha,ToolSha256=toolSha,Renderer=renderer,GlVersion=version,SourceSha256Before=sourceBefore,SourceSha256After=sourceAfter,
                NativeSha256Before=nativeBefore,NativeSha256After=nativeAfter,TemporaryDerivedDataDeleted=cleaned,Captures=captures,
                Runs=runs.Select(run=>new{run.Identity,Frames=run.Trace.Length,GameplayTraceSha256=Sha(Encoding.UTF8.GetBytes(string.Join("\n",run.Trace))),
                    run.Stages,run.CannonFrames,run.FallbackFrames,run.RepeatedDraws,run.MaximumTipError,run.MaximumPaletteError,run.FractionalSchedules,run.Ball,
                    TrailHistoryFrames=run.TrailTrace?.Length,TrailHistoryTraceSha256=run.TrailTrace==null?null:Sha(Encoding.UTF8.GetBytes(string.Join("\n",run.TrailTrace))),run.Trail}),
                Scope="Actual owned offline input/Scene/GX; fixed "+(trooper?"Weavel/four Trooper206tri/35joint/three-material":"Samus/normal392tri+Final793tri/39joint/own source materials+Final shell alpha")+" source cannon; "+(probeBall?"normal2220tri and Final8170tri two-joint source balls with independent material/heart policy,1415-step parity trace with native roll/bomb/injected death/native respawn plus separately counted ball pause/guards/fractional schedules/retirement;":"675-step parity trace;")+" native muzzle/effect/weapon authority plus separately counted source-cannon simulated90/120/240/540 schedules with fractional late aim; common pre-script RNG seed and status fixtures declared",
                TrailScope=probeTrail?"actual native TrailStack packet tint only, completed-step active native history parity; no fabricated emitter or gameplay ticks; no other core/boost/furl effects":null,
                Unqualified=new[]{"physical controls","Android/macOS","online/replay","source cannon skeletal animation","other weapons","achieved refresh rate","visual art completeness"}}));
        }
        return error==null?0:1;
    }

    private static List<RenderItem> AllSourceItems(Scene scene)=>(List<RenderItem>)typeof(Scene).GetField("_sourceBodyItems",Hidden)!.GetValue(scene)!;
    private static List<RenderItem> SourceItems(Scene scene)=>AllSourceItems(scene).Where(i=>i.ViewModel).ToList();
    private static List<RenderItem> BallItems(Scene scene)=>AllSourceItems(scene).Where(i=>i.SourceGx?.AlternateForm==true).ToList();
    private static List<RenderItem> TrailItems(Scene scene)=>WorldItems(scene).Where(i=>i.Type==RenderItemType.TrailStack).ToList();
    private static string TrailHistory(PlayerEntity player,bool activeOnly)
    {
        var alphas=(float[,])typeof(PlayerEntity).GetField("_mbTrailAlphas",Hidden)!.GetValue(player)!;
        var matrices=(Matrix4[,])typeof(PlayerEntity).GetField("_mbTrailMatrices",Hidden)!.GetValue(player)!;
        var indices=(int[])typeof(PlayerEntity).GetField("_mbTrailIndices",Hidden)!.GetValue(player)!;
        using var bytes=new MemoryStream();using var writer=new BinaryWriter(bytes);int slot=player.SlotIndex;
        writer.Write(slot);writer.Write(alphas.GetLength(1));writer.Write(indices[slot]);
        for(int i=0;i<alphas.GetLength(1);i++)
        {writer.Write(alphas[slot,i]);if(!activeOnly||alphas[slot,i]>0)for(int r=0;r<4;r++)for(int c=0;c<4;c++)writer.Write(matrices[slot,i][r,c]);}
        return Sha(bytes.ToArray());
    }
    private static string TrailPacketHash(Scene scene)
    {
        using var bytes=new MemoryStream();using var writer=new BinaryWriter(bytes);var trails=TrailItems(scene);writer.Write(trails.Count);
        foreach(var item in trails)
        {
            writer.Write(item.Diffuse.X);writer.Write(item.Diffuse.Y);writer.Write(item.Diffuse.Z);writer.Write(item.Alpha);writer.Write(item.ItemCount);writer.Write(item.MatrixStackCount);writer.Write(item.TextureBindingId);
            foreach(float value in item.MatrixStack.Take(16*item.MatrixStackCount))writer.Write(value);
            foreach(var point in item.Points.Take(8*item.ItemCount)){writer.Write(point.X);writer.Write(point.Y);writer.Write(point.Z);}
        }
        return Sha(bytes.ToArray());
    }
    private static IEnumerable<BombEntity> SceneBombs(Scene scene)=>((IEnumerable<EntityBase>)typeof(Scene).GetField("_entities",Hidden)!.GetValue(scene)!).OfType<BombEntity>();
    private static string BombState(Scene scene,PlayerEntity player)
    {
        using var bytes=new MemoryStream();using var writer=new BinaryWriter(bytes);
        foreach(var bomb in SceneBombs(scene)){writer.Write((int)bomb.BombType);writer.Write(bomb.Countdown);writer.Write((int)bomb.Flags);writer.Write(bomb.Position.X);writer.Write(bomb.Position.Y);writer.Write(bomb.Position.Z);
            writer.Write(bomb.Radius);writer.Write(bomb.SelfRadius);writer.Write(bomb.Damage);writer.Write(bomb.EnemyDamage);writer.Write(ReferenceEquals(bomb.Owner,player));}
        return Sha(bytes.ToArray());
    }
    private static IEnumerable<RenderItem> WorldItems(Scene scene)=>new[]{"_nonDecalItems","_translucentItems","_decalItems"}
        .SelectMany(name=>(List<RenderItem>)typeof(Scene).GetField(name,Hidden)!.GetValue(scene)!);
    private static Matrix4[] Globals(CharacterSourceRig rig,IReadOnlyList<Matrix4> pose)
    {
        var result=new Matrix4[pose.Count];var done=new bool[pose.Count];
        Matrix4 Resolve(int i){if(!done[i]){int parent=rig.Nodes[i].Parent;result[i]=parent<0?pose[i]:pose[i]*Resolve(parent);done[i]=true;}return result[i];}
        for(int i=0;i<pose.Count;i++)Resolve(i);return result;
    }
    private static Matrix4 ReadMatrix(float[] values,int offset)=>new(values[offset],values[offset+1],values[offset+2],values[offset+3],
        values[offset+4],values[offset+5],values[offset+6],values[offset+7],values[offset+8],values[offset+9],values[offset+10],values[offset+11],
        values[offset+12],values[offset+13],values[offset+14],values[offset+15]);
    private static double Distance(Matrix4 a,Matrix4 b){double maximum=0;for(int r=0;r<4;r++)for(int c=0;c<4;c++)maximum=Math.Max(maximum,Math.Abs(a[r,c]-b[r,c]));return maximum;}
    private static string RawPalette(CharacterWeightedRenderModel model)
    {using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);foreach(var s in model.Segments)foreach(float value in s.SourceMatrixPalette!)writer.Write(value);return Sha(stream.ToArray());}
    private static string State(Scene scene,PlayerEntity player)
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        void Vector(Vector3 value){writer.Write(value.X);writer.Write(value.Y);writer.Write(value.Z);}
        writer.Write(scene.FrameCount);writer.Write(scene.Random.Rng1);writer.Write(scene.Random.Rng2);writer.Write(player.Health);
        Vector(player.Position);Vector(player.Speed);Vector(player.FacingVector);writer.Write((int)player.CurrentWeapon);writer.Write((int)player.GunAnimation);
        writer.Write(player.EquipInfo.ChargeLevel);writer.Write((uint)player.Flags1);writer.Write(player.IsAltForm);writer.Write(player.IsMorphing);writer.Write(player.IsUnmorphing);
        foreach(string name in new[]{"_aimVec","_gunVec1","_gunVec2","_muzzlePos","_gunDrawPos"})Vector((Vector3)typeof(PlayerEntity).GetField(name,Hidden)!.GetValue(player)!);
        foreach(int ammo in (int[])typeof(PlayerEntity).GetField("_ammo",Hidden)!.GetValue(player)!)writer.Write(ammo);
        foreach(var beam in player.EquipInfo.Beams){writer.Write(beam.Lifespan);Vector(beam.Position);Vector(beam.Direction);writer.Write((int)beam.Flags);writer.Write(ReferenceEquals(beam.Owner,player));}
        foreach(string name in new[]{"_chargeEffect","_muzzleEffect"})
        {
            var effect=typeof(PlayerEntity).GetField(name,Hidden)!.GetValue(player) as EffectEntry;writer.Write(effect!=null);
            if(effect!=null)
            {
                writer.Write(effect.EffectId);writer.Write(effect.Elements.Count);
                foreach(var element in effect.Elements)
                {Matrix4 matrix=element.OwnTransform;for(int r=0;r<4;r++)for(int c=0;c<4;c++)writer.Write(matrix[r,c]);writer.Write(element.Particles.Count);}
            }
        }
        return Sha(stream.ToArray());
    }
    private sealed class SourceFileLocks : IDisposable
    {
        private readonly List<FileStream> _files = new();
        internal int Count => _files.Count;
        internal SourceFileLocks(string root)
        {
            try
            {
                foreach (string path in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories))
                    _files.Add(new(path,FileMode.Open,FileAccess.Read,FileShare.None));
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { foreach(var file in _files)file.Dispose();_files.Clear(); }
    }
    private static void Release(Scene scene)
    {var commit=PostMatchCommitContext.Capture(scene.GameState,0,SaveWhen.Never,0);try{scene.DoCleanup();}finally{try{scene.UnloadGl();}finally{commit.Commit();commit.Release();ForegroundSceneBinding.TryRelease(commit);}}}
    private static string HashFile(string file){using var stream=File.OpenRead(file);return Convert.ToHexStringLower(SHA256.HashData(stream));}
    private static string Sha(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Inventory(string root)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var pending=new Stack<string>();pending.Push(root);int count=0;long bytes=0;
        while(pending.Count>0){string folder=pending.Pop();if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Native inventory rejects aliases.");
            foreach(string child in Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal).Reverse())pending.Push(child);
            foreach(string file in Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal))
            {if(++count>65536||(bytes+=new FileInfo(file).Length)>1024L*1024*1024||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Native inventory budget exceeded.");
                hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root,file)));hash.AppendData(new byte[]{0});hash.AppendData(Convert.FromHexString(HashFile(file)));}}
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
