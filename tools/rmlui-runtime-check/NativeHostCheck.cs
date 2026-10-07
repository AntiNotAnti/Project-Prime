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
            body { font-family: Rajdhani; font-size: 24px; background-color: #102030; }
            button, input { display: block; width: 260px; height: 40px; tab-index: auto; }
            </style></head><body><div id="message">TEXT</div><input id="name" type="text" value="" />
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
        host.SetClipboard("日本語 λ😀");
        Check(host.ReadClipboard() == "日本語 λ😀", "clipboard UTF-8 ABI failed");
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
        Console.WriteLine("Managed/native RmlUi host integration passed: actual P/Invoke v1 handshake, DOM intent, Unicode bindings/fields/clipboard, focus restoration, document cancellation, reinit, and device loss.");
    }
}
