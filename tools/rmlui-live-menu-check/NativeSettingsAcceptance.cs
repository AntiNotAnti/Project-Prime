using GamepadState = MphRead.Mods.Input.GamepadState;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using OpenTK.Windowing.GraphicsLibraryFramework;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Render.Hud;
using MphRead.Mods.Settings;

internal static class NativeSettingsAcceptance
{
    internal const string ChildArgument = "--native-settings-acceptance-isolated";

    // A separate process prevents successful commits, runtime publication and
    // settings store initialization from affecting another fixture or real prefs.
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "prime-native-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing test executable.");
            var start = new ProcessStartInfo(executable)
            { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(NativeSettingsAcceptance).Assembly.Location);
            start.ArgumentList.Add(ChildArgument);
            start.Environment["PROJECT_PRIME_USER_DATA"] = root;
            start.Environment["PROJECT_PRIME_NATIVE_SETTINGS_TEST_ROOT"] = root;
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start settings fixture.");
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(60000)) { child.Kill(entireProcessTree: true); throw new TimeoutException("Settings CPU fixture timed out."); }
            string stdout = output.GetAwaiter().GetResult(), stderr = error.GetAwaiter().GetResult();
            Console.Write(stdout);
            Check(child.ExitCode == 0, "isolated native settings acceptance completes\n" + stderr);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    internal static void RunIsolated()
    {
        string root = Environment.GetEnvironmentVariable("PROJECT_PRIME_NATIVE_SETTINGS_TEST_ROOT") ?? "";
        string userData = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? "";
        Check(root.Length > 0 && Path.GetFullPath(root) == Path.GetFullPath(userData)
            && Path.GetFileName(root).StartsWith("prime-native-settings-", StringComparison.Ordinal)
            && Directory.GetFileSystemEntries(root).Length == 0,
            "settings fixture begins with fresh isolated user data");
        // Canonicalize macOS /var -> /private/var before any engine owner is read.
        Directory.SetCurrentDirectory(root); root = Directory.GetCurrentDirectory();
        Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", root);
        Check(Path.GetFullPath(LauncherPrefs.Directory) == root, "all portable preference owners use isolated working directory");

        var settings = new MenuSettings();
        using (var controller = new NativeFullSettingsController(settings))
        {
            string[] originalRows = controller.Rows.Select(r => r.Read()).ToArray();
            var invalid = controller.Rows.Where(r => r.Validate(r.Value) != null)
                .Select(r => r.Category + "/" + r.Label + ": " + r.Validate(r.Value)).ToArray();
            Check(invalid.Length == 0, "every stock row validates: " + string.Join("; ", invalid));
            Check(!controller.Dirty && controller.Rows.Count > 150, "full native catalog starts clean and includes full controls");
            foreach (string category in new[] { "Display", "Graphics", "Audio", "Controls", "Replays", "Profile", "System" })
                Check(controller.Rows.Any(r => r.Category == category && r.Group.Length > 0), category + " has grouped engine-owned settings");
            foreach (string group in new[] { "Keyboard bindings", "Controller bindings", "Controller calibration", "Touch layout", "Stylus guide" })
                Check(controller.Rows.Any(r => r.Group == group), "Controls includes " + group);
            Check(Row(controller, "Audio", "Sfx Volume (%)").Value == "35"
                && Row(controller, "Audio", "Music Volume (%)").Value == "50", "mixer gains are readable UI percentages");
            Check(controller.Stage(Row(controller, "Audio", "Sfx Volume (%)"), "65") && settings.SfxVolume == "0.35",
                "staging mixer percent leaves MenuSettings untouched");
            var language = Row(controller, "Audio", "Language");
            Check(controller.Stage(language, "english") && language.Value == "English", "choice staging canonicalizes engine-sensitive language strings");
            var mode = Row(controller, "Display", "Window Mode");
            Check(controller.Stage(mode, mode.Value.ToLowerInvariant()) && mode.Value == mode.Choices.First(c => c.Equals(mode.Value, StringComparison.OrdinalIgnoreCase)),
                "window choice staging keeps canonical enum strings");
            Check(controller.Stage(Row(controller, "Graphics", "Graphics Preset"), "ULTRA")
                && Row(controller, "Graphics", "Graphics Preset").Value == "ultra", "graphics preset canonicalizes persisted menu string");
            Check(controller.Stage(Row(controller, "Graphics", "Resolution Scale"), "125")
                && Row(controller, "Graphics", "Graphics Preset").Value == "custom", "editing graphics fields marks preset custom");
            Check(controller.Stage(Row(controller, "Controls", "Mouse Sensitivity"), "2"), "mouse sensitivity stages valid draft");
            Check(controller.Rows.Select(r => r.Read()).SequenceEqual(originalRows)
                && !Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Any(), "draft staging changes no engine statics or owner files");
            controller.Discard();
            Check(!controller.Dirty && controller.Rows.All(r => r.Value == r.Saved)
                && controller.Rows.Select(r => r.Read()).SequenceEqual(originalRows), "discard restores every draft without runtime mutation");
            Range(controller, "Controls", "Imperialist Zoom Amount", ".25", "2", ".24", "2.01");
            Range(controller, "Controls", "Imperialist Zoom Sensitivity", ".1", "3", ".09", "3.01");
            Range(controller, "Controls", "Alt Swipe Sensitivity", ".25", "4", ".24", "4.01");
            Range(controller, "Controls", "Button Scale", ".5", "1.75", ".49", "1.76");
            Range(controller, "Controls", "Stick Scale", ".6", "1.8", ".59", "1.81");
            Range(controller, "Replays", "Post Roll Seconds", "0", "5", "-1", "6");
            foreach (string cap in new[] { "30", "75", "100", "200" })
                Check(Row(controller, "Display", "Frame Rate Cap").Validate(cap) == null, "legacy valid frame cap remains available: " + cap);
            foreach (var pair in new[] { ("Display", "Field Of View"), ("Graphics", "Resolution Scale"), ("Graphics", "Cel Bands"), ("Graphics", "Cel Edge") })
                Check(Row(controller, pair.Item1, pair.Item2).Validate(pair.Item2 == "Cel Bands" ? "3.5" : "78.5") != null,
                    "integer engine parser rejects fractional " + pair.Item2);
            Check(Row(controller, "Controls", "Guide Color").Validate("invalid") != null,
                "invalid guide color cannot become a silently ignored saved draft");
            Check(controller.Rows.Where(r => r.Group == "Keyboard bindings").All(r => r.Validate("Unknown") == null),
                "keyboard bindings retain existing unbound support");

            string runtimeHud = HudProfileStore.Serialize(HudProfiles.CopyCurrent());
            HudProfile proposed = controller.CopyHud(); proposed.SafeArea = .13f;
            proposed.Elements["core.health"].OffsetX = 37;
            controller.SetHud(proposed); proposed.SafeArea = .2f;
            HudProfile copy = controller.CopyHud();
            Check(Math.Abs(copy.SafeArea - .13f) < .0001f && copy.Elements["core.health"].OffsetX == 37,
                "HUD handoff clones the editor result into the settings draft");
            copy.Elements["core.health"].OffsetX = 99;
            Check(controller.CopyHud().Elements["core.health"].OffsetX == 37
                && HudProfileStore.Serialize(HudProfiles.CopyCurrent()) == runtimeHud,
                "HUD copy edits cannot mutate draft owner or live HUD");
            controller.Discard();
            Check(!controller.Dirty && HudProfileStore.Serialize(HudProfiles.CopyCurrent()) == runtimeHud,
                "discarding HUD edits preserves live user layout");
        }
        MainSettingsRegressions(settings);
        ControlsParity(settings);
        LegacyControllerAlternatives(settings);
        HudEditorDraft();
        VideoRollback(settings);
        Persistence(root, settings);
    }

    private static void MainSettingsRegressions(MenuSettings settings)
    {
        bool wanted=StylusZone.Wanted,mode=PointerInput.StylusMode;
        try
        {
            StylusZone.Enabled=true;PointerInput.StylusMode=false;
            using var stylus=new NativeFullSettingsController(settings);
            var zone=stylus.Rows.Single(r=>r.Group=="Stylus guide"&&r.Label=="Enabled");
            Check(bool.Parse(zone.Value),"native stylus setting retains saved Wanted while effective mode is off");
            Check(stylus.Stage(zone,"false")&&StylusZone.Wanted,"disabling saved stylus zone remains detached until Apply");stylus.Discard();
            Check(bool.Parse(zone.Value)&&StylusZone.Wanted,"discard restores saved stylus preference independent of effective mode");
        }
        finally{StylusZone.Enabled=wanted;PointerInput.StylusMode=mode;}
        var owner=GamepadRuntimeConfig.Current.Bindings;var saved=owner.Clone();
        try
        {
            owner.Set(PadAction.ReplayPlayPause,GamepadButtons.B|GamepadButtons.X|GamepadButtons.Y);
            owner.Set(PadAction.Morph,GamepadButtons.A|GamepadButtons.B|GamepadButtons.X|GamepadButtons.Y);
            var before=owner.Clone();using var controller=new NativeFullSettingsController(settings);
            Check(controller.Rows.Where(r=>r.BindingAction is {} a&&owner.ReplayActions.Contains(a)).All(r=>r.Category=="Replays"),"replay controller controls appear in their owning Replays category");
            controller.ResetReplayControls();
            Check(controller.Dirty&&owner.Get(PadAction.ReplayPlayPause)==before.Get(PadAction.ReplayPlayPause),"reset replay controls stages defaults without mutating runtime");
            controller.Select(3);controller.ResetCategory();
            Check(controller.Rows.Single(r=>r.BindingAction==PadAction.ReplayPlayPause&&r.BindingSlot==0).Value=="None+A","reverting gameplay controls preserves staged replay defaults");
            controller.Select(4);controller.ResetCategory();Check(!controller.Dirty,"reverting Replays restores full saved alternatives after reset");
            controller.ResetReplayControls();controller.Discard();Check(!controller.Dirty&&owner.Get(PadAction.Morph)==before.Get(PadAction.Morph),"replay reset/discard preserves gameplay legacy alternatives");
            var camera=controller.Rows.Single(r=>r.Label=="Kill Cam Camera");Check(camera.ChoiceLabels?["1"]=="First person"&&!camera.HasSlider,"kill camera exposes friendly canonical choices");
        }
        finally{owner.CopyFrom(saved);}
    }

    private static void LegacyControllerAlternatives(MenuSettings settings)
    {
        var owner=GamepadRuntimeConfig.Current.Bindings;var before=owner.Clone();
        string[] Lines(PadBindingState state){var lines=new List<string>();state.Write(lines);lines.Add("preset="+state.Preset);return lines.ToArray();}
        try
        {
            owner.Set(PadAction.Morph,GamepadButtons.A|GamepadButtons.B|GamepadButtons.X|GamepadButtons.Y);
            owner.Preset="Default"; // Valid old files can retain a stale layout label.
            string[] legacy=Lines(owner);
            using var controller=new NativeFullSettingsController(settings);
            var target=controller.Rows.Single(r=>r.BindingAction==PadAction.Jump&&r.BindingSlot==0);
            Check(controller.Rows.Any(r=>r.BindingAction==PadAction.Morph&&r.Hint.Contains("additional legacy alternatives")),"legacy alternatives beyond two slots are visible as preserved binding hints");
            foreach(string resolution in new[]{"Swap","Replace","Keep Both","Cancel"})
            {
                controller.Stage(target,"None+RightTrigger");controller.BeginCapture(target);
                var baseline=GamepadManager.Snapshot;
                controller.PollBindingCapture(new(baseline.DeviceId,new GamepadState{Connected=true,Buttons=GamepadButtons.X},baseline.Revision));
                Check(controller.BindingConflict,"hidden third alternative participates in owner conflict lookup: "+resolution);
                Check(controller.ResolveBinding(resolution),"resolve legacy alternative conflict: "+resolution);
                var field=typeof(NativeFullSettingsController).GetField("_bindingsDraft",BindingFlags.NonPublic|BindingFlags.Instance)!;
                var staged=(PadBindingState?)field.GetValue(controller);
                if(resolution is "Swap" or "Replace")
                    Check(staged!=null&&(staged.Get(PadAction.Morph)&GamepadButtons.X)==0
                        &&(staged.Get(PadAction.Morph)&GamepadButtons.Y)!=0,"resolution changes only the conflicting hidden alternative, preserving fourth alternative");
                if(resolution=="Swap")Check((staged!.Get(PadAction.Morph)&GamepadButtons.RightTrigger)!=0,"Swap carries old target button into legacy alternatives");
                if(resolution=="Keep Both")Check(staged!=null&&(staged.Get(PadAction.Morph)&GamepadButtons.X)!=0,"Keep Both retains hidden legacy assignment");
                Check(Lines(owner).SequenceEqual(legacy),"conflict proposal never mutates actual legacy owner");
                controller.Discard();Check(!controller.Dirty&&Lines(owner).SequenceEqual(legacy),"Discard preserves exact legacy flags, slots, modifiers and stale preset label");
            }
            controller.Stage(controller.Rows.Single(r=>r.Label=="Control layout"),"Classic");controller.Select(3);controller.ResetCategory();
            Check(!controller.Dirty&&Lines(owner).SequenceEqual(legacy),"Controls Reset retires full hidden binding draft without changing legacy owner");
            var copy=new PadBindingState();long revision=copy.Revision;copy.CopyFrom(owner);
            Check(copy.Revision>revision&&Lines(copy).SequenceEqual(legacy),"binding owner CopyFrom preserves full flags slots modifiers and bumps revision");
            copy.SetSlot(PadAction.Morph,0,GamepadButtons.LeftBumper);
            Check((copy.Get(PadAction.Morph)&(GamepadButtons.X|GamepadButtons.Y))==(GamepadButtons.X|GamepadButtons.Y)
                &&Lines(owner).SequenceEqual(legacy),"ordinary slot edit retains unrelated third and fourth legacy alternatives");
            owner.Set(PadAction.Jump,GamepadButtons.X);owner.Set(PadAction.Scan,0);owner.Preset="Custom";
            using var hiddenOnly=new NativeFullSettingsController(settings);
            var existingTarget=hiddenOnly.Rows.Single(r=>r.BindingAction==PadAction.Jump&&r.BindingSlot==0);
            hiddenOnly.BeginCapture(existingTarget);var snap=GamepadManager.Snapshot;
            hiddenOnly.PollBindingCapture(new(snap.DeviceId,new GamepadState{Connected=true,Buttons=GamepadButtons.X},snap.Revision));
            Check(hiddenOnly.ResolveBinding("Replace")&&hiddenOnly.Rows.All(r=>!r.Dirty)&&hiddenOnly.Dirty,
                "hidden-alternative-only removal participates in Dirty even when visible slots and preset are unchanged");
            hiddenOnly.Discard();Check(!hiddenOnly.Dirty&&(owner.Get(PadAction.Morph)&GamepadButtons.X)!=0,"discard retires hidden-only proposal without changing live flags");
        }
        finally{owner.CopyFrom(before);}
    }

    private static void ControlsParity(MenuSettings settings)
    {
        using(var controller=new NativeFullSettingsController(settings))
        {
            string[] actual=controller.Rows.Select(r=>r.Read()).ToArray();
            var keys=controller.Rows.Where(r=>r.Group=="Keyboard bindings").ToArray();
            controller.BeginCapture(keys[0]);Check(controller.KeyDown(Keys.Backspace)&&keys[0].Value=="Unknown"&&!controller.Capturing,"Backspace clears a captured keyboard binding in the draft");
            var chat=controller.Rows.Single(r=>r.Label=="Chat Key");controller.BeginCapture(chat);Check(controller.KeyDown(Keys.Delete)&&chat.Value=="Unknown","Delete clears a captured direct chat key");controller.Discard();
            foreach(string resolution in new[]{"Swap","Replace","Keep Both","Cancel"})
            {
                foreach(var key in keys)controller.Stage(key,"Unknown");
                controller.Stage(keys[0],"F1");controller.Stage(keys[1],"F2");
                controller.BeginCapture(keys[0]);controller.KeyDown(Keys.F2);
                Check(controller.BindingConflict&&keys[0].Value=="F1"&&keys[1].Value=="F2","keyboard conflict freezes proposal before "+resolution);
                Check(controller.ResolveBinding(resolution)&&!controller.BindingConflict
                    &&keys[0].Value==(resolution=="Cancel"?"F1":"F2")
                    &&keys[1].Value==(resolution=="Swap"?"F1":resolution=="Replace"?"Unknown":"F2"),"keyboard conflict resolution: "+resolution);
                controller.Discard();
            }
            var layout=controller.Rows.Single(r=>r.Label=="Control layout");
            foreach(string preset in new[]{"Default","Bumper Jumper","Southpaw","Classic"})
            {
                Check(controller.Stage(layout,preset),"native controller stages "+preset+" layout");
                var expected=GamepadRuntimeConfig.Current.Clone();expected.Layout.Apply(preset);
                Check(controller.Rows.Where(r=>r.BindingAction!=null).All(r=>r.Value==expected.Bindings.Modifier(r.BindingAction!.Value,r.BindingSlot)+"+"+expected.Bindings.Slot(r.BindingAction.Value,r.BindingSlot))
                    &&bool.Parse(controller.Rows.Single(r=>r.Group=="Controller"&&r.Label=="Southpaw").Value)==expected.Options.Southpaw,
                    "native layout uses actual owner bindings and Southpaw semantics: "+preset);
                controller.Discard();
            }
            var target=controller.Rows.Single(r=>r.BindingAction==PadAction.Jump&&r.BindingSlot==0);
            controller.BeginCapture(target);Check(controller.KeyDown(Keys.Delete)&&target.Value=="None+None","Delete clears a single controller slot");controller.Discard();
            var modifier=controller.Rows.Single(r=>r.Group=="Controller"&&r.Label=="Binding Modifier");
            controller.Stage(modifier,"LeftBumper");controller.BeginCapture(target);
            var baseline=GamepadManager.Snapshot;
            GamepadSnapshot Pad(GamepadButtons buttons)=>new(baseline.DeviceId,new GamepadState{Connected=true,Buttons=buttons},baseline.Revision);
            controller.PollBindingCapture(Pad(GamepadButtons.LeftBumper));Check(controller.Capturing,"modifier press itself cannot become the action button");
            controller.PollBindingCapture(Pad(GamepadButtons.LeftBumper|GamepadButtons.A));
            Check(!controller.Capturing&&!controller.BindingConflict&&target.Value=="LeftBumper+A","controller capture stores a distinct per-slot modifier chord");controller.Discard();
            foreach(string resolution in new[]{"Swap","Replace","Keep Both"})
            {
                foreach(var binding in controller.Rows.Where(r=>r.BindingAction!=null))controller.Stage(binding,"None+None");
                var other=controller.Rows.Single(r=>r.BindingAction==PadAction.Morph&&r.BindingSlot==0);
                controller.Stage(target,"None+A");controller.Stage(other,"None+B");controller.BeginCapture(target);
                controller.PollBindingCapture(Pad(GamepadButtons.B));
                Check(controller.BindingConflict&&controller.ResolveBinding(resolution)
                    &&target.Value=="None+B"&&other.Value==(resolution=="Swap"?"None+A":resolution=="Replace"?"None+None":"None+B"),"context-aware controller conflict resolution: "+resolution);
                controller.Discard();
            }
            foreach(var binding in controller.Rows.Where(r=>r.BindingAction!=null))controller.Stage(binding,"None+None");
            var replay=controller.Rows.Single(r=>r.BindingAction==PadAction.ReplayPlayPause&&r.BindingSlot==0);
            controller.Stage(replay,"None+A");controller.BeginCapture(target);controller.PollBindingCapture(Pad(GamepadButtons.A));
            Check(!controller.BindingConflict&&target.Value=="None+A","gameplay and replay contexts can share a controller button without false conflict");controller.Discard();
            controller.BeginCapture(target);controller.PollBindingCapture(new(baseline.DeviceId,default,baseline.Revision));
            Check(!controller.Capturing&&controller.Status.Contains("disconnected"),"controller loss cancels capture without staging input");
            Check(controller.Rows.Select(r=>r.Read()).SequenceEqual(actual),"clear, conflict, chord and named layout operations do not change engine statics before Apply");
        }
        const string one="native-settings-test-one",two="native-settings-test-two";
        try
        {
            GamepadManager.UpdateDevice(one,new GamepadState{Connected=true,Name="Fixture pad one"},mapped:true);
            GamepadManager.UpdateDevice(two,new GamepadState{Connected=true,Name="Fixture pad two"},mapped:true);
            GamepadManager.SelectDevice(null);
            using var controller=new NativeFullSettingsController(settings);
            Check(controller.CycleControllerDevice()&&GamepadManager.SelectedDeviceId==one&&controller.CurrentDeviceLabel=="Fixture pad one","device selector targets first actual manager device");
            var look=controller.Rows.Single(r=>r.Group=="Controller"&&r.Label=="Look X");controller.Stage(look,"2");
            Check(!controller.CycleControllerDevice()&&GamepadManager.SelectedDeviceId==one,"device selector protects dirty Controls draft");controller.Discard();
            Check(controller.CycleControllerDevice()&&GamepadManager.SelectedDeviceId==two&&controller.CurrentDeviceLabel=="Fixture pad two"&&!controller.Dirty,"device selector rebases clean Controls for the new runtime");
            Check(controller.CycleControllerDevice()&&GamepadManager.SelectedDeviceId==null,"device selector returns to automatic last-used mode");
        }
        finally{GamepadManager.RemoveDevice(one);GamepadManager.RemoveDevice(two);GamepadManager.SelectDevice(null);}
    }

    private static void HudEditorDraft()
    {
        var source = HudProfileDefaults.Create("Project Prime");
        string original = HudProfileStore.Serialize(source);
        using var editor = new HudEditorController(source, new NoIoHudBackend());
        Check(editor.SelectElement(1), "native HUD controller selects actual health element");
        float beforeX = editor.CopyDraft().Elements["core.health"].OffsetX;
        editor.Nudge(11, -5);
        editor.Dispatch(HudEditorAction.ScaleUp);
        Check(editor.CopyDraft().Elements["core.health"].OffsetX == beforeX + 11
            && HudProfileStore.Serialize(source) == original,
            "native HUD move and scale mutate only detached editor draft");
        Check(editor.Dispatch(HudEditorAction.Undo) && editor.Dispatch(HudEditorAction.Redo)
            && editor.Dispatch(HudEditorAction.ResetElement)
            && editor.CopyDraft().Elements["core.health"].OffsetX == beforeX,
            "native HUD undo redo and element reset use existing history owner");
        Check(editor.Dispatch(HudEditorAction.Use, name: "Acceptance HUD")
            && editor.TryTakeAccepted(out var accepted) && accepted.Name == "Acceptance HUD"
            && !editor.TryTakeAccepted(out _) && HudProfileStore.Serialize(source) == original,
            "native HUD acceptance produces one detached handoff without activating or saving layout");
        using var cancelled = new HudEditorController(source, new NoIoHudBackend());
        cancelled.SelectElement(1); cancelled.Nudge(9, 0);
        Check(cancelled.Dispatch(HudEditorAction.Cancel) && cancelled.TryTakeCancelled()
            && !cancelled.TryTakeAccepted(out _) && HudProfileStore.Serialize(source) == original,
            "native HUD cancellation cannot commit editor changes");
    }
    private sealed class NoIoHudBackend : IHudEditorBackend
    {
        public string? Warning => null;
        public void SaveNamed(string name, HudProfile profile) => throw new InvalidOperationException("Unexpected HUD persistence in detached fixture.");
        public HudProfile LoadNamed(string name) => throw new InvalidOperationException("Unexpected HUD load in detached fixture.");
    }

    private static void VideoRollback(MenuSettings settings)
    {
        var calls = new List<WindowStartMode>();
        WindowStartMode original = LauncherPrefs.WindowMode;
        using var controller = new NativeFullSettingsController(settings, calls.Add);
        var mode = Row(controller, "Display", "Window Mode");
        string next = mode.Choices.First(c => !c.Equals(mode.Value, StringComparison.OrdinalIgnoreCase));
        Check(controller.Stage(mode, next) && controller.Apply() && controller.ConfirmingVideo
            && controller.VideoSeconds is >=14 and <=15 && calls.Count == 1 && LauncherPrefs.WindowMode == original,
            "video proposal uses fake window callback and a fifteen-second rollback deadline without persistence");
        Check(!controller.CanKeepVideo&&!controller.KeepVideo()&&controller.ConfirmingVideo,"Keep requires acknowledgement of a usable video frame");
        controller.AcknowledgeVideoFrame();Check(controller.CanKeepVideo,"usable video frame enables explicit Keep");
        typeof(NativeFullSettingsController).GetField("_videoDeadline", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(controller, DateTime.UtcNow.AddMilliseconds(-1));
        controller.Tick();
        Check(!controller.ConfirmingVideo && calls.Count == 2 && calls[^1] == original && controller.Dirty,
            "expired display proposal restores old mode and retains editable draft");
        controller.Discard();
        Check(!controller.Dirty, "discard clears reverted display proposal");
        Check(controller.Stage(mode, next) && controller.Apply(), "second display proposal opens");
        controller.Dispose();
        Check(calls[^1] == original && !controller.ConfirmingVideo, "retiring settings reverts pending display proposal");
    }

    private static void Persistence(string root, MenuSettings settings)
    {
        // Initialization and every subsequent write are confined to this child.
        HudProfiles.Load(Path.Combine(root, "Savedata", "hud-profiles"));
        LauncherPrefs.DebugLogs = false;
        LauncherPrefs.ReplayAutoPrune = false;
        using var controller = new NativeFullSettingsController(settings);
        var volume = Row(controller, "Audio", "Sfx Volume (%)");
        Check(controller.Stage(volume, "64") && controller.Apply(), "Apply commits native settings through existing engine owners: " + controller.Status);
        string settingsPath = Path.Combine(root, "Savedata", "settings.json"),
            launcherPath = Path.Combine(root, "launcher.txt"), controlsPath = Path.Combine(root, "controls.txt"),
            hudPath = Path.Combine(root, "Savedata", "hud-profiles", "active.json");
        Check(new[] { settingsPath, launcherPath, controlsPath, hudPath }.All(File.Exists)
            && settings.SfxVolume == "0.64" && volume.Value == "64" && !controller.Dirty,
            "successful Apply writes all four existing owner stores and rebases percent draft");
        var before = new[] { settingsPath, launcherPath, controlsPath, hudPath }
            .ToDictionary(p => p, File.ReadAllBytes);
        string hudBefore = HudProfileStore.Serialize(HudProfiles.CopyCurrent());
        string blocker = "clear";
        var injected = new NativeSettingRow("System", "Test fault", "Controls write fault", () => blocker,
            value =>
            {
                if (value == "block") { File.Delete(controlsPath); Directory.CreateDirectory(controlsPath); }
                else if (Directory.Exists(controlsPath)) Directory.Delete(controlsPath);
                blocker = value;
            }, value => value is "clear" or "block" ? null : "Invalid fixture value.");
        controller.Rows.Add(injected);
        Check(controller.Stage(volume, "22") && controller.Stage(injected, "block") && !controller.Apply(),
            "later controls-store write failure rejects complete settings commit");
        Check(before.All(p => File.Exists(p.Key) && File.ReadAllBytes(p.Key).SequenceEqual(p.Value))
            && settings.SfxVolume == "0.64" && Math.Abs(MphRead.Sound.Sfx.Volume - .64f) < .0001f
            && HudProfileStore.Serialize(HudProfiles.CopyCurrent()) == hudBefore && !SettingsPersistence.RestartRequired,
            "failed later store restores earlier files, engine gain, live HUD and write availability");
        Check(controller.Dirty && volume.Value == "22" && controller.Status.Contains("Previous settings restored"),
            "failed transaction retains editable proposed values and reports completed rollback");
        controller.Discard();
        var bindingOwner=GamepadRuntimeConfig.Current.Bindings;
        bindingOwner.Set(PadAction.Morph,GamepadButtons.A|GamepadButtons.B|GamepadButtons.X|GamepadButtons.Y);bindingOwner.Preset="Default";
        using var legacy=new NativeFullSettingsController(settings);
        Check(legacy.Stage(Row(legacy,"Audio","Sfx Volume (%)"),"63")&&legacy.Apply(),"unrelated Apply preserves a four-alternative legacy mapping");
        Check((bindingOwner.Get(PadAction.Morph)&(GamepadButtons.X|GamepadButtons.Y))==(GamepadButtons.X|GamepadButtons.Y)
            &&File.ReadAllText(controlsPath).Contains("pad_Morph=A, B, X, Y"),"controls.txt persists complete legacy alternatives beyond two visible slots");
        var exact=bindingOwner.Clone();var persisted=File.ReadAllBytes(controlsPath);
        legacy.Rows.Add(injected);
        Check(legacy.Stage(Row(legacy,"Controls","Control layout"),"Classic")&&legacy.Stage(injected,"block")&&!legacy.Apply(),"failed named-layout commit exercises full binding rollback");
        var actualLines=new List<string>();var expectedLines=new List<string>();bindingOwner.Write(actualLines);exact.Write(expectedLines);
        Check(actualLines.SequenceEqual(expectedLines)&&bindingOwner.Preset==exact.Preset
            &&File.ReadAllBytes(controlsPath).SequenceEqual(persisted)&&!SettingsPersistence.RestartRequired,
            "failed preset restores exact legacy flags and owner-file bytes despite stale original preset label");
        legacy.Discard();
        var jump=legacy.Rows.Single(r=>r.BindingAction==PadAction.Jump&&r.BindingSlot==0);legacy.BeginCapture(jump);var snapshot=GamepadManager.Snapshot;
        legacy.PollBindingCapture(new(snapshot.DeviceId,new GamepadState{Connected=true,Buttons=GamepadButtons.X},snapshot.Revision));
        Check(legacy.BindingConflict&&legacy.ResolveBinding("Replace")&&legacy.Apply(),"Apply commits resolved hidden alternative through the full existing binding owner");
        Check((bindingOwner.Get(PadAction.Morph)&GamepadButtons.X)==0&&(bindingOwner.Get(PadAction.Morph)&GamepadButtons.Y)!=0
            &&File.ReadAllLines(controlsPath).Contains("pad_Morph=A, B, Y"),"successful Replace persists removal of only the conflicting hidden alternative");
    }

    private static NativeSettingRow Row(NativeFullSettingsController controller, string category, string label)
        => controller.Rows.Single(r => r.Category == category && r.Label == label);
    private static void Range(NativeFullSettingsController controller, string category, string label,
        string low, string high, string below, string above)
    {
        var row = Row(controller, category, label);
        Check(row.Validate(low) == null && row.Validate(high) == null && row.Validate(below) != null
            && row.Validate(above) != null && row.Validate("NaN") != null && row.Validate("Infinity") != null,
            "exact owner range and finite values enforced for " + label);
    }
    private static void Check(bool pass, string name)
    { if (!pass) throw new InvalidOperationException("Native settings: " + name); Console.WriteLine("RML97 PASS " + name); }
}
