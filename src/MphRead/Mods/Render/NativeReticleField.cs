using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MphRead.Hud;

namespace MphRead.Mods.Render.Hud;

/// <summary>Cartridge silhouette reconstructed with subpixel contour edges as a supersampled signed distance field.
/// Immutable templates are prepared at room/HUD load, not while aiming or editing thickness.</summary>
internal static class NativeReticleField
{
    internal const int Samples = 4, Padding = 16;
    private static readonly ConditionalWeakTable<object, Dictionary<(int,int,int,bool), Template>> Cache = new();
    internal sealed record Template(int Width, int Height, float[] Distance, int[] Source);
    private readonly record struct Edge(float X0,float Y0,float X1,float Y1,int Source);
    internal static bool PixelContourReference { get; set; }
    internal static void Prepare(HudObject asset)
    {
        int count = asset.CharacterData.Count / (asset.Width * asset.Height);
        for(int frame=0;frame<count;frame++) _ = Get(asset.CharacterData,asset.Width,asset.Height,frame);
    }
    internal static Template Get(IReadOnlyList<byte> data,int width,int height,int frame)
    {
        var frames=Cache.GetOrCreateValue(data);
        var key=(width,height,frame,PixelContourReference);
        if(frames.TryGetValue(key,out var cached))return cached;
        var mask=new bool[width*height];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            mask[y*width+x]=data[frame*width*height+(y/8)*(width/8)*64+(x/8)*64+(y%8)*8+x%8]!=0;
        bool On(int x,int y)=>x>=0&&y>=0&&x<width&&y<height&&mask[y*width+x];
        var rectangles=new List<(int X,int Y,int W,int H,int Source)>();
        var rectanglePixels=new bool[mask.Length];var visited=new bool[mask.Length];
        if(!PixelContourReference)for(int seed=0;seed<mask.Length;seed++)
        {
            if(!mask[seed]||visited[seed])continue;
            var component=new List<int>();var pending=new Queue<int>();pending.Enqueue(seed);visited[seed]=true;
            int minX=width,minY=height,maxX=0,maxY=0;
            while(pending.Count>0)
            {
                int at=pending.Dequeue(),x=at%width,y=at/width;component.Add(at);
                minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);
                foreach(var point in new[]{(x-1,y),(x+1,y),(x,y-1),(x,y+1)})
                    if(On(point.Item1,point.Item2)){int next=point.Item2*width+point.Item1;if(!visited[next]){visited[next]=true;pending.Enqueue(next);}}
            }
            if(component.Count==(maxX-minX+1)*(maxY-minY+1))
            {rectangles.Add((minX,minY,maxX-minX+1,maxY-minY+1,seed));foreach(int at in component)rectanglePixels[at]=true;}
        }
        var edges=new List<Edge>();
        if(PixelContourReference)
        {
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)if(On(x,y))
        {
            int i=y*width+x;
            if(!On(x-1,y))edges.Add(new(x,y,x,y+1,i));
            if(!On(x+1,y))edges.Add(new(x+1,y,x+1,y+1,i));
            if(!On(x,y-1))edges.Add(new(x,y,x+1,y,i));
            if(!On(x,y+1))edges.Add(new(x,y+1,x+1,y+1,i));
        }
        }
        else
        {
            foreach(var rectangle in rectangles)
            {
                var (x,y,rw,rh,sourceIndex)=rectangle;
                edges.Add(new(x,y,x+rw,y,sourceIndex));edges.Add(new(x+rw,y,x+rw,y+rh,sourceIndex));
                edges.Add(new(x,y+rh,x+rw,y+rh,sourceIndex));edges.Add(new(x,y,x,y+rh,sourceIndex));
            }
            // Marching squares follows the 50% coverage contour of cartridge pixels.
            // Straight edges retain their positions; staircase corners gain bounded
            // half-pixel bevels. Disconnected islands and holes remain distinct.
            for(int y=-1;y<height;y++)for(int x=-1;x<width;x++)
            {
                bool a=On(x,y),b=On(x+1,y),c=On(x+1,y+1),d=On(x,y+1);
                var points=new List<(float X,float Y)>();
                if(a!=b)points.Add((x+1,y+.5f));
                if(b!=c)points.Add((x+1.5f,y+1));
                if(c!=d)points.Add((x+1,y+1.5f));
                if(d!=a)points.Add((x+.5f,y+1));
                int sourceIndex=a?y*width+x:b?y*width+x+1:c?(y+1)*width+x+1:d?(y+1)*width+x:-1;
                if(sourceIndex>=0 && rectanglePixels[sourceIndex])continue;
                void Add(int i,int j)=>edges.Add(new(points[i].X,points[i].Y,points[j].X,points[j].Y,sourceIndex));
                if(points.Count==2)Add(0,1);
                else if(points.Count==4){if(a){Add(0,3);Add(1,2);}else{Add(0,1);Add(2,3);}}
            }
        }
        int w=(width+2*Padding)*Samples,h=(height+2*Padding)*Samples;
        var distance=new float[w*h];var source=new int[w*h];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            float px=(x+.5f)/Samples-Padding,py=(y+.5f)/Samples-Padding;
            float best=float.MaxValue;int nearest=-1;
            foreach(var edge in edges)
            {
                float vx=edge.X1-edge.X0,vy=edge.Y1-edge.Y0;
                float t=Math.Clamp(((px-edge.X0)*vx+(py-edge.Y0)*vy)/(vx*vx+vy*vy),0,1);
                float dx=px-(edge.X0+t*vx),dy=py-(edge.Y0+t*vy);
                float d=dx*dx+dy*dy;
                if(d<best){best=d;nearest=edge.Source;}
            }
            int sx=(int)MathF.Floor(px),sy=(int)MathF.Floor(py);bool inside=On(sx,sy);
            if(!PixelContourReference && !(On(sx,sy)&&rectanglePixels[sy*width+sx]))
            {
                float cx=px-.5f,cy=py-.5f;int ix=(int)MathF.Floor(cx),iy=(int)MathF.Floor(cy);
                float tx=cx-ix,ty=cy-iy;
                float coverage=(On(ix,iy)?1-tx:0)*(1-ty)+(On(ix+1,iy)?tx:0)*(1-ty)
                    +(On(ix,iy+1)?1-tx:0)*ty+(On(ix+1,iy+1)?tx:0)*ty;
                inside=coverage>=.5f;
            }
            int at=y*w+x;
            distance[at]=(inside?1:-1)*MathF.Min(Padding,MathF.Sqrt(best));
            source[at]=On(sx,sy)?sy*width+sx:nearest;
        }
        cached=new(w,h,distance,source);frames.Add(key,cached);return cached;
    }
    internal static void Fill(Template template,ColorRgba[] original,float[] destination)
    {
        for(int i=0;i<template.Source.Length;i++)
        {
            var color=template.Source[i]>=0?original[template.Source[i]]:default;
            destination[i*4]=color.Red/255f;destination[i*4+1]=color.Green/255f;
            destination[i*4+2]=color.Blue/255f;destination[i*4+3]=template.Distance[i];
        }
    }
}
