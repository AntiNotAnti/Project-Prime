using System.Reflection;
using MphRead;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using GL = OpenTK.Graphics.OpenGL.GL;

static partial class Program
{
    static int ReceiverProgram(string fragment)
    {
        const string vertex = "#version 120\nvarying vec2 texcoord; varying vec4 color; varying vec3 surface_normal; varying vec3 surface_position; void main(){ gl_Position=gl_Vertex; texcoord=gl_Vertex.xy*.5+.5; color=vec4(.25,.25,.25,1); surface_position=gl_Vertex.xyz; surface_normal=vec3(0,0,1); }";
        int Compile(ShaderType kind, string source)
        {
            int shader = GL.CreateShader(kind); GL.ShaderSource(shader, source); GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
            if (status == 0) throw new Exception(GL.GetShaderInfoLog(shader));
            return shader;
        }
        int vs = Compile(ShaderType.VertexShader, vertex), fs = Compile(ShaderType.FragmentShader, fragment);
        int program = GL.CreateProgram(); GL.AttachShader(program,vs); GL.AttachShader(program,fs); GL.LinkProgram(program);
        GL.DeleteShader(vs); GL.DeleteShader(fs);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
        if (linked == 0) throw new Exception(GL.GetProgramInfoLog(program));
        return program;
    }
    static byte[] ReceiverPicture(int program, RenderItem item, int shadow, int emissive, int cutout,
        bool receive, bool emit = false, bool alphaCutout = false, bool outline = false)
    {
        GL.UseProgram(program);
        void I(string name,int value) => GL.Uniform1(GL.GetUniformLocation(program,name),value);
        I("mat_mode",0); GL.Uniform1(GL.GetUniformLocation(program,"mat_alpha"),item.Alpha);
        I("advanced_materials",emit ? 1 : 0); I("use_emissive_map",emit ? 1 : 0);
        GL.Uniform1(GL.GetUniformLocation(program,"emissive_intensity"),1f);
        I("receive_world_shadow",receive ? 1 : 0); I("world_shadow_tex",4);
        I("emissive_tex",3); I("tex",0); I("use_texture",alphaCutout ? 1 : 0);
        I("player_outline_mask",outline ? 1 : 0);
        GL.Uniform3(GL.GetUniformLocation(program,"player_outline_color"),1f,1f,0f);
        Matrix4 identity = Matrix4.Identity;
        GL.UniformMatrix4(GL.GetUniformLocation(program,"world_shadow_matrix"),false,ref identity);
        GL.Uniform2(GL.GetUniformLocation(program,"world_shadow_texel"),1f/64,1f/64);
        GL.Uniform3(GL.GetUniformLocation(program,"world_shadow_light"),0f,0f,1f);
        GL.ActiveTexture(TextureUnit.Texture4); GL.BindTexture(TextureTarget.Texture2D,shadow);
        GL.ActiveTexture(TextureUnit.Texture3); GL.BindTexture(TextureTarget.Texture2D,emissive);
        GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D,cutout);
        GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.CullFace);
        GL.ColorMask(true,true,true,true); GL.ClearColor(0,0,0,1); GL.Clear(ClearBufferMask.ColorBufferBit);
        if (alphaCutout) { GL.Enable(EnableCap.AlphaTest); GL.AlphaFunc(AlphaFunction.Equal,1f); }
        else GL.Disable(EnableCap.AlphaTest);
        GL.Begin(PrimitiveType.Quads);
        GL.Vertex3(-1f,-1f,0f); GL.Vertex3(1f,-1f,0f); GL.Vertex3(1f,1f,0f); GL.Vertex3(-1f,1f,0f); GL.End();
        byte[] pixels=new byte[32*32*4]; GL.ReadPixels(0,0,32,32,PixelFormat.Rgba,PixelType.UnsignedByte,pixels);
        GL.Disable(EnableCap.AlphaTest);
        return pixels;
    }
    static void ShadowChecks()
    {
        var settings=DesktopGlContext.Settings(true); settings.ClientSize=new Vector2i(32,32); settings.StartVisible=false;
        using var window=new NativeWindow(settings); window.Context.MakeCurrent();
        GL.LoadBindings(new OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext()); GL.Viewport(0,0,32,32);
        string output=Path.GetFullPath("docs/performance/renderer-next/shadow-receiver-images"); Directory.CreateDirectory(output);
        void Save(string name,byte[] pixels)
        {
            using var stream=File.Create(Path.Combine(output,name+".png"));
            ReFuel.Stb.StbImage.FlipVerticallyOnSave=true;
            PngEncoder.Write<byte>(pixels,32,32,ReFuel.Stb.StbiImageFormat.Rgba,stream);
        }
        int Texture(byte[] pixels,int width,int height)
        {
            int id=GL.GenTexture(); GL.BindTexture(TextureTarget.Texture2D,id);
            GL.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Rgba8,width,height,0,PixelFormat.Rgba,PixelType.UnsignedByte,pixels);
            GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)TextureMagFilter.Nearest);
            return id;
        }
        int shadow=Texture(Enumerable.Repeat((byte)64,64*64*4).ToArray(),64,64);
        int emissive=Texture([128,0,0,255],1,1), cutout=Texture([255,255,255,0,255,255,255,255],2,1);
        int canonical=ReceiverProgram(Shaders.FragmentShader), receiver=ReceiverProgram(Shaders.ShadowFragmentShader);
        var item=new RenderItem { Type=RenderItemType.Mesh, Alpha=1, RenderMode=RenderMode.Normal };
        int Pixel(byte[] pixels,int x,int channel=0)=>pixels[(16*32+x)*4+channel];
        byte[] baseline=ReceiverPicture(canonical,item,shadow,emissive,cutout,false);
        byte[] dark=ReceiverPicture(receiver,item,shadow,emissive,cutout,true);
        Check(Pixel(dark,16)<Pixel(baseline,16)-20,"opaque world receiver is shadowed");
        Check(Pixel(dark,0)>Pixel(dark,16),"light-space border fades toward unshadowed range");
        byte[] glowing=ReceiverPicture(receiver,item,shadow,emissive,cutout,true,emit:true);
        Save("original",baseline); Save("opaque-shadow",dark); Save("emissive-shadow",glowing);
        Check(Math.Abs(Pixel(glowing,16)-Pixel(dark,16)-96)<=2,"emissive contribution survives shadow without postmultiplication");
        Check(ShadowReceiverPolicy.Eligible(item),"opaque mesh actor/room receive eligibility");
        foreach (string excluded in new[]{"viewmodel","transparency","decal","effect","outline"})
        {
            item.ViewModel=excluded=="viewmodel"; item.Alpha=excluded=="transparency"?.5f:1;
            item.RenderMode=excluded=="decal"?RenderMode.Decal:RenderMode.Normal;
            item.Type=excluded=="effect"?RenderItemType.Particle:RenderItemType.Mesh;
            item.PlayerOutlineColor=excluded=="outline"?Vector4.One:null;
            Check(!ShadowReceiverPolicy.Eligible(item),excluded+" excluded from shadow receiver");
            byte[] expected=ReceiverPicture(canonical,item,shadow,emissive,cutout,false,outline:excluded=="outline");
            byte[] actual=ReceiverPicture(receiver,item,shadow,emissive,cutout,ShadowReceiverPolicy.Eligible(item),outline:excluded=="outline");
            Check(expected.SequenceEqual(actual),excluded+" pixels preserve canonical shader result");
            Save(excluded,actual);
        }
        item.Type=RenderItemType.Mesh; item.ViewModel=false; item.Alpha=1; item.PlayerOutlineColor=null;
        byte[] clipped=ReceiverPicture(receiver,item,shadow,emissive,cutout,true,alphaCutout:true);
        Check(Pixel(clipped,8)==0 && Pixel(clipped,24)>0,"shadow receiver preserves opaque cutout coverage");
        Save("cutout",clipped);
        item.RenderMode=RenderMode.Translucent;
        Check(ShadowReceiverPolicy.Eligible(item),"alpha-textured mesh opaque portions remain eligible in opaque pass");
        GL.UseProgram(0); GL.DeleteProgram(canonical); GL.DeleteProgram(receiver);
        GL.DeleteTexture(emissive); GL.DeleteTexture(cutout);

        ShadowQuality previous=RenderOptions.Shadows; RenderOptions.Shadows=ShadowQuality.Low;
        Scene scene=Scene.CreateEditorRenderer(new Vector2i(32,32));
        Set(scene,"_shadowDepthTexture",shadow); Set(scene,"_shadowReady",true); Set(scene,"_shadowTargetSize",64);
        int original=Get<int>(scene,"_shaderProgramId"); GL.UseProgram(original);
        GL.StencilMask(0xFF); GL.StencilFunc(StencilFunction.Always,99,0xFF);
        int sentinel=Texture([255,255,255,255],1,1);
        GL.ActiveTexture(TextureUnit.Texture4); GL.BindTexture(TextureTarget.Texture2D,sentinel); GL.ActiveTexture(TextureUnit.Texture3);
        Call(scene,"BeginShadowReceivers");
        Check(Get<bool>(scene,"_shadowReceiverScope"),"actual scene optional receiver program initializes");
        Set(scene,"_shadowReceiverColorPass",true); Call(scene,"SetShadowReceiver",item);
        int current=GL.GetInteger(GetPName.CurrentProgram); int location=GL.GetUniformLocation(current,"receive_world_shadow");
        GL.GetUniform(current,location,out int enabled); Check(enabled==1,"actual world item receives per-draw uniform");
        Set(scene,"_shadowReceiverColorPass",false); Call(scene,"SetShadowReceiver",item); GL.GetUniform(current,location,out enabled);
        Check(enabled==0,"transparent passes cannot inherit opaque cutout reception");
        Set(scene,"_shadowReceiverColorPass",true);
        item.ViewModel=true; Call(scene,"SetShadowReceiver",item); GL.GetUniform(current,location,out enabled);
        Check(enabled==0,"viewmodel receiver uniform cannot inherit previous world item"); item.ViewModel=false;
        Call(scene,"EndShadowReceivers");
        Check(GL.GetInteger(GetPName.CurrentProgram)==original && GL.GetInteger(GetPName.ActiveTexture)==(int)TextureUnit.Texture3,
            "world boundary restores prior program and active texture unit");
        Check(GL.GetInteger(GetPName.StencilWritemask)==0xFF && GL.GetInteger(GetPName.StencilValueMask)==0xFF
            && GL.GetInteger(GetPName.StencilRef)==99,"receiver boundary preserves full native polygon-ID stencil masks/reference");
        GL.ActiveTexture(TextureUnit.Texture4); Check(GL.GetInteger(GetPName.TextureBinding2D)==sentinel,"world boundary restores unit4 binding");
        var menu=typeof(SceneGameState).GetProperty(nameof(SceneGameState.MenuPause))!;
        menu.SetValue(scene.GameState,true); Set(scene,"_cameraMode",CameraMode.Player);
        (Matrix4 pauseView,Matrix4 pauseProjection)=scene.Players.Main.GetPauseMapMatrices();
        Call(scene,"SetPauseMenuUniforms"); Call(scene,"BeginShadowReceivers");
        int pauseProgram=GL.GetInteger(GetPName.CurrentProgram);
        float[] uploaded=new float[16];
        bool MatrixMatches(string name,Matrix4 expected)
        {
            GL.GetUniform(pauseProgram,GL.GetUniformLocation(pauseProgram,name),uploaded);
            return Enumerable.Range(0,16).All(i=>Math.Abs(uploaded[i]-expected[i/4,i%4])<.00001f);
        }
        Check(MatrixMatches("view_mtx",pauseView) && MatrixMatches("proj_mtx",pauseProjection),
            "shadow-enabled pause map preserves its actual native view/orthographic projection");
        Set(scene,"_shadowReceiverColorPass",true); Call(scene,"SetShadowReceiver",item);
        GL.GetUniform(pauseProgram,GL.GetUniformLocation(pauseProgram,"receive_world_shadow"),out enabled);
        Check(enabled==0,"native pause-map UI never receives world shadows");
        Call(scene,"EndShadowReceivers"); menu.SetValue(scene.GameState,false);
        Call(scene,"ReleaseShadowReceivers");
        var allocated=new List<int>();
        OpenGlFailureInjection.ShaderObjectCallback=(stage,id)=> { allocated.Add(id); if(stage=="variant-fragment") throw new OpenGlInjectedFailureException(stage); };
        try { Call(scene,"BeginShadowReceivers"); }
        finally { OpenGlFailureInjection.ShaderObjectCallback=null; }
        Check(!Get<bool>(scene,"_shadowReceiverScope") && GL.GetInteger(GetPName.CurrentProgram)==original
            && allocated.All(id=>!GL.IsShader(id)),"optional shader failure releases pending objects and preserves native program");
        RenderOptions.Shadows=ShadowQuality.Off; Call(scene,"BeginShadowReceivers");
        RenderOptions.Shadows=ShadowQuality.Low; Call(scene,"BeginShadowReceivers");
        Check(Get<bool>(scene,"_shadowReceiverScope"),"Off/lower-quality transition deliberately retries receiver failure");
        int optionalProgram=Get<int>(scene,"_shaderProgramId");
        Call(scene,"EndShadowReceivers"); scene.UnloadGl(); GL.DeleteTexture(sentinel); RenderOptions.Shadows=previous;
        Check(!GL.IsProgram(optionalProgram),"optional receiver program released with its scene owner");
        GL.ActiveTexture(TextureUnit.Texture0);
        Check(GL.GetError()==ErrorCode.NoError,"receiver fixtures preserve valid GL state");
        File.WriteAllText(Path.Combine(output,"receipt.json"),System.Text.Json.JsonSerializer.Serialize(new {
            schema=1,scope="synthetic-material-receivers",platform="Windows",width=32,height=32,
            renderer=GL.GetString(StringName.Renderer),gl=GL.GetString(StringName.Version),mvid=typeof(Scene).Module.ModuleVersionId,
            note="In-memory canonical/candidate pixel comparisons; game-map/motion/dynamic offscreen acceptance remains pending."
        },new System.Text.Json.JsonSerializerOptions { WriteIndented=true }));
        Console.WriteLine("SHADOW RECEIVERS PASS: native material/cutout/exclusion/ownership/failure fixtures; offscreen dynamic caster coverage NOT_RUN");
    }
}
