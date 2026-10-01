using System;
using System.Buffers.Binary;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using OpenTK.Mathematics;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Network
{
    public static partial class NetLobbyTest
    {
        public static int RunAdvancedRules()
        {
            try { AdvancedRulesChecks(); AdvancedRulesScenario(); Console.WriteLine($"[advanced-rules] PASS {_checks} checks"); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { NetSession.Stop(); }
        }
        public static int RunAdvancedRulesScene()
        {
            try
            {
                foreach (var option in Launcher.OfflineLaunch.Modes)
                {
                    if (option.Mode is GameMode.GunGame or GameMode.OneInTheChamber)
                    {
                        Check(!MatchModifierRules.Validate(new MatchDefinition { Mode = option.Mode, InstaGib = true }, out _),
                            option.Label + " rejects a modifier that would replace its loadout");
                        continue;
                    }
                    string room = ThumbnailGenerator.MultiplayerRooms().First(key =>
                        MapModeCapabilities.Supports(key, option.Mode, MatchWorldProfile.Resolve(8), out _));
                    var definition = new MatchDefinition { RoomKey = room, Mode = option.Mode,
                        InstaGib = true, LowTier = true, TimeLimitSeconds = 600, PointGoal = MatchGoalRules.DefaultValue(option.Mode) };
                    var session = new SessionStatePacket { MatchId = 1, AuthorityEpoch = 1, MaxPlayers = 8,
                        Phase = SessionPhase.InMatch, Match = definition, WorldProfile = MatchWorldProfile.Resolve(8) };
                    var roster = RosterPacket.Create(); roster.Count = 8; roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1;
                    for (byte i = 0; i < 8; i++)
                    { roster.Slots[i] = i; roster.Generations[i] = 1; roster.Names[i] = "RULE TEST";
                        roster.Hunters[i] = (byte)HunterRules.RandomAllowed(i, true); roster.Teams[i] = (sbyte)(i % 2); }
                    var sim = new ServerSim();
                    try
                    {
                        Check(sim.Start(room, option.Mode, 8, _ => { }, () => { }, roster, session), option.Label + " loads");
                        NetSlotManager.Sync();
                        var player = PlayerEntity.Players[0];
                        var game = player.OwningScene.GameState;
                        string handler = game.ModeState.Method.Name;
                        Check(game.Mode == option.Mode && game.InstaGib && game.LowTier, option.Label + " keeps base mode and modifiers");
                        Check(handler == "ModeState" + (option.Mode.ToString().Replace("Teams", "")), option.Label + " uses base objective handler");
                        for (int life = 0; life < 3; life++)
                        {
                            player.Spawn(player.Position, Vector3.UnitZ, Vector3.UnitY, player.NodeRef, respawn: life > 0);
                            Check(player.CurrentWeapon == BeamType.Imperialist && player.EquipInfo.InfiniteAmmo
                                && Enumerable.Range(0, 9).All(i => player.AvailableWeapons[(BeamType)i] == (i == (int)BeamType.Imperialist)),
                                option.Label + " Imperialist-only respawn " + life);
                            typeof(PlayerEntity).GetMethod("PickUpWeapon", BindingFlags.Instance | BindingFlags.NonPublic)!
                                .Invoke(player, new object[] { ItemType.ShockCoil });
                            player.ModSetWeapon(BeamType.ShockCoil);
                            Check(!player.AvailableWeapons[BeamType.ShockCoil], option.Label + " rejects weapon pickup " + life);
                            var ammo = (int[])typeof(PlayerEntity).GetField("_ammo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
                            Check(ammo[0] == 99, option.Label + " displays 99 UA " + life);
                        }
                        Check(PlayerEntity.Players.All(p => HunterRules.Allowed(p.Hunter, true)), "eight-player Low Tier construction");
                        Check(sim.StepFailures == 0, "no authority step failures");
                    }
                    finally { sim.Stop(); NetSession.Stop(); }
                }
                CheckNoImpScene();
                Console.WriteLine($"[advanced-rules-scene] PASS {_checks} checks"); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private static void CheckNoImpScene()
        {
            const string room = "MP1 SANCTORUS";
            var match = new MatchDefinition { RoomKey = room, Mode = GameMode.Battle,
                NoImperialist = true, AffinityWeapons = true, TimeLimitSeconds = 600, PointGoal = 100 };
            var session = new SessionStatePacket { MatchId = 1, AuthorityEpoch = 1, MaxPlayers = 8,
                Phase = SessionPhase.InMatch, Match = match, WorldProfile = MatchWorldProfile.Resolve(8) };
            var roster = RosterPacket.Create(); roster.Count = 1; roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1;
            roster.Slots[0] = 0; roster.Generations[0] = 1; roster.Names[0] = "TRACE"; roster.Hunters[0] = (byte)Hunter.Trace;
            var sim = new ServerSim();
            try
            {
                Check(sim.Start(room, GameMode.Battle, 8, _ => { }, () => { }, roster, session), "No Imp world loads");
                NetSlotManager.Sync();
                var player = PlayerEntity.Players[0]; var scene = player.OwningScene;
                Check(scene.GameState.NoImperialist && player.Hunter == Hunter.Trace, "No Imp allows Trace");
                foreach (var entity in scene.Entities)
                    if (entity is ItemSpawnEntity spawn) Check(spawn.Data.ItemType != ItemType.Imperialist, "No Imp removes Imperialist spawn before construction");
                for (int life = 0; life < 3; life++)
                {
                    player.Spawn(player.Position, Vector3.UnitZ, Vector3.UnitY, player.NodeRef, respawn: life > 0);
                    foreach (var item in new[] { ItemType.Imperialist, ItemType.AffinityWeapon })
                    {
                        typeof(PlayerEntity).GetMethod("PickUpWeapon", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(player, new object[] { item });
                        player.ModSetWeapon(BeamType.Imperialist);
                        Check(!player.AvailableWeapons[BeamType.Imperialist], "Trace cannot acquire Imperialist on life " + life);
                    }
                }
                var drop = new ItemInstanceEntity(new ItemInstanceEntityData(Vector3.Zero, ItemType.Imperialist, 60), default, scene);
                Check(drop.ItemType != ItemType.Imperialist, "scripted and dropped Imperialist use the same restriction");
            }
            finally { sim.Stop(); NetSession.Stop(); }
        }

        private static void AdvancedRulesChecks()
        {
            var defaults = new MatchDefinition();
            Check(!defaults.ShadowFreeze && !defaults.SpawnProtection && !defaults.InstaGib
                && !defaults.LowTier && !defaults.NoImperialist, "advanced rules default off");
            Check(!new MatchStatePacket().ShadowFreeze && !new MatchStatePacket().SpawnProtection, "zero match flags mean off");
            var settings = new MenuSettings();
            Check(settings.ShadowFreeze == "off" && settings.SpawnProtection == "off", "new settings opt in to protection and shadow freeze");
            var state = new SessionStatePacket { MaxPlayers = 8, MatchId = 1, AuthorityEpoch = 1,
                Match = new MatchDefinition { RoomKey = Rooms()[0], Mode = GameMode.Capture,
                    InstaGib = true, LowTier = true, ShadowFreeze = true, SpawnProtection = true } };
            byte[] bytes = new byte[SessionStatePacket.Size];
            state.Write(bytes);
            Check(SessionStatePacket.TryRead(bytes, out var read) && read.Match == state.Match, "all combat rules survive session round trip");
            state.Match = state.Match with { InstaGib = false, NoImperialist = true };
            state.Write(bytes);
            Check(SessionStatePacket.TryRead(bytes, out read) && read.Match.NoImperialist && read.Match.LowTier, "No Imp survives session round trip");
            foreach (bool insta in new[] { false, true })
            {
                state.Match = state.Match with { InstaGib = insta, NoImperialist = !insta };
                var decoder = new ReplayReplicaState();
                byte[] matchPacket = new byte[1 + MatchStatePacket.Size]; matchPacket[0] = (byte)PacketType.MatchState;
                new MatchStatePacket { RoomKey = state.Match.RoomKey, Mode = (byte)state.Match.Mode,
                    MatchId = state.MatchId, AuthorityEpoch = state.AuthorityEpoch }.Write(matchPacket.AsSpan(1));
                decoder.Accept(matchPacket, 0);
                byte[] sessionPacket = new byte[1 + SessionStatePacket.Size]; sessionPacket[0] = (byte)PacketType.SessionState;
                state.Write(sessionPacket.AsSpan(1)); decoder.Accept(sessionPacket, 0);
                var restored = new ReplayReplicaState(); restored.RestoreCheckpoint(decoder.CaptureCheckpoint());
                Check(restored.Configuration?.Match == state.Match, "replay checkpoint retains every modifier");
            }
            var legacy = new MatchDefinition { Mode = GameMode.InstaGib }.NormalizeLegacy();
            Check(legacy.Mode == GameMode.Battle && legacy.InstaGib, "legacy mode normalizes at data boundary");
            foreach (var mode in Launcher.OfflineLaunch.Modes.Select(m => m.Mode))
            {
                var match = new MatchDefinition { RoomKey = Rooms()[0], Mode = mode, InstaGib = true };
                Check(LobbyRules.ValidateDefinition(match, out _) == (mode is GameMode.GunGame or GameMode.OneInTheChamber ? LobbyResultCode.InvalidConfiguration : LobbyResultCode.Ok), $"{mode} validates Insta-Gib compatibility");
                Check(LobbyRules.ValidateDefinition(match with { NoImperialist = true }, out _) == LobbyResultCode.InvalidConfiguration, $"{mode} rejects conflicting weapons");
            }
            foreach (Hunter hunter in Enum.GetValues<Hunter>())
            {
                bool expected = hunter is Hunter.Kanden or Hunter.Spire or Hunter.Noxus or Hunter.Weavel;
                Check(HunterRules.Allowed(hunter, true) == expected, $"Low Tier membership {hunter}");
                Check(HunterRules.Sanitize(hunter, true) == (expected ? hunter : Hunter.Kanden), $"Low Tier fallback {hunter}");
            }
            Check(Enumerable.Range(0, 4096).All(i => HunterRules.Allowed(HunterRules.RandomAllowed((uint)i, true), true)), "4096 random selections obey Low Tier");
            var replacements = Enumerable.Range(0, 256).Select(id => WeaponResourceRules.Replacement("TEST MAP", id)).ToArray();
            Check(replacements.Distinct().Count() == 5, "spawn identities vary across the full replacement pool");
            Check(replacements.All(type => type is ItemType.VoltDriver or ItemType.Battlehammer or ItemType.Judicator or ItemType.Magmaul or ItemType.ShockCoil), "replacement pool excludes Imperialist and special weapons");
            Check(replacements.SequenceEqual(Enumerable.Range(0, 256).Select(id => WeaponResourceRules.Replacement("TEST MAP", id))), "reconstruction preserves replacements");
            Check(WeaponResourceRules.ResolveBeam(BeamType.Imperialist, true, "TEST MAP", 0) != BeamType.Imperialist, "Trace affinity cannot grant Imperialist");
            byte[] item = new byte[72]; BinaryPrimitives.WriteInt32LittleEndian(item.AsSpan(44), (int)ItemType.Imperialist);
            var data = Read.ReadStruct<ItemSpawnEntityData>(item);
            var room = Metadata.RoomMetadata[Rooms()[0]];
            var resolved = MapResourceRules.ResolveData(room, ResourceSpawnProfile.Vanilla, data, true);
            Check(resolved.ItemType == WeaponResourceRules.Replacement(room.Name, data.Header.EntityId), "map rule transforms even vanilla resource profiles");
            Check(MapResourceRules.ResolveData(room, ResourceSpawnProfile.Vanilla, data).ItemType == ItemType.Imperialist, "ordinary maps retain Imperialist");
            var old = new byte[1 + MatchStatePacket.Size]; old[0] = (byte)PacketType.MatchState;
            new MatchStatePacket { Mode = (byte)GameMode.InstaGib }.Write(old.AsSpan(1));
            var adapted = MatchStatePacket.Read(ReplayIdentityCompatibility.Convert(old, 27)[1..]);
            Check(adapted.ShadowFreeze && adapted.SpawnProtection, "legacy replay zero flags retain historical enabled rules");
            Check(old[11] == 0, "replay adaptation never mutates source bytes");
            var oldDecoder = new ReplayReplicaState();
            new MatchStatePacket { RoomKey = "MP1 SANCTORUS", Mode = (byte)GameMode.InstaGib,
                MatchId = 1, AuthorityEpoch = 1 }.Write(old.AsSpan(1));
            oldDecoder.Accept(old, 0);
            byte[] historicalCheckpoint = oldDecoder.CaptureCheckpoint().Bytes.ToArray();
            // Build actual protocol-27 bytes; changing only the version label would
            // leave protocol-29 player extensions in the historical fixture.
            var historicalBytes = historicalCheckpoint.ToList();
            // Schemas 5/6 add empty semantic-history counts/frontiers and require protocol 35.
            // This fixture represents schema 4/protocol 27, not a relabelled current schema.
            historicalBytes.RemoveRange(historicalBytes.Count - 16, 16);
            historicalBytes[4] = 4; historicalBytes[5] = 0;
            const int matchStart = 46;
            int rosterStart = matchStart + MatchStatePacket.Size + 1;
            int firstPlayer = rosterStart + RosterPacket.Size;
            int stride = 7 + PlayerState.Size + 1 + IntentPacket.FullSize + 4;
            for (int slot = PlayerEntity.SlotCapacity - 1; slot >= 0; slot--)
                historicalBytes.RemoveRange(firstPlayer + slot * stride + 7 + PlayerState.LegacySize,
                    Mods.EnhancedHunters.EnhancedHunterNetState.Size);
            // Protocol 33 added one handicap byte to every fixed roster entry.
            // A protocol-27 checkpoint genuinely predates those bytes, so remove
            // them rather than only relabelling a current-width roster.
            for (int slot = RosterPacket.MaxSlots - 1; slot >= 0; slot--)
                historicalBytes.RemoveAt(rosterStart + RosterPacket.HeaderSize
                    + slot * RosterPacket.EntrySize + RosterPacket.EntrySize - 1);
            historicalBytes.RemoveRange(matchStart + 103, 2);
            historicalCheckpoint = historicalBytes.ToArray();
            historicalCheckpoint[6] = 27;
            var legacyDecoder = new ReplayReplicaState(); legacyDecoder.RestoreCheckpoint(new(historicalCheckpoint));
            Check(legacyDecoder.Match is { ShadowFreeze: true, SpawnProtection: true }, "historical decoder checkpoints adapt negative flags");
        }
        private static void AdvancedRulesScenario()
        {
            using var rig = new Rig();
            Client owner = rig.Add(880), other = rig.Add(881);
            var config = owner.State!.Value;
            config.Match = config.Match with { LowTier = true, NoImperialist = true, ShadowFreeze = false, SpawnProtection = false };
            rig.Expect(owner, owner.Command(LobbyCommandType.UpdateMatch, config: config), LobbyResultCode.Ok);
            Check(Enumerable.Range(0, owner.Roster.Count).All(i => owner.Roster.Hunters[i] == (byte)Hunter.Kanden), "enabling Low Tier remaps existing roster");
            other.Identify((byte)Hunter.Weavel);
            rig.Wait(() => owner.Roster.Hunters[Array.IndexOf(owner.Roster.Slots, (byte)other.Slot)] == (byte)Hunter.Weavel, "allowed Identify is accepted");
            other.Identify((byte)Hunter.Trace);
            rig.Wait(() => owner.Roster.Hunters[Array.IndexOf(owner.Roster.Slots, (byte)other.Slot)] == (byte)Hunter.Kanden, "direct Identify is sanitized");
            rig.Expect(owner, owner.Command(LobbyCommandType.AddBot, hunter: (byte)Hunter.Sylux), LobbyResultCode.Ok);
            Check(Enumerable.Range(0, owner.Roster.Count).All(i => HunterRules.Allowed((Hunter)owner.Roster.Hunters[i], true)), "server bots obey Low Tier");
            var conflict = owner.State!.Value;
            conflict.Match = conflict.Match with { InstaGib = true };
            rig.Expect(owner, owner.Command(LobbyCommandType.UpdateMatch, config: conflict), LobbyResultCode.InvalidConfiguration);
            rig.ReadyAll();
            rig.Expect(owner, owner.Command(LobbyCommandType.StartMatch), LobbyResultCode.Ok);
            owner.Loaded(); other.Loaded();
            rig.Wait(() => owner.State!.Value.Phase == SessionPhase.InMatch, "modified match starts");
            Client joiner = rig.Add(882);
            Check(joiner.State!.Value.Match.LowTier && joiner.State.Value.Match.NoImperialist
                && !joiner.State.Value.Match.ShadowFreeze && !joiner.State.Value.Match.SpawnProtection,
                "late join receives exact authoritative modifiers before play");
        }
    }
}
