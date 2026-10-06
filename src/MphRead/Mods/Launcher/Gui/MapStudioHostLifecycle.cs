using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapEditor;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    internal MapDocument? Document => _document;
    internal event Action? DocumentChanged;
    private TaskCompletionSource? _jobCompletion;
    internal void AddExternalStudioLaunch(Action launch)
    {
        if (_services.IsStandalone || _hostToolbar is null) return;
        var button = new PrimeButton("OPEN MAP STUDIO",launch,primary:true,compact:true);
        _hostToolbar.Children.Insert(1,button);
        _editingControls.Add(button);
    }
    private async Task BrowseNativeAsync(string title, bool save, Action<string> selected, string[] extensions)
    {
        try
        {
            string? path = await _services.PickFileAsync(title,save,extensions,CancellationToken.None);
            if (path != null && !_detached) selected(path);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Failure(ex); }
    }
    internal async Task SaveDocumentAsync(string path, CancellationToken cancellation)
    {
        if (_document is not { } document) throw new InvalidOperationException("No map project is open.");
        if (string.IsNullOrWhiteSpace(path)) throw new IOException("Choose a project filename before saving.");
        cancellation.ThrowIfCancellationRequested();
        _autosave.Dispose();
        try
        {
            await _autosave.Completion;
            cancellation.ThrowIfCancellationRequested();
            document.Save(path);
            document.DiscardRecovery(_services.UserMapDirectory);
            _path.Text = document.FilePath;
            _status.Text = "Saved " + document.FilePath;
            RefreshStudioChrome(); DocumentChanged?.Invoke();
        }
        finally { _autosave = new(); }
    }
    internal async Task PreserveRecoveryAsync(CancellationToken cancellation)
    {
        _autosave.Dispose();
        try
        {
            await _autosave.Completion;
            cancellation.ThrowIfCancellationRequested();
            if (_document is { IsDirty: true } document)
            {
                using var writer = new MapAutosaveService();
                writer.Queue(document.CaptureAutosave(_services.UserMapDirectory));
                await writer.Completion;
                if (writer.Result?.Error is { } error) throw new IOException(error);
            }
        }
        finally { if (!_detached) _autosave = new(); }
    }
    internal void RecoverDocument() { _document?.Restore(_services.UserMapDirectory); Dismiss(); }
    internal Task ValidateDocumentAsync() => Validate();
    internal Task BuildDocumentAsync(bool package) => Build(package);
    internal void SelectPanel(string name) { if (name=="Community") ShowCommunity(); else ShowInspectorPage(name); }
    internal void SelectFaces(Guid objectId, int[] faces)
    {
        if (_document is null || _viewport is null || MapObjects.Find(_document.Project.Definition,objectId)?.Value is not MphRead.Mods.MapGen.MapMesh mesh)
            throw new ArgumentException("Choose an authored mesh for face selection.");
        if (Array.Exists(faces,face => face<0 || face>=mesh.Faces.Count)) throw new ArgumentOutOfRangeException(nameof(faces));
        _document.Selection.Clear(); _document.Selection.Add(objectId); _document.ActiveObjectId=objectId;
        _viewport.ElementMode="Face"; _viewport.SubSelection.Bind(objectId);
        _viewport.SelectedFaceIndices.Clear(); _viewport.SelectedFaceIndices.UnionWith(faces);
        _viewport.SubSelection.ActiveFace=faces.Length>0?faces[^1]:-1;
        _document.SelectionChanged(); _viewport.InvalidateVisual(); ShowInspectorPage("UV");
    }
    internal async Task DiscardRecoveryAsync(CancellationToken cancellation)
    {
        _idle.Stop(); _autosave.Dispose(); await _autosave.Completion;
        cancellation.ThrowIfCancellationRequested(); _document?.DiscardRecovery(_services.UserMapDirectory);
    }
    internal async Task ShutdownAsync(bool preserveRecovery)
    {
        _idle.Stop();
        _work?.Cancel();
        if (_jobCompletion is { } completion) await completion.Task;
        if (preserveRecovery) await PreserveRecoveryAsync(CancellationToken.None);
        Dispose();
        await _autosave.Completion;
    }
}
