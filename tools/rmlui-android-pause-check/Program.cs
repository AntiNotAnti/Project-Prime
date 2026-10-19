using MphRead.Droid;
using MphRead.Mods.Launcher.RmlUi.Host;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
var notices = new List<RmlUiAndroidPauseSnapshot>();
var logs = new List<string>();
var surfaces = new List<FakeSurface>();
var scene = new object();
object? captured = null;
var route = new RmlUiAndroidPauseRoute(Task.FromResult("verified-app-private-root"), (ownedScene, root) =>
{
    captured = ownedScene;
    Check(root == "verified-app-private-root", "owner receives verified asset result without a package read on UI");
    var surface = new FakeSurface(); surfaces.Add(surface); return surface;
}, notices.Add, logs.Add);
Check(Task.Run(route.RequestToggle).Result && route.OwnsInput && surfaces.Count == 0,
    "UI request captures no scene and performs no native/controller creation");
route.Process(scene, 1280, 720, 2);
Check(surfaces.Count == 0, "request remains pending until caller's existing EGL surface is current");
route.SurfaceReady(); route.Process(scene, 1280, 720, 2);
var first = notices[^1]; var surface1 = surfaces[^1];
Check(first.State == RmlUiAndroidPauseState.Open && ReferenceEquals(captured, scene)
    && route.IsCurrent(first) && surface1.Opens == 1, "existing EGL owner opens a scene-fenced page exactly once");
var pointer = surface1.Input.Current;
Check(route.Input!.TryEnqueue(pointer, new(pointer.Document, RmlUiPlatformInputKind.PointerDown,
    RmlUiInputDevice.Touch, X: 47, Y: 53)), "copied touch enters active pause lifetime");
Check(route.Draw(scene, 1280, 720, 2) && surface1.Native.Buttons == 1 && surface1.Native.Pointer == (47, 53),
    "only terminal owner render drains native pause touch");
Task.Run(route.SuspendInput).Wait();
Check(!surface1.Input.Current.Active && surface1.Native.Buttons == 1,
    "UI pause invalidates pointer immediately without a native call");
route.SurfaceLost();
Check(surface1.Native.Buttons == 0 && surface1.Suspends == 1,
    "owner flushes pointer hold before caller releases EGL surface");
Check(!route.Draw(scene, 1280, 720, 2), "suspended surface cannot render native page");
route.SurfaceReady();
Check(surface1.Resumes == 1 && surface1.Input.Current.Active
    && !surface1.Input.TryEnqueue(pointer, new(pointer.Document, RmlUiPlatformInputKind.PointerUp)),
    "replacement surface has a fresh input lifetime and rejects old contact");
Task.Run(() => route.SetFocused(false)).Wait(); route.Process(scene, 1280, 720, 2);
Check(surface1.Suspends == 2 && !surface1.Input.Current.Active, "window focus loss also retires native input on owner");
route.SetFocused(true); route.Process(scene, 1280, 720, 2);
Check(surface1.Resumes == 2 && surface1.Input.Current.Active, "focus restoration resumes input on existing owner");
Check(Task.Run(route.RequestToggle).Result, "UI Back request is copied");
surface1.BackClosesJournal = true; route.Process(scene, 1280, 720, 2);
Check(surface1.Backs == 1 && route.OwnsInput && surface1.Disposes == 0,
    "Back can close journal without closing pause or creating another native session");
route.RequestToggle(); route.Process(scene, 1280, 720, 2);
Check(!route.OwnsInput && surface1.Disposes == 1 && notices[^1].State == RmlUiAndroidPauseState.Resume
    && !route.IsCurrent(first), "second Back retires pause before publishing exact resume effect");

route.RequestToggle(); route.Process(scene, 1280, 720, 2);
var surface2 = surfaces[^1]; var oldOpened = notices[^1];
surface2.Effect = RmlUiAndroidPauseState.Settings;
route.Draw(scene, 1280, 720, 2);
var settings = notices[^1];
Check(settings.State == RmlUiAndroidPauseState.Settings && surface2.Disposes == 1 && route.Input == null,
    "Settings handoff follows input/native retirement rather than overlapping page stacks");
route.RequestToggle();
Check(!route.IsCurrent(settings) && !route.IsCurrent(oldOpened),
    "a new request invalidates delayed UI handoffs from the prior route");
route.Process(scene, 1280, 720, 2);
var surface3 = surfaces[^1]; var replacedScene = new object();
surface3.Effect = RmlUiAndroidPauseState.Leave;
route.Process(replacedScene, 1280, 720, 2);
Check(surface3.Disposes == 1 && notices[^1].State == RmlUiAndroidPauseState.Closed && !route.OwnsInput,
    "scene replacement discards a stale confirmed Leave instead of ending replacement scene");
Check(!route.Draw(replacedScene, 1280, 720, 2), "old native surface cannot draw the replacement scene");
var wrongThread = Task.Run(() =>
{
    try { route.Process(replacedScene, 1, 1, 1); return false; }
    catch (InvalidOperationException) { return true; }
}).Result;
Check(wrongThread, "another thread cannot own page/controller processing");
route.RequestToggle(); route.RequestStop(); route.Process(replacedScene, 1280, 720, 2);
Check(!route.RequestToggle() && !route.IsCurrent(settings), "Activity/view stop permanently rejects stale requests and callbacks");
route.DisposeOwner();

var pending = new TaskCompletionSource<string>();
var pendingRoute = new RmlUiAndroidPauseRoute(pending.Task, (_, _) => throw new Exception(), _ => { }, logs.Add);
Check(!pendingRoute.RequestToggle(), "unready assets immediately preserve existing pause fallback");
pending.SetException(new IOException("fake package missing"));
Check(!pendingRoute.RequestToggle(), "failed package never starts a native host");
_ = pending.Task.Exception;

foreach (bool initialization in new[] { true, false })
{
    var broken = new FakeSurface { ThrowOnOpen = initialization, ThrowOnRender = !initialization, ThrowOnDispose = true };
    var failureNotices = new List<RmlUiAndroidPauseSnapshot>();
    var failed = new RmlUiAndroidPauseRoute(Task.FromResult("root"), (_, _) => broken, failureNotices.Add, logs.Add);
    failed.RequestToggle(); failed.SurfaceReady(); failed.Process(scene, 10, 10, 1);
    if (!initialization) failed.Draw(scene, 10, 10, 1);
    Check(broken.Disposes == 1 && failureNotices[^1].State == RmlUiAndroidPauseState.Fallback
        && failed.Input == null && !failed.RequestToggle(), "native initialization/render failure still retires owner and publishes existing pause fallback");
    failed.DisposeOwner();
}

var releaseFailure = new FakeSurface();
var releaseNotices = new List<RmlUiAndroidPauseSnapshot>();
var releaseRoute = new RmlUiAndroidPauseRoute(Task.FromResult("root"), (_, _) => releaseFailure, releaseNotices.Add, logs.Add);
releaseRoute.RequestToggle(); releaseRoute.SurfaceReady(); releaseRoute.Process(scene, 10, 10, 1);
releaseFailure.Native.ThrowOnRelease = true;
releaseRoute.SuspendInput(); releaseRoute.SurfaceLost();
Check(releaseFailure.Disposes == 1 && releaseFailure.Shutdowns == 1 && !releaseFailure.Input.Current.Active
    && releaseNotices[^1].State == RmlUiAndroidPauseState.Fallback,
    "native input-release failure still reaches surface/native shutdown before fallback");
releaseRoute.DisposeOwner();

var beforeDrawStop = new FakeSurface();
var drawStopNotices = new List<RmlUiAndroidPauseSnapshot>();
var drawStopRoute = new RmlUiAndroidPauseRoute(Task.FromResult("root"), (_, _) => beforeDrawStop, drawStopNotices.Add, logs.Add);
drawStopRoute.RequestToggle(); drawStopRoute.SurfaceReady(); drawStopRoute.Process(scene, 10, 10, 1);
beforeDrawStop.Effect = RmlUiAndroidPauseState.Leave;
drawStopRoute.RequestStop();
Check(!drawStopRoute.Draw(scene, 10, 10, 1) && beforeDrawStop.Renders == 0 && beforeDrawStop.Disposes == 1
    && drawStopNotices.All(notice => notice.State != RmlUiAndroidPauseState.Leave),
    "stop between Process and terminal Draw retires without rendering or draining native intents");
drawStopRoute.DisposeOwner();

var handoffNotices = new List<RmlUiAndroidPauseSnapshot>();
var handoffSurface = new FakeSurface { ThrowOnOpen = true };
var handoffRoute = new RmlUiAndroidPauseRoute(Task.FromResult("root"), (_, _) => handoffSurface, handoffNotices.Add, logs.Add);
handoffRoute.RequestToggle(); handoffRoute.SurfaceReady(); handoffRoute.Process(scene, 10, 10, 1);
var delayedFallback = handoffNotices[^1];
handoffRoute.RetireForUiHandoff();
Check(!handoffRoute.IsCurrent(delayedFallback) && !handoffRoute.OwnsInput,
    "an independently opened existing menu invalidates pending native fallback before it can hide that menu");
handoffRoute.DisposeOwner();

var reopenSurfaces = new List<FakeSurface>();
var reopenRoute = new RmlUiAndroidPauseRoute(Task.FromResult("root"), (_, _) =>
{ var surface = new FakeSurface(); reopenSurfaces.Add(surface); return surface; }, _ => { }, logs.Add);
reopenRoute.RequestToggle(); reopenRoute.SurfaceReady(); reopenRoute.Process(scene, 10, 10, 1);
reopenRoute.RetireForUiHandoff();
Check(reopenRoute.RequestToggle(), "new explicit native request may follow an existing-route handoff without waiting on EGL");
reopenRoute.Process(scene, 10, 10, 1);
Check(reopenSurfaces.Count == 2 && reopenSurfaces[0].Disposes == 1
    && reopenRoute.Current.State == RmlUiAndroidPauseState.Open,
    "batched close then open retires old native session and honors new request instead of stranding Requested");
reopenRoute.DisposeOwner();

var slowOpenSurface = new FakeSurface();
var slowOpenNotices = new List<RmlUiAndroidPauseSnapshot>();
var slowOpenRoute = new RmlUiAndroidPauseRoute(Task.FromResult("root"), (_, _) => slowOpenSurface, slowOpenNotices.Add, logs.Add);
slowOpenSurface.OnOpen = () => Task.Run(slowOpenRoute.RetireForUiHandoff).Wait();
slowOpenRoute.RequestToggle(); slowOpenRoute.SurfaceReady(); slowOpenRoute.Process(scene, 10, 10, 1);
Check(slowOpenSurface.Disposes == 1 && slowOpenNotices.Count == 0 && slowOpenRoute.Input == null
    && slowOpenRoute.Current.State == RmlUiAndroidPauseState.Closed,
    "handoff racing a slow native Open cannot overwrite existing UI with a stale Open publication");
slowOpenRoute.DisposeOwner();

var batchedNotices = new List<RmlUiAndroidPauseSnapshot>();
var batchedSurface = new FakeSurface();
var batchedRoute = new RmlUiAndroidPauseRoute(Task.FromResult("root"), (_, _) => batchedSurface, batchedNotices.Add, logs.Add);
batchedRoute.RequestToggle(); batchedRoute.RequestToggle(); batchedRoute.SurfaceReady(); batchedRoute.Process(scene, 10, 10, 1);
Check(batchedSurface.Disposes == 1 && batchedNotices.Count == 2 && !batchedRoute.IsCurrent(batchedNotices[0])
    && batchedRoute.IsCurrent(batchedNotices[1]) && batchedNotices[1].State == RmlUiAndroidPauseState.Resume,
    "rapid open then Back leaves only terminal Resume current even when UI never applied Open");
batchedRoute.DisposeOwner();

var stoppedDuringOpen = new FakeSurface();
var stopNotices = new List<RmlUiAndroidPauseSnapshot>();
var stopRoute = new RmlUiAndroidPauseRoute(Task.FromResult("root"), (_, _) => stoppedDuringOpen, stopNotices.Add, logs.Add);
stoppedDuringOpen.OnOpen = () => Task.Run(stopRoute.RequestStop).Wait();
stopRoute.RequestToggle(); stopRoute.SurfaceReady(); stopRoute.Process(scene, 10, 10, 1);
Check(stoppedDuringOpen.Disposes == 1 && stopNotices.Count == 0 && stopRoute.Input == null && !stopRoute.OwnsInput,
    "Activity stop racing native Open cannot publish an active queue or stale Open callback");
stopRoute.DisposeOwner();

var lease1 = new object(); var lease2 = new object();
Check(RmlUiAndroidPauseNativeLease.AvailableForNewOwner && RmlUiAndroidPauseNativeLease.TryAcquire(lease1)
    && !RmlUiAndroidPauseNativeLease.AvailableForNewOwner && !RmlUiAndroidPauseNativeLease.TryAcquire(lease2),
    "asynchronous replacement view cannot initialize process-global native bridge twice");
RmlUiAndroidPauseNativeLease.Release(lease1, shutdownSucceeded: true);
Check(RmlUiAndroidPauseNativeLease.AvailableForNewOwner && RmlUiAndroidPauseNativeLease.TryAcquire(lease2), "next view acquires native bridge only after old shutdown completes");
RmlUiAndroidPauseNativeLease.Release(lease2, shutdownSucceeded: false);
Check(!RmlUiAndroidPauseNativeLease.AvailableForNewOwner && !RmlUiAndroidPauseNativeLease.TryAcquire(lease1), "failed native teardown quarantines later initialization");
Console.WriteLine($"RMLUI ANDROID PAUSE PASS {checks} checks; production route/input/host with fake page/native surface, no Android/APK/GLES/touch/device acceptance.");

internal sealed class FakeSurface : IRmlUiAndroidPauseSurface
{
    internal readonly FakeBridge Native = new();
    private readonly RmlUiHost _host;
    internal int Opens, Backs, Suspends, Resumes, Disposes, Shutdowns, Renders;
    internal Action? OnOpen;
    internal bool BackClosesJournal, ThrowOnOpen, ThrowOnRender, ThrowOnDispose;
    internal RmlUiAndroidPauseState Effect;
    public RmlUiAndroidInputQueue Input { get; } = new();
    internal FakeSurface() => _host = new(Native);
    public void Open(int width, int height, float density)
    {
        Opens++;
        if (ThrowOnOpen) throw new InvalidOperationException("fake initialize failure");
        _host.Initialize(width, height, density, Directory.GetCurrentDirectory(), RmlUiRenderBackend.DrawList);
        Input.Resume(_host);
        OnOpen?.Invoke();
    }
    public void Back()
    {
        Backs++;
        if (BackClosesJournal) { BackClosesJournal = false; return; }
        Effect = RmlUiAndroidPauseState.Resume;
    }
    public void Render(int width, int height, float density)
    {
        Renders++;
        if (ThrowOnRender) throw new InvalidOperationException("fake terminal render failure");
        Input.Drain(_host); _host.Update(); Input.Refresh(_host);
    }
    public bool TryTakeEffect(out RmlUiAndroidPauseState effect)
    { effect = Effect; Effect = RmlUiAndroidPauseState.Idle; return effect != RmlUiAndroidPauseState.Idle; }
    public void Suspend() { Suspends++; Input.Suspend(); Input.Drain(_host); }
    public void Resume() { Resumes++; Input.Resume(_host); }
    public void Dispose()
    {
        Disposes++; Input.Suspend();
        try { Input.Drain(_host); }
        finally { Shutdowns++; _host.Shutdown(); }
        if (ThrowOnDispose) throw new InvalidOperationException("fake cleanup failure after native teardown");
    }
}
