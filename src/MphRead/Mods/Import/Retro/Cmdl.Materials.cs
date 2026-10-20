using System;
using System.Buffers.Binary;
using System.IO;

namespace MphRead.Mods.Import.Retro;

public sealed partial class Cmdl
{
    // Bounded GX material profile shared by character CMDL and world MREA.
    // Source array extents are proved before allocation, independently of
    // the size of the enclosing resource or following material records.
    public const int MaxMaterialBytes = 1024 * 1024;
    public const int MaxMaterialTextureSlots = 8;
    public const int MaxMaterialKonstColors = 4;
    public const int MaxMaterialColorChannels = 2;
    public const int MaxMaterialTevStages = 16;
    public const int MaxMaterialTexGens = 8;
    public const int MaxMaterialUvAnimations = 8;

    public static Material ReadMaterial(byte[] bytes, int start, int end, bool echoes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (start < 0 || end < start || end > bytes.Length || end - start > MaxMaterialBytes)
            throw new InvalidDataException("Retro material range exceeds its bounds.");
        var reader = new MaterialReader(bytes.AsSpan(start, end - start));
        var material = new Material { Flags = reader.U32() };
        int count = reader.Count(MaxMaterialTextureSlots, 4, "texture slots");
        material.Tex = new int[count];
        for (int i = 0; i < count; i++)
        {
            uint index = reader.U32();
            if (index > int.MaxValue) throw new InvalidDataException("Retro material texture index exceeds its range.");
            material.Tex[i] = (int)index;
        }
        material.VertexAttributes = reader.U32();
        if (echoes) material.EchoesUnknown = new[] { reader.U32(), reader.U32() };
        material.Group = reader.U32();
        if ((material.Flags & Konst) != 0)
        {
            count = reader.Count(MaxMaterialKonstColors, 4, "konst colors");
            material.Konst = new byte[count][];
            for (int i = 0; i < count; i++) material.Konst[i] = reader.Take(4).ToArray();
        }
        material.BlendDst = reader.U16(); material.BlendSrc = reader.U16();
        if ((material.Flags & IndStage) != 0) material.IndTex = reader.U32();
        count = reader.Count(MaxMaterialColorChannels, 4, "color channels");
        material.Channels = new uint[count];
        for (int i = 0; i < count; i++) material.Channels[i] = reader.U32();
        material.Stages = ReadMaterialStages(ref reader);
        count = reader.Count(MaxMaterialTexGens, 4, "texture generators");
        material.TexGens = new TexGen[count];
        for (int i = 0; i < count; i++)
        {
            uint packed = reader.U32();
            material.TexGens[i] = new TexGen
            {
                Type = (int)packed & 0xf, Src = (int)(packed >> 4) & 0x1f,
                Mtx = (int)(packed >> 9) & 0x1f, Normalize = (packed & 0x4000) != 0,
                Post = (int)(packed >> 15) & 0x3f
            };
        }
        uint animationBytes = reader.U32();
        if (animationBytes > int.MaxValue)
            throw new InvalidDataException("Retro UV animation block exceeds its range.");
        var animation = new MaterialReader(reader.Take((int)animationBytes));
        count = animation.Count(MaxMaterialUvAnimations, 4, "UV animations");
        material.UvAnims = new UvAnim[count];
        for (int i = 0; i < count; i++)
        {
            uint mode = animation.U32();
            int parameters = mode switch { 2 => 4, 3 => 2, 4 => 4, 5 => 4, 7 => 2, 8 => 9, _ => 0 };
            animation.Require(parameters * 4);
            var values = new double[parameters];
            for (int j = 0; j < values.Length; j++)
            {
                float value = animation.F32();
                if (!float.IsFinite(value)) throw new InvalidDataException("Retro UV animation contains a nonfinite parameter.");
                values[j] = value;
            }
            material.UvAnims[i] = new UvAnim { Mode = mode, Params = values };
        }
        if (animation.Remaining != 0)
            throw new InvalidDataException("Retro UV animation block has unexpected trailing data.");
        // Existing source records may have opaque alignment/tail bytes inside
        // their material extent. They are not interpreted or executed here.
        return material;
    }

    private static TevStage[] ReadMaterialStages(ref MaterialReader reader)
    {
        // All 20-byte stage bodies precede their 4-byte texture bindings.
        int count = reader.Count(MaxMaterialTevStages, 24, "TEV stages");
        var stages = new TevStage[count];
        for (int i = 0; i < count; i++)
        {
            uint color = reader.U32(), alpha = reader.U32(), colorOp = reader.U32(), alphaOp = reader.U32();
            reader.U8(); // opaque stage byte
            var stage = new TevStage
            {
                ColorOp = TevOp.From(colorOp), AlphaOp = TevOp.From(alphaOp),
                RawColorOp = colorOp, RawAlphaOp = alphaOp,
                KAlpha = reader.U8(), KColor = reader.U8(), Ras = reader.U8()
            };
            for (int k = 0; k < 4; k++)
            {
                stage.ColorIn[k] = (int)(color >> (5 * k)) & 0x1f;
                stage.AlphaIn[k] = (int)(alpha >> (5 * k)) & 0x1f;
            }
            stages[i] = stage;
        }
        for (int i = 0; i < count; i++)
        {
            reader.Take(2); // opaque binding bytes
            stages[i].Tex = reader.U8(); stages[i].TexCoord = reader.U8();
        }
        return stages;
    }

    private ref struct MaterialReader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _at;
        internal MaterialReader(ReadOnlySpan<byte> bytes) { _bytes = bytes; _at = 0; }
        internal int Remaining => _bytes.Length - _at;
        internal void Require(int count)
        {
            if (count < 0 || count > Remaining)
                throw new InvalidDataException("Truncated Retro material record.");
        }
        internal ReadOnlySpan<byte> Take(int count)
        {
            Require(count); var value = _bytes.Slice(_at, count); _at += count; return value;
        }
        internal byte U8() => Take(1)[0];
        internal ushort U16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));
        internal uint U32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4));
        internal float F32() => BinaryPrimitives.ReadSingleBigEndian(Take(4));
        internal int Count(int maximum, int stride, string kind)
        {
            uint count = U32();
            if (count > maximum || (long)count * stride > Remaining)
                throw new InvalidDataException("Retro material " + kind + " exceed their count or byte bounds.");
            return (int)count;
        }
    }
}
