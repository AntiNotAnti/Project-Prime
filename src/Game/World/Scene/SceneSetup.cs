using System;
using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Text;
using OpenTK.Mathematics;

namespace MphRead
{
    public static class SceneSetup
    {
        // todo: artifact flags
        public static (RoomEntity, RoomMetadata, CollisionInstance, IReadOnlyList<EntityBase>)
            LoadGame(string name, Scene scene, GameMode mode, int playerCount = 0,
            int nodeLayerMask = 0, int entityLayerId = -1)
        {
            (RoomMetadata? metadata, int roomId) = Metadata.GetRoomByName(name);
            if (metadata == null || !metadata.Multiplayer)
            {
                throw new ProgramException("No supported multiplayer room with this name is known.");
            }
            if (mode == GameMode.None)
            {
                mode = metadata.Name == "AD1 TRANSFER LOCK BT" ? GameMode.Bounty : GameMode.Battle;
            }
            if (mode < GameMode.Battle || mode > GameMode.PrimeHunter)
            {
                throw new ProgramException("Room runtime requires a supported multiplayer mode.");
            }
            Weapons.Current = Weapons.WeaponsMP;
            // Full admitted rules must exist before objective/player constructors run.
            if (scene.Match.Rules.Mode != mode.ToMatchMode()
                || !String.Equals(scene.Match.Rules.RoomKey, metadata.Name, StringComparison.OrdinalIgnoreCase))
            {
                scene.Match.ApplyRules(MatchRules.CreateDefault(mode.ToMatchMode(), metadata.Name));
            }
            scene.Match.Phase = MatchPhase.Playing;
            if (!scene.IsHeadless)
            {
                RuntimeData.Load();
            }
            else
            {
                Metadata.UseSilentSoundTables();
            }
            LoadResources(scene);
            CamSeqEntity.ClearData();
            CamSeqEntity.Current = null;
            CameraSequence.Current = null;
            CameraSequence.Intro = null;
            if (!scene.IsHeadless && PlayerEntity.PlayerCount > 0)
            {
                int seqId = roomId - 93 + 172;
                if (seqId >= 172 && seqId < 199)
                {
                    CameraSequence.Intro = CameraSequence.Load(seqId, scene);
                }
            }
            var room = new RoomEntity(scene);
            (CollisionInstance collision, IReadOnlyList<EntityBase> entities) = SetUpRoom(mode, playerCount,
                nodeLayerMask, entityLayerId, metadata, room, scene, isRoomTransition: false);
            AiPersonality.LoadAll(mode);
            room.SetNodeData(LoadNodeData(metadata.NodePath, room.RoomId, mode, entities, metadata.FirstHunt));
            return (room, metadata, collision, entities);
        }

        public static NodeData? LoadNodeData(string? nodePath, int roomId, GameMode mode,
            IReadOnlyList<EntityBase> entities, bool firstHunt)
        {
            NodeData? nodeData = null;
            if (mode == GameMode.Capture)
            {
                if (Metadata.CtfNodeDataOverrides.TryGetValue(roomId, out string? nodeOverride))
                {
                    nodePath = nodeOverride;
                }
            }
            else if ((mode == GameMode.Nodes || mode == GameMode.NodesTeams // MP14 OUTER REACH (Outer Reach)
                || mode == GameMode.Defender || mode == GameMode.DefenderTeams) && roomId == 107)
            {
                nodePath = @"levels\nodeData\mp14_KOTH_node.bin";
            }
            if (nodePath != null
                && !System.IO.File.Exists(Paths.Combine(firstHunt ? Paths.FhFileSystem : Paths.FileSystem, nodePath)))
            {
                // A room with no node data has bots that wander; a room that
                // throws while loading it has no match at all. A custom map
                // generates its own, so the case that happens is an install
                // where that generation has not run yet -- and taking the
                // whole match down for it is the wrong trade.
                Console.WriteLine($"[nodes] {nodePath} is missing; bots in this room will not navigate.");
                nodePath = null;
            }
            if (nodePath != null)
            {
                nodeData = ReadNodeData.ReadData(Paths.Combine(@"", nodePath), firstHunt);
                if (nodeData.Simple)
                {
                    for (int i = 0; i < entities.Count; i++)
                    {
                        EntityBase entity = entities[i];
                        if (entity.Type == EntityType.JumpPad)
                        {
                            var jumpPad = (JumpPadEntity)entity;
                            jumpPad.ClosestNode = ReadNodeData.FindClosestNode(nodeData, jumpPad.Position, useMaxDist: true);
                        }
                        else if (entity.Type == EntityType.OctolithFlag)
                        {
                            var octoFlag = (OctolithFlagEntity)entity;
                            octoFlag.ClosestNode = ReadNodeData.FindClosestNode(nodeData, octoFlag.Position);
                            octoFlag.BaseClosestNode = ReadNodeData.FindClosestNode(nodeData, octoFlag.BasePosition);
                        }
                        else if (entity.Type == EntityType.FlagBase)
                        {
                            var flagBase = (FlagBaseEntity)entity;
                            flagBase.ClosestNode = ReadNodeData.FindClosestNode(nodeData, flagBase.Position);
                        }
                        else if (entity.Type == EntityType.NodeDefense)
                        {
                            var nodeDefense = (NodeDefenseEntity)entity;
                            nodeDefense.ClosestNode = ReadNodeData.FindClosestNode(nodeData, nodeDefense.Position);
                        }
                    }
                }
            }
            return nodeData;
        }

        public static (CollisionInstance, IReadOnlyList<EntityBase>) SetUpRoom(GameMode mode,
            int playerCount, int nodeLayerMask, int entityLayerId,
            RoomMetadata metadata, RoomEntity room, Scene scene, bool isRoomTransition)
        {
            if (playerCount == 0)
            {
                playerCount = PlayerEntity.PlayerCount;
            }
            if (entityLayerId < 0 || entityLayerId > 15)
            {
                entityLayerId = Metadata.GetMultiplayerEntityLayer(mode, playerCount);

            }
            if (nodeLayerMask == 0)
            {
                int nodePlayerCount = Features.MaxRoomDetail ? 2 : playerCount;
                nodeLayerMask = GetNodeLayer(mode, metadata.NodeLayer, nodePlayerCount);
            }
            CollisionInstance collision = Collision.GetCollision(metadata, nodeLayerMask);
            if (isRoomTransition)
            {
                collision.Active = false;
            }
            room.Setup(metadata.Name, metadata, collision, nodeLayerMask, metadata.Id);
            IReadOnlyList<EntityBase> entities = LoadEntities(metadata, entityLayerId, scene);
            return (collision, entities);
        }

        public static int GetNodeLayer(GameMode mode, int roomLayer, int playerCount)
        {
            int nodeLayerMask = 0;
            if (mode < GameMode.Battle || mode > GameMode.PrimeHunter)
            {
                if (roomLayer > 0)
                {
                    nodeLayerMask = nodeLayerMask & 0xC03F | (((1 << roomLayer) & 0xFF) << 6);
                }
            }
            else
            {
                nodeLayerMask |= (int)NodeLayer.MultiplayerU;
                if (playerCount <= 2)
                {
                    nodeLayerMask |= (int)NodeLayer.MultiplayerLod0;
                }
                else
                {
                    nodeLayerMask |= (int)NodeLayer.MultiplayerLod1;
                }
                if (mode == GameMode.Capture)
                {
                    nodeLayerMask |= (int)NodeLayer.CaptureTheFlag;
                }
            }
            return nodeLayerMask;
        }

        private static IReadOnlyList<EntityBase> LoadEntities(RoomMetadata metadata, int layerId, Scene scene)
        {
            var results = new List<EntityBase>();
            if (metadata.EntityPath == null)
            {
                return results;
            }
            // only FirstHunt is passed here, not Hybrid -- model/anim/col should be loaded from FH, and ent/node from MPH
            IReadOnlyList<Entity> entities = Read.GetEntities(metadata.EntityPath, layerId, metadata.FirstHunt);
            foreach (Entity entity in entities)
            {
                string nodeName = entity.NodeName;
                if (entity.Type == EntityType.Platform)
                {
                    results.Add(new PlatformEntity(((Entity<PlatformEntityData>)entity).Data, nodeName, scene));
                }
                else if (entity.Type == EntityType.FhPlatform)
                {
                    results.Add(new FhPlatformEntity(((Entity<FhPlatformEntityData>)entity).Data, scene));
                }
                else if (entity.Type == EntityType.Object)
                {
                    results.Add(new ObjectEntity(((Entity<ObjectEntityData>)entity).Data, nodeName, scene));
                }
                else if (entity.Type == EntityType.PlayerSpawn || entity.Type == EntityType.FhPlayerSpawn)
                {
                    results.Add(new PlayerSpawnEntity(((Entity<PlayerSpawnEntityData>)entity).Data, nodeName, scene));
                }
                else if (entity.Type == EntityType.Door)
                {
                    results.Add(new DoorEntity(((Entity<DoorEntityData>)entity).Data, nodeName, scene));
                }
                else if (entity.Type == EntityType.FhDoor)
                {
                    results.Add(new FhDoorEntity(((Entity<FhDoorEntityData>)entity).Data, scene));
                }
                else if (entity.Type == EntityType.ItemSpawn)
                {
                    results.Add(new ItemSpawnEntity(((Entity<ItemSpawnEntityData>)entity).Data, nodeName, scene));
                }
                else if (entity.Type == EntityType.FhItemSpawn)
                {
                    results.Add(new FhItemSpawnEntity(((Entity<FhItemSpawnEntityData>)entity).Data, scene));
                }
                else if (entity.Type == EntityType.FhEnemySpawn)
                {
                    results.Add(new FhEnemySpawnEntity(((Entity<FhEnemySpawnEntityData>)entity).Data, scene));
                }
                else if (entity.Type == EntityType.TriggerVolume)
                {
                    results.Add(new TriggerVolumeEntity(((Entity<TriggerVolumeEntityData>)entity).Data, scene));
                }
                else if (entity.Type == EntityType.FhTriggerVolume)
                {
                    results.Add(new FhTriggerVolumeEntity(((Entity<FhTriggerVolumeEntityData>)entity).Data, scene));
                }
                else if (entity.Type == EntityType.AreaVolume)
                {
                    results.Add(new AreaVolumeEntity(((Entity<AreaVolumeEntityData>)entity).Data, nodeName, scene));
                }
                else if (entity.Type == EntityType.FhAreaVolume)
                {
                    results.Add(new FhAreaVolumeEntity(((Entity<FhAreaVolumeEntityData>)entity).Data, scene));
                }
                else if (entity.Type == EntityType.JumpPad)
                {
                    results.Add(new JumpPadEntity(((Entity<JumpPadEntityData>)entity).Data, nodeName, scene));
                }
                else if (entity.Type == EntityType.FhJumpPad)
                {
                    results.Add(new FhJumpPadEntity(((Entity<FhJumpPadEntityData>)entity).Data, scene));
                }
                else if (entity.Type == EntityType.PointModule || entity.Type == EntityType.FhPointModule)
                {
                    results.Add(new PointModuleEntity(((Entity<PointModuleEntityData>)entity).Data, scene));
                }
                else if (entity.Type == EntityType.MorphCamera)
                {
                    results.Add(new MorphCameraEntity(((Entity<MorphCameraEntityData>)entity).Data, nodeName, scene));
                }
                else if (entity.Type == EntityType.FhMorphCamera)
                {
                    results.Add(new FhMorphCameraEntity(((Entity<FhMorphCameraEntityData>)entity).Data, scene));
                }
                else if (entity.Type == EntityType.OctolithFlag)
                {
                    results.Add(new OctolithFlagEntity(((Entity<OctolithFlagEntityData>)entity).Data, scene));
                }
                else if (entity.Type == EntityType.FlagBase)
                {
                    results.Add(new FlagBaseEntity(((Entity<FlagBaseEntityData>)entity).Data, scene));
                }
                else if (entity.Type == EntityType.Teleporter)
                {
                    results.Add(new TeleporterEntity(((Entity<TeleporterEntityData>)entity).Data, nodeName, scene));
                }
                else if (entity.Type == EntityType.NodeDefense)
                {
                    results.Add(new NodeDefenseEntity(((Entity<NodeDefenseEntityData>)entity).Data, scene));
                }
                else if (entity.Type == EntityType.LightSource)
                {
                    results.Add(new LightSourceEntity(((Entity<LightSourceEntityData>)entity).Data, scene));
                }
                else if (entity.Type == EntityType.CameraSequence)
                {
                    results.Add(new CamSeqEntity(((Entity<CameraSequenceEntityData>)entity).Data, scene));
                }
                else if (entity.Type == EntityType.ForceField)
                {
                    results.Add(new ForceFieldEntity(((Entity<ForceFieldEntityData>)entity).Data, nodeName, scene));
                }
                else
                {
                    throw new ProgramException($"Invalid entity type {entity.Type}");
                }
            }
            return results;
        }

        private static void LoadResources(Scene? scene)
        {
            // todo: this could also allocate effect lists and stuff, since we don't need those if there's no room
            // todo: sort this all out by game mode/etc. for what's actually needed
            // todo: add an assert if any loading occurs after room init (besides manual model/entity loading)
            if (scene != null)
            {
                LoadBombResources(scene);
                LoadBeamEffectResources(scene);
                LoadBeamProjectileResources(scene);
                LoadRoomResources(scene);
                LoadHunterResources(Hunter.Samus, scene);
                // A preview creates one Samus so the multiplayer intro camera
                // has a main player to run against, and draws no hunter at
                // all. The other seven are 832 ms of a 2970 ms room load.
                if (scene.Presentation?.MinimalResources != true)
                {
                    LoadHunterResources(Hunter.Kanden, scene);
                    LoadHunterResources(Hunter.Trace, scene);
                    LoadHunterResources(Hunter.Sylux, scene);
                    LoadHunterResources(Hunter.Noxus, scene);
                    LoadHunterResources(Hunter.Spire, scene);
                    LoadHunterResources(Hunter.Weavel, scene);
                    LoadHunterResources(Hunter.Guardian, scene);
                }
                LoadCommonHunterResources(scene);
            }
        }

        public static void LoadCommonHunterResources(Scene scene)
        {
            PlayerEntity.GeneratePlayerVolumes();
            if (scene.IsHeadless)
            {
                return;
            }
            scene.LoadModel("doubleDamage_img");
            scene.LoadModel("alt_ice");
            scene.LoadModel("gunSmoke");
            scene.LoadModel("trail");
            scene.LoadModel("octolith_simple");
            scene.LoadModel("Octolith");
            // todo?: same as above
            scene.LoadEffect(10, persistent: true); // ballDeath
            scene.LoadEffect(216, persistent: true); // deathAlt
            scene.LoadEffect(187, persistent: true); // flamingAltForm
            scene.LoadEffect(188, persistent: true); // flamingGun
            scene.LoadEffect(189, persistent: true); // flamingHunter
            for (int i = 0; i < 9; i++)
            {
                scene.LoadEffect(Metadata.MuzzleEffectIds[i], persistent: true);
                scene.LoadEffect(Metadata.ChargeEffectIds[i], persistent: true);
                scene.LoadEffect(Metadata.ChargeLoopEffectIds[i], persistent: true);
            }
            PlayerEntity.LoadWeaponNames();
            Strings.ReadStringTable(StringTables.HudMsgsCommon);
            Strings.ReadStringTable(StringTables.HudMessagesMP);
        }

        public static void LoadHunterResources(Hunter hunter, Scene scene)
        {
            if (!scene.IsHeadless)
            {
                scene.LoadModel(hunter == Hunter.Noxus || hunter == Hunter.Trace ? "nox_ice" : "samus_ice");
            }
            foreach (string modelName in Metadata.HunterModels[hunter])
            {
                scene.LoadModel(modelName);
            }
            if (hunter == Hunter.Samus)
            {
                scene.LoadEffect(30, persistent: true); // samusFurl
                scene.LoadEffect(136, persistent: true); // samusDash
            }
            else if (hunter == Hunter.Kanden)
            {
                PlayerEntity.GenerateKandenAltNodeDistances();
            }
            else if (hunter == Hunter.Spire)
            {
                scene.LoadEffect(37, persistent: true); // spireAltSlam
            }
            else if (hunter == Hunter.Noxus)
            {
                scene.LoadEffect(235, persistent: true); // noxHit
            }
        }

        private static void LoadBombResources(Scene scene)
        {
            // todo?: not all of these need to be loaded depending on the hunters/mode
            scene.LoadModel("KandenAlt_TailBomb");
            if (!scene.IsHeadless)
            {
                scene.LoadModel("arcWelder");
                scene.LoadModel("arcWelder1");
            }
            scene.LoadEffect(9, persistent: true); // bombStart
            scene.LoadEffect(113, persistent: true); // bombStartSylux
            scene.LoadEffect(119, persistent: true); // bombStartMP
            scene.LoadEffect(128, persistent: true); // bombKanden
            scene.LoadEffect(129, persistent: true); // collapsingStreaks
            scene.LoadEffect(145, persistent: true); // bombBlue
            scene.LoadEffect(146, persistent: true); // bombSylux
            scene.LoadEffect(149, persistent: true); // bombStartSyluxG
            scene.LoadEffect(150, persistent: true); // bombStartSyluxO
            scene.LoadEffect(151, persistent: true); // bombStartSyluxP
            scene.LoadEffect(152, persistent: true); // bombStartSyluxR
            scene.LoadEffect(153, persistent: true); // bombStartSyluxW
        }

        private static void LoadBeamEffectResources(Scene scene)
        {
            if (scene.IsHeadless)
            {
                return;
            }
            scene.LoadModel("iceWave");
            scene.LoadModel("sniperBeam");
            scene.LoadModel("cylBossLaserBurn");
        }

        private static void LoadBeamProjectileResources(Scene scene)
        {
            scene.LoadModel("iceShard");
            scene.LoadModel("energyBeam");
            if (!scene.IsHeadless)
            {
                scene.LoadModel("trail");
                scene.LoadModel("electroTrail");
                scene.LoadModel("arcWelder");
            }
            scene.LoadEffect(57, persistent: true); // muzzleElc
            scene.LoadEffect(58, persistent: true); // muzzleGst
            scene.LoadEffect(59, persistent: true); // muzzleIce
            scene.LoadEffect(60, persistent: true); // muzzleJak
            scene.LoadEffect(61, persistent: true); // muzzleMrt
            scene.LoadEffect(62, persistent: true); // muzzlePB
            scene.LoadEffect(63, persistent: true); // muzzleSnp
            scene.LoadEffect(78, persistent: true); // iceWave
            scene.LoadEffect(85, persistent: true); // electroCharge
            scene.LoadEffect(86, persistent: true); // electroHit
            scene.LoadEffect(92, persistent: true); // powerBeamCharge
            scene.LoadEffect(98, persistent: true); // powerBeamChargeNoSplat
            scene.LoadEffect(99, persistent: true); // powerBeamHolo
            scene.LoadEffect(100, persistent: true); // powerBeamLava
            scene.LoadEffect(121, persistent: true); // powerBeamHoloBG
            scene.LoadEffect(122, persistent: true); // powerBeamHoloB
            scene.LoadEffect(123, persistent: true); // powerBeamIce
            scene.LoadEffect(124, persistent: true); // powerBeamRock
            scene.LoadEffect(125, persistent: true); // powerBeamSand
            scene.LoadEffect(126, persistent: true); // powerBeamSnow
            scene.LoadEffect(130, persistent: true); // fireProjectile
            scene.LoadEffect(134, persistent: true); // hammerProjectile
            scene.LoadEffect(137, persistent: true); // electroProjectile
            scene.LoadEffect(140, persistent: true); // energyRippleB
            scene.LoadEffect(141, persistent: true); // energyRippleBG
            scene.LoadEffect(142, persistent: true); // energyRippleO
            scene.LoadEffect(171, persistent: true); // electroChargeNA
            scene.LoadEffect(211, persistent: true); // mortarProjectile
            scene.LoadEffect(237, persistent: true); // electroProjectileUncharged
            scene.LoadEffect(238, persistent: true); // enemyProjectile1
            scene.LoadEffect(246, persistent: true); // enemyMortarProjectile
        }

        private static void LoadRoomResources(Scene scene)
        {
            scene.LoadEffect(1, persistent: true); // powerBeam
            scene.LoadEffect(2, persistent: true); // powerBeamNoSplat
            scene.LoadEffect(5, persistent: true); // missile1
            scene.LoadEffect(6, persistent: true); // mortar1
            scene.LoadEffect(7, persistent: true); // shotGunCol
            scene.LoadEffect(8, persistent: true); // shotGunShrapnel
            scene.LoadEffect(11, persistent: true); // jackHammerCol
            scene.LoadEffect(12, persistent: true); // effectiveHitPB
            scene.LoadEffect(13, persistent: true); // effectiveHitElectric
            scene.LoadEffect(14, persistent: true); // effectiveHitMsl
            scene.LoadEffect(15, persistent: true); // effectiveHitJack
            scene.LoadEffect(16, persistent: true); // effectiveHitSniper
            scene.LoadEffect(17, persistent: true); // effectiveHitIce
            scene.LoadEffect(18, persistent: true); // effectiveHitMortar
            scene.LoadEffect(19, persistent: true); // effectiveHitGhost
            scene.LoadEffect(20, persistent: true); // sprEffectivePB
            scene.LoadEffect(21, persistent: true); // sprEffectiveElectric
            scene.LoadEffect(22, persistent: true); // sprEffectiveMsl
            scene.LoadEffect(23, persistent: true); // sprEffectiveJack
            scene.LoadEffect(24, persistent: true); // sprEffectiveSniper
            scene.LoadEffect(25, persistent: true); // sprEffectiveIce
            scene.LoadEffect(26, persistent: true); // sprEffectiveMortar
            scene.LoadEffect(27, persistent: true); // sprEffectiveGhost
            scene.LoadEffect(28, persistent: true); // sniperCol
            scene.LoadEffect(31, persistent: true); // spawnEffect
            scene.LoadEffect(33, persistent: true); // spawnEffectMP
            scene.LoadEffect(99, persistent: true); // powerBeamHolo
            scene.LoadEffect(115, persistent: true); // ineffectivePsycho
            scene.LoadEffect(154, persistent: true); // mpEffectivePB
            scene.LoadEffect(155, persistent: true); // mpEffectiveElectric
            scene.LoadEffect(156, persistent: true); // mpEffectiveMsl
            scene.LoadEffect(157, persistent: true); // mpEffectiveJack
            scene.LoadEffect(158, persistent: true); // mpEffectiveSniper
            scene.LoadEffect(159, persistent: true); // mpEffectiveIce
            scene.LoadEffect(160, persistent: true); // mpEffectiveMortar
            scene.LoadEffect(161, persistent: true); // mpEffectiveGhost
            scene.LoadEffect(173, persistent: true); // jackHammerColNA
            scene.LoadEffect(190, persistent: true); // missileCharged
            scene.LoadEffect(191, persistent: true); // mortarCharged
            scene.LoadEffect(192, persistent: true); // mortarChargedAffinity
            scene.LoadEffect(231, persistent: true); // iceShatter
            scene.LoadEffect(239, persistent: true); // enemyCol1
            if (!scene.IsHeadless)
            {
                scene.LoadModel(Read.GetSingleParticle(SingleType.Death).Model);
                scene.LoadModel(Read.GetSingleParticle(SingleType.Fuzzball).Model);
            }
            // skdebug - the game only loads these if the Omega Cannon item is in the room
            scene.LoadEffect(209, persistent: true); // ultimateProjectile
            scene.LoadEffect(245, persistent: true); // ultimateCol
        }

        public static void LoadEntityResources(EntityBase entity, Scene scene)
        {
            if (entity is ObjectEntity obj)
            {
                LoadObjectResources(obj, scene);
            }
            else if (entity is PlatformEntity plat)
            {
                LoadPlatformResources(plat, scene);
            }
            else if (entity is ItemSpawnEntity itemSpawner)
            {
                LoadItemResources(itemSpawner, scene);
            }
        }

        public static void LoadObjectResources(Scene scene)
        {
            foreach (ObjectEntity obj in scene.GetObjectEntities())
            {
                LoadObjectResources(obj, scene);
            }
        }

        public static void LoadObjectResources(ObjectEntity obj, Scene scene)
        {
            if (obj.Data.EffectId != 0)
            {
                scene.LoadEffect(obj.Data.EffectId, persistent: false);
            }
        }

        public static void LoadPlatformResources(Scene scene)
        {
            foreach (PlatformEntity platform in scene.GetPlatformEntities())
            {
                LoadPlatformResources(platform, scene);
            }
        }

        public static void LoadPlatformResources(PlatformEntity platform, Scene scene)
        {
            if (platform.Data.ResistEffectId != 0)
            {
                scene.LoadEffect(platform.Data.ResistEffectId, persistent: false);
            }
            if (platform.Data.DamageEffectId != 0)
            {
                scene.LoadEffect(platform.Data.DamageEffectId, persistent: false);
            }
            if (platform.Data.DeadEffectId != 0)
            {
                scene.LoadEffect(platform.Data.DeadEffectId, persistent: false);
            }
            if (platform.Data.Flags.TestFlag(PlatformFlags.BeamSpawner) && platform.Data.BeamId == 0)
            {
                scene.LoadEffect(183, persistent: false); // syluxMissile
                scene.LoadEffect(184, persistent: false); // syluxMissileCol
                scene.LoadEffect(185, persistent: false); // syluxMissileFlash
            }
            if (platform.Data.ItemChance > 0)
            {
                LoadItem(platform.Data.ItemType, scene);
            }
        }

        public static void LoadItemResources(Scene scene)
        {
            // todo: pre-allocate
            LoadItem(ItemType.UASmall, scene);
            LoadItem(ItemType.UABig, scene);
            LoadItem(ItemType.MissileSmall, scene);
            LoadItem(ItemType.MissileBig, scene);

            foreach (ItemSpawnEntity itemSpawner in scene.GetItemSpawnEntities())
            {
                LoadItemResources(itemSpawner, scene);
            }
        }

        public static void LoadItemResources(ItemSpawnEntity itemSpawner, Scene scene)
        {
            LoadItem(itemSpawner.Data.ItemType, scene);
            if (itemSpawner.Data.HasBase != 0)
            {
                scene.LoadModel("items_base");
            }
        }

        private static void LoadItem(ItemType item, Scene scene)
        {
            if (item == ItemType.None)
            {
                return;
            }
            // todo: affinity weapon replacement
            int index = (int)item;
            Debug.Assert(index < Metadata.Items.Count);
            scene.LoadModel(Metadata.Items[index]);
            if (item == ItemType.ArtifactKey)
            {
                scene.LoadEffect(144, persistent: false); // artifactKeyEffect
            }
            else if (item == ItemType.Deathalt)
            {
                scene.LoadEffect(181, persistent: true); // deathBall
            }
            else if (item == ItemType.OmegaCannon)
            {
                scene.LoadEffect(209, persistent: true); // ultimateProjectile
                scene.LoadEffect(245, persistent: true); // ultimateCol
            }
            else if (item == ItemType.DoubleDamage)
            {
                scene.LoadEffect(244, persistent: true); //  doubleDamageGun
            }
        }

        public static BeamProjectileEntity[] CreateBeamList(int size, Scene scene)
        {
            Debug.Assert(size > 0);
            var beams = new BeamProjectileEntity[size];
            for (int i = 0; i < size; i++)
            {
                beams[i] = new BeamProjectileEntity(scene);
            }
            return beams;
        }

    }
}
