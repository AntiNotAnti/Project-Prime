using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.Network;
using OpenTK.Graphics.OpenGL;

internal sealed partial class ReplayWindowAcceptance
{
    // Inspect the final native composition, after world and UI have both painted.
    // A valid scene receipt alone cannot detect an opaque UI panel over the world.
    private void RequireStudioCompositeVariation(string stage)
    {
        Require(StudioWorkspaceForeground() && !ReplayController.IsSeeking && ReplayController.IsPaused,
            "final Studio pixel check owns a settled paused fixture picture: " + stage);
        var viewport=StudioBounds("replay_viewport");
        int width=_window!.FramebufferSize.X,height=_window.FramebufferSize.Y;
        int insetX=Math.Max(12,(int)Math.Ceiling(viewport.Width*.08)),insetY=Math.Max(12,(int)Math.Ceiling(viewport.Height*.12));
        int x=Math.Clamp((int)Math.Ceiling(viewport.X)+insetX,0,width-1);
        int top=Math.Clamp((int)Math.Ceiling(viewport.Y)+insetY,0,height-1);
        int cropWidth=Math.Min(width-x,(int)Math.Floor(viewport.Width)-2*insetX);
        int cropHeight=Math.Min(height-top,(int)Math.Floor(viewport.Height)-2*insetY);
        Require(cropWidth>=32&&cropHeight>=24,"bounded final viewport pixel interior: "+stage);
        byte[] pixels=new byte[checked(cropWidth*cropHeight*3)];
        int readBuffer=GL.GetInteger(GetPName.ReadBuffer),readFbo=GL.GetInteger(GetPName.ReadFramebufferBinding),alignment=GL.GetInteger(GetPName.PackAlignment);
        try
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,0);GL.ReadBuffer(ReadBufferMode.Front);GL.PixelStore(PixelStoreParameter.PackAlignment,1);
            GL.ReadPixels(x,height-top-cropHeight,cropWidth,cropHeight,PixelFormat.Rgb,PixelType.UnsignedByte,pixels);
        }
        finally
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,readFbo);GL.ReadBuffer((ReadBufferMode)readBuffer);GL.PixelStore(PixelStoreParameter.PackAlignment,alignment);
        }
        var bins=new Dictionary<int,int>();
        for(int i=0;i<pixels.Length;i+=3)
        {int bin=((pixels[i]>>3)<<10)|((pixels[i+1]>>3)<<5)|(pixels[i+2]>>3);bins[bin]=bins.GetValueOrDefault(bin)+1;}
        var dominant=bins.MaxBy(pair=>pair.Value);int r=((dominant.Key>>10)&31)*8+4,g=((dominant.Key>>5)&31)*8+4,b=(dominant.Key&31)*8+4;
        int varied=0,total=pixels.Length/3;
        for(int i=0;i<pixels.Length;i+=3)
            if(Math.Max(Math.Abs(pixels[i]-r),Math.Max(Math.Abs(pixels[i+1]-g),Math.Abs(pixels[i+2]-b)))>12)varied++;
        double variedFraction=varied/(double)total;
        _rml97StudioChecks.Add(new{Feature="actual-final-composite-pixels",Stage=stage,Swap=_observedFrames,Frame=ReplayController.CurrentFrame,
            X=x,Y=top,Width=cropWidth,Height=cropHeight,Pixels=total,Rgb5Bins=bins.Count,DominantPixels=dominant.Value,VariedPixels=varied,VariedFraction=variedFraction});
        Require(bins.Count>=16&&variedFraction>=.02,
            "actual final Studio viewport contains a varied world picture: "+stage+" bins="+bins.Count+" varied="+variedFraction.ToString("P2"));
    }
}
