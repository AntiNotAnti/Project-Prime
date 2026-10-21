#if MPHREAD_RMLUI_GL_SMOKE
using System.Runtime.InteropServices;
using System.Reflection;
using MphRead.Mods;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Hud;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render.Hud;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

internal static class NativeHudGlCheck
{
    public static void Run(string library,string assets,string output)
    {
        Directory.CreateDirectory(output);
        nint module=NativeLibrary.Load(Path.GetFullPath(library));
        NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,(name,_,_)=>name=="ProjectPrime.RmlUi.Native"?module:0);
        int checks=0;
        void Check(bool value,string message){if(!value)throw new InvalidOperationException("HUD GL: "+message);checks++;}
        foreach(var size in new[]{(1280,720),(960,540),(640,800)})
        {
            using var window=new NativeWindow(new NativeWindowSettings{ClientSize=new(size.Item1,size.Item2),API=ContextAPI.OpenGL,APIVersion=new Version(3,3),Profile=ContextProfile.Compatability,StartVisible=false,Title="HUD Studio validation"});
            window.Context.MakeCurrent();GL.LoadBindings(new GLFWBindingsContext());
            int width=window.FramebufferSize.X,height=window.FramebufferSize.Y;
            using var host=new RmlUiHost();Check(host.Initialize(width,height,1,Path.GetFullPath(assets),RmlUiRenderBackend.OpenGl),"native GL initialization");
            using var pages=new RmlUiPageManager(host);
            using var controller=new HudEditorController(HudProfileDefaults.Create("Project Prime"),new HudBoundary());
            using var page=new HudEditorPagePresenter(host,pages,controller,new HudPreviewBitmap(Path.Combine(output,"cache"),nativeRoot:""));
            page.Open();host.Update();
            Check(host.TryGetElementBounds(page.Document,"hud_canvas",out _,out _,out float cw,out float ch)&&cw>100&&ch>100,"preview canvas bounds");
            page.SetCanvasSize(cw,ch);
            Action? visualPresent=null;
            void Frame()
            {
                page.Present();visualPresent?.Invoke();host.Update();GL.BindFramebuffer(FramebufferTarget.Framebuffer,0);GL.Viewport(0,0,width,height);
                GL.Disable(EnableCap.ScissorTest);GL.ClearColor(.025f,.05f,.08f,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit|ClearBufferMask.StencilBufferBit);
                RmlUiOpenGlFrame.Draw(host,width,height);GL.Finish();
            }
            void Settle(){var watch=System.Diagnostics.Stopwatch.StartNew();do{Frame();Thread.Sleep(5);}while(watch.ElapsedMilliseconds<650);}
            void Capture(string name){Settle();Check(ScreenCapture.SaveWindow(width,height,Path.Combine(output,$"hud-{width}x{height}-{name}.png")),"capture "+name);}
            Capture("appearance");
            controller.Dispatch(HudEditorAction.SectionLayout);Capture("position-inline");
            controller.Dispatch(HudEditorAction.SectionDecorations);Capture("decorations-inline");
            controller.Dispatch(HudEditorAction.SectionGlobal);Capture("hud-wide");
            controller.Dispatch(HudEditorAction.WorkspaceProfiles);Capture("profiles");
            Type settingsType=typeof(HudEditorController).Assembly.GetType("MphRead.Mods.Launcher.NativeFullSettingsController")!;
            using var settingsController=(IDisposable)settingsType.GetConstructors(BindingFlags.NonPublic|BindingFlags.Instance).Single().Invoke(new object?[]{new MphRead.MenuSettings(),null});
            Type visualType=typeof(HudEditorController).Assembly.GetType("MphRead.Mods.Launcher.RmlUi.Pages.Settings.NativeSettingsVisualEditor")!;
            using(var visual=(IDisposable)visualType.GetConstructors(BindingFlags.NonPublic|BindingFlags.Instance).Single().Invoke(new object[]{host,pages,settingsController,2}))
            {
                visualPresent=()=>visualType.GetMethod("Present",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(visual,null);
                Settle();
                Check(host.TryGetElementBounds(pages.Top,"settings_visual_wheel",out float wx,out float wy,out float ww,out float wh)&&ww>100&&wh>100,"outline wheel has real native bounds");
                Capture("outline-wheel");
                Check((bool)visualType.GetMethod("PointerDown",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(visual,new object[]{wx+ww*.8f,wy+wh*.5f})!,"outline disk accepts direct pointer selection");
                visualType.GetMethod("ReleasePointer",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(visual,null);
                Capture("outline-wheel-selected");visualPresent=null;
            }
            Check(GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,"GL frame leaves no error");
        }
        Console.WriteLine($"HUD GL PASS {checks} checks; real native screenshots in {Path.GetFullPath(output)}");
        // Resolver owns this library for the process lifetime.
    }
}
#endif
