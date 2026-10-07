using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;

if(args.Length!=2)throw new ArgumentException("Usage: rmlui-drawlist-cache-check <native-bridge> <asset-root>");
nint module=NativeLibrary.Load(Path.GetFullPath(args[0]));
NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,(name,_,_)=>name=="ProjectPrime.RmlUi.Native"?module:0);
string root=Directory.CreateTempSubdirectory("prime-drawlist-cache-").FullName;
int checks=0;
void Check(bool condition,string message)
{
    if(!condition)throw new InvalidOperationException(message);
    checks++;Console.WriteLine("RMLDRAW CACHE PASS "+message);
}
try
{
    foreach(string file in Directory.EnumerateFiles(Path.GetFullPath(args[1]),"*",SearchOption.AllDirectories))
    {
        string target=Path.Combine(root,Path.GetRelativePath(Path.GetFullPath(args[1]),file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);
    }
    File.WriteAllText(Path.Combine(root,"cache-check.rml"),"""
        <rml><head><style>
        body { font-family: Rajdhani; font-weight: 600; font-size: 24px; background-color: #102030; }
        #message { width: 400px; height: 50px; }
        </style></head><body><div id="message">FIRST</div></body></rml>
        """);
    File.WriteAllText(Path.Combine(root,"unsupported-check.rml"),"""
        <rml><head><style>
        body { font-family: Rajdhani; font-weight: 600; font-size: 24px; }
        #filtered { width: 400px; height: 50px; filter: blur(2px); }
        </style></head><body><div id="filtered">FILTERED</div></body></rml>
        """);
    using var host=new RmlUiHost();
    Check(host.Initialize(1280,720,1,root,RmlUiRenderBackend.DrawList),"actual draw-list host initializes");
    host.ShowDocument(host.HomeDocument,false);
    var document=host.OpenDocument("cache-check.rml",RmlUiDocumentLayer.Page);
    var reader=new RmlUiDrawListReader();
    RmlUiDrawListFrame Frame(int width=1280,int height=720)
    {host.Render(width,height);return reader.Capture();}
    var first=Frame();
    Check(host.TryGetUpdateState(out var state)&&state.DrawListValid,"native completed list advertises the optional retained-frame contract");
    Frame(); // Warm the optional probe and JIT paths before measuring allocation.
    long allocated=GC.GetAllocatedBytesForCurrentThread();bool retained=true;
    for(int i=0;i<100;i++)retained&=ReferenceEquals(first,Frame());
    long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
    Check(retained&&reader.CapturedFrames==1&&reader.ReusedFrames==101,"100 unchanged renders reuse the exact copied frame");
    Check(bytes==0,"unchanged native render plus managed capture allocates zero owner-thread bytes");
    host.SetText(document,"message","CHANGED TEXT WITH NEW GLYPHS");
    var edited=Frame();
    Check(!ReferenceEquals(first,edited)&&edited.Commands.Length>0,"text and generated geometry invalidate the copied frame");
    Check(ReferenceEquals(edited,Frame()),"the edited frame becomes reusable after capture");
    var resized=Frame(1600,900);
    Check(!ReferenceEquals(edited,resized)&&ReferenceEquals(resized,Frame(1600,900)),"viewport changes invalidate once and then retain the new list");
    var retiredGeometry=resized.Geometry.Keys.ToArray();
    Check(host.CloseDocument(document),"native document retires");
    var closed=Frame(1600,900);
    Check(!ReferenceEquals(resized,closed)&&retiredGeometry.All(handle=>!closed.Geometry.ContainsKey(handle)),"document retirement cannot retain its released geometry");
    reader.Forget();
    Check(!ReferenceEquals(closed,Frame(1600,900)),"explicit compositor retirement clears the managed frame cache");
    ulong generation=host.HomeDocument.Generation;
    Check(host.Reinitialize()&&host.HomeDocument.Generation!=generation,"native restart changes the lifetime generation");
    host.ShowDocument(host.HomeDocument,false);
    var restarted=Frame();
    Check(restarted.Generation!=generation&&!ReferenceEquals(closed,restarted),"generation changes cannot reuse an earlier native lifetime");
    var unsupported=host.OpenDocument("unsupported-check.rml",RmlUiDocumentLayer.Page);
    bool RejectsFeatures()
    {
        try{Frame();return false;}
        catch(NotSupportedException){return true;}
    }
    Check(RejectsFeatures(),"an actual unsupported native filter cannot bypass compositor feature validation");
    host.CloseDocument(unsupported);
    Check(RejectsFeatures(),"unsupported features remain rejected for the current native lifetime after page retirement");
    host.Reinitialize();host.ShowDocument(host.HomeDocument,false);
    Check(Frame().Generation!=restarted.Generation,"a fresh native lifetime resets unsupported-feature state and capture cache");
    Console.WriteLine($"RMLDRAW CACHE CHECK PASS {checks} checks");
}
finally{Directory.Delete(root,true);}
