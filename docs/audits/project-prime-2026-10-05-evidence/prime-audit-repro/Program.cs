using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using MphRead.Mods.Update;
if (args.Length > 0 && args[0] == "pool") {
 System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context,name) => {
  string location=Path.Combine("/tmp/prime-audit-server-output",name.Name+".dll");
  return File.Exists(location) ? context.LoadFromAssemblyPath(location) : null;
 };
 var pool=typeof(MphRead.Formats.CollisionDetection).GetField("_inactiveItems",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
 var count=pool.GetType().GetProperty("Count")!;
 Console.WriteLine("POOL_BEFORE="+count.GetValue(pool));
 for(int i=0;i<10;i++) MphRead.Formats.CollisionDetection.Init();
 Console.WriteLine("POOL_AFTER_TEN_SCENE_INITIALIZATIONS="+count.GetValue(pool));return;
}
if (args.Length > 0 && args[0] == "wait") {
 using var child=Process.Start("/bin/sleep", "45")!;
 var timer=Stopwatch.StartNew();
 typeof(DesktopUpdate).GetMethod("WaitForExit",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{child.Id});
 Console.WriteLine($"WaitForExit returned after {timer.Elapsed.TotalSeconds:F2}s with child alive={!child.HasExited}");
 if (!child.HasExited) child.Kill(); child.WaitForExit(); return;
}
string root=Path.Combine(Path.GetTempPath(),"prime-manifest-repro-"+Guid.NewGuid().ToString("N"));
string source=Path.Combine(root,"source"), target=Path.Combine(root,"install"), neighbor=Path.Combine(root,"install-neighbor");
Directory.CreateDirectory(source);Directory.CreateDirectory(target);Directory.CreateDirectory(neighbor);
string victim=Path.Combine(neighbor,"save.txt");File.WriteAllText(victim,"player save");
Directory.CreateDirectory(Path.Combine(root,"empty-neighbor"));
File.WriteAllText(Path.Combine(source,DesktopUpdate.ReleaseManifestName),JsonSerializer.Serialize(new {Version=1,Files=Array.Empty<string>()}));
File.WriteAllText(Path.Combine(target,DesktopUpdate.ReleaseManifestName),JsonSerializer.Serialize(new {Version=1,Files=new[]{"../install-neighbor/save.txt","../empty-neighbor/absent.txt"}}));
typeof(DesktopUpdate).GetMethod("RemoveObsoleteReleaseFiles",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{source,target});
Console.WriteLine($"OUTSIDE_FILE_DELETED={!File.Exists(victim)}");
Console.WriteLine($"OUTSIDE_DIRECTORY_DELETED={!Directory.Exists(Path.Combine(root,"empty-neighbor"))}");
Directory.Delete(root,true);
