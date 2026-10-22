#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Campaign;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Adventure;

internal interface ICampaignCreditsLoadJob : IDisposable
{
    Task<CampaignCreditsCatalog> Completion { get; }
    bool Current { get; }
}

// Captures mutable configuration on the UI owner. The worker sees only a value
// identity and cancellation; the configured-files capability spans its lifetime.
internal sealed class CampaignCreditsLoadJob : ICampaignCreditsLoadJob
{
    private readonly string _configuration;
    private readonly CampaignCreditsSourceIdentity _source;
    private readonly CancellationTokenSource _cancel = new();
    private readonly GameFiles.ConfiguredFilesUse _files;
    private int _disposed;
    public Task<CampaignCreditsCatalog> Completion { get; }
    public bool Current => Volatile.Read(ref _disposed) == 0 && GameFiles.Root == _configuration
        && Paths.MphKey == _source.Edition && Paths.FileSystem == _source.Root && Scene.Language == _source.Language;

    private CampaignCreditsLoadJob(string configuration, CampaignCreditsSourceIdentity source, GameFiles.ConfiguredFilesUse files)
    {
        _configuration = configuration; _source = source; _files = files;
        Completion = Task.Run(() => CampaignCreditsLoad.Read(source, _cancel.Token));
    }
    internal static ICampaignCreditsLoadJob? TryStart()
    {
        string configuration = GameFiles.Root;
        var files = GameFiles.TryUseConfiguredFiles(configuration);
        if (files == null) return null;
        try
        {
            var source = new CampaignCreditsSourceIdentity(Paths.FileSystem, Paths.MphKey, Scene.Language);
            if (!CampaignCreditsLoad.Supports(source)) { files.Dispose(); return null; }
            return new CampaignCreditsLoadJob(configuration, source, files);
        }
        catch { files.Dispose(); throw; }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cancel.Cancel();
        // Never wait on the UI thread. Observe faults and release the capability
        // only after a canceled worker has actually stopped using its inputs.
        _ = Completion.ContinueWith(task =>
        {
            _ = task.Exception;
            _files.Dispose(); _cancel.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
#endif
