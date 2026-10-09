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
            // The portable contract check already wrote the expected Unicode.
            // Reset it so this OS check proves a fresh external SetValue call.
            host.SetField(document,"email","OS_FIXTURE_PENDING");
            host.SetText(document,"heading","ACCOUNT");
            host.FocusDocument(document,"email");host.Update();provider.Publish(service.Capture(host));
            var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",Path.Combine(AppContext.BaseDirectory,"windows-client.ps1"),GLFW.GetWin32Window(window).ToInt64().ToString()})start.ArgumentList.Add(arg);
            using var process=Process.Start(start)!;
            var timer=Stopwatch.StartNew();bool invoked=false;int accepted=0;
            int observed=0;string lastIntent="none";
            void DrainIntents() {
                while(host.TryTakeIntent(out var intent)) {
                    ++observed;
                    lastIntent=$"{intent.Kind}:{intent.Argument}";
                    if(intent.Kind!=RmlUiIntentKind.Navigate || intent.Argument!=(int)RmlUiRouteArgument.Settings)continue;
                    invoked=true;host.SetText(document,"heading","ACTION RECEIVED");
                }
            }
            while(!process.HasExited&&timer.Elapsed<TimeSpan.FromSeconds(30)) {
                GLFW.PollEvents();accepted+=service.Drain(host);
                // Observe owner-accepted actions before and after DOM animation
                // updates; do not mistake a queued UIA COM invocation for a
                // dispatched and acknowledged game command.
                DrainIntents();host.Update();DrainIntents();
                provider.Publish(service.Capture(host));
                Thread.Sleep(5);
            }
            if(!process.HasExited){process.Kill();throw new TimeoutException("UI Automation client fixture timed out");}
            string output=process.StandardOutput.ReadToEnd();string errors=process.StandardError.ReadToEnd();
            bool edited=host.ReadField(document,"email")=="日本語 😀";
            bool windowFocused=GetFocus()==GLFW.GetWin32Window(window);
            if(process.ExitCode!=0)throw new InvalidOperationException($"UIA OS client failed (accepted={accepted}, observed={observed}, lastIntent={lastIntent}, invoked={invoked}, UnicodeApplied={edited}, HwndFocused={windowFocused}): "+errors);
            // Drain the final queued request after the external client exits.
            service.Drain(host);DrainIntents();host.Update();DrainIntents();
            if(!invoked||!edited||!windowFocused)throw new InvalidOperationException($"external UIA did not reach real controls (accepted={accepted}, observed={observed}, lastIntent={lastIntent}, invoked={invoked}, UnicodeApplied={edited}, HwndFocused={windowFocused})");
            Console.WriteLine(output.Trim());Console.WriteLine("PASS actual Windows UIAutomationClient -> HWND COM -> guarded owner/native focus/value/invoke");
        } finally { GLFW.DestroyWindow(window);GLFW.Terminate(); }
    }
    [DllImport("user32.dll")] private static extern nint GetFocus();
}
