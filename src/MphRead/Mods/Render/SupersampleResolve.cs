using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead;

public partial class Scene
{
    private readonly List<(Vector2i Size,int Texture,int Framebuffer)> _supersampleResolveTargets=new();
    // A single bilinear fetch sees only four texels, even at 4x/8x source size.
    // Resolve successive 2:1 stages so all rendered samples contribute, then
    // let the established final compositor resolve the last <=2:1 stage.
    private int ResolveSupersampledCompositeTexture()
    {
        int source=GraphicsCompositeTexture(),read=GraphicsReadFramebuffer();
        Vector2i size=_graphicsOutputReady?_graphicsOutputSize:_targetSize;
        int used=0;
        GL.Disable(EnableCap.ScissorTest);
        while(size.X>Size.X*2 || size.Y>Size.Y*2)
        {
            var next=new Vector2i(Math.Max(Size.X,(size.X+1)/2),Math.Max(Size.Y,(size.Y+1)/2));
            if(used<_supersampleResolveTargets.Count && _supersampleResolveTargets[used].Size!=next)
                ReleaseSupersampleResolveTargets(used);
            if(used==_supersampleResolveTargets.Count)
            {
                if(!CanAllocateTargetBytes((long)next.X*next.Y*4))break;
                int texture=AllocateTexture(),framebuffer=GL.GenFramebuffer();
                GL.BindTexture(TextureTarget.Texture2D,texture);
                GL.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Rgb,next.X,next.Y,0,PixelFormat.Rgb,PixelType.UnsignedByte,IntPtr.Zero);
                GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)TextureMagFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapS,(int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapT,(int)TextureWrapMode.ClampToEdge);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer,framebuffer);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,FramebufferAttachment.ColorAttachment0,TextureTarget.Texture2D,texture,0);
                if(GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer)!=FramebufferErrorCode.FramebufferComplete)
                {GL.DeleteFramebuffer(framebuffer);ReleaseTexture(texture);throw new ProgramException("Supersampling resolve target is incomplete.");}
                _supersampleResolveTargets.Add((next,texture,framebuffer));
            }
            var target=_supersampleResolveTargets[used++];
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,read);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer,target.Framebuffer);
            GL.BlitFramebuffer(0,0,size.X,size.Y,0,0,next.X,next.Y,ClearBufferMask.ColorBufferBit,BlitFramebufferFilter.Linear);
            source=target.Texture;read=target.Framebuffer;size=next;
        }
        // The shadow pass may already have consumed the scene's resolve chain.
        // Keep it for the next frame instead of allocating/deleting it every frame.
        if (!_graphicsOutputReady || (_targetSize.X<=Size.X*2 && _targetSize.Y<=Size.Y*2))
            ReleaseSupersampleResolveTargets(used);
        return source;
    }
    private void ReleaseSupersampleResolveTargets(int keep=0)
    {
        while(_supersampleResolveTargets.Count>keep)
        {
            var target=_supersampleResolveTargets[^1];_supersampleResolveTargets.RemoveAt(_supersampleResolveTargets.Count-1);
            GL.DeleteFramebuffer(target.Framebuffer);ReleaseTexture(target.Texture);
        }
    }
}
