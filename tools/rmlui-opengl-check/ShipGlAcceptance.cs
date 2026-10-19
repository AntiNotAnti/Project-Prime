using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MphRead;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Adventure;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static class ShipGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        AdapterChecks();
        string output = Path.GetFullPath(Path.Combine("artifacts", "rmlui-campaign-ship-rml50"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "README.txt"),
            "Actual native RmlUi GL gunship with production CurrentNativeCampaignShipBackend and injected SYNTHETIC save/hash/dock/plan owners plus SILENT audio. No user files, preferences, cartridge, audio device, scene or launch authority are loaded or changed. Plans are inspected and never executed. This establishes native presentation/adapter fences, not live campaign travel acceptance.\n");
        using var pages = new RmlUiPageManager(host);
        NativeCampaignShipPage page = null!;
        Owner owner = new();
        SilentAudio audio = new();
        host.Input.SetFramebufferScale(1, 1);
        try
        {
            Open(owner);
            var document = page!.Document;
            Check(host.FocusedElement() == "ship_open_map" && !page.Closed && !page.TryTakeEffect(out _),
                "native cockpit opens with launch-map focus and no effect");
            var semantic = NativeSemanticReadback.Read(host, document);
            Check(semantic.ContainsText("SAVE SLOT 02 // KANDEN") && semantic.ContainsText("CELESTIAL ARCHIVES // SHIP DOCKED")
                && semantic.ContainsText("OCTOLITHS 1/2 // ARTIFACTS 1/6"), "native cockpit displays actual detached dock totals and captured hunter");
            Check(audio.Sequence == CampaignAudioCatalog.Sequence(CampaignAudioPage.Dock, CampaignPlanet.CelestialArchives)
                && audio.Starts == 1 && audio.Ticks > 0, "existing dock audio is pumped through silent injected owner");
            Capture("cockpit", atOrigin: true);
            Click("ship_open_map", RmlUiIntentKind.CampaignShipAction, 0);
            Check(audio.Sequence == SeqId.SHIP && audio.Starts == 2 && audio.Stops == 1
                && owner.Creates.Count == 0, "native star map transitions existing audio without launching");
            for (int i = 0; i < 5; i++)
            {
                Click("ship_planet_" + i, RmlUiIntentKind.CampaignShipAction, i + 5);
                semantic = NativeSemanticReadback.Read(host, document);
                Check(semantic.ContainsText(CampaignNavigation.Get((CampaignPlanet)i).Name), "native selection presents real destination " + i);
                Check(host.FocusDocument(document, "ship_land") == (i < 2), "native Land availability follows actual saved unlock for planet " + i);
            }
            Capture("locked-oubliette", atOrigin: true);
            Check(!page.Handle(new(RmlUiIntentKind.CampaignShipAction, 10, document, ulong.MaxValue)),
                "slot-page open-ship argument cannot execute within existing ship page");
            Check(!page.Handle(new(RmlUiIntentKind.CampaignShipAction, 2, host.HomeDocument, ulong.MaxValue)),
                "wrong document cannot exit gunship");
            var modal = pages.OpenModal(new("synthetic-ship-fence", "pages/social/confirm.rml", "social_confirm_cancel"));
            Frame();
            Check(!page.Back() && !page.Handle(new(RmlUiIntentKind.CampaignShipAction, 2, document, ulong.MaxValue)),
                "foreground modal fences Back and delayed ship launch");
            pages.CloseModal();
            Check(!host.IsAlive(modal), "synthetic ship modal retires");
            pages.SetVisible(false); Frame();
            Check(!page.Back() && !page.Handle(new(RmlUiIntentKind.CampaignShipAction, 2, document, ulong.MaxValue)),
                "hidden ship cannot launch or close");
            pages.SetVisible(true); Frame();

            Click("ship_planet_0", RmlUiIntentKind.CampaignShipAction, 5);
            owner.Hash = Owner.HashB;
            Click("ship_land", RmlUiIntentKind.CampaignShipAction, 3, expectedAccepted: false);
            semantic = NativeSemanticReadback.Read(host, document);
            Check(owner.Creates.Count == 0 && !page.Closed && !page.TryTakeEffect(out _)
                && semantic.ContainsText("DEPARTURE UNAVAILABLE") && !semantic.ContainsText("DOCK AND SAVE VERIFIED")
                && !semantic.ContainsText("OCTOLITHS 1/2"), "changed SHA refuses native departure and removes old verified progress");
            Check(!host.FocusDocument(document, "ship_land"), "failed departure requires refresh before another native Land");
            Capture("changed-save-error", atOrigin: true);
            Click("ship_refresh", RmlUiIntentKind.CampaignShipAction, 4);
            Check(host.FocusDocument(document, "ship_land"), "explicit refresh captures new save hash and restores verified Land");
            owner.Dock = new(2, CampaignPlanet.Alinos);
            Click("ship_land", RmlUiIntentKind.CampaignShipAction, 3, expectedAccepted: false);
            Check(owner.Creates.Count == 0 && !page.Closed && !page.TryTakeEffect(out _),
                "same-slot replacement dock planet refuses native departure");
            owner.Dock = Owner.ExpectedDock;
            owner.Save!.Areas = 0x1ff; owner.Hash = Owner.HashC;
            Click("ship_refresh", RmlUiIntentKind.CampaignShipAction, 4);
            for (int i = 0; i < 5; i++)
            {
                Click("ship_planet_" + i, RmlUiIntentKind.CampaignShipAction, i + 5);
                Check(host.FocusDocument(document, "ship_land"), "actual saved all-sector unlock enables native planet " + i);
            }
            Capture("all-unlocked-oubliette", atOrigin: true);
            Click("ship_land", RmlUiIntentKind.CampaignShipAction, 3);
            Check(page.Closed && page.TryTakeEffect(out var effect) && effect.Launch && effect.Dock == Owner.Expected
                && effect.Fingerprint == Owner.HashC && effect.Plan.Kind == LaunchKind.Adventure && effect.Plan.SaveSlot == 2
                && effect.Plan.Hunter == Hunter.Kanden && effect.Plan.AdventureDestination == CampaignPlanet.Oubliette
                && !effect.Plan.NewGame && !effect.Plan.ExitShipToSurface && !page.TryTakeEffect(out _),
                "native landing transfers one exact captured slot/hunter/destination/SHA plan");
            Check(owner.Creates.Count == 1 && audio.Landings == 1 && !page.Back(),
                "successful native departure creates one synthetic plan and landing cue only");
            page.Dispose();
            Check(!host.IsAlive(document) && !page.Handle(new(RmlUiIntentKind.CampaignShipAction, 3, document, ulong.MaxValue)),
                "retired native ship rejects delayed departure");

            owner = new(); Open(owner); document = page.Document;
            Click("ship_exit", RmlUiIntentKind.CampaignShipAction, 2);
            Check(page.TryTakeEffect(out effect) && effect.Launch && effect.Fingerprint == Owner.HashA
                && effect.Plan.AdventureDestination == CampaignPlanet.CelestialArchives && effect.Plan.ExitShipToSurface
                && owner.Creates.Count == 1 && audio.Landings == 1 && !page.TryTakeEffect(out _),
                "native surface exit captures dock planet and skip-landing-film flag once");
            page.Dispose();

            owner = new(); Open(owner);
            Click("ship_open_map", RmlUiIntentKind.CampaignShipAction, 0);
            Check(page.Back() && !page.Closed && !page.TryTakeEffect(out _), "Back from native star map returns to cockpit");
            Check(page.Back() && page.Closed && !page.Back() && page.TryTakeEffect(out effect)
                && !effect.Launch && effect.Fingerprint.Length == 0 && effect.Dock == Owner.Expected && !page.TryTakeEffect(out _)
                && owner.Creates.Count == 0 && audio.Landings == 0,
                "cockpit Back transfers one safe file-select effect without creating a plan");
            page.Dispose();

            owner = new(); Open(owner);
            Click("ship_back", RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Adventure);
            Check(page.TryTakeEffect(out effect) && !effect.Launch && owner.Creates.Count == 0 && !page.TryTakeEffect(out _),
                "native file-select button returns once without mutation");
            page.Dispose();

            owner = new(); Open(owner); document = page.Document;
            pages.ClosePage(); page.Refresh();
            Check(audio.Stops == 1 && !page.Back() && !page.TryTakeEffect(out _)
                && !page.Handle(new(RmlUiIntentKind.CampaignShipAction, 2, document, ulong.MaxValue)),
                "external page retirement releases silent audio without inventing travel effect");
            Check(GL.GetError() == ErrorCode.NoError, "ship fixture leaves no OpenGL error");
            Console.WriteLine("RML CAMPAIGN SHIP GL synthetic-only screenshots: " + output);
        }
        finally { page?.Dispose(); }

        void Open(Owner current)
        {
            audio = new SilentAudio();
            var controller = new NativeCampaignShipController<LaunchPlan>(current.Backend(), Owner.Expected, (int)Hunter.Kanden);
            page = new NativeCampaignShipPage(host, pages, controller, audio); page.Open(); Settle(200);
        }
        void Frame()
        {
            page?.Refresh(); host.Update();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, width, height); GL.Disable(EnableCap.ScissorTest);
            GL.ClearColor(0.025f, 0.05f, 0.08f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle(int milliseconds)
        {
            var timer = Stopwatch.StartNew();
            do { Frame(); Thread.Sleep(10); } while (timer.ElapsedMilliseconds < milliseconds);
            Frame();
        }
        void Click(string id, RmlUiIntentKind kind, int argument, bool expectedAccepted = true)
        {
            Frame(); var target = pages.Top;
            Check(host.FocusDocument(target, id), "actual native focus reaches " + id); Settle(450);
            Check(host.TryGetElementBounds(target, id, out float x, out float y, out float w, out float h), "native ship bounds resolve " + id);
            double px = x + w / 2d, py = y + h / 2d;
            Check(px >= 0 && px < width && py >= 0 && py < height, "native ship button center is within framebuffer: " + id);
            host.Input.PointerButton(0, px, py, true); host.Input.PointerButton(0, px, py, false); host.Update();
            bool received = false;
            while (host.TryTakeIntent(out var intent))
            {
                bool accepted = page!.Handle(intent);
                if (intent.Kind != kind || intent.Argument != argument) continue;
                Check(accepted == expectedAccepted && intent.Document == target, "actual native pointer dispatches ship action " + id);
                Check(!page.Handle(intent), "duplicate native ship sequence rejected: " + id); received = true;
            }
            Check(received, "native ship button emits expected action: " + id); Settle(180);
        }
        void Capture(string name, bool atOrigin)
        {
            if (atOrigin) { host.Input.PointerMoved(width / 2d, height / 2d); host.Input.PointerWheel(1000); Settle(450); }
            else Settle(180);
            byte[] pixels = FinalCompositeCapture.Read(width, height);
            string path = Path.Combine(output, $"synthetic-{name}-{width}x{height}.ppm");
            using (var stream = File.Create(path))
            {
                stream.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC GUNSHIP OWNERS - SILENT AUDIO - ACTUAL NATIVE GL\n{width} {height}\n255\n"));
                for (int row = height - 1; row >= 0; row--) stream.Write(pixels, row * width * 3, width * 3);
            }
            FinalCompositeCapture.WriteEvidence(path, width, height, "Actual native ship page and production adapter with synthetic in-memory save/hash/dock/plan and silent audio; no user I/O/assets/scene/device.");
        }
    }

    private static void AdapterChecks()
    {
        var owner = new Owner(); var backend = owner.Backend();
        var read = backend.Read(Owner.Expected);
        Check(read.Error.Length == 0 && read.Fingerprint == Owner.HashA && read.Destinations.Length == 5
            && read.Destinations[0].Unlocked && read.Destinations[1].Unlocked && !read.Destinations[4].Unlocked,
            "production adapter projects actual five-sector unlocks from injected save");
        Check(read.Destinations[1].Octoliths == 1 && read.Destinations[1].Artifacts == 1,
            "production adapter preserves actual saved per-sector progress");
        Check(backend.TryCreateLaunch(Request(0), out var plan, out _) && plan.AdventureDestination == CampaignPlanet.Alinos
            && plan.SaveSlot == 2 && plan.Hunter == Hunter.Kanden && !plan.ExitShipToSurface && owner.Creates.Count == 1,
            "production adapter authorizes unlocked landing through injected existing plan owner");
        Check(owner.Save!.CheckpointRoomId == -1 && owner.Save.CheckpointEntityId == -1,
            "adapter does not prepare or mutate the synthetic save checkpoint");
        Check(!backend.TryCreateLaunch(Request(4), out _, out _) && !backend.TryCreateLaunch(Request(0, exit: true), out _, out _)
            && owner.Creates.Count == 1, "real unlock and surface-destination owner checks reject forged requests");
        Check(backend.TryCreateLaunch(Request(1, exit: true), out plan, out _) && plan.ExitShipToSurface
            && plan.AdventureDestination == CampaignPlanet.CelestialArchives, "surface exit reuses exact docked destination");

        foreach (string hash in new[] { Owner.HashB, "EMPTY", "UNAVAILABLE" })
        {
            owner = new Owner { Hash = hash }; backend = owner.Backend();
            Check(!backend.TryCreateLaunch(Request(0), out _, out _) && owner.Creates.Count == 0,
                "production adapter rejects changed, deleted or unavailable SHA before plan creation");
        }
        foreach (var dock in new CampaignDockReceipt?[] { null, new(1, CampaignPlanet.CelestialArchives), new(2, CampaignPlanet.Alinos) })
        {
            owner = new Owner { Dock = dock }; backend = owner.Backend();
            Check(backend.Read(Owner.Expected).Error.Length > 0 && !backend.TryCreateLaunch(Request(0), out _, out _)
                && owner.Creates.Count == 0, "production adapter compares full slot and planet dock receipt");
        }
        owner = new Owner(); backend = owner.Backend();
        Check(backend.Read(new(0, 1)).Error.Length > 0 && owner.HashReads == 0 && owner.SaveReads == 0,
            "invalid slot is rejected before touching any save path owner");
        foreach (bool changeDock in new[] { false, true })
        {
            owner = new Owner(); var captured = owner;
            owner.OnRead = () => { if (changeDock) captured.Dock = new(2, CampaignPlanet.Alinos); else captured.Hash = Owner.HashB; };
            backend = owner.Backend();
            Check(backend.Read(Owner.Expected).Error.Length > 0 && owner.Creates.Count == 0,
                "display projection rejects dock or SHA changed during save read");
            owner = new Owner(); captured = owner;
            owner.OnRead = () => { if (changeDock) captured.Dock = new(2, CampaignPlanet.Alinos); else captured.Hash = Owner.HashB; };
            Check(!owner.Backend().TryCreateLaunch(Request(0), out _, out _) && owner.Creates.Count == 0,
                "launch rechecks full dock or SHA immediately after fresh save read");
        }
        owner = new Owner(); var beforePlan = owner;
        owner.OnHash = count => { if (count == 3) beforePlan.Hash = Owner.HashB; };
        Check(!owner.Backend().TryCreateLaunch(Request(0), out _, out _) && owner.Creates.Count == 0,
            "SHA rechecked immediately before existing plan creation");
        foreach (bool changeDock in new[] { false, true })
        {
            owner = new Owner(); var captured = owner;
            owner.OnCreate = () => { if (changeDock) captured.Dock = new(2, CampaignPlanet.Alinos); else captured.Hash = Owner.HashB; };
            Check(!owner.Backend().TryCreateLaunch(Request(0), out plan, out _) && plan.Kind == default
                && owner.Creates.Count == 1, "post-create witness change discards candidate plan before publication");
        }
        owner = new Owner { Ready = false };
        Check(!owner.Backend().TryCreateLaunch(Request(0), out _, out _) && owner.Creates.Count == 0,
            "existing game-file readiness remains required for departure");
        owner = new Owner { Save = null };
        Check(owner.Backend().Read(Owner.Expected).Error.Length > 0
            && !owner.Backend().TryCreateLaunch(Request(0), out _, out _) && owner.Creates.Count == 0,
            "missing fresh save cannot be converted into a departure");
        owner = new Owner { OnRead = () => throw new IOException("SYNTHETIC PRIVATE PATH /private/fixture") };
        read = owner.Backend().Read(Owner.Expected);
        Check(read.Error.Length > 0 && !read.Error.Contains("PRIVATE") && !read.Error.Contains("/private"),
            "read failure is redacted instead of leaking local paths");
        owner = new Owner(); owner.Save!.Areas = 0x1ff; backend = owner.Backend();
        for (int i = 0; i < 5; i++) Check(backend.TryCreateLaunch(Request(i), out plan, out _)
            && plan.AdventureDestination == (CampaignPlanet)i, "all five real saved unlocks authorize correct landing " + i);

        static NativeCampaignShipRequest Request(int destination, bool exit = false)
            => new(new(Owner.Expected, Owner.HashA, (int)Hunter.Kanden), destination, exit);
    }

    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("RML CAMPAIGN SHIP GL PASS " + label); }

    private sealed class Owner
    {
        internal const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        internal const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        internal const string HashC = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
        internal static readonly CampaignDockReceipt ExpectedDock = new(2, CampaignPlanet.CelestialArchives);
        internal static readonly NativeCampaignShipDock Expected = new(2, (int)CampaignPlanet.CelestialArchives);
        internal StorySave? Save = CampaignGlFixture.Save();
        internal string Hash = HashA;
        internal CampaignDockReceipt? Dock = ExpectedDock;
        internal bool Ready = true;
        internal Action? OnRead, OnCreate;
        internal Action<int>? OnHash;
        internal int HashReads, SaveReads;
        internal readonly List<NativeCampaignShipRequest> Creates = new();
        internal Owner()
        {
            Save!.Areas = 0xf; Save.FoundOctoliths = 0x5; Save.Artifacts = 0x41;
            Save.SetVisitedRoom(CampaignNavigation.Get(CampaignPlanet.CelestialArchives).LandingRoomId);
        }
        internal CurrentNativeCampaignShipBackend Backend() => new(slot =>
        {
            if (slot != 2) throw new InvalidOperationException("unexpected fixture slot");
            SaveReads++; OnRead?.Invoke(); return Save;
        }, slot =>
        {
            if (slot != 2) throw new InvalidOperationException("unexpected fixture slot");
            OnHash?.Invoke(++HashReads); return Hash;
        }, () => Dock, request =>
        {
            Creates.Add(request);
            var plan = new LaunchPlan { Kind = LaunchKind.Adventure, SaveSlot = request.Witness.Dock.Slot,
                NewGame = false, Hunter = (Hunter)request.Witness.Hunter, AdventureDestination = (CampaignPlanet)request.Destination,
                ExitShipToSurface = request.ExitShipToSurface, PlayerName = "SYNTHETIC SHIP", RoomKey = "" };
            OnCreate?.Invoke(); return plan;
        }, () => Ready);
    }

    private sealed class SilentAudio : ICampaignAudioSink
    {
        internal int Starts, Ticks, Stops, Landings;
        internal SeqId Sequence;
        private long _epoch;
        public long Start(SeqId sequence) { Sequence = sequence; return _epoch = ++Starts; }
        public bool Owns(SeqId sequence, long epoch) => epoch != 0 && epoch == _epoch && sequence == Sequence;
        public void Tick() => Ticks++;
        public void Stop(SeqId sequence, long epoch)
        { if (!Owns(sequence, epoch)) throw new InvalidOperationException("silent ship audio witness mismatch"); Stops++; _epoch = 0; }
        public void Cue(MphRead.Mods.Sound.UiFeedbackCue cue)
        { if (cue == MphRead.Mods.Sound.UiFeedbackCue.CampaignLand) Landings++; }
    }
}
