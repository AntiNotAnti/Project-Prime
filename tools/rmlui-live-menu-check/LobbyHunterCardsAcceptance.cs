using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using MphRead;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class LobbyHunterCardsAcceptance
{
    internal static void Run(Action<bool, string> check)
    {
        var cards = RmlUiLobbyHunterCards.Slots;
        string[] expected = { "SAMUS", "UNAVAILABLE", "SPIRE", "KANDEN", "TRACE", "NOXUS", "SYLUX", "WEAVEL", "UNAVAILABLE", "UNAVAILABLE", "RANDOM" };
        check(cards.Select(card => card.Label).SequenceEqual(expected)
            && cards.Select(card => card.Slot).SequenceEqual(Enumerable.Range(0, 11)),
            "RML98 lobby cards preserve all eleven requested display slots");
        check(cards.Where(card => card.Hunter.HasValue).Select(card => (int)card.Hunter!.Value)
            .SequenceEqual(new[] { 0, 5, 1, 2, 4, 3, 6 }),
            "RML98 display order preserves existing numeric Hunter wire IDs");
        XDocument source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "lobby-hunter-identity.rml"));
        var buttons = source.Descendants("div").Single(element => (string?)element.Attribute("id") == "identity_cards")
            .Elements("button").ToArray();
        check(buttons.Select(button => (string?)button.Attribute("id")).SequenceEqual(cards.Select(card => card.ElementId)),
            "RML98 actual lobby modal authors the exact mapped card order");
        foreach (var card in cards)
        {
            XElement button = buttons[card.Slot];
            if (card.Hunter is { } hunter)
                check((string?)button.Attribute("data-action") == "hunter:select:" + (int)hunter
                    && button.Descendants("div").Any(viewport => (string?)viewport.Attribute("id") == "hunter_model" + (int)hunter),
                    "RML98 " + hunter + " card has a full-body model viewport and unchanged selection action");
            else if (!card.Random)
                check(button.Attribute("disabled") != null && button.Attribute("data-action") == null,
                    "RML98 placeholder slot " + card.Slot + " has no selectable Hunter identity");
            else check((string?)button.Attribute("data-action") == "hunter:random",
                "RML98 Random card uses the bounded lobby-only intent");
        }

        check(!RmlUiLobbyHunterCards.TryRandom(null, 0, out _)
            && !RmlUiLobbyHunterCards.TryRandom(Array.Empty<Hunter>(), 0, out _)
            && !RmlUiLobbyHunterCards.TryRandom(new[] { Hunter.Random, (Hunter)8, (Hunter)255 }, 0, out _),
            "RML98 empty and placeholder-only pools cannot select Random");
        Hunter[] playable = Enumerable.Range(0, 7).Select(i => (Hunter)i).ToArray();
        var fullRolls = Enumerable.Range(0, 7).Select(i =>
        {
            check(RmlUiLobbyHunterCards.TryRandom(playable, (uint)i, out Hunter hunter), "RML98 playable random roll resolves");
            return hunter;
        });
        check(fullRolls.Distinct().OrderBy(hunter => (int)hunter).SequenceEqual(playable),
            "RML98 Random includes every playable Hunter and no placeholder");
        Hunter[] restricted = { Hunter.Spire, Hunter.Trace, Hunter.Random, (Hunter)8, Hunter.Spire };
        check(Enumerable.Range(0, 4096).All(i => RmlUiLobbyHunterCards.TryRandom(restricted, (uint)i, out Hunter hunter)
            && hunter is Hunter.Spire or Hunter.Trace),
            "RML98 4096 restricted rolls select only available playable Hunters");

        var services = new Stub
        {
            LobbyView = Stub.EmptyLobby with { Active = true, Owner = true },
            HunterDraft = new(17, 9001, Hunter.Samus, 2, new[] { Hunter.Spire, Hunter.Trace }, false)
        };
        using var controller = new RmlUiMenuController(services);
        check(!controller.Dispatch(RmlUiIntentKind.HunterRandom)
            && controller.Dispatch(RmlUiIntentKind.HunterOpen), "RML98 Random requires the current live lobby modal");
        for (int i = 0; i < 64; i++)
        {
            check(controller.Dispatch(RmlUiIntentKind.HunterRandom), "RML98 actual lobby Random stages an allowed card");
            var snapshot = controller.Tick();
            check(Flag(snapshot, "hunter_choice5_selected") != Flag(snapshot, "hunter_choice2_selected")
                && !Flag(snapshot, "hunter_choice0_selected") && services.HunterApplyCalls == 0,
                "RML98 Random changes only local draft, never the identity request");
        }
        check(controller.Dispatch(RmlUiIntentKind.HunterApply) && services.HunterApplyCalls == 1
            && services.SentHunter is Hunter.Spire or Hunter.Trace && services.SentSuit == 2 && services.SentEpoch == 9001,
            "RML98 explicit Apply sends one resolved numeric Hunter and retains suit/current epoch");
        check(controller.Dispatch(RmlUiIntentKind.HunterOpen), "RML98 live selector reopens for pool removal");
        services.HunterDraft = services.HunterDraft!.Value with { Allowed = Array.Empty<Hunter>() };
        var empty = controller.Tick();
        var bindings = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Hunters, empty.Bindings);
        check(bindings["disabled:hunter_random"].Boolean && !controller.Dispatch(RmlUiIntentKind.HunterRandom)
            && !controller.Dispatch(RmlUiIntentKind.HunterApply) && services.HunterApplyCalls == 1,
            "RML98 removed live pool disables Random and prevents stale draft submission");
        check(controller.Dispatch(RmlUiIntentKind.HunterCancel) && !controller.Dispatch(RmlUiIntentKind.HunterRandom),
            "RML98 closed lobby cards cannot roll or send further changes");
    }

    private static bool Flag(RmlUiMenuSnapshot snapshot, string key)
        => snapshot.Bindings.TryGetValue(key, out var value) && value.Boolean;
}
