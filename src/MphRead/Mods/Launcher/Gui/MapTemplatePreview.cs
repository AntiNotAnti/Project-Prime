using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed class MapTemplatePreview : Control
{
    public MapTemplateInfo Template { get; }

    public MapTemplatePreview(MapTemplateInfo template)
    {
        Template=template;
        Width=180;Height=104;ClipToBounds=true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var rect=new Rect(Bounds.Size);
        context.FillRectangle(PrimeTheme.PanelBrush,rect);
        context.DrawRectangle(new Pen(PrimeTheme.BorderBrush,1),rect);

        MapDefinition definition;
        try{definition=MapTemplates.Preview(Template.Id);}
        catch
        {
            DrawWorkflow(context);
            return;
        }

        var geometry=definition.Geometry.OfType<MapBox>().ToArray();
        if(geometry.Length==0){DrawWorkflow(context);return;}
        float minX=geometry.Min(g=>g.Transform.Position[0]-g.Transform.Scale[0]/2);
        float maxX=geometry.Max(g=>g.Transform.Position[0]+g.Transform.Scale[0]/2);
        float minZ=geometry.Min(g=>g.Transform.Position[2]-g.Transform.Scale[2]/2);
        float maxZ=geometry.Max(g=>g.Transform.Position[2]+g.Transform.Scale[2]/2);
        float width=Math.Max(1,maxX-minX),depth=Math.Max(1,maxZ-minZ);
        double scale=Math.Min((Bounds.Width-18)/width,(Bounds.Height-18)/depth);
        Point Project(float x,float z)=>new(9+(x-minX)*scale,9+(z-minZ)*scale);

        foreach(MapBox box in geometry.OrderBy(g=>g.Transform.Position[1]))
        {
            Point p=Project(box.Transform.Position[0]-box.Transform.Scale[0]/2,
                box.Transform.Position[2]-box.Transform.Scale[2]/2);
            var r=new Rect(p.X,p.Y,Math.Max(2,box.Transform.Scale[0]*scale),
                Math.Max(2,box.Transform.Scale[2]*scale));
            byte alpha=box.Transform.Scale[1]>.9f?(byte)95:(byte)55;
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(alpha,110,190,230)),r);
            context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromArgb(170,150,220,255)),1),r);
        }
        foreach(MapSpawn spawn in definition.Spawns)
        {
            Point p=Project(spawn.Position[0],spawn.Position[2]);
            IBrush brush=spawn.Team switch
            {
                0=>Brushes.DeepSkyBlue,
                1=>Brushes.OrangeRed,
                _=>Brushes.LimeGreen
            };
            context.DrawEllipse(brush,new Pen(Brushes.White,1),p,3,3);
        }
        foreach(MapJumpPad pad in definition.JumpPads)
        {
            Point p=Project(pad.Position[0],pad.Position[2]);
            context.DrawEllipse(Brushes.Gold,new Pen(Brushes.White,1),p,4,4);
        }
    }

    private void DrawWorkflow(DrawingContext context)
    {
        var center=new Point(Bounds.Width/2,Bounds.Height/2);
        IBrush brush=Template.Action==MapTemplateAction.ImportQ3?Brushes.DeepSkyBlue:Brushes.Gold;
        context.DrawEllipse(null,new Pen(brush,3),center,28,28);
        context.DrawLine(new Pen(brush,3),new Point(center.X-16,center.Y),new Point(center.X+16,center.Y));
        context.DrawLine(new Pen(brush,3),new Point(center.X,center.Y-16),new Point(center.X,center.Y+16));
    }
}
