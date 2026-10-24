using System;
using System.Buffers;
using System.Collections.Generic;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Oddball;

/// <summary>Original solid skull mesh. No ROM geometry or external model; submission storage uses the renderer-owned shared pool.</summary>
internal sealed class OddballSkullModel
{
    private readonly record struct Face(Vector3 A, Vector3 B, Vector3 C, Vector3 D, Vector4 Color);
    private static readonly Face[] Geometry = Build();
    static OddballSkullModel()
    {
        // Warm the shared packet pool during native player/flag loading, not on
        // the first pickup. Scene takes ownership of rented arrays each draw.
        var buffers = new Vector3[Geometry.Length][];
        for (int i = 0; i < buffers.Length; i++) buffers[i] = ArrayPool<Vector3>.Shared.Rent(4);
        foreach (var buffer in buffers) ArrayPool<Vector3>.Shared.Return(buffer);
    }
    private static Face[] Build()
    {
        var faces = new List<Face>(700);
        var bone = new Vector3(.83f, .77f, .62f);
        void Sphere(Vector3 center, Vector3 radius, Vector3 color, int segments = 24, int rings = 12)
        {
            Vector3 Point(float a, float b) => center + radius * new Vector3(MathF.Cos(a) * MathF.Sin(b), MathF.Cos(b), MathF.Sin(a) * MathF.Sin(b));
            for (int y = 0; y < rings; y++) for (int x = 0; x < segments; x++)
            {
                float a = x * MathF.Tau / segments, next = (x + 1) * MathF.Tau / segments;
                float b = y * MathF.PI / rings, end = (y + 1) * MathF.PI / rings;
                float light = .61f + .34f * Math.Clamp(MathF.Cos(b + .3f) * .65f + MathF.Sin(a + .5f) * .35f, 0, 1);
                faces.Add(new Face(Point(a,b), Point(next,b), Point(next,end), Point(a,end), new Vector4(color * light, 1)));
            }
        }
        void Box(Vector3 c, Vector3 r, Vector3 color)
        {
            var a=c-r;var b=c+r;var v=new[]{new Vector3(a.X,a.Y,a.Z),new Vector3(b.X,a.Y,a.Z),new Vector3(b.X,b.Y,a.Z),new Vector3(a.X,b.Y,a.Z),new Vector3(a.X,a.Y,b.Z),new Vector3(b.X,a.Y,b.Z),new Vector3(b.X,b.Y,b.Z),new Vector3(a.X,b.Y,b.Z)};
            int[,] indices={{0,1,2,3},{4,7,6,5},{0,4,5,1},{3,2,6,7},{0,3,7,4},{1,5,6,2}};
            for(int i=0;i<6;i++) faces.Add(new Face(v[indices[i,0]],v[indices[i,1]],v[indices[i,2]],v[indices[i,3]],new Vector4(color*(.68f+i*.048f),1)));
        }
        Sphere(new Vector3(0,.17f,-.03f), new Vector3(.43f,.46f,.35f), bone);
        Sphere(new Vector3(0,-.21f,.07f), new Vector3(.29f,.20f,.25f), bone*.86f,20,8);
        Sphere(new Vector3(-.19f,.13f,.281f), new Vector3(.142f,.15f,.073f), new Vector3(.045f,.025f,.018f),16,8);
        Sphere(new Vector3(.19f,.13f,.281f), new Vector3(.142f,.15f,.073f), new Vector3(.045f,.025f,.018f),16,8);
        // Cheekbones, brow ridges, nasal cavity and individual teeth keep the silhouette readable.
        Sphere(new Vector3(-.30f,-.07f,.24f),new Vector3(.115f,.09f,.08f),bone,12,6);
        Sphere(new Vector3(.30f,-.07f,.24f),new Vector3(.115f,.09f,.08f),bone,12,6);
        Sphere(new Vector3(-.18f,.29f,.29f),new Vector3(.16f,.055f,.065f),bone,16,6);
        Sphere(new Vector3(.18f,.29f,.29f),new Vector3(.16f,.055f,.065f),bone,16,6);
        faces.Add(new Face(new Vector3(0,.08f,.363f),new Vector3(-.071f,-.07f,.35f),new Vector3(.071f,-.07f,.35f),new Vector3(0,.08f,.363f),new Vector4(.04f,.027f,.017f,1)));
        Box(new Vector3(0,-.22f,.298f),new Vector3(.215f,.063f,.023f),new Vector3(.055f,.035f,.02f));
        for(int i=0;i<7;i++)
        {
            float x=(i-3)*.057f;
            Box(new Vector3(x,-.169f,.325f),new Vector3(.024f,.042f,.03f),bone*1.07f);
            Box(new Vector3(x,-.29f,.299f),new Vector3(.024f,.036f,.033f),bone*.96f);
        }
        return faces.ToArray();
    }
    public void Draw(Scene scene, Vector3 origin, Vector3 right, Vector3 up, Vector3 forward, float scale)
    {
        Vector3 Transform(Vector3 v) => origin + scale * (right * v.X + up * v.Y + forward * v.Z);
        for (int i = 0; i < Geometry.Length; i++)
        {
            // Non-mesh render packets transfer array ownership to Scene, which returns
            // every packet to this pool at the next frame. Never submit retained new[].
            var face = Geometry[i]; var p = ArrayPool<Vector3>.Shared.Rent(4);
            p[0] = Transform(face.A); p[1] = Transform(face.B); p[2] = Transform(face.C); p[3] = Transform(face.D);
            scene.AddRenderItem(CullingMode.Neither, scene.GetNextPolygonId(), face.Color, RenderItemType.Ngon, p, 4, true);
        }
    }
}
