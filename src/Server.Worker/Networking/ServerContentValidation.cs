using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Network
{
    /// <summary>Runs only in the staged executable, without opening a transport or generating content.</summary>
    public static class ServerContentValidation
    {
        private const int MaximumScenarios = 768;
        private const int ProbeFrames = 120;

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
