#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Buffers;
using System.Collections.Immutable;
using MphRead.Mods.Launcher.Core;
using MphRead.Formats;
using OpenTK.Mathematics;
namespace MphRead;
public partial class Scene
{
    // Per-draw geometry only: never creates particles, advances effects or enters replay checkpoints.
    internal void AddNativeReplaySpatialMarkers(ImmutableArray<TheatreSpatialSample> samples)
    {
        foreach(var sample in samples)
        {
            var center=new Vector3(sample.X,sample.Y,sample.Z);
            float size=Math.Clamp(sample.Weight/10,.1f,.7f);
            var right=_cameraRight*size;var up=_cameraUp*size;
            var vertices=ArrayPool<Vector3>.Shared.Rent(4);
            vertices[0]=center-right-up;vertices[1]=center+right-up;
            vertices[2]=center+right+up;vertices[3]=center-right+up;
            AddRenderItem(CullingMode.Neither,0,new Vector4(1,.4f,.15f,.8f),RenderItemType.Quad,vertices);
        }
    }
}
#endif
