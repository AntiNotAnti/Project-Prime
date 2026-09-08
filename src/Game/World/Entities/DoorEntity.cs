using System;
using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public class DoorEntity : EntityBase
    {
        private readonly DoorEntityData _data;
        private readonly Matrix4 _lockTransform;
        internal readonly ModelInstance _lock;
        internal AnimationInfo AnimInfo => _models[0].AnimInfo;

        public float Radius { get; }
        public float RadiusSquared { get; }
        public DoorFlags Flags { get; set; } = DoorFlags.None;
        public Portal? Portal { get; private set; }
        internal bool Locked => Flags.TestFlag(DoorFlags.Locked);
        private bool Unlocked => Flags.TestFlag(DoorFlags.Unlocked);
        public Vector3 LockPosition => (_transform * _lockTransform).Row3.Xyz;
        public DoorEntityData Data => _data;

        private static readonly IReadOnlyList<int> _scanIds = new int[10]
        {
            0, 255, 264, 252, 256, 253, 254, 249, 266, 265
        };

        public DoorEntity(DoorEntityData data, string nodeName, Scene scene) : base(EntityType.Door, nodeName, scene)
        {
            _data = data;
            Id = data.Header.EntityId;
            SetTransform(data.Header.FacingVector, data.Header.UpVector, data.Header.Position);
            DoorMetadata meta = Metadata.Doors[(int)data.DoorType];
            Radius = meta.Radius;
            RadiusSquared = Radius * Radius;
            int recolorId = 0;
            if (data.DoorType == DoorType.Standard || data.DoorType == DoorType.Thin)
            {
                recolorId = Metadata.DoorPalettes[(int)data.PaletteId];
            }
            Recolor = recolorId;
            // in practice (actual palette indices, not the index into the metadata):
            // - standard = 0, 1, 2, 3, 4, 6
            // - morph ball = 0
            // - boss = 0
            // - thin = 0, 7
            ModelInstance inst = SetUpModel(meta.Name);
            if (_data.DoorType == DoorType.Thin)
            {
                inst.SetAnimation(1, 0, SetFlags.Texture | SetFlags.Texcoord | SetFlags.Node, AnimFlags.None);
            }
            else
            {
                inst.SetAnimation(0, 0, SetFlags.Texture | SetFlags.Texcoord | SetFlags.Node, AnimFlags.Ended | AnimFlags.NoLoop);
                inst.AnimInfo.Flags[0] |= AnimFlags.Reverse;
            }
            inst.SetAnimation(0, 1, SetFlags.Material, AnimFlags.Ended | AnimFlags.NoLoop);
            inst.AnimInfo.Flags[1] |= AnimFlags.Reverse;
            _lock = SetUpModel(meta.LockName);
            _lockTransform = Matrix4.CreateTranslation(0, meta.LockOffset, 0);
            int state = _scene.GetInitialEntityState(Id, active: _data.Locked != 0);
            if (state != 0 && !_scene.Features.Cheats.UnlockAllDoors)
            {
                Flags |= DoorFlags.Locked;
            }
            UpdateScanId();
            Flags |= DoorFlags.Closed;
            if (_data.PaletteId == 9) // any beam door
            {
                Flags |= DoorFlags.ShowLock;
            }
        }

        public override void Initialize()
        {
            base.Initialize();
            _scene.LoadEffect(114, persistent: false); // lockDefeat
            string portalName = $"{_data.NodeName.MarshalString()}_{_nodeName}";
            Portal? portal = _scene.Room?.GetPortalByName(portalName);
            if (portal != null)
            {
                portal.Active = false;
                Portal = portal;
            }

        }

        private void UpdateScanId()
        {
            if (_data.DoorType == DoorType.Boss)
            {
                _scanId = 269;
            }
            else if (Flags.TestFlag(DoorFlags.Locked))
            {
                _scanId = _scanIds[(int)_data.PaletteId];
            }
            else
            {
                _scanId = 251;
            }
        }

        public override void GetPosition(out Vector3 position)
        {
            position = LockPosition;
        }

        public override void GetVectors(out Vector3 position, out Vector3 up, out Vector3 facing)
        {
            position = LockPosition;
            up = UpVector;
            facing = FacingVector;
        }

        public override int GetScanId(bool alternate = false)
        {
            if (Flags.TestFlag(DoorFlags.ShouldOpen))
            {
                return 0;
            }
            return _scanId;
        }

        public override bool Process()
        {
            if (Unlocked && _lock.AnimInfo.Flags[0].TestFlag(AnimFlags.Ended))
            {
                Flags &= ~DoorFlags.Locked;
                Flags &= ~DoorFlags.Unlocked;
            }
            UpdateScanId();
            if (Locked && !Unlocked)
            {
                Flags &= ~DoorFlags.ShotOpen;
            }
            if (!_scene.InRoomTransition)
            {
                if (ShouldOpen())
                {
                    Flags |= DoorFlags.ShouldOpen;
                }
                else
                {
                    Flags &= ~DoorFlags.ShouldOpen;
                }
            }
            _soundSource.Update(Position, rangeIndex: 6);
            if (Flags.TestFlag(DoorFlags.ShouldOpen))
            {
                Flags &= ~DoorFlags.Closed;
                if (AnimInfo.Index[0] != 0)
                {
                    _models[0].SetAnimation(0, 0, SetFlags.Texture | SetFlags.Texcoord | SetFlags.Node, AnimFlags.NoLoop);
                    _soundSource.PlaySfx(SfxId.DOOR3_OPEN_SCR);
                }
                else if (AnimInfo.Flags[0].TestFlag(AnimFlags.Ended) && AnimInfo.Flags[0].TestFlag(AnimFlags.Reverse))
                {
                    AnimInfo.Flags[0] &= ~AnimFlags.Ended;
                    AnimInfo.Flags[0] &= ~AnimFlags.Paused;
                    AnimInfo.Flags[0] &= ~AnimFlags.Reverse;
                    _soundSource.PlaySfx(_data.DoorType == DoorType.Boss ? SfxId.DOOR2_OPEN : SfxId.DOOR_OPEN);
                }
            }
            else if (AnimInfo.Index[0] == 0 && AnimInfo.Flags[0].TestFlag(AnimFlags.Ended))
            {
                if (!AnimInfo.Flags[0].TestFlag(AnimFlags.Reverse))
                {
                    Flags |= DoorFlags.Closed;
                    AnimInfo.Flags[0] &= ~AnimFlags.Ended;
                    AnimInfo.Flags[0] &= ~AnimFlags.Paused;
                    AnimInfo.Flags[0] |= AnimFlags.Reverse;
                    AnimInfo.Flags[1] = AnimInfo.Flags[0];
                    SfxId sfx = _data.DoorType switch
                    {
                        DoorType.Boss => SfxId.DOOR2_CLOSE_SCR,
                        DoorType.Thin => SfxId.DOOR3_CLOSE_SCR,
                        _ => SfxId.DOOR_CLOSE
                    };
                    _soundSource.PlaySfx(sfx);
                }
                else if (_data.DoorType == DoorType.Thin)
                {
                    _models[0].SetAnimation(1, 0, SetFlags.Texture | SetFlags.Texcoord | SetFlags.Node);
                }
            }
            if (Flags.TestFlag(DoorFlags.ShotOpen))
            {
                if (_data.DoorType == DoorType.Boss && AnimInfo.Flags[1].TestFlag(AnimFlags.Reverse))
                {
                    _soundSource.StopSfx(SfxId.DOOR2_LOOP);
                    if (AnimInfo.Frame[1] < 2)
                    {
                        _soundSource.PlaySfx(SfxId.DOOR2_PRE_OPEN, recency: Single.MaxValue, sourceOnly: true);
                    }
                }
                AnimInfo.Flags[1] &= ~AnimFlags.Ended;
                AnimInfo.Flags[1] &= ~AnimFlags.Paused;
                AnimInfo.Flags[1] &= ~AnimFlags.Reverse;
            }
            else if (_data.DoorType == DoorType.Boss && AnimInfo.Flags[1].TestFlag(AnimFlags.Reverse))
            {
                _soundSource.PlaySfx(SfxId.DOOR2_LOOP, loop: true);
            }
            UpdateAnimFrames(_models[0]);
            UpdateAnimFrames(_models[1]);
            bool portalActive = false;
            if (Flags.TestFlag(DoorFlags.ShouldOpen))
            {
                // todo: FPS stuff
                if (AnimInfo.Frame[0] > AnimInfo.FrameCount[0] / 2)
                {
                    Flags |= DoorFlags.Open;
                }
                if (_data.DoorType != DoorType.Standard || AnimInfo.Frame[0] >= 10)
                {
                    portalActive = true;
                }
            }
            else
            {
                if (Flags.TestFlag(DoorFlags.Closed))
                {
                    if (_data.DoorType == DoorType.Thin)
                    {
                        if (AnimInfo.Index[0] == 0)
                        {
                            portalActive = true;
                        }
                    }
                    else if (!AnimInfo.Flags[0].TestFlag(AnimFlags.Ended))
                    {
                        portalActive = true;
                    }
                }
                else
                {
                    portalActive = true;
                }
                Flags &= ~DoorFlags.Open;
            }
            if (Portal != null)
            {
                Portal.Active = portalActive;
            }
            Flags &= ~DoorFlags.Opening;
            Flags &= ~DoorFlags.Bit10;
            if (Flags.TestFlag(DoorFlags.ShouldOpen))
            {
                Flags |= DoorFlags.Opening;
            }
            if (Flags.TestFlag(DoorFlags.Bit8))
            {
                Flags |= DoorFlags.Bit10;
            }
            if (Locked && Flags.TestFlag(DoorFlags.ShowLock) && !Flags.TestFlag(DoorFlags.ShouldOpen)
                && (AnimInfo.Index[0] != 0 || AnimInfo.Flags[0].TestFlag(AnimFlags.Ended)))
            {
                // todo: bits 8/9 and 10 are basically a counter and a bool, and we should just replace them with that
                if (Flags.TestFlag(DoorFlags.Bit10) && _scene.FrameCount > (ulong)SimTicks.From30HzFrames(3))
                {
                    _soundSource.PlaySfx(SfxId.LOCK_ANIM, recency: Single.MaxValue, sourceOnly: true);
                }
                uint flags = (uint)Flags;
                uint bits = (flags << 22) >> 30;
                if (bits < 2)
                {
                    bits = (bits + 1) & 3;
                    flags &= 0xFFFFFCFF;
                    flags |= bits << 8;
                    Flags = (DoorFlags)flags;
                }
            }
            else
            {
                Flags &= ~DoorFlags.Bit8;
                Flags &= ~DoorFlags.Bit9;
            }
            return true;
        }

        private bool ShouldOpen()
        {
            if (Locked || !Flags.TestFlag(DoorFlags.ShotOpen))
            {
                return false;
            }
            foreach (PlayerEntity player in _scene.GetPlayerEntities())
            {
                if (player.Health > 0 && !player.IsBot && (Position - player.Position).LengthSquared < 16)
                {
                    return true;
                }
            }
            if (Flags.TestFlag(DoorFlags.Opening))
            {
                Flags &= ~DoorFlags.ShotOpen;
            }
            return false;
        }

        private void ForceClose()
        {
            ModelInstance inst = _models[0];
            if (_data.DoorType == DoorType.Thin)
            {
                inst.SetAnimation(1, 0, SetFlags.Texture | SetFlags.Texcoord | SetFlags.Node, AnimFlags.None);
            }
            else
            {
                inst.SetAnimation(0, 0, SetFlags.Texture | SetFlags.Texcoord | SetFlags.Node, AnimFlags.Ended | AnimFlags.NoLoop);
                inst.AnimInfo.Flags[0] |= AnimFlags.Reverse;
            }
            if (Portal != null)
            {
                Portal.Active = false;
            }
        }

        public void Lock(bool updateState)
        {
            Flags |= DoorFlags.Locked;
        }

        public void Unlock(bool updateState, bool noLockAnimSfx)
        {
            if (_scene.InRoomTransition)
            {
                return;
            }
            Flags |= DoorFlags.Unlocked;
            // hack to prevent door SFX from playing after boss room transitions/movies
            // UNIT2_B1, UNIT3_B1, UNIT1_B2, UNIT4_B2 (Cretaphid)
            // UNIT1_B1, UNIT4_B1, UNIT2_B2, UNIT3_B2 (Slench)
            // the game has its own weird hacks for this where fade state is checked in the timed SFX code.
            // note: lock SFX is prevented by the frame count check in Process()
            if (_scene.RoomId != 55 && _scene.RoomId != 71 && _scene.RoomId != 44 && _scene.RoomId != 88
                && _scene.RoomId != 35 && _scene.RoomId != 82 && _scene.RoomId != 64 && _scene.RoomId != 76)
            {
                _scene.LocalPlayer?.SetDoorChimeTimer(2 / (float)SimTicks.LegacyHz);
                if (!noLockAnimSfx)
                {
                    _scene.LocalPlayer?.SetDoorUnlockTimer(2 / (float)SimTicks.LegacyHz);
                }
            }
            _lock.SetAnimation(1, AnimFlags.NoLoop);
            _scene.SpawnEffect(114, UpVector, FacingVector, LockPosition); // lockDefeat
        }

        public override void HandleMessage(MessageInfo info)
        {
            if (info.Message == Message.Unlock)
            {
                Unlock(updateState: true, noLockAnimSfx: false);
            }
            else if (info.Message == Message.Lock)
            {
                Lock(updateState: true);
            }
            else if (info.Message == Message.UnlockConnectors)
            {
                foreach (DoorEntity door in _scene.GetDoorEntities())
                {
                    if (door.Id == -1)
                    {
                        door.Unlock(updateState: true, noLockAnimSfx: false);
                    }
                }
            }
            else if (info.Message == Message.LockConnectors)
            {
                foreach (DoorEntity door in _scene.GetDoorEntities())
                {
                    if (door.Id == -1)
                    {
                        door.Lock(updateState: true);
                    }
                }
            }
        }

        public override void Destroy()
        {
            _soundSource.StopSfx(SfxId.DOOR_OPEN);
            Portal = null;
            base.Destroy();
        }

        protected internal override Matrix4 GetModelTransform(ModelInstance inst, int index)
        {
            if (index == 1)
            {
                return Matrix4.CreateScale(inst.Model.Scale) * _transform * _lockTransform;
            }
            return base.GetModelTransform(inst, index);
        }

        protected internal override int GetModelRecolor(ModelInstance inst, int index)
        {
            if (index == 1 || !Flags.TestFlag(DoorFlags.Locked))
            {
                return 0;
            }
            return Recolor;
        }
    }

    public class FhDoorEntity : EntityBase
    {
        private readonly FhDoorEntityData _data;

        public FhDoorEntity(FhDoorEntityData data, Scene scene) : base(EntityType.FhDoor, scene)
        {
            _data = data;
            Id = data.Header.EntityId;
            SetTransform(data.Header.FacingVector, data.Header.UpVector, data.Header.Position);
            ModelInstance inst = SetUpModel(Metadata.FhDoors[(int)data.ModelId], firstHunt: true);
            inst.SetAnimation(0, AnimFlags.Ended | AnimFlags.NoLoop);
        }
    }

    [Flags]
    public enum DoorFlags : ushort
    {
        None = 0,
        Loaded = 1,
        Locked = 2,
        Unlocked = 4,
        ShotOpen = 8,
        Opening = 0x10,
        ShouldOpen = 0x20,
        Open = 0x40,
        Closed = 0x80,
        Bit8 = 0x100,
        Bit9 = 0x200,
        Bit10 = 0x400,
        Bit11 = 0x800, // unused?
        ShowLock = 0x1000
    }
}
