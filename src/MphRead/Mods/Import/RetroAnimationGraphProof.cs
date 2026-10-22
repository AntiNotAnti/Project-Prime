using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Mods.Import.Retro;
namespace MphRead.Mods.Import;

// Raw-source retention only. No graph execution, clock or gameplay meaning.
public static class RetroAnimationGraphProof
{
 public const int MaximumProofBytes=4*1024*1024,MaximumNodes=4096,MaximumTransitions=4096;
 public sealed record Node(int Index,uint Type,string Kind,int Offset,int Length,uint AnimId,uint PrimId,string PrimName,
  int PlayParametersOffset,uint? PlayTimeBits,uint? PlayTimeTypeRaw,int BlendParametersOffset,uint? BlendFactorBits,byte? BlendFlagRaw,int[] Children,uint[] Weights);
 public sealed record TransitionNode(int Index,uint Type,int Offset,int Length,int ParametersOffset,int? AnimationNode,
  uint? DurationBits,uint? DurationTypeRaw,byte? UnknownRaw,byte? RunARaw,uint? FlagsRaw);
 public sealed record Root(int Ordinal,string Name,int Node);
 public sealed record FullTransition(uint Id,uint AnimA,uint AnimB,int Transition,int Offset,int Length);
 public sealed record HalfTransition(uint Anim,int Transition,int Offset,int Length);
 public sealed record ResourceReference(uint Anim,uint Evnt);
 public sealed record Document(int Format,string SourceGameId,uint SourceAncsId,string SourceCharacterId,string SourceCharacterName,
  string SourceSnapshotFile,int SourceAncsBytes,string SourceAncsSha256,uint Cmdl,uint Cskr,uint Cinf,
  int AnimSetVersion,int AnimSetOffset,int AnimSetLength,Root[] Roots,Node[] Nodes,TransitionNode[] TransitionNodes,
  FullTransition[] FullTransitions,int DefaultTransition,HalfTransition[] HalfTransitions,ResourceReference[] Resources,
  bool UninterpretedSetBytesPreservedInSnapshot,bool NamedGraphSemanticsAccepted,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourcePart=null);
 public sealed record Capture(byte[] SourceSnapshot,byte[] Descriptor,Document Metadata);
 public enum Availability { Present, NoAnimationSet }
 public sealed record CaptureResult(Availability Status,Capture? Graph);
 private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,MaxDepth=16};
 private static void Require(bool c,string why){if(!c)throw new InvalidDataException(why);}
 public static string Sha(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
 public static Capture FromSource(byte[] source,string gameId,uint resource,string characterId,string characterName,RetroCharacterRefs refs,string? sourcePart=null)
  =>CaptureOptional(source,gameId,resource,characterId,characterName,refs,sourcePart).Graph??throw new InvalidDataException("ANCS graph set absent; graph proof requires a Set");
 public static CaptureResult CaptureOptional(byte[] source,string gameId,uint resource,string characterId,string characterName,RetroCharacterRefs refs,string? sourcePart=null)
 {
  Require(sourcePart is null or "body" or "ball" or "drone","Explicit supported source artifact part");
  sourcePart=sourcePart is "ball" or "drone"?sourcePart:null;
  ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(gameId);ArgumentNullException.ThrowIfNull(characterId);ArgumentNullException.ThrowIfNull(characterName);Require(source.Length is >=8 and <=RetroAnimationIndex.MaxAncsBytes,"Bounded ANCS source snapshot");
  Require(gameId.Length is >0 and <=16&&characterId.Length is >0 and <=64&&characterId.All(c=>char.IsAsciiLetterOrDigit(c)||c=='-')
   &&characterName.Length is >0 and <=128,"Explicit bounded source identity");
  byte[] snapshot=source.ToArray();Require(RetroCharacterSource.FindCharacter(snapshot,characterName)==refs,"Same source character body references");
  var set=Ancs.Parse(snapshot).Set;
  if(set==null)return new(Availability.NoAnimationSet,null); // Valid parsed legacy sparse source; no parse exception is caught.
  Require(set.Anims.Count<=MaximumNodes&&set.Transitions.Count+set.HalfTransitions.Count+1<=MaximumTransitions,"Bounded named graph/transition tables");
  void Range(int at,int length){Require(at>=set.SourceOffset&&length>=0&&at<=set.SourceOffset+set.SourceLength&&length<=set.SourceOffset+set.SourceLength-at,"Raw graph source span");}
  Require(set.SourceOffset>=0&&set.SourceLength>=0&&set.SourceOffset<=snapshot.Length&&set.SourceLength<=snapshot.Length-set.SourceOffset,"Bounded complete source set");
  uint U32(int at){Range(at,4);return BinaryPrimitives.ReadUInt32BigEndian(snapshot.AsSpan(at,4));}
  var nodes=new List<Node?>();var transitions=new List<TransitionNode>();int names=0;
  int Meta(Ancs.MetaAnim m,int depth=0)
  {
   Require(depth<=64&&nodes.Count<MaximumNodes,"Raw graph depth/node budget");Range(m.SourceOffset,m.SourceLength);Require(U32(m.SourceOffset)==m.SourceType,"Raw graph type bits");
   Require(m.PrimName.Length<=1024&&(names+=m.PrimName.Length)<=128*1024,"Graph metadata string budget");int index=nodes.Count;nodes.Add(null);
   int[] children=m.Children.Select(c=>Meta(c,depth+1)).ToArray();
   if(m.SourceType==0)
   {
    Require(m.Kind=="play"&&children.Length==0&&m.Weights.Count==0&&m.PlayTimeBits.HasValue&&m.PlayTimeTypeRaw.HasValue
     &&U32(m.SourceOffset+4)==m.AnimId&&U32(m.SourceOffset+8)==m.PrimId&&m.PlayParametersOffset>=m.SourceOffset+13
     &&U32(m.PlayParametersOffset)==m.PlayTimeBits!.Value&&U32(m.PlayParametersOffset+4)==m.PlayTimeTypeRaw!.Value
     &&m.SourceLength==m.PlayParametersOffset+8-m.SourceOffset,"Exact raw play fields");
    int count=m.PlayParametersOffset-(m.SourceOffset+12)-1;Range(m.SourceOffset+12,count+1);
    Require(snapshot[m.PlayParametersOffset-1]==0&&Encoding.Latin1.GetString(snapshot,m.SourceOffset+12,count)==m.PrimName,"Exact raw primitive name");
   }
   else if(m.SourceType is 1 or 2)
   {
    Require(children.Length==2&&m.Kind==(m.SourceType==1?"blend":"phaseblend")&&m.BlendFactorBits.HasValue&&m.BlendFlagRaw.HasValue,"Exact raw blend fields");
    Range(m.BlendParametersOffset,5);Require(U32(m.BlendParametersOffset)==m.BlendFactorBits!.Value&&snapshot[m.BlendParametersOffset+4]==m.BlendFlagRaw!.Value
     &&m.Children[0].SourceOffset==m.SourceOffset+4&&m.Children[1].SourceOffset==m.Children[0].SourceOffset+m.Children[0].SourceLength
     &&m.BlendParametersOffset==m.Children[1].SourceOffset+m.Children[1].SourceLength&&m.SourceOffset+m.SourceLength==m.BlendParametersOffset+5,"Exact raw blend source ranges");
   }
   else
   {
    Require(m.SourceType is 3 or 4&&m.Kind==(m.SourceType==3?"random":"seq")&&U32(m.SourceOffset+4)==children.Length
     &&m.Weights.Count==(m.SourceType==3?children.Length:0),"Exact raw sequence/random metadata");int cursor=m.SourceOffset+8;
    for(int i=0;i<m.Children.Count;i++){Require(m.Children[i].SourceOffset==cursor,"Ordered graph child source span");cursor=checked(cursor+m.Children[i].SourceLength);if(m.SourceType==3){Require(U32(cursor)==m.Weights[i],"Raw random weight");cursor+=4;}}
    Require(cursor==m.SourceOffset+m.SourceLength,"Complete raw graph source extent");
   }
   nodes[index]=new(index,m.SourceType,m.Kind,m.SourceOffset,m.SourceLength,m.AnimId,m.PrimId,m.PrimName,m.PlayParametersOffset,m.PlayTimeBits,m.PlayTimeTypeRaw,
    m.BlendParametersOffset,m.BlendFactorBits,m.BlendFlagRaw,children,m.Weights.ToArray());return index;
  }
  int Trans(Ancs.MetaTrans t)
  {
   Require(transitions.Count<MaximumTransitions,"Raw transition metadata budget");Range(t.SourceOffset,t.SourceLength);Require(U32(t.SourceOffset)==t.SourceType,"Raw transition type bits");
   int? animation=null;
   if(t.SourceType==0){Require(t.Animation!=null&&t.Animation.SourceOffset==t.SourceOffset+4,"Raw transition graph source span");animation=Meta(t.Animation!);Require(t.SourceLength==4+t.Animation!.SourceLength,"Complete transition graph extent");}
   else if(t.SourceType is 1 or 2)
   {
    Range(t.ParametersOffset,14);Require(t.ParametersOffset==t.SourceOffset+4&&t.SourceLength==18&&U32(t.ParametersOffset)==t.DurationBits&&U32(t.ParametersOffset+4)==t.DurationTypeRaw
     &&snapshot[t.ParametersOffset+8]==t.UnknownRaw&&snapshot[t.ParametersOffset+9]==t.RunARaw&&U32(t.ParametersOffset+10)==t.FlagsRaw,"All14raw transition bytes retained");
   }
   else Require(t.SourceType==3&&t.SourceLength==4,"Raw Snap transition extent");
   int index=transitions.Count;transitions.Add(new(index,t.SourceType,t.SourceOffset,t.SourceLength,t.ParametersOffset,animation,t.DurationBits,t.DurationTypeRaw,t.UnknownRaw,t.RunARaw,t.FlagsRaw));return index;
  }
  var roots=set.Anims.Select((a,i)=>{Require(a.Name.Length is >0 and <=1024,"Bounded original graph root name");return new Root(i,a.Name,Meta(a.Anim));}).ToArray();
  var full=set.Transitions.Select(t=>{Range(t.SourceOffset,t.SourceLength);Require(U32(t.SourceOffset)==t.Id&&U32(t.SourceOffset+4)==t.AnimA&&U32(t.SourceOffset+8)==t.AnimB&&t.Meta.SourceOffset==t.SourceOffset+12&&t.SourceLength==12+t.Meta.SourceLength,"Exact full transition lookup source fields");return new FullTransition(t.Id,t.AnimA,t.AnimB,Trans(t.Meta),t.SourceOffset,t.SourceLength);}).ToArray();
  Require(set.DefaultTransition!=null,"Explicit source default transition");int defaultTransition=Trans(set.DefaultTransition!);
  var half=set.HalfTransitions.Select(t=>{Range(t.SourceOffset,t.SourceLength);Require(U32(t.SourceOffset)==t.Anim&&t.Meta.SourceOffset==t.SourceOffset+4&&t.SourceLength==4+t.Meta.SourceLength,"Exact half transition lookup source fields");return new HalfTransition(t.Anim,Trans(t.Meta),t.SourceOffset,t.SourceLength);}).ToArray();
  string artifactStem=characterId+(sourcePart==null?"":"."+sourcePart);
  var doc=new Document(1,gameId,resource,characterId,characterName,artifactStem+".ancs.bin",snapshot.Length,Sha(snapshot),refs.Cmdl,refs.Cskr,refs.Cinf,
   set.Version,set.SourceOffset,set.SourceLength,roots,nodes.Select(n=>n??throw new InvalidDataException("Incomplete graph node")).ToArray(),transitions.ToArray(),full,defaultTransition,half,
   set.Resources.Select(r=>new ResourceReference(r.Anim,r.Evnt)).ToArray(),true,false,sourcePart);
  return new(Availability.Present,new(snapshot,Serialize(doc),doc));
 }
 public static Document Verify(byte[] descriptor,byte[] source)
 {
  try{return VerifyCore(descriptor,source);}
  catch(Exception error)when(error is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or OverflowException or FormatException)
  {throw new InvalidDataException("Malformed raw graph proof schema or source range",error);}
 }
 private static Document VerifyCore(byte[] descriptor,byte[] source)
 {
  ArgumentNullException.ThrowIfNull(descriptor);ArgumentNullException.ThrowIfNull(source);
  Require(descriptor.Length is >0 and <=MaximumProofBytes,"Bounded graph descriptor");using var json=JsonDocument.Parse(descriptor,new JsonDocumentOptions{MaxDepth=16});var r=json.RootElement;
  var expected=FromSource(source,r.GetProperty("sourceGameId").GetString()!,r.GetProperty("sourceAncsId").GetUInt32(),r.GetProperty("sourceCharacterId").GetString()!,r.GetProperty("sourceCharacterName").GetString()!,
   new RetroCharacterRefs(r.GetProperty("cmdl").GetUInt32(),r.GetProperty("cskr").GetUInt32(),r.GetProperty("cinf").GetUInt32()),
   r.TryGetProperty("sourcePart",out var part)?part.GetString():null);
  Require(descriptor.SequenceEqual(expected.Descriptor),"Graph descriptor must reproduce exactly from the bound raw source snapshot");return expected.Metadata;
 }
 private static byte[] Serialize(Document value)
 {using var output=new BoundedStream();JsonSerializer.Serialize(output,value,Json);return output.ToArray();}
 private sealed class BoundedStream:MemoryStream
 {
  private void Reserve(int count){if(count<0||Length>MaximumProofBytes-count)throw new InvalidDataException("Graph proof output exceeds4MiB");}
  public override void Write(byte[] bytes,int offset,int count){Reserve(count);base.Write(bytes,offset,count);}
  public override void Write(ReadOnlySpan<byte> bytes){Reserve(bytes.Length);base.Write(bytes);}
  public override void WriteByte(byte value){Reserve(1);base.WriteByte(value);}
 }
}
