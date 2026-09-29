using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private MapSourceWatchService? _sourceWatch;
    private readonly HashSet<string> _changedSources=new(StringComparer.Ordinal);
    private string _sourceWatchSignature="";
    private void RefreshSourceWatch()
    {
        if(_document==null||_detached)return;
        var definition=_document.Project.Definition;
        var sources=definition.ModelSources.SelectMany(s=>s.Dependencies.Count>0?s.Dependencies:new List<MapSourceDependency>{new(s.Source,"")}).ToList();
        sources.AddRange(definition.Assets.Where(a=>!string.IsNullOrEmpty(a.SourcePath)).Select(a=>new MapSourceDependency(a.SourcePath!,"")));
        if(definition.Collision?.Resolve() is {} collision)sources.Add(new(collision,""));
        if(definition.Import?.Resolve() is {} import)sources.Add(new(import,""));
        if(definition.BaseDirectory is {} root)sources.AddRange(definition.Materials.Where(m=>m.Texture!=null).Select(m=>new MapSourceDependency(Path.Combine(root,m.Texture!),"")));
        sources=sources.Where(s=>Path.IsPathFullyQualified(s.Path)).GroupBy(s=>s.Path).Select(g=>g.First()).OrderBy(s=>s.Path,StringComparer.Ordinal).ToList();
        string signature=string.Join("\n",sources.Select(s=>s.Path+"\0"+s.Hash));if(signature==_sourceWatchSignature&&_sourceWatch!=null)return;_sourceWatchSignature=signature;
        if(_sourceWatch==null)
        {
            _sourceWatch=new();var document=_document;var service=_sourceWatch;
            service.Changed+=path=>Dispatcher.UIThread.Post(()=>{if(_document!=document||_sourceWatch!=service||_detached)return;_changedSources.Add(path);_status.Text=$"SOURCE CHANGED · {Path.GetFileName(path)} · File → Source changes to review.";});
        }
        _sourceWatch.SetSources(sources.Select(s=>s.Hash.Length>0?s:new(s.Path,File.Exists(s.Path)?MapHash256.HashFile(s.Path).ToString():"missing")));
    }
    private void ShowSourceChanges()
    {
        if(_document==null)return;var panel=new StackPanel{Spacing=8};panel.Children.Add(Text("SOURCE CHANGES"));
        foreach(string path in _changedSources.Order(StringComparer.Ordinal).ToArray())
        {
            panel.Children.Add(Text(Path.GetFileName(path)+" changed externally."));
            var model=_document.Project.Definition.ModelSources.FirstOrDefault(s=>s.Source==path||s.Dependencies.Any(d=>d.Path==path));
            if(model!=null)
            {AddButton(panel,"Review changes",()=>{Dismiss();ModelImportOptions(model.Source,model);});AddButton(panel,"Reimport preview",()=>{Dismiss();AnalyzeModel(model.Source,model.Settings,model.Id);});}
            else
            {
                var asset=_document.Project.Definition.Assets.FirstOrDefault(a=>a.SourcePath==path);
                if(asset!=null)AddButton(panel,"Reload texture",()=>{Dismiss();_=ReplaceAsset(asset.Path,path);_changedSources.Remove(path);});
                else AddButton(panel,"Reload / validate",()=>{Dismiss();_viewport?.Cache.Invalidate(_document.Project.Definition,new(MapChangeDomain.Material|MapChangeDomain.Geometry));_viewport?.InvalidateVisual();_changedSources.Remove(path);_=PreviewImport();});
            }
            AddButton(panel,"Ignore this change",()=>{_changedSources.Remove(path);ShowSourceChanges();});
        }
        if(_changedSources.Count==0)panel.Children.Add(Text("No pending external source changes."));AddButton(panel,"Close",Dismiss);Modal(panel);
    }
}
