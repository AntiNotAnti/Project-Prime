using System;
using MphRead.Mods.MapEditor;
#if !ANDROID && !MPHREAD_SERVER
using MphRead.Mods.StudioRendering;
#endif

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private static readonly string[] ViewportModes={"Overlays","Rendered","Lighting","Shadows","Fog","Wireframe","Collision","Collision normals","Collision heat","Collision repairs","Terrain","Partitions","Kill plane","Navigation","Spawns","Pickups","Jump trajectories","Overdraw","Texel density","Material ID"};
    private string _viewportMode="Overlays";
#if !ANDROID && !MPHREAD_SERVER
    internal StudioViewportImage? CaptureViewport()
    {
#if MPHREAD_SHELL
        return _viewport?.CaptureStudioViewport();
#else
        return null;
#endif
    }
#endif
    internal void SetViewportMode(string mode)
    {
        if(Array.IndexOf(ViewportModes,mode)<0)throw new ArgumentException("Choose an available viewport mode.",nameof(mode));
        _viewportMode=mode;
        foreach(var view in _views)
        {
            view.Wireframe=mode=="Wireframe";
            view.Collision=mode is "Collision" or "Collision normals" or "Collision heat" or "Collision repairs";
            view.CollisionNormalsOverlay=mode=="Collision normals";view.CollisionHeatmap=mode=="Collision heat";view.CollisionRepairsOverlay=mode=="Collision repairs";
            view.PartitionOverlay=mode=="Partitions";view.KillPlane=mode=="Kill plane";
            view.LightingPreview=mode is "Lighting" or "Shadows";view.ShadowPreview=mode=="Shadows";view.FogPreview=mode=="Fog";
            view.DiagnosticMode=mode switch {"Terrain"=>MapViewportDiagnosticMode.Terrain,"Overdraw"=>MapViewportDiagnosticMode.Overdraw,"Texel density"=>MapViewportDiagnosticMode.TexelDensity,"Material ID"=>MapViewportDiagnosticMode.MaterialId,_=>MapViewportDiagnosticMode.None};
            view.EntityVisualization=mode is "Overlays" or "Spawns" or "Pickups" or "Jump trajectories";
            view.ShowSpawns=mode is "Overlays" or "Spawns";view.ShowPickups=mode is "Overlays" or "Pickups";view.ShowJumpPads=mode is "Overlays" or "Jump trajectories";
            view.NavigationOverlay=mode is "Overlays" or "Navigation";view.InvalidateVisual();
        }
        if(mode=="Navigation" && _viewport?.Navigation==null && _document!=null)_=Navigation();
    }
}
