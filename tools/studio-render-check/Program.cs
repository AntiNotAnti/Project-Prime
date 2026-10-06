using System.Numerics;
using MphRead.Mods.MapEditor;
using MphRead.Mods.StudioRendering;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render.Materials;
using SkiaSharp;
using System.Runtime.InteropServices;

int checks=0;
void Check(bool condition,string name) { if(!condition)throw new InvalidOperationException(name);Console.WriteLine("STUDIO RENDER PASS "+name);checks++; }
Guid front=Guid.NewGuid(),back=Guid.NewGuid();
MapViewportMesh Mesh(Guid id,float depth) => new(id,new[] { new MapViewportFace(id,
    new[] {new Vector3(-2,-2,depth),new Vector3(2,-2,depth),new Vector3(0,2,depth)},.8f,0,true) });
var meshes=new[] {Mesh(front,0),Mesh(back,-2)};
var frame=new MapRenderFrame(new(128,128),new(new(0,0,8),Vector3.Zero,true),meshes,new HashSet<Guid>(),new Dictionary<Guid,Matrix4x4>(),false,false);
Check(EditorPickPass.Cpu(frame,64,64).Surface?.ObjectId==front,"CPU parity oracle resolves nearest actual triangle");
foreach(uint id in new uint[] {0,1,255,256,65535,0x80ff00fe,uint.MaxValue})
{
    var encoded=EditorPickPass.EncodeRgba(id);Check(EditorPickPass.DecodeRgba(encoded.R,encoded.G,encoded.B,encoded.A)==id,"RGBA fallback preserves all 32 ID bits "+id);
}
var dpi=frame with {Layout=new(64,64,2)};
Check(dpi.Layout.PixelWidth==128 && dpi.Camera.Project(dpi.Layout,Vector3.Zero) is {X:32,Y:32},"DPI bounds and canonical logical camera agree");
if(args.Contains("--gpu"))
{
    using var device=new StudioRenderDevice();using var world=device.CreateWorld();
    var views=Enumerable.Range(0,4).Select(_=>device.CreateSurface()).ToArray();
    try
    {
        foreach(var view in views)
        {
            // Real authoring controls keep independent canonical CPU caches.
            // Equivalent source objects must share one GPU promotion too.
            var separateCache=new[] {Mesh(front,0),Mesh(back,-2)};
            var picture=view.Render(world,frame with {Meshes=separateCache,ResidentMeshes=separateCache});
            Check(picture.Rgba[(64*128+64)*4]>180,"retained modern graph produces expected shaded triangle pixels");
        }
        Check(world.MeshUploads==2,"four independent canonical CPU caches share document mesh uploads");
        long uploads=world.MeshUploads;
        views[0].Render(world,frame with {Camera=new(new(1,1,8),Vector3.Zero,true),Selection=new HashSet<Guid>{front}});
        Check(world.MeshUploads==uploads,"camera and selection changes retain uploaded geometry");
        var hit=views[0].Pick(world,frame,64,64);
        Check(hit.GpuUsed && hit.MatchesCpu && hit.Surface?.ObjectId==front,"one-pixel R32Uint depth pick agrees with CPU oracle");
        var vertex=views[0].Pick(world,frame,36,92,StudioPickKind.Vertex);
        Check(vertex.GpuUsed && vertex.MatchesCpu && vertex.Element is {Kind:StudioPickKind.Vertex,A:0},"GPU vertex ID quads preserve CPU element parity");
        var edge=views[0].Pick(world,frame,64,90,StudioPickKind.Edge);
        Check(edge.GpuUsed && edge.MatchesCpu && edge.Element is {Kind:StudioPickKind.Edge,A:0,B:1},"GPU edge ID quads preserve CPU element parity");
        views[0].Render(world,frame with {Meshes=new[] {meshes[1]},ResidentMeshes=new[] {meshes[1]}});
        Check(world.ResidentMeshes==1,"removed document mesh releases retained resources");
        device.SimulateDeviceLossForDiagnostics();
        views[0].Render(world,frame);
        Check(device.Generation==2 && world.ResidentMeshes==2,"device loss recreates native resources from retained canonical CPU meshes");
        string materialDirectory=Path.Combine(Path.GetTempPath(),"studio-material-check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(materialDirectory);
        try
        {
            string normalPath=Path.Combine(materialDirectory,"normal.png"),specularPath=Path.Combine(materialDirectory,"specular.png"),emissivePath=Path.Combine(materialDirectory,"emissive.png");
            void Channel(string path,SKColor color)
            {
                using var bitmap=new SKBitmap(1,1,SKColorType.Rgba8888,SKAlphaType.Unpremul);bitmap.SetPixel(0,0,color);
                using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(path,png.ToArray());
            }
            Channel(normalPath,new(128,128,255));Channel(specularPath,new(128,255,0,0));Channel(emissivePath,new(0,255,0));
            var enhanced=new ResolvedMaterial(new("map/studio-render-check"),null,new(normalPath,1,1),new(specularPath,1,1),new(emissivePath,1,1));
            var green=new MapViewportMaterial("pbr-solid",1,1,new[] {new MphRead.ColorRgba(0,0,0,255)},Enhanced:enhanced);
            var greenFrame=frame with {Materials=new Dictionary<(bool,int),MapViewportMaterial>{{(false,0),green}}};
            long pbrUploads=world.MeshUploads;
            var greenPicture=views[0].Render(world,greenFrame);int center=(64*128+64)*4;
            Console.WriteLine("PBR green center="+string.Join(',',greenPicture.Rgba.Skip(center).Take(4))+" resident textures="+world.ResidentTextures);
            Check(greenPicture.Rgba[center+1]>210 && greenPicture.Rgba[center]<30,"shared material decoder and authoritative PBR shader apply emissive companion pixels");
            Channel(emissivePath,new(255,0,0));
            // Same canonical albedo key and same mutable source path, with the
            // original green material still resident in another material slot.
            var red=green with {Pixels=green.Pixels.ToArray()};
            var redFrame=frame with {Materials=new Dictionary<(bool,int),MapViewportMaterial>{{(false,0),red},{(false,1),green}}};
            var redPicture=views[0].Render(world,redFrame);
            Check(redPicture.Rgba[center]>210 && redPicture.Rgba[center+1]<30 && world.MeshUploads==pbrUploads,"companion content revisions avoid mutable-path collisions without geometry uploads");
            File.Delete(normalPath);File.Delete(specularPath);File.Delete(emissivePath);
            device.SimulateDeviceLossForDiagnostics();var restored=views[0].Render(world,redFrame);
            Check(device.Generation==3 && restored.Rgba[center]>210 && restored.Rgba[center+1]<30,"device recovery promotes frozen companion bytes after external sources disappear");
        }
        finally{Directory.Delete(materialDirectory,recursive:true);}
        int outputIndex=Array.IndexOf(args,"--output");
        if(outputIndex>=0 && outputIndex+1<args.Length)
        {
            string directory=Path.GetFullPath(args[outputIndex+1]);Directory.CreateDirectory(directory);
            var definition=MapTemplates.Create("Studio rendering acceptance",false).Definition;
            var cache=new MapViewportCache();cache.Invalidate(definition,new(MapChangeDomain.All));
            var materialTable=cache.Meshes.SelectMany(mesh=>mesh.Faces).Select(face=>(false,face.Material)).Distinct()
                .ToDictionary(key=>key,_=>MapViewportMaterials.Checker);
            var canonical=new MapRenderFrame(new(960,600),new(new(28,24,32),Vector3.Zero,true),cache.Meshes,new HashSet<Guid>(),new Dictionary<Guid,Matrix4x4>(),false,false)
                {ResidentMeshes=cache.Meshes,Materials=materialTable};
            using var canonicalWorld=device.CreateWorld();
            void Save(string name,MapRenderFrame pictureFrame)
            {
                var capture=views[0].Render(canonicalWorld,pictureFrame);
                using var bitmap=new SKBitmap(capture.Width,capture.Height,SKColorType.Rgba8888,SKAlphaType.Opaque);
                Marshal.Copy(capture.Rgba,0,bitmap.GetPixels(),capture.Rgba.Length);
                using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);
                File.WriteAllBytes(Path.Combine(directory,name+".png"),png.ToArray());
            }
            Save("map-default",canonical);Save("map-hidpi",canonical with {Layout=new(960,600,2)});
            Save("map-uv-checker",canonical with {UvChecker=true});
            Save("map-wireframe",canonical with {Wireframe=true});
            Save("map-four-view-perspective",canonical with {Layout=new(480,300)});
            Save("map-four-view-top",canonical with {Layout=new(480,300),Camera=new(new(0,60,0),Vector3.Zero,false)});
            Save("map-four-view-front",canonical with {Layout=new(480,300),Camera=new(new(0,0,60),Vector3.Zero,false)});
            Save("map-four-view-side",canonical with {Layout=new(480,300),Camera=new(new(60,0,0),Vector3.Zero,false)});
            Console.WriteLine("Explicit diagnostic GPU captures: "+directory);
        }
        Console.WriteLine($"backend={device.Backend} adapter={device.Adapter} generation={device.Generation}");
    }
    finally {foreach(var view in views)view.Dispose();}
}
Console.WriteLine($"Studio rendering checks passed: {checks}. GPU mode: {args.Contains("--gpu")}.");
