using System.Buffers;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

// Original procedural fallback shared by offline and synchronized world items.
internal static class CustomPickupPresentation
{
    internal static void Draw(Scene scene,Vector3 center)
    {
        Box(scene,center,new(.18f,.14f,.42f),new(.12f,.8f,.95f,1));
        Box(scene,center+new Vector3(0,.03f,.6f),new(.055f,.055f,.35f),new(.45f,.65f,.75f,1));
        Box(scene,center+new Vector3(0,.23f,0),new(.075f,.055f,.2f),new(.4f,1,.9f,1));
    }
    private static void Box(Scene scene,Vector3 center,Vector3 half,Vector4 color)
    {
        var points=ArrayPool<Vector3>.Shared.Rent(8);var start=center-half;
        var x=new Vector3(half.X*2,0,0);var y=new Vector3(0,half.Y*2,0);var z=new Vector3(0,0,half.Z*2);
        points[0]=start;points[1]=start+z;points[2]=start+x;points[3]=start+x+z;
        points[4]=start+y;points[5]=start+y+z;points[6]=start+x+y;points[7]=start+x+y+z;
        scene.AddRenderItem(CullingMode.Neither,scene.GetNextPolygonId(),color,RenderItemType.Box,points,8);
    }
    internal static void DrawOnline(Scene scene)
    {
        if(Headless.Active||scene.Services.IsReplica||DemoPlayback.IsActive||!NetSession.IsClient||NetSession.IsAuthority
            ||!NetSession.TryGetCustomPickupReplica(out var receiver))return;
        for(int i=0;i<receiver.Layout.Count;i++)
            if(receiver.TryState(i,out var item)&&item.Available)Draw(scene,receiver.Layout.Placements[i].Position);
    }
}
