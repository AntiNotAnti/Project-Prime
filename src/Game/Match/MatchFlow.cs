using System;
using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Sound;

namespace MphRead
{
    /// <summary>Multiplayer lifecycle and its existing local end presentation.
    /// The authority captures a result before replacing the remaining clock with
    /// presentation/intermission timers.</summary>
    public sealed class MatchFlow
    {
        private readonly Scene _scene;
        private readonly MatchRuntime _match;
        private bool _tempoChanged;
        private bool _stateChanged;
        private float _matchEndTime;
        private float _lastAlarmTime;
        private int _nextAlarmIndex;
        private static readonly IReadOnlyList<float> _alarmIntervals = new float[4]
        {
            1 / 30f, 8 / 30f, 15 / 30f, 6 / 30f
        };

        internal MatchFlow(Scene scene, MatchRuntime match)
        {
            _scene = scene;
            _match = match;
        }

        public void ProcessFrame()
        {
            if (_scene.Services.IsReplica) { return; }
            if (CameraSequence.Current?.IsIntro == true)
            {
                Debug.Assert(CameraSequence.Current.CamInfoRef == PlayerEntity.Main.CameraInfo);
                CameraSequence.Current.Process();
            }
            switch (_match.Phase)
            {
                case MatchPhase.Playing: ProcessPlaying(); break;
                case MatchPhase.Ending: if (!_match.UsesServerLifecycle) { ProcessEnding(); } break;
                case MatchPhase.Intermission: if (!_match.UsesServerLifecycle) { ProcessIntermission(); } break;
                case MatchPhase.WaitingForPlayers:
                case MatchPhase.Countdown: break;
                default: throw new InvalidOperationException("Unknown match phase.");
            }
        }

        private void ProcessPlaying()
        {
            // todo: update SFX
            for (int i = 0; i < _scene.MessageQueue.Count; i++)
            {
                MessageInfo message = _scene.MessageQueue[i];
                if (message.Message == Message.Complete && message.ExecuteFrame == _scene.FrameCount)
                {
                    _match.PendingEndReason = MatchEndReason.CompletionMessage;
                    _match.MatchTime = 0;
                }
            }
            // todo: update MP playtime to license info
            if (!Features.AllowInvalidTeams)
            {
                bool invalid = PlayerEntity.MaxPlayers < 2
                    && !(_match.UsesServerLifecycle && _match.Rules.MaxPlayers == 1);
                if (!invalid && _match.Rules.Teams)
                {
                    bool[] teams = new bool[2];
                    for (int i = 0; i < PlayerEntity.SlotCapacity; i++)
                    {
                        PlayerEntity player = PlayerEntity.Players[i];
                        if (player.LoadFlags.TestFlag(LoadFlags.Active))
                        {
                            teams[player.TeamIndex] = true;
                        }
                    }
                    invalid = !teams[0] || !teams[1];
                }
                if (invalid)
                {
                    _match.PendingEndReason = MatchEndReason.InvalidTeams;
                    _match.MatchTime = 0;
                    CameraSequence.Current?.End();
                    // todo: stop music/SFX, state bits/disconnect message?
                }
            }
            bool regulationExpired = _match.Period == MatchPeriod.Regulation && _match.MatchTime == 0
                && !_match.PendingEndReason.HasValue;
            MatchEndReason? explicitEnd = _match.PendingEndReason is MatchEndReason.CompletionMessage or MatchEndReason.InvalidTeams
                or MatchEndReason.Forced ? _match.PendingEndReason : null;
            _match.Logic.ProcessMode();
            MatchOvertime.Evaluate(_scene, regulationExpired, explicitEnd);
            if (_match.MatchTime != 0 && !_match.ForceEndGame)
            {
                if (_match.Period == MatchPeriod.Regulation) { UpdatePlayingPresentation(); }
            }
            else
            {
                CompleteMatch();
            }
        }

        private void UpdatePlayingPresentation()
        {
            var time = TimeSpan.FromSeconds(_match.MatchTime);
            if (time.TotalMinutes < 1 && time.Seconds <= 59 && !_tempoChanged)
            {
                if (!_scene.IsHeadless)
                {
                    _scene.Audio.UpdateMusicTempo(307, 900 / 30f);
                }
                _tempoChanged = true;
            }
            if (time.TotalMinutes < 1 && time.Seconds <= 9)
            {
                float comparison = 1;
                if (time.Seconds <= 5)
                {
                    if (Features.HalfSecondAlarm)
                    {
                        comparison = 0.5f;
                    }
                    else
                    {
                        comparison = _alarmIntervals[_nextAlarmIndex];
                    }
                }
                if (_lastAlarmTime == 0 || _scene.ElapsedTime - _lastAlarmTime >= comparison)
                {
                    _scene.Audio.Emit(new AudioRequest(AudioRequestKind.Play, Id: (int)SfxId.ALARM));
                    _lastAlarmTime = _scene.ElapsedTime;
                    _nextAlarmIndex++;
                    if (_nextAlarmIndex >= _alarmIntervals.Count)
                    {
                        _nextAlarmIndex = 0;
                    }
                }
            }
        }

        private void CompleteMatch()
        {
            if (!_scene.IsHeadless)
            {
                PlayerEntity.Main.HudEndDisrupted();
            }
            if ((_match.Rules.Mode == MatchMode.Survival || _match.Rules.Mode == MatchMode.TeamSurvival) && !_match.ForceEndGame)
            {
                for (int i = 0; i < PlayerEntity.SlotCapacity; i++)
                {
                    PlayerEntity player = PlayerEntity.Players[i];
                    // the game also checks if the player's time is greater than or equal to the time goal,
                    // which in survival is always zero, so the check isn't needed
                    if (player.LoadFlags.TestFlag(LoadFlags.Active)
                        && (player.Health > 0 || _match.TeamDeaths[player.TeamIndex] <= _match.Rules.LegacyPointGoal))
                    {
                        _match.Time[i] = -1;
                        _match.TeamTime[player.TeamIndex] = -1;
                    }
                }
                _match.Logic.UpdateState();
            }
            if (_scene.IsHeadless)
            {
                _match.CaptureResult(_scene.GlobalElapsedTime);
            }
            _match.Phase = MatchPhase.Ending;
            if (!_match.UsesServerLifecycle) { _match.MatchTime = 90 / 30f; }
            BeginEndingPresentation();
        }

        private void BeginEndingPresentation()
        {
            _scene.SetFade(FadeType.None, length: 0, overwrite: true);
            _stateChanged = true;
            _matchEndTime = _scene.GlobalElapsedTime;
            _scene.Audio.StopFreeScripts();
            _scene.Audio.StopAll();
            PlayerEntity.Main.StopLongSfx();
            if (!_scene.IsHeadless)
            {
                _scene.Audio.PlayMusicSequence(SeqId.TIMEOUT);
            }
        }

        private void ProcessEnding()
        {
            PlayerEntity winner = PlayerEntity.Players[_match.ResultSlots[0]];
            if (!_scene.IsHeadless && winner.Health > 0 && winner.LoadFlags.TestFlag(LoadFlags.Active)
                && winner.LoadFlags.TestFlag(LoadFlags.Spawned))
            {
                if (_stateChanged)
                {
                    _stateChanged = false;
                    winner.SetUpMatchEndCamera();
                }
                PlayerEntity.Main.UpdateMatchEndCamera(winner, _scene.GlobalElapsedTime - _matchEndTime);
            }
            else if (!_scene.IsHeadless)
            {
                EnsureIntroCamSeq();
            }
            if (_match.MatchTime == 0)
            {
                _match.Phase = MatchPhase.Intermission;
                _match.MatchTime = 150 / 30f;
                // todo: update license info, stop SFX
            }
        }

        private void ProcessIntermission()
        {
            if (!_scene.IsHeadless)
            {
                EnsureIntroCamSeq();
            }
            // todo: more stuff?
            if (_match.MatchTime == 0)
            {
                _match.MatchTime = -1;
                if (_scene.Services.ShouldLeaveAfterMatch)
                {
                    _scene.SetFade(FadeType.FadeOutBlack, 20 / 30f, overwrite: true, AfterFade.Exit);
                }
                // Connected, the match ending is not the session ending.
                // The server is running its intermission and is about to
                // say which map is next; NetRoomChange fades to black and
                // loads it, which is the fade the player sees. Quitting
                // here sent everybody back to their own launcher instead,
                // which is how a match that somebody won broke up the
                // group that was playing it.
            }
        }

        private void EnsureIntroCamSeq()
        {
            if (CameraSequence.Current == null && CameraSequence.Intro != null)
            {
                CameraSequence.Intro.SetUp(PlayerEntity.Main.CameraInfo, transitionTime: 0);
                PlayerEntity.Main.CameraInfo.Update();
                CameraSequence.Intro.Flags |= CamSeqFlags.Loop;
            }
        }

        public void UpdateTime()
        {
            if (_match.UsesServerLifecycle || _scene.Services.IsReplica) { return; }
            // todo: update license info etc.
            if (_match.MatchTime > 0)
            {
                _match.MatchTime = MathF.Max(_match.MatchTime - _scene.FrameTime, 0);
            }
        }

        public void ResetProgress()
        {
            _match.Phase = MatchPhase.Playing;
            _match.ResetResult();
            _match.Period = MatchPeriod.Regulation;
            _match.PeriodStartTick = 0;
            _match.ForceEndGame = false;
            _tempoChanged = false;
            _stateChanged = false;
            _matchEndTime = 0;
            _lastAlarmTime = 0;
            _nextAlarmIndex = 0;
        }

        public void Setup()
        {
            if (_match.Rules.Teams)
            {
                for (int i = 0; i < PlayerEntity.SlotCapacity; i++)
                {
                    PlayerEntity player = PlayerEntity.Players[i];
                    if (player.LoadFlags.TestFlag(LoadFlags.Active))
                    {
                        player.Team = player.TeamIndex == 0 ? Team.Orange : Team.Green;
                        player.Recolor = player.TeamIndex == 0 ? 4 : 5;
                    }
                }
            }
            switch (_match.Rules.Mode)
            {
                case MatchMode.Battle:
                case MatchMode.TeamBattle:
                    _match.ApplyRules(_match.Rules.With(scoreGoal: 7, timeLimit: TimeSpan.FromSeconds(420)));
                    _match.MatchTime = 7 * 60;
                    break;
                case MatchMode.Survival:
                case MatchMode.TeamSurvival:
                    _match.ApplyRules(_match.Rules.With(startingLives: 2, timeLimit: TimeSpan.FromSeconds(900)));
                    _match.MatchTime = 15 * 60;
                    break;
                case MatchMode.Bounty:
                case MatchMode.TeamBounty:
                    _match.ApplyRules(_match.Rules.With(scoreGoal: 3, timeLimit: TimeSpan.FromSeconds(900)));
                    _match.MatchTime = 15 * 60;
                    break;
                case MatchMode.Capture:
                    _match.ApplyRules(_match.Rules.With(scoreGoal: 5, timeLimit: TimeSpan.FromSeconds(900)));
                    _match.MatchTime = 15 * 60;
                    break;
                case MatchMode.Defender:
                case MatchMode.TeamDefender:
                case MatchMode.PrimeHunter:
                    _match.ApplyRules(_match.Rules.With(objectiveTimeGoal: TimeSpan.FromSeconds(90), timeLimit: TimeSpan.FromSeconds(900)));
                    _match.MatchTime = 15 * 60;
                    break;
                case MatchMode.Nodes:
                case MatchMode.TeamNodes:
                    _match.ApplyRules(_match.Rules.With(scoreGoal: 70, timeLimit: TimeSpan.FromSeconds(900)));
                    _match.MatchTime = 15 * 60;
                    break;
                default: throw new InvalidOperationException("Unknown multiplayer mode.");
            }
            if (CameraSequence.Intro != null)
            {
                CameraSequence.Intro.Initialize();
                CameraSequence.Intro.SetUp(PlayerEntity.Main.CameraInfo, transitionTime: 0);
                CameraSequence.Intro.Flags |= CamSeqFlags.Loop;
                _scene.SetFade(FadeType.FadeInBlack, 20 / 30f, overwrite: true);
            }
            _match.ForceEndGame = false;
            _tempoChanged = false;
            _stateChanged = false;
            _lastAlarmTime = 0;
            _nextAlarmIndex = 0;
        }
    }
}
