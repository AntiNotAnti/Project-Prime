using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using GL = OpenTK.Graphics.OpenGL.GL;

static partial class Program
{
    static FieldInfo DeepField(object owner,string name)
    {
        for (Type? type=owner.GetType();type!=null;type=type.BaseType)
            if (type.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly) is {} field) return field;
        throw new Exception("Missing fixture field "+name);
    }
    static void Put(object owner,string name,object value)=>DeepField(owner,name).SetValue(owner,value);
    static T Take<T>(object owner,string name)=>(T)DeepField(owner,name).GetValue(owner)!;
    static Model DynamicGeometry(int nodes=5,int meshes=1,int materials=2)
    {
        byte[] Name(string text) { byte[] name=new byte[64]; System.Text.Encoding.ASCII.GetBytes(text).CopyTo(name,0); return name; }
        var rawNodes=Enumerable.Range(0,nodes).Select(i=>Raw<RawNode>(
            (nameof(RawNode.Name),Name(i==1?"Spine_1":"node"+i)),
            (nameof(RawNode.ParentId),(short)(i==0?-1:i-1)),
            (nameof(RawNode.ChildId),(short)(i+1<nodes?i+1:-1)),(nameof(RawNode.NextId),(short)-1),
            (nameof(RawNode.Enabled),1u),(nameof(RawNode.MeshCount),(ushort)(i==0?meshes:0)),
            (nameof(RawNode.Scale),new Vector3Fx(4096,4096,4096)))).ToArray();
        var material=Raw<RawMaterial>((nameof(RawMaterial.Name),Name("body")),
            (nameof(RawMaterial.TextureId),(short)-1),(nameof(RawMaterial.PaletteId),(short)-1),
            (nameof(RawMaterial.Alpha),(byte)31),(nameof(RawMaterial.Culling),CullingMode.Neither));
        IReadOnlyList<RenderInstruction> triangle=[new(InstructionCode.BEGIN_VTXS,0),new(InstructionCode.NORMAL,511u<<20),
            new(InstructionCode.COLOR,32767),new(InstructionCode.VTX_16,0,0),
            new(InstructionCode.VTX_16,3072,0),new(InstructionCode.VTX_16,3072u<<16,0),new(InstructionCode.END_VTXS)];
        return new("detached-fixture",false,Raw<Header>((nameof(Header.ScaleBase),new Fixed(4096))),
            rawNodes,Enumerable.Repeat(default(RawMesh),meshes),Enumerable.Repeat(material,materials),[],new[]{triangle},
            new AnimationResults(),[],[],[],[],[],[],[]);
    }
    static string PoseState(ModelInstance instance)
    {
        float[] M(Matrix4 m)=>Enumerable.Range(0,16).Select(i=>m[i/4,i%4]).ToArray();
        float[] V(Vector3 v)=>[v.X,v.Y,v.Z];
        return System.Text.Json.JsonSerializer.Serialize(new {
            Nodes=instance.Model.Nodes.Select(n=>new {n.Enabled,Transform=M(n.Transform),Animation=M(n.Animation),Before=n.BeforeTransform is {} b?M(b):null,After=n.AfterTransform is {} a?M(a):null,Scale=V(n.Scale),Position=V(n.Position),Angle=V(n.Angle),n.AnimIgnoreChild,n.AnimIgnoreParent}),
            Materials=instance.Model.Materials.Select(m=>new {m.CurrentAlpha,m.CurrentTextureId,m.CurrentPaletteId,m.TextureBindingId,m.RenderMode,Diffuse=V(m.CurrentDiffuse),Ambient=V(m.CurrentAmbient),Specular=V(m.CurrentSpecular)}),
            Frames=instance.AnimInfo.Frame, Indices=instance.AnimInfo.Index, Flags=instance.AnimInfo.Flags,
            Palette=instance.Model.MatrixStackValues
        });
    }
    static List<RenderItem> Detached(Scene scene)=>Take<List<RenderItem>>(scene,"_detachedShadowItems");
    static void Collection(Scene scene)
    {
        var used=Take<Queue<RenderItem>>(scene,"_usedRenderItems"); var free=Take<Queue<RenderItem>>(scene,"_freeRenderItems");
        while(used.TryDequeue(out var item)) free.Enqueue(item);
        Take<List<RenderItem>>(scene,"_nonDecalItems").Clear();Take<List<RenderItem>>(scene,"_decalItems").Clear();
        Take<List<RenderItem>>(scene,"_translucentItems").Clear();Call(scene,"BeginDetachedShadowCollection");
    }
    static PlayerEntity DynamicPlayer(Scene scene,Model model)
    {
        PlayerEntity player=scene.Players.Items[1]; player.Hunter=Hunter.Samus;
        Put(player,"<Values>k__BackingField",Metadata.PlayerValues[(int)Hunter.Samus]);
        Put(player,"_health",100);Put(player,"_curAlpha",1f);Put(player,"_timeSinceDamage",(ushort)255);
        Put(player,"_facingVector",Vector3.UnitZ);Put(player,"_upVector",Vector3.UnitY);
        Put(player,"_field74",1f);Put(player,"_modelTransform",Matrix4.Identity);
        var lower=new ModelInstance(model);var upper=new ModelInstance(model);
        Put(player,"_bipedModel1",lower);Put(player,"_bipedModel2",upper);Put(player,"_altModel",new ModelInstance(model));
        Put(player,"_trailModel",new ModelInstance(model));
        var lods=Take<ModelInstance[]>(player,"_bipedModelLods");lods[0]=lower;lods[1]=lower;
        var spines=Take<Node?[]>(player,"_spineNodes");spines[0]=model.Nodes[1];spines[1]=model.Nodes[1];
        player.NodeRef=new NodeRef("offscreen",1,1,0);player.LoadFlags=LoadFlags.Active|LoadFlags.Spawned;
        player.Position=new(.25f,0,0);return player;
    }
    static PlatformEntity DynamicPlatform(Scene scene,Model model)
    {
        var platform=(PlatformEntity)RuntimeHelpers.GetUninitializedObject(typeof(PlatformEntity));
        Put(platform,"_scene",scene);Put(platform,"_models",new List<ModelInstance>{new(model)});
        Put(platform,"_effects",new List<MphRead.Effects.EffectEntry?> {null,null,null,null});
        Put(platform,"_drawScale",1f);Put(platform,"<Alpha>k__BackingField",1f);
        Put(platform,"_animFlags",PlatAnimFlags.Draw|PlatAnimFlags.WasDrawn);
        platform.Transform=Matrix4.Identity;platform.NodeRef=new NodeRef("offscreen",1,1,0);return platform;
    }
    static void DynamicCacheChecks()
    {
        Model model=DynamicGeometry();var source=new ModelInstance(model);var cache=new DetachedShadowPoseCache();
        model.Nodes[0].Animation=Matrix4.CreateTranslation(9,2,3);source.AnimInfo.Frame[0]=7;
        string before=PoseState(source);object owner=new();
        Check(cache.TryPrepare(owner,source,1,out var first),"detached pose prepared");
        Check(!ReferenceEquals(first.Instance.Model,model)&&!ReferenceEquals(first.Instance.Model.Nodes[0],model.Nodes[0]),"mutable pose and nodes are detached");
        Check(ReferenceEquals(first.Instance.Model.RenderInstructionLists,model.RenderInstructionLists),"immutable geometry is borrowed");
        first.Instance.Model.Nodes[0].Animation=Matrix4.Identity;first.Instance.Model.Materials[0].CurrentAlpha=.1f;
        first.Instance.AnimInfo.Frame[0]=99;
        Check(PoseState(source)==before,"detached node/material/cursor changes cannot mutate source");
        Check(!cache.TryPrepare(owner,source,1,out _),"one preparation per owner/source/collection");
        Check(cache.TryPrepare(owner,new ModelInstance(model),1,out _),"distinct animation instances sharing one model retain distinct pose packets");
        Check(cache.TryPrepare(owner,source,2,out var again)&&ReferenceEquals(first,again),"warmed pose storage reused");
        Check(again.Instance.AnimInfo.Frame[0]==7&&again.Instance.Model.Materials[0].CurrentAlpha==1,"current cursor/material refreshed without advancement");
        Check(again.BoundsKnown&&again.Min==Vector3.Zero&&again.Max==new Vector3(.75f,.75f,0),"bounds decoded from actual source vertices");
        cache.Clear();
        for(int i=0;i<DetachedShadowPoseCache.MaxRigs;i++) Check(cache.TryPrepare(new object(),source,1,out _),"bounded rig slot available");
        Check(!cache.TryPrepare(new object(),source,1,out _)&&cache.Count==DetachedShadowPoseCache.MaxRigs,"same-picture rig overflow bounded");
        Check(cache.TryPrepare(new object(),source,2,out _)&&cache.Count==DetachedShadowPoseCache.MaxRigs,"old rig evicted for new owner without growth");
        Check(cache.Nodes<=DetachedShadowPoseCache.MaxNodes&&cache.Meshes<=DetachedShadowPoseCache.MaxMeshes,"aggregate node/mesh caps enforced");
        Check(!cache.TryPrepare(new object(),new(DynamicGeometry(nodes:DetachedShadowPoseCache.MaxNodes+1)),3,out _),"oversize skeleton refused before copy");
        Check(!cache.TryPrepare(new object(),new(DynamicGeometry(meshes:DetachedShadowPoseCache.MaxMeshes+1)),3,out _),"oversize mesh table refused before copy");
        Check(!cache.TryPrepare(new object(),new(DynamicGeometry(materials:DetachedShadowPoseCache.MaxMaterials+1)),3,out _),"oversize unused material table refused before copy");
        cache.Clear();var heavy=new ModelInstance(DynamicGeometry(materials:DetachedShadowPoseCache.MaxMaterials/2+1));
        Check(cache.TryPrepare(new object(),heavy,1,out _),"bounded material table fits first rig");
        Check(!cache.TryPrepare(new object(),heavy,1,out _)&&cache.Materials==heavy.Model.Materials.Count,"aggregate material budget refuses same-picture growth");
        cache.Clear();Check(cache.Count==0&&cache.Nodes==0&&cache.Meshes==0,"cache release returns owned CPU storage");
        var bounds=new RenderItem {RetainedGpuVisibilityEligible=true,RetainedBoundsMin=Vector3.Zero,RetainedBoundsMax=Vector3.One,Transform=Matrix4.Identity};
        Check(DetachedShadowBounds.Overlaps(bounds,Matrix4.Identity),"light-space overlap retained");
        bounds.Transform=Matrix4.CreateTranslation(100,0,0);Check(!DetachedShadowBounds.Overlaps(bounds,Matrix4.Identity),"light-space rejection independent of camera");
        bounds.RetainedGpuVisibilityEligible=false;Check(DetachedShadowBounds.Overlaps(bounds,Matrix4.Identity),"unknown HD bounds conservatively retained");
        bounds.RetainedGpuVisibilityEligible=true;bounds.Transform=Matrix4.Identity;bounds.MatrixStackCount=2;
        for(int m=0;m<2;m++){var matrix=Matrix4.CreateTranslation(m==0?-100:100,0,0);for(int i=0;i<16;i++)bounds.MatrixStack[m*16+i]=matrix[i/4,i%4];}
        Check(DetachedShadowBounds.Overlaps(bounds,Matrix4.Identity),"union of separated palettes retains possible weighted interpolation");
    }
    static void DynamicHdChecks(Scene scene,PlayerEntity player,Model source)
    {
        const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
        FieldInfo packField=typeof(CharacterModelRuntime).GetField("_pack",flags)!,loadedField=typeof(CharacterModelRuntime).GetField("_packLoaded",flags)!;
        object? oldPack=packField.GetValue(null),oldLoaded=loadedField.GetValue(null);
        var assets=new Dictionary<(Hunter,CharacterModelPart,int),CharacterModelAsset>();
        object pack=typeof(CharacterModelPack).GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single().Invoke([assets,null]);
        packField.SetValue(null,pack);loadedField.SetValue(null,true);
        object table=typeof(CharacterModelRuntime).GetField("_scenes",flags)!.GetValue(null)!;
        object?[] lookup=[scene,null];
        if(!(bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table,lookup)!)
        {
            lookup[1]=Activator.CreateInstance(typeof(CharacterModelRuntime).GetNestedType("SceneResources",BindingFlags.NonPublic)!,nonPublic:true)!;
            table.GetType().GetMethod("Add")!.Invoke(table,[scene,lookup[1]]);
        }
        object resources=lookup[1]!;
        var rigid=(IDictionary)resources.GetType().GetField("RigidModels")!.GetValue(resources)!;
        var weighted=(IDictionary)resources.GetType().GetField("WeightedModels")!.GetValue(resources)!;
        RenderOptions.CharacterModelReplacements=true;
        foreach(bool skin in new[]{false,true})
        {
            Model hd=source.CreateSceneCopy();Call(scene,"GenerateLists",hd,false);
            var asset=new CharacterModelAsset(Hunter.Samus,CharacterModelPart.Biped,0,
                skin?CharacterSkinningMode.Weighted4:CharacterSkinningMode.RigidNodes,
                "in-memory-detached-"+skin,new Dictionary<string,string>(),new HashSet<string>(),skin,1);
            assets[(Hunter.Samus,CharacterModelPart.Biped,0,(BeamType?)null)]=asset;
            if(skin) weighted.Add(CharacterModelRuntime.ModelCacheKey(asset,source.Id),new CharacterWeightedRenderModel(asset,
                new[]{new CharacterWeightedRenderSegment(0,hd.Meshes[0].ListId)},new[]{new CharacterWeightedRenderJoint(0,Matrix4.Identity)},3,3));
            else rigid.Add(CharacterModelRuntime.ModelCacheKey(asset,source.Id),new CharacterRigidRenderModel(asset,
                new[]{new CharacterRigidRenderSegment(0,0,hd.Meshes[0].ListId)},3,3));
            Collection(scene);Set(scene,"_cameraMode",CameraMode.Player);
            string before=PoseState(Take<ModelInstance>(player,"_bipedModel2"));player.Draw();
            RenderItem detached=Detached(scene).Single();Matrix4 transform=detached.Transform;
            float[] palette=detached.MatrixStack.Take(detached.MatrixStackCount*16).ToArray();
            Check(detached.ListId==hd.Meshes[0].ListId&&detached.WeightedSkinning==skin,$"actual {(skin?"weighted":"rigid")} HD emitter uses detached pose");
            Check(!detached.RetainedGpuVisibilityEligible&&PoseState(Take<ModelInstance>(player,"_bipedModel2"))==before,"unknown HD envelope retained; native skeleton untouched");
            Collection(scene);Set(scene,"_cameraMode",CameraMode.Pivot);player.Draw();
            RenderItem visible=Take<List<RenderItem>>(scene,"_nonDecalItems").Single();
            Check(visible.ListId==hd.Meshes[0].ListId&&visible.Transform==transform
                &&visible.MatrixStack.Take(visible.MatrixStackCount*16).SequenceEqual(palette),$"{(skin?"weighted":"rigid")} HD detached/visible packet and palette parity");
            for(int i=0;i<8;i++){Collection(scene);Set(scene,"_cameraMode",CameraMode.Player);player.Draw();}
            Check(rigid.Count==1&&weighted.Count==(skin?1:0),"detached clone identity cannot duplicate scene HD geometry cache");
        }
        RenderOptions.CharacterModelReplacements=false;packField.SetValue(null,oldPack);loadedField.SetValue(null,oldLoaded);
        Set(scene,"_cameraMode",CameraMode.Player);
    }
    static void DynamicShadowChecks()
    {
        if(!OperatingSystem.IsWindows()) throw new Exception("Windows-only native acceptance");
        DynamicCacheChecks();
        var settings=DesktopGlContext.Settings(true);settings.ClientSize=new(32,32);settings.StartVisible=false;
        using var window=new NativeWindow(settings);window.Context.MakeCurrent();
        GL.LoadBindings(new OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext());
        Console.WriteLine($"dynamic shadows native renderer={GL.GetString(StringName.Renderer)} gl={GL.GetString(StringName.Version)} mvid={typeof(Scene).Module.ModuleVersionId}");
        ShadowQuality previous=RenderOptions.Shadows;bool replacements=RenderOptions.CharacterModelReplacements;
        bool cosmetics=RenderOptions.ShowCustomCosmetics,detail=Features.MaxPlayerDetail;
        RenderOptions.Shadows=ShadowQuality.Low;RenderOptions.CharacterModelReplacements=false;RenderOptions.ShowCustomCosmetics=false;Features.MaxPlayerDetail=true;
        Scene scene=new(new(32,32),null!,null!,_=>{},()=>{},initializeRuntime:false);Call(scene,"InitShaders");Set(scene,"_cameraMode",CameraMode.Player);
        Model model=DynamicGeometry();Call(scene,"GenerateLists",model,false);
        PlayerEntity player=DynamicPlayer(scene,model);ModelInstance body=Take<ModelInstance>(player,"_bipedModel2");
        string before=PoseState(body);ulong frame=scene.FrameCount;long particles=scene.ModEffectParticles;
        Collection(scene);int polygon=Take<int>(scene,"_nextPolygonId");player.Draw();
        Check(Detached(scene).Count==1&&Take<List<RenderItem>>(scene,"_nonDecalItems").Count==0,"actual offscreen Player.Draw hook emits only detached packets");
        Check(PoseState(body)==before&&scene.FrameCount==frame&&scene.ModEffectParticles==particles,"offscreen player preserves source pose/materials/cursors/simulation/effect totals");
        Check(Take<int>(scene,"_nextPolygonId")==polygon&&!player.Flags2.TestFlag(PlayerFlags2.DrawnThirdPerson),"shadow-only player does not claim camera draw or polygon IDs");
        RenderItem detached=Detached(scene)[0];Matrix4 expected=detached.Transform;
        Check(detached.ListId==model.Meshes[0].ListId&&!detached.ViewModel,"native detached packet borrows source geometry and world scope");
        Collection(scene);Set(scene,"_cameraMode",CameraMode.Pivot);player.Draw();
        RenderItem visible=Take<List<RenderItem>>(scene,"_nonDecalItems").Single();
        Check(Detached(scene).Count==0&&visible.Transform==expected&&visible.ListId==model.Meshes[0].ListId,"ordinary visible path stays single and matches detached native pose");
        Set(scene,"_cameraMode",CameraMode.Player);
        foreach(Hunter hunter in new[]{Hunter.Samus,Hunter.Kanden,Hunter.Spire})
        {
            player.Hunter=hunter;Put(player,"<Values>k__BackingField",Metadata.PlayerValues[(int)hunter]);
            Put(player,"<Flags1>k__BackingField",PlayerFlags1.AltForm);Put(player,"<Flags2>k__BackingField",hunter==Hunter.Samus?PlayerFlags2.Cloaking:PlayerFlags2.None);
            if(hunter==Hunter.Kanden){var segments=Take<Matrix4[]>(player,"_kandenSegMtx");for(int i=0;i<segments.Length;i++)segments[i]=Matrix4.CreateTranslation(i*.1f,0,0);}
            Collection(scene);string altBefore=PoseState(Take<ModelInstance>(player,"_altModel"));player.Draw();
            Check(Detached(scene).Count==1&&PoseState(Take<ModelInstance>(player,"_altModel"))==altBefore,$"{hunter} alternate-form detached packet leaves native segment/skeleton ownership");
            Matrix4 alt=Detached(scene)[0].Transform;
            Collection(scene);Set(scene,"_cameraMode",CameraMode.Pivot);player.Draw();
            Check(Take<List<RenderItem>>(scene,"_nonDecalItems").First().Transform==alt,$"{hunter} detached/visible alternate-form pose parity");
            Set(scene,"_cameraMode",CameraMode.Player);
        }
        player.Hunter=Hunter.Samus;Put(player,"<Values>k__BackingField",Metadata.PlayerValues[0]);Put(player,"<Flags1>k__BackingField",PlayerFlags1.None);
        player.Hunter=Hunter.Spire;Put(player,"<Values>k__BackingField",Metadata.PlayerValues[(int)Hunter.Spire]);
        Put(player,"<Flags1>k__BackingField",PlayerFlags1.AltForm);Put(player,"<Flags2>k__BackingField",PlayerFlags2.AltAttack);
        Put(player,"_spireAltFacing",Vector3.UnitZ);Put(player,"_spireAltUp",Vector3.UnitY);Put(player,"_modelTransform",Matrix4.CreateTranslation(player.Position));
        Call(player,"AnimateSpireAltAttack");Collection(scene);before=PoseState(Take<ModelInstance>(player,"_altModel"));
        Call(player,"CollectDetachedPlayerShadow",player.Position,Vector3.UnitZ,true,0);
        Matrix4 attack=Detached(scene).Single().Transform;
        Check(PoseState(Take<ModelInstance>(player,"_altModel"))==before,"Spire attack detached preparation cannot rewrite collision/effect nodes");
        Collection(scene);Call(player,"DrawSpireAltAttack",null!,null!);
        Check(Take<List<RenderItem>>(scene,"_nonDecalItems").Single().Transform==attack,"Spire specialized attack detached/visible packet parity");
        player.Hunter=Hunter.Samus;Put(player,"<Values>k__BackingField",Metadata.PlayerValues[0]);Put(player,"<Flags1>k__BackingField",PlayerFlags1.None);Put(player,"<Flags2>k__BackingField",PlayerFlags2.None);
        Put(player,"_bipedIceModel",new ModelInstance(model));Put(player,"_altIceModel",new ModelInstance(model));Put(player,"_frozenGfxTimer",(ushort)1);
        Collection(scene);before=PoseState(Take<ModelInstance>(player,"_bipedIceModel"));player.Draw();
        Matrix4[] frozen=Detached(scene).Select(p=>p.Transform).ToArray();
        Check(frozen.Length==2&&PoseState(Take<ModelInstance>(player,"_bipedIceModel"))==before,"frozen body and shell prepared separately without canonical ice-anchor mutation");
        Collection(scene);Set(scene,"_cameraMode",CameraMode.Pivot);player.Draw();
        Check(Take<List<RenderItem>>(scene,"_nonDecalItems").Select(p=>p.Transform).SequenceEqual(frozen),"native frozen body/shell detached versus visible parity");
        Put(player,"_frozenGfxTimer",(ushort)0);Set(scene,"_cameraMode",CameraMode.Player);
        DynamicHdChecks(scene,player,model);
        player.Hunter=Hunter.Weavel;Put(player,"<Values>k__BackingField",Metadata.PlayerValues[(int)Hunter.Weavel]);
        Check(player.ModTryBindCharacterGameplay(MphRead.Mods.Multiplayer.CharacterGameplayRules.TrooperDroneV1),"real private drone owner admitted before initialization");
        Put(player,"_droneModel",new ModelInstance(model));Put(player,"<Flags1>k__BackingField",PlayerFlags1.AltForm);
        Collection(scene);before=PoseState(Take<ModelInstance>(player,"_droneModel"));int shots=player.DroneShots,lasers=player.DroneLasers;
        player.Draw();Matrix4 drone=Detached(scene).Single().Transform;
        Check(PoseState(Take<ModelInstance>(player,"_droneModel"))==before&&player.DroneShots==shots&&player.DroneLasers==lasers,"detached drone source pose/weapon callbacks preserved");
        Collection(scene);Set(scene,"_cameraMode",CameraMode.Pivot);player.Draw();
        Check(Take<List<RenderItem>>(scene,"_nonDecalItems").Single().Transform==drone,"actual drone body emitter detached/visible pose parity");
        Set(scene,"_cameraMode",CameraMode.Player);Collection(scene);scene.SideScene=true;
        bool retired=Take<bool>(player,"_modCharacterGameplayRetired");
        Call(player,"CollectDetachedPlayerShadow",player.Position,Vector3.UnitZ,true,0);
        Check(Detached(scene).Count==0&&Take<bool>(player,"_modCharacterGameplayRetired")==retired
            &&Take<MphRead.Mods.Multiplayer.CharacterGameplayRules>(player,"_modCharacterGameplay").UsesScoutDrone,"shadow preparation cannot retire stale private gameplay owner");
        scene.SideScene=false;player.ModRetireCharacterGameplay();player.Hunter=Hunter.Samus;
        Put(player,"<Values>k__BackingField",Metadata.PlayerValues[0]);Put(player,"<Flags1>k__BackingField",PlayerFlags1.None);
        var effect=new MphRead.Effects.EffectEntry();
        var element=new MphRead.Effects.EffectElementEntry {Flags=MphRead.Effects.EffElemFlags.DrawEnabled|MphRead.Effects.EffElemFlags.ElementExtension,OwnTransform=Matrix4.CreateTranslation(7,8,9)};
        effect.Elements.Add(element);Put(player,"_chargeEffect",effect);
        Collection(scene);particles=scene.ModEffectParticles;player.Draw();
        Check(element.Flags==MphRead.Effects.EffElemFlags.ElementExtension&&element.OwnTransform==Matrix4.CreateTranslation(7,8,9)
            &&scene.ModEffectParticles==particles,"offscreen player retains native emitter visibility and never moves/spawns effect anchors");
        Put(player,"_chargeEffect",null!);
        Collection(scene);var platform=DynamicPlatform(scene,model);ModelInstance platformSource=platform.GetModels()[0];
        before=PoseState(platformSource);platform.GetDrawInfo();
        Check(Detached(scene).Count==1&&Take<List<RenderItem>>(scene,"_nonDecalItems").Count==0,"actual offscreen platform hook emits separate packet");
        Check(PoseState(platformSource)==before&&!Take<PlatAnimFlags>(platform,"_animFlags").TestFlag(PlatAnimFlags.WasDrawn),"offscreen platform preserves source and native WasDrawn visibility");
        Matrix4 platformTransform=Detached(scene)[0].Transform;
        Collection(scene);Set(scene,"_cameraMode",CameraMode.Pivot);platform.GetDrawInfo();
        Check(Take<List<RenderItem>>(scene,"_nonDecalItems").Single().Transform==platformTransform&&Take<PlatAnimFlags>(platform,"_animFlags").TestFlag(PlatAnimFlags.WasDrawn),"visible/detached platform pose parity and native flag semantics");
        Set(scene,"_cameraMode",CameraMode.Player);
        Collection(scene);platform.Transform=Matrix4.CreateTranslation(30,0,0);before=PoseState(platformSource);platform.GetDrawInfo();
        Check(Detached(scene).Single().Transform.Row3.X==30&&PoseState(platformSource)==before,"moving platform refreshes detached world pose without touching collision skeleton");
        var platformEffects=Take<List<MphRead.Effects.EffectEntry?>>(platform,"_effects");platformEffects[0]=effect;
        Put(platform,"<Flags>k__BackingField",PlatformFlags.SamusShip);element.Flags=MphRead.Effects.EffElemFlags.DrawEnabled|MphRead.Effects.EffElemFlags.ElementExtension;
        Collection(scene);platform.GetDrawInfo();
        Check(element.Flags==MphRead.Effects.EffElemFlags.ElementExtension&&element.OwnTransform==Matrix4.CreateTranslation(7,8,9),"offscreen platform keeps ordinary effect visibility/anchor contract");
        var delano=DynamicPlatform(scene,model);Put(delano,"_useDelano7",true);
        Put(delano,"_models",new List<ModelInstance>{new(model),new(model),new(model)});delano.Transform=Matrix4.CreateTranslation(3,2,1);
        Collection(scene);before=PoseState(delano.GetModels()[0]);delano.GetDrawInfo();
        Check(Detached(scene).Count==2&&Detached(scene).All(p=>p.Transform.Row3.Xyz==new Vector3(3,2,1)),"Delano detached invisible rig/ship/turret parent dependency order");
        Check(PoseState(delano.GetModels()[0])==before&&!Take<PlatAnimFlags>(delano,"_animFlags").TestFlag(PlatAnimFlags.WasDrawn),"Delano detached parent chain leaves canonical model and camera flags untouched");
        var generation=typeof(FrameTiming).GetProperty("PresentationGeneration",BindingFlags.Static|BindingFlags.NonPublic)!;
        long oldGeneration=FrameTiming.PresentationGeneration;generation.SetValue(null,oldGeneration+1);Collection(scene);
        Check(Take<DetachedShadowPoseCache>(scene,"_detachedShadowPoses").Count==0,"presentation generation change clears detached rig ownership");
        generation.SetValue(null,oldGeneration);Collection(scene);player.Draw();
        Check(Detached(scene).Count==1,"current rig rebuilt after presentation generation reset");
        Collection(scene);Check(scene.TryPrepareDetachedShadow(new object(),body,out var packetPose),"packet budget pose available");
        using(scene.BeginDetachedShadowSubmission(packetPose))
            for(int i=0;i<Scene.MaxDetachedShadowPackets+5;i++)
                scene.AddRenderItem(model.Materials[0],0,1,Vector3.Zero,LightInfo.Zero,Matrix4.Identity,Matrix4.Identity,model.Meshes[0].ListId,0,Array.Empty<float>(),null,null,SelectionType.None,BillboardMode.None,1);
        Check(Detached(scene).Count==Scene.MaxDetachedShadowPackets,"detached packet cap enforced without world queue spill");
        Collection(scene);player.Draw();Take<List<RenderItem>>(scene,"_nonDecalItems").Add(new RenderItem());
        Set(scene,"_cameraPosition",Vector3.Zero);Set(scene,"_cameraFacing",Vector3.Zero);Set(scene,"_light1Vector",Vector3.UnitZ);
        Call(scene,"RenderShadowMap");
        int shadow=Take<int>(scene,"_shadowFramebuffer"),size=Take<int>(scene,"_shadowTargetSize");
        GL.BindFramebuffer(FramebufferTarget.Framebuffer,shadow);float[] depth=new float[size*size];GL.ReadPixels(0,0,size,size,PixelFormat.DepthComponent,PixelType.Float,depth);
        int covered=depth.Count(x=>x<.9999f);Check(covered>0&&scene.DetachedShadowDraws>0,"production light pass draws offscreen actor depth into native shadow target");
        Set(scene,"_cameraMode",CameraMode.Pivot);Collection(scene);player.Draw();Call(scene,"CaptureRetainedRenderWorld");Call(scene,"RenderShadowMap");
        GL.BindFramebuffer(FramebufferTarget.Framebuffer,Take<int>(scene,"_shadowFramebuffer"));float[] visibleDepth=new float[depth.Length];GL.ReadPixels(0,0,size,size,PixelFormat.DepthComponent,PixelType.Float,visibleDepth);
        Check(depth.SequenceEqual(visibleDepth),"actual offscreen and ordinary native shadow depth pixels match");
        Console.WriteLine($"DYNAMIC SHADOWS native covered_depth_pixels={covered} target={size} rigs={Take<DetachedShadowPoseCache>(scene,"_detachedShadowPoses").Count} packet_cap={Scene.MaxDetachedShadowPackets}");
        Collection(scene);RenderOptions.Shadows=ShadowQuality.Off;Call(scene,"BeginDetachedShadowCollection");
        Check(!scene.CanPrepareDetachedShadows&&Take<DetachedShadowPoseCache>(scene,"_detachedShadowPoses").Count==0,"quality Off clears detached ownership");
        scene.UnloadGl();Check(Take<DetachedShadowPoseCache>(scene,"_detachedShadowPoses").Count==0,"scene unload clears detached CPU owners");
        Check(GL.IsList(model.Meshes[0].ListId),"detached scene teardown cannot delete borrowed geometry");
        SharedModelResources.Retain(model);SharedModelResources.Release(model);
        RenderOptions.Shadows=previous;RenderOptions.CharacterModelReplacements=replacements;RenderOptions.ShowCustomCosmetics=cosmetics;Features.MaxPlayerDetail=detail;
        Check(GL.GetError()==ErrorCode.NoError,"dynamic caster native GL state valid");
    }
}
