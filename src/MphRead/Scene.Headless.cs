using System;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead
{
    public partial class Scene
    {
        public bool IsHeadless { get; }
        private bool _headlessInitialized;

        /// <summary>
        /// The existing game owns process-global player, collision and mode
        /// state. Run exactly one scene on one owner thread per server process.
        /// CPU models remain required for animation, collision attachments and
        /// weapon transforms; texture upload, effects, audio and HUD do not.
        /// </summary>
        public static Scene CreateHeadless()
        {
            Read.ServerMode = true;
            try
            {
                return new Scene(Vector2i.Zero, null!, null!, _ => { }, () => { }, headless: true);
            }
            catch
            {
                Read.ServerMode = false;
                throw;
            }
        }

        public void LoadServerRoom(string room, GameMode mode, int players, bool bots = false,
            int? roomPlayerCount = null)
        {
            if (!IsHeadless || _headlessInitialized || players < 0 || players > PlayerEntity.SlotCapacity
                || mode < GameMode.Battle || mode > GameMode.PrimeHunter)
            {
                throw new InvalidOperationException("Invalid headless room setup.");
            }
            Mods.Network.ServerContent.RequireRoom(room, mode);
            AllocateBombs();
            PlayerEntity.MaxPlayers = PlayerEntity.SlotCapacity;
            for (int slot = 0; slot < players; slot++)
            {
                AddPlayer((Hunter)(slot % 8), team: GameState.IsTeamMode(mode) ? slot % 2 : -1);
                PlayerEntity.Players[slot].IsBot = bots;
            }
            AddRoom(room, mode, playerCount: Mods.Network.ServerContent.ResolveRoomPlayerCount(
                roomPlayerCount, Math.Max(2, players)));
            CollisionDetection.Init();
            foreach (EntityBase entity in Entities)
            {
                if (!entity.Initialized)
                {
                    entity.Initialize();
                    entity.Initialized = true;
                }
            }
            foreach (PlayerEntity player in PlayerEntity.Players)
            {
                if (player.LoadFlags.TestFlag(LoadFlags.SlotActive))
                {
                    player.Initialize();
                }
            }
            _headlessInitialized = true;
        }

        /// <summary>One 60 Hz simulation step, with no rendering or device input.</summary>
        public void StepHeadlessFrame(bool advanceMatch = true)
        {
            if (!IsHeadless || !_headlessInitialized)
            {
                throw new InvalidOperationException("Load the headless room before stepping it.");
            }
            _frameTime = 1 / 60f;
            _globalElapsedTime += _frameTime;
            if (GameState.MatchState == MatchState.InProgress)
            {
                _elapsedTime += _frameTime;
                foreach (PlayerEntity player in GetPlayerEntities())
                {
                    if (player.IsBot && player.LoadFlags.TestFlag(LoadFlags.Active))
                    {
                        player.AiData.ProcessInput();
                    }
                }
            }
            if (advanceMatch)
            {
                GameState.ProcessFrame(this);
            }
            if (GameState.MatchState == MatchState.InProgress)
            {
                UpdateScene();
                ProcessMessageQueue();
                _liveFrames++;
            }
            _frameCount++;
            if (advanceMatch)
            {
                GameState.UpdateTime(this);
            }
        }

        public void CloseHeadless()
        {
            if (!IsHeadless)
            {
                throw new InvalidOperationException("This scene has a renderer.");
            }
            foreach (EntityBase entity in Entities)
            {
                entity.Destroy();
            }
            _entities.Clear();
            _entityMap.Clear();
            _entityNodesByType.Clear();
            Sound.Sfx.ShutDown();
            _headlessInitialized = false;
            Read.ServerMode = false;
        }
    }
}
