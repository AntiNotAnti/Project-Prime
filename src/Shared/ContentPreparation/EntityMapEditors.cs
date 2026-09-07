using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Editor
{
    public abstract class EntityEditorBase
    {
        public EntityType Type { get; set; }
        public short Id { get; set; }
        public ushort LayerMask { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Up { get; set; }
        public Vector3 Facing { get; set; }
        public string NodeName { get; set; } = "";

        public EntityEditorBase(EntityType type)
        {
            Type = type;
        }

        public EntityEditorBase(Entity header)
        {
            Type = header.Type;
            Id = header.EntityId;
            LayerMask = header.LayerMask;
            Position = header.Position;
            Up = header.UpVector;
            Facing = header.FacingVector;
            NodeName = header.NodeName;
        }

        protected void PrintValue(string value1, string value2, string name)
        {
            if (value1 != value2)
            {
                Console.WriteLine($"{name}: {value1} / {value2}");
            }
        }

        protected void PrintValue<T>(T value1, T value2, string name) where T : struct
        {
            if (!value1.Equals(value2))
            {
                Console.WriteLine($"{name}: {value1} / {value2}");
            }
        }

        protected void PrintValues<T>(List<T> value1, List<T> value2, string name) where T : struct
        {
            if (value1.Count != value2.Count)
            {
                Console.WriteLine($"{name}: {value1.Count} / {value2.Count}");
                for (int i = 0; i < Math.Max(value1.Count, value2.Count); i++)
                {
                    Console.WriteLine($"{(i < value1.Count ? value1[i].ToString() : "N/A")} / {(i < value2.Count ? value2[i].ToString() : "N/A")}");
                }
            }
            else if (!Enumerable.SequenceEqual(value1, value2))
            {
                for (int i = 0; i < value1.Count; i++)
                {
                    Console.WriteLine($"{value1[i]} / {value2[i]}");
                }
            }
        }
    }

    public class PlayerSpawnEntityEditor : EntityEditorBase
    {
        public byte Availability { get; set; } // 0 - any time, 1 - no first frame, 2 - bot only (FH)
        public bool Active { get; set; } = true;
        public sbyte TeamIndex { get; set; } = -1; // 0, 1, or -1 (only used in CTF)

        public PlayerSpawnEntityEditor() : base(EntityType.PlayerSpawn)
        {
        }

        public PlayerSpawnEntityEditor(Entity header, PlayerSpawnEntityData raw) : base(header)
        {
            Availability = raw.Availability;
            Active = raw.Active != 0;
            TeamIndex = raw.TeamIndex;
        }

        public void CompareTo(PlayerSpawnEntityEditor other)
        {
            PrintValue(Availability, other.Availability, nameof(Availability));
            PrintValue(Active, other.Active, nameof(Active));
            PrintValue(TeamIndex, other.TeamIndex, nameof(TeamIndex));
        }
    }

    public class ItemSpawnEntityEditor : EntityEditorBase
    {
        public int ParentId { get; set; }
        public ItemType ItemType { get; set; }
        public bool Enabled { get; set; }
        public bool HasBase { get; set; }
        public bool AlwaysActive { get; set; } // set flags bit 0 based on Active boolean only and ignore room state
        public ushort MaxSpawnCount { get; set; }
        public ushort SpawnInterval { get; set; }
        public ushort SpawnDelay { get; set; }
        public short NotifyEntityId { get; set; } // todo: parent? child?
        public Message CollectedMessage { get; set; }
        public int CollectedMsgParam1 { get; set; }
        public int CollectedMsgParam2 { get; set; }

        public ItemSpawnEntityEditor() : base(EntityType.ItemSpawn)
        {
        }

        public ItemSpawnEntityEditor(Entity header, ItemSpawnEntityData raw) : base(header)
        {
            ParentId = raw.ParentId;
            ItemType = raw.ItemType;
            Enabled = raw.Enabled != 0;
            HasBase = raw.HasBase != 0;
            AlwaysActive = raw.AlwaysActive != 0;
            MaxSpawnCount = raw.MaxSpawnCount;
            SpawnInterval = raw.SpawnInterval;
            SpawnDelay = raw.SpawnDelay;
            NotifyEntityId = raw.NotifyEntityId;
            CollectedMessage = raw.CollectedMessage;
            CollectedMsgParam1 = raw.CollectedMsgParam1;
            CollectedMsgParam2 = raw.CollectedMsgParam2;
        }

        public void CompareTo(ItemSpawnEntityEditor other)
        {
            PrintValue(ParentId, other.ParentId, nameof(ParentId));
            PrintValue(ItemType, other.ItemType, nameof(ItemType));
            PrintValue(Enabled, other.Enabled, nameof(Enabled));
            PrintValue(HasBase, other.HasBase, nameof(HasBase));
            PrintValue(AlwaysActive, other.AlwaysActive, nameof(AlwaysActive));
            PrintValue(MaxSpawnCount, other.MaxSpawnCount, nameof(MaxSpawnCount));
            PrintValue(SpawnInterval, other.SpawnInterval, nameof(SpawnInterval));
            PrintValue(SpawnDelay, other.SpawnDelay, nameof(SpawnDelay));
            PrintValue(NotifyEntityId, other.NotifyEntityId, nameof(NotifyEntityId));
            PrintValue(CollectedMessage, other.CollectedMessage, nameof(CollectedMessage));
            PrintValue(CollectedMsgParam1, other.CollectedMsgParam1, nameof(CollectedMsgParam1));
            PrintValue(CollectedMsgParam2, other.CollectedMsgParam2, nameof(CollectedMsgParam2));
        }
    }

    public class JumpPadEntityEditor : EntityEditorBase
    {
        public int ParentId { get; set; }
        public uint Unused28 { get; set; } // usually 0, occasionally 2
        public CollisionVolume Volume { get; set; }
        public Vector3 BeamVector { get; set; }
        public float Speed { get; set; }
        public ushort ControlLockTime { get; set; }
        public ushort CooldownTime { get; set; }
        public bool Active { get; set; }
        public uint ModelId { get; set; }
        public uint BeamType { get; set; }
        public TriggerFlags TriggerFlags { get; set; }

        public JumpPadEntityEditor() : base(EntityType.JumpPad)
        {
        }

        public JumpPadEntityEditor(Entity header, JumpPadEntityData raw) : base(header)
        {
            ParentId = raw.ParentId;
            Unused28 = raw.Unused28;
            Volume = new CollisionVolume(raw.Volume);
            BeamVector = raw.BeamVector.ToFloatVector();
            Speed = raw.Speed.FloatValue;
            ControlLockTime = raw.ControlLockTime;
            CooldownTime = raw.CooldownTime;
            Active = raw.Active != 0;
            ModelId = raw.ModelId;
            BeamType = raw.BeamType;
            TriggerFlags = raw.TriggerFlags;
        }

        public void CompareTo(JumpPadEntityEditor other)
        {
            PrintValue(ParentId, other.ParentId, nameof(ParentId));
            PrintValue(Unused28, other.Unused28, nameof(Unused28));
            PrintValue(Volume, other.Volume, nameof(Volume));
            PrintValue(BeamVector, other.BeamVector, nameof(BeamVector));
            PrintValue(Speed, other.Speed, nameof(Speed));
            PrintValue(ControlLockTime, other.ControlLockTime, nameof(ControlLockTime));
            PrintValue(CooldownTime, other.CooldownTime, nameof(CooldownTime));
            PrintValue(Active, other.Active, nameof(Active));
            PrintValue(ModelId, other.ModelId, nameof(ModelId));
            PrintValue(BeamType, other.BeamType, nameof(BeamType));
            PrintValue(TriggerFlags, other.TriggerFlags, nameof(TriggerFlags));
        }
    }
}
