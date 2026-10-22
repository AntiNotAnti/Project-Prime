using System;
using System.Linq;
using MphRead.Formats;
using MphRead.Mods.Campaign;

namespace MphRead.NetTest;

internal static class CampaignPresentationTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("CAMP-05 FAIL " + label);
            checks++;
            Console.WriteLine("CAMP-05 PASS " + label);
        }
        try
        {
            StorySave save = PostMatchSaveTests.Fixture();
            Array.Clear(save.VisitedRooms);
            Array.Clear(save.Logbook);
            save.Areas = 0xC; // new game: only Celestial Archives
            save.FoundOctoliths = 0b00001100;
            save.ScanCount = 2;
            save.SetVisitedRoom(45); // playable Celestial Archives landing
            save.SetVisitedRoom(27); // stale Alinos bit but still locked
            var scanEntries = new[]
            {
                new StringTableEntry("L000", 'L', "HIDDEN LORE", "not yet scanned", 0, 'L'),
                new StringTableEntry("L001", 'L', "DISCOVERED LORE", "unlocked story", 0, 'L'),
                new StringTableEntry("L002", 'B', "HIDDEN BIOFORM", "not yet scanned", 0, 'B'),
                new StringTableEntry("L003", 'O', "DISCOVERED OBJECT", "unlocked item", 0, 'O')
            };
            save.Logbook[0] = 0b00001010; // indices 1 and 3 only
            var initial = CampaignJournal.Capture(save, scanEntries);
            Check(initial.Rooms.Count == 1 && initial.Rooms[0].RoomId == 45,
                "journal displays visited, unlocked story room and hides locked planet");
            Check(initial.Rooms.All(room => room.Planet == CampaignPlanet.CelestialArchives),
                "journal discloses no rooms belonging to locked planet");
            Check(initial.Planets.Count == 5 && initial.Planets.Single(p => p.Unlocked).Planet.Id
                == CampaignPlanet.CelestialArchives, "journal uses original five-planet unlocks");
            Check(initial.Scans.Count == 2 && initial.Scans[0].Id == 1 && initial.Scans[1].Id == 3,
                "only prior scans appear in logbook");
            Check(initial.Scans[0].Title == "DISCOVERED LORE"
                && initial.Scans[1].Description == "unlocked item"
                && initial.Scans.All(e => !e.Title.StartsWith("HIDDEN")),
                "undiscovered logbook text is not exposed");
            Check(initial.DiscoveredScanBits == 2 && initial.ScanTableCount == 4,
                "logbook count derives from recorded bits, not unseen categories");
            Check(CampaignJournal.CompactHud(save) == "OCTOLITHS 2/8  //  SCANS 2",
                "compact native mission HUD derives from existing StorySave");

            // The cartridge's ScanLog IDs are sparse. Do not treat ordinal
            // array positions as the save's actual scan-bit positions.
            Array.Clear(save.Logbook);
            save.Logbook[29 >> 3] |= (byte)(1 << (29 & 7));
            save.Logbook[35 >> 3] |= (byte)(1 << (35 & 7));
            var sparseEntries = new[]
            {
                new StringTableEntry("L027", 'L', "UNSCANNED 27", "secret", 0, 'L'),
                new StringTableEntry("L029", 'L', "SCANNED 29", "observed", 0, 'L'),
                new StringTableEntry("L032", 'L', "UNSCANNED 32", "still secret", 0, 'L'),
                new StringTableEntry("L035", 'L', "SCANNED 35", "observed", 0, 'L'),
                new StringTableEntry("Lbad", 'L', "INVALID RECORD", "hidden", 0, 'L')
            };
            var sparse = CampaignJournal.Capture(save, sparseEntries);
            Check(sparse.Scans.Count == 2 && sparse.Scans[0].Id == 29
                && sparse.Scans[1].Id == 35
                && sparse.Scans.All(x => x.Title.StartsWith("SCANNED ")),
                "sparse cartridge record identifiers map to actual saved scan bits");
            Check(CampaignJournal.ReadScanId(sparseEntries[4]) == -1
                && CampaignJournal.ReadScanId(new StringTableEntry(
                    "L999", 'L', "INVALID", "hidden", 0, 'L')) == -1,
                "invalid or out-of-range scan IDs never access unowned save bits");

            // Restore the original fixture for the separate planet/snapshot cases.
            Array.Clear(save.Logbook);
            save.Logbook[0] = 0b00001010;

            save.Areas |= 1; // Alinos now unlocked
            save.Logbook[0] |= 1; // first lore scanned later
            var later = CampaignJournal.Capture(save, scanEntries);
            Check(later.Rooms.Count == 2 && later.Rooms.Any(room => room.RoomId == 27),
                "newly unlocked, already-visited planet is shown on refreshed snapshot");
            Check(later.Scans.Count == 3 && initial.Scans.Count == 2
                && initial.Rooms.Count == 1, "UI snapshot detached from mutable gameplay state");
            var noAssets = CampaignJournal.Capture(save);
            Check(!noAssets.HasLogbookTable && noAssets.Scans.Count == 0
                && noAssets.DiscoveredScanBits == 3 && noAssets.Rooms.Count == 2,
                "missing cartridge logbook keeps exploration and persistent scan bits");
            Check(!noAssets.Rooms.Any(room => room.RoomId < 27 || room.RoomId > 92),
                "multiplayer maps never leak into campaign journal");

            var ds = CampaignMapPresentation.Projection(256, 192, 0.75f);
            var wide = CampaignMapPresentation.Projection(1920, 1080, 0.75f);
            var ultra = CampaignMapPresentation.Projection(3840, 1080, 0.75f);
            Check(MathF.Abs(ds.Width - 192) < 0.01f && MathF.Abs(ds.Height - 144) < 0.01f,
                "original 4:3 map framing unchanged");
            Check(MathF.Abs(wide.Width - 256) < 0.01f && wide.Height == ds.Height,
                "16:9 map extends horizontal visibility without shrinking height");
            Check(MathF.Abs(ultra.Width - 336) < 0.01f,
                "ultrawide map aspect is bounded to prevent HUD model clipping");
            var portrait = CampaignMapPresentation.Projection(480, 900, 0.75f);
            Check(portrait.Width == ds.Width && portrait.Height == ds.Height,
                "portrait handset retains safe original 4:3 map space");
            Check(CampaignMapPresentation.ClampZoom(-2) == CampaignMapPresentation.MinZoom
                && CampaignMapPresentation.ClampZoom(100) == CampaignMapPresentation.MaxZoom
                && CampaignMapPresentation.ClampZoom(float.NaN) == CampaignMapPresentation.DefaultZoom,
                "map zoom stays in native limits even with invalid input");
            Check(CampaignMapPresentation.Visible(0.5f, 0.5f)
                && !CampaignMapPresentation.Visible(-0.1f, 0.5f)
                && !CampaignMapPresentation.Visible(1.2f, 0.5f),
                "map picking uses ProjectPosition's actual 0..1 coordinate basis");
            Check(CampaignMapPresentation.CenterDistanceSquared(0.5f, 0.5f) == 0
                && CampaignMapPresentation.CenterDistanceSquared(0, 0) == 2
                && float.IsPositiveInfinity(CampaignMapPresentation.CenterDistanceSquared(float.NaN, 0)),
                "room focus scoring is centered and finite-safe");

            var scan = CampaignScanPresentation.Meter(1, 0.5f);
            Check(scan.Total == 120 && scan.Filled == 60,
                "half-complete scan meter has exact unit parity");
            Check(CampaignScanPresentation.Meter(1, 2).Filled == 120
                && CampaignScanPresentation.Meter(1, -1).Filled == 0,
                "scan display clamps overshoot and negative time");
            Check(CampaignScanPresentation.Meter(0, 0) == (1, 1)
                && CampaignScanPresentation.Meter(float.NaN, 1) == (1, 1),
                "instant and malformed scan durations avoid zero-size HUD meter");
            Check(CampaignScanPresentation.Meter(100000, float.PositiveInfinity) == (36000, 0),
                "malformed scan table cannot overflow native HUD meter");

            Console.WriteLine($"CAMP-05 PASS total={checks}: map, scan visor and HUD without ROM/GL");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("CAMP-05 FAIL " + ex);
            return 1;
        }
    }
}
