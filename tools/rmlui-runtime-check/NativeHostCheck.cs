using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class ManagedHostCheck
{
    public static void Run(string library, string assets)
    {
        string root = Path.Combine(Path.GetTempPath(), "prime-rmlui-managed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        nint module = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(Path.GetFullPath(assets), "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(root, Path.GetRelativePath(Path.GetFullPath(assets), file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            module = NativeLibrary.Load(Path.GetFullPath(library));
            Run(module, root);
        }
        finally
        {
            if (module != 0) NativeLibrary.Free(module);
            Directory.Delete(root, recursive: true);
        }
    }

    // Source-linked by core-check to cover the real managed P/Invoke boundary as
    // well as the native-only ABI checks. Both runtimes must be idle on entry.
    public static void Run(nint module, string root)
    {
        NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly, (name, _, _) =>
            name == "ProjectPrime.RmlUi.Native" ? module : 0);
        File.WriteAllText(Path.Combine(root, "managed-contract.rml"), """
            <rml><head><title>Managed host contract</title><style>
            body { font-family: Rajdhani; font-weight: 600; font-size: 24px; background-color: #102030; }
            button, input { display: block; width: 260px; height: 40px; tab-index: auto; }
            </style></head><body><div id="message">TEXT</div><input id="name" type="text" value="" />
            <input id="limited" type="text" maxlength="4" value="" /><textarea id="profile" />
            <button id="submit" data-action="route:settings">SETTINGS</button></body></rml>
            """);
        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Managed/native host: " + message);
        }
        using var host = new RmlUiHost();
        Check(host.Initialize(1280, 720, 1.5f, root, RmlUiRenderBackend.DrawList), "real initialization failed");
        Check(host.ProtocolVersion == RmlUiIntentRegistry.ProtocolVersion, "protocol negotiation failed");
        var home = host.HomeDocument;
        host.Present(new(home, 1, new Dictionary<string, RmlUiBindingValue>
        {
            ["player_name"] = RmlUiBindingValue.FromText("Hunter λ😀"),
            ["reduce_motion"] = RmlUiBindingValue.FromBoolean(true)
        }));
        Check(host.FocusDocument(home, "activity_selector"), "home binding/focus failed");
        var modal = host.OpenDocument("managed-contract.rml", RmlUiDocumentLayer.Modal);
        var cancellation = host.DocumentCancellation(modal);
        host.SetText(modal, "message", "<button>untrusted Unicode λ😀</button>");
        Check(host.FocusDocument(modal, "name") && host.TextInputActive, "input did not acquire focus");
        host.Input.Text("λ😀");
        host.Update();
        Check(host.ReadField(modal, "name") == "λ😀", "UTF-8 field ABI failed");
        Check(host.TryGetTextInputState(out var textScope)
            && textScope.Document == modal && (textScope.Capabilities & RmlUiTextInputCapabilities.Composition) != 0,
            "versioned text-input context ABI failed");
        RmlUiPlatformInputEvent Composition(RmlUiTextInputState scope, RmlUiPlatformInputKind kind, string text = "", int cursor = -1)
            => new(scope.Document, kind, RmlUiInputDevice.InputMethod, scope.FocusEpoch, Text: text, Cursor: cursor);
        Check(host.Input.Dispatch(Composition(textScope, RmlUiPlatformInputKind.CompositionBegin)) == RmlUiInputResult.Accepted,
            "real composition begin failed");
        Check(host.Input.Dispatch(Composition(textScope, RmlUiPlatformInputKind.CompositionUpdate, "日本😀", 2)) == RmlUiInputResult.Accepted
            && host.ReadField(modal, "name") == "λ😀日本😀", "real preedit was not rendered in the input widget");
        Check(host.Input.Dispatch(Composition(textScope, RmlUiPlatformInputKind.CompositionUpdate, "日", 1)) == RmlUiInputResult.Accepted
            && host.ReadField(modal, "name") == "λ😀日", "preedit appended instead of replacing its prior range");
        Check(host.Input.Dispatch(Composition(textScope, RmlUiPlatformInputKind.CompositionCancel)) == RmlUiInputResult.Accepted
            && host.ReadField(modal, "name") == "λ😀", "composition cancel lost the user's committed draft");
        host.Input.Dispatch(Composition(textScope, RmlUiPlatformInputKind.CompositionBegin));
        host.Input.Dispatch(Composition(textScope, RmlUiPlatformInputKind.CompositionUpdate, "ニホン", 3));
        Check(host.Input.Dispatch(Composition(textScope, RmlUiPlatformInputKind.CompositionCommit, "日本")) == RmlUiInputResult.Accepted
            && host.ReadField(modal, "name") == "λ😀日本", "composition commit did not replace the marked range once");
        host.FocusDocument(modal, "submit");
        host.FocusDocument(modal, "name");
        Check(host.Input.Dispatch(Composition(textScope, RmlUiPlatformInputKind.CompositionCommit, "late")) == RmlUiInputResult.StaleFocus
            && host.ReadField(modal, "name") == "λ😀日本", "old text-focus lifetime committed into the current field");
        host.SetField(modal, "limited", "A");
        host.Update(); // RmlUi lays out the authored draft before End can address its last character.
        host.FocusDocument(modal, "limited");
        host.Input.Key(10, true, RmlUiInputModifiers.Control); host.Input.Key(10, false, RmlUiInputModifiers.Control);
        Check(host.TryGetTextInputState(out var limitedScope), "limited input context unavailable");
        Check(limitedScope.SelectionStart == 1 && limitedScope.SelectionEnd == 1,
            $"End did not reach the authored draft end ({limitedScope.SelectionStart},{limitedScope.SelectionEnd})");
        Check(host.Input.Dispatch(Composition(limitedScope, RmlUiPlatformInputKind.CompositionBegin)) == RmlUiInputResult.Accepted,
            "limited composition begin failed");
        Check(host.Input.Dispatch(Composition(limitedScope, RmlUiPlatformInputKind.CompositionUpdate, "😀日本", 3)) == RmlUiInputResult.Accepted,
            "limited composition preedit failed");
        Check(host.Input.Dispatch(Composition(limitedScope, RmlUiPlatformInputKind.CompositionCommit, "😀日本")) == RmlUiInputResult.Accepted,
            "limited composition commit failed");
        Check(host.ReadField(modal, "limited") == "A😀日本", "composition length counted UTF-16 units instead of Unicode scalars");
        host.Input.Dispatch(Composition(limitedScope, RmlUiPlatformInputKind.CompositionBegin));
        host.Input.Dispatch(Composition(limitedScope, RmlUiPlatformInputKind.CompositionUpdate, "MORE", 4));
        host.Input.Dispatch(Composition(limitedScope, RmlUiPlatformInputKind.CompositionCommit, "MORE"));
        Check(host.ReadField(modal, "limited") == "A😀日本", "composition commit exceeded authored maxlength");
        host.SetClipboard("日本語 λ😀");
        Check(host.ReadClipboard() == "日本語 λ😀", "clipboard UTF-8 ABI failed");
        string fullProfile = new string('x', 128 * 1024 - 8) + "λ😀ab";
        host.SetField(modal, "profile", fullProfile);
        Check(host.ReadField(modal, "profile", 128 * 1024) == fullProfile, "maximum Unicode UTF-8 textarea profile was truncated");
        bool oversized = false;
        try { host.ReadField(modal, "profile"); } catch (InvalidOperationException) { oversized = true; }
        Check(oversized, "small textarea bound silently truncated a valid large profile");
        Check(host.FocusDocument(modal, "profile") && host.TextInputActive, "textarea did not acquire text input ownership");
        host.SetClipboard(fullProfile);
        Check(host.ReadClipboard() == fullProfile, "maximum Unicode UTF-8 clipboard profile was truncated");
        host.SetClipboard(fullProfile + "x");
        oversized = false;
        try { host.ReadClipboard(); } catch (InvalidOperationException) { oversized = true; }
        Check(oversized, "oversized clipboard profile was truncated silently");
        host.SetField(modal, "profile", ""); // Keep final render bounded; large profile roundtrip is a field contract.
        host.SetClipboard("日本語 λ😀");
        Check(host.FocusDocument(modal, "submit"), "DOM action control focus failed");
        host.Input.Key(2, true); // Desktop adapter maps GLFW Enter (257) to ABI key 2.
        host.Input.Key(2, false);
        host.Update();
        Check(host.TryTakeIntent(out var intent), "real DOM action did not cross managed/native intent ABI");
        Check(intent.Kind == RmlUiIntentKind.Navigate && intent.Argument == (int)RmlUiRouteArgument.Settings
            && intent.Document == modal && intent.Sequence != 0, "intent payload/lifetime was corrupted");
        Check(!host.TryTakeIntent(out _), "action dispatched twice");
        Check(host.CloseDocument(modal) && cancellation.IsCancellationRequested && !host.IsAlive(modal),
            "real close did not retire its managed lifetime");
        Check(host.FocusedElement() == "activity_selector", "modal close did not restore home focus");
        var backModal = host.OpenDocument("managed-contract.rml", RmlUiDocumentLayer.Modal);
        var backCancellation = host.DocumentCancellation(backModal);
        host.EnqueueSnapshot(new(backModal, 1, new Dictionary<string, RmlUiBindingValue>
        {
            ["message"] = RmlUiBindingValue.FromText("late completion after Back")
        }));
        Check(host.FocusDocument(backModal, "submit") && host.Back()
            && backCancellation.IsCancellationRequested && !host.IsAlive(backModal),
            "Back closed native modal without retiring managed ownership");
        host.Update(); // A retired token cannot reach native document bindings.
        Check(host.FocusedElement() == "activity_selector", "Back did not restore the launching control focus");
        host.ReleaseInput();
        Check(!host.TextInputActive && host.FocusedElement() == "", "focus release ABI failed");
        host.Update();
        host.Render(1280, 720);
        Check(host.Reinitialize() && !host.IsAlive(home) && host.HomeDocument != home,
            "real runtime reinitialization did not retire old native IDs");
        host.Update();
        host.Render(1280, 720);
        host.DeviceLost();
        Check(!host.Active && host.HomeDocument == default, "real device-loss teardown failed");
        NativeScheduleCheck.Run(root);
        Console.WriteLine("Managed/native RmlUi host integration passed: actual P/Invoke v1 handshake, DOM intent, Unicode bindings/fields/clipboard, focus restoration, document cancellation, reinit, and device loss.");
    }
}
