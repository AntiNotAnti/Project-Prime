using System;
using System.Linq;
using System.Threading;
using MphRead;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class OfflineBotLobbyAcceptance
{
    internal static void Run()
    {
        var services = new Stub();
        services.BuiltInMaps = new[] { "MP1 SANCTORUS" };
        services.OfflineRules = new("7", "7:00", "3:00", "medium", 0);
        using var controller = new RmlUiMenuController(services);

        Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Offline),
            "offline bot lobby route opens");
        var snapshot = controller.Tick();
        var page = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Offline, snapshot.Bindings);
        Check(Text(page, "offline_heading") == "BOT LOBBY"
            && Text(page, "offline_eyebrow") == "OFFLINE // BOT LOBBY"
            && Flag(page, "visible:offline_match_panel")
            && Flag(page, "visible:offline_roster_panel")
            && !Flag(page, "visible:offline_training_panel")
            && Text(page, "offline_start") == "START BOT MATCH",
            "offline route exposes a clearly labelled local bot lobby and Start Bot Match action");
        Check(!Flag(snapshot, "lobby_mode") && !Flag(snapshot, "multiplayer_mode")
            && Text(snapshot, "offline_availability").Contains("NO NETWORK LOBBY", StringComparison.Ordinal),
            "offline lobby presentation remains local and does not project online session state");
        Check(Text(snapshot, "offline_bot_slot0_name") == "BOT 1"
            && Flag(snapshot, "offline_bot_slot0_occupied")
            && Text(snapshot, "offline_bot_slot1_name") == "OPEN SLOT"
            && !Flag(snapshot, "offline_bot_slot1_occupied")
            && Text(snapshot, "offline_bot_slot1_state") == "EMPTY"
            && Text(snapshot, "offline_roster_count") == "2 / 8 PLAYERS",
            "native roster distinguishes the local human, configured bot and empty slots");
        Check(RmlUiIntentRegistry.TryParseLegacy("offline:choice:30",
                new RmlUiDocumentToken(1, 1), 1, out var slotSeven)
            && slotSeven.Kind == RmlUiIntentKind.OfflineChoice && slotSeven.Argument == 30
            && RmlUiIntentRegistry.IsValid(slotSeven.Kind, slotSeven.Argument)
            && RmlUiIntentRegistry.TryParseLegacy("offline:previous-choice:23",
                new RmlUiDocumentToken(1, 1), 2, out _)
            && !RmlUiIntentRegistry.TryParseLegacy("offline:previous-choice:24",
                new RmlUiDocumentToken(1, 1), 3, out _)
            && !RmlUiIntentRegistry.IsValid(RmlUiIntentKind.OfflineChoice, 31)
            && !RmlUiIntentRegistry.IsValid(RmlUiIntentKind.OfflineChoicePrevious, 24)
            && RmlUiLauncherCapabilities.ConfiguredLocal.Allows(slotSeven),
            "managed offline intent parser and local capability admit only the seven bot selector slots");

        Check(!controller.Dispatch(RmlUiIntentKind.OfflineChoice, 25),
            "empty roster rows cannot select a nonexistent bot");
        for (int i = 1; i < 7; i++)
            Check(controller.Dispatch(RmlUiIntentKind.OfflineChoice, 3),
                "bot-slot count advances within the existing local draft");
        snapshot = controller.Tick();
        Check(Text(snapshot, "offline_roster_count") == "8 / 8 PLAYERS"
            && Enumerable.Range(0, 7).All(i => Flag(snapshot, $"offline_bot_slot{i}_occupied"))
            && Enumerable.Range(0, 7).All(i => Text(snapshot, $"offline_bot_slot{i}_name") == "BOT " + (i + 1)),
            "all seven bot rows fill the eight-player local roster");
        page = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Offline, snapshot.Bindings);
        Check(page.TryGetValue("class:offline_bot_slot6:occupied", out var finalSlotOccupied)
            && finalSlotOccupied.Boolean
            && page.TryGetValue("disabled:offline_bot_slot6", out var finalSlotEnabled)
            && !finalSlotEnabled.Boolean,
            "the final native roster slot is presented as occupied and selectable");

        Check(controller.Dispatch(RmlUiIntentKind.OfflineChoice, 24)
            && controller.Dispatch(RmlUiIntentKind.OfflineChoice, 18)
            && controller.Dispatch(RmlUiIntentKind.OfflineChoice, 22),
            "first bot row selects its independent hunter and damage-reduction profile");
        string firstHunter = Text(controller.Tick(), "offline_bot_hunter_value");
        string firstDetail = Text(controller.Tick(), "offline_bot_slot0_detail");
        Check(firstHunter != "RANDOM" && firstDetail.Contains(firstHunter, StringComparison.Ordinal)
            && Text(controller.Tick(), "offline_bot_selected_value") == "BOT 1 / 7",
            "selected roster row and bot editor describe the same first bot");

        Check(controller.Dispatch(RmlUiIntentKind.OfflineChoice, 25)
            && controller.Dispatch(RmlUiIntentKind.OfflineChoice, 19)
            && controller.Dispatch(RmlUiIntentKind.OfflineChoice, 20)
            && Text(controller.Tick(), "offline_bot_selected_value") == "BOT 2 / 7"
            && Text(controller.Tick(), "offline_bot_suit_value") == "SUIT 2"
            && Text(controller.Tick(), "offline_bot_skill_value") == "HARD"
            && Text(controller.Tick(), "offline_bot_slot0_detail") == firstDetail
            && Text(controller.Tick(), "offline_bot_slot1_detail").Contains("SUIT 2", StringComparison.Ordinal),
            "direct roster selection preserves each bot's separate hunter, suit, difficulty and damage settings");

        Check(controller.Dispatch(RmlUiIntentKind.OfflineChoice, 2)
            && Flag(controller.Tick(), "offline_teams")
            && controller.Dispatch(RmlUiIntentKind.OfflineChoice, 23)
            && controller.Dispatch(RmlUiIntentKind.OfflineChoice, 21),
            "team matchup keeps both local-player and selected-bot team controls active");
        Check(controller.Dispatch(RmlUiIntentKind.OfflineChoice, 5)
            && controller.Dispatch(RmlUiIntentKind.OfflineChoice, 6)
            && Text(controller.Tick(), "offline_suit_value") == "SUIT 2",
            "the local player keeps independent hunter and suit selection");
        string selectedLocalHunter = Text(controller.Tick(), "offline_hunter_value");
        string selectedBotHunter = Text(controller.Tick(), "offline_bot_hunter_value");
        Check(controller.Dispatch(RmlUiIntentKind.OfflineChoice, 30)
            && Text(controller.Tick(), "offline_bot_selected_value") == "BOT 7 / 7"
            && controller.Dispatch(RmlUiIntentKind.OfflineChoice, 24)
            && Text(controller.Tick(), "offline_bot_selected_value") == "BOT 1 / 7",
            "the first and seventh roster rows select the exact configured bot");

        services.OfflineValidationPending.SetResult("");
        Check(controller.Dispatch(RmlUiIntentKind.OfflineLaunchMatch),
            "Start Bot Match enters the existing validated OfflineLaunch path");
        for (int i = 0; i < 100 && services.OfflineLaunches == 0; i++)
        {
            controller.Tick();
            Thread.Sleep(10);
        }
        var request = services.LastOffline;
        Check(services.OfflineValidations == 1 && services.OfflineLaunches == 1
            && request.ValidateForm(services.BuiltInMaps)
            && request.Bots == 7 && request.Mode == GameMode.BattleTeams
            && request.Hunter.ToString().ToUpperInvariant() == selectedLocalHunter
            && request.Suit == 1 && request.LocalTeam == 1
            && request.BotRoster.Length == 7
            && request.BotRoster[0].Hunter.ToString().ToUpperInvariant() == firstHunter
            && request.BotRoster[0].Suit == 0
            && request.BotRoster[0].Handicap > 0
            && request.BotRoster[1].Hunter == Hunter.Random
            && request.BotRoster[1].Suit == 1 && request.BotRoster[1].Level == 2
            && request.BotRoster[1].Team == 0,
            "validated offline launch carries the selected local loadout and all independent bot profiles");

        Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Training)
            && Text(controller.Tick(), "offline_heading_label") == "AIM LAB"
            && Flag(controller.Tick(), "offline_training_mode")
            && !Flag(RmlUiMenuPageBindings.Project(RmlUiMenuPage.Offline, controller.Tick().Bindings), "visible:offline_roster_panel")
            && controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Offline)
            && Text(controller.Tick(), "offline_heading_label") == "BOT LOBBY"
            && Text(controller.Tick(), "offline_bot_slot0_detail").Contains("SUIT 1", StringComparison.Ordinal),
            "Aim Lab retains its existing route and the shared offline draft returns with bot profiles intact");
    }

    private static void Check(bool pass, string name)
    {
        if (!pass) throw new InvalidOperationException("Offline bot lobby: " + name);
        Console.WriteLine("RML OFFLINE PASS " + name);
    }

    private static bool Flag(RmlUiMenuSnapshot snapshot, string key)
        => snapshot.Bindings.TryGetValue(key, out var value)
            && value.Kind == RmlUiBindingKind.Boolean && value.Boolean;

    private static bool Flag(System.Collections.Generic.IReadOnlyDictionary<string, RmlUiBindingValue> values, string key)
        => values.TryGetValue(key, out var value)
            && value.Kind == RmlUiBindingKind.Boolean && value.Boolean;

    private static string Text(RmlUiMenuSnapshot snapshot, string key)
        => snapshot.Bindings.TryGetValue(key, out var value) ? value.Text : "";

    private static string Text(System.Collections.Generic.IReadOnlyDictionary<string, RmlUiBindingValue> values, string key)
        => values.TryGetValue(key, out var value) ? value.Text : "";
}
