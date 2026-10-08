#if MPHREAD_RMLUI_POC
using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Adventure;

internal static class NativeAdventureCheck
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ElementBounds(ulong document, [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
        out float x, out float y, out float width, out float height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CommandCount();

    public static void Run(string library, string assets)
    {
        nint module = NativeLibrary.Load(Path.GetFullPath(library));
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,
                (name, _, _) => name == "ProjectPrime.RmlUi.Native" ? module : 0);
            var bounds = Marshal.GetDelegateForFunctionPointer<ElementBounds>(
                NativeLibrary.GetExport(module, "pp_rmlui_document_element_bounds"));
            var commands = Marshal.GetDelegateForFunctionPointer<CommandCount>(
                NativeLibrary.GetExport(module, "pp_rmlui_draw_command_count"));
            int checks = 0;
            void Check(bool condition, string description)
            {
                if (!condition) throw new InvalidOperationException("Adventure native: " + description);
                checks++;
            }

            foreach (var viewport in new[] { (1280, 720, 1f), (1920, 1080, 1f), (960, 540, 1f), (1920, 1080, 2f) })
            {
                using var host = new RmlUiHost();
                Check(host.Initialize(viewport.Item1, viewport.Item2, viewport.Item3,
                    Path.GetFullPath(assets), RmlUiRenderBackend.DrawList), "initialize real native core");
                Check(host.FocusDocument(host.HomeDocument, "activity_selector"), "baseline focus");
                using var controller = new AdventureController(new AdventureBoundary());
                using var page = new AdventurePagePresenter(host, controller);
                page.Open();
                host.Update();
                Check(page.IsOpen && host.FocusedElement() == "adventure_slot1", "page opens with native focus");
                Check(bounds(page.Document.DocumentId, "adventure_title", out _, out float titleY, out float titleWidth, out float titleHeight) != 0
                    && titleWidth > 0 && titleHeight > 0
                    && bounds(page.Document.DocumentId, "adventure_description", out _, out float descriptionY, out float descriptionWidth, out float descriptionHeight) != 0
                    && descriptionWidth > 0 && descriptionHeight > 0 && descriptionY >= titleY + titleHeight - 1,
                    "native block heading and description have positive widths and vertical separation");
                Check(!host.FocusDocument(page.Document, "adventure_continue"), "empty slot disables native Continue focus");
                int measured = bounds(page.Document.DocumentId, "adventure_slot1", out float x, out float y, out float width, out float height);
                Check(measured != 0
                    && width > 0 && height >= 32 * viewport.Item3 && x >= 0 && y >= 0
                    && x + width <= viewport.Item1 + 1,
                    $"slot uses live responsive native layout ({measured}: {x},{y} {width}x{height}; viewport {viewport})");

                void Activate(RmlUiDocumentToken document, string id, RmlUiIntentKind expected)
                {
                    Check(host.FocusDocument(document, id), "focus " + id);
                    host.Input.Key(2, true);
                    host.Input.Key(2, false);
                    host.Update();
                    Check(host.TryTakeIntent(out var intent) && intent.Kind == expected
                        && intent.Document == document && page.HandleIntent(intent), "actual DOM action " + id);
                    Check(!host.TryTakeIntent(out _), "action is emitted once");
                }

                Activate(page.Document, "adventure_slot2", RmlUiIntentKind.AdventureSelectSlot);
                Check(controller.Snapshot().SelectedSlot == 2 && controller.Snapshot().CanContinue,
                    "slot action reaches the real controller");
                Activate(page.Document, "adventure_new_run", RmlUiIntentKind.AdventureNewRun);
                var modal = page.ConfirmationDocument;
                var cancellation = host.DocumentCancellation(modal);
                Check(modal != default && host.FocusedElement() == "adventure_cancel_new_run",
                    "overwrite opens a native modal with safe initial focus");
                Check(!page.HandleIntent(new(RmlUiIntentKind.AdventureContinue, 0, page.Document, ulong.MaxValue)),
                    "covered page actions cannot escape the modal");
                Activate(modal, "adventure_cancel_new_run", RmlUiIntentKind.AdventureCancelNewRun);
                Check(cancellation.IsCancellationRequested && !host.IsAlive(modal)
                    && host.FocusedElement() == "adventure_new_run", "modal cancellation retires and restores focus");
                Activate(page.Document, "adventure_new_run", RmlUiIntentKind.AdventureNewRun);
                modal = page.ConfirmationDocument;
                Check(host.Back() && page.Present() && !controller.Snapshot().ConfirmOverwrite
                    && !host.IsAlive(modal), "host Back reconciles the confirmation choice");
                Activate(page.Document, "adventure_new_run", RmlUiIntentKind.AdventureNewRun);
                Activate(page.ConfirmationDocument, "adventure_confirm_new_run", RmlUiIntentKind.AdventureConfirmNewRun);
                Check(page.TryTakeLaunch(out var launch) && launch.SaveSlot == 2 && launch.NewGame
                    && !page.TryTakeLaunch(out _), "native confirmation produces one authoritative launch handoff");
                page.ReportLaunchFailure("<button id=\"injected_action\">unsafe λ😀</button>");
                host.Update();
                Check(bounds(page.Document.DocumentId, "injected_action", out _, out _, out _, out _) == 0,
                    "untrusted load error remains encoded text");
                host.Render(viewport.Item1, viewport.Item2);
                Check(commands() > 0, "page emits native backend-neutral draw commands");
                var pageCancellation = host.DocumentCancellation(page.Document);
                Check(page.Close() && pageCancellation.IsCancellationRequested
                    && host.FocusedElement() == "activity_selector", "page close retires its lifetime and restores home focus");
            }
            Console.WriteLine($"ADVENTURE NATIVE PASS {checks} checks (real core/DOM/action/focus; 720p, 1080p, small layout, 2x density).");
        }
        finally { NativeLibrary.Free(module); }
    }
}
#endif
