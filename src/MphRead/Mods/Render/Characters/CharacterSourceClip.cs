using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Render.Characters;
using MphRead.Mods.Import.Retro;
using M = OpenTK.Mathematics.Matrix4;
using Q = OpenTK.Mathematics.Quaternion;

// PRIVATE existing binding input adapter. Immutable captured full keys, no clock,
// state mapping, root-motion application, retarget, draw ownership or IO on Sample.
namespace MphRead.Mods.Render.Characters;

internal sealed class CharacterSourceClip
{
 private readonly CharacterModelPack.SourcePreviewLease _lease;
 private readonly CharacterModelAsset _asset;
 private readonly CharacterSourceRig _rig;
 private readonly Anim _anim;
 private readonly (uint Id,int Node,double[] Bind)[] _bones;
 internal readonly int Ordinal;
 internal readonly string Name;
 internal readonly uint ResourceId;
 internal readonly string ContentSha256;
 internal double Duration=>_anim.Duration;
 internal static int CapturedFileReads {get;private set;}
 private static readonly M Basis=new(-1,0,0,0,0,0,1,0,0,1,0,0,0,0,0,1);
 private CharacterSourceClip(CharacterModelPack.SourcePreviewLease lease,CharacterModelAsset asset,CharacterSourceRig rig,Anim anim,(uint,int,double[])[] bones,int ordinal,string name,uint id,string sha)
  =>(_lease,_asset,_rig,_anim,_bones,Ordinal,Name,ResourceId,ContentSha256)=(lease,asset,rig,anim,bones,ordinal,name,id,sha);
 private static byte[] Read(string path,int max){CapturedFileReads++;if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Clip proof leaf must be a regular contained file");return CharacterSourceProofIO.ReadFile(path,max);}
 private static string Sha(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
 private static void Require(bool c,string why){if(!c)throw new InvalidDataException(why);}
 private static string Flat(string root,string name){Require(name.Length is >0 and <128&&name==Path.GetFileName(name)&&!name.Contains('\\')&&!name.Contains(':'),"Contained flat clip witness name");return Path.Combine(root,name);}
 internal static CharacterSourceClip Load(CharacterModelPack.SourcePreviewLease lease,CharacterModelAsset asset,CharacterSourceRig rig,int ordinal)
 {
  if(asset.Part==CharacterModelPart.ViewModel)Require(lease.SourceCannon!=null&&ordinal==32,"Only the verified fixed cannon authoring primitive is allowed");
  lease.Require(asset);Require(String.Equals(rig.ContentSha256,lease.ModelSha256,StringComparison.OrdinalIgnoreCase),"Exact current loaded source rig required");
  if(lease.SourceAnimations is { } imported)return LoadImported(lease,asset,rig,imported,ordinal);
  string root=Path.GetDirectoryName(asset.ModelPath)!;
  using var manifest=JsonDocument.Parse(Read(Path.Combine(root,"characters.json"),1024*1024));
  var matches=manifest.RootElement.GetProperty("models").EnumerateArray().Where(m=>m.GetProperty("model").GetString()==Path.GetFileName(asset.ModelPath)
   &&m.GetProperty("skinSource").GetString()==asset.PrivateAppearance!.Value.SkinSource
   &&m.GetProperty("characterId").GetString()==asset.PrivateAppearance.Value.CharacterId
   &&String.Equals(m.GetProperty("part").GetString(),asset.Part.ToString(),StringComparison.OrdinalIgnoreCase)
   &&m.GetProperty("lod").GetInt32()==asset.Lod&&String.Equals(m.GetProperty("hunter").GetString(),asset.Hunter.ToString(),StringComparison.OrdinalIgnoreCase)).ToArray();
  Require(matches.Length==1,"Exact source asset/part/LOD motion receipt owner");var entry=matches[0];
  byte[] receiptBytes=Read(Flat(root,entry.GetProperty("provenance").GetString()!),1024*1024);Require(Sha(receiptBytes)==entry.GetProperty("provenanceSha256").GetString(),"Exact owner provenance receipt");
  using var receipt=JsonDocument.Parse(receiptBytes);byte[] inventoryBytes=Read(Flat(root,lease.SourceGraphProof?.MotionManifestFile??"source-motion-manifest.json"),1024*1024);Require(Sha(inventoryBytes)==receipt.RootElement.GetProperty("sourceMotionManifestSha256").GetString(),"Motion inventory bound to same verified pack receipt");
  using var inventory=JsonDocument.Parse(inventoryBytes);var entries=inventory.RootElement.GetProperty("entries");Require(ordinal>=0&&ordinal<entries.GetArrayLength(),"Explicit clip ordinal bound");var selected=entries[ordinal];Require(selected.GetProperty("index").GetInt32()==ordinal,"Exact captured source ordinal");
  if(selected.GetProperty("Kind").GetString()!="play"||selected.GetProperty("status").GetString()!="complete-temporal-primitive"||selected.GetProperty("ids").GetArrayLength()!=1)throw new NotSupportedException("Composite source graph remains unsupported; no leaf substitution");
  uint id=selected.GetProperty("ids")[0].GetUInt32();lease.SourceGraphProof?.RequireStrictPlay(ordinal,id);var descriptor=selected.GetProperty("data");string filename=descriptor.GetProperty("clip").GetString()!;Require(filename=="clip-"+id.ToString("x8")+".json","Exact source resource filename");byte[] clipBytes=Read(Flat(root,filename),16*1024*1024);Require(Sha(clipBytes)==descriptor.GetProperty("clipSha256").GetString(),"Full captured clip bytes match inventory");
  using var clip=JsonDocument.Parse(clipBytes);var source=clip.RootElement;Require(source.GetProperty("id").GetUInt32()==id,"Resource ID retained");
  var anim=ReadAnim(source);Require(!source.GetProperty("scaleUnsupported").GetBoolean()&&anim.Scale.Values.All(v=>v.All(s=>s.All(x=>Math.Abs(x-1)<=0.00001))),"Nonrigid scale remains unsupported; never dropped silently");
  byte[] skeletonBytes=Read(Path.Combine(root,"source.skeleton.json"),1024*1024);Require(Sha(skeletonBytes)==receipt.RootElement.GetProperty("sourceSkeletonSha256").GetString()&&Sha(skeletonBytes)=="2c093b4f4d4a6e38c1e5168889534c07e9daec7563cdd974c623722cdb9d3c56","Exact original Echoes CINF/root-sentinel witness");
  using var skeleton=JsonDocument.Parse(skeletonBytes);var bones=skeleton.RootElement.GetProperty("Bones").EnumerateArray().ToArray();Require(bones.Length==39&&rig.Nodes.Count==40,"Whole39-joint hierarchy plus existing identity mesh");var byId=bones.ToDictionary(b=>b.GetProperty("Id").GetUInt32());var bindings=new List<(uint,int,double[])>();
  foreach(var bone in bones)
  {
   uint boneId=bone.GetProperty("Id").GetUInt32(),parent=bone.GetProperty("Parent").GetUInt32();string name=bone.GetProperty("Name").GetString()!;
   Require(boneId==0?parent==97&&name=="root":byId.ContainsKey(parent),"Exact root97 policy; all other parents present");double[] p=bone.GetProperty("Position").EnumerateArray().Select(v=>v.GetDouble()).ToArray();if(byId.TryGetValue(parent,out var parentBone)){var parentPos=parentBone.GetProperty("Position").EnumerateArray().Select(v=>v.GetDouble()).ToArray();for(int i=0;i<3;i++)p[i]-=parentPos[i];}
   int node=Enumerable.Range(0,rig.Nodes.Count).Single(i=>rig.Nodes[i].Name=="source_bone_"+boneId+"_"+name);M bind=M.CreateTranslation((float)-p[0],(float)p[2],(float)p[1]);Require(MatrixDistance(bind,rig.Nodes[node].Local)<0.00001f,"Loaded bind hierarchy matches captured CINF exactly");bindings.Add((boneId,node,p));
  }
  Require(anim.Rot.Keys.Concat(anim.Trans.Keys).Concat(anim.Scale.Keys).All(byId.ContainsKey),"No unknown channel IDs");lease.Require(asset);
  return new(lease,asset,rig,anim,bindings.ToArray(),ordinal,selected.GetProperty("Name").GetString()!,id,Sha(clipBytes));
 }
 internal static CharacterSourceClip LoadImported(CharacterModelPack.SourcePreviewLease lease,CharacterModelAsset asset,
  CharacterSourceRig rig,CharacterSourceAnimationArchive imported,int ordinal)
 {
  Require((asset.Part==CharacterModelPart.Biped||asset.Part==CharacterModelPart.ViewModel&&lease.SourceCannon?.IsGenerated==true&&ordinal==32)&&ReferenceEquals(lease.SourceAnimations,imported)
   &&String.Equals(rig.ContentSha256,lease.ModelSha256,StringComparison.OrdinalIgnoreCase),"Exact source-rig imported animation owner required");
  var clip=imported.Open(ordinal);
  if(asset.Part==CharacterModelPart.ViewModel)Require(clip.Source.Id==0xd12fdfa3&&clip.RigidScale,"Exact fixed original cannon primitive required");
  var anim=clip.CaptureAnimation();var source=imported.Skeleton.Bones;
  Require(source.Count==39&&rig.Nodes.Count==40,"Complete imported skeleton and source rig required");
  var bindings=new List<(uint,int,double[])>();
  foreach(var bone in source.Values.OrderBy(b=>b.Id))
  {
   Require(bone.Id==0?bone.Parent==97&&bone.Name=="root":source.ContainsKey(bone.Parent),"Exact source root/parent policy");
   double[] p={bone.X,bone.Y,bone.Z};
   if(source.TryGetValue(bone.Parent,out var parent)){p[0]-=parent.X;p[1]-=parent.Y;p[2]-=parent.Z;}
   int node=Enumerable.Range(0,rig.Nodes.Count).Single(i=>rig.Nodes[i].Name=="source_bone_"+bone.Id+"_"+bone.Name);
   M bind=M.CreateTranslation((float)-p[0],(float)p[2],(float)p[1]);
   Require(MatrixDistance(bind,rig.Nodes[node].Local)<.00001f,"Imported CINF must match the complete loaded source bind hierarchy");
   int expectedParent=bone.Id==0?-1:Enumerable.Range(0,rig.Nodes.Count).Single(i=>rig.Nodes[i].Name=="source_bone_"+parent!.Id+"_"+parent.Name);
   Require(rig.Nodes[node].Parent==expectedParent,"Imported CINF parent identity must match the loaded hierarchy");
   bindings.Add((bone.Id,node,p));
  }
  Require(anim.Rot.Keys.Concat(anim.Trans.Keys).Concat(anim.Scale.Keys).All(source.ContainsKey),"No foreign imported animation channel bones");
  lease.Require(asset);
  return new(lease,asset,rig,anim,bindings.ToArray(),ordinal,imported.Data.Roots[ordinal].Name,clip.Source.Id,clip.Source.Sha256);
 }
 internal static Anim ReadAnim(JsonElement value)
 {
  var anim=new Anim{Duration=value.GetProperty("Duration").GetDouble(),Tick=value.GetProperty("Tick").GetDouble(),Keys=value.GetProperty("Keys").GetInt32()};Require(double.IsFinite(anim.Duration)&&anim.Duration>0&&anim.Duration<=120&&double.IsFinite(anim.Tick)&&anim.Tick>0&&anim.Keys is >=2 and <=20000,"Bounded full temporal timing");
  long remaining=4_000_000;
  foreach(var (name,dictionary,width) in new[]{("rot",anim.Rot,4),("trans",anim.Trans,3),("scale",anim.Scale,3)})
  {
   var channels=value.GetProperty(name);int count=channels.EnumerateObject().Count();Require(count<=256&&((long)anim.Keys*count*width)<=remaining,"Channel count/scalar bound before allocation");remaining-=(long)anim.Keys*count*width;
   foreach(var channel in channels.EnumerateObject()){uint id=uint.Parse(channel.Name);Require(channel.Value.GetArrayLength()==anim.Keys,"All authored keys retained");var keys=channel.Value.EnumerateArray().Select(v=>v.EnumerateArray().Select(n=>n.GetDouble()).ToArray()).ToList();Require(keys.All(k=>k.Length==width&&k.All(n=>double.IsFinite(n)&&Math.Abs(n)<=100000)),"Full channel width/value bounds");dictionary.Add(id,keys);}
  }
  Require((long)anim.Keys*(anim.Rot.Count*4+anim.Trans.Count*3+anim.Scale.Count*3)<=4_000_000,"Clip scalar memory bound");return anim;
 }
 internal bool Matches(CharacterModelPack.SourcePreviewLease lease,CharacterModelAsset asset,CharacterSourceRig rig)
  =>ReferenceEquals(_lease,lease)&&ReferenceEquals(_asset,asset)&&ReferenceEquals(_rig,rig);
 internal ReadOnlyCollection<M> Sample(double seconds)
 {
  if(_lease.SourceCannon!=null&&seconds!=0)throw new NotSupportedException("Source cannon holds its reviewed authoring phase; no FP clock/state semantics");
  _lease.RequireCurrent(_asset);Require(String.Equals(_rig.ContentSha256,_lease.ModelSha256,StringComparison.OrdinalIgnoreCase),"Exact immutable rig identity");if(!double.IsFinite(seconds))throw new ArgumentOutOfRangeException(nameof(seconds));
  _anim.Sample(seconds,out var rotations,out var translations);M[] pose=Enumerable.Repeat(M.Identity,_rig.Nodes.Count).ToArray();
  foreach(var bone in _bones)
  {
   pose[bone.Node]=ComposeSourceLocal(bone.Bind,rotations.GetValueOrDefault(bone.Id),translations.GetValueOrDefault(bone.Id));
  }
  return Array.AsReadOnly(pose);
 }
 // Shared source-basis composition for deterministic importer authoring and
 // the runtime's captured full-key sampler. No second animation decoder.
 internal static M ComposeSourceLocal(double[] bind,double[]? quaternion,double[]? translation)
 {
  M rotation=quaternion==null?M.Identity:Basis*M.CreateFromQuaternion(new Q((float)quaternion[0],(float)quaternion[1],(float)quaternion[2],(float)quaternion[3]))*Basis;
  double[] p=translation??bind;M local=rotation*M.CreateTranslation((float)-p[0],(float)p[2],(float)p[1]);CharacterSourceRig.RequireRigid(local);return local;
 }
 internal static float MatrixDistance(M a,M b)
 {
  float[] av=[a.M11,a.M12,a.M13,a.M14,a.M21,a.M22,a.M23,a.M24,a.M31,a.M32,a.M33,a.M34,a.M41,a.M42,a.M43,a.M44],bv=[b.M11,b.M12,b.M13,b.M14,b.M21,b.M22,b.M23,b.M24,b.M31,b.M32,b.M33,b.M34,b.M41,b.M42,b.M43,b.M44];return av.Zip(bv,(x,y)=>MathF.Abs(x-y)).Max();
 }
}
