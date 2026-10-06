using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media.Imaging;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MphRead.Mods.MapEditor;
using MphRead.AvaloniaShared;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;

namespace MphRead.Mods.Launcher.Gui
{
    internal sealed partial class MapStudioScreen
    {
        private async Task Work(string label,Func<MapProject,CancellationToken,Task> action)
        {
            if(_document==null||_work!=null)return;var snapshot=_document.CaptureBuildSnapshot();await Job(label,async token=>{var project=await Task.Run(()=>new MapProject(snapshot.CreateDefinition()),token);GuardJob(token);await action(project,token);});
        }
        private async Task Job(string label,Func<CancellationToken,Task> action)
        {
            if(_work!=null||_detached||_poppedOut)return;var work=new CancellationTokenSource();_work=work; _jobCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
            _jobDocument=_document;_jobState=_document?.CurrentStateId;_jobGeneration=_editorGeneration;long generation=_editorGeneration;
            _status.Text=label+"…";
            SetBusy(true);
            try{await _services.RunJobAsync(label,action,work.Token);}catch(OperationCanceledException){if(!_detached&&generation==_editorGeneration)_status.Text="Cancelled.";}catch(Exception ex){if(!_detached&&generation==_editorGeneration)Failure(ex);}finally{_jobCompletion?.TrySetResult();work.Dispose();if(_work==work){_work=null;if(!_detached)SetBusy(false);}}
        }
        private void SetBusy(bool busy)
        {
            foreach(var control in _editingControls)control.IsEnabled=!busy&&!_poppedOut;
            if(_cancelJob!=null)_cancelJob.IsVisible=busy;
        }
    }
}
