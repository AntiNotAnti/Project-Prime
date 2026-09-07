using System;
using System.Buffers.Binary;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>
/// Loads a real AMHE1 multiplayer room and adds controlled force-field records
/// for the nine lock variants. The records are synthetic so the fixture covers
/// the variants even though no available multiplayer map contains all of them;
/// models, lock weapons and the entity damage path are still loaded from the
/// real AMHE1 content.
/// </summary>
internal static class SharedLockCheck
{
    private const int LockTypeCount = 9;

    public static int Run(string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            Console.Error.WriteLine("Usage: nettest --shared-lock DATA_DIRECTORY [VERSION]");
            return 2;
        }

        try
        {
            string version = args.Length == 3 ? args[2] : "AMHE1";
            ServerContent.Open(args[1], version);
            Scene scene = Scene.CreateHeadless();
            try
            {
                scene.LoadServerRoom("MP1 SANCTORUS", GameMode.Battle, players: 0,
                    roomPlayerCount: NetLaunch.RoomPlayerCount);
                for (int type = 0; type < LockTypeCount; type++)
                {
                    ForceFieldEntity field = new(CreateData(type), nodeName: null!, scene);
                    scene.AddEntity(field);
                    ForceFieldLockEntity fieldLock = field.Lock
                        ?? throw new InvalidOperationException($"Synthetic type {type} has no initialized lock.");
                    CheckLock(field, fieldLock, type, scene);
                }
            }
            finally
            {
                scene.CloseHeadless();
            }

            Console.WriteLine($"SHAREDLOCK PASS types={LockTypeCount} source=synthetic-records real-assets={version}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("SHAREDLOCK FAIL " + error);
            return 1;
        }
    }

    private static ForceFieldEntityData CreateData(int type)
    {
        // ForceFieldEntityData is a packed readonly wire record without a
        // public constructor. Build its reviewed layout explicitly rather
        // than adding a production-only test constructor.
        ForceFieldEntityData data = default;
        Span<byte> bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(
            System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref data, 1));
        EntityDataHeader header = new((ushort)EntityType.ForceField, (short)(2000 + type),
            new Vector3(type * 3, 0, 0), Vector3.UnitY, Vector3.UnitZ);
        System.Runtime.InteropServices.MemoryMarshal.Write(bytes, in header);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[40..44], (uint)type);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[44..48], Fixed.ToInt(2));
        BinaryPrimitives.WriteInt32LittleEndian(bytes[48..52], Fixed.ToInt(2));
        bytes[52] = 1;
        return data;
    }

    private static void CheckLock(ForceFieldEntity field, ForceFieldLockEntity fieldLock,
        int type, Scene scene)
    {
        Require(fieldLock.Owner == field, $"type {type} owner link");
        Require(fieldLock.GetTargetable(), $"type {type} starts targetable");
        Require(fieldLock.CanTakeAltAttackDamage == (type == 8), $"type {type} alt-attack rule");

        FieldInfo equipField = typeof(ForceFieldLockEntity).GetField("_equipInfo",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Force-field lock weapon state is unavailable.");
        EquipInfo equip = (EquipInfo)(equipField.GetValue(fieldLock)
            ?? throw new InvalidOperationException($"type {type} has no weapon state."));
        Require(ReferenceEquals(equip.Weapon, Weapons.ForceFieldLockWeapons[type]),
            $"type {type} uses its exact configured lock weapon");
        Require(equip.Beams.Length == 64, $"type {type} shares the lock projectile pool");

        for (int beam = 0; beam < 9; beam++)
        {
            Effectiveness expected = type < 8 && beam == type
                ? Effectiveness.Normal : Effectiveness.Zero;
            Require(fieldLock.GetEffectiveness((BeamType)beam) == expected,
                $"type {type} beam {(BeamType)beam} effectiveness");
        }

        if (type == 0)
        {
            fieldLock.SetHealth(2);
            fieldLock.TakeDamage(1, NewBeam(scene, BeamType.VoltDriver));
            Require(fieldLock.Health == 2, "type 0 wrong beam must not damage");
            fieldLock.SetHealth(2);
            fieldLock.TakeDamage(1, NewBeam(scene, BeamType.PowerBeam));
            Require(fieldLock.Health == 1, "type 0 right beam damage");
            fieldLock.SetHealth(2);
            BombEntity bomb = NewBomb(scene, fieldLock);
            Require(fieldLock.CheckHitByBomb(bomb), "type 0 bomb hit registration");
            Require(fieldLock.Health == 2, "type 0 bomb must not damage");
            fieldLock.SetHealth(1);
            fieldLock.TakeDamage(1, NewBeam(scene, BeamType.PowerBeam));
        }
        else if (type == 8)
        {
            fieldLock.SetHealth(2);
            fieldLock.TakeDamage(1, NewBeam(scene, BeamType.PowerBeam));
            Require(fieldLock.Health == 2, "type 8 beam must not damage");
            fieldLock.SetHealth(2);
            BombEntity bomb = NewBomb(scene, fieldLock);
            Require(fieldLock.CheckHitByBomb(bomb), "type 8 bomb hit registration");
            Require(fieldLock.Health == 1, "type 8 bomb damage");
            fieldLock.SetHealth(1);
            BombEntity lethalBomb = NewBomb(scene, fieldLock);
            Require(fieldLock.CheckHitByBomb(lethalBomb), "type 8 lethal bomb registration");
        }
        if (type is 0 or 8)
        {
            // A lethal hit dispatches Unlock immediately, so the lock and
            // barrier cannot both remain targetable during one frame.
            Require(fieldLock.Health == 0, $"type {type} lethal damage");
            Require(!field.Active && !fieldLock.GetTargetable(), $"type {type} same-frame unlock");
        }
    }

    private static BeamProjectileEntity NewBeam(Scene scene, BeamType beam)
    {
        return new BeamProjectileEntity(scene) { Beam = beam, BeamKind = beam };
    }

    private static BombEntity NewBomb(Scene scene, ForceFieldLockEntity fieldLock)
    {
        return new BombEntity(scene)
        {
            Position = fieldLock.Position,
            Radius = 1f,
            EnemyDamage = 1
        };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
