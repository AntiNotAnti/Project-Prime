using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Formats;
using MphRead.Sound;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public class CamSeqEntity : EntityBase
    {
        public CameraSequenceEntityData Data { get; }
        protected override Vector4? OverrideColor { get; } = new ColorRgb(0xFF, 0x69, 0xB4).AsVector4();
        public CameraSequence Sequence { get; }
        public string Name => Sequence.Name;

        private bool _active = false;
        private bool _handoff = false;
        private byte _handoffTimer = 0;
        private byte _delayTimer = 0;
        private EntityBase? _endMessageTarget = null;

        public CamSeqEntity(CameraSequenceEntityData data, Scene scene) : base(EntityType.CameraSequence, scene)
        {
            Data = data;
            Id = data.Header.EntityId;
            SetTransform(data.Header.FacingVector, data.Header.UpVector, data.Header.Position);
            AddPlaceholderModel();
            byte seqId = data.SequenceId;
            Sequence = scene.CameraSequences.GetOrLoad(seqId);
        }

        public override void Initialize()
        {
            base.Initialize();
            // these would override the keyframe refs if they were used, but they aren't
            Debug.Assert(Data.PlayerId1 == 0);
            Debug.Assert(Data.PlayerId2 == 0);
            Debug.Assert(Data.Entity1 == -1);
            Debug.Assert(Data.Entity2 == -1);
            if (Data.EndMessageTargetId != -1)
            {
                _scene.TryGetEntity(Data.EndMessageTargetId, out _endMessageTarget);
            }
            Sequence.Initialize();
        }

        public override bool Process()
        {
            if (_scene.LocalPlayer == null || !_active)
            {
                return base.Process();
            }
            if (_handoffTimer > 0)
            {
                _handoffTimer--;
                if (_handoffTimer == 0)
                {
                    Cancel();
                    return base.Process();
                }
            }
            if (_delayTimer <= Data.DelayFrames * 2) // todo: FPS stuff
            {
                TryStart();
            }
            if (_delayTimer > Data.DelayFrames * 2) // todo: FPS stuff
            {
                Sequence.Process();
                if (Sequence.Flags.TestFlag(CamSeqFlags.CanEnd))
                {
                    if (Data.Loop != 0)
                    {
                        if (_scene.Features.Bugfixes.SmoothCamSeqHandoff)
                        {
                            Sequence.Restart(Sequence.TransitionTimer, Sequence.TransitionTime);
                        }
                        else
                        {
                            // setting back the timer doesn't do anything, since the time value it compares against is lost,
                            // and Restart will update the frame values while both are set to zero anyway
                            ushort transitionTimer = Sequence.TransitionTimer;
                            Sequence.Restart();
                            Sequence.TransitionTimer = transitionTimer;
                        }
                    }
                    else
                    {
                        int sfxData = CameraSequence.SfxData[Data.SequenceId];
                        // the game stops free SFX scripts here, but we don't have the kind of
                        // "detach" action we need to do that without cutting off ending sounds
                        if ((sfxData & 0x4000) != 0)
                        {
                            _scene.Audio.ChangeForceFieldMute(increase: false);
                        }
                        if ((sfxData & 0x8000) != 0)
                        {
                            _scene.LocalPlayer?.RestartLongSfx();
                        }
                        else
                        {
                            _scene.LocalPlayer?.RestartTimedSfx();
                        }
                        int musicValue = CameraSequence.MusicData[Data.SequenceId];
                        if (musicValue != 0
                            && (musicValue & 0x4000) == 0
                            && (musicValue & 0x8000) == 0)
                        {
                            _scene.Audio.ResumeMusic();
                        }
                        _active = false;
                        Sequence.End();
                        _scene.SpecialEntities.CameraSequence = null;
                        _scene.LocalPlayer?.RefreshExternalCamera();
                        SendEndMessage();
                    }
                }
            }
            return base.Process();
        }

        private void TryStart()
        {
            if (_scene.LocalPlayer is not PlayerEntity player) return;
            int musicValue = CameraSequence.MusicData[Data.SequenceId];
            bool hasMusic = musicValue != 0;
            int sfxData = CameraSequence.SfxData[Data.SequenceId];
            if (_delayTimer == 0)
            {
                if (Data.Loop == 0)
                {
                    if ((sfxData & 0x2000) != 0)
                    {
                        _scene.Audio.StopFreeSound(SfxId.CHIME1);
                    }
                    if ((sfxData & 0x4000) != 0)
                    {
                        _scene.Audio.ChangeForceFieldMute(increase: true);
                    }
                    if ((sfxData & 0x8000) != 0)
                    {
                        _scene.LocalPlayer?.StopLongSfx();
                    }
                    else
                    {
                        _scene.LocalPlayer?.StopTimedSfx();
                    }
                }
                if (hasMusic && (musicValue & 0x4000) == 0)
                {
                    _scene.Audio.PauseMusic();
                }
            }
            _delayTimer++;
            if (_delayTimer > Data.DelayFrames * 2) // todo: FPS stuff
            {
                if (hasMusic)
                {
                    int musicOrSeqId = musicValue & 0x3FF;
                    if ((musicValue & 0x4000) != 0)
                    {
                        _scene.Audio.PlayMusic((MusicId)musicOrSeqId);
                    }
                    else
                    {
                        _scene.Audio.PlayMusicSequence((SeqId)musicOrSeqId);
                    }
                }
                int scriptId = sfxData & 0x1FFF;
                if (scriptId != 0)
                {
                    _scene.Audio.StopFreeScripts();
                    _scene.Audio.PlayScript(scriptId);
                }
                Start();
            }
        }

        private void Start()
        {
            if (_scene.LocalPlayer is not PlayerEntity player) return;
            // the game overrides keyframe values with ent1/ent2/player1/player2, but none of those are ever set
            Sequence.Flags &= ~CamSeqFlags.BlockInput;
            Sequence.Flags &= ~CamSeqFlags.ForceAlt;
            Sequence.Flags &= ~CamSeqFlags.ForceBiped;
            if (Data.BlockInput != 0)
            {
                Sequence.Flags |= CamSeqFlags.BlockInput;
            }
            if (Data.ForceAltForm != 0)
            {
                Sequence.Flags |= CamSeqFlags.ForceAlt;
            }
            else if (Data.ForceBipedForm != 0 && Data.BlockInput != 0)
            {
                Sequence.Flags |= CamSeqFlags.ForceBiped;
            }
            ushort transitionTime = (ushort)(_handoff ? 60 * 2 : 0); // todo: FPS stuff
            Sequence.SetUp(player.CameraInfo, transitionTime);
            _scene.LocalPlayer?.RefreshExternalCamera();
        }

        internal void Cancel()
        {
            if (_scene.LocalPlayer is not PlayerEntity player) return;
            player.RestartLongSfx();
            bool currentSeq = _scene.CameraSequences.Current == Sequence;
            bool playerCam = Sequence.CamInfoRef == player.CameraInfo;
            SendEndMessage();
            Sequence.End();
            _active = false;
            if (currentSeq)
            {
                player.RefreshExternalCamera();
                if (playerCam && (player.IsAltForm || player.IsMorphing || player.IsUnmorphing))
                {
                    player.ResumeOwnCamera();
                }
            }
            if (_scene.SpecialEntities.CameraSequence == this)
            {
                _scene.SpecialEntities.CameraSequence = null;
            }
        }

        private void SendEndMessage()
        {
            if (Data.EndMessage != Message.None)
            {
                _scene.SendMessage(Data.EndMessage, this, _endMessageTarget, Data.EndMessageParam, 0);
            }
        }

        public override void HandleMessage(MessageInfo info)
        {
            if (info.Message == Message.Activate || (info.Message == Message.SetActive && (int)info.Param1 != 0))
            {
                if (_scene.LocalPlayer is not PlayerEntity player) return;
                bool activate = true;
                bool handoff = false;
                if (_scene.SpecialEntities.CameraSequence != null)
                {
                    if (_scene.SpecialEntities.CameraSequence.Data.BlockInput != 0)
                    {
                        activate = false;
                    }
                    if (_scene.SpecialEntities.CameraSequence.Data.Handoff != 0 && Data.Handoff != 0)
                    {
                        handoff = true;
                        if (_scene.SpecialEntities.CameraSequence._handoffTimer == 0)
                        {
                            activate = false;
                        }
                    }
                }
                if (_scene.CameraSequences.Current != null && _scene.CameraSequences.Current.Flags.TestFlag(CamSeqFlags.BlockInput))
                {
                    activate = false;
                }
                if (activate)
                {
                    if (_scene.SpecialEntities.CameraSequence != null && _scene.SpecialEntities.CameraSequence != this)
                    {
                        if (handoff)
                        {
                            _scene.SpecialEntities.CameraSequence.Sequence.CamInfoRef = null;
                        }
                        _scene.SpecialEntities.CameraSequence.Cancel();
                    }
                    if (_scene.CameraSequences.Current != null && _scene.CameraSequences.Current != Sequence)
                    {
                        _scene.CameraSequences.Current.End();
                    }
                    if (!_active)
                    {
                        _active = true;
                        _delayTimer = 0;
                        _handoffTimer = 0;
                        _handoff = handoff;
                        _scene.SpecialEntities.CameraSequence = this;
                        if (Data.DelayFrames == 0)
                        {
                            TryStart();
                        }
                    }
                }
                else
                {
                    IReadOnlyList<CameraSequenceKeyframe> keyframes = Sequence.Keyframes;
                    for (int i = 0; i < keyframes.Count; i++)
                    {
                        CameraSequenceKeyframe keyframe = keyframes[i];
                        var message = (Message)keyframe.MessageId;
                        if (message != Message.None)
                        {
                            // game sets the keyframe as the sender
                            _scene.SendMessage(message, null!, keyframe.MessageTarget, (int)keyframe.MessageParam, 0);
                        }
                    }
                }
            }
            else if (info.Message == Message.SetActive && (int)info.Param1 == 0)
            {
                if (_handoffTimer == 0)
                {
                    _handoffTimer = 2 * 2;
                }
            }
        }


    }
}
