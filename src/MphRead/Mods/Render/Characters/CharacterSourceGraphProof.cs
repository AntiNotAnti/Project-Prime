using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead.Mods.Import;
namespace MphRead.Mods.Render.Characters;

internal sealed partial class CharacterModelPack
{
 private readonly Dictionary<CharacterModelAsset,CharacterSourceGraphProof> _sourceGraphs=new(ReferenceEqualityComparer.Instance);
 internal static byte[] ReadGraphInput(string root,string name,int limit)=>CharacterSourceProofIO.ReadFile(ContainedPath(root,name),limit);
 internal static string HashGraphInput(string root,string name,int limit)=>CharacterSourceProofIO.HashFile(ContainedPath(root,name),limit);
}

// Raw retained graph proof issued by the SAME pack/asset/generation owner.
// There is deliberately no graph evaluator or user-constructible acceptance flag.
internal sealed class CharacterSourceGraphProof
{
 internal const string QualifiedAncsSha256="7933e22a780b7f2cbee01be3ee5a4d48e6f08eb39df81608d638bee1729a5f7b";
 internal const string QualifiedParentReceiptSha256="18e2bac8e52825766cc2c22a6fbc22a678d1afe23193ea0f16eff99dfed88a4d";
 internal const string MotionFile="source-motion-manifest-v2.json",ReceiptFile="receipt-graph.json";
 internal string ProofSha256 {get;}
 internal string MotionManifestFile=>MotionFile;
 private readonly string _proofFile,_motionSha;
 private readonly RetroAnimationGraphProof.Document _metadata;
 private readonly ReadOnlyDictionary<string,(string Sha,int Limit)> _inputs;
 private CharacterSourceGraphProof(string proofFile,string proofSha,string motionSha,RetroAnimationGraphProof.Document metadata,Dictionary<string,(string,int)> inputs)
  =>(_proofFile,ProofSha256,_motionSha,_metadata,_inputs)=(proofFile,proofSha,motionSha,metadata,new(inputs));
 private static void Require(bool c,string reason){if(!c)throw new InvalidDataException(reason);}
 internal static void RequireQualified(RetroAnimationGraphProof.Document doc,string modelSha)
 {
  Require(modelSha==CharacterSourceCannonRecipe.QualifiedBodySha256,"Only the exact original graph-bound body is qualified");
  RequireQualifiedSource(doc);
 }
 internal static void RequireQualifiedSource(RetroAnimationGraphProof.Document doc)
 {
  Require(doc.Format==1&&doc.SourceGameId=="G2ME01"&&doc.SourceAncsId==0x39900a11
   &&doc.SourceAncsSha256==QualifiedAncsSha256&&doc.SourceAncsBytes==70744&&doc.SourceCharacterId=="darksamus-echoes"&&doc.SourceCharacterName=="Dark_Samus"
   &&doc.Roots.Length==76&&doc.Nodes.Length==87&&doc.Nodes.Count(n=>n.Type==0)==79&&doc.FullTransitions.Length==4&&doc.HalfTransitions.Length==6&&doc.TransitionNodes.Length==11
   &&doc.SourceSnapshotFile=="darksamus-echoes.ancs.bin"&&!doc.NamedGraphSemanticsAccepted,"Only exact retained Echoes source graph proof is qualified; no evaluator");
 }
 internal static CharacterSourceGraphProof Load(string root,CharacterModelManifestEntry entry,CharacterModelAsset asset,string modelSha)
 {
  try{return LoadCore(root,entry,asset,modelSha);}
  catch(Exception error)when(error is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or OverflowException or FormatException)
  {throw new InvalidDataException("Malformed graph/motion/provenance binding",error);}
 }
 private static CharacterSourceGraphProof LoadCore(string root,CharacterModelManifestEntry entry,CharacterModelAsset asset,string modelSha)
 {
  Require(asset.Part==CharacterModelPart.Biped&&asset.PoseSource==CharacterPoseSource.SourceRigPreview&&entry.SourceGraphProof=="darksamus-echoes.ancs-graph.json"
   &&entry.SourceMotionManifest==MotionFile&&entry.SourceGraphProofSha256 is {Length:64},"Explicit private Biped graph binding fields");
  byte[] descriptor=CharacterModelPack.ReadGraphInput(root,entry.SourceGraphProof!,RetroAnimationGraphProof.MaximumProofBytes);
  Require(RetroAnimationGraphProof.Sha(descriptor)==entry.SourceGraphProofSha256,"Exact exported raw graph descriptor hash");
  byte[] snapshot=CharacterModelPack.ReadGraphInput(root,"darksamus-echoes.ancs.bin",RetroAnimationIndex.MaxAncsBytes);
  var metadata=RetroAnimationGraphProof.Verify(descriptor,snapshot);RequireQualified(metadata,modelSha);
  byte[] receipt=CharacterModelPack.ReadGraphInput(root,entry.Provenance!,CharacterModelPack.MaximumManifestBytes);
  Require(RetroAnimationGraphProof.Sha(receipt)==entry.ProvenanceSha256,"Exact graph-bound provenance receipt");using var provenance=JsonDocument.Parse(receipt);
  var p=provenance.RootElement;
  Require(p.GetProperty("sourceGraphProofSha256").GetString()==entry.SourceGraphProofSha256&&p.GetProperty("sourceGraphProofFile").GetString()==entry.SourceGraphProof
   &&p.GetProperty("sourceAncsSnapshotFile").GetString()==metadata.SourceSnapshotFile&&p.GetProperty("sourceAncsSha256").GetString()==metadata.SourceAncsSha256
   &&p.GetProperty("sourceAncsResourceId").GetUInt32()==metadata.SourceAncsId&&p.GetProperty("sourceParentReceiptSha256").GetString()==QualifiedParentReceiptSha256
   &&p.GetProperty("sourceGraphMotionManifestFile").GetString()==MotionFile&&!p.GetProperty("sourceNamedGraphSemanticsAccepted").GetBoolean(),"Graph proof/provenance identity chain");
  byte[] parent=CharacterModelPack.ReadGraphInput(root,"receipt.json",CharacterModelPack.MaximumManifestBytes);Require(RetroAnimationGraphProof.Sha(parent)==QualifiedParentReceiptSha256,"Qualified original source receipt remains unchanged");
  using var parentReceipt=JsonDocument.Parse(parent);byte[] legacy=CharacterModelPack.ReadGraphInput(root,"source-motion-manifest.json",CharacterModelPack.MaximumManifestBytes);
  Require(RetroAnimationGraphProof.Sha(legacy)==parentReceipt.RootElement.GetProperty("sourceMotionManifestSha256").GetString(),"Original source temporal inventory remains bound");
  byte[] motion=CharacterModelPack.ReadGraphInput(root,MotionFile,CharacterModelPack.MaximumManifestBytes);string motionSha=RetroAnimationGraphProof.Sha(motion);
  Require(motionSha==p.GetProperty("sourceMotionManifestSha256").GetString(),"New motion manifest bound into graph provenance");
  var resources=ValidateMotion(motion,legacy,metadata,entry.SourceGraphProof!,entry.SourceGraphProofSha256!);
  var inputs=new Dictionary<string,(string,int)>(StringComparer.Ordinal)
  {
   [entry.SourceGraphProof!]=(entry.SourceGraphProofSha256!,RetroAnimationGraphProof.MaximumProofBytes),
   [metadata.SourceSnapshotFile]=(metadata.SourceAncsSha256,RetroAnimationIndex.MaxAncsBytes),
   [MotionFile]=(motionSha,CharacterModelPack.MaximumManifestBytes),["receipt.json"]=(QualifiedParentReceiptSha256,CharacterModelPack.MaximumManifestBytes),
   ["source-motion-manifest.json"]=(RetroAnimationGraphProof.Sha(legacy),CharacterModelPack.MaximumManifestBytes)
  };
  foreach(var resource in resources)inputs.Add(resource.File,(resource.Sha,16*1024*1024));
  var captured=new CharacterSourceGraphProof(entry.SourceGraphProof!,entry.SourceGraphProofSha256!,motionSha,metadata,inputs);captured.Revalidate(root,entry,modelSha);return captured;
 }
 internal void Revalidate(string root,CharacterModelManifestEntry entry,string modelSha)
 {
  Require(entry.Part==CharacterModelPart.Biped&&modelSha==CharacterSourceCannonRecipe.QualifiedBodySha256&&entry.SourceGraphProof==_proofFile
   &&entry.SourceGraphProofSha256==ProofSha256&&entry.SourceMotionManifest==MotionFile,"Exact graph/part/asset proof identity");
  foreach(var input in _inputs)Require(CharacterModelPack.HashGraphInput(root,input.Key,input.Value.Limit)==input.Value.Sha,"Bound graph/motion/source input changed: "+input.Key);
 }
 internal void RequireStrictPlay(int ordinal,uint resource)
 {
  if((uint)ordinal>=_metadata.Roots.Length)throw new InvalidDataException("Graph root ordinal outside captured proof");
  var root=_metadata.Roots[ordinal];var node=_metadata.Nodes[root.Node];
  if(root.Ordinal!=ordinal||node.Type!=0||node.Kind!="play"||node.Children.Length!=0||node.AnimId!=resource
   ||node.PlayTimeBits!=0||node.PlayTimeTypeRaw!=2)throw new NotSupportedException("Only exact qualified raw-play roots are supported; named graph/unknown time remains unsupported");
 }
 internal sealed record Resource(uint Id,string File,string Sha);
 private static Resource[] ParentResources(JsonElement entries)
 {
  Require(entries.ValueKind==JsonValueKind.Array&&entries.GetArrayLength()<=4096,"Bounded parent motion entries");var found=new Dictionary<uint,Resource>();
  foreach(var entry in entries.EnumerateArray())if(entry.GetProperty("Kind").GetString()=="play")
  {
   Require(entry.GetProperty("ids").GetArrayLength()==1&&entry.GetProperty("status").GetString()=="complete-temporal-primitive","Existing full temporal source clips required");
   uint id=entry.GetProperty("ids")[0].GetUInt32();var data=entry.GetProperty("data");string file=data.GetProperty("clip").GetString()!,sha=data.GetProperty("clipSha256").GetString()!;
   Require(file=="clip-"+id.ToString("x8")+".json"&&sha.Length==64&&sha.All(Uri.IsHexDigit),"Exact bounded original clip resource identity");var r=new Resource(id,file,sha);
   if(found.TryGetValue(id,out var previous))Require(previous==r,"Resource aliases must bind identical full clip bytes");else found.Add(id,r);
  }
  return found.Values.OrderBy(r=>r.Id).ToArray();
 }
 private static uint[] Leaves(RetroAnimationGraphProof.Document metadata,int root)
 {
  var leaves=new List<uint>();int visits=0;void Walk(int index,int depth){Require(depth<=64&&(uint)index<metadata.Nodes.Length&&++visits<=4096,"Bounded raw graph traversal");var n=metadata.Nodes[index];if(n.Type==0)leaves.Add(n.AnimId);else foreach(int child in n.Children)Walk(child,depth+1);}Walk(root,0);return leaves.Distinct().ToArray();
 }
 private static void CheckEntries(JsonElement entries,RetroAnimationGraphProof.Document metadata)
 {
  Require(entries.GetArrayLength()==metadata.Roots.Length,"All original named roots/ordinals retained");
  for(int i=0;i<metadata.Roots.Length;i++){var e=entries[i];var r=metadata.Roots[i];Require(r.Ordinal==i&&e.GetProperty("index").GetInt32()==i&&e.GetProperty("Name").GetString()==r.Name
   &&e.GetProperty("Kind").GetString()==metadata.Nodes[r.Node].Kind&&e.GetProperty("ids").EnumerateArray().Select(v=>v.GetUInt32()).SequenceEqual(Leaves(metadata,r.Node)),"Exact raw root/name/resource order; no flattening into play");}
 }
 internal static Resource[] ValidateMotion(byte[] motion,byte[] legacy,RetroAnimationGraphProof.Document metadata,string proofFile,string proofSha)
 {
  using var original=JsonDocument.Parse(legacy);using var current=JsonDocument.Parse(motion);var o=original.RootElement;var m=current.RootElement;
  CheckEntries(o.GetProperty("entries"),metadata);CheckEntries(m.GetProperty("entries"),metadata);var expected=ParentResources(o.GetProperty("entries"));var actual=ParentResources(m.GetProperty("entries"));
  Require(expected.SequenceEqual(actual)&&expected.Length==66,"All79raw leaves bind same66original full-key resources");
  Require(m.GetProperty("format").GetInt32()==2&&!m.GetProperty("namedGraphSemanticsAccepted").GetBoolean()
   &&m.GetProperty("parentSourceMotionManifestFile").GetString()=="source-motion-manifest.json"&&m.GetProperty("parentSourceMotionManifestSha256").GetString()==RetroAnimationGraphProof.Sha(legacy),"Explicit graph inventory successor preserves original projection");
  var graph=m.GetProperty("sourceGraphProof");Require(graph.GetProperty("file").GetString()==proofFile&&graph.GetProperty("sha256").GetString()==proofSha
   &&graph.GetProperty("sourceSnapshotFile").GetString()==metadata.SourceSnapshotFile&&graph.GetProperty("sourceAncsSha256").GetString()==metadata.SourceAncsSha256
   &&graph.GetProperty("sourceAncsId").GetUInt32()==metadata.SourceAncsId,"Inventory to raw graph/source identity");
  var table=m.GetProperty("graphResourceClips");Require(table.GetArrayLength()==expected.Length,"Complete graph resource table");
  for(int i=0;i<expected.Length;i++){var r=table[i];Require(r.GetProperty("resourceId").GetUInt32()==expected[i].Id&&r.GetProperty("clip").GetString()==expected[i].File&&r.GetProperty("clipSha256").GetString()==expected[i].Sha,"Every graph leaf maps to original full temporal source bytes");}
  return expected;
 }
 internal static byte[] BuildMotion(byte[] legacy,RetroAnimationGraphProof.Document metadata,string proofFile,string proofSha)
 {
  using(var old=JsonDocument.Parse(legacy))CheckEntries(old.RootElement.GetProperty("entries"),metadata);
  var node=JsonNode.Parse(legacy)!.AsObject();node["format"]=2;node["parentSourceMotionManifestFile"]="source-motion-manifest.json";node["parentSourceMotionManifestSha256"]=RetroAnimationGraphProof.Sha(legacy);
  if(node.TryGetPropertyValue("sourceMetaGraphProofSha256",out var prior)){node["legacyMetaGraphProofSha256"]=prior?.DeepClone();node.Remove("sourceMetaGraphProofSha256");}
  node["namedGraphSemanticsAccepted"]=false;
  node["sourceGraphProof"]=JsonSerializer.SerializeToNode(new{file=proofFile,sha256=proofSha,sourceSnapshotFile=metadata.SourceSnapshotFile,sourceAncsId=metadata.SourceAncsId,sourceAncsSha256=metadata.SourceAncsSha256});
  using(var document=JsonDocument.Parse(legacy))node["graphResourceClips"]=JsonSerializer.SerializeToNode(ParentResources(document.RootElement.GetProperty("entries")).Select(r=>new{resourceId=r.Id,clip=r.File,clipSha256=r.Sha}));
  foreach(var entry in node["entries"]!.AsArray())if(entry!["Kind"]!.GetValue<string>()!="play")entry["status"]="source-graph-unsupported";
  byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(node);Require(bytes.Length<=CharacterModelPack.MaximumManifestBytes,"Bounded graph motion metadata");ValidateMotion(bytes,legacy,metadata,proofFile,proofSha);return bytes;
 }
}

internal static partial class CharacterModelAuthoring
{
 internal sealed record GraphBindingMetadata(string ProofFile,string SnapshotFile,string MotionFile,string ReceiptFile,byte[] Proof,byte[] Snapshot,byte[] Motion,byte[] Receipt);
 // Existing authoring owner returns private staging bytes; no install/CLI/session.
 internal static GraphBindingMetadata BuildSourceGraphBinding(CharacterModelPack pack,CharacterModelAsset sourceBody,byte[] exportedProof,byte[] exportedSnapshot)
 {
  if(sourceBody.Part!=CharacterModelPart.Biped||!pack.TryCreateSourcePreviewLease(sourceBody,out var lease)||lease.SourceCannon!=null||lease.SourceGraphProof!=null)
   throw new InvalidDataException("Exact original legacy body lease required for graph successor authoring");
  if(exportedProof==null||exportedProof.Length is <1 or >RetroAnimationGraphProof.MaximumProofBytes||exportedSnapshot==null||exportedSnapshot.Length is <8 or >RetroAnimationIndex.MaxAncsBytes)
   throw new InvalidDataException("Bounded exported graph/source snapshot required");
  byte[] capturedProof=exportedProof.ToArray(),capturedSnapshot=exportedSnapshot.ToArray();
  lease.Require(sourceBody);var graph=RetroAnimationGraphProof.Verify(capturedProof,capturedSnapshot);CharacterSourceGraphProof.RequireQualified(graph,lease.ModelSha256);
  string root=Path.GetDirectoryName(sourceBody.ModelPath)!;byte[] originalReceipt=CharacterModelPack.ReadGraphInput(root,"receipt.json",CharacterModelPack.MaximumManifestBytes);
  if(RetroAnimationGraphProof.Sha(originalReceipt)!=CharacterSourceGraphProof.QualifiedParentReceiptSha256)throw new InvalidDataException("Original qualified source provenance required");
  var receipt=JsonNode.Parse(originalReceipt)!.AsObject();byte[] legacy=CharacterModelPack.ReadGraphInput(root,"source-motion-manifest.json",CharacterModelPack.MaximumManifestBytes);
  if(RetroAnimationGraphProof.Sha(legacy)!=receipt["sourceMotionManifestSha256"]!.GetValue<string>())throw new InvalidDataException("Original source inventory changed");
  string proofFile=graph.SourceCharacterId+".ancs-graph.json",proofSha=RetroAnimationGraphProof.Sha(capturedProof);byte[] motion=CharacterSourceGraphProof.BuildMotion(legacy,graph,proofFile,proofSha);
  receipt["sourceParentReceiptSha256"]=CharacterSourceGraphProof.QualifiedParentReceiptSha256;receipt["sourceMotionManifestSha256"]=RetroAnimationGraphProof.Sha(motion);
  receipt["sourceGraphProofFile"]=proofFile;receipt["sourceGraphProofSha256"]=proofSha;receipt["sourceAncsSnapshotFile"]=graph.SourceSnapshotFile;
  receipt["sourceAncsSha256"]=graph.SourceAncsSha256;receipt["sourceAncsResourceId"]=graph.SourceAncsId;receipt["sourceGraphMotionManifestFile"]=CharacterSourceGraphProof.MotionFile;
  receipt["sourceNamedGraphSemanticsAccepted"]=false;byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(receipt);
  if(bytes.Length>CharacterModelPack.MaximumManifestBytes)throw new InvalidDataException("Bounded graph provenance output");
  lease.Require(sourceBody);return new(proofFile,graph.SourceSnapshotFile,CharacterSourceGraphProof.MotionFile,CharacterSourceGraphProof.ReceiptFile,capturedProof,capturedSnapshot,motion,bytes);
 }
}
