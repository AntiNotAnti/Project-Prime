using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MphRead.Mods.Import;

/// <summary>
/// Bounded first-stage Retro ANCS character locator and CINF native skeleton
/// reader for locally imported Echoes models. Adapted from Second Hunt's
/// MIT-licensed ANCS/CINF research; no model, animation or disc assets shipped.
/// </summary>
public static class RetroCharacterSource
{
    public const uint EchoesDarkSamusAncs = 0x39900a11;
    public const int MaxCharacters = 256;
    public const int MaxBones = 1024;

    public static RetroCharacterRefs FindCharacter(ReadOnlySpan<byte> ancs, string wanted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wanted);
        if (wanted.Length > 128 || ancs.Length < 20)
            throw new InvalidDataException("Invalid ANCS data or character name.");
        uint characters = U32(ancs, 4);
        if (characters == 0 || characters > MaxCharacters)
            throw new InvalidDataException("ANCS character table exceeds safe count.");
        int at = 8;
        Span<byte> next = stackalloc byte[6];
        for (uint index = 0; index < characters; index++)
        {
            Require(ancs, at, 7);
            uint serial = U32(ancs, at);
            ushort version = U16(ancs, at + 4);
            if (serial != index || version > 64)
                throw new InvalidDataException("Invalid ANCS character header.");
            int start = at + 6;
            int end = ancs.Slice(start).IndexOf((byte)0);
            if (end < 0 || end > 256)
                throw new InvalidDataException("ANCS character name is missing or oversized.");
            string name = Encoding.Latin1.GetString(ancs.Slice(start, end));
            int refs = checked(start + end + 1);
            Require(ancs, refs, 12);
            if (name == wanted)
                return new RetroCharacterRefs(U32(ancs, refs), U32(ancs, refs + 4), U32(ancs, refs + 8));
            if (index + 1 >= characters) break;
            // ANCS character records include variable PAS, effect and clip
            // sections. The upstream locator recognizes the next character
            // serial+version prefix. Search only after this record's model ids,
            // then validate the candidate's complete header before advancing.
            BinaryPrimitives.WriteUInt32BigEndian(next[..4], index + 1);
            BinaryPrimitives.WriteUInt16BigEndian(next[4..], version);
            int cursor = refs + 12;
            bool found = false;
            while (cursor <= ancs.Length - 6)
            {
                int delta = ancs[cursor..].IndexOf(next);
                if (delta < 0) break;
                int candidate = cursor + delta;
                if (candidate <= ancs.Length - 7 &&
                    ancs[(candidate + 6)..].IndexOf((byte)0) is int n && n is >= 1 and <= 256 &&
                    candidate + 7 + n + 12 <= ancs.Length)
                {
                    at = candidate;
                    found = true;
                    break;
                }
                cursor = candidate + 1;
            }
            if (!found)
                throw new InvalidDataException("Next ANCS character entry could not be validated.");
        }
        throw new KeyNotFoundException("Character is absent from the supplied ANCS: " + wanted);
    }

    public static RetroNativeSkeleton ReadSkeleton(ReadOnlySpan<byte> cinf)
    {
        if (cinf.Length < 4) throw new InvalidDataException("CINF skeleton is truncated.");
        int bones = checked((int)U32(cinf, 0));
        if (bones <= 0 || bones > MaxBones)
            throw new InvalidDataException("CINF bone count exceeds supported size.");
        var result = new Dictionary<uint, RetroNativeBone>(bones);
        long at = 4;
        for (int i = 0; i < bones; i++)
        {
            Require(cinf, at, 24);
            uint id = U32(cinf, at);
            uint parent = U32(cinf, at + 4);
            float x = F32(cinf, at + 8), y = F32(cinf, at + 12), z = F32(cinf, at + 16);
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                throw new InvalidDataException("CINF bone has non-finite bind position.");
            at += 20;
            uint maybeCount = U32(cinf, at);
            // Some Echoes bones include 32 bytes between bind position and
            // the list. Follow the upstream version-aware disambiguation.
            if (maybeCount == 0 || maybeCount > 100)
            {
                Require(cinf, at, 32);
                at += 32;
            }
            uint links = U32(cinf, at);
            if (links > MaxBones)
                throw new InvalidDataException("CINF bone links exceed budget.");
            at += 4;
            Require(cinf, at, links * 4L);
            at += links * 4L;
            if (!result.TryAdd(id, new RetroNativeBone(id, parent, null, x, y, z)))
                throw new InvalidDataException("Duplicate CINF bone ID.");
        }
        uint ids = U32(cinf, at);
        if (ids > MaxBones)
            throw new InvalidDataException("CINF ordered bone indexes exceed budget.");
        at += 4;
        Require(cinf, at, ids * 4L);
        at += ids * 4L;
        uint named = U32(cinf, at);
        if (named > MaxBones)
            throw new InvalidDataException("CINF bone names exceed budget.");
        at += 4;
        for (int i = 0; i < named; i++)
        {
            Require(cinf, at, 1);
            int terminator = cinf[(int)at..].IndexOf((byte)0);
            if (terminator < 0 || terminator > 256)
                throw new InvalidDataException("Invalid CINF bone name.");
            string name = Encoding.Latin1.GetString(cinf.Slice((int)at, terminator));
            at += terminator + 1;
            uint bone = U32(cinf, at);
            at += 4;
            if (result.TryGetValue(bone, out var existing))
                result[bone] = existing with { Name = name };
        }
        return new RetroNativeSkeleton(result);
    }

    private static uint U32(ReadOnlySpan<byte> b, long at)
    {
        Require(b, at, 4);
        return BinaryPrimitives.ReadUInt32BigEndian(b.Slice((int)at, 4));
    }
    private static ushort U16(ReadOnlySpan<byte> b, long at)
    {
        Require(b, at, 2);
        return BinaryPrimitives.ReadUInt16BigEndian(b.Slice((int)at, 2));
    }
    private static float F32(ReadOnlySpan<byte> b, long at)
    {
        Require(b, at, 4);
        return BinaryPrimitives.ReadSingleBigEndian(b.Slice((int)at, 4));
    }
    private static void Require(ReadOnlySpan<byte> b, long at, long n)
    {
        if (at < 0 || n < 0 || at > b.Length || n > b.Length - at)
            throw new InvalidDataException("Retro character source exceeds local asset bounds.");
    }
}

public readonly record struct RetroCharacterRefs(uint Cmdl, uint Cskr, uint Cinf);
public sealed record RetroNativeBone(uint Id, uint Parent, string? Name, float X, float Y, float Z);
public sealed record RetroNativeSkeleton(IReadOnlyDictionary<uint, RetroNativeBone> Bones);
