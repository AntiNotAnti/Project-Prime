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
        private void EnvironmentInspector()
        {
            _inspector.Children.Clear();if(_document==null)return;var d=_document.Project.Definition;_inspector.Children.Add(Text("PROJECT & ENVIRONMENT"));var edits=new List<Action<MapDefinition>>();
            void Field(string label,string value,Action<MapDefinition,string> apply){_inspector.Children.Add(Text(label));var input=new TextBox{Text=value};_inspector.Children.Add(input);edits.Add(map=>apply(map,input.Text??""));}
            Field("Runtime name",d.Name,(m,s)=>{MapValidator.RequireRuntimeName(s);m.Name=s;});Field("Display name",d.InGameName??d.Name,(m,s)=>m.InGameName=s);Field("Author",d.Author??"",(m,s)=>m.Author=s);Field("Version",d.Version??"",(m,s)=>m.Version=s);
            Field("Kill height",d.KillHeight.ToString(CultureInfo.InvariantCulture),(m,s)=>m.KillHeight=Number(s));Field("Far clip",d.FarClip.ToString(CultureInfo.InvariantCulture),(m,s)=>m.FarClip=Number(s));
            Field("Supported modes (comma separated)",string.Join(",",d.Capabilities?.SupportedModes??new(){"Battle","Survival"}),(m,s)=>{m.Capabilities??=new();m.Capabilities.SupportedModes=s.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).ToList();});
            Field("Hardpoint order (objective IDs, comma separated)", string.Join(",", d.HardpointOrder ?? new()),
                (m,s)=>m.HardpointOrder=s.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)
                    .Select(value=>int.Parse(value,CultureInfo.InvariantCulture)).ToList());
            Field("Light 1 color (0–31)",string.Join(",",d.Light1Color),(m,s)=>m.Light1Color=ParseVector(s,3).Select(v=>(int)v).ToArray());
            Field("Light 1 direction",string.Join(",",d.Light1Vector),(m,s)=>m.Light1Vector=ParseVector(s,3));
            Field("Light 2 color (0–31)",string.Join(",",d.Light2Color),(m,s)=>m.Light2Color=ParseVector(s,3).Select(v=>(int)v).ToArray());
            Field("Fog color (0–31)",string.Join(",",d.FogColor),(m,s)=>m.FogColor=ParseVector(s,3).Select(v=>(int)v).ToArray());
            if(d.NativeRoom is {} native)
            {
                _inspector.Children.Add(Text("NATIVE ROOM REMIX"));
                _inspector.Children.Add(Text($"Source: {native.Room}\nOriginal architecture is preserved from the extracted game files and never overwritten."));
                foreach(var pair in new[]{
                    ("Render native architecture",native.UseNativeArchitecture),
                    ("Preserve unsupported/native entities",native.PreserveEntities),
                    ("Editable native spawns",native.EditableSpawns),
                    ("Editable native pickups",native.EditableItems),
                    ("Use native collision",native.UseNativeCollision),
                    ("Multiplayer layer only",native.MultiplayerLayerOnly)})
                {
                    var check=new CheckBox{Content=pair.Item1,IsChecked=pair.Item2};_inspector.Children.Add(check);
                    edits.Add(m=>
                    {
                        var n=m.NativeRoom!;
                        switch(pair.Item1)
                        {
                            case "Render native architecture":n.UseNativeArchitecture=check.IsChecked==true;break;
                            case "Preserve unsupported/native entities":n.PreserveEntities=check.IsChecked==true;break;
                            case "Editable native spawns":n.EditableSpawns=check.IsChecked==true;break;
                            case "Editable native pickups":n.EditableItems=check.IsChecked==true;break;
                            case "Use native collision":n.UseNativeCollision=check.IsChecked==true;break;
                            case "Multiplayer layer only":n.MultiplayerLayerOnly=check.IsChecked==true;break;
                        }
                    });
                }
                if(native.UseNativeArchitecture)
                {
                    _inspector.Children.Add(Text("Native render architecture is currently an immutable source layer. Detach it to convert the source display lists into editable Project Prime meshes while retaining source materials/UVs and native collision/entities."));
                    AddButton(_inspector,"Detach native architecture for editing",()=>_=DetachNativeArchitecture());
                }
                else
                {
                    _inspector.Children.Add(Text("Native render architecture is detached. The authored meshes in layer 'Native Detached' are now the visible source geometry; native collision/entities can remain preserved independently."));
                }
                _inspector.Children.Add(Text("Add Project Prime boxes, wedges, prisms, meshes, prefabs, spawns, pickups and navigation normally."));
            }
            if(d.Import is {} import)
            {
                _inspector.Children.Add(Text("IMPORTED ARCHITECTURE + HYBRID AUTHORING"));
                Field("Q3 units per world unit",import.UnitsPerUnit.ToString(CultureInfo.InvariantCulture),(m,s)=>m.Import!.UnitsPerUnit=Number(s));
                Field("Patch detail (1–8)",import.PatchLevel.ToString(),(m,s)=>m.Import!.PatchLevel=int.Parse(s,CultureInfo.InvariantCulture));
                var collisionPatch=new ComboBox
                {
                    ItemsSource=new[]{"Auto","Off","Level 1","Level 2","Level 3","Level 4","Level 5","Level 6","Level 7","Level 8"},
                    SelectedIndex=import.CollisionPatchLevel<0?0:import.CollisionPatchLevel+1
                };
                _inspector.Children.Add(Text("Patch collision"));
                _inspector.Children.Add(collisionPatch);
                edits.Add(m=>m.Import!.CollisionPatchLevel=collisionPatch.SelectedIndex==0?-1:collisionPatch.SelectedIndex-1);
                _inspector.Children.Add(Text("Auto keeps full patch collision when it fits, then tries level 1, then structural BSP brushes/clips only."));
                var autoHeal=new CheckBox{Content="Auto-heal imported collision",IsChecked=import.AutoHealCollision};
                var healTolerance=new TextBox{Text=import.CollisionHealTolerance.ToString(CultureInfo.InvariantCulture)};
                _inspector.Children.Add(autoHeal);
                _inspector.Children.Add(Text("Collision seam heal tolerance (0.0005–0.25)"));
                _inspector.Children.Add(healTolerance);
                _inspector.Children.Add(Text("Auto Heal snaps collision to runtime precision, repairs invalid polygons and seams, restores dropped floor support, creates safe floor proxies, validates spawns and runs reachability/probe checks."));
                edits.Add(m=>{m.Import!.AutoHealCollision=autoHeal.IsChecked==true;m.Import.CollisionHealTolerance=Number(healTolerance.Text??"");});
                if(_viewport?.Cache.CollisionHealth is {} collisionHealth)
                {
                    _inspector.Children.Add(Text(
                        $"AUTO-HEAL HEALTH · {collisionHealth.Confidence*100:0.0}%\n"
                        +$"{collisionHealth.OutputFaces:N0} final faces · {collisionHealth.StitchedVertices:N0} seam welds · {collisionHealth.TJunctions:N0} T-junctions\n"
                        +$"{collisionHealth.RestoredBuriedFaces:N0} buried faces restored · {collisionHealth.FloorProxies:N0} floor proxies · {collisionHealth.PhantomFacesRemoved:N0} phantom faces removed\n"
                        +$"{collisionHealth.SpawnsMoved:N0} spawns moved · {collisionHealth.ProbeFailures:N0}/{collisionHealth.ProbeCount:N0} floor probes failed · {collisionHealth.SweepFailures:N0}/{collisionHealth.SweepCount:N0} walk sweeps failed"));
                    AddButton(_inspector,"Show collision repairs",()=>{if(_viewport!=null){_viewport.Collision=true;_viewport.CollisionRepairsOverlay=true;_viewport.InvalidateVisual();}});
                }
                foreach(var pair in new[]{("Use source spawns",import.KeepSpawns),("Keep player clips",import.KeepClip),("Keep sky",import.KeepSky),("Keep source pickups",import.KeepItems)})
                {
                    var check=new CheckBox{Content=pair.Item1,IsChecked=pair.Item2};_inspector.Children.Add(check);
                    edits.Add(m=>{switch(pair.Item1){case "Use source spawns":m.Import!.KeepSpawns=check.IsChecked==true;break;case "Keep player clips":m.Import!.KeepClip=check.IsChecked==true;break;case "Keep sky":m.Import!.KeepSky=check.IsChecked==true;break;case "Keep source pickups":m.Import!.KeepItems=check.IsChecked==true;break;}});
                }
                _inspector.Children.Add(Text("Imported BSP surfaces stay immutable. Boxes, wedges, prisms and convex brushes can be layered on top."));
                string provenanceRoot=d.BaseDirectory??Path.GetDirectoryName(d.SourcePath??"")??_services.MapLibraryDirectory;
                if(Q3ImportManifest.Load(provenanceRoot) is {} provenance)
                {
                    _inspector.Children.Add(Text("Q3 SOURCE PROVENANCE\n"+provenance.Summary()));
                    AddButton(_inspector,"Show unresolved textures",()=>
                    {
                        string[] missing=provenance.Textures.Where(t=>t.Fallback).Select(t=>t.Shader).ToArray();
                        _status.Text=missing.Length==0?"All imported textures resolved.":String.Join(" · ",missing.Take(20))+(missing.Length>20?$" · +{missing.Length-20} more":"");
                    });
                }
                AddButton(_inspector,"Rebake Q3 textures",()=>_=RebakeImportTextures());
                AddButton(_inspector,"Reimport Q3 source",()=>_=PickReimportSource());
            }
            var fog=new CheckBox {Content="Fog enabled",IsChecked=d.FogEnabled};_inspector.Children.Add(fog);edits.Add(m=>m.FogEnabled=fog.IsChecked==true);
            AddButton(_inspector,"Apply",()=>{try{_document.Edit("Environment",map=>{foreach(var edit in edits)edit(map);},MapChangeDomain.Environment | MapChangeDomain.Metadata | (d.Import != null || d.NativeRoom != null ? MapChangeDomain.Import : MapChangeDomain.None));}catch(Exception ex){Failure(ex);}});
            AddButton(_inspector,"Upgrade project",()=>_document.Upgrade());
            AddButton(_inspector,"Use camera as preview",()=>{if(_viewport!=null){var p=_viewport.CameraPosition;var t=_viewport.CameraTarget;_document.Edit("Preview camera",m=>m.Preview=new(){Position=new[]{p.X,p.Y,p.Z},Target=new[]{t.X,t.Y,t.Z}});}});
        }
        private Task DetachNativeArchitecture()=>Job("Detaching native architecture",async token=>
        {
            if(_document?.Project.Definition.NativeRoom is not {UseNativeArchitecture:true})return;
            if(!_services.GameFilesReady)throw new IOException("Set up game files before detaching native architecture.");
            _services.ApplyGamePaths();
            MapDefinition snapshot=_document.CaptureBuildSnapshot().CreateDefinition();
            IReadOnlyList<MapMesh> meshes=await Task.Run(()=>NativeRoomImport.DetachArchitecture(snapshot,token),token);
            GuardJob(token);
            if(meshes.Count==0)throw new InvalidDataException("The source room produced no detachable render meshes.");
            _document.Edit("Detach native architecture",d=>
            {
                d.Geometry.AddRange(meshes);
                d.NativeRoom!.UseNativeArchitecture=false;
            },MapChangeDomain.Geometry|MapChangeDomain.Import|MapChangeDomain.Material);
            _document.Selection.Clear();
            foreach(Guid id in meshes.Take(1).Select(m=>m.Id))_document.Selection.Add(id);
            _document.ActiveObjectId=_document.Selection.FirstOrDefault();
            _document.SelectionChanged();
            _status.Text=$"Detached {meshes.Count:N0} native render meshes. Source collision/entities remain preserved.";
            _=Validate();
        });

    }
}
