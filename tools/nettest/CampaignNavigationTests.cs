using System;
using System.Linq;
using MphRead.Formats;
using MphRead.Mods.Campaign;

namespace MphRead.NetTest;

internal static class CampaignNavigationTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("CAMP-02 FAIL " + name);
            checks++;
            Console.WriteLine("CAMP-02 PASS " + name);
        }

        try
        {
            StorySave save = PostMatchSaveTests.Fixture();
            Array.Clear(save.VisitedRooms);
            save.Areas = 0xC;
            save.Artifacts = 0;
            save.FoundOctoliths = 0;
            save.BossFlags = BossFlags.None;
            save.CheckpointRoomId = -1;
            byte activeSlot = Menu.SaveSlot;
            int priorRoom = save.CheckpointRoomId;
            var planets = CampaignNavigation.Planets;
            Check(planets.Count == 5, "exact five planets");
            Check(planets.Select(x => x.LandingRoomId).SequenceEqual(new[] { 27, 45, 65, 77, 89 }),
                "landing IDs match the original game");
            Check(planets.Select(x => x.FirstArea).SequenceEqual(new[] { 0, 2, 4, 6, 8 }),
                "original area pairs");
            Check(planets.Select(x => x.Cockpit.Loop).SequenceEqual(new[] { 102, 103, 104, 105, 168 }),
                "cockpit camera lookup is versioned data, not rendering code");
            Check(CampaignNavigation.CheckpointPlanet(save).Id == CampaignPlanet.CelestialArchives,
                "fresh save selects Celestial Archives");

            var initial = CampaignNavigation.StarMap(save);
            Check(initial.Count(s => s.Unlocked) == 1
                && initial.Single(s => s.Unlocked).Planet.Id == CampaignPlanet.CelestialArchives,
                "fresh save reveals only Celestial Archives");
            Check(!CampaignNavigation.TryLandingRoom(save, CampaignPlanet.Alinos, out string locked)
                && locked.Length == 0, "locked planet cannot be launched");
            Check(CampaignNavigation.TryLandingRoom(save, CampaignPlanet.CelestialArchives, out string first)
                && first == "UNIT2_LAND", "unlocked landing resolves existing MphRead room");
            Check(initial[1].OctolithTotal == 2 && initial[1].ArtifactTotal == 6,
                "two-area collectible totals");
            Check(initial[4].OctolithTotal == 0 && initial[4].ArtifactTotal == 0,
                "Oubliette has no area collectibles");

            save.Areas = 0x1FF;
            save.FoundOctoliths = 0b00001001; // Alinos area 0; Celestial Archives area 3
            save.Artifacts = (1u << 0) | (1u << 2) | (1u << 9);
            save.SetVisitedRoom(27);
            save.BossFlags = (BossFlags)((2 << (2 * 0)) | (2 << (2 * 1)) | (1 << (2 * 2)));
            var alinos = CampaignNavigation.Status(save, CampaignPlanet.Alinos);
            var celestial = CampaignNavigation.Status(save, CampaignPlanet.CelestialArchives);
            Check(alinos.Unlocked && alinos.Visited && alinos.Octoliths == 1
                && alinos.Artifacts == 2 && alinos.Cleared, "Alinos progress comes solely from StorySave");
            Check(celestial.Octoliths == 1 && celestial.Artifacts == 1
                && celestial.FirstAreaState == AreaState.Escape && !celestial.Cleared,
                "two-area boss state has no inferred clear");
            Check(CampaignNavigation.TryLandingRoom(save, CampaignPlanet.Oubliette, out string finalRoom)
                && finalRoom == "Gorea_Land", "Oubliette unlock follows original area bit");
            Check(CampaignNavigation.ByArea(8)?.Id == CampaignPlanet.Oubliette
                && CampaignNavigation.ByArea(9) == null, "area to planet bounds");
            Check(CampaignNavigation.ByLandingRoom(77)?.Id == CampaignPlanet.Arcterra
                && CampaignNavigation.ByLandingRoom(91) == null, "landing-room lookup doesn't admit bosses");
            Check(CampaignNavigation.ByLandingRoomName("uNiT3_lAnD")?.Id == CampaignPlanet.VesperDefenseOutpost,
                "landing lookup is case insensitive");
            Check(CampaignNavigation.CheckpointPlanet(save).Id == CampaignPlanet.CelestialArchives,
                "invalid checkpoint falls back to starting planet");
            save.CheckpointRoomId = 65;
            Check(CampaignNavigation.CheckpointPlanet(save).Id == CampaignPlanet.VesperDefenseOutpost,
                "checkpoint identifies landing planet");

            Check(CampaignNavigation.Classify(false, true, 27) == CampaignDeparture.None
                && CampaignNavigation.Classify(true, false, 27) == CampaignDeparture.Quit,
                "non-ending and ordinary quit remain distinct");
            Check(CampaignNavigation.Classify(true, true, 27) == CampaignDeparture.ReturnedToShip
                && CampaignNavigation.Classify(true, true, 91) == CampaignDeparture.Ending
                && CampaignNavigation.Classify(true, true, 92) == CampaignDeparture.Ending
                && CampaignNavigation.Classify(true, true, 28) == CampaignDeparture.Quit,
                "ship boarding and two ending rooms classify without changing save policy");
            Check(Menu.SaveSlot == activeSlot && priorRoom == -1 && save.CheckpointRoomId == 65,
                "projection does not touch global save slot or rewrite progress");

            bool rejected = false;
            try { _ = CampaignNavigation.Get((CampaignPlanet)99); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "unknown planet cannot index outside catalog");
            Console.WriteLine($"CAMP-02 PASS total={checks}: asset-free planet and exit-state contract");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("CAMP-02 FAIL " + ex);
            return 1;
        }
    }
}
