using System;
using System.IO;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private bool NativeCommandAvailable => !_detached && !_poppedOut && _work == null && _nativeEditor == null && _nativeChange == null;
    internal NativeStudioResult NativeOpenProject(string path, bool recovery)
    {
        if (!NativeCommandAvailable) return new(false, "EXISTING EDITOR IS BUSY // FINISH ITS CURRENT OPERATION");
        try
        {
            MapProject project = recovery ? MapDocument.ReadRecovery(path) : MapProjectSerializer.Load(path);
            WithUnsaved(() => Load(project, recovery ? null : path));
            return new(true, "CONTINUE IN THE EXISTING GEOMETRY EDITOR // UNSAVED CHANGES REMAIN EDITOR-OWNED", true);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { return new(false, "PROJECT COULD NOT BE OPENED // EXISTING DOCUMENT RETAINED"); }
    }
    internal NativeStudioResult NativeCreateProject(string name, string templateId)
    {
        if (!NativeCommandAvailable) return new(false, "EXISTING EDITOR IS BUSY // FINISH ITS CURRENT OPERATION");
        try
        {
            var project = MapTemplates.Create(name, templateId);
            WithUnsaved(() => Load(project));
            return new(true, "NEW PROJECT REQUESTED IN THE EXISTING GEOMETRY EDITOR", true);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { return new(false, "PROJECT NAME OR TEMPLATE IS INVALID // EXISTING DOCUMENT RETAINED"); }
    }
    internal NativeStudioResult NativeDuplicateProject(string path)
    {
        if (!NativeCommandAvailable) return new(false, "EXISTING EDITOR IS BUSY // FINISH ITS CURRENT OPERATION");
        try
        {
            var project = MapProjectMigrator.Upgrade(MapProjectSerializer.Load(path));
            project.Definition.MapId = Guid.NewGuid();
            project.Definition.Name = NextCopyName(project.Definition.Name);
            WithUnsaved(() => Load(project));
            return new(true, "DUPLICATE OPEN REQUESTED // NEW ID; SAVE IT IN THE EXISTING EDITOR", true);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { return new(false, "PROJECT COULD NOT BE DUPLICATED // EXISTING DOCUMENT RETAINED"); }
    }
    internal bool NativeOwnsProjectPath(string path)
    {
        // Hidden Forge workspaces remain retained document owners.
        if (_document == null) return false;
        string target = Path.GetFullPath(path);
        foreach (string? candidate in new[] { _document.FilePath, _document.Project.Definition.SourcePath, _path.Text })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try { if (Path.GetFullPath(candidate).Equals(target, StringComparison.OrdinalIgnoreCase)) return true; }
            catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException) { }
        }
        return false;
    }
    internal bool TryCaptureCommunityPublishIntent(out CommunityStudioPublishIntent? intent, out string error)
    {
        intent = null;
        if (_poppedOut || _work != null || _document == null)
        { error = "OPEN AN EDITOR PROJECT AND FINISH ITS CURRENT OPERATION BEFORE PUBLISHING"; return false; }
        try
        {
            // Invoked synchronously on the editor owner. The native Community
            // page receives a detached build snapshot, including unsaved edits.
            intent = CommunityStudioPublishIntent.Capture(_document.Project.Definition);
            error = ""; return true;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException and not StackOverflowException)
        { error = "CURRENT PROJECT CANNOT BE PREPARED FOR COMMUNITY PUBLISHING"; return false; }
    }
}
