using System.Reflection;
using MphRead;
using MphRead.Formats;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

/// <summary>Production retained document/history/writer lease; isolated synthetic map, no GL or application loop.</summary>
internal static class StudioEditorOwnerAcceptance
{
    internal static void Run()
    {
        // Process-launch isolation also protects against early static initialization.
        string startupIsolation = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? "";
        if (!Path.IsPathFullyQualified(startupIsolation))
            throw new ArgumentException("--studio-owner requires explicit absolute isolated PROJECT_PRIME_USER_DATA at process launch.");
        string canonical = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Project Prime");
        string isolated = Path.GetFullPath(startupIsolation).TrimEnd(Path.DirectorySeparatorChar);
        if (isolated == canonical || isolated.StartsWith(canonical + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("--studio-owner requires an isolated user-data root.");
        string root = Path.Combine(isolated, "prime-native-studio-owner-" + Guid.NewGuid().ToString("N"));
        string previousDirectory = Directory.GetCurrentDirectory();
        string? previousData = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", root);
        Directory.SetCurrentDirectory(root);
        int checks = 0;
        try
        {
            Paths.UpdatePaths(); Paths.SetPath(Paths.MphKey, Path.Combine(root, "no-cartridge-assets"));
            CustomRooms.UserMapDirectory = Path.Combine(root, "user-maps");
            CustomRooms.MapDirectory = Path.Combine(root, "catalog");
            Directory.CreateDirectory(CustomRooms.UserMapDirectory); Directory.CreateDirectory(CustomRooms.MapDirectory);
            Check(GuiLauncher.EnsureSetup(requireDisplay: false), "existing headless toolkit initializes on the test owner thread without a game window");
            using var owner = new MapStudioScreen(preview: true);
            var document = Field<MapDocument>(owner, "_document");
            var viewport = Field<MapViewport>(owner, "_viewport");
            Check(Field<Scene?>(viewport, "_renderer") == null, "synthetic retained document creates no renderer/Scene/GL resource");
            Check(owner.TryAcquireNativeEditor(out var acquired, out _) && acquired != null, "real retained editor grants one native presentation lease");
            using var session = acquired!;
            Check(!owner.TryAcquireNativeEditor(out _, out _) && session.Capture().Alive,
                "second presentation lease is refused while original remains live");
            var writer = Field<MapAutosaveService>(owner, "_autosave");
            var watcher = Field<MapSourceWatchService>(owner, "_sourceWatch");
            Check(Field<bool>(owner, "_ownerRunning") && !Field<bool>(owner, "_detached") && viewport.NativePresentationLeased,
                "hidden legacy control keeps its real autosave/validation/source-watch owner active during native lease");
            Check(watcher != null && writer != null && ReferenceEquals(document, Field<MapDocument>(owner, "_document")),
                "native lease retains the exact document and source watcher rather than creating an editor session");
            Check(!owner.NativeCreateProject("SHOULD_NOT_REPLACE", "basic-ffa").Success && ReferenceEquals(document, Field<MapDocument>(owner, "_document")),
                "native library creation cannot replace a document under an active editor presentation lease");
            Guid id = document.Project.Definition.Geometry.First().Id;
            Check(session.Select(session.Capture().Ticket, id).Success, "production hierarchy selection targets existing object ID");
            var selected = MapObjects.Find(document.Project.Definition, id)!;
            float[] before = (float[])selected.Position.Clone();
            var stale = session.Capture().Ticket;
            Check(session.Execute(stale, NativeStudioEditorOperation.Move, new(1, 2, 3)).Success,
                "production numeric Move executes existing document TransformSelection command");
            Check(MapObjects.Find(document.Project.Definition, id)!.Position.SequenceEqual(new[] { before[0] + 1, before[1] + 2, before[2] + 3 })
                && document.History.CanUndo && ReferenceEquals(writer, Field<MapAutosaveService>(owner, "_autosave"))
                && ReferenceEquals(watcher, Field<MapSourceWatchService>(owner, "_sourceWatch")),
                "native edit keeps exact document/history/writer/watcher ownership and changes only requested transform");
            string edited = document.Project.Definition.Serialize();
            Check(!session.Execute(stale, NativeStudioEditorOperation.Move, new(100, 0, 0)).Success
                && document.Project.Definition.Serialize() == edited, "stale document revision cannot apply a second transform");
            Check(session.Execute(session.Capture().Ticket, NativeStudioEditorOperation.Undo, default).Success
                && MapObjects.Find(document.Project.Definition, id)!.Position.SequenceEqual(before), "real retained history undoes native Move exactly");
            Check(session.Execute(session.Capture().Ticket, NativeStudioEditorOperation.Redo, default).Success
                && document.Project.Definition.Serialize() == edited, "real retained history redoes native Move exactly");
            Check(!session.Execute(session.Capture().Ticket, NativeStudioEditorOperation.Move, new(float.NaN, 0, 0)).Success
                && document.Project.Definition.Serialize() == edited, "owner itself rejects nonfinite transform outside controller");
            string save = Path.Combine(CustomRooms.UserMapDirectory, "native-fixture.json");
            Check(session.Execute(session.Capture().Ticket, NativeStudioEditorOperation.Save, new(Path: save)).Success
                && File.Exists(save) && !document.IsDirty && ReferenceEquals(document, Field<MapDocument>(owner, "_document")),
                "native save uses existing SaveTo and keeps exact retained document while marking saved history state");
            File.Delete(save); Directory.CreateDirectory(save);
            Check(!session.Execute(session.Capture().Ticket, NativeStudioEditorOperation.Save, new(Path: save)).Success && !document.IsDirty,
                "clean same-path save reports actual I/O failure instead of inferring success from clean history");
            Directory.Delete(save);
            Check(session.Execute(session.Capture().Ticket, NativeStudioEditorOperation.Save, new(Path: save)).Success,
                "repaired private save destination permits the existing save writer to complete");
            Check(session.Execute(session.Capture().Ticket, NativeStudioEditorOperation.Move, new(4, 0, 0)).Success, "post-save edit becomes dirty in retained history");
            var activeWriter = Field<MapAutosaveService>(owner, "_autosave");
            Check(activeWriter.Queue(document.CaptureAutosave(CustomRooms.UserMapDirectory)), "existing sole autosave writer accepts a detached current snapshot");
            Check(session.Execute(session.Capture().Ticket, NativeStudioEditorOperation.Move, new(5, 0, 0)).Success, "newer edit follows in-flight recovery snapshot");
            string latest = document.Project.Definition.Serialize();
            Check(session.TryClose().Success && activeWriter.Completion.IsCompleted,
                "native close waits the existing older writer before final recovery and release");
            string recovery = document.RecoveryPath(CustomRooms.UserMapDirectory);
            Check(MapDocument.ReadRecovery(recovery).Definition.Serialize() == latest,
                "final recovery exactly preserves newest native edit after older autosave retires");
            Check(!session.Capture().Alive && !viewport.NativePresentationLeased && Field<bool>(owner, "_detached")
                && !Field<bool>(owner, "_ownerRunning") && ReferenceEquals(document, Field<MapDocument>(owner, "_document")),
                "close releases native presentation and suspends hidden owner while retaining document/history for handback");
            Check(owner.TryAcquireNativeEditor(out var resumed, out _) && resumed != null, "same retained editor can reacquire native presentation");
            using var second = resumed!;
            viewport.GetType().GetField("_orbit", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewport, true);
            session.CancelInput();
            Check(Field<bool>(viewport, "_orbit"), "retired session input cleanup cannot cancel replacement owner's viewport interaction");
            second.CancelInput();
            Check(!Field<bool>(viewport, "_orbit"), "current session may cancel its own borrowed viewport interaction");
            Check(second.Capture().Ticket.Lease != stale.Lease && !second.Execute(stale, NativeStudioEditorOperation.Undo, default).Success,
                "new lease rejects old session ticket even when document/history remain retained");
            Check(second.Execute(second.Capture().Ticket, NativeStudioEditorOperation.Undo, default).Success
                && ReferenceEquals(document.History, Field<MapDocument>(owner, "_document").History),
                "reacquired native editor uses the exact original undo history");
            var reservedWriter = Field<MapAutosaveService>(owner, "_autosave");
            Check(reservedWriter.Queue(document.CaptureAutosave(CustomRooms.UserMapDirectory)), "existing writer owns recovery before rejected reserved manual save");
            Check(!second.Execute(second.Capture().Ticket, NativeStudioEditorOperation.Save,
                    new(Path: document.RecoveryPath(CustomRooms.UserMapDirectory))).Success
                && ReferenceEquals(reservedWriter, Field<MapAutosaveService>(owner,"_autosave")) && second.Capture().Alive,
                "reserved-path preflight leaves exact sole autosave writer and current native document owner intact");
            string normalMaps = CustomRooms.UserMapDirectory;
            string blocked = Path.Combine(root, "file-not-directory"); File.WriteAllText(blocked, "synthetic blocker");
            CustomRooms.UserMapDirectory = blocked;
            Check(!second.TryClose().Success && second.Capture().Alive && viewport.NativePresentationLeased,
                "real recovery I/O failure vetoes native close and retains live editable document");
            CustomRooms.UserMapDirectory = normalMaps;
            Check(second.TryClose().Success && !second.Capture().Alive, "repairing isolated recovery destination permits clean close");
            Check(Field<Scene?>(viewport, "_renderer") == null, "all owner lifecycle/edit/save tests create no renderer or Scene");
            Check(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).All(p => Path.GetFullPath(p).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)),
                "all synthetic map/save/recovery/source-watch files remain inside private fixture root");
            owner.Dispose();
            StudioObjectOwnerChecks.Run(Check);
            StudioPropertyOwnerChecks.Run(Check);
            StudioProjectTransitionOwnerChecks.Run(Check);
            StudioMaterialOwnerChecks.Run(Check);
            StudioResourceOwnerChecks.Run(Check);
            StudioModelingCoreChecks.Run(Check);
            StudioModelingOverlayChecks.Run(Check);
            StudioModelingOwnerChecks.Run(Check);
            StudioAssetReferenceChecks.Run(Check);
            StudioAssetFileChecks.Run(Check);
            StudioAssetIoOwnerChecks.Run(Check);
            StudioBuildOwnerAcceptance.Run(Check);
            Console.WriteLine($"RML STUDIO OWNER {checks} checks passed; production retained owner/document/history/autosave, synthetic private files, no window/GL/Scene/application/network loop");
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", previousData);
            Directory.Delete(root, recursive: true);
        }
        void Check(bool value, string label)
        { if (!value) throw new InvalidOperationException("RML STUDIO OWNER: " + label); checks++; Console.WriteLine("RML STUDIO OWNER PASS " + label); }
    }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
}
