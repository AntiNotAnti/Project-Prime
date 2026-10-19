using System;
using System.Collections.Generic;
using System.Linq;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class LobbyBotAcceptance
{
    public static void Run()
    {
        RmlUiAdminSnapshot roster = Roster(2);
        RmlUiLobbyView lobby = Lobby(roster);
        var services = new Stub { LobbyView = lobby, AdminState = roster };
        using (var controller = new RmlUiMenuController(services))
        {
            var model = controller.Tick();
            var bindings = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Lobby, model.Bindings);
            Check(!RmlUiLobbyDisplayOrder.CanAddBot(lobby, roster, 0)
                && RmlUiLobbyDisplayOrder.Resolve(roster, 0)?.Slot == 6,
                "occupied local-front display row stays occupied even when physical network slot zero is free");
            for (int i = 0; i < 8; i++)
            {
                bool expected = i is 2 or 3;
                Check(Flag(model.Bindings, "slot" + i + "_can_add_bot") == expected
                    && Flag(bindings, "visible:lobby_add_bot" + i) == expected
                    && Flag(bindings, "disabled:lobby_add_bot" + i) == !expected,
                    "display row " + i + " projects the exact authoritative capacity into ADD BOT visibility and enabled state");
            }
            Check(services.AdminCalls == 0, "rendering available capacity never creates a bot");
            Check(controller.Dispatch(RmlUiIntentKind.LobbyPlayerSelect, 3)
                && services.AdminCalls == 1 && !controller.AdminOpen,
                "clicking an eligible empty capacity row submits one existing AddBot command without opening player administration");
            RmlUiAdminRequest request = services.LastAdmin;
            Check(request.Operation == RmlUiAdminOperation.AddBot
                && request.MatchId == roster.MatchId && request.AuthorityEpoch == roster.AuthorityEpoch
                && request.SessionRevision == roster.SessionRevision
                && request.TargetSlot == byte.MaxValue && request.TargetGeneration == 0
                && request.Hunter == Hunter.Random && request.Suit == 0 && request.BotLevel == 1
                && request.Team == -1 && request.Handicap == 0,
                "AddBot captures current match epoch/revision and server-selected slot with Random Hunter, suit zero and normal difficulty");
            model = controller.Tick();
            Check(ReferenceEquals(services.AdminState, roster) && services.AdminState?.Players.Length == 2
                && services.LobbyView.Players.Length == 2
                && !Flag(model.Bindings, "slot3_occupied")
                && Text(model.Bindings, "lobby_player_count") == "2 / 4",
                "request acknowledgement never inserts a synthetic bot or increments displayed player count");
        }

        var full = Roster(4);
        var cases = new (string Name, RmlUiLobbyView Lobby, RmlUiAdminSnapshot? Roster, int Index)[]
        {
            ("inactive lobby", lobby with { Active = false }, roster, 2),
            ("starting lobby", lobby with { Starting = true }, roster, 2),
            ("non-owner lobby", lobby with { Owner = false }, roster, 2),
            ("non-owner authoritative roster", lobby, roster with { Owner = false }, 2),
            ("pending server command", lobby, roster with { CommandPending = true }, 2),
            ("full authoritative roster", Lobby(full), full, 4),
            ("missing authoritative roster", lobby, null, 2),
            ("invalid match identity", lobby, roster with { MatchId = 0 }, 2),
            ("invalid authority epoch", lobby, roster with { AuthorityEpoch = 0 }, 2),
            ("zero server capacity", lobby with { MaxPlayers = 0 }, roster with { MaxPlayers = 0 }, 2),
            ("server capacity beyond eight", lobby with { MaxPlayers = 9 }, roster with { MaxPlayers = 9 }, 2),
            ("disagreeing presentation capacity", lobby with { MaxPlayers = 8 }, roster, 2),
            ("missing server player array", lobby, roster with { Players = null! }, 2),
            ("negative display index", lobby, roster, -1),
            ("index at server capacity", lobby, roster, 4),
            ("index outside eight-row roster", lobby, roster, 8),
            ("overflow-sized display index", lobby, roster, int.MaxValue),
            ("presentation already has an occupant", lobby with
                { Players = lobby.Players.Concat(new[] { new RmlUiLobbyPlayer("Arriving", "TRACE", false, false, false) }).ToArray() }, roster, 2)
        };
        foreach (var item in cases)
        {
            Check(!RmlUiLobbyDisplayOrder.CanAddBot(item.Lobby, item.Roster, item.Index),
                item.Name + " does not advertise AddBot capacity");
            var rejected = new Stub { LobbyView = item.Lobby, AdminState = item.Roster };
            using var controller = new RmlUiMenuController(rejected);
            Check(!controller.Dispatch(RmlUiIntentKind.LobbyPlayerSelect, item.Index) && rejected.AdminCalls == 0,
                item.Name + " never reaches the AddBot authority");
        }

        var stale = new Stub { LobbyView = lobby, AdminState = roster };
        using (var controller = new RmlUiMenuController(stale))
        {
            Check(Flag(controller.Tick().Bindings, "slot2_can_add_bot"),
                "stale-click fixture initially renders an empty eligible row");
            var joined = Roster(3) with { SessionRevision = 42 };
            stale.AdminState = joined;
            stale.LobbyView = Lobby(joined);
            controller.Dispatch(RmlUiIntentKind.LobbyPlayerSelect, 2);
            Check(stale.AdminCalls == 0 && controller.AdminOpen
                && Text(controller.Tick().Bindings, "admin_selected_name") == "Hunter 1",
                "a newly occupied row is freshly resolved to its current player and never receives the stale AddBot request");
        }
        Console.WriteLine("RML-83 LOBBY ADD BOT CAPABILITY/MODEL/DISPATCH ACCEPTANCE COMPLETE // FAKE AUTHORITY, NO NETWORK");
    }

    private static RmlUiAdminSnapshot Roster(int count)
    {
        byte[] slots = { 6, 2, 0, 7, 4, 1, 3, 5 };
        return new(23, 5678, 41, true, 6, 6, false, false, 0, 4, false, "READY", "",
            new[] { Hunter.Samus, Hunter.Kanden, Hunter.Trace },
            Enumerable.Range(0, count).Select(i => new RmlUiAdminPlayer(slots[i], (ushort)(10 + i),
                "Hunter " + i, Hunter.Samus, 0, -1, false, false, 0, 0, 25)).ToArray(),
            Array.Empty<string>());
    }

    private static RmlUiLobbyView Lobby(RmlUiAdminSnapshot roster)
        => new(true, false, true, false, false, true, false, "CPU lobby", "MP1 SANCTORUS", "Battle",
            "FreeForAll", "READY", roster.MaxPlayers, RmlUiLobbyDisplayOrder.Ordered(roster)
                .Select(row => new RmlUiLobbyPlayer(row.Name, row.Hunter.ToString(), row.Ready,
                    row.Slot == roster.LocalSlot, row.Bot)).ToArray());

    private static bool Flag(IReadOnlyDictionary<string, RmlUiBindingValue> model, string key)
        => model.TryGetValue(key, out var value) && value.Kind == RmlUiBindingKind.Boolean && value.Boolean;
    private static string Text(IReadOnlyDictionary<string, RmlUiBindingValue> model, string key)
        => model.TryGetValue(key, out var value) ? value.Text : "";
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("RML LOBBY BOT FAIL " + label);
        Console.WriteLine("RML LOBBY BOT PASS " + label);
    }
}
