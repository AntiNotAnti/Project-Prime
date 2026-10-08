using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Render;
using SkiaSharp;

// Diagnostic rasterization only. Captures native layout/font geometry, not GPU or scene parity.
internal static class SoftwarePreview
{
 internal static unsafe void Save(RmlUiDrawListFrame frame,int width,int height,string path)
 {
  using var surface=SKSurface.Create(new SKImageInfo(width,height));var canvas=surface.Canvas;canvas.Clear(new SKColor(3,12,22));
  var textures=new Dictionary<ulong,SKBitmap>();
  try
  {
   foreach(var pair in frame.Textures){var source=pair.Value;var bitmap=new SKBitmap(new SKImageInfo(source.Width,source.Height,SKColorType.Rgba8888,SKAlphaType.Premul));Marshal.Copy(source.Pixels,0,bitmap.GetPixels(),source.Pixels.Length);textures.Add(pair.Key,bitmap);}
   bool scissor=false;SKRect clip=new(0,0,width,height);SKMatrix transform=SKMatrix.Identity;
   foreach(var command in frame.Commands)
   {
    if(command.Kind==RmlUiDrawCommandKind.EnableScissor){scissor=command.Enabled!=0;continue;}
    if(command.Kind==RmlUiDrawCommandKind.Scissor){clip=SKRect.Create(command.X,command.Y,command.Width,command.Height);continue;}
    if(command.Kind==RmlUiDrawCommandKind.Transform){var c=command;transform=command.Enabled==0?SKMatrix.Identity:new SKMatrix(c.Transform[0],c.Transform[4],c.Transform[12],c.Transform[1],c.Transform[5],c.Transform[13],c.Transform[3],c.Transform[7],c.Transform[15]);continue;}
    if(command.Kind is RmlUiDrawCommandKind.EnableClipMask or RmlUiDrawCommandKind.ClipMask)continue;
    var geometry=frame.Geometry[command.Geometry];var bitmap=command.Texture!=0?textures[command.Texture]:null;
    var vertices=geometry.Vertices;
    var positions=vertices.Select(v=>new SKPoint(v.X,v.Y)).ToArray();
    var uv=vertices.Select(v=>new SKPoint(v.U*(bitmap?.Width??1),v.V*(bitmap?.Height??1))).ToArray();
    var colors=vertices.Select(v=>{uint c=v.Color,a=c>>24;byte Channel(int shift)=>(byte)(a==0?0:Math.Min(255,((c>>shift)&255)*255/a));return new SKColor(Channel(0),Channel(8),Channel(16),(byte)a);}).ToArray();
    using var mesh=SKVertices.CreateCopy(SKVertexMode.Triangles,positions,uv,colors,geometry.Indices.Select(i=>checked((ushort)i)).ToArray());
    using var paint=new SKPaint{IsAntialias=true,Color=SKColors.White};if(bitmap!=null)paint.Shader=bitmap.ToShader();
    canvas.Save();if(scissor)canvas.ClipRect(clip);canvas.Concat(in transform);canvas.Translate(command.TranslationX,command.TranslationY);canvas.DrawVertices(mesh,SKBlendMode.Modulate,paint);canvas.Restore();
   }
   using var image=surface.Snapshot();using var data=image.Encode(SKEncodedImageFormat.Png,100);using var file=File.Create(path);data.SaveTo(file);
  }
  finally{foreach(var bitmap in textures.Values)bitmap.Dispose();}
 }
}
