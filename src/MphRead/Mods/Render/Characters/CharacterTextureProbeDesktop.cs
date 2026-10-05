#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
namespace MphRead.Mods.Render.Characters;
internal static class CharacterTextureProbeDesktop
{
 internal static int Run(string root,string output,string? compression)
 {
  try
  {
   GraphicsBackendPolicy.Configure("metal");
   if(compression!=null) ModernGraphicsCompat.TextureCompressionForCheck=Enum.Parse<GpuTextureCompressionFormat>(compression,true);
   var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new Vector2i(32,32);
   using var window=new NativeWindow(settings);using var graphics=new DesktopGraphicsSession(window);DesktopGraphicsSession.Resize(window);
   File.WriteAllText(output,CharacterTextureProbe.Verify(root));Console.WriteLine("CHARACTER TEXTURE PROBE PASS "+output);return 0;
  }
  catch(Exception ex){File.WriteAllText(output,System.Text.Json.JsonSerializer.Serialize(new{pass=false,error=ex.ToString()}));Console.Error.WriteLine(ex);return 1;}
  finally{ModernGraphicsCompat.TextureCompressionForCheck=null;}
 }
}
#endif
