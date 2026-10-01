using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MphRead.Mods.Settings;
using MphRead.Mods.Render.Hud;

string root = Path.Combine(Environment.CurrentDirectory, ".settings-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string originalPreferencesDirectory = MphRead.Mods.Launcher.LauncherPrefs.Directory;
MphRead.Mods.Launcher.LauncherPrefs.Directory = root;
int passed = 0;
try
{
    void Test(string name, Action action) { action(); Console.WriteLine("PASS " + name); passed++; }
    void Require(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    void Reject(Action action)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or JsonException or FormatException or ArgumentException or IOException) { return; }
        throw new Exception("Invalid archive accepted.");
    }
    byte[] Archive((string Path, byte[] Bytes)[] files, int format = 1, bool manifest = true, string? extra = null)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            if (manifest)
            {
                using var entry = zip.CreateEntry("manifest.json").Open();
                var records = files.Select(f => new SettingsArchiveFile(f.Path, f.Path == "launcher.txt" ? "Launcher" : f.Path == "controls.txt" ? "Controls" : "General")).ToList();
                JsonSerializer.Serialize(entry, new SettingsArchiveManifest(format, "Project Prime", "0.1", records), SettingsArchiveManifest.Json);
            }
            foreach (var file in files) { using var entry = zip.CreateEntry(file.Path).Open(); entry.Write(file.Bytes); }
            if (extra != null) zip.CreateEntry(extra);
        }
        return memory.ToArray();
    }
    byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);
    byte[] Good() => Archive(new[] { ("launcher.txt", Bytes("player_name=Before\n")), ("controls.txt", Bytes("sensitivity=1\n")) });
    void Import(byte[] archive) => SettingsArchive.Import(root, new MemoryStream(archive));
    Test("round trip and private files excluded", () =>
    {
        Directory.CreateDirectory(Path.Combine(root, "Savedata/hud-profiles"));
        File.WriteAllText(Path.Combine(root, "Savedata/settings.json"), "{\"MenuSettings\":{}}");
        File.WriteAllText(Path.Combine(root, "Savedata/hud-profiles/active.json"), HudProfileStore.Serialize(HudProfileDefaults.Create("Project Prime")));
        File.WriteAllText(Path.Combine(root, "launcher.txt"), "player_name=Before\n");
        File.WriteAllText(Path.Combine(root, "career.env"), "private");
        File.WriteAllText(Path.Combine(root, "Savedata/auth.json"), "private");
        using var output = new MemoryStream(); SettingsArchive.Export(root, output, "test");
        output.Position = 0; var data = SettingsArchive.Read(output);
        Require(data.Count == 3 && !data.ContainsKey("career.env"));
        File.WriteAllText(Path.Combine(root, "launcher.txt"), "player_name=After\n");
        output.Position = 0; SettingsArchive.Import(root, output);
        Require(File.ReadAllText(Path.Combine(root, "launcher.txt")) == "player_name=Before\n");
    });
    Test("unknown entry", () => Reject(() => Import(Archive(new[] { ("evil.exe", Bytes("x")) }))));
    Test("unlisted entry", () => Reject(() => Import(Archive(Array.Empty<(string, byte[])>(), extra: "evil.sh"))));
    Test("duplicate entry", () => Reject(() => Import(Archive(new[] { ("launcher.txt", Bytes("")), ("launcher.txt", Bytes("")) }))));
    Test("case alias duplicate", () => Reject(() => Import(Archive(new[] { ("launcher.txt", Bytes("")), ("LAUNCHER.TXT", Bytes("")) }))));
    Test("oversized entry", () => Reject(() => Import(Archive(new[] { ("launcher.txt", new byte[2 * 1024 * 1024 + 1]) }))));
    Test("missing manifest", () => Reject(() => Import(Archive(Array.Empty<(string, byte[])>(), manifest: false))));
    Test("unsupported version", () => Reject(() => Import(Archive(Array.Empty<(string, byte[])>(), format: 2))));
    Test("malformed settings", () => Reject(() => Import(Archive(new[] { ("Savedata/settings.json", Bytes("{oops")) }))));
    Test("invalid UTF8", () => Reject(() => Import(Archive(new[] { ("launcher.txt", new byte[] { 0xc3, 0x28 }) }))));
    Test("traversal", () => { foreach (string path in new[] { "../launcher.txt", "/launcher.txt", "Savedata/../launcher.txt", "Savedata\\settings.json", "C:/launcher.txt" }) Reject(() => Import(Archive(new[] { (path, Bytes("")) }))); });
    Test("partial install rollback", () =>
    {
        File.WriteAllText(Path.Combine(root, "launcher.txt"), "player_name=Original\n");
        File.Delete(Path.Combine(root, "controls.txt"));
        try { SettingsArchive.Import(root, new MemoryStream(Good()), i => { if (i == 1) throw new IOException("Injected failure"); }); throw new Exception("Expected failure"); }
        catch (IOException) { }
        Require(File.ReadAllText(Path.Combine(root, "launcher.txt")) == "player_name=Original\n" && !File.Exists(Path.Combine(root, "controls.txt")));
        Require(!Directory.EnumerateFiles(root, "*.archive-*", SearchOption.AllDirectories).Any());
    });
    Test("rollback removes newly installed store", () =>
    {
        File.Delete(Path.Combine(root, "launcher.txt"));
        try { SettingsArchive.Import(root, new MemoryStream(Good()), i => { if (i == 1) throw new IOException("Injected failure"); }); throw new Exception("Expected failure"); } catch (IOException) { }
        Require(!File.Exists(Path.Combine(root, "launcher.txt")));
    });
    Test("older build and removed preference", () => Import(Archive(new[] { ("Savedata/settings.json", Bytes("{\"MenuSettings\":{\"SettingsSchemaVersion\":0,\"RemovedOption\":true}}")), ("controls.txt", Bytes("retired_option=1\n")) })));
    Test("missing optional stores", () => Import(Archive(Array.Empty<(string, byte[])>())));
    Test("duplicate JSON key", () => Reject(() => Import(Archive(new[] { ("Savedata/settings.json", Bytes("{\"MenuSettings\":{},\"MenuSettings\":{}}")) }))));
    Test("credentials rejected", () => Reject(() => Import(Archive(new[] { ("launcher.txt", Bytes("refresh_token=secret\n")) }))));
    Test("invalid preferences", () => { Reject(() => Import(Archive(new[] { ("controls.txt", Bytes("bad line")) }))); Reject(() => Import(Archive(new[] { ("controls.txt", Bytes("sensitivity=NaN")) }))); });
    Test("symlink destination", () =>
    {
        string outside = Path.Combine(root, "outside.txt"), link = Path.Combine(root, "launcher.txt");
        File.WriteAllText(outside, "untouched"); File.Delete(link); File.CreateSymbolicLink(link, outside);
        Reject(() => Import(Good())); Require(File.ReadAllText(outside) == "untouched"); File.Delete(link);
    });
    Test("invalid controller profiles", () => Reject(() => SettingsArchiveValidator.Validate(SettingsArchiveRegistry.Resolve("controller-profiles.json"), Bytes("{\"Profiles\":[null],\"Assignments\":{}}"))));
    Test("canonical controls and launcher export", () =>
    {
        string prior = MphRead.Mods.Launcher.LauncherPrefs.Directory;
        try
        {
            MphRead.Mods.Launcher.LauncherPrefs.Directory = root;
            MphRead.Mods.InputSettings.Save(); MphRead.Mods.Launcher.LauncherPrefs.Save();
            using var output = new MemoryStream(); SettingsArchive.Export(root, output, "test");
        }
        finally { MphRead.Mods.Launcher.LauncherPrefs.Directory = prior; }
    });
    Test("detached complete text grammar", () =>
    {
        foreach (string text in new[] { "auto_update=maybe", "bright_skin_style=999", "window_size=3xno", "window_mode=unknown", "aim_trainer={oops" })
            Reject(() => PreferenceText.Parse(text, true));
        foreach (string text in new[] { "invert_x=maybe", "touch_buttons=no", "touch_button_scale=NaN", "gamepad_preset=unknown", "gamepad_wheel_order=0,1,1,3,4,5", "clip_key=999999" })
            Reject(() => PreferenceText.Parse(text, false));
        var values = PreferenceText.Parse("retired_option=old\nretired_option=new\nsensitivity=2", false);
        Require(values["retired_option"] == "new");
        Require(PreferenceText.Parse("window_mode=borderless fullscreen\nwindow_size=1280x720", true).Count == 2);
    });
    Test("mapping grammar", () =>
    {
        const string prefix = "03000000000000000000000000000000,Test,";
        MphRead.Mods.Input.GamepadMappings.ValidateMapping(prefix + "a:b0,leftx:+a1~,dpup:h0.1,platform:Windows,");
        foreach (string mapping in new[] { prefix + "a:bNaN,", prefix + "leftx:a-1,", prefix + "dpup:h0.0,", prefix + "a:b0,a:b1," })
            Reject(() => MphRead.Mods.Input.GamepadMappings.ValidateMapping(mapping));
    });
    Test("reset rollback and private files preserved", () =>
    {
        File.WriteAllText(Path.Combine(root, "launcher.txt"), "player_name=Keep");
        File.WriteAllText(Path.Combine(root, "controls.txt"), "sensitivity=2");
        File.WriteAllText(Path.Combine(root, "Savedata/hud-profiles/orphan.json.bak"), "old layout");
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        try { SettingsArchive.Reset(root, i => { if (i == 1) throw new IOException("Injected reset failure"); }); throw new Exception("Expected failure"); } catch (IOException) { }
        Require(before.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)));
        SettingsArchive.Reset(root);
        Require(!File.Exists(Path.Combine(root, "launcher.txt")) && !File.Exists(Path.Combine(root, "controls.txt")));
        Require(!File.Exists(Path.Combine(root, "Savedata/hud-profiles/orphan.json.bak")));
        Require(File.ReadAllText(Path.Combine(root, "career.env")) == "private" && File.ReadAllText(Path.Combine(root, "Savedata/auth.json")) == "private");
    });
    if (args.Contains("--ui")) Test("reset confirmation can be cancelled", () =>
    {
        Require(MphRead.Mods.Launcher.Gui.GuiLauncher.EnsureSetup(requireDisplay: false));
        Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
        {
            var view = new MphRead.Mods.Launcher.Gui.SettingsView(new MphRead.MenuSettings());
            object? previous = view.Content;
            typeof(MphRead.Mods.Launcher.Gui.SettingsView).GetMethod("ShowResetConfirmation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, null);
            var panel = (Avalonia.Controls.StackPanel)view.Content!;
            Require(panel.Children.OfType<MphRead.Mods.Launcher.Gui.HubNavButton>().Any(b => b.Label == "CONFIRM RESET"));
            var cancel = panel.Children.OfType<MphRead.Mods.Launcher.Gui.HubNavButton>().Single(b => b.Label == "CANCEL");
            MphRead.Mods.Launcher.Gui.FocusNavigator.Key(cancel, Avalonia.Input.Key.Enter);
            Require(ReferenceEquals(previous, view.Content) && !SettingsPersistence.RestartRequired);
        });
    });
    Test("failed replacement releases write fence", () =>
    {
        try { SettingsPersistence.Replace(() => throw new IOException("Before installation")); } catch (IOException) { }
        Require(!SettingsPersistence.RestartRequired);
        using var lease = SettingsPersistence.BeginWrite(); Require(lease != null);
    });
    Test("restart later fences stale runtime writers", () =>
    {
        string prior = MphRead.Mods.Launcher.LauncherPrefs.Directory;
        try
        {
            MphRead.Mods.Launcher.LauncherPrefs.Directory = root;
            SettingsPersistence.Replace(() => Import(Good()));
            Require(SettingsPersistence.RestartRequired);
            MphRead.Mods.InputSettings.Save(); MphRead.Mods.Launcher.LauncherPrefs.Save();
            MphRead.Mods.Input.GamepadProfiles.WriteAtomic(Path.Combine(root, "gamecontrollerdb.txt"), "stale");
            Require(File.ReadAllText(Path.Combine(root, "launcher.txt")) == "player_name=Before\n");
            Require(File.ReadAllText(Path.Combine(root, "controls.txt")) == "sensitivity=1\n");
            Require(!File.Exists(Path.Combine(root, "gamecontrollerdb.txt")));
            using var lease = SettingsPersistence.BeginWrite(); Require(lease == null);
        }
        finally { MphRead.Mods.Launcher.LauncherPrefs.Directory = prior; }
    });
    if (args.Contains("--ui")) Test("reopened editor offers restart now and later", () =>
    {
        Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
        {
            var view = new MphRead.Mods.Launcher.Gui.SettingsView(new MphRead.MenuSettings());
            var panel = (Avalonia.Controls.StackPanel)view.Content!;
            var buttons = panel.Children.OfType<MphRead.Mods.Launcher.Gui.HubNavButton>().ToArray();
            bool restarted = false, closed = false;
            SettingsArchivePlatform.RestartApplication = () => restarted = true;
            view.Closed += (_, _) => closed = true;
            try
            {
                MphRead.Mods.Launcher.Gui.FocusNavigator.Key(buttons.Single(b => b.Label == "RESTART NOW"), Avalonia.Input.Key.Enter);
                MphRead.Mods.Launcher.Gui.FocusNavigator.Key(buttons.Single(b => b.Label == "RESTART LATER"), Avalonia.Input.Key.Enter);
                Require(restarted && closed && !view.IsDirty);
            }
            finally { SettingsArchivePlatform.RestartApplication = null; }
        });
    });
    Console.WriteLine($"{passed} settings archive checks passed.");
}
finally { MphRead.Mods.Launcher.LauncherPrefs.Directory = originalPreferencesDirectory; Directory.Delete(root, true); }
