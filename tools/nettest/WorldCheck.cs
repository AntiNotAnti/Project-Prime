using System;
using System.Collections.Generic;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Network;

namespace MphRead.NetTest
{
    /// <summary>Asset-backed capture/assembly/presentation probe; run once per process and mode.</summary>
    internal static class WorldCheck
    {
        private static int Inventory(string dataDirectory)
        {
            ServerContent.Open(dataDirectory, "AMHE1");
            int rooms = 0, unsupported = 0;
            foreach (RoomMetadata room in Metadata.RoomList)
            {
                if (!room.Multiplayer || room.FirstHunt || room.EntityPath == null) { continue; }
                int surfaces = 0;
                foreach (Entity entity in Read.GetEntities(room.EntityPath, -1, false))
                {
                    if (entity.Type is EntityType.Door or EntityType.ForceField or EntityType.Platform) { surfaces++; }
                }
                Console.WriteLine($"WORLDINVENTORY {room.Name} mutableCollisionEntities={surfaces}");
                rooms++; unsupported += surfaces;
            }
            Console.WriteLine($"WORLDINVENTORY rooms={rooms} unsupported={unsupported} {(unsupported == 0 && rooms > 0 ? "PASS" : "FAIL")}");
            return unsupported == 0 && rooms > 0 ? 0 : 1;
        }

        private static string SelectRoom(GameMode mode)
        {
            int layer = Metadata.GetMultiplayerEntityLayer(mode, NetLaunch.RoomPlayerCount);
            foreach (RoomMetadata room in Metadata.RoomList)
            {
                if (!room.Multiplayer || room.FirstHunt || room.EntityPath == null) { continue; }
                int flagTeams = 0, baseTeams = 0, spawns = 0;
                bool hasFlag = false, hasBase = false, hasNode = false;
                foreach (Entity entity in Read.GetEntities(room.EntityPath, layer, false))
                {
                    if (entity is Entity<OctolithFlagEntityData> flag)
                    { hasFlag = true; if (flag.Data.TeamId < 2) { flagTeams |= 1 << (int)flag.Data.TeamId; } }
                    else if (entity is Entity<FlagBaseEntityData> flagBase)
                    { hasBase = true; if (flagBase.Data.TeamId < 2) { baseTeams |= 1 << (int)flagBase.Data.TeamId; } }
                    else if (entity.Type == EntityType.NodeDefense) { hasNode = true; }
                    else if (entity is Entity<PlayerSpawnEntityData> spawn && spawn.Data.Active != 0) { spawns++; }
                }
                bool supported = mode switch
                {
                    GameMode.Nodes or GameMode.NodesTeams or GameMode.Defender or GameMode.DefenderTeams => hasNode,
                    GameMode.Bounty or GameMode.BountyTeams => hasFlag && hasBase,
                    GameMode.Capture => flagTeams == 3 && baseTeams == 3,
                    _ => true
                };
                if (supported && spawns >= 2) { return room.Name; }
            }
            throw new InvalidOperationException($"No extracted multiplayer room supplies {mode} objectives.");
        }

        private static int RejectEmptyObjectives(string dataDirectory)
        {
            ServerContent.Open(dataDirectory, "AMHE1");
            try
            {
                using var simulation = new ServerSimulation(new RotationEntry { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Bounty });
                Console.Error.WriteLine("WORLDCHECK missing objective configuration was accepted.");
                return 1;
            }
            catch (ProgramException error) when (error.Message.Contains("no required objectives", StringComparison.Ordinal))
            {
                if (Read.ServerMode) { throw new InvalidOperationException("Rejected room left the global server read mode enabled."); }
                Console.WriteLine("WORLDCHECK missing Bounty objectives rejected before accepting players PASS");
                return 0;
            }
        }

        public static int Run(string[] args)
        {
            if (args.Length == 3 && args[2] == "inventory") { return Inventory(args[1]); }
            if (args.Length == 3 && args[2] == "reject-empty-objectives") { return RejectEmptyObjectives(args[1]); }
            if (args.Length != 3 || !Enum.TryParse(args[2], out GameMode mode)
                || mode < GameMode.Battle || mode > GameMode.PrimeHunter)
            {
                Console.Error.WriteLine("Usage: nettest --world-check DATA_DIRECTORY MODE");
                return 2;
            }
            ServerContent.Open(args[1], "AMHE1");
            string room = SelectRoom(mode);
            Scene scene = Scene.CreateHeadless();
            try
            {
                scene.LoadServerRoom(room, mode, 8, bots: true, roomPlayerCount: NetLaunch.RoomPlayerCount);
                scene.Match.Phase = MatchPhase.Playing;
                var capture = new WorldStateCapture();
                var client = new ClientWorldState(); client.Reset(7);
                byte[] body = new byte[WorldPacket.MaxSize];
                int maximum = 0, flagStates = 0, nodeStates = 0;
                for (uint tick = 0; tick < 3600; tick++)
                {
                    scene.StepHeadlessFrame();
                    if (tick % 6 != 0) { continue; }
                    capture.Capture(scene, 7, tick / 6, tick);
                    maximum = Math.Max(maximum, capture.Count);
                    foreach (WorldRecord state in capture.Records)
                    { if (state.Kind == WorldRecordKind.Flag) { flagStates++; } else if (state.Kind == WorldRecordKind.Node) { nodeStates++; } }
                    for (int batch = capture.BatchCount - 1; batch >= 0; batch--)
                    {
                        int length = capture.WriteBatch(body, batch);
                        if (!WorldPacket.TryValidate(body.AsSpan(0, length), 7)) { throw new InvalidOperationException("Captured world failed validation."); }
                        client.Receive(body.AsSpan(0, length));
                    }
                    if (!client.HasState || client.Revision != capture.Revision || !capture.Records.SequenceEqual(client.Records))
                    { throw new InvalidOperationException("World capture and assembled state differ."); }
                }
                // Reusing a slot cannot inherit the previous connection's objectives.
                PlayerEntity departing = PlayerEntity.Players[7];
                foreach (NodeDefenseEntity node in scene.GetNodeDefenseEntities())
                {
                    if (mode is not (GameMode.Nodes or GameMode.NodesTeams or GameMode.Defender or GameMode.DefenderTeams)) { break; }
                    WorldRecord state = node.CaptureWorldState() with { Slot = 7, A = (uint)departing.TeamIndex | (8u << 8) };
                    node.ApplyWorldState(state);
                }
                if (scene.Match.Rules.IsOctolithMode)
                {
                    foreach (OctolithFlagEntity flag in scene.GetOctolithFlagEntities())
                    { flag.ApplyWorldState(flag.CaptureWorldState() with { Slot = 7, Flags = 2 }); break; }
                }
                scene.Match.PrimeHunter = 7;
                WorldStateCapture.ReleasePlayer(scene, departing);
                if (scene.Match.PrimeHunter != -1 || departing.OctolithFlag != null) { throw new InvalidOperationException("Departing player retained an objective."); }
                foreach (NodeDefenseEntity node in scene.GetNodeDefenseEntities())
                { if (node.CapturedPlayer == departing) { throw new InvalidOperationException("Node score ownership survived slot release."); } }
                foreach (OctolithFlagEntity flag in scene.GetOctolithFlagEntities())
                { if (flag.Carrier == departing) { throw new InvalidOperationException("Flag ownership survived slot release."); } }
                // Emulate a newly loaded client scene with no locally spawned items.
                var originals = new List<ItemInstanceEntity>();
                foreach (ItemInstanceEntity item in scene.GetItemInstanceEntities()) { originals.Add(item); }
                foreach (ItemInstanceEntity item in originals)
                {
                    if (item.Owner?.Item == item) { item.Owner.Item = null; }
                    item.Destroy(); scene.RemoveEntity(item);
                }
                client.Apply(scene);
                int expectedItems = 0, actualItems = 0;
                foreach (WorldRecord state in client.Records) { if (state.Kind == WorldRecordKind.Item) { expectedItems++; } }
                foreach (ItemInstanceEntity item in scene.GetItemInstanceEntities()) { actualItems++; }
                if (expectedItems != actualItems) { throw new InvalidOperationException("Late join item presentation is incomplete."); }
                client.Apply(scene);
                int duplicateItems = 0;
                foreach (ItemInstanceEntity item in scene.GetItemInstanceEntities()) { duplicateItems++; }
                if (duplicateItems != actualItems) { throw new InvalidOperationException("Repeated application duplicated an item."); }
                Console.WriteLine($"WORLDCHECK {mode} room={room} ticks=3600 frames=600 maxRecords={maximum} items={actualItems} flags={flagStates} nodes={nodeStates} PASS");
                return 0;
            }
            finally { scene.CloseHeadless(); }
        }
    }
}
