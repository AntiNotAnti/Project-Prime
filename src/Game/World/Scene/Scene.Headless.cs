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
                return new Scene(headless: true);
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
            PlayerEntity.MaxPlayers = PlayerEntity.SlotCapacity;
            for (int slot = 0; slot < players; slot++)
            {
                AddPlayer((Hunter)(slot % 8), team: mode.IsTeamMode() ? slot % 2 : -1);
                PlayerEntity.Players[slot].IsBot = bots;
            }
            AddRoom(room, mode, playerCount: roomPlayerCount ?? Math.Max(2, players));
            InitializeWorld();
            _headlessInitialized = true;
        }

        /// <summary>One 60 Hz simulation step, with no rendering or device input.</summary>
        public void StepHeadlessFrame(bool advanceMatch = true)
        {
            if (!IsHeadless || !_headlessInitialized)
            {
                throw new InvalidOperationException("Load the headless room before stepping it.");
            }
            // Waiting/countdown worlds stay exactly at their loaded state. A
            // lifecycle-owned result/intermission also has no competitive ticks.
            if (Match.Phase is MatchPhase.WaitingForPlayers or MatchPhase.Countdown
                || Match.UsesServerLifecycle && Match.Phase != MatchPhase.Playing)
            {
                return;
            }
            _frameTime = 1 / 60f;
            _globalElapsedTime += _frameTime;
            if (Match.LegacyState == MatchState.InProgress)
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
                Match.Flow.ProcessFrame();
            }
            if (Match.LegacyState == MatchState.InProgress)
            {
                UpdateScene();
                ProcessMessageQueue();
                _liveFrames++;
            }
            _frameCount++;
            if (advanceMatch)
            {
                Match.Flow.UpdateTime();
            }
        }

        public void CloseHeadless()
        {
            if (!IsHeadless)
            {
                throw new InvalidOperationException("This scene has a renderer.");
            }
            try
            {
                CloseWorld();
            }
            finally
            {
                _headlessInitialized = false;
                Read.ServerMode = false;
            }
        }
    }
}
