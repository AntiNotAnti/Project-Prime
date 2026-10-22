using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static class AdventureSaveGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Path.Combine("artifacts", "rmlui-adventure-saves-rml58"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "README.txt"), "Actual native RmlUi save-management journey with SYNTHETIC immutable slot metadata and in-memory command authority. No saves, serializer, game assets, campaign, filesystem mutation or network are used. Actual byte preservation is tested separately in rmlui-adventure-save-check temporary files.\n");
        var services = new Services();
        using var controller = new RmlUiMenuController(services);
        using var manager = new RmlUiPageManager(host);
        using var layout = new RmlUiLauncherPages(host, manager);
        long presented = 0;
        host.Input.SetFramebufferScale(1, 1);
        Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Adventure), "native Adventure save management opens without cartridge files or legacy fallback");
        Frame(); var parent = layout.Document;
        Check(!host.FocusDocument(parent, "adventure_continue") && !host.FocusDocument(parent, "adventure_new_run")
            && NativeSemanticReadback.Read(host, parent).ContainsText("GAME FILES ARE REQUIRED TO LAUNCH CAMPAIGN"), "missing assets disable gameplay while save management remains native");
        Click("adventure_destination3", RmlUiIntentKind.AdventureCopyDestination, 3);
        Click("adventure_copy", RmlUiIntentKind.AdventureCopy);
        var modal = manager.Top;
        Check(modal != parent && NativeSemanticReadback.Read(host, modal).ContainsText(First)
            && NativeSemanticReadback.Read(host, modal).ContainsText("DESTINATION SLOT 03 // EMPTY") && services.Changes.Count == 0,
            "actual copy modal captures source SHA and exact empty destination before any effect");
        Check(!Forward(new(RmlUiIntentKind.AdventureConfirmCopy, 0, parent, ulong.MaxValue)), "background save page cannot confirm modal mutation");
        Capture("copy-empty-review", modal);
        Click("adventure_cancel_save_change", RmlUiIntentKind.AdventureCancelSaveChange);
        var cancelled = controller.Tick();
        Console.WriteLine($"RML ADVENTURE CANCEL DIAGNOSTIC parentRestored={manager.Top == parent} modalAlive={host.IsAlive(modal)} changes={services.Changes.Count} focused={host.FocusedElement()} confirming={cancelled.Bindings["adventure_confirmation_open"].Boolean} canCopy={cancelled.Bindings["adventure_can_copy"].Boolean}");
        Check(manager.Top == parent && !host.IsAlive(modal) && services.Changes.Count == 0
            && host.FocusedElement() == "adventure_copy", "native Cancel retires exact proposal and restores copy focus without write");
        Check(!Forward(new(RmlUiIntentKind.AdventureConfirmCopy, 0, modal, ulong.MaxValue)), "retired copy document cannot replay command");
        Click("adventure_copy", RmlUiIntentKind.AdventureCopy); modal = manager.Top;
        Click("adventure_confirm_copy", RmlUiIntentKind.AdventureConfirmCopy);
        Check(manager.Top == parent && services.Changes.Count == 1 && services.Slots[2].Fingerprint == First,
            "actual native confirmation emits one exact copy command and rereads resulting slots");
        Click("adventure_destination2", RmlUiIntentKind.AdventureCopyDestination, 2);
        Click("adventure_copy", RmlUiIntentKind.AdventureCopy); modal = manager.Top;
        Check(NativeSemanticReadback.Read(host, modal).ContainsText(Second), "occupied unreadable destination SHA remains explicit in native copy confirmation");
        services.Slots[0] = services.Slots[0] with { Fingerprint = new string('C', 64) };
        Click("adventure_confirm_copy", RmlUiIntentKind.AdventureConfirmCopy, expected: false);
        Check(services.Changes.Count == 1 && services.Slots[1].Fingerprint == Second && manager.Top == parent,
            "stale source byte witness rejects native write and leaves destination unchanged");
        Capture("copy-stale-refused", parent);
        Click("adventure_slot2", RmlUiIntentKind.AdventureSelectSlot, 2);
        Check(!host.FocusDocument(parent, "adventure_copy"), "unreadable source cannot be copied as a readable game");
        Click("adventure_archive", RmlUiIntentKind.AdventureArchive); modal = manager.Top;
        Check(NativeSemanticReadback.Read(host, modal).ContainsText("ARCHIVE SLOT 02 // " + Second)
            && !host.FocusDocument(modal, "adventure_confirm_new_run"), "unreadable bytes have explicit recoverable archive confirmation only");
        Capture("unreadable-archive-review", modal);
        var external = manager.OpenModal(new("synthetic-adventure-fence", "pages/social/confirm.rml", "social_confirm_cancel")); Frame();
        Check(!Forward(new(RmlUiIntentKind.AdventureConfirmArchive, 0, modal, ulong.MaxValue)), "unrelated foreground fences Adventure archive effect");
        manager.CloseModal(); Check(!host.IsAlive(external), "synthetic foreground retires");
        Check(layout.Back(out var cancel) && cancel.Kind == RmlUiIntentKind.AdventureCancelSaveChange && controller.Dispatch(cancel.Kind, cancel.Argument),
            "composed native Back uses save-change cancellation rather than new-run action");
        Frame(); Check(!host.IsAlive(modal) && services.Changes.Count == 1
            && host.FocusedElement() == "adventure_archive", "Back discards captured archive without mutation and restores Archive focus");
        Click("adventure_archive", RmlUiIntentKind.AdventureArchive); modal = manager.Top;
        services.Available = false;
        int attempts = services.Attempts;
        Click("adventure_confirm_archive", RmlUiIntentKind.AdventureConfirmArchive, expected: false);
        Check(services.Attempts == attempts && services.Changes.Count == 1,
            "active or pending owner refusal prevents actual native confirmation from entering writer");
        services.Available = true; controller.Dispatch(RmlUiIntentKind.AdventureRefresh); Frame();
        Click("adventure_archive", RmlUiIntentKind.AdventureArchive); modal = manager.Top;
        Click("adventure_confirm_archive", RmlUiIntentKind.AdventureConfirmArchive);
        Check(services.Changes.Count == 2 && services.Changes[1].Source.Fingerprint == Second && services.Slots[1].Fingerprint == "EMPTY",
            "native archive emits exact captured unreadable-byte command once and refreshes slot empty");
        Check(host.FocusedElement() == "adventure_slot2" && !host.FocusDocument(parent, "adventure_archive"),
            "successful Archive restores the selected empty slot instead of retrying a disabled Archive control");
        Capture("archive-verified", parent);
        Click("adventure_slot1", RmlUiIntentKind.AdventureSelectSlot, 1);
        Click("adventure_archive", RmlUiIntentKind.AdventureArchive); modal = manager.Top;
        controller.Dispatch(RmlUiIntentKind.AdventureRefresh); Frame();
        Check(!host.IsAlive(modal) && !Forward(new(RmlUiIntentKind.AdventureConfirmArchive, 0, modal, ulong.MaxValue))
            && services.Changes.Count == 2, "refresh retires native archive proposal without any write");
        Click("adventure_archive", RmlUiIntentKind.AdventureArchive); modal = manager.Top;
        controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home); Frame();
        Check(!host.IsAlive(modal) && !Forward(new(RmlUiIntentKind.AdventureConfirmArchive, 0, modal, ulong.MaxValue))
            && services.Changes.Count == 2 && services.Launches == 0, "page retirement prevents late save mutation or game launch");
        Check(GL.GetError() == ErrorCode.NoError, "Adventure save fixture completes without GL errors");
        Console.WriteLine("RML ADVENTURE SAVE GL screenshots: " + output);

        bool Forward(in RmlUiIntent intent)
            => layout.HandleIntent(intent, out var forwarded) && forwarded.Kind != 0 && controller.Dispatch(forwarded.Kind, forwarded.Argument);
        void Frame()
        {
            var state = controller.Tick();
            if (state.Revision > presented)
            {
                foreach (var entry in state.Bindings)
                    if (entry.Value.Kind == RmlUiBindingKind.Text) layout.SetText(entry.Key, entry.Value.Text);
                    else layout.SetBool(entry.Key, entry.Value.Boolean);
                presented = state.Revision;
            }
            layout.Flush(); host.Update(); layout.AfterUpdate();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height); GL.Disable(EnableCap.ScissorTest);
            GL.ClearColor(.025f, .05f, .08f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle(int milliseconds)
        { var timer = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(10); } while (timer.ElapsedMilliseconds < milliseconds); Frame(); }
        void Click(string id, RmlUiIntentKind kind, int argument = 0, bool expected = true)
        {
            Frame(); var target = manager.Top;
            Check(host.FocusDocument(target, id), "native focus reaches " + id); Settle(450);
            Check(host.TryGetElementBounds(target, id, out float x, out float y, out float w, out float h), "native bounds resolve " + id);
            double px = x + w / 2d, py = y + h / 2d;
            Check(px >= 0 && px < width && py >= 0 && py < height, "save management control is reachable inside framebuffer " + id);
            host.Input.PointerMoved(px, py); Frame();
            string hovered = host.HoveredElement();
            bool authoredSlotLabel = false;
            if (id is "adventure_slot1" or "adventure_slot2" or "adventure_slot3" && hovered == id + "_label"
                && host.TryGetElementBounds(target, hovered, out float childX, out float childY, out float childW, out float childH))
                authoredSlotLabel = childW > 0 && childH > 0 && childX >= x && childY >= y
                    && childX + childW <= x + w && childY + childH <= y + h
                    && px >= childX && px <= childX + childW && py >= childY && py <= childY + childH;
            if (hovered != id)
            {
                bool labelFound = host.TryGetElementBounds(target, id + "_label", out float lx, out float ly, out float lw, out float lh);
                var current = controller.Tick();
                Console.WriteLine($"RML ADVENTURE HOVER DIAGNOSTIC requested={id} hovered={hovered} focused={host.FocusedElement()} top={manager.Top} target={target} button=({x},{y},{w},{h}) labelFound={labelFound} label=({lx},{ly},{lw},{lh}) confirming={current.Bindings["adventure_confirmation_open"].Boolean}");
            }
            Check(hovered == id || authoredSlotLabel, "native hover matches save action or contained authored slot label " + id);
            host.Input.PointerButton(0, px, py, true); host.Input.PointerButton(0, px, py, false); host.Update();
            bool received = false;
            while (host.TryTakeIntent(out var intent))
            {
                bool accepted = Forward(intent);
                if (intent.Kind != kind || intent.Argument != argument) continue;
                Check(accepted == expected && intent.Document == target, "actual pointer dispatches expected save action " + id);
                Check(!Forward(intent), "duplicate save action sequence rejected " + id); received = true;
            }
            Check(received, "native save control emits expected intent " + id); Settle(180);
        }
        void Capture(string name, RmlUiDocumentToken document)
        {
            if (host.TryGetElementBounds(document, "adventure_confirmation", out float x, out float y, out float w, out float h))
            { host.Input.PointerMoved(x + w / 2d, y + h / 2d); host.Input.PointerWheel(1000); }
            Settle(450); byte[] pixels = FinalCompositeCapture.Read(width, height);
            string path = Path.Combine(output, $"synthetic-{name}-{width}x{height}.ppm");
            using (var stream = File.Create(path))
            {
                stream.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC ADVENTURE SAVE UI - NO USER SAVE MUTATION\n{width} {height}\n255\n"));
                for (int row = height - 1; row >= 0; row--) stream.Write(pixels, row * width * 3, width * 3);
            }
            FinalCompositeCapture.WriteEvidence(path, width, height, "Actual native Adventure save management with synthetic slots and command authority. No save files/game assets/serializer/network; real byte-owner tested separately in temp tree.");
        }
    }
    private static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException(label); Console.WriteLine("RML ADVENTURE SAVE GL PASS " + label); }
    private static readonly string First = new('A', 64), Second = new('B', 64);
    private sealed class Services : IRmlUiMenuServices
    {
        internal RmlUiAdventureSlot[] Slots = { new(1, true, false, "SYNTHETIC ARCHIVES", 2, 79, 99, First),
            new(2, false, true, "", 0, 0, 0, Second), new(3, false, false, "", 0, 0, 0, "EMPTY") };
        internal readonly List<RmlUiAdventureSaveChange> Changes = new();
        internal bool Available = true;
        internal int Attempts, Launches;
        public bool CanManageAdventureSaves => Available;
        public RmlUiAdventureSlot[] ReadAdventureSlots() => Slots.ToArray();
        public bool ManageAdventureSave(RmlUiAdventureSaveChange change, out string status)
        {
            Attempts++; status = "SYNTHETIC SAVE CHANGE REFUSED // REFRESH";
            if (!Available || Slots[change.Source.Slot - 1] != change.Source || change.Destination is { } to && Slots[to.Slot - 1] != to) return false;
            Changes.Add(change);
            if (change.Destination is { } target) Slots[target.Slot - 1] = change.Source with { Slot = target.Slot };
            else Slots[change.Source.Slot - 1] = new(change.Source.Slot, false, false, "", 0, 0, 0, "EMPTY");
            status = "SYNTHETIC EXACT SAVE CHANGE VERIFIED"; return true;
        }
        public bool LaunchAdventure(RmlUiAdventureSlot slot, bool newGame, Hunter hunter) { Launches++; return false; }
        public HubSnapshot Hub() => new("SYNTHETIC SAVE FIXTURE", Hunter.Samus, Hunter.Samus, 0, false, "NO CARTRIDGE FILES");
        public string BuildVersion() => "synthetic-adventure-save";
        public RmlUiLobbyView Lobby() => new(false, false, false, false, false, false, false, "", "", "", "", "", 8, Array.Empty<RmlUiLobbyPlayer>());
        public Task<RmlUiDirectory> Browse(CancellationToken token) => Task.FromResult(new RmlUiDirectory(false, "SYNTHETIC", Array.Empty<RmlUiServerCandidate>()));
        public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token) => Task.FromResult<RmlUiServerCandidate?>(null);
        public Task<RmlUiJoinResult> Join(string endpoint, string player, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, new LaunchPlan(), "SYNTHETIC"));
        public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, new LaunchPlan(), "SYNTHETIC"));
        public bool AcceptJoin(LaunchPlan plan) => false;
        public bool OpenCreateLobby() => false;
        public bool NavigateTo(RmlUiRouteArgument route) => false;
        public bool OpenStudio() => false;
        public bool SetReady() => false;
        public bool StartMatch() => false;
        public bool LeaveLobby() => false;
        public bool NextHunter() => false;
        public bool NextSuit() => false;
        public bool OpenLobbyDetails() => false;
        public RmlUiRuleDraft? ReadRules() => null;
        public IReadOnlyList<string> BuiltInArenaKeys() => Array.Empty<string>();
        public bool SubmitRules(RmlUiRuleDraft draft, out string error) { error = "SYNTHETIC"; return false; }
        public void Quit() { }
    }
}
