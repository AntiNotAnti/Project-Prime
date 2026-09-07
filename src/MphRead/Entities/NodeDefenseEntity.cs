using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MphRead.Formats;
using MphRead.Mods.Network;
using MphRead.Hud;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public class NodeDefenseEntity : EntityBase
    {
        private readonly NodeDefenseEntityData _data;
        private readonly Matrix4 _circleScale;
        private readonly CollisionVolume _volume;
        public CollisionVolume Volume => _volume;
        private readonly bool _defender = false;

        public const int NeutralTeam = PlayerEntity.SlotCapacity;
        private int _currentTeam = NeutralTeam;
        private int _occupyingTeam = NeutralTeam;
        private readonly bool[] _occupiedBy = new bool[PlayerEntity.SlotCapacity];
        private readonly bool[] _previousOccupiedBy = new bool[PlayerEntity.SlotCapacity];
        private float _blinkTimer = 0;
        private PlayerEntity? _capturedPlayer = null;
        public PlayerEntity? CapturedPlayer => _capturedPlayer;
        private float _progress = 0;
        private float _scoreTimer = 0;
        private float _curRotation = 0;
        private float _spinSpeed = 0;
        private bool _contested = false;
        private bool _inProgress = false;
        public bool Contested => _contested;
        public bool InProgress => _inProgress;

        public int CurrentTeam => _currentTeam;
        public int OccupyingTeam => _occupyingTeam;
        public bool Blinking => _blinkTimer > 0;
        public IReadOnlyList<bool> OccupiedBy => _occupiedBy;
        public bool IsOccupied => _occupiedBy.AsSpan().Contains(true);
        public float Progress => _progress;

        private readonly Material _terminalMat = null!;
        private readonly Material _ringMat = null!;

        public NodeData3? ClosestNode { get; set; } = null;

        public NodeDefenseEntity(NodeDefenseEntityData data, Scene scene) : base(EntityType.NodeDefense, scene)
        {
            _data = data;
            Id = data.Header.EntityId;
            SetTransform(data.Header.FacingVector, data.Header.UpVector, data.Header.Position);
            _volume = CollisionVolume.Move(_data.Volume, Position);
            MatchMode mode = _scene.Match.Rules.Mode;
            if (mode == MatchMode.Defender || mode == MatchMode.TeamDefender
                || mode == MatchMode.Nodes || mode == MatchMode.TeamNodes)
            {
                // yes, these names are correct
                ModelInstance terminalInst = SetUpModel("koth_data_flow");
                ModelInstance ringInst = SetUpModel("koth_terminal");
                float scale = data.Volume.CylinderRadius.FloatValue;
                _circleScale = Matrix4.CreateScale(scale);
                _terminalMat = terminalInst.Model.Materials.First(m => m.Name == "lambert4");
                _ringMat = ringInst.Model.Materials.First(m => m.Name == "lambert2");
            }
            if (mode == MatchMode.Defender || mode == MatchMode.TeamDefender)
            {
                _defender = true;
            }
        }

        public override bool Process()
        {
            if (AuthoritativePlay.Active)
            {
                _curRotation = (_curRotation + _spinSpeed * _scene.FrameTime) % 360;
                return true;
            }
            if (_defender)
            {
                ProcessDefender();
            }
            else
            {
                ProcessNodes();
            }
            return true;
        }

        public WorldRecord CaptureWorldState()
        {
            uint occupied = 0;
            for (int i = 0; i < _occupiedBy.Length; i++) { if (_occupiedBy[i]) { occupied |= 1u << i; } }
            return new WorldRecord(WorldRecordKind.Node, (byte)(_capturedPlayer?.SlotIndex ?? 255),
                (ushort)((_contested ? 1 : 0) | (_inProgress ? 2 : 0) | (_blinkTimer > 0 ? 4 : 0)),
                unchecked((uint)Id), Position, (uint)_currentTeam | ((uint)_occupyingTeam << 8) | (occupied << 16),
                0, WorldRecord.Bits(_progress), WorldRecord.Bits(_curRotation), WorldRecord.Bits(_spinSpeed));
        }

        public void ApplyWorldState(in WorldRecord state)
        {
            _capturedPlayer = state.Slot < 8 ? PlayerEntity.Players[state.Slot] : null;
            _currentTeam = (int)(state.A & 255);
            _occupyingTeam = (int)((state.A >> 8) & 255);
            for (int i = 0; i < _occupiedBy.Length; i++) { _occupiedBy[i] = (state.A & (1u << (16 + i))) != 0; }
            _contested = (state.Flags & 1) != 0;
            _inProgress = (state.Flags & 2) != 0;
            _blinkTimer = (state.Flags & 4) != 0 ? 1 / 30f : 0;
            _progress = WorldRecord.Float(state.C);
            _curRotation = WorldRecord.Float(state.D);
            _spinSpeed = WorldRecord.Float(state.E);
        }

        internal void ReleaseServerPlayer(PlayerEntity player)
        {
            _occupiedBy[player.SlotIndex] = false;
            _previousOccupiedBy[player.SlotIndex] = false;
            if (_capturedPlayer != player) { return; }
            _capturedPlayer = null;
            if (_scene.Match.Rules.Teams)
            {
                foreach (PlayerEntity teammate in _scene.GetPlayerEntities())
                {
                    if (teammate != player && teammate.TeamIndex == _currentTeam && teammate.LoadFlags.TestFlag(LoadFlags.Active))
                    { _capturedPlayer = teammate; break; }
                }
            }
            if (_capturedPlayer == null)
            {
                _currentTeam = _occupyingTeam = NeutralTeam;
                _progress = _scoreTimer = _blinkTimer = 0;
                _inProgress = _contested = false;
                Array.Clear(_occupiedBy); Array.Clear(_previousOccupiedBy);
            }
        }

        private void ProcessDefender()
        {
            int team = NeutralTeam;
            _contested = false;
            foreach (PlayerEntity player in _scene.GetPlayerEntities())
            {
                if (player.Health > 0 && _volume.TestPoint(player.Volume.SpherePosition))
                {
                    if (team == NeutralTeam)
                    {
                        team = player.TeamIndex;
                    }
                    else if (team != player.TeamIndex)
                    {
                        _contested = true;
                    }
                }
            }
            if (_contested)
            {
                team = NeutralTeam;
            }
            float speed;
            float rotation;
            if (team == NeutralTeam)
            {
                (speed, rotation) = ConstantAcceleration(-0.25f, _spinSpeed, minVelocity: 0);
            }
            else
            {
                (speed, rotation) = ConstantAcceleration(0.25f, _spinSpeed, maxVelocity: 8 * 30f);
                _scene.Match.TeamTime[team] += _scene.FrameTime;
            }
            _spinSpeed = speed;
            _curRotation += rotation;
            if (_curRotation >= 360)
            {
                _curRotation -= 360;
            }
            _currentTeam = team;
        }

        private void ProcessNodes()
        {
            int value1 = 0;
            int value2 = 0;
            bool[] prevOccupiedBy = _previousOccupiedBy;
            for (int i = 0; i < PlayerEntity.SlotCapacity; i++)
            {
                prevOccupiedBy[i] = _occupiedBy[i];
                _occupiedBy[i] = false;
            }
            _contested = false;
            int slot = 0;
            bool occupiedByAny = false;
            _soundSource.Update(Position, rangeIndex: 17);
            foreach (PlayerEntity player in _scene.GetPlayerEntities())
            {
                if (player.Health > 0 && _volume.TestPoint(player.Volume.SpherePosition))
                {
                    if (_occupyingTeam == player.TeamIndex)
                    {
                        _occupiedBy[player.SlotIndex] = true;
                        occupiedByAny = true;
                        slot = player.SlotIndex;
                    }
                    else if (_occupyingTeam == NeutralTeam && _currentTeam != player.TeamIndex)
                    {
                        _occupiedBy[player.SlotIndex] = true;
                        occupiedByAny = true;
                        _occupyingTeam = player.TeamIndex;
                        _progress = 0;
                        _inProgress = false;
                        slot = player.SlotIndex;
                    }
                    else if (_occupyingTeam != player.TeamIndex)
                    {
                        _contested = true;
                    }
                }
            }
            float rotation = 0;
            if (occupiedByAny)
            {
                if (_contested)
                {
                    if (_occupiedBy[PlayerEntity.Main.SlotIndex])
                    {
                        _soundSource.SetPausedFreeSfxScripts(true);
                    }
                }
                else if (_currentTeam != _occupyingTeam)
                {
                    if (_occupiedBy[PlayerEntity.Main.SlotIndex])
                    {
                        if (!_inProgress && _progress >= 10 / 30f)
                        {
                            if (!_scene.IsHeadless)
                            {
                                Music.PlayRoomMusic(_scene.RoomId, track: 2);
                            }
                            value1 = 1;
                            _inProgress = true;
                        }
                        _soundSource.SetPausedFreeSfxScripts(false);
                    }
                    _progress += _scene.FrameTime;
                    float spinSpeed = _progress / (300 / 30f) * (15 * 30f);
                    rotation = _spinSpeed * _scene.FrameTime + (spinSpeed - _spinSpeed) / 2 * _scene.FrameTime;
                    _spinSpeed = spinSpeed;
                    if (_progress >= 300 / 30f)
                    {
                        Complete(ref value1, ref value2);
                        occupiedByAny = false;
                    }
                }
            }
            else
            {
                if (prevOccupiedBy[PlayerEntity.Main.SlotIndex])
                {
                    if (!_scene.IsHeadless)
                    {
                        Music.PlayRoomMusic(_scene.RoomId, track: 0);
                    }
                    if (value1 != 2)
                    {
                        value1 = 3;
                    }
                }
                _occupyingTeam = NeutralTeam;
                _progress = 0;
                _inProgress = false;
                (_spinSpeed, rotation) = ConstantAcceleration(-0.15f, _spinSpeed, minVelocity: 0);
            }
            int nodeCount = 0;
            int team = _currentTeam;
            float scoreThreshold = 150 / 30f;
            if (team == NeutralTeam)
            {
                team = _occupyingTeam;
            }
            if (team != NeutralTeam)
            {
                foreach (NodeDefenseEntity node in _scene.GetNodeDefenseEntities())
                {
                    if (node._currentTeam == team && node._occupyingTeam == NeutralTeam)
                    {
                        nodeCount++;
                        if (nodeCount > 1)
                        {
                            scoreThreshold -= 45 / 30f;
                        }
                    }
                }
            }
            if (_currentTeam != NeutralTeam && !occupiedByAny)
            {
                _scoreTimer += _scene.FrameTime;
                if (_scoreTimer >= scoreThreshold)
                {
                    Debug.Assert(_capturedPlayer != null);
                    _scene.Match.Players[_capturedPlayer.SlotIndex].Points++;
                    _scoreTimer = 0;
                }
                // these SFX are empty
                if (nodeCount == 1)
                {
                    _soundSource.PlaySfx(SfxId.DATA_SLOW, loop: true);
                }
                else if (nodeCount > 1)
                {
                    _soundSource.PlaySfx(SfxId.DATA_FAST, loop: true);
                }
            }
            if (value2 != 0 && nodeCount >= 2)
            {
                value1 = 5;
            }
            if (value1 == 1)
            {
                _soundSource.StopFreeSfxScripts();
                if (nodeCount == 0)
                {
                    _soundSource.PlayFreeSfx(SfxId.CAPTURE_RING_SCRIPT1);
                }
                else if (nodeCount == 1)
                {
                    _soundSource.PlayFreeSfx(SfxId.CAPTURE_RING_SCRIPT2);
                }
                else
                {
                    _soundSource.PlayFreeSfx(SfxId.CAPTURE_RING_SCRIPT3);
                }
            }
            else if (value1 == 2)
            {
                if (nodeCount >= 2)
                {
                    _soundSource.QueueStream(VoiceId.VOICE_MULTI_NODE, delay: 1, expiration: 35 / 30f);
                }
            }
            else if (value1 == 3)
            {
                _soundSource.StopFreeSfxScripts();
                _soundSource.PlayFreeSfx(SfxId.CAPTURE_RING_FAIL);
            }
            float prevRotation = _curRotation;
            _curRotation += rotation;
            if (_curRotation >= 360)
            {
                _curRotation -= 360;
            }
            if (!occupiedByAny)
            {
                _blinkTimer = 0;
            }
            else if (Fixed.ToInt(_curRotation) / 61440 != Fixed.ToInt(prevRotation) / 61440)
            {
                _blinkTimer = 1 / 30f;
            }
            else if (_blinkTimer > 0)
            {
                _blinkTimer -= _scene.FrameTime;
            }
        }

        private void Complete(ref int dest1, ref int dest2)
        {
            if (_currentTeam == PlayerEntity.Main.TeamIndex)
            {
                dest1 = 4;
                string msg = Text.Strings.GetHudMessage(211); // node stolen
                PlayerEntity.Main.QueueHudMessage(128, 133, Align.Center, 256, 8, new ColorRgba(31), 1, 90 / 30f, 17, msg);
            }
            for (int i = 0; i < PlayerEntity.SlotCapacity; i++)
            {
                PlayerEntity player = PlayerEntity.Players[i];
                if (_occupiedBy[i])
                {
                    _scene.Match.Players[i].NodesCaptured++;
                    if (player.LoadFlags.TestFlag(LoadFlags.Active))
                    {
                        _capturedPlayer = player;
                    }
                }
                else if (_currentTeam == player.TeamIndex)
                {
                    _scene.Match.Players[i].NodesLost++;
                }
                _occupiedBy[i] = false;
            }
            if (_capturedPlayer == PlayerEntity.Main)
            {
                PlayerEntity.Main.QueueHudMessage(128, 133, 90 / 30f, 1, 206); // complete
            }
            _currentTeam = _occupyingTeam;
            _progress = 0;
            _inProgress = false;
            _occupyingTeam = NeutralTeam;
            _scoreTimer = 150 / 30f;
            if (_currentTeam == PlayerEntity.Main.TeamIndex)
            {
                if (!_scene.IsHeadless)
                {
                    Music.PlayRoomMusic(_scene.RoomId, track: 0);
                }
                dest1 = 2;
            }
            else
            {
                dest2 = 1;
            }
            _blinkTimer = 0;
        }

        private static readonly ColorRgb _neutralColor = new ColorRgb(31, 31, 31);
        private static readonly ColorRgb _selfColor = new ColorRgb(15, 15, 31);
        private static readonly ColorRgb _enemyColor = new ColorRgb(31, 0, 0);

        // todo: is_visible
        public override void GetDrawInfo()
        {
            bool blinking = _blinkTimer > 0;
            ColorRgb color = _neutralColor;
            if (_currentTeam == NeutralTeam)
            {
                if (blinking)
                {
                    if (_scene.Match.Rules.Teams)
                    {
                        color = Metadata.TeamColors[_occupyingTeam];
                    }
                    else if (_occupyingTeam == PlayerEntity.Main.TeamIndex)
                    {
                        color = _selfColor;
                    }
                    else
                    {
                        color = _enemyColor;
                    }
                }
            }
            else if (_scene.Match.Rules.Teams)
            {
                if (blinking)
                {
                    color = Metadata.TeamColors[_occupyingTeam];
                }
                else
                {
                    color = Metadata.TeamColors[_currentTeam];
                }
            }
            else
            {
                if (_currentTeam == PlayerEntity.Main.TeamIndex)
                {
                    if (!blinking || _occupyingTeam == PlayerEntity.Main.TeamIndex)
                    {
                        color = _selfColor;
                    }
                    else
                    {
                        color = _enemyColor;
                    }
                }
                else if (blinking && _occupyingTeam == PlayerEntity.Main.TeamIndex)
                {
                    color = _selfColor;
                }
                else
                {
                    color = _enemyColor;
                }
            }
            _terminalMat.Diffuse = color;
            _ringMat.Diffuse = color;
            base.GetDrawInfo();
        }

        protected override Matrix4 GetModelTransform(ModelInstance inst, int index)
        {
            Matrix4 transform = base.GetModelTransform(inst, index);
            if (index == 1)
            {
                var rotY = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(_curRotation));
                transform = _circleScale * rotY * transform;
                transform.Row3.Xyz = Position.AddY(0.7f);
            }
            return transform;
        }

        public override void GetDisplayVolumes()
        {
            if (_scene.ShowVolumes == VolumeDisplay.DefenseNode)
            {
                AddVolumeItem(_volume, Vector3.One);
            }
        }
    }
}
