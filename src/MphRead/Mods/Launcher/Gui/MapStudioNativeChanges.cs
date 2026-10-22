using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private NativeProjectChange? _nativeChange;
    private sealed class NativeProjectChange
    {
        internal readonly Guid Ticket = Guid.NewGuid();
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly ConcurrentQueue<(MapProject? Project, NativeStudioFileVersion? Source, NativeStudioFileVersion? Context, string Error)> Results = new();
        internal required NativeStudioChangeSpec Spec;
        internal MapDocument? OldDocument;
        internal DocumentStateId? OldState;
        internal long Generation;
        internal NativeStudioChangeStage Stage = NativeStudioChangeStage.Reading;
        internal MapProject? Candidate;
        internal NativeStudioFileVersion? Source, Context, SaveTarget, OriginalSaveTarget;
        internal NativeStudioEditorSession? Committed;
        internal string Error = "", SavePath = "";
        internal Task Worker = Task.CompletedTask;
    }
    internal NativeStudioResult BeginNativeChange(NativeStudioChangeSpec spec)
    {
        if (_disposedOwner || _poppedOut || _work != null || _nativeChange != null || _nativeEditor != null)
            return new(false, "FINISH THE CURRENT EDITOR OPERATION BEFORE CHANGING PROJECTS");
        if (!Enum.IsDefined(spec.Kind)) return new(false, "UNKNOWN PROJECT CHANGE");
        EnsureEditorOwnerRunning();
        var change = new NativeProjectChange { Spec = spec, OldDocument = _document, OldState = _document?.CurrentStateId,
            Generation = _editorGeneration, SavePath = _document?.FilePath ?? _path.Text ?? "" };
        try
        {
            if (!string.IsNullOrWhiteSpace(change.SavePath)) change.OriginalSaveTarget = NativeStudioFileVersion.Capture(change.SavePath, true);
        }
        catch (Exception error) when (RecoverableNativeChange(error)) { change.Error = "CURRENT SAVE TARGET NEEDS A DIRECT WRITABLE JSON PATH"; }
        _nativeChange = change; _work = change.Cancellation;
        change.Worker = Task.Run(() =>
        {
            try
            {
                var token = change.Cancellation.Token; token.ThrowIfCancellationRequested();
                MapProject project; NativeStudioFileVersion? source = null, context = null;
                if (spec.Kind == NativeStudioChangeKind.Create) project = MapTemplates.Create(spec.Name, spec.TemplateId);
                else
                {
                    source = NativeStudioFileVersion.Capture(spec.Path);
                    if(spec.Kind==NativeStudioChangeKind.Recovery)context=NativeStudioFileVersion.Capture(source.Path+".context.json",true);
                    if (!source.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !MapBundle.Is(source.Path))
                        throw new IOException("Choose a JSON project or PPMAP package.");
                    project = spec.Kind == NativeStudioChangeKind.Recovery ? MapDocument.ReadRecovery(source.Path) : MapProjectSerializer.Load(source.Path);
                    if (!source.Matches()||context is {} readContext&&!readContext.Matches()) throw new IOException("Project changed while it was read.");
                    if (spec.Kind == NativeStudioChangeKind.Duplicate)
                    { project = MapProjectMigrator.Upgrade(project); project.Definition.MapId = Guid.NewGuid(); project.Definition.Name = NextCopyName(project.Definition.Name); }
                }
                RequireNativeCandidate(project);
                token.ThrowIfCancellationRequested(); change.Results.Enqueue((project, source, context, ""));
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (RecoverableNativeChange(error))
            { if (!change.Cancellation.IsCancellationRequested) change.Results.Enqueue((null, null, null, "PROJECT COULD NOT BE READ OR SAFELY EDITED // CURRENT DOCUMENT RETAINED")); }
        });
        return new(true, "PREPARING PROJECT CHANGE // CURRENT DOCUMENT RETAINED");
    }
    private bool NativeChangeCurrent(NativeProjectChange change) => !_disposedOwner && _nativeChange == change
        && !change.Cancellation.IsCancellationRequested && _document == change.OldDocument
        && _document?.CurrentStateId == change.OldState && _editorGeneration == change.Generation;
    private void PumpNativeChange()
    {
        var change = _nativeChange; if (change == null) return;
        while (change.Results.TryDequeue(out var result))
        {
            if (!NativeChangeCurrent(change) || change.Stage != NativeStudioChangeStage.Reading) continue;
            if (result.Project == null) { change.Stage = NativeStudioChangeStage.Failed; change.Error = result.Error; continue; }
            change.Candidate = result.Project; change.Source = result.Source;change.Context=result.Context;
            if (_document?.IsDirty == true) change.Stage = NativeStudioChangeStage.NeedsDecision;
            else CommitNativeChange(change, preserve: false);
        }
        if (change.Stage is not (NativeStudioChangeStage.Ready or NativeStudioChangeStage.Failed) && !NativeChangeCurrent(change))
        { change.Stage = NativeStudioChangeStage.Failed; change.Error = "EDITOR CHANGED // CANCEL AND REQUEST THE PROJECT AGAIN"; }
    }
    internal NativeStudioChangeSnapshot? CaptureNativeChange()
    {
        PumpNativeChange(); var change = _nativeChange;
        return change == null ? null : new(change.Ticket, change.Stage, change.OldDocument?.Project.Definition.Name ?? "NO CURRENT PROJECT",
            change.Candidate?.Definition.Name ?? change.Spec.Name, change.SavePath, change.Error);
    }
    internal NativeStudioResult ResolveNativeChange(Guid ticket, NativeStudioChangeDecision decision, string savePath)
    {
        PumpNativeChange(); var change = _nativeChange;
        if (change == null || change.Ticket != ticket) return new(false, "PROJECT CHANGE HAS RETIRED");
        if (decision == NativeStudioChangeDecision.Cancel) { CancelNativeChange(ticket); return new(true, "PROJECT CHANGE CANCELLED // CURRENT DOCUMENT RETAINED"); }
        if (!NativeChangeCurrent(change) || change.Candidate == null) return new(false, "PROJECT CHANGE IS NOT READY");
        if (decision == NativeStudioChangeDecision.KeepFile && change.Stage == NativeStudioChangeStage.NeedsOverwrite)
        { change.SaveTarget = null; change.Stage = NativeStudioChangeStage.NeedsDecision; change.Error = "EXISTING FILE KEPT // CHOOSE ANOTHER SAVE PATH OR CANCEL"; return new(true, change.Error); }
        if (decision == NativeStudioChangeDecision.Preserve && change.Stage == NativeStudioChangeStage.NeedsDecision)
            return CommitNativeChange(change, preserve: true);
        if (decision is not (NativeStudioChangeDecision.Save or NativeStudioChangeDecision.Overwrite)) return new(false, "CHOOSE A CURRENT PROJECT CHANGE ACTION");
        try
        {
            if (decision == NativeStudioChangeDecision.Save)
            {
                if (change.Stage != NativeStudioChangeStage.NeedsDecision || string.IsNullOrWhiteSpace(savePath) || savePath.Length > 32767)
                    return new(false, "ENTER A JSON SAVE PATH");
                var target = NativeStudioFileVersion.Capture(RequireManualSavePath(savePath), true);
                if (!target.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose JSON.");
                change.SavePath = target.Path; change.SaveTarget = target;
                if (target.Exists && (change.OriginalSaveTarget == null || target != change.OriginalSaveTarget))
                { change.Stage = NativeStudioChangeStage.NeedsOverwrite; change.Error = "CONFIRM REPLACING THE REVIEWED EXISTING JSON FILE"; return new(true, change.Error); }
            }
            else if (change.Stage != NativeStudioChangeStage.NeedsOverwrite) return new(false, "OVERWRITE CONFIRMATION HAS EXPIRED");
            if (change.SaveTarget == null || !change.SaveTarget.Matches())
            { change.Stage = NativeStudioChangeStage.NeedsDecision; change.Error = "SAVE TARGET CHANGED // REVIEW IT AGAIN BEFORE SAVING"; return new(false, change.Error); }
            if (change.Source != null && !change.Source.Matches()||change.Context is {} saveContext&&!saveContext.Matches()) throw new IOException("Candidate changed.");
            if (!SaveTo(change.SaveTarget.Path))
            { change.Stage = NativeStudioChangeStage.NeedsDecision; change.Error = "SAVE FAILED // CURRENT DOCUMENT AND PROJECT CHANGE RETAINED"; return new(false, change.Error); }
            if (change.Source?.Path == change.SaveTarget.Path)
            {
                // Opening/duplicating the same file after Save must use those
                // newly saved bytes, never the pre-save staged candidate.
                change.Source = NativeStudioFileVersion.Capture(change.SaveTarget.Path);
                change.Candidate = MapProjectSerializer.Load(change.Source.Path);
                if (change.Spec.Kind == NativeStudioChangeKind.Duplicate)
                { change.Candidate.Definition.MapId = Guid.NewGuid(); change.Candidate.Definition.Name = NextCopyName(change.Candidate.Definition.Name); }
                RequireNativeCandidate(change.Candidate);
            }
            return CommitNativeChange(change, preserve: false);
        }
        catch (Exception error) when (RecoverableNativeChange(error))
        { change.Stage = NativeStudioChangeStage.NeedsDecision; change.Error = "PROJECT OR SAVE TARGET CHANGED/UNAVAILABLE // CURRENT DOCUMENT RETAINED"; return new(false, change.Error); }
    }
    private NativeStudioResult CommitNativeChange(NativeProjectChange change, bool preserve)
    {
        if (!NativeChangeCurrent(change) || change.Candidate == null || change.Source is { } source && !source.Matches()
            ||change.Context is {} context&&!context.Matches())
        { change.Stage = NativeStudioChangeStage.Failed; change.Error = "PROJECT SOURCE CHANGED // CANCEL AND OPEN IT AGAIN"; return new(false, change.Error); }
        change.Stage = NativeStudioChangeStage.Preserving;
        if (preserve && !SaveRecovery())
        { change.Stage = NativeStudioChangeStage.NeedsDecision; change.Error = "RECOVERY FAILED // CURRENT DOCUMENT REMAINS OPEN"; return new(false, change.Error); }
        if (!NativeChangeCurrent(change) || change.Source is { } checkedSource && !checkedSource.Matches()
            ||change.Context is {} checkedContext&&!checkedContext.Matches())
        { change.Stage = NativeStudioChangeStage.Failed; change.Error = "PROJECT CHANGED AFTER PRESERVATION // CURRENT DOCUMENT RETAINED"; return new(false, change.Error); }
        // The pending transition pins this owner while the old presentation retires.
        if (_work == change.Cancellation) _work = null;
        _nativeEditor?.Dispose();
        string? path = change.Spec.Kind == NativeStudioChangeKind.Open ? change.Source?.Path : null;
        if(!TryInstallNativeCandidate(change.Candidate,path))
        {change.Generation=_editorGeneration;change.Stage=NativeStudioChangeStage.Failed;change.Error="PROJECT COULD NOT BE INSTALLED // PREVIOUS DOCUMENT AND HISTORY RETAINED";return new(false,change.Error);}
        if (!TryAcquireNativeEditor(out var session, out string error) || session == null)
        { change.Stage = NativeStudioChangeStage.Failed; change.Error = error; return new(false, error); }
        change.Committed = session; change.Stage = NativeStudioChangeStage.Ready; change.Error = "";
        if(change.Candidate.Definition.Import!=null||change.Candidate.Definition.NativeRoom!=null)_=PreviewImport();
        return new(true, "PROJECT READY IN THE NATIVE EDITOR");
    }
    internal bool TryTakeNativeCommittedEditor(Guid ticket, out INativeStudioEditorSession? session)
    {
        PumpNativeChange(); session = null; var change = _nativeChange;
        if (change == null || change.Ticket != ticket || change.Stage != NativeStudioChangeStage.Ready || change.Committed == null) return false;
        session = change.Committed; change.Committed = null; _nativeChange = null;
        change.Cancellation.Dispose(); return true;
    }
    internal void CancelNativeChange(Guid ticket)
    {
        var change = _nativeChange; if (change == null || change.Ticket != ticket) return;
        _nativeChange = null; change.Cancellation.Cancel();
        if (_work == change.Cancellation) _work = null;
        change.Committed?.Dispose(); change.Committed = null;
        _ = change.Worker.ContinueWith(_ => change.Cancellation.Dispose(), TaskScheduler.Default);
        SuspendEditorOwner();
    }
    private static bool RecoverableNativeChange(Exception error) => error is not OutOfMemoryException and not StackOverflowException;
    private string RequireManualSavePath(string path)
    {
        if (_document == null) throw new IOException("No current document.");
        // Recovery is owned by the autosave service. A manual save there can
        // otherwise be deleted by the following DiscardRecovery operation.
        string target = NativeStudioFileVersion.Capture(path, allowMissing: true).Path;
        string recovery = NativeStudioFileVersion.Capture(_document.RecoveryPath(CustomRooms.UserMapDirectory), allowMissing: true).Path;
        if (target.StartsWith(Path.GetDirectoryName(recovery)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a save path outside the reserved recovery directory.");
        return target;
    }
    private static void RequireNativeCandidate(MapProject candidate)
    {
        var definition=candidate.Definition;
        // Exercise the existing CPU authoring readers before retiring any live
        // document. Runtime-budget diagnostics may remain editable; malformed
        // object/array shapes must not reach destructive owner installation.
        _=MapValidator.Validate(definition,false);
        _=candidate.ToDefinition();
        if(!MapValidator.Vector(definition.Light1Vector)||!MapValidator.Vector(definition.Light2Vector)
            ||definition.FogColor?.Length!=3||definition.Light1Color?.Length!=3||definition.Light2Color?.Length!=3
            ||definition.ModelSources==null||definition.Assets==null||definition.HardpointOrder==null
            ||definition.Materials.Any(m=>m==null))
            throw new IOException("Project authoring fields are malformed.");
        var ids=new System.Collections.Generic.HashSet<Guid>();
        foreach(var item in MapObjects.All(definition))
        {
            if(item.Id==Guid.Empty)item.SetId(Guid.NewGuid());
            if(!ids.Add(item.Id))throw new IOException("Project contains duplicate object identities.");
            if(!MapValidator.Vector(item.Position))throw new IOException("Project object position is malformed.");
            if(item.Value is MapGeometry geometry&&(!MapValidator.Vector(geometry.Transform.Scale)||geometry.Transform.Rotation?.Length!=4
                ||geometry.Uv==null||geometry.Uv.Scale?.Length!=2||geometry.Uv.Offset?.Length!=2))throw new IOException("Project geometry fields are malformed.");
            _=MapObjectPropertySchema.Read(definition,item.Id);
        }
        foreach(var source in definition.ModelSources)
            if(source==null||source.Dependencies==null||source.Objects==null||source.MaterialMappings==null||source.Dependencies.Any(d=>d==null))
                throw new IOException("Project source metadata is malformed.");
        foreach(var asset in definition.Assets)if(asset==null)throw new IOException("Project asset metadata is malformed.");
        if(definition.Preview is {} preview&&(!MapValidator.Vector(preview.Position)||!MapValidator.Vector(preview.Target)))throw new IOException("Project preview is malformed.");
        var cache=new MapViewportCache();cache.Invalidate(definition,new(MapChangeDomain.All));
        foreach(var entity in cache.Entities)_=MapViewportScene.Vector(entity.Position);
    }
    private bool TryInstallNativeCandidate(MapProject candidate,string? path)
    {
        var previous=_document;var viewport=_viewport;var views=_views.ToArray();var studioState=_studioState;
        var hostChildren=_viewportHost.Children.ToArray();var changedSources=_changedSources.ToArray();
        var importWarnings=_importWarnings;var lastBuild=_lastBuild;string? previousPath=_path.Text;
        try
        {
            _autosave.Dispose();_autosave.Completion.GetAwaiter().GetResult();
            Load(candidate,path,showRecoveryPrompt:false,prepareImportedPreview:false);return true;
        }
        catch(Exception error) when(RecoverableNativeChange(error))
        {
            if(_document!=null)_document.Changed-=Changed;
            ReleaseViews();_sourceWatch?.Dispose();_sourceWatch=null;_sourceWatchSignature="";
            _document=previous;_viewport=viewport;_studioState=studioState;_importWarnings=importWarnings;_lastBuild=lastBuild;_path.Text=previousPath;
            _changedSources.Clear();foreach(string changed in changedSources)_changedSources.Add(changed);
            _views.Clear();_viewportHost.Children.Clear();
            foreach(var view in views){view.ReattachDocument();_views.Add(view);}
            foreach(var child in hostChildren)_viewportHost.Children.Add(child);
            if(previous!=null){previous.Changed-=Changed;previous.Changed+=Changed;}
            _autosave=new();_validatedState=null;_validationSignature=null;_hierarchySignature="";
            try{Changed();}catch(Exception restoreError) when(RecoverableNativeChange(restoreError)){_status.Text="PREVIOUS DOCUMENT AND HISTORY RETAINED";}
            return false;
        }
    }
}
