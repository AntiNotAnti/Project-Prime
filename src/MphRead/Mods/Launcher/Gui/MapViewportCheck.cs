#if MPHREAD_SHELL
using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using GLFWBindingsContext = OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext;

namespace MphRead.Mods.Launcher.Gui;

internal static class MapViewportCheck
{
    internal static int Run(string directory, string? projectPath = null)
    {
        directory = Path.GetFullPath(Path.Combine(ConsoleSetup.LaunchDirectory, directory));
        Directory.CreateDirectory(directory);
        try
        {
            return RunCore(directory, projectPath);
        }
        catch (Exception ex) when (DesktopGlContext.HostedMacLacksNsgl(ex)
            || DesktopGlContext.HostedWindowsLacksWgl(ex))
        {
            // GitHub-hosted macOS/Windows runners can expose no OpenGL context.
            // Preserve the exact driver error as a reported hardware SKIP,
            // never as real-device OpenGL rendering acceptance. Linux Mesa
            // exercises the pixels; Windows/macOS need a GPU-capable host.
            string reason = OperatingSystem.IsWindows()
                ? "hosted Windows runner has no WGL OpenGL driver"
                : "hosted macOS runner has no NSGL pixel format";
            Console.WriteLine("MAPVIEWPORT SKIP: " + reason);
            File.WriteAllText(Path.Combine(directory, "mapviewport-skip.txt"),
                "SKIP: " + reason + Environment.NewLine + ex);
            return 0;
        }
        catch (Exception ex)
        {
            // Windows publishes a GUI subsystem binary even for -mapviewportcheck,
            // so stdout/stderr may be invisible. Always preserve the failure in
            // the CI artifact, including failures before the GL context exists.
            File.WriteAllText(Path.Combine(directory, "mapviewport-error.txt"), ex.ToString());
            Console.Error.WriteLine("MAPVIEWPORT SETUP FAIL: " + ex);
            return 1;
        }
    }

    private static int RunCore(string directory, string? projectPath)
    {
        directory = Path.GetFullPath(Path.Combine(ConsoleSetup.LaunchDirectory, directory));
        Directory.CreateDirectory(directory);
        var settings = DesktopGlContext.Settings(background: true);
        settings.StartVisible = true;
        settings.StartFocused = false;
        settings.ClientSize = new(960, 600);
        using var window = new NativeWindow(settings);
        using var graphics = new MphRead.Mods.Render.DesktopGraphicsSession(window);
        var surface = UiSurface.Ensure() ?? throw new InvalidOperationException("No UI surface.");
        int checks = 0;
        var foregroundPlayers = MphRead.Entities.PlayerEntity.LegacyRegistry;
        var foregroundState = GameState.Current;
        var foregroundRandom = Rng.Current;
        void Check(bool success, string name)
        { if (!success) throw new InvalidOperationException(name); Console.WriteLine("MAPVIEWPORT PASS " + name); checks++; }
        var definition = MapTemplates.Create("Renderer check", false).Definition;
        var far = new MapBox { Transform = new() { Position = new[] { 7f, 2f, 0f } } };
        definition.Geometry.Add(far);
        var document = new MapDocument(new MapProject(definition));
        var viewport = new MapViewport(document);
        var panel = new Panel { Background = Brushes.DarkRed, Margin = new Thickness(70, 60, 90, 80) };
        panel.Children.Add(viewport);
        var label = new Border { Background = Brushes.DeepPink, Width = 90, Height = 40,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, IsVisible = false };
        panel.Children.Add(label);
        try
        {
            var inputCamera = viewport.CameraPosition;
            var saveKey = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.S, KeyModifiers = KeyModifiers.Control };
            viewport.RaiseEvent(saveKey);
            Check(!saveKey.Handled && viewport.CameraPosition == inputCamera, "Ctrl+S bubbles without moving camera");
            var commandSave = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.S, KeyModifiers = KeyModifiers.Meta };
            viewport.RaiseEvent(commandSave);
            Check(!commandSave.Handled && viewport.CameraPosition == inputCamera, "Command+S bubbles without moving camera");
            document.TransformSelection(new[] { far.Id }, "Move", System.Numerics.Vector3.One, 0, 1, false);
            var movedState = document.CurrentStateId;
            viewport.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Z, KeyModifiers = KeyModifiers.Control });
            Check(document.CurrentStateId != movedState, "Ctrl+Z undoes viewport edit");
            viewport.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Z, KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift });
            Check(document.CurrentStateId == movedState, "Ctrl+Shift+Z redoes viewport edit");
            document.History.Undo();
            foreach (var size in new[] { new Vector2i(960, 600), new Vector2i(1440, 900), new Vector2i(2880, 1800) })
            {
                window.ClientSize = size;
                NativeWindow.ProcessWindowEvents(false);
                MphRead.Mods.Render.DesktopGraphicsSession.Resize(window);
                MphRead.Mods.Render.DesktopGraphicsSession.Present(window); // GLX commits the resized drawable at swap.
                NativeWindow.ProcessWindowEvents(false);
                // The UI and rendering consume framebuffer pixels, including Retina.
                var target = window.FramebufferSize;
                surface.Resize(target.X, target.Y); surface.Show(panel);
                viewport.FrameAll();
                surface.PrepareMapRenderer();
                for (int i = 0; i < 5; i++) { System.Threading.Thread.Sleep(20); surface.Invalidate(); surface.Tick(); }
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                GL.Viewport(0, 0, target.X, target.Y);
                GL.ClearColor(0, 0, 0, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                surface.DrawMapViewport(target.X, target.Y); UiOverlay.Draw(target.X, target.Y);
                Check(viewport.GpuMeshUploads == 2, "stable meshes upload once at " + size);
                Check(ScreenCapture.SaveWindow(target.X, target.Y, Path.Combine(directory, $"viewport-{size.X}.png")), "window capture " + size);
                using var overlay = new RenderTargetBitmap(new PixelSize((int)viewport.Bounds.Width, (int)viewport.Bounds.Height), new Avalonia.Vector(96, 96));
                overlay.Render(viewport);
                using var stream = new MemoryStream(); overlay.Save(stream, PngBitmapEncoderOptions.Default);
                byte[] picture = viewport.CaptureGpuPreview(stream.ToArray());
                File.WriteAllBytes(Path.Combine(directory, $"preview-{size.X}.png"), picture);
                using var rendered = SkiaSharp.SKBitmap.Decode(picture);
                Check(rendered.Pixels.Count(p => p.Blue > 60 && p.Red > 40) > rendered.Width * rendered.Height / 100,
                    "GPU preview contains visible mesh " + size);
                Check(GL.GetError() == ErrorCode.NoError, "renderer restores GL state " + size);
                var logical = new MapViewportLayout(viewport.Bounds.Width, viewport.Bounds.Height);
                var frame = viewport.BuildRenderFrame(logical);
                var point = frame.Camera.Project(logical, new System.Numerics.Vector3(7, 2, 0))!.Value;
                var surfacePoint = viewport.TranslatePoint(new Point(point.X, point.Y), surface.Root)!.Value;
                document.Selection.Clear();
                surface.PointerMoved(surfacePoint.X / surface.WindowWidth * target.X, surfacePoint.Y / surface.WindowHeight * target.Y);
                surface.PointerButton(Avalonia.Input.MouseButton.Left, true);
                surface.PointerButton(Avalonia.Input.MouseButton.Left, false);
                Check(document.Selection.Contains(far.Id), "window-pixel pointer selects rendered brush " + size);
                document.Selection.Clear(); document.SelectionChanged();
            }
            int uploads = viewport.GpuMeshUploads;
            document.Selection.Add(far.Id); document.SelectionChanged();
            surface.DrawMapViewport(window.FramebufferSize.X, window.FramebufferSize.Y);
            Check(viewport.GpuMeshUploads == uploads, "selection does not upload geometry");
            viewport.SetView("Top"); viewport.Collision = true; viewport.Wireframe = true;
            surface.DrawMapViewport(window.FramebufferSize.X, window.FramebufferSize.Y);
            Check(viewport.GpuMeshUploads == uploads, "camera and overlays do not upload geometry");
            document.TransformSelection(new[] { far.Id }, "Move", System.Numerics.Vector3.UnitX, 0, 1, false);
            surface.DrawMapViewport(window.FramebufferSize.X, window.FramebufferSize.Y);
            Check(viewport.GpuMeshUploads == uploads + 1, "moving one object uploads only that object");
            label.IsVisible = true;
            for (int i = 0; i < 4; i++) { System.Threading.Thread.Sleep(20); surface.Invalidate(); surface.Tick(); }
            surface.DrawMapViewport(window.FramebufferSize.X, window.FramebufferSize.Y);
            UiOverlay.Draw(window.FramebufferSize.X, window.FramebufferSize.Y);
            Check(ScreenCapture.SaveWindow(window.FramebufferSize.X, window.FramebufferSize.Y,
                Path.Combine(directory, "overlay-order.png")), "overlay order capture");
            var center = label.TranslatePoint(new Point(45, 20), surface.Root)!.Value;
            int px = (int)(center.X / surface.WindowWidth * window.FramebufferSize.X);
            int py = window.FramebufferSize.Y - (int)(center.Y / surface.WindowHeight * window.FramebufferSize.Y);
            byte[] marker = new byte[4];
            GL.ReadPixels(px, py, 1, 1, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, marker);
            Check(marker[0] > 240 && marker[1] < 40 && marker[2] > 100,
                $"UI overlay remains above GPU geometry ({px},{py}; {string.Join(',', marker)})");
            label.IsVisible = false;
            foreach (string viewName in new[] { "Front", "Side" })
            {
                viewport.SetView(viewName); viewport.FrameSelection();
                for (int i = 0; i < 3; i++) { System.Threading.Thread.Sleep(20); surface.Invalidate(); surface.Tick(); }
                var position = MapObjects.Find(document.Project.Definition, far.Id)!.Position;
                var beforeDrag = (float[])position.Clone();
                var dragLayout = new MapViewportLayout(viewport.Bounds.Width, viewport.Bounds.Height);
                var projected = viewport.BuildRenderFrame(dragLayout).Camera.Project(dragLayout,
                    new System.Numerics.Vector3(position[0], position[1], position[2]))!.Value;
                var start = viewport.TranslatePoint(new Point(projected.X, projected.Y), surface.Root)!.Value;
                double x = start.X / surface.WindowWidth * window.FramebufferSize.X;
                double y = start.Y / surface.WindowHeight * window.FramebufferSize.Y;
                surface.PointerMoved(x, y);
                surface.PointerButton(MouseButton.Left, true);
                surface.PointerMoved(x, y - 100);
                surface.PointerButton(MouseButton.Left, false);
                var afterDrag = MapObjects.Find(document.Project.Definition, far.Id)!.Position;
                Check(afterDrag[1] > beforeDrag[1], viewName + " free drag moves vertically ("
                    + string.Join(',',beforeDrag) + " -> " + string.Join(',',afterDrag) + ")");
                document.History.Undo();
                var beforeCancel = document.CurrentStateId;
                surface.PointerMoved(x, y);
                surface.PointerButton(MouseButton.Left, true);
                surface.PointerMoved(x + 100, y - 100);
                var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
                viewport.RaiseEvent(escape);
                surface.PointerButton(MouseButton.Left, false);
                Check(escape.Handled && document.CurrentStateId == beforeCancel,
                    viewName + " Escape cancels drag without changing history");
            }
            surface.Hide(); surface.PrepareMapRenderer();
            Check(viewport.GpuMeshUploads == 0 && GL.GetError() == ErrorCode.NoError, "leaving editor releases renderer");
            window.ClientSize = new(1440, 900);
            NativeWindow.ProcessWindowEvents(false);
            MphRead.Mods.Render.DesktopGraphicsSession.Resize(window);
                MphRead.Mods.Render.DesktopGraphicsSession.Present(window);
            NativeWindow.ProcessWindowEvents(false);
            surface.Resize(window.FramebufferSize.X, window.FramebufferSize.Y);
            var studio = new MapStudioScreen(); studio.Load(MapTemplates.Create("Renderer check", false));
            surface.Show(studio); surface.Resize(window.FramebufferSize.X, window.FramebufferSize.Y); surface.PrepareMapRenderer();
            for (int i = 0; i < 5; i++) { System.Threading.Thread.Sleep(20); surface.Invalidate(); surface.Tick(); }
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            surface.DrawMapViewport(window.FramebufferSize.X, window.FramebufferSize.Y);
            UiOverlay.Draw(window.FramebufferSize.X, window.FramebufferSize.Y);
            Check(ScreenCapture.SaveWindow(window.FramebufferSize.X, window.FramebufferSize.Y,
                Path.Combine(directory, "map-studio-renderer.png")), "full editor composite capture");
            var back = studio.GetVisualDescendants().OfType<PrimeButton>().First(b => b.Label == "SAVE");
            var backCenter = back.TranslatePoint(new Point(back.Bounds.Width / 2, back.Bounds.Height / 2), surface.Root)!.Value;
            GL.ReadPixels((int)(backCenter.X / surface.WindowWidth * window.FramebufferSize.X),
                window.FramebufferSize.Y - (int)(backCenter.Y / surface.WindowHeight * window.FramebufferSize.Y),
                1, 1, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, marker);
            // The authored SAVE button is deep blue (#103c76), not light grey.
            // It is visibly present and layered over the world in the captured
            // screenshot, but its red channel is intentionally below 20.
            Check(marker[1] > 35 && marker[2] > 70 && marker[2] > marker[0],
                "editor toolbar survives GPU composite");
            Check(ReferenceEquals(foregroundPlayers, MphRead.Entities.PlayerEntity.LegacyRegistry)
                && ReferenceEquals(foregroundState, GameState.Current) && ReferenceEquals(foregroundRandom, Rng.Current),
                "editor renderer preserves foreground scene ownership");
            Check(surface.ClickOn(c=>c is MenuItem { Header: "File" }),"File menu receives pointer input");
            for(int i=0;i<3;i++){System.Threading.Thread.Sleep(20);surface.Invalidate();surface.Tick();}
            var fileMenu=studio.GetVisualDescendants().OfType<MenuItem>().First(m=>m.Header?.ToString()=="File");
            Check(fileMenu.IsSubMenuOpen,"File menu opens in embedded surface");
            fileMenu.IsSubMenuOpen=false;
            studio.ToggleFourViews();
            surface.PrepareMapRenderer();
            for (int i = 0; i < 5; i++) { System.Threading.Thread.Sleep(20); surface.Invalidate(); surface.Tick(); }
            var panes=studio.GetVisualDescendants().OfType<MapViewport>().ToArray();
            Check(panes.Length==4 && panes.All(v=>ReferenceEquals(v.Document,panes[0].Document)), "four views share one document");
            surface.DrawMapViewport(window.FramebufferSize.X,window.FramebufferSize.Y);UiOverlay.Draw(window.FramebufferSize.X,window.FramebufferSize.Y);
            Check(panes.All(v=>v.GpuMeshUploads>0) && GL.GetError()==ErrorCode.NoError, "all four GPU viewports render");
            Check(ScreenCapture.SaveWindow(window.FramebufferSize.X,window.FramebufferSize.Y,Path.Combine(directory,"map-studio-four-gpu.png")),"four-view GPU capture");
            studio.ToggleFourViews();surface.PrepareMapRenderer();
            Check(panes.Count(v=>v.GpuMeshUploads>0)==1,"single view releases other GPU resources");
            studio.ToggleFourViews();studio.ToggleFourViews();
            Check(studio.GetVisualDescendants().OfType<MapViewport>().Count()==1,"repeated layout toggles reparent safely");
            Check(UiLayout.EditorFactor(1440,900)==1 && UiLayout.EditorFactor(2880,1800)==2,
                "editor doubles control size for doubled framebuffer resolution");
            var overlays = new PrimeOverlayHost();
            var libraryStudio = new MapStudioScreen(overlays); libraryStudio.Load(MapTemplates.Create("Library layout",false));
            var libraryRoot = new Panel(); libraryRoot.Children.Add(libraryStudio); libraryRoot.Children.Add(overlays);
            surface.Show(libraryRoot); surface.Resize(window.FramebufferSize.X,window.FramebufferSize.Y);
            typeof(MapStudioScreen).GetMethod("ShowLibrary",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(libraryStudio,null);
            for(int i=0;i<5;i++){System.Threading.Thread.Sleep(20);surface.Invalidate();surface.Tick();}
            // PrimeOverlayHost always contains a scrim plus the active modal
            // frame. Identify the actual Map Library modal, not an arbitrary
            // count of overlay children (which also includes animated frames).
            var libraryFrame = overlays.Children.OfType<Border>().Single(frame =>
                frame.Child is Grid grid && grid.Children.OfType<TextBlock>()
                    .Any(label => label.Text == "MAP LIBRARY"));
            Check(libraryFrame.Bounds.Height < 550 && libraryFrame.Bounds.Height > 460,
                "map library fits its content instead of stretching to window height");
            GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
            surface.PrepareMapRenderer();surface.DrawMapViewport(window.FramebufferSize.X,window.FramebufferSize.Y);UiOverlay.Draw(window.FramebufferSize.X,window.FramebufferSize.Y);
            Check(ScreenCapture.SaveWindow(window.FramebufferSize.X,window.FramebufferSize.Y,Path.Combine(directory,"map-library-scaled.png")),"scaled map library capture");
            overlays.Clear();surface.Show(studio);surface.Resize(window.FramebufferSize.X,window.FramebufferSize.Y);surface.PrepareMapRenderer();
            if (projectPath != null)
            {
                var imported = MapDefinition.Load(Path.GetFullPath(Path.Combine(ConsoleSetup.LaunchDirectory, projectPath)));
                var analysis = MapBuildScheduler.Shared.AnalyzeAsync(MapBuildSnapshot.Capture(imported)).GetAwaiter().GetResult();
                Check(analysis.Succeeded, "imported benchmark map compiles");
                var importedViewport = new MapViewport(new MapDocument(new MapProject(imported)));
                importedViewport.SetImported(analysis); importedViewport.FrameAll();
                var importedPanel = new Panel { Margin = new Thickness(40) }; importedPanel.Children.Add(importedViewport);
                surface.Show(importedPanel); surface.PrepareMapRenderer();
                for (int i = 0; i < 5; i++) { System.Threading.Thread.Sleep(20); surface.Invalidate(); surface.Tick(); }
                var target = window.FramebufferSize;
                GL.Disable(EnableCap.ScissorTest);
                GL.Viewport(0, 0, target.X, target.Y);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 20; i++) { surface.DrawMapViewport(target.X, target.Y); GL.Finish(); }
                double gpuMs = clock.Elapsed.TotalMilliseconds / 20;
                int importedUploads = importedViewport.GpuMeshUploads;
                UiOverlay.Draw(target.X, target.Y);
                Check(ScreenCapture.SaveWindow(target.X, target.Y, Path.Combine(directory, "imported.png")), "imported map renderer capture");
                Check(importedViewport.VisibleEntityLabelBounds.Count > 0, "imported entity labels remain visible");
                var labelBounds = importedViewport.VisibleEntityLabelBounds;
                Check(!labelBounds.Where((bounds, index) => labelBounds.Skip(index + 1).Any(bounds.Intersects)).Any(),
                    "dense imported entity labels do not overlap");
                importedViewport.EntityVisualization = false;
                importedViewport.InvalidateVisual();
                for (int i = 0; i < 5; i++) { System.Threading.Thread.Sleep(20); surface.Invalidate(); surface.Tick(); }
                Check(importedViewport.VisibleEntityLabelBounds.Count == 0, "entity helper toggle hides imported labels");
                importedViewport.EntityVisualization = true;
                importedViewport.Collision = true;
                surface.DrawMapViewport(target.X, target.Y);
                Check(importedViewport.GpuMeshUploads == importedUploads, "imported collision switch reuses GPU geometry");
                UiOverlay.Draw(target.X, target.Y);
                Check(ScreenCapture.SaveWindow(target.X, target.Y, Path.Combine(directory, "imported-collision.png")), "imported collision capture");
                importedViewport.Collision = false; importedViewport.ReleaseRenderer();
                using var cpuTarget = new RenderTargetBitmap(new PixelSize((int)importedViewport.Bounds.Width,
                    (int)importedViewport.Bounds.Height), new Avalonia.Vector(96, 96));
                cpuTarget.Render(importedViewport); clock.Restart();
                for (int i = 0; i < 5; i++) cpuTarget.Render(importedViewport);
                Console.WriteLine($"MAPVIEWPORT PROFILE faces={analysis.Faces.Length} collision={analysis.CollisionFaces.Length} "
                    + $"uploads={importedUploads} GPU-draw-ms={gpuMs:F3} CPU-fallback-raster-ms={clock.Elapsed.TotalMilliseconds / 5:F3}; "
                    + "GPU includes driver completion; CPU includes UI polygon rasterization, excludes upload. Not whole-editor frame times.");
            }
            string texturePath = Path.Combine(directory,"green.tex");
            using (var texture = new BinaryWriter(File.Create(texturePath)))
            {
                texture.Write(System.Text.Encoding.ASCII.GetBytes("FPTX")); texture.Write((ushort)1); texture.Write((ushort)1);
                texture.Write((ushort)0); texture.Write((ushort)8); texture.Write((ushort)8); texture.Write((ushort)1); texture.Write((ushort)0);
                texture.Write((ushort)992); texture.Write(new byte[64]);
            }
            var texturedDefinition = new MapDefinition { BaseDirectory = directory };
            texturedDefinition.Materials.Add(new() {Texture="green.tex"});
            texturedDefinition.Geometry.Add(new MapBox());
            var texturedDocument = new MapDocument(new MapProject(texturedDefinition));
            var texturedView = new MapViewport(texturedDocument);
            var texturedPanel = new Panel(); texturedPanel.Children.Add(texturedView);
            surface.Show(texturedPanel); surface.Resize(window.FramebufferSize.X,window.FramebufferSize.Y); surface.PrepareMapRenderer();
            for(int i=0;i<5;i++){System.Threading.Thread.Sleep(20);surface.Invalidate();surface.Tick();}
            texturedView.FrameAll();
            surface.DrawMapViewport(window.FramebufferSize.X,window.FramebufferSize.Y);
            using(var overlay = new RenderTargetBitmap(new PixelSize((int)texturedView.Bounds.Width,(int)texturedView.Bounds.Height),new Avalonia.Vector(96,96)))
            {
                overlay.Render(texturedView); using var stream = new MemoryStream(); overlay.Save(stream,PngBitmapEncoderOptions.Default);
                byte[] picture = texturedView.CaptureGpuPreview(stream.ToArray());
                File.WriteAllBytes(Path.Combine(directory,"textured-material.png"),picture);
                using var bitmap = SkiaSharp.SKBitmap.Decode(picture);
                Check(bitmap.Pixels.Count(p=>p.Green>60 && p.Green>p.Red*2 && p.Green>p.Blue*2)>bitmap.Pixels.Length/100,
                    "authored baked texture is visible in GPU output");
            }
            int textureUploads = texturedView.GpuTextureUploads;
            Check(textureUploads==1,"one material uploads exactly one texture");
            texturedView.SetView("Top"); texturedDocument.Selection.Add(texturedDefinition.Geometry[0].Id); texturedDocument.SelectionChanged();
            surface.DrawMapViewport(window.FramebufferSize.X,window.FramebufferSize.Y);
            Check(texturedView.GpuTextureUploads==textureUploads,"camera and selection reuse texture bindings");
            texturedView.UvChecker=true; surface.DrawMapViewport(window.FramebufferSize.X,window.FramebufferSize.Y);
            Check(texturedView.GpuTextureUploads==textureUploads+1,"checker uploads once without replacing material assets");
            texturedView.UvChecker=false; surface.DrawMapViewport(window.FramebufferSize.X,window.FramebufferSize.Y);
            Check(texturedView.GpuTextureUploads==textureUploads+1,"leaving checker reuses authored texture");
            Check(GL.GetError()==ErrorCode.NoError,"textured rendering leaves valid GL state");
            Console.WriteLine($"MAPVIEWPORT {checks} checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "mapviewport-error.txt"), ex.ToString());
            Console.Error.WriteLine("MAPVIEWPORT FAIL: " + ex);
            Console.WriteLine("MAPVIEWPORT " + ex);
            return 1;
        }
        finally { surface.ReleaseMapRenderer(); surface.Hide(); UiOverlay.Release(); }
    }
}
#endif
