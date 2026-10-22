using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class LobbyGeometryAcceptance
{
    internal static int Run(string assets)
    {
        int checks = 0;
        void Check(bool pass, string name)
        {
            if (!pass) throw new InvalidOperationException("LOBBY GEOMETRY: " + name);
            checks++;
        }
        foreach (var size in new[] { (1280, 720, 1f), (1920, 1080, 1f),
            (2560, 1440, 2f), (1920, 1080, 1.5f), (3440, 1440, 1f), (1920, 1080, 2f) })
        foreach (bool accessible in new[] { false, true })
        {
            using var host = new RmlUiHost();
            Check(host.Initialize(size.Item1, size.Item2, size.Item3,
                Path.GetFullPath(assets), RmlUiRenderBackend.DrawList), "native CPU context initializes");
            using var pages = new RmlUiPageManager(host);
            var doc = pages.OpenPage(new("lobby-geometry", "pages/lobby/lobby.rml", "lobby_chat_input"));
            host.SetBool(doc, "class:@document:reduce-motion", true);
            host.SetBool(doc, "class:@document:touch-targets", accessible);
            host.SetBool(doc, "class:@document:large-text", accessible);
            host.SetBool(doc, "visible:lobby_countdown", false);
            host.SetBool(doc, "visible:lobby_ready", true);
            host.SetBool(doc, "visible:lobby_start", true);
            host.SetBool(doc, "class:lobby_slot0:local", true);
            host.SetText(doc, "lobby_brief_title", "MULTIPLAYER LOBBY");
            for (int slot = 0; slot < 8; slot++)
            {
                host.SetBool(doc, "visible:lobby_slot" + slot, true);
                host.SetBool(doc, "visible:lobby_add_bot" + slot, false);
                host.SetText(doc, $"text_slot{slot}_name_1", "HUNTER " + (slot + 1));
                host.SetText(doc, $"text_slot{slot}_hunter_1", "SAMUS");
                host.SetText(doc, $"text_slot{slot}_state_1", "READY");
                host.SetText(doc, $"text_slot{slot}_name_2", "HUNTER " + (slot + 1));
                host.SetText(doc, $"text_slot{slot}_hunter_2", "SAMUS");
                var pad = LauncherLobbyFormation.At(slot);
                host.SetLobbyAnchor(slot, pad.LabelX, pad.LabelY);
            }
            host.Update(); host.Update();
            (float X, float Y, float W, float H) Bounds(string id)
            {
                Check(host.TryGetElementBounds(doc, id, out float x, out float y, out float w, out float h)
                    && w > 0 && h > 0, "laid-out " + id);
                return (x, y, w, h);
            }
            var actions = Bounds("lobby_actions");
            for (int slot = 0; slot < 8 && size.Item1 / size.Item3 > 1000; slot++)
            {
                var plate = Bounds("lobby_slot" + slot);
                var pad = LauncherLobbyFormation.At(slot);
                Check(Math.Abs(plate.X + plate.W / 2 - pad.PadX * size.Item1) <= 1,
                    $"{size} accessibility={accessible}: plate {slot + 1} centered under its Hunter");
                Check(plate.Y + plate.H <= actions.Y + 1, "plate stays above actions");
                float expectedCenterY = Math.Min(pad.LabelY * size.Item2,
                    actions.Y - plate.H / 2 - 8);
                if (slot == 7)
                {
                    var brief = Bounds("lobby_brief");
                    expectedCenterY = Math.Max(expectedCenterY,
                        brief.Y + brief.H + 8 * size.Item3 + plate.H / 2);
                    Check(plate.Y + plate.H < pad.PadY * size.Item2,
                        "eighth plate stays above its Hunter platform");
                }
                Check(Math.Abs(plate.Y + plate.H / 2 - expectedCenterY) <= 1,
                    $"{size} accessibility={accessible}: plate {slot + 1} follows its authored vertical anchor and action clearance");
            }
            var roster = Bounds("lobby_roster_panel");
            var last = Bounds("visible_slot7_occupied_1");
            var chat = Bounds("lobby_chat_panel");
            var rail = Bounds("lobby_right_panels");
            Check(chat.Y >= roster.Y + roster.H - 1 && chat.Y - roster.Y - roster.H <= 13 * size.Item3,
                "chat immediately follows roster with the authored gap");
            Check(Math.Abs(chat.Y + chat.H - rail.Y - rail.H) <= 2,
                "chat fills the remaining right rail");
            var start = Bounds("lobby_start");
            if (size.Item1 / size.Item3 > 1000)
                Check(Math.Abs(chat.Y + chat.H - start.Y - start.H) <= 2,
                    "desktop chat bottom aligns with Start Match bottom");
            else
                Check(chat.Y + chat.H < actions.Y,
                    "compact chat stays above its full-width action row");
            if (roster.H >= last.Y + last.H - roster.Y)
                Check(roster.Y + roster.H - last.Y - last.H <= 12 * size.Item3,
                    "no empty expanding panel beneath eighth roster row");
            Console.WriteLine($"LOBBY GEOMETRY {size} accessibility={accessible} roster={roster.H:F1} chat={chat.H:F1}");
        }
        Console.WriteLine($"LOBBY GEOMETRY PASS {checks} native bounds checks.");
        return 0;
    }
}
