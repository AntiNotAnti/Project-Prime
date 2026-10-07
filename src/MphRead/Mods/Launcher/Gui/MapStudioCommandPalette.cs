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
        private sealed record StudioCommand(string Name,string Keywords,Action Run)
        {
            public override string ToString()=>Name;
        }

        private void ShowCommandPalette()
        {
            var panel=new StackPanel{Spacing=6,MinWidth=620};
            panel.Children.Add(Text("COMMAND PALETTE · CTRL+SHIFT+P"));
            var search=new TextBox{PlaceholderText="Type a command…"};panel.Children.Add(search);
            var list=new ListBox{MaxHeight=390};panel.Children.Add(list);
            StudioCommand[] commands=
            {
                new("Save project","file save ctrl s",Save),
                new("Pop out editor","window maximize",()=>_=PopOut()),
                new("Four views / single view","view layout top front side",ToggleFourViews),
                new("Community maps","online upload download share",ShowCommunity),
                new("Host current map","online lobby multiplayer",()=>_=PrepareOnline()),
                new("Validate map","check validation diagnostics",()=>_=Validate()),
                new("Build runtime map","compile build",()=>_=Build(false)),
                new("Build .ppmap package","package bundle",()=>_=Build(true)),
                new("Playtest from camera","play launch ctrl enter",()=>_=Play()),
                new("Run map audit","audit test",()=>_=Audit()),
                new("Undo","history ctrl z",()=>_document?.History.Undo()),
                new("Redo","history ctrl y",()=>_document?.History.Redo()),
                new("Frame all","camera view",()=>_viewport?.FrameAll()),
                new("Frame selection","camera focus f",()=>_viewport?.FrameSelection()),
                new("Tool: Move","transform g",()=>{if(_viewport!=null)_viewport.Tool="Move";}),
                new("Tool: Rotate","transform r",()=>{if(_viewport!=null)_viewport.Tool="Rotate";}),
                new("Tool: Scale","transform t",()=>{if(_viewport!=null)_viewport.Tool="Scale";}),
                new("Selection mode: Object","object mode 1",()=>{if(_viewport!=null){_viewport.ElementMode="Object";_viewport.ClearSubSelection();}}),
                new("Selection mode: Face","face polygon mode 2",()=>{if(_viewport!=null){_viewport.ElementMode="Face";_viewport.ClearSubSelection();}}),
                new("Selection mode: Edge","edge mode 3",()=>{if(_viewport!=null){_viewport.ElementMode="Edge";_viewport.ClearSubSelection();}}),
                new("Selection mode: Vertex","vertex point mode 4",()=>{if(_viewport!=null){_viewport.ElementMode="Vertex";_viewport.ClearSubSelection();}}),
                new("Placement: Cursor","place add cursor",()=>{if(_viewport!=null)_viewport.PlacementMode="Cursor";}),
                new("Placement: Camera target","place add target",()=>{if(_viewport!=null)_viewport.PlacementMode="Camera target";}),
                new("Placement: Surface","place add surface hit",()=>{if(_viewport!=null)_viewport.PlacementMode="Surface";}),
                new("Add Box","create geometry box",()=>AddObject("Box")),
                new("Add Wedge","create geometry ramp",()=>AddObject("Wedge")),
                new("Add Prism","create geometry prism",()=>AddObject("Prism")),
                new("Add Editable Mesh","create geometry mesh",()=>AddObject("Mesh")),
                new("Add Spawn","create gameplay spawn",()=>AddObject("Spawn")),
                new("Add Pickup","create gameplay item",()=>AddObject("Pickup")),
                new("Add Jump Pad","create gameplay jump",()=>AddObject("Jump pad")),
                new("Add Navigation Link","create navigation link",()=>AddObject("Navigation link")),
                new("Snap selection to grid","arrange snap grid",()=>{if(_document!=null)EditSelection("Snap to grid",(d,ids)=>MapLayoutCommands.Snap(d,ids,Math.Max(.01f,_viewport?.Snap??1)));}),
                new("Snap selection to floor","arrange floor",()=>{if(_viewport!=null)EditSelection("Snap to floor",(d,ids)=>MapLayoutCommands.SnapToFloor(d,ids,p=>_viewport.Cache.CollisionNear(p)));}),
                new("Snap selection to nearest surface","arrange surface",()=>{if(_viewport!=null)EditSelection("Snap to surface",(d,ids)=>MapLayoutCommands.SnapToSurface(d,ids,p=>_viewport.Cache.SurfaceNear(p),false));}),
                new("Snap + align selection to surface","arrange surface normal align",()=>{if(_viewport!=null)EditSelection("Align to surface",(d,ids)=>MapLayoutCommands.SnapToSurface(d,ids,p=>_viewport.Cache.SurfaceNear(p),true));}),
                new("Material eyedropper","material pick sample",()=>{if(_viewport!=null){_viewport.MaterialEyedropper=true;_status.Text="Material eyedropper active · click a surface.";}}),
                new("Toggle measurement tool","measure distance m",()=>{if(_viewport!=null){_viewport.MeasureMode=!_viewport.MeasureMode;_viewport.InvalidateVisual();}}),
                new("Toggle entity visualization","spawn capsule item jump trigger",()=>{if(_viewport!=null){_viewport.EntityVisualization=!_viewport.EntityVisualization;_viewport.InvalidateVisual();}}),
                new("Show Inspector","panel properties",()=>ShowInspectorPage("Inspector")),
                new("Show Modeling","panel mesh modeling",()=>ShowInspectorPage("Modeling")),
                new("Show Materials","panel material browser",()=>ShowInspectorPage("Materials")),
                new("Show Collision Auto-Heal review","panel collision repairs",()=>ShowInspectorPage("Collision repairs")),
                new("Show Layers","panel layers",()=>ShowInspectorPage("Layers")),
                new("Show Map Health","panel statistics budgets",()=>ShowInspectorPage("Map health")),
                new("Show Navigation Path","panel navigation",()=>ShowInspectorPage("Navigation path")),
                new("Overlay: Rendered","view render",()=>{if(_viewport!=null){_viewport.Wireframe=false;_viewport.Collision=false;_viewport.CollisionNormalsOverlay=false;_viewport.CollisionHeatmap=false;_viewport.CollisionRepairsOverlay=false;_viewport.InvalidateVisual();}}),
                new("Overlay: Wireframe","view wire",()=>{if(_viewport!=null){_viewport.Wireframe=true;_viewport.Collision=false;_viewport.CollisionNormalsOverlay=false;_viewport.InvalidateVisual();}}),
                new("Overlay: Collision","view collision",()=>{if(_viewport!=null){_viewport.Collision=true;_viewport.CollisionNormalsOverlay=false;_viewport.CollisionHeatmap=false;_viewport.CollisionRepairsOverlay=false;_viewport.InvalidateVisual();}}),
                new("Overlay: Collision Normals","view collision normals face direction",()=>{if(_viewport!=null){_viewport.Collision=true;_viewport.CollisionNormalsOverlay=true;_viewport.CollisionHeatmap=false;_viewport.CollisionRepairsOverlay=false;_viewport.InvalidateVisual();}}),
                new("Overlay: Collision Heat","view collision heat budget",()=>{if(_viewport!=null){_viewport.Collision=true;_viewport.CollisionNormalsOverlay=false;_viewport.CollisionHeatmap=true;_viewport.CollisionRepairsOverlay=false;_viewport.InvalidateVisual();}}),
                new("Overlay: Collision Repairs","view collision repairs heal topology movement",()=>{if(_viewport!=null){_viewport.Collision=true;_viewport.CollisionNormalsOverlay=false;_viewport.CollisionHeatmap=false;_viewport.CollisionRepairsOverlay=true;_viewport.InvalidateVisual();}}),
                new("New map / template gallery","new template",NewMap),
                new("Import Q3 BSP / PK3","import bsp pk3",Import),
                new("Clone built-in map","native remix clone",CloneBuiltIn)
            };
            if(_services.IsStandalone)commands=commands.Where(command=>command.Name!="Pop out editor").ToArray();
            commands=commands.Concat(ViewportModes.Select(mode=>new StudioCommand("Viewport: "+mode,"view overlay rendering diagnostics "+mode,()=>SetViewportMode(mode)))).ToArray();
            void Refresh()
            {
                string query=(search.Text??"").Trim();
                StudioCommand[] matches=commands.Where(command=>query.Length==0
                    ||command.Name.Contains(query,StringComparison.OrdinalIgnoreCase)
                    ||command.Keywords.Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
                list.ItemsSource=matches;
                if(matches.Length>0&&list.SelectedIndex<0)list.SelectedIndex=0;
            }
            void Run()
            {
                if(list.SelectedItem is not StudioCommand command)return;
                Dismiss();command.Run();
            }
            search.TextChanged+=(_,_)=>Refresh();
            list.DoubleTapped+=(_,_)=>Run();
            var buttons=new WrapPanel();AddButton(buttons,"Run",Run);AddButton(buttons,"Cancel",Dismiss);panel.Children.Add(buttons);
            Refresh();Modal(panel);search.Focus();
        }

    }
}
