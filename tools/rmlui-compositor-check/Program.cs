using System;
using MphRead.Mods.Render;
RmlUiCompositorCheck.RunDesktop(args.Length == 0 ? "vulkan" : args[0],
    Array.IndexOf(args, "--recovery") >= 0);
Console.WriteLine("RMLGPU CHECK PASS " + GraphicsBackendPolicy.Resolved);
