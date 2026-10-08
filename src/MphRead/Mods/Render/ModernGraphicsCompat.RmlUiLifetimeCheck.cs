#if MPHREAD_RMLUI_POC && !MPHREAD_SERVER && !ANDROID
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render;

/// <summary>Actual native documents on the existing engine surface; no mock GPU or second device.</summary>
public static class RmlUiLifetimeCheck
{
    public static void RunDesktop(string backend,string assets,string output,int switches=100)
    {
        if(switches<100||switches>1000)throw new ArgumentOutOfRangeException(nameof(switches));
        GraphicsBackendPolicy.Configure(backend);
        var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new(1280,720);
        using var window=new OpenTK.Windowing.Desktop.NativeWindow(settings);
        using var session=new DesktopGraphicsSession(window);
        if(!ModernGraphicsCompat.Active)throw new InvalidOperationException("Use Metal, Vulkan or DX12 for the native GPU lifetime check.");
        int width=window.FramebufferSize.X,height=window.FramebufferSize.Y;
        using var host=new RmlUiHost();
        if(!host.Initialize(width,height,window.FramebufferSize.X/1280f,assets,RmlUiRenderBackend.DrawList))throw new InvalidOperationException("Native host initialization failed.");
        using var pages=new RmlUiPageManager(host);
        string[] documents={"settings/settings","hunters/selection","community/browser","theatre/library","adventure/saves","offline/setup","social/social","news/news","setup/setup","hud/editor"};
        ModernGraphicsCompat.SetVSync(false);
        long presented=0;
        void Draw()
        {
            host.Update();host.Render(width,height);
            GraphicsApi.Viewport(0,0,width,height);GraphicsApi.Disable(EnableCap.ScissorTest);
            GraphicsApi.ClearColor(.02f,.04f,.08f,1);GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            RmlUiGpuCompositor.DrawNativeFrame(width,height);
            ModernGraphicsCompat.ThrowIfDeviceFailedForCheck();ModernGraphicsCompat.Present();
            if(!ModernGraphicsCompat.LastPresentationSucceeded)throw new InvalidOperationException("Lifetime sample did not reach the surface.");
            presented++;
        }
        void Switch(string document)
        {
            pages.OpenPage(new(document,"pages/"+document+".rml"));Draw();
            if(!pages.ClosePage())throw new InvalidOperationException("Document retirement failed.");
            // The next native frame retires resources referenced by the previous command list.
            Draw();Draw();
        }
        try
        {
            Draw();foreach(string document in documents)Switch(document);
            var before=ModernGraphicsCompat.RmlUiResources;var sharedBefore=ModernGraphicsCompat.LiveResources;
            long allocated=GC.GetTotalAllocatedBytes(false);var clock=Stopwatch.StartNew();
            for(int i=0;i<switches;i++)Switch(documents[i%documents.Length]);
            clock.Stop();var after=ModernGraphicsCompat.RmlUiResources;var sharedAfter=ModernGraphicsCompat.LiveResources;
            if(after!=before)throw new InvalidOperationException($"Native UI resources grew after warmup: {before} -> {after}");
            if(sharedAfter!=sharedBefore)throw new InvalidOperationException($"Shared renderer resources grew after warmup: {sharedBefore} -> {sharedAfter}");
            Console.WriteLine($"RMLGPU LIFETIME PASS {switches} actual page replacements retain stable native UI and shared GPU resources");
            int generation=ModernGraphicsCompat.DeviceGeneration;ModernGraphicsCompat.DestroyDeviceForCheck();Draw();Draw();
            var afterRecovery=ModernGraphicsCompat.RmlUiResources;
            if(ModernGraphicsCompat.DeviceGeneration<=generation||afterRecovery.Geometry!=after.Geometry
                ||afterRecovery.BufferCapacityBytes!=after.BufferCapacityBytes||afterRecovery.Textures!=after.Textures
                ||afterRecovery.TextureCapacityBytes!=after.TextureCapacityBytes||afterRecovery.EstimatedStencilBytes!=after.EstimatedStencilBytes)
                throw new InvalidOperationException($"Device recovery did not recreate the current native document resources: {after} -> {afterRecovery}");
            Console.WriteLine("RMLGPU LIFETIME PASS device recovery reconstructs current document and atlas on the same renderer owner");
            var capture=RmlUiGpuCompositor.CaptureMetrics;
            pages.ClosePage();host.Shutdown();RmlUiGpuCompositor.ReleaseNativeFrame();
            var retired=ModernGraphicsCompat.RmlUiResources;
            if(retired.Geometry!=0||retired.Textures!=0||retired.Buffers!=0
                ||retired.StencilTextures>afterRecovery.StencilTextures||retired.Pipelines>afterRecovery.Pipelines||retired.ShaderModules>afterRecovery.ShaderModules)
                throw new InvalidOperationException("Native UI shutdown retained GPU resources.");
            Console.WriteLine("RMLGPU LIFETIME PASS native shutdown releases geometry and atlases; bounded shader/stencil cache remains renderer-owned");
            ModernGraphicsCompat.Shutdown();
            if(ModernGraphicsCompat.Active)throw new InvalidOperationException("Renderer shutdown retained its GPU owner.");
            Console.WriteLine("RMLGPU LIFETIME PASS renderer teardown disposes the remaining native UI shader/stencil cache");
            var report=new{format=1,backend=GraphicsBackendPolicy.Resolved.ToString(),width,height,switches,documents,
                seconds=clock.Elapsed.TotalSeconds,successfulPresentations=presented,managedAllocatedBytes=GC.GetTotalAllocatedBytes(false)-allocated,
                before,after,sharedBefore,sharedAfter,afterRecovery,retired,
                drawListCapture=new{requests=capture.Requests,captured=capture.Captured,reused=capture.Reused},
                scope="Actual RML page documents and shared modern GPU compositor; controller/network activity and world draws excluded. Warmup visits each page once. Capacity tracks owned resources, not driver VRAM residency."};
            string path=Path.GetFullPath(output);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{RmlUiGpuCompositor.ReleaseNativeFrame();}
    }
}
#endif
