#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Studio;

/// <summary>Catalog reads are detached; editor commands remain with the retained Forge workspace.</summary>
internal sealed class CurrentStudioLauncher : INativeStudioLauncher, INativeStudioProjectChanges
{
    public NativeStudioChangeSnapshot? CaptureChange() => Shell.NativeStudioChange();
    public NativeStudioResult ResolveChange(Guid ticket, NativeStudioChangeDecision decision, string savePath) => Shell.ResolveNativeStudioChange(ticket, decision, savePath);
    public void CancelChange(Guid ticket) => Shell.CancelNativeStudioChange(ticket);
    public bool TryTakeCommittedEditor(Guid ticket, out INativeStudioEditorSession? session) => Shell.TryTakeNativeStudioCommittedEditor(ticket, out session);
    private sealed record Target(string Path, bool Recovery, long Length, long ModifiedUtcTicks);
    private readonly object _gate = new();
    private readonly Dictionary<string, Target> _targets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NativeStudioFileStamp> _deletes = new(StringComparer.Ordinal);
    public bool LaunchMapStudio() => Shell.RmlUiNavigateStudio();
    public bool PickerAvailable => NativeFilePicker.Available;
    public IReadOnlyList<NativeStudioTemplate> Templates() => MapTemplates.Catalog
        .Where(t => t.Action == MapTemplateAction.Create).Select(t => new NativeStudioTemplate(t.Id, t.Name)).ToArray();

    public Task<IReadOnlyList<NativeStudioLibraryRow>> ReadLibraryAsync(bool recoveries, CancellationToken token)
        => Task.Run<IReadOnlyList<NativeStudioLibraryRow>>(() =>
        {
            var rows = new List<NativeStudioLibraryRow>();
            var targets = new Dictionary<string, Target>(StringComparer.Ordinal);
            void Add(string path, string name, string detail, bool canOpen, bool canDuplicate, bool canDelete)
            {
                token.ThrowIfCancellationRequested();
                var info = new FileInfo(path); string key = Guid.NewGuid().ToString("N");
                targets[key] = new(info.FullName, recoveries, info.Length, info.LastWriteTimeUtc.Ticks);
                rows.Add(new(key, name, detail, canOpen, canDuplicate, canDelete));
            }
            if (recoveries)
            {
                string directory = Path.Combine(CustomRooms.UserMapDirectory, ".autosave");
                foreach (string path in (Directory.Exists(directory)
                    ? Directory.EnumerateFiles(directory, "*.json") : Array.Empty<string>())
                    .OrderBy(p => p, StringComparer.Ordinal))
                {
                    token.ThrowIfCancellationRequested();
                    if (path.EndsWith(".context.json", StringComparison.OrdinalIgnoreCase)) continue;
                    try { Add(path, MapDocument.ReadRecovery(path).Definition.Name, "RECOVERY SNAPSHOT // OPENS AS UNSAVED PROJECT", true, false, false); }
                    catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
                    { Add(path, Path.GetFileName(path), "UNREADABLE RECOVERY // ORIGINAL FILE RETAINED", false, false, false); }
                }
            }
            else
            {
                foreach (var entry in new MapCatalog(CustomRooms.MapDirectory).Refresh(false))
                {
                    token.ThrowIfCancellationRequested();
                    bool delete = false;
                    try { _ = NativeStudioProjectFiles.Capture(entry.Path, CustomRooms.UserMapDirectory); delete = true; }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
                    string kind = MapBundle.Is(entry.Path) ? "PACKAGE" : "PROJECT";
                    string detail = entry.Validation.Diagnostics.FirstOrDefault()?.Message ?? "LOCAL " + kind;
                    Add(entry.Path, entry.Definition?.Name ?? Path.GetFileName(entry.Path), detail,
                        entry.Definition != null, entry.Definition != null, delete);
                }
            }
            token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _targets.Clear(); foreach (var target in targets) _targets.Add(target.Key, target.Value);
                _deletes.Clear();
            }
            return rows.ToArray();
        }, token);

    public async Task<NativeStudioPickResult> PickMapAsync(CancellationToken token)
    {
        if (!PickerAvailable) return new(null, "SYSTEM PICKER UNAVAILABLE // ENTER A PROJECT PATH");
        string? path = await NativeFilePicker.OpenFile("Open Map Studio project", "JSON map project", "json")
            .WaitAsync(token).ConfigureAwait(false);
        return new(path);
    }
    public NativeStudioResult OpenPath(string path)
    {
        string value = path.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
        string canonical = Path.GetFullPath(value);
        if (!File.Exists(canonical) || !(canonical.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || MapBundle.Is(canonical)))
            return new(false, "CHOOSE AN EXISTING .JSON PROJECT OR .PPMAP PACKAGE");
        return Shell.RmlUiOpenStudioProject(canonical, false);
    }
    public NativeStudioResult OpenEntry(string key)
    {
        var target = Resolve(key);
        return target == null ? new(false, "PROJECT CHANGED // REFRESH LIBRARY")
            : Shell.RmlUiOpenStudioProject(target.Path, target.Recovery);
    }
    public NativeStudioResult CreateProject(string name, string templateId) => Shell.RmlUiCreateStudioProject(name, templateId);
    public NativeStudioResult DuplicateEntry(string key)
    {
        var target = Resolve(key);
        return target == null || target.Recovery ? new(false, "PROJECT CHANGED // REFRESH LIBRARY")
            : Shell.RmlUiDuplicateStudioProject(target.Path);
    }
    public NativeStudioDeleteRequest? PrepareDelete(string key)
    {
        var target = Resolve(key);
        if (target == null || target.Recovery || Shell.RmlUiStudioOwnsPath(target.Path)) return null;
        var stamp = NativeStudioProjectFiles.Capture(target.Path, CustomRooms.UserMapDirectory);
        string ticket = Guid.NewGuid().ToString("N"); lock (_gate) _deletes[ticket] = stamp;
        return new(ticket, Path.GetFileName(target.Path));
    }
    public NativeStudioResult Delete(NativeStudioDeleteRequest request)
    {
        NativeStudioFileStamp? stamp;
        lock (_gate) { if (!_deletes.Remove(request.Ticket, out stamp)) return new(false, "DELETE CONFIRMATION EXPIRED"); }
        bool removed = NativeStudioProjectFiles.Delete(stamp, Shell.RmlUiStudioOwnsPath);
        return new(removed, removed ? "SAVED PROJECT FILE DELETED // ASSETS RETAINED" : "PROJECT CHANGED OR HAS AN EDITOR OWNER // DELETE REFUSED");
    }
    private Target? Resolve(string key)
    {
        Target? target; lock (_gate) _targets.TryGetValue(key, out target);
        if (target == null || !File.Exists(target.Path)) return null;
        var info = new FileInfo(target.Path);
        return info.Length == target.Length && info.LastWriteTimeUtc.Ticks == target.ModifiedUtcTicks ? target : null;
    }
}
#endif
