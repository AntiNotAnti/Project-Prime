using System.Reflection;
using System.Text.Json;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Launcher.Gui;

internal static class PauseSelectionAcceptance
{
    internal static int Run(string output)
    {
        int checks = 0;
        var captures = new List<object>();
        bool oldPhone = Deck.Phone, oldStill = Deck.Still;
        void Check(bool pass, string label)
        {
            if (!pass) throw new InvalidOperationException(label);
            checks++;
            Console.WriteLine("PAUSE SELECTION UI PASS " + label);
        }
        HubNavButton? Button(PauseMenuView view, string id) => view.GetLogicalDescendants()
            .OfType<HubNavButton>().SingleOrDefault(button => button.GetValue(ControllerNav.NavIdProperty) == id);
        void Click(HubNavButton button) => ((EventHandler)typeof(HubNavButton)
            .GetField("Click", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!)
            .Invoke(button, EventArgs.Empty);
        try
        {
        Deck.Still = true;
        Dispatcher.UIThread.Invoke(() =>
        {
            var nativeOnly = new PauseMenuView(offerWindowMode: true);
            Check(Button(nativeOnly, "pause.custom.equip") == null && Button(nativeOnly, "pause.custom.native") == null,
                "Native-only snapshot omits both custom menu actions");
            var unavailable = new PauseMenuView(false, customSelection: new(new(default, false, true, false, false, false)));
            Check(Button(unavailable, "pause.custom.equip") == null && Button(unavailable, "pause.custom.native") == null,
                "Unavailable immutable snapshot omits both actions");
            var ready = new PauseMenuView(false, customSelection: new(new(default, true, true, false, false, false)));
            var equip = Button(ready, "pause.custom.equip")!;
            var native = Button(ready, "pause.custom.native")!;
            Check(equip.Label == "EQUIP RIFTLINE M7" && equip.IsEnabled
                && native.Label == "RETURN TO NATIVE" && !native.IsEnabled,
                "Ready native-selected snapshot exposes stable IDs and exact enablement");
            int equipped = 0, returned = 0, resumed = 0;
            ready.CustomEquipRequested += (_, _) => equipped++;
            ready.CustomReturnNativeRequested += (_, _) => returned++;
            ready.Resumed += (_, _) => resumed++;
            Click(equip);
            Check(equipped == 1 && returned == 0 && resumed == 0,
                "Actual equip button emits only its host request event, without resuming itself");
            string currentBinding = InputSettings.Describe(InputSettings.Current.CustomWeapon);
            Check(ready.GetLogicalDescendants().OfType<TextBlock>().Any(text =>
                text.Text?.Contains("custom slot " + currentBinding) == true
                && text.Text.Contains("when selection is unbound")),
                "Help uses actual current binding and explicit unbound menu fallback");
            ready.ReportCustomSelectionRefused();
            Check(ready.GetLogicalDescendants().OfType<TextBlock>().Any(text =>
                text.Text == "The loadout changed. Reopen the match menu and try again." && text.IsVisible)
                && resumed == 0, "Refused request has visible menu status without resuming");
            var selected = new PauseMenuView(false, customSelection: new(new(default, true, true, true, false, true)));
            Check(!Button(selected, "pause.custom.equip")!.IsEnabled && Button(selected, "pause.custom.native")!.IsEnabled,
                "Custom-selected snapshot enables native return only");
            selected.CustomReturnNativeRequested += (_, _) => returned++;
            Click(Button(selected, "pause.custom.native")!);
            Check(returned == 1, "Actual native-return button emits its host request event");
            var pending = new PauseMenuView(false, customSelection: new(new(default, true, true, true, true, true)));
            Check(!Button(pending, "pause.custom.native")!.IsEnabled
                && pending.GetLogicalDescendants().OfType<TextBlock>().Any(text => text.Text?.StartsWith("Return to native pending.") == true),
                "Pending native return is disabled and explicitly described");
            var blocked = new PauseMenuView(false, customSelection: new(new(default, true, false, false, false, true)));
            Check(!Button(blocked, "pause.custom.equip")!.IsEnabled && !Button(blocked, "pause.custom.native")!.IsEnabled,
                "Not-ready actor keeps availability visible but disables both actions");
            foreach (var (device, size) in new[] { ("desktop", new Size(940, 528)),
                ("landscape", new Size(800, 360)), ("portrait", new Size(360, 800)) })
            foreach (string state in new[] { "native", "custom", "pending", "refused" })
            {
                Deck.Phone = device != "desktop";
                bool custom = state is "custom" or "pending";
                var view = new PauseMenuView(offerWindowMode: device == "desktop",
                    customSelection: new(new(default, true, true, custom, state == "pending", custom)));
                if (state == "refused") view.ReportCustomSelectionRefused();
                HubNavButton equipButton = Button(view, "pause.custom.equip")!;
                HubNavButton nativeButton = Button(view, "pause.custom.native")!;
                int equipEvents = 0, nativeEvents = 0, resumeEvents = 0;
                view.CustomEquipRequested += (_, _) => equipEvents++;
                view.CustomReturnNativeRequested += (_, _) => nativeEvents++;
                view.Resumed += (_, _) => resumeEvents++;
                if (state == "native") Click(equipButton);
                if (state == "custom") Click(nativeButton);
                string path = Path.Combine(output, $"pause-selection-{device}-{state}.png");
                Check(UiCapture.Capture(view, path, size), "Actual pause layout captured " + device + "/" + state);
                object Geometry(Control control)
                {
                    Point start = control.TranslatePoint(default, view) ?? new(double.NaN, double.NaN);
                    Point end = control.TranslatePoint(new(control.Bounds.Width, control.Bounds.Height), view)
                        ?? new(double.NaN, double.NaN);
                    bool contained = double.IsFinite(start.X) && double.IsFinite(start.Y)
                        && start.X >= 0 && start.Y >= 0 && end.X <= size.Width + .1 && end.Y <= size.Height + .1;
                    return new { x = start.X, y = start.Y, width = end.X - start.X, height = end.Y - start.Y,
                        contained, control.IsVisible, control.IsEnabled };
                }
                var refusal = view.GetLogicalDescendants().OfType<TextBlock>().SingleOrDefault(text =>
                    text.Text == "The loadout changed. Reopen the match menu and try again.");
                Check(equipButton.IsVisible && nativeButton.IsVisible
                    && (state != "refused" || refusal?.IsVisible == true),
                    "Buttons and requested refusal status remain present " + device + "/" + state);
                Check(equipEvents == (state == "native" ? 1 : 0) && nativeEvents == (state == "custom" ? 1 : 0)
                    && resumeEvents == 0, "Captured action event stays host-controlled " + device + "/" + state);
                captures.Add(new { device, state, width = size.Width, height = size.Height, path,
                    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
                    equip = Geometry(equipButton), native = Geometry(nativeButton),
                    refusal = refusal == null ? null : Geometry(refusal), equipEvents, nativeEvents, resumeEvents });
            }
        });
        }
        finally { Deck.Phone = oldPhone; Deck.Still = oldStill; }
        File.WriteAllText(Path.Combine(output, "pause-selection-ui-evidence.json"), JsonSerializer.Serialize(new
        {
            checks, captures,
            product = typeof(PauseMenuView).Assembly.Location,
            productMvid = typeof(PauseMenuView).Assembly.ManifestModule.ModuleVersionId,
            productHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(PauseMenuView).Assembly.Location))).ToLowerInvariant(),
            scope = "Actual production Avalonia PauseMenuView constructors, stable IDs, enablement, binding text, refusal status and synthetic button event callbacks. Immutable fixture snapshots use synthetic contexts; exact UiCapture viewport PNGs and rendered hit-area geometry at940x528/800x360/360x800. Captures record containment/size; presence and synthetic events do not establish readable phone text or usable touch targets. No scene admission, resource mutation, game GL, physical input or Android device qualification."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
