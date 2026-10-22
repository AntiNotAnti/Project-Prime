using System.Reflection;
using MphRead;
using MphRead.Mods.CustomWeapons;
using OpenTK.Mathematics;

// Identify station geometry in the render queue, independently of held models
// sharing the exact palette. Expected positions are the authored box corners
// scaled .75 about the station, with no weapon mount or animation transform.
internal static class CustomPickupRenderChecks
{
    private static readonly FieldInfo Used=typeof(Scene).GetField("_usedRenderItems",BindingFlags.Instance|BindingFlags.NonPublic)!;
    internal static int CountStation(Scene scene,CustomRifleModel? model,Vector3 center)
        =>model==null?CountFallback(scene,center):CountModel(scene,model,center,.75f,0);
    internal static int CountModel(Scene scene,CustomRifleModel model,Vector3 center,float scale,int lod)
    {
        int count=0;
        foreach(var item in (Queue<RenderItem>)Used.GetValue(scene)!)
            foreach(var box in model.Lod(lod))
                if(Matches(item,center+box.Center*scale,box.Half*scale,model.Color(box.Material)))
                {count++;break;}
        return count;
    }
    internal static int CountFallback(Scene scene,Vector3 center)
    {
        int count=0;
        foreach(var item in (Queue<RenderItem>)Used.GetValue(scene)!)
            if(Matches(item,center,new(.18f,.14f,.42f),new(.12f,.8f,.95f,1))
                ||Matches(item,center+new Vector3(0,.03f,.6f),new(.055f,.055f,.35f),new(.45f,.65f,.75f,1))
                ||Matches(item,center+new Vector3(0,.23f,0),new(.075f,.055f,.2f),new(.4f,1,.9f,1)))count++;
        return count;
    }
    private static bool Matches(RenderItem item,Vector3 center,Vector3 half,Vector4 color)
    {
        if(item.Type!=RenderItemType.Box||item.ItemCount!=8||item.Points.Length<8||item.OverrideColor!=color)return false;
        // Require all eight distinct expected corners, rather than matching a
        // color, origin, or broad bounding volume. Corner order is irrelevant.
        int seen=0;
        for(int point=0;point<8;point++)
        {
            int found=-1;
            for(int corner=0;corner<8;corner++)
            {
                var expected=center+new Vector3((corner&1)==0?-half.X:half.X,
                    (corner&2)==0?-half.Y:half.Y,(corner&4)==0?-half.Z:half.Z);
                if((item.Points[point]-expected).LengthSquared<=1e-9f){found=corner;break;}
            }
            if(found<0||(seen&(1<<found))!=0)return false;
            seen|=1<<found;
        }
        return seen==255;
    }
}
