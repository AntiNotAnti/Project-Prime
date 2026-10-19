using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Adventure;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static class EndingGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Path.Combine("artifacts", "rmlui-campaign-ending-rml49"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "README.txt"),
            "Actual native RmlUi framebuffer with SYNTHETIC in-memory StorySave receipts and an injected SILENT audio sink. No user save, game assets, audio device, scene or campaign authority is loaded or mutated. Capturing a synthetic ending does not establish live campaign completion acceptance.\n");
        using var pages = new RmlUiPageManager(host);
        NativeCampaignEndingPage? page = null;
        host.Input.SetFramebufferScale(1, 1);
        try
        {
            var story = CampaignGlFixture.Save();
            story.FoundOctoliths = 0xff;
            story.Stats.Deaths = 12; story.Stats.HunterKills = 34; story.Stats.EnemyKills = 567;
            CampaignEndingSummary complete = CampaignEndingSummary.Capture(2,
                CampaignNavigation.GoreaSecondRoomId, story, _ => 93)!.Value with { SaveCommitted = true };
            var audio = new SilentAudio();
            page = new NativeCampaignEndingPage(host, pages, complete, audio);
            page.Open();
            var document = page.Document;
            story.Stats.Deaths = 999; story.Stats.EnemyKills = 999; story.FoundOctoliths = 0;
            Settle(200);
            Check(page.Receipt == complete && !page.Closed && !page.TryTakeReturn(out _),
                "opening presents a detached receipt without returning or retaining mutable save data");
            Check(host.FocusedElement() == "campaign_ending_continue", "ending opens with native return focus");
            var semantic = NativeSemanticReadback.Read(host, document);
            Check(semantic.Label("campaign_ending_heading") == complete.Heading
                && semantic.Label("campaign_ending_result") == complete.GoreaResult,
                "native heading and final encounter use captured ending kind");
            Check(semantic.ContainsText("SAVE SLOT 02") && semantic.ContainsText("SAVE COMMITTED"),
                "native slot and save result use captured receipt");
            Check(semantic.ContainsText("COMPLETION  93%\nOCTOLITHS  8/8\nDEATHS  12\nHUNTER KILLS  34\nENEMY KILLS  567")
                && !semantic.ContainsText("999"),
                "all native totals match exact capture despite later synthetic save mutation");
            Check(semantic.ContainsText(Credits.Summary) && semantic.ContainsText(Credits.Author)
                && semantic.ContainsText(Credits.Foundation.What) && semantic.ContainsText("DB-IP"),
                "native credits include current shared attribution without truncated fixed rows");
            Check(audio.Starts == 1 && audio.Sequence == SeqId.CREDITS && audio.Ticks > 0 && audio.Cues == 1,
                "existing ending audio lifecycle is pumped through injected silent sink only");
            Capture("complete-summary");
            host.Input.PointerMoved(width / 2d, height * 0.7d);
            host.Input.PointerWheel(-1000); Settle(450);
            Capture("complete-credits");

            Check(!page.Handle(new(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Adventure,
                host.HomeDocument, ulong.MaxValue)), "wrong document cannot consume ending receipt");
            Check(!page.Handle(new(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Settings,
                document, ulong.MaxValue)), "unrelated route cannot consume ending receipt");
            var modal = pages.OpenModal(new("synthetic-ending-fence", "pages/social/confirm.rml", "social_confirm_cancel"));
            Frame();
            Check(!page.Back() && !page.Handle(new(RmlUiIntentKind.Navigate,
                (int)RmlUiRouteArgument.Adventure, document, ulong.MaxValue)),
                "foreground modal fences both Back and delayed native return");
            pages.CloseModal();
            Check(!host.IsAlive(modal), "synthetic ending modal retires");
            pages.SetVisible(false); Frame();
            Check(!page.Back() && !page.Handle(new(RmlUiIntentKind.Navigate,
                (int)RmlUiRouteArgument.Adventure, document, ulong.MaxValue)), "hidden ending cannot navigate");
            pages.SetVisible(true); Frame();
            ClickReturn();
            Check(page.Closed && audio.Stops == 1, "actual native return closes ending and stops only injected audio");
            Check(page.TryTakeReturn(out var returned) && returned == complete && !page.TryTakeReturn(out _),
                "native pointer yields original immutable receipt exactly once");
            Check(!page.Back() && !host.FocusDocument(document, "campaign_ending_continue"),
                "closed return is disabled and Back cannot recreate consumed command");
            page.Dispose();
            Check(audio.Stops == 1 && !host.IsAlive(document)
                && !page.Handle(new(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Adventure, document, ulong.MaxValue)),
                "disposal and retired intent neither replay return nor stop unrelated audio");

            var unfinishedSave = CampaignGlFixture.Save();
            unfinishedSave.FoundOctoliths = 0x3f;
            unfinishedSave.Stats.Deaths = 42;
            CampaignEndingSummary incomplete = CampaignEndingSummary.Capture(3,
                CampaignNavigation.GoreaFirstRoomId, unfinishedSave,
                _ => throw new InvalidOperationException("synthetic unavailable completion"))!.Value;
            var incompleteAudio = new SilentAudio();
            page = new NativeCampaignEndingPage(host, pages, incomplete, incompleteAudio);
            page.Open(); Settle(200);
            semantic = NativeSemanticReadback.Read(host, page.Document);
            Check(semantic.Label("campaign_ending_heading") == incomplete.Heading
                && semantic.Label("campaign_ending_result") == "GOREA BATTLE INCOMPLETE"
                && semantic.ContainsText("SAVE NOT REQUESTED"),
                "unfinished capture preserves actual encounter and uncommitted save status");
            Check(semantic.ContainsText("COMPLETION  NOT AVAILABLE\nOCTOLITHS  6/8\nDEATHS  42\nHUNTER KILLS  0\nENEMY KILLS  0"),
                "unavailable completion is explicit and missing activity is not invented");
            Capture("incomplete-unknown-completion");
            Check(page.Back() && !page.Back() && page.TryTakeReturn(out returned)
                && returned == incomplete && !page.TryTakeReturn(out _),
                "Back transfers exact incomplete receipt once");
            Check(incompleteAudio.Stops == 1, "Back releases injected ending audio once");
            page.Dispose();

            var retiredAudio = new SilentAudio();
            page = new NativeCampaignEndingPage(host, pages, complete, retiredAudio);
            page.Open(); Frame();
            var retiredDocument = page.Document;
            pages.ClosePage(); page.Refresh();
            Check(retiredAudio.Stops == 1 && !page.Back() && !page.TryTakeReturn(out _)
                && !page.Handle(new(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Adventure, retiredDocument, ulong.MaxValue)),
                "external retirement releases audio without synthesizing a return command");
            Check(GL.GetError() == ErrorCode.NoError, "ending fixture leaves no GL error");
            Console.WriteLine("RML CAMPAIGN ENDING GL synthetic-only screenshots: " + output);
        }
        finally { page?.Dispose(); }

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
        void ClickReturn()
        {
            var document = page!.Document;
            Check(host.FocusDocument(document, "campaign_ending_continue"), "native return button remains focusable above scrolled credits");
            Settle(450);
            Check(host.TryGetElementBounds(document, "campaign_ending_continue", out float x, out float y, out float w, out float h),
                "native return bounds resolve");
            double px = x + w / 2d, py = y + h / 2d;
            Check(px >= 0 && px < width && py >= 0 && py < height, "native return center is within framebuffer");
            host.Input.PointerButton(0, px, py, true); host.Input.PointerButton(0, px, py, false); host.Update();
            bool received = false;
            while (host.TryTakeIntent(out var intent))
            {
                bool accepted = page.Handle(intent);
                if (intent.Kind != RmlUiIntentKind.Navigate || intent.Argument != (int)RmlUiRouteArgument.Adventure) continue;
                Check(accepted && intent.Document == document, "actual native pointer returns from captured ending");
                Check(!page.Handle(intent), "duplicate native ending sequence is rejected");
                received = true;
            }
            Check(received, "native return emits expected stable Adventure route"); Settle(180);
        }
        void Capture(string name)
        {
            Settle(180);
            byte[] pixels = FinalCompositeCapture.Read(width, height);
            string path = Path.Combine(output, $"synthetic-{name}-{width}x{height}.ppm");
            using (var stream = File.Create(path))
            {
                stream.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC ENDING RECEIPT - SILENT AUDIO - ACTUAL NATIVE GL\n{width} {height}\n255\n"));
                for (int row = height - 1; row >= 0; row--) stream.Write(pixels, row * width * 3, width * 3);
            }
            FinalCompositeCapture.WriteEvidence(path, width, height,
                "Actual native ending page; synthetic captured in-memory StorySave receipt and injected silent audio. No scene/save/device/game assets.");
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("RML CAMPAIGN ENDING GL PASS " + label);
    }

    private sealed class SilentAudio : ICampaignAudioSink
    {
        internal int Starts, Ticks, Stops, Cues;
        internal SeqId Sequence;
        private long _epoch;
        public long Start(SeqId sequence) { Starts++; Sequence = sequence; return _epoch = Starts; }
        public bool Owns(SeqId sequence, long epoch) => epoch != 0 && epoch == _epoch && sequence == Sequence;
        public void Tick() => Ticks++;
        public void Stop(SeqId sequence, long epoch)
        { if (!Owns(sequence, epoch)) throw new InvalidOperationException("silent audio witness mismatch"); Stops++; _epoch = 0; }
        public void Cue(MphRead.Mods.Sound.UiFeedbackCue cue)
        { if (cue != MphRead.Mods.Sound.UiFeedbackCue.CampaignEnding) throw new InvalidOperationException("wrong ending cue"); Cues++; }
    }
}
