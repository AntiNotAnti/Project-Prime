#if DEBUG
using System;
using System.IO;
using System.Runtime.InteropServices;
using Android.App;
using Android.OS;
using Android.Views;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;
namespace MphRead.Droid;

[Activity(Name="com.projectprime.game.CharacterTextureAcceptanceActivity",Exported=true)]
public sealed class CharacterTextureAcceptanceActivity : Activity,ISurfaceHolderCallback
{
 private nint _window;
 [DllImport("android")] private static extern nint ANativeWindow_fromSurface(nint env,nint surface);
 [DllImport("android")] private static extern void ANativeWindow_release(nint window);
 protected override void OnCreate(Bundle? state){base.OnCreate(state);var view=new SurfaceView(this);view.Holder!.AddCallback(this);SetContentView(view);}
 public void SurfaceCreated(ISurfaceHolder holder){}
 public void SurfaceChanged(ISurfaceHolder holder,Android.Graphics.Format format,int width,int height)
 {
  string report=Path.Combine(FilesDir!.AbsolutePath,"character-texture-check.json");
  try
  {
   ReleaseSurface();GraphicsBackendPolicy.Configure("vulkan");
   _window=ANativeWindow_fromSurface(Android.Runtime.JNIEnv.Handle,holder.Surface!.Handle);if(_window==0)throw new InvalidOperationException("Native surface unavailable.");
   ModernGraphicsCompat.AttachAndroidWindow(_window,width,height);
   string result=CharacterTextureProbe.Verify(Path.Combine(FilesDir.AbsolutePath,"character-models/default"));
   ModernGraphicsCompat.Present();File.WriteAllText(report,result);
  }
  catch(Exception ex){File.WriteAllText(report,System.Text.Json.JsonSerializer.Serialize(new{pass=false,error=ex.ToString()}));}
 }
 public void SurfaceDestroyed(ISurfaceHolder holder)=>ReleaseSurface();
 private void ReleaseSurface(){if(_window==0)return;ModernGraphicsCompat.DetachAndroidWindow();ANativeWindow_release(_window);_window=0;}
 protected override void OnDestroy(){ReleaseSurface();ModernGraphicsCompat.Shutdown();base.OnDestroy();}
}
#endif
