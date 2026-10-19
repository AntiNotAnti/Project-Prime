#if !MPHREAD_SERVER && !ANDROID
using System;
using System.IO;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead;

public partial class Scene
{
    // Acceptance-only transaction: existing source model/cache/pose owner, one
    // source material. No default draw dispatch, clock, selected Hunter or RHI.
    // Nearest/clamp/no-mip is explicit diagnostic sampling, NOT original GX state.
    internal void DrawSourceMaterial4Fixture(CharacterModelRuntime.Material4FixtureTicket ticket,
        Matrix4 ownerView, Matrix4 ownerProjection)
    {
        ticket.RequireCurrent(this);
        if(DesktopRetainedGeometry.WorldScopeActive)
            throw new InvalidOperationException("Invoke this isolated fixture after the existing retained world scope closes.");
        var facts=OpenGlContextPolicy.Current;
        if(!OperatingSystem.IsMacOS()||facts.IsEs||facts.Major!=2||facts.Has("GL_ARB_sampler_objects"))
            throw new NotSupportedException("Material4 diagnostic is currently scoped to the qualified Mac GL2 compatibility owner without sampler objects.");
        foreach(var matrix in new[]{ownerView,ownerProjection})
            for(int r=0;r<4;r++)for(int c=0;c<4;c++)
                if(!float.IsFinite(matrix[r,c]))throw new InvalidDataException("Finite existing camera owner matrices required.");
        using var input=new MemoryStream(ticket.Recipe.CopyImage(),writable:false);
        var rgba=ModernTextureAsset.Decode(input,"source-material4-fixture",TextureAssetClass.Hunter,TextureAssetChannel.Albedo,128);
        if(rgba.Width!=128||rgba.Height!=64||CharacterSourceMaterial4Recipe.Sha(rgba.Pixels)!="3110464f55c952d8c17800380ad3cc843f339a0ec5c4f9439949ad24ab365ecb")
            throw new InvalidDataException("Decoded source RGBA changed; no alpha overwrite, resize or color transform allowed.");
        int oldProgram=GL.GetInteger(GetPName.CurrentProgram),oldActive=GL.GetInteger(GetPName.ActiveTexture);
        int oldUnpack=GL.GetInteger(GetPName.UnpackAlignment);
        int vertex=0,fragment=0,program=0,texture=0;
        // Existing wrapper invalidates capability/binding/sampler elision around
        // this compatibility scope. Native program uniforms are never modified.
        GL.PushAttrib(AttribMask.AllAttribBits);
        try
        {
            vertex=CompileSceneShader(ShaderType.VertexShader,Shaders.SelectedUnlitVertexShader,"material4-fixture-vertex");
            fragment=CompileSceneShader(ShaderType.FragmentShader,CharacterSourceMaterial4Recipe.FragmentShader,"material4-fixture-fragment");
            program=LinkSceneProgram(vertex,fragment,"material4-fixture-program");
            ticket.RequireCurrent(this);
            GL.UseProgram(program);
            int U(string name)=>GL.GetUniformLocation(program,name);
            foreach(string name in new[]{"proj_mtx","view_mtx","view_inv_mtx","tex_mtx","weighted_skinning","use_texture","texgen_mode","source_tex","mtx_stack"})
                if(U(name)<0)throw new InvalidOperationException("Required material4 shader uniform missing: "+name);
            Matrix4 identity=Matrix4.Identity;
            GL.UniformMatrix4(U("proj_mtx"),false,ref ownerProjection);
            GL.UniformMatrix4(U("view_mtx"),false,ref ownerView);
            GL.UniformMatrix4(U("view_inv_mtx"),false,ref identity);
            GL.UniformMatrix4(U("tex_mtx"),false,ref identity);
            GL.Uniform1(U("weighted_skinning"),1);GL.Uniform1(U("use_texture"),1);
            GL.Uniform1(U("texgen_mode"),0);GL.Uniform1(U("source_tex"),0);
            // New program defaults already neutralize other native flags; make
            // the depth specialization inputs explicit for reproducibility.
            GL.Uniform1(U("depth_only"),0);GL.Uniform1(U("depth_vertex_fast"),0);
            GL.ActiveTexture(TextureUnit.Texture0);
            texture=AllocateTexture();
            if(texture==0){ReleaseTexture(0);throw new InvalidOperationException("Material4 source texture allocation failed.");}
            GL.BindTexture(TextureTarget.Texture2D,texture);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment,4);
            GL.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Rgba8,128,64,0,PixelFormat.Rgba,PixelType.UnsignedByte,rgba.Pixels);
            GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapS,(int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapT,(int)TextureWrapMode.ClampToEdge);
            GL.Enable(EnableCap.Blend);GL.BlendEquation(BlendEquationMode.FuncAdd);
            GL.BlendFunc(BlendingFactor.One,BlendingFactor.One);
            GL.DepthMask(true); // Actual CMDL DepthWrite flag0x80 is set.
            GL.Disable(EnableCap.AlphaTest);GL.ColorMask(true,true,true,true);
            // Existing preview pass supplies depth test/function/culling and
            // viewport. This fixture never asserts they equal original GX state.
            foreach(var segment in ticket.Segments)
            {
                ticket.RequireCurrent(this);
                var palette=segment.CopyPalette();
                GL.UniformMatrix4(U("mtx_stack"),segment.MatrixCount,false,palette);
                GL.CallList(segment.ListId);
            }
            ticket.RequireCurrent(this);
        }
        finally
        {
            // Pop restores unit0's old texture and every changed compatibility
            // render/current attribute. Units1..7 were never touched. UseProgram
            // restores wrapper tracking; temporary uniform/palette caches retire
            // through the existing DeleteProgram owner below.
            GL.PopAttrib();GL.ActiveTexture((TextureUnit)oldActive);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment,oldUnpack);
            GL.UseProgram(oldProgram);
            if(texture!=0)ReleaseTexture(texture);
            if(program!=0)GL.DeleteProgram(program);
            if(fragment!=0)GL.DeleteShader(fragment);
            if(vertex!=0)GL.DeleteShader(vertex);
        }
    }
}
#endif
