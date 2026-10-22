using System;
using System.Numerics;
namespace MphRead.Mods.Render.Hud;

public static class HudRadarGeometry
{
    public static int ContactLayer(RadarContactKind kind,HudRadarStyle style) => style==HudRadarStyle.Basic ? 0
        : kind==RadarContactKind.Hunter ? 2 : kind is RadarContactKind.Weapon or RadarContactKind.Powerup ? 0 : 1;
    public const int FrameCapacity = 128;
    public const int MarkerCapacity = 8;
    public static int Build(Span<HudShapePrimitive> output,HudRadarRuntime style,float radius,bool background,bool outlines,
        Vector4 backing,Vector4 ring,Vector4 cone,float unit, float time = 0, bool reduceMotion = false, bool reduceTransparency = false)
    {
        if(output.Length<8) throw new ArgumentException("Radar requires eight primitive slots.");
        if(style.Style >= HudRadarStyle.Tactical && output.Length < FrameCapacity)
            throw new ArgumentException("Enhanced radar requires FrameCapacity slots.");
        var definition = HudRadarStyles.Get(style.Style);
        if (definition.Glow) { backing = new(.02f,.25f,.35f,backing.W); ring = new(.2f,.85f,1,ring.W); }
        float backgroundAlpha = backing.W * style.BackgroundOpacity;
        if (reduceTransparency && backgroundAlpha > 0) backgroundAlpha = MathF.Max(.85f,backgroundAlpha);
        int count=0;
        if(style.Style==HudRadarStyle.Minimal) return 0;
        if(background && backing.W>0)
            output[count++]=new(style.Style==HudRadarStyle.Square ? HudShapeKind.Square : HudShapeKind.Disc,default,default,radius,0,backing with { W=backgroundAlpha });
        if(!outlines) return count;
        float width=FrameWidth(style)*unit;
        if(style.Style==HudRadarStyle.Square)
        {
            output[count++]=new(HudShapeKind.Line,new(-radius,-radius),new(radius,-radius),0,width,ring);
            output[count++]=new(HudShapeKind.Line,new(radius,-radius),new(radius,radius),0,width,ring);
            output[count++]=new(HudShapeKind.Line,new(radius,radius),new(-radius,radius),0,width,ring);
            output[count++]=new(HudShapeKind.Line,new(-radius,radius),new(-radius,-radius),0,width,ring);
        }
        else if (!definition.Segmented) output[count++]=new(HudShapeKind.Ring,default,default,radius,width,ring);
        else
        {
            var segments=HudRadarStyles.SegmentEndpoints;
            for(int i=0;i<segments.Length;i+=2)
                output[count++]=new(HudShapeKind.Line,segments[i]*radius,segments[i+1]*radius,0,width,ring);
        }
        if(definition.Glow) output[count++]=new(HudShapeKind.Ring,default,default,radius,width*3,ring with { W=ring.W*.18f });
        for(int i=1;style.RangeRings && i<=definition.Rings;i++)
            output[count++]=new(HudShapeKind.Ring,default,default,radius*i/(definition.Rings+1),width*.6f,ring with { W=ring.W*.35f });
        var ticks=HudRadarStyles.TickDirections(style.Style);
        for(int i=0;i<ticks.Length;i++)
            output[count++]=new(HudShapeKind.Line,ticks[i]*radius*(i%4==0 ? .88f : .95f),ticks[i]*radius,0,width,ring);
        if(definition.Sweep && !reduceMotion)
        {
            float angle=time*style.SweepSpeed*MathF.Tau;
            output[count++]=new(HudShapeKind.Line,default,Polar(radius,angle),0,width*4,ring with { W=style.SweepOpacity*.15f });
            output[count++]=new(HudShapeKind.Line,default,Polar(radius,angle),0,width,ring with { W=style.SweepOpacity });
        }
        if(style.Style==HudRadarStyle.Basic)
        {
            float innerWidth=.25f*unit*style.OutlineThickness;
            if(style.RangeRings) output[count++]=new(HudShapeKind.Ring,default,default,radius*.55f,innerWidth,ring);
            float angle=55*MathF.PI/180;
            output[count++]=new(HudShapeKind.Line,default,new(-radius*MathF.Sin(angle),radius*MathF.Cos(angle)),0,innerWidth,cone);
            output[count++]=new(HudShapeKind.Line,default,new(radius*MathF.Sin(angle),radius*MathF.Cos(angle)),0,innerWidth,cone);
        }
        return count;
    }

    private static float FrameWidth(HudRadarRuntime style) => (style.Style is HudRadarStyle.Tactical or HudRadarStyle.Competitive ? .5f : .35f)*style.OutlineThickness;

    public static int BuildSelfMarker(Span<HudShapePrimitive> output, HudRadarRuntime style, float heading,
        RadarBasis basis, float unit, Vector4 color)
    {
        int count=0;
        if(style.Style==HudRadarStyle.Holographic)
        {
            output[count++]=new(HudShapeKind.Disc,default,default,2*unit,0,new(.2f,.85f,1,.15f));
            output[count++]=new(HudShapeKind.Ring,default,default,1.6f*unit,.3f*unit,new(.2f,.85f,1,.5f));
        }
        output[count++]=BuildSelfMarker(heading,basis,unit,color);
        return count;
    }

    public static Vector2 Polar(float radius, float angle) => new(MathF.Sin(angle)*radius,MathF.Cos(angle)*radius);

    public static Vector2 GetBounds(HudRadarRuntime style, float textScale=1)
    {
        float radius=113.724f*style.RadiusScale;
        // Include markers clamped to the boundary, chevrons, stroke and glow.
        float margin=MathF.Max(5.625f*1.56f*style.BlipScale*(style.Elevation==HudRadarElevationMode.Chevron ? 2.8f : 1.25f),
            5.85f*FrameWidth(style)*(HudRadarStyles.Get(style.Style).Glow ? 1.5f : .5f));
        if(style.Cardinals) margin=MathF.Max(margin,18*textScale);
        return new Vector2(2*(radius+margin));
    }

    public static int BuildContact(Span<HudShapePrimitive> output, RadarContact contact, RadarProjectedContact projected,
        RadarBasis basis, HudRadarRuntime style, float unit, float time=0, bool reduceMotion=false)
    {
        if(output.Length<MarkerCapacity) throw new ArgumentException("Marker buffer too small.");
        if(!HudRadarProjection.Visible(contact.Kind,style) || style.BlipOpacity<=0) return 0;
        float scale=1.56f*unit*style.BlipScale;
        Vector4 color=contact.Color with { W=contact.Color.W*contact.Alpha*style.BlipOpacity };
        if(!reduceMotion && style.Style==HudRadarStyle.Scanner)
        {
            float age=((time*style.SweepSpeed-projected.Bearing/MathF.Tau)%1+1)%1/style.SweepSpeed;
            color.W*=.7f+.3f*MathF.Max(0,1-age/.25f);
        }
        if(!reduceMotion && style.Style==HudRadarStyle.Holographic) color.W*=.9f+.1f*MathF.Sin(time*2);
        if(style.Elevation==HudRadarElevationMode.Color && projected.Elevation!=RadarElevation.Level)
        {
            float amount=projected.Elevation==RadarElevation.Above ? .25f : -.2f;
            color=new(Math.Clamp(color.X+amount,0,1),Math.Clamp(color.Y+amount,0,1),Math.Clamp(color.Z+amount,0,1),color.W);
        }
        Vector2 position=projected.Position;
        int count=0;
        bool arrow=projected.OutOfRange && style.OutOfRange==HudRadarOutOfRangeMode.EdgeArrow
            && contact.Kind is not (RadarContactKind.Weapon or RadarContactKind.Powerup);
        if(arrow || contact.Kind==RadarContactKind.Hunter && style.HunterFacing)
            output[count++]=new(HudShapeKind.Triangle,position,new(arrow ? projected.Bearing : basis.Heading(contact.Heading),0),scale*(arrow ? 1.15f : .85f),0,color);
        else switch(contact.Kind)
        {
            case RadarContactKind.Hunter: output[count++]=new(HudShapeKind.Ring,position,default,.59f*scale,.2f*scale,color); break;
            case RadarContactKind.Weapon: output[count++]=new(HudShapeKind.Diamond,position,default,.49f*scale,0,color); break;
            case RadarContactKind.Powerup: output[count++]=new(HudShapeKind.Disc,position,default,.39f*scale,0,color); break;
            case RadarContactKind.Objective: output[count++]=new(HudShapeKind.Hexagon,position,default,.8f*scale,0,color); break;
            case RadarContactKind.PrimeHunter:
                output[count++]=new(HudShapeKind.Ring,position,default,1.05f*scale,.15f*scale,color);
                output[count++]=new(HudShapeKind.Ring,position,default,.8f*scale,.12f*scale,color);
                output[count++]=new(HudShapeKind.Diamond,position,default,.5f*scale,0,color); break;
            default:
                int sides=4; float rotation=contact.Kind==RadarContactKind.Node ? MathF.PI/4 : 0;
                for(int i=0;i<sides;i++) output[count++]=new(HudShapeKind.Line,position+Polar(.85f*scale,rotation+i*MathF.Tau/sides),
                    position+Polar(.85f*scale,rotation+(i+1)*MathF.Tau/sides),0,.18f*scale,color);
                break;
        }
        if(style.Elevation==HudRadarElevationMode.Chevron && projected.Elevation!=RadarElevation.Level)
        {
            float sign=projected.Elevation==RadarElevation.Above ? 1 : -1;
            Vector2 center=position+new Vector2(0,sign*1.6f*scale);
            output[count++]=new(HudShapeKind.Line,center+new Vector2(-.5f*scale,0),center+new Vector2(0,sign*.5f*scale),0,.15f*scale,color);
            output[count++]=new(HudShapeKind.Line,center+new Vector2(0,sign*.5f*scale),center+new Vector2(.5f*scale,0),0,.15f*scale,color);
        }
        return count;
    }
    public static HudShapePrimitive BuildSelfMarker(float heading, RadarBasis basis, float unit, Vector4 color)
        => new(HudShapeKind.Triangle,default,new(basis.Heading(heading),0),1.25f*unit,0,color);

    public static int Polygon(HudShapePrimitive primitive, Span<Vector2> vertices)
    {
        int count=primitive.Kind==HudShapeKind.Triangle ? 3 : primitive.Kind==HudShapeKind.Hexagon ? 6 : 4;
        float r=primitive.Radius, angle=primitive.B.X;
        for(int i=0;i<count;i++)
        {
            Vector2 v=primitive.Kind==HudShapeKind.Triangle ? i==0 ? new(0,r) : new((i==1 ? -.75f : .75f)*r,-.7f*r)
                : Polar(r,i*MathF.Tau/count);
            vertices[i]=new(v.X*MathF.Cos(angle)+v.Y*MathF.Sin(angle),v.Y*MathF.Cos(angle)-v.X*MathF.Sin(angle));
        }
        return count;
    }
}
