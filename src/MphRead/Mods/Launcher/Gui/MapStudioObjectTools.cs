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
        internal void ShowPrimitiveDialog(string kind)
        {
            if(kind is not ("Box" or "Wedge" or "Prism" or "Convex" or "Mesh"))throw new ArgumentException("Choose Box, Wedge, Prism, Convex, or Mesh.",nameof(kind));
            if(_document is not {} document || _detached)throw new InvalidOperationException("Open a map before creating geometry.");
            if(_work!=null)throw new InvalidOperationException("Wait for the current map operation or cancel it first.");
            var state=document.CurrentStateId;long generation=_editorGeneration;
            var point=_viewport?.GetPlacementPoint()??System.Numerics.Vector3.Zero;
            float y=point.Y+(kind is "Box" or "Wedge" or "Prism"?1:0);
            float[] initialScale=kind switch {"Box"=>new[]{4f,2,4},"Wedge"=>new[]{4f,2,6},"Prism"=>new[]{3f,2,3},_=>new[]{1f,1,1}};
            var panel=new StackPanel {Spacing=8,MinWidth=400};panel.Children.Add(Text("CREATE "+kind.ToUpperInvariant()));
            var name=new TextBox {Name="PrimitiveName",Text=kind=="Wedge"?"Ramp":kind=="Mesh"?"Editable mesh":kind};
            var position=new TextBox {Name="PrimitivePosition",Text=string.Join(", ",new[]{point.X,y,point.Z}.Select(value=>value.ToString("R",CultureInfo.InvariantCulture)))};
            var scale=new TextBox {Name="PrimitiveScale",Text=string.Join(", ",initialScale.Select(value=>value.ToString("R",CultureInfo.InvariantCulture)))};
            panel.Children.Add(Text("Name"));panel.Children.Add(name);
            panel.Children.Add(Text("Position · X, Y, Z"));panel.Children.Add(position);
            panel.Children.Add(Text("Scale · X, Y, Z"));panel.Children.Add(scale);
            var error=Text("");error.Name="PrimitiveError";panel.Children.Add(error);
            AddButton(panel,"Create",()=>
            {
                try
                {
                    if(_detached || _document!=document || generation!=_editorGeneration || document.CurrentStateId!=state || _work!=null)
                        throw new InvalidOperationException("This map changed while the dialog was open. Cancel and open Create again.");
                    string label=(name.Text??"").Trim();if(label.Length==0||label.Length>128)throw new ArgumentException("Enter a name between 1 and 128 characters.");
                    float[] Parse(string? text,bool positive)
                    {
                        var values=(text??"").Split(',',StringSplitOptions.TrimEntries);
                        if(values.Length!=3)throw new ArgumentException("Enter three comma-separated coordinates.");
                        var result=values.Select(value=>float.Parse(value,CultureInfo.InvariantCulture)).ToArray();
                        if(result.Any(value=>!float.IsFinite(value)||(positive&&value<=0)))throw new ArgumentException(positive?"Scale must contain three finite positive numbers.":"Position must contain three finite numbers.");
                        return result;
                    }
                    AddObject(kind,new MapTransform {Position=Parse(position.Text,false),Scale=Parse(scale.Text,true)},label);Dismiss();
                }
                catch(Exception failure){error.Text=failure.Message;}
            });
            AddButton(panel,"Cancel",Dismiss);Modal(panel,fitContent:true);
        }
        private void AddObject(string kind,MapTransform? transform=null,string? label=null)
        {
            if(_document==null)return;
            System.Numerics.Vector3 place=_viewport?.GetPlacementPoint()??System.Numerics.Vector3.Zero;
            Guid created=Guid.Empty;
            _document.EditObjects("Create "+kind,Array.Empty<Guid>(),d=>
            {
                switch(kind)
                {
                    case "Box":
                    {
                        var value=new MapBox{Label="Box",Transform=new(){Position=new[]{place.X,place.Y+1,place.Z},Scale=new[]{4f,2,4}}};
                        created=value.Id;d.Geometry.Add(value);break;
                    }
                    case "Wedge":
                    {
                        var value=new MapWedge{Label="Ramp",Transform=new(){Position=new[]{place.X,place.Y+1,place.Z},Scale=new[]{4f,2,6}}};
                        created=value.Id;d.Geometry.Add(value);break;
                    }
                    case "Prism":
                    {
                        var value=new MapPrism{Label="Prism",Transform=new(){Position=new[]{place.X,place.Y+1,place.Z},Scale=new[]{3f,2,3}}};
                        created=value.Id;d.Geometry.Add(value);break;
                    }
                    case "Convex":
                    {
                        var value=new MapConvexBrush{Label="Convex brush",Transform=new(){Position=new[]{place.X,place.Y,place.Z}},
                            Vertices=new(){new[]{-1f,0,-1},new[]{1f,0,-1},new[]{0f,2,0},new[]{0f,0,1}},
                            Faces=new(){new[]{0,1,2},new[]{0,1,3},new[]{0,2,3},new[]{1,2,3}}};
                        created=value.Id;d.Geometry.Add(value);break;
                    }
                    case "Mesh":
                    {
                        var value=new MapMesh{Label="Editable mesh",Transform=new(){Position=new[]{place.X,place.Y,place.Z}},
                            Vertices=new(){new[]{-2f,0,-2},new[]{2f,0,-2},new[]{2f,0,2},new[]{-2f,0,2}},
                            Faces=new(){new[]{0,1,2,3}},FaceMaterials=new(){0},Solid=false};
                        created=value.Id;d.Geometry.Add(value);break;
                    }
                    case "Spawn":
                    {
                        var value=new MapSpawn{Id=Guid.NewGuid(),Position=new[]{place.X,place.Y+.1f,place.Z}};
                        created=value.Id;d.Spawns.Add(value);break;
                    }
                    case "Pickup":
                    {
                        var value=new MapItem{Id=Guid.NewGuid(),Type="HealthMedium",Position=new[]{place.X,place.Y+.1f,place.Z}};
                        created=value.Id;d.Items.Add(value);break;
                    }
                    case "Jump pad":
                    {
                        var value=new MapJumpPad{Id=Guid.NewGuid(),Position=new[]{place.X,place.Y+.1f,place.Z},
                            Target=new[]{place.X+8,place.Y+2,place.Z}};
                        created=value.Id;d.JumpPads.Add(value);break;
                    }
                    case "Navigation link":
                    {
                        var value=new MapNavigationLink{From=new[]{place.X,place.Y+.1f,place.Z},To=new[]{place.X+6,place.Y+.1f,place.Z}};
                        created=value.Id;d.NavigationLinks.Add(value);break;
                    }
                }
                if(created!=Guid.Empty && d.Geometry.FirstOrDefault(value=>value.Id==created) is {} geometry)
                {
                    if(transform!=null)geometry.Transform=transform;
                    if(label!=null)geometry.Label=label;
                }
            });
            if(created!=Guid.Empty)
            {
                _document.Selection.Clear();_document.Selection.Add(created);_document.ActiveObjectId=created;
                _document.SelectionChanged();_viewport?.FrameSelection();
            }
        }
    }
}
