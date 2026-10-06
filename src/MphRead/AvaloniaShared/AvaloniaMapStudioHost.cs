#if MPHREAD_AVALONIA && !ANDROID
using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Avalonia.Controls;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.AvaloniaShared;

/// <summary>Native Avalonia editor facade over the authoritative map document and creator algorithms.</summary>
public sealed class AvaloniaMapStudioHost : UserControl, IAsyncDisposable
{
    private readonly MapStudioScreen _screen;
    private readonly IMapStudioHostServices _services;
    private bool _disposed;
    public MapDocument? Document => _screen.Document;
    public string? RecoveryPath => Document?.RecoveryPath(_services.UserMapDirectory);
    public event Action? Changed;
    public event EventHandler? CloseRequested;
    public AvaloniaMapStudioHost(IMapStudioHostServices services)
    {
        StudioGameAssets.InitializeAuthoringProcess();
        _services = services;
        _screen = new MapStudioScreen(services);
        _screen.DocumentChanged += OnChanged;
        _screen.Closed += OnClose;
        Content = _screen;
    }
    private void OnChanged() => Changed?.Invoke();
    private void OnClose(object? sender, EventArgs e) => CloseRequested?.Invoke(this,e);
    public void NewProject(string name = "Untitled Map", bool example = false) => _screen.Load(MapTemplates.Create(name,example));
    public async Task OpenAsync(string path, CancellationToken cancellation = default)
    {
        MapProject project = await Task.Run(() => MapProjectSerializer.Load(path),cancellation);
        cancellation.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed,this);
        _screen.Load(project,path);
    }
    public Task SaveAsync(string path, CancellationToken cancellation = default) => _screen.SaveDocumentAsync(path,cancellation);
    public Task PreserveRecoveryAsync(CancellationToken cancellation = default) => _screen.PreserveRecoveryAsync(cancellation);
    public Task FlushAutosaveAsync(CancellationToken cancellation = default) => PreserveRecoveryAsync(cancellation);
    public async Task OpenRecoveryAsync(string recoveryPath, CancellationToken cancellation = default)
    {
        MapProject recovered = await Task.Run(() => MapDocument.ReadRecovery(recoveryPath),cancellation);
        cancellation.ThrowIfCancellationRequested();
        string? source = recovered.Definition.SourcePath;
        if (source != null && File.Exists(source) && !MapBundle.Is(source))
        {
            MapProject original = await Task.Run(() => MapProjectSerializer.Load(source),cancellation);
            _screen.Load(original,source,promptRecovery:false);
            Document!.RestoreRecoveryFile(recoveryPath);
        }
        else _screen.Load(recovered,promptRecovery:false);
    }
    public void RestoreRecovery() => _screen.RecoverDocument();
    public void Undo() => Document?.History.Undo();
    public void Redo() => Document?.History.Redo();
    public void ShowPanel(string name) => _screen.SelectPanel(name);
    public void SelectFaces(Guid objectId, params int[] faces) => _screen.SelectFaces(objectId,faces);
    public void ToggleFourViews() => _screen.ToggleFourViews();
    public Task ValidateAsync() => _screen.ValidateDocumentAsync();
    public Task BuildAsync(bool package = true) => _screen.BuildDocumentAsync(package);
    public Task PlaytestAsync() => _screen.PlaytestAsync();
    public void FrameAll() => _screen.FrameAll();
    public void ToggleWireframe() => _screen.ToggleWireframe();
    public void OpenAssetBrowser() => _screen.OpenAssetBrowser();
    public Task ImportAssetAsync(string path,CancellationToken cancellation=default) => _screen.ImportAssetAsync(path,cancellation);
    public static bool SupportsAssetDrop(string path) => MapStudioScreen.SupportsAssetDrop(path);
    public Task ImportDroppedFilesAsync(System.Collections.Generic.IReadOnlyList<string> paths,CancellationToken cancellation=default) => _screen.ImportDroppedFilesAsync(paths,cancellation);
    public Task WaitForAssetThumbnailsAsync() => _screen.WaitForAssetThumbnailsAsync();
    public Task ReplaceAssetAsync(string assetPath,string sourcePath,CancellationToken cancellation=default) => _screen.ReplaceAssetAsync(assetPath,sourcePath,cancellation);
    public Task ImportModelAsync(string path,ModelImportSettings? settings=null,Guid? sourceId=null,CancellationToken cancellation=default) => _screen.ImportModelAsync(path,settings,sourceId,cancellation);
    public void ShowPrimitiveDialog(string kind) => _screen.ShowPrimitiveDialog(kind);
    public void ShowModelImportDialog(string path,Guid? sourceId=null) => _screen.ShowModelImportDialog(path,sourceId);
    public static Avalonia.Input.DataFormat<MapAssetDragData> AssetDragFormat => MapStudioScreen.AssetDragFormat;
    public Task ApplyAssetDropAsync(MapAssetDragData data,Avalonia.Point position) => _screen.ApplyAssetDropAsync(data,position);
    public MapAssetDragData CreateAssetDragData(MapAssetDragKind kind,string key) => _screen.CreateAssetDragData(kind,key);
    public void ShowStatus(string message) => _screen.ShowStatus(message);
    public void SetViewportMode(string mode) => _screen.SetViewportMode(mode);
    public MphRead.Mods.StudioRendering.StudioViewportImage? CaptureViewport() => _screen.CaptureViewport();
    public ValueTask DisposeAsync() => DisposeAsync(true);
    public Task DiscardRecoveryAsync(CancellationToken cancellation = default) => _screen.DiscardRecoveryAsync(cancellation);
    public Task CancelPendingJobsAsync(CancellationToken cancellation = default) => _screen.CancelPendingJobsAsync(cancellation);
    public void ResumeAssetThumbnails() => _screen.ResumeAssetThumbnails();
    public async ValueTask DisposeAsync(bool preserveRecovery)
    {
        if (_disposed) return;
        _disposed = true;
        _screen.DocumentChanged -= OnChanged;
        _screen.Closed -= OnClose;
        try { await _screen.ShutdownAsync(preserveRecovery); }
        finally
        {
            try
            {
                if(_services is IAsyncDisposable asynchronous)await asynchronous.DisposeAsync();
                else if (_services is IDisposable disposable) disposable.Dispose();
            }
            finally { Content = null; }
        }
    }
}

#endif
