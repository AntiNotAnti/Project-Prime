using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.MapEditor;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Materials;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;
using SkiaSharp;
using N = System.Numerics;
using G = MphRead.Mods.Render.GraphicsApi;

internal static class PixelCheck
{
    internal static void Run(string backend, string output)
    {
        string pack = TextureReplacementPack.Root;
        if (Directory.Exists(pack)) throw new IOException("Pixel check requires an unused test-output pack directory.");
        Directory.CreateDirectory(pack);
        try
        {
            string empty = Path.Combine(pack, "empty-maps"); Directory.CreateDirectory(empty);
            MphRead.Mods.MapGen.CustomRooms.MapDirectory = empty;
            MphRead.Mods.MapGen.CustomRooms.UserMapDirectory = empty;
            GraphicsBackendPolicy.Configure(backend);
            var settings = DesktopGlContext.Settings(background: true); settings.ClientSize = new(96,96);
            using var window = new NativeWindow(settings);
            using var session = new DesktopGraphicsSession(window);
            bool modern = ModernGraphicsCompat.Active;
            string version = G.GetString(StringName.Version);
            Check(backend.Equals("opengl",StringComparison.OrdinalIgnoreCase) ? !modern
                : modern && version.Contains(backend,StringComparison.OrdinalIgnoreCase), "requested backend executes without fallback");
            var results = new Dictionary<string, byte[]>();
            var alpha = new Dictionary<string, byte>();
            var scene = Scene.CreateEditorRenderer(new(96,96));
            int target=G.GenFramebuffer(), color=G.GenTexture(), depth=G.GenRenderbuffer();
            G.BindTexture(TextureTarget.Texture2D,color);
            G.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Rgba,96,96,0,PixelFormat.Rgba,PixelType.UnsignedByte,new byte[96*96*4]);
            G.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.Nearest);
            G.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)TextureMagFilter.Nearest);
            G.BindFramebuffer(FramebufferTarget.Framebuffer,target);
            G.FramebufferTexture2D(FramebufferTarget.Framebuffer,FramebufferAttachment.ColorAttachment0,TextureTarget.Texture2D,color,0);
            G.BindRenderbuffer(RenderbufferTarget.Renderbuffer,depth);
            G.RenderbufferStorage(RenderbufferTarget.Renderbuffer,RenderbufferStorage.DepthComponent24,96,96);
            G.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,FramebufferAttachment.DepthAttachment,RenderbufferTarget.Renderbuffer,depth);
            Check(G.CheckFramebufferStatus(FramebufferTarget.Framebuffer)==FramebufferErrorCode.FramebufferComplete,"RGBA/depth target complete");
            G.BindTexture(TextureTarget.Texture2D,0);
            try
            {
                MaterialImage Map(string name, byte r, byte g, byte b)
                {
                    string path = Path.Combine(pack, name + ".png");
                    using var bitmap = new SKBitmap(1,1); bitmap.SetPixel(0,0,new SKColor(r,g,b,255));
                    using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png,100);
                    using var stream = File.Create(path); data.SaveTo(stream); stream.Close();
                    return MaterialPack.ValidateImage(path);
                }
                var flat = Map("flat",128,128,255); var tilted = Map("tilted",92,247,104);
                var noSpec = Map("no-spec",0,255,0); var glossy = Map("glossy",255,0,0); var rough = Map("rough",255,255,0);
                var emission = Map("emission",0,128,0);
                var key = MaterialAssetKey.Authored(Guid.NewGuid(),Guid.NewGuid());
                var baseMaterial = new ResolvedMaterial(key,null,flat,noSpec,null);
                var gray = new[] { new ColorRgba((byte)128,(byte)128,(byte)128,(byte)255) };
                var id = Guid.NewGuid();
                var face = new MapViewportFace(id, new[] { new N.Vector3(-2,-2,0),new N.Vector3(2,-2,0),new N.Vector3(2,2,0),new N.Vector3(-2,2,0) },
                    1,0,true,Texcoords:new[] { new N.Vector2(0,0),new N.Vector2(2,0),new N.Vector2(2,2),new N.Vector2(0,2) });
                var mesh = new MapViewportMesh(id,new[] { face });
                var frame = new MapRenderFrame(new(96,96),new(new N.Vector3(0,0,5),N.Vector3.Zero,true),
                    new[] { mesh },new HashSet<Guid>(),new Dictionary<Guid,N.Matrix4x4>(),false,false);
                void Render(string name, ColorRgba[] pixels, int width, int height, ResolvedMaterial? enhanced,
                    int uvWidth = 2, int uvHeight = 2, bool filter = false, bool mipmap = false)
                {
                    RenderOptions.TextureFiltering = filter; RenderOptions.TextureMipmaps = mipmap;
                    var texture = new MapViewportMaterial(name,width,height,pixels,uvWidth,uvHeight,enhanced);
                    frame = frame with { Materials = new Dictionary<(bool,int),MapViewportMaterial> { [(false,0)] = texture } };
                    G.BindFramebuffer(FramebufferTarget.Framebuffer,target);
                    scene.DrawEditorFrame(frame,new(96,96));
                    G.BindFramebuffer(FramebufferTarget.ReadFramebuffer,target);
                    G.PixelStore(PixelStoreParameter.PackAlignment,1);
                    byte[] rgb = new byte[96*96*3]; G.ReadPixels(0,0,96,96,PixelFormat.Rgb,PixelType.UnsignedByte,rgb);
                    results[name]=rgb;
                    var rgba = new byte[4]; G.ReadPixels(43,43,1,1,PixelFormat.Rgba,PixelType.UnsignedByte,rgba);
                    alpha[name]=rgba[3];
                    if(G.GetError()!=ErrorCode.NoError) throw new Exception("GPU error: " + name);
                    Console.WriteLine($"PIXEL {name}: {string.Join(',', Sample(rgb))}");
                }
                Render("flat",gray,1,1,baseMaterial);
                Render("normal",gray,1,1,baseMaterial with { Normal=tilted });
                Render("glossy",gray,1,1,baseMaterial with { SpecularRoughness=glossy });
                Render("rough",gray,1,1,baseMaterial with { SpecularRoughness=rough });
                Render("emissive",gray,1,1,baseMaterial with { Emissive=emission });
                Render("alpha-zero",new[] { new ColorRgba((byte)255,(byte)0,(byte)0,(byte)0) },1,1,null);
                Render("alpha-half",new[] { new ColorRgba((byte)255,(byte)0,(byte)0,(byte)128) },1,1,null);
                var pattern = new[] { new ColorRgba((byte)255,(byte)0,(byte)0,(byte)255),new ColorRgba((byte)0,(byte)255,(byte)0,(byte)255),
                    new ColorRgba((byte)0,(byte)0,(byte)255,(byte)255),new ColorRgba((byte)255,(byte)255,(byte)255,(byte)255) };
                Render("uv-nearest",pattern,2,2,null);
                Render("uv-linear",pattern,2,2,null,filter:true);
                Render("uv-repeat",pattern,2,2,null,uvWidth:1,uvHeight:1);
                var checker = Enumerable.Range(0,64*64).Select(i => ((i%64+i/64)&1)==0
                    ? new ColorRgba((byte)255,(byte)255,(byte)255,(byte)255) : new ColorRgba((byte)0,(byte)0,(byte)0,(byte)255)).ToArray();
                Render("mip-off",checker,64,64,null,uvWidth:1,uvHeight:1,filter:true);
                Render("mip-on",checker,64,64,null,uvWidth:1,uvHeight:1,filter:true,mipmap:true);
                byte[] baseline=Sample(results["flat"]), normal=Sample(results["normal"]), roughPixel=Sample(results["rough"]), glossPixel=Sample(results["glossy"]), emit=Sample(results["emissive"]);
                Check(Math.Abs(normal[0]-baseline[0])>=2,"normal channel changes lighting");
                Check(roughPixel[0]>glossPixel[0]+2,"green roughness changes highlight width");
                Check(roughPixel[0]>baseline[0]+2,"red specular controls highlight strength");
                Check(emit[1]>baseline[1]+70&&Math.Abs(emit[0]-baseline[0])<=2,"green emission adds only green");
                Check(alpha["alpha-zero"]==0&&Math.Abs(alpha["alpha-half"]-128)<=1,"shared shader preserves zero and partial texture alpha");
                Check(!results["mip-on"].SequenceEqual(results["mip-off"]),"mipmap sampler changes minified checker pixels");
                int Transitions(byte[] pixels) => Enumerable.Range(21,55).Count(x =>
                    !pixels.AsSpan((43*96+x)*3,3).SequenceEqual(pixels.AsSpan((43*96+x-1)*3,3)));
                Check(Transitions(results["uv-repeat"])>=Transitions(results["uv-nearest"])+2,"UV repeat adds repeated texel transitions");
                Check(!results["uv-linear"].SequenceEqual(results["uv-nearest"]),"linear sampler changes interpolated UV pixels");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                File.WriteAllText(output,JsonSerializer.Serialize(new { Backend=backend, Renderer=G.GetString(StringName.Renderer), Version=version, Modern=modern, Width=96,Height=96,Alpha=alpha,Images=results }));
                Console.WriteLine($"MATERIAL PIXELS PASS actual={G.GetString(StringName.Renderer)} {version} output={output}");
            }
            finally { scene.UnloadGl(); G.BindFramebuffer(FramebufferTarget.Framebuffer,0); G.DeleteFramebuffer(target); G.DeleteTexture(color); G.DeleteRenderbuffer(depth); }
        }
        finally { Directory.Delete(pack,true); }
    }
    internal static void Compare(string first, string second)
    {
        using var a = JsonDocument.Parse(File.ReadAllText(first)); using var b = JsonDocument.Parse(File.ReadAllText(second));
        var left = a.RootElement; var right = b.RootElement;
        Check(left.GetProperty("Width").GetInt32()==96 && left.GetProperty("Height").GetInt32()==96
            && right.GetProperty("Width").GetInt32()==96 && right.GetProperty("Height").GetInt32()==96,"matching pixel fixture dimensions");
        Check(left.GetProperty("Backend").GetString()!=right.GetProperty("Backend").GetString(),"different measured backends");
        string[] expected={"flat","normal","glossy","rough","emissive","alpha-zero","alpha-half","uv-nearest","uv-linear","uv-repeat","mip-off","mip-on"};
        foreach(string name in expected)
        {
            byte[] x=left.GetProperty("Images").GetProperty(name).GetBytesFromBase64();
            byte[] y=right.GetProperty("Images").GetProperty(name).GetBytesFromBase64();
            Check(x.Length==96*96*3&&y.Length==x.Length,"bounded RGB fixture "+name);
            int surface=0, full=0, differing=0;
            for(int i=0;i<x.Length;i++) { int difference=Math.Abs(x[i]-y[i]); full=Math.Max(full,difference); if(difference!=0)differing++; }
            // Exclude geometry/background-grid coverage edges. The 56x56 interior
            // still includes texel transitions and minified checker samples.
            for(int row=20;row<76;row++)for(int column=20;column<76;column++)for(int channel=0;channel<3;channel++)
            { int i=(row*96+column)*3+channel; surface=Math.Max(surface,Math.Abs(x[i]-y[i])); }
            Check(surface<=1,"material surface parity "+name);
            Check(left.GetProperty("Alpha").GetProperty(name).GetInt32()==right.GetProperty("Alpha").GetProperty(name).GetInt32(),"alpha parity "+name);
            Console.WriteLine($"COMPARE {name}: surfaceMax={surface} fullFrameMax={full} differingFullFrameChannels={differing}");
        }
        Console.WriteLine("MATERIAL PIXEL COMPARISON PASS (12 synthetic fixtures; measured surface tolerance <=1/255)");
    }
    private static byte[] Sample(byte[] pixels) => pixels.AsSpan((43*96+43)*3,3).ToArray();
    private static void Check(bool value,string name) { if(!value)throw new Exception(name); Console.WriteLine("PASS "+name); }
}
