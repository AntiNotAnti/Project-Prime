using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Network
{
    /// <summary>One map and the multiplayer modes supported by the Worker.</summary>
    public sealed record ContentMapDescriptor(string MapKey, MatchMode[] Modes);

    /// <summary>Runs only in the staged executable, without opening a transport or generating content.</summary>
    public static class ServerContentValidation
    {
        private const int MaximumMaps = 256;
        private const int MaximumScenarios = 768;
        private const int ProbeFrames = 120;

        /// <summary>
        /// Opens and validates content for the launcher's Worker descriptor.
        /// A baked package has already recorded its exact supported scenarios,
        /// so those declarations are used without a second scene probe. An
        /// extracted AMHE1 tree has no declaration and is discovered through
        /// the bounded hosting probe instead.
        /// </summary>
        public static ContentMapDescriptor[] DescribeMaps(string directory, string version)
        {
            ServerContent.Open(directory, version);
            ServerContentScenario[] declared = ServerContent.ValidatedScenarios.ToArray();
            if (declared.Length > 0)
            {
                // A baked package is the authority for its catalog. Custom
                // definitions belong to extracted hosting discovery and are
                // intentionally not merged into a package manifest here.
                return BuildMapDescriptors(declared);
            }

            return BuildMapDescriptors(DiscoverExtractedScenarios());
        }

        /// <summary>
        /// Canonicalizes a set of scenario pairs into the stable map/mode
        /// shape emitted by <c>--describe-content</c>.
        /// </summary>
        public static ContentMapDescriptor[] BuildMapDescriptors(IEnumerable<ServerContentScenario> scenarios)
        {
            ServerContentScenario[] entries = CanonicalizeScenarios(scenarios);
            return entries.GroupBy(scenario => scenario.Room, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ContentMapDescriptor(group.Key,
                    group.Select(scenario => scenario.Mode.ToMatchMode()).Distinct()
                        .OrderBy(mode => (byte)mode).ToArray()))
                .ToArray();
        }

        public static int Validate(string directory, string version, IReadOnlyList<RotationEntry> rotation,
            bool hosting = false)
        {
            if (rotation == null || rotation.Count > MaximumScenarios || (!hosting && rotation.Count == 0))
            {
                throw new ProgramException("Content validation requires at most 768 scenarios and a configured match or hosting mode.");
            }
            var required = new HashSet<ServerContentScenario>();
            foreach (RotationEntry entry in rotation)
            {
                if (entry == null || String.IsNullOrWhiteSpace(entry.RoomKey)
                    || entry.Mode is < GameMode.Battle or > GameMode.PrimeHunter
                    || !Single.IsFinite(entry.TimeLimit) || entry.TimeLimit < 0 || entry.PointGoal < 0)
                {
                    throw new ProgramException("Invalid configured server scenario.");
                }
                try { _ = entry.ToMatchRules(); }
                catch (ArgumentException error)
                {
                    throw new ProgramException($"Invalid configured server rules: {error.Message}");
                }
                required.Add(new(entry.RoomKey, entry.Mode));
            }

            // Open validates the package's entire file inventory once. Extracted
            // content has no manifest; the staged parsers check its dependencies.
            ServerContent.Open(directory, version);
            foreach (ServerContentScenario scenario in ServerContent.ValidatedScenarios)
            {
                required.Add(scenario);
            }
            var scenarios = new HashSet<ServerContentScenario>(required);
            if (hosting && ServerContent.ValidatedScenarios.IsEmpty)
            {
                foreach (string room in ServerContentPackage.RetailRooms) { AddModes(scenarios, room); }
                foreach (MapDefinition definition in CustomRooms.Definitions) { AddModes(scenarios, definition.Name); }
            }
            if (scenarios.Count is < 1 or > MaximumScenarios)
            {
                throw new ProgramException("Content validation supports 1–768 unique scenarios.");
            }

            int supported = 0;
            foreach (ServerContentScenario scenario in scenarios)
            {
                ServerContent.RequireRoom(scenario.Room, scenario.Mode);
                string? missing = CustomRooms.WhyUnplayable(scenario.Room);
                if (missing != null) { throw new ProgramException(missing); }
                if (Probe(scenario)) { supported++; }
                else if (required.Contains(scenario))
                {
                    throw new ProgramException($"Missing objectives or incomplete player spawns for {scenario.Room}/{scenario.Mode}.");
                }
                // Extracted hosting discovery excludes unsupported objective or
                // spawn layouts, matching ServerContentPackage.ProbeScenarios. Parser
                // failures and invalid simulation state always fail validation.
            }
            if (supported == 0) { throw new ProgramException("Content has no supported server scenarios."); }
            return supported;
        }

        private static ServerContentScenario[] DiscoverExtractedScenarios()
        {
            var candidates = new HashSet<ServerContentScenario>();
            foreach (string room in ServerContentPackage.RetailRooms) { AddModes(candidates, room); }
            IReadOnlyList<MapDefinition> customDefinitions = PlayableCustomDefinitions();
            foreach (MapDefinition definition in customDefinitions)
            {
                AddModes(candidates, definition.Name);
            }

            var supported = new List<ServerContentScenario>();
            var customRooms = customDefinitions.Select(definition => definition.Name)
                .ToHashSet(StringComparer.Ordinal);
            foreach (ServerContentScenario scenario in candidates.OrderBy(scenario => scenario.Room, StringComparer.Ordinal)
                .ThenBy(scenario => (byte)scenario.Mode))
            {
                if (customRooms.Contains(scenario.Room))
                {
                    if (TryProbeOptional(scenario)) { supported.Add(scenario); }
                }
                else if (Probe(scenario))
                {
                    supported.Add(scenario);
                }
            }
            if (supported.Count == 0) { throw new ProgramException("Content has no supported server scenarios."); }
            return CanonicalizeScenarios(supported);
        }

        private static IReadOnlyList<MapDefinition> PlayableCustomDefinitions()
        {
            var definitions = CustomRooms.Definitions;
            var names = new HashSet<string>(StringComparer.Ordinal);
            var playable = new List<MapDefinition>(definitions.Count);
            foreach (MapDefinition definition in definitions)
            {
                if (definition == null || !IsMapKey(definition.Name))
                {
                    throw new ProgramException("Custom map keys must be printable ASCII and at most 128 characters.");
                }
                if (!names.Add(definition.Name))
                {
                    throw new ProgramException("Custom map definitions contain a duplicate map key: " + definition.Name);
                }
                if (CustomRooms.WhyUnplayable(definition.Name) == null)
                {
                    playable.Add(definition);
                }
            }
            return playable;
        }

        private static bool TryProbeOptional(ServerContentScenario scenario)
        {
            try { return Probe(scenario); }
            catch (Exception error) when (error is ProgramException or ArgumentException or InvalidDataException
                or InvalidOperationException or IOException or EndOfStreamException or IndexOutOfRangeException)
            {
                return false;
            }
        }

        private static ServerContentScenario[] CanonicalizeScenarios(IEnumerable<ServerContentScenario> scenarios)
        {
            if (scenarios == null) { throw new ProgramException("Content description scenarios are required."); }
            ServerContentScenario[] entries = scenarios.ToArray();
            if (entries.Length is < 1 or > MaximumScenarios)
            {
                throw new ProgramException("Content description supports 1–768 map/mode pairs.");
            }

            var maps = new HashSet<string>(StringComparer.Ordinal);
            var pairs = new HashSet<ServerContentScenario>();
            foreach (ServerContentScenario? scenario in entries)
            {
                if (scenario == null || !IsMapKey(scenario.Room) || !IsKnownMode(scenario.Mode)
                    || !pairs.Add(scenario))
                {
                    throw new ProgramException("Content description contains an invalid or duplicate map/mode pair.");
                }
                maps.Add(scenario.Room);
            }
            if (maps.Count > MaximumMaps)
            {
                throw new ProgramException("Content description supports at most 256 maps.");
            }
            return entries.OrderBy(scenario => scenario.Room, StringComparer.Ordinal)
                .ThenBy(scenario => (byte)scenario.Mode).ToArray();
        }

        private static bool IsMapKey(string? value)
            => value is { Length: > 0 and <= 128 } && value.Any(c => !Char.IsWhiteSpace(c))
                && value.All(c => c is >= ' ' and <= '~');

        private static bool IsKnownMode(GameMode mode)
            => mode is GameMode.Battle or GameMode.BattleTeams or GameMode.Survival or GameMode.SurvivalTeams
                or GameMode.Capture or GameMode.Bounty or GameMode.BountyTeams or GameMode.Nodes or GameMode.NodesTeams
                or GameMode.Defender or GameMode.DefenderTeams or GameMode.PrimeHunter;

        private static void AddModes(HashSet<ServerContentScenario> scenarios, string room)
        {
            for (GameMode mode = GameMode.Battle; mode <= GameMode.PrimeHunter; mode++)
            {
                scenarios.Add(new(room, mode));
                if (scenarios.Count > MaximumScenarios)
                {
                    throw new ProgramException("Content validation supports at most 768 unique scenarios.");
                }
            }
        }

        private static bool Probe(ServerContentScenario scenario)
        {
            Read.ClearCache();


            Scene scene = Scene.CreateHeadless();
            try
            {
                scene.LoadServerRoom(scenario.Room, scenario.Mode, players: 8, bots: true,
                    roomPlayerCount: NetConfig.RoomPlayerCount);
                WorldStateCapture.ValidateRoom(scene);
                bool objectives = ServerContentPackage.HasObjectives(scene, scenario.Mode);
                int spawned = 0;
                for (int tick = 0; tick < ProbeFrames; tick++)
                {
                    scene.StepHeadlessFrame();
                    foreach (PlayerEntity player in scene.GetPlayerEntities())
                    {
                        var position = player.Position;
                        if (!Single.IsFinite(position.X) || !Single.IsFinite(position.Y) || !Single.IsFinite(position.Z))
                        {
                            throw new ProgramException($"Invalid player state for {scenario.Room}/{scenario.Mode}/{tick}.");
                        }
                        if (player.Health > 0) { spawned |= 1 << player.SlotIndex; }
                    }
                }
                return spawned == 0xFF && objectives;
            }
            finally { scene.CloseHeadless(); }
        }
    }
}
