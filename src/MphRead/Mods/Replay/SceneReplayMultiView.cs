#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Runtime.CompilerServices;
using System.Linq;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using MphRead.Entities;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead;
public partial class Scene
{
    private sealed class MultiViewPass
    {
        internal int Index;
        internal int Subject;
        internal Vector3 Position,Target,Up;
        internal float Fov;
    }
    private static readonly ConditionalWeakTable<Scene,MultiViewPass> MultiViewPasses=new();
    internal bool NativeReplayMultiViewActive=>MultiViewPasses.TryGetValue(this,out _);
    private bool NativeReplayMultiViewPreserveCanvas=>MultiViewPasses.TryGetValue(this,out var pass)&&pass.Index>0;
    private bool ApplyNativeReplayMultiViewCamera()
    {
        if(!MultiViewPasses.TryGetValue(this,out var pass))return false;
        // The primary tile retains the actual player/free/authored pipeline,
        // including first-person viewmodel and HUD. Skip only replay global requests.
        if(pass.Index==0)return true;
        if(pass.Index==2)
        {
            Players.MainPlayerIndex=pass.Subject;
            _cameraMode=CameraMode.Player;_freeCam=false;
            return true;
        }
        SetReplicaCamera(pass.Position,pass.Target,pass.Fov);
        _cameraFov=MathHelper.DegreesToRadians(pass.Fov);_cameraUp=pass.Up;_cameraRight=Vector3.Cross(_cameraFacing,pass.Up).Normalized();
        _viewMatrix=Matrix4.LookAt(pass.Position,pass.Target,pass.Up);_viewInvRotMatrix=Matrix4.Transpose(_viewMatrix.ClearTranslation());return true;
    }
    private int NativeReplayTargetPovSlot(int primary)
    {
        if(ReplayCombatDiagnostics.Selected is{} selected&&ReplayPoses is{} poses
            &&poses.TryCombatDiagnostic(selected,out var shot)
            &&Services is ReplaySceneServices replay
            &&replay.State.MatchesLife(shot.Fact.VictimSlot,shot.Fact.VictimGeneration,shot.Fact.VictimLifeId)
            &&Players.Items[shot.Fact.VictimSlot].LoadFlags.TestFlag(LoadFlags.Spawned))
            return shot.Fact.VictimSlot;
        // The old fallback is the next slot; skip absent subjects so the tile
        // remains an actual recorded POV instead of an uninitialized camera.
        for(int offset=1;offset<Players.Items.Count;offset++)
        {
            int slot=(primary+offset)%Players.Items.Count;
            if(Players.Items[slot].LoadFlags.TestFlag(LoadFlags.Spawned))return slot;
        }
        return primary;
    }
    /// <summary>Four read-only pictures share this one scene/clock. Camera, target size and GL state are restored even on failure.</summary>
    internal void DrawNativeReplayFourViews(Vector4i bounds,Vector2i framebuffer)
    {
        if(!DemoPlayback.Owns(this)||!ReferenceEquals(DemoPlayback.PresentationScene,this)||ReplayController.IsSeeking
            ||Mods.Replay.ReplayVideoExporter.Active||NativeReplayMultiViewActive||bounds.Z<4||bounds.W<4)return;
        var preview=ReplayPreviewSize;var previewBounds=ReplayPreviewBounds;
        var mode=_cameraMode;var position=_cameraPosition;var facing=_cameraFacing;var up=_cameraUp;var right=_cameraRight;float fov=_cameraFov;
        var view=_viewMatrix;var inv=_viewInvRotMatrix;var invY=_viewInvRotYMatrix;var projection=_perspectiveMatrix;
        var modelProjection=_viewModelPerspectiveMatrix;float modelFov=_viewModelFov;
        int frustumIndex=FrustumInfo.Index,frustumCount=FrustumInfo.Count;var planes=FrustumInfo.Planes.ToArray();
        int subject=Players.MainPlayerIndex;bool free=_freeCam;
        // Decode the successfully rendered primary camera rather than treating
        // the free-camera fields as a first-person pose. Its pipeline stays intact.
        bool primaryDecoded=Mods.Replay.ReplayCameraPresentationMath.TryDecode(view,projection,out var primary);
        var target=Players.Main.Position+Vector3.UnitY;var actorFacing=Players.Main.FacingVector;
        int draw=GL.GetInteger(GetPName.DrawFramebufferBinding),read=GL.GetInteger(GetPName.ReadFramebufferBinding),program=GL.GetInteger(GetPName.CurrentProgram);
        void RestoreCamera()
        {
            _cameraMode=mode;_cameraPosition=position;_cameraFacing=facing;_cameraUp=up;_cameraRight=right;_cameraFov=fov;_freeCam=free;Players.MainPlayerIndex=subject;
            _viewMatrix=view;_viewInvRotMatrix=inv;_viewInvRotYMatrix=invY;_perspectiveMatrix=projection;
            _viewModelPerspectiveMatrix=modelProjection;_viewModelFov=modelFov;
            FrustumInfo.Index=frustumIndex;FrustumInfo.Count=frustumCount;Array.Copy(planes,FrustumInfo.Planes,planes.Length);
        }
        GL.PushAttrib(AttribMask.AllAttribBits);
        var pass=new MultiViewPass{Subject=NativeReplayTargetPovSlot(subject)};MultiViewPasses.Add(this,pass);
        try
        {
            // Keep the existing render targets. Each final blit downsamples into
            // its tile; no resize, target allocation, or simulation step occurs.
            ReplayPreviewSize=framebuffer;
            int leftWidth=bounds.Z/2,topHeight=bounds.W/2;
            for(int index=0;index<4;index++)
            {
                // Restore the primary subject before each tile. In particular,
                // target POV must not become the next tile's or final world's owner.
                RestoreCamera();
                pass.Index=index;pass.Fov=index==0&&primaryDecoded?MathHelper.RadiansToDegrees(primary.Fov):70;
                pass.Position=index switch{0=>primaryDecoded?primary.Position:position,1=>target-actorFacing*5+Vector3.UnitY*2,2=>target+new Vector3(8,4,0),_=>target+new Vector3(0,30,15)};
                pass.Target=index==0?pass.Position+facing:target;pass.Up=index==0?up:Vector3.UnitY;
                bool rightTile=index%2!=0,bottomTile=index/2!=0;
                ReplayPreviewBounds=new(bounds.X+(rightTile?leftWidth:0),bounds.Y+(bottomTile?topHeight:0),
                    rightTile?bounds.Z-leftWidth:leftWidth,bottomTile?bounds.W-topHeight:topHeight);
                OnDrawFrame();OnRenderFrame();
            }
        }
        finally
        {
            RestoreCamera();ReplayPreviewSize=preview;ReplayPreviewBounds=previewBounds;
            try
            {
                // Item collections are recycled for each view, so restore the
                // primary retained world by preparing it again without rendering
                // over the four tiles. Index zero also fences draw-time requests.
                pass.Index=0;OnDrawFrame();
            }
            finally
            {
                RestoreCamera();MultiViewPasses.Remove(this);GL.PopAttrib();
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer,draw);GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,read);GL.UseProgram(program);
            }
        }
    }
}
#endif
