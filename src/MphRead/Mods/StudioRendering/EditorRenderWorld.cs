#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.IO;
using MphRead.Mods.MapEditor;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Materials;
using Silk.NET.WebGPU;
using GpuTexture = Silk.NET.WebGPU.Texture;
using GpuTextureFormat = Silk.NET.WebGPU.TextureFormat;
using GpuBuffer = Silk.NET.WebGPU.Buffer;

namespace MphRead.Mods.StudioRendering;

[StructLayout(LayoutKind.Sequential)]
internal struct EditorVertex
{
    internal Vector3 Position;
    internal Vector3 Normal;
    internal Vector2 Uv;
    internal float Shade;
    internal uint FaceId;
}

/// <summary>Direct vertex resources; the game Scene/display-list command path is never involved.</summary>
public sealed unsafe class MeshResource : IDisposable
{
    internal sealed class Part
    {
        internal GpuBuffer* Vertices;
        internal GpuBuffer* Uniform;
        internal BindGroup* Binding;
        internal int Count;
        internal bool CollisionOnly, SurfaceOnly, Solid;
        internal (bool Imported, int Index) Material;
        internal MaterialResource? BoundMaterial;
        internal StudioPickKind PickKind = StudioPickKind.Face;
    }
    internal readonly List<Part> Parts = new();
    internal readonly List<Part> Edges = new();
    internal readonly List<Part> PickVertices = new();
    internal readonly List<Part> PickEdges = new();
    private readonly StudioRenderDevice _owner;
    public Guid ObjectId { get; }
    public long ResidentBytes => Parts.Concat(Edges).Concat(PickVertices).Concat(PickEdges).Sum(part => (long)part.Count * 40 + 240);
    public long GeometryUploadBytes => Parts.Concat(Edges).Concat(PickVertices).Concat(PickEdges).Sum(part => (long)part.Count * 40);
    internal MeshResource(StudioRenderDevice owner, MapViewportMesh source, ref uint id,
        Dictionary<uint, StudioPickElement> picks)
    {
        _owner = owner; ObjectId = source.ObjectId;
        var admissionFaces=source.Faces.Concat(source.CollisionFaces ?? Array.Empty<MapViewportFace>());
        owner.RequireGeometryAdmission(admissionFaces.Sum(face => (long)(Math.Max(0,face.Points.Length-2)*3+face.Points.Length*14)*40+960));
        try
        {
            foreach (var variant in source.CollisionFaces == null
                ? new[] { (Faces: source.Faces, CollisionOnly: false, SurfaceOnly: false) }
                : new[] { (Faces: source.Faces, CollisionOnly: false, SurfaceOnly: true), (Faces: source.CollisionFaces, CollisionOnly: true, SurfaceOnly: false) })
            {
                var groups = new Dictionary<(bool, int, bool), (List<EditorVertex> Fill, List<EditorVertex> Edge, List<EditorVertex> VertexPick, List<EditorVertex> EdgePick)>();
                for (int f = 0; f < variant.Faces.Count; f++)
                {
                    var face = variant.Faces[f];
                    if (face.Points.Length < 3) continue;
                    uint faceId = ++id;
                    if (faceId == 0) throw new InvalidOperationException("Studio pick ID space is exhausted.");
                    picks[faceId] = new(source.ObjectId, f, StudioPickKind.Face, -1, -1);
                    var key = (face.ObjectId == Guid.Empty, face.Material, face.Solid);
                    if (!groups.TryGetValue(key, out var group)) groups[key] = group = (new(), new(), new(), new());
                    var normal = Vector3.Cross(face.Points[1] - face.Points[0], face.Points[2] - face.Points[0]);
                    normal = normal.LengthSquared() < 1e-10f ? Vector3.UnitY : Vector3.Normalize(normal);
                    EditorVertex Vertex(int index) => new() { Position = face.Points[index], Normal = normal,
                        Uv = face.Texcoords is { } uv && index < uv.Length ? uv[index] : Vector2.Zero,
                        Shade = Math.Clamp(face.Shade, .2f, 1), FaceId = faceId };
                    for (int i = 1; i < face.Points.Length - 1; i++)
                    { group.Fill.Add(Vertex(0)); group.Fill.Add(Vertex(i)); group.Fill.Add(Vertex(i + 1)); }
                    for (int i = 0; i < face.Points.Length; i++)
                    {
                        int next=(i+1)%face.Points.Length;
                        group.Edge.Add(Vertex(i)); group.Edge.Add(Vertex(next));
                        uint vertexId=++id,edgeId=++id;
                        picks[vertexId]=new(source.ObjectId,f,StudioPickKind.Vertex,i,-1);
                        picks[edgeId]=new(source.ObjectId,f,StudioPickKind.Edge,i,next);
                        foreach(var offset in new[] {new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,-1),new Vector2(1,1),new Vector2(-1,1)})
                        {var v=Vertex(i);v.FaceId=vertexId;v.Uv=offset;group.VertexPick.Add(v);}
                        foreach(var offset in new[] {new Vector2(0,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(0,-1),new Vector2(1,1),new Vector2(0,1)})
                        {var v=Vertex(i);v.FaceId=edgeId;v.Normal=face.Points[next];v.Uv=offset;group.EdgePick.Add(v);}
                    }
                }
                foreach (var group in groups)
                {
                    Parts.Add(Upload(group.Value.Fill, group.Key, variant.CollisionOnly, variant.SurfaceOnly));
                    Edges.Add(Upload(group.Value.Edge, group.Key, variant.CollisionOnly, variant.SurfaceOnly));
                    var vertexPart=Upload(group.Value.VertexPick,group.Key,variant.CollisionOnly,variant.SurfaceOnly);vertexPart.PickKind=StudioPickKind.Vertex;PickVertices.Add(vertexPart);
                    var edgePart=Upload(group.Value.EdgePick,group.Key,variant.CollisionOnly,variant.SurfaceOnly);edgePart.PickKind=StudioPickKind.Edge;PickEdges.Add(edgePart);
                }
            }
        }
        catch { Dispose(); throw; }
    }
    private Part Upload(List<EditorVertex> vertices, (bool Imported, int Index, bool Solid) material, bool collisionOnly, bool surfaceOnly)
    {
        var part = new Part { Count = vertices.Count, Material = (material.Imported, material.Index), Solid = material.Solid,
            CollisionOnly = collisionOnly, SurfaceOnly = surfaceOnly };
        try
        {
            ulong bytes = (ulong)(vertices.Count * Marshal.SizeOf<EditorVertex>());
            part.Vertices = _owner.Api.DeviceCreateBuffer(_owner.Device.Device, new BufferDescriptor { Size = bytes, Usage = BufferUsage.Vertex | BufferUsage.CopyDst });
            part.Uniform = _owner.Api.DeviceCreateBuffer(_owner.Device.Device, new BufferDescriptor { Size = 240, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
            if (part.Vertices == null || part.Uniform == null) throw new InvalidOperationException("Studio retained mesh allocation failed.");
            var queue = _owner.Api.DeviceGetQueue(_owner.Device.Device);
            try { fixed (EditorVertex* data = vertices.ToArray()) _owner.Api.QueueWriteBuffer(queue, part.Vertices, 0, data, (nuint)bytes); }
            finally { _owner.Api.QueueRelease(queue); }
            return part;
        }
        catch
        { if(part.Vertices!=null)_owner.Api.BufferRelease(part.Vertices);if(part.Uniform!=null)_owner.Api.BufferRelease(part.Uniform);throw; }
    }
    public void Dispose()
    {
        foreach (var part in Parts.Concat(Edges).Concat(PickVertices).Concat(PickEdges))
        {
            if (part.Binding != null) _owner.Api.BindGroupRelease(part.Binding);
            if (part.Uniform != null) _owner.Api.BufferRelease(part.Uniform);
            if (part.Vertices != null) _owner.Api.BufferRelease(part.Vertices);
        }
        Parts.Clear(); Edges.Clear();PickVertices.Clear();PickEdges.Clear();
    }
}

public sealed unsafe class MaterialResource
{
    internal GpuTexture* Texture;
    internal TextureView* View;
    internal Sampler* Sampler;
    public int Width { get; internal set; }
    public int Height { get; internal set; }
    internal int Normal, Specular, Emissive;
    internal bool Enhanced;
}

public sealed unsafe class EditorRenderWorld : IDisposable
{
    internal readonly StudioRenderDevice Owner;
    internal readonly MapViewportMeshResources<MeshResource> Meshes = new();
    internal readonly Dictionary<uint, StudioPickElement> Picks = new();
    internal readonly Dictionary<string, int> MaterialBindings = new(StringComparer.Ordinal);
    internal readonly Dictionary<int, MaterialResource> Textures = new();
    private sealed record MeshIdentity(string Hash);
    private sealed record CompanionSource(string Key,byte[] Bytes);
    private sealed record MaterialIdentity(string Key,CompanionSource? Normal,CompanionSource? Specular,CompanionSource? Emissive);
    private readonly ConditionalWeakTable<MapViewportMesh,MeshIdentity> _identities = new();
    private readonly ConditionalWeakTable<MapViewportMaterial,MaterialIdentity> _materialIdentities = new();
    private readonly Dictionary<Guid,(string Hash,MapViewportMesh Mesh)> _canonicalSources = new();
    private TextureAssetManager _assets;
    private uint _nextPick;
    private int _nextTexture;
    private bool _disposed;
    public long MeshUploads { get; private set; }
    public int ResidentMeshes => Meshes.Count;
    public int ResidentTextures => _assets.ResidentCount;
    public long TextureBytes => _assets.ResidentBytes;
    public long GeometryBytes { get; private set; }
    public long GeometryUploadBytes { get; private set; }
    internal EditorRenderWorld(StudioRenderDevice owner) { Owner = owner; _assets = MakeManager(); }
    private TextureAssetManager MakeManager() => new(() => ++_nextTexture, ReleaseTexture, UploadTexture, () => Owner.Device.MaxTextureDimension2D);

    internal void Synchronize(MapRenderFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Owner.RequireOwner();
        // Canonical authoring controls maintain separate CPU viewport caches.
        // Intern identical immutable geometry once per document, so four views
        // with distinct source objects still share exactly one GPU promotion.
        MapViewportMesh Intern(MapViewportMesh source)
        {
            string hash=_identities.GetValue(source,HashMesh).Hash;
            if(_canonicalSources.TryGetValue(source.ObjectId,out var existing) && existing.Hash==hash)return existing.Mesh;
            _canonicalSources[source.ObjectId]=(hash,source);return source;
        }
        var residents=frame.ResidentMeshes.Select(Intern).ToArray();
        var visible=frame.Meshes.Select(Intern).ToArray();
        var membership=residents.Select(mesh=>mesh.ObjectId).ToHashSet();
        foreach(var id in _canonicalSources.Keys.Where(id=>!membership.Contains(id)).ToArray())_canonicalSources.Remove(id);
        var retainedFrame=frame with {Meshes=visible,ResidentMeshes=residents};
        Meshes.Synchronize(retainedFrame, mesh => { var resource=new MeshResource(Owner, mesh, ref _nextPick, Picks);MeshUploads++;GeometryBytes+=resource.ResidentBytes;GeometryUploadBytes+=resource.GeometryUploadBytes;return resource; }, mesh =>
        {
            foreach (uint id in Picks.Where(p => p.Value.ObjectId == mesh.ObjectId).Select(p => p.Key).ToArray()) Picks.Remove(id);
            GeometryBytes-=mesh.ResidentBytes;mesh.Dispose();
        });
        var used = frame.Materials.Values.Select(m => Identity(m).Key).ToHashSet();
        used.Add("studio-white");
        if (frame.UvChecker) used.Add(MapViewportMaterials.Checker.Key);
        foreach (string key in MaterialBindings.Keys.Where(k => !used.Contains(k)).ToArray())
        {
            int binding = MaterialBindings[key]; MaterialBindings.Remove(key);
            var material = Textures[binding];
            foreach (int companion in new[] { material.Normal, material.Specular, material.Emissive })
                if (companion != 0 && !MaterialBindings.Values.Any(other => {
                    var remaining = Textures[other]; return remaining.Normal == companion || remaining.Specular == companion || remaining.Emissive == companion; }))
                    _assets.ReleaseBinding(companion);
            _assets.ReleaseBinding(binding);
        }
    }
    private static MeshIdentity HashMesh(MapViewportMesh mesh)
    {
        using var data=new MemoryStream();using var writer=new BinaryWriter(data);
        void Faces(IReadOnlyList<MapViewportFace>? faces)
        {
            writer.Write(faces?.Count ?? -1);if(faces==null)return;
            foreach(var face in faces)
            {
                writer.Write(face.ObjectId.ToByteArray());writer.Write(face.Material);writer.Write(face.SourceMaterial);
                writer.Write(face.Shade);writer.Write(face.Solid);writer.Write(face.CollisionOnly);writer.Write(face.Points.Length);
                foreach(var p in face.Points){writer.Write(p.X);writer.Write(p.Y);writer.Write(p.Z);}
                writer.Write(face.Texcoords?.Length ?? -1);
                if(face.Texcoords is { } uv)foreach(var p in uv){writer.Write(p.X);writer.Write(p.Y);}
            }
        }
        Faces(mesh.Faces);Faces(mesh.CollisionFaces);writer.Flush();
        return new(Convert.ToHexString(SHA256.HashData(data.GetBuffer().AsSpan(0,(int)data.Length))));
    }
    internal MaterialResource Material(MapViewportMaterial source)
    {
        var identity=Identity(source);
        if (MaterialBindings.TryGetValue(identity.Key, out int existing)) return Textures[existing];
        byte[] rgba = new byte[source.Pixels.Length * 4];
        for (int i = 0; i < source.Pixels.Length; i++)
        { rgba[i*4] = source.Pixels[i].Red; rgba[i*4+1] = source.Pixels[i].Green; rgba[i*4+2] = source.Pixels[i].Blue; rgba[i*4+3] = source.Pixels[i].Alpha; }
        int binding = _assets.UploadRgba(identity.Key, TextureAssetClass.World, TextureAssetChannel.Albedo,
            source.Width, source.Height, rgba, true, out _, out _);
        if (binding == 0) throw new InvalidOperationException("Studio material exceeded the shared texture residency budget.");
        var result = Textures[binding];
        int Companion(CompanionSource? image, TextureAssetChannel channel) => image == null ? 0 : _assets.Upload(image.Key,
            () => new MemoryStream(image.Bytes,writable:false), TextureAssetClass.World, channel, true, out _, out _);
        if (source.Enhanced is { } enhanced)
        {
            result.Normal = Companion(identity.Normal, TextureAssetChannel.Normal);
            result.Specular = Companion(identity.Specular, TextureAssetChannel.Material);
            result.Emissive = Companion(identity.Emissive, TextureAssetChannel.Emissive);
            result.Enhanced = true;
        }
        MaterialBindings.Add(identity.Key, binding); return result;
    }
    private MaterialIdentity Identity(MapViewportMaterial source) => _materialIdentities.GetValue(source,material =>
    {
        if(material.Enhanced is not { } enhanced)return new(material.Key,null,null,null);
        CompanionSource? Read(MaterialImage? image)
        {
            if(image==null)return null;
            try
            {
                using var stream=image.OpenRead();using var data=new MemoryStream();
                byte[] block=new byte[81920];int count;
                while((count=stream.Read(block,0,block.Length))>0)
                {
                    if(data.Length+count>ModernTextureAsset.MaximumDecodedBytes)throw new InvalidDataException("Creator texture source exceeds the shared texture allocation limit.");
                    data.Write(block,0,count);
                }
                byte[] bytes=data.ToArray();
                return new("studio-content/"+Convert.ToHexString(SHA256.HashData(bytes)),bytes);
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { return null; }
        }
        var normal=Read(enhanced.Normal);var specular=Read(enhanced.SpecularRoughness);var emissive=Read(enhanced.Emissive);
        // Freeze the admitted encoded source once. Content identity prevents
        // mutable-path collisions; recovery can decode the same bytes even if
        // an external source disappears after this material was promoted.
        return new(material.Key+"|pbr="+enhanced.Key.Value+"|normal="+normal?.Key+"|specular="+specular?.Key+"|emissive="+emissive?.Key,normal,specular,emissive);
    });
    internal MaterialResource White => Material(new("studio-white", 1, 1, new[] { new ColorRgba(255,255,255,255) }));
    private void UploadTexture(int id, PreparedTextureAsset asset, bool repeat, TextureSamplerDescriptor sampling)
    {
        // The same preparation policy chooses authored RGBA mips or the device's
        // supported block format. Explicit uploads retain those prepared bytes.
        var mips = asset switch
        {
            ModernTextureAsset rgba => sampling.Mipmaps ? rgba.CreateMipChain() : new[] { new RgbaTextureMip(rgba.Width, rgba.Height, rgba.Pixels) },
            RgbaMipTextureAsset authored => authored.Mips,
            _ => null
        };
        var compressed = asset as Ktx2TextureAsset;
        GpuTextureFormat format = compressed?.CompressionFormat switch
        {
            GpuTextureCompressionFormat.Bc7Rgba => GpuTextureFormat.BC7RgbaUnorm,
            GpuTextureCompressionFormat.Etc2Rgba8 => GpuTextureFormat.Etc2Rgba8Unorm,
            GpuTextureCompressionFormat.Astc4x4Rgba => GpuTextureFormat.Astc4x4Unorm,
            _ => GpuTextureFormat.Rgba8Unorm
        };
        int mipCount = sampling.Mipmaps ? mips?.Length ?? compressed?.Mips.Length ?? 1 : 1;
        var result = new MaterialResource { Width = asset.Width, Height = asset.Height };
        Textures[id] = result;
        var api = Owner.Api;
        result.Texture = api.DeviceCreateTexture(Owner.Device.Device, new TextureDescriptor {
            Size = new((uint)asset.Width, (uint)asset.Height, 1), Format = format,
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst, Dimension = TextureDimension.Dimension2D,
            MipLevelCount = (uint)mipCount, SampleCount = 1 });
        if (result.Texture == null) throw new InvalidOperationException("Studio material allocation failed.");
        var queue = api.DeviceGetQueue(Owner.Device.Device);
        try
        {
            for (int level = 0; level < mipCount; level++)
            {
                int width = mips?[level].Width ?? compressed!.Mips[level].Width;
                int height = mips?[level].Height ?? compressed!.Mips[level].Height;
                byte[] bytes = mips?[level].Data ?? compressed!.Mips[level].Data;
                var destination = new ImageCopyTexture { Texture = result.Texture, MipLevel = (uint)level, Aspect = TextureAspect.All };
                uint rowBytes = (uint)(mips != null ? width * 4 : (width + 3) / 4 * 16);
                var layout = new TextureDataLayout { BytesPerRow = rowBytes, RowsPerImage = (uint)(mips != null ? height : (height+3)/4) };
                var extent = new Extent3D((uint)width, (uint)height, 1);
                fixed (byte* data = bytes) api.QueueWriteTexture(queue, &destination, data, (nuint)bytes.Length, &layout, &extent);
            }
        }
        finally { api.QueueRelease(queue); }
        result.View = api.TextureCreateView(result.Texture, null);
        result.Sampler = api.DeviceCreateSampler(Owner.Device.Device, new SamplerDescriptor {
            AddressModeU = repeat ? AddressMode.Repeat : AddressMode.ClampToEdge,
            AddressModeV = repeat ? AddressMode.Repeat : AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = sampling.LinearMagnification ? FilterMode.Linear : FilterMode.Nearest,
            MinFilter = sampling.LinearMinification ? FilterMode.Linear : FilterMode.Nearest,
            MipmapFilter = sampling.LinearMinification ? MipmapFilterMode.Linear : MipmapFilterMode.Nearest,
            LodMaxClamp = mipCount - 1, MaxAnisotropy = 1 });
    }
    private void ReleaseTexture(int id)
    {
        if (!Textures.Remove(id, out var resource)) return;
        if (resource.Sampler != null) Owner.Api.SamplerRelease(resource.Sampler);
        if (resource.View != null) Owner.Api.TextureViewRelease(resource.View);
        if (resource.Texture != null) Owner.Api.TextureRelease(resource.Texture);
    }
    internal void ReleaseNative()
    {
        Meshes.Clear(mesh => mesh.Dispose()); Picks.Clear(); _nextPick = 0;
        _canonicalSources.Clear();_identities.Clear();
        GeometryBytes=0;
        _assets.Dispose(); MaterialBindings.Clear(); _assets = MakeManager();
    }
    public void Dispose()
    { if (_disposed) return; Owner.RequireOwner(); ReleaseNative();_materialIdentities.Clear(); Owner.Forget(this); _disposed = true; }
}
#endif
