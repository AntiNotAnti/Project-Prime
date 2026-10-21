using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;
using MphRead.Mods.Render.Characters;
using OpenTK.Mathematics;

// Actual owned-source CPU proof. Expected folds/selection/normal sums come from
// raw CMDL/CSKR/CINF/ANCS/ANIM, never RetroFinalBallSource or its generated mesh.
// No actor, graphics context, screenshots or game payload output.
internal static class CharacterFinalBallSourceOwnedProbe
{
    private const string Id = "darksamus-overcharged-echoes", Stem = Id + ".ball", Normal = "darksamus-echoes";
    private const string ModelPin = "57158cb41d062bab7b93920016855c8fd7a8b899a48c8dde23e4838e4b95beab";
    private const string ReceiptPin = "8eba953c52a647a6e4c8bea6c66b289bd5f4bc95abe692782959248522bb4a55";
    private const string MaterialPin = "28d5d167d48281c98234335eb8b6e766942d2832f6dbbcb35ffeb8854827e1eb";
    private const string BodyMaterialPin = "69d27a8e4867b4ec50084260a6fb570936e79edb7b08ad2bbd04a58d5204df8d";
    private const string PosePin = "c29325b7b5ae5eb0efc3ae336d9e5a1d260a3aeef57745fe2b299851f2c0a872";
    private static readonly Dictionary<string,string> OldPins = new()
    {
        [".ancs-graph.json"]="21f25c3ed6cf862be1f2bce392a8f85cc2eb2b2339cf70e0bdbcbededeceaab3",
        [".ancs.bin"]="7933e22a780b7f2cbee01be3ee5a4d48e6f08eb39df81608d638bee1729a5f7b",
        [".animations.bin"]="15f102868334b98e271050493bbfdd996ca3d7121a6f4b04d54f6308b5d07b0d",
        [".ball.ancs-graph.json"]="18b40d1d94ae6600cdcbd501040f0d4dd66b56ab7e34c7f1294a6481d36a8a40",
        [".ball.ancs.bin"]="46ddcf531037bd4ae3f1949f0ecc7febe6deede70e5d19ddd720e5a7cfa21ad8",
        [".ball.animations.bin"]="05d491daf8b02ade697cbc225bdc145406d996a35bbb69fc52ea2e922b51f362",
        [".ball.cinf.bin"]="08ff767eecccfa572cf9424862ad8a059128eaf035bcc65c8f9faa77fc09cbce",
        [".ball.motions.json"]="fce144ecc486fed1cf375eb01b8d517e3f1d9ae6ccafdaa96ea7f525114b2b2e",
        [".ball.obj"]="780ee45bc5dfbe27059043e00078a8ecafe69970ec1ba8cde3265b0a7c5c42d0",
        [".ball.skeleton.json"]="eb87c0f56871ea3e322cd279b38cbfe2b3e91f0e54b8f6a0cba477cdc455580d",
        [".ball.source.glb"]="eca17407d4933e3a1d829bc931de02561321c185d9bbaa3a574538d57d8851c9",
        [".ball.source.json"]="1f9cea28bc98078070f083c122859f47b7465acd5f8ac109cdb9e28451c9cb55",
        [".ball.weights.json"]="5deb2ea870a6cc58e9ba7baec4fc42f70872576df1cab30b180800bab79d4e81",
        [".cannon-source.json"]="46c828de8c0c7f6ec6102216400891c30475036bc2aa7da0f7b6b752abf0752c",
        [".cinf.bin"]="3a8d0a84695906ac06fee59478fac134623a4a885cb3b1a1e6e144c4670b3de4",
        [".materials.bin"]=BodyMaterialPin,
        [".motions.json"]="e514ff9eab6f86685b718ab2bb59ff5bc2bb16fc5c870eea6e733f91d0d463c4",
        [".obj"]="44af9aef3aea7945d7d8763bed8bbacee026f4fe02b7046683a2ec90252e5588",
        [".skeleton.json"]="3533363e4b473621b2cc446b9333b2198f9c2fc0117461db7fe4028c23d8ea80",
        [".source.glb"]="ff1866fd7c92c7aad0326381e059119febd0993d63aacc85b45400b019af5164",
        [".source.json"]="d25a4980c25f974a5a3a71b692601bf2ac0d289522434c1e918470351e35d934",
        [".weights.json"]="fe231d65f233f48128217d672e8862605f843b3a7407db6cdd3b0b124e0c756c"
    };
    private static readonly Dictionary<uint,string> VariantPins = new()
    {
        [0xbc7a8b7b]="65ab9c84ab0da18a2c9e8fd4e1d8752836e9069ec7742781325186b6ac3860d2",
        [0x99e37db0]="dc0a2cd372162e2b062d62301680b58905809a2dac761c842995ec6f11bd9b6e",
        [0x0aa4aed0]="4f337593bc2eec39796596ef00dca4c1068fa066abecee4cd3bc5daa17377a15",
        [0x76c58b0b]="6cec7b588ba49863234c0f839f774df411e6d09806d6a9bf5249bdb7dad0fc2e"
    };
    private sealed class Proof
    {
        internal int Checks, Refusals; internal string? Failed;
        internal readonly List<string> Cases = new();
        internal void Check(bool value,string why)
        { if(!value){Failed=why;throw new InvalidDataException(why);} Checks++; }
        internal void Reject(Action action,string why)
        {
            try{action();}catch(Exception e)when(e is InvalidDataException or IOException or NotSupportedException or ArgumentException or JsonException)
            {Refusals++;Cases.Add(why);return;}
            Failed=why;throw new InvalidDataException("Expected Final ball rejection: "+why);
        }
    }
    private sealed record Batch(string Name,Cmdl Raw,int[] Faces,Vector3d[] Positions,Vector3d[]? Normals,int[] CompositeFaces,int SourceMaterial);
    private sealed record Expected(Batch[] Batches,Vector3d Center,RetroNativeSkeleton BallRig);

    internal static int Run(string rvz)
    {
        string temporary=Path.Combine(Path.GetTempPath(),"prime-final-ball-source-"+Guid.NewGuid().ToString("N")),imports=Path.Combine(temporary,"imports");
        string core=typeof(CharacterModelPack).Assembly.Location,tool=typeof(CharacterFinalBallSourceOwnedProbe).Assembly.Location;
        string coreBefore=HashFile(core),toolBefore=HashFile(tool);var caches=Caches();uint rng1=Rng.Rng1,rng2=Rng.Rng2;
        string? before=null,after=null,error=null,errorHash=null;bool cleaned=false,unchanged=false,admitted=false;object? metadata=null;
        var proof=new Proof();
        try
        {
            proof.Check(OperatingSystem.IsWindows(),"Exclusive file-lock proof uses this Windows host.");before=HashFile(rvz);
            using var resources=new Resources(rvz);
            RetroCharacterPreviewExporter.ExportFromOwnedGame(rvz,imports,selectedIds:[Id,Normal]);
            var state=LocalImportBundle.Inspect(imports,Id,RetroCharacterPreviewExporter.BundleSpec(Id));
            proof.Check(state.State==ImportBundleState.Ready&&state.Receipt!=null,"Actual complete Final composite export commits atomically.");
            string folder=Path.GetDirectoryName(state.Files.Single(f=>Path.GetFileName(f)=="characters.json"))!;
            var componentHashes=state.Files.ToDictionary(f=>f,HashFile);
            proof.Check(state.Files.All(f=>Path.GetDirectoryName(f)==folder),"Every Final body/cannon/raw-ball/composite input shares one immutable generation.");
            foreach(var pin in OldPins)proof.Check(HashFile(Path.Combine(folder,Id+pin.Key))==pin.Value,"Frozen Final raw body/cannon/ball source bytes unchanged: "+pin.Key);
            proof.Check(ModelPin.Length==64&&ReceiptPin.Length==64&&MaterialPin.Length==64
                &&HashFile(Path.Combine(folder,Stem+".composite.glb"))==ModelPin
                &&HashFile(Path.Combine(folder,Stem+".composite.json"))==ReceiptPin
                &&HashFile(Path.Combine(folder,Stem+".materials.bin"))==MaterialPin,"Composite geometry/receipt/profile8 agree with independent immutable authoring pins.");
            byte[] ancs=resources.Read(0x39900a11),bodyCinf=resources.Read(0x4abc8a50),ballCinf=resources.Read(0xc7dbc44a);
            proof.Check(Sha(ancs)==OldPins[".ancs.bin"]&&Sha(bodyCinf)==OldPins[".cinf.bin"]&&Sha(ballCinf)==OldPins[".ball.cinf.bin"],"Original body and ball ANCS/CINF remain separate literal sources.");
            var bodyRefs=RetroCharacterSource.FindCharacter(ancs,"Battle_3");
            var ballRefs=RetroCharacterSource.FindCharacter(resources.Read(0x654a3f97),"Dark_Samus_MorphBall");
            proof.Check(bodyRefs.Cmdl==0xfaa12c05&&bodyRefs.Cskr==0x918e83d3&&bodyRefs.Cinf==0x4abc8a50
                &&ballRefs.Cmdl==0x2f2c1e6f&&ballRefs.Cskr==0x4403b1b9&&ballRefs.Cinf==0xc7dbc44a,"Separate original body/ball resource tuples are exact.");
            proof.Check(Sha(resources.Read(bodyRefs.Cmdl))=="f49d5a84271eb08ed780170887e4301af428303c24fda87a55ac60cf865b17db"
                &&Sha(resources.Read(bodyRefs.Cskr))=="ae7f5ddf6bef6c9c8aa5903757e7e6ba8e0bd70693806898d7e6f8531dc646a1"
                &&Sha(resources.Read(ballRefs.Cmdl))=="211e3048ab042395eb4f0835e68452e15a4fb8a7dceac15f833a19092e06b950"
                &&Sha(resources.Read(ballRefs.Cskr))=="dce1ee91aff2334520e8cea066d4421d62d82a6d373c13b3bf6d8ed1404fd5ea","Original CMDL/CSKR bytes independently match preimplementation metadata.");
            var body=Cmdl.Load(resources.Read,bodyRefs.Cmdl,bodyRefs.Cskr);var ball=Cmdl.Load(resources.Read,ballRefs.Cmdl,ballRefs.Cskr);
            var expected=Reconstruct(resources,ancs,body,ball,RetroCharacterSource.ReadSkeleton(bodyCinf),RetroCharacterSource.ReadSkeleton(ballCinf),proof);
            VerifyGlb(File.ReadAllBytes(Path.Combine(folder,Stem+".composite.glb")),expected,proof);
            using(var receipt=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,Stem+".composite.json"))))
            {
                var r=receipt.RootElement;var inputs=r.GetProperty("sourceInputs");var selection=r.GetProperty("selection");
                proof.Check(inputs.EnumerateObject().Count()==14&&inputs.EnumerateObject().All(p=>HashFile(Path.Combine(folder,p.Name))==p.Value.GetString())
                    &&r.GetProperty("sourceTriangleCount").GetInt32()==8170&&r.GetProperty("sourceBoneCount").GetInt32()==2
                    &&r.GetProperty("sourceMaterialCount").GetInt32()==6&&selection.GetProperty("clipOrdinal").GetInt32()==52
                    &&selection.GetProperty("clipResourceId").GetUInt32()==0xc2c4a405&&selection.GetProperty("clipSha256").GetString()==PosePin
                    &&selection.GetProperty("clipSeconds").GetDouble()==0&&r.GetProperty("staticSourceBake").GetBoolean()
                    &&r.GetProperty("nativeRollAndCollision").GetBoolean()&&!r.GetProperty("gameplayChanged").GetBoolean(),"Exact fourteen-input receipt preserves original fixed52 bake and separate native presentation.");
                foreach(var name in new[]{"shell","vent","vessel","line","heart"})
                {
                    int[] faces=expected.Batches.Where(b=>name=="line"?b.Name.StartsWith("line",StringComparison.Ordinal):b.Name==name).SelectMany(b=>b.Faces).Order().ToArray();
                    proof.Check(selection.GetProperty(name).GetProperty("triangles").GetInt32()==faces.Length
                        &&selection.GetProperty(name).GetProperty("sourceIdsSha256").GetString()==IdsSha(faces),"Receipt global source faces agree with independent selection: "+name);
                }
            }
            var viewer=CharacterSourcePreviewSelection.Load(Id,CancellationToken.None,imports);
            var local=CharacterSourcePreviewSelection.LoadForLocalBody(Id,CancellationToken.None,imports);var pack=local.Pack;
            proof.Check(viewer.HasBall&&!viewer.RuntimeBall&&local.HasBall&&local.RuntimeBall&&local.HasCannon&&local.RuntimeCannon,
                "Final viewer capability remains separate from explicit local ball/cannon ownership.");
            proof.Check(pack.TryResolvePrivate(Hunter.Samus,local.SelectedAppearance,CharacterModelPart.Biped,0,out var bodyAsset)
                &&pack.TryResolvePrivate(Hunter.Samus,local.SelectedAppearance,CharacterModelPart.AlternateForm,0,out _),"Final resolves independent exact body and AlternateForm assets.");
            pack.TryResolvePrivate(Hunter.Samus,local.SelectedAppearance,CharacterModelPart.AlternateForm,0,out var asset);
            proof.Check(pack.TryCreateSourcePreviewLease(bodyAsset,out var bodyLease)&&pack.TryCreateSourcePreviewLease(asset,out _),"Current actual Final body and composite have independent leases.");
            pack.TryCreateSourcePreviewLease(asset,out var lease);
            proof.Check(pack.HasSourceBall(asset)&&!pack.HasSourceBall(bodyAsset)&&lease.ModelSha256==ModelPin&&lease.SourceCannon==null
                &&lease.SourceAnimations is{IsFinalBall:true,IsBall:true,ExpectedJointCount:2}&&lease.CacheKey!=bodyLease.CacheKey,
                "Composite lease owns original2rig captured animation, separate model/cache identity and no cannon cut.");
            VerifyMaterials(resources,folder,body,ball,lease.SourceMaterials!,bodyLease.SourceMaterials!,proof);
            var geometry=CharacterWeightedModelLoader.LoadSourcePreview(asset,lease);
            proof.Check(geometry.SourceRig?.Nodes.Count==3&&geometry.Joints.Count==2&&geometry.IndexCount==8170*3&&geometry.Primitives.Count==6
                &&geometry.SourceCannon==null,"Loaded composite keeps six complete batches and both original ball joints plus mesh node.");
            var ready=CharacterSourceClip.Load(lease,asset,geometry.SourceRig!,1);var readyPose=ready.Sample(0);
            var skin=geometry.SourceRig!.Evaluate(readyPose,geometry.Joints);
            proof.Check(ready.ResourceId==0x3bf596d0&&ready.ContentSha256=="792bb9d335da503cf1fe0eb59c65a01f7c47ee2aebb48385e5d44c77736e470e"
                &&skin.Length==2&&skin.All(m=>MatrixError(m,Matrix4.Identity)<.000002f),"Original Ready1 time0 has independently identity ball skins; no second body52 skin or travel is applied.");
            for(int i=0;i<6;i++)
                proof.Check(geometry.Primitives[i].SourceMaterialOrdinal==i&&geometry.Primitives[i].SourceTriangleIds!.SequenceEqual(expected.Batches[i].CompositeFaces)
                    &&geometry.Primitives[i].Indices.Length==expected.Batches[i].Faces.Length*3,"Loaded batch retains independently mapped composite IDs/count: "+i);
            string held=GeometryHash(geometry),heldPose=PoseHash(readyPose);int reads=CharacterSourceClip.CapturedFileReads;
            using(var locked=new FileLocks(state.Files))for(int i=0;i<100;i++)
            {
                lease.RequireCurrent(asset);proof.Check(GeometryHash(geometry)==held&&PoseHash(ready.Sample(0))==heldPose,
                    "Captured composite geometry/Ready pose repeat unchanged with all source generation files exclusively locked.");
            }
            proof.Check(CharacterSourceClip.CapturedFileReads==reads,"Locked repeated CPU presentation has no lazy source clip/file reads.");
            proof.Check(!pack.TryCreateSourcePreviewLease(asset with{},out _),"Copied asset record cannot mint composite capability.");
            proof.Reject(()=>CharacterWeightedModelLoader.LoadSourcePreview(asset,bodyLease),"body lease cannot substitute composite lease");
            proof.Reject(()=>CharacterWeightedModelLoader.LoadSourcePreview(bodyAsset,lease),"composite lease cannot substitute body lease");
            foreach(int ordinal in new[]{0,2,32,52,int.MaxValue})proof.Reject(()=>CharacterSourceClip.Load(lease,asset,geometry.SourceRig!,ordinal),"composite runtime accepts only original ballReady1");
            foreach(double time in new[]{double.Epsilon,-.1,.1,double.NaN,double.PositiveInfinity})proof.Reject(()=>ready.Sample(time),"baked composite source pose never accepts travel/nonzero raw time");
            var normal=CharacterSourcePreviewSelection.LoadForLocalBody(Normal,CancellationToken.None,imports);
            normal.Pack.TryResolvePrivate(Hunter.Samus,normal.SelectedAppearance,CharacterModelPart.AlternateForm,0,out var normalAsset);
            normal.Pack.TryCreateSourcePreviewLease(normalAsset,out var normalLease);
            proof.Reject(()=>CharacterWeightedModelLoader.LoadSourcePreview(asset,normalLease),"normal ball lease cannot authorize Final composite");
            proof.Reject(()=>normalLease.SourceMaterials!.ForFinalBall(bodyLease.SourceMaterials!),"normal profile7 cannot become Final profile8");
            VerifyRefusals(temporary,folder,state,local.SelectedAppearance,proof);
            unchanged=componentHashes.All(p=>HashFile(p.Key)==p.Value);proof.Check(unchanged,"Every committed body/cannon/raw-ball/composite file remains unchanged after disposable proofs.");
            metadata=new{Generation=state.Receipt!.Generation,CompositeSha256=ModelPin,ReceiptSha256=ReceiptPin,MaterialArchiveSha256=MaterialPin,
                Triangles=8170,BatchTriangles=expected.Batches.Select(b=>b.Faces.Length),OriginalBodyPose=52,OriginalBodyPoseSha256=PosePin,FullSourceAuthoringJoints=39,FullCompositeJoints=2,
                SelectionSha256=expected.Batches.Select(b=>new{b.Name,SourceIdsSha256=IdsSha(b.Faces)}),TextureVariants=VariantPins};admitted=true;
        }
        catch(Exception e){error=e.GetType().Name;errorHash=Sha(Encoding.UTF8.GetBytes(e.Message));Console.Error.WriteLine("Final composite source CPU failure: "+e);}
        finally
        {
            try
            {
                string target=Path.GetFullPath(temporary),parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(!target.StartsWith(parent,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(target).StartsWith("prime-final-ball-source-",StringComparison.Ordinal))throw new InvalidDataException("Derived cleanup target escaped fixture root.");
                if(Directory.Exists(target))Directory.Delete(target,true);cleaned=!Directory.Exists(target);
            }catch(Exception e){error??=e.GetType().Name;}
            try{after=HashFile(rvz);}catch(Exception e){error??=e.GetType().Name;}
        }
        var finalCaches=Caches();bool cachesUnchanged=caches.All(p=>finalCaches[p.Key].SequenceEqual(p.Value)),rngUnchanged=rng1==Rng.Rng1&&rng2==Rng.Rng2;
        bool passed=error==null&&admitted&&unchanged&&cleaned&&before==after&&coreBefore==HashFile(core)&&toolBefore==HashFile(tool)&&cachesUnchanged&&rngUnchanged;
        Console.WriteLine(JsonSerializer.Serialize(new{Passed=passed,proof.Checks,proof.Refusals,FailedCheck=proof.Failed,ErrorType=error,ErrorMessageSha256=errorHash,
            CpuOnly=true,RenderingActivated=false,GameplayActivated=false,Metadata=metadata,RefusalCases=proof.Cases,SourceSha256Before=before,SourceSha256After=after,
            CoreSha256=coreBefore,ToolSha256=toolBefore,ComponentsUnchanged=unchanged,CachesUnchanged=cachesUnchanged,NativeRngUnchanged=rngUnchanged,TemporaryDerivedPayloadsDeleted=cleaned,
            Scope="Independent original39-rig pose52, triangle selection/fold/normals/UV, full2-rig composite, own material/pixel/lease authority; no actor or GPU qualification"}));
        return passed?0:1;
    }

    private static Expected Reconstruct(Resources resources,byte[] ancs,Cmdl body,Cmdl ball,RetroNativeSkeleton rig,RetroNativeSkeleton ballRig,Proof p)
    {
        p.Check(body.TriangleCount==12734&&body.PositionCount==4849&&body.Materials.Length==9&&rig.Bones.Count==39
            &&ball.TriangleCount==2220&&ball.PositionCount==1553&&ballRig.Bones.Count==2,"Complete raw original body39 and ball2 are retained before independent baking.");
        var ballAncs=Ancs.Parse(resources.Read(0x654a3f97));var ballRoot=ballAncs.Set!.Anims[1];var ballLeaves=Ancs.Prims(ballRoot.Anim);
        byte[] rawReady=resources.Read(0x3bf596d0);
        p.Check(ballRoot.Name=="B_ready_Dark_Samus_MorphBall"&&ballRoot.Anim.Kind=="play"&&ballLeaves.Count==1&&ballLeaves[0].AnimId==0x3bf596d0
            &&Sha(rawReady)=="792bb9d335da503cf1fe0eb59c65a01f7c47ee2aebb48385e5d44c77736e470e","Original raw ANCS separately identifies ballReady1, distinct from body fixed52.");
        var originalReady=Anim.Load(rawReady,true);originalReady.Sample(0,out var readyRot,out var readyTrans);var readyWorld=new Dictionary<uint,Matrix4d>();
        Matrix4d ReadyWorld(uint id)
        {
            if(readyWorld.TryGetValue(id,out var existing))return existing;var b=ballRig.Bones[id];var parent=ballRig.Bones.GetValueOrDefault(b.Parent);
            Vector3d t=new(b.X-(parent?.X??0),b.Y-(parent?.Y??0),b.Z-(parent?.Z??0));
            if(readyTrans.TryGetValue(id,out var supplied))t=new(supplied[0],supplied[1],supplied[2]);
            var q=readyRot.TryGetValue(id,out var r)?new Quaterniond(r[0],r[1],r[2],r[3]):Quaterniond.Identity;
            var local=Matrix4d.CreateFromQuaternion(q)*Matrix4d.CreateTranslation(t);
            return readyWorld[id]=parent==null?local:local*ReadyWorld(parent.Id);
        }
        foreach(var bone in ballRig.Bones.Values)
        {
            var rawSkin=Matrix4d.CreateTranslation(-bone.X,-bone.Y,-bone.Z)*ReadyWorld(bone.Id);
            p.Check(rawSkin==Matrix4d.Identity,"Each of both original raw ball Ready1 inverse-bind/skin matrices is exactly identity before composite loading.");
        }
        var parsed=Ancs.Parse(ancs);var root=parsed.Set!.Anims[52];var leaves=Ancs.Prims(root.Anim);
        p.Check(root.Name=="A_outofBoostBall_toChargeReady_dark_samus"&&root.Anim.Kind=="play"&&leaves.Count==1&&leaves[0].AnimId==0xc2c4a405
            &&parsed.Characters.Single(c=>c.Name=="Battle_3").AnimNames.Any(n=>n.Index==52),"Original ANCS itself declares exact strict52 primitive, independently of generated archives.");
        byte[] rawAnim=resources.Read(leaves[0].AnimId);p.Check(Sha(rawAnim)==PosePin&&rawAnim.Length==2829,"Original fixed52 ANIM has its own preimplementation pin.");
        var anim=Anim.Load(rawAnim,true);anim.Sample(0,out var rotations,out var translations);
        p.Check(anim.Keys==19&&Math.Abs(anim.Duration-.6000000238418579)<1e-14
            &&anim.Scale.Values.All(frames=>frames.All(v=>v.All(x=>Math.Abs(x-1)<.000001))),"Fixed source bake uses complete rigid-scale sample0, not a cannon or normal Ready pose.");
        var worlds=new Dictionary<uint,Matrix4d>();
        Matrix4d World(uint id)
        {
            if(worlds.TryGetValue(id,out var found))return found;var b=rig.Bones[id];bool parent=rig.Bones.ContainsKey(b.Parent);
            Vector3d t=parent?new(b.X-rig.Bones[b.Parent].X,b.Y-rig.Bones[b.Parent].Y,b.Z-rig.Bones[b.Parent].Z):new(b.X,b.Y,b.Z);
            if(translations.TryGetValue(id,out var supplied))t=new(supplied[0],supplied[1],supplied[2]);
            var q=rotations.TryGetValue(id,out var r)?new Quaterniond(r[0],r[1],r[2],r[3]):Quaterniond.Identity;
            Matrix4d local=Matrix4d.CreateFromQuaternion(q)*Matrix4d.CreateTranslation(t);
            return worlds[id]=parent?local*World(b.Parent):local;
        }
        Vector3d Raw(Cmdl m,int position)=>new(m.Positions[position*3],m.Positions[position*3+1],m.Positions[position*3+2]);
        var posed=new Vector3d[body.PositionCount];
        for(int i=0;i<posed.Length;i++)
        {
            Vector4d point=new(Raw(body,i),1);Vector3d sum=Vector3d.Zero;double total=0;
            foreach(var influence in body.Skin![i])
            {
                var bind=rig.Bones[influence.Bone];sum+=influence.Weight*(point*(Matrix4d.CreateTranslation(-bind.X,-bind.Y,-bind.Z)*World(influence.Bone))).Xyz;
                total+=influence.Weight;
            }
            p.Check(total>0&&Math.Abs(total-1)<.00001,"Original positive source influences are normalized without pruning.");posed[i]=sum/total;
        }
        int[] Faces(params int[] materials)=>Enumerable.Range(0,body.TriangleCount).Where(i=>materials.Contains(body.TriangleMaterial[i])).ToArray();
        int[] Points(Cmdl raw,IEnumerable<int> faces)=>faces.SelectMany(t=>new[]{raw.Triangles[t*3].P,raw.Triangles[t*3+1].P,raw.Triangles[t*3+2].P}).Distinct().ToArray();
        var originalBall=Enumerable.Range(0,ball.PositionCount).Select(i=>Raw(ball,i)).ToArray();
        Vector3d ballCenter=(originalBall.Aggregate(new Vector3d(double.PositiveInfinity),Vector3d.ComponentMin)
            +originalBall.Aggregate(new Vector3d(double.NegativeInfinity),Vector3d.ComponentMax))/2;
        var inner=Points(body,Faces(2,3,4,5,6,7,8));
        double Axis(int axis)
        {
            var values=inner.Select(i=>posed[i][axis]).Order().ToArray();
            return(values[(int)Math.Round(.02*(values.Length-1),MidpointRounding.ToEven)]+values[(int)Math.Round(.98*(values.Length-1),MidpointRounding.ToEven)])/2;
        }
        Vector3d center=new(Axis(0),Axis(1),Axis(2));int[] red=Faces(7);
        // Independently use adjacency/BFS of faces sharing raw position IDs.
        // No position welding, UV/normal connectivity or production union-find.
        var adjacent=new Dictionary<int,List<int>>();foreach(int face in red)foreach(int point in Points(body,new[]{face}))
        {if(!adjacent.TryGetValue(point,out var linked))adjacent[point]=linked=new();linked.Add(face);}
        var remaining=red.ToHashSet();var heart=new HashSet<int>();int components=0;
        foreach(int start in red)
        {
            if(!remaining.Remove(start))continue;components++;var queue=new Queue<int>();queue.Enqueue(start);var component=new List<int>();
            while(queue.TryDequeue(out int face))
            {
                component.Add(face);foreach(int point in Points(body,new[]{face}))foreach(int next in adjacent[point])if(remaining.Remove(next))queue.Enqueue(next);
            }
            var points=Points(body,component).Select(i=>posed[i]).ToArray();
            var extent=points.Aggregate(new Vector3d(double.NegativeInfinity),Vector3d.ComponentMax)-points.Aggregate(new Vector3d(double.PositiveInfinity),Vector3d.ComponentMin);
            if(component.Count>40&&Math.Max(extent.X,Math.Max(extent.Y,extent.Z))<.18)heart.UnionWith(component);
        }
        int[] heartFaces=red.Where(heart.Contains).ToArray(),vessels=red.Where(t=>!heart.Contains(t)).ToArray();
        double Facing(int face)
        {
            var a=posed[body.Triangles[face*3].P];var b=posed[body.Triangles[face*3+1].P];var c=posed[body.Triangles[face*3+2].P];
            var cross=Vector3d.Cross(b-a,c-a);var outward=(a+b+c)/3-center;
            return cross.Length<1e-12||outward.Length<1e-9?0:Vector3d.Dot(cross/cross.Length,outward/outward.Length);
        }
        int[] lines=Faces(4,5).Where(t=>Facing(t)>.25).ToArray(),line4=lines.Where(t=>body.TriangleMaterial[t]==4).ToArray(),line5=lines.Where(t=>body.TriangleMaterial[t]==5).ToArray();
        p.Check(components==7&&vessels.Length==822&&heartFaces.Length==88&&line4.Length==2098&&line5.Length==722,"Independent connectivity/facing produce exact822/2098/722/88 selections.");
        foreach(var sample in new[]{(vessels,"3a71a321cdc51f12a67c5384f23b1388b764ac99a3ce3ac7ca52a241787d56f1"),
            (lines,"edccb1dbe70f626690e0d522c8f3ffcca4c7e9ebb1dca74fc623f182188c0d06"),
            (line4,"d18ad4accd142f710d1dc92a8099581c83dc47e952489c92f50723a44f113047"),
            (line5,"75b6e0909f44e875d28a238ddb69b82dfea5f984eb6babdafa586ab56ce7f0f5"),
            (heartFaces,"628379186fd26df3f028424b5c8d2a25e08641a1fe238bf807ab0079a6c54fe8")})
            p.Check(IdsSha(sample.Item1)==sample.Item2,"Independent global source triangle array matches literal preimplementation research pin.");
        double[] distances=Points(body,vessels).Select(i=>(posed[i]-center).Length).Order().ToArray();double rmax=distances[(int)Math.Round(.995*(distances.Length-1),MidpointRounding.ToEven)];
        var heartPoints=Points(body,heartFaces);var heartCenter=heartPoints.Select(i=>posed[i]).Aggregate(Vector3d.Zero,(a,b)=>a+b)/heartPoints.Length;
        p.Check((ballCenter-new Vector3d(0,-.000006198883056640625,-.0021520256996154785)).Length<1e-14
            &&(center-new Vector3d(.036365375766482466,4.740778518541573,.38662049380268165)).Length<1e-12
            &&(heartCenter-new Vector3d(-.08465333828836533,4.8931388416547055,.3615210286993793)).Length<1e-12
            &&Math.Abs(rmax-.41165193573411873)<1e-12,"Independent percentile/unique-position centers and995percentile radius retain literal metrics.");
        Vector3d[] Map(int[] faces,Func<Vector3d,Vector3d> f){var result=new Vector3d[posed.Length];foreach(int point in Points(body,faces))result[point]=f(posed[point]);return result;}
        var vesselPositions=Map(vessels,q=>{var d=q-center;return d.Length<1e-9?ballCenter:ballCenter+d/d.Length*(.445*Math.Pow(d.Length/rmax,.2));});
        var linePositions=Map(lines,q=>{var d=q-center;return d.Length<1e-9?ballCenter:ballCenter+d/d.Length*.47;});
        var heartPositions=Map(heartFaces,q=>ballCenter+(q-heartCenter)*2.3);
        Vector3d[] Normals(int[] faces,Vector3d[] points)
        {
            var sums=new Vector3d[points.Length];foreach(int face in faces)
            {int a=body.Triangles[face*3].P,b=body.Triangles[face*3+1].P,c=body.Triangles[face*3+2].P;var cross=Vector3d.Cross(points[b]-points[a],points[c]-points[a]);sums[a]+=cross;sums[b]+=cross;sums[c]+=cross;}
            return sums.Select(n=>n.Length>1e-12?n/n.Length:Vector3d.Zero).ToArray();
        }
        var vesselNormals=Normals(vessels,vesselPositions);var lineNormals=Normals(lines,linePositions);var heartNormals=Normals(heartFaces,heartPositions);
        int[] all=Enumerable.Range(0,2220).ToArray();p.Check(IdsSha(all)=="64b59658ea98061c2a8347c1e1fe12e3c40871b2e003b2b51b001518c2a4af70","Both shell and vent keep every original whole ball triangle.");
        var lineIndices=lines.Select((face,index)=>(face,index)).ToDictionary(v=>v.face,v=>v.index);
        return new(new[]{new Batch("shell",ball,all,originalBall,null,all,3),new Batch("vent",ball,all,originalBall,null,all.Select(i=>i+2220).ToArray(),-1),
            new Batch("vessel",body,vessels,vesselPositions,vesselNormals,Enumerable.Range(4440,vessels.Length).ToArray(),7),
            new Batch("line4",body,line4,linePositions,lineNormals,line4.Select(t=>5262+lineIndices[t]).ToArray(),4),
            new Batch("line5",body,line5,linePositions,lineNormals,line5.Select(t=>5262+lineIndices[t]).ToArray(),5),
            new Batch("heart",body,heartFaces,heartPositions,heartNormals,Enumerable.Range(8082,heartFaces.Length).ToArray(),7)},ballCenter,ballRig);
    }

    private static void VerifyGlb(byte[] bytes,Expected expected,Proof p)
    {
        int jsonBytes=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12))),binAt=20+jsonBytes;
        p.Check(BinaryPrimitives.ReadUInt32LittleEndian(bytes)==0x46546c67&&BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4))==2
            &&BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8))==bytes.Length
            &&BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16))==0x4e4f534a
            &&BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(binAt+4))==0x004e4942
            &&binAt+8L+BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(binAt))==bytes.Length,"Composite GLB owns exact bounded JSON/BIN chunks.");
        using var doc=JsonDocument.Parse(bytes.AsMemory(20,jsonBytes));var root=doc.RootElement;var accessors=root.GetProperty("accessors");var views=root.GetProperty("bufferViews");
        int Access(int ordinal,int count,string type,int component,int stride)
        {
            var a=accessors[ordinal];var v=views[a.GetProperty("bufferView").GetInt32()];
            int at=v.GetProperty("byteOffset").GetInt32()+(a.TryGetProperty("byteOffset",out var offset)?offset.GetInt32():0);
            p.Check(a.GetProperty("count").GetInt32()==count&&a.GetProperty("type").GetString()==type&&a.GetProperty("componentType").GetInt32()==component
                &&v.GetProperty("byteLength").GetInt32()==count*stride&&at>=0&&at+(long)count*stride<=bytes.Length-binAt-8,"Exact complete composite accessor shape.");
            return binAt+8+at;
        }
        bool Bits(int at,float value)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at,4))==BitConverter.SingleToInt32Bits(value);
        var materials=root.GetProperty("materials");var primitives=root.GetProperty("meshes")[0].GetProperty("primitives");
        p.Check(materials.GetArrayLength()==6&&primitives.GetArrayLength()==6
            &&materials.EnumerateArray().Select(m=>m.GetProperty("extras").GetProperty("sourceMaterialOrdinal").GetInt32()).SequenceEqual(Enumerable.Range(0,6)),"Composite exports six distinct original/derived batch identities.");
        for(int batch=0;batch<6;batch++)
        {
            var e=expected.Batches[batch];var primitive=primitives[batch];var attributes=primitive.GetProperty("attributes");int count=e.Faces.Length*3;
            p.Check(primitive.GetProperty("material").GetInt32()==batch&&primitive.GetProperty("extras").GetProperty("sourceTriangleIds").EnumerateArray().Select(v=>v.GetInt32()).SequenceEqual(e.CompositeFaces),
                "Composite material/source face order stays whole and complete: "+e.Name);
            int positions=Access(attributes.GetProperty("POSITION").GetInt32(),count,"VEC3",5126,12),normals=Access(attributes.GetProperty("NORMAL").GetInt32(),count,"VEC3",5126,12),
                uv=Access(attributes.GetProperty("TEXCOORD_0").GetInt32(),count,"VEC2",5126,8),joints=Access(attributes.GetProperty("JOINTS_0").GetInt32(),count,"VEC4",5123,8),
                weights=Access(attributes.GetProperty("WEIGHTS_0").GetInt32(),count,"VEC4",5126,16),indices=Access(primitive.GetProperty("indices").GetInt32(),count,"SCALAR",5125,4);
            for(int i=0;i<count;i++)
            {
                var corner=e.Raw.Triangles[e.Faces[i/3]*3+i%3];Vector3d position=e.Positions[corner.P];
                Vector3d normal=e.Normals==null?new(e.Raw.Normals[corner.N*3],e.Raw.Normals[corner.N*3+1],e.Raw.Normals[corner.N*3+2]):e.Normals[corner.P];
                bool uvAliases=new[]{corner.T1,corner.T2}.All(alias=>alias<0||e.Raw.Uv0[corner.T0*2]==e.Raw.Uv0[alias*2]&&e.Raw.Uv0[corner.T0*2+1]==e.Raw.Uv0[alias*2+1]);
                p.Check(Bits(positions+i*12,-(float)position.X)&&Bits(positions+i*12+4,(float)position.Z)&&Bits(positions+i*12+8,(float)position.Y)
                    &&Bits(normals+i*12,-(float)normal.X)&&Bits(normals+i*12+4,(float)normal.Z)&&Bits(normals+i*12+8,(float)normal.Y)
                    &&Bits(uv+i*8,e.Raw.Uv0[corner.T0*2])&&Bits(uv+i*8+4,e.Raw.Uv0[corner.T0*2+1])&&uvAliases
                    &&BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(indices+i*4))==i,"All generated corner position/area-weighted normal/rawUV/winding bits match independent source reconstruction: "+e.Name);
                p.Check(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(joints+i*8))==1
                    &&Enumerable.Range(1,3).All(slot=>BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(joints+i*8+slot*2))==0)
                    &&Bits(weights+i*16,1)&&Enumerable.Range(1,3).All(slot=>Bits(weights+i*16+slot*4,0)),"Every baked corner carries only unchanged original ball bone1, not source body39 weights or a synthetic heart joint.");
            }
        }
        var nodes=root.GetProperty("nodes");var skin=root.GetProperty("skins")[0];
        p.Check(nodes.GetArrayLength()==3&&skin.GetProperty("joints").EnumerateArray().Select(n=>n.GetInt32()).SequenceEqual(new[]{0,1}),"Full original2rig plus own mesh node are retained without invented joints.");
        int inverses=Access(skin.GetProperty("inverseBindMatrices").GetInt32(),2,"MAT4",5126,64);
        foreach(var bone in expected.BallRig.Bones.Values.OrderBy(b=>b.Id))
        {
            var node=nodes[(int)bone.Id];var parent=expected.BallRig.Bones.GetValueOrDefault(bone.Parent);var translation=node.GetProperty("translation");
            p.Check(node.GetProperty("name").GetString()=="source_bone_"+bone.Id+"_"+bone.Name
                &&translation[0].GetSingle()==-(bone.X-(parent?.X??0))&&translation[1].GetSingle()==bone.Z-(parent?.Z??0)
                &&translation[2].GetSingle()==bone.Y-(parent?.Y??0),"Original source ball hierarchy/bind-local translation preserved exactly.");
            for(int k=0;k<16;k++)
            {
                float value=k is 0 or 5 or 10 or 15?1:k==12?bone.X:k==13?-bone.Z:k==14?-bone.Y:0;
                p.Check(Bits(inverses+(int)bone.Id*64+k*4,value),"Full original ball inverse bind bits remain unchanged.");
            }
        }
    }

    private static void VerifyMaterials(Resources resources,string folder,Cmdl body,Cmdl ball,CharacterSourceGxMaterials variant,CharacterSourceGxMaterials bodyMaterials,Proof p)
    {
        byte[] rawBytes=File.ReadAllBytes(Path.Combine(folder,Stem+".materials.bin"));var archive=RetroCharacterMaterialArchive.Read(rawBytes);
        p.Check(BinaryPrimitives.ReadUInt32LittleEndian(rawBytes.AsSpan(4))==1&&BinaryPrimitives.ReadUInt32LittleEndian(rawBytes.AsSpan(16))==8
            &&archive.Sha256==MaterialPin&&archive.Binding.CharacterId==Id+"-ball"&&archive.GeometrySha256==ModelPin
            &&archive.Binding.Cmdl==0x2f2c1e6f&&archive.Binding.Cskr==0x4403b1b9&&archive.Binding.Cinf==0xc7dbc44a&&archive.Binding.Ancs==0x654a3f97
            &&archive.Binding.SourceFingerprint=="e25524c84e498e3ee77d4717ac3a3c812fbc8bd7"&&archive.Programs.Count==1&&archive.Textures.Count==4,
            "Closed profile8 preserves original ball's raw sole program/fourTXTRs while binding the independent composite geometry.");
        byte[] sourceCmdl=resources.Read(0x2f2c1e6f);uint sections=BinaryPrimitives.ReadUInt32BigEndian(sourceCmdl.AsSpan(0x24));
        int materialAt=checked((int)((0x2c+sections*4+31)&~31u)),materialLength=checked((int)BinaryPrimitives.ReadUInt32BigEndian(sourceCmdl.AsSpan(0x2c)));
        var set=sourceCmdl.AsSpan(materialAt,materialLength);int textures=checked((int)BinaryPrimitives.ReadUInt32BigEndian(set)),countAt=4+textures*4;
        int count=checked((int)BinaryPrimitives.ReadUInt32BigEndian(set[countAt..])),recordAt=countAt+4+count*4,recordBytes=checked((int)BinaryPrimitives.ReadUInt32BigEndian(set[(countAt+4)..]));
        p.Check(count==1&&archive.CmdlSha256==Sha(sourceCmdl)&&archive.CskrSha256==Sha(resources.Read(0x4403b1b9))
            &&archive.MaterialSetSha256==Sha(set)&&archive.RawProgram(0).SequenceEqual(set.Slice(recordAt,recordBytes)),"Profile8 keeps every original raw material-set/program byte, including uninterpreted source fields.");
        p.Check(bodyMaterials.Archive.Sha256==BodyMaterialPin&&bodyMaterials.Programs.Count==9&&bodyMaterials.Programs.All(g=>g.PresentationAlphaScale==1)
            &&variant.IsFinalBall&&variant.Archive.Sha256==MaterialPin&&variant.Programs.Count==6&&variant.Images.Count==7
            &&variant.Policy!=bodyMaterials.Policy&&!variant.Policy.Contains("alpha055",StringComparison.Ordinal),"Composite derives its own six programs/seven images under a private variant policy, without Final cannon alpha.");
        foreach(var texture in archive.Textures)
            p.Check(archive.RawTexture(texture.ResourceId).SequenceEqual(resources.Read(texture.ResourceId))
                &&archive.DecodeTexture(texture.ResourceId).Pixels.SequenceEqual(RetroTextureSource.DecodeTop(resources.Read(texture.ResourceId)).Pixels),"Profile8 transports raw complete source TXTR and original decoded RGBA without baking private filters into archive.");
        var rawBall=CharacterSourceGxMaterials.Read(rawBytes,ModelPin,Id+"-ball");
        var bodyPixels=bodyMaterials.Images.ToDictionary(pair=>pair.Key,pair=>Sha(pair.Value.Pixels));var ballPixels=rawBall.Images.ToDictionary(pair=>pair.Key,pair=>Sha(pair.Value.Pixels));
        var raw=ball.Materials[0];var additive=body.Materials[4];var textureStage=raw.Stages.First(s=>s.Tex==1);
        var vent=new Cmdl.Material{Flags=additive.Flags,BlendSrc=additive.BlendSrc,BlendDst=additive.BlendDst,Channels=additive.Channels,
            TexIds=new[]{raw.TexIds[1]},TexGens=new[]{raw.TexGens[textureStage.TexCoord]},Stages=new[]{new Cmdl.TevStage{
                ColorIn=new[]{15,8,12,15},AlphaIn=new[]{7,7,7,7},ColorOp=new(){Clamp=1},AlphaOp=new(){Clamp=1},Tex=0,TexCoord=0,Ras=255,
                RawColorOp=additive.Stages[0].RawColorOp,RawAlphaOp=additive.Stages[0].RawAlphaOp}}};
        var records=new[]{body.Materials[3],vent,body.Materials[7],body.Materials[4],body.Materials[5],body.Materials[7]};
        var passes=new[]{CharacterSourceGxProgram.Pass.Alpha,CharacterSourceGxProgram.Pass.Additive,CharacterSourceGxProgram.Pass.Opaque,
            CharacterSourceGxProgram.Pass.Additive,CharacterSourceGxProgram.Pass.Additive,CharacterSourceGxProgram.Pass.Opaque};
        for(int i=0;i<records.Length;i++)
        {
            var expected=CharacterSourceGxProgram.Compile(records[i]);var actual=variant.Programs[i];
            p.Check(actual.DrawPass==passes[i]&&actual.FrontFacesOnly==(i==1)&&actual.DrawCopies==(i is 1 or 3 or 4?2:1)
                &&actual.PresentationAlphaScale==1&&actual.InitialRegister1==System.Numerics.Vector4.Zero
                &&actual.Vertex120==expected.Vertex120&&actual.Fragment120==expected.Fragment120&&actual.Vertex300==expected.Vertex300&&actual.Fragment300==expected.Fragment300
                &&actual.Textures.SequenceEqual(records[i].TexIds),"Independent source record/constructed vent yields exact desktop/ES program, pass/culling/copies authority: "+i);
        }
        p.Check(variant.Programs[1].Stages.Count==1&&variant.Programs[1].Generators.Count==1&&variant.Programs[1].Animations.Count==0
            &&variant.Programs[1].Textures.SequenceEqual(new uint[]{0x99e37db0}),"Only vent uses independently constructed one-stage/one-texgen additive ball-glow authority.");
        foreach(var image in variant.Images)
        {
            var original=RetroTextureSource.DecodeTop(resources.Read(image.Key));byte[] pixels=original.Pixels.ToArray();
            if(VariantPins.ContainsKey(image.Key))FilterExpected(image.Key,pixels);
            p.Check(image.Value.Pixels.SequenceEqual(pixels)&&(image.Value.Layout with{Mips=original.Layout.Mips})==original.Layout
                &&image.Value.Layout.Mips.SequenceEqual(original.Layout.Mips)
                &&(!VariantPins.TryGetValue(image.Key,out var pin)||Sha(pixels)==pin),"Every original or privately filtered texel agrees with independent source decode/filter and corrected research pin.");
            if(VariantPins.ContainsKey(image.Key))
                p.Check(!bodyMaterials.Images.TryGetValue(image.Key,out var baseBody)||!ReferenceEquals(ImageStorage(baseBody),ImageStorage(image.Value)),"Filtered composite pixel storage is detached from body images.");
            if(VariantPins.ContainsKey(image.Key)&&rawBall.Images.TryGetValue(image.Key,out var baseBall))
                p.Check(!ReferenceEquals(ImageStorage(baseBall),ImageStorage(image.Value)),"Filtered composite pixel storage is detached from raw ball images.");
        }
        var again=rawBall.ForFinalBall(bodyMaterials);
        p.Check(!ReferenceEquals(again,variant)&&ReferenceEquals(again.Archive,rawBall.Archive)
            &&again.Images.All(pair=>pair.Value.Pixels.SequenceEqual(variant.Images[pair.Key].Pixels))
            &&bodyPixels.All(pair=>Sha(bodyMaterials.Images[pair.Key].Pixels)==pair.Value)&&ballPixels.All(pair=>Sha(rawBall.Images[pair.Key].Pixels)==pair.Value)
            &&HashFile(Path.Combine(folder,Id+".materials.bin"))==BodyMaterialPin&&HashFile(Path.Combine(folder,Stem+".materials.bin"))==MaterialPin,
            "Repeated private derivation preserves original archive bytes/programs and every base image array.");
        foreach(var image in bodyMaterials.Images)p.Check(image.Value.Pixels.SequenceEqual(RetroTextureSource.DecodeTop(resources.Read(image.Key)).Pixels),"Every retained original body decoded image stays bit-exact after private derivation.");
        for(int i=0;i<bodyMaterials.Programs.Count;i++)
        {
            var original=CharacterSourceGxProgram.Compile(body.Materials[i]);var retained=bodyMaterials.Programs[i];
            p.Check(retained.PresentationAlphaScale==1&&retained.Vertex120==original.Vertex120&&retained.Fragment120==original.Fragment120
                &&retained.Vertex300==original.Vertex300&&retained.Fragment300==original.Fragment300,"Every retained body program remains its original source equation after composite derivation.");
        }
        foreach(uint id in VariantPins.Keys)p.Check(!ReferenceEquals(ImageStorage(again.Images[id]),ImageStorage(variant.Images[id])),"Each independent derivation owns detached filtered pixels.");
        p.Reject(()=>rawBall.ForFinalBall(bodyMaterials.ForFinalCannon()),"Final cannon alpha055 variant cannot authorize composite materials");
        p.Reject(()=>variant.ForFinalBall(bodyMaterials),"Composite derived materials cannot masquerade as raw profile8 inputs");
        p.Reject(()=>rawBall.ForFinalCannon(),"Profile8 ball cannot become a Final cannon");
    }

    private static void FilterExpected(uint id,byte[] rgba)
    {
        for(int i=0;i<rgba.Length;i+=4)
        {
            if(id==0xbc7a8b7b)
            {
                int a=rgba[i+3];rgba[i+3]=(byte)(a<=109?115:(200*a+4233+114)/228);continue;
            }
            double l=(rgba[i]+rgba[i+1]+rgba[i+2])/765d;
            if(id==0x99e37db0)
            {
                double k=Math.Min(1d,Math.Max(0d,(l-.18)/.30))*.35;
                for(int channel=0;channel<3;channel++)rgba[i+channel]=(byte)(rgba[i+channel]*k);
            }
            else
            {
                double f=l<.5?Math.Pow(Math.Min(1d,Math.Max(0d,(l-.10)/.24)),1.5):0;
                for(int channel=0;channel<3;channel++)rgba[i+channel]=(byte)((.6*new[]{.35,.6,1d}[channel]+.4*rgba[i+channel]/255d)*f*.65*255);
            }
        }
    }

    private static void VerifyRefusals(string temporary,string folder,ImportBundleInspection state,PrivateCharacterAppearance appearance,Proof p)
    {
        string mutable=Path.Combine(temporary,"mutable");Directory.CreateDirectory(mutable);
        foreach(string file in state.Files)File.Copy(file,Path.Combine(mutable,Path.GetFileName(file)));
        byte[] manifest=File.ReadAllBytes(Path.Combine(mutable,"characters.json"));
        (CharacterModelPack Pack,CharacterModelAsset Asset,CharacterModelPack.SourcePreviewLease Lease) Load()
        {
            var pack=CharacterModelPack.Load(mutable);
            if(!pack.TryResolvePrivate(Hunter.Samus,appearance,CharacterModelPart.AlternateForm,0,out var asset))throw new InvalidDataException("Exact own Final composite asset required.");
            pack.CaptureSourceBallMaterials(asset);
            if(!pack.TryCreateSourcePreviewLease(asset,out var lease))throw new InvalidDataException("Exact own Final composite lease required.");
            var geometry=CharacterWeightedModelLoader.LoadSourcePreview(asset,lease);_ = CharacterSourceClip.Load(lease,asset,geometry.SourceRig!,1).Sample(0);
            return(pack,asset,lease);
        }
        _=Load();
        void Manifest(string label,Action<JsonObject> edit)
        {
            var value=JsonNode.Parse(manifest)!.AsObject();var entry=value["models"]!.AsArray().Single(n=>n!["part"]!.GetValue<string>()=="AlternateForm")!.AsObject();
            try{edit(entry);File.WriteAllText(Path.Combine(mutable,"characters.json"),value.ToJsonString());p.Reject(()=>Load(),label);}
            finally{File.WriteAllBytes(Path.Combine(mutable,"characters.json"),manifest);}
        }
        Manifest("Final composite refuses wrong native Hunter",e=>e["hunter"]="Weavel");
        Manifest("Final composite refuses normal appearance alias",e=>e["skinSource"]="MP2DarkSamus");
        Manifest("Final composite refuses wrong part",e=>e["part"]="ViewModel");
        Manifest("Final composite refuses wrong LOD",e=>e["lod"]=1);
        Manifest("Raw original ball geometry is not Final composite",e=>e["model"]=Stem+".source.glb");
        Manifest("Body geometry cannot substitute composite",e=>e["model"]=Id+".source.glb");
        Manifest("Body animation bank cannot substitute original ball bank",e=>e["sourceAnimationArchive"]=Id+".animations.bin");
        using var receipt=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,Stem+".composite.json")));
        var required=receipt.RootElement.GetProperty("sourceInputs").EnumerateObject().Select(n=>n.Name)
            .Concat(new[]{Stem+".composite.glb",Stem+".composite.json"}).Distinct().ToArray();
        foreach(string name in required)
        {
            string path=Path.Combine(mutable,name);byte[] original=File.ReadAllBytes(path),changed=original.ToArray();changed[^1]^=1;
            try{File.WriteAllBytes(path,changed);p.Reject(()=>Load(),"Changed exact Final composite input: "+name);}finally{File.WriteAllBytes(path,original);}
            try{File.Delete(path);p.Reject(()=>Load(),"Missing exact Final composite input: "+name);}finally{File.WriteAllBytes(path,original);}
        }
        string materials=Path.Combine(mutable,Stem+".materials.bin");byte[] own=File.ReadAllBytes(materials);
        try{File.Copy(Path.Combine(folder,Id+".materials.bin"),materials,true);p.Reject(()=>Load(),"Final body archive cannot substitute profile8 ball archive");}
        finally{File.WriteAllBytes(materials,own);}
        var held=Load();
        p.Check(held.Lease.SourceMaterials is{IsFinalBall:true},"Restored exact generation can mint a fresh independent composite lease.");
        string geometryPath=Path.Combine(mutable,Stem+".composite.glb");byte[] saved=File.ReadAllBytes(geometryPath),corrupt=saved.ToArray();corrupt[^1]^=1;
        try
        {
            File.WriteAllBytes(geometryPath,corrupt);p.Reject(()=>held.Lease.Require(held.Asset),"Changed source retires the admitted exact composite owner");
        }
        finally{File.WriteAllBytes(geometryPath,saved);}
        p.Reject(()=>held.Lease.RequireCurrent(held.Asset),"Restoring bytes never resurrects an earlier retired lease");
        p.Check(!held.Pack.TryCreateSourcePreviewLease(held.Asset,out _),"Retired proof owner cannot issue a new source lease without explicit reopen.");
        var reopened=Load();p.Check(!ReferenceEquals(reopened.Pack,held.Pack)&&reopened.Lease.SourceMaterials is{IsFinalBall:true},"Fresh reopened owner can admit restored complete immutable inputs.");
    }

    private sealed class Resources:IDisposable
    {
        private readonly GameCubeDiscReader _disc;private readonly List<RetroPakReader> _paks=new();
        internal Resources(string source)
        {
            _disc=GameCubeDiscReader.Open(source);
            try
            {
                if(_disc.GameId!="G2ME01"||_disc.Revision!=0||_disc.DiscNumber!=0||ExternalSourceRequirements.VerifyKnown(_disc).Any(r=>r.Status!=ExternalArchiveStatus.Verified))
                    throw new InvalidDataException("Exact individually SHA1-verified Echoes source edition required before resource reads.");
                foreach(var requirement in ExternalSourceRequirements.For(_disc))_paks.Add(new(_disc.OpenRead(requirement.Path),leaveOpen:false));
            }catch{Dispose();throw;}
        }
        internal byte[] Read(uint id)=>_paks.First(p=>p.Contains(id)).ReadResource(id);
        public void Dispose(){foreach(var pak in _paks)pak.Dispose();_disc.Dispose();}
    }
    private sealed class FileLocks:IDisposable
    {
        private readonly List<FileStream> _files=new();
        internal FileLocks(IEnumerable<string> paths){try{foreach(string path in paths)_files.Add(new(path,FileMode.Open,FileAccess.Read,FileShare.None));}catch{Dispose();throw;}}
        public void Dispose(){foreach(var file in _files)file.Dispose();}
    }
    private static object ImageStorage(RetroTextureSource.Decoded image)=>typeof(RetroTextureSource.Decoded).GetField("_pixels",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(image)!;
    private static Dictionary<string,object[]> Caches()=>new[]{"_modelCache","_fhModelCache","_retiredRoomModels","_effects","_particleDefs"}
        .ToDictionary(n=>n,n=>((IEnumerable)typeof(Read).GetField(n,BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!).Cast<object>().ToArray());
    private static float[] Values(Matrix4 m)=>new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
    private static float MatrixError(Matrix4 a,Matrix4 b)=>Values(a).Zip(Values(b),(x,y)=>MathF.Abs(x-y)).Max();
    private static string PoseHash(IReadOnlyList<Matrix4> pose)
    {
        using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))foreach(var m in pose)foreach(float value in Values(m))writer.Write(value);
        return Sha(stream.ToArray());
    }
    private static string GeometryHash(CharacterWeightedModelData geometry)
    {
        using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
        {
            foreach(var primitive in geometry.Primitives)
            {
                writer.Write(primitive.SourceMaterialOrdinal);foreach(int face in primitive.SourceTriangleIds!)writer.Write(face);
                foreach(uint index in primitive.Indices)writer.Write(index);foreach(int joint in primitive.JointPalette!)writer.Write(joint);
                foreach(var v in primitive.Vertices)
                {writer.Write(v.Position.X);writer.Write(v.Position.Y);writer.Write(v.Position.Z);writer.Write(v.Normal.X);writer.Write(v.Normal.Y);writer.Write(v.Normal.Z);
                    writer.Write(v.Texcoord.X);writer.Write(v.Texcoord.Y);writer.Write(v.Weights.X);writer.Write(v.Weights.Y);writer.Write(v.Weights.Z);writer.Write(v.Weights.W);writer.Write(v.PackedJoints);}
            }
            foreach(var joint in geometry.Joints){writer.Write(joint.SourceNode);writer.Write(joint.TargetNode);foreach(float value in Values(joint.InverseBind))writer.Write(value);}
            foreach(var node in geometry.SourceRig!.Nodes){writer.Write(node.Name);writer.Write(node.Parent);foreach(float value in Values(node.Local))writer.Write(value);}
        }
        return Sha(stream.ToArray());
    }
    private static string IdsSha(int[] ids)=>Sha(JsonSerializer.SerializeToUtf8Bytes(ids));
    private static string Sha(ReadOnlySpan<byte> bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string HashFile(string file){using var stream=File.OpenRead(file);return Convert.ToHexStringLower(SHA256.HashData(stream));}
}
