using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CharacterSourcePreviewLease = MphRead.Mods.Render.Characters.CharacterModelPack.SourcePreviewLease;
using System.Numerics;
using System.Text.Json;
using OpenTK.Mathematics;
using M = OpenTK.Mathematics.Matrix4;
using Q = OpenTK.Mathematics.Quaternion;
using V = OpenTK.Mathematics.Vector3;
using NVector4 = System.Numerics.Vector4;
namespace MphRead.Mods.Render.Characters;

internal readonly record struct CharacterSourceCacheKey(object Pack,string Path,string Sha256,PrivateCharacterAppearance Appearance,long Generation,CharacterModelPart Part=CharacterModelPart.Biped,string? PresentationRecipeSha256=null,string? SourceGraphProofSha256=null,string? SourceAnimationArchiveSha256=null);
internal sealed record CharacterSourceNode(string Name,int Parent,M Local);
internal sealed class CharacterSourceRig
{
 internal const int MaximumNodes=1024;
 internal readonly System.Collections.ObjectModel.ReadOnlyCollection<CharacterSourceNode> Nodes;
 internal readonly System.Collections.ObjectModel.ReadOnlyCollection<int> JointNodes;
 internal readonly System.Collections.ObjectModel.ReadOnlyCollection<int> MeshAncestors;
 internal string ContentSha256 {get;private set;}="";
 internal void BindContentSha256(string sha){if(ContentSha256.Length!=0||sha.Length!=64||!sha.All(Uri.IsHexDigit))throw new InvalidDataException("Source content identity is immutable.");ContentSha256=sha;}
 private CharacterSourceRig(CharacterSourceNode[] nodes,int[] joints,int[] meshAncestors)=>(Nodes,JointNodes,MeshAncestors)=(Array.AsReadOnly(nodes.ToArray()),Array.AsReadOnly(joints.ToArray()),Array.AsReadOnly(meshAncestors.ToArray()));
 internal static CharacterSourceRig Read(JsonElement[] nodes,JsonElement[] jointElements,List<JsonElement> meshes)
 {
  if(nodes.Length==0||nodes.Length>MaximumNodes)throw new InvalidDataException("Source hierarchy exceeds node budget.");
  int[] parents=Enumerable.Repeat(-1,nodes.Length).ToArray();var names=new HashSet<string>(StringComparer.Ordinal);
  var locals=new M[nodes.Length];
  for(int i=0;i<nodes.Length;i++)
  {
   var node=nodes[i];string name=node.TryGetProperty("name",out var p)?p.GetString()??"":"";
   if(name.Length is <1 or >128||name.Any(char.IsControl)||!names.Add(name))throw new InvalidDataException("Source node names must be stable and unique.");
   locals[i]=ReadLocal(node);
   if(node.TryGetProperty("children",out var children))
   {
    if(children.ValueKind!=JsonValueKind.Array||children.GetArrayLength()>MaximumNodes)throw new InvalidDataException("Invalid source children.");
    foreach(var child in children.EnumerateArray())
    {if(!child.TryGetInt32(out int index)||(uint)index>=nodes.Length||parents[index]!=-1)throw new InvalidDataException("Source hierarchy has invalid or multiple parents.");parents[index]=i;}
   }
  }
  // Iterative graph validation avoids trusting serialized order or recursion depth.
  for(int i=0;i<nodes.Length;i++)
  {var seen=new HashSet<int>();for(int n=i;n!=-1;n=parents[n])if(!seen.Add(n))throw new InvalidDataException("Source hierarchy cycle.");}
  var graph=nodes.Select((n,i)=>new CharacterSourceNode(n.GetProperty("name").GetString()!,parents[i],locals[i])).ToArray();
  int[] joints=jointElements.Select(j=>j.GetInt32()).ToArray();
  if(joints.Any(j=>(uint)j>=nodes.Length)||joints.Distinct().Count()!=joints.Length)throw new InvalidDataException("Invalid source joint binding.");
  // A transformed mesh ancestor would require a separate mesh-space bind.
  // This first private seam accepts only the existing identity mesh contract.
  var meshAncestors=new SortedSet<int>();
  for(int i=0;i<nodes.Length;i++)if(nodes[i].TryGetProperty("mesh",out _))
   for(int n=i;n!=-1;n=parents[n])
   {if(graph[n].Local!=M.Identity)throw new InvalidDataException("Source mesh must have identity ancestors.");meshAncestors.Add(n);}
  return new(graph,joints,meshAncestors.ToArray());
 }
 private static float[] Values(JsonElement value,int size)
 {
  if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()!=size)throw new InvalidDataException("Invalid source transform width.");
  var values=value.EnumerateArray().Select(x=>x.GetSingle()).ToArray();
  if(values.Any(x=>!float.IsFinite(x)||MathF.Abs(x)>100000))throw new InvalidDataException("Nonfinite or oversized source transform.");return values;
 }
 private static M ReadLocal(JsonElement node)
 {
  if(node.TryGetProperty("matrix",out var matrix))
  {
   if(node.TryGetProperty("translation",out _)||node.TryGetProperty("rotation",out _)||node.TryGetProperty("scale",out _))throw new InvalidDataException("Source matrix and TRS cannot coexist.");
   var a=Values(matrix,16);var result=new M(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);RequireRigid(result);return result;
  }
  var t=node.TryGetProperty("translation",out var translation)?Values(translation,3):new float[3];
  var r=node.TryGetProperty("rotation",out var rotation)?Values(rotation,4):new float[]{0,0,0,1};
  var s=node.TryGetProperty("scale",out var scale)?Values(scale,3):new float[]{1,1,1};
  if(s.Any(x=>MathF.Abs(x-1)>.00001f))throw new InvalidDataException("Scaled source bones need separate acceptance.");
  var q=new Q(r[0],r[1],r[2],r[3]);if(MathF.Abs(q.LengthSquared-1)>.0001f)throw new InvalidDataException("Source quaternion is not unit length.");
  var resultTrs=M.CreateFromQuaternion(q)*M.CreateTranslation(t[0],t[1],t[2]);RequireRigid(resultTrs);return resultTrs;
 }
 internal static void RequireRigid(M matrix)
 {
  float[] a={matrix.M11,matrix.M12,matrix.M13,matrix.M14,matrix.M21,matrix.M22,matrix.M23,matrix.M24,matrix.M31,matrix.M32,matrix.M33,matrix.M34,matrix.M41,matrix.M42,matrix.M43,matrix.M44};
  if(a.Any(x=>!float.IsFinite(x)||MathF.Abs(x)>100000)||MathF.Abs(matrix.M14)>.00001f||MathF.Abs(matrix.M24)>.00001f||MathF.Abs(matrix.M34)>.00001f||MathF.Abs(matrix.M44-1)>.00001f)
   throw new InvalidDataException("Source transform must be finite bounded affine.");
  var x=new V(matrix.M11,matrix.M12,matrix.M13);var y=new V(matrix.M21,matrix.M22,matrix.M23);var z=new V(matrix.M31,matrix.M32,matrix.M33);
  if(MathF.Abs(x.LengthSquared-1)>.001f||MathF.Abs(y.LengthSquared-1)>.001f||MathF.Abs(z.LengthSquared-1)>.001f||MathF.Abs(V.Dot(x,y))>.001f||MathF.Abs(V.Dot(x,z))>.001f||MathF.Abs(V.Dot(y,z))>.001f||V.Dot(V.Cross(x,y),z)<.999f)
   throw new InvalidDataException("Source transform must be rigid and non-mirrored.");
 }
 internal M[] Evaluate(IReadOnlyList<M>? localPose,IReadOnlyList<CharacterWeightedJoint> joints)
 {
  if(localPose!=null&&localPose.Count!=Nodes.Count)throw new InvalidDataException("Source pose must cover the full hierarchy.");
  if(joints.Count!=JointNodes.Count)throw new InvalidDataException("Source joint inventory differs from skin.");
  if(localPose!=null&&MeshAncestors.Any(i=>localPose[i]!=M.Identity))throw new InvalidDataException("Animated source mesh ancestry requires mesh-space acceptance.");
  var globals=new M[Nodes.Count];var done=new bool[Nodes.Count];
  for(int i=0;i<Nodes.Count;i++)
  {
   var chain=new Stack<int>();for(int n=i;n!=-1&&!done[n];n=Nodes[n].Parent)chain.Push(n);
   while(chain.TryPop(out int n))
   {M local=localPose?[n]??Nodes[n].Local;RequireRigid(local);int parent=Nodes[n].Parent;globals[n]=parent<0?local:local*globals[parent];RequireRigid(globals[n]);done[n]=true;}
  }
  var result=new M[joints.Count];for(int i=0;i<result.Length;i++){result[i]=joints[i].InverseBind*globals[JointNodes[i]];RequireRigid(result[i]);}return result;
 }
 internal static float[] GatherSegmentPalette(CharacterWeightedPrimitive batch,IReadOnlyList<M> fullSkin)
 {
  var matrices=Gather(batch,fullSkin);var result=new float[matrices.Length*16];
  for(int i=0;i<matrices.Length;i++)
  {
   var m=matrices[i];float[] values={m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
   Array.Copy(values,0,result,i*16,16);
  }
  return result;
 }
 internal static M[] Gather(CharacterWeightedPrimitive batch,IReadOnlyList<M> fullSkin)
 {
  if(batch.JointPalette is not {Length:>0 and <=32} palette||palette.Distinct().Count()!=palette.Length||palette.Any(x=>(uint)x>=fullSkin.Count))throw new InvalidDataException("Invalid source draw gather map.");
  var result=palette.Select(i=>fullSkin[i]).ToArray();foreach(var matrix in result)RequireRigid(matrix);return result;
 }
}
internal static class CharacterSourceBatches
{
 internal const int MaximumBatches=2048;
 internal static NVector4 SourceWeights(NVector4 weight,int[] joints)
 {
  float[] values={weight.X,weight.Y,weight.Z,weight.W};
  if(values.Any(x=>!float.IsFinite(x)||x<0)||MathF.Abs(values.Sum()-1)>.00001f)throw new InvalidDataException("Source weights must be unit-sum; no silent normalization.");
  var used=new HashSet<int>();for(int i=0;i<4;i++)if(values[i]>0&&!used.Add(joints[i]))throw new InvalidDataException("Duplicate positive source influence.");return weight;
 }
 internal static IReadOnlyList<CharacterWeightedPrimitive> Partition(CharacterWeightedPrimitive primitive,int[][] sourceJoints)
 {
  var batches=new List<CharacterWeightedPrimitive>();var faces=new List<int>();var used=new HashSet<int>();
  void Flush()
  {
   if(faces.Count==0)return;if(batches.Count>=MaximumBatches)throw new InvalidDataException("Source draw batch budget exceeded.");
   int[] palette=used.Order().ToArray();var local=palette.Select((j,i)=>(j,i)).ToDictionary(x=>x.j,x=>x.i);
   var vertices=new List<CharacterWeightedVertex>();var indices=new List<uint>();var vertexMap=new Dictionary<uint,uint>();
   foreach(int face in faces)for(int corner=0;corner<3;corner++)
   {
    uint source=primitive.Indices[face+corner];if(!vertexMap.TryGetValue(source,out uint index))
    {
     if(vertices.Count>=CharacterWeightedModelLoader.MaximumVertices)throw new InvalidDataException("Source batch vertex budget exceeded.");
     index=(uint)vertices.Count;vertexMap[source]=index;var vertex=primitive.Vertices[source];float[] w={vertex.Weights.X,vertex.Weights.Y,vertex.Weights.Z,vertex.Weights.W};
     int[] mapped=sourceJoints[source].Select((j,i)=>w[i]>0?local[j]:0).ToArray();
     vertices.Add(vertex with{PackedJoints=CharacterWeightedModelLoader.PackJoints(mapped[0],mapped[1],mapped[2],mapped[3])});
    }
    indices.Add(index);
   }
   batches.Add(primitive with{Vertices=vertices.ToArray(),Indices=indices.ToArray(),JointPalette=palette,
    SourceTriangleIds=primitive.SourceTriangleIds==null?null:faces.Select(face=>primitive.SourceTriangleIds[face/3]).ToArray()});
  }
  for(int face=0;face<primitive.Indices.Length;face+=3)
  {
   var triangle=new HashSet<int>();for(int corner=0;corner<3;corner++)
   {uint index=primitive.Indices[face+corner];var w=primitive.Vertices[index].Weights;float[] weights={w.X,w.Y,w.Z,w.W};for(int i=0;i<4;i++)if(weights[i]>0)triangle.Add(sourceJoints[index][i]);}
   if(used.Union(triangle).Count()>32){Flush();used.Clear();faces.Clear();}faces.Add(face);used.UnionWith(triangle);
  }
  Flush();return batches;
 }
}
