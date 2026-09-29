using System;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Asset-backed checks against the production authority and objective handlers.</summary>
public static class GameModeSceneCheck
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    public static int Run()
    {
        try
        {
            _checks = 0;
            foreach (int count in new[] { 2, 4, 8 })
            foreach (var option in Launcher.OfflineLaunch.Modes)
            {
                GameMode mode = option.Mode;
                var profile = MatchWorldProfile.Resolve(count);
                string room = ThumbnailGenerator.MultiplayerRooms().First(key => MapModeCapabilities.Supports(key, mode, profile, out _));
                var match = new MatchDefinition { RoomKey = room, Mode = mode,
                    PointGoal = MatchGoalRules.DefaultValue(mode), TimeLimitSeconds = 600 };
                var session = new SessionStatePacket { MatchId = 1, AuthorityEpoch = 1,
                    MaxPlayers = (byte)count, Phase = SessionPhase.InMatch, Match = match, WorldProfile = profile };
                var roster = RosterPacket.Create(); roster.Count = (byte)count;
                roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1;
                for (byte i = 0; i < count; i++)
                {
                    roster.Slots[i] = i; roster.Generations[i] = 1; roster.Hunters[i] = (byte)Hunter.Samus;
                    roster.Teams[i] = (sbyte)(GameState.IsTeamMode(mode) ? i % 2 : -1); roster.Names[i] = "MODE TEST";
                }
                var sim = new ServerSim();
                try
                {
                    Check(sim.Start(room, mode, count, _ => { }, () => { }, roster, session), $"{mode}/{count}: authority loads");
                    NetSlotManager.Sync();
                    var players = PlayerEntity.Players;
                    var scene = players[0].OwningScene;
                    var state = scene.GameState;
                    foreach (var player in players.Take(count))
                    {
                        player.LoadFlags |= LoadFlags.Active;
                        player.Health = 99;
                        player.TeamIndex = state.Teams ? player.SlotIndex % 2 : player.SlotIndex;
                    }
                    Check(state.Mode == mode && state.Teams == GameState.IsTeamMode(mode), $"{mode}/{count}: configuration");
                    Check(state.Points.All(p => p == 0) && state.Time.All(t => t == 0), $"{mode}/{count}: fresh round");
                    sim.Step();
                    if (mode is GameMode.Battle or GameMode.BattleTeams)
                    {
                        players[1].TakeDamage(500, DamageFlags.IgnoreInvuln, null, players[0]);
                        Check(state.Points[0] == 1 && state.Kills[0] == 1 && state.Deaths[1] == 1, $"{mode}/{count}: enemy kill");
                        players[0].TakeDamage(500, DamageFlags.IgnoreInvuln, null, null);
                        Check(state.Points[0] == 0 && state.Suicides[0] == 1, $"{mode}/{count}: suicide penalty");
                        state.OneInTheChamber = true;
                        players[0].Spawn(players[0].Position, Vector3.UnitZ, Vector3.UnitY, players[0].NodeRef, respawn: true);
                        int shotCost = Math.Max(1, (int)scene.WeaponRules[(int)BeamType.Imperialist].AmmoCost);
                        Check(players[0].CurrentWeapon == BeamType.Imperialist && players[0].ModAmmo.Ua == shotCost
                            && !players[0].EquipInfo.InfiniteAmmo, $"Chamber/{count}: spawn has exactly one precision shot");
                        players[0].ModSetAmmo(ushort.MaxValue, ushort.MaxValue);
                        Check(players[0].ModAmmo.Ua == shotCost, $"Chamber/{count}: reported ammo cannot grant extra shots");
                        players[0].ApplyChamberAmmo(0, NetSession.NetFrame);
                        Check(players[0].CurrentWeapon == BeamType.PowerBeam, $"Chamber/{count}: empty precision ammo selects Power Beam");
                        players[1].Spawn(players[1].Position, Vector3.UnitZ, Vector3.UnitY, players[1].NodeRef, respawn: true);
                        var precision = new BeamProjectileEntity(scene) { Owner = players[0], Beam = BeamType.Imperialist, BeamKind = BeamType.Imperialist };
                        NetPlayerLifecycle.StampProjectile(precision);
                        players[1].TakeDamage(1, DamageFlags.IgnoreInvuln, null, precision);
                        Check(players[1].Health == 0 && players[0].ModAmmo.Ua == shotCost,
                            $"Chamber/{count}: precision hit is lethal and kill earns one shot");
                        var ammoWorld = ReplayAuthorityWorld.Decode(ReplayAuthorityWorld.Capture(scene, 1, 1, 25).Encode());
                        Check(ammoWorld.Chamber[0].Ammo == shotCost && NetObjectiveSync.Resolve(scene, ammoWorld.Chamber[0].Actor) == players[0],
                            $"Chamber/{count}: authoritative ammo includes exact player life");
                        state.OneInTheChamber = false; state.Fiesta = true;
                        players[0].Spawn(players[0].Position, Vector3.UnitZ, Vector3.UnitY, players[0].NodeRef, respawn: true);
                        var loadout = SpawnLoadoutRules.Fiesta(NetSession.CurrentMatchId, NetPlayerLifecycle.Get(0), 0, state.NoImperialist);
                        Check(players[0].CurrentWeapon == loadout.First && players[0].AvailableWeapons[loadout.Second],
                            $"Fiesta/{count}: spawn selects the deterministic loadout");
                        state.Fiesta = false;
                        players[0].Spawn(players[0].Position, Vector3.UnitZ, Vector3.UnitY, players[0].NodeRef, respawn: true);

                    }
                    else if (state.IsTokenMode)
                    {
                        players[1].TakeDamage(500, DamageFlags.IgnoreInvuln, null, players[0]);
                        ItemInstanceEntity? token = null;
                        foreach (var item in scene.GetItemInstanceEntities())
                            if (item.TokenId > 0 && item.DespawnTimer > 0) { token = item; break; }
                        Check(token != null, $"{mode}/{count}: token exists");
                        Check(state.Points[0] == 0 && token!.TokenVictimSlot == 1 && token.TokenValue == 1,
                            $"{mode}/{count}: death drops a token without awarding kill score");
                        foreach (var player in players.Take(count)) player.Position = token!.Position + new Vector3(100 + player.SlotIndex*10, 0, 0);
                        players[0].Position = token!.Position;
                        token.Process();
                        Check(token.DespawnTimer == 0, $"{mode}/{count}: contact consumes the token");
                        if (mode == GameMode.Headhunter)
                        {
                            Check(state.TokenCarried[0] == 1 && state.Points[0] == 0, $"{mode}/{count}: collected tokens remain unbanked");
                            FlagBaseEntity? bank = null;
                            foreach (var flagBase in scene.GetFlagBaseEntities()) { bank = flagBase; break; }
                            Check(bank != null, $"{mode}/{count}: bank exists");
                            var bankVolume = CollisionVolume.Move(bank!.Data.Volume, bank.Position);
                            players[0].Spawn(bankVolume.GetCenter().AddY(-1), Vector3.UnitZ, Vector3.UnitY, players[0].NodeRef, respawn: true);
                            bank.Process();
                            Check(state.Points[0] == 1 && state.TokenCarried[0] == 0 && state.LargestBank[0] == 1,
                                $"{mode}/{count}: banking awards carried value once");
                            state.TokenCarried[0] = 3;
                            players[0].TakeDamage(500, DamageFlags.IgnoreInvuln, null, null);
                            bool droppedFour = false;
                            foreach (var item in scene.GetItemInstanceEntities())
                                if (item.TokenId > 0 && item.TokenValue == 4) droppedFour = true;
                            Check(state.TokenCarried[0] == 0 && droppedFour,
                                $"{mode}/{count}: death drops all carried tokens plus the victim token");
                        }
                        else
                        {
                            Check(state.Points[0] == 1 && state.TokenConfirms[0] == 1, $"{mode}/{count}: enemy token confirms once");
                            TokenRules.Collect(scene, token, players[0]);
                            Check(state.Points[0] == 1, $"{mode}/{count}: duplicate collection cannot score");
                            players[1].Spawn(players[1].Position, Vector3.UnitZ, Vector3.UnitY, players[1].NodeRef, respawn: true);
                            token.DespawnTimer = 100;
                            TokenRules.Collect(scene, token, players[1]);
                            Check(state.TokenDenies[1] == 1 && state.Points[1] == 0, $"{mode}/{count}: victim recovers token as a denial");
                        }
                        var tokenWorld = ReplayAuthorityWorld.Decode(ReplayAuthorityWorld.Capture(scene, 1, 1, 50).Encode());
                        Check(tokenWorld.TokenStats[8] == state.TokenConfirms[0] && tokenWorld.NextTokenId == state.NextTokenId,
                            $"{mode}/{count}: token statistics and identity survive world serialization");
                        foreach (var player in players.Take(count)) player.Position = new Vector3(1000 + player.SlotIndex*10,1000,1000);
                        token.DespawnTimer = 1;
                        Check(!token.Process() && token.DespawnTimer == 0, $"{mode}/{count}: expired token despawns without scoring");
                    }
                    else if (mode == GameMode.GunGame)
                    {
                        for (int stage = 0; stage < GunGameRules.StageCount; stage++)
                        {
                            players[0].Spawn(players[0].Position, Vector3.UnitZ, Vector3.UnitY, players[0].NodeRef, respawn: true);
                            Check(players[0].CurrentWeapon == GunGameRules.Weapon(stage)
                                && state.Points[0] == stage, $"{mode}/{count}: stage {stage} persists through respawn");
                            typeof(PlayerEntity).GetMethod("PickUpWeapon", BindingFlags.Instance | BindingFlags.NonPublic)!
                                .Invoke(players[0], new object[] { ItemType.OmegaCannon });
                            Check(!players[0].AvailableWeapons[BeamType.OmegaCannon], $"{mode}/{count}: pickup cannot add weapons");
                            players[1].Spawn(players[1].Position, Vector3.UnitZ, Vector3.UnitY, players[1].NodeRef, respawn: true);
                            var shot = new BeamProjectileEntity(scene) { Owner = players[0], Beam = GunGameRules.Weapon(stage), BeamKind = GunGameRules.Weapon(stage) };
                            NetPlayerLifecycle.StampProjectile(shot);
                            players[1].TakeDamage(500, DamageFlags.IgnoreInvuln, null, shot);
                            Check(state.Points[0] == stage + 1 && players[0].CurrentWeapon == GunGameRules.Weapon(stage + 1),
                                $"{mode}/{count}: kill advances exactly one stage");
                        }
                        state.ModeState(scene);
                        Check(state.MatchTime == 0, $"{mode}/{count}: final ladder kill wins");
                        players[0].TakeDamage(500, DamageFlags.IgnoreInvuln, null, null);
                        Check(state.Points[0] == GunGameRules.StageCount, $"{mode}/{count}: suicide cannot erase stage");
                    }
                    else if (mode is GameMode.Survival or GameMode.SurvivalTeams)
                    {
                        float previous = state.Time[0]; state.ModeState(scene);
                        Check(state.Time[0] > previous, $"{mode}/{count}: survival clock");
                        for (int i = 1; i < count; i++)
                        {
                            if (state.Teams && players[i].TeamIndex == players[0].TeamIndex) continue;
                            players[i].Health = 0;
                            state.TeamDeaths[players[i].TeamIndex] = state.PointGoal + 1;
                        }
                        state.ModeState(scene);
                        Check(state.MatchTime == 0 && state.Time[0] == -1, $"{mode}/{count}: last survivor wins");
                    }
                    else if (mode == GameMode.PrimeHunter)
                    {
                        players[1].TakeDamage(500, DamageFlags.IgnoreInvuln, null, players[0]);
                        Check(state.PrimeHunter == 0, $"{mode}/{count}: first killer becomes Prime");
                        players[1].Spawn(players[1].Position, Vector3.UnitZ, Vector3.UnitY, players[1].NodeRef, respawn: true);
                        players[0].Health = 20;
                        players[1].TakeDamage(500, DamageFlags.IgnoreInvuln, null, players[0]);
                        Check(players[0].Health == 90, $"{mode}/{count}: Prime kill heals 70 health");
                        players[1].Spawn(players[1].Position, Vector3.UnitZ, Vector3.UnitY, players[1].NodeRef, respawn: true);
                        players[0].TakeDamage(500, DamageFlags.IgnoreInvuln, null, players[1]);
                        Check(state.PrimeHunter == 1, $"{mode}/{count}: killing Prime transfers ownership");
                        players[1].TakeDamage(500, DamageFlags.IgnoreInvuln, null, null);
                        Check(state.PrimeHunter == -1, $"{mode}/{count}: environmental death clears Prime");
                        players[0].Spawn(players[0].Position, Vector3.UnitZ, Vector3.UnitY, players[0].NodeRef, respawn: true);
                        state.PrimeHunter = 0; state.Time[0] = state.TimeGoal - scene.FrameTime / 2;
                        state.ModeState(scene);
                        Check(state.MatchTime == 0, $"{mode}/{count}: time victory");
                        players[0].LoadFlags &= ~LoadFlags.Active; state.ModeState(scene);
                        Check(state.PrimeHunter == -1, $"{mode}/{count}: inactive Prime clears ownership");
                        players[0].LoadFlags |= LoadFlags.Active;
                    }
                    else if (mode == GameMode.Relic)
                    {
                        OctolithFlagEntity? relic = null;
                        foreach (var flag in scene.GetOctolithFlagEntities()) { relic = flag; break; }
                        Check(relic != null, $"{mode}/{count}: neutral relic exists");
                        var touch = typeof(OctolithFlagEntity).GetMethod("OnTouched", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        touch.Invoke(relic, new object[] { players[0] });
                        float previous = state.Time[0];
                        state.ModeState(scene);
                        Check(state.Time[0] > previous && state.Time[1] == 0, $"{mode}/{count}: only carrier earns time");
                        Check(!(bool)typeof(PlayerEntity).GetMethod("TrySwitchForms", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .Invoke(players[0], new object[] { false })!, $"{mode}/{count}: carrier cannot morph");
                        relic!.OnCaptured();
                        Check(state.Points[0] == 0 && relic.Carrier == players[0], $"{mode}/{count}: deposit cannot score or remove relic");
                        state.Time[0] = state.TimeGoal - scene.FrameTime / 2;
                        state.ModeState(scene);
                        Check(state.MatchTime == 0, $"{mode}/{count}: hold target ends match");
                        players[0].Health = 0; relic.Process();
                        Check(relic.Carrier == null, $"{mode}/{count}: death drops relic");
                        previous = state.Time[0]; state.ModeState(scene);
                        Check(state.Time[0] == previous, $"{mode}/{count}: dropped relic awards no time");
                        players[0].Spawn(players[0].Position, Vector3.UnitZ, Vector3.UnitY, players[0].NodeRef, respawn: true);
                        touch.Invoke(relic, new object[] { players[0] });
                        var captured = ReplayAuthorityWorld.Capture(scene, 1, 1, 17);
                        var decoded = ReplayAuthorityWorld.Decode(captured.Encode());
                        var recordedFlag = decoded.Flags.Single(f => f.Id == relic.Id);
                        Check(recordedFlag.Carrier.Resolve(scene, new ReplayReplicaState()) == null,
                            $"{mode}/{count}: replay identity requires its own roster");
                        Check(NetObjectiveSync.Resolve(scene, recordedFlag.Carrier) == players[0],
                            $"{mode}/{count}: live identity resolves exact carrier life");
                        typeof(OctolithFlagEntity).GetMethod("Reset", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(relic, null);
                        relic.ApplyLiveAuthority(recordedFlag);
                        Check(relic.Carrier == players[0] && players[0].OctolithFlag == relic,
                            $"{mode}/{count}: serialized ownership restores both directions");
                        var roleProperty = typeof(NetSession).GetProperty(nameof(NetSession.Role))!;
                        var authorityProperty = typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!;
                        NetRole savedRole = NetSession.Role;
                        bool savedAuthority = NetSession.IsAuthority;
                        try
                        {
                            roleProperty.SetValue(null, NetRole.Client);
                            authorityProperty.SetValue(null, false);
                            typeof(OctolithFlagEntity).GetMethod("Reset", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(relic, null);
                            NetObjectiveSync.Apply(scene, decoded);
                            Check(relic.Carrier == players[0], $"{mode}/{count}: client applies matching world facts");
                            previous = state.Time[0]; state.ModeState(scene);
                            Check(state.Time[0] == previous, $"{mode}/{count}: client cannot author hold time");
                            typeof(OctolithFlagEntity).GetMethod("Reset", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(relic, null);
                            decoded.MatchId++;
                            NetObjectiveSync.Apply(scene, decoded);
                            Check(relic.Carrier == null, $"{mode}/{count}: client rejects previous/other match ownership");
                            decoded.MatchId--; decoded.Epoch++;
                            NetObjectiveSync.Apply(scene, decoded);
                            Check(relic.Carrier == null, $"{mode}/{count}: client rejects other authority ownership");
                            decoded.Epoch--;
                        }
                        finally
                        {
                            roleProperty.SetValue(null, savedRole);
                            authorityProperty.SetValue(null, savedAuthority);
                        }
                        players[0].Spawn(players[0].Position, Vector3.UnitZ, Vector3.UnitY, players[0].NodeRef, respawn: true);
                        relic.ApplyLiveAuthority(recordedFlag);
                        Check(relic.Carrier == null && players[0].OctolithFlag == null,
                            $"{mode}/{count}: old-life ownership cannot attach to respawn");
                        touch.Invoke(relic, new object[] { players[0] });
                    }
                    else if (MatchModifierRules.UsesOctolith(mode))
                    {
                        var flags = new System.Collections.Generic.List<OctolithFlagEntity>();
                        foreach (var flag in scene.GetOctolithFlagEntities()) flags.Add(flag);
                        var enemy = flags.First(flag => mode != GameMode.Capture || flag.Data.TeamId != players[0].TeamIndex);
                        var touch = typeof(OctolithFlagEntity).GetMethod("OnTouched", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        if (mode == GameMode.Capture)
                        {
                            var own = flags.First(flag => flag.Data.TeamId == players[0].TeamIndex);
                            Check(!(bool)touch.Invoke(own, new object[] { players[0] })!, $"{mode}/{count}: own flag cannot be carried");
                        }
                        Check((bool)touch.Invoke(enemy, new object[] { players[0] })! && enemy.Carrier == players[0], $"{mode}/{count}: pickup");
                        if (mode == GameMode.Capture)
                        {
                            var own = flags.First(flag => flag.Data.TeamId == players[0].TeamIndex);
                            Check((bool)touch.Invoke(own, new object[] { players[1] })!, $"{mode}/{count}: opponent steals own flag");
                            FlagBaseEntity? home = null;
                            foreach (var entity in scene.GetFlagBaseEntities())
                                if (entity is FlagBaseEntity flagBase && flagBase.Data.TeamId == players[0].TeamIndex) home = flagBase;
                            Check(home != null, $"{mode}/{count}: home base exists");
                            var volume = CollisionVolume.Move(home!.Data.Volume, home.Position);
                            Vector3 center = volume.GetCenter();
                            players[0].Spawn(center.AddY(-1), Vector3.UnitZ, Vector3.UnitY, players[0].NodeRef, respawn: true);
                            // Spawn clears the carry pointer; restore through the production pickup handler.
                            typeof(OctolithFlagEntity).GetMethod("Reset", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(enemy, null);
                            touch.Invoke(enemy, new object[] { players[0] });
                            home.Process();
                            Check(state.Points[0] == 0 && enemy.Carrier == players[0], $"{mode}/{count}: missing own flag blocks deposit");
                            typeof(OctolithFlagEntity).GetMethod("Reset", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(own, null);
                            Check(volume.TestPoint(players[0].Position), $"{mode}/{count}: carrier is inside deposit volume");
                            Check(players[0].OctolithFlag == enemy, $"{mode}/{count}: carrier retains pickup pointer");
                            Check(flags.Where(f => f.Data.TeamId == players[0].TeamIndex).All(f => f.AtBase), $"{mode}/{count}: own flags restored");
                            home.Process();
                        }
                        else enemy.OnCaptured();
                        Check(state.Points[0] == 1 && state.OctolithScores[0] == 1, $"{mode}/{count}: capture scores");
                        foreach (bool reset in new[] { false, true })
                        {
                            players[0].Health = 99; touch.Invoke(enemy, new object[] { players[0] });
                            state.OctolithReset = reset; players[0].Health = 0; enemy.Process();
                            Check(enemy.Carrier == null && enemy.AtBase == reset, $"{mode}/{count}: death auto reset {reset}");
                            players[0].Health = 99;
                        }
                    }
                    if (mode is GameMode.Nodes or GameMode.NodesTeams or GameMode.Defender or GameMode.DefenderTeams or GameMode.Hardpoint or GameMode.HardpointTeams)
                    {
                        NodeDefenseEntity? node = null;
                        foreach (var candidate in scene.GetNodeDefenseEntities())
                            if (!state.IsHardpoint || candidate.Id == state.ActiveHardpointId) { node = candidate; break; }
                        Check(node != null, $"{mode}/{count}: objective exists");
                        var volume = node!.Volume;
                        Vector3 center = volume.Type switch
                        {
                            VolumeType.Cylinder => volume.CylinderPosition + volume.CylinderVector * volume.CylinderDot / 2,
                            VolumeType.Sphere => volume.SpherePosition,
                            _ => volume.BoxPosition + (volume.BoxVector1 * volume.BoxDot1
                                + volume.BoxVector2 * volume.BoxDot2 + volume.BoxVector3 * volume.BoxDot3) / 2
                        };
                        void Place(PlayerEntity player, Vector3 target)
                        {
                            Vector3 offset = player.Volume.SpherePosition - player.Position;
                            player.Spawn(target - offset - Vector3.UnitY, Vector3.UnitZ, Vector3.UnitY, player.NodeRef, respawn: true);
                        }
                        foreach (var player in players.Take(count)) Place(player, center + new Vector3(1000, 1000, 1000));
                        Check(node.CurrentTeam == NodeDefenseEntity.NoTeam, $"{mode}/{count}: objective neutral");
                        Place(players[0], center); node.Process();
                        bool defender = MatchGoalRules.UsesTimeTarget(mode);
                        Check(defender ? state.TeamTime[0] > 0 : node.Progress > 0, $"{mode}/{count}: occupancy advances objective");
                        float before = defender ? state.TeamTime[0] : node.Progress;
                        Place(players[1], center); node.Process();
                        Check(node.Contested && (defender ? state.TeamTime[0] : node.Progress) == before,
                            $"{mode}/{count}: enemy contest pauses scoring/capture");
                        Place(players[1], center + new Vector3(1000, 1000, 1000));
                        if (!defender)
                        {
                            for (int frame = 0; frame < 610; frame++) node.Process();
                            Check(node.CurrentTeam == players[0].TeamIndex && state.NodesCaptured[0] > 0,
                                $"{mode}/{count}: ownership capture");
                        }
                        Place(players[0], center + new Vector3(1000, 1000, 1000));
                        before = state.TeamTime[0]; node.Process();
                        Check(defender ? state.TeamTime[0] == before : !node.IsOccupied,
                            $"{mode}/{count}: leaving clears occupancy");
                        if (state.IsHardpoint)
                        {
                            int first = state.ActiveHardpointId;
                            state.HardpointTicksRemaining = 1;
                            state.ModeState(scene);
                            Check(state.ActiveHardpointId != first && state.HardpointTicksRemaining == HardpointRules.RotationTicks,
                                $"{mode}/{count}: rotation selects next node and resets 60-second clock");
                            Place(players[0], center);
                            before = state.TeamTime[0]; node.Process();
                            Check(state.TeamTime[0] == before && node.CurrentTeam == NodeDefenseEntity.NoTeam,
                                $"{mode}/{count}: inactive node cannot score or retain ownership");
                            var world = ReplayAuthorityWorld.Decode(ReplayAuthorityWorld.Capture(scene, 1, 1, 20).Encode());
                            Check(world.ActiveHardpointId == state.ActiveHardpointId
                                && world.HardpointTicksRemaining == state.HardpointTicksRemaining,
                                $"{mode}/{count}: active node and rotation clock survive world serialization");
                        }
                        if (!defender)
                        {
                            int points = state.Points[0];
                            for (int frame = 0; frame < 1000; frame++) node.Process();
                            Check(state.Points[0] > points, $"{mode}/{count}: owned node scores independently of occupancy");
                        }
                    }
                    // Exercise each mode's real victory handler and reset between rounds.
                    if (!MatchGoalRules.UsesLives(mode) && mode != GameMode.PrimeHunter && mode != GameMode.Relic && mode != GameMode.GunGame)
                    {
                        state.MatchTime = 600;
                        if (MatchGoalRules.UsesTimeTarget(mode)) state.TeamTime[0] = state.TimeGoal;
                        else state.TeamPoints[0] = state.PointGoal;
                        state.ModeState(scene);
                        Check(state.MatchTime == 0, $"{mode}/{count}: objective target ends match");
                    }
                    if (MatchGoalRules.UsesTimeTarget(mode))
                    {
                        state.MatchTime = 600; state.TimeGoal = 0;
                        if (mode == GameMode.PrimeHunter) state.PrimeHunter = 0;
                        state.ModeState(scene);
                        Check(state.MatchTime == 600, $"{mode}/{count}: disabled time target does not end match");
                    }
                    state.ResetRoundState();
                    Check(state.Points.All(p => p == 0) && state.TeamPoints.All(p => p == 0)
                        && state.Time.All(t => t == 0) && state.TeamTime.All(t => t == 0) && state.PrimeHunter == -1,
                        $"{mode}/{count}: rematch clears objective progress");
                    Check(sim.Frames > 0 && sim.StepFailures == 0, $"{mode}/{count}: no simulation failures");
                }
                finally { sim.Stop(); NetSession.Stop(); }
            }
            Console.WriteLine($"[gamemodecheck-scene] PASS {_checks} authority checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("[gamemodecheck-scene] FAIL " + ex); return 1; }
    }
}
