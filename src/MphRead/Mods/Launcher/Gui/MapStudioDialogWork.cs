using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private readonly List<DialogWorkScope> _dialogWorkScopes=new();
    private readonly SemaphoreSlim _dialogWorkSlots=new(2,2);
    private DialogWorkScope CreateDialogWorkScope(Control content)
    {
        for(int i=_dialogWorkScopes.Count-1;i>=0;i--)
            if(_dialogWorkScopes[i].Retired){_dialogWorkScopes[i].Dispose();_dialogWorkScopes.RemoveAt(i);}
        var scope=new DialogWorkScope(_dialogWorkSlots,_services.RunJobAsync);
        _dialogWorkScopes.Add(scope);
        content.DetachedFromVisualTree+=(_,_)=>scope.Cancel();
        return scope;
    }
    private void CancelDialogWork()
    {foreach(var scope in _dialogWorkScopes)scope.Cancel();}
    private async Task DrainDialogWorkAsync()
    {
        CancelDialogWork();
        var scopes=_dialogWorkScopes.ToArray();
        foreach(var scope in scopes)await scope.Completion;
        foreach(var scope in scopes){scope.Dispose();_dialogWorkScopes.Remove(scope);}
    }
    /// <summary>At most one preflight per dialog, with two worker slots per editor.</summary>
    private sealed class DialogWorkScope(SemaphoreSlim slots,Func<string,Func<CancellationToken,Task>,CancellationToken,Task> runJob):IDisposable
    {
        private readonly CancellationTokenSource _cancellation=new();
        private Task _completion=Task.CompletedTask;
        private bool _disposed;
        public CancellationToken Token=>_cancellation.Token;
        public Task Completion=>_completion;
        public bool Retired=>_cancellation.IsCancellationRequested&&_completion.IsCompleted;
        public Task RunAsync(Func<CancellationToken,Task> work)
        {
            if(_disposed || _cancellation.IsCancellationRequested || !_completion.IsCompleted)return _completion;
            return _completion=RunCoreAsync(work);
        }
        private async Task RunCoreAsync(Func<CancellationToken,Task> work)
        {
            bool acquired=false;
            try
            {
                await runJob("Quake 3 import preflight",async token=>
                {
                    await slots.WaitAsync(token);acquired=true;
                    token.ThrowIfCancellationRequested();await work(token);
                },Token);
            }
            catch(OperationCanceledException){}
            finally{if(acquired)slots.Release();}
        }
        public void Cancel(){if(!_disposed)_cancellation.Cancel();}
        public void Dispose(){if(_disposed)return;_disposed=true;_cancellation.Dispose();}
    }
}
