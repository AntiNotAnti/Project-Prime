using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static class InputGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height, string assetRoot)
    {
        string root = Path.GetFullPath(assetRoot);
        string outputRoot = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(outputRoot, StringComparison.Ordinal))
            throw new ArgumentException("--input requires the copied rmlui assets below this test's output directory; source assets are never written.");
        string nonce = Guid.NewGuid().ToString("N");
        string pageName = "synthetic-input-" + nonce + ".rml";
        string modalName = "synthetic-transform-" + nonce + ".rml";
        string pagePath = Path.Combine(root, pageName), modalPath = Path.Combine(root, modalName);
        string output = Path.GetFullPath(Path.Combine("artifacts", "rmlui-input-rml46"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "README.txt"),
            "Actual native RmlUi OpenGL with synthetic host API keyboard/pointer/IME events and temporary synthetic RML in the copied tool output assets. No OS IME/gamepad/physical device, account, scene, game session or network service is exercised. Temporary RML documents are deleted after the run.\n");
        RmlUiPageManager? ownedPages = null;
        try
        {
            WriteNew(pagePath, """
                <rml><head><title>Synthetic input fixture</title><style>
                body { margin: 0; padding: 0; width: 100%; height: 100%; font-family: Rajdhani; font-size: 16px; color: #ffffff; background-color: #071a2a; }
                #input_panel { position: absolute; left: 5%; top: 5%; width: 90%; height: 90%; overflow-y: auto; }
                h1 { display: block; font-size: 20px; margin: 0 0 8px; }
                button, input { display: block; width: 220px; height: 28px; margin: 5px 0; padding: 2px 6px; tab-index: auto; color: #ffffff; background-color: #163c58; border: 1px #79b4d4; }
                button:focus, input:focus { background-color: #275f7f; }
                #input_hidden { display: none; }
                </style></head><body><div id="input_panel"><h1>SYNTHETIC INPUT FIXTURE</h1>
                <button id="input_first" data-action="route:play">FIRST</button>
                <button id="input_disabled" disabled="disabled" data-action="route:home">DISABLED</button>
                <button id="input_nonfocus" disabled="disabled" style="focus: none;" data-action="route:home">AUTHORED NONFOCUS</button>
                <button id="input_hidden" data-action="route:home">HIDDEN</button>
                <button id="input_second" data-action="route:settings">SECOND</button>
                <input id="input_a" type="text" value=""/><input id="input_b" type="text" value=""/>
                <button id="input_last" data-action="route:offline">LAST</button>
                </div></body></rml>
                """);
            WriteNew(modalPath, """
                <rml><head><title>Synthetic transformed modal</title><style>
                body { margin: 0; padding: 0; width: 100%; height: 100%; font-family: Rajdhani; font-size: 16px; color: #ffffff; background-color: rgba(0,0,0,70%); }
                #transform_parent { position: absolute; left: 50%; top: 50%; width: 120px; height: 50px; transform: translate(-50%, -50%) rotate(20deg) scale(1.2); transform-origin: 50% 50%; }
                #transform_action { display: block; position: absolute; left: 0; top: 0; width: 120px; height: 50px; margin: 0; padding: 0; border: 0px; tab-index: auto; background-color: #176b90; color: #ffffff; }
                </style></head><body><div id="transform_parent"><button id="transform_action" data-action="route:offline">SYNTHETIC TRANSFORM</button></div></body></rml>
                """);
            var pages = new RmlUiPageManager(host);
            ownedPages = pages;
            host.Input.SetFramebufferScale(1, 1);
            var document = pages.OpenPage(new("synthetic-input", pageName, "input_first"));
            Frame(); host.DiscardIntents();
            Check(host.FocusedElement() == "input_first", "synthetic page initial native focus");
            Key(1); Check(host.FocusedElement() == "input_second", "actual Tab skips disabled/hidden/nonfocus buttons; actual focus=" + host.FocusedElement());
            Key(1, RmlUiInputModifiers.Shift); Check(host.FocusedElement() == "input_first", "actual Shift+Tab reverses native traversal; actual focus=" + host.FocusedElement());
            Key(1); Key(2);
            Intent(document, RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Settings,
                "actual Tab plus Enter activates selected native button exactly once");
            Key(1); Check(host.FocusedElement() == "input_a" && host.TextInputActive, "Tab enters native editable field");
            Key(1); Check(host.FocusedElement() == "input_b", "Tab advances between editable fields");
            Key(1, RmlUiInputModifiers.Shift); Check(host.FocusedElement() == "input_a", "Shift+Tab returns to prior field");
            Check(!host.FocusDocument(document, "input_disabled") && !host.FocusDocument(document, "input_hidden"),
                "disabled and hidden controls cannot acquire native focus");
            Check(!host.TryGetElementBounds(document, "input_hidden", out _, out _, out _, out _),
                "hidden DOM control exposes no drawable bounds");
            host.SetBool(document, "disabled:input_disabled", false); Frame();
            Check(host.FocusDocument(document, "input_disabled"), "reenabling generic button restores native focus");
            host.SetBool(document, "disabled:input_disabled", true); Frame();
            Check(!host.FocusDocument(document, "input_disabled"), "disabling generic button removes native focus again");
            host.SetBool(document, "disabled:input_disabled", false); Frame();
            Check(host.FocusDocument(document, "input_first"), "focus starts disabled binding roundtrip traversal");
            Key(1);
            Check(host.FocusedElement() == "input_disabled", "enabled generic button returns to authored Tab order; actual focus=" + host.FocusedElement());
            host.SetBool(document, "disabled:input_disabled", true);
            host.SetBool(document, "disabled:input_nonfocus", false); Frame();
            Check(!host.FocusDocument(document, "input_nonfocus"), "reenabling preserves authored local focus:none");
            host.FocusDocument(document, "input_first"); Key(1);
            Check(host.FocusedElement() == "input_second", "enabled authored focus:none remains outside Tab order; actual focus=" + host.FocusedElement());
            host.SetBool(document, "disabled:input_nonfocus", true); Frame();

            host.SetField(document, "input_a", "BASE "); Frame();
            Check(host.FocusDocument(document, "input_a"), "IME fixture field acquires native focus");
            Key(10, RmlUiInputModifiers.Control);
            Check(host.TryGetTextInputState(out var scope) && scope.Document == document
                && (scope.Capabilities & RmlUiTextInputCapabilities.Composition) != 0,
                "real native text-input scope advertises composition capability");
            Check(Compose(scope, RmlUiPlatformInputKind.CompositionBegin) == RmlUiInputResult.Accepted,
                "scoped synthetic IME composition begins");
            Check(Compose(scope, RmlUiPlatformInputKind.CompositionUpdate, "日本😀", 2) == RmlUiInputResult.Accepted
                && host.ReadField(document, "input_a") == "BASE 日本😀", "Unicode preedit appears in actual native field");
            Check(host.Input.Dispatch(new(document, RmlUiPlatformInputKind.TextCommitted,
                RmlUiInputDevice.InputMethod, scope.FocusEpoch, Text: "duplicate")) == RmlUiInputResult.Invalid,
                "ordinary character callback cannot double-append active composition");
            Check(Compose(scope, RmlUiPlatformInputKind.CompositionUpdate, "日", 1) == RmlUiInputResult.Accepted
                && host.ReadField(document, "input_a") == "BASE 日", "preedit update replaces its previous marked range");
            Check(Compose(scope, RmlUiPlatformInputKind.CompositionCancel) == RmlUiInputResult.Accepted
                && host.ReadField(document, "input_a") == "BASE ", "composition cancellation restores committed draft");
            Check(Compose(scope, RmlUiPlatformInputKind.CompositionBegin) == RmlUiInputResult.Accepted
                && Compose(scope, RmlUiPlatformInputKind.CompositionUpdate, "ニホン", 3) == RmlUiInputResult.Accepted
                && Compose(scope, RmlUiPlatformInputKind.CompositionCommit, "日本") == RmlUiInputResult.Accepted
                && host.ReadField(document, "input_a") == "BASE 日本", "composition commit replaces preedit once");
            Check(Compose(scope, RmlUiPlatformInputKind.CompositionCommit, "duplicate") == RmlUiInputResult.StaleFocus
                && host.ReadField(document, "input_a") == "BASE 日本", "repeated composition commit cannot append twice");
            Check(host.TryGetTextSelectionUtf16(scope, out int selectionStart, out int selectionEnd)
                && selectionStart == selectionEnd && selectionStart == "BASE 日本".Length,
                "native UTF16 selection reports committed Unicode cursor without exposing text");
            Check(host.FocusDocument(document, "input_b") && host.FocusDocument(document, "input_a"),
                "native text focus can leave and return to same field");
            Check(host.TryGetTextInputState(out var fresh) && fresh.FocusEpoch != scope.FocusEpoch,
                "returning focus creates a fresh text input lifetime");
            Check(Compose(scope, RmlUiPlatformInputKind.CompositionBegin) == RmlUiInputResult.StaleFocus
                && host.Input.Dispatch(new(document, RmlUiPlatformInputKind.KeyDown,
                    RmlUiInputDevice.InputMethod, scope.FocusEpoch, Code: 13)) == RmlUiInputResult.StaleFocus
                && host.ReadField(document, "input_a") == "BASE 日本" && host.ReadField(document, "input_b") == "",
                "stale composition/deletion cannot change current or replacement field");
            Capture("unicode-input");

            host.ShowDocument(document, false); Frame();
            Check(!host.TryGetElementBounds(document, "input_a", out _, out _, out _, out _)
                && !host.FocusDocument(document, "input_a"), "hidden document has no focus or exposed element bounds");
            Check(host.Input.Dispatch(new(document, RmlUiPlatformInputKind.TextCommitted,
                RmlUiInputDevice.InputMethod, fresh.FocusEpoch, Text: "hidden")) == RmlUiInputResult.HiddenDocument,
                "scoped text input rejects hidden document");
            host.ShowDocument(document, true); host.FocusDocument(document, "input_a"); Frame();
            Check(host.TryGetTextInputState(out var backgroundScope), "visible field reacquires text scope");
            var modal = pages.OpenModal(new("synthetic-existing-dialog", "pages/social/confirm.rml", "social_confirm_cancel"));
            pages.Present(modal, 1, new Dictionary<string, RmlUiBindingValue>
            { ["social_confirmation_text"] = RmlUiBindingValue.FromText("SYNTHETIC INPUT CONFIRMATION // NO ACCOUNT OR SESSION CHANGE") });
            Settle(450);
            Check(host.CurrentInputDocument == modal, "visible modal becomes native input foreground");
            Check(!host.FocusDocument(document, "input_a"), "foreground modal prevents background native field from stealing focus");
            Check(Compose(backgroundScope, RmlUiPlatformInputKind.CompositionBegin) is RmlUiInputResult.Unsupported or RmlUiInputResult.StaleFocus,
                "old background text scope cannot compose under foreground modal");
            Pointer(modal, "social_confirm_apply");
            Intent(modal, RmlUiIntentKind.SocialAction, 46,
                "actual translated existing dialog center dispatches its native pointer action");
            Capture("existing-translated-dialog");
            Check(pages.CloseModal(), "existing synthetic dialog closes");

            var transformed = pages.OpenModal(new("synthetic-transformed-dialog", modalName, "transform_action"));
            Settle(180);
            Check(host.TryGetElementBounds(transformed, "transform_action", out float tx, out float ty, out float tw, out float th),
                "transform-aware native bounds resolve child through transformed ancestor");
            double radians = 20 * Math.PI / 180;
            double expectedWidth = 1.2 * (120 * Math.Cos(radians) + 50 * Math.Sin(radians));
            double expectedHeight = 1.2 * (120 * Math.Sin(radians) + 50 * Math.Cos(radians));
            Check(Math.Abs(tw - expectedWidth) < 3 && Math.Abs(th - expectedHeight) < 3
                && Math.Abs(tx + tw / 2 - width / 2.0) < 3 && Math.Abs(ty + th / 2 - height / 2.0) < 3,
                "native AABB includes ancestor translation,20degree rotation and1.2scale in framebuffer coordinates");
            Pointer(transformed, "transform_action");
            Intent(transformed, RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Offline,
                "actual pointer center agrees with transformed native rendering/hit test");
            Capture("rotated-scaled-dialog");
            Check(pages.CloseModal() && !host.IsAlive(transformed), "transformed document retires");
            host.FocusDocument(document, "input_a"); Frame();
            Check(host.TryGetTextInputState(out var retiredScope), "page has scoped text input before retirement");
            var lifetime = host.DocumentCancellation(document);
            Check(pages.ClosePage() && lifetime.IsCancellationRequested, "page close retires document cancellation lifetime");
            Check(!host.TryGetElementBounds(document, "input_a", out _, out _, out _, out _)
                && host.Input.Dispatch(new(document, RmlUiPlatformInputKind.TextCommitted,
                    RmlUiInputDevice.InputMethod, retiredScope.FocusEpoch, Text: "retired")) == RmlUiInputResult.StaleDocument,
                "retired document rejects delayed text and bounds queries");
            Check(GL.GetError() == ErrorCode.NoError, "native synthetic input leaves no GL error");
            Console.WriteLine("RML INPUT GL synthetic-only screenshots: " + output);

            RmlUiInputResult Compose(RmlUiTextInputState state, RmlUiPlatformInputKind kind, string text = "", int cursor = -1)
                => host.Input.Dispatch(new(state.Document, kind, RmlUiInputDevice.InputMethod, state.FocusEpoch, Text: text, Cursor: cursor));
            void Frame()
            {
                host.Update(); GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                GL.Viewport(0, 0, width, height); GL.Disable(EnableCap.ScissorTest);
                GL.ClearColor(0.025f, 0.05f, 0.08f, 1);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
                RmlUiOpenGlFrame.Draw(host, width, height);
            }
            void Settle(int milliseconds)
            {
                var watch = Stopwatch.StartNew();
                do { Frame(); Thread.Sleep(10); } while (watch.ElapsedMilliseconds < milliseconds);
                Frame();
            }
            void Key(int code, RmlUiInputModifiers modifiers = default)
            {
                host.Input.Key(code, true, modifiers); host.Input.Key(code, false, modifiers); Frame();
                Console.WriteLine($"RML INPUT GL KEY code={code} modifiers={modifiers} focused={host.FocusedElement()}");
            }
            void Intent(RmlUiDocumentToken source, RmlUiIntentKind kind, int argument, string label)
            {
                Check(host.TryTakeIntent(out var intent) && intent.Document == source && intent.Kind == kind
                    && intent.Argument == argument && pages.Accept(intent) && !pages.Accept(intent), label);
                Check(!host.TryTakeIntent(out _), "one native activation emits exactly one typed intent");
            }
            void Pointer(RmlUiDocumentToken source, string element)
            {
                Check(host.FocusDocument(source, element), "native pointer target can focus " + element); Settle(450);
                Check(host.TryGetElementBounds(source, element, out float x, out float y, out float w, out float h), "pointer target bounds resolve " + element);
                double px = x + w / 2, py = y + h / 2;
                Check(px >= 0 && px < width && py >= 0 && py < height, "transformed pointer center lies in framebuffer");
                host.Input.PointerButton(0, px, py, true); host.Input.PointerButton(0, px, py, false); Frame();
            }
            void Capture(string name)
            {
                Settle(180); byte[] picture = FinalCompositeCapture.Read(width, height);
                string path = Path.Combine(output, $"synthetic-{name}-{width}x{height}.ppm");
                using (var stream = File.Create(path))
                {
                    stream.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC HOST INPUT - ACTUAL NATIVE GL FRAMEBUFFER\n{width} {height}\n255\n"));
                    for (int row = height - 1; row >= 0; row--) stream.Write(picture, row * width * 3, width * 3);
                }
                FinalCompositeCapture.WriteEvidence(path, width, height, "Synthetic host API keyboard/pointer/IME input; actual native GL; no physical input device or gameplay scene acceptance.");
            }
        }
        finally
        {
            try { ownedPages?.Dispose(); }
            finally { if (File.Exists(pagePath)) File.Delete(pagePath); if (File.Exists(modalPath)) File.Delete(modalPath); }
        }
    }
    private static void WriteNew(string path, string source)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(Encoding.UTF8.GetBytes(source));
    }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("RML INPUT GL PASS " + label);
    }
}
