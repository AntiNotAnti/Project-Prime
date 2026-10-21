using MphRead.Droid;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class PointerBurstAcceptance
{
    internal static void Run(Action<bool, string> check)
    {
        var native = new FakeBridge();
        using var host = new RmlUiHost(native);
        host.Initialize(640, 360, 1, ".", RmlUiRenderBackend.DrawList);
        var queue = new RmlUiAndroidInputQueue(capacity: 4);
        queue.Resume(host);
        var scope = queue.Current;
        RmlUiPlatformInputEvent Input(RmlUiPlatformInputKind kind, int x) =>
            new(scope.Document, kind, RmlUiInputDevice.Touch, X: x, Y: 40);

        check(queue.TryEnqueue(scope, Input(RmlUiPlatformInputKind.PointerDown, 10)), "burst press queued");
        bool accepted = true;
        for (int sample = 0; sample < 8000; sample++)
            accepted &= queue.TryEnqueue(scope, Input(RmlUiPlatformInputKind.PointerMove, sample));
        check(accepted && queue.Metrics.Pending == 2 && queue.Metrics.Overflows == 0,
            "8000 adjacent touch samples retain only latest position without overflow");
        check(queue.TryEnqueue(scope, Input(RmlUiPlatformInputKind.PointerUp, 8000))
            && queue.TryEnqueue(scope, Input(RmlUiPlatformInputKind.PointerMove, 9000)),
            "release and later move fit behind coalesced drag");
        queue.Drain(host);
        check(native.PointerHistory.SequenceEqual(new[] { (10, 40, 0), (7999, 40, 1), (8000, 40, 1), (9000, 40, 0) })
            && native.Buttons == 0 && queue.Metrics.Dispatched == 4,
            "down/latest drag/up/hover preserve exact order and native button state");

        // Wrap the ring and prove a key event cannot be removed or crossed.
        native.PointerHistory.Clear();
        queue.TryEnqueue(scope, Input(RmlUiPlatformInputKind.PointerMove, 20));
        queue.TryEnqueue(scope, new(scope.Document, RmlUiPlatformInputKind.KeyDown, Code: 1));
        queue.TryEnqueue(scope, Input(RmlUiPlatformInputKind.PointerMove, 30));
        queue.TryEnqueue(scope, Input(RmlUiPlatformInputKind.PointerMove, 31));
        check(queue.Metrics.Pending == 3, "key edge fences pointer coalescing even across ring wrap");
        queue.Drain(host);
        check(native.PointerHistory.SequenceEqual(new[] { (20, 40, 0), (31, 40, 0) }) && native.Keys == 2,
            "ring wrap retains both positions around key edge");

        var move = Input(RmlUiPlatformInputKind.PointerMove, 50);
        queue.TryEnqueue(scope, move);
        queue.TryEnqueue(scope, move with { X = 51, Modifiers = RmlUiInputModifiers.Shift });
        queue.TryEnqueue(scope, move with { X = 52, Device = RmlUiInputDevice.Keyboard });
        check(queue.Metrics.Pending == 3, "modifiers and input devices fence move replacement");
        queue.Suspend();
        check(!queue.TryEnqueue(scope, move) && queue.Metrics.Pending == 0,
            "suspension retires coalesced moves and stale witness immediately");
        queue.Drain(host);
        check(native.Buttons == 0 && native.Keys == 0, "suspension still releases held edges after coalescing");
    }
}
