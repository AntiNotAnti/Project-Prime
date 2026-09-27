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

public enum HudShapeKind { Disc, Ring, Line, Square, Triangle, Diamond, Hexagon }
public readonly record struct HudShapePrimitive(HudShapeKind Kind,Vector2 A,Vector2 B,float Radius,float Thickness,Vector4 Color);
