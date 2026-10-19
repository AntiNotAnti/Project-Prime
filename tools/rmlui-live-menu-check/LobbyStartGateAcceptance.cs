using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class LobbyStartGateAcceptance
{
    public static void Run()
    {
        var service = new Stub();
        using var controller = new RmlUiMenuController(service);
        var ready = new RmlUiLobbyView(true, false, true, true, true, true, true,
            "SYNTHETIC START GATE", "MP1 SANCTORUS", "Battle", "Free for all", "VERIFIED", 8,
            new[] { new RmlUiLobbyPlayer("Synthetic owner", "SAMUS", true, true, false) });
        foreach (var scenario in new[]
        {
            (View: ready with { CanStart = false }, Allowed: false, Label: "authority not ready"),
            (View: ready, Allowed: true, Label: "verified owner ready"),
            (View: ready with { Owner = false }, Allowed: false, Label: "foreign owner"),
            (View: ready with { Starting = true }, Allowed: false, Label: "already starting"),
            (View: ready with { Active = false }, Allowed: false, Label: "inactive session")
        })
        {
            service.LobbyView = scenario.View;
            // The production controller polls authority at eight Hz; allow a fresh projection.
            Thread.Sleep(140);
            var snapshot = controller.Tick();
            Check(snapshot.Bindings.TryGetValue("lobby_can_start", out var flag)
                && flag.Kind == RmlUiBindingKind.Boolean && flag.Boolean == scenario.Allowed,
                "snapshot publishes explicit start authority for " + scenario.Label);
            var bindings = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Lobby, snapshot.Bindings);
            Check(bindings["disabled:lobby_start"].Boolean == !scenario.Allowed,
                "actual authored Start disabled binding follows " + scenario.Label);
            int before = service.StartCalls;
            Check(controller.Dispatch(RmlUiIntentKind.LobbyStart) == scenario.Allowed
                && service.StartCalls == before + (scenario.Allowed ? 1 : 0),
                "controller forwards start exactly when allowed for " + scenario.Label);
            Check(service.LobbyView == scenario.View, "request cannot optimistically change authoritative lobby phase");
        }
        Console.WriteLine("RML LOBBY START GATE ACCEPTANCE COMPLETE");
    }
    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("RML LOBBY START PASS " + label); }
}
