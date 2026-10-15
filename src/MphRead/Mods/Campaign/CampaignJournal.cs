using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using MphRead.Formats;

namespace MphRead.Mods.Campaign
{
    /// <summary>One already-discovered room; never exposes an unexplored room.</summary>
    public readonly record struct CampaignVisitedRoom(
        int RoomId, int AreaId, CampaignPlanet Planet, string RoomKey, string DisplayName,
        bool LandingSite);

    /// <summary>One previously scanned logbook record, detached from the ROM table.</summary>
    public readonly record struct CampaignJournalScan(
        int Id, char Category, string Title, string Description);

    /// <summary>
    /// Safe, value-only snapshot for campaign UI. The caller borrows StorySave
    /// just once. The screen must not retain the live scene or mutable arrays,
    /// and every displayed item must have been discovered by that save.
    /// </summary>
    public sealed class CampaignJournal
    {
        private CampaignJournal(IReadOnlyList<CampaignVisitedRoom> rooms,
            IReadOnlyList<CampaignPlanetStatus> planets,
            IReadOnlyList<CampaignJournalScan> scans, int discoveredScanBits,
            int octoliths, int scanTableCount)
        {
            Rooms = rooms;
            Planets = planets;
            Scans = scans;
            DiscoveredScanBits = discoveredScanBits;
            Octoliths = octoliths;
            ScanTableCount = scanTableCount;
        }

        public IReadOnlyList<CampaignVisitedRoom> Rooms { get; }
        public IReadOnlyList<CampaignPlanetStatus> Planets { get; }
        public IReadOnlyList<CampaignJournalScan> Scans { get; }
        public int DiscoveredScanBits { get; }
        public int Octoliths { get; }
        public int ScanTableCount { get; }
        public bool HasLogbookTable => ScanTableCount != 0;

        /// <summary>
        /// ScanLog contains sparse cartridge record IDs (for example, L029
        /// followed by L032); its array index is not the bit index in
        /// StorySave.Logbook. Only an L-prefixed, bounded decimal record may
        /// be read as the actual discovered scan ID.
        /// </summary>
        internal static int ReadScanId(StringTableEntry entry)
        {
            ReadOnlySpan<char> label = entry.Id.AsSpan();
            if (label.Length != 4 || label[0] != 'L'
                || !int.TryParse(label[1..], NumberStyles.None,
                    CultureInfo.InvariantCulture, out int id)
                || (uint)id >= 68 * 8)
                return -1;
            return id;
        }

        public static CampaignJournal Capture(StorySave save,
            IReadOnlyList<StringTableEntry>? logbook = null)
        {
            ArgumentNullException.ThrowIfNull(save);
            var rooms = new List<CampaignVisitedRoom>();
            // Story rooms occupy 27-92. The runtime has multiplayer rooms
            // outside this range; they must not appear in the story notebook.
            for (int roomId = 27; roomId <= 92; roomId++)
            {
                if (!save.CheckVisitedRoom(roomId)) continue;
                RoomMetadata? meta = Metadata.GetRoomById(roomId, noThrow: true);
                if (meta == null || meta.Multiplayer) continue;
                int area = Metadata.GetAreaInfo(roomId);
                CampaignPlanetInfo? planet = CampaignNavigation.ByArea(area);
                if (planet == null || !CampaignNavigation.IsUnlocked(save, planet.Id))
                    continue;
                rooms.Add(new CampaignVisitedRoom(roomId, area, planet.Id,
                    meta.Name, String.IsNullOrWhiteSpace(meta.InGameName) ? meta.Name : meta.InGameName,
                    roomId == planet.LandingRoomId));
            }

            int scannedBits = 0;
            foreach (byte value in save.Logbook)
                scannedBits += BitOperations.PopCount((uint)value);

            var scans = new List<CampaignJournalScan>();
            if (logbook != null)
            {
                foreach (StringTableEntry entry in logbook)
                {
                    int id = ReadScanId(entry);
                    if (id < 0 || (save.Logbook[id >> 3] & (1 << (id & 7))) == 0)
                        continue;
                    // Never use the ScanLog entry's ordinal here. Gaps in
                    // its numeric identifiers are real and must not reveal
                    // unscanned text or hide legitimately scanned records.
                    scans.Add(new CampaignJournalScan(id, entry.Category,
                        String.IsNullOrWhiteSpace(entry.String1) ? $"ENTRY {id:000}" : entry.String1,
                        entry.String2));
                }
            }
            return new CampaignJournal(rooms.AsReadOnly(),
                CampaignNavigation.StarMap(save), scans.AsReadOnly(),
                scannedBits, save.CountFoundOctoliths(), logbook?.Count ?? 0);
        }

        /// <summary>Safe to call from the render/UI paths even before an asset import.</summary>
        public static string CompactHud(StorySave save)
        {
            ArgumentNullException.ThrowIfNull(save);
            return $"OCTOLITHS {save.CountFoundOctoliths()}/8  //  SCANS {save.ScanCount}";
        }
    }
}
