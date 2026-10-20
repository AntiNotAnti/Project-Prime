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
            var points=ArrayPool<Vector3>.Shared.Rent(8);var start=box.Center+offset-box.Half;
            var x=new Vector3(box.Half.X*2,0,0);var y=new Vector3(0,box.Half.Y*2,0);var z=new Vector3(0,0,box.Half.Z*2);
            points[0]=start;points[1]=start+z;points[2]=start+x;points[3]=start+x+z;
            points[4]=start+y;points[5]=start+y+z;points[6]=start+x+y;points[7]=start+x+y+z;
            for(int i=0;i<8;i++)points[i]=Matrix.Vec3MultMtx4(points[i],transform);
            var color=model.Color(box.Material);color.W=Math.Clamp(alpha,0,1);
            scene.AddRenderItem(CullingMode.Neither,scene.GetNextPolygonId(),color,RenderItemType.Box,points,8);
        }
    }
}
