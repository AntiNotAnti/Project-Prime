using ProjectPrime.Studio.Shell;

namespace ProjectPrime.Studio.Replay;

public sealed partial class ReplayStudioWorkspace
{
    private readonly List<Task> _ownedJobs=[];
    private CancellationTokenSource _jobLifetime=new();
    private long _jobGeneration;
    private bool _acceptingJobs=true;
    private Action? _applyAnalysisFilter;
    public MphRead.Mods.StudioReplay.StudioReplayAnalysis? Analysis=>_analysis;
    public bool HasPendingJobs=>_ownedJobs.Any(task=>!task.IsCompleted)||_session?.HasPendingTransportJobs==true;
    private Task RunDocumentJobAsync(string label,Func<CancellationToken,Task> work,Action? adopt=null)
    {
        if(_disposed || !_acceptingJobs || _session is null)throw new InvalidOperationException("This replay is saving or closing.");
        _ownedJobs.RemoveAll(task=>task.IsCompleted);
        if(_ownedJobs.Count>=2)throw new InvalidOperationException("Two replay operations are already running. Wait for one or cancel it in Jobs.");
        long generation=_jobGeneration;
        var task=RunCoreAsync();_ownedJobs.Add(task);return task;
        async Task RunCoreAsync()
        {
            using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(_jobLifetime.Token);
            if(_session.JobRunner is {} runJob)await runJob(label,work,cancellation.Token);
            else await work(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if(_disposed || !_acceptingJobs || generation!=_jobGeneration)throw new OperationCanceledException("The replay operation no longer belongs to this workspace.");
            adopt?.Invoke();
        }
    }
    public Task AnalyzeAsync()
    {
        MphRead.Mods.StudioReplay.StudioReplayAnalysis? proposed=null;
        return RunDocumentJobAsync("Analyze replay movement and combat",async token=>proposed=await _session!.Player.AnalyzeAsync(token),
            ()=>{_analysis=proposed;_applyAnalysisFilter?.Invoke();});
    }
    public Task ExtractClipAsync(string destination)
    {
        var range=Range();return RunDocumentJobAsync("Extract immutable replay clip",token=>_session!.Player.ExtractClipAsync(range.Start,range.End,destination,token));
    }
    public Task ExportPortableAsync(string destination)
        =>RunDocumentJobAsync("Export portable replay bundle",token=>_session!.Player.ExportPortableAsync(destination,cancellation:token));
    public Task ExportDiagnosticBundleAsync(string destination)
        =>RunDocumentJobAsync("Export replay diagnostic bundle",token=>_session!.Player.ExportDiagnosticBundleAsync(destination,
            ProjectPrime.Studio.Rendering.StudioGraphicsHost.Backend??"unavailable",token));
    /// <summary>Cancel and drain document work before its player or native host is replaced.</summary>
    public async Task PauseJobsAsync(CancellationToken cancellation=default)
    {
        _acceptingJobs=false;_jobGeneration++;_jobLifetime.Cancel();
        if(_session is not null)await _session.PauseTransportJobsAsync(cancellation);
        try{await Task.WhenAll(_ownedJobs.ToArray()).WaitAsync(cancellation);}
        catch(OperationCanceledException)when(!cancellation.IsCancellationRequested){}
        catch(Exception)when(!cancellation.IsCancellationRequested){ /* Each operation has its own visible error and central job result. */ }
        cancellation.ThrowIfCancellationRequested();
    }
    internal void ResumeJobs()
    {
        if(_disposed || _acceptingJobs)return;
        _jobLifetime.Dispose();_jobLifetime=new();_acceptingJobs=true;
        _session?.ResumeTransportJobs();
    }
    private void DisposeJobScope()
    {_acceptingJobs=false;_jobGeneration++;_jobLifetime.Cancel();_jobLifetime.Dispose();}
}
