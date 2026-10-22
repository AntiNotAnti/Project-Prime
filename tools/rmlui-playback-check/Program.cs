using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Launcher.RmlUi.Host;
using OpenTK.Windowing.GraphicsLibraryFramework;

int checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException("Playback contract: " + message);
    checks++;
}
void RejectOtherThread(Action action, string message)
{
    bool rejected = Task.Run(() =>
    {
        try { action(); return false; }
        catch (InvalidOperationException) { return true; }
    }).GetAwaiter().GetResult();
    Check(rejected, message);
}

Check(!DemoPlayback.IsActive, "fixture starts without a replay session");
Check(DemoPlayback.MovementLabel == "Unknown", "inactive playback movement label has no recorded authority");
var playbackDocument = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "playback.rml"));
var studioButton = playbackDocument.Descendants("button").Single(element => (string?)element.Attribute("id") == "replay_studio");
Check((string?)studioButton.Attribute("data-action") == "replay:studio"
    && RmlUiIntentRegistry.TryParseLegacy((string)studioButton.Attribute("data-action")!, new(1, 1), 1, out var studioIntent)
    && studioIntent.Kind == RmlUiIntentKind.ReplayAction && studioIntent.Argument == (int)TheatrePlaybackAction.Studio,
    "production Studio button uses the shared Studio route so viewer can launch external app without an editor backend");
Check(studioButton.Value == "OPEN IN PRIME STUDIO", "viewer Studio action has external-app wording");
var rangeControls = playbackDocument.Descendants("div").Single(element => (string?)element.Attribute("id") == "replay_range_controls");
Check(rangeControls.Descendants("button").Select(element => (string?)element.Attribute("id"))
    .SequenceEqual(new[] { "replay_mark_in", "replay_mark_out", "replay_ranges" }),
    "all range editing entry points share one capability-hidden group");
var backend = new PlaybackBoundary();
using (var controller = new TheatrePlaybackController(backend))
{
    var original = controller.Snapshot();
    Check(!original.Active && original.Frame == 0 && original.Duration == 0,
        "inactive snapshot contains no recorded world state");
    Check(ReferenceEquals(original, controller.Snapshot()), "unchanged snapshot keeps its revision");
    foreach (TheatrePlaybackAction action in Enum.GetValues<TheatrePlaybackAction>())
        if (action is not (TheatrePlaybackAction.Back or TheatrePlaybackAction.Fullscreen))
            Check(!controller.Dispatch(action), "inactive session refuses " + action);
    Check(!controller.Dispatch((TheatrePlaybackAction)999), "undefined transport action is rejected");
    Check(backend.Calls == 0, "inactive Studio action never starts an editor");
    Check(controller.Dispatch(TheatrePlaybackAction.Back)
        && controller.Dispatch(TheatrePlaybackAction.Fullscreen), "shell commands work without a replay");
    Check(controller.TryTakeEngineCommand(out var first) && first == TheatrePlaybackAction.Back,
        "first shell command retains ownership until consumed");
    Check(!controller.TryTakeEngineCommand(out _), "shell command is consumed once");
    Check(controller.Dispatch(TheatrePlaybackAction.Fullscreen)
        && controller.TryTakeEngineCommand(out var fullscreen) && fullscreen == TheatrePlaybackAction.Fullscreen,
        "fullscreen is handed to the current shell owner");
    RejectOtherThread(() => controller.Dispatch(TheatrePlaybackAction.Back), "transport rejects another thread");
    RejectOtherThread(() => controller.Snapshot(), "snapshot rejects another thread");
    RejectOtherThread(controller.Dispose, "retirement rejects another thread");
    controller.Dispatch(TheatrePlaybackAction.Back);
    controller.Dispose();
    Check(!controller.TryTakeEngineCommand(out _) && !controller.Dispatch(TheatrePlaybackAction.Back),
        "retirement drops pending commands and refuses new dispatch");
    Check(!controller.Snapshot().Active, "retired controller remains inactive");
}

var viewport = new TheatreViewportController();
Check(!viewport.Focused && !viewport.KeyDown(Keys.W), "unfocused viewport does not capture movement");
viewport.Focus();
Check(viewport.Focused && viewport.KeyDown(Keys.W), "focused viewport owns its held movement keys");
viewport.Release();
Check(!viewport.Focused && !viewport.KeyUp(Keys.W) && !viewport.KeyDown(Keys.W),
    "release clears capture and held-key ownership");
viewport.Focus();
Check(viewport.KeyDown(Keys.Escape) && !viewport.Focused, "Escape releases viewport focus");
viewport.Release();
viewport.PointerUp();
RejectOtherThread(viewport.Focus, "viewport focus rejects another thread");
RejectOtherThread(viewport.Release, "viewport release rejects another thread");
Check(!DemoPlayback.IsActive && backend.Calls == 0,
    "fixture leaves playback inactive without loading recordings, scenes or Studio");
Console.WriteLine($"PASS {checks} playback transport and viewport ownership checks");

sealed class PlaybackBoundary : ITheatrePlaybackBackend
{
    public int Calls;
    public bool OpenStudio(string path, out string? error)
    {
        Calls++;
        error = "Studio must not launch from this inactive fixture.";
        return false;
    }
}
