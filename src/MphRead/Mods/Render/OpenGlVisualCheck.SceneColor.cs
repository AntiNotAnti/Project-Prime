#if !ANDROID && !MPHREAD_SERVER
using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using NativeGL = OpenTK.Graphics.OpenGL.GL;

namespace MphRead.Mods.Render;

internal static partial class OpenGlVisualCheck
{
    private static float SceneFogSrgbEncode(float value)
    {
        float c = Math.Clamp(value, 0f, 1f);
        return c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
    }

    private static float SceneFogSrgbDecode(float value)
    {
        float c = Math.Max(value, 0f);
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    private static Vector3 SceneFogOracle(Vector3 surface, Vector3 encodedFog,
        float density, bool linear)
    {
        if (!linear) return surface * (1f - density) + encodedFog * density;
        var encodedSurface = new Vector3(SceneFogSrgbEncode(surface.X),
            SceneFogSrgbEncode(surface.Y), SceneFogSrgbEncode(surface.Z));
        Vector3 fogged = encodedSurface * (1f - density) + encodedFog * density;
        return new Vector3(SceneFogSrgbDecode(fogged.X), SceneFogSrgbDecode(fogged.Y),
            SceneFogSrgbDecode(fogged.Z));
    }

    private static float SceneFogDensity(float depth, float minimum, float maximum)
    {
        if (depth >= maximum) return 1f;
        if (depth > minimum) return (depth - minimum) / (maximum - minimum) * 124f / 128f;
        return 0f;
    }

    private static byte SceneFogByte(float value)
        => (byte)Math.Clamp((int)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f,
            MidpointRounding.AwayFromZero), 0, 255);

    private static void CheckSceneLinearColor(string output)
    {
        Scene.RunInventedSceneColorAcceptance(Require);
        int previous = GraphicsApi.GetInteger(GetPName.DrawFramebufferBinding);
        int linearTexture = 0, encodedTexture = 0, sourceTexture = 0;
        int linearFramebuffer = 0, encodedFramebuffer = 0, depthStencil = 0;
        int world = 0, encode = 0, normal = 0;
        int Program(string vertexSource, string fragmentSource)
        {
            int vertex = GraphicsApi.CreateShader(ShaderType.VertexShader);
            int fragment = GraphicsApi.CreateShader(ShaderType.FragmentShader);
            int program = 0;
            try
            {
                GraphicsApi.ShaderSource(vertex, vertexSource); GraphicsApi.CompileShader(vertex);
                GraphicsApi.GetShader(vertex, ShaderParameter.CompileStatus, out int v);
                Require(v != 0, "linear fixture vertex compiles: " + GraphicsApi.GetShaderInfoLog(vertex));
                GraphicsApi.ShaderSource(fragment, fragmentSource); GraphicsApi.CompileShader(fragment);
                GraphicsApi.GetShader(fragment, ShaderParameter.CompileStatus, out int f);
                Require(f != 0, "linear fixture fragment compiles: " + GraphicsApi.GetShaderInfoLog(fragment));
                program = GraphicsApi.CreateProgram(); GraphicsApi.AttachShader(program, vertex); GraphicsApi.AttachShader(program, fragment);
                GraphicsApi.LinkProgram(program); GraphicsApi.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
                Require(linked != 0, "linear fixture program links: " + GraphicsApi.GetProgramInfoLog(program));
                int ready = program; program = 0; return ready;
            }
            finally
            {
                GraphicsApi.DeleteShader(vertex); GraphicsApi.DeleteShader(fragment);
                if (program != 0) GraphicsApi.DeleteProgram(program);
            }
        }
        void Target(out int texture, out int framebuffer)
        {
            texture = GraphicsApi.GenTexture(); framebuffer = GraphicsApi.GenFramebuffer();
            GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, Width, Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
            Require(GraphicsApi.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete,
                "linear RGBA8 fixture is renderable");
        }
        void Quad(float r = 1, float g = 1, float b = 1, float a = 1)
        {
            GraphicsApi.Color4(r,g,b,a);
            GraphicsApi.Begin(PrimitiveType.Quads);
            GraphicsApi.TexCoord2(0,0); GraphicsApi.Vertex3(-1,-1,0);
            GraphicsApi.TexCoord2(1,0); GraphicsApi.Vertex3(1,-1,0);
            GraphicsApi.TexCoord2(1,1); GraphicsApi.Vertex3(1,1,0);
            GraphicsApi.TexCoord2(0,1); GraphicsApi.Vertex3(-1,1,0);
            GraphicsApi.End();
        }
        void Int(int program, string name, int value) => GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(program,name),value);
        void Float(int program, string name, float value) => GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(program,name),value);
        void Float4(int program, string name, Vector4 value) => GraphicsApi.Uniform4(GraphicsApi.GetUniformLocation(program,name),value);
        void Matrix(int program, string name, Matrix4 value) => GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(program,name),false,ref value);
        void World(bool linear)
        {
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, linearFramebuffer);
            ResetPass(); GraphicsApi.Disable(EnableCap.DepthTest); GraphicsApi.Disable(EnableCap.CullFace);
            GraphicsApi.UseProgram(world);
            Int(world,"scene_linear_color",linear?1:0); Int(world,"show_colors",1);
            Int(world,"fog_enable",0);
            Int(world,"use_texture",0); Int(world,"use_light",0); Int(world,"depth_only",0);
            Int(world,"authored_alpha_enabled",0); Int(world,"authored_linear_tint",0);
            Int(world,"authored_alpha_mode",0);
            Float(world,"mat_alpha",1); Float(world,"authored_alpha_factor",1);
            Matrix(world,"proj_mtx",Matrix4.Identity); Matrix(world,"view_mtx",Matrix4.Identity);
            Matrix(world,"view_inv_mtx",Matrix4.Identity); Matrix(world,"mtx_stack[0]",Matrix4.Identity);
        }
        byte[] Encoded(string name)
        {
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, encodedFramebuffer);
            GraphicsApi.UseProgram(encode); GraphicsApi.Disable(EnableCap.Blend);
            GraphicsApi.ActiveTexture(TextureUnit.Texture0); GraphicsApi.BindTexture(TextureTarget.Texture2D,linearTexture);
            Int(encode,"tex",0); Quad();
            return Snapshot(output,name);
        }
        byte[] FogPicture(string name, bool linear, bool fogEnabled, float depth,
            float fogMinimum, float fogMaximum, Vector3 surface, Vector4 fog, float materialAlpha)
        {
            World(linear);
            Int(world,"authored_linear_tint",linear?1:0);
            Int(world,"fog_enable",fogEnabled?1:0);
            Int(world,"use_texture",0);
            Float(world,"mat_alpha",materialAlpha);
            Float(world,"fog_min",fogMinimum); Float(world,"fog_max",fogMaximum);
            Float4(world,"fog_color",fog);
            float clipDepth = depth * 2f - 1f;
            GraphicsApi.Color4(surface.X,surface.Y,surface.Z,1f);
            GraphicsApi.Begin(PrimitiveType.Quads);
            GraphicsApi.Vertex3(-1,-1,clipDepth); GraphicsApi.Vertex3(1,-1,clipDepth);
            GraphicsApi.Vertex3(1,1,clipDepth); GraphicsApi.Vertex3(-1,1,clipDepth);
            GraphicsApi.End();
            return Snapshot(output,name);
        }
        void FogPixelMatches(byte[] frame, Vector3 expectedRgb, float expectedAlpha, string label)
        {
            PixelIs(frame,Width/2,Width/2,(r,g,b)=>Math.Abs(r-SceneFogByte(expectedRgb.X))<=2
                && Math.Abs(g-SceneFogByte(expectedRgb.Y))<=2 && Math.Abs(b-SceneFogByte(expectedRgb.Z))<=2,
                label+" matches independent CPU color-space oracle");
            int offset=(Width/2*Width+Width/2)*4+3;
            Require(Math.Abs(frame[offset]-SceneFogByte(expectedAlpha))<=1,
                label+" preserves surface alpha independently of fog alpha");
        }
        try
        {
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            Target(out linearTexture,out linearFramebuffer); Target(out encodedTexture,out encodedFramebuffer);
            depthStencil=GraphicsApi.GenRenderbuffer(); GraphicsApi.BindRenderbuffer(RenderbufferTarget.Renderbuffer,depthStencil);
            GraphicsApi.RenderbufferStorage(RenderbufferTarget.Renderbuffer,RenderbufferStorage.Depth24Stencil8,Width,Height);
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer,linearFramebuffer);
            GraphicsApi.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,FramebufferAttachment.DepthStencilAttachment,RenderbufferTarget.Renderbuffer,depthStencil);
            Require(GraphicsApi.CheckFramebufferStatus(FramebufferTarget.Framebuffer)==FramebufferErrorCode.FramebufferComplete,
                "alpha fixture has actual depth/stencil storage");
            world=Program(Shaders.VertexShader,Shaders.FragmentShader);
            encode=Program(SceneColorEncodeShader.Vertex,SceneColorEncodeShader.Fragment);
            normal=Program(Shaders.VertexShader,"#version 120\nvarying vec3 surface_normal;\nvoid main(){gl_FragColor=vec4(surface_normal*0.5+0.5,1.0);}");
            World(true); Quad(.5f,.5f,.5f);
            byte[] stored=Snapshot(output,"linear-vertex-working");
            PixelIs(stored,32,32,(r,g,b)=>Math.Abs(r-55)<=1 && r==g && g==b,"vertex RGB decodes before interpolation/lighting");
            byte[] roundtrip=Encoded("linear-vertex-output");
            PixelIs(roundtrip,32,32,(r,g,b)=>Math.Abs(r-128)<=2,"single IEC encode restores authoring gray");
            World(false); Quad(.5f,.5f,.5f);
            byte[] legacy=Snapshot(output,"linear-legacy-control");
            PixelIs(legacy,32,32,(r,g,b)=>Math.Abs(r-128)<=1,"disabled branch retains native encoded gray");
            World(true); Int(world,"authored_linear_tint",1); Quad(.5f,.5f,.5f);
            byte[] linearTint=Snapshot(output,"linear-gltf-tint-working");
            PixelIs(linearTint,32,32,(r,g,b)=>Math.Abs(r-128)<=1,"linear glTF baseColorFactor bypasses native sRGB vertex decode");
            byte[] encodedTint=Encoded("linear-gltf-tint-output");
            PixelIs(encodedTint,32,32,(r,g,b)=>Math.Abs(r-188)<=2,"linear glTF factor encodes once at the final scene boundary");
            World(true); Int(world,"authored_linear_tint",1); Int(world,"use_light",1);
            GraphicsApi.Normal3(0,0,1);
            GraphicsApi.Uniform3(GraphicsApi.GetUniformLocation(world,"diffuse"),new Vector3(.5f));
            GraphicsApi.Uniform3(GraphicsApi.GetUniformLocation(world,"ambient"),new Vector3(.25f));
            GraphicsApi.Uniform3(GraphicsApi.GetUniformLocation(world,"specular"),Vector3.Zero);
            GraphicsApi.Uniform3(GraphicsApi.GetUniformLocation(world,"emission"),Vector3.Zero);
            GraphicsApi.Uniform3(GraphicsApi.GetUniformLocation(world,"light1vec"),-Vector3.UnitZ);
            GraphicsApi.Uniform3(GraphicsApi.GetUniformLocation(world,"light1col"),Vector3.One);
            GraphicsApi.Uniform3(GraphicsApi.GetUniformLocation(world,"light2col"),Vector3.Zero);
            Quad(.5f,.5f,.5f);
            byte[] litTint=Snapshot(output,"linear-gltf-lit-tint-working");
            PixelIs(litTint,32,32,(r,g,b)=>Math.Abs(r-191)<=2,"linear glTF diffuse and matching ambient are not decoded twice");
            World(false); Int(world,"authored_linear_tint",1); Quad(.5f,.5f,.5f);
            byte[] fallbackTint=Snapshot(output,"linear-gltf-native-fallback-tint");
            PixelIs(fallbackTint,32,32,(r,g,b)=>Math.Abs(r-128)<=1,"native color fallback retains authored factor values");
            World(true); GraphicsApi.Enable(EnableCap.Blend);
            GraphicsApi.BlendFunc(BlendingFactor.SrcAlpha,BlendingFactor.OneMinusSrcAlpha);
            Float(world,"mat_alpha",.5f); Quad();
            byte[] blend=Encoded("linear-half-white-blend");
            PixelIs(blend,32,32,(r,g,b)=>Math.Abs(r-188)<=2,"half white blends in linear light before output encoding");

            sourceTexture=GraphicsApi.GenTexture(); GraphicsApi.BindTexture(TextureTarget.Texture2D,sourceTexture);
            byte[] source={0,0,0,255,255,255,255,255};
            NativeGL.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Srgb8Alpha8,2,1,0,PixelFormat.Rgba,PixelType.UnsignedByte,source);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.Linear);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)TextureMagFilter.Linear);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapS,(int)TextureWrapMode.ClampToEdge);
            World(true); Int(world,"use_texture",1); Int(world,"tex",0);
            // Constant middle coordinate proves hardware decodes each texel
            // before bilinear filtering, including mixed native/HD variants.
            Matrix4 middle=Matrix4.CreateScale(0,0,1)*Matrix4.CreateTranslation(.5f,.5f,0);
            Matrix(world,"tex_mtx",middle);
            GraphicsApi.BindTexture(TextureTarget.Texture2D,sourceTexture); Quad();
            byte[] filtered=Encoded("linear-srgb-texel-filter");
            PixelIs(filtered,32,32,(r,g,b)=>Math.Abs(r-188)<=2,"sRGB sampler filters decoded texels");

            Vector3 fogSurface = new(.15f,.43f,.82f);
            Vector4 nativeFog = new(.91f,.09f,.38f,.12f);
            const float fogMaterialAlpha = .37f;
            foreach (float fogDepth in new[] { .25f,.5f,.75f })
            {
                byte[] fogRamp = FogPicture("linear-native-fog-ramp-"+fogDepth.ToString("0.00"),
                    linear:true,fogEnabled:true,depth:fogDepth,fogMinimum:0f,fogMaximum:1f,
                    surface:fogSurface,fog:nativeFog,materialAlpha:fogMaterialAlpha);
                float density = SceneFogDensity(fogDepth,0f,1f);
                FogPixelMatches(fogRamp,SceneFogOracle(fogSurface,
                    new Vector3(nativeFog.X,nativeFog.Y,nativeFog.Z),density,linear:true),
                    fogMaterialAlpha,"native-authored fog ramp at depth "+fogDepth.ToString("0.00"));
            }
            byte[] zeroFogBaseline = FogPicture("linear-fog-zero-baseline",linear:true,
                fogEnabled:false,depth:.2f,fogMinimum:.25f,fogMaximum:1f,
                surface:fogSurface,fog:nativeFog,materialAlpha:fogMaterialAlpha);
            byte[] zeroFog = FogPicture("linear-fog-zero-density",linear:true,
                fogEnabled:true,depth:.2f,fogMinimum:.25f,fogMaximum:1f,
                surface:fogSurface,fog:nativeFog,materialAlpha:fogMaterialAlpha);
            Require(zeroFog.SequenceEqual(zeroFogBaseline),
                "zero-density linear fog leaves the surface byte-identical without an encode/decode roundtrip");
            byte[] fullFog = FogPicture("linear-fog-full-density",linear:true,
                fogEnabled:true,depth:.75f,fogMinimum:0f,fogMaximum:.75f,
                surface:fogSurface,fog:nativeFog,materialAlpha:fogMaterialAlpha);
            FogPixelMatches(fullFog,SceneFogOracle(fogSurface,
                new Vector3(nativeFog.X,nativeFog.Y,nativeFog.Z),1f,linear:true),
                fogMaterialAlpha,"full-density native-authored fog endpoint");
            Vector3 nativeSurface = new(.18f,.46f,.73f);
            byte[] nativeFogGuard = FogPicture("native-fog-original-arithmetic-guard",linear:false,
                fogEnabled:true,depth:.5f,fogMinimum:0f,fogMaximum:1f,
                surface:nativeSurface,fog:nativeFog,materialAlpha:fogMaterialAlpha);
            FogPixelMatches(nativeFogGuard,SceneFogOracle(nativeSurface,
                new Vector3(nativeFog.X,nativeFog.Y,nativeFog.Z),
                SceneFogDensity(.5f,0f,1f),linear:false),
                fogMaterialAlpha,"Original/native fog arithmetic guard");

            // Production color and early-depth paths must share authored
            // coverage, independently from optional color-target availability.
            void AlphaCase(string name, bool linear, bool enabled, int mode, byte textureAlpha,
                float factor, float actorAlpha, float cutoff, bool depthOnly, bool covered, int expectedRed=255)
            {
                World(linear);
                Int(world,"authored_alpha_enabled",enabled?1:0); Int(world,"authored_alpha_mode",mode);
                Float(world,"authored_alpha_factor",factor); Float(world,"authored_alpha_cutoff",cutoff);
                Float(world,"mat_alpha",actorAlpha); Int(world,"depth_only",depthOnly?1:0);
                Int(world,"use_texture",1); Int(world,"tex",0); Matrix(world,"tex_mtx",Matrix4.Identity);
                GraphicsApi.ActiveTexture(TextureUnit.Texture0); GraphicsApi.BindTexture(TextureTarget.Texture2D,sourceTexture);
                NativeGL.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Rgba8,1,1,0,PixelFormat.Rgba,PixelType.UnsignedByte,
                    new byte[]{255,255,255,textureAlpha});
                GraphicsApi.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.Nearest);
                GraphicsApi.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)TextureMagFilter.Nearest);
                GraphicsApi.Enable(EnableCap.DepthTest); GraphicsApi.DepthMask(true);
                if(mode==3)
                {
                    GraphicsApi.Enable(EnableCap.Blend);
                    GraphicsApi.BlendFunc(BlendingFactor.SrcAlpha,BlendingFactor.OneMinusSrcAlpha);
                }
                else
                {
                    GraphicsApi.Enable(EnableCap.AlphaTest); GraphicsApi.AlphaFunc(AlphaFunction.Equal,1f);
                }
                GraphicsApi.ColorMask(!depthOnly,!depthOnly,!depthOnly,!depthOnly); Quad();
                GraphicsApi.ColorMask(true,true,true,true);
                float[] depthSample=new float[1];
                NativeGL.ReadPixels(32,32,1,1,PixelFormat.DepthComponent,PixelType.Float,depthSample);
                Require(covered?Math.Abs(depthSample[0]-.5f)<.0001f:Math.Abs(depthSample[0]-1f)<.0001f,
                    name+": production fragment discard agrees with depth coverage");
                if(!depthOnly)
                {
                    byte[] pixels=Snapshot(output,name);
                    PixelIs(pixels,32,32,(r,g,b)=>Math.Abs(r-(covered?expectedRed:0))<=2,
                        name+": production color coverage/alpha matches authored policy");
                }
            }
            AlphaCase("alpha-opaque-zero-texture-factor",true,true,1,0,0,1,.5f,false,true);
            AlphaCase("alpha-opaque-zero-depth",true,true,1,0,0,1,.5f,true,true);
            AlphaCase("alpha-mask-below",true,true,2,254,.5f,1,.5f,false,false);
            AlphaCase("alpha-mask-equal",true,true,2,255,.5f,1,.5f,false,true);
            AlphaCase("alpha-mask-below-depth",true,true,2,254,.5f,1,.5f,true,false);
            AlphaCase("alpha-mask-equal-depth",true,true,2,255,.5f,1,.5f,true,true);
            AlphaCase("alpha-blend-factor-actor",true,true,3,128,.5f,.5f,.5f,false,true,32);
            AlphaCase("alpha-fallback-opaque",false,true,1,0,0,1,.5f,false,true);
            AlphaCase("alpha-fallback-mask-below",false,true,2,254,.5f,1,.5f,true,false);
            AlphaCase("alpha-fallback-mask-equal",false,true,2,255,.5f,1,.5f,true,true);
            AlphaCase("alpha-fallback-blend",false,true,3,128,.5f,.5f,.5f,false,true,32);
            AlphaCase("alpha-original-guard",false,false,1,0,0,1,.5f,false,false);

            // Guarded inverse transpose is production vertex code, not a CPU
            // mirror. Reflecting X must preserve its determinant sign.
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer,linearFramebuffer);
            ResetPass(); GraphicsApi.Disable(EnableCap.DepthTest); GraphicsApi.Disable(EnableCap.CullFace);
            GraphicsApi.UseProgram(normal); Int(normal,"scene_linear_color",1); Int(normal,"show_colors",1);
            Matrix(normal,"proj_mtx",Matrix4.Identity); Matrix(normal,"view_mtx",Matrix4.Identity); Matrix(normal,"view_inv_mtx",Matrix4.Identity);
            Matrix(normal,"mtx_stack[0]",Matrix4.CreateScale(2,1,1));
            GraphicsApi.Normal3(1,1,0); Quad();
            byte[] scaled=Snapshot(output,"linear-normal-nonuniform");
            PixelIs(scaled,32,32,(r,g,b)=>Math.Abs(r-185)<=2 && Math.Abs(g-242)<=2 && Math.Abs(b-128)<=1,
                "nonuniform scale uses inverse-transpose normal");
            Matrix(normal,"mtx_stack[0]",Matrix4.CreateScale(-2,1,1)); Quad();
            byte[] reflected=Snapshot(output,"linear-normal-reflection");
            PixelIs(reflected,32,32,(r,g,b)=>Math.Abs(r-70)<=2 && Math.Abs(g-242)<=2,"negative determinant retains reflected normal direction");
            Matrix(normal,"mtx_stack[0]",Matrix4.CreateScale(0,1,1)); Quad();
            Require(GraphicsApi.GetError()==ErrorCode.NoError,"linear color/filter/blend/normal fixtures produce no GL errors");
        }
        finally
        {
            GraphicsApi.UseProgram(0); GraphicsApi.BindTexture(TextureTarget.Texture2D,0);
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer,previous);
            foreach(int value in new[]{world,encode,normal}) if(value!=0)GraphicsApi.DeleteProgram(value);
            if(depthStencil!=0)GraphicsApi.DeleteRenderbuffer(depthStencil);
            foreach(int value in new[]{linearFramebuffer,encodedFramebuffer}) if(value!=0)GraphicsApi.DeleteFramebuffer(value);
            foreach(int value in new[]{linearTexture,encodedTexture,sourceTexture}) if(value!=0)GraphicsApi.DeleteTexture(value);
        }
    }
}
#endif
