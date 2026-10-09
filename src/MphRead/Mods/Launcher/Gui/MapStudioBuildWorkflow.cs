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
        private Task PreviewImport()=>Work("Preparing source map preview",async(p,token)=>
        {
            if(p.Definition.Import==null&&p.Definition.NativeRoom==null)return;
            int authoredDetail=p.Definition.Import?.PatchLevel??0;
            if(p.Definition.Import!=null)p.Definition.Import.PatchLevel=1;
            var result=await _services.BuildScheduler.AnalyzeAsync(MapBuildSnapshot.Capture(p),cancellation:token);
            GuardJob(token);Problems(result.Validation());
            if(result.Faces.Length>0)
            {
                foreach(var view in _views)view.SetImported(result);
                string kind=p.Definition.NativeRoom!=null?"Native room":"Imported map";
                _status.Text=result.Succeeded
                    ? $"{kind} preview ready"+(authoredDetail>0?$" · runtime patch detail {authoredDetail}":"")
                        +(_importWarnings.Length>0?$" · {_importWarnings.Length} gameplay warnings in Problems":"")
                    : $"{kind} preview ready · validation problems need attention";
            }
        });
        private Task Validate()=>Work("Validating",async(p,token)=>
        {
            var result=await _services.BuildScheduler.AnalyzeAsync(MapBuildSnapshot.Capture(p),cancellation:token);
            GuardJob(token);Problems(result.Validation());
            // Invalid runtime budgets should not make the authoring viewport
            // disappear. If geometry compiled, show it and keep the errors as
            // build blockers.
            if((p.Definition.Import!=null||p.Definition.NativeRoom!=null)&&result.Faces.Length>0)foreach(var view in _views)view.SetImported(result);
        });
        private Task Navigation()=>Work("Generating navigation",async(p,token)=>
        {
            var result=await _services.BuildScheduler.AnalyzeAsync(MapBuildSnapshot.Capture(p),navigation:true,cancellation:token);
            GuardJob(token);Problems(result.Validation());if(!result.Succeeded)return;
            var graph=result.CreateNavigation();if(graph==null)return;
            if(_viewport!=null){_viewport.Navigation=graph;_viewport.InvalidateVisual();}
            int components=graph.Components.Distinct().Count();_status.Text=$"{graph.Positions.Length} navigation nodes · {graph.Edges} edges · {components} connected regions";
        });
        private Task Build(bool package)
        {
            if(package)CapturePreview();
            return Work(package?"Building package":"Building map",async(p,token)=>
        {
            if(package)
            {
                string output=Path.ChangeExtension(_path.Text??Path.Combine(_services.MapLibraryDirectory,p.Definition.Name),".ppmap");
                string path=await _services.BuildScheduler.PackageAsync(MapBuildSnapshot.Capture(p),output,token);
                GuardJob(token);_status.Text="Package built: "+path;
            }
            else
            {
                if(!_services.IsStandalone){if(!_services.GameFilesReady)throw new IOException("Set up game files in Settings before building runtime files.");_services.ApplyGamePaths();}
                var built = await _services.BuildScheduler.BuildAsync(MapBuildSnapshot.Capture(p), token);
                GuardJob(token); _lastBuild = built;
                 Problems(built.Validation()); if (!built.Succeeded) return;
                await _services.PublishBuildAsync(built,p.Definition,token);
                GuardJob(token);_status.Text=$"Runtime map ready · {(built.CacheHit ? "cache hit" : "compiled")} · {built.Milliseconds:0} ms";
            }
        });
        }
        private void PlaytestInspector()
        {
            _inspector.Children.Clear(); _inspector.Children.Add(Text("PLAYTEST START"));
            AddButton(_inspector,"From default authored spawns",()=>_=Play(useCamera:false));
            AddButton(_inspector,"From camera",()=>_=Play());
            AddButton(_inspector,"From selected spawn",()=>
            {
                var spawn=_document?.Project.Definition.Spawns.FirstOrDefault(s=>_document.Selection.Contains(s.Id));
                if(spawn==null){_status.Text="Select a spawn first.";return;} _=Play(spawn);
            });
            foreach(int team in new[]{0,1}) AddButton(_inspector,team==0?"From Team A spawn":"From Team B spawn",()=>
            {
                var spawn=_document?.Project.Definition.Spawns.FirstOrDefault(s=>s.Team==team);
                if(spawn==null){_status.Text="No spawn exists for this team.";return;} _=Play(spawn);
            });
        }
        private Task Play(MapSpawn? start=null,bool useCamera=true)
        {
            MapSpawn? selected=start is null ? null : MapSnapshotCopy.Copy(start);
            var cameraPosition=_viewport?.CameraPosition;var cameraTarget=_viewport?.CameraTarget;
            return Work("Preparing playtest",async(p,token)=>
            {
            if(!_services.IsStandalone){if(!_services.GameFilesReady)throw new IOException("Set up game files in Settings before playtesting.");_services.ApplyGamePaths();}
            p.Definition.Name=_previewName;p.Definition.SourcePath=null;
            if(selected!=null || useCamera)
            {
                p.Definition.Capabilities=null;
                if(p.Definition.Import!=null)p.Definition.Import.KeepSpawns=false;
                if(selected!=null){p.Definition.Spawns.Clear();p.Definition.Spawns.Add(selected);}
                else if(cameraPosition is {} pos)
                {
                    float yaw=0;if(cameraTarget is {} target){var forward=target-pos;yaw=MathF.Atan2(forward.X,forward.Z)*180/MathF.PI;}
                    p.Definition.Spawns.Clear();p.Definition.Spawns.Add(new(){Position=new[]{pos.X,pos.Y,pos.Z},Yaw=yaw});
                }
            }
            var result=await _services.BuildScheduler.BuildAsync(MapBuildSnapshot.Capture(p),token);
            GuardJob(token);Problems(result.Validation());if(!result.Succeeded)return;
            await _services.PublishBuildAsync(result,p.Definition,token);
            GuardJob(token);
            if (_services.IsStandalone)
            {
                _status.Text="Waiting for Project Prime to load the playtest…";
                await _services.RequestPlaytestAsync(p,token);
                GuardJob(token);_status.Text="Playtest started in Project Prime.";
            }
            else PlayRequested?.Invoke(this,p.Definition);
            });
        }
        private Task Audit()=>Work("Running map audit",async(p,token)=>
        {
            if(!_services.IsStandalone&&!_services.GameFilesReady)throw new IOException("Set up game files before running a map audit.");
            var result=await _services.AuditAsync(p,token);GuardJob(token);_status.Text=result.Passed?"Map audit passed.":"Map audit failed.";
            _problems.ItemsSource=result.Lines;
        });
    }
}
