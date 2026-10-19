using MphRead;
using MphRead.Mods;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

int checks = 0;
void Check(bool value, string message)
{ if (!value) throw new InvalidOperationException("Actual export owner: " + message); checks++; }
string temp = Path.Combine(Path.GetTempPath(), "prime-export-owner-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
ReplayVideoExportManifest Job(int id) => new(3, "/synthetic.ppdemo", 1, 2, 1280, 720, 60, true, false, false,
    Path.Combine(temp, id.ToString(), "frame_%08d.png"), Path.Combine(temp, id.ToString(), "replay.mp4"), "FAKE-ENCODER-NO-PROCESS");
try
{
    var first = Job(1); var owner = DemoPlayback.PlaybackOwner;
    var presentation = ReplayExportPresentation.Capture();
    int sourceValidations = 0, frameValidations = 0;
    string? sourceError = null;
    var policy = new ReplayExportPolicy(() => { sourceValidations++; return sourceError; },
        () => { frameValidations++; return ReferenceEquals(owner, DemoPlayback.PlaybackOwner) ? null : "owner replaced"; }, presentation);
    Check(ReplayExportQueue.EnqueueOwned(first, policy), "existing queue accepts one immutable owned job");
    var reviewed = ReplayExportQueue.OwnerSnapshot();
    Check(reviewed.Pending.Length == 1 && ReferenceEquals(reviewed.Pending[0].Manifest, first), "queue receipt keeps exact original manifest identity");
    sourceError = "camera descriptor changed at same file stamp";
    Check(!ReplayExportQueue.Pump() && !ReplayVideoExporter.Active && sourceValidations == 1
        && ReplayExportQueue.OwnerSnapshot().Retry?.Manifest == first, "dequeue validates source witness and preserves original failed retry target without starting renderer");
    Check(!Directory.Exists(Path.GetDirectoryName(first.FramePattern)), "rejected source ownership creates no frame output directory");
    var staleRetry = ReplayExportQueue.OwnerSnapshot();
    ReplayExportQueue.Enqueue(Job(2));
    Check(!ReplayExportQueue.RetryExact(staleRetry.Retry!, staleRetry.Revision), "stale queue revision cannot retry a changed job set");
    var clear = ReplayExportQueue.OwnerSnapshot();
    Check(ReplayExportQueue.ClearExact(clear.Pending, clear.Revision) && ReplayExportQueue.PendingCount == 0, "exact reviewed pending set can be cleared");
    var empty = ReplayExportQueue.OwnerSnapshot(); ReplayExportQueue.Enqueue(Job(3));
    Check(!ReplayExportQueue.ClearExact(empty.Pending, empty.Revision) && ReplayExportQueue.PendingCount == 1, "stale clear cannot delete a newly admitted pending export");
    ReplayExportQueue.ClearPending(); sourceError = null;
    var retry = ReplayExportQueue.OwnerSnapshot();
    Check(ReplayExportQueue.RetryExact(retry.Retry!, retry.Revision) && ReplayExportQueue.Pump(), "original failed manifest can retry through same queue and existing exporter");
    var active = ReplayExportQueue.OwnerSnapshot();
    Check(ReferenceEquals(active.Active?.Manifest, first) && ReplayVideoExporter.State == ReplayExportState.Seeking, "single active renderer owns exact accepted manifest");
    ReplayController.State = ReplayState.Paused;
    DemoPlayback.PresentationScene!.ReplayPresentationFrame = 1;
    var shell = new Scene();
    DemoPlayback.Shell = shell;
    int validatedBeforeFrames = sourceValidations;
    ReplayVideoExporter.AfterSceneDraw(new Scene { ReplayPresentationFrame = 1 });
    Check(ScreenCapture.Captures == 0 && frameValidations == 0 && ReplayVideoExporter.FramesWritten == 0,
        "unrelated caller scene cannot submit another replay's picture or advance its active export");
    ReplayVideoExporter.AfterSceneDraw(shell);
    Check(ScreenCapture.Captures == 1 && ReferenceEquals(ScreenCapture.LastScene, DemoPlayback.PresentationScene),
        "production AfterSceneDraw resolves real presentation replica when caller supplies shell");
    Check(frameValidations == 1 && sourceValidations == validatedBeforeFrames, "frame checks are cheap owner checks and never repeat explicit source-file validation");
    DemoPlayback.PresentationScene = new Scene { ReplayPresentationFrame = 2 };
    ReplayController.CurrentFrame = 2;
    ReplayVideoExporter.AfterSceneDraw(shell);
    Check(ReplayVideoExporter.State == ReplayExportState.Encoding && ReplayEncoderJob.Last != null,
        "same durable playback owner survives exporter seek-driven presentation replacement and reaches stub encoder");
    var encoder = ReplayEncoderJob.Last!;
    var encoding = ReplayExportQueue.OwnerSnapshot();
    encoder.Result.SetResult(new(false, 0, "synthetic encoding failed", ""));
    Check(!ReplayExportQueue.CancelExact(encoding.Active!, encoding.Revision) && encoder.Disposed && !encoder.Cancelled,
        "queued encoder completion is polled before Cancel so original completion wins over stale UI intent");
    Check(ReplayVideoExporter.State == ReplayExportState.Failed && ReplayExportQueue.OwnerSnapshot().Retry?.Manifest == first,
        "failed original encoding result stays attached to original retry manifest");
    string completedMarker = Path.Combine(temp, "completed.marker");
    var completedJob = Job(8) with { SuggestedOutput = completedMarker };
    Check(ReplayExportQueue.EnqueueOwned(completedJob, policy) && ReplayExportQueue.Pump(), "next exact job can use same single exporter");
    ReplayController.State = ReplayState.Paused; ReplayController.CurrentFrame = 1; DemoPlayback.PresentationScene!.ReplayPresentationFrame = 1;
    ReplayVideoExporter.AfterSceneDraw(shell);
    ReplayController.CurrentFrame = 2; DemoPlayback.PresentationScene.ReplayPresentationFrame = 2; ReplayVideoExporter.AfterSceneDraw(shell);
    var completing = ReplayExportQueue.OwnerSnapshot(); var completedEncoder = ReplayEncoderJob.Last!;
    File.WriteAllText(completedMarker, "SYNTHETIC COMPLETION MARKER - NOT A VIDEO");
    completedEncoder.Result.SetResult(new(false, 0, null, ""));
    Check(!ReplayExportQueue.CancelExact(completing.Active!, completing.Revision) && !completedEncoder.Cancelled
        && ReplayVideoExporter.State == ReplayExportState.Completed && ReplayVideoExporter.LastOutput == completedMarker,
        "already queued successful completion preserves original output instead of becoming cancelled");
    File.Delete(completedMarker);
    var second = Job(4);
    Check(ReplayExportQueue.EnqueueOwned(second, policy) && ReplayExportQueue.Pump(), "subsequent owned job starts through same exporter");
    var previous = ReplayExportQueue.OwnerSnapshot();
    DemoPlayback.ReplaceOwner(); ReplayCamera.FieldOfView = 123;
    ReplayVideoExporter.AfterSceneDraw(shell);
    Check(ReplayVideoExporter.State == ReplayExportState.Failed && ReplayCamera.FieldOfView == 123,
        "same-path replay replacement stops old export without restoring old camera over new owner");
    Check(!ReplayExportQueue.CancelExact(previous.Active!, previous.Revision), "stale active receipt cannot cancel replacement or terminal work");
    ReplayExportQueue.Enqueue(Job(5));
    int pendingBeforeUnrelated = ReplayExportQueue.PendingCount;
    ReplayVideoExporter.AfterSceneDraw(new Scene());
    Check(ReplayExportQueue.PendingCount == pendingBeforeUnrelated && !ReplayVideoExporter.Active,
        "unrelated scene callback cannot pump waiting jobs into the exporter");
    ReplayExportQueue.ClearPending();
    var direct = Job(6);
    Check(ReplayVideoExporter.Start(direct), "legacy direct start uses same exporter ownership");
    ReplayVideoExporter.Cancel();
    Check(ReferenceEquals(ReplayExportQueue.OwnerSnapshot().Retry?.Manifest, direct),
        "directly started cancelled export keeps exact retry receipt without relying on queue's last job");
    var throwsOnFrame = new ReplayExportPolicy(() => null, () => throw new InvalidOperationException("injected frame-owner failure"), ReplayExportPresentation.Capture());
    Check(ReplayExportQueue.EnqueueOwned(Job(7), throwsOnFrame) && ReplayExportQueue.Pump(), "owned frame-error fixture starts through existing queue");
    ReplayController.State = ReplayState.Paused; DemoPlayback.PresentationScene!.ReplayPresentationFrame = 1;
    ReplayVideoExporter.AfterSceneDraw(shell);
    Check(ReplayVideoExporter.State == ReplayExportState.Failed && ReplayVideoExporter.LastError == "injected frame-owner failure",
        "owner callback failure stops exact job rather than escaping renderer or capturing stale frame");
    Check(ScreenCapture.Captures == 4 && !Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories).Any(),
        "fixture creates no recording, PNG, movie, encoder process or actual file outputs");
    Console.WriteLine($"PASS {checks} actual source-linked queue/exporter identity/completion/scene tests; capture+encoder stubs only");
}
finally { ReplayVideoExporter.Cancel(); ReplayExportQueue.ClearPending(); Directory.Delete(temp, true); }
BackendChecks.Run();
RetentionChecks.Run();
