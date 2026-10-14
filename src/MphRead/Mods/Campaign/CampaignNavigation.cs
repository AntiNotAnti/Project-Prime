using System;
using System.Collections.Generic;
using MphRead.Formats;

namespace MphRead.Mods.Campaign
{
    /// <summary>Five original Alimbic Cluster destinations, independent of presentation.</summary>
    public enum CampaignPlanet
    {
        Alinos,
        CelestialArchives,
        VesperDefenseOutpost,
        Arcterra,
        Oubliette
    }

    /// <summary>
    /// ROM-derived cockpit camera sequences, not a new renderer or a second scene.
    /// The presentation adapter may choose how to use them.
    /// </summary>
    public readonly record struct CampaignCockpitSequences(int Loop, int Landing, int Takeoff);

    public sealed record CampaignPlanetInfo(
        CampaignPlanet Id, string Name, string LandingRoom, int LandingRoomId,
        int FirstArea, int AreaCount, CampaignCockpitSequences Cockpit);

    /// <summary>A value-only projection of the existing Project Prime StorySave.</summary>
    public readonly record struct CampaignPlanetStatus(
        CampaignPlanetInfo Planet, bool Unlocked, bool Visited,
        int Octoliths, int OctolithTotal, int Artifacts, int ArtifactTotal,
        AreaState FirstAreaState, AreaState SecondAreaState)
    {
        public bool Cleared => FirstAreaState == AreaState.Clear
            && (Planet.AreaCount == 1 || SecondAreaState == AreaState.Clear);
    }

    public enum CampaignDeparture
    {
        None,
        Quit,
        ReturnedToShip,
        Ending
    }

    /// <summary>
    /// SecondHunt's ship/planet metadata adapted to the already-existing
    /// MphRead adventure and StorySave. This type never owns Scene,
    /// Menu.SaveSlot, a serialized save, graphics or audio.
    /// </summary>
    public static class CampaignNavigation
    {
        public const int GoreaFirstRoomId = 91;
        public const int GoreaSecondRoomId = 92;

        private static readonly IReadOnlyList<CampaignPlanetInfo> _planets =
            Array.AsReadOnly(new[]
            {
                new CampaignPlanetInfo(CampaignPlanet.Alinos, "ALINOS", "UNIT1_LAND", 27, 0, 2,
                    new CampaignCockpitSequences(102, 114, 115)),
                new CampaignPlanetInfo(CampaignPlanet.CelestialArchives, "CELESTIAL ARCHIVES", "UNIT2_LAND", 45, 2, 2,
                    new CampaignCockpitSequences(103, 116, 117)),
                new CampaignPlanetInfo(CampaignPlanet.VesperDefenseOutpost, "VESPER DEFENSE OUTPOST", "UNIT3_LAND", 65, 4, 2,
                    new CampaignCockpitSequences(104, 118, 119)),
                new CampaignPlanetInfo(CampaignPlanet.Arcterra, "ARCTERRA", "UNIT4_LAND", 77, 6, 2,
                    new CampaignCockpitSequences(105, 120, 121)),
                new CampaignPlanetInfo(CampaignPlanet.Oubliette, "OUBLIETTE", "Gorea_Land", 89, 8, 1,
                    new CampaignCockpitSequences(168, 169, 170))
            });

        public static IReadOnlyList<CampaignPlanetInfo> Planets => _planets;

        public static CampaignPlanetInfo Get(CampaignPlanet id)
        {
            int index = (int)id;
            if ((uint)index >= (uint)_planets.Count)
                throw new ArgumentOutOfRangeException(nameof(id));
            return _planets[index];
        }

        public static CampaignPlanetInfo? ByLandingRoom(int roomId)
        {
            foreach (CampaignPlanetInfo planet in _planets)
                if (planet.LandingRoomId == roomId) return planet;
            return null;
        }

        public static CampaignPlanetInfo? ByLandingRoomName(string? roomName)
        {
            if (String.IsNullOrWhiteSpace(roomName)) return null;
            foreach (CampaignPlanetInfo planet in _planets)
                if (String.Equals(planet.LandingRoom, roomName, StringComparison.OrdinalIgnoreCase))
                    return planet;
            return null;
        }

        public static CampaignPlanetInfo? ByArea(int areaId)
        {
            foreach (CampaignPlanetInfo planet in _planets)
                if (areaId >= planet.FirstArea && areaId < planet.FirstArea + planet.AreaCount)
                    return planet;
            return null;
        }

        public static CampaignPlanetInfo CheckpointPlanet(StorySave save)
        {
            ArgumentNullException.ThrowIfNull(save);
            int roomId = save.CheckpointRoomId;
            if (roomId >= 27 && roomId <= 92)
            {
                CampaignPlanetInfo? landing = ByLandingRoom(roomId);
                if (landing != null) return landing;
                CampaignPlanetInfo? area = ByArea(Metadata.GetAreaInfo(roomId));
                if (area != null) return area;
            }
            return Get(CampaignPlanet.CelestialArchives);
        }

        public static bool IsUnlocked(StorySave save, CampaignPlanet id)
        {
            ArgumentNullException.ThrowIfNull(save);
            CampaignPlanetInfo planet = Get(id);
            // Both halves of a planet are unlocked together by original game
            // progression; Oubliette has only area 8.
            return (save.Areas & (1 << planet.FirstArea)) != 0;
        }

        public static CampaignPlanetStatus Status(StorySave save, CampaignPlanet id)
        {
            ArgumentNullException.ThrowIfNull(save);
            CampaignPlanetInfo planet = Get(id);
            int octoliths = 0, artifacts = 0;
            AreaState first = AreaState.None, second = AreaState.None;
            for (int i = 0; i < planet.AreaCount; i++)
            {
                int area = planet.FirstArea + i;
                AreaState state = (AreaState)(((int)save.BossFlags >> (2 * area)) & 3);
                if (i == 0) first = state;
                else second = state;

                if (area < 8)
                {
                    if (save.CheckFoundOctolith(area)) octoliths++;
                    artifacts += save.CountFoundArtifacts(area);
                }
            }
            bool hasCollectibles = planet.FirstArea < 8;
            return new CampaignPlanetStatus(planet, IsUnlocked(save, id),
                save.CheckVisitedRoom(planet.LandingRoomId), octoliths,
                hasCollectibles ? planet.AreaCount : 0, artifacts,
                hasCollectibles ? 3 * planet.AreaCount : 0, first, second);
        }

        public static IReadOnlyList<CampaignPlanetStatus> StarMap(StorySave save)
        {
            ArgumentNullException.ThrowIfNull(save);
            var result = new CampaignPlanetStatus[_planets.Count];
            for (int i = 0; i < _planets.Count; i++)
                result[i] = Status(save, _planets[i].Id);
            return Array.AsReadOnly(result);
        }

        /// <summary>
        /// Returns a destination only when the actual saved progression
        /// unlocks it. A locked selection never mutates the save or changes
        /// the checkpoint. New-game flows must not call this for other planets.
        /// </summary>
        public static bool TryLandingRoom(StorySave save, CampaignPlanet destination, out string roomName)
        {
            CampaignPlanetInfo planet = Get(destination);
            roomName = IsUnlocked(save, destination) ? planet.LandingRoom : "";
            return roomName.Length != 0;
        }

        /// <summary>
        /// Pure exit classification: the caller supplies whether a real ship
        /// exit occurred. Do not infer it from SaveWhen settings, which can be
        /// identical for ship departure and a normal quit.
        /// </summary>
        public static CampaignDeparture Classify(bool ended, bool enteringShip, int lastRoomId)
        {
            if (!ended) return CampaignDeparture.None;
            if (!enteringShip) return CampaignDeparture.Quit;
            if (lastRoomId is GoreaFirstRoomId or GoreaSecondRoomId)
                return CampaignDeparture.Ending;
            return ByLandingRoom(lastRoomId) != null
                ? CampaignDeparture.ReturnedToShip : CampaignDeparture.Quit;
        }
    }
}
