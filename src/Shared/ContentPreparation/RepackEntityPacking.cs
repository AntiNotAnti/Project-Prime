using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using MphRead.Editor;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Utility
{
    public static partial class Repack
    {
        private static void WritePlayerSpawn(PlayerSpawnEntityEditor entity, BinaryWriter writer)
        {
            writer.Write(entity.Availability);
            writer.WriteByte(entity.Active);
            writer.Write(entity.TeamIndex);
        }

        private static void WriteMphItemSpawn(ItemSpawnEntityEditor entity, BinaryWriter writer)
        {
            byte padByte = 0;
            writer.Write(entity.ParentId);
            writer.Write((uint)entity.ItemType);
            writer.WriteByte(entity.Enabled);
            writer.WriteByte(entity.HasBase);
            writer.WriteByte(entity.AlwaysActive);
            writer.Write(padByte); // Padding2F
            writer.Write(entity.MaxSpawnCount);
            writer.Write(entity.SpawnInterval);
            writer.Write(entity.SpawnDelay);
            writer.Write(entity.NotifyEntityId);
            writer.Write((uint)entity.CollectedMessage);
            writer.Write(entity.CollectedMsgParam1);
            writer.Write(entity.CollectedMsgParam2);
        }

        private static void WriteMphJumpPad(JumpPadEntityEditor entity, BinaryWriter writer)
        {
            byte padByte = 0;
            ushort padShort = 0;
            writer.Write(entity.ParentId);
            writer.Write(entity.Unused28);
            writer.WriteVolume(entity.Volume);
            writer.WriteVector3(entity.BeamVector);
            writer.WriteFloat(entity.Speed);
            writer.Write(entity.ControlLockTime);
            writer.Write(entity.CooldownTime);
            writer.WriteByte(entity.Active);
            writer.Write(padByte); // Padding81
            writer.Write(padShort); // Padding82
            writer.Write(entity.ModelId);
            writer.Write(entity.BeamType);
            writer.Write((uint)entity.TriggerFlags);
        }

        public static void WriteVolume(this BinaryWriter writer, CollisionVolume volume)
        {
            uint padInt = 0;
            Debug.Assert(Enum.IsDefined(typeof(VolumeType), volume.Type));
            writer.Write((uint)volume.Type);
            if (volume.Type == VolumeType.Box)
            {
                writer.WriteVector3(volume.BoxVector1);
                writer.WriteVector3(volume.BoxVector2);
                writer.WriteVector3(volume.BoxVector3);
                writer.WriteVector3(volume.BoxPosition);
                writer.WriteFloat(volume.BoxDot1);
                writer.WriteFloat(volume.BoxDot2);
                writer.WriteFloat(volume.BoxDot3);
            }
            else if (volume.Type == VolumeType.Cylinder)
            {
                writer.WriteVector3(volume.CylinderVector);
                writer.WriteVector3(volume.CylinderPosition);
                writer.WriteFloat(volume.CylinderRadius);
                writer.WriteFloat(volume.CylinderDot);
                for (int i = 0; i < 7; i++)
                {
                    writer.Write(padInt);
                }
            }
            else if (volume.Type == VolumeType.Sphere)
            {
                writer.WriteVector3(volume.SpherePosition);
                writer.WriteFloat(volume.SphereRadius);
                for (int i = 0; i < 11; i++)
                {
                    writer.Write(padInt);
                }
            }
        }

        private static byte[] PackEntityRecords(IReadOnlyList<EntityEditorBase> entities,
            Action<EntityEditorBase> validate, Func<EntityEditorBase, BinaryWriter, int> writeEntity)
        {
            byte padByte = 0;
            ushort padShort = 0;
            uint padInt = 0;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            // header
            uint version = 2;
            ushort[] lengths = new ushort[16];
            foreach (EntityEditorBase entity in entities)
            {
                validate(entity);
                for (int i = 0; i < 16; i++)
                {
                    if ((entity.LayerMask & (1 << i)) != 0)
                    {
                        lengths[i]++;
                    }
                }
            }
            writer.Write(version);
            foreach (ushort length in lengths)
            {
                writer.Write(length);
            }
            Debug.Assert(stream.Position == Sizes.EntityHeader);
            // entity data
            stream.Position += Sizes.EntityEntry * (entities.Count + 1);
            var results = new List<(int, int)>();
            for (int i = 0; i < entities.Count; i++)
            {
                EntityEditorBase entity = entities[i];
                int offset = (int)stream.Position;
                int size = writeEntity(entity, writer);
                results.Add((offset, size));
                if (i < entities.Count - 1)
                {
                    while (stream.Position % 4 != 0)
                    {
                        writer.Write(padByte);
                    }
                }
            }
            // entity entries
            stream.Position = Sizes.EntityHeader;
            for (int i = 0; i < entities.Count; i++)
            {
                EntityEditorBase entity = entities[i];
                (int offset, int size) = results[i];
                writer.WriteString(entity.NodeName, 16);
                writer.Write(entity.LayerMask);
                writer.Write((ushort)size);
                writer.Write(offset);
            }
            // entry terminator
            writer.WriteString("", 16);
            writer.Write(padShort);
            writer.Write(padShort);
            writer.Write(padInt);
            return stream.ToArray();
        }

        public static byte[] PackEntities(IReadOnlyList<EntityEditorBase> entities)
        {
            return PackEntityRecords(entities, entity =>
            {
                if (entity.Id < 0)
                {
                    throw new ProgramException("File entities must have a positive entity ID.");
                }
                if (entity is not PlayerSpawnEntityEditor && entity is not ItemSpawnEntityEditor
                    && entity is not JumpPadEntityEditor)
                {
                    throw new ProgramException($"Unsupported generated map entity type {entity.Type}.");
                }
            }, WriteMapEntity);
        }

        private static int WriteMapEntity(EntityEditorBase entity, BinaryWriter writer)
        {
            long position = writer.BaseStream.Position;
            writer.Write((ushort)entity.Type);
            writer.Write(entity.Id);
            writer.WriteVector3(entity.Position);
            writer.WriteVector3(entity.Up);
            writer.WriteVector3(entity.Facing);
            switch (entity)
            {
            case PlayerSpawnEntityEditor spawn:
                WritePlayerSpawn(spawn, writer);
                break;
            case ItemSpawnEntityEditor item:
                WriteMphItemSpawn(item, writer);
                break;
            case JumpPadEntityEditor pad:
                WriteMphJumpPad(pad, writer);
                break;
            }
            return (int)(writer.BaseStream.Position - position);
        }
    }
}
