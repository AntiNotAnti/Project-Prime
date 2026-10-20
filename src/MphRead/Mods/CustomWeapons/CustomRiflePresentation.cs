using System;
using System.Buffers;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

// Render submission borrows only an immutable prewarmed model. It cannot load
// files, advance animation, select weapons or mutate a gameplay muzzle.
internal static class CustomRiflePresentation
{
    internal static void Draw(Scene scene,CustomRifleModel model,in Matrix4 transform,float alpha,int lod,
        float bolt=0,float magazine=0)
    {
        if(Mods.Headless.Active||!float.IsFinite(alpha)||alpha<=0)return;
        foreach(var box in model.Lod(lod))
        {
            var offset=box.Part switch
            {CustomRiflePart.Bolt=>new Vector3(0,0,-Math.Clamp(bolt,0,1)*.16f),
             CustomRiflePart.Magazine=>new Vector3(0,-Math.Clamp(magazine,0,1)*.2f,0),_=>Vector3.Zero};
            var color=model.Color(box.Material);color.W=Math.Clamp(alpha,0,1);
            Box(scene,box.Center+offset,box.Half,transform,color);
        }
    }
    internal static void Box(Scene scene,Vector3 center,Vector3 half,in Matrix4 transform,Vector4 color)
    {
        var points=ArrayPool<Vector3>.Shared.Rent(8);var start=center-half;
        var x=new Vector3(half.X*2,0,0);var y=new Vector3(0,half.Y*2,0);var z=new Vector3(0,0,half.Z*2);
        points[0]=start;points[1]=start+z;points[2]=start+x;points[3]=start+x+z;
        points[4]=start+y;points[5]=start+y+z;points[6]=start+x+y;points[7]=start+x+y+z;
        for(int i=0;i<8;i++)points[i]=Matrix.Vec3MultMtx4(points[i],transform);
        scene.AddRenderItem(CullingMode.Neither,scene.GetNextPolygonId(),color,RenderItemType.Box,points,8);
    }
    internal static void DrawFallback(Scene scene,in Matrix4 transform,float alpha)
    {
        if(Mods.Headless.Active||alpha<=0)return;
        Box(scene,new(0,0,-.02f),new(.12f,.12f,.42f),transform,new(.12f,.8f,.95f,alpha));
        Box(scene,new(0,.04f,.9f),new(.05f,.05f,.52f),transform,new(.45f,.65f,.75f,alpha));
        Box(scene,new(0,.24f,0),new(.07f,.05f,.22f),transform,new(.4f,1,.9f,alpha));
    }
    internal static void Flash(Scene scene,in Matrix4 transform,Vector3 socket,float alpha)
    {
        float size=.04f+Math.Clamp(alpha,0,1)*.06f;
        Box(scene,socket,new(size,size,size*1.5f),transform,new(1,.72f,.22f,Math.Clamp(alpha,0,1)));
    }
    internal static void Trail(Scene scene,Vector3 from,Vector3 to,float alpha)
    {
        var delta=to-from;if(!float.IsFinite(delta.LengthSquared)||delta.LengthSquared<.000001f||delta.LengthSquared>1000000)return;
        var side=Vector3.Cross(delta.Normalized(),Vector3.UnitY);
        if(side.LengthSquared<.001f)side=Vector3.UnitX;else side=side.Normalized();
        var up=Vector3.Cross(delta.Normalized(),side).Normalized();
        Ribbon(scene,from,to,side*.012f,alpha);Ribbon(scene,from,to,up*.012f,alpha);
    }
    private static void Ribbon(Scene scene,Vector3 from,Vector3 to,Vector3 side,float alpha)
    {
        var points=ArrayPool<Vector3>.Shared.Rent(4);
        points[0]=from-side;points[1]=from+side;points[2]=to+side;points[3]=to-side;
        scene.AddRenderItem(CullingMode.Neither,scene.GetNextPolygonId(),new(.24f,.9f,1,Math.Clamp(alpha,0,1)),RenderItemType.Quad,points,4);
    }
    internal static void Spark(Scene scene,Vector3 point,float alpha)
    {
        float size=.03f+(1-Math.Clamp(alpha,0,1))*.10f;
        var transform=Matrix4.CreateTranslation(point);var color=new Vector4(.9f,1,.8f,Math.Clamp(alpha,0,1));
        Box(scene,Vector3.Zero,new(size,.012f,.012f),transform,color);
        Box(scene,Vector3.Zero,new(.012f,size,.012f),transform,color);
        Box(scene,Vector3.Zero,new(.012f,.012f,size),transform,color);
    }

}
