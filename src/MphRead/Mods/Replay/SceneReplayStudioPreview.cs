#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using MphRead.Formats;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;
namespace MphRead;
public partial class Scene
{
    private sealed class NativeStudioPreviewSettings { public bool Hud=true,Overlay,Path; }
    private static readonly ConditionalWeakTable<Scene,NativeStudioPreviewSettings> NativeStudioPreviews=new();
    internal bool NativeStudioPreviewGameHud=>ReplayCamera.Mode!=ReplayCameraMode.Overview&&(!NativeStudioPreviews.TryGetValue(this,out var s)||s.Hud);
    internal bool NativeStudioPreviewOverlay=>NativeStudioPreviews.TryGetValue(this,out var s)&&s.Overlay;
    internal bool NativeStudioPreviewPath=>NativeStudioPreviews.TryGetValue(this,out var s)&&s.Path;
    internal void SetNativeStudioPreview(bool hud,bool overlay,bool path)
    {
        if(!DemoPlayback.Owns(this))throw new InvalidOperationException("The replay presentation owner changed.");
        var settings=NativeStudioPreviews.GetOrCreateValue(this);settings.Hud=hud;settings.Overlay=overlay;settings.Path=path;
    }
    internal void AddNativeStudioCameraPath()
    {
        if(!NativeStudioPreviewPath||!DemoPlayback.Owns(this)||ReplayVideoExporter.Active||ReplayCamera.Track.Keys.Count<2)return;
        var keys=ReplayCamera.Track.Keys;double first=keys[0].Frame,last=keys[keys.Count-1].Frame;
        for(int i=0;i<=128;i++)
        {
            if(!ReplayCamera.Track.Sample(first+(last-first)*i/128,out var key))continue;
            var right=_cameraRight*.07f;var up=_cameraUp*.07f;var v=ArrayPool<Vector3>.Shared.Rent(4);
            v[0]=key.Position-right-up;v[1]=key.Position+right-up;v[2]=key.Position+right+up;v[3]=key.Position-right+up;
            AddRenderItem(CullingMode.Neither,0,new Vector4(.2f,.9f,1,.85f),RenderItemType.Quad,v);
        }
    }
}
#endif
