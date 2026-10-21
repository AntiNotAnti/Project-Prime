using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

internal static class CommunityHostingAcceptance
{
    internal static void Run()
    {
        string root = GameFiles.Root;
        try
        {
            GameFiles.Root = "/synthetic/community-host-root";
            SubmitAndRetry();
            CancelAndCycle();
            RefuseStaleAndInvalid();
            NoBuiltInArenas();
        }
        finally { GameFiles.Root = root; }
    }

    private static NativeCommunityHostSelection Target() => new(GameFiles.Root,
        new MapContentIdentity(Guid.Parse("bf8d71da-2c44-4a87-b08f-f90fce089c5b"),
            "SYNTHETIC COMMUNITY ARENA", new MapHash256(1, 2, 3, 4), new MapHash256(5, 6, 7, 8), true));

    private static void NoBuiltInArenas()
    {
        var services = new Stub { BuiltInMaps = Array.Empty<string>() };
        using var controller = new RmlUiMenuController(services);
        Check(!controller.Dispatch(RmlUiIntentKind.PlayCreateOpen) && !Flag(controller, "play_create_mode")
            && services.LegacyCreateCalls == 0 && services.CreateCalls == 0
            && controller.Tick().Bindings.Values.Any(value => value.Text == "NO MPH ARENAS AVAILABLE // SET UP GAME FILES OR HOST AN INSTALLED COMMUNITY MAP"),
            "empty built-in catalog retains native browser with actionable status and no legacy create handoff");
    }

    private static void SubmitAndRetry()
    {
        var services = new Stub { LobbyCapacity = 4 };
        var target = Target();
        using var controller = new RmlUiMenuController(services);
        Check(controller.OpenCreateLobbyForInstalled(target)
            && Flag(controller, "play_create_mode")
            && Text(controller, "play_create_map") == target.Identity.RoomKey
            && Text(controller, "play_create_mode_name") == "Battle"
            && Text(controller, "play_create_host") == "HOSTED // ONLINE"
            && Text(controller, "play_create_capacity").StartsWith("4 PLAYER SLOTS", StringComparison.Ordinal)
            && services.CreateCalls == 0, "exact installed target opens reviewable native Battle/Hosted form with map capacity");
        Check(controller.Dispatch(RmlUiIntentKind.PlayCreate, playerName: "Pilot", lobbyName: "Community Night")
            && controller.Creating, "explicit submit commits authoritative creation");
        Await(controller, () => services.CreateCalls == 1);
        Check(ReferenceEquals(services.CreatedRequest.CommunityMap, target)
            && services.CreatedRequest.CommunityMap!.ConfiguredRoot == GameFiles.Root
            && services.CreatedRequest.RoomKey == target.Identity.RoomKey
            && services.CreatedRequest.MapIdentity.MapId == target.Identity.MapId
            && services.CreatedRequest.MapIdentity.ContentHash == target.Identity.ContentHash
            && services.CreatedRequest.MapIdentity.PackageHash == target.Identity.PackageHash
            && services.CreatedRequest.MaxPlayers == 4
            && services.CreatedRequest.Mode == GameMode.Battle
            && services.CreatedRequest.Format == MatchFormat.Auto
            && !services.CreatedRequest.Dedicated, "immutable root and complete target reach backend unchanged");
        Check(!controller.Dispatch(RmlUiIntentKind.PlayCreate)
            && !controller.OpenCreateLobbyForInstalled(target)
            && services.CreateCalls == 1, "pending allocation blocks duplicate submit and target replacement");
        services.CreatePending.SetResult(new(false, default, "SYNTHETIC ALLOCATION REFUSED"));
        Await(controller, () => !controller.Creating && !controller.Joining);
        Check(Flag(controller, "play_create_mode") && Text(controller, "play_create_map") == target.Identity.RoomKey,
            "failed allocation preserves native form and selected revision");
        services.CreatePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Check(controller.Dispatch(RmlUiIntentKind.PlayCreate), "failed allocation can retry explicitly");
        Await(controller, () => services.CreateCalls == 2);
        Check(ReferenceEquals(services.CreatedRequest.CommunityMap, target) && services.CreatedRequest.MaxPlayers == 4,
            "retry retains exact original target and capacity");
    }

    private static void CancelAndCycle()
    {
        foreach (bool back in new[] { false, true })
        {
            var services = new Stub();
            using var controller = new RmlUiMenuController(services);
            Check(controller.OpenCreateLobbyForInstalled(Target()), "fresh custom review opens before cancellation");
            Check(back ? controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home)
                : controller.Dispatch(RmlUiIntentKind.PlayCancel), "back or cancel leaves unsubmitted review");
            Check(services.CreateCalls == 0 && controller.Dispatch(RmlUiIntentKind.PlayCreateOpen)
                && Text(controller, "play_create_map") == "Data Shrine"
                && Text(controller, "play_create_source") == "MPH // BUILT-IN ARENA", "normal Create restores built-in choice without allocation");
        }
        var cycleServices = new Stub { LobbyCapacity = 4 };
        using var cycle = new RmlUiMenuController(cycleServices);
        Check(cycle.OpenCreateLobbyForInstalled(Target()) && cycle.Dispatch(RmlUiIntentKind.PlayNextMap)
            && cycle.Dispatch(RmlUiIntentKind.PlayCreate), "cycling custom choice permits built-in submission");
        Await(cycle, () => cycleServices.CreateCalls == 1);
        Check(cycleServices.CreatedRequest.CommunityMap == null && cycleServices.CreatedRequest.RoomKey == "MP1 SANCTORUS"
            && cycleServices.CreatedRequest.MaxPlayers == 8, "map cycle clears captured custom target and resets capacity");
    }

    private static void RefuseStaleAndInvalid()
    {
        var valid = Target();
        foreach (var target in new[]
        {
            valid with { ConfiguredRoot = valid.ConfiguredRoot + "/changed" },
            valid with { Identity = valid.Identity with { ContentHash = default } },
            valid with { Identity = valid.Identity with { PackageHash = default } },
            valid with { Identity = valid.Identity with { IsCustom = false } },
            valid with { Identity = valid.Identity with { MapId = Guid.Empty } },
            valid with { Identity = valid.Identity with { RoomKey = "MP1 SANCTORUS" } }
        })
        {
            var services = new Stub();
            using var controller = new RmlUiMenuController(services);
            Check(!controller.OpenCreateLobbyForInstalled(target) && !Flag(controller, "play_create_mode")
                && !controller.Dispatch(RmlUiIntentKind.PlayCreate) && services.CreateCalls == 0,
                "stale root, invalid hashes or built-in masquerade cannot open or allocate custom hosting");
        }
        var changedServices = new Stub();
        using var changed = new RmlUiMenuController(changedServices);
        Check(changed.OpenCreateLobbyForInstalled(valid), "valid target opens before configured root changes");
        GameFiles.Root += "/changed";
        Check(!changed.Dispatch(RmlUiIntentKind.PlayCreate) && changedServices.CreateCalls == 0,
            "root change between review and submit refuses allocation");
    }

    private static bool Flag(RmlUiMenuController controller, string key) => controller.Tick().Bindings[key].Boolean;
    private static string Text(RmlUiMenuController controller, string key) => controller.Tick().Bindings[key].Text;
    private static void Await(RmlUiMenuController controller, Func<bool> predicate)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate())
        {
            controller.Tick();
            if (watch.ElapsedMilliseconds > 3000) throw new TimeoutException("Community hosting completion");
            Thread.Sleep(5);
        }
        controller.Tick();
    }
    private static void Check(bool pass, string label)
    {
        if (!pass) throw new InvalidOperationException(label);
        Console.WriteLine("COMMUNITY HOSTING PASS " + label);
    }
}
