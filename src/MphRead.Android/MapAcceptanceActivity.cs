#if DEBUG
using System;
using System.IO;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using MphRead.Mods.MapGen;

namespace MphRead.Droid;

[Activity(Name="com.projectprime.game.MapAcceptanceActivity",Exported=true)]
public sealed class MapAcceptanceActivity : Activity
{
    protected override async void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        string report=Path.Combine(FilesDir!.AbsolutePath,"map-runtime-check.txt");
        try
        {
            await Task.Run(async () =>
            {
                if (Mods.Render.NativeTexturePixelBufferCheck.Run() != 0)
                    throw new InvalidOperationException("Native texture parity check failed.");
                await MapRuntimeCheck.RunAsync(Path.Combine(CacheDir!.AbsolutePath,"map-runtime-"+Guid.NewGuid().ToString("N")));
            });
            File.WriteAllText(report,"PASS: ppmap load, package verification, download, registration and runtime decode\nPASS: native texture parity and compact storage\n");
        }
        catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex+"\n");}
        finally{Finish();}
    }
}
#endif
