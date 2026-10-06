#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using ReFuel.Stb;

namespace MphRead.Mods.Render.Characters;

/// <summary>Controlled pixels from the real forward material program, compared with an independent double precision BRDF oracle.</summary>
internal static class CharacterPbrResponseCheck
{
    private const int Width = 64, Height = 64;
    private const int MaximumChannelError = 2;
    private static readonly byte[] Albedo = { 64, 128, 192, 255 };
    private static readonly byte[] Emissive = { 128, 64, 32, 255 };
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed record ProbeCase(string Name, byte Ao, byte Roughness, byte Metallic,
        bool AmbientOnly = false, bool MappedEmission = false);

    internal static int Run(string output)
    {
        string directory = Path.GetFullPath(output);
        Directory.CreateDirectory(directory);
        try
        {
            var tests = new[] {
                new ProbeCase("direct-r02-metal0-ao1",255,51,0),
                new ProbeCase("direct-r08-metal0-ao1",255,204,0),
                new ProbeCase("direct-r02-metal1-ao1",255,51,255),
                new ProbeCase("direct-r08-metal1-ao1",255,204,255),
                new ProbeCase("direct-r02-metal0-ao0",0,51,0),
                new ProbeCase("ambient-metal0-ao1",255,128,0,true),
                new ProbeCase("ambient-metal0-ao0",0,128,0,true),
                new ProbeCase("direct-r02-metal0-emission",255,51,0,false,true) };
            string config = JsonSerializer.Serialize(new {
                width=Width,height=Height,planeZ=-2,planeXY=new[]{-1,1},normal=new[]{0,0,1},
                camera=new[]{0,0,0},orthographicNear=1,orthographicFar=3,
                albedoRgba=Albedo,emissiveRgba=Emissive,materialAlphaMarker=0,
                nativeDiffuse=new[]{1,1,1},nativeEmission=new[]{0,0,0},
                lightColor=new[]{.18,.18,.18},directLightDirection=new[]{-.4,0.0,-1.0},
                ambientOnlyLightDirection=new[]{-1,0,0},nativeAmbientDirect=0,nativeAmbientOnly=.5,
                emissionIntensity=1,emissionScale=.75,maximumChannelError=MaximumChannelError,
                gridPixelCoordinates=new[]{8,16,24,32,40,48,56},tests,
                scope="Actual forward program with normal mapping disabled to isolate BRDF channels. Fixed camera/plane, no game assets or postprocess. CPU oracle uses double precision and the exact quantized input texels."
            },Json);
            File.WriteAllText(Path.Combine(directory,"config.json"),config);
            var settings = DesktopGlContext.Settings(background:true);
            settings.ClientSize=new(Width,Height);
            settings.StartVisible=true; settings.StartFocused=false;
            settings.Title="Project Prime controlled PBR response";
            using var window=new NativeWindow(settings);
            using var graphics=new DesktopGraphicsSession(window);
            DesktopGraphicsSession.Resize(window);
            var identity=ModernGraphicsCompat.Active ? ModernGraphicsCompat.DeviceIdentity
                : (GraphicsBackend.OpenGL,GraphicsApi.GetString(StringName.Renderer),GraphicsApi.GetString(StringName.Version));
            int vertex=0,fragment=0,program=0,target=0,framebuffer=0;
            var textures=new List<int>();
            var frames=new Dictionary<string,byte[]>();
            var evidence=new List<object>();
            int maximumError=0,checkedChannels=0; double squaredError=0;
            try
            {
                vertex=Compile(ShaderType.VertexShader,Shaders.VertexShader);
                fragment=Compile(ShaderType.FragmentShader,Shaders.FragmentShader);
                program=GraphicsApi.CreateProgram();
                GraphicsApi.AttachShader(program,vertex);GraphicsApi.AttachShader(program,fragment);
                GraphicsApi.LinkProgram(program);
                GraphicsApi.GetProgram(program,GetProgramParameterName.LinkStatus,out int linked);
                Require(linked!=0,"Forward shader link failed: "+GraphicsApi.GetProgramInfoLog(program));
                GraphicsApi.UseProgram(program);
                int Uniform(string name)=>GraphicsApi.GetUniformLocation(program,name);
                void Bool(string name,bool value)=>GraphicsApi.Uniform1(Uniform(name),value?1:0);
                foreach(string name in new[]{"use_light","use_texture","advanced_materials","use_specular_map"})Bool(name,true);
                foreach(string name in new[]{"use_normal_map","show_colors","fog_enable","use_override","use_pal_override",
                    "use_flat","player_outline_mask","weighted_skinning"})Bool(name,false);
                foreach(string name in new[]{"mat_mode","texgen_mode","cel_bands","textured_player_skin","cosmetic_skin",
                    "cosmetic_effect","cosmetic_preserve_palette","alpha_test"})GraphicsApi.Uniform1(Uniform(name),0);
                GraphicsApi.Uniform1(Uniform("mat_alpha"),1f);
                GraphicsApi.Uniform1(Uniform("emissive_intensity"),1f);
                GraphicsApi.Uniform1(Uniform("cosmetic_dissolve"),0f);
                GraphicsApi.Uniform3(Uniform("diffuse"),Vector3.One);
                GraphicsApi.Uniform3(Uniform("specular"),Vector3.Zero);
                GraphicsApi.Uniform3(Uniform("emission"),Vector3.Zero);
                GraphicsApi.Uniform3(Uniform("light1col"),new Vector3(.18f));
                GraphicsApi.Uniform3(Uniform("light2col"),Vector3.Zero);
                GraphicsApi.Uniform3(Uniform("light2vec"),-Vector3.UnitX);
                Matrix4 matrix=Matrix4.Identity;
                foreach(string name in new[]{"view_mtx","view_inv_mtx","tex_mtx","mtx_stack"})
                    GraphicsApi.UniformMatrix4(Uniform(name),false,ref matrix);
                matrix=Matrix4.CreateOrthographicOffCenter(-1,1,-1,1,1,3);
                GraphicsApi.UniformMatrix4(Uniform("proj_mtx"),false,ref matrix);
                int[] inputs={Texture(Albedo),Texture(new byte[]{128,128,255,255}),Texture(new byte[]{255,51,0,0}),Texture(Emissive)};
                textures.AddRange(inputs);
                foreach(var pair in new[]{("tex",0),("normal_tex",1),("specular_tex",2),("emissive_tex",3)})
                {
                    GraphicsApi.Uniform1(Uniform(pair.Item1),pair.Item2);
                    GraphicsApi.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0+pair.Item2));
                    GraphicsApi.BindTexture(TextureTarget.Texture2D,inputs[pair.Item2]);
                }
                GraphicsApi.ActiveTexture(TextureUnit.Texture0);
                target=GraphicsApi.GenTexture();GraphicsApi.BindTexture(TextureTarget.Texture2D,target);
                GraphicsApi.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Rgba8,Width,Height,0,
                    PixelFormat.Rgba,PixelType.UnsignedByte,new byte[Width*Height*4]);
                framebuffer=GraphicsApi.GenFramebuffer();GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer,framebuffer);
                GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,FramebufferAttachment.ColorAttachment0,TextureTarget.Texture2D,target,0);
                Require(GraphicsApi.CheckFramebufferStatus(FramebufferTarget.Framebuffer)==FramebufferErrorCode.FramebufferComplete,
                    "PBR response framebuffer incomplete.");
                GraphicsApi.Viewport(0,0,Width,Height);
                foreach(var cap in new[]{EnableCap.Blend,EnableCap.DepthTest,EnableCap.CullFace,EnableCap.ScissorTest,EnableCap.AlphaTest})GraphicsApi.Disable(cap);
                GraphicsApi.ColorMask(true,true,true,true);
                GraphicsApi.BindTexture(TextureTarget.Texture2D,inputs[0]);
                foreach(var test in tests)
                {
                    GraphicsApi.ActiveTexture(TextureUnit.Texture2);GraphicsApi.BindTexture(TextureTarget.Texture2D,inputs[2]);
                    GraphicsApi.TexSubImage2D(TextureTarget.Texture2D,0,0,0,1,1,PixelFormat.Rgba,PixelType.UnsignedByte,
                        new byte[]{test.Ao,test.Roughness,test.Metallic,0});
                    GraphicsApi.ActiveTexture(TextureUnit.Texture0);
                    GraphicsApi.Uniform3(Uniform("ambient"),new Vector3(test.AmbientOnly?.5f:0f));
                    var light=test.AmbientOnly ? -Vector3.UnitX : new Vector3(-.4f,0,-1).Normalized();
                    GraphicsApi.Uniform3(Uniform("light1vec"),light);
                    Bool("use_emissive_map",test.MappedEmission);
                    GraphicsApi.ClearColor(0,0,0,0);GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
                    GraphicsApi.Color4(1f,1f,1f,1f);GraphicsApi.Normal3(0f,0f,1f);
                    GraphicsApi.Begin(PrimitiveType.Quads);
                    foreach(var xy in new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)})
                    {
                        GraphicsApi.TexCoord3(.5f,.5f,0f);GraphicsApi.Vertex3(xy.X,xy.Y,-2f);
                    }
                    GraphicsApi.End();
                    var pixels=new byte[Width*Height*4];GraphicsApi.ReadPixels(0,0,Width,Height,PixelFormat.Rgba,PixelType.UnsignedByte,pixels);
                    frames.Add(test.Name,pixels);
                    int caseError=0;var samples=new List<object>();
                    foreach(int y in new[]{8,16,24,32,40,48,56})foreach(int x in new[]{8,16,24,32,40,48,56})
                    {
                        double[] expected=Oracle(test,x,y);int at=(y*Width+x)*4;
                        for(int channel=0;channel<3;channel++)
                        {
                            int predicted=(int)Math.Round(Math.Clamp(expected[channel],0,1)*255);
                            int delta=Math.Abs(pixels[at+channel]-predicted);
                            caseError=Math.Max(caseError,delta);maximumError=Math.Max(maximumError,delta);
                            squaredError+=delta*delta;checkedChannels++;
                        }
                        Require(pixels[at+3]==255,test.Name+": ORM companion alpha affected draw alpha.");
                        samples.Add(new{x,y,expectedSrgb=expected,actualRgba=pixels.AsSpan(at,4).ToArray()});
                    }
                    Require(caseError<=MaximumChannelError,test.Name+": CPU/GPU BRDF delta "+caseError+" exceeds "+MaximumChannelError+"/255.");
                    string capture=Path.Combine(directory,test.Name+".png");
                    StbImage.FlipVerticallyOnSave=true;
                    using(var file=File.Create(capture))StbImage.WritePng<byte>(pixels,Width,Height,StbiImageFormat.Rgba,file);
                    evidence.Add(new{test.Name,test.Ao,test.Roughness,test.Metallic,test.AmbientOnly,test.MappedEmission,
                        maximumChannelError=caseError,capture=Path.GetFileName(capture),captureSha256=FileHash(capture),samples});
                    Console.WriteLine("PBR RESPONSE case PASS "+test.Name+" delta "+caseError);
                }
                int roughnessDifference=RgbDifference(frames[tests[0].Name],frames[tests[1].Name]);
                int metallicDifference=RgbDifference(frames[tests[0].Name],frames[tests[2].Name]);
                int ambientAoDifference=RgbDifference(frames[tests[5].Name],frames[tests[6].Name]);
                int directAoDifference=RgbDifference(frames[tests[0].Name],frames[tests[4].Name]);
                int emissiveDifference=RgbDifference(frames[tests[0].Name],frames[tests[7].Name]);
                Require(roughnessDifference>=4,"Roughness produced no measurable response.");
                Require(metallicDifference>=4,"Metalness produced no measurable response.");
                Require(ambientAoDifference>=4,"AO produced no ambient response.");
                Require(directAoDifference==0,"AO incorrectly attenuated direct light.");
                Require(emissiveDifference>=4,"Mapped emission produced no measurable response.");
                string? exe=Environment.ProcessPath;
                File.WriteAllText(Path.Combine(directory,"response.json"),JsonSerializer.Serialize(new {
                    pass=true,backend=identity.Item1.ToString(),adapter=identity.Item2,driver=identity.Item3,
                    exePath=exe,exeSha256=exe!=null && File.Exists(exe)?FileHash(exe):null,
                    vertexShaderSha256=StringHash(Shaders.VertexShader),fragmentShaderSha256=StringHash(Shaders.FragmentShader),
                    generatedVertexShaderSha256=ResourceHash("World","vertex"),generatedFragmentShaderSha256=ResourceHash("World","fragment"),
                    configSha256=StringHash(config),Width,Height,checkedChannels,maximumError,
                    rmsError=Math.Sqrt(squaredError/checkedChannels),maximumChannelError=MaximumChannelError,
                    roughnessDifference,metallicDifference,ambientAoDifference,directAoDifference,emissiveDifference,
                    companionAlphaIndependentOfSurfaceOpacity=true,evidence,
                    scope="Actual application forward shader and generated World WGSL on modern backends. Fixed camera/geometry/maps; 49 CPU-oracle sample points per case. Only a 2/255 float/raster/UNORM quantization allowance. Direct-only AO renders must be byte-identical in RGB; native ambient-only AO and linear mapped emission tested separately. Normal map disabled to isolate BRDF. Does not test animation, asset paint fidelity, IBL, Android hardware, display latency or frame pacing."
                },Json));
                File.Delete(Path.Combine(directory,"failure.txt"));
                Console.WriteLine("PBR RESPONSE PASS "+directory);return 0;
            }
            finally
            {
                GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer,0);GraphicsApi.UseProgram(0);
                if(framebuffer!=0)GraphicsApi.DeleteFramebuffer(framebuffer);
                if(target!=0)GraphicsApi.DeleteTexture(target);
                foreach(int texture in textures)GraphicsApi.DeleteTexture(texture);
                if(program!=0)GraphicsApi.DeleteProgram(program);
                if(vertex!=0)GraphicsApi.DeleteShader(vertex);
                if(fragment!=0)GraphicsApi.DeleteShader(fragment);
            }
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(directory,"failure.txt"),ex.ToString());
            File.WriteAllText(Path.Combine(directory,"response.json"),JsonSerializer.Serialize(new{pass=false,error=ex.ToString()},Json));
            Console.Error.WriteLine("PBR RESPONSE FAIL "+ex);return 1;
        }
    }

    private static int Texture(byte[] rgba)
    {
        int texture=GraphicsApi.GenTexture();GraphicsApi.BindTexture(TextureTarget.Texture2D,texture);
        GraphicsApi.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Rgba8,1,1,0,PixelFormat.Rgba,PixelType.UnsignedByte,rgba);
        GraphicsApi.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.Nearest);
        GraphicsApi.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)TextureMagFilter.Nearest);
        GraphicsApi.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapS,(int)TextureWrapMode.ClampToEdge);
        GraphicsApi.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapT,(int)TextureWrapMode.ClampToEdge);
        return texture;
    }
    private static int Compile(ShaderType type,string source)
    {
        int shader=GraphicsApi.CreateShader(type);GraphicsApi.ShaderSource(shader,source);GraphicsApi.CompileShader(shader);
        GraphicsApi.GetShader(shader,ShaderParameter.CompileStatus,out int compiled);
        if(compiled==0){string error=GraphicsApi.GetShaderInfoLog(shader);GraphicsApi.DeleteShader(shader);throw new InvalidOperationException(type+": "+error);}
        return shader;
    }
    private static int RgbDifference(byte[] a,byte[] b)
    {
        int maximum=0;for(int i=0;i<a.Length;i++)if(i%4!=3)maximum=Math.Max(maximum,Math.Abs(a[i]-b[i]));return maximum;
    }
    private static string StringHash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static string FileHash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string ResourceHash(string kind,string stage)
    {
        using var stream=typeof(Shaders).Assembly.GetManifestResourceStream($"MphRead.Mods.Render.Generated.{kind}.{stage}.wgsl")
            ?? throw new InvalidOperationException("Missing generated shader resource.");
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static double Linear(double x)=>x<=.04045?x/12.92:Math.Pow((x+.055)/1.055,2.4);
    private static double Srgb(double x)=>x<=.0031308?x*12.92:1.055*Math.Pow(x,1/2.4)-.055;
    private static double[] Oracle(ProbeCase test,int x,int y)
    {
        double sx=(x+.5)/Width*2-1,sy=(y+.5)/Height*2-1;
        double length=Math.Sqrt(sx*sx+sy*sy+4);
        double vx=-sx/length,vy=-sy/length,vz=2/length;
        // Inputs originate as float uniforms even though the oracle uses doubles.
        var nativeLight=test.AmbientOnly ? -Vector3.UnitX : new Vector3(-.4f,0,-1).Normalized();
        double lx=-nativeLight.X,ly=-nativeLight.Y,lz=-nativeLight.Z;
        double llen=Math.Sqrt(lx*lx+ly*ly+lz*lz);lx/=llen;ly/=llen;lz/=llen;
        double hx=vx+lx,hy=vy+ly,hz=vz+lz,hlen=Math.Sqrt(hx*hx+hy*hy+hz*hz);
        hx/=hlen;hy/=hlen;hz/=hlen;
        double nl=Math.Max(lz,0),nv=Math.Max(vz,0),nh=Math.Clamp(hz,0,1),vh=Math.Clamp(vx*hx+vy*hy+vz*hz,0,1);
        double roughness=Math.Clamp(test.Roughness/255.0,.08,1),metal=test.Metallic/255.0,ao=test.Ao/255.0;
        double a=roughness*roughness,a2=a*a,denominator=nh*nh*(a2-1)+1;
        double distribution=a2/Math.Max(Math.PI*denominator*denominator,.000001);
        double k=(roughness+1)*(roughness+1)/8;
        double geometry=nv/Math.Max(nv*(1-k)+k,.000001)*nl/Math.Max(nl*(1-k)+k,.000001);
        double[] result=new double[3];
        for(int channel=0;channel<3;channel++)
        {
            double albedo=Linear(Albedo[channel]/255.0),f0=.04*(1-metal)+albedo*metal;
            double fresnel=f0+(1-f0)*Math.Pow(1-vh,5);
            double ambientRadiance=(test.AmbientOnly?.5:0)*(double).18f;
            double linear=((1-metal)*albedo+f0*.35)*ambientRadiance*ao;
            if(nl>0 && nv>0)
            {
                double spec=distribution*geometry*fresnel/Math.Max(4*nv*nl,.0001);
                linear+=((1-fresnel)*(1-metal)*albedo/Math.PI+spec)*(double).18f*Math.PI*nl;
            }
            if(test.MappedEmission)linear+=Linear(Emissive[channel]/255.0)*.75;
            result[channel]=Srgb(linear);
        }
        return result;
    }
}
#endif
