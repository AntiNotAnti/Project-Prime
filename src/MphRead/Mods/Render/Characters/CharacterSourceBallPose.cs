using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render.Characters;

// Presentation-only fit of the captured ready pose. Native roll, position,
// collision radius and gameplay motion stay with the initialized player.
internal static class CharacterSourceBallPose
{
    internal static Matrix4 Fit(CharacterWeightedModelData source,IReadOnlyList<Matrix4> readyPose,float nativeRadius)
    {
        if (source.SourceRig is not { } rig || !float.IsFinite(nativeRadius) || nativeRadius is <= 0 or > 1000)
            throw new InvalidDataException("Complete source rig and finite positive native ball radius required.");
        var skin=rig.Evaluate(readyPose,source.Joints);
        var points=new List<Vector3>();
        var minimum=new Vector3(float.PositiveInfinity);var maximum=new Vector3(float.NegativeInfinity);
        foreach(var primitive in source.Primitives)
        {
            if(primitive.JointPalette is not {Length:>0 and <=32} palette)
                throw new InvalidDataException("Complete local source ball palette required.");
            foreach(var vertex in primitive.Vertices)
            {
                if(vertex.PackedJoints<0 || vertex.PackedJoints>>20!=0) throw new InvalidDataException("Invalid packed source ball joints.");
                Vector3 position=new(vertex.Position.X,vertex.Position.Y,vertex.Position.Z),point=Vector3.Zero;
                float sum=0;
                for(int influence=0;influence<4;influence++)
                {
                    float weight=vertex.Weights[influence];
                    if(!float.IsFinite(weight)||weight<0)throw new InvalidDataException("Finite positive source ball weights required.");
                    if(weight==0)continue;
                    int slot=vertex.PackedJoints>>(influence*5)&31;
                    if(slot>=palette.Length||(uint)palette[slot]>=(uint)skin.Length)throw new InvalidDataException("Source ball joint outside complete palette.");
                    point+=Vector3.TransformPosition(position,skin[palette[slot]])*weight;sum+=weight;
                }
                if(MathF.Abs(sum-1)>.00001f || !float.IsFinite(point.X)||!float.IsFinite(point.Y)||!float.IsFinite(point.Z))
                    throw new InvalidDataException("Finite unit-weight posed source ball required.");
                points.Add(point);minimum=Vector3.ComponentMin(minimum,point);maximum=Vector3.ComponentMax(maximum,point);
            }
        }
        if(points.Count==0)throw new InvalidDataException("Complete nonempty source ball required.");
        Vector3 center=(minimum+maximum)*.5f;float radius=0;
        foreach(var point in points)radius=MathF.Max(radius,(point-center).Length);
        if(!float.IsFinite(radius)||radius<.0001f)throw new InvalidDataException("Source ball has invalid posed bounds.");
        return Matrix4.CreateTranslation(-center)*Matrix4.CreateScale(nativeRadius/radius);
    }
}
