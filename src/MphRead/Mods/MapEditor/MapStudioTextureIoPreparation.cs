using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using MphRead.Mods.Launcher;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;

namespace MphRead.Mods.MapEditor;

public sealed class MapStudioPreparedTexture
{
    public NativeStudioFileVersion Source { get; }
    public string PortableExtension { get; }
    public int Width { get; }
    public int Height { get; }
    private readonly byte[] _portable,_native;
    public ReadOnlyMemory<byte> PortableBytes=>_portable;
    public ReadOnlyMemory<byte> NativeBytes=>_native;
    internal MapStudioPreparedTexture(NativeStudioFileVersion source,string extension,int width,int height,byte[] portable,byte[] native)
    {Source=source;PortableExtension=extension;Width=width;Height=height;_portable=portable;_native=native;}
}
public sealed record MapStudioPreparedAssetExport(string Relative,string AssetFingerprint,string Sha256,ReadOnlyMemory<byte> Bytes,NativeStudioFileVersion? LooseSource);

/// <summary>Existing CPU texture converters and asset reader; no document, renderer or file publication is created.</summary>
public static class MapStudioTextureIoPreparation
{
    public static MapStudioPreparedTexture PrepareTexture(string source,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var read=MapStudioReviewedAssetFiles.ReadSource(source);token.ThrowIfCancellationRequested();
        var dimensions=ModernTextureAsset.ProbeDimensions(read.Bytes);
        string extension=ModernTextureAsset.PortableEncodedExtension(read.Bytes)??throw new InvalidDataException("Choose PNG, JPEG, TGA or KTX2 texture bytes.");
        token.ThrowIfCancellationRequested();byte[] baked=MapTextureBake.BakeImage(read.Bytes,token);token.ThrowIfCancellationRequested();
        if(baked.LongLength>MapPackageReader.MaxEntryBytes)throw new InvalidDataException("Baked texture exceeds the256MiB asset limit.");
        if(!MapStudioReviewedAssetFiles.Matches(read.Version))throw new IOException("Texture source changed during preparation. Review it again.");
        return new(read.Version,extension,dimensions.Width,dimensions.Height,read.Bytes,baked);
    }
    public static MapStudioPreparedAssetExport PrepareExport(MapDefinition definition,string relative,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var asset=MapStudioResourceCommands.Asset(definition,relative);
        NativeStudioFileVersion? looseSource=null;
        if(definition.BundlePath==null)
        {
            string root=Path.GetFullPath(definition.BaseDirectory??throw new IOException("Loose asset project root is unavailable."));
            string canonicalRoot=Path.GetDirectoryName(NativeStudioFileVersion.Capture(Path.Combine(root,".asset-root-probe"),true).Path)!;
            string requested=Path.GetFullPath(Path.Combine(canonicalRoot,MapPackageReader.CanonicalName(relative)));
            for(string? current=requested;current!=canonicalRoot&&current!=null;current=Path.GetDirectoryName(current))
                if(new FileInfo(current).LinkTarget!=null||(File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("Loose asset source links require a direct contained project path.");
            looseSource=MapStudioReviewedAssetFiles.ReviewTarget(requested);
            if(!looseSource.Exists)throw new FileNotFoundException("Loose asset source is missing.");
            if(looseSource.Path!=requested||!looseSource.Path.StartsWith(canonicalRoot.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.Ordinal))
                throw new IOException("Loose asset source is linked or escapes the reviewed project.");
        }
        string fingerprint=MapStudioResourceCommands.Fingerprint(asset),expected=MapAssets.HashPackageAsset(definition,relative);
        byte[]? bytes=null;
        MapAssets.WithPackageRead(definition,relative,(input,length)=>{
            if(length<0||length>MapPackageReader.MaxEntryBytes)throw new InvalidDataException("Export asset exceeds the256MiB entry limit.");
            bytes=new byte[checked((int)length)];int offset=0;
            while(offset<bytes.Length){token.ThrowIfCancellationRequested();int read=input.Read(bytes,offset,Math.Min(65536,bytes.Length-offset));if(read<=0)throw new IOException("Asset length changed while reading.");offset+=read;}
            if(input.ReadByte()!=-1)throw new IOException("Asset length changed while reading.");
        });
        token.ThrowIfCancellationRequested();string actual=Convert.ToHexString(SHA256.HashData(bytes!)).ToLowerInvariant();
        if(actual!=expected||MapAssets.HashPackageAsset(definition,relative)!=expected)throw new IOException("Asset bytes changed during export review.");
        if(looseSource!=null&&(!MapStudioReviewedAssetFiles.Matches(looseSource)||!string.Equals(looseSource.Sha256,actual,StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Loose asset identity changed during export preparation.");
        return new(relative,fingerprint,actual,bytes!,looseSource);
    }
}
