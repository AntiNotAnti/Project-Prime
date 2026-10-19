using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

internal static class BackendChecks
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("Actual export backend: " + message); checks++; }
        string temp = Path.Combine(Path.GetTempPath(), "prime-export-witness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string source = Path.Combine(temp, "synthetic.ppdemo");
        byte[] original = { 1, 2, 3, 4, 5, 6, 7, 8 };
        File.WriteAllBytes(source, original);
        DemoLibrary.Directory = temp; DemoPlayback.IsActive = true; DemoPlayback.ReplaceOwner();
        DemoPlayback.PlaybackPath = DemoPlayback.LogicalPath = source;
        ReplacePresentationWorld(); ReplayCamera.Track.Clear();
        ReplayController.State = ReplayState.Paused; ReplayController.CurrentFrame = 10;
        ReplayController.DurationFrames = 100; ReplayController.ClipIn = 10; ReplayController.ClipOut = 20;
        var backend = RmlUiOpenGlSmoke.FixtureBackend();
        ReplayDetachedExportQueue.Reset();
        var options = new TheatreExportOptions(0, 1, 60, false, false, "libx264", TheatreExportSource.Selection, TheatreExportCamera.FirstPerson);
        try
        {
            var context = backend.Capture();
            Check(context.SourceRevision.Contains(Convert.ToHexString(SHA256.HashData(original)), StringComparison.Ordinal),
                "explicit source review includes actual recording SHA, not only file length/time");
            Check(backend.IsCurrent(context), "source review uses original durable player token and current camera settings");
            DateTime stamp = File.GetLastWriteTimeUtc(source);
            File.WriteAllBytes(source, new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 }); File.SetLastWriteTimeUtc(source, stamp);
            Check(Reject(() => backend.Enqueue(context, options)) && ReplayDetachedExportQueue.Snapshot().Active == null,
                "same-length same-stamp recording ABA cannot enqueue reviewed export or create manifest");
            Check(!Directory.Exists(Path.Combine(temp, "exports")), "stale source fails before generating any export artifact directory");
            File.WriteAllBytes(source, original); File.SetLastWriteTimeUtc(source, stamp);
            var key = new ReplayCameraKeyframe(10, Vector3.One, Quaternion.Identity, 1);
            var track = new ReplayCameraTrack(); Check(track.Put(key) && track.Save(source), "synthetic camera sidecar created using actual track owner");
            Check(ReplayCamera.Track.Load(source), "current camera owner reviews same synthetic persisted keys");
            context = backend.Capture();
            string camera = source + ".camera"; byte[] cameraBytes = File.ReadAllBytes(camera); DateTime cameraStamp = File.GetLastWriteTimeUtc(camera);
            byte[] damaged = (byte[])cameraBytes.Clone(); damaged[30] ^= 1; File.WriteAllBytes(camera, damaged); File.SetLastWriteTimeUtc(camera, cameraStamp);
            Check(Reject(() => backend.Enqueue(context, options)), "same-stamp camera sidecar ABA rejects creation rather than retargeting track");
            File.WriteAllBytes(camera, cameraBytes); File.SetLastWriteTimeUtc(camera, cameraStamp);
            context = backend.Capture();
            File.WriteAllText(source + ReplayReels.Extension, "{broken");
            Check(Reject(() => backend.Capture()), "unreadable reel refuses review rather than silently exporting empty or altered ranges");
            File.Delete(source + ReplayReels.Extension);
            string descriptor = Path.Combine(temp, "virtual.ppclip"), cacheDir = Path.Combine(temp, ".virtual-cache");
            Directory.CreateDirectory(cacheDir); string playback = Path.Combine(cacheDir, "virtual.ppdemo"); File.WriteAllBytes(playback, new byte[] { 9, 9, 9, 9 });
            var clip = new ReplayVirtualClipDocument(1, source, 10, 20, DateTime.UnixEpoch, "Synthetic virtual");
            File.WriteAllText(descriptor, JsonSerializer.Serialize(clip));
            DemoPlayback.LogicalPath = descriptor; DemoPlayback.PlaybackPath = playback; ReplayCamera.Track.Clear();
            context = backend.Capture();
            DateTime descriptorStamp = File.GetLastWriteTimeUtc(descriptor); long descriptorLength = new FileInfo(descriptor).Length;
            File.WriteAllText(descriptor, JsonSerializer.Serialize(clip with { StartFrame = 11 })); File.SetLastWriteTimeUtc(descriptor, descriptorStamp);
            Check(new FileInfo(descriptor).Length == descriptorLength && Reject(() => backend.Enqueue(context, options)),
                "same-size same-stamp virtual descriptor range ABA rejects the original reviewed export");
            File.WriteAllText(descriptor, JsonSerializer.Serialize(clip)); File.SetLastWriteTimeUtc(descriptor, descriptorStamp);
            context = backend.Capture(); DateTime playbackStamp = File.GetLastWriteTimeUtc(playback);
            File.WriteAllBytes(playback, new byte[] { 7, 7, 7, 7 }); File.SetLastWriteTimeUtc(playback, playbackStamp);
            Check(Reject(() => backend.Enqueue(context, options)), "same-stamp physical clip-cache replacement rejects reviewed original source");
            File.WriteAllBytes(playback, new byte[] { 9, 9, 9, 9 }); File.SetLastWriteTimeUtc(playback, playbackStamp);
            context = backend.Capture();
            object token = DemoPlayback.PlaybackOwner; WeakReference oldPlayer = DemoPlayback.PlayerReference(); DemoPlayback.ReplaceOwner();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(!oldPlayer.IsAlive && !ReferenceEquals(token, DemoPlayback.PlaybackOwner),
                "actual production CWT accessor changes restart token without token retaining a disposed player/world");
            Check(!backend.IsCurrent(context) && Reject(() => backend.Enqueue(context, options)),
                "same-path new playback cannot reuse original reviewed export context");
            context = backend.Capture();
            ReplayController.ClipOut=21;
            Check(!backend.IsCurrent(context)&&Reject(()=>backend.Enqueue(context,options)),"changed reviewed selection marks reject detached admission");
            ReplayController.ClipOut=20;
            float fov=ReplayCamera.FieldOfView;ReplayCamera.FieldOfView=fov+1;
            Check(!backend.IsCurrent(context)&&Reject(()=>backend.Enqueue(context,options)),"changed reviewed camera values reject detached admission");
            ReplayCamera.FieldOfView=fov;
            var audioReview=ReplayStudioAudioSettingsStore.Read(descriptor,context.SourceRevision);
            ReplayStudioAudioSettingsStore.Save(audioReview,context.SourceRevision,new(Enabled:false),()=>{});
            Check(!backend.IsCurrent(context)&&Reject(()=>backend.Enqueue(context,options)),"changed persisted audio revision rejects detached admission");
            context=backend.Capture();
            string missingWave=Path.Combine(temp,"missing.wav");
            audioReview=ReplayStudioAudioSettingsStore.Read(descriptor,context.SourceRevision);
            ReplayStudioAudioSettingsStore.Save(audioReview,context.SourceRevision,new(MusicFile:missingWave),()=>{});
            Check(Reject(()=>backend.Capture()),"actual audio snapshot refuses missing external WAV before admission");
            audioReview=ReplayStudioAudioSettingsStore.Read(descriptor,context.SourceRevision);
            ReplayStudioAudioSettingsStore.Save(audioReview,context.SourceRevision,new(Enabled:false),()=>{});
            context=backend.Capture();
            Check(context.AudioSnapshot is ReplayExportAudioInput {Settings.Enabled:false,Sources.IsEmpty:true},"review freezes actual disabled audio values rather than retaining an editor provider");
            var finalOptions = options with { Preset = 1, Resolution = 0, Fps = 48, GameHud = true, ReplayOverlay = true, Camera = TheatreExportCamera.Chase };
            var lifetime=EnqueueDetached(finalOptions);
            var accepted = backend.Queue();var admission=ReplayDetachedExportQueue.Active!;
            Check(accepted.Active != null && admission.LogicalPath == descriptor
                &&ReferenceEquals(accepted.Active.Token,admission.Receipt)&&admission.PlaybackHash==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(playback)))
                &&admission.Room==DemoPlayback.Metadata!.RoomKey&&admission.Subject==DemoPlayback.PresentationScene!.Players.MainPlayerIndex,
                "valid synthetic export keeps original logical clip identity in accepted receipt");
            Collect();
            Check(!lifetime.Backend.IsAlive&&!lifetime.Context.IsAlive,"detached admission captures immutable values without retaining editor backend or reviewed context closures");
            string manifestPath = Path.Combine(Path.GetDirectoryName(admission.Receipt.Manifest.FramePattern)!, "render.json");
            var persisted = JsonSerializer.Deserialize<ReplayVideoExportManifest>(File.ReadAllText(manifestPath));
            Check(persisted is { Width: 1280, Height: 720, Fps: 48, GameHud: true, ReplayOverlay: true }
                && persisted.PresetName == ReplayExportPresets.All[1].Name
                && persisted.Segments?.Single().Camera == ReplaySegmentCamera.Chase,
                "final persisted manifest reflects reviewed preset overrides, HUD/overlay and explicit camera");
            var forged=accepted with{Active=accepted.Active! with{Token=new ReplayExportReceipt(admission.Receipt.Id,admission.Receipt.Manifest)}};
            Check(!backend.CancelExact(forged)&&!backend.CancelExact(accepted with{Revision=accepted.Revision-1})
                &&ReferenceEquals(backend.Queue().Active!.Token,admission.Receipt),"equal-valued forged admission token and stale revision cannot cancel current detached ownership");
            var frozenPresentation=admission.Presentation;ReplayCamera.FieldOfView+=7;
            Check(admission.Presentation==frozenPresentation&&admission.Presentation.Fov!=ReplayCamera.FieldOfView,
                "accepted detached camera presentation is independent of later live editor changes");
            Check(backend.CancelExact(accepted)
                && ReferenceEquals(ReplayDetachedExportQueue.Retry?.Receipt.Manifest, admission.Receipt.Manifest),
                "accepted native cancellation retains exact original immutable retry manifest");
            var cancelledPlayer = DemoPlayback.PlayerReference();
            var cancelledWorld = ReplacePresentationWorld();
            DemoPlayback.ReplaceOwner();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(!cancelledPlayer.IsAlive && !cancelledWorld.IsAlive
                && ReferenceEquals(ReplayDetachedExportQueue.Retry?.Receipt.Manifest, admission.Receipt.Manifest),
                "actual native cancellation receipt keeps retry inputs without retaining disposed player or presentation world");
            var retry=backend.Queue();
            Check(backend.RetryExact(retry)&&!ReferenceEquals(backend.Queue().Active!.Token,accepted.Active!.Token)
                &&ReferenceEquals(ReplayDetachedExportQueue.Active!.Receipt.Manifest,admission.Receipt.Manifest)
                &&!backend.CancelExact(accepted),"retry after player/editor replacement uses original immutable inputs with a fresh exact admission token");
            Check(backend.CancelExact(backend.Queue()),"fresh retry admission can be cancelled only with its current receipt");
            Check(ReplayEncoderJob.Last == null || ReplayEncoderJob.Last.Disposed, "backend witnesses start no encoder processes or pending work");
            Console.WriteLine($"PASS {checks} actual source-linked export backend SHA/sidecar/marks/camera/audio/lifetime checks; detached admission/receipt seam, no worker or mix claims");
        }
        finally { ReplayVideoExporter.Cancel(); ReplayExportQueue.ClearPending();ReplayDetachedExportQueue.Reset(); Directory.Delete(temp, true); }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Backend,WeakReference Context) EnqueueDetached(TheatreExportOptions options)
    {
        var backend=RmlUiOpenGlSmoke.FixtureBackend();var context=backend.Capture();
        var result=(new WeakReference(backend),new WeakReference(context));backend.Enqueue(context,options);return result;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Collect(){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReplacePresentationWorld()
    {
        var old = new WeakReference(DemoPlayback.PresentationScene);
        DemoPlayback.PresentationScene = new Scene(); return old;
    }
    private static bool Reject(Action action)
    {
        try { action(); return false; }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException) { return true; }
    }
}
