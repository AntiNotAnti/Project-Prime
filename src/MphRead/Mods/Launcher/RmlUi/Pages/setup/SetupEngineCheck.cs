#if (MPHREAD_RMLUI_POC || MPHREAD_RMLUI) && !MPHREAD_SERVER
using System;
using System.IO;
using System.Threading;
using MphRead.Mods.Update;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Setup;

public static class SetupEngineCheck
{
    // Run in an isolated process: this exercises the authoritative global path service.
    public static void Run(string? assets = null)
    {
        string cwd = Directory.GetCurrentDirectory(), root = GameFiles.Root;
        string fixture = Directory.CreateTempSubdirectory("prime-rmlui-setup-").FullName;
        int checks = 0;
        void Check(bool ok, string message)
        { if (!ok) throw new InvalidOperationException(message); checks++; Console.WriteLine("RMLSETUP ENGINE PASS " + message); }
        try
        {
            Directory.SetCurrentDirectory(fixture); GameFiles.Root = fixture;
            using var backend = new EngineSetupBackend();
            Check(!backend.Inspect().Ready, "missing extraction is reported as unavailable");
            string rom = Path.Combine(fixture, "invalid.nds"); File.WriteAllBytes(rom, new byte[32]);
            string last = "";
            Check(!backend.Extract(rom, line => last = line).Success && last.Length > 0 && !File.Exists("paths.txt"), "invalid ROM is rejected by the actual compatibility service before writing paths");
            Check(!backend.ConfigureExtractedPath("invalid", fixture).Success && !File.Exists("paths.txt"), "unknown revision cannot write paths");
            Check(!backend.ConfigureExtractedPath(Ver.AMHE1, fixture).Success && !File.Exists("paths.txt"), "empty data directory cannot be accepted as an installation");
            string valid = Path.Combine(fixture, "extracted");
            foreach (var pair in new[] { (@"_archives\mp1\mp1_Model.bin", Sizes.Header), (@"_archives\mp1\mp1_Collision.bin", Sizes.CollisionHeader), (@"levels\entities\mp1_Ent.bin", Sizes.EntityHeader + Sizes.EntityEntry) })
            {
                string file = Paths.Combine(valid, pair.Item1); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                using var output = File.Create(file); output.SetLength(pair.Item2);
            }
            Check(backend.ConfigureExtractedPath(Ver.AMHE1, valid).Success && GameFiles.Ready, "validated sentinel files configure the authoritative path service");
            File.AppendAllText("paths.txt", "future_entry=preserved\nExport=" + Path.Combine(fixture, "exports") + "\n");
            Check(backend.ConfigureExtractedPath(Ver.AMHE1, valid).Success && File.ReadAllText("paths.txt").Contains("future_entry=preserved") && Paths.Export.EndsWith("exports", StringComparison.Ordinal), "path changes preserve forward entries and export directory");
            byte[] before = File.ReadAllBytes("paths.txt");
            Check(!backend.ConfigureExtractedPath(Ver.AMHE1, Path.Combine(fixture, "missing")).Success && before.AsSpan().SequenceEqual(File.ReadAllBytes("paths.txt")), "invalid manual path leaves previous installation byte-for-byte intact");
            File.WriteAllText("paths.txt", "0.18.0.0\n" + Ver.AMHE1 + "=" + valid + "\n"); before = File.ReadAllBytes("paths.txt");
            Check(!backend.ConfigureExtractedPath(Ver.AMHE1, valid).Success && before.AsSpan().SequenceEqual(File.ReadAllBytes("paths.txt")), "old extraction version cannot be falsely upgraded by changing paths");
            File.Delete("paths.txt");
            double clock = 1; bool enabled = true; int requests = 0;
            using (var monitor = new NativeUpdateMonitor(cancel =>
            {
                int request = Interlocked.Increment(ref requests);
                return new(new UpdateInfo { Tag = request < 3 ? "v2" : "v3", Version = new Version(2, 0, 0), AssetName = "test", AssetUrl = "", AssetDigest = "", PageUrl = "https://example.test", Notes = "" }, "available");
            }, () => enabled, () => clock))
            {
                monitor.Tick(false); Thread.Sleep(10); monitor.Tick(false);
                Check(!monitor.TryTakeAvailable(out _), "automatic release prompt is deferred while routing is unsafe");
                DateTime until = DateTime.UtcNow.AddSeconds(2); UpdateInfo discovered = default; bool found = false;
                while (!found && DateTime.UtcNow < until) { monitor.Tick(true); found = monitor.TryTakeAvailable(out discovered); Thread.Sleep(1); }
                Check(found && discovered.Tag == "v2" && requests == 1, "deferred immutable release is delivered on the owner thread");
                clock += 299; monitor.Tick(true); Check(requests == 1, "automatic discovery observes the five-minute interval");
                clock += 1; monitor.Tick(true); Thread.Sleep(10); monitor.Tick(true);
                Check(requests == 2 && !monitor.TryTakeAvailable(out _), "same release tag is prompted only once");
                clock += 300; monitor.Tick(false); Thread.Sleep(10); monitor.Tick(false);
                enabled = false; monitor.Tick(true); Check(!monitor.TryTakeAvailable(out _), "disabled automatic updates drop pending prompts");
            }
            if (assets != null)
            {
                using var host = new RmlUiHost();
                Check(host.Initialize(1280, 720, 1, assets, RmlUiRenderBackend.DrawList), "actual native setup host initializes");
                using var pages = new RmlUiPageManager(host);
                bool closed = false;
                using var presenter = new SetupPagePresenter(host, pages, () => closed = true, () => throw new InvalidOperationException("Unexpected install exit"), required: true);
                presenter.Open(); host.Update(); host.Render(1280, 720);
                var document = presenter.Document;
                void Click(string id)
                {
                    presenter.Refresh(); host.Update(); Check(host.FocusDocument(pages.Top, id), "native focus " + id);
                    host.Input.Key(2, true); host.Input.Key(2, false); host.Update();
                    Check(host.TryTakeIntent(out var intent) && presenter.HandleAction(intent), "typed setup action " + id);
                    host.Update(); host.Render(1280, 720);
                }
                Check(host.TryGetElementBounds(document, "setup_rom_path", out _, out _, out float width, out float height) && width > 200 && height >= 30, "native ROM input has a usable positive layout");
                Check(host.TryGetElementBounds(document, "setup_pick_rom", out _, out _, out width, out height) && width > 200 && height >= 35, "native ROM picker button has a usable positive layout");
                presenter.Back(); Check(!closed && pages.Page == document, "required setup blocks Back while files are missing");
                host.SetField(document, "setup_rom_path", rom); Click("setup_install_rom");
                Check(pages.ModalCount == 1 && presenter.Controller.Snapshot().ConfirmingRom, "native ROM selection opens replacement confirmation");
                Click("setup_confirm_cancel"); Check(pages.ModalCount == 0 && !File.Exists("paths.txt"), "native cancelled ROM confirmation leaves data untouched");
                Click("setup_install_rom"); Click("setup_confirm_yes");
                DateTime timeout = DateTime.UtcNow.AddSeconds(5);
                while (presenter.Busy && DateTime.UtcNow < timeout) { presenter.Refresh(); Thread.Sleep(1); }
                presenter.Refresh();
                Check(!presenter.Busy && presenter.Controller.Snapshot().Error.Length > 0 && presenter.Controller.Snapshot().Log.Length > 0 && !File.Exists("paths.txt"), "native invalid ROM completion shows actual failure log");
                host.SetField(document, "setup_revision", Ver.AMHE1); host.SetField(document, "setup_data_path", valid); Click("setup_configure_path");
                Check(GameFiles.Ready && presenter.Controller.Snapshot().Files.Ready, "native validated manual path updates readiness");
                Click("setup_back"); Check(closed && pages.Page == default, "ready setup returns through native Back");
            }
            Console.WriteLine($"RMLSETUP ENGINE CHECK PASS {checks} checks");
        }
        finally { Directory.SetCurrentDirectory(cwd); GameFiles.Root = root; Paths.UpdatePaths(); Directory.Delete(fixture, true); }
    }
}
#endif
