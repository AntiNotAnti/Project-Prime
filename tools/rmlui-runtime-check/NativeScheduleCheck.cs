using System.Diagnostics;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class NativeScheduleCheck
{
    public static void Run(string root)
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new InvalidOperationException("Managed/native schedule: " + message);
        }
        File.WriteAllText(Path.Combine(root, "managed-schedule.rml"), """
            <rml><head><title>Schedule contract</title><style>
            body { font-family: Rajdhani; font-weight: 600; font-size: 24px; background-color: #102030; }
            button, input { display: block; width: 260px; height: 40px; tab-index: auto; }
            #message { display: block; width: 260px; height: 40px; }
            </style></head><body><div id="message">INITIAL</div><input id="name" type="text" value="" />
            <button id="submit" data-action="route:settings">SETTINGS</button></body></rml>
            """);
        File.WriteAllText(Path.Combine(root, "managed-animation.rml"), """
            <rml><head><title>Timer contract</title><style>
            body { font-family: Rajdhani; font-weight: 600; font-size: 24px; }
            @keyframes enter { from { opacity: 0; } to { opacity: 1; } }
            #animated { display: block; width: 260px; height: 40px; animation: enter 0.16s; }
            </style></head><body><div id="animated">ANIMATION</div></body></rml>
            """);
        using var host = new RmlUiHost();
        Check(host.Initialize(1280, 720, 1, root, RmlUiRenderBackend.DrawList), "initialization failed");
        if (!host.TryGetUpdateState(out _))
        {
            Console.WriteLine("Managed/native schedule: older bridge retains eager compatibility; optional scheduling checks skipped.");
            return;
        }
        var page = host.OpenDocument("managed-schedule.rml", RmlUiDocumentLayer.Page);
        Check(host.ShowDocument(host.HomeDocument, false), "legacy home could not be hidden");
        void Settle()
        {
            host.Update(); host.Render(1280, 720);
            Check(host.TryGetUpdateState(out var state) && !state.Dirty && state.DrawListValid,
                "update/render did not produce a valid retained draw list");
        }
        RmlUiUpdateState State()
        {
            if (!host.TryGetUpdateState(out var state))
                throw new InvalidOperationException("Managed/native schedule: status ABI became unavailable");
            return state;
        }
        void Mutation(Action action, string message)
        {
            var before = State();
            action();
            var after = State();
            Check((after.Dirty || after.VisualRevision != before.VisualRevision) && !after.DrawListValid,
                message + " did not invalidate the prior draw list");
            Settle();
        }
        Settle();
        host.ReleaseInput(); Settle();
        var idle = State();
        Check(double.IsPositiveInfinity(idle.NextUpdateDelaySeconds), "plain unfocused page was not genuinely idle");
        var metrics = host.UpdateMetrics;
        for (int frame = 0; frame < 100; frame++) { host.Update(); host.Render(1280, 720); }
        var retained = State();
        Check(host.UpdateMetrics.NativeUpdates == metrics.NativeUpdates
            && host.UpdateMetrics.SkippedUpdates == metrics.SkippedUpdates + 100
            && retained.VisualRevision == idle.VisualRevision && retained.DrawListValid,
            "idle frames performed updates or invalidated the retained frame");
        Mutation(() => host.SetText(page, "message", "Unicode λ😀"), "text binding");
        Mutation(() => host.SetBool(page, "disabled:submit", true), "boolean binding");
        Mutation(() => host.SetBool(page, "disabled:submit", false), "button enable");
        Mutation(() => host.SetField(page, "name", "λ😀"), "field publication");
        metrics = host.UpdateMetrics;
        host.SetText(page, "message", "Unicode λ😀"); host.Update();
        Check(host.UpdateMetrics.NativeUpdates == metrics.NativeUpdates,
            "unchanged managed binding triggered native work");
        Mutation(() => host.Input.PointerMoved(900, 600), "pointer event");
        idle = State();
        host.Input.PointerMoved(900, 600);
        Check(State().VisualRevision == idle.VisualRevision && !State().Dirty,
            "unchanged pointer coordinates invalidated idle state");
        Mutation(() => host.FocusDocument(page, "name"), "text focus");
        var caret = State();
        Check(caret.NextUpdateDelaySeconds > 0 && double.IsFinite(caret.NextUpdateDelaySeconds),
            "focused input did not advertise a finite caret deadline");
        var remaining = caret.NextUpdateDelaySeconds;
        Thread.Sleep(20);
        var elapsed = State();
        Check(elapsed.NextUpdateDelaySeconds < remaining,
            "relative RmlUi timeout was not decremented using elapsed wall time");
        Mutation(() => host.Input.Text("日本"), "committed text input");
        Check(host.ReadField(page, "name").Contains("日本", StringComparison.Ordinal),
            "scheduled input update lost Unicode text");
        Mutation(() => { host.Input.Key(13, true); host.Input.Key(13, false); }, "IME-independent delete key");
        Check(host.TryGetTextInputState(out var text), "text context unavailable");
        Mutation(() => host.Input.Dispatch(new(text.Document, RmlUiPlatformInputKind.CompositionBegin,
            RmlUiInputDevice.InputMethod, text.FocusEpoch)), "composition begin");
        Mutation(() => host.Input.Dispatch(new(text.Document, RmlUiPlatformInputKind.CompositionUpdate,
            RmlUiInputDevice.InputMethod, text.FocusEpoch, Text: "日本😀", Cursor: 3)), "composition update");
        Mutation(() => host.Input.Dispatch(new(text.Document, RmlUiPlatformInputKind.CompositionCancel,
            RmlUiInputDevice.InputMethod, text.FocusEpoch)), "composition cancellation");
        var blink = State();
        Check(blink.NextUpdateDelaySeconds > 0 && blink.NextUpdateDelaySeconds <= 1,
            "caret did not retain a bounded future deadline after composition");
        metrics = host.UpdateMetrics;
        Thread.Sleep((int)Math.Ceiling(blink.NextUpdateDelaySeconds * 1000) + 10);
        Check(State().NextUpdateDelaySeconds == 0, "expired caret timer did not become due");
        host.Update(); host.Render(1280, 720);
        Check(host.UpdateMetrics.NativeUpdates == metrics.NativeUpdates + 1
            && State().VisualRevision != blink.VisualRevision,
            "idle scheduling dropped the due caret update");
        host.ReleaseInput(); Settle();
        Mutation(() => host.Resize(1440, 900, 1.25f), "viewport/density change");
        host.Resize(1280, 720, 1); Settle();
        var modal = default(RmlUiDocumentToken);
        Mutation(() => modal = host.OpenDocument("managed-schedule.rml", RmlUiDocumentLayer.Modal), "modal open");
        Mutation(() => host.CloseDocument(modal), "modal close/resource release");
        Check(!host.IsAlive(modal), "closed modal lifetime survived");
        var animation = host.OpenDocument("managed-animation.rml", RmlUiDocumentLayer.Modal);
        var deadline = Stopwatch.StartNew();
        bool sawAnimationDeadline = false;
        while (deadline.ElapsedMilliseconds < 350)
        {
            host.Update(); host.Render(1280, 720);
            sawAnimationDeadline |= State().NextUpdateDelaySeconds == 0;
            Thread.Sleep(10);
        }
        Check(sawAnimationDeadline, "authored entry animation did not request immediate updates");
        Check(double.IsPositiveInfinity(State().NextUpdateDelaySeconds), "completed entry animation continued updating indefinitely");
        host.CloseDocument(animation); host.ReleaseInput(); Settle();
        var previous = host.HomeDocument;
        Check(host.Reinitialize() && !host.IsAlive(previous), "reinitialization did not retire the cached generation");
        Check(State().Generation != previous.Generation && !State().DrawListValid,
            "reinitialization retained a stale draw-list epoch");
        Console.WriteLine($"Managed/native schedule passed: {checks} real status, idle reuse, mutation, timer, composition, resource, and generation assertions.");
    }
}
