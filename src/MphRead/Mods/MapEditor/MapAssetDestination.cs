using System;
using System.IO;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

/// <summary>Checks the current filesystem ownership of an imported map asset.</summary>
public static class MapAssetDestination
{
    public static string Resolve(string root,string relative,string? expectedCanonicalRoot=null)
    {
        MapPackageReader.CanonicalName(relative);
        string fullRoot=Path.GetFullPath(root);
        string canonicalRoot=MapPublicationLease.CanonicalizeRuntimeDirectory(fullRoot);
        if(expectedCanonicalRoot is not null && canonicalRoot!=expectedCanonicalRoot)
            throw new InvalidDataException("The map asset folder changed during import. Choose the current project folder and retry.");
        string destination=Path.GetFullPath(Path.Combine(fullRoot,relative));
        string parent=MapPublicationLease.CanonicalizeRuntimeDirectory(Path.GetDirectoryName(destination)!);
        string prefix=Path.EndsInDirectorySeparator(canonicalRoot)?canonicalRoot:canonicalRoot+Path.DirectorySeparatorChar;
        if(parent!=canonicalRoot && !parent.StartsWith(prefix,StringComparison.Ordinal))
            throw new InvalidDataException("The imported asset folder resolves outside its map project.");
        // Reusing an existing content-addressed file must not follow a link to
        // another owner's bytes. The same check protects failed-import cleanup.
        if(new FileInfo(destination).LinkTarget is not null)
            throw new InvalidDataException("Imported asset files cannot be symbolic links.");
        return destination;
    }
}
