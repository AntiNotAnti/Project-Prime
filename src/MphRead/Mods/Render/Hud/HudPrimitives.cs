using System;
using System.Numerics;
namespace MphRead.Mods.Render.Hud;

public readonly record struct HudRectPrimitive(float X, float Y, float Width, float Height, Vector4 Color);

/// <summary>Pure presentation geometry shared by the GL HUD and editor. Caller owns the buffer.</summary>
public static class HudPrimitives
{
    public static int Meter(Span<HudRectPrimitive> destination, float x, float y, float width, float height,
        float fraction, Vector4 color, bool vertical)
    {
        if (destination.Length < 3) throw new ArgumentException("Meter requires three primitive slots.", nameof(destination));
        fraction = Math.Clamp(fraction, 0, 1);
        if (vertical) { (width, height) = (height, width); y -= height; }
        destination[0] = new(x-1,y-1,width+2,height+2,new(0,0,0,.55f));
        destination[1] = new(x,y,width,height,new(1,1,1,.16f));
        destination[2] = vertical ? new(x,y+height*(1-fraction),width,height*fraction,color)
            : new(x,y,width*fraction,height,color);
        return fraction > 0 ? 3 : 2;
    }
}

public enum HudShapeKind { Disc, Ring, Line, Square }
public readonly record struct HudShapePrimitive(HudShapeKind Kind,Vector2 A,Vector2 B,float Radius,float Thickness,Vector4 Color);
public static class HudRadarGeometry
{
    public static int Build(Span<HudShapePrimitive> output,HudRadarRuntime style,float radius,bool background,bool outlines,
        Vector4 backing,Vector4 ring,Vector4 cone,float unit)
    {
        if(output.Length<8) throw new ArgumentException("Radar requires eight primitive slots.");
        int count=0;
        if(style.Style==HudRadarStyle.Minimal) return 0;
        if(background && backing.W>0)
            output[count++]=new(style.Style==HudRadarStyle.Square ? HudShapeKind.Square : HudShapeKind.Disc,default,default,radius,0,backing with { W=backing.W*style.BackgroundOpacity });
        if(!outlines) return count;
        float width=.35f*unit*style.OutlineThickness;
        if(style.Style==HudRadarStyle.Square)
        {
            output[count++]=new(HudShapeKind.Line,new(-radius,-radius),new(radius,-radius),0,width,ring);
            output[count++]=new(HudShapeKind.Line,new(radius,-radius),new(radius,radius),0,width,ring);
            output[count++]=new(HudShapeKind.Line,new(radius,radius),new(-radius,radius),0,width,ring);
            output[count++]=new(HudShapeKind.Line,new(-radius,radius),new(-radius,-radius),0,width,ring);
        }
        else output[count++]=new(HudShapeKind.Ring,default,default,radius,width,ring);
        if(style.Style==HudRadarStyle.Basic)
        {
            float innerWidth=.25f*unit*style.OutlineThickness;
            output[count++]=new(HudShapeKind.Ring,default,default,radius*.55f,innerWidth,ring);
            float angle=55*MathF.PI/180;
            output[count++]=new(HudShapeKind.Line,default,new(-radius*MathF.Sin(angle),radius*MathF.Cos(angle)),0,innerWidth,cone);
            output[count++]=new(HudShapeKind.Line,default,new(radius*MathF.Sin(angle),radius*MathF.Cos(angle)),0,innerWidth,cone);
        }
        return count;
    }
}
