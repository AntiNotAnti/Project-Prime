using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    internal sealed partial class NativeStudioEditorSession : INativeStudioEditorBuild
    {
        private sealed class BuildWork
        {
            public required NativeStudioBuildSnapshot View;
            public required MapBuildSnapshot Input;
            public required MapDefinition Definition;
            public required string Root;
            public required CancellationTokenSource Cancellation;
            public required IMapBuildScheduler Scheduler;
            public Task<BuildAnswer>? Task;
            public string? TemporaryDirectory;
            public string? ReviewedPackage;
            public NativeStudioFileVersion? Destination;
            public int Retired;
            public Action<string, CancellationToken>? Checkpoint;
        }
        private sealed record BuildAnswer(MapAnalysisResult? Analysis = null, MapBuildResult? Build = null,
            string Package = "", NativeStudioFileVersion? Target = null, string Status = "",
            IReadOnlyList<MapDiagnostic>? Audit = null, Exception? Error = null, bool Published = false);
        private BuildWork? _buildWork;
        private long _buildRevision;
        internal IMapBuildScheduler? BuildSchedulerOverride = null;
        internal Action<string, CancellationToken>? BuildCheckpoint = null;

        private bool BuildCurrent(BuildWork work) => Alive && !_owner._detached
            && work.View.Ticket.Lease == _lease && work.View.Ticket.State == _document.CurrentStateId.Value
            && work.Root == GameFiles.Root && Volatile.Read(ref work.Retired) == 0;
        private void BuildChanged(BuildWork work, NativeStudioBuildStage stage, string status)
        {
            work.View = work.View with { Revision = ++_buildRevision, Stage = stage, Status = status,
                ResultCurrent = BuildCurrent(work) };
            _revision++; _snapshot = null;
        }
        public NativeStudioBuildSnapshot CaptureBuild()
        {
            CheckThread(); PumpBuild();
            if (_buildWork is not { } work)
                return new(_buildRevision, Guid.Empty, Ticket, NativeStudioBuildKind.Validate,
                    NativeStudioBuildStage.Idle, "VALIDATE, BUILD OR PLAYTEST THE CURRENT PROJECT", "", "",
                    Array.Empty<MapDiagnostic>(), Array.Empty<MapBudget>(), Alive);
            bool current = BuildCurrent(work);
            if (current != work.View.ResultCurrent)
            {
                work.View = work.View with { Revision = ++_buildRevision, ResultCurrent = current };
                if (!current && work.Task != null) CancelBuild(work.View.Operation);
            }
            return work.View;
        }
        public NativeStudioResult BeginBuild(NativeStudioEditTicket ticket, NativeStudioBuildKind kind, string destination = "")
        {
            CheckThread(); PumpBuild();
            if (!Current(ticket) || !Enum.IsDefined(kind) || _buildWork?.Task != null) return Stale();
            try
            {
                bool play = kind is NativeStudioBuildKind.PlayCamera or NativeStudioBuildKind.PlaySelection
                    or NativeStudioBuildKind.PlayTeamA or NativeStudioBuildKind.PlayTeamB;
                var definition = _document.CaptureBuildSnapshot().CreateDefinition();
                if (kind == NativeStudioBuildKind.Package && (String.IsNullOrWhiteSpace(destination)
                    || destination.Length > 32767 || !destination.EndsWith(".ppmap", StringComparison.OrdinalIgnoreCase)))
                    return new(false, "ENTER A LOCAL .PPMAP DESTINATION BEFORE PREPARING A PACKAGE");
                if (play)
                {
                    MapSpawn? spawn = kind switch
                    {
                        NativeStudioBuildKind.PlaySelection => definition.Spawns.FirstOrDefault(s => _document.Selection.Contains(s.Id)),
                        NativeStudioBuildKind.PlayTeamA => definition.Spawns.FirstOrDefault(s => s.Team == 0),
                        NativeStudioBuildKind.PlayTeamB => definition.Spawns.FirstOrDefault(s => s.Team == 1), _ => null
                    };
                    if (kind != NativeStudioBuildKind.PlayCamera && spawn == null)
                        return new(false, "SELECT A PLAYER SPAWN OR ADD THE REQUESTED TEAM SPAWN BEFORE PLAYTESTING");
                    definition.Name = _owner._previewName; definition.SourcePath = null; definition.Capabilities = null;
                    if (definition.Import != null) definition.Import.KeepSpawns = false;
                    var position = _viewport.CameraPosition;
                    definition.Spawns.Clear(); definition.Spawns.Add(spawn == null
                        ? new() { Position = new[] { position.X, position.Y, position.Z } }
                        : new() { Position = (float[])spawn.Position.Clone(), Yaw = spawn.Yaw, Team = spawn.Team });
                }
                RetireBuild(false);
                var cancellation = new CancellationTokenSource();
                var work = new BuildWork
                {
                    View = new(++_buildRevision, Guid.NewGuid(), Ticket, kind, NativeStudioBuildStage.Running,
                        "PREPARING " + kind.ToString().ToUpperInvariant(), destination, "",
                        Array.Empty<MapDiagnostic>(), Array.Empty<MapBudget>(), true),
                    Input = MapBuildSnapshot.Capture(new MapProject(definition)), Definition = definition,
                    Root = GameFiles.Root, Cancellation = cancellation,
                    Scheduler = BuildSchedulerOverride ?? MapBuildScheduler.Shared, Checkpoint = BuildCheckpoint
                };
                _buildWork = work; StartBuildTask(work, () => RunBuild(work));
                return new(true, work.View.Status);
            }
            catch (Exception error) when (AssetRecoverable(error))
            { return new(false, "BUILD COULD NOT START // " + error.Message); }
        }
        private void StartBuildTask(BuildWork work, Func<Task<BuildAnswer>> action)
        {
            if (_owner._work != null) throw new InvalidOperationException("Finish the current editor job first.");
            _owner._work = work.Cancellation; _owner._jobDocument = _document;
            _owner._jobState = _document.CurrentStateId; _owner._jobGeneration = _owner._editorGeneration;
            _owner.SetBusy(true); _revision++; _snapshot = null;
            work.Task = Task.Run(async () =>
            {
                try { return await action().ConfigureAwait(false); }
                catch (Exception error) when (AssetRecoverable(error)) { return new BuildAnswer(Error: error); }
            });
        }
        private static void VerifyBuildWorker(BuildWork work)
        {
            work.Cancellation.Token.ThrowIfCancellationRequested();
            if (Volatile.Read(ref work.Retired) != 0 || work.Root != GameFiles.Root)
                throw new OperationCanceledException("The build's editor or game-file configuration changed.");
        }
        private static async Task<BuildAnswer> RunBuild(BuildWork work)
        {
            var token = work.Cancellation.Token; VerifyBuildWorker(work);
            work.Checkpoint?.Invoke("build-start", token);
            using var files = GameFiles.TryUseConfiguredFiles(work.Root);
            bool needsFiles = work.View.Kind is NativeStudioBuildKind.Runtime or NativeStudioBuildKind.PlayCamera
                or NativeStudioBuildKind.PlaySelection or NativeStudioBuildKind.PlayTeamA or NativeStudioBuildKind.PlayTeamB or NativeStudioBuildKind.Audit;
            if (needsFiles && (files == null || !GameFiles.Ready))
                throw new InvalidOperationException("SET UP GAME FILES BEFORE RUNTIME BUILD, PLAYTEST OR AUDIT");
            switch (work.View.Kind)
            {
                case NativeStudioBuildKind.Validate:
                case NativeStudioBuildKind.Navigation:
                    var analysis = await work.Scheduler.AnalyzeAsync(work.Input,
                        work.View.Kind == NativeStudioBuildKind.Navigation, token).ConfigureAwait(false);
                    VerifyBuildWorker(work); return new(Analysis: analysis);
                case NativeStudioBuildKind.Package:
                    var target = NativeStudioFileVersion.Capture(work.View.Destination, allowMissing: true);
                    if (!Directory.Exists(Path.GetDirectoryName(target.Path))) throw new IOException("Choose an existing package destination folder.");
                    work.TemporaryDirectory = Path.Combine(Platform.AppPaths.UserDataDirectory, "studio-package-review", work.View.Operation.ToString("N"));
                    Directory.CreateDirectory(work.TemporaryDirectory);
                    string package = Path.Combine(work.TemporaryDirectory, "review.ppmap");
                    await work.Scheduler.PackageAsync(work.Input, package, token).ConfigureAwait(false);
                    VerifyBuildWorker(work);
                    var identity = MapContentIdentity.FromPackage(package);
                    return new(Package: package, Target: target, Status: identity.PackageHash.ToString());
                case NativeStudioBuildKind.Audit:
                    var audit = await MapAuditRunner.Run(new MapProject(work.Definition), token).ConfigureAwait(false);
                    VerifyBuildWorker(work);
                    return new(Status: audit.Passed ? "MAP AUDIT PASSED" : "MAP AUDIT FAILED",
                        Audit: audit.Lines.Select(line => new MapDiagnostic("MAP-AUDIT", audit.Passed
                            ? MapDiagnosticSeverity.Info : MapDiagnosticSeverity.Warning, line.ToString() ?? "")).ToArray());
                default:
                    MapRuntimeUsage.RequireInstallationAllowed();
                    var built = await work.Scheduler.BuildAsync(work.Input, token).ConfigureAwait(false);
                    VerifyBuildWorker(work);
                    if (built.Succeeded)
                    {
                        work.Checkpoint?.Invoke("before-runtime-install", token); VerifyBuildWorker(work);
                        MapRuntimeUsage.RequireInstallationAllowed();
                        MapBuildScheduler.Install(built, work.Definition, CustomRooms.ArchiveDirectory(work.Definition),
                            CustomRooms.EntityDirectory(), CustomRooms.NodeDirectory());
                    }
                    return new(Build: built);
            }
        }
        private void PumpBuild()
        {
            if (_buildWork is not { Task.IsCompleted: true } work) return;
            BuildAnswer answer = work.Task.GetAwaiter().GetResult(); work.Task = null;
            if (_owner._work == work.Cancellation) { _owner._work = null; _owner.SetBusy(false); }
            if (!BuildCurrent(work) || answer.Error != null)
            {
                string status = answer.Error is not null and not OperationCanceledException ? "BUILD FAILED // " + answer.Error.Message
                    : "BUILD CANCELED OR EDITOR CHANGED // CURRENT PROJECT RETAINED";
                BuildChanged(work, NativeStudioBuildStage.Failed, status); CleanupBuildTemporary(work); return;
            }
            if (answer.Published)
            { BuildChanged(work, NativeStudioBuildStage.Complete, answer.Status); CleanupBuildTemporary(work); return; }
            if (answer.Package.Length > 0)
            {
                work.ReviewedPackage = answer.Package; work.Destination = answer.Target;
                work.View = work.View with { Destination = answer.Target!.Path, Fingerprint = answer.Status };
                BuildChanged(work, NativeStudioBuildStage.ReadyPackage, (answer.Target.Exists
                    ? "REVIEW OVERWRITE // PRIOR PACKAGE WILL BE RETAINED AT A RECOVERY PATH"
                    : "REVIEW NEW PACKAGE DESTINATION") + " // " + answer.Target.Path
                    + " // " + new FileInfo(answer.Package).Length.ToString("N0") + " BYTES // PRESS CONFIRM TO EXPORT THESE REVIEWED BYTES");
                return;
            }
            var diagnostics = answer.Analysis?.Diagnostics ?? answer.Build?.Diagnostics ?? answer.Audit ?? Array.Empty<MapDiagnostic>();
            var budgets = answer.Analysis?.Budgets ?? answer.Build?.Budgets ?? Array.Empty<MapBudget>();
            work.View = work.View with { Diagnostics = Array.AsReadOnly(diagnostics.ToArray()),
                Budgets = Array.AsReadOnly(budgets.ToArray()), Fingerprint = answer.Analysis?.Fingerprint ?? answer.Build?.Fingerprint ?? "" };
            if (answer.Analysis is { } analyzed)
            {
                _owner.Problems(analyzed.Validation());
                if ((work.Definition.Import != null || work.Definition.NativeRoom != null) && analyzed.Faces.Length > 0)
                    foreach (var view in _owner._views) view.SetImported(analyzed);
                if (work.View.Kind == NativeStudioBuildKind.Navigation && analyzed.CreateNavigation() is { } graph)
                { _viewport.Navigation = graph; _viewport.InvalidateVisual(); }
            }
            if (answer.Build is { } build)
            {
                _owner._lastBuild = build; _owner.Problems(build.Validation());
                if (build.Succeeded)
                {
                    if (work.View.Kind == NativeStudioBuildKind.Runtime) Metadata.RegisterDownloadedMap(work.Definition);
                    else _owner.PlayRequested?.Invoke(_owner, work.Definition);
                }
            }
            bool valid = diagnostics.All(row => row.Severity != MapDiagnosticSeverity.Error);
            BuildChanged(work, valid ? NativeStudioBuildStage.Complete : NativeStudioBuildStage.Failed,
                answer.Status.Length > 0 ? answer.Status : valid ? work.View.Kind.ToString().ToUpperInvariant() + " COMPLETE"
                    : "BUILD BLOCKED // REVIEW PROBLEMS; AUTHORING GEOMETRY RETAINED");
        }
        public NativeStudioResult ConfirmBuildPackage(Guid operation)
        {
            CheckThread(); PumpBuild(); var work = _buildWork;
            if (work == null || work.View.Operation != operation || !BuildCurrent(work)
                || work.View.Stage != NativeStudioBuildStage.ReadyPackage || work.ReviewedPackage == null || work.Destination == null
                || _owner._work != null) return Stale();
            BuildChanged(work, NativeStudioBuildStage.Running, "EXPORTING EXACT REVIEWED PACKAGE");
            try
            {
                StartBuildTask(work, () =>
                {
                    VerifyBuildWorker(work);
                    if (MapContentIdentity.FromPackage(work.ReviewedPackage).PackageHash.ToString() != work.View.Fingerprint)
                        throw new IOException("Reviewed package bytes changed; prepare the package again.");
                    // ReviewedAssetFiles is the existing owner of explicit new/overwrite publication,
                    // recovery retention, destination ABA checks and failure reporting.
                    var publication = MapStudioReviewedAssetFiles.PublishFile(work.Destination, NativeStudioFileVersion.Capture(work.ReviewedPackage),
                        () => VerifyBuildWorker(work), () => work.Checkpoint?.Invoke("before-package-publication", work.Cancellation.Token));
                    return Task.FromResult(new BuildAnswer(Status: publication.Notice,
                        Error: publication.Verified ? null : new IOException(publication.Notice), Published: publication.Verified));
                });
                return new(true, "EXPORTING THE REVIEWED PACKAGE");
            }
            catch (Exception error) when (AssetRecoverable(error))
            { BuildChanged(work, NativeStudioBuildStage.Failed, error.Message); return new(false, error.Message); }
        }
        public void CancelBuild(Guid operation)
        {
            CheckThread(); if (_buildWork is not { } work || work.View.Operation != operation) return;
            Volatile.Write(ref work.Retired, 1); work.Cancellation.Cancel();
            BuildChanged(work, work.Task == null ? NativeStudioBuildStage.Failed : NativeStudioBuildStage.Canceling,
                "CANCELING BUILD WORK // CURRENT PROJECT RETAINED");
            if (work.Task == null) CleanupBuildTemporary(work);
        }
        public void ClearBuild(Guid operation)
        { CheckThread(); PumpBuild(); if (_buildWork is { } work && work.View.Operation == operation && work.Task == null) RetireBuild(false); }
        public NativeStudioResult SelectBuildProblem(Guid operation, int absoluteIndex)
        {
            CheckThread(); PumpBuild(); var work = _buildWork;
            if (work == null || work.View.Operation != operation || !BuildCurrent(work) || work.Task != null
                || absoluteIndex < 0 || absoluteIndex >= work.View.Diagnostics.Count
                || work.View.Diagnostics[absoluteIndex].ObjectId is not { } id || id == Guid.Empty) return Stale();
            var result = Select(Ticket, id); if (result.Success) _viewport.FrameSelection(); return result;
        }
        private void BuildOwnerInvalidated()
        {
            if (_buildWork is { } work && work.View.Ticket.State != _document.CurrentStateId.Value)
            { Volatile.Write(ref work.Retired, 1); work.Cancellation.Cancel(); }
        }
        private bool TryLeaveBuild()
        {
            PumpBuild(); if (_buildWork is not { } work) return true;
            if (work.Task != null) { CancelBuild(work.View.Operation); return false; }
            RetireBuild(false); return true;
        }
        private void RetireBuild(bool drain)
        {
            var work = _buildWork; if (work == null) return;
            Volatile.Write(ref work.Retired, 1); work.Cancellation.Cancel();
            if (work.Task != null)
            {
                if (!drain) throw new InvalidOperationException("Cancel the current build before starting another.");
                work.Task.GetAwaiter().GetResult();
                if (_owner._work == work.Cancellation) { _owner._work = null; _owner.SetBusy(false); }
            }
            CleanupBuildTemporary(work); work.Cancellation.Dispose(); _buildWork = null;
            _buildRevision++; _revision++; _snapshot = null;
        }
        private static void CleanupBuildTemporary(BuildWork work)
        {
            if (work.TemporaryDirectory is not { } directory) return;
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            work.TemporaryDirectory = null; work.ReviewedPackage = null;
        }
    }
}
