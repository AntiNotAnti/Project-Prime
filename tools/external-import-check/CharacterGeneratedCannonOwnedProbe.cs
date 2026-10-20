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

// Optional local CPU acceptance. Original source and all temporary derivatives
// remain private; only identity hashes, counts and boolean outcomes are printed.
internal static class CharacterGeneratedCannonOwnedProbe
{
    private const string Id="darksamus-echoes",Receipt=Id+".cannon-source.json";
    internal static int Run(string rvz)
    {
        string temporary=Path.Combine(Path.GetTempPath(),"prime-generated-cannon-"+Guid.NewGuid().ToString("N"));
        string core=typeof(RetroCharacterPreviewExporter).Assembly.Location,tool=typeof(CharacterGeneratedCannonOwnedProbe).Assembly.Location;
        string coreBefore=HashFile(core),toolBefore=HashFile(tool);var caches=Caches();
        string? before=null,after=null,receiptSha=null,cutSha=null,basisSha=null,errorType=null,errorSha=null,generation=null;
        string stage="source",independentBasisSha="";int checks=0,refusals=0,triangles=0,batches=0;bool cleaned=false,cachesUnchanged=false,admitted=false;
        var cases=new List<string>();
        void Check(bool value,string why){if(!value)throw new InvalidDataException(why);checks++;}
        void Reject(Action action,string why)
        {
            try{action();}catch(Exception e)when(e is InvalidDataException or IOException or NotSupportedException or ArgumentException or JsonException)
            {refusals++;cases.Add(why);return;}
            throw new InvalidDataException("Expected generated cannon refusal: "+why);
        }
        try
        {
            before=HashFile(rvz);Directory.CreateDirectory(temporary);string imports=Path.Combine(temporary,"imports");
            var outputs=RetroCharacterPreviewExporter.ExportFromOwnedGame(rvz,imports,selectedIds:new[]{Id});
            var spec=RetroCharacterPreviewExporter.BundleSpec(Id);var state=LocalImportBundle.Inspect(imports,Id,spec);
            Check(spec.Version==5&&outputs.Count==1&&state.State==ImportBundleState.Ready&&state.Receipt!=null,"Actual importer must publish complete normal recipe5.");
            string folder=Path.GetDirectoryName(outputs.Single())!;generation=state.Receipt!.Generation;
            Check(Path.GetFileName(folder)==generation&&state.Files.All(f=>Path.GetDirectoryName(f)==folder),"One immutable source generation required.");
            var original=state.Files.ToDictionary(f=>Path.GetFileName(f)!,f=>HashFile(f));
            byte[] receipt=File.ReadAllBytes(Path.Combine(folder,Receipt));receiptSha=Sha(receipt);
            using var parsed=JsonDocument.Parse(receipt);var proof=parsed.RootElement;
            int[][] declared=proof.GetProperty("triangles").EnumerateArray().Select(a=>a.EnumerateArray().Select(n=>n.GetInt32()).ToArray()).ToArray();
            float[] matrix=proof.GetProperty("toCannon").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
            Check(matrix.Length==16&&matrix.All(float.IsFinite),"Complete finite authoring basis required.");
            cutSha=Digest(w=>{w.Write(declared.Length);foreach(var material in declared){w.Write(material.Length);foreach(int id in material)w.Write(id);}});
            basisSha=Digest(w=>{foreach(float value in matrix)w.Write(value);});
            Check(proof.GetProperty("format").GetInt32()==2&&proof.GetProperty("clipOrdinal").GetInt32()==32
                &&proof.GetProperty("clipResourceId").GetUInt32()==0xd12fdfa3&&proof.GetProperty("clipSeconds").GetDouble()==0
                &&proof.GetProperty("sourceElbowId").GetInt32()==24&&proof.GetProperty("sourceParticleLocatorId").GetInt32()==32
                &&proof.GetProperty("sourceCharacter").GetString()==Id,"Receipt must retain fixed original source IDs and zero authoring phase.");
            foreach(var input in proof.GetProperty("sourceInputs").EnumerateObject())
            {
                string path=Path.Combine(folder,input.Name);LocalImportBundle.CheckPath(path,imports);
                Check(state.Files.Contains(path)&&HashFile(path)==input.Value.GetString(),"Receipt source inputs match committed generation.");
            }
            // This oracle opens the original source archives independently of
            // generated GLB/source-ID metadata and of the runtime cut selector.
            using(var resources=new OwnedResources(rvz))
            {
                byte[] ancs=resources.Read(0x39900a11);var refs=RetroCharacterSource.FindCharacter(ancs,"Dark_Samus");
                var raw=Cmdl.Load(resources.Read,refs.Cmdl,refs.Cskr);var skeleton=RetroCharacterSource.ReadSkeleton(resources.Read(refs.Cinf));
                bool Descends(uint bone)
                {
                    var seen=new HashSet<uint>();
                    while(skeleton.Bones.TryGetValue(bone,out var b))
                    {if(!seen.Add(bone))throw new InvalidDataException("Original cut ancestry cycle.");if(bone==24)return true;bone=b.Parent;}
                    if(bone!=97)throw new InvalidDataException("Original source ancestry must end at root97.");return false;
                }
                var expected=Enumerable.Range(0,raw.Materials.Length).Select(_=>new List<int>()).ToArray();
                for(int face=0;face<raw.TriangleCount;face++)
                {
                    bool selected=true;
                    for(int corner=0;corner<3;corner++)selected&=raw.Skin![raw.Triangles[face*3+corner].P].Any(v=>v.Weight>0&&Descends(v.Bone));
                    if(selected)expected[raw.TriangleMaterial[face]].Add(face);
                }
                Check(expected.Length==7&&expected.Sum(x=>x.Count)==392&&expected[2].Count==66&&expected[5].Count==326
                    &&declared.Length==expected.Length&&declared.Select((x,m)=>x.SequenceEqual(expected[m])).All(x=>x),
                    "Every declared global triangle ID matches the original any-positive elbow-descendant cut.");
                var sourceAnim=Anim.Load(resources.Read(0xd12fdfa3),true);sourceAnim.Sample(0,out var rot,out var trans);
                var worlds=new Dictionary<uint,Matrix4>();
                Matrix4 World(uint id)
                {
                    if(worlds.TryGetValue(id,out var cached))return cached;var b=skeleton.Bones[id];
                    double[] bind={b.X,b.Y,b.Z};if(skeleton.Bones.TryGetValue(b.Parent,out var parent)){bind[0]-=parent.X;bind[1]-=parent.Y;bind[2]-=parent.Z;}
                    var q=rot.GetValueOrDefault(id);var p=trans.GetValueOrDefault(id)??bind;
                    Matrix4 local=(q==null?Matrix4.Identity:Matrix4.CreateFromQuaternion(new Quaternion((float)q[0],(float)q[1],(float)q[2],(float)q[3])))
                        *Matrix4.CreateTranslation((float)p[0],(float)p[1],(float)p[2]);
                    return worlds[id]=skeleton.Bones.ContainsKey(b.Parent)?local*World(b.Parent):local;
                }
                Vector3 Convert(Vector3 p)=>new(-p.X,p.Z,p.Y);
                Vector3 elbow=Convert(World(24).Row3.Xyz),tip=Convert(World(32).Row3.Xyz);
                Vector3 forward=Vector3.Normalize(tip-elbow),up=Vector3.Normalize(Vector3.UnitY-Vector3.Dot(Vector3.UnitY,forward)*forward),left=Vector3.Cross(up,forward);
                Matrix4 independent=Matrix4.CreateTranslation(-tip)*new Matrix4(left.X,up.X,forward.X,0,left.Y,up.Y,forward.Y,0,left.Z,up.Z,forward.Z,0,0,0,0,1);
                float[] expectedMatrix=Values(independent);independentBasisSha=Digest(w=>{foreach(float v in expectedMatrix)w.Write(v);});
                Check(matrix.Zip(expectedMatrix,(a,b)=>MathF.Abs(a-b)).Max()<.0001f,"Authoring basis reproduces independently from original zero-phase ANIM/CINF and source locators.");
            }
            // Emit these identities before admission: pending profile pins are
            // expected to fail later and must never suppress measurement.
            stage="metadata-complete";
            Console.WriteLine(JsonSerializer.Serialize(new{Schema=1,Kind="generated-cannon-authoring-identities",MetadataOutputOnly=true,
                ReceiptSha256=receiptSha,CutIdsSha256=cutSha,BasisSha256=basisSha,IndependentBasisSha256=independentBasisSha,
                OriginalCutVerified=true,Triangles=392,Material2Triangles=66,Material5Triangles=326,
                PendingRuntimePin=CharacterSourceCannonRecipe.QualifiedGeneratedReceiptSha256!=receiptSha}));
            stage="runtime-admission";
            var pack=CharacterModelPack.Load(folder);var appearance=new PrivateCharacterAppearance("darksamus","MP2DarkSamus");
            Check(pack.TryResolvePrivate(Hunter.Samus,appearance,CharacterModelPart.Biped,0,out var bodyAsset)
                &&pack.TryResolvePrivate(Hunter.Samus,appearance,CharacterModelPart.ViewModel,0,out _),"Actual pack resolves distinct body and explicit source ViewModel.");
            Check(pack.TryResolvePrivate(Hunter.Samus,appearance,CharacterModelPart.ViewModel,0,out var cannonAsset)
                &&!ReferenceEquals(bodyAsset,cannonAsset)&&pack.HasSourceCannon(cannonAsset)&&!pack.HasSourceCannon(bodyAsset),"Only the explicit ViewModel owns the cannon capability.");
            Check(pack.TryCreateSourcePreviewLease(bodyAsset,out var bodyLease)&&pack.TryCreateSourcePreviewLease(cannonAsset,out _),"Each exact asset receives its own lease.");
            Check(pack.TryCreateSourcePreviewLease(cannonAsset,out var cannonLease)&&bodyLease.ModelSha256==cannonLease.ModelSha256
                &&bodyLease.CacheKey!=cannonLease.CacheKey&&!ReferenceEquals(bodyLease,cannonLease),"Same generated GLB has distinct body/ViewModel owner identity.");
            Check(!pack.TryCreateSourcePreviewLease(bodyAsset with{},out _)&&!pack.TryCreateSourcePreviewLease(cannonAsset with{},out _),"Copied asset DTO cannot acquire either source capability.");
            Reject(()=>CharacterWeightedModelLoader.LoadSourcePreview(bodyAsset,cannonLease),"cannon-lease-body-swap");
            Reject(()=>CharacterWeightedModelLoader.LoadSourcePreview(cannonAsset,bodyLease),"body-lease-cannon-swap");
            var body=CharacterWeightedModelLoader.LoadSourcePreview(bodyAsset,bodyLease);var cannon=CharacterWeightedModelLoader.LoadSourcePreview(cannonAsset,cannonLease);
            Check(body.IndexCount/3==6340&&cannon.IndexCount/3==392&&cannon.SourceCannon?.IsGenerated==true&&body.SourceCannon==null
                &&cannon.SourceRig?.Nodes.Count==40&&cannon.Joints.Count==39&&cannon.Joints.SequenceEqual(body.Joints),"Loader retains complete rig with exact generated cut and no body replacement.");
            var whole=Triangles(body);var cut=Triangles(cannon);triangles=cut.Count;batches=cannon.Primitives.Count;
            Check(cut.Keys.Order().SequenceEqual(declared.SelectMany(x=>x).Order())&&cut.All(p=>whole.TryGetValue(p.Key,out string? value)&&value==p.Value),
                "Every loaded cut source ID preserves exact fullbody corner attributes, weights, local slots and complete palette.");
            var clip=CharacterSourceClip.Load(cannonLease,cannonAsset,cannon.SourceRig!,32);
            Check(clip.Ordinal==32&&clip.ResourceId==0xd12fdfa3&&clip.Sample(0).Count==40,"Fixed primitive32 resource retains complete zero-phase pose.");
            var bodyClip=CharacterSourceClip.Load(bodyLease,bodyAsset,body.SourceRig!,32);
            int samplingReadsBefore=CharacterSourceClip.CapturedFileReads;
            Check(clip.Sample(0).SequenceEqual(bodyClip.Sample(0))&&cannon.SourceAuthoringPose!.SequenceEqual(bodyClip.Sample(0)),"Cannon authoring pose equals fullbody original zero phase.");
            foreach(int ordinal in new[]{0,31,33,76})Reject(()=>CharacterSourceClip.Load(cannonLease,cannonAsset,cannon.SourceRig!,ordinal),"foreign-cannon-clip-"+ordinal);
            foreach(double time in new[]{double.Epsilon,.1,-.1,double.MaxValue,double.NaN,double.PositiveInfinity})Reject(()=>clip.Sample(time),"nonzero-or-nonfinite-cannon-phase");
            Check(samplingReadsBefore==CharacterSourceClip.CapturedFileReads,"Captured cannon/body phase sampling has no source file IO.");
            var declaredBasis=new Matrix4(matrix[0],matrix[1],matrix[2],matrix[3],matrix[4],matrix[5],matrix[6],matrix[7],matrix[8],matrix[9],matrix[10],matrix[11],matrix[12],matrix[13],matrix[14],matrix[15]);
            Check(CharacterSourceClip.MatrixDistance(cannon.SourceCannon!.ToCannon,declaredBasis)<.00001f,"Runtime retains exact authoring basis.");
            stage="negative-fixtures";string mutable=Path.Combine(temporary,"mutable");Directory.CreateDirectory(mutable);
            string[] names=proof.GetProperty("sourceInputs").EnumerateObject().Select(p=>p.Name).Append(Receipt).Append("characters.json").Distinct().ToArray();
            foreach(string name in names){LocalImportBundle.CheckPath(Path.Combine(folder,name),imports);File.Copy(Path.Combine(folder,name),Path.Combine(mutable,name));}
            foreach(string name in new[]{Id+".ball.source.glb",Id+".ball.source.json"})
            {LocalImportBundle.CheckPath(Path.Combine(folder,name),imports);Check(state.Files.Contains(Path.Combine(folder,name)),"Wrong-part fixture copies verified real ball inputs.");File.Copy(Path.Combine(folder,name),Path.Combine(mutable,name));}
            byte[] manifest=File.ReadAllBytes(Path.Combine(mutable,"characters.json"));
            void Mutate(string label,Action<JsonObject,JsonObject> edit,string? file=null,byte[]? data=null)
            {
                byte[]? saved=file==null?null:File.ReadAllBytes(Path.Combine(mutable,file));
                var changed=JsonNode.Parse(receipt)!.AsObject();var document=JsonNode.Parse(manifest)!.AsObject();
                var entry=document["models"]!.AsArray().Single(e=>e!["part"]!.GetValue<string>()=="ViewModel")!.AsObject();
                try
                {
                    if(file!=null)File.WriteAllBytes(Path.Combine(mutable,file),data!);edit(changed,entry);
                    byte[] rewritten=Encoding.UTF8.GetBytes(changed.ToJsonString());entry["sourceCannonSha256"]=Sha(rewritten);
                    File.WriteAllBytes(Path.Combine(mutable,Receipt),rewritten);File.WriteAllText(Path.Combine(mutable,"characters.json"),document.ToJsonString());
                    Reject(()=>CharacterModelPack.Load(mutable),label);
                }
                finally
                {
                    if(file!=null)File.WriteAllBytes(Path.Combine(mutable,file),saved!);
                    File.WriteAllBytes(Path.Combine(mutable,Receipt),receipt);File.WriteAllBytes(Path.Combine(mutable,"characters.json"),manifest);
                }
            }
            Check(CharacterModelPack.Load(mutable).TryResolvePrivate(Hunter.Samus,appearance,CharacterModelPart.ViewModel,0,out _),"Unchanged copied generation remains valid before mutation.");
            Mutate("changed-rehashed-global-cut",(p,_)=>p["triangles"]![2]![0]=1);
            Mutate("changed-rehashed-basis",(p,_)=>p["toCannon"]![12]=.01);
            Mutate("changed-rehashed-phase",(p,_)=>p["clipSeconds"]=.1);
            Mutate("changed-rehashed-resource",(p,_)=>p["clipResourceId"]=0xd12fdfa2u);
            Mutate("false-first-person-qualification",(p,_)=>p["firstPersonVerified"]=true);
            Mutate("wrong-source-part",(p,_)=>p["sourceCharacter"]="darksamus-overcharged-echoes");
            Mutate("wrong-presentation-part",(_,e)=>e["part"]="AlternateForm");
            Mutate("Final-refusal",(_,e)=>e["skinSource"]="MP2DarkSamusFinal");
            Mutate("ball-substitution",(_,e)=>{e["model"]=Id+".ball.source.glb";e["provenance"]=Id+".ball.source.json";e["provenanceSha256"]=HashFile(Path.Combine(mutable,Id+".ball.source.json"));});
            byte[] glb=File.ReadAllBytes(Path.Combine(mutable,Id+".source.glb"));glb[^1]^=1;
            Mutate("changed-source-glb",(_,_)=>{},Id+".source.glb",glb);
            byte[] cinf=File.ReadAllBytes(Path.Combine(mutable,Id+".cinf.bin"));cinf[12]^=1;
            Mutate("changed-source-cinf",(_,_)=>{},Id+".cinf.bin",cinf);
            // Keep receipt/manifest bytes exactly pinned: each refusal here
            // must come from a changed real source input, not a rehashed recipe.
            foreach(var input in new[]{(Id+".ancs.bin","changed-source-ANCS"),(Id+".ancs-graph.json","changed-source-graph"),(Id+".animations.bin","changed-source-archive")})
            {
                string path=Path.Combine(mutable,input.Item1);byte[] saved=File.ReadAllBytes(path);byte[] changed=(byte[])saved.Clone();changed[^1]^=1;
                try
                {
                    Check(HashFile(Path.Combine(mutable,Receipt))==receiptSha&&File.ReadAllBytes(Path.Combine(mutable,"characters.json")).AsSpan().SequenceEqual(manifest),
                        "Source-input refusal fixture retains exact pinned receipt and manifest.");
                    File.WriteAllBytes(path,changed);Reject(()=>CharacterModelPack.Load(mutable),input.Item2);
                }
                finally{File.WriteAllBytes(path,saved);}
            }
            var validCopy=CharacterModelPack.Load(mutable);
            Check(validCopy.TryResolvePrivate(Hunter.Samus,appearance,CharacterModelPart.ViewModel,0,out var copyAsset)
                &&validCopy.TryCreateSourcePreviewLease(copyAsset,out _),"Every negative fixture restores an admissible source owner.");
            Check(validCopy.TryCreateSourcePreviewLease(copyAsset,out var copyLease),"Exact copied generation issues current ViewModel lease.");
            var copyModel=CharacterWeightedModelLoader.LoadSourcePreview(copyAsset,copyLease);var copyClip=CharacterSourceClip.Load(copyLease,copyAsset,copyModel.SourceRig!,32);
            File.Delete(Path.Combine(mutable,Receipt));
            Check(copyClip.Sample(0).Count==40,"Captured zero-phase sampling performs no deferred source IO.");
            Reject(()=>copyLease.Require(copyAsset),"explicit-proof-detects-missing-receipt");Reject(()=>copyClip.Sample(0),"retired-source-cannon-clip");
            Check(state.Files.All(f=>original[Path.GetFileName(f)]==HashFile(f)),"Negative fixtures leave committed source generation byte-identical.");
            admitted=true;stage="complete";
        }
        catch(Exception e){errorType=e.GetType().Name;errorSha=Sha(Encoding.UTF8.GetBytes(e.Message));}
        finally
        {
            try{if(Directory.Exists(temporary))Directory.Delete(temporary,true);cleaned=!Directory.Exists(temporary);}catch(Exception e){errorType??=e.GetType().Name;errorSha??=Sha(Encoding.UTF8.GetBytes(e.Message));}
            try{after=HashFile(rvz);}catch(Exception e){errorType??=e.GetType().Name;errorSha??=Sha(Encoding.UTF8.GetBytes(e.Message));}
            var final=Caches();cachesUnchanged=caches.All(p=>final[p.Key].SequenceEqual(p.Value));
        }
        string coreAfter=HashFile(core),toolAfter=HashFile(tool);
        bool passed=errorType==null&&admitted&&cleaned&&before!=null&&before==after&&coreBefore==coreAfter&&toolBefore==toolAfter&&cachesUnchanged;
        Console.WriteLine(JsonSerializer.Serialize(new{Schema=1,Passed=passed,Stage=stage,Checks=checks,Refusals=refusals,Cases=cases,ErrorType=errorType,ErrorMessageSha256=errorSha,
            CpuOnly=true,MetadataOutputOnly=true,RenderingActivated=false,GameplayActivated=false,ReceiptSha256=receiptSha,CutIdsSha256=cutSha,BasisSha256=basisSha,IndependentBasisSha256=independentBasisSha,
            SourceBeforeSha256=before,SourceAfterSha256=after,CoreBeforeSha256=coreBefore,CoreAfterSha256=coreAfter,ToolBeforeSha256=toolBefore,ToolAfterSha256=toolAfter,
            TemporaryDerivedPayloadsDeleted=cleaned,RetainedSourcePayloadsWritten=!cleaned,CachesUnchanged=cachesUnchanged,Generation=generation,RuntimeAdmitted=admitted,Triangles=triangles,Batches=batches}));
        return passed?0:1;
    }
    private static Dictionary<int,string> Triangles(CharacterWeightedModelData model)
    {
        var result=new Dictionary<int,string>();
        foreach(var p in model.Primitives)
        {
            if(p.SourceTriangleIds==null||p.JointPalette==null)throw new InvalidDataException("Original source IDs/palettes required.");
            for(int triangle=0;triangle<p.SourceTriangleIds.Length;triangle++)
            {
                int t=triangle;
                result.Add(p.SourceTriangleIds[t],Digest(w=>
                {
                    w.Write(p.SourceMaterialOrdinal);w.Write(p.SourcePrimitiveOrdinal);w.Write(p.JointPalette.Length);foreach(int joint in p.JointPalette)w.Write(joint);
                    for(int c=0;c<3;c++)
                    {
                        var v=p.Vertices[p.Indices[t*3+c]];
                        w.Write(v.Position.X);w.Write(v.Position.Y);w.Write(v.Position.Z);w.Write(v.Normal.X);w.Write(v.Normal.Y);w.Write(v.Normal.Z);
                        w.Write(v.Texcoord.X);w.Write(v.Texcoord.Y);w.Write(v.PackedJoints);for(int i=0;i<4;i++)w.Write(v.Weights[i]);
                    }
                }));
            }
        }
        return result;
    }
    private sealed class OwnedResources:IDisposable
    {
        private readonly GameCubeDiscReader disc;private readonly List<RetroPakReader> paks=new();
        internal OwnedResources(string path)
        {
            disc=GameCubeDiscReader.Open(path);
            try
            {
                if(disc.GameId!="G2ME01"||disc.Revision!=0||disc.DiscNumber!=0)throw new InvalidDataException("Pinned Echoes edition required.");
                foreach(var source in ExternalSourceRequirements.For(disc.Game))paks.Add(new(disc.OpenRead(source.Path),leaveOpen:false));
            }
            catch{Dispose();throw;}
        }
        internal byte[] Read(uint id){foreach(var pak in paks)if(pak.Contains(id))return pak.ReadResource(id);throw new InvalidDataException("Original source resource absent.");}
        public void Dispose(){foreach(var pak in paks)pak.Dispose();disc.Dispose();}
    }
    private static Dictionary<string,object[]> Caches()
    {var result=new Dictionary<string,object[]>();foreach(string name in new[]{"_modelCache","_fhModelCache","_retiredRoomModels","_effects","_particleDefs"})result.Add(name,((IEnumerable)typeof(Read).GetField(name,BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!).Cast<object>().ToArray());return result;}
    private static float[] Values(Matrix4 m)=>new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
    private static string Digest(Action<BinaryWriter> append){using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))append(writer);return Sha(bytes.ToArray());}
    private static string HashFile(string path){using var input=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(input));}
    private static string Sha(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
}
