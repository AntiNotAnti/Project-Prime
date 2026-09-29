using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace MphRead.Mods.MapGen;

/// <summary>Static glTF 2.0 geometry, flattened into ordinary editable meshes.</summary>
public sealed class GltfModelImporter : IModelImporter
{
    private const int MaxBytes=128*1024*1024;
    public bool CanImport(string extension)=>extension.Equals(".gltf",StringComparison.OrdinalIgnoreCase)||extension.Equals(".glb",StringComparison.OrdinalIgnoreCase);
    private static int Int(JsonElement value,string name,int fallback=0)=>value.TryGetProperty(name,out var p)?p.GetInt32():fallback;
    private static JsonElement[] Array(JsonElement value,string name)=>value.TryGetProperty(name,out var p)?p.EnumerateArray().ToArray():System.Array.Empty<JsonElement>();
    private static string Name(JsonElement value,string fallback)=>value.TryGetProperty("name",out var p)?(p.GetString()??fallback)[..Math.Min(128,(p.GetString()??fallback).Length)]:fallback;
    public ImportedModel Import(string path,ModelImportSettings settings,CancellationToken cancellation=default)
    {
        if(!float.IsFinite(settings.Scale)||settings.Scale<=0)throw new InvalidDataException("Model scale must be positive and finite.");
        path=Path.GetFullPath(path);string directory=Path.GetDirectoryName(path)!;
        var dependencies=new HashSet<string>(StringComparer.Ordinal);long bytesRead=0;
        byte[] ReadFile(string file)
        {cancellation.ThrowIfCancellationRequested();long length=new FileInfo(file).Length;if(length>MaxBytes||(bytesRead+=length)>MaxBytes)throw new InvalidDataException("glTF dependencies exceed the 128 MiB source budget.");dependencies.Add(file);return File.ReadAllBytes(file);}
        byte[] UriBytes(string uri)
        {
            if(uri.StartsWith("data:",StringComparison.OrdinalIgnoreCase))
            {
                int comma=uri.IndexOf(',');if(comma<0||!uri[..comma].EndsWith(";base64",StringComparison.OrdinalIgnoreCase)||uri.Length>MaxBytes*4L/3)throw new InvalidDataException("Only bounded base64 data URIs are supported.");
                byte[] data=Convert.FromBase64String(uri[(comma+1)..]);if((bytesRead+=data.Length)>MaxBytes)throw new InvalidDataException("Embedded data exceeds source budget.");return data;
            }
            string relative=Uri.UnescapeDataString(uri).Replace('\\','/');
            if(Path.IsPathRooted(relative)||relative.Contains(':')||relative.Split('/').Any(p=>p==".."))throw new InvalidDataException("Model dependencies must stay inside the model folder.");
            string full=Path.GetFullPath(Path.Combine(directory,relative));
            if(!full.StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.Ordinal))throw new InvalidDataException("Model dependency escapes its folder.");
            for(string? part=full;part!=directory&&part!=null;part=Path.GetDirectoryName(part))
                if(File.Exists(part)||Directory.Exists(part))if(File.GetAttributes(part).HasFlag(FileAttributes.ReparsePoint))throw new InvalidDataException("Model dependencies cannot be symbolic links.");
            return ReadFile(full);
        }
        byte[] source=ReadFile(path),json=source;byte[]? binary=null;
        if(Path.GetExtension(path).Equals(".glb",StringComparison.OrdinalIgnoreCase))
        {
            if(source.Length<20||BinaryPrimitives.ReadUInt32LittleEndian(source)!=0x46546c67||BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(4))!=2||BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(8))!=source.Length)throw new InvalidDataException("Invalid GLB header or length.");
            int at=12;bool first=true;json=System.Array.Empty<byte>();
            while(at<source.Length)
            {
                if(source.Length-at<8)throw new InvalidDataException("Truncated GLB chunk.");
                uint size=BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(at)),type=BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(at+4));at+=8;
                if(size>source.Length-at||size%4!=0)throw new InvalidDataException("Invalid GLB chunk bounds.");
                if(first&&type!=0x4e4f534a)throw new InvalidDataException("GLB must start with JSON.");
                if(type==0x4e4f534a){if(!first)throw new InvalidDataException("Duplicate GLB JSON chunk.");json=source.AsSpan(at,(int)size).ToArray();}
                else if(type==0x004e4942){if(binary!=null)throw new InvalidDataException("Duplicate GLB binary chunk.");binary=source.AsSpan(at,(int)size).ToArray();}
                first=false;at+=(int)size;
            }
        }
        using var document=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=64});var root=document.RootElement;
        if(!root.TryGetProperty("asset",out var asset)||asset.GetProperty("version").GetString()!="2.0")throw new InvalidDataException("Only glTF 2.0 is supported.");
        if(Array(root,"extensionsRequired").Length>0)throw new InvalidDataException("Required glTF extensions are unsupported: "+string.Join(", ",Array(root,"extensionsRequired").Select(e=>e.GetString())));
        var warnings=new HashSet<string>();
        foreach(string feature in new[]{"animations","skins","cameras"})if(Array(root,feature).Length>0)warnings.Add(feature+" are present; Map Studio imports static geometry only.");
        if(root.TryGetProperty("extensionsUsed",out _))warnings.Add("Optional glTF extensions are ignored; core geometry and materials are imported.");
        var buffers=Array(root,"buffers").Select((buffer,i)=>
        {
            byte[] bytes=buffer.TryGetProperty("uri",out var uri)?UriBytes(uri.GetString()!):i==0&&binary!=null?binary:throw new InvalidDataException("Missing buffer URI or GLB buffer.");
            int length=Int(buffer,"byteLength");if(length<0||length>bytes.Length)throw new InvalidDataException("Truncated glTF buffer.");return bytes.AsMemory(0,length);
        }).ToArray();
        var views=Array(root,"bufferViews");var accessors=Array(root,"accessors");
        ReadOnlyMemory<byte> View(int index)
        {
            if((uint)index>=views.Length)throw new InvalidDataException("Invalid bufferView index.");var view=views[index];int buffer=Int(view,"buffer",-1),offset=Int(view,"byteOffset"),length=Int(view,"byteLength",-1);
            if((uint)buffer>=buffers.Length||offset<0||length<0||(long)offset+length>buffers[buffer].Length)throw new InvalidDataException("Invalid bufferView range.");return buffers[buffer].Slice(offset,length);
        }
        static int Size(int component)=>component switch{5120 or 5121=>1,5122 or 5123=>2,5125 or 5126=>4,_=>throw new InvalidDataException("Unsupported accessor component type.")};
        static double Number(ReadOnlySpan<byte> data,int component,bool normalized)
        {
            double value=component switch{5120=>(sbyte)data[0],5121=>data[0],5122=>BinaryPrimitives.ReadInt16LittleEndian(data),5123=>BinaryPrimitives.ReadUInt16LittleEndian(data),5125=>BinaryPrimitives.ReadUInt32LittleEndian(data),5126=>BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data)),_=>throw new InvalidDataException()};
            if(normalized)value=component switch{5120=>Math.Max(-1,value/127),5121=>value/255,5122=>Math.Max(-1,value/32767),5123=>value/65535,_=>throw new InvalidDataException("Invalid normalized component.")};
            if(!double.IsFinite(value))throw new InvalidDataException("Non-finite glTF accessor value.");return value;
        }
        double[][] Accessor(int index,int components,bool indices=false)
        {
            cancellation.ThrowIfCancellationRequested();if((uint)index>=accessors.Length)throw new InvalidDataException("Invalid accessor index.");var accessor=accessors[index];
            string type=accessor.GetProperty("type").GetString()!;int count=Int(accessor,"count"),component=Int(accessor,"componentType"),size=Size(component);
            if(type!=(components==1?"SCALAR":components==2?"VEC2":"VEC3")||count<1||count>(indices?196605:65535))throw new InvalidDataException("Unsupported accessor shape or element budget.");
            bool normalized=accessor.TryGetProperty("normalized",out var norm)&&norm.GetBoolean();
            if(indices&&(component is not (5121 or 5123 or 5125)||normalized))throw new InvalidDataException("Indices must be unsigned integers.");
            var values=Enumerable.Range(0,count).Select(_=>new double[components]).ToArray();
            if(accessor.TryGetProperty("bufferView",out var vi))
            {
                int viewIndex=vi.GetInt32();var data=View(viewIndex);int stride=Int(views[viewIndex],"byteStride",size*components),offset=Int(accessor,"byteOffset");
                if(stride<size*components||stride>252||stride%size!=0||offset<0||offset%size!=0||(long)offset+(count-1L)*stride+size*components>data.Length)throw new InvalidDataException("Accessor exceeds buffer view bounds.");
                for(int i=0;i<count;i++)for(int c=0;c<components;c++)values[i][c]=Number(data.Span.Slice(offset+i*stride+c*size,size),component,normalized);
            }
            if(accessor.TryGetProperty("sparse",out var sparse))
            {
                int sparseCount=Int(sparse,"count");if(sparseCount<0||sparseCount>count)throw new InvalidDataException("Invalid sparse count.");var ids=sparse.GetProperty("indices");var data=sparse.GetProperty("values");
                int idType=Int(ids,"componentType"),idSize=Size(idType),idOffset=Int(ids,"byteOffset"),dataOffset=Int(data,"byteOffset");
                if(idType is not (5121 or 5123 or 5125))throw new InvalidDataException("Invalid sparse index type.");
                var idBytes=View(Int(ids,"bufferView",-1));var valueBytes=View(Int(data,"bufferView",-1));
                if(idOffset<0||dataOffset<0||(long)idOffset+sparseCount*idSize>idBytes.Length||(long)dataOffset+sparseCount*components*size>valueBytes.Length)throw new InvalidDataException("Sparse accessor exceeds bounds.");
                int previous=-1;for(int i=0;i<sparseCount;i++){double raw=Number(idBytes.Span.Slice(idOffset+i*idSize,idSize),idType,false);if(raw<=previous||raw>=count)throw new InvalidDataException("Invalid sparse accessor index.");int target=(int)raw;previous=target;for(int c=0;c<components;c++)values[target][c]=Number(valueBytes.Span.Slice(dataOffset+(i*components+c)*size,size),component,normalized);}
            }
            return values;
        }
        var assets=new Dictionary<string,byte[]>();var assetSources=new Dictionary<string,string>();var materials=new List<MapMaterial>();var images=Array(root,"images");var textures=Array(root,"textures");
        var sourceMaterials=Array(root,"materials");if(sourceMaterials.Length>255)throw new InvalidDataException("glTF exceeds material budget.");
        foreach(var material in sourceMaterials)
        {
            var color=new[]{1f,1f,1f,1f};byte[]? textureBytes=null;
            if(material.TryGetProperty("pbrMetallicRoughness",out var pbr))
            {
                if(pbr.TryGetProperty("baseColorFactor",out var factor)){color=factor.EnumerateArray().Select(v=>v.GetSingle()).ToArray();if(color.Length!=4||color.Any(v=>!float.IsFinite(v)||v<0||v>1))throw new InvalidDataException("Invalid base color factor.");}
                if(pbr.TryGetProperty("baseColorTexture",out var reference))
                {
                    int texture=Int(reference,"index",-1);if((uint)texture>=textures.Length)throw new InvalidDataException("Missing glTF texture.");int image=Int(textures[texture],"source",-1);if((uint)image>=images.Length)throw new InvalidDataException("Missing glTF image.");
                    try{textureBytes=images[image].TryGetProperty("uri",out var uri)?UriBytes(uri.GetString()!):View(Int(images[image],"bufferView",-1)).ToArray();ObjModelImporter.ValidateImage(textureBytes);}
                    catch(FileNotFoundException){warnings.Add("Missing base-color texture; imported its base color instead.");}
                    if(Int(reference,"texCoord")!=0)warnings.Add("Only UV0 is imported; a material requested another UV channel.");
                }
                if(pbr.TryGetProperty("metallicRoughnessTexture",out _)||(!pbr.TryGetProperty("metallicFactor",out var metallic)||metallic.GetSingle()!=0))warnings.Add("Metallic/roughness shading is not imported.");
            }
            if(material.TryGetProperty("normalTexture",out _))warnings.Add("Normal maps are not imported.");
            if(color[3]<1||material.TryGetProperty("alphaMode",out var alpha)&&alpha.GetString()!="OPAQUE")warnings.Add("Transparent materials are imported as opaque.");
            byte[] baked=textureBytes==null?ObjModelImporter.Solid(color[0],color[1],color[2]):MapTextureBake.BakeImage(textureBytes,cancellation);
            if(textureBytes!=null)
            {
                int count=BinaryPrimitives.ReadUInt16LittleEndian(baked.AsSpan(14)),nameLength=BinaryPrimitives.ReadUInt16LittleEndian(baked.AsSpan(16));
                for(int i=0;i<count;i++){int offset=18+nameLength+i*2;ushort rgb=BinaryPrimitives.ReadUInt16LittleEndian(baked.AsSpan(offset));int tinted=0;for(int c=0;c<3;c++)tinted|=(int)Math.Clamp(Math.Round(((rgb>>(c*5))&31)*MathF.Pow(color[c],1/2.2f)),0,31)<<(c*5);BinaryPrimitives.WriteUInt16LittleEndian(baked.AsSpan(offset),(ushort)tinted);}
            }
            string assetPath="textures/gltf-"+Convert.ToHexString(SHA256.HashData(baked)).ToLowerInvariant()+".tex";assets.TryAdd(assetPath,baked);assetSources[assetPath]=path;
            materials.Add(new(){Name=Name(material,"Material "+materials.Count),Texture=assetPath});
        }
        int defaultMaterial=-1;
        int DefaultMaterial()
        {
            if(defaultMaterial>=0)return defaultMaterial;
            byte[] baked=ObjModelImporter.Solid(1,1,1);
            string assetPath="textures/gltf-default-"+Convert.ToHexString(SHA256.HashData(baked)).ToLowerInvariant()+".tex";
            assets.TryAdd(assetPath,baked);assetSources[assetPath]=path;
            defaultMaterial=materials.Count;materials.Add(new(){Name="Default",Texture=assetPath});return defaultMaterial;
        }
        var meshes=new List<MapMesh>();var nodes=Array(root,"nodes");var definitions=Array(root,"meshes");var scenes=Array(root,"scenes");
        if(nodes.Length>10000)throw new InvalidDataException("glTF exceeds node budget.");int totalVertices=0,totalFaces=0;var visited=new HashSet<int>();
        float[] Floats(JsonElement node,string name,float[] defaults){if(!node.TryGetProperty(name,out var p))return defaults;var values=p.EnumerateArray().Select(v=>v.GetSingle()).ToArray();if(values.Length!=defaults.Length||values.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Invalid node transform.");return values;}
        void Node(int index,Matrix4x4 parent,int depth)
        {
            cancellation.ThrowIfCancellationRequested();if((uint)index>=nodes.Length||depth>128||!visited.Add(index))throw new InvalidDataException("Invalid, repeated or cyclic scene node.");var node=nodes[index];Matrix4x4 local;
            if(node.TryGetProperty("matrix",out _)){var m=Floats(node,"matrix",new float[16]);local=new(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);}
            else{var t=Floats(node,"translation",new float[3]);var r=Floats(node,"rotation",new[]{0f,0,0,1});var s=Floats(node,"scale",new[]{1f,1,1});var q=new Quaternion(r[0],r[1],r[2],r[3]);if(MathF.Abs(q.LengthSquared()-1)>.001f)throw new InvalidDataException("Node quaternion is not normalized.");local=Matrix4x4.CreateScale(s[0],s[1],s[2])*Matrix4x4.CreateFromQuaternion(q)*Matrix4x4.CreateTranslation(t[0],t[1],t[2]);}
            Matrix4x4 world=local*parent;
            if(node.TryGetProperty("mesh",out var meshIndex))
            {
                int mi=meshIndex.GetInt32();if((uint)mi>=definitions.Length)throw new InvalidDataException("Invalid mesh index.");var definition=definitions[mi];int primitiveIndex=0;
                foreach(var primitive in Array(definition,"primitives"))
                {
                    int ordinal=primitiveIndex++;if(Int(primitive,"mode",4)!=4){warnings.Add("Non-triangle primitives were skipped.");continue;}
                    if(primitive.TryGetProperty("targets",out _))warnings.Add("Morph targets are present; only the base mesh is imported.");
                    var attributes=primitive.GetProperty("attributes");var positions=Accessor(Int(attributes,"POSITION",-1),3);var coords=attributes.TryGetProperty("TEXCOORD_0",out var uv)?Accessor(uv.GetInt32(),2):null;
                    if(coords!=null&&coords.Length!=positions.Length)throw new InvalidDataException("UV count does not match vertex count.");
                    var indices=primitive.TryGetProperty("indices",out var ia)?Accessor(ia.GetInt32(),1,true).Select(v=>v[0]).ToArray():Enumerable.Range(0,positions.Length).Select(i=>(double)i).ToArray();
                    if(indices.Length%3!=0||indices.Any(i=>i<0||i>=positions.Length))throw new InvalidDataException("Invalid triangle index stream.");
                    if((totalVertices+=positions.Length)>1000000||(totalFaces+=indices.Length/3)>1000000)throw new InvalidDataException("Scene exceeds geometry budget.");
                    int material=primitive.TryGetProperty("material",out var materialProperty)
                        ?materialProperty.GetInt32():DefaultMaterial();
                    if((uint)material>=materials.Count)throw new InvalidDataException("Invalid primitive material.");
                    var mesh=new MapMesh{Label=Name(node,Name(definition,"Mesh "+mi))+" / "+ordinal,Material=material,Solid=settings.VisualCollision};
                    foreach(var value in positions){Vector3 p=Vector3.Transform(new((float)value[0],(float)value[1],(float)value[2]),world)*settings.Scale;if(settings.ZUp)p=new(p.X,p.Z,-p.Y);if(!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z))throw new InvalidDataException("Node transform exceeds coordinate range.");mesh.Vertices.Add(new[]{p.X,p.Y,p.Z});}
                    bool flip=settings.FlipWinding^(world.GetDeterminant()<0);
                    for(int i=0;i<indices.Length;i+=3){int[] face=new[]{(int)indices[i],(int)indices[i+1],(int)indices[i+2]};if(flip)System.Array.Reverse(face);mesh.Faces.Add(face);mesh.FaceMaterials.Add(material);mesh.FaceTexcoords.Add(coords==null?null:face.Select(v=>new[]{(float)coords[v][0]*64,(settings.FlipUvVertical?1-(float)coords[v][1]:(float)coords[v][1])*64}).ToArray());}
                    _=GeometryCompiler.Compile(mesh,16);meshes.Add(mesh);
                }
            }
            foreach(var child in Array(node,"children"))Node(child.GetInt32(),world,depth+1);
        }
        int[] roots;
        if(scenes.Length>0){int scene=Int(root,"scene");if((uint)scene>=scenes.Length)throw new InvalidDataException("Invalid default scene.");roots=Array(scenes[scene],"nodes").Select(n=>n.GetInt32()).ToArray();}
        else{var children=nodes.SelectMany(n=>Array(n,"children")).Select(c=>c.GetInt32()).ToHashSet();roots=Enumerable.Range(0,nodes.Length).Where(i=>!children.Contains(i)).ToArray();}
        foreach(int node in roots)Node(node,Matrix4x4.Identity,0);
        if(meshes.Count==0)throw new InvalidDataException("No supported static triangle geometry was found.");
        return new(meshes,materials,assets,warnings.ToArray()){AssetSources=assetSources,Dependencies=dependencies.Order(StringComparer.Ordinal).ToArray()};
    }
}
