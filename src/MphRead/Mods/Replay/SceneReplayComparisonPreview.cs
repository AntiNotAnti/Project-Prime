#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Linq;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
namespace MphRead;
public partial class Scene
{
    internal bool DrawNativeReplayComparisonViews(Scene compared,Vector4i bounds,Vector2i framebuffer)
    {
        if(ReferenceEquals(this,compared)||!DemoPlayback.Owns(this)||DemoPlayback.Owns(compared)
            ||!ReferenceEquals(DemoPlayback.PresentationScene,this)||ReplayController.IsSeeking
            ||ReplayVideoExporter.Active||NativeReplayMultiViewActive||compared.NativeReplayMultiViewActive||bounds.Z<4||bounds.W<4)return false;
        var preview=ReplayPreviewSize;var previewBounds=ReplayPreviewBounds;
        var bPreview=compared.ReplayPreviewSize;var bBounds=compared.ReplayPreviewBounds;
        var mode=_cameraMode;var position=_cameraPosition;var facing=_cameraFacing;var up=_cameraUp;var right=_cameraRight;float fov=_cameraFov;
        var view=_viewMatrix;var inv=_viewInvRotMatrix;var invY=_viewInvRotYMatrix;var projection=_perspectiveMatrix;
        var modelProjection=_viewModelPerspectiveMatrix;float modelFov=_viewModelFov;
        int frustumIndex=FrustumInfo.Index,frustumCount=FrustumInfo.Count;var planes=FrustumInfo.Planes.ToArray();
        int subject=Players.MainPlayerIndex;bool free=_freeCam;
        int draw=GL.GetInteger(GetPName.DrawFramebufferBinding),read=GL.GetInteger(GetPName.ReadFramebufferBinding),program=GL.GetInteger(GetPName.CurrentProgram);
        void RestoreCamera()
        {
            _cameraMode=mode;_cameraPosition=position;_cameraFacing=facing;_cameraUp=up;_cameraRight=right;_cameraFov=fov;_freeCam=free;Players.MainPlayerIndex=subject;
            _viewMatrix=view;_viewInvRotMatrix=inv;_viewInvRotYMatrix=invY;_perspectiveMatrix=projection;
            _viewModelPerspectiveMatrix=modelProjection;_viewModelFov=modelFov;
            FrustumInfo.Index=frustumIndex;FrustumInfo.Count=frustumCount;Array.Copy(planes,FrustumInfo.Planes,planes.Length);
        }
        GL.PushAttrib(AttribMask.AllAttribBits);MultiViewPasses.Add(this,new MultiViewPass{Index=0});
        try
        {
            // Both images retain the exact reviewed camera projection. Fit each
            // complete picture in its half instead of squeezing it horizontally.
            Vector4i FitHalf(int x,int width)
            {
                double scale=Math.Min(width/(double)Size.X,bounds.W/(double)Size.Y);
                int fittedWidth=Math.Max(1,(int)Math.Floor(Size.X*scale)),fittedHeight=Math.Max(1,(int)Math.Floor(Size.Y*scale));
                return new(x+(width-fittedWidth)/2,bounds.Y+(bounds.W-fittedHeight)/2,fittedWidth,fittedHeight);
            }
            int left=bounds.Z/2;ReplayPreviewSize=framebuffer;ReplayPreviewBounds=FitHalf(bounds.X,left);
            OnDrawFrame();OnRenderFrame();RestoreCamera();
            compared.ReplayPreviewSize=framebuffer;compared.ReplayPreviewBounds=FitHalf(bounds.X+left,bounds.Z-left);
            using(compared.PrepareNativeReplayComparisonPicture(this))
            {
                // Preparation has already built B's exact view and retained world.
                MultiViewPasses.Add(compared,new MultiViewPass{Index=1});
                try{compared.OnRenderFrame();}finally{MultiViewPasses.Remove(compared);}
            }
            return true;
        }
        finally
        {
            RestoreCamera();ReplayPreviewSize=preview;ReplayPreviewBounds=previewBounds;
            compared.ReplayPreviewSize=bPreview;compared.ReplayPreviewBounds=bBounds;
            try{OnDrawFrame();}
            finally
            {
                RestoreCamera();MultiViewPasses.Remove(this);GL.PopAttrib();
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer,draw);GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,read);GL.UseProgram(program);
            }
        }
    }
}
#endif
