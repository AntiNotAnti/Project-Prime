#if MPHREAD_STUDIO
using System;
using MphRead.Mods.Replay;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
namespace MphRead.Mods.StudioReplay;

/// <summary>A presentation target, never a second simulation or reader.</summary>
internal sealed class StudioReplayViewTarget : IDisposable
{
    private int _framebuffer,_texture;
    private Vector2i _size;
    internal void Render(Scene scene,ReplayCameraTrack track,double frame,float alpha,ReplayCameraMode mode,int slot,bool hud,Vector2i size)
    {
        if(_size!=size||_framebuffer==0)
        {
            Dispose();_size=size;
            _texture=GL.GenTexture();GL.BindTexture(TextureTarget.Texture2D,_texture);
            GL.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Rgb,size.X,size.Y,0,PixelFormat.Rgb,PixelType.UnsignedByte,IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)TextureMagFilter.Linear);
            _framebuffer=GL.GenFramebuffer();GL.BindFramebuffer(FramebufferTarget.Framebuffer,_framebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,FramebufferAttachment.ColorAttachment0,TextureTarget.Texture2D,_texture,0);
            if(GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer)!=FramebufferErrorCode.FramebufferComplete)throw new InvalidOperationException("Replay comparison target is incomplete.");
        }
        if(scene.Size!=size){scene.Size=size;scene.OnResize();}
        scene.StudioOutputFramebuffer=_framebuffer;scene.StudioReplayGameHud=hud;
        scene.StudioReplayPrepare(track,frame,alpha,mode,slot);scene.OnDrawFrame();scene.OnRenderFrame();
    }
    internal void Blit(int destination,int x,int y,int width,int height)
    {
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,_framebuffer);GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer,destination);
        GL.BlitFramebuffer(0,0,_size.X,_size.Y,x,y,x+width,y+height,ClearBufferMask.ColorBufferBit,BlitFramebufferFilter.Linear);
    }
    public void Dispose()
    {if(_framebuffer!=0)GL.DeleteFramebuffer(_framebuffer);if(_texture!=0)GL.DeleteTexture(_texture);_framebuffer=_texture=0;}
}

#endif
