using System.Diagnostics;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public class OctolithFlagEntity : EntityBase
    {
        private readonly OctolithFlagEntityData _data;
        public OctolithFlagEntityData Data => _data;
        private readonly Vector3 _basePosition = Vector3.Zero;
        public Vector3 BasePosition => _basePosition;
        private readonly bool _bounty = false;

        private PlayerEntity? _carrier = null;
        public PlayerEntity? Carrier => _carrier;
        private PlayerEntity? _lastCarrier = null;
        private bool _atBase = false;
        public bool AtBase => _atBase;
        private bool _grounded = false;
        private float _resetTimer = 0;
        private float _gravity = 0;

        public NodeData3? ClosestNode { get; set; } = null;
        public NodeData3? BaseClosestNode { get; set; } = null;

        public OctolithFlagEntity(OctolithFlagEntityData data, Scene scene) : base(EntityType.OctolithFlag, scene)
        {
            _data = data;
            Id = data.Header.EntityId;
            SetTransform(data.Header.FacingVector, data.Header.UpVector, data.Header.Position);
            MatchMode mode = _scene.Match.Rules.Mode;
            Recolor = mode == MatchMode.Capture ? data.TeamId : 2;
            _bounty = mode != MatchMode.Capture;
            if (mode == MatchMode.Capture || mode == MatchMode.Bounty || mode == MatchMode.TeamBounty)
            {
                SetUpModel("octolith_ctf");
                SetUpModel(mode == MatchMode.Capture ? "flagbase_ctf" : "flagbase_bounty");
                _basePosition = Position;
                SetAtBase();
            }
        }

        private void SetAtBase()
        {
            Position = _basePosition.AddY(1.25f);
            _atBase = true;
            _grounded = true;
            _resetTimer = 0;
            _gravity = 0;
            if (_carrier != null)
            {
                _carrier.OctolithFlag = null;
                _carrier = null;
            }
            _lastCarrier = null;
            ClosestNode = BaseClosestNode;
        }

        public override void GetVectors(out Vector3 position, out Vector3 up, out Vector3 facing)
        {
            position = _basePosition;
            up = UpVector;
            facing = FacingVector;
        }

        public override void GetPosition(out Vector3 position)
        {
            position = _basePosition;
        }

        public override bool Process()
        {
            base.Process();
            if (AuthoritativePlay.Active) { return true; }
            // todo?: lots of wifi stuff
            bool pickedUp = false;
            if (_carrier == null)
            {
                foreach (PlayerEntity player in _scene.GetPlayerEntities())
                {
                    if (player.Health == 0 || player.IsAltForm || player.IsMorphing
                        || !_bounty && player.TeamIndex == _data.TeamId && _atBase)
                    {
                        continue;
                    }
                    float max = Fixed.ToFloat(player.Values.MaxPickupHeight);
                    float min = Fixed.ToFloat(player.Values.MinPickupHeight);
                    float radius = Fixed.ToFloat(player.Values.BipedColRadius);
                    var cylPos = new Vector3(Position.X, Position.Y - max - 1.25f, Position.Z);
                    float cylHeight = max - min + 0.5f;
                    float radii = radius + 0.5f;
                    CollisionResult discard = default;
                    if (CollisionDetection.CheckCylinderBetweenPoints(player.PrevPosition, player.Position,
                        cylPos, cylHeight, radii, ref discard))
                    {
                        pickedUp = OnTouched(player);
                        break;
                    }
                }
                if (!_atBase && _carrier == null)
                {
                    _resetTimer += _scene.FrameTime;
                    if (_resetTimer >= 20)
                    {
                        Reset();
                    }
                }
            }
            if (_carrier != null)
            {
                _atBase = false;
                _grounded = true;
                _resetTimer = 0;
                ClosestNode = _carrier.ClosestNode;
                Position = new Vector3(
                    _carrier.Position.X + -0.35f * _carrier.Field70,
                    _carrier.Position.Y + 1.05f,
                    _carrier.Position.Z + -0.35f * _carrier.Field74
                );
                if (_carrier.Health <= 0 || _carrier.IsAltForm || _carrier.IsMorphing
                    || !_carrier.LoadFlags.TestFlag(LoadFlags.Active))
                {
                    bool reset = _carrier.Health == 0 && _scene.Match.Rules.OctolithReset;
                    OnDropped(reset);
                }
                else if (pickedUp)
                {
                    if (!_bounty)
                    {
                        if (PlayerEntity.Main.TeamIndex == _data.TeamId)
                        {
                            _soundSource.QueueStream(VoiceId.VOICE_OCTO_PICKUP, delay: 1, expiration: 2);
                            PlayerEntity.Main.StartFlagCarrySfx();
                            if (!_scene.IsHeadless)
                            {
                                Music.PlayRoomMusic(_scene.RoomId, track: 1);
                            }
                        }
                        else
                        {
                            _soundSource.PlayFreeSfx(SfxId.FLAG_ACQUIRED);
                        }
                    }
                    else
                    {
                        if (!_scene.IsHeadless)
                        {
                            Music.PlayRoomMusic(_scene.RoomId, track: 1);
                        }
                        if (_carrier == PlayerEntity.Main)
                        {
                            _soundSource.QueueStream(VoiceId.VOICE_OCTO_PICKUP, delay: 1, expiration: 2);
                            _soundSource.PlayFreeSfx(SfxId.FLAG_ACQUIRED);
                            PlayerEntity.Main.QueueHudMessage(128, 133, 90 / 30f, 1, 202); // return to base
                        }
                        else
                        {
                            _soundSource.QueueStream(VoiceId.VOICE_OCTO_PICKUP, delay: 1, expiration: 2);
                            PlayerEntity.Main.StartFlagCarrySfx();
                        }
                    }
                }
            }
            if (_grounded)
            {
                _gravity = 0;
            }
            else
            {
                Vector3 prevPos = Position;
                (float gravity, float displacement) = ConstantAcceleration(-0.02f, _gravity);
                Position = Position.AddY(displacement);
                _gravity = gravity;
                ClosestNode = null;
                var results = new CollisionResult[16];
                int count = CollisionDetection.CheckSphereBetweenPoints(prevPos, Position, radius: 1.25f,
                    limit: 16, includeOffset: false, TestFlags.None, _scene, results);
                for (int i = 0; i < count; i++)
                {
                    CollisionResult result = results[i];
                    if (result.Plane.Y > Fixed.ToFloat(1401))
                    {
                        Vector3 pos = Position.AddY(-1.25f);
                        float dist = result.Plane.W - Vector3.Dot(pos, result.Plane.Xyz);
                        Position += result.Plane.Xyz * dist;
                        _grounded = true;
                    }
                }
                Debug.Assert(_scene.Room != null);
                if (Position.Y < _scene.Room.Meta.KillHeight)
                {
                    Reset();
                }
            }
            return true;
        }

        public WorldRecord CaptureWorldState() => new WorldRecord(WorldRecordKind.Flag,
            (byte)(_carrier?.SlotIndex ?? 255), (ushort)((_atBase ? 1 : 0) | (_grounded ? 2 : 0)),
            unchecked((uint)Id), Position, _data.TeamId, WorldRecord.Bits(_resetTimer), 0, 0, 0);

        public void ApplyWorldState(in WorldRecord state)
        {
            if (_carrier != null && _carrier.OctolithFlag == this) { _carrier.OctolithFlag = null; }
            _carrier = state.Slot < 8 ? PlayerEntity.Players[state.Slot] : null;
            if (_carrier != null) { _carrier.OctolithFlag = this; }
            _atBase = (state.Flags & 1) != 0;
            _grounded = (state.Flags & 2) != 0;
            _resetTimer = WorldRecord.Float(state.B);
            Position = state.Position;
        }

        internal void ReleaseServerPlayer(PlayerEntity player)
        {
            if (_carrier == player) { OnDropped(_scene.Match.Rules.OctolithReset); }
            if (_lastCarrier == player) { _lastCarrier = null; }
            if (player.OctolithFlag == this) { player.OctolithFlag = null; }
        }

        private bool OnTouched(PlayerEntity player)
        {
            if (_lastCarrier != null && player.TeamIndex != _lastCarrier.TeamIndex)
            {
                _scene.Match.Players[player.SlotIndex].OctolithStops++;
            }
            if (!_bounty && player.TeamIndex == _data.TeamId)
            {
                if (!_atBase)
                {
                    _soundSource.PlayFreeSfx(SfxId.FLAG_RESET2);
                    // your octolith reset! / enemy octolith reset!
                    int messageId = PlayerEntity.Main.TeamIndex == _data.TeamId ? 201 : 207;
                    PlayerEntity.Main.QueueHudMessage(128, 133, 60 / 30f, 1, messageId);
                }
                SetAtBase();
                return false;
            }
            if (_carrier != null)
            {
                _carrier.OctolithFlag = null;
            }
            player.OctolithFlag = this;
            _carrier = player;
            _lastCarrier = player;
            _atBase = false;
            _grounded = true;
            _resetTimer = 0;
            return true;
        }

        private void Reset()
        {
            SetAtBase();
            int messageId;
            if (!_bounty)
            {
                if (PlayerEntity.Main.TeamIndex == _data.TeamId)
                {
                    messageId = 201; // your octolith reset!
                    _soundSource.PlayFreeSfx(SfxId.FLAG_RESET2);
                }
                else
                {
                    messageId = 207; // enemy octolith reset!
                    _soundSource.PlayFreeSfx(SfxId.FLAG_RESET1);
                }
            }
            else
            {
                messageId = 257; // octolith reset!
                _soundSource.PlayFreeSfx(SfxId.FLAG_RESET2);
            }
            PlayerEntity.Main.QueueHudMessage(128, 133, 60 / 30f, 1, messageId);
        }

        private void OnDropped(bool reset)
        {
            Debug.Assert(_carrier != null);
            _scene.Match.Players[_carrier.SlotIndex].OctolithDrops++;
            int messageId;
            if (!_bounty)
            {
                if (PlayerEntity.Main.TeamIndex == _data.TeamId)
                {
                    if (reset)
                    {
                        messageId = 201; // your octolith reset!
                        _soundSource.PlayFreeSfx(SfxId.FLAG_RESET2);
                    }
                    else
                    {
                        messageId = 230; // the enemy dropped your octolith!
                        _soundSource.QueueStream(VoiceId.VOICE_OCTO_RESET, delay: 1, expiration: 2);
                    }
                }
                else if (reset)
                {
                    messageId = 207; // enemy octolith reset!
                    _soundSource.PlayFreeSfx(SfxId.FLAG_RESET1);
                }
                else
                {
                    messageId = 231; // your team dropped the octolith!
                    _soundSource.PlayFreeSfx(SfxId.FLAG_DROPPED);
                }
            }
            else if (reset)
            {
                messageId = 257; // octolith reset!
                _soundSource.PlayFreeSfx(SfxId.FLAG_RESET2);
            }
            else
            {
                _soundSource.QueueStream(VoiceId.VOICE_OCTO_RESET, delay: 1, expiration: 2);
                messageId = 229; // the octolith has been dropped!
                _soundSource.PlayFreeSfx(SfxId.FLAG_DROPPED);
            }
            PlayerEntity.Main.QueueHudMessage(128, 133, 60 / 30f, 1, messageId);
            PlayerEntity.Main.StopFlagCarrySfx();
            if (!_scene.IsHeadless)
            {
                Music.PlayRoomMusic(_scene.RoomId, track: 0);
            }
            if (reset)
            {
                SetAtBase();
            }
            else
            {
                _grounded = false;
                _atBase = false;
                if (_carrier != null)
                {
                    _carrier.OctolithFlag = null;
                    _carrier = null;
                }
                _resetTimer = 0;
            }
        }

        public void OnCaptured()
        {
            if (AuthoritativePlay.Active) { return; }
            Debug.Assert(_carrier != null);
            if (!_bounty)
            {
                if (PlayerEntity.Main.TeamIndex == _data.TeamId)
                {
                    _soundSource.QueueStream(VoiceId.VOICE_OCTO_SCORE, delay: 40 / 30f);
                    _soundSource.PlayFreeSfx(SfxId.SCORE);
                }
                else
                {
                    _soundSource.PlayFreeSfx(SfxId.SCORED_ON);
                }
            }
            else
            {
                if (Bugfixes.CorrectBountySfx && _carrier.TeamIndex == PlayerEntity.Main.TeamIndex
                    || !Bugfixes.CorrectBountySfx && _carrier.IsMainPlayer)
                {
                    _soundSource.QueueStream(VoiceId.VOICE_BOUNTY, delay: 40 / 30f);
                    _soundSource.PlayFreeSfx(SfxId.SCORE);
                }
                else
                {
                    _soundSource.PlayFreeSfx(SfxId.SCORED_ON);
                }
                PlayerEntity.Main.QueueHudMessage(128, 133, 90 / 30f, 1, 203); // bounty received
            }
            PlayerEntity.Main.StopFlagCarrySfx();
            if (!_scene.IsHeadless)
            {
                Music.PlayRoomMusic(_scene.RoomId, track: 0);
            }
            _scene.Match.Players[_carrier.SlotIndex].Points++;
            _scene.Match.Players[_carrier.SlotIndex].OctolithScores++;
            SetAtBase();
        }

        // todo: is_visible for base and flag
        protected override Matrix4 GetModelTransform(ModelInstance inst, int index)
        {
            Matrix4 transform = base.GetModelTransform(inst, index);
            if (index == 1)
            {
                transform.Row3.Xyz = _basePosition;
            }
            return transform;
        }

        protected override int GetModelRecolor(ModelInstance inst, int index)
        {
            if (index == 1 && _bounty)
            {
                return 0;
            }
            return base.GetModelRecolor(inst, index);
        }
    }
}
