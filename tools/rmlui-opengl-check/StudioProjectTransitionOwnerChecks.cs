using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

/// <summary>Production retained-owner transitions using only private synthetic projects and CPU authoring readers.</summary>
internal static class StudioProjectTransitionOwnerChecks
{
    internal static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(CustomRooms.UserMapDirectory, "transition-owner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var owner = new MapStudioScreen(openLibrary: false);
            check(Field<MapDocument?>(owner, "_document") == null && NoModal(owner), "cold native owner starts without a document or legacy modal/sheet");
            var cold = Begin(owner, new(NativeStudioChangeKind.Create, TemplateId: "basic-ffa", Name: "COLD_NATIVE"));
            check(cold.Stage == NativeStudioChangeStage.Ready && NoModal(owner), "cold template creation reaches native Ready without a legacy project modal");
            var session = Take(owner, cold);
            var document = Document(owner);
            check(session.Capture().Alive && Field<object?>(Field<MapViewport>(owner, "_viewport"), "_renderer") == null,
                "cold committed production session owns the real document without a renderer or GL resource");
            foreach (NativeStudioCreateKind kind in Enum.GetValues<NativeStudioCreateKind>())
            {
                string before = document.Project.Definition.Serialize();
                int commands = document.History.CommandCount;
                check(session.CreateObject(session.Capture().Ticket, kind).Success && document.History.CommandCount == commands + 1
                    && document.Selection.Count == 1 && document.Selection.Contains(document.ActiveObjectId),
                    $"production native {kind} creation adds one shared object-history command and selection");
                string after = document.Project.Definition.Serialize();
                check(session.Execute(session.Capture().Ticket, NativeStudioEditorOperation.Undo, default).Success
                    && document.Project.Definition.Serialize() == before
                    && session.Execute(session.Capture().Ticket, NativeStudioEditorOperation.Redo, default).Success
                    && document.Project.Definition.Serialize() == after,
                    $"production native {kind} creation undoes and redoes the exact document");
            }
            // Finish on geometry so the production schema includes numeric, text, boolean and choice fields.
            check(session.CreateObject(session.Capture().Ticket, NativeStudioCreateKind.Box).Success, "native owner creates schema fixture geometry");
            var schema = session.ReadProperties(session.Capture().Ticket);
            int propertyCommands = document.History.CommandCount;
            check(schema.Fields.Count > 8 && session.ApplyProperties(schema.Ticket, new Dictionary<string, string>
                { ["label"] = "Native properties", ["shade"] = "0.5", ["solid"] = "false" }).Success
                && document.History.CommandCount == propertyCommands + 1,
                "production session reads the shared schema and applies multiple typed fields in one existing history command");
            string properties = document.Project.Definition.Serialize();
            check(!session.ApplyProperties(schema.Ticket, new Dictionary<string, string> { ["label"] = "STALE" }).Success
                && document.Project.Definition.Serialize() == properties, "production property ticket rejects stale document state");
            var fresh = session.ReadProperties(session.Capture().Ticket);
            check(!session.ApplyProperties(fresh.Ticket, new Dictionary<string, string> { ["label"] = "PARTIAL", ["shade"] = "NaN" }).Success
                && document.Project.Definition.Serialize() == properties && document.History.CommandCount == propertyCommands + 1,
                "production property validation rejects nonfinite patches before any partial mutation or history entry");
            check(!session.ApplyProperties(fresh.Ticket, new Dictionary<string, string> { ["label"] = new string('x', 257) }).Success
                && document.Project.Definition.Serialize() == properties, "production schema enforces text limits atomically");
            var retiredTicket = session.Capture().Ticket;
            check(session.TryClose().Success && !session.Capture().Alive, "real native close releases the presentation before project change");
            var history = document.History;
            var canceled = Begin(owner, Create("CANCEL_CANDIDATE"));
            check(canceled.Stage == NativeStudioChangeStage.NeedsDecision
                && owner.ResolveNativeChange(canceled.Ticket, NativeStudioChangeDecision.Cancel, "").Success
                && owner.CaptureNativeChange() == null && ReferenceEquals(document, Document(owner))
                && ReferenceEquals(history, Document(owner).History) && document.Project.Definition.Serialize() == properties,
                "dirty project change Cancel retains the exact document, history and unsaved bytes");

            // This later command must replace the recovery written by TryClose.
            MapStudioObjectCommands.Create(document, MapStudioObjectKind.Spawn, System.Numerics.Vector3.Zero);
            string latest = document.Project.Definition.Serialize();
            string recoveryPath = document.RecoveryPath(CustomRooms.UserMapDirectory);
            var preserved = Begin(owner, Create("PRESERVED_NEXT"));
            check(preserved.Stage == NativeStudioChangeStage.NeedsDecision
                && owner.ResolveNativeChange(preserved.Ticket, NativeStudioChangeDecision.Preserve, "").Success
                && MapDocument.ReadRecovery(recoveryPath).Definition.Serialize() == latest,
                "Preserve writes the final exact dirty document before installing the candidate");
            session = Take(owner, owner.CaptureNativeChange()!);
            check(!ReferenceEquals(document, Document(owner)) && !session.Execute(retiredTicket, NativeStudioEditorOperation.Undo, default).Success
                && NoModal(owner), "successful preservation installs a new document and rejects the retired lease without a legacy modal");
            check(session.TryClose().Success, "preserved candidate presentation closes cleanly");

            string candidatePath = WriteProject(root, "open.json", "OPEN_CANDIDATE");
            var prior = Document(owner);
            string savePath = Path.Combine(root, "saved-current.json");
            var open = Begin(owner, new(NativeStudioChangeKind.Open, candidatePath));
            check(open.Stage == NativeStudioChangeStage.NeedsDecision
                && owner.ResolveNativeChange(open.Ticket, NativeStudioChangeDecision.Save, savePath).Success
                && File.Exists(savePath) && MapDefinition.Load(savePath).MapId == prior.Project.Definition.MapId,
                "Save and Continue writes the real current private JSON before opening the staged candidate");
            session = Take(owner, owner.CaptureNativeChange()!);
            check(Document(owner).FilePath == NativeStudioFileVersion.Capture(candidatePath).Path
                && Document(owner).Project.Definition.Name == "OPEN_CANDIDATE" && !Document(owner).IsDirty,
                "candidate Open commits its own source path and clean saved state");
            Guid sourceMapId = Document(owner).Project.Definition.MapId;
            check(session.TryClose().Success, "opened candidate presentation closes cleanly");
            var duplicate = Begin(owner, new(NativeStudioChangeKind.Duplicate, candidatePath));
            check(duplicate.Stage == NativeStudioChangeStage.Ready, "clean owner can stage and commit Duplicate without a dirty decision");
            session = Take(owner, duplicate);
            check(Document(owner).Project.Definition.MapId != sourceMapId && Document(owner).FilePath == null && Document(owner).IsDirty,
                "Duplicate creates a new map identity and unsaved document path");
            check(session.TryClose().Success, "duplicated presentation closes cleanly");

            string originalSourcePath = WriteProject(root, "context-original.json", "RECOVERY_SOURCE");
            var recoverySource = new MapDocument(MapProjectSerializer.Load(originalSourcePath), originalSourcePath);
            MapStudioObjectCommands.Create(recoverySource, MapStudioObjectKind.Box, new(2, 3, 4));
            recoverySource.Autosave(root);
            string recover = recoverySource.RecoveryPath(root);
            var recovered = Begin(owner, new(NativeStudioChangeKind.Recovery, recover));
            check(recovered.Stage == NativeStudioChangeStage.NeedsDecision
                && owner.ResolveNativeChange(recovered.Ticket, NativeStudioChangeDecision.Preserve, "").Success,
                "native Recovery stages the real autosave and its context stamp before committing");
            session = Take(owner, owner.CaptureNativeChange()!);
            var recoveredDocument = Document(owner);
            Console.WriteLine("STUDIO RECOVERY DIAGNOSTIC " + System.Text.Json.JsonSerializer.Serialize(new {
                sourceFilePath = recoverySource.FilePath,
                sourceBaseDirectory = recoverySource.Project.Definition.BaseDirectory,
                actualFilePath = recoveredDocument.FilePath,
                actualSourcePath = recoveredDocument.Project.Definition.SourcePath,
                actualBaseDirectory = recoveredDocument.Project.Definition.BaseDirectory,
                isDirty = recoveredDocument.IsDirty,
                authoredBytesEqual = recoveredDocument.Project.Definition.Serialize() == recoverySource.Project.Definition.Serialize()
            }));
            check(recoveredDocument.FilePath == null && recoveredDocument.Project.Definition.SourcePath == recoverySource.FilePath
                && recoveredDocument.Project.Definition.BaseDirectory == Path.GetDirectoryName(recoverySource.FilePath)
                && recoveredDocument.Project.Definition.Serialize() == recoverySource.Project.Definition.Serialize(),
                "Recovery preserves exact authored bytes and original source/base context while remaining unsaved");
            check(session.TryClose().Success, "recovered presentation closes cleanly");

            string unchanged = recoveredDocument.Project.Definition.Serialize();
            var retainedHistory = recoveredDocument.History;
            var saveFailure = Begin(owner, Create("SAVE_FAILURE"));
            string reservedRecovery = recoveredDocument.RecoveryPath(CustomRooms.UserMapDirectory);
            foreach (string reserved in new[] { reservedRecovery, reservedRecovery + ".context.json" })
                check(!owner.ResolveNativeChange(saveFailure.Ticket, NativeStudioChangeDecision.Save, reserved).Success
                    && ReferenceEquals(recoveredDocument, Document(owner)) && recoveredDocument.IsDirty
                    && Document(owner).Project.Definition.Serialize() == unchanged,
                    "manual Save cannot mark clean then discard its own reserved recovery target: " + Path.GetFileName(reserved));
            string destinationDirectory = Path.Combine(root, "directory.json"); Directory.CreateDirectory(destinationDirectory);
            check(!owner.ResolveNativeChange(saveFailure.Ticket, NativeStudioChangeDecision.Save, destinationDirectory).Success
                && ReferenceEquals(recoveredDocument, Document(owner)) && ReferenceEquals(retainedHistory, Document(owner).History)
                && Document(owner).Project.Definition.Serialize() == unchanged,
                "directory save destination failure retains the old document and history");
            string blocker = Path.Combine(root, "blocked-parent"); File.WriteAllText(blocker, "synthetic blocker");
            check(!owner.ResolveNativeChange(saveFailure.Ticket, NativeStudioChangeDecision.Save, Path.Combine(blocker, "failed.json")).Success
                && ReferenceEquals(recoveredDocument, Document(owner)) && Document(owner).Project.Definition.Serialize() == unchanged,
                "actual retained SaveTo I/O failure leaves the current document installed");
            owner.CancelNativeChange(saveFailure.Ticket);

            string overwrite = WriteProject(root, "overwrite.json", "OVERWRITE_REVIEW");
            var overwriteChange = Begin(owner, Create("OVERWRITE_NEXT"));
            check(owner.ResolveNativeChange(overwriteChange.Ticket, NativeStudioChangeDecision.Save, overwrite).Success
                && owner.CaptureNativeChange()!.Stage == NativeStudioChangeStage.NeedsOverwrite,
                "saving to a different existing JSON requires explicit reviewed overwrite");
            byte[] reviewed = File.ReadAllBytes(overwrite); DateTime reviewedTime = File.GetLastWriteTimeUtc(overwrite);
            byte[] changed = (byte[])reviewed.Clone(); int nameAt = Encoding.UTF8.GetString(changed).IndexOf("OVERWRITE_REVIEW", StringComparison.Ordinal);
            changed[nameAt] = (byte)'X'; File.WriteAllBytes(overwrite, changed); File.SetLastWriteTimeUtc(overwrite, reviewedTime);
            check(!owner.ResolveNativeChange(overwriteChange.Ticket, NativeStudioChangeDecision.Overwrite, "").Success
                && File.ReadAllBytes(overwrite).SequenceEqual(changed) && ReferenceEquals(recoveredDocument, Document(owner))
                && owner.CaptureNativeChange()!.Stage == NativeStudioChangeStage.NeedsDecision,
                "same-length same-mtime save-target replacement invalidates overwrite confirmation without writing");
            owner.CancelNativeChange(overwriteChange.Ticket);

            string changingSource = WriteProject(root, "changing.json", "SOURCE_BEFORE");
            var changedSource = Begin(owner, new(NativeStudioChangeKind.Open, changingSource));
            File.WriteAllText(changingSource, MapTemplates.Create("SOURCE_AFTER", "basic-ffa").Definition.Serialize());
            check(changedSource.Stage == NativeStudioChangeStage.NeedsDecision
                && !owner.ResolveNativeChange(changedSource.Ticket, NativeStudioChangeDecision.Preserve, "").Success
                && ReferenceEquals(recoveredDocument, Document(owner)) && ReferenceEquals(retainedHistory, Document(owner).History),
                "staged candidate source mutation is rejected before old document retirement");
            owner.CancelNativeChange(changedSource.Ticket);

            var malformed = MapTemplates.Create("MALFORMED_SOURCE", "basic-ffa").Definition;
            malformed.Spawns.Add(new MapSpawn { Id = Guid.NewGuid(), Position = Array.Empty<float>() });
            string malformedPath = Path.Combine(root, "malformed.json"); File.WriteAllText(malformedPath, malformed.Serialize());
            var rejected = Begin(owner, new(NativeStudioChangeKind.Open, malformedPath));
            check(rejected.Stage == NativeStudioChangeStage.Failed && ReferenceEquals(recoveredDocument, Document(owner))
                && ReferenceEquals(retainedHistory, Document(owner).History) && Document(owner).Project.Definition.Serialize() == unchanged,
                "malformed spawn position is rejected by candidate preflight before document/history retirement");
            owner.CancelNativeChange(rejected.Ticket);

            foreach (bool deleteContext in new[] { false, true })
            {
                recoverySource.Autosave(root);
                var contextChange = Begin(owner, new(NativeStudioChangeKind.Recovery, recover));
                if (deleteContext) File.Delete(recover + ".context.json");
                else File.AppendAllText(recover + ".context.json", " ");
                check(contextChange.Stage == NativeStudioChangeStage.NeedsDecision
                    && !owner.ResolveNativeChange(contextChange.Ticket, NativeStudioChangeDecision.Preserve, "").Success
                    && ReferenceEquals(recoveredDocument, Document(owner)) && ReferenceEquals(retainedHistory, Document(owner).History),
                    $"recovery context sidecar {(deleteContext ? "deletion" : "mutation")} after staging rejects commit and retains the old document");
                owner.CancelNativeChange(contextChange.Ticket);
            }

            check(owner.BeginNativeChange(Create("LATE_CANCEL")).Success, "late-reader cancellation fixture begins a real asynchronous candidate read");
            object pending = Field<object>(owner, "_nativeChange");
            var worker = (Task)pending.GetType().GetField("Worker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pending)!;
            Guid cancelTicket = (Guid)pending.GetType().GetField("Ticket", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pending)!;
            owner.CancelNativeChange(cancelTicket);
            check(worker.Wait(TimeSpan.FromSeconds(5)) && owner.CaptureNativeChange() == null
                && !owner.TryTakeNativeCommittedEditor(cancelTicket, out _) && ReferenceEquals(recoveredDocument, Document(owner))
                && ReferenceEquals(retainedHistory, Document(owner).History) && Document(owner).Project.Definition.Serialize() == unchanged,
                "canceled asynchronous reader completion cannot install a late candidate or retire the retained document");
            check(NoModal(owner) && Field<object?>(Field<MapViewport>(owner, "_viewport"), "_renderer") == null,
                "all real owner transitions retain native presentation without legacy modals, windows or renderer resources");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static NativeStudioChangeSpec Create(string name) => new(NativeStudioChangeKind.Create, TemplateId: "basic-ffa", Name: name);
    private static NativeStudioChangeSnapshot Begin(MapStudioScreen owner, NativeStudioChangeSpec spec)
    {
        var result = owner.BeginNativeChange(spec);
        if (!result.Success) throw new InvalidOperationException("Native transition did not begin: " + result.Message);
        var timer = Stopwatch.StartNew();
        while (true)
        {
            var snapshot = owner.CaptureNativeChange() ?? throw new InvalidOperationException("Native transition disappeared.");
            if (snapshot.Stage != NativeStudioChangeStage.Reading) return snapshot;
            if (timer.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Synthetic native transition reader did not complete.");
            Thread.Sleep(1);
        }
    }
    private static MapStudioScreen.NativeStudioEditorSession Take(MapStudioScreen owner, NativeStudioChangeSnapshot change)
    {
        if (!owner.TryTakeNativeCommittedEditor(change.Ticket, out var session) || session is not MapStudioScreen.NativeStudioEditorSession production)
            throw new InvalidOperationException("Native transition did not yield the real committed production session: " + change.Stage + " " + change.Error);
        return production;
    }
    private static string WriteProject(string root, string filename, string name)
    {
        string path = Path.Combine(root, filename);
        File.WriteAllText(path, MapTemplates.Create(name, "basic-ffa").Definition.Serialize()); return path;
    }
    private static bool NoModal(MapStudioScreen owner) => Field<object?>(owner, "_sheet") == null && !Field<Control>(owner, "_modal").IsVisible;
    private static MapDocument Document(MapStudioScreen owner) => Field<MapDocument>(owner, "_document");
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
}
