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
        private void AddObject(string kind)
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
            });
            if(created!=Guid.Empty)
            {
                _document.Selection.Clear();_document.Selection.Add(created);_document.ActiveObjectId=created;
                _document.SelectionChanged();_viewport?.FrameSelection();
            }
        }
    }
}
