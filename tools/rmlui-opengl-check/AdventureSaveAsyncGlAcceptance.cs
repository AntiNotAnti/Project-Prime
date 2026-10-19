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
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static class AdventureSaveAsyncGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Path.Combine("artifacts", "rmlui-adventure-save-work-rml59"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "README.txt"), "Actual native save-management pending/cancel journey with SYNTHETIC immutable slots and queued in-memory authority. No Android dispatcher, GameView, save files, serializer, assets, scenes, audio or network. Separate source/temp-file tests cover dispatch/bytes.\n");
        var services = new Services();
        using var controller = new RmlUiMenuController(services);
        using var manager = new RmlUiPageManager(host);
        using var pages = new RmlUiLauncherPages(host, manager, RmlUiLauncherCapabilities.ConfiguredLocalAdventureSaves);
        host.Input.SetFramebufferScale(1, 1);
        long presented = 0;
        Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Adventure), "native async save route opens through current controller");
        Frame(); var parent = pages.Document;
        Click("adventure_copy", RmlUiIntentKind.AdventureCopy); var confirmation = manager.Top;
        Click("adventure_confirm_copy", RmlUiIntentKind.AdventureConfirmCopy);
        Check(manager.Top == parent && !host.IsAlive(confirmation) && controller.Busy && services.Writes == 0
            && host.FocusedElement() == "adventure_cancel_save_work"
            && NativeSemanticReadback.Read(host, parent).ContainsText("SAVE CHANGE PENDING"),
            "actual confirmation consumes proposal and exposes pending Cancel without optimistic write");
        Check(!host.FocusDocument(parent, "adventure_slot2") && !host.FocusDocument(parent, "adventure_next_hunter")
            && !host.FocusDocument(parent, "adventure_copy"), "busy native bindings remove new source/hunter/write controls from focus");
        Capture("pending-copy");
        var covering = manager.OpenModal(new("synthetic-save-work-cover", "pages/social/confirm.rml", "social_confirm_cancel")); Frame();
        Check(!Forward(new(RmlUiIntentKind.AdventureCancelSaveChange, 0, parent, ulong.MaxValue)),
            "unrelated foreground prevents background pending save cancellation");
        manager.CloseModal(); Check(!host.IsAlive(covering), "synthetic foreground retires");
        Click("adventure_cancel_save_work", RmlUiIntentKind.AdventureCancelSaveChange);
        Check(services.Token.IsCancellationRequested && !controller.Busy && services.Writes == 0
            && NativeSemanticReadback.Read(host, parent).ContainsText("A COMMITTED WRITE IS NOT UNDONE"),
            "actual Cancel retires queued work and makes no rollback claim");
        services.Complete(); Settle(180);
        Check(services.Writes == 0 && !Forward(new(RmlUiIntentKind.AdventureConfirmCopy, 0, confirmation, ulong.MaxValue)),
            "late queued callback and retired confirmation cannot write or reopen proposal");
        Capture("cancelled-copy");
        Click("adventure_copy", RmlUiIntentKind.AdventureCopy);
        Click("adventure_confirm_copy", RmlUiIntentKind.AdventureConfirmCopy);
        services.Complete(); Settle(180);
        Check(!controller.Busy && services.Writes == 1 && services.Slots[1].Fingerprint == Hash
            && NativeSemanticReadback.Read(host, parent).ContainsText("SYNTHETIC EXACT SAVE CHANGE VERIFIED"),
            "fresh completed owner result and exact readback alone publish verified copy");
        Click("adventure_slot2", RmlUiIntentKind.AdventureSelectSlot, 2);
        Click("adventure_archive", RmlUiIntentKind.AdventureArchive);
        Click("adventure_confirm_archive", RmlUiIntentKind.AdventureConfirmArchive);
        services.Complete(refuse: true); Settle(180);
        Check(!controller.Busy && services.Writes == 1 && services.Slots[1].Used
            && NativeSemanticReadback.Read(host, parent).ContainsText("SYNTHETIC OWNER REFUSED"),
            "actual pending owner refusal retains save and enables explicit retry");
        Capture("archive-refused");
        Click("adventure_archive", RmlUiIntentKind.AdventureArchive);
        Click("adventure_confirm_archive", RmlUiIntentKind.AdventureConfirmArchive);
        Check(controller.Dispatch(RmlUiIntentKind.AdventureRefresh) && services.Token.IsCancellationRequested,
            "explicit refresh cancels current async save epoch");
        services.Complete(); Settle(180);
        Check(services.Writes == 1 && services.Slots[1].Used && !controller.Busy, "refresh retired queued writer produces no effect");
        Click("adventure_archive", RmlUiIntentKind.AdventureArchive);
        Click("adventure_confirm_archive", RmlUiIntentKind.AdventureConfirmArchive);
        services.Complete(); Settle(180);
        Check(services.Writes == 2 && services.Slots[1].Fingerprint == "EMPTY"
            && host.FocusedElement() == "adventure_slot2", "verified async Archive returns native focus to its actual empty selected slot");
        Capture("archive-verified");
        Click("adventure_slot1", RmlUiIntentKind.AdventureSelectSlot, 1);
        Click("adventure_copy", RmlUiIntentKind.AdventureCopy);
        Click("adventure_confirm_copy", RmlUiIntentKind.AdventureConfirmCopy);
        Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home) && services.Token.IsCancellationRequested,
            "route retirement cancels queued UI command");
        services.Complete(); Settle(180);
        Check(services.Writes == 2 && !host.IsAlive(parent) && services.Launches == 0,
            "retired native route cannot mutate saves or start gameplay");
        Check(GL.GetError() == ErrorCode.NoError, "async save native journey completes without GL errors");
        Console.WriteLine("RML ADVENTURE ASYNC GL screenshots: " + output);

        bool Forward(in RmlUiIntent intent)
            => pages.HandleIntent(intent, out var forwarded) && forwarded.Kind != 0 && controller.Dispatch(forwarded.Kind, forwarded.Argument);
        void Frame()
        {
            var snapshot = controller.Tick();
            if (snapshot.Revision > presented)
            {
                foreach (var binding in snapshot.Bindings)
                    if (binding.Value.Kind == RmlUiBindingKind.Text) pages.SetText(binding.Key, binding.Value.Text);
                    else pages.SetBool(binding.Key, binding.Value.Boolean);
                presented = snapshot.Revision;
            }
            pages.Flush(); host.Update(); pages.AfterUpdate();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height); GL.Disable(EnableCap.ScissorTest);
            GL.ClearColor(.025f, .05f, .08f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle(int milliseconds)
        { var timer = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(10); } while (timer.ElapsedMilliseconds < milliseconds); Frame(); }
        void Click(string id, RmlUiIntentKind kind, int argument = 0)
        {
            Frame(); var target = manager.Top;
            Check(host.FocusDocument(target, id), "actual native focus reaches " + id); Settle(450);
            Check(host.TryGetElementBounds(target, id, out float x, out float y, out float w, out float h), "native bounds resolve " + id);
            double px = x + w / 2d, py = y + h / 2d;
            Check(px >= 0 && py >= 0 && px < width && py < height, "async save action center remains inside framebuffer " + id);
            host.Input.PointerMoved(px, py); Frame(); string hovered = host.HoveredElement();
            bool label = id is "adventure_slot1" or "adventure_slot2" or "adventure_slot3" && hovered == id + "_label"
                && host.TryGetElementBounds(target, hovered, out float lx, out float ly, out float lw, out float lh)
                && lw > 0 && lh > 0 && lx >= x && ly >= y && lx + lw <= x + w && ly + lh <= y + h
                && px >= lx && px <= lx + lw && py >= ly && py <= ly + lh;
            Check(hovered == id || label, "native hover matches action or contained authored label " + id);
            host.Input.PointerButton(0, px, py, true); host.Input.PointerButton(0, px, py, false); host.Update();
            bool received = false;
            while (host.TryTakeIntent(out var intent))
            {
                bool accepted = Forward(intent);
                if (intent.Kind != kind || intent.Argument != argument) continue;
                Check(accepted && intent.Document == target, "actual pointer dispatches exact save command " + id);
                Check(!Forward(intent), "duplicate pointer sequence cannot repeat save command " + id); received = true;
            }
            Check(received, "native action emits expected intent " + id); Settle(180);
        }
        void Capture(string name)
        {
            Settle(450); byte[] pixels = FinalCompositeCapture.Read(width, height);
            string path = Path.Combine(output, $"synthetic-{name}-{width}x{height}.ppm");
            using (var stream = File.Create(path))
            {
                stream.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC ASYNC ADVENTURE SAVE UI - NO USER FILES\n{width} {height}\n255\n"));
                for (int row = height - 1; row >= 0; row--) stream.Write(pixels, row * width * 3, width * 3);
            }
            FinalCompositeCapture.WriteEvidence(path, width, height, "Actual native pending/cancel UI with synthetic slots and queued memory writer. No Android runtime, files, serializer, assets or network.");
        }
    }
    private static void Check(bool ok, string label)
    { if (!ok) throw new InvalidOperationException(label); Console.WriteLine("RML ADVENTURE ASYNC GL PASS " + label); }
    private static readonly string Hash = new('A', 64);
    private sealed class Services : IRmlUiMenuServices
    {
        internal RmlUiAdventureSlot[] Slots = { new(1, true, false, "SYNTHETIC ARCHIVES", 2, 79, 99, Hash),
            new(2, false, false, "", 0, 0, 0, "EMPTY"), new(3, false, true, "", 0, 0, 0, new string('B', 64)) };
        private TaskCompletionSource<RmlUiAdventureSaveResult>? _pending;
        private RmlUiAdventureSaveChange _change;
        internal CancellationToken Token;
        internal int Writes, Launches;
        public bool CanManageAdventureSaves => true;
        public bool AdventureSaveChangesAreAsync => true;
        public RmlUiAdventureSlot[] ReadAdventureSlots() => Slots.ToArray();
        public Task<RmlUiAdventureSaveResult> ManageAdventureSaveAsync(RmlUiAdventureSaveChange change, CancellationToken token)
        { _change = change; Token = token; _pending = new(TaskCreationOptions.RunContinuationsAsynchronously); return _pending.Task; }
        internal void Complete(bool refuse = false)
        {
            var completion = _pending ?? throw new InvalidOperationException("No synthetic queued writer."); _pending = null;
            bool success = !refuse && !Token.IsCancellationRequested && Slots[_change.Source.Slot - 1] == _change.Source
                && (_change.Destination is not { } destination || Slots[destination.Slot - 1] == destination);
            if (success)
            {
                Writes++;
                if (_change.Destination is { } target) Slots[target.Slot - 1] = _change.Source with { Slot = target.Slot };
                else Slots[_change.Source.Slot - 1] = new(_change.Source.Slot, false, false, "", 0, 0, 0, "EMPTY");
            }
            completion.SetResult(new(_change, success, success ? "SYNTHETIC EXACT SAVE CHANGE VERIFIED" : "SYNTHETIC OWNER REFUSED"));
        }
        public bool LaunchAdventure(RmlUiAdventureSlot slot, bool fresh, Hunter hunter) { Launches++; return false; }
        public HubSnapshot Hub() => new("SYNTHETIC ASYNC SAVE", Hunter.Samus, Hunter.Samus, 0, false, "NO CARTRIDGE FILES");
        public string BuildVersion() => "synthetic-save-work";
        public RmlUiLobbyView Lobby() => new(false, false, false, false, false, false, false, "", "", "", "", "", 8, Array.Empty<RmlUiLobbyPlayer>());
        public Task<RmlUiDirectory> Browse(CancellationToken token) => Task.FromResult(new RmlUiDirectory(false, "SYNTHETIC", Array.Empty<RmlUiServerCandidate>()));
        public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token) => Task.FromResult<RmlUiServerCandidate?>(null);
        public Task<RmlUiJoinResult> Join(string endpoint, string player, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, default, "SYNTHETIC"));
        public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, default, "SYNTHETIC"));
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
