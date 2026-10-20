using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods.Campaign;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class CampaignMapDetailTests
{
    private static readonly Vector3[] Square={new(0,0,0),new(2,0,0),new(2,0,2),new(0,0,2)};
    private static CollisionFace Face(int count=4,int start=0,int plane=0,CollisionFlags flags=0)=>new(0,plane,flags,0,0,count,start);
    // Use the actual normalized collision owner and actual CollisionFace constructor.
    // Its fixed-point constructor's detached float tables are populated directly so
    // malformed floating inputs can be tested without proprietary bytes or a parser.
    private static PrimeCollisionInfo Source(Vector3[]? points=null,CollisionFace[]? faces=null,
        int[]? indices=null,Vector4[]? planes=null)
    {
        points ??= Square; faces ??= new[]{Face()}; indices ??= new[]{0,1,2,3}; planes ??= new[]{new Vector4(0,1,0,0)};
        var source=new PrimeCollisionInfo(default,new Vector3Fx[points.Length],new Vector4Fx[planes.Length],
            indices,faces,Array.Empty<int>(),Array.Empty<CollisionGridEntry>(),Array.Empty<Portal>());
        for(int i=0;i<points.Length;i++)((IList<Vector3>)source.Points)[i]=points[i];
        for(int i=0;i<planes.Length;i++)((IList<Vector4>)source.Planes)[i]=planes[i];
        return source;
    }
    internal static int Run()
    {
        int checks=0;
        void Check(bool condition,string label)
        { if(!condition)throw new InvalidOperationException(label);++checks;Console.WriteLine("CAMPAIGN MAP DETAIL PASS "+label); }
        void Refused(PrimeCollisionInfo source,string label,int budget=CampaignMapDetail.MaxOutputVertices)
        {
            var result=CampaignMapDetail.Build(source,"UNIT1_LAND",Vector3.Zero,maximumOutputVertices:budget);
            Check(!result.Ready&&result.Reason.Length>0&&result.Polygons.Count==0,label);
        }
        try
        {
            var source=Source();
            var good=CampaignMapDetail.Build(source,"UNIT1_LAND",new Vector3(4,5,6));
            Check(good.Ready&&good.Reason.Length==0&&good.Polygons.Count==2
                &&good.Polygons[0].Vertices.SequenceEqual(new[]{Square[0],Square[1],Square[2]}.Select(p=>p+new Vector3(4,5,6)))
                &&good.Polygons[1].Vertices.SequenceEqual(new[]{Square[0],Square[2],Square[3]}.Select(p=>p+new Vector3(4,5,6)))
                &&good.Polygons.All(p=>p.Normal==-Vector3.UnitY&&p.SourceFace==0&&p.Vertices.Count==3)
                &&good.SkippedDegenerateTriangles==0,
                "valid square retains the two exact native fan triangles and geometry-derived winding normals");
            ((IList<Vector3>)source.Points)[0]=new(99,99,99);
            Check(good.Polygons[0].Vertices[0]==new Vector3(4,5,6),"source edits cannot alter published detail vertices");
            bool readOnly=false;
            try{((IList<Vector3>)good.Polygons[0].Vertices)[0]=Vector3.Zero;}catch(NotSupportedException){readOnly=true;}
            Check(readOnly,"published polygon vertices reject mutation");
            readOnly=false;
            try{((IList<CampaignMapDetailPolygon>)good.Polygons).Clear();}catch(NotSupportedException){readOnly=true;}
            Check(readOnly,"published polygon inventory rejects mutation");

            var filtered=CampaignMapDetail.Build(Source(faces:new[]{Face(flags:CollisionFlags.IgnorePlayers),Face(2),Face()}),null,Vector3.Zero);
            Check(filtered.Ready&&filtered.Polygons.Count==2&&filtered.Polygons.All(p=>p.SourceFace==2)
                &&filtered.SkippedDegenerateTriangles==0,
                "beam-only and short faces are filtered without changing either retained triangle source ordinal");
            Refused(Source(faces:new[]{Face(flags:CollisionFlags.IgnorePlayers)}),"empty player geometry fails closed");
            Check(!CampaignMapDetail.Build(null,null,Vector3.Zero).Ready,"missing source has an explicit refusal");
            Refused(Source(faces:new[]{Face(),Face(start:2)}),"bad later span publishes no earlier valid polygon");
            Refused(Source(faces:new[]{Face(start:-1)}),"negative face start rejected");
            Refused(Source(faces:new[]{Face(count:-1)}),"negative face count rejected");
            Refused(Source(faces:new[]{Face(count:11)}),"oversized face rejected");
            Refused(Source(faces:new[]{Face(start:Int32.MaxValue)}),"overflowing face end rejected");
            Refused(Source(faces:new[]{Face(plane:-1)}),"negative plane index rejected");
            Refused(Source(faces:new[]{Face(plane:1)}),"missing plane rejected");
            Refused(Source(indices:new[]{0,1,-1,3}),"negative point index rejected");
            Refused(Source(indices:new[]{0,1,4,3}),"point index beyond table rejected");
            Refused(Source(faces:new[]{Face(start:-1,flags:CollisionFlags.IgnorePlayers)}),"ignored face cannot conceal malformed span");
            Refused(Source(points:new[]{new Vector3(float.NaN,0,0),Square[1],Square[2],Square[3]}),"nonfinite source point rejected");
            Refused(Source(points:new[]{new Vector3(float.PositiveInfinity,0,0),Square[1],Square[2],Square[3]}),"infinite source point rejected");
            Refused(Source(points:new[]{new Vector3(CampaignMapDetail.MaxCoordinate+1,0,0),Square[1],Square[2],Square[3]}),"out-of-budget coordinate rejected");
            var zeroPlane=CampaignMapDetail.Build(Source(planes:new[]{Vector4.Zero}),null,Vector3.Zero);
            Check(zeroPlane.Ready&&zeroPlane.Polygons.Count==2&&zeroPlane.Polygons[0].Normal==-Vector3.UnitY,
                "finite zero physics plane cannot suppress valid source-preserving render triangles");
            Refused(Source(planes:new[]{new Vector4(0,float.NaN,0,0)}),"nonfinite plane normal rejected");
            Refused(Source(planes:new[]{new Vector4(0,1,0,float.PositiveInfinity)}),"nonfinite plane offset rejected");
            var mismatchedPlane=CampaignMapDetail.Build(Source(planes:new[]{new Vector4(0,1,0,19.55f)}),null,Vector3.Zero);
            Check(mismatchedPlane.Ready&&mismatchedPlane.Polygons[0].Vertices.SequenceEqual(new[]{Square[0],Square[1],Square[2]})
                &&mismatchedPlane.Polygons[1].Vertices.SequenceEqual(new[]{Square[0],Square[2],Square[3]}),
                "referenced plane disagreement does not project or repair native render vertices");
            var zeroArea=CampaignMapDetail.Build(Source(points:new[]{new Vector3(0,0,0),new(1,0,0),new(2,0,0),new(3,0,0)}),null,Vector3.Zero);
            Check(!zeroArea.Ready&&zeroArea.Reason.Length>0&&zeroArea.Polygons.Count==0&&zeroArea.SkippedDegenerateTriangles==2,
                "all-zero-area native fan has an explicit fallback and exact skipped triangle count");
            var closing=CampaignMapDetail.Build(Source(indices:new[]{0,1,2,0}),null,Vector3.Zero);
            Check(closing.Ready&&closing.Polygons.Count==1&&closing.SkippedDegenerateTriangles==1
                &&closing.Polygons[0].Vertices.SequenceEqual(new[]{Square[0],Square[1],Square[2]}),
                "repeated closing vertex skips only its exact-zero triangle without repairing the source face");
            var firstZero=CampaignMapDetail.Build(Source(indices:new[]{0,0,1,2}),null,Vector3.Zero);
            Check(firstZero.Ready&&firstZero.Polygons.Count==1&&firstZero.SkippedDegenerateTriangles==1
                &&firstZero.Polygons[0].Vertices.SequenceEqual(new[]{Square[0],Square[1],Square[2]}),
                "an initial exact-zero fan triangle cannot hide a later valid triangle");
            var bowtie=CampaignMapDetail.Build(Source(indices:new[]{0,2,1,3}),null,Vector3.Zero);
            Check(bowtie.Ready&&bowtie.Polygons.Count==2&&bowtie.SkippedDegenerateTriangles==0
                &&bowtie.Polygons[0].Vertices.SequenceEqual(new[]{Square[0],Square[2],Square[1]})
                &&bowtie.Polygons[1].Vertices.SequenceEqual(new[]{Square[0],Square[1],Square[3]})
                &&Vector3.Dot(bowtie.Polygons[0].Normal,bowtie.Polygons[1].Normal)<-0.99f,
                "bowtie source preserves exact native fan order and opposing windings without triangulation repair");
            var concavePoints=new[]{new Vector3(0,0,0),new(2,0,0),new(.5f,0,.75f),new(2,0,2),new(0,0,2)};
            var concave=CampaignMapDetail.Build(Source(points:concavePoints,faces:new[]{Face(5)},indices:new[]{0,1,2,3,4}),null,Vector3.Zero);
            Check(concave.Ready&&concave.Polygons.Count==3&&concave.SkippedDegenerateTriangles==0
                &&concave.Polygons[0].Vertices.SequenceEqual(new[]{concavePoints[0],concavePoints[1],concavePoints[2]})
                &&concave.Polygons[1].Vertices.SequenceEqual(new[]{concavePoints[0],concavePoints[2],concavePoints[3]})
                &&concave.Polygons[2].Vertices.SequenceEqual(new[]{concavePoints[0],concavePoints[3],concavePoints[4]})
                &&Vector3.Dot(concave.Polygons[0].Normal,concave.Polygons[1].Normal)<-0.99f,
                "concave source keeps all three exact native fan triangles including opposite winding");
            var warpedPoints=new[]{Square[0],Square[1],Square[2],new Vector3(0,5.91f,2)};
            var warped=CampaignMapDetail.Build(Source(points:warpedPoints),null,Vector3.Zero);
            Check(warped.Ready&&warped.Polygons.Count==2&&warped.SkippedDegenerateTriangles==0
                &&warped.Polygons[0].Vertices.SequenceEqual(new[]{warpedPoints[0],warpedPoints[1],warpedPoints[2]})
                &&warped.Polygons[1].Vertices.SequenceEqual(new[]{warpedPoints[0],warpedPoints[2],warpedPoints[3]})
                &&Vector3.Dot(warped.Polygons[0].Normal,warped.Polygons[1].Normal)<0.9f,
                "warped source preserves each native fan triangle with its own geometric normal");
            var tiny=CampaignMapDetail.Build(Source(points:new[]{Vector3.Zero,new Vector3(1f/4096f,0,0),new Vector3(0,0,1f/4096f)},
                faces:new[]{Face(3)},indices:new[]{0,1,2}),null,Vector3.Zero);
            Check(tiny.Ready&&tiny.Polygons.Count==1&&tiny.SkippedDegenerateTriangles==0
                &&tiny.Polygons[0].Vertices[1].X==1f/4096f,
                "tiny nonzero native triangle survives without epsilon-based welding or area culling");
            var reversed=CampaignMapDetail.Build(Source(indices:new[]{3,2,1,0}),null,Vector3.Zero);
            Check(reversed.Ready&&reversed.Polygons.Count==2
                &&reversed.Polygons[0].Vertices.SequenceEqual(new[]{Square[3],Square[2],Square[1]})
                &&reversed.Polygons[1].Vertices.SequenceEqual(new[]{Square[3],Square[1],Square[0]})
                &&reversed.Polygons.All(p=>p.Normal==Vector3.UnitY),
                "valid reversed winding retains both source fan triangles and reversed normals");
            var exactBudget=CampaignMapDetail.Build(Source(),null,Vector3.Zero,maximumOutputVertices:6);
            Check(exactBudget.Ready&&exactBudget.Polygons.Sum(p=>p.Vertices.Count)==6,
                "output budget counts emitted triangle vertices rather than original quad corners");
            Refused(Source(),"insufficient fan output budget publishes no partial triangle",5);

            Refused(Source(points:new Vector3[CampaignMapDetail.MaxPoints+1]),"input point budget checked before decoding geometry");
            Refused(Source(planes:new Vector4[CampaignMapDetail.MaxPlanes+1]),"input plane budget checked before validation");
            Refused(Source(faces:new CollisionFace[CampaignMapDetail.MaxFaces+1]),"input face budget rejected");
            Refused(Source(indices:new int[CampaignMapDetail.MaxIndices+1]),"input index budget rejected");
            Refused(Source(faces:new[]{Face(),Face()}),"output budget is all-or-nothing",7);
            Refused(Source(),"invalid requested output budget rejected",0);
            Refused(Source(),"caller cannot expand hard output budget",CampaignMapDetail.MaxOutputVertices+1);
            Check(!CampaignMapDetail.Build(Source(),null,new Vector3(float.NaN,0,0)).Ready,"nonfinite placement rejected");
            Check(!CampaignMapDetail.Build(Source(),null,new Vector3(CampaignMapDetail.MaxCoordinate,0,0)).Ready,
                "transform overflow publishes no detail polygons");

            Vector3 point=new(3,10,7),origin=new(100,200,300),connector=new(1,2,3);
            Check(CampaignMapDetail.RoomToMap("UNIT2_C2",point,origin,connector)==new Vector3(102,218.5f,304),
                "Fan Room Alpha scales local Y before subtracting connector translation");
            var beta=CampaignMapDetail.RoomToMap("unit2_c3",point,origin,connector);
            Check((beta-new Vector3(102,211.05f,304)).Length<0.0001f,"Fan Room Beta uses canonical height transform");
            Vector3 geometry=CampaignMapDetail.RoomToMap("UNIT2_C2",point,origin,connector);
            Vector3 marker=CampaignMapDetail.RoomToMap("UNIT2_C2",point,origin,connector,markerLift:1);
            Check(marker==geometry+Vector3.UnitY,"marker lift is separate from room and connector transforms");
            Vector3 half=new(-80.87378f,22.282959f,205.73096f);
            Check((CampaignMapDetail.RoomToMap(null,point,origin+half)-CampaignMapDetail.RoomToMap(null,point,origin)-half).Length<0.0001f,
                "two area halves share the supplied native NAV-origin offset exactly once");
            var slopes=new[]{new Vector3(0,0,0),new(1,-1,0),new(1,-1,1),new(0,0,1)};
            var sloped=CampaignMapDetail.Build(Source(points:slopes,planes:new[]{new Vector4(1,1,0,0)}),"UNIT2_C2",Vector3.Zero);
            Check(sloped.Ready&&MathF.Abs(Vector3.Dot(sloped.Polygons[0].Normal,
                sloped.Polygons[0].Vertices[1]-sloped.Polygons[0].Vertices[0]))<0.0001f,
                "Fan Room triangle normal is derived from its transformed geometry");
            bool badTransform=false;
            try{CampaignMapDetail.RoomToMap(null,point,origin,markerLift:float.NaN);}catch(ArgumentException){badTransform=true;}
            Check(badTransform,"nonfinite independent marker lift rejected");
            CampaignMapDetailSourceTests.Run(Check);
            Console.WriteLine($"CAMPAIGN MAP DETAIL PASS {checks} checks; detached geometry and synthetic source/load lifecycle, no cartridge/GL/privacy activation claim.");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine("CAMPAIGN MAP DETAIL FAIL "+error);return 1;}
    }
}
