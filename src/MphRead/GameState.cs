using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Sound;
using MphRead.Text;

namespace MphRead
{
    public enum TransitionState
    {
        None = 0,
        Start = 1,
        Process = 2,
        End = 3
    }

    public enum EscapeState
    {
        None = 0,
        Event = 1,
        Escape = 2
    }

    public static class GameState
    {
        // Legacy content/campaign selector; multiplayer runtime uses scene.Match.Rules.Mode.
        public static GameMode Mode { get; set; } = GameMode.SinglePlayer;
        public static bool SinglePlayer => Mode == GameMode.SinglePlayer;
        public static bool Multiplayer => Mode != GameMode.SinglePlayer;
        public static bool PausePrevented { get; set; }
        public static bool MenuPause { get; private set; }
        public static bool DialogPause { get; private set; }
        public static TransitionState TransitionState { get; set; } = TransitionState.None;
        public static bool InRoomTransition => TransitionState != TransitionState.None;
        public static EscapeState EscapeState { get; set; } = EscapeState.None;
        public static float EscapeTimer { get; set; } = -1;
        public static bool EscapePaused { get; set; }
        private static string[] BuildDefaultNicknames()
        {
            string[] names = new string[PlayerEntity.SlotCapacity];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = $"Player{i + 1}";
            }
            return names;
        }

        public static int[] EncounterState { get; } = new int[PlayerEntity.SlotCapacity];
        public static bool[] CompletedRandomEncounterRooms { get; } = new bool[66]; // only for the no repeat encounters feature
        public static int TransitionRoomId { get; set; } = -1;
        public static bool TransitionAltForm { get; set; }
        public static string[] Nicknames { get; } = BuildDefaultNicknames();
        private static bool _pausingDialog = false;
        private static bool _unpausingDialog = false;

        public static void PauseMenu()
        {
            MenuPause = true;
            Sfx.Instance.StopAllSound();
            Sfx.TimedSfxMute++;
        }

        public static void UnpauseMenu()
        {
            MenuPause = false;
            Sfx.TimedSfxMute--;
        }

        public static void PauseDialog()
        {
            _pausingDialog = true;
        }

        public static void UnpauseDialog()
        {
            _unpausingDialog = true;
        }

        public static void ApplyPause()
        {
            if (CameraSequence.Current?.Flags.TestFlag(CamSeqFlags.BlockInput) == true)
            {
                return;
            }
            if (_pausingDialog)
            {
                DialogPause = true;
            }
            if (_unpausingDialog)
            {
                DialogPause = false;
            }
            _pausingDialog = false;
            _unpausingDialog = false;
        }

        // Campaign-only frame behavior remains until R6 removes adventure runtime.
        public static void ProcessFrame(Scene scene)
        {
            if (!SinglePlayer) { return; }
            if (SinglePlayer && !PausePrevented && !scene.MoviePlaying)
            {
                if (MenuPause && PlayerEntity.Main.Controls.Pause.IsPressed)
                {
                    Sfx.Instance.PlayFreeSfx(SfxId.MENU_CANCEL);
                    UnpauseMenu();
                    PlayerEntity.Main.EndMenuPauseHud();
                    PlayerEntity.Main.Controls.Pause.IsPressed = false;
                    return;
                }
                if (!MenuPause && CameraSequence.Current?.BlockInput != true && PlayerEntity.Main.Controls.Pause.IsPressed)
                {
                    PlayerEntity.Main.Controls.Pause.IsPressed = false;
                    PauseMenu();
                    PlayerEntity.Main.SetUpMenuPauseHud();
                    Sfx.Instance.PlayFreeSfx(SfxId.MENU_CONFIRM);
                }
                if (MenuPause)
                {
                    PlayerEntity.Main.ProcessPauseMenu();
                    return;
                }
            }
            ModeStateAdventure(scene);
            if (SinglePlayer && EscapeTimer != -1)
            {
                // bugfix?: this fade check seems to count things like the Omega Cannon flash
                if (!EscapePaused && !MenuPause && !DialogPause && scene.FadeType == FadeType.None
                    && CameraSequence.Current?.Flags.TestFlag(CamSeqFlags.BlockInput) != true)
                {
                    EscapeTimer -= scene.FrameTime;
                    if (EscapeState == EscapeState.Escape)
                    {
                        Music.UpdateEscapeMusic();
                    }
                    else
                    {
                        UpdateEventSounds(EscapeTimer);
                    }
                }
                if (EscapeTimer <= 0)
                {
                    if (EscapeState == EscapeState.Escape && PlayerEntity.Main.Health > 0)
                    {
                        scene.SendMessage(Message.Death, null!, PlayerEntity.Main, 0, 0);
                    }
                    EscapeTimer = -1;
                }
            }
        }


        public static AreaState GetAreaState(int areaId, StorySave? save = null)
        {
            if (save == null)
            {
                save = StorySave;
            }
            if (save == null)
            {
                return AreaState.None;
            }
            return (AreaState)(((int)save.BossFlags >> (2 * areaId)) & 3);
        }

        public static bool QueuedOublietteUnlockMessage { get; set; }

        public static void ModeStateAdventure(Scene scene)
        {
            PlayerEntity.Main.SaveStatus();
            if ((StorySave.Areas & 0x100) == 0)
            {
                if (QueuedOublietteUnlockMessage && scene.FadeType == FadeType.FadeInBlack && !scene.MoviePlaying)
                {
                    StorySave.Areas |= 0x100;
                    StorySave.CurrentOctoliths = 0;
                    // GUNSHIP TRANSMISSION severe timefield disruption detected in the vicinity of the ALIMBIC CLUSTER.
                    PlayerEntity.Main.ShowDialog(DialogType.Okay, messageId: 43);
                    QueuedOublietteUnlockMessage = false;
                }
                for (int i = 0; i < scene.MessageQueue.Count; i++)
                {
                    MessageInfo message = scene.MessageQueue[i];
                    if (message.Message == Message.UnlockOubliette && message.ExecuteFrame == scene.FrameCount)
                    {
                        if (StorySave.CurrentOctoliths == 0xFF)
                        {
                            GameState.PausePrevented = true;
                            scene.StartMovie(Movie.OublietteUnlock, FadeType.FadeOutInWhite, 20 / 30f, FadeType.FadeOutInBlack, 5 / 30f);
                            GameState.QueuedOublietteUnlockMessage = true;
                        }
                        else
                        {
                            foreach (CamSeqEntity entity in scene.GetCamSeqEntities())
                            {
                                scene.SendMessage(Message.Activate, null!, entity, param1: 0, param2: 0);
                                break;
                            }
                        }
                        break;
                    }
                }
            }
            if (PlayerEntity.Main.Health > 0)
            {
                for (int i = 0; i < scene.MessageQueue.Count; i++)
                {
                    MessageInfo message = scene.MessageQueue[i];
                    if (message.Message == Message.Checkpoint && message.ExecuteFrame == scene.FrameCount)
                    {
                        Debug.Assert(scene.Room != null);
                        scene.SendMessage(Message.SetActive, null!, message.Sender, param1: 0, param2: 0);
                        StorySave.CheckpointEntityId = message.Sender.Id;
                        StorySave.CheckpointRoomId = scene.Room.RoomId;
                        UpdateCleanSave(force: false);
                        break;
                    }
                }
                for (int i = 0; i < scene.MessageQueue.Count; i++)
                {
                    MessageInfo message = scene.MessageQueue[i];
                    if (message.Message == Message.LoadOubliette && message.ExecuteFrame == scene.FrameCount)
                    {
                        TransitionRoomId = 91; // Gorea_b1
                        scene.StartMovie(Movie.Gorea1Intro, FadeType.FadeOutInWhite, 10 / 30f, FadeType.FadeOutInWhite, 10 / 30f);
                        break;
                    }
                }
            }
            // todo: game timer/boss record stuff
        }

        private static bool _whiteoutStarted = false;
        private static bool _gameOverShown = false;
        public static int QueuedOctolithMessageId { get; set; } = -1;

        public static void UpdateFrame(Scene scene)
        {
            PromptType prompt = PlayerEntity.Main.DialogPromptType;
            ConfirmState confirm = PlayerEntity.Main.DialogConfirmState;

            void Quit()
            {
                scene.SetFade(FadeType.FadeOutBlack, length: 10 / 30f, overwrite: true, AfterFade.Exit);
                Music.Stop(fadeTime: 10 / 30f);
                Sfx.Instance.PlaySample((int)SfxId.QUIT_GAME, source: null, loop: false,
                    noUpdate: false, recency: -1, sourceOnly: false, cancellable: false);
            }

            if (prompt != PromptType.Any && confirm != ConfirmState.Okay)
            {
                Sfx.Instance.StopFreeSfxScripts();
                if (confirm == ConfirmState.Yes)
                {
                    if (prompt == PromptType.ShipHatch)
                    {
                        // yes to ship hatch (enter)
                        EnterShip(); // the game does this in the cockpit
                        Debug.Assert(scene.Room != null);
                        Movie movieId = scene.Room.RoomId switch
                        {
                            27 => Movie.AlinosTakeoff,
                            45 => Movie.CATakeoff,
                            65 => Movie.VDOTakeoff,
                            77 => Movie.ArcterraTakeoff,
                            _ => Movie.None
                        };
                        if (movieId != Movie.None && !Cheats.SkipPlanetIntros)
                        {
                            scene.StartMovie(movieId, FadeType.FadeOutInWhite, 20 / 30f,
                                FadeType.FadeOutBlack, 5 / 30f, afterMovieAction: AfterMovie.EndGame);
                        }
                        else
                        {
                            scene.SetFade(FadeType.FadeOutWhite, length: 20 / 30f, overwrite: true, AfterFade.EnterShip);
                        }
                        Music.Stop(fadeTime: 20 / 30f);
                        // todo: fade SFX
                        GameState.PausePrevented = true;
                        Sfx.Instance.PlaySample((int)SfxId.RETURN_TO_SHIP_YES, source: null, loop: false,
                            noUpdate: false, recency: -1, sourceOnly: false, cancellable: false);
                    }
                    else if (prompt == PromptType.GameOver)
                    {
                        // yes to game over (continue)
                        RestoreCleanSave();
                        Debug.Assert(scene.Room != null);
                        if (Cheats.ContinueFromCurrentRoom)
                        {
                            if (StorySave.CheckpointRoomId != scene.Room.RoomId)
                            {
                                StorySave.CheckpointEntityId = -1;
                            }
                            StorySave.CheckpointRoomId = scene.Room.RoomId;
                        }
                        else if (StorySave.CheckpointRoomId == -1)
                        {
                            StorySave.CheckpointEntityId = -1;
                            if (scene.AreaId == 0 || scene.AreaId == 1) // Alinos 1/2
                            {
                                StorySave.CheckpointRoomId = 27; // UNIT1_LAND
                            }
                            else if (scene.AreaId == 2 || scene.AreaId == 3) // CA 1/2
                            {
                                StorySave.CheckpointRoomId = 45; // UNIT2_LAND
                            }
                            else if (scene.AreaId == 4 || scene.AreaId == 5) // VDO 1/2
                            {
                                StorySave.CheckpointRoomId = 65; // UNIT3_LAND
                            }
                            else if (scene.AreaId == 6 || scene.AreaId == 7) // Arcterra 1/2
                            {
                                StorySave.CheckpointRoomId = 77; // UNIT4_LAND
                            }
                            else if (scene.AreaId == 8) // Oubliette
                            {
                                StorySave.CheckpointRoomId = 89; // Gorea_Land
                            }
                        }
                        if (StorySave.CheckpointRoomId == -1)
                        {
                            Quit();
                        }
                        // if CheckpointEntityId isn't set, we'll still spawn, just using the respawn point code path
                        TransitionRoomId = StorySave.CheckpointRoomId;
                        Sfx.Instance.PlaySample((int)SfxId.MENU_CONFIRM, source: null, loop: false,
                            noUpdate: false, recency: -1, sourceOnly: false, cancellable: false);
                        GameState.PausePrevented = true;
                        scene.SetFade(FadeType.FadeOutWhite, length: 10 / 30f, overwrite: true, AfterFade.LoadRoom);
                        UnpauseDialog();
                        PlayerEntity.Main.RestartLongSfx(force: true);
                    }
                }
                else if (prompt == PromptType.GameOver)
                {
                    // no to game over (quit)
                    Quit();
                }
                else
                {
                    // no to ship hatch (resume)
                    PlayerEntity.Main.RestartLongSfx();
                    Sfx.Instance.PlaySample((int)SfxId.RETURN_TO_SHIP_NO, source: null, loop: false,
                        noUpdate: false, recency: -1, sourceOnly: false, cancellable: false);
                    UnpauseDialog();
                }
                PlayerEntity.Main.DialogPromptType = PromptType.Any;
                PlayerEntity.Main.DialogConfirmState = ConfirmState.Okay;
            }
            if (!DialogPause)
            {
                if (PlayerEntity.Main.Health > 0)
                {
                    for (int i = 0; i < scene.MessageQueue.Count; i++)
                    {
                        MessageInfo message = scene.MessageQueue[i];
                        if (message.Message == Message.ShipHatch && message.ExecuteFrame == scene.FrameCount)
                        {
                            Debug.Assert(scene.Room != null);
                            ResetEscapeState(updateSounds: false); // skdebug
                            PlayerEntity.Main.DialogPromptType = PromptType.ShipHatch;
                            StorySave.CheckpointEntityId = message.Sender.Id;
                            StorySave.CheckpointRoomId = scene.Room.RoomId;
                            UpdateCleanSave(force: true);
                            // HUNTER GUNSHIP enter your ship?
                            PlayerEntity.Main.ShowDialog(DialogType.YesNo, messageId: 1);
                            Sfx.Instance.StopFreeSfxScripts();
                            Sfx.Instance.PlayScript((int)SfxId.RETURN_TO_SHIP_SCR, source: null,
                                noUpdate: false, recency: -1, sourceOnly: false, cancellable: false);
                            break;
                        }
                    }
                    for (int i = 0; i < scene.MessageQueue.Count; i++)
                    {
                        MessageInfo message = scene.MessageQueue[i];
                        if (message.Message == Message.EscapeUpdate1 && message.ExecuteFrame == scene.FrameCount)
                        {
                            UpdateEscapeState((int)message.Param1 * 30, (int)message.Param2);
                        }
                    }
                    for (int i = 0; i < scene.MessageQueue.Count; i++)
                    {
                        MessageInfo message = scene.MessageQueue[i];
                        if (message.Message == Message.EscapeUpdate2 && message.ExecuteFrame == scene.FrameCount)
                        {
                            UpdateEscapeState((int)message.Param1, (int)message.Param2);
                        }
                    }
                    for (int i = 0; i < scene.MessageQueue.Count; i++)
                    {
                        MessageInfo message = scene.MessageQueue[i];
                        if (message.Message == Message.ShowPrompt && message.ExecuteFrame == scene.FrameCount)
                        {
                            int promptType = (int)message.Param2;
                            if (promptType == 0)
                            {
                                PlayerEntity.Main.ShowDialog(DialogType.Okay, messageId: (int)message.Param1);
                            }
                            else if (promptType == 1)
                            {
                                PlayerEntity.Main.ShowDialog(DialogType.YesNo, messageId: (int)message.Param1);
                            }
                        }
                    }
                }
                for (int i = 0; i < scene.MessageQueue.Count; i++)
                {
                    MessageInfo message = scene.MessageQueue[i];
                    if (message.Message == Message.ShowWarning && message.ExecuteFrame == scene.FrameCount)
                    {
                        int messageId = (int)message.Param1;
                        int duration = (int)message.Param2;
                        if (duration == 0)
                        {
                            duration = 15;
                        }
                        PlayerEntity.Main.ShowDialog(DialogType.Overlay, messageId, param1: duration, param2: 1);
                    }
                }
                for (int i = 0; i < scene.MessageQueue.Count; i++)
                {
                    MessageInfo message = scene.MessageQueue[i];
                    if (message.Message == Message.ShowOverlay && message.ExecuteFrame == scene.FrameCount)
                    {
                        int messageId = (int)message.Param1;
                        int duration = (int)message.Param2;
                        PlayerEntity.Main.ShowDialog(DialogType.Overlay, messageId, param1: duration, param2: 0);
                    }
                }
            }
            // start displaying dialog during the fade back in after the movie
            if (QueuedOctolithMessageId != -1 && scene.FadeType == FadeType.FadeInWhite && !scene.MoviePlaying)
            {
                // OCTOLITH ACQUIRED you obtained an OCTOLITH!
                PlayerEntity.Main.ShowDialog(DialogType.Event, messageId: 7, param1: (int)EventType.Octolith);
                scene.SendMessage(Message.ShowPrompt, PlayerEntity.Main, null, param1: QueuedOctolithMessageId, param2: 0, delay: 1);
                QueuedOctolithMessageId = -1;
            }
            float countdown = PlayerEntity.Main.DeathCountdown;
            if (SinglePlayer && PlayerEntity.Main.Health == 0 && countdown > 0)
            {
                if (countdown >= 145 / 30f)
                {
                    _whiteoutStarted = false;
                    _gameOverShown = false;
                    if (EscapeState == EscapeState.Escape)
                    {
                        // EMERGENCY security system activated.
                        PlayerEntity.Main.ShowDialog(DialogType.Hud, messageId: 120, param1: 69, param2: 1);
                    }
                    else
                    {
                        // EMERGENCY POWER SUIT energy is depleted.
                        PlayerEntity.Main.ShowDialog(DialogType.Hud, messageId: 116, param1: 45, param2: 1);
                    }
                }
                else if (countdown <= 1 / 30f && !_gameOverShown)
                {
                    PlayerEntity.Main.CameraInfo.SetShake(0);
                    //ENERGY DEPLETED continue from last checkpoint?
                    PlayerEntity.Main.DialogPromptType = PromptType.GameOver;
                    PlayerEntity.Main.ShowDialog(DialogType.YesNo, messageId: 2);
                    ResetEscapeState(updateSounds: true); // the game does when reloading the room
                    _gameOverShown = true;
                }
                else if (countdown <= 50 / 30f && !_whiteoutStarted)
                {
                    PlayerEntity.Main.BeginWhiteout();
                    _whiteoutStarted = true;
                }
            }
        }

        public static void UpdateBossFlags(int areaId)
        {
            uint flags = (uint)StorySave.BossFlags;
            flags &= (uint)~(3 << (2 * areaId));
            flags |= (uint)(1 << (2 * areaId));
            StorySave.BossFlags = (BossFlags)flags;
        }

        private static void EnterShip()
        {
            // update flags for the end of the escape sequence
            StorySave.TriggerState[2] &= 0x7F;
            if (StorySave.BossFlags.TestAny(BossFlags.Unit1B1Kill))
            {
                StorySave.BossFlags &= ~BossFlags.Unit1B1Kill;
                StorySave.BossFlags |= BossFlags.Unit1B1Done;
            }
            if (StorySave.BossFlags.TestAny(BossFlags.Unit1B2Kill))
            {
                StorySave.BossFlags &= ~BossFlags.Unit1B2Kill;
                StorySave.BossFlags |= BossFlags.Unit1B2Done;
            }
            if (StorySave.BossFlags.TestAny(BossFlags.Unit2B1Kill))
            {
                StorySave.BossFlags &= ~BossFlags.Unit2B1Kill;
                StorySave.BossFlags |= BossFlags.Unit2B1Done;
            }
            if (StorySave.BossFlags.TestAny(BossFlags.Unit2B2Kill))
            {
                StorySave.BossFlags &= ~BossFlags.Unit2B2Kill;
                StorySave.BossFlags |= BossFlags.Unit2B2Done;
            }
            if (StorySave.BossFlags.TestAny(BossFlags.Unit3B1Kill))
            {
                StorySave.BossFlags &= ~BossFlags.Unit3B1Kill;
                StorySave.BossFlags |= BossFlags.Unit3B1Done;
            }
            if (StorySave.BossFlags.TestAny(BossFlags.Unit3B2Kill))
            {
                StorySave.BossFlags &= ~BossFlags.Unit3B2Kill;
                StorySave.BossFlags |= BossFlags.Unit3B2Done;
            }
            if (StorySave.BossFlags.TestAny(BossFlags.Unit4B1Kill))
            {
                StorySave.BossFlags &= ~BossFlags.Unit4B1Kill;
                StorySave.BossFlags |= BossFlags.Unit4B1Done;
            }
            if (StorySave.BossFlags.TestAny(BossFlags.Unit4B2Kill))
            {
                StorySave.BossFlags &= ~BossFlags.Unit4B2Kill;
                StorySave.BossFlags |= BossFlags.Unit4B2Done;
            }
        }

        public static void ResetEscapeState(bool updateSounds)
        {
            EscapeState = EscapeState.None;
            EscapeTimer = -1;
            EscapePaused = false;
            if (updateSounds)
            {
                UpdateEventSounds(timer: -1);
            }
        }

        private static void UpdateEscapeState(int frames, int stateId)
        {
            var state = (EscapeState)stateId;
            float time = frames / 30f;
            if (state == EscapeState.None)
            {
                EscapeTimer = -1;
                UpdateEventSounds(timer: -1);
            }
            else if (time == 0)
            {
                EscapePaused = !EscapePaused;
            }
            else if (EscapeState != state || EscapeTimer == -1)
            {
                EscapeTimer = time;
                EscapePaused = false;
                if (state == EscapeState.Escape)
                {
                    Sfx.QueueStream(VoiceId.VOICE_EVACUATE);
                    Sfx.QueueStream(VoiceId.VOICE_EVACUATE, delay: 3);
                    Sfx.QueueStream(VoiceId.VOICE_EVACUATE, delay: 6);
                    Music.PlayMusic(MusicId.SEQ_OREGANO_M55);
                    Music.UpdateTempo(245, 0); // 95.7%
                    StorySave.TriggerState[2] |= 0x80;
                }
                else
                {
                    UpdateEventSounds(timer: -1);
                }
            }
            EscapeState = state;
        }

        private static bool _playedTimedEventSfx = false;

        private static void UpdateEventSounds(float timer)
        {
            Music.UpdateEventMusic(timer);
            if (timer > 165 / 30f)
            {
                _playedTimedEventSfx = false;
            }
            else if (timer >= 0 && !_playedTimedEventSfx)
            {
                PlayerEntity.Main.PlayTimedSfx(SfxId.PUZZLE_TIMER1_SCR);
                _playedTimedEventSfx = true;
            }
            else if (timer < 0)
            {
                PlayerEntity.Main.StopTimedSfx(SfxId.PUZZLE_TIMER1_SCR);
                _playedTimedEventSfx = false;
            }
        }

        public static void CompleteRandomEncounter(int roomId)
        {
            if (roomId >= 27 && roomId <= 92)
            {
                CompletedRandomEncounterRooms[roomId - 27] = true;
            }
        }

        private static StorySave _cleanStorySave = null!;
        public static StorySave StorySave { get; private set; } = null!;

        public static void UpdateCleanSave(bool force)
        {
            if (!force && EscapeTimer != -1 && EscapeState == EscapeState.Escape)
            {
                return;
            }
            StorySave.CopyTo(_cleanStorySave);
        }

        public static void RestoreCleanSave()
        {
            // todo: save and restore more fields
            ushort prevFoundOctos = StorySave.FoundOctoliths;
            ushort prevCurOctos = StorySave.CurrentOctoliths;
            uint prevLostOctos = StorySave.LostOctoliths;
            byte[] prevAreaHunters = StorySave.AreaHunters.ToArray();
            _cleanStorySave.CopyTo(StorySave);
            int curCurCount = 0;
            int prevCurCount = 0;
            for (int i = 0; i < 8; i++)
            {
                if ((StorySave.CurrentOctoliths & (1 << i)) != 0)
                {
                    curCurCount++;
                }
                if ((prevCurOctos & (1 << i)) != 0)
                {
                    prevCurCount++;
                }
            }
            if (curCurCount > prevCurCount)
            {
                // octolith has been lost -- need to restore values from the dirty save
                StorySave.FoundOctoliths = prevFoundOctos;
                StorySave.CurrentOctoliths = prevCurOctos;
                StorySave.LostOctoliths = prevLostOctos;
            }
            prevAreaHunters.CopyTo(StorySave.AreaHunters, index: 0);
        }

        private const string _saveFolder = "Savedata";
        private static readonly JsonSerializerOptions _jsonOpt = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            WriteIndented = true,
            Converters = { new ByteArrayConverter() }
        };

        private static string GetSavePath(byte slot)
        {
            return Paths.Combine(_saveFolder, $"save{slot:000}.json");
        }

        private static string GetSettingsPath()
        {
            return Paths.Combine(_saveFolder, $"settings.json");
        }

        public static void LoadSave()
        {
            StorySave = ReadSave();
        }

        /// <summary>
        /// Begin a new game in the current slot.
        ///
        /// Nothing is written until the game itself saves, so choosing "new
        /// game" and then quitting leaves whatever was in the slot alone.
        /// </summary>
        public static void StartNewSave()
        {
            StorySave = new StorySave();
        }

        public static StorySave ReadSave()
        {
            StorySave? save = null;
            if (Menu.SaveSlot != 0)
            {
                string path = GetSavePath(Menu.SaveSlot);
                if (File.Exists(path))
                {
                    save = JsonSerializer.Deserialize<StorySave>(File.ReadAllText(path), _jsonOpt);
                }
            }
            return save ?? new StorySave();
        }

        /// <summary>True when that slot has a game in it.</summary>
        public static bool SaveExists(byte slot)
        {
            return slot != 0 && File.Exists(GetSavePath(slot));
        }

        /// <summary>
        /// Read one slot without making it the current game.
        ///
        /// <see cref="ReadSave"/> reads whichever slot <see cref="Menu.SaveSlot"/>
        /// points at, which is the wrong question for a menu listing every
        /// slot at once: it would have to move the selection to read a slot
        /// and move it back, and a throw in between would leave it moved.
        /// Returns null when the slot is empty or the file cannot be read.
        /// </summary>
        public static StorySave? PeekSave(byte slot)
        {
            if (!SaveExists(slot))
            {
                return null;
            }
            try
            {
                return JsonSerializer.Deserialize<StorySave>(
                    File.ReadAllText(GetSavePath(slot)), _jsonOpt);
            }
            catch (Exception)
            {
                // A save written by a different build, or half-written by a
                // crash, must not stop the menu that offers the other slots.
                return null;
            }
        }

        public static void CommitSave()
        {
            if (Menu.SaveSlot == 0)
            {
                return;
            }
            if (!Directory.Exists(_saveFolder))
            {
                Directory.CreateDirectory(_saveFolder);
            }
            StorySave.Weapons &= 0xFF;
            if (StorySave.WeaponSlots[2] == (int)BeamType.OmegaCannon)
            {
                StorySave.WeaponSlots[2] = (int)BeamType.None;
            }
            for (int r = 91; r <= 92; r++)
            {
                for (int b = 0; b < 60; b++)
                {
                    StorySave.RoomState[r - 27][b] = 0;
                }
            }
            File.WriteAllText(GetSavePath(Menu.SaveSlot), JsonSerializer.Serialize(StorySave, _jsonOpt));
        }

        internal sealed class ByteArrayConverter : JsonConverter<byte[]>
        {
            public override byte[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                short[]? sByteArray = JsonSerializer.Deserialize<short[]>(ref reader, options);
                if (sByteArray == null)
                {
                    return null;
                }
                byte[] value = new byte[sByteArray.Length];
                for (int i = 0; i < sByteArray.Length; i++)
                {
                    value[i] = (byte)sByteArray[i];
                }
                return value;
            }

            public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                foreach (byte val in value)
                {
                    writer.WriteNumberValue(val);
                }
                writer.WriteEndArray();
            }
        }

        private class SerializedSettings
        {
            // Bugfixes and Cheats stay at their code defaults -- there is no
            // UI to change them any more, and an old settings.json is not
            // allowed to override that. Features still persists, but only
            // the subset Commit()/Load() actually write -- see Features.cs.
            public IReadOnlyDictionary<string, string>? Features { get; set; }
            public MenuSettings? MenuSettings { get; set; }
        }

        public static MenuSettings LoadSettings()
        {
            string path = GetSettingsPath();
            if (File.Exists(path))
            {
                SerializedSettings? settings = JsonSerializer.Deserialize<SerializedSettings>(File.ReadAllText(path), _jsonOpt);
                if (settings != null)
                {
                    if (settings.Features != null)
                    {
                        Features.Load(settings.Features);
                    }
                    if (settings.MenuSettings != null)
                    {
                        return settings.MenuSettings;
                    }
                }
            }
            return new MenuSettings();
        }

        public static void CommitSettings(MenuSettings menuSettings)
        {
            // sktodo: commit menu options, including save slot
            if (!Directory.Exists(_saveFolder))
            {
                Directory.CreateDirectory(_saveFolder);
            }
            var settings = new SerializedSettings
            {
                Features = Features.Commit(),
                MenuSettings = menuSettings
            };
            File.WriteAllText(GetSettingsPath(), JsonSerializer.Serialize(settings, _jsonOpt));
        }

        public static void Reset()
        {
            _cleanStorySave = new StorySave();
            LoadSave();
            CommitSave();
            UpdateCleanSave(force: true);
            TransitionState = TransitionState.None;
            TransitionRoomId = -1;
            TransitionAltForm = false;
            if (!Mods.Network.NetSession.Active)
            {
                for (int i = 0; i < Nicknames.Length; i++) { Nicknames[i] = $"Player{i + 1}"; }
            }
            PlayerEntity.Reset();
            CamSeqEntity.Current = null;
            CameraSequence.Current = null;
            MenuPause = false;
            DialogPause = false;
            _pausingDialog = false;
            _unpausingDialog = false;
            PausePrevented = false;
            EscapeState = EscapeState.None;
            EscapeTimer = -1;
            EscapePaused = false;
            QueuedOctolithMessageId = -1;
            QueuedOublietteUnlockMessage = false;
        }
    }

    public class StorySave
    {
        // use jagged arrays since 2D arrays can't be serialized out of the box
        public byte[][] RoomState { get; init; } // 66 x 60
        public byte[] VisitedRooms { get; init; } = new byte[9];
        public int[] VisitedConnectors { get; init; } = new int[9];
        public byte[] TriggerState { get; init; } = new byte[4];
        public byte[] Logbook { get; init; } = new byte[68];
        public byte[][] EnemyEncounters { get; init; } // 9 x 8
        public int ScanCount { get; set; }
        public int EquipmentCount { get; set; }
        public int CheckpointEntityId { get; set; } = -1;
        public int CheckpointRoomId { get; set; } = -1;
        public int Health { get; set; }
        public int HealthMax { get; set; }
        public int[] Ammo { get; init; } = new int[2];
        public int[] AmmoMax { get; init; } = new int[2];
        public int[] WeaponSlots { get; init; } = new int[3];
        public ushort Weapons { get; set; }
        public uint Artifacts { get; set; }
        public ushort FoundOctoliths { get; set; }
        public ushort CurrentOctoliths { get; set; }
        public uint LostOctoliths { get; set; } = UInt32.MaxValue;
        public ushort Areas { get; set; } = 0xC; // Celestial Archives 1 & 2
        public BossFlags BossFlags { get; set; } = BossFlags.None;
        public byte[] AreaHunters { get; init; } = new byte[4];
        public byte DefeatedHunters { get; set; }
        public SaveStats Stats { get; init; } = new SaveStats();

        public StorySave()
        {
            RoomState = new byte[66][];
            for (int i = 0; i < RoomState.Length; i++)
            {
                RoomState[i] = new byte[60];
            }
            EnemyEncounters = new byte[8][];
            for (int i = 0; i < EnemyEncounters.Length; i++)
            {
                EnemyEncounters[i] = new byte[8];
            }
            PlayerValues values = Metadata.PlayerValues[0];
            Health = HealthMax = values.EnergyTank - 1;
            Ammo[0] = AmmoMax[0] = 400;
            Ammo[1] = 0;
            AmmoMax[1] = 50;
            Weapons = (ushort)(WeaponUnlockBits.PowerBeam | WeaponUnlockBits.Missile);
            if (Cheats.StartWithAllUpgrades)
            {
                Health = HealthMax = 799;
                Ammo[0] = AmmoMax[0] = 4000;
                Ammo[1] = 950;
                AmmoMax[1] = 950;
                Weapons = 0xFF;
            }
            WeaponSlots[0] = (int)BeamType.PowerBeam;
            WeaponSlots[1] = (int)BeamType.Missile;
            WeaponSlots[2] = (int)BeamType.None;
            // todo: initialize more fields
            if (!Read.ServerMode)
            {
                UpdateLogbook(0); // SCAN VISOR
                UpdateLogbook(1); // THERMAL POSITIONER
                UpdateLogbook(2); // ARM CANNON
                UpdateLogbook(3); // POWER BEAM
                UpdateLogbook(4); // MISSILE LAUNCHER
                UpdateLogbook(5); // MORPH BALL
                UpdateLogbook(6); // MORPH BALL BOMB
                UpdateLogbook(26); // JUMP BOOTS
                UpdateLogbook(28); // CHARGE SHOT
            }
            if (Cheats.StartWithAllOctoliths)
            {
                FoundOctoliths = CurrentOctoliths = 0xFF;
            }
        }

        public int InitRoomState(int roomId, int entityId, bool active,
            int activeState = 3, int inactiveState = 1)
        {
            if (entityId == -1 || roomId < 27)
            {
                return 0;
            }
            if (roomId > 92 || entityId > 239) // skdebug
            {
                return 1;
            }
            roomId -= 27;
            activeState &= 3;
            inactiveState &= 3;
            (int byteIndex, int pairIndex) = Math.DivRem(entityId, 4);
            pairIndex *= 2;
            int pairMask = 3 << pairIndex;
            if ((RoomState[roomId][byteIndex] & pairMask) == 0)
            {
                RoomState[roomId][byteIndex] &= (byte)~pairMask;
                RoomState[roomId][byteIndex] |= (byte)((active ? activeState : inactiveState) << pairIndex);
            }
            return GetRoomState(roomId + 27, entityId);
        }

        public int GetRoomState(int roomId, int entityId)
        {
            if (entityId == -1 || roomId < 27 || roomId > 92)
            {
                return 0;
            }
            if (entityId > 239) // skdebug
            {
                return 1;
            }
            roomId -= 27;
            (int byteIndex, int pairIndex) = Math.DivRem(entityId, 4);
            pairIndex *= 2;
            return ((RoomState[roomId][byteIndex] >> pairIndex) & 3) - 1;
        }

        public void SetRoomState(int roomId, int entityId, int state)
        {
            if (entityId == -1 || roomId < 27 || roomId > 92 || entityId > 239)
            {
                return;
            }
            roomId -= 27;
            state &= 3;
            (int byteIndex, int pairIndex) = Math.DivRem(entityId, 4);
            pairIndex *= 2;
            int pairMask = 3 << pairIndex;
            RoomState[roomId][byteIndex] &= (byte)~pairMask;
            RoomState[roomId][byteIndex] |= (byte)(state << pairIndex);
            return;
        }

        public bool CheckVisitedRoom(int roomId)
        {
            if (roomId < 27 || roomId > 92)
            {
                return false;
            }
            roomId -= 27;
            (int byteIndex, int bitIndex) = Math.DivRem(roomId, 8);
            return (VisitedRooms[byteIndex] & (1 << bitIndex)) != 0;
        }

        public void SetVisitedRoom(int roomId)
        {
            if (roomId < 27 || roomId > 92)
            {
                return;
            }
            roomId -= 27;
            (int byteIndex, int bitIndex) = Math.DivRem(roomId, 8);
            VisitedRooms[byteIndex] |= (byte)(1 << bitIndex);
        }

        public bool CheckVisitedConnector(int connectorId, int areaId)
        {
            if (connectorId < 0 || connectorId > 63)
            {
                return false;
            }
            // there are two ints per planet (plus one for Oubliette, which doesn't have any connectors),
            // for 64 total connectors across both sub-areas, though the max number of connectors in any area is 15.
            if (connectorId >= 32)
            {
                return ((VisitedConnectors[(areaId & ~1) + 1] >> (connectorId - 32)) & 1) != 0;
            }
            return ((VisitedConnectors[areaId & ~1] >> connectorId) & 1) != 0;
        }

        public void SetVisitedConnector(int connectorId, int areaId)
        {
            if (connectorId < 0 || connectorId > 63)
            {
                return;
            }
            if (connectorId >= 32)
            {
                VisitedConnectors[(areaId & ~1) + 1] |= 1 << (connectorId - 32);
            }
            else
            {
                VisitedConnectors[areaId & ~1] |= 1 << connectorId;
            }
        }

        public bool CheckFoundOctolith(int areaId)
        {
            return (FoundOctoliths & (1 << areaId)) != 0;
        }

        public int CountFoundOctoliths()
        {
            int count = 0;
            for (int i = 0; i < 8; i++)
            {
                if (CheckFoundOctolith(i))
                {
                    count++;
                }
            }
            return count;
        }

        public void UpdateFoundOctolith(int areaId)
        {
            FoundOctoliths |= (ushort)(1 << areaId);
            CurrentOctoliths |= (ushort)(1 << areaId);
        }

        public bool CheckFoundArtifact(int artifactId, int modelId)
        {
            return (Artifacts & (1 << (artifactId + 3 * modelId))) != 0;
        }

        public int CountFoundArtifacts(int modelId)
        {
            int count = 0;
            for (int i = 0; i < 3; i++)
            {
                if (CheckFoundArtifact(i, modelId))
                {
                    count++;
                }
            }
            return count;
        }

        public void UpdateFoundArtifact(int artifactId, int modelId)
        {
            Artifacts |= (uint)(1 << (artifactId + 3 * modelId));
        }

        public int GetEnemyOctolithDrop(int hunter)
        {
            for (int i = 0; i < 8; i++)
            {
                if (((LostOctoliths >> (4 * i)) & 15) == hunter)
                {
                    return i;
                }
            }
            return 8;
        }

        public void UpdateLogbook(int scanId)
        {
            Debug.Assert(scanId >= 0 && scanId < 68 * 8);
            int index = scanId / 8;
            byte bit = (byte)(1 << (scanId % 8));
            if ((Logbook[index] & bit) == 0)
            {
                Logbook[index] |= bit;
                int category = Strings.GetScanEntryCategory(scanId);
                if (category < 3)
                {
                    // lore, bioform, object
                    ScanCount++;
                }
                else if (category == 3)
                {
                    // equipment
                    EquipmentCount++;
                }
            }
        }

        public bool CheckLogbook(int scanId)
        {
            Debug.Assert(scanId >= 0 && scanId < 68 * 8);
            int index = scanId / 8;
            byte bit = (byte)(1 << (scanId % 8));
            return (Logbook[index] & bit) != 0;
        }

        public int GetLogbookCount(bool unlockedOnly, params char[] categories)
        {
            IReadOnlyList<StringTableEntry> logbook = Strings.ReadStringTable(StringTables.ScanLog);
            int result = 0;
            for (int i = 0; i < logbook.Count; i++)
            {
                StringTableEntry entry = logbook[i];
                if (categories.Any(c => c == entry.Category) && (!unlockedOnly || CheckLogbook(i)))
                {
                    result++;
                }
            }
            return result;
        }

        public int GetMaxScanCount()
        {
            // 215 = 82 lore + 58 bioform + 75 object
            return GetLogbookCount(unlockedOnly: false, 'L', 'B', 'O');
        }

        public int GetCompletionPercentage()
        {
            int maxScans = GetMaxScanCount();
            if (maxScans == 0)
            {
                return 0;
            }
            int count = ScanCount;
            for (int i = 1; i < 8; i++)
            {
                if (i == 2)
                {
                    continue;
                }
                if ((Weapons & (1 << i)) != 0)
                {
                    count++;
                }
            }
            for (int i = 0; i < 8; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    if ((Artifacts & (1 << (i * 3 + j))) != 0)
                    {
                        count++;
                    }
                }
            }
            for (int i = 0; i < 8; i++)
            {
                if ((FoundOctoliths & (1 << i)) != 0)
                {
                    count++;
                }
            }
            int etankCount = HealthMax / Metadata.PlayerValues[0].EnergyTank;
            int missileCount = (AmmoMax[1] - 50) / 100;
            int uaCount = (AmmoMax[0] - 400) / 300;
            count += etankCount + missileCount + uaCount;
            // 66 = 7 etanks + 12 missiles + 9 UA + 6 weapons + 24 artifacts + 8 Octoliths
            return 100 * count / (maxScans + 66);
        }

        public void CopyTo(StorySave other)
        {
            for (int i = 0; i < RoomState.Length; i++)
            {
                byte[] source = RoomState[i];
                Array.Copy(source, other.RoomState[i], source.Length);
            }
            for (int i = 0; i < EnemyEncounters.Length; i++)
            {
                byte[] source = EnemyEncounters[i];
                Array.Copy(source, other.EnemyEncounters[i], source.Length);
            }
            VisitedRooms.CopyTo(other.VisitedRooms, index: 0);
            VisitedConnectors.CopyTo(other.VisitedConnectors, index: 0);
            TriggerState.CopyTo(other.TriggerState, index: 0);
            Logbook.CopyTo(other.Logbook, index: 0);
            other.ScanCount = ScanCount;
            other.EquipmentCount = EquipmentCount;
            other.CheckpointEntityId = CheckpointEntityId;
            other.CheckpointRoomId = CheckpointRoomId;
            other.Health = Health;
            other.HealthMax = HealthMax;
            Ammo.CopyTo(other.Ammo, index: 0);
            AmmoMax.CopyTo(other.AmmoMax, index: 0);
            WeaponSlots.CopyTo(other.WeaponSlots, index: 0);
            other.Weapons = Weapons;
            other.Artifacts = Artifacts;
            other.Artifacts = Artifacts;
            other.FoundOctoliths = FoundOctoliths;
            other.CurrentOctoliths = CurrentOctoliths;
            other.LostOctoliths = LostOctoliths;
            other.Areas = Areas;
            other.BossFlags = BossFlags;
            AreaHunters.CopyTo(other.AreaHunters, index: 0);
            other.DefeatedHunters = DefeatedHunters;
        }

        public class SaveStats
        {
            public uint HunterKills { get; set; }
            public uint Deaths { get; set; }
            public uint EnemyHunterDeaths { get; set; }
            // todo: this should probably be stored globally and not deleted along with the save slot
            public uint EnemyKills { get; set; }
        }
    }
}
