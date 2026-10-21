using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead;
using CharacterSourcePreviewLease = MphRead.Mods.Render.Characters.CharacterModelPack.SourcePreviewLease;
using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Numerics;
using MphRead.Mods.Render.Characters;
using MphRead.Formats;
using O=OpenTK.Mathematics.Matrix4;
using OV=OpenTK.Mathematics.Vector3;

namespace MphRead.Mods.Render.Characters;
internal static partial class CharacterModelAssetCheck
{
 internal static string? SourceRigFixtureOutput;
 internal static string SourceRigFixtureMaterialName="BodyMat";
 internal static void RunSourceRigChecks()
 {
 RunSourceRootChecks();
if(!CharacterSourceRootWitness.QualifiedForCurrentProcess)
 {
  Console.WriteLine("[charactermodelcheck] source-rig checks skipped: "+CharacterSourceRootWitness.UnsupportedPlatformMessage);
  return;
 }
int checks=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);++checks;}
void Reject(Action work,string label){try{work();}catch(Exception e)when(e is InvalidDataException or InvalidOperationException or JsonException or EndOfStreamException or ArgumentException or OverflowException){++checks;return;}throw new Exception("accepted "+label);}
O Open(Matrix4x4 x)=>new(x.M11,x.M12,x.M13,x.M14,x.M21,x.M22,x.M23,x.M24,x.M31,x.M32,x.M33,x.M34,x.M41,x.M42,x.M43,x.M44);
O FloatMatrix(float[] a,int joint){int n=joint*16;return new(a[n],a[n+1],a[n+2],a[n+3],a[n+4],a[n+5],a[n+6],a[n+7],a[n+8],a[n+9],a[n+10],a[n+11],a[n+12],a[n+13],a[n+14],a[n+15]);}
Vector3 Transform(O m,Vector3 p){var v=OV.TransformPosition(new OV(p.X,p.Y,p.Z),m);return new(v.X,v.Y,v.Z);}
var temporary=Path.Combine(Path.GetTempPath(),"prime-cpp08-synthetic-glb-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
try
{
 if(SourceRigFixtureMaterialName.Length is <1 or >128||SourceRigFixtureMaterialName.Any(char.IsControl))throw new ArgumentException("Invalid synthetic material identity.");
 var fixture=CharacterSourceRigCheckFixture.Make(39);fixture.Json["materials"]![0]!["name"]=SourceRigFixtureMaterialName;string path=Path.Combine(temporary,"source39.glb");fixture.Save(path);
 string Hash(string file)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
 int owners=0;
 CharacterSourceRigCheckVerified VerifyFile(string file,Action<JsonObject>? editManifest=null,Action<JsonObject>? editReceipt=null)
 {
  string root=Path.Combine(temporary,"verified-"+(++owners));Directory.CreateDirectory(root);File.Copy(file,Path.Combine(root,"body.glb"));
  string witness=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[]{1,2,3})).ToLowerInvariant();
  var receipt=new JsonObject{{"format",1},{"sourceCharacter","darksamus-echoes"},{"nativeHunter","Samus"},{"characterId","darksamus"},{"skinSource","MP2DarkSamus"},{"presentationPart","biped"},{"visualOnly",true},
   {"sourceGlbSha256",Hash(Path.Combine(root,"body.glb")).ToLowerInvariant()},{"sourceNativeKitSha256",witness},{"sourceMappingSha256",witness},{"sourceSkeletonSha256",witness},{"sourceWeightsSha256",witness}};
  foreach(string flag in new[]{"approvedForGameplay","multiplayerEnabled","playerSelectionEnabled","firstPersonVerified","alternateFormVerified","sourceMotionRetargetVerified","devicePerformanceVerified"})receipt[flag]=false;
  editReceipt?.Invoke(receipt);string receiptPath=Path.Combine(root,"receipt.json");File.WriteAllText(receiptPath,receipt.ToJsonString());
  var entry=new JsonObject{{"hunter","Samus"},{"part","biped"},{"lod",0},{"model","body.glb"},{"skinning","weighted4"},{"poseSource","sourceRigPreview"},{"characterId","darksamus"},{"skinSource","MP2DarkSamus"},{"provenance","receipt.json"},{"provenanceSha256",Hash(receiptPath).ToLowerInvariant()},{"boneMap",new JsonObject()}};
  var manifest=new JsonObject{{"format",1},{"id","synthetic-private-source"},{"models",new JsonArray(entry)}};editManifest?.Invoke(manifest);File.WriteAllText(Path.Combine(root,"characters.json"),manifest.ToJsonString());
  var pack=CharacterModelPack.Load(root);if(!pack.TryResolvePrivate(Hunter.Samus,new("darksamus","MP2DarkSamus"),CharacterModelPart.Biped,0,out var asset)||!pack.TryCreateSourcePreviewLease(asset,out var lease))throw new InvalidDataException("Actual owner refused source lease.");
  return new(root,pack,asset,lease);
 }
 CharacterWeightedModelData SourceFile(string file){var verified=VerifyFile(file);return CharacterWeightedModelLoader.LoadSourcePreview(verified.Asset,verified.Lease);}
 var verified=VerifyFile(path);
 if(SourceRigFixtureOutput is { } export)
 {
  string exportPath=Path.GetFullPath(export);if(!exportPath.StartsWith("/tmp/",StringComparison.Ordinal)&&!exportPath.StartsWith("/private/tmp/",StringComparison.Ordinal))throw new InvalidDataException("Synthetic draft fixture output must stay under /tmp.");
  if(Directory.Exists(exportPath)&&Directory.EnumerateFileSystemEntries(exportPath).Any())throw new InvalidDataException("Synthetic fixture output must be empty.");
  Directory.CreateDirectory(exportPath);foreach(string source in Directory.EnumerateFiles(verified.Root))File.Copy(source,Path.Combine(exportPath,Path.GetFileName(source)),false);
  var exported=CharacterModelPack.Load(exportPath);Check(exported.TryResolvePrivate(Hunter.Samus,new("darksamus","MP2DarkSamus"),CharacterModelPart.Biped,0,out var exportedAsset)&&exported.TryCreateSourcePreviewLease(exportedAsset,out _),"exported synthetic real pack revalidates at its actual root");
 }
 var asset=verified.Asset;var lease=verified.Lease;var packIdentity=verified.Pack;
 Reject(()=>CharacterWeightedModelLoader.Load(asset),"legacy path must reject oversized39 source skin");
 var model=CharacterWeightedModelLoader.LoadSourcePreview(asset,lease);
 Check(model.SourceRig!=null&&model.SourceRig.Nodes.Count==41&&model.Joints.Count==39,"actual GLB bytes retain39 source joints plus unweighted helper and mesh nodes");
 Check(model.Primitives.Count==2&&model.Primitives.All(p=>p.JointPalette is {Length:>0 and <=32}),"actual candidate loader partitions39 joints into bounded local draws");
 Check(model.IndexCount==fixture.Indices.Length&&model.Primitives.All(p=>p.Indices.All(i=>i<p.Vertices.Length)),"all triangle indices retained and remapped into exact local buffers");
 Check(model.SourceRig!.ContentSha256==Hash(path),"candidate cache content identity binds exact actual GLB");
 Check(ReferenceEquals(lease.CacheIdentity,packIdentity),"actual verified pack issues capability/cache identity");
 Check(!verified.Pack.TryCreateSourcePreviewLease(asset with{},out _),"equal copied asset cannot forge exact owned source capability");
 Check(!CharacterModelPack.Empty.TryCreateSourcePreviewLease(asset,out _),"another pack cannot claim source asset ownership");
 var second=VerifyFile(path);Check(second.Lease.CacheKey!=lease.CacheKey,"separate actual private pack owner cannot alias source cache");
 foreach(var change in new Action<JsonObject>[] {
  j=>j["models"]![0]!["lod"]=1,j=>j["models"]![0]!["part"]="viewModel",j=>j["models"]![0]!["characterId"]="unknown",
  j=>j["models"]![0]!["boneMap"]=new JsonObject{{"bone_2","native"}},j=>j["models"]![0]!["model"]="../escape.glb",
  j=>j["models"]![0]!["skinSource"]="MP3DarkSamus",j=>j["models"]![0]!["provenanceSha256"]=new string('0',64)})
  Reject(()=>VerifyFile(path,change),"real source pack part/LOD/catalog/path/receipt boundary");
 Reject(()=>VerifyFile(path,null,j=>j["approvedForGameplay"]=true),"actual provenance cannot invent gameplay acceptance");
 Reject(()=>VerifyFile(path,null,j=>j["sourceGlbSha256"]=new string('a',64)),"actual provenance detects GLB hash mismatch");
 foreach(var change in new Action<JsonObject>[] {
  j=>j["models"]![0]!["sourceAnimationArchive"]="darksamus-echoes.animations.bin",
  j=>j["models"]![0]!["sourceAnimationArchiveSha256"]=CharacterSourceAnimationArchive.QualifiedArchiveSha256,
  j=>{j["models"]![0]!["sourceAnimationArchive"]="other.animations.bin";j["models"]![0]!["sourceAnimationArchiveSha256"]=new string('a',64);},
  j=>{j["models"]![0]!["sourceAnimationArchive"]="darksamus-echoes.animations.bin";j["models"]![0]!["sourceAnimationArchiveSha256"]=CharacterSourceAnimationArchive.QualifiedArchiveSha256;j["models"]![0]!["poseSource"]="native";},
  j=>{j["models"]![0]!["sourceAnimationArchive"]="darksamus-echoes.animations.bin";j["models"]![0]!["sourceAnimationArchiveSha256"]=CharacterSourceAnimationArchive.QualifiedArchiveSha256;j["models"]![0]!["sourceGraphProof"]="legacy.json";}})
  Reject(()=>VerifyFile(path,change),"full animation archive pair/profile/exclusive source-rig boundary");
 Check((lease.CacheKey with{SourceAnimationArchiveSha256=new string('a',64)})!=lease.CacheKey,"archive content identity participates in source cache key");
 foreach(float phase in new[]{0f,.125f,.5f,1f,2f,5f})
 {
  var locals=fixture.Locals.Select((m,i)=>phase==0||i==0?m:Matrix4x4.CreateRotationZ(MathF.Sin(i*.17f+phase)*.1f)*m).ToArray();
  var skin=model.SourceRig.Evaluate(locals.Select(Open).ToArray(),model.Joints);
  var expected=fixture.Evaluate(locals);int sourceCorner=0;
  foreach(var batch in model.Primitives)
  {
   var packedPalette=CharacterSourceRig.GatherSegmentPalette(batch,skin);
   Check(packedPalette.Length==batch.JointPalette!.Length*16&&packedPalette.Length<=512,"segment submission uses exact bounded row-major float palette");
   Check(!ReferenceEquals(packedPalette,CharacterSourceRig.GatherSegmentPalette(batch,skin)),"segment/pose buffers do not alias another submission");
   var localPalette=CharacterSourceRig.Gather(batch,skin);Check(localPalette.Length<=32,"each actual draw has<=32 matrices");
   foreach(uint index in batch.Indices)
   {
    var vertex=batch.Vertices[index];int originalVertex=(int)fixture.Indices[sourceCorner++];var ji=CharacterWeightedModelLoader.UnpackJoints(vertex.PackedJoints);
    int[] joints={ji.J0,ji.J1,ji.J2,ji.J3};float[] weights={vertex.Weights.X,vertex.Weights.Y,vertex.Weights.Z,vertex.Weights.W};
    Vector3 actual=Vector3.Zero;for(int i=0;i<4;i++)actual+=weights[i]*Transform(FloatMatrix(packedPalette,joints[i]),vertex.Position);
    Vector3 oracle=.25f*Vector3.Transform(fixture.Positions[originalVertex],expected[fixture.Joints[originalVertex][0]])+.75f*Vector3.Transform(fixture.Positions[originalVertex],expected[fixture.Joints[originalVertex][1]]);
    Check(Vector3.Distance(actual,oracle)<.00003f,"actual decoded/remapped vertex equals independent full-hierarchy CPU skin across poses");
    Check(vertex.Position==fixture.Positions[originalVertex]&&vertex.Texcoord==fixture.Uvs[originalVertex],"actual source corner/UV seam and triangle winding retained");
    Check(vertex.Weights==new Vector4(.25f,.75f,0,0),"positive source weights preserved without pruning or normalization");
   }
  }
  Check(sourceCorner==fixture.Indices.Length,"every original triangle corner covered exactly once");
 }
 var full=model.SourceRig.Evaluate(null,model.Joints);
 Check(full.All(m=>MathF.Abs(m.M41)<.00001f&&MathF.Abs(m.M42)<.00001f&&MathF.Abs(m.M43)<.00001f),"rest global pose times nonidentity inverse bind yields identity skin matrices");
 var movedMesh=fixture.Locals.Select(Open).ToArray();movedMesh[0]=O.CreateTranslation(.1f,0,0);Reject(()=>model.SourceRig.Evaluate(movedMesh,model.Joints),"animated mesh ancestry cannot silently ignore source mesh-space transform");
 Reject(()=>model.SourceRig.Evaluate(new O[1],model.Joints),"truncated pose cannot drop source hierarchy");
 var invalidPose=fixture.Locals.Select(Open).ToArray();invalidPose[1]=O.CreateScale(2);Reject(()=>model.SourceRig.Evaluate(invalidPose,model.Joints),"invalid diagnostic source pose transform");
 var mutated=model.Primitives[0] with{JointPalette=new[]{10000}};Reject(()=>CharacterSourceRig.Gather(mutated,full),"corrupted gather map");
 // Native32 retains exact existing global packed indices and byte values.
 // The isolated check also differentially compares the pre-promotion loader.
 var native=CharacterSourceRigCheckFixture.Make(32);native.Save(Path.Combine(temporary,"native32.glb"));
 var nativeAsset=asset with{ModelPath=Path.Combine(temporary,"native32.glb"),BoneMap=native.Names.ToDictionary(n=>n,n=>"native_"+n),PoseSource=CharacterPoseSource.Native};
 var retained=CharacterWeightedModelLoader.Load(nativeAsset);
 Check(retained.Joints.Count==32&&retained.SourceRig==null&&retained.Primitives.Count==1&&retained.Primitives[0].JointPalette==null,"native32 retains one global palette and native pose contract");
 var nativePrimitive=retained.Primitives[0];Check(nativePrimitive.Indices.SequenceEqual(native.Indices),"native32 triangle indices unchanged");
 for(int v=0;v<nativePrimitive.Vertices.Length;v++)
 {
  var vertex=nativePrimitive.Vertices[v];int expectedPacked=native.Joints[v][0]|(native.Joints[v][1]<<5);
  Check(vertex.Position==native.Positions[v]&&vertex.Texcoord==native.Uvs[v]&&vertex.Weights==new Vector4(.25f,.75f,0,0)&&vertex.PackedJoints==expectedPacked,"native32 exact source geometry/weights/packing retained");
  Check(float.IsFinite(vertex.Normal.LengthSquared())&&MathF.Abs(vertex.Normal.LengthSquared()-1)<.0001f,"native32 generated normals remain finite unit vectors");
 }
 void Malformed(Action<JsonObject> edit,string label)
 {var bad=fixture.Copy();edit(bad.Json);string name="bad-"+checks+".glb";bad.Save(Path.Combine(temporary,name));Reject(()=>SourceFile(Path.Combine(temporary,name)),label);}
 Malformed(j=>j["nodes"]![1]!["children"]=new JsonArray(1),"source hierarchy self-cycle");
 Malformed(j=>j["nodes"]![0]!["children"]=new JsonArray(2),"multiple-parent source graph");
 Malformed(j=>j["nodes"]![1]!["scale"]=new JsonArray(2,1,1),"source scale acceptance closed");
 Malformed(j=>j["nodes"]![1]!["rotation"]=new JsonArray(0,0,0,0),"invalid source quaternion");
 Malformed(j=>j["nodes"]![1]!["translation"]=new JsonArray(100001,0,0),"oversized source transform");
 Malformed(j=>j["nodes"]![1]!["children"]=new JsonArray(5000),"source child out of bounds");
 Malformed(j=>j["nodes"]![2]!["name"]=j["nodes"]![3]!["name"]!.GetValue<string>(),"duplicate node identity");
 Malformed(j=>j["skins"]![0]!["joints"]![0]=j["skins"]![0]!["joints"]![1]!.GetValue<int>(),"duplicate source joint binding");
 Malformed(j=>j["accessors"]![1]!["count"]=1000000,"actual accessor budget");
 Malformed(j=>j["accessors"]![1]!["sparse"]=new JsonObject(),"actual sparse accessor rejection");
 Malformed(j=>j["bufferViews"]![0]!["byteLength"]=10000000,"actual bufferView bounds");
 Malformed(j=>j["meshes"]![0]!["primitives"]![0]!["attributes"]!["WEIGHTS_1"]=2,"extra influences cannot be silently ignored");
 Malformed(j=>{var nodes=j["nodes"]!.AsArray();while(nodes.Count<=1024)nodes.Add(new JsonObject{{"name","oversized_"+nodes.Count}});},"full hierarchy node budget");
 var nan=fixture.Copy();BinaryPrimitives.WriteInt32LittleEndian(nan.Binary.AsSpan(nan.InverseOffset),BitConverter.SingleToInt32Bits(float.NaN));nan.Save(Path.Combine(temporary,"nan.glb"));Reject(()=>SourceFile(Path.Combine(temporary,"nan.glb")),"actual inversebind nonfinite bytes");
 var invalidWeight=fixture.Copy();BinaryPrimitives.WriteInt32LittleEndian(invalidWeight.Binary.AsSpan(invalidWeight.WeightOffset),BitConverter.SingleToInt32Bits(.1f));invalidWeight.Save(Path.Combine(temporary,"weight.glb"));Reject(()=>SourceFile(Path.Combine(temporary,"weight.glb")),"source weights cannot silently normalize");
 Console.WriteLine($"[charactermodelcheck] source-rig synthetic pack/loader/palette PASS {checks}; no GL/media/clip acceptance");
}
finally{Directory.Delete(temporary,true);}
 }
}

sealed class CharacterSourceRigCheckFixture
{
 internal JsonObject Json=null!;internal byte[] Binary=null!;internal Vector3[] Positions=null!;internal Vector2[] Uvs=null!;internal uint[] Indices=null!;internal int[][] Joints=null!;internal Matrix4x4[] Locals=null!;internal int[] Parents=null!,JointNodes=null!;internal string[] Names=null!;internal int InverseOffset,WeightOffset;
 internal CharacterSourceRigCheckFixture Copy()=>new(){Json=Json.DeepClone().AsObject(),Binary=Binary.ToArray(),Positions=Positions,Uvs=Uvs,Indices=Indices,Joints=Joints,Locals=Locals,Parents=Parents,JointNodes=JointNodes,Names=Names,InverseOffset=InverseOffset,WeightOffset=WeightOffset};
 internal Matrix4x4[] Evaluate(Matrix4x4[] locals)
 {
  var world=new Matrix4x4[locals.Length];var done=new bool[locals.Length];Matrix4x4 At(int n){if(done[n])return world[n];world[n]=Parents[n]<0?locals[n]:locals[n]*At(Parents[n]);done[n]=true;return world[n];}
  for(int i=0;i<locals.Length;i++)At(i);var rest=new Matrix4x4[locals.Length];var restDone=new bool[locals.Length];Matrix4x4 Rest(int n){if(restDone[n])return rest[n];rest[n]=Parents[n]<0?Locals[n]:Locals[n]*Rest(Parents[n]);restDone[n]=true;return rest[n];}
  return JointNodes.Select(n=>{Matrix4x4.Invert(Rest(n),out var inverse);return inverse*world[n];}).ToArray();
 }
 internal static CharacterSourceRigCheckFixture Make(int count)
 {
  var f=new CharacterSourceRigCheckFixture();f.Locals=Enumerable.Repeat(Matrix4x4.Identity,count+2).ToArray();f.Parents=Enumerable.Repeat(-1,count+2).ToArray();f.Locals[1]=Matrix4x4.CreateTranslation(.3f,.7f,0);
  var nodes=new JsonArray(new JsonObject{{"name","mesh"},{"mesh",0},{"skin",0}},new JsonObject{{"name","source_helper"},{"translation",new JsonArray(.3,.7,0)},{"children",new JsonArray(count+1)}});
  for(int node=2;node<count+2;node++)
  {int logical=count+1-node;f.Parents[node]=logical==0?1:node+1;f.Locals[node]=Matrix4x4.CreateTranslation(.01f*(logical+1),.02f,0);var entry=new JsonObject{{"name","bone_"+node},{"translation",new JsonArray(.01*(logical+1),.02,0)}};if(logical<count-1)entry["children"]=new JsonArray(node-1);nodes.Add(entry);}
  f.JointNodes=Enumerable.Range(2,count).ToArray();f.Names=f.JointNodes.Select(n=>"bone_"+n).ToArray();f.Positions=Enumerable.Range(0,count+1).Select(i=>new Vector3((i%count)*.1f,(i%3)*.2f,(i%7)*.1f)).ToArray();f.Positions[^1]=f.Positions[0];f.Uvs=Enumerable.Range(0,count+1).Select(i=>new Vector2(i*.01f,i*.02f)).ToArray();f.Joints=Enumerable.Range(0,count+1).Select(i=>new[]{i%count,(i%count+1)%count,0,0}).ToArray();
  var indices=new List<uint>();for(int i=0;i+2<count;i+=3)indices.AddRange(new uint[]{(uint)i,(uint)i+1,(uint)i+2});indices.AddRange(new uint[]{(uint)count,(uint)count-2,(uint)count-1});f.Indices=indices.ToArray();
  using var bytes=new MemoryStream();using var writer=new BinaryWriter(bytes);var views=new JsonArray();var accessors=new JsonArray();
  int Access(string type,int component,int elements,Action write){while(bytes.Position%4!=0)writer.Write((byte)0);int offset=(int)bytes.Position;write();int length=(int)bytes.Position-offset;views.Add(new JsonObject{{"buffer",0},{"byteOffset",offset},{"byteLength",length}});accessors.Add(new JsonObject{{"bufferView",views.Count-1},{"componentType",component},{"count",elements},{"type",type}});return accessors.Count-1;}
  int position=Access("VEC3",5126,f.Positions.Length,()=>{foreach(var v in f.Positions){writer.Write(v.X);writer.Write(v.Y);writer.Write(v.Z);}});
  int joints=Access("VEC4",5123,f.Joints.Length,()=>{foreach(var j in f.Joints)foreach(int index in j)writer.Write((ushort)index);});
  int weights=Access("VEC4",5126,f.Joints.Length,()=>{f.WeightOffset=(int)bytes.Position;foreach(var j in f.Joints){writer.Write(.25f);writer.Write(.75f);writer.Write(0f);writer.Write(0f);}});
  int uv=Access("VEC2",5126,f.Uvs.Length,()=>{foreach(var t in f.Uvs){writer.Write(t.X);writer.Write(t.Y);}});
  int triangles=Access("SCALAR",5125,f.Indices.Length,()=>{foreach(uint index in f.Indices)writer.Write(index);});
  Matrix4x4 Rest(int node)=>f.Parents[node]<0?f.Locals[node]:f.Locals[node]*Rest(f.Parents[node]);
  int inverse=Access("MAT4",5126,count,()=>{f.InverseOffset=(int)bytes.Position;foreach(int node in f.JointNodes){Matrix4x4.Invert(Rest(node),out var m);foreach(float value in new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44})writer.Write(value);}});
  f.Binary=bytes.ToArray();f.Json=new JsonObject{{"asset",new JsonObject{{"version","2.0"}}},{"buffers",new JsonArray(new JsonObject{{"byteLength",f.Binary.Length}})},{"bufferViews",views},{"accessors",accessors},{"nodes",nodes},{"skins",new JsonArray(new JsonObject{{"joints",new JsonArray(f.JointNodes.Select(n=>JsonValue.Create(n)).ToArray())},{"inverseBindMatrices",inverse}})},{"materials",new JsonArray(new JsonObject{{"name","BodyMat"}})},{"meshes",new JsonArray(new JsonObject{{"primitives",new JsonArray(new JsonObject{{"material",0},{"attributes",new JsonObject{{"POSITION",position},{"JOINTS_0",joints},{"WEIGHTS_0",weights},{"TEXCOORD_0",uv}}},{"indices",triangles}})}})}};return f;
 }
 internal void Save(string path)
 {byte[] json=System.Text.Encoding.UTF8.GetBytes(Json.ToJsonString());int jsonLength=(json.Length+3)&~3,binLength=(Binary.Length+3)&~3;using var file=File.Create(path);using var w=new BinaryWriter(file);w.Write(0x46546C67u);w.Write(2u);w.Write((uint)(12+8+jsonLength+8+binLength));w.Write((uint)jsonLength);w.Write(0x4E4F534Au);w.Write(json);for(int i=json.Length;i<jsonLength;i++)w.Write((byte)32);w.Write((uint)binLength);w.Write(0x004E4942u);w.Write(Binary);for(int i=Binary.Length;i<binLength;i++)w.Write((byte)0);}
}

sealed record CharacterSourceRigCheckVerified(string Root,CharacterModelPack Pack,CharacterModelAsset Asset,CharacterSourcePreviewLease Lease);
