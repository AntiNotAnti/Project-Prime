using System;
using System.IO;
using System.Reflection;
using MphRead;
using MphRead.Editor;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Utility;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Initial native actor construction only. These helpers must run before the
// player is initially placed/settled and before keyboard commands begin.
internal static class ParkourGrappleFixtures
{
    private static byte[] Serialize(EntityEditorBase editor)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        typeof(Repack).GetMethod("WriteEntity", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { editor, writer });
        return stream.ToArray();
    }

    internal static ForceFieldEntity AddFieldBeforeSafeWall(Scene scene)
    {
        var editor = new ForceFieldEntityEditor { Id = 31002, ForceFieldType = 9,
            Position = new(-42, 1.4f, 32), Up = Vector3.UnitY, Facing = -Vector3.UnitZ,
            Width = 2, Height = 2, Active = true };
        var field = new ForceFieldEntity(Read.ReadStruct<ForceFieldEntityData>(Serialize(editor)), "", scene);
        scene.AddEntity(field);
        if (!field.Active) throw new InvalidOperationException("Native field fixture did not become active");
        return field;
    }

    internal static PlatformEntity AddPlatformBeforeSafeWall(Scene scene)
    {
        var editor = new PlatformEntityEditor { Id = 31003, ModelId = 0, ParentId = -1,
            // Model0 is a horizontal platform with a thin collision deck.
            // Rotate that real native deck upright so the horizontal source
            // ray crosses its front face rather than passing above the deck.
            Position = new(-42, 1.4f, 32), Up = -Vector3.UnitZ, Facing = Vector3.UnitY,
            Active = true, PositionCount = 1, ForwardSpeed = 0, BackwardSpeed = 0,
            Flags = PlatformFlags.NoRecoil, BeamId = -1, BeamHitMsgTarget = -1,
            PlayerColMsgTarget = -1, DeadMsgTarget = -1, ScanMsgTarget = -1,
            PositionOffset = Vector3.Zero, ReverseType = 0 };
        for (int i = 0; i < 10; i++)
        { editor.Positions.Add(editor.Position); editor.Rotations.Add(new(-.70710678f, 0, 0, .70710678f)); }
        var platform = new PlatformEntity(Read.ReadStruct<PlatformEntityData>(Serialize(editor)), "", scene);
        scene.AddEntity(platform);
        if (platform.EntityCollision[0]?.Collision == null)
            throw new InvalidOperationException("Native platform fixture supplied no collision");
        return platform;
    }
}

