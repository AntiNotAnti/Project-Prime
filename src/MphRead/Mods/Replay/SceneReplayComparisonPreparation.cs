#if !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Network;
namespace MphRead;
public partial class Scene
{
    private sealed class ComparisonSubjectScope:IDisposable
    {
        private readonly Scene _scene;private readonly int _subject;
        internal ComparisonSubjectScope(Scene scene){_scene=scene;_subject=scene.Players.MainPlayerIndex;}
        public void Dispose()=>_scene.Players.MainPlayerIndex=_subject;
    }
    internal IDisposable PrepareNativeReplayComparisonPicture(Scene foreground)
    {
        if(ReferenceEquals(this,foreground)||DemoPlayback.Owns(this))throw new InvalidOperationException("Comparison preparation requires an unpublished private world.");
        var scope=new ComparisonSubjectScope(this);
        try
        {
            // The subject is a per-scene presentation choice. Never assign the legacy registry.
            Players.MainPlayerIndex=foreground.Players.MainPlayerIndex;
            for(int i=0;i<Players.Items.Count;i++)
                if(foreground.Players.Items[i].HudReady&&!Players.Items[i].HudReady)Players.Items[i].SetUpHud();
            _cameraMode=foreground._cameraMode;_cameraPosition=foreground._cameraPosition;_cameraFacing=foreground._cameraFacing;
            _cameraUp=foreground._cameraUp;_cameraRight=foreground._cameraRight;_cameraFov=foreground._cameraFov;_freeCam=foreground._freeCam;
            _viewMatrix=foreground._viewMatrix;_viewInvRotMatrix=foreground._viewInvRotMatrix;_viewInvRotYMatrix=foreground._viewInvRotYMatrix;
            _perspectiveMatrix=foreground._perspectiveMatrix;_viewModelPerspectiveMatrix=foreground._viewModelPerspectiveMatrix;_viewModelFov=foreground._viewModelFov;
            ReplayPresentationFrame=foreground.ReplayPresentationFrame;ReplayRenderAlpha=foreground.ReplayRenderAlpha;
            // Index zero fences global replay camera requests without forcing roam.
            MultiViewPasses.Add(this,new MultiViewPass{Index=0});
            try{OnDrawFrame();}finally{MultiViewPasses.Remove(this);}
            return scope;
        }
        catch{scope.Dispose();throw;}
    }
}
#endif
