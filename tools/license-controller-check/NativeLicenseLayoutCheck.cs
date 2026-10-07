#if MPHREAD_RMLUI_POC
using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.License;

internal static class NativeLicenseLayoutCheck
{
    public static void Run(string library, string assets)
    {
        nint module = NativeLibrary.Load(Path.GetFullPath(library));
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,
                (name, _, _) => name == "ProjectPrime.RmlUi.Native" ? module : 0);
            int checks = 0;
            void Check(bool value, string message)
            {
                if (!value) throw new InvalidOperationException("License native: " + message);
                checks++;
            }
            foreach (var viewport in new[] { (640, 320, 1f), (1280, 720, 1f), (1280, 640, 2f) })
            {
                using var host = new RmlUiHost();
                Check(host.Initialize(viewport.Item1, viewport.Item2, viewport.Item3, Path.GetFullPath(assets), RmlUiRenderBackend.DrawList), "real native initialization");
                using var pages = new RmlUiPageManager(host);
                var backend = new FakeLicense();
                using var controller = new LicenseController(backend);
                using var presenter = new LicensePagePresenter(host, pages, controller);
                presenter.Open(loadProfile: false); host.Update();
                var page = presenter.Document;
                Check(host.FocusDocument(page, "license_account"), "authored Account tab is focusable");
                host.Input.Key(2, true); host.Input.Key(2, false); host.Update();
                Check(host.TryTakeIntent(out var tab) && tab.Kind == RmlUiIntentKind.LicenseAction && tab.Argument == 8 && presenter.Handle(tab), "real native Account action selects complete account workflow");
                host.Update();
                Check(host.TryGetElementBounds(page, "stage", out _, out _, out float stageWidth, out _), "shared shell stage has real bounds");
                Check(host.TryGetElementBounds(page, "license_page", out float px, out float py, out float pw, out float ph)
                    && pw > stageWidth * .8f && ph > 50 && px >= 0 && py >= 0 && px + pw <= viewport.Item1 + 1 && py + ph <= viewport.Item2 + 1,
                    $"outer License viewport stays within compact window ({px},{py} {pw}x{ph}; stage width {stageWidth}; viewport {viewport})");
                float previousBottom = float.MinValue;
                foreach (string id in new[] { "license_email", "license_password", "license_confirmation" })
                {
                    Check(host.TryGetElementBounds(page, id, out float x, out float y, out float width, out float height)
                        && width > 200 * viewport.Item3 && height >= 44 * viewport.Item3 && x >= px && x + width <= px + pw + 1 && y >= previousBottom,
                        "independent account field has readable width and vertical layout: " + id);
                    previousBottom = y + height;
                }
                Check(host.TryGetElementBounds(page, "license_email", out _, out float beforeEmailY, out _, out _), "email starts in actual authored flow");
                host.Input.PointerMoved(px + pw * .85, py + ph / 2);
                host.Input.PointerWheel(-4); host.Update();
                Check(host.TryGetElementBounds(page, "license_email", out _, out float afterEmailY, out _, out _) && afterEmailY < beforeEmailY,
                    "wheel over content advances the real outer License scroll viewport");
                bool Reach(string id, out float x, out float y, out float width, out float height)
                {
                    x = y = width = height = 0;
                    for (int i = 0; i < 60; i++)
                    {
                        if (!host.TryGetElementBounds(page, id, out x, out y, out width, out height)) return false;
                        if (y >= py && y + height <= py + ph) return true;
                        host.Input.PointerMoved(px + pw * .85, py + ph / 2);
                        host.Input.PointerWheel(y < py ? .5 : -.5); Thread.Sleep(16); host.Update();
                    }
                    return false;
                }
                foreach (string id in new[] { "license_email", "license_password", "license_confirmation" })
                {
                    Check(Reach(id, out float x, out float y, out float width, out float height),
                        $"outer wheel brings account field fully into compact viewport: {id} ({x},{y} {width}x{height}; outer {px},{py} {pw}x{ph})");
                    host.Input.PointerButton(0, x + width / 2, y + height / 2, true);
                    host.Input.PointerButton(0, x + width / 2, y + height / 2, false); host.Update();
                    Check(host.FocusedElement() == id && host.TextInputActive, "actual pointer activates native editable field: " + id);
                }
                bool closed = false; presenter.Closed += () => closed = true;
                Check(Reach("license_back", out float bx, out float by, out float bw, out float bh), "outer wheel reaches Back beneath full account workflow");
                host.Input.PointerButton(0, bx + bw / 2, by + bh / 2, true);
                host.Input.PointerButton(0, bx + bw / 2, by + bh / 2, false); host.Update();
                Check(host.TryTakeIntent(out var back) && back.Kind == RmlUiIntentKind.LicenseAction && back.Argument == 20
                    && presenter.Handle(back) && closed, "actual pointer activates reachable Back control");
                Check(backend.Accounts.Count == 0, "layout/input checks do not invoke production account commands");
                presenter.Dispose(); Check(!host.IsAlive(page), "License document retires its native lifetime");
            }
            Console.WriteLine($"LICENSE NATIVE PASS {checks} checks (actual account controls, outer wheel, compact 640x320 and density2; account operations: 0).");
        }
        finally { NativeLibrary.Free(module); }
    }
}
#endif
