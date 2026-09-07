using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace MphRead
{
    // size: 400 (256 + 36 * 4)
    public readonly struct EnemySpawnFields00
    {
        public readonly RawCollisionVolume Volume0;
        public readonly RawCollisionVolume Volume1;
        public readonly RawCollisionVolume Volume2;
        public readonly RawCollisionVolume Volume3;
    }

    // size: 400
    public readonly struct EnemySpawnFields01
    {
        public readonly EnemySpawnFieldsWW WarWasp;
        public readonly uint Padding1B0;
        public readonly uint Padding1B4;
    }

    // size: 400 (204 + 49 * 4)
    public readonly struct EnemySpawnFields02
    {
        public readonly RawCollisionVolume Volume0;
        public readonly Vector3Fx PathVector;
        public readonly RawCollisionVolume Volume1;
        public readonly RawCollisionVolume Volume2;
    }

    // size: 400 (128 + 68 * 4)
    public readonly struct EnemySpawnFields03
    {
        public readonly RawCollisionVolume Volume0;
        public readonly uint Unused68;
        public readonly uint Unused6C;
        public readonly uint Unused70;
        public readonly uint Unused74;
        public readonly uint Unused78;
        public readonly uint Unused7C;
        public readonly uint Unused80;
        public readonly Vector3Fx Facing;
        public readonly Vector3Fx Position;
        public readonly Vector3Fx IdleRange;
    }

    // size: 400 (100 + 75 * 4)
    public readonly struct EnemySpawnFields04
    {
        public readonly RawCollisionVolume Volume0;
        public readonly uint Unused68;
        public readonly uint Unused6C;
        public readonly uint Unused70;
        public readonly uint Unused74;
        public readonly Vector3Fx Position;
        public readonly int WeaveOffset;
        public readonly int Field88;
    }

    // size: 400 (260 + 35 * 4)
    public readonly struct EnemySpawnFields05
    {
        public readonly uint EnemySubtype;
        public readonly RawCollisionVolume Volume0;
        public readonly RawCollisionVolume Volume1;
        public readonly RawCollisionVolume Volume2;
        public readonly RawCollisionVolume Volume3;
    }

    // size: 400 (264 + 34 * 4)
    public readonly struct EnemySpawnFields06
    {
        public readonly uint EnemySubtype;
        public readonly uint EnemyVersion;
        public readonly RawCollisionVolume Volume0;
        public readonly RawCollisionVolume Volume1;
        public readonly RawCollisionVolume Volume2;
        public readonly RawCollisionVolume Volume3;
    }

    // size: 400 (72 + 82 * 4)
    public readonly struct EnemySpawnFields07
    {
        public readonly ushort EnemyHealth;
        public readonly ushort EnemyDamage;
        public readonly uint EnemySubtype;
        public readonly RawCollisionVolume Volume0; // unused
    }

    // size: 400
    public readonly struct EnemySpawnFields08
    {
        public readonly uint EnemySubtype;
        public readonly uint EnemyVersion;
        public readonly EnemySpawnFieldsWW WarWasp;
    }

    // size: 400 (20 + 95 * 4)
    public readonly struct EnemySpawnFields09
    {
        public readonly uint HunterId;
        public readonly uint EncounterType;
        public readonly uint HunterWeapon;
        public readonly ushort HunterHealth;
        public readonly ushort HunterHealthMax;
        public readonly ushort HunterHealthThreshold;
        public readonly byte HunterColor;
        public readonly byte HunterChance;
    }

    // size: 400 (140 + 65 * 4)
    public readonly struct EnemySpawnFields10
    {
        public readonly uint EnemySubtype;
        public readonly uint EnemyVersion;
        public readonly RawCollisionVolume Volume0;
        public readonly RawCollisionVolume Volume1;
        public readonly int Index;
    }

    // size: 400 (32 + 92 * 4)
    public readonly struct EnemySpawnFields11
    {
        public readonly Vector3Fx Sphere1Position;
        public readonly Fixed Sphere1Radius;
        public readonly Vector3Fx Sphere2Position;
        public readonly Fixed Sphere2Radius;
    }

    // size: 400 (20 + 95 * 4)
    public readonly struct EnemySpawnFields12
    {
        public readonly Vector3Fx Field28;
        public readonly Fixed Field34;
        public readonly Fixed Field38;
    }

    // size: 400
    public readonly struct EnemySpawnFieldsWW
    {
        public readonly RawCollisionVolume Volume0;
        public readonly RawCollisionVolume Volume1;
        public readonly RawCollisionVolume Volume2;
        public readonly Vector3FxArray16 MovementVectors;
        public readonly byte PositionCount;
        public readonly byte Padding1A9;
        public readonly ushort Padding1AA;
        public readonly uint MovementType;
    }

    // size: 400
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct EnumSpawnUnion
    {
        [FieldOffset(0)]
        public readonly EnemySpawnFields00 S00;
        [FieldOffset(0)]
        public readonly EnemySpawnFields01 S01;
        [FieldOffset(0)]
        public readonly EnemySpawnFields02 S02;
        [FieldOffset(0)]
        public readonly EnemySpawnFields03 S03;
        [FieldOffset(0)]
        public readonly EnemySpawnFields04 S04;
        [FieldOffset(0)]
        public readonly EnemySpawnFields05 S05;
        [FieldOffset(0)]
        public readonly EnemySpawnFields06 S06;
        [FieldOffset(0)]
        public readonly EnemySpawnFields07 S07;
        [FieldOffset(0)]
        public readonly EnemySpawnFields08 S08;
        [FieldOffset(0)]
        public readonly EnemySpawnFields09 S09;
        [FieldOffset(0)]
        public readonly EnemySpawnFields10 S10;
        [FieldOffset(0)]
        public readonly EnemySpawnFields11 S11;
        [FieldOffset(0)]
        public readonly EnemySpawnFields12 S12;
    }

    // size: 512
    public readonly struct EnemySpawnEntityData
    {
        public readonly EntityDataHeader Header;
        public readonly EnemyType EnemyType;
        public readonly byte Padding25; // in-game, the type is 4 bytes on this struct (but is 1 byte on the class),
        public readonly ushort Padding26; // so this padding isn't actually there
        public readonly EnumSpawnUnion Fields;
        public readonly short LinkedEntityId; // always -1 except for Cretaphid 4
        public readonly byte SpawnTotal; // max enemy instances to spawn overall
        public readonly byte SpawnLimit; // max concurrent enemy instances
        public readonly byte SpawnCount; // number of instances spawned at once
        public readonly byte Active; // boolean
        public readonly byte AlwaysActive; // boolean
        public readonly byte ItemChance;
        public readonly ushort SpawnerHealth;
        public readonly ushort CooldownTime;
        public readonly ushort InitialCooldown;
        public readonly ushort Padding1C6;
        public readonly Fixed ActiveDistance; // todo: display spheres
        public readonly Fixed EnemyActiveDistance;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public readonly char[] NodeName;
        public readonly short EntityId1;
        public readonly ushort Padding1E2;
        public readonly Message Message1;
        public readonly short EntityId2;
        public readonly ushort Padding1EA;
        public readonly Message Message2;
        public readonly short EntityId3;
        public readonly ushort Padding1F2;
        public readonly Message Message3;
        public readonly ItemType ItemType;
    }

    // size: 268
    public readonly struct FhEnemySpawnEntityData
    {
        public readonly EntityDataHeader Header;
        public readonly FhRawCollisionVolume Box; // used by Mochtroid1 and Metroid
        public readonly FhRawCollisionVolume Cylinder; // used by Mochtroid2/3/4
        public readonly FhRawCollisionVolume Sphere; // used by Zoomer
        public readonly FhEnemyType EnemyType;
        public readonly byte SpawnTotal;
        public readonly byte SpawnLimit;
        public readonly byte SpawnCount;
        public readonly byte PaddingEB;
        public readonly ushort Cooldown;
        public readonly ushort StartFrame;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public readonly char[] NodeName;
        public readonly short ParentId;
        public readonly ushort Padding102;
        public readonly FhMessage EmptyMessage;
    }
}
