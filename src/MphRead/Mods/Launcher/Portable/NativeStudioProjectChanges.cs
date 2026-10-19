using System;
using System.IO;
using System.Security.Cryptography;

namespace MphRead.Mods.Launcher;

public enum NativeStudioChangeKind { Create, Open, Recovery, Duplicate }
public enum NativeStudioChangeStage { Reading, NeedsDecision, NeedsOverwrite, Preserving, Ready, Failed }
public enum NativeStudioChangeDecision { Save, Preserve, Cancel, Overwrite, KeepFile }
public sealed record NativeStudioChangeSpec(NativeStudioChangeKind Kind, string Path = "", string TemplateId = "", string Name = "");
public sealed record NativeStudioChangeSnapshot(Guid Ticket, NativeStudioChangeStage Stage, string CurrentName,
    string TargetName, string SavePath, string Error)
{
    public string ReviewedFileName => System.IO.Path.GetFileName(SavePath);
    public string StatusText => Error.Length > 0 ? Error : Stage switch {
        NativeStudioChangeStage.Reading => "READING THE NEXT PROJECT // CURRENT PROJECT REMAINS OPEN",
        NativeStudioChangeStage.NeedsDecision => "SAVE YOUR CURRENT PROJECT, KEEP A RECOVERY COPY, OR CANCEL",
        NativeStudioChangeStage.NeedsOverwrite => "REVIEW THE FILENAME AND COMPLETE PATH BEFORE REPLACING THIS FILE",
        NativeStudioChangeStage.Preserving => "KEEPING THE CURRENT PROJECT BEFORE OPENING THE NEXT",
        NativeStudioChangeStage.Ready => "PROJECT READY // OPENING THE NATIVE EDITOR",
        _ => "PROJECT CHANGE COULD NOT COMPLETE // CURRENT PROJECT REMAINS OPEN"
    };
}
public interface INativeStudioProjectChanges
{
    NativeStudioChangeSnapshot? CaptureChange();
    NativeStudioResult ResolveChange(Guid ticket, NativeStudioChangeDecision decision, string savePath);
    void CancelChange(Guid ticket);
    bool TryTakeCommittedEditor(Guid ticket, out INativeStudioEditorSession? session);
}

/// <summary>Read-only project/save-target version. Confirmation never relies on path or mtime alone.</summary>
public sealed record NativeStudioFileVersion(string Path, bool Exists, long Length, long ModifiedUtcTicks, string Sha256)
{
    public static NativeStudioFileVersion Capture(string path, bool allowMissing = false)
    {
        string full = CanonicalParents(System.IO.Path.GetFullPath(path)); GuardLinks(full);
        if (Directory.Exists(full)) throw new IOException("Project path is a directory.");
        var file = new FileInfo(full);
        if (!file.Exists)
        {
            if (!allowMissing) throw new FileNotFoundException("Project source is missing.");
            return new(full, false, 0, 0, "");
        }
        long length = file.Length, modified = file.LastWriteTimeUtc.Ticks;
        using var stream = File.OpenRead(full);
        string hash = Convert.ToHexString(SHA256.HashData(stream));
        file.Refresh(); GuardLinks(full);
        if (!file.Exists || file.Length != length || file.LastWriteTimeUtc.Ticks != modified) throw new IOException("Project source changed while reading.");
        return new(full, true, length, modified, hash);
    }
    public bool Matches()
    {
        try { return this == Capture(Path, allowMissing: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
    private static void GuardLinks(string path)
    {
        for (string? current = path; current != null; current = System.IO.Path.GetDirectoryName(current))
            if (new FileInfo(current).LinkTarget != null || (File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked project/save paths require a direct path.");
    }
    private static string CanonicalParents(string full)
    {
        return System.IO.Path.Combine(CanonicalDirectory(System.IO.Path.GetDirectoryName(full)!, 0), System.IO.Path.GetFileName(full));
    }
    private static string CanonicalDirectory(string parent, int depth)
    {
        if(depth>32)throw new IOException("Project parent links exceed resolution limit.");
        string current = System.IO.Path.GetPathRoot(parent)!;
        foreach (string part in parent[current.Length..].Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = System.IO.Path.Combine(current, part);
            var directory = new DirectoryInfo(current);
            if (directory.LinkTarget != null)
                current = CanonicalDirectory(directory.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Project parent link is unavailable."),depth+1);
        }
        return current;
    }
}
