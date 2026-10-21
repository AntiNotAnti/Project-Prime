// Geometry policy adapted from Second Hunt VeinBall at41452f2d053fcfa679e33c00a73d120a53f59e61.
// MIT, Copyright2020 NoneGiven; see repository LICENSE. Original source stays private.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using MphRead.Mods.Import.Retro;
using OpenTK.Mathematics;

namespace MphRead.Mods.Import;

// Import-time baking only. The complete original39-joint pose is evaluated
// before folding; the resulting static composite rides the original ball rig.
internal static class RetroFinalBallSource
{
    internal const string Policy = "final-vein-ball-v1-source52-time0-shell-vent-vessel-line-heart";
    internal sealed record Result(Cmdl Geometry, object Selection, Vector3d BallCenter);

    internal static Result Build(Cmdl ball, Cmdl body, RetroNativeSkeleton skeleton,
        RetroCharacterAnimationArchive.Snapshot bank, CancellationToken cancellation = default)
    {
        void Require(bool value, string why) { if (!value) throw new InvalidDataException(why); }
        Require(ball.TriangleCount == 2220 && ball.PositionCount == 1553 && ball.Materials.Length == 1
            && body.TriangleCount == 12734 && body.PositionCount == 4849 && body.Materials.Length == 9
            && body.Skin?.Count == body.PositionCount && skeleton.Bones.Count == 39
            && skeleton.Bones[0].Parent == 97, "Complete original Final body and ball required.");
        var root = bank.Roots[52]; var clip = bank.OpenStrictPrimitive(52);
        Require(root.CharacterDeclared && root.Name == "A_outofBoostBall_toChargeReady_dark_samus"
            && clip.Source.Id == 0xc2c4a405 && clip.RigidScale
            && clip.Source.Sha256 == "c29325b7b5ae5eb0efc3ae336d9e5a1d260a3aeef57745fe2b299851f2c0a872",
            "Final composite requires its own strict original fixed pose52.");
        var animation = clip.CaptureAnimation(); animation.Sample(0, out var rotations, out var translations);
        var bones = skeleton.Bones;
        Require(animation.Rot.Keys.Concat(animation.Trans.Keys).Concat(animation.Scale.Keys).All(bones.ContainsKey),
            "Foreign Final composite pose channel.");
        var world = new Dictionary<uint, Matrix4d>(); var visiting = new HashSet<uint>();
        Matrix4d World(uint id)
        {
            if (world.TryGetValue(id, out var existing)) return existing;
            Require(visiting.Add(id), "Final composite source ancestry cycle.");
            var bone = bones[id]; bool hasParent = bones.ContainsKey(bone.Parent);
            Require(hasParent || bone.Parent == 97, "Complete Final source root required.");
            Vector3d bind = hasParent ? new(bone.X-bones[bone.Parent].X,bone.Y-bones[bone.Parent].Y,bone.Z-bones[bone.Parent].Z)
                : new(bone.X,bone.Y,bone.Z);
            var rotation = rotations.GetValueOrDefault(id); var translation = translations.GetValueOrDefault(id);
            var local = Matrix4d.CreateFromQuaternion(rotation == null ? Quaterniond.Identity : new(rotation[0],rotation[1],rotation[2],rotation[3]))
                * Matrix4d.CreateTranslation(translation == null ? bind : new(translation[0],translation[1],translation[2]));
            var result = hasParent ? local * World(bone.Parent) : local;
            visiting.Remove(id); return world[id] = result;
        }
        Vector3d Position(float[] values,int id) => new(values[id*3],values[id*3+1],values[id*3+2]);
        var posed = new Vector3d[body.PositionCount];
        for (int i=0;i<posed.Length;i++)
        {
            cancellation.ThrowIfCancellationRequested();
            Vector4d original = new(Position(body.Positions,i),1); Vector3d sum=Vector3d.Zero;double total=0;
            foreach (var (id,weight) in body.Skin![i])
            {
                Require(float.IsFinite(weight)&&weight>=0&&bones.ContainsKey(id),"Complete finite source influence required.");
                var bind=bones[id]; sum+=weight*(original*(Matrix4d.CreateTranslation(-bind.X,-bind.Y,-bind.Z)*World(id))).Xyz; total+=weight;
            }
            Require(Math.Abs(total-1)<.00001,"Unchanged unit Final source influences required."); posed[i]=sum/total;
        }
        int[] Faces(params int[] materials) => Enumerable.Range(0,body.TriangleCount).Where(t=>materials.Contains(body.TriangleMaterial[t])).ToArray();
        int[] Positions(Cmdl model,IEnumerable<int> faces) => faces.SelectMany(t=>Enumerable.Range(0,3).Select(k=>model.Triangles[t*3+k].P)).Distinct().ToArray();
        Vector3d Center(IEnumerable<Vector3d> values)
        { var points=values.ToArray();return(points.Aggregate(new Vector3d(double.MaxValue),Vector3d.ComponentMin)+points.Aggregate(new Vector3d(double.MinValue),Vector3d.ComponentMax))*.5; }
        var ballPositions=Enumerable.Range(0,ball.PositionCount).Select(i=>Position(ball.Positions,i)).ToArray();
        Vector3d bc=Center(ballPositions);int[] middle=Positions(body,Faces(2,3,4,5,6,7,8));
        double Mid(int axis) {double[] sorted=middle.Select(i=>posed[i][axis]).Order().ToArray();return(sorted[(int)Math.Round(.02*(sorted.Length-1))]+sorted[(int)Math.Round(.98*(sorted.Length-1))])*.5;}
        Vector3d center=new(Mid(0),Mid(1),Mid(2));int[] red=Faces(7);
        var parents=new Dictionary<int,int>();
        int Find(int id) {if(!parents.TryGetValue(id,out int parent)){parents[id]=id;return id;}while(parent!=id){int next=parents[parent];parents[id]=next;id=parent;parent=next;}return id;}
        foreach(int face in red){int a=Find(body.Triangles[face*3].P);parents[Find(body.Triangles[face*3+1].P)]=a;parents[Find(body.Triangles[face*3+2].P)]=Find(a);}
        var heartSet=new HashSet<int>();
        foreach(var group in red.GroupBy(t=>Find(body.Triangles[t*3].P)))
        {
            int[] faces=group.ToArray();var points=Positions(body,faces).Select(i=>posed[i]).ToArray();
            Vector3d extent=points.Aggregate(new Vector3d(double.MinValue),Vector3d.ComponentMax)-points.Aggregate(new Vector3d(double.MaxValue),Vector3d.ComponentMin);
            if(Math.Max(extent.X,Math.Max(extent.Y,extent.Z))<.18&&faces.Length>40)heartSet.UnionWith(faces);
        }
        int[] vessels=red.Where(t=>!heartSet.Contains(t)).ToArray(),heart=red.Where(heartSet.Contains).ToArray();
        double[] distances=Positions(body,vessels).Select(i=>(posed[i]-center).Length).Order().ToArray();
        double maximum=distances[(int)Math.Round(.995*(distances.Length-1))];
        double Facing(int face)
        {
            Vector3d a=posed[body.Triangles[face*3].P],b=posed[body.Triangles[face*3+1].P],c=posed[body.Triangles[face*3+2].P];
            Vector3d normal=Vector3d.Cross(b-a,c-a),outward=(a+b+c)/3-center;
            return normal.Length<1e-12||outward.Length<1e-9?0:Vector3d.Dot(normal/normal.Length,outward/outward.Length);
        }
        int[] lines=Faces(4,5).Where(t=>Facing(t)>.25).ToArray();
        Require(vessels.Length==822&&heart.Length==88&&lines.Length==2820
            &&lines.Count(t=>body.TriangleMaterial[t]==4)==2098,"Complete pinned Final vessel/line/heart selections required.");
        Vector3d heartCenter=Positions(body,heart).Select(i=>posed[i]).Aggregate(Vector3d.Zero,(a,b)=>a+b)/Positions(body,heart).Length;
        Vector3d[] Map(int[] faces,Func<Vector3d,Vector3d> transform)
        {var values=new Vector3d[posed.Length];foreach(int id in Positions(body,faces))values[id]=transform(posed[id]);return values;}
        var vesselPositions=Map(vessels,p=>{var d=p-center;double radius=d.Length;return radius<1e-9?bc:bc+d/radius*(.445*Math.Pow(radius/maximum,.2));});
        var linePositions=Map(lines,p=>{var d=p-center;return d.Length<1e-9?bc:bc+d/d.Length*.47;});
        var heartPositions=Map(heart,p=>bc+(p-heartCenter)*2.3);
        var positionsOut=new List<float>();var normalsOut=new List<float>();var uvOut=new List<float>();
        var cornersOut=new List<Cmdl.Corner>();var materialsOut=new List<int>();
        void Append(Cmdl source,int[] faces,Vector3d[] positions,bool smooth,Func<int,int> material)
        {
            cancellation.ThrowIfCancellationRequested();
            int positionOffset=positionsOut.Count/3,normalOffset=normalsOut.Count/3,uvOffset=uvOut.Count/2;
            var normals=new Vector3d[positions.Length];
            if(smooth)foreach(int face in faces)
            {int a=source.Triangles[face*3].P,b=source.Triangles[face*3+1].P,c=source.Triangles[face*3+2].P;var n=Vector3d.Cross(positions[b]-positions[a],positions[c]-positions[a]);normals[a]+=n;normals[b]+=n;normals[c]+=n;}
            foreach(var point in positions)positionsOut.AddRange(new[]{(float)point.X,(float)point.Y,(float)point.Z});
            if(smooth)foreach(var normal in normals){var n=normal.Length>1e-12?normal/normal.Length:Vector3d.Zero;normalsOut.AddRange(new[]{(float)n.X,(float)n.Y,(float)n.Z});}
            else normalsOut.AddRange(source.Normals);
            uvOut.AddRange(source.Uv0);
            foreach(int face in faces)
            {
                materialsOut.Add(material(source.TriangleMaterial[face]));
                for(int k=0;k<3;k++)
                {
                    var corner=source.Triangles[face*3+k];
                    Require(corner.T0>=0&&corner.T0*2+1<source.Uv0.Length,"Complete Final composite UV required.");
                    foreach(int alias in new[]{corner.T1,corner.T2})Require(alias<0||alias*2+1<source.Uv0.Length
                        &&source.Uv0[alias*2]==source.Uv0[corner.T0*2]&&source.Uv0[alias*2+1]==source.Uv0[corner.T0*2+1],"Final composite UV alias changed.");
                    corner.P+=positionOffset;corner.N=(smooth?source.Triangles[face*3+k].P:corner.N)+normalOffset;
                    corner.T0+=uvOffset;corner.T1=corner.T0;corner.T2=corner.T0;cornersOut.Add(corner);
                }
            }
        }
        int[] all=Enumerable.Range(0,ball.TriangleCount).ToArray();
        Append(ball,all,ballPositions,false,_=>0);Append(ball,all,ballPositions,false,_=>1);
        Append(body,vessels,vesselPositions,true,_=>2);Append(body,lines,linePositions,true,m=>m==4?3:4);Append(body,heart,heartPositions,true,_=>5);
        var geometry=new Cmdl{Version=4,Positions=positionsOut.ToArray(),Normals=normalsOut.ToArray(),Uv0=uvOut.ToArray(),
            Triangles=cornersOut.ToArray(),TriangleMaterial=materialsOut.ToArray(),Materials=Enumerable.Range(0,6).Select(_=>new Cmdl.Material()).ToArray(),
            Skin=Enumerable.Range(0,positionsOut.Count/3).Select(_=>new[]{(Bone:1u,Weight:1f)}).ToList()};
        string Hash(int[] ids)=>RetroAnimationGraphProof.Sha(JsonSerializer.SerializeToUtf8Bytes(ids));
        object selection=new{policy=Policy,clipOrdinal=52,clipResourceId=clip.Source.Id,clipSha256=clip.Source.Sha256,clipSeconds=0,
            shell=new{triangles=2220,sourceIdsSha256=Hash(all)},vent=new{triangles=2220,sourceIdsSha256=Hash(all)},
            vessel=new{triangles=vessels.Length,sourceIdsSha256=Hash(vessels)},line=new{triangles=lines.Length,sourceIdsSha256=Hash(lines)},
            heart=new{triangles=heart.Length,sourceIdsSha256=Hash(heart)},ballCenter=new[]{bc.X,bc.Y,bc.Z},
            foldedFromCompleteSourceRig=39,staticCompositeBallRig=2,sourcePhysicsChanged=false};
        Require(geometry.TriangleCount==8170,"Complete Final composite required.");
        return new(geometry,selection,bc);
    }
}
