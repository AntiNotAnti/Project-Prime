using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static class PartyQueueGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Path.Combine("artifacts", "rmlui-party-queue-rml58"));
        Directory.CreateDirectory(output);
        using var pages = new RmlUiPageManager(host);
        using var launcher = new RmlUiLauncherPages(host, pages);
        host.Input.SetFramebufferScale(1, 1);
        var play = launcher.ShowBaseline(RmlUiMenuPage.Play);
        launcher.SetBool("party_queue_open", true);
        launcher.SetBool("party_queue_can_accept", false);
        launcher.SetBool("party_queue_can_decline", false);
        launcher.SetBool("party_queue_can_leave", true);
        launcher.SetBool("party_queue_offer", false);
        launcher.SetText("party_queue_phase", "VERIFYING");
        launcher.SetText("party_queue_status", "VERIFYING THE CURRENT PARTY AND DESTINATION");
        launcher.SetText("party_queue_position", "SERVER VERIFICATION IN PROGRESS");
        launcher.SetText("party_queue_seats", "NO SEAT RESERVED");
        Frame(); var queue = pages.Top;
        Check(queue != play && host.CurrentInputDocument == queue && host.FocusedElement() == "play_queue_leave",
            "actual queue modal owns input above the existing joining route");
        Check(!host.FocusDocument(queue, "play_queue_accept") && !host.FocusDocument(queue, "play_queue_decline"),
            "unverified states offer no false admission or decline action");
        var background = new RmlUiIntent(RmlUiIntentKind.PlayCancel, 0, play, 1);
        Check(launcher.HandleIntent(background, out var ignored) && ignored.Kind == 0,
            "background Play cannot cancel a foreground reservation");
        Capture("verifying");

        launcher.SetText("party_queue_phase", "RESERVED");
        launcher.SetText("party_queue_status", "THE SERVER HOLDS TWO SEATS FOR THIS PARTY");
        launcher.SetText("party_queue_position", "WAITLIST 1 / 3");
        launcher.SetText("party_queue_seats", "2 SEATS RESERVED // ADMISSION STILL REQUIRED");
        launcher.SetText("party_queue_seconds", "10");
        launcher.SetBool("party_queue_offer", true);
        launcher.SetBool("party_queue_can_accept", true);
        launcher.SetBool("party_queue_can_decline", true);
        Frame();
        var text = NativeSemanticReadback.Read(host, queue);
        Check(text.ContainsText("RESERVED") && text.ContainsText("ADMISSION STILL REQUIRED"),
            "actual native semantics distinguish a reserved seat from admitted session");
        Click("play_queue_accept", 1);
        Click("play_queue_decline", 2);
        Check(launcher.Back(out var back) && back.Kind == RmlUiIntentKind.PlayQueueAction
            && back.Argument == 3 && back.Document == queue, "native Back requests Leave on the exact queue");
        var actions = new List<(float X, float Y, float W, float H)>();
        foreach (string id in new[] { "play_queue_accept", "play_queue_decline", "play_queue_leave" })
        {
            Check(host.FocusDocument(queue, id), "current queue action is keyboard reachable: " + id);
            Frame(); var box = Bounds(id);
            Check(box.X >= -1 && box.Y >= -1 && box.X + box.W <= width + 1 && box.Y + box.H <= height + 1,
                "focus scroll reveals the complete action within the framebuffer: " + id);
            actions.Add(box);
        }
        Capture("reserved");
        launcher.SetText("party_queue_phase", "ADMITTING");
        launcher.SetText("party_queue_status", "WAITING FOR THE SERVER'S ACTUAL WELCOME");
        launcher.SetBool("party_queue_can_accept", false);
        launcher.SetBool("party_queue_can_decline", false);
        Frame();
        Check(!host.FocusDocument(queue, "play_queue_accept") && !host.FocusDocument(queue, "play_queue_decline")
            && NativeSemanticReadback.Read(host, queue).ContainsText("ADMITTING"),
            "an admission claim retires consent controls while awaiting the existing join owner");
        Capture("admitting");
        Click("play_queue_leave", 3);
        launcher.SetBool("party_queue_open", false);
        Frame();
        Check(!host.IsAlive(queue) && pages.Top == play && host.CurrentInputDocument == play,
            "settled queue retires only its modal and restores the existing route");
        Check(!pages.Accept(new(RmlUiIntentKind.PlayQueueAction, 1, queue, ulong.MaxValue)),
            "late retired-document admission cannot affect another session");
        Check(GL.GetError() == ErrorCode.NoError, "actual queue UI leaves no GL error");

        void Frame()
        {
            launcher.Flush(); host.Update(); launcher.AfterUpdate();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.ClearColor(.2f, .3f, .4f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        (float X, float Y, float W, float H) Bounds(string id)
        {
            Check(host.TryGetElementBounds(queue, id, out float x, out float y, out float w, out float h)
                && w > 0 && h > 0, "actual native bounds: " + id);
            return (x, y, w, h);
        }
        void Click(string id, int action)
        {
            Check(host.FocusDocument(queue, id), "actual native focus: " + id); Frame();
            var box = Bounds(id); host.DiscardIntents();
            host.Input.PointerButton(0, box.X + box.W / 2, box.Y + box.H / 2, true);
            host.Input.PointerButton(0, box.X + box.W / 2, box.Y + box.H / 2, false); host.Update();
            bool received = false;
            while (host.TryTakeIntent(out var intent))
            {
                if (intent.Kind != RmlUiIntentKind.PlayQueueAction || intent.Argument != action) continue;
                Check(launcher.HandleIntent(intent, out var forwarded) && forwarded == intent && intent.Document == queue,
                    "actual pointer command uses the exact queue document: " + id);
                Check(launcher.HandleIntent(intent, out var duplicate) && duplicate.Kind == 0,
                    "duplicate pointer sequence is rejected: " + id); received = true;
            }
            Check(received, "native pointer emitted the expected finite queue action: " + id);
        }
        void Capture(string phase)
        {
            Frame(); byte[] pixels = FinalCompositeCapture.Read(width, height);
            string path = Path.Combine(output, $"synthetic-party-queue-{phase}-{width}x{height}.ppm");
            using (var stream = File.Create(path))
            {
                stream.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC PARTY QUEUE UI - NO NETWORK OR ADMISSION\n{width} {height}\n255\n"));
                for (int row = height - 1; row >= 0; row--) stream.Write(pixels, row * width * 3, width * 3);
            }
            FinalCompositeCapture.WriteEvidence(path, width, height,
                "Synthetic queue states through actual native composed documents and pointer intents; no backend, UDP, party, seat or session exists.");
        }
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("RML PARTY QUEUE GL PASS " + message);
    }
}
