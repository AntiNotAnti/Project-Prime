using System;
using System.IO;
using System.Reflection;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher;

namespace MphRead.NetTest;

internal static class CampaignShipFlowTests
{
    internal static int Run()
    {
        string oldDirectory = Directory.GetCurrentDirectory();
        byte oldSlot = Menu.SaveSlot;
        SaveWhen oldRequest = Menu.NeededSave;
        string root = Path.Combine(Path.GetTempPath(), "prime-ship-flow-" + Guid.NewGuid().ToString("N"));
        int checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("CAMP-03 FAIL " + name);
            checks++;
            Console.WriteLine("CAMP-03 PASS " + name);
        }
        SceneGameState State(StorySave save, CampaignDeparture departure, int room)
        {
            var state = new SceneGameState(new ScenePlayerRegistry())
            {
                LastCampaignDeparture = departure,
                LastCampaignDepartureRoomId = room
            };
            typeof(SceneGameState).GetProperty(nameof(SceneGameState.StorySave),
                BindingFlags.Public | BindingFlags.Instance)!.SetValue(state, save);
            return state;
        }

        try
        {
            Directory.CreateDirectory(root);
            Directory.SetCurrentDirectory(root);
            StorySave save = PostMatchSaveTests.Fixture();
            save.Areas = 0xC;
            save.CheckpointRoomId = 45;
            save.Artifacts = 0;
            save.FoundOctoliths = 0;

            CampaignDockReceipt? ticket = null;
            Check(!CampaignShipTravel.TryChooseLanding(save, ticket, 1, false,
                CampaignPlanet.CelestialArchives, out _, out _), "no mid-mission fast travel");
            ticket = new CampaignDockReceipt(1, CampaignPlanet.CelestialArchives);
            Check(!CampaignShipTravel.TryChooseLanding(save, ticket, 2, false,
                CampaignPlanet.CelestialArchives, out _, out _), "ticket bound to original save slot");
            Check(!CampaignShipTravel.TryChooseLanding(save, ticket, 1, true,
                CampaignPlanet.CelestialArchives, out _, out _), "new game cannot use dock ticket");
            Check(!CampaignShipTravel.TryChooseLanding(save, ticket, 1, false,
                CampaignPlanet.Alinos, out _, out _), "locked planet rejected despite valid ticket");
            Check(CampaignShipTravel.TryChooseLanding(save, ticket, 1, false,
                CampaignPlanet.CelestialArchives, out string land, out string reason)
                && land == "UNIT2_LAND" && reason.Length == 0, "dock permits current planet landing");
            save.Areas |= 1;
            Check(CampaignShipTravel.TryChooseLanding(save, ticket, 1, false,
                CampaignPlanet.Alinos, out land, out _)
                && land == "UNIT1_LAND", "newly unlocked planet is selectable");
            Check(save.CheckpointRoomId == 45, "travel query never rewrites checkpoint");
            save.CheckpointEntityId = 77; // an entity from the old planet
            CampaignShipTravel.PrepareLandingSpawn(save, ticket.Value, CampaignPlanet.Alinos);
            Check(save.CheckpointRoomId == 27 && save.CheckpointEntityId == -1,
                "new planet clears foreign checkpoint entity and selects landing");
            save.CheckpointRoomId = 45;
            save.CheckpointEntityId = 77;
            CampaignShipTravel.PrepareLandingSpawn(save, ticket.Value, CampaignPlanet.CelestialArchives);
            Check(save.CheckpointRoomId == 45 && save.CheckpointEntityId == 77,
                "Exit Ship keeps the original hatch checkpoint");
            save.CheckpointEntityId = -1;

            Check(!CampaignShipTravel.TryChooseLanding(null, ticket, 1, false,
                CampaignPlanet.Alinos, out _, out _), "missing save refused");

            bool threw = false;
            try { _ = AdventureLaunch.Create(1, true, Hunter.Samus, CampaignPlanet.Alinos); }
            catch (ArgumentException) { threw = true; }
            Check(threw, "fresh run cannot inject arbitrary destination in launch plan");
            LaunchPlan plan = AdventureLaunch.Create(1, false, Hunter.Samus, CampaignPlanet.Alinos);
            Check(plan.Kind == LaunchKind.Adventure && plan.AdventureDestination == CampaignPlanet.Alinos
                && plan.SaveSlot == 1 && !plan.NewGame, "ship-selected destination survives launch plan");

            // This is the same post-match save owner used by desktop and
            // Android; the ticket must not appear until a real committed ship
            // save, and never from SaveFromShip == Never.
            MatchStart.ClearRecentShipDock();
            Menu.SaveSlot = 1;
            Menu.NeededSave = SaveWhen.Always;
            var ship = State(save, CampaignDeparture.ReturnedToShip, 45);
            var pending = MatchStart.PrepareAfterMatch(ship);
            Check(MatchStart.RecentShipDock == null && pending.HasPayload,
                "pending save does not pre-authorize star map");
            MatchStart.AfterMatch(pending);
            Check(MatchStart.RecentShipDock is { Slot: 1, Planet: CampaignPlanet.CelestialArchives }
                && File.Exists(Path.Combine("Savedata", "save001.json")),
                "committed ship departure publishes scoped ticket");
            Check(CampaignShipTravel.TryChooseLanding(save, MatchStart.RecentShipDock,
                1, false, CampaignPlanet.Alinos, out land, out _)
                && land == "UNIT1_LAND", "saved native bytes support authorized planet select");

            // A normal quit with the exact same SaveWhen.Always policy cannot
            // create a fake ship visit.
            Menu.NeededSave = SaveWhen.Always;
            var quit = MatchStart.PrepareAfterMatch(State(save, CampaignDeparture.Quit, 45));
            MatchStart.AfterMatch(quit);
            Check(MatchStart.RecentShipDock == null,
                "ordinary quit never grants ship access, even with identical save policy");

            Menu.NeededSave = SaveWhen.Never;
            var skipped = MatchStart.PrepareAfterMatch(State(save, CampaignDeparture.ReturnedToShip, 45));
            MatchStart.AfterMatch(skipped);
            Check(MatchStart.RecentShipDock == null,
                "ship departure without committed save never grants access");

            Menu.NeededSave = SaveWhen.Always;
            var ending = MatchStart.PrepareAfterMatch(State(save, CampaignDeparture.Ending, 91));
            MatchStart.AfterMatch(ending);
            Check(MatchStart.RecentShipDock == null, "Gorea ending never acts as a regular ship visit");

            Menu.NeededSave = SaveWhen.Always;
            var badRoom = MatchStart.PrepareAfterMatch(State(save, CampaignDeparture.ReturnedToShip, 28));
            MatchStart.AfterMatch(badRoom);
            Check(MatchStart.RecentShipDock == null, "unknown departure room cannot grant a dock ticket");
            MatchStart.ClearRecentShipDock();
            Console.WriteLine($"CAMP-03 PASS total={checks}: ship-only launch and commit boundaries");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("CAMP-03 FAIL " + ex);
            return 1;
        }
        finally
        {
            MatchStart.ClearRecentShipDock();
            Menu.SaveSlot = oldSlot;
            Menu.NeededSave = oldRequest;
            Directory.SetCurrentDirectory(oldDirectory);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
