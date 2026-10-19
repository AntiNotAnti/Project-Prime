using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Network;

internal static class MapPickerCatalogAcceptance
{
    internal static void Run()
    {
        CategoriesAndGrouping(); AsyncPublicationAndBindings(); RetirementAndErrors();
    }
    private static void CategoriesAndGrouping()
    {
        Check((int)RmlUiArenaCategory.Mph == 0 && (int)RmlUiArenaCategory.Community == 1
            && (int)RmlUiArenaCategory.Echoes == 2 && (int)RmlUiArenaCategory.Halo == 3 && (int)RmlUiArenaCategory.Quake == 4,
            "all five category commands preserve stable Community index one");
        string[] vanilla = Enumerable.Range(0, 8).Select(i => "MPH " + i).Reverse().ToArray();
        var model = new RmlUiMapPickerModel(vanilla.Append("mph 0"), Array.Empty<string>());
        var custom = Enumerable.Range(1, 4).SelectMany(category => Enumerable.Range(0, 8).Reverse()
            .Select(i => new RmlUiArenaChoice("CUSTOM " + category + " " + i, (RmlUiArenaCategory)category))).ToArray();
        model.LoadInstalled(custom.Concat(new[] { custom[0] with { Room = custom[0].Room.ToLowerInvariant() },
            new RmlUiArenaChoice("MPH 0", RmlUiArenaCategory.Halo),
            new RmlUiArenaChoice("", RmlUiArenaCategory.Community),
            new RmlUiArenaChoice(new string('x', 257), RmlUiArenaCategory.Community),
            new RmlUiArenaChoice("WRONG CATEGORY", (RmlUiArenaCategory)5) }).ToArray());
        for (int category = 0; category < 5; category++)
        {
            Check(model.SelectCategory(category) && model.Page == 0 && !model.HasPrevious,
                "valid category resets paging to first six-card page");
            var expected = category == 0 ? vanilla.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
                : custom.Where(c => category == 1 || (int)c.Category == category).Select(c => c.Room)
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            Check(model.Count == expected.Length && model.Current.SequenceEqual(expected),
                "category normalizes sorting and duplicates without losing Community superset");
            Check(model.Detail(0)?.Room == model.Choice(0), "map detail resolves exact current sorted card identity");
            var seen = new List<string>();
            do
            {
                seen.AddRange(Enumerable.Range(0, 6).Select(model.Choice).Where(x => x != null).Select(x => x!));
                Check(model.Choice(-1) == null && model.Choice(6) == null, "map card indices remain within six-card page");
            } while (model.NextPage());
            Check(seen.SequenceEqual(expected) && !model.NextPage(), "paged category visits each installed identity exactly once");
            while (model.PreviousPage()) { }
            Check(model.Page == 0 && !model.PreviousPage(), "category previous paging stops at zero");
        }
        Check(!model.SelectCategory(-1) && !model.SelectCategory(5), "unknown category five never aliases Quake");
        model.SelectCategory(1); var before = model.Current.ToArray();
        model.LoadInstalled(new[] { new RmlUiArenaChoice("LATE REORDER", RmlUiArenaCategory.Community) });
        Check(model.Current.SequenceEqual(before), "installed catalog publishes only once without rearranging actionable cards");
        foreach (string title in new[] { "Sidehopper Station", "Shooting Gallery", "Crossfire Chaos", "Spider Complex", "Pipeline", "Spires" })
            Check(RmlUiMapPickerModel.Group("opaque-installed-package", title, "", false) == RmlUiArenaCategory.Echoes,
                "installed Echoes title grouped for display: " + title);
        Check(RmlUiMapPickerModel.Group("echoes_faithful_sidehopperstation_v1", "Sidehopper Station", "", false) == RmlUiArenaCategory.Echoes
            && RmlUiMapPickerModel.Group("echoes_faithful_shootinggallery_v1", "Shooting Gallery", "", false) == RmlUiArenaCategory.Echoes,
            "legacy faithful Echoes installed keys retain familiar theme grouping");
        foreach (string title in new[] { "Midship", "Haven", "Guardian", "Lockout", "Blood Gulch", "Bloodgulch", "Valhalla" })
            Check(RmlUiMapPickerModel.Group("installed-package", title, "", false) == RmlUiArenaCategory.Halo,
                "installed Halo title grouped for display: " + title);
        Check(RmlUiMapPickerModel.Group("custom", "Custom Arena", "Imported Quake arena", true) == RmlUiArenaCategory.Quake
            && RmlUiMapPickerModel.Group("custom", "Custom Arena", "", false) == RmlUiArenaCategory.Community
            && RmlUiMapPickerModel.Group("custom", "Havenwood", "", false) == RmlUiArenaCategory.Community,
            "Quake import fallback and display-name word boundaries avoid false theme provenance");
        Check(RmlUiMapPickerModel.Group("custom", "Custom", "Faithful Metroid Prime 2 arena", false) == RmlUiArenaCategory.Echoes
            && RmlUiMapPickerModel.Group("custom", "Custom", new string('x', 2048) + " Halo", false) == RmlUiArenaCategory.Community,
            "bounded description grouping recognizes Echoes and ignores unbounded trailing hints");
    }
    private static void AsyncPublicationAndBindings()
    {
        var services = Services(); var pending = new TaskCompletionSource<IReadOnlyList<RmlUiArenaChoice>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default; int ownerThread = Environment.CurrentManagedThreadId, previewThread = 0;
        services.MapCatalog = token => { captured = token; return pending.Task; };
        services.MapPreviews = rooms =>
        {
            previewThread = Environment.CurrentManagedThreadId;
            return rooms.Count == 0 ? Array.Empty<RmlUiArenaPreview>() : rooms.Select((room, i) => room.Length == 0 ? default
                : new RmlUiArenaPreview(i == 0 ? "/synthetic/existing-map-preview.tga" : "", i == 0 ? "EXACT FIXTURE IMAGE" : "GENERATING MAP PREVIEW")).ToArray();
        };
        using var controller = new RmlUiMenuController(services);
        int watchdogFired = 0;
        using (var watchdog = new Timer(_ => { Interlocked.Exchange(ref watchdogFired, 1); pending.TrySetException(new TimeoutException("WATCHDOG")); }, null, 4000, Timeout.Infinite))
            Check(controller.Dispatch(RmlUiIntentKind.LobbyMapOpen) && Volatile.Read(ref watchdogFired) == 0 && !pending.Task.IsCompleted,
                "map picker opens without waiting for held installed-catalog task");
        Check(controller.MapPickerOpen && Text(controller, "map_choice0_name") == "MP1 SANCTORUS"
            && Flag(controller, "map_choice0_enabled") && previewThread == ownerThread,
            "built-in MPH card remains immediately selectable and preview bindings run on owner");
        var bindings = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Maps, controller.Tick().Bindings);
        Check(bindings["image:map_image0"].Text == "/synthetic/existing-map-preview.tga"
            && bindings["visible:map_image0"].Boolean && !bindings["visible:map_art_label0"].Boolean
            && !bindings["visible:map_image1"].Boolean && bindings["visible:map_art_label1"].Boolean
            && bindings["map_art_label1"].Text == "GENERATING MAP PREVIEW",
            "preview binding shows actual image path and hides placeholder only when image is ready");
        Check(controller.Dispatch(RmlUiIntentKind.LobbyMapCategory, 1) && Flag(controller, "map_empty")
            && Text(controller, "map_empty_text").Contains("VERIFYING INSTALLED MAPS") && !captured.IsCancellationRequested,
            "custom tab remains empty with bounded status while catalog is pending");
        var oldSnapshot = controller.Tick();
        Task.Run(() => pending.TrySetResult(new[] { new RmlUiArenaChoice("LOCAL ONLY PACKAGE", RmlUiArenaCategory.Echoes, false, "LOCAL ONLY // UNAVAILABLE IN LOBBY") })).GetAwaiter().GetResult();
        Check(oldSnapshot.Bindings["map_choice0_name"].Text == "" && previewThread == ownerThread,
            "worker completion cannot mutate prior presentation snapshot or call UI preview callback");
        Await(() => Text(controller, "map_choice0_name") == "LOCAL ONLY PACKAGE", "owner Tick publishes completed installed catalog");
        Check(!Flag(controller, "map_choice0_enabled") && !controller.Dispatch(RmlUiIntentKind.LobbyMapSelect, 0)
            && controller.MapPickerOpen && services.Submissions == 0,
            "local-only installed package is visible but cannot alter lobby rules or submit");
        Check(controller.Dispatch(RmlUiIntentKind.LobbyMapCategory, 2) && Text(controller, "map_choice0_name") == "LOCAL ONLY PACKAGE"
            && !controller.Dispatch(RmlUiIntentKind.LobbyMapCategory, 5), "themed view shares installed identity and rejects wrong category index");
        Check(controller.Dispatch(RmlUiIntentKind.LobbyMapCategory, 0) && controller.Dispatch(RmlUiIntentKind.LobbyMapSelect, 0)
            && !controller.MapPickerOpen && Flag(controller, "lobby_rules_open") && services.Submissions == 0,
            "pending catalog never prevents staged MPH selection through existing rule draft");
    }
    private static void RetirementAndErrors()
    {
        var services = Services(); var requests = new List<(TaskCompletionSource<IReadOnlyList<RmlUiArenaChoice>> Work, CancellationToken Token)>();
        services.MapCatalog = token => { var work = new TaskCompletionSource<IReadOnlyList<RmlUiArenaChoice>>(TaskCreationOptions.RunContinuationsAsynchronously); requests.Add((work, token)); return work.Task; };
        using var controller = new RmlUiMenuController(services);
        Check(controller.Dispatch(RmlUiIntentKind.LobbyMapOpen) && controller.Dispatch(RmlUiIntentKind.LobbyMapSelect, 0)
            && requests[0].Token.IsCancellationRequested, "MPH selection cancels still-pending custom catalog");
        requests[0].Work.TrySetResult(new[] { new RmlUiArenaChoice("LATE SELECTED CATALOG", RmlUiArenaCategory.Halo) }); controller.Tick();
        Check(!controller.MapPickerOpen && Text(controller, "rules_map") == "MP1 SANCTORUS", "late selected catalog cannot change staged rule map");
        controller.Dispatch(RmlUiIntentKind.LobbyRulesClose);
        Check(controller.Dispatch(RmlUiIntentKind.LobbyMapOpen) && controller.Dispatch(RmlUiIntentKind.LobbyMapClose)
            && requests[1].Token.IsCancellationRequested && !Flag(controller, "lobby_rules_open"),
            "direct picker close cancels catalog and its privately opened rules");
        Check(controller.Dispatch(RmlUiIntentKind.LobbyMapOpen), "replacement picker begins fresh catalog generation");
        requests[1].Work.TrySetResult(new[] { new RmlUiArenaChoice("RETIRED OLD PACKAGE", RmlUiArenaCategory.Halo) });
        requests[2].Work.TrySetResult(new[] { new RmlUiArenaChoice("CURRENT PACKAGE", RmlUiArenaCategory.Halo) });
        controller.Dispatch(RmlUiIntentKind.LobbyMapCategory, 1);
        Await(() => Text(controller, "map_choice0_name") == "CURRENT PACKAGE", "current completed catalog wins over retired earlier task");
        Check(!controller.Tick().Bindings.Values.Any(v => v.Text?.Contains("RETIRED OLD PACKAGE") == true), "late old task cannot replace current picker identities");
        controller.Dispatch(RmlUiIntentKind.LobbyMapClose);
        controller.Dispatch(RmlUiIntentKind.LobbyMapOpen);
        services.AuthorityRules = services.AuthorityRules!.Value with { AuthorityEpoch = 123456 };
        controller.Tick();
        Check(requests[3].Token.IsCancellationRequested && !controller.MapPickerOpen && !Flag(controller, "lobby_rules_open"),
            "changed live rule authority retires picker and pending catalog");
        requests[3].Work.TrySetResult(new[] { new RmlUiArenaChoice("OLD ROOT PACKAGE", RmlUiArenaCategory.Community) }); controller.Tick();
        Check(!controller.MapPickerOpen && !Flag(controller, "lobby_rules_open"), "late root-context catalog cannot recreate rules editor");
        services.MapCatalog = _ => Task.FromException<IReadOnlyList<RmlUiArenaChoice>>(new InvalidOperationException("PRIVATE ROOT /private/user/files/token"));
        Check(controller.Dispatch(RmlUiIntentKind.LobbyMapOpen) && controller.Dispatch(RmlUiIntentKind.LobbyMapCategory, 1), "failed catalog still opens native custom tab");
        Await(() => Text(controller, "map_empty_text").Contains("CATALOG UNAVAILABLE"), "catalog failure exposes bounded unavailable status");
        Check(!Text(controller, "map_empty_text").Contains("PRIVATE ROOT") && !Text(controller, "map_empty_text").Contains("/private/user"),
            "catalog failure never exposes root paths or raw service exceptions");
        controller.Dispatch(RmlUiIntentKind.LobbyMapClose);
        services.MapCatalog = _ => throw new InvalidOperationException("PRIVATE ROOT SYNC THROW");
        Check(controller.Dispatch(RmlUiIntentKind.LobbyMapOpen) && controller.Dispatch(RmlUiIntentKind.LobbyMapCategory, 1)
            && Text(controller, "map_empty_text").Contains("CATALOG UNAVAILABLE")
            && !Text(controller, "map_empty_text").Contains("PRIVATE ROOT"), "synchronous catalog start failure is also redacted");
        controller.Dispatch(RmlUiIntentKind.LobbyMapClose);
        services.MapCatalog = token => { var work = new TaskCompletionSource<IReadOnlyList<RmlUiArenaChoice>>(TaskCreationOptions.RunContinuationsAsynchronously); requests.Add((work, token)); return work.Task; };
        controller.Dispatch(RmlUiIntentKind.LobbyMapOpen); var disposed = requests.Last(); controller.Dispose();
        Check(disposed.Token.IsCancellationRequested, "controller disposal cancels last held catalog");
        disposed.Work.TrySetException(new InvalidOperationException("PRIVATE LATE DISPOSAL"));
        Check(services.Submissions == 0, "all asynchronous catalog outcomes remain presentation-only without rule submission");
    }
    private static Stub Services() => new()
    {
        LobbyView = new(true, false, true, false, false, true, true, "SYNTHETIC MAP PICKER", "MP1 SANCTORUS", "Battle", "FreeForAll", "READY", 8, Array.Empty<RmlUiLobbyPlayer>()),
        AuthorityRules = new(5, 999, 17, new MatchDefinition { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle, Format = MatchFormat.FreeForAll, PointGoal = 7, TimeLimitSeconds = 420 }, LobbyRuleFlags.RequireReady),
        BuiltInMaps = new[] { "MP1 SANCTORUS", "MP2 HIGHGROUND" }
    };
    private static string Text(RmlUiMenuController controller, string key) => controller.Tick().Bindings.TryGetValue(key, out var value) ? value.Text : "";
    private static bool Flag(RmlUiMenuController controller, string key) => controller.Tick().Bindings.TryGetValue(key, out var value) && value.Boolean;
    private static void Await(Func<bool> predicate, string label)
    { var timer = System.Diagnostics.Stopwatch.StartNew(); while (!predicate()) { if (timer.ElapsedMilliseconds > 3000) throw new TimeoutException(label); Thread.Sleep(5); } Check(true, label); }
    private static void Check(bool pass, string label)
    { if (!pass) throw new InvalidOperationException(label); Console.WriteLine("MAP PICKER CATALOG PASS " + label); }
}
