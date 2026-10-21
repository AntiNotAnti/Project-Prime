using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using OpenTK.Mathematics;
namespace MphRead.Mods.Render.Characters;

internal sealed partial class CharacterModelPack
{
 private readonly Dictionary<CharacterModelAsset,CharacterSourceCannonRecipe> _sourceCannons=new(ReferenceEqualityComparer.Instance);
 internal bool HasSourceCannon(CharacterModelAsset asset)=>asset.Part==CharacterModelPart.ViewModel&&_sourceCannons.ContainsKey(asset)&&_sourceProofs.ContainsKey(asset);
 internal static byte[] ReadCannonInput(string root,string name,int maximum)=>CharacterSourceProofIO.ReadFile(ContainedPath(root,name),maximum);
}

// Fixed private source cannon recipes. The same pack/lease owns source proof;
// this is not a user constructible proof token or generic first-person capability.
internal sealed partial class CharacterSourceCannonRecipe
{
 internal const string QualifiedReceiptSha256="6db484fe85113f767acb633490d87b7a439d5585f82d35166418d0e5247851cf";
 internal const string QualifiedBodySha256="702a73d06d71d285d3e0a08de5fc175d07ac6b446cd0ee8cf13aaf814f4ba7c6";
 internal string ReceiptSha256 {get;}
 internal Matrix4 ToCannon {get;}
 internal bool IsGenerated {get;}
 internal CharacterGeneratedTrooperProfile? GeneratedTrooper {get;}
 internal int ClipOrdinal=>GeneratedTrooper==null?32:1;
 internal uint ClipResourceId=>GeneratedTrooper==null?0xd12fdfa3u:0x5528972bu;
 internal int TriangleCount=>GeneratedTrooper==null?392:206;
 private readonly ReadOnlyDictionary<string,string> _inputs;
 private readonly ReadOnlyCollection<ReadOnlyCollection<int>> _triangles;
 private CharacterSourceCannonRecipe(string sha,Matrix4 basis,Dictionary<string,string> inputs,int[][] triangles,bool generated=false,CharacterGeneratedTrooperProfile? trooper=null)
  =>(ReceiptSha256,ToCannon,_inputs,_triangles,IsGenerated,GeneratedTrooper)=(sha,basis,new(inputs),Array.AsReadOnly(triangles.Select(a=>Array.AsReadOnly(a.ToArray())).ToArray()),generated,trooper);
 private static void Require(bool c,string why){if(!c)throw new InvalidDataException(why);}
 private static string Sha(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
 private static int Budget(string name)=>name switch{"body.glb"=>128*1024*1024,"mesh.json"=>32*1024*1024,"clip-d12fdfa3.json"=>16*1024*1024,_=>1024*1024};
 internal static CharacterSourceCannonRecipe Load(string root,CharacterModelManifestEntry entry,string modelSha)
 {
  if(CharacterGeneratedTrooperProfile.FromSkin(entry.SkinSource) is { } trooper && entry.SourceCannonReceipt==trooper.Id+".cannon-source.json")
   return LoadGeneratedTrooper(root,entry,modelSha,trooper);
  if(entry.SourceCannonReceipt==GeneratedReceiptFile)return LoadGenerated(root,entry,modelSha);
  Require(entry.Part==CharacterModelPart.ViewModel&&entry.Lod==0&&entry.PoseSource==CharacterPoseSource.SourceRigPreview
   &&entry.SourceCannonReceipt=="source-cannon-receipt.json"&&entry.SourceCannonSha256==QualifiedReceiptSha256
   &&modelSha==QualifiedBodySha256,"Only the explicit fixed source-cannon ViewModel authoring receipt is qualified");
  var bytes=CharacterModelPack.ReadCannonInput(root,entry.SourceCannonReceipt!,1024*1024);Require(Sha(bytes)==entry.SourceCannonSha256,"Cannon receipt changed");
  using var document=JsonDocument.Parse(bytes);var r=document.RootElement;
  Require(r.GetProperty("format").GetInt32()==1&&r.GetProperty("mode").GetString()=="sourceCannonAuthoring"
   &&r.GetProperty("presentationPart").GetString()=="viewModel"&&r.GetProperty("sourceCharacter").GetString()=="darksamus-echoes"
   &&r.GetProperty("clipOrdinal").GetInt32()==32&&r.GetProperty("clipResourceId").GetUInt32()==3509575587
   &&r.GetProperty("clipSeconds").GetDouble()==0&&r.GetProperty("sourceElbowNode").GetInt32()==24&&r.GetProperty("sourceParticleLocatorNode").GetInt32()==32
   &&r.GetProperty("sourceUnitsUnscaled").GetBoolean()&&r.GetProperty("calibrationOnly").GetBoolean()
   &&!r.GetProperty("gameplayMuzzleChanged").GetBoolean()&&!r.GetProperty("publicSelectionEnabled").GetBoolean()
   &&!r.GetProperty("networkReplayEnabled").GetBoolean()&&!r.GetProperty("firstPersonVerified").GetBoolean(),"Cannon recipe is authoring only");
  var inputs=r.GetProperty("sourceInputs").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()!,StringComparer.Ordinal);
  string[] names={"body.glb","receipt.json","source-mapping.json","mesh.json","source.skeleton.json","source.weights.json","source-motion-manifest.json","clip-d12fdfa3.json"};
  Require(inputs.Keys.OrderBy(n=>n).SequenceEqual(names.OrderBy(n=>n))&&inputs["body.glb"]==modelSha&&inputs["receipt.json"]==entry.ProvenanceSha256&&entry.Provenance=="receipt.json","Exact original source proof inputs");
  var m=r.GetProperty("toCannon").EnumerateArray().Select(n=>n.GetSingle()).ToArray();Require(m.Length==16,"Full fixed source authoring basis");
  var basis=new Matrix4(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);CharacterSourceRig.RequireRigid(basis);
  var arrays=r.GetProperty("triangles");Require(arrays.GetArrayLength()==7,"Seven original source material groups");
  var triangles=arrays.EnumerateArray().Select(a=>{Require(a.GetArrayLength()<=6340,"Bounded cut triangle metadata");return a.EnumerateArray().Select(n=>n.GetInt32()).ToArray();}).ToArray();
  Require(triangles.Sum(a=>a.Length)==392&&triangles[2].Length==66&&triangles[5].Length==326,"Fixed whole-triangle authoring selection");
  var result=new CharacterSourceCannonRecipe(entry.SourceCannonSha256!,basis,inputs,triangles);result.Revalidate(root,entry,modelSha);return result;
 }
 internal void Revalidate(string root,CharacterModelManifestEntry entry,string modelSha)
 {
  if(GeneratedTrooper is { } trooper){RevalidateGeneratedTrooper(root,entry,modelSha,trooper);return;}
  if(IsGenerated){RevalidateGenerated(root,entry,modelSha);return;}
  Require(entry.Part==CharacterModelPart.ViewModel&&entry.SourceCannonSha256==ReceiptSha256&&modelSha==QualifiedBodySha256
   &&entry.SourceCannonReceipt=="source-cannon-receipt.json"&&Sha(CharacterModelPack.ReadCannonInput(root,entry.SourceCannonReceipt,1024*1024))==ReceiptSha256,"Exact cannon receipt/part/hash owner");
  foreach(var input in _inputs)
  {
   string actual=input.Key=="body.glb"?modelSha:Sha(CharacterModelPack.ReadCannonInput(root,input.Key,Budget(input.Key)));
   Require(actual==input.Value,"Original cannon source input changed: "+input.Key);
  }
 }
 internal CharacterWeightedModelData Derive(CharacterModelPack.SourcePreviewLease lease,CharacterModelAsset asset,CharacterWeightedModelData source)
 {
  if(GeneratedTrooper!=null)return DeriveGeneratedTrooper(lease,asset,source);
  if(IsGenerated)return DeriveGenerated(lease,asset,source);
  lease.Require(asset);Require(ReferenceEquals(lease.SourceCannon,this)&&asset.Part==CharacterModelPart.ViewModel&&source.SourceCannon==null,"Exact issued cannon lease/part");
  var rig=source.SourceRig;Require(rig!=null&&source.Joints.Count==39&&rig.Nodes.Count==40&&source.Primitives.Count==7
   &&source.Primitives.Select(p=>p.MaterialName).SequenceEqual(Enumerable.Range(0,7).Select(i=>"retro_material_"+i)),"Complete original source rig/material identity");
  Require(rig!.Nodes[24].Name=="source_bone_24_R_elbow"&&rig.Nodes[32].Name=="source_bone_32_GUN_Particle_LCTR"&&Under(rig,32,24),"Exact named source elbow/locator ancestry");
  var computed=Compute(lease,asset,source);
  Require(CharacterSourceClip.MatrixDistance(computed.Basis,ToCannon)<.000001f,"Fixed source basis must reproduce from complete raw source pose");
  var filtered=new List<CharacterWeightedPrimitive>();
  for(int material=0;material<source.Primitives.Count;material++)
  {
   var p=source.Primitives[material];var selected=computed.Selected[material];
   Require(selected.SequenceEqual(_triangles[material]),"Source-derived cut differs from frozen recipe");
   if(selected.Count!=0)filtered.Add(p with{Indices=selected.SelectMany(t=>new[]{p.Indices[t*3],p.Indices[t*3+1],p.Indices[t*3+2]}).ToArray(),SourcePrimitiveOrdinal=material});
  }
  lease.Require(asset);return source with{Primitives=filtered.AsReadOnly(),SourceCannon=this,SourceAuthoringPose=Array.AsReadOnly(computed.Pose)};
 }
 internal static byte[] Author(CharacterModelPack.SourcePreviewLease lease,CharacterModelAsset asset,CharacterWeightedModelData source)
 {
  lease.Require(asset);Require(asset.Part==CharacterModelPart.Biped&&lease.SourceCannon==null&&lease.ModelSha256==QualifiedBodySha256,"Author from exact verified original biped only");
  var computed=Compute(lease,asset,source);string root=Path.GetDirectoryName(asset.ModelPath)!;
  string[] names={"body.glb","receipt.json","source-mapping.json","mesh.json","source.skeleton.json","source.weights.json","source-motion-manifest.json","clip-d12fdfa3.json"};
  var inputs=names.ToDictionary(n=>n,n=>Sha(CharacterModelPack.ReadCannonInput(root,n,Budget(n))),StringComparer.Ordinal);
  using var receipt=JsonDocument.Parse(CharacterModelPack.ReadCannonInput(root,"receipt.json",1024*1024));
  foreach(var pair in new[]{("source-mapping.json","sourceMappingSha256"),("source.skeleton.json","sourceSkeletonSha256"),("source.weights.json","sourceWeightsSha256"),("source-motion-manifest.json","sourceMotionManifestSha256")})
   Require(inputs[pair.Item1]==receipt.RootElement.GetProperty(pair.Item2).GetString(),"Authoring inputs must match verified source receipt");
  using var mapping=JsonDocument.Parse(CharacterModelPack.ReadCannonInput(root,"source-mapping.json",1024*1024));Require(inputs["mesh.json"]==mapping.RootElement.GetProperty("sourceMeshSha256").GetString()&&inputs["clip-d12fdfa3.json"]==computed.ClipSha,"Original mesh/CSKR/raw temporal authoring source");
  var m=computed.Basis;float[] matrix={m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
  var bytes=JsonSerializer.SerializeToUtf8Bytes(new{format=1,mode="sourceCannonAuthoring",presentationPart="viewModel",sourceCharacter="darksamus-echoes",sourceInputs=inputs,sourceElbowNode=24,sourceParticleLocatorNode=32,clipOrdinal=32,clipResourceId=3509575587u,clipSeconds=0,toCannon=matrix,triangles=computed.Selected,sourceUnitsUnscaled=true,calibrationOnly=true,gameplayMuzzleChanged=false,publicSelectionEnabled=false,networkReplayEnabled=false,firstPersonVerified=false});
  Require(bytes.Length<=65535,"Bounded authoring metadata");lease.Require(asset);return bytes.Append((byte)'\n').ToArray();
 }
 private static (Matrix4 Basis,Matrix4[] Pose,List<int>[] Selected,string ClipSha) Compute(CharacterModelPack.SourcePreviewLease lease,CharacterModelAsset asset,CharacterWeightedModelData source)
 {
  var rig=source.SourceRig??throw new InvalidDataException("Complete original source authoring rig required");Require(source.Joints.Count==39&&rig.Nodes.Count==40&&source.Primitives.Count==7,"Complete original source authoring rig");
  var clip=CharacterSourceClip.Load(lease,asset,rig,32);var pose=clip.Sample(0);var skin=rig.Evaluate(pose,source.Joints);
  Vector3 World(int node){int joint=rig.JointNodes.IndexOf(node);Require(joint>=0,"Source locator joint retained");return(source.Joints[joint].InverseBind.Inverted()*skin[joint]).Row3.Xyz;}
  var elbow=World(24);var tip=World(32);var forward=tip-elbow;Require(forward.LengthSquared>1e-8f,"Nonzero source barrel axis");forward.Normalize();
  var up=Vector3.UnitY-Vector3.Dot(Vector3.UnitY,forward)*forward;Require(up.LengthSquared>1e-8f,"Source authoring frame has unambiguous up");up.Normalize();var left=Vector3.Cross(up,forward);
  var basis=Matrix4.CreateTranslation(-tip)*new Matrix4(left.X,up.X,forward.X,0,left.Y,up.Y,forward.Y,0,left.Z,up.Z,forward.Z,0,0,0,0,1);

  var selected=new List<int>[7];
  for(int material=0;material<7;material++)
  {
   var p=source.Primitives[material];Require(p.MaterialName=="retro_material_"+material&&p.Indices.Length%3==0&&p.Indices.All(i=>i<p.Vertices.Length),"Original bounded whole source triangles/material");selected[material]=new();
   for(int i=0;i<p.Indices.Length;i+=3)if(On(p.Vertices[p.Indices[i]],p,rig!)&&On(p.Vertices[p.Indices[i+1]],p,rig!)&&On(p.Vertices[p.Indices[i+2]],p,rig!))selected[material].Add(i/3);
  }
  return(basis,pose.ToArray(),selected,clip.ContentSha256);
 }
 private static bool Under(CharacterSourceRig rig,int node,int root)
 {for(int n=node,steps=0;n>=0;n=rig.Nodes[n].Parent){Require((uint)n<rig.Nodes.Count&&++steps<=rig.Nodes.Count,"Bounded source ancestry");if(n==root)return true;}return false;}
 private static bool On(CharacterWeightedVertex vertex,CharacterWeightedPrimitive primitive,CharacterSourceRig rig)
 {
  Require(primitive.JointPalette is {Length:>0 and <=32}&&vertex.PackedJoints>=0&&(vertex.PackedJoints>>20)==0,"Bounded source joint slots");bool result=false;
  for(int i=0;i<4;i++)
  {float weight=vertex.Weights[i];Require(float.IsFinite(weight)&&weight>=0,"Original positive finite influences");if(weight==0)continue;int slot=(vertex.PackedJoints>>(i*5))&31;Require(slot<primitive.JointPalette!.Length,"Local source slot in range");int global=primitive.JointPalette[slot];Require((uint)global<rig.JointNodes.Count,"Global source joint in range");result|=Under(rig,rig.JointNodes[global],24);}
  return result;
 }
}

// Existing authoring owner: internal metadata output only, no CLI/UI install.
internal static partial class CharacterModelAuthoring
{
 internal static byte[] BuildSourceCannonReceipt(CharacterModelPack pack,CharacterModelAsset sourceBody)
 {
  if(sourceBody.Part!=CharacterModelPart.Biped||!pack.TryCreateSourcePreviewLease(sourceBody,out var lease))
   throw new InvalidDataException("Exact verified original source body owner required");
  var geometry=CharacterWeightedModelLoader.LoadSourcePreview(sourceBody,lease);
  return CharacterSourceCannonRecipe.Author(lease,sourceBody,geometry);
 }
}
