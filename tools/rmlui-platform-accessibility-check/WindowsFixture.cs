using System.Diagnostics;
using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Components;
using MphRead.Mods.Launcher.RmlUi.Host;
using OpenTK.Windowing.GraphicsLibraryFramework;

static class WindowsFixture
{
    public static unsafe void Run(RmlUiHost host,RmlUiAccessibilityService service,string root,RmlUiDocumentToken document)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Actual UIA fixture requires Windows");
        if(!GLFW.Init())throw new InvalidOperationException("GLFW initialization failed");
        GLFW.WindowHint(WindowHintClientApi.ClientApi,ClientApi.NoApi);
        Window* window=GLFW.CreateWindow(1280,720,"Project Prime accessibility fixture",null,null);
        if(window==null)throw new InvalidOperationException("real HWND creation failed");
        try {
            using var provider=new RmlUiWindowsAccessibility(GLFW.GetWin32Window(window),service);
            if(!provider.Attached)throw new InvalidOperationException("real HWND UIA hook failed");
            host.FocusDocument(document,"email");host.Update();provider.Publish(service.Capture(host));
            var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",Path.Combine(AppContext.BaseDirectory,"windows-client.ps1"),GLFW.GetWin32Window(window).ToInt64().ToString()})start.ArgumentList.Add(arg);
            using var process=Process.Start(start)!;
            var timer=Stopwatch.StartNew();bool invoked=false;
            while(!process.HasExited&&timer.Elapsed<TimeSpan.FromSeconds(30)) {
                GLFW.PollEvents();service.Drain(host);host.Update();provider.Publish(service.Capture(host));
                while(host.TryTakeIntent(out var intent))invoked|=intent.Kind==RmlUiIntentKind.Navigate;
                Thread.Sleep(5);
            }
            if(!process.HasExited){process.Kill();throw new TimeoutException("UI Automation client fixture timed out");}
            string output=process.StandardOutput.ReadToEnd();string errors=process.StandardError.ReadToEnd();
            if(process.ExitCode!=0)throw new InvalidOperationException("UIA OS client failed: "+errors);
            // Drain the final queued request after the external client exits.
            service.Drain(host);host.Update();while(host.TryTakeIntent(out var intent))invoked|=intent.Kind==RmlUiIntentKind.Navigate;
            if(!invoked||host.ReadField(document,"email")!="日本語 😀")throw new InvalidOperationException("external UIA did not reach real controls");
            Console.WriteLine(output.Trim());Console.WriteLine("PASS actual Windows UIAutomationClient -> HWND COM -> guarded owner/native focus/value/invoke");
        } finally { GLFW.DestroyWindow(window);GLFW.Terminate(); }
    }
}
