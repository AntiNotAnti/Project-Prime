using System;
using MphRead.Mods.Render;
if(Array.IndexOf(args,"--lifetime") is int lifetime && lifetime>=0)
{
    if(args.Length<lifetime+3)throw new ArgumentException("--lifetime requires asset root and output JSON path.");
    RmlUiLifetimeCheck.RunDesktop(args[0],args[lifetime+1],args[lifetime+2]);
}
else RmlUiCompositorCheck.RunDesktop(args.Length == 0 ? "vulkan" : args[0],Array.IndexOf(args, "--recovery") >= 0);
Console.WriteLine("RMLGPU CHECK PASS " + GraphicsBackendPolicy.Resolved);
