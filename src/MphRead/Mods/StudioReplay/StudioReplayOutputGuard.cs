#if MPHREAD_STUDIO
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.Replay;
namespace MphRead.Mods.StudioReplay;

/// <summary>Export publication can never target an input recording or authored sidecar.</summary>
internal static class StudioReplayOutputGuard
{
    internal static void ValidateVideo(string destination,IEnumerable<string> inputs)=>Validate(destination,".mp4",inputs);
    internal static void ValidateClip(string destination,IEnumerable<string> inputs)=>Validate(destination,".ppdemo",inputs);
    private static void Validate(string destination,string extension,IEnumerable<string> inputs)
    {
        if(!Path.IsPathFullyQualified(destination))throw new IOException("Replay export output must be an absolute path.");
        if(!Path.GetExtension(destination).Equals(extension,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Replay export output must use "+extension+".");
        string physical=Physical(destination);
        // A symlink named video.mp4 may point at a recording or its sidecar.
        if(!Path.GetExtension(physical).Equals(extension,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Replay output aliases a file of another type.");
        foreach(string input in inputs.Where(value=>!string.IsNullOrWhiteSpace(value)).SelectMany(Sources))
            foreach(string source in new[]{input,input+".camera",input+".studio.json",input+".reel.json"})
                if(Same(physical,Physical(source)))throw new IOException("Replay export cannot replace an input recording, clip or sidecar.");
    }
    private static IEnumerable<string> Sources(string input)
    {
        yield return input;
        if(input.EndsWith(ReplayVirtualClips.Extension,StringComparison.OrdinalIgnoreCase)
            && ReplayVirtualClips.TryLoad(input,out var clip) && clip is not null)yield return clip.SourceReplay;
    }
    private static bool Same(string left,string right)=>string.Equals(left,right,
        OperatingSystem.IsWindows()||OperatingSystem.IsMacOS()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);
    internal static string Physical(string path)=>Physical(path,0);
    private static string Physical(string path,int depth)
    {
        if(depth>40)throw new IOException("Replay output has too many symbolic link levels.");
        path=Path.GetFullPath(path);
        string root=Path.GetPathRoot(path)!;
        string current=root;
        foreach(string component in path[root.Length..].Split(new[]{Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar},StringSplitOptions.RemoveEmptyEntries))
        {
            current=Path.Combine(current,component);
            FileSystemInfo entry=Directory.Exists(current)?new DirectoryInfo(current):new FileInfo(current);
            if(entry.LinkTarget is not null)
            {
                string target=entry.ResolveLinkTarget(returnFinalTarget:true)?.FullName
                    ?? throw new IOException("Replay output has an unresolved symbolic link.");
                // Targets may themselves traverse linked parents (e.g. macOS /var -> /private/var).
                current=Physical(target,depth+1);
            }
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }
}

#endif
