using System;
using System.Collections.Generic;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;

namespace MphRead.Utility
{
    public class CollisionDataEditor
    {
        public List<Vector3> Points { get; } = new List<Vector3>();
        public Vector4 Plane { get; set; }
        public ushort LayerMask { get; set; }

        public bool Damaging { get => Check(CollisionFlags.Damaging); set => Update(CollisionFlags.Damaging, value); }
        public bool Reflect { get => Check(CollisionFlags.ReflectBeams); set => Update(CollisionFlags.ReflectBeams, value); }
        public bool Players { get => Check(CollisionFlags.IgnorePlayers); set => Update(CollisionFlags.IgnorePlayers, !value); }
        public bool Beams { get => Check(CollisionFlags.IgnoreBeams); set => Update(CollisionFlags.IgnoreBeams, !value); }
        public bool Scan { get => Check(CollisionFlags.IgnoreScan); set => Update(CollisionFlags.IgnoreScan, !value); }

        public int Slipperiness
        {
            get
            {
                return ((ushort)Flags & 0x18) >> 3;
            }
            set
            {
                if (value < 0 || value > 3)
                {
                    throw new ProgramException($"Invalid slipperiness value {value}.");
                }
                Flags = (CollisionFlags)((ushort)Flags & 0xFFE7 | (value << 3));
            }
        }

        public Terrain Terrain
        {
            get
            {
                return (Terrain)(((ushort)Flags & 0x1E0) >> 5);
            }
            set
            {
                if (!Enum.IsDefined(typeof(Terrain), value))
                {
                    throw new ProgramException($"Invalid terrain type {value}.");
                }
                Flags = (CollisionFlags)((ushort)Flags & 0xFE1F | ((byte)value << 5));
            }
        }

        public CollisionFlags Flags { get; set; }

        private bool Check(CollisionFlags flag)
        {
            return Flags.TestFlag(flag);
        }

        private void Update(CollisionFlags flag, bool value)
        {
            if (value)
            {
                Flags |= flag;
            }
            else
            {
                Flags &= ~flag;
            }
        }
    }
}
