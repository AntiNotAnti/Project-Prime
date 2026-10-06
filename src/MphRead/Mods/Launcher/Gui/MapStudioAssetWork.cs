using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    internal static bool SupportsAssetDrop(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".tga" or ".ktx2" or ".tex" or ".wav" or ".ogg" or ".mp3" or ".obj" or ".gltf" or ".glb";
    internal async Task ImportDroppedFilesAsync(IReadOnlyList<string> paths,CancellationToken cancellation=default)
    {
        if(paths.Count>32)throw new IOException("Drop up to 32 assets at a time.");
        foreach(string path in paths)
        {
            cancellation.ThrowIfCancellationRequested();if(!SupportsAssetDrop(path))continue;
            if(Path.GetExtension(path).ToLowerInvariant() is ".obj" or ".gltf" or ".glb")await ImportModelAsync(path,cancellation:cancellation);
            else await ImportAssetAsync(path,cancellation);
        }
    }
    private sealed record AssetEditContext(MapDocument Document, DocumentStateId State,
        string? FilePath, string? BaseDirectory, string? SourcePath, string? BundlePath, string Root,string CanonicalRoot);
    private AssetEditContext CaptureAssetContext()
    {
        if (_document is not { } document) throw new InvalidOperationException("Open a map project first.");
        if (_detached) throw new ObjectDisposedException(nameof(MapStudioScreen));
        if (_work is not null) throw new InvalidOperationException("Wait for the current map operation or cancel it first.");
        var definition=document.Project.Definition;
        if(definition.BundlePath is not null) throw new IOException("Save this package as an editable project before importing assets.");
        string root=Path.GetFullPath(definition.BaseDirectory ?? (document.FilePath is { } file ? Path.GetDirectoryName(file)! : _services.MapLibraryDirectory));
        return new(document,document.CurrentStateId,document.FilePath,definition.BaseDirectory,definition.SourcePath,definition.BundlePath,root,
            MapPublicationLease.ResolveRuntimeDirectoryAliases(root));
    }
    private void GuardAssetContext(AssetEditContext context,CancellationToken cancellation)
    {
        GuardJob(cancellation);
        var definition=context.Document.Project.Definition;
        if(context.Document.CurrentStateId!=context.State || context.Document.FilePath!=context.FilePath
            || definition.BaseDirectory!=context.BaseDirectory || definition.SourcePath!=context.SourcePath || definition.BundlePath!=context.BundlePath)
            throw new OperationCanceledException("The map or its asset location changed. Import again in the current project.",cancellation);
        if(!MapPublicationLease.ResolveRuntimeDirectoryAliases(context.Root).Equals(context.CanonicalRoot,
            OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
            throw new OperationCanceledException("The map asset folder changed during import. Retry in the current project.",cancellation);
    }
    private static byte[] ReadImportBytes(string path,long maximum,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(input.Length<1 || input.Length>maximum)throw new IOException($"Choose a nonempty asset no larger than {maximum/1024/1024} MiB.");
        byte[] bytes=new byte[checked((int)input.Length)];input.ReadExactly(bytes);
        cancellation.ThrowIfCancellationRequested();return bytes;
    }
    private static void ValidateAudio(byte[] bytes,string extension)
    {
        bool valid=extension switch
        {
            ".wav"=>bytes.Length>=12 && bytes.AsSpan(0,4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8,4).SequenceEqual("WAVE"u8),
            ".ogg"=>bytes.Length>=4 && bytes.AsSpan(0,4).SequenceEqual("OggS"u8),
            ".mp3"=>bytes.Length>=3 && (bytes.AsSpan(0,3).SequenceEqual("ID3"u8) || bytes[0]==255 && (bytes[1]&224)==224),
            _=>false
        };
        if(!valid)throw new InvalidDataException("The audio file does not contain the selected WAV, OGG or MP3 format.");
    }
    internal Task ImportAssetAsync(string path,CancellationToken cancellation=default)
    {
        path=Path.GetFullPath(path);AssetEditContext context=CaptureAssetContext();
        return Job("Importing asset",async token=>
        {
            var prepared=await Task.Run(()=>
            {
                string extension=Path.GetExtension(path).ToLowerInvariant();
                bool audio=extension is ".wav" or ".ogg" or ".mp3";
                byte[] source=ReadImportBytes(path,audio?32*1024*1024:MapPackageReader.MaxEntryBytes,token);
                string name=Path.GetFileNameWithoutExtension(path), id=Guid.NewGuid().ToString("N");
                var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);
                var assets=new List<MapAsset>();MapMaterial? material=null;MapAudioSettings? music=null;
                if(audio)
                {
                    ValidateAudio(source,extension);string relative="audio/"+id+extension;files.Add(relative,source);
                    assets.Add(new(){Name=name,Kind="audio",Path=relative,SourcePath=path});music=new(){Music=relative};
                }
                else
                {
                    string fallback="textures/"+id+".tex";byte[] baked;
                    string? encoded=null;
                    if(extension==".tex")
                    {var pack=MapTexturePack.Load(source,path);if(pack.Entries.Count!=1)throw new IOException("Choose a single baked texture.");baked=source;}
                    else
                    {
                        encoded=ModernTextureAsset.PortableEncodedExtension(source)??throw new IOException("Choose PNG, JPEG, TGA, KTX2 or a baked .tex texture.");
                        _=ModernTextureAsset.ProbeDimensions(source);baked=MapTextureBake.BakeImage(source,token);
                    }
                    files.Add(fallback,baked);assets.Add(new(){Name=name+" · native",Kind="texture",Path=fallback,SourcePath=path});
                    string? albedo=null;
                    if(encoded is not null){albedo="textures/"+id+encoded;files.Add(albedo,source);assets.Add(new(){Name=name+" · HD",Kind="texture",Path=albedo,SourcePath=path});}
                    material=new(){Id=Guid.NewGuid(),Name=name,Texture=fallback,Albedo=albedo,TexScale=16};
                }
                return new PreparedAssetImport(files,assets,material,music);
            },token);
            GuardAssetContext(context,token);
            await AdoptAssetFilesAsync(context,prepared.Files,token,()=>context.Document.Edit(prepared.Music is null?"Import texture and material":"Import map music",definition=>
            {
                definition.BaseDirectory=context.Root;definition.Assets.AddRange(prepared.Assets);
                if(prepared.Material is {} material)definition.Materials.Add(material);
                if(prepared.Music is {} music)definition.Audio=music;
            },MapChangeDomain.Material|MapChangeDomain.Metadata));
            ShowInspectorPage("Assets & music");_status.Text="Imported "+Path.GetFileName(path)+" · one Undo step";
        },cancellation,propagateErrors:true);
    }
    private sealed record PreparedAssetImport(IReadOnlyDictionary<string,byte[]> Files,List<MapAsset> Assets,MapMaterial? Material,MapAudioSettings? Music);
    internal Task ReplaceAssetAsync(string previous,string source,CancellationToken cancellation=default)
    {
        source=Path.GetFullPath(source);AssetEditContext context=CaptureAssetContext();
        MapAsset original=context.Document.Project.Definition.Assets.Find(asset=>asset.Path==previous)??throw new ArgumentException("Choose a declared map asset.",nameof(previous));
        string kind=original.Kind;
        return Job("Replacing asset",async token=>
        {
            var prepared=await Task.Run(()=>
            {
                string extension=Path.GetExtension(source).ToLowerInvariant();
                byte[] bytes=ReadImportBytes(source,kind=="audio"?32*1024*1024:MapPackageReader.MaxEntryBytes,token);
                if(kind=="texture")
                {
                    if(Path.GetExtension(previous).Equals(".tex",StringComparison.OrdinalIgnoreCase))
                    {bytes=extension==".tex"?bytes:MapTextureBake.BakeImage(bytes,token);extension=".tex";if(MapTexturePack.Load(bytes,source).Entries.Count!=1)throw new IOException("Choose a single baked texture.");}
                    else{_=ModernTextureAsset.ProbeDimensions(bytes);extension=ModernTextureAsset.PortableEncodedExtension(bytes)??throw new IOException("Choose a portable PNG, JPEG, TGA or KTX2 image.");}
                }
                else if(kind=="audio")ValidateAudio(bytes,extension);
                else if(kind=="preview" && extension!=".png")throw new IOException("Choose a PNG preview.");
                string relative=kind+"/"+Guid.NewGuid().ToString("N")+extension;return(relative,bytes);
            },token);
            GuardAssetContext(context,token);
            await AdoptAssetFilesAsync(context,new Dictionary<string,byte[]> {{prepared.relative,prepared.bytes}},token,()=>context.Document.Edit("Replace asset",definition=>
            {
                var asset=definition.Assets.Find(asset=>asset.Path==previous)??throw new IOException("The asset changed. Choose it again.");
                asset.Path=prepared.relative;asset.SourcePath=source;definition.BaseDirectory=context.Root;
                MapAssetCatalog.ReplaceReferences(definition,previous,prepared.relative);
            },MapChangeDomain.Material|MapChangeDomain.Metadata));
            ShowInspectorPage("Assets & music");_status.Text="Asset replaced. Undo restores all previous references.";
        },cancellation,propagateErrors:true);
    }
    private async Task AdoptAssetFilesAsync(AssetEditContext context,IReadOnlyDictionary<string,byte[]> files,CancellationToken cancellation,Action adopt)
    {
        using var lease=await AssetFileLease.AcquireAsync(context.Root,cancellation);
        GuardAssetContext(context,cancellation);var created=new List<string>();bool accepted=false;
        try
        {
            await Task.Run(()=>
            {
                foreach(var file in files)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string destination=MapAssetDestination.Resolve(context.Root,file.Key,context.CanonicalRoot);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    destination=MapAssetDestination.Resolve(context.Root,file.Key,context.CanonicalRoot);
                    if(File.Exists(destination))
                    {
                        if(!ReadImportBytes(destination,MapPackageReader.MaxEntryBytes,cancellation).AsSpan().SequenceEqual(file.Value))
                            throw new IOException("An existing content-addressed model asset contains different bytes: "+file.Key);
                        continue;
                    }
                    AtomicFile.Write(destination,file.Value);created.Add(file.Key);
                }
                cancellation.ThrowIfCancellationRequested();
            },cancellation);
            GuardAssetContext(context,cancellation);
            try { adopt(); }
            finally
            {
                // History commits before it notifies views. A failing view must not
                // cause cleanup to remove files referenced by that committed edit.
                accepted=context.Document.CurrentStateId!=context.State;
                if(accepted)foreach(string path in created)context.Document.RegisterGeneratedAsset(path,context.Root,context.CanonicalRoot);
            }
        }
        finally
        {
            if(!accepted)await Task.Run(()=>{foreach(string path in created)try
                {File.Delete(MapAssetDestination.Resolve(context.Root,path,context.CanonicalRoot));}
                catch(IOException){}catch(UnauthorizedAccessException){}});
        }
    }
    private sealed class AssetFileLease : IDisposable
    {
        private sealed class Entry { public readonly SemaphoreSlim Gate=new(1);public int Users; }
        private static readonly object Sync=new();
        private static readonly Dictionary<string,Entry> Entries=new(StringComparer.OrdinalIgnoreCase);
        private readonly string _root;private readonly Entry _entry;private bool _disposed;
        private AssetFileLease(string root,Entry entry){_root=root;_entry=entry;}
        internal static async Task<AssetFileLease> AcquireAsync(string root,CancellationToken cancellation)
        {
            root=MapPublicationLease.CanonicalizeRuntimeDirectory(root);
            Entry entry;lock(Sync){if(!Entries.TryGetValue(root,out entry!))Entries.Add(root,entry=new());entry.Users++;}
            try{await entry.Gate.WaitAsync(cancellation);return new(root,entry);}
            catch{ReleaseUser(root,entry);throw;}
        }
        private static void ReleaseUser(string root,Entry entry)
        {lock(Sync){if(--entry.Users==0){Entries.Remove(root);entry.Gate.Dispose();}}}
        public void Dispose(){if(_disposed)return;_disposed=true;_entry.Gate.Release();ReleaseUser(_root,_entry);}
    }
}
