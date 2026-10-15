#if MPHREAD_AVALONIA && !ANDROID
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.AvaloniaShared;

/// <summary>Desktop facade over the current canonical map screen, document and compiler.</summary>
public sealed class AvaloniaMapStudioHost : UserControl, IAsyncDisposable
{
    private readonly MapStudioScreen _screen;
    private bool _disposed;
    public MapDocument? Document => _screen.StudioDocument;
    public string? RecoveryPath => Document?.RecoveryPath(CustomRooms.UserMapDirectory);
    public event Action? Changed;
    public event EventHandler? CloseRequested;
    public AvaloniaMapStudioHost(Func<MapProject,CancellationToken,Task>? playtest = null,
        Action<System.Diagnostics.ProcessStartInfo>? radiantLauncher = null)
    {
        StudioGameAssets.InitializeAuthoringProcess();
        _screen = new MapStudioScreen();
        _screen.RadiantLauncher = radiantLauncher;
        _screen.NativePlaytest = playtest;
        _screen.DocumentChanged += OnChanged;
        _screen.Closed += OnClose;
        Content = _screen;
    }
    private void OnChanged() => Changed?.Invoke();
    private void OnClose(object? sender, EventArgs e) => CloseRequested?.Invoke(this,e);
    public void ConfigureSourceOwnership(Func<string,bool> canOpen,Action<string> requireSaveTarget,
        Action<string>? editGeometrySource = null)
    { _screen.NativeCanOpenSource=canOpen;_screen.NativeRequireSaveTarget=requireSaveTarget;_screen.NativeEditGeometrySource=editGeometrySource; }
    public void ConfigureGameServices(ICommunityCredentialSource credentials,
        Func<string,MapContentIdentity,CancellationToken,Task<MapDefinition>> install,
        Func<string,MapContentIdentity,string,CancellationToken,Task> host,
        Func<MapProject,CancellationToken,Task<MapAuditResult>> audit)
    {
        _screen.NativeCommunityCredentials=credentials;_screen.NativeInstall=install;
        _screen.NativeHost=host;_screen.NativeAudit=audit;
    }
    public void NewProject(string name = "Untitled Map", bool example = false) => _screen.Load(MapTemplates.Create(name,example));
    public void NewProjectInRadiant(string name = "Untitled Map", bool example = false)
    { NewProject(name,example);EditGeometryInRadiant(); }
    public void EditGeometryInRadiant() => _screen.EditCurrentInNetRadiant();
    public void OpenGeometryInRadiant(string path) => _screen.OpenGeometryInRadiant(path);
    public void ReviewRadiantGeometryChanges() => _screen.ReviewRadiantGeometryChanges();
    public void ShowNativeGeometryTools() => _screen.ShowNativeGeometryTools();
    public async Task OpenAsync(string path,CancellationToken cancellation = default)
    {
        var project = await Task.Run(()=>MapProjectSerializer.Load(path),cancellation);
        cancellation.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed,this);
        _screen.Load(project,path);
    }
    public Task SaveAsync(string path,CancellationToken cancellation = default) => _screen.StudioSaveAsync(path,cancellation);
    public Task PreserveRecoveryAsync(CancellationToken cancellation = default) => _screen.StudioRecoveryAsync(cancellation);
    public Task FlushAutosaveAsync(CancellationToken cancellation = default) => PreserveRecoveryAsync(cancellation);
    public async Task OpenRecoveryAsync(string path,CancellationToken cancellation = default)
    {
        var recovered = await Task.Run(()=>MapDocument.ReadRecovery(path),cancellation);
        cancellation.ThrowIfCancellationRequested();
        _screen.Load(recovered);
    }
    public void RestoreRecovery() => Document?.Restore(CustomRooms.UserMapDirectory);
    public void Undo() => Document?.History.Undo();
    public void Redo() => Document?.History.Redo();
    public void ShowPanel(string name) => _screen.StudioShowPanel(name);
    public void ToggleFourViews() => _screen.ToggleFourViews();
    public Task ValidateAsync() => _screen.StudioValidateAsync();
    public Task BuildAsync(bool package = true) => _screen.StudioBuildAsync(package);
    public Task PlaytestAsync() => _screen.StudioPlaytestAsync();
    public void FrameAll() => _screen.StudioFrameAll();
    public void ToggleWireframe() => _screen.StudioToggleWireframe();
    public static bool SupportsAssetDrop(string path) => Path.GetExtension(path).ToLowerInvariant() is ".pk3" or ".zip" or ".bsp" or ".json" or ".ppmap";
    public Task ImportDroppedFilesAsync(IReadOnlyList<string> paths,CancellationToken cancellation=default)
    { cancellation.ThrowIfCancellationRequested();_screen.StudioFilesDropped(paths);return Task.CompletedTask; }
    public void ShowStatus(string message) => _screen.ShowStatus(message);
    public async Task DiscardRecoveryAsync(CancellationToken cancellation=default)
    { await _screen.StudioDrainAsync(cancellation);Document?.DiscardRecovery(CustomRooms.UserMapDirectory); }
    public Task CancelPendingJobsAsync(CancellationToken cancellation=default) => _screen.StudioDrainAsync(cancellation);
    public void ResumeAssetThumbnails() { }
    public ValueTask DisposeAsync() => DisposeAsync(true);
    public async ValueTask DisposeAsync(bool preserveRecovery)
    {
        if (_disposed) return;
        if(preserveRecovery)await PreserveRecoveryAsync();
        _disposed=true;
        _screen.DocumentChanged-=OnChanged;_screen.Closed-=OnClose;
        await _screen.StudioDrainAsync();
        _screen.Dispose();Content=null;
    }
}
#endif
