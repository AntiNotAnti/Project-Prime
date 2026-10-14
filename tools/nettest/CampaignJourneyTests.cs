using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher;

namespace MphRead.NetTest;

/// <summary>
/// CAMP-07: a single three-slot journey using the real native save serializer
/// and post-scene handoff. Simulated discoveries/boss exits are named honestly;
/// the asset-free runner cannot claim to have played the original rooms.
/// </summary>
internal static class CampaignJourneyTests
{
    internal static int Run()
    {
        Headless.Enter();
        string priorDirectory = Directory.GetCurrentDirectory();
        byte priorSlot = Menu.SaveSlot;
        SaveWhen priorSaveWhen = Menu.NeededSave;
        string root = Path.Combine(Path.GetTempPath(),
            "prime-camp07-journey-" + Guid.NewGuid().ToString("N"));
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("CAMP-07 JOURNEY FAIL: " + label);
            checks++;
            Console.WriteLine("CAMP-07 JOURNEY PASS " + label);
        }

        SceneGameState State(StorySave save, CampaignDeparture departure, int lastRoom)
        {
            var state = new SceneGameState(new ScenePlayerRegistry())
            {
                LastCampaignDeparture = departure,
                LastCampaignDepartureRoomId = lastRoom
            };
            typeof(SceneGameState).GetProperty(nameof(SceneGameState.StorySave),
                BindingFlags.Public | BindingFlags.Instance)!.SetValue(state, save);
            return state;
        }

        void Commit(byte slot, StorySave save, CampaignDeparture departure, int room,
            SaveWhen saveWhen = SaveWhen.Always)
        {
            Menu.SaveSlot = slot;
            Menu.NeededSave = saveWhen;
            var context = MatchStart.PrepareAfterMatch(State(save, departure, room));
            MatchStart.AfterMatch(context);
        }

        try
        {
            Directory.CreateDirectory(root);
            Directory.SetCurrentDirectory(root);
            MatchStart.ClearRecentShipDock();
            MatchStart.ClearRecentCampaignEnding();

            // Stage 1: a new-file-equivalent initial state. Do not invoke
            // StorySave() here: its logbook constructor needs extracted assets.
            var fresh = PostMatchSaveTests.Fixture();
            Array.Clear(fresh.VisitedRooms);
            Array.Clear(fresh.Logbook);
            fresh.Areas = 0xC;
            fresh.Artifacts = 0;
            fresh.FoundOctoliths = fresh.CurrentOctoliths = 0;
            fresh.CheckpointRoomId = 45;
            fresh.CheckpointEntityId = 19;
            fresh.ScanCount = 0;
            fresh.SetVisitedRoom(45);
            Check(!AdventureSave.HasFile(1) && !AdventureSave.HasFile(2)
                && !AdventureSave.HasFile(3), "all three slots initially empty");
            Check(CampaignNavigation.StarMap(fresh).Count(p => p.Unlocked) == 1
                && CampaignNavigation.CheckpointPlanet(fresh).Id
                    == CampaignPlanet.CelestialArchives,
                "fresh story starts only in Celestial Archives");
            Check(CampaignMovieCatalog.Landing(45) == Movie.CALanding
                && CampaignMovieCatalog.Takeoff(45) == Movie.CATakeoff
                && CampaignAudioCatalog.Sequence(CampaignAudioPage.Dock,
                    CampaignPlanet.CelestialArchives) == SeqId.SHIP_LAND2,
                "intro/landing/ship audio IDs retain original cartridge mapping");
            Check(!CampaignShipTravel.TryChooseLanding(fresh, null, 1, false,
                CampaignPlanet.Alinos, out _, out _), "fast travel forbidden before ship boarding");

            // Stage 2: scan and explored-room log, no hidden log entries.
            var catalog = new[]
            {
                new StringTableEntry("000", 'L', "SECRET", "must stay hidden", 0, 'L'),
                new StringTableEntry("001", 'L', "FIRST SCAN", "saved observation", 0, 'L')
            };
            fresh.Logbook[0] |= 0b10;
            fresh.ScanCount = 1;
            var discovered = CampaignJournal.Capture(fresh, catalog);
            Check(discovered.Scans.Count == 1 && discovered.Scans[0].Title == "FIRST SCAN"
                && discovered.Rooms.Count == 1 && discovered.Rooms[0].RoomId == 45,
                "first scan/room in journal without leaking undiscovered text");

            // Stage 3: native post-match save and verified docking receipt.
            Commit(1, fresh, CampaignDeparture.ReturnedToShip, 45);
            Check(MatchStart.RecentShipDock is
                { Slot: 1, Planet: CampaignPlanet.CelestialArchives }
                && MatchStart.RecentCampaignEnding == null, "boarding publishes only slot-1 ship ticket");
            var slot1 = Path.Combine("Savedata", "save001.json");
            byte[] firstSave = File.ReadAllBytes(slot1);
            StorySave? loaded = GameState.PeekSave(1);
            Check(loaded != null && loaded.CheckpointRoomId == 45
                && loaded.ScanCount == 1 && loaded.Logbook[0] == 0b10
                && loaded.CheckVisitedRoom(45), "native disk roundtrip retains initial scan/checkpoint");
            Check(!CampaignShipTravel.TryChooseLanding(loaded,
                MatchStart.RecentShipDock, 1, false, CampaignPlanet.Alinos,
                out _, out _), "locked planet remains locked after reload");
            Check(!CampaignShipTravel.TryChooseLanding(loaded,
                MatchStart.RecentShipDock, 2, false, CampaignPlanet.CelestialArchives,
                out _, out _), "receipt cannot be replayed for another slot");

            // Stage 4: simulate vanilla progression unlocking Alinos and
            // boarding again from the authentic landing room.
            loaded!.Areas |= 0x3;
            loaded.SetVisitedRoom(27);
            loaded.UpdateFoundOctolith(2);
            loaded.UpdateFoundArtifact(0, 2);
            Commit(1, loaded, CampaignDeparture.ReturnedToShip, 45);
            var updated = GameState.PeekSave(1)!;
            Check(CampaignNavigation.IsUnlocked(updated, CampaignPlanet.Alinos)
                && CampaignNavigation.StarMap(updated)[1].Octoliths == 1,
                "earned planet unlock and collectible survive native commit");
            var dock = MatchStart.RecentShipDock;
            Check(CampaignShipTravel.TryChooseLanding(updated, dock, 1, false,
                CampaignPlanet.Alinos, out string alinosName, out _)
                && alinosName == "UNIT1_LAND", "legitimate star map can select unlocked Alinos");
            int originalCheckpoint = GameState.PeekSave(1)!.CheckpointRoomId;
            CampaignShipTravel.PrepareLandingSpawn(updated, dock!.Value, CampaignPlanet.Alinos);
            Check(updated.CheckpointRoomId == 27 && updated.CheckpointEntityId == -1
                && GameState.PeekSave(1)!.CheckpointRoomId == originalCheckpoint,
                "cross-planet spawn is only changed in memory until next save");

            // Stage 5: persist a real different-planet checkpoint and copy
            // exact native bytes to independent bad/good ending branches.
            Commit(1, updated, CampaignDeparture.ReturnedToShip, 27);
            Check(GameState.PeekSave(1)!.CheckpointRoomId == 27
                && MatchStart.RecentShipDock is { Slot: 1, Planet: CampaignPlanet.Alinos },
                "next boarding persists destination and identifies docked planet");
            updated = GameState.PeekSave(1)!;
            updated.Areas |= 0x100;
            updated.FoundOctoliths = 0xFF;
            updated.CurrentOctoliths = 0xFF;
            updated.SetVisitedRoom(89);
            Commit(1, updated, CampaignDeparture.ReturnedToShip, 27);
            Check(CampaignNavigation.TryLandingRoom(GameState.PeekSave(1)!,
                CampaignPlanet.Oubliette, out string finalRoom) && finalRoom == "Gorea_Land",
                "late campaign unlock is visible only after earned save commit");
            Check(CampaignShipTravel.TryChooseLanding(GameState.PeekSave(1)!,
                MatchStart.RecentShipDock, 1, false, CampaignPlanet.Oubliette,
                out _, out _), "Oubliette travel requires final unlock and ship receipt");

            AdventureSave.Copy(1, 2);
            AdventureSave.Copy(1, 3);
            byte[] lateSave = File.ReadAllBytes(slot1);
            Check(File.ReadAllBytes(Path.Combine("Savedata", "save002.json"))
                .SequenceEqual(lateSave)
                && File.ReadAllBytes(Path.Combine("Savedata", "save003.json"))
                    .SequenceEqual(lateSave)
                && Menu.SaveSlot == 1,
                "late-game save forks preserve exact native bytes and active slot");

            // Stage 6: two ending branches. The final boss encounters/movie
            // are *simulated as departures*, not rendered or combat-tested.
            StorySave bad = GameState.PeekSave(2)!;
            bad.CheckpointRoomId = 91;
            Commit(2, bad, CampaignDeparture.Ending, 91);
            Check(MatchStart.RecentCampaignEnding is
                { Slot: 2, Kind: CampaignEndingKind.Incomplete, SaveCommitted: true }
                && MatchStart.RecentShipDock == null
                && CampaignMovieCatalog.Ending(91) == Movie.BadEndingPart1,
                "bad ending commits slot 2, routes two-part film, not ship travel");
            byte[] badSave = File.ReadAllBytes(Path.Combine("Savedata", "save002.json"));
            StorySave good = GameState.PeekSave(3)!;
            good.CheckpointRoomId = 92;
            Commit(3, good, CampaignDeparture.Ending, 92);
            Check(MatchStart.RecentCampaignEnding is
                { Slot: 3, Kind: CampaignEndingKind.Complete, SaveCommitted: true }
                && MatchStart.RecentShipDock == null
                && CampaignMovieCatalog.Ending(92) == Movie.GoodEnding
                && CampaignAudioCatalog.Sequence(CampaignAudioPage.Ending) == SeqId.CREDITS,
                "good ending commits slot 3 with good film and original credits track");
            Check(File.ReadAllBytes(Path.Combine("Savedata", "save002.json"))
                .SequenceEqual(badSave)
                && File.ReadAllBytes(slot1).SequenceEqual(lateSave),
                "separate bad/good endings never overwrite source or other slot");

            // Stage 7: no-save ending and ordinary quit cannot publish
            // a false saved result or leak stale ship/ending state.
            byte[] beforeUnsaved = File.ReadAllBytes(Path.Combine("Savedata", "save003.json"));
            Commit(3, good, CampaignDeparture.Ending, 92, SaveWhen.Never);
            Check(MatchStart.RecentCampaignEnding is { SaveCommitted: false }
                && File.ReadAllBytes(Path.Combine("Savedata", "save003.json"))
                    .SequenceEqual(beforeUnsaved),
                "Never-save ending does not write or claim progress was saved");
            Commit(3, good, CampaignDeparture.Quit, 92, SaveWhen.Never);
            Check(MatchStart.RecentCampaignEnding == null && MatchStart.RecentShipDock == null,
                "normal quit clears transient campaign navigation/presentation tickets");
            Check(AdventureSave.Read(1).Used && AdventureSave.Read(2).Used
                && AdventureSave.Read(3).Used, "all three slots remain readable without schema conversion");
            Console.WriteLine($"CAMP-07 JOURNEY PASS total={checks}: simulated journey, actual native save IO, no ROM");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("CAMP-07 JOURNEY FAIL " + ex);
            return 1;
        }
        finally
        {
            MatchStart.ClearRecentShipDock();
            MatchStart.ClearRecentCampaignEnding();
            Menu.SaveSlot = priorSlot;
            Menu.NeededSave = priorSaveWhen;
            Directory.SetCurrentDirectory(priorDirectory);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
