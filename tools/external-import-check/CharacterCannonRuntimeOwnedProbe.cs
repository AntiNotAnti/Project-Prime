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
        int FallbackFrames, int RepeatedDraws, double MaximumTipError, double MaximumPaletteError, object[] FractionalSchedules);

    internal static int Run(string rvz)
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
            RetroCharacterPreviewExporter.ExportFromOwnedGame(rvz,imports,selectedIds:["darksamus-echoes","darksamus-overcharged-echoes"]);
            var normal=CharacterSourcePreviewSelection.LoadForLocalBody("darksamus-echoes",CancellationToken.None,imports);
            var final=CharacterSourcePreviewSelection.LoadForLocalBody("darksamus-overcharged-echoes",CancellationToken.None,imports);
            Check(normal.HasCannon && !final.HasCannon,"Final retains no borrowed normal cannon capability");
            Check(normal.Pack.TryResolvePrivate(Hunter.Samus,normal.SelectedAppearance,CharacterModelPart.Biped,0,out var normalBody),
                "normal cannon comparison resolves canonical source body");
            Check(normal.Pack.TryCreateSourcePreviewLease(normalBody,out var normalLease) && normalLease.SourceMaterials!=null,
                "normal cannon comparison captures canonical normal body GX archive");
            var canonicalMaterials=normalLease!.SourceMaterials!;
            var settings=DesktopGlContext.Settings(background:true); settings.ClientSize=new(640,480);
            settings.StartVisible=true; settings.StartFocused=false; settings.Title="Project Prime actual owned source cannon runtime";
            using var window=new NativeWindow(settings); using var graphics=new DesktopGraphicsSession(window); GLFW.SwapInterval(0);
            renderer=GL.GetString(StringName.Renderer);version=GL.GetString(StringName.Version);
            foreach(var selection in new CharacterSourcePreviewSelection?[]{null,normal,final})
            {
                Result result=Exercise(selection); runs.Add(result);
                if(runs.Count>1)
                {
                    Result baseline=runs[0];
                    Check(result.Trace.Length==baseline.Trace.Length,"first-person parity traces have matching lengths");
                    int mismatch=Enumerable.Range(0,result.Trace.Length).FirstOrDefault(i=>result.Trace[i]!=baseline.Trace[i],-1);
                    Check(mismatch<0,result.Identity+" native health/actor/ammo/aim/projectile/effect/RNG parity; first mismatch="+mismatch);
                }
            }

            Result Exercise(CharacterSourcePreviewSelection? selection)
            {
                Bound();FrameTiming.Reset();PointerInput.Reset();PointerDevice.Reset();
                Rng.SetRng1(8128);Rng.SetRng2(0x8128a5a5);
                var keyboard=SyntheticInput.CreateKeyboard();var mouse=SyntheticInput.CreateMouse();Scene? scene=null;
                try
                {
                    scene=new(window.FramebufferSize,keyboard,mouse,_=>{},()=>{});
                    scene.Random.SetRng1(123456);scene.Random.SetRng2(654321);scene.AddPlayer(Hunter.Samus);
                    scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:1,nodeLayerMask:0);scene.OnLoad();scene.OnResize();
                    var inputMode=typeof(Scene).GetField("_inputMode",Hidden)!;inputMode.SetValue(scene,Enum.Parse(inputMode.FieldType,"All"));
                    scene.GameState.MatchTime=-1;scene.GameState.PointGoal=10000;scene.GameState.ThirdPersonCamera=false;
                    scene.GameState.SpawnProtection=false;scene.GameState.EnhancedHunters=false;scene.GameState.ShadowFreeze=false;
                    var player=scene.Players.Main;player.IsBot=false;player.IgnoreItemPickups=true;
                    foreach(var button in player.Controls.All){button.Type=ButtonType.Key;button.Key=Keys.Unknown;}
                    player.Controls.Shoot.Key=Keys.Enter;player.Controls.MoveUp.Key=Keys.W;
                    player.Controls.Missile.Key=Keys.M;player.Controls.PowerBeam.Key=Keys.P;player.Controls.Morph.Key=Keys.B;
                    player.Controls.MouseAim=true;player.Controls.KeyboardAim=false;
                    Key(keyboard,Keys.Enter,true);
                    for(int n=0;n<600&&!player.LoadFlags.TestFlag(LoadFlags.Spawned);n++)scene.OnSimulationFrame();
                    Key(keyboard,Keys.Enter,false);scene.CameraSequences.Current?.End();
                    for(int n=0;n<240;n++)scene.OnSimulationFrame();scene.CameraSequences.Current?.End();
                    Check(player.Health>0&&player.ModIsInPlay&&!player.IsAltForm,"native input reaches actual first-person Samus life");
                    if(selection!=null)player.ModBindSourceBody(selection);
                    CharacterSourceBodyOwner? owner=player.ModSourceBody;CharacterWeightedRenderModel? cannon=owner?.CannonModel;
                    bool expectsCannon=ReferenceEquals(selection,normal);
                    Check(expectsCannon ? owner is {CannonCurrent:true}&&cannon!=null : cannon==null,
                        "only normal local-body selection owns a runtime cannon");
                    var gun=(ModelInstance)typeof(PlayerEntity).GetField("_gunModel",Hidden)!.GetValue(player)!;
                    var nativeGunLists=gun.Model.Meshes.Select(mesh=>mesh.ListId).Where(id=>id>0).ToHashSet();
                    Check(nativeGunLists.Count>0,"actual native gun owns positive mesh list identities");
                    CharacterWeightedModelData? geometry=cannon==null?null:(CharacterWeightedModelData)typeof(CharacterWeightedRenderModel)
                        .GetField("_sourceGeometry",Hidden)!.GetValue(cannon)!;
                    Matrix4[]? globals=null,skin=null;
                    if(cannon!=null)
                    {
                        Check(cannon.Joints.Count==39&&cannon.IndexCount==392*3&&geometry!.SourceAuthoringPose!=null
                            &&geometry.SourceRig!=null&&geometry.SourceCannon!=null,"fixed runtime cannon is392triangles/full39rig with original authoring pose");
                        int[] materialIds=geometry!.Primitives.Select(p=>p.SourceMaterialOrdinal).Distinct().Order().ToArray();
                        Check(materialIds.SequenceEqual(new[]{2,5})&&geometry.Primitives.Where(p=>p.SourceMaterialOrdinal==2).Sum(p=>p.Indices.Length)==66*3
                            &&geometry.Primitives.Where(p=>p.SourceMaterialOrdinal==5).Sum(p=>p.Indices.Length)==326*3,
                            "runtime cannon retains exact whole-triangle material2/5 cut");
                        globals=Globals(geometry.SourceRig!,geometry.SourceAuthoringPose!);
                        skin=geometry.Joints.Select((joint,i)=>joint.InverseBind*globals[geometry.SourceRig!.JointNodes[i]]).ToArray();
                        for(int segment=0;segment<cannon.Segments.Count;segment++)
                        {
                            var batch=cannon.Segments[segment];var primitive=geometry.Primitives[segment];
                            Check(batch.SourceGx!=null&&batch.SourceGx.Program.Vertex120==canonicalMaterials.Programs[primitive.SourceMaterialOrdinal].Vertex120
                                &&batch.SourceGx.Program.Fragment120==canonicalMaterials.Programs[primitive.SourceMaterialOrdinal].Fragment120,
                                "cannon material comes from exact original normal body GX program");
                        }
                    }
                    int sourceFrames=0,fallbackFrames=0,repeats=0,sourceFire=0,sourceCharge=0,sourceRelease=0;
                    double maxTip=0,maxPalette=0;bool fired=false,charged=false,morphed=false,missile=false;
                    var trace=new List<string>();var stages=new List<object>();var captured=new HashSet<string>();
                    var fractionalSchedules=new List<object>();
                    Picture(false);Rng.SetRng1(123456);Rng.SetRng2(654321);scene.Random.SetRng1(123456);scene.Random.SetRng2(654321);
                    foreach(Stage stage in Script)
                    {
                        int beforeSource=sourceFrames;var animations=new HashSet<string>();
                        for(int age=0;age<stage.Steps;age++)
                        {
                            Bound();foreach(Keys key in KeysUsed)Key(keyboard,key,false);
                            if(stage.Name=="fire"&&age<2||stage.Name=="charge")Key(keyboard,Keys.Enter,true);
                            if(stage.Name=="walk")Key(keyboard,Keys.W,true);
                            if(stage.Name=="missile"&&age==0)Key(keyboard,Keys.M,true);
                            if(stage.Name=="power"&&age==0)Key(keyboard,Keys.P,true);
                            if(stage.Name is "morph" or "unmorph"&&age==0)Key(keyboard,Keys.B,true);
                            if(stage.Name=="turn")MousePosition(mouse,mouse.Position+new Vector2(8,0));
                            FrameTiming.Reset();scene.OnSimulationFrame();Picture(true,stage.Name);
                            trace.Add(State(scene,player));animations.Add(player.GunAnimation.ToString());
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
                    if(expectsCannon)
                    {
                        Check(sourceFrames>0&&sourceFire>0&&sourceCharge>0&&sourceRelease>0,
                            "actual source cannon submits during native fire, charging and charge release");
                        foreach(Keys key in KeysUsed)Key(keyboard,key,false);
                        for(int n=0;n<300&&SourceItems(scene).Count==0;n++){scene.OnSimulationFrame();Picture(false);}
                        Check(SourceItems(scene).Count==cannon!.Segments.Count,"fallback fixtures start from complete source cannon");
                        // This normal-only phase follows the captured parity
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
                        int[] lists=cannon.Segments.Select(segment=>segment.ListId).ToArray();
                        CharacterModelRuntime.Release(scene);
                        Check(!owner!.CannonCurrent&&lists.All(id=>!GL.IsList(id)),"context retirement deletes cannon geometry and authority");
                        scene.OnDrawFrame();Check(SourceItems(scene).Count==0&&!owner.CannonCurrent,"retired cannon never resurrects on draw");
                    }
                    else Check(sourceFrames==0,"native and Final first-person paths retain native gun rendering");
                    return new(selection?.SelectedAppearance.SkinSource??"native",trace.ToArray(),stages.ToArray(),sourceFrames,fallbackFrames,repeats,maxTip,maxPalette,fractionalSchedules.ToArray());

                    void Picture(bool repeat,string? label=null)
                    {
                        scene.OnDrawFrame();var items=SourceItems(scene);
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
                            int tip=Enumerable.Range(0,geometry.SourceRig!.Nodes.Count).Single(i=>geometry.SourceRig.Nodes[i].Name=="source_bone_32_GUN_Particle_LCTR");
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
                            if(!string.IsNullOrWhiteSpace(capturesRoot)&&label is "idle" or "fire"&&captured.Add(label))
                            {
                                string path=Path.Combine(capturesRoot,selection!.SelectedAppearance.SkinSource+"-cannon-"+label+".png");
                                Check(ScreenCapture.SaveWindow(scene,path),"explicit optional cannon gameplay PNG saved");
                                captures.Add(new{Path=path,Sha256=HashFile(path),SourcePackets=items.Count});
                            }
                        }
                        if(repeat)
                        {
                            string state=State(scene,player),raw=cannon==null?"":RawPalette(cannon);double? clock=owner?.MaterialSeconds;
                            scene.OnDrawFrame();Check(scene.OnRenderFrame(),"repeated actual first-person frame renders");
                            Check(State(scene,player)==state&&owner?.MaterialSeconds==clock&&(cannon==null||RawPalette(cannon)==raw),
                                "repeated draw preserves native effects/projectiles/aim/ammo/RNG and fixed raw cannon/owner clock");repeats++;
                        }
                        Check(GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,"actual source/native first-person frame is GL-clean");
                        DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();
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
                    run.Stages,run.CannonFrames,run.FallbackFrames,run.RepeatedDraws,run.MaximumTipError,run.MaximumPaletteError,run.FractionalSchedules}),
                Scope="Actual owned offline first-person input/Scene/GX; fixed source pose, original material2/5 cut, native muzzle/effect/weapon authority;675-step parity trace plus separately counted normal-only simulated90/120/240/540 schedules with fractional late aim; common pre-script RNG seed and status fixtures declared",
                Unqualified=new[]{"physical controls","Android/macOS","online/replay","source cannon skeletal animation","other weapons","Overcharged source cannon","achieved refresh rate","visual art completeness"}}));
        }
        return error==null?0:1;
    }

    private static List<RenderItem> SourceItems(Scene scene)=>(List<RenderItem>)typeof(Scene).GetField("_sourceBodyItems",Hidden)!.GetValue(scene)!;
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
