using MphRead.Mods.Render;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Studio;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using OpenTK.Graphics.OpenGL;

/// <summary>Actual native editor chrome and retained geometry renderer on the tool's existing GL context.</summary>
internal static class StudioEditorGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string isolated = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? "";
        string canonical = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Project Prime");
        if (!Path.IsPathFullyQualified(isolated) || Path.GetFullPath(isolated).TrimEnd(Path.DirectorySeparatorChar) == canonical
            || Path.GetFullPath(isolated).StartsWith(canonical + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("--studio-editor requires a fresh explicit absolute isolated PROJECT_PRIME_USER_DATA at process launch.");
        string root = Path.Combine(Path.GetFullPath(isolated), "native-studio-editor", width + "x" + height);
        Directory.CreateDirectory(root);
        string previousMaps = CustomRooms.UserMapDirectory, previousCatalog = CustomRooms.MapDirectory;
        CustomRooms.UserMapDirectory = Path.Combine(root, "synthetic-maps"); CustomRooms.MapDirectory = Path.Combine(root, "empty-catalog");
        Directory.CreateDirectory(CustomRooms.UserMapDirectory); Directory.CreateDirectory(CustomRooms.MapDirectory);
        int checks = 0;
        try
        {
            Check(GuiLauncher.EnsureSetup(requireDisplay: false), "retained owner uses existing headless toolkit without another game window");
            using var owner = new MapStudioScreen(preview: true);
            var document = Field<MapDocument>(owner, "_document");
            var viewport = Field<MapViewport>(owner, "_viewport");
            Check(owner.TryAcquireNativeEditor(out var acquired, out _) && acquired != null, "actual native editor borrows production retained owner");
            using var session = acquired!;
            using var pages = new RmlUiPageManager(host);
            using var page = new NativeStudioEditorPage(host, pages, session);
            host.Input.SetFramebufferScale(1, 1); page.Open(); Settle();
            var renderer = Field<Scene>(viewport, "_renderer");
            Check(renderer.EditorMeshCount > 0 && ReferenceEquals(document, Field<MapDocument>(owner, "_document"))
                && viewport.NativePresentationLeased, "actual existing renderer has synthetic geometry under native chrome with same document owner");
            Capture("entry-before-semantic");
            var initialSemantic=NativeSemanticReadback.Read(host,page.Document);var initialOwner=session.Capture();
            string expectedTitle=NativeSemanticReadback.Normalize(initialOwner.Name+(initialOwner.Dirty?" // UNSAVED":" // SAVED"));
            bool titleBounds=host.TryGetElementBounds(page.Document,"native_editor_title",out float titleX,out float titleY,out float titleWidth,out float titleHeight);
            bool statusBounds=host.TryGetElementBounds(page.Document,"native_editor_status",out float statusX,out float statusY,out float statusWidth,out float statusHeight);
            File.WriteAllText(Path.Combine(root,"entry-semantic-diagnostics.json"),JsonSerializer.Serialize(new{
                documentId=page.Document.DocumentId,headingId="native_editor_title",title=initialSemantic.Label("native_editor_title"),expectedTitle,
                statusId="native_editor_status",status=initialSemantic.Label("native_editor_status"),
                titleBounds=new{valid=titleBounds,x=titleX,y=titleY,width=titleWidth,height=titleHeight},
                statusBounds=new{valid=statusBounds,x=statusX,y=statusY,width=statusWidth,height=statusHeight},width,height
            },new JsonSerializerOptions{WriteIndented=true}));
            Check(titleBounds&&titleWidth>0&&titleHeight>0&&initialSemantic.Label("native_editor_title")==expectedTitle,
                "actual visible project heading presents exact project name and saved state by native ID");
            Check(initialSemantic.Label("native_editor_status").StartsWith("NATIVE OBJECT AND COMPONENT EDITOR //",StringComparison.Ordinal),
                "actual initial native editor status identifies object and component workspace by native ID");
            Capture("entry");
            Action("native_editor_row0", 47);
            Guid selected = document.ActiveObjectId; float[] before = (float[])MapObjects.Find(document.Project.Definition, selected)!.Position.Clone();
            host.SetField(page.Document, "native_editor_move_x", "2"); host.SetField(page.Document, "native_editor_move_y", "0"); host.SetField(page.Document, "native_editor_move_z", "0");
            int uploads = renderer.EditorMeshUploads; Action("native_editor_move", 36);
            Check(MapObjects.Find(document.Project.Definition, selected)!.Position[0] == before[0] + 2 && renderer.EditorMeshUploads > uploads,
                "native Apply Move updates real document history and existing renderer mesh cache");
            Action("native_editor_undo", 33); Check(MapObjects.Find(document.Project.Definition, selected)!.Position.SequenceEqual(before), "native Undo restores actual geometry transform");
            Action("native_editor_redo", 34); Check(MapObjects.Find(document.Project.Definition, selected)!.Position[0] == before[0] + 2, "native Redo reapplies actual geometry transform");
            Action("native_editor_frame_all", 39); Action("native_editor_top", 42); Action("native_editor_front", 43); Action("native_editor_side", 44); Action("native_editor_perspective", 41);
            Check(ReferenceEquals(renderer, Field<Scene>(viewport, "_renderer")), "all view/edit commands reuse the exact existing renderer resource owner");
            Capture("edited");
            Bounds(out int left, out int top, out int vw, out int vh);
            double cx = left + vw / 2d, cy = top + vh / 2d;
            var layout = new MapViewportLayout(vw, vh, 1, left, top);
            Guid expected = MapViewportPicking.Pick(viewport.BuildRenderFrame(layout), vw / 2d, vh / 2d);
            Check(expected != Guid.Empty && page.PointerButton(0, cx, cy, true), "actual native viewport center ray hits synthetic geometry");
            page.PointerButton(0, cx, cy, false);
            Check(document.ActiveObjectId == expected, "native pointer selection uses existing geometry picker and exact object identity"); Settle();
            string unchanged = document.Project.Definition.Serialize();
            var camera = viewport.CameraPosition;
            Check(page.PointerButton(1, cx, cy, true) && page.PointerMoved(cx + 4, cy + 2), "actual viewport owns contained right-drag camera capture");
            page.PointerButton(1, cx + 4, cy + 2, false);
            Check(viewport.CameraPosition != camera && document.Project.Definition.Serialize() == unchanged, "native orbit moves the existing camera without editing document/history");
            Check(!page.PointerButton(0, -1, -1, true) && !page.Wheel(-1, -1, 1), "outside-rectangle pointer and wheel cannot select or move camera");
            Settle(); camera = viewport.CameraPosition;
            Check(page.PointerButton(1, cx, cy, true), "camera captures before density-only change");
            Check(page.DrawViewport(width, height, Math.Max(1, width / 2), Math.Max(1, height / 2)), "same framebuffer rectangle draws under new client density");
            Check(!page.PointerMoved(cx / 2 + 1, cy / 2 + 1) && viewport.CameraPosition == camera,
                "density-only basis change with same pixel rectangle releases drag before next movement");
            Check(page.ValidateInputBasis(width, height, Math.Max(1, width / 2), Math.Max(1, height / 2)), "input basis accepts exact last successful density draw");
            Check(!page.ValidateInputBasis(width, height, width, height) && !page.PointerButton(0, cx, cy, true),
                "resize-before-draw basis fence invalidates stale rectangle before pointer selection");
            Frame(); Check(page.ValidateInputBasis(width, height, width, height), "fresh layout/draw republishes valid window input basis");
            Check(page.PointerButton(1, cx, cy, true), "camera recaptures after current basis restored");
            Check(host.FocusDocument(page.Document, "native_editor_move_x"), "native numeric field takes focus from viewport"); host.Update();
            camera = viewport.CameraPosition; page.DrawViewport(width, height, width, height);
            Check(!page.PointerMoved(cx + 2, cy + 2) && viewport.CameraPosition == camera, "post-update focus transfer releases camera before movement");
            host.FocusDocument(page.Document, NativeStudioEditorPage.ViewportId); Frame();
            Check(page.PointerButton(1, cx, cy, true), "camera captures before modal coverage");
            pages.OpenModal(new("studio-test-modal", "pages/studio/native-entry.rml", "native_studio_back")); host.Update();
            Check(!page.DrawViewport(width, height) && !page.PointerMoved(cx + 2, cy + 2), "covered editor cannot draw/pick/drag from background document");
            pages.CloseModal(); Settle();
            StudioPropertyGlChecks.Run(host, pages, page, owner, width, height, Check);
            StudioResourceGlChecks.Run(host,pages,page,owner,width,height,root,Check);
            StudioModelingGlChecks.Run(host,pages,page,owner,width,height,root,Check);
            string save = Path.Combine(CustomRooms.UserMapDirectory, "native-gl.json"); host.SetField(page.Document, "native_editor_path", save);
            Action("native_editor_save", 35);
            bool saveExists=File.Exists(save),saveClean=!document.IsDirty,sameSaveDocument=ReferenceEquals(document,Field<MapDocument>(owner,"_document"));
            string saveStatus=Field<NativeStudioEditorController>(page,"_controller").Current().Status;
            Check(saveExists && saveClean && sameSaveDocument, "actual native Save delegates existing writer in private fixture directory"
                + $" // file={saveExists}; clean={saveClean}; same document={sameSaveDocument}; status={saveStatus}");
            StudioAssetIoGlChecks.Run(host,pages,page,owner,width,height,root,Check);
            Action(NativeStudioEditorPage.PublishButtonId, 5, RmlUiIntentKind.CommunityDetailAction);
            Check(page.TryTakePublishRequest() && !page.TryTakePublishRequest(), "actual editor publish button requests one frozen-owner capture without network action");
            Capture("saved");
            Action("native_editor_back", 32);
            Check(page.Closed && !session.Capture().Alive && !viewport.NativePresentationLeased, "native Back releases lease after successful real recovery ordering");
            page.Dispose(); owner.Dispose();
            StudioProjectChangeGlChecks.Run(host, pages, width, height, root, Check);
            Check(GL.GetError() == ErrorCode.NoError, "actual native editor frames/input leave no GL error");
            File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(new { checks, width, height,
                scope = "Actual native RmlUi object-editor page/input and existing retained synthetic-map geometry renderer on tool GL context. Real document/history/private save/recovery; no new game window, application loop, account, presence, network or game simulation. Not device/performance/full-editor qualification." }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"RML STUDIO EDITOR GL {checks} checks passed at actual {width}x{height}; retained synthetic-map owner/renderer and private saves only");

            void Frame()
            {
                page.Refresh(); host.Update(); GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.UseProgram(0); GL.ActiveTexture(TextureUnit.Texture0);
                GL.Viewport(0, 0, width, height); GL.Disable(EnableCap.ScissorTest); GL.ClearColor(.02f, .04f, .06f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                int program = GL.GetInteger(GetPName.CurrentProgram), draw = GL.GetInteger(GetPName.DrawFramebufferBinding), read = GL.GetInteger(GetPName.ReadFramebufferBinding);
                int texture = GL.GetInteger(GetPName.ActiveTexture); var box = new int[4]; GL.GetInteger(GetPName.Viewport, box);
                Check(page.DrawViewport(width, height, width, height), "existing viewport draws a contained actual native rectangle");
                var after = new int[4]; GL.GetInteger(GetPName.Viewport, after);
                Check(GL.GetInteger(GetPName.CurrentProgram) == program && GL.GetInteger(GetPName.DrawFramebufferBinding) == draw
                    && GL.GetInteger(GetPName.ReadFramebufferBinding) == read && GL.GetInteger(GetPName.ActiveTexture) == texture
                    && box.SequenceEqual(after) && !GL.IsEnabled(EnableCap.ScissorTest), "existing engine viewport restores compositor program/FBO/active-texture/viewport/scissor contract");
                RmlUiOpenGlFrame.Draw(host, width, height);
            }
            void Settle() { for (int i = 0; i < 4; i++) Frame(); }
            void Action(string id, int argument, RmlUiIntentKind kind = RmlUiIntentKind.StudioAction)
            {
                host.DiscardIntents(); Check(host.FocusDocument(page.Document, id), "native editor control focuses: " + id); Frame();
                host.Input.Key(2, true); host.Input.Key(2, false); host.Update();
                Check(host.TryTakeIntent(out var intent) && intent.Document == page.Document && intent.Kind == kind && intent.Argument == argument
                    && page.Handle(intent) && !host.TryTakeIntent(out _), "one exact typed native editor action: " + id);
                if (!page.Closed) Settle();
            }
            void Bounds(out int left, out int top, out int w, out int h)
            {
                Check(host.TryGetElementBounds(page.Document, NativeStudioEditorPage.ViewportId, out float x, out float y, out float widthValue, out float heightValue), "native viewport rectangle is authored/visible");
                left = (int)Math.Ceiling(x); top = (int)Math.Ceiling(y); w = (int)Math.Floor(x + widthValue) - left; h = (int)Math.Floor(y + heightValue) - top;
            }
            void Capture(string name)
            { Settle(); string path = Path.Combine(root, name + ".png"); Check(ScreenCapture.SaveWindow(width, height, path), "native editor capture: " + name); FinalCompositeCapture.WriteEvidence(path, width, height, "Actual native RmlUi editor and retained synthetic-map geometry renderer; private saves, no live application/account/network."); }
        }
        finally { CustomRooms.UserMapDirectory = previousMaps; CustomRooms.MapDirectory = previousCatalog; }
        void Check(bool value, string label)
        { if (!value) throw new InvalidOperationException("RML STUDIO EDITOR GL: " + label); checks++; Console.WriteLine("RML STUDIO EDITOR GL PASS " + label); }
    }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
}
