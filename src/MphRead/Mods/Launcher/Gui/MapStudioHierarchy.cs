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
        private sealed record HierarchyRow(string Group,MapObject? Object,bool Header)
        {
            public override string ToString()=>Header?Group:Object?.ToString()??Group;
        }

        private void Changed()
        {
            DocumentChanged?.Invoke();
            try { RefreshSourceWatch(); } catch (IOException ex) { _status.Text="Source watch: "+ex.Message; }
            RefreshHierarchy();
            ShowInspectorPage(_inspectorPage, remember:false);
            RefreshStudioChrome();
            _status.Text=(_document?.IsDirty==true?"Unsaved changes · ":"")
                +"RMB orbit · MMB pan · WASD/QE fly · 1–4 modes · G/R/S transforms · E/I/B model · M merge · F fill (object mode: F focus, M measure) · Ctrl+Shift+P commands";
        }
        private void RefreshHierarchy(bool force=false)
        {
            if(_document==null)return;_refreshing=true;
            try
            {
                var definition=_document.Project.Definition;
                string? previousFilter=_hierarchyFilter.SelectedItem as string;
                string[] layers=definition.Geometry.Select(g=>String.IsNullOrWhiteSpace(g.Layer)?"Architecture":g.Layer)
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
                string[] filters=new[]{"All","Geometry","Spawns","Pickups","Jump Pads","Navigation"}
                    .Concat(layers.Select(layer=>"Layer: "+layer)).ToArray();
                if(_hierarchyFilter.ItemsSource is not string[] current||!current.SequenceEqual(filters))
                {
                    _hierarchyFilter.ItemsSource=filters;
                    _hierarchyFilter.SelectedItem=filters.Contains(previousFilter??"",StringComparer.OrdinalIgnoreCase)
                        ?previousFilter:"All";
                }
                string filter=_hierarchyFilter.SelectedItem as string??"All";
                string search=_search.Text??"";

                string Group(MapObject o)=>o.Value switch
                {
                    MapGeometry g=>$"Geometry · {(String.IsNullOrWhiteSpace(g.Layer)?"Architecture":g.Layer)}",
                    MapBrush=>"Geometry · Legacy",
                    MapSpawn=>"Spawns",
                    MapItem=>"Pickups",
                    MapJumpPad=>"Jump Pads",
                    MapNavigationLink=>"Navigation",
                    _=>"Other"
                };
                bool Match(MapObject o)
                {
                    if(!o.ToString().Contains(search,StringComparison.OrdinalIgnoreCase))return false;
                    if(filter=="All")return true;
                    if(filter=="Geometry")return o.Value is MapGeometry or MapBrush;
                    if(filter=="Spawns")return o.Value is MapSpawn;
                    if(filter=="Pickups")return o.Value is MapItem;
                    if(filter=="Jump Pads")return o.Value is MapJumpPad;
                    if(filter=="Navigation")return o.Value is MapNavigationLink;
                    if(filter.StartsWith("Layer: ",StringComparison.Ordinal))
                    {
                        string layer=filter[7..];
                        return o.Value is MapGeometry g
                            && (String.IsNullOrWhiteSpace(g.Layer)?"Architecture":g.Layer)
                                .Equals(layer,StringComparison.OrdinalIgnoreCase);
                    }
                    return true;
                }

                MapObject[] objects=MapObjects.All(definition).Where(Match).ToArray();
                var rows=new List<HierarchyRow>();
                foreach(var group in objects.GroupBy(Group).OrderBy(g=>g.Key,StringComparer.OrdinalIgnoreCase))
                {
                    rows.Add(new(group.Key,null,true));
                    rows.AddRange(group.OrderBy(o=>o.ToString(),StringComparer.OrdinalIgnoreCase)
                        .Select(o=>new HierarchyRow(group.Key,o,false)));
                }

                string signature=filter+"|"+search+"|"+string.Join("|",objects.Select(o=>o.Id+":"+o.ToString()+":"+Group(o)));
                if(force||signature!=_hierarchySignature)
                {
                    _hierarchySignature=signature;
                    _hierarchy.ItemsSource=rows;
                }
                _hierarchy.SelectedItems?.Clear();
                foreach(var row in rows.Where(row=>row.Object!=null&&_document.Selection.Contains(row.Object.Id)))
                    _hierarchy.SelectedItems?.Add(row);
            }
            finally{_refreshing=false;}
        }
        private void EditSelection(string label,Action<MapDefinition,ISet<Guid>> edit)
        {if(_document==null)return;var ids=_document.Selection.ToHashSet();_document.EditObjects(label,ids,d=>edit(d,ids));}
    }
}
