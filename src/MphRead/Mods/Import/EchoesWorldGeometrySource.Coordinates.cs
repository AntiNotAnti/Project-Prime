using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.Import.Retro;

namespace MphRead.Mods.Import;

public static partial class EchoesWorldGeometrySource
{
    public const string CoordinateContract = "echoes-base-float-t1-lightmap-s16-t0-v1";
    public const int MaxSourceCornerVisits = 2_000_000;
    public const int MaxSourceUvPairsPerPool = 2_000_000;
    public const long MaxSourceUvPoolBytes = 64L * 1024 * 1024;
    public const long MaxRetainedSourceCoordinateBytes = 32L * 1024 * 1024;

    // This identifies a decoded source-program candidate for coordinate
    // preservation. It is deliberately not renderer/material qualification.
    private static bool IsCommonOpaqueCandidate(Cmdl.Material material)
    {
        if (material.Flags != 0x36882 || material.Tex.Length != 2
            || material.BlendSrc != 1 || material.BlendDst != 0
            || !material.Channels.SequenceEqual(new uint[] { 0x3001 })
            || material.Konst.Length != 0 || material.IndTex != null
            || material.UvAnims.Length != 0 || material.Stages.Length != 2
            || material.TexGens.Length != 2)
            return false;
        bool Stage(Cmdl.TevStage stage, int index, int[] color, int[] alpha, uint operation)
            => stage.ColorIn.SequenceEqual(color) && stage.AlphaIn.SequenceEqual(alpha)
                && stage.RawColorOp == operation && stage.RawAlphaOp == operation
                && stage.Tex == index && stage.TexCoord == index
                && stage.KColor == 0 && stage.KAlpha == 0 && stage.Ras == 4;
        bool Generator(Cmdl.TexGen generator, int source)
            => generator.Type == 0 && generator.Src == source && generator.Mtx == 30
                && generator.Post == 61 && !generator.Normalize;
        return Stage(material.Stages[0], 0, new[] { 15, 4, 8, 10 }, new[] { 7, 7, 7, 6 }, 0x300)
            && Stage(material.Stages[1], 1, new[] { 15, 2, 8, 15 }, new[] { 7, 7, 7, 4 }, 0x100)
            && Generator(material.TexGens[0], 4) && Generator(material.TexGens[1], 5);
    }

    private sealed class CoordinateCapture
    {
        internal EchoesSourceMaterial[] Materials = Array.Empty<EchoesSourceMaterial>();
        internal readonly List<EchoesSourceTriangle> Triangles = new();
        internal int CornerVisits;
        internal int CandidateCornerVisits;
        private long _poolBytes;

        internal SourceUvPools AdmitPools(ReadOnlyMemory<byte> floats, ReadOnlyMemory<byte> shorts)
        {
            int floatPairs = floats.Length / 8, shortPairs = shorts.Length / 4;
            if (floatPairs > MaxSourceUvPairsPerPool || shortPairs > MaxSourceUvPairsPerPool)
                throw new InvalidDataException("Echoes source UV pool exceeds its pair budget.");
            _poolBytes = checked(_poolBytes + (long)floatPairs * 8 + (long)shortPairs * 4);
            if (_poolBytes > MaxSourceUvPoolBytes)
                throw new InvalidDataException("Echoes source UV pools exceed their aggregate budget.");
            return new(floats, shorts);
        }

        internal void AdmitCorners(int count)
        {
            if (count > MaxSourceCornerVisits - CornerVisits)
                throw new InvalidDataException("Echoes source corner visits exceed their budget.");
            CornerVisits += count;
        }
    }

    private sealed class SourceUvPools
    {
        private readonly ReadOnlyMemory<byte> _floats, _shorts;
        internal SourceUvPools(ReadOnlyMemory<byte> floats, ReadOnlyMemory<byte> shorts)
        { _floats = floats; _shorts = shorts; }

        internal EchoesSourceCoordinates Read(int shortIndex, int floatIndex)
        {
            if ((uint)shortIndex >= _shorts.Length / 4 || (uint)floatIndex >= _floats.Length / 8)
                throw new InvalidDataException("Echoes source UV index exceeds its model-local pool.");
            var pair = _floats.Span.Slice(floatIndex * 8, 8);
            float u = BinaryPrimitives.ReadSingleBigEndian(pair), v = BinaryPrimitives.ReadSingleBigEndian(pair[4..]);
            if (!float.IsFinite(u) || !float.IsFinite(v))
                throw new InvalidDataException("Referenced Echoes base UV is not finite.");
            var lightmap = _shorts.Span.Slice(shortIndex * 4, 4);
            return new(u, v, BinaryPrimitives.ReadInt16BigEndian(lightmap) / 32768f,
                BinaryPrimitives.ReadInt16BigEndian(lightmap[2..]) / 32768f);
        }
    }
}

/// <summary>Known decoded source-program candidate, not an activated shader.
/// The hash retains identity of opaque/uninterpreted material bytes too.</summary>
public sealed record EchoesSourceMaterial(int Ordinal, string ProgramSha256,
    bool CommonOpaqueCandidate, uint? BaseResourceId, uint? LightmapResourceId);

public readonly record struct EchoesSourceCoordinates(float BaseU, float BaseV, float LightmapU, float LightmapV);
public readonly record struct EchoesSourceUvTriangle(EchoesSourceCoordinates A,
    EchoesSourceCoordinates B, EchoesSourceCoordinates C);
public readonly record struct EchoesSourceTriangle(int MaterialOrdinal, EchoesSourceUvTriangle? Coordinates);

/// <summary>Detached source evidence alongside the unchanged positional
/// geometry. All corner channels stay together through primitive expansion.</summary>
public sealed class EchoesWorldSourceGeometry
{
    public EchoesWorldGeometry Geometry { get; }
    public IReadOnlyList<EchoesSourceMaterial> Materials { get; }
    public IReadOnlyList<EchoesSourceTriangle> Triangles { get; }
    public int SourceCornerVisits { get; }
    public int CandidateSourceCornerVisits { get; }
    internal EchoesWorldSourceGeometry(EchoesWorldGeometry geometry, EchoesSourceMaterial[] materials,
        EchoesSourceTriangle[] triangles, int cornerVisits, int candidateCornerVisits)
    {
        Geometry = geometry; Materials = Array.AsReadOnly(materials);
        Triangles = Array.AsReadOnly(triangles); SourceCornerVisits = cornerVisits;
        CandidateSourceCornerVisits = candidateCornerVisits;
    }
}
