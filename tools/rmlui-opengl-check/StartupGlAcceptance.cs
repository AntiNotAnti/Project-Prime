using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using MphRead.Mods;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Startup;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

/// <summary>Actual authored splash and native pointer/keyboard routing. Completion effects are observed, never applied.</summary>
internal static class StartupGlAcceptance
{
    internal static void Run(RmlUiHost host,int width,int height)
    {
        const string scope="Actual authored startup artwork encoded losslessly as native TGA, contained aspect ratio, static Press Start, fullscreen pointer target and Enter/Space one-shot routing. No game scene, process launch, settings write or owner completion callback. Does not qualify physical Cocoa event delivery.";
        string output=Path.GetFullPath(Path.Combine("artifacts","rmlui-startup",width+"x"+height));Directory.CreateDirectory(output);
        int checks=0;
        foreach(string input in new[]{"pointer","enter","space"})
        {
            using var pages=new RmlUiPageManager(host);using var page=new NativeStartupPage(host,pages,studio:input=="space");
            host.Input.SetFramebufferScale(1,1);page.Open();Settle();
            Check(host.TryGetElementBounds(page.Document,"native_startup_continue",out float x,out float y,out float w,out float h)
                &&x>=-.5f&&y>=-.5f&&x<=.5f&&y<=.5f&&Math.Abs(w-width)<=1&&Math.Abs(h-height)<=1,"continue target fills actual startup viewport");
            Check(host.TryGetElementBounds(page.Document,"native_startup_art",out float ax,out float ay,out float aw,out float ah)
                &&aw>0&&ah>0&&ax>=-1&&ay>=-1&&ax+aw<=width+1&&ay+ah<=height+1
                &&Math.Abs(aw/ah-NativeStartupPage.ArtworkWidth/(float)NativeStartupPage.ArtworkHeight)<.005f,
                "native startup art preserves exact original aspect inside framebuffer");
            var semantics=NativeSemanticReadback.Read(host,page.Document);
            Check(semantics.ContainsText("PRESS START")&&semantics.ContainsText(input=="space"?"PROJECT PRIME STUDIO":"PROJECT PRIME"),"startup presents application identity and readable Press Start");
            Check(!host.TryGetElementBounds(page.Document,"topbar",out _,out _,out _,out _),"startup remains headerless");
            if(input=="pointer") StartupTextureStateAcceptance.Run(host,page.Document,height,Frame,Check);
            Check(host.FocusDocument(page.Document,"native_startup_continue"),"continue has native keyboard focus");
            string path=Path.Combine(output,"startup-"+input+".png");Check(ScreenCapture.SaveWindow(width,height,path),"capture native startup "+input);
            FinalCompositeCapture.WriteEvidence(path,width,height,scope);File.WriteAllText(path+".semantic.json",semantics.RawJson);
            if(input=="pointer")
            {
                pages.OpenModal(new("startup-cover","pages/startup/startup.rml","native_startup_continue"));host.Update();
                Check(!page.Handle(new(RmlUiIntentKind.Navigate,(int)RmlUiRouteArgument.Home,page.Document,ulong.MaxValue))&&!page.Closed,
                    "covered startup refuses background completion");pages.CloseModal();host.Update();
                Check(host.FocusDocument(page.Document,"native_startup_continue"),"uncovered startup restores continue focus");
            }
            host.DiscardIntents();
            if(input=="pointer")
            {host.Input.PointerMoved(width/2d,height/2d);host.Input.PointerButton(0,width/2d,height/2d,true);host.Input.PointerButton(0,width/2d,height/2d,false);}
            else{int code=input=="enter"?2:4;host.Input.Key(code,true);host.Input.Key(code,false);}
            host.Update();
            Check(host.TryTakeIntent(out var intent)&&intent.Kind==RmlUiIntentKind.Navigate&&intent.Argument==(int)RmlUiRouteArgument.Home
                &&intent.Document==page.Document&&!host.TryTakeIntent(out _),"actual "+input+" emits one exact foreground Home action");
            Check(!page.Handle(intent with{Kind=RmlUiIntentKind.Quit})&&!page.Closed,"startup refuses unrelated typed action");
            Check(page.Handle(intent)&&page.Closed&&!page.Handle(intent),"startup completion is accepted exactly once for "+input);
            page.Dispose();Check(!page.Handle(intent),"retired startup cannot receive delayed completion");host.DiscardIntents();

            void Frame(){page.Refresh(width,height,true);host.Update();GL.BindFramebuffer(FramebufferTarget.Framebuffer,0);GL.Viewport(0,0,width,height);GL.Disable(EnableCap.ScissorTest);GL.ClearColor(.01f,.03f,.05f,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit|ClearBufferMask.StencilBufferBit);RmlUiOpenGlFrame.Draw(host,width,height);}
            void Settle(){var timer=Stopwatch.StartNew();do{Frame();Thread.Sleep(5);}while(timer.ElapsedMilliseconds<160);}
        }
        HeaderBindingOwnership(host, Check);
        Check(GL.GetError()==ErrorCode.NoError,"startup inputs leave no GL error");
        File.WriteAllText(Path.Combine(output,"startup-summary.json"),JsonSerializer.Serialize(new{passed=true,checks,width,height,scope},new JsonSerializerOptions{WriteIndented=true}));
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks++;Console.WriteLine("NATIVE STARTUP GL PASS "+label);}
    }

    private static void HeaderBindingOwnership(RmlUiHost host, Action<bool,string> check)
    {
        Type smoke = typeof(RmlUiHost).Assembly.GetType(
            "MphRead.Mods.Launcher.RmlUi.Render.RmlUiOpenGlSmoke", throwOnError: true)!;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        FieldInfo hostField = smoke.GetField("_host", flags)!;
        FieldInfo pagesField = smoke.GetField("_pages", flags)!;
        FieldInfo headerField = smoke.GetField("_applicationHeader", flags)!;
        FieldInfo launcherPagesField = smoke.GetField("_launcherPages", flags)!;
        MethodInfo present = smoke.GetMethod("PresentApplicationHeader", flags)!;
        object? oldHost = hostField.GetValue(null);
        object? oldPages = pagesField.GetValue(null);
        object? oldHeader = headerField.GetValue(null);
        object? oldLauncherPages = launcherPagesField.GetValue(null);
        try
        {
            hostField.SetValue(null, host);
            launcherPagesField.SetValue(null, null);
            headerField.SetValue(null, default(RmlUiDocumentToken));
            using (var startupPages = new RmlUiPageManager(host))
            using (var startup = new NativeStartupPage(host, startupPages, studio: false))
            {
                startup.Open();
                pagesField.SetValue(null, startupPages);
                host.Update();
                check(!host.TryGetElementBounds(startup.Document, "topbar", out _, out _, out _, out _),
                    "native startup fixture remains headerless before production presentation");
                Present();
                check(true, "production header presentation skips bindings for a headerless startup owner");
            }

            using (var headerPages = new RmlUiPageManager(host))
            {
                RmlUiDocumentToken headerPage = headerPages.OpenPage(
                    new("application-header-check", "pages/home/home.rml"));
                pagesField.SetValue(null, headerPages);
                host.Update();
                check(host.TryGetElementBounds(headerPage, "topbar", out _, out _, out _, out _),
                    "application header fixture owns the authored topbar");
                Present();
                host.SetBool(headerPage, "visible:header_required_weapon", true);
                host.Update();
                check(host.TryGetElementBounds(headerPage, "header_required_weapon", out _, out _, out float width, out float height)
                    && width > 0 && height > 0,
                    "production header owner accepts and displays the required-weapon alert binding");
                host.SetBool(headerPage, "visible:header_required_weapon", false);
                host.Update();
            }
        }
        finally
        {
            hostField.SetValue(null, oldHost);
            pagesField.SetValue(null, oldPages);
            headerField.SetValue(null, oldHeader);
            launcherPagesField.SetValue(null, oldLauncherPages);
        }

        void Present()
        {
            try { present.Invoke(null, new object?[] { host }); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }
        }
    }
}
