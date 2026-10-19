using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.RmlUi.Host;
using OpenTK.Graphics.OpenGL;

/// <summary>Checks the actual static artwork with caller-owned client texture state.</summary>
internal static class StartupTextureStateAcceptance
{
    internal static void Run(RmlUiHost host, RmlUiDocumentToken document, int height, Action frame, Action<bool,string> check)
    {
        check(host.TryGetElementBounds(document,"native_startup_art",out float ax,out float ay,out float aw,out float ah)
            && aw>=32 && ah>=32,"texture-state check has visible authored startup artwork");
        // Sample only the picture interior, clear of the identity and prompt.
        int x=(int)Math.Ceiling(ax+aw*.2f),top=(int)Math.Ceiling(ay+ah*.3f);
        int width=Math.Max(1,(int)(aw*.6f)),cropHeight=Math.Max(1,(int)(ah*.3f));
        int client=GL.GetInteger(GetPName.ClientActiveTexture);
        int alignment=GL.GetInteger(GetPName.PackAlignment);
        int readBuffer=GL.GetInteger(GetPName.ReadBuffer),readFramebuffer=GL.GetInteger(GetPName.ReadFramebufferBinding);
        GL.PushClientAttrib(ClientAttribMask.ClientVertexArrayBit);
        try
        {
            GL.ClientActiveTexture(TextureUnit.Texture0);
            frame();
            byte[] baseline=Read();
            var colors=new HashSet<int>();
            for(int i=0;i<baseline.Length;i+=3) colors.Add((baseline[i]<<16)|(baseline[i+1]<<8)|baseline[i+2]);
            check(colors.Count>=128,"startup pixel baseline contains detailed artwork rather than a flat background");

            // An engine may leave unit zero's array disabled while another client
            // unit is current. The overlay must establish its own coordinates.
            GL.ClientActiveTexture(TextureUnit.Texture0);
            GL.DisableClientState(ArrayCap.TextureCoordArray);
            GL.ClientActiveTexture(TextureUnit.Texture1);
            frame();
            check(GL.GetInteger(GetPName.ClientActiveTexture)==(int)TextureUnit.Texture1,
                "startup draw restores the caller's nonzero client texture unit");
            byte[] contaminated=Read();
            check(baseline.AsSpan().SequenceEqual(contaminated),
                "actual startup artwork pixels survive inherited nonzero client texture state");
        }
        finally
        {
            GL.PopClientAttrib();
            GL.ClientActiveTexture((TextureUnit)client);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,readFramebuffer);
            GL.ReadBuffer((ReadBufferMode)readBuffer);
            GL.PixelStore(PixelStoreParameter.PackAlignment,alignment);
        }

        byte[] Read()
        {
            byte[] pixels=new byte[checked(width*cropHeight*3)];
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,0);
            GL.ReadBuffer(ReadBufferMode.Back);
            GL.PixelStore(PixelStoreParameter.PackAlignment,1);
            GL.ReadPixels(x,height-top-cropHeight,width,cropHeight,PixelFormat.Rgb,PixelType.UnsignedByte,pixels);
            return pixels;
        }
    }
}
