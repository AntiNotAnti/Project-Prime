using System.Diagnostics;
using MphRead.Mods.Launcher.RmlUi.Components;
using MphRead.Mods.Launcher.RmlUi.Host;
using OpenTK.Windowing.GraphicsLibraryFramework;

static class LinuxFixture
{
    public static unsafe void Run(RmlUiHost host,RmlUiAccessibilityService service,RmlUiDocumentToken document)
    {
        if(!OperatingSystem.IsLinux())throw new PlatformNotSupportedException("Actual AT-SPI fixture requires Linux");
        if(!GLFW.Init())throw new InvalidOperationException("GLFW initialization failed");
        GLFW.WindowHint(WindowHintClientApi.ClientApi,ClientApi.NoApi);
        Window* window=GLFW.CreateWindow(1280,720,"Project Prime accessibility fixture",null,null);
        if(window==null)throw new InvalidOperationException("real Linux window creation failed");
        try {
            using var provider=new RmlUiLinuxAccessibility(service);
            host.FocusDocument(document,"email");host.Update();
            var start=new ProcessStartInfo("/usr/bin/python3"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"atspi-client.py"));
            using var process=Process.Start(start)!;var timer=Stopwatch.StartNew();bool invoked=false;
            while(!process.HasExited&&timer.Elapsed<TimeSpan.FromSeconds(30)) {
                GLFW.PollEvents();service.Drain(host);host.Update();
                GLFW.GetWindowPos(window,out int x,out int y);GLFW.GetWindowSize(window,out int width,out int height);
                provider.Publish(service.Capture(host),new(x,y,width,height));
                while(host.TryTakeIntent(out var intent))invoked|=intent.Kind==RmlUiIntentKind.Navigate;
                Thread.Sleep(5);
            }
            if(!process.HasExited){process.Kill();throw new TimeoutException("AT-SPI external client timed out");}
            string output=process.StandardOutput.ReadToEnd(),errors=process.StandardError.ReadToEnd();
            if(process.ExitCode!=0)throw new InvalidOperationException("AT-SPI OS client failed: "+errors);
            service.Drain(host);host.Update();while(host.TryTakeIntent(out var intent))invoked|=intent.Kind==RmlUiIntentKind.Navigate;
            if(!provider.Available||!invoked||host.ReadField(document,"email")!="日本語 😀")throw new InvalidOperationException("external AT-SPI did not reach real native controls");
            Console.WriteLine(output.Trim());Console.WriteLine("PASS actual Linux AT-SPI registry -> pyatspi -> GDBus -> guarded native focus/Unicode/edit/invoke");
        } finally{GLFW.DestroyWindow(window);GLFW.Terminate();}
    }
}
