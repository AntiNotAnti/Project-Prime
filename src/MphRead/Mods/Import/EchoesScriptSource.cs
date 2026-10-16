using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MphRead.Mods.Import;

/// <summary>
/// Local-only Echoes SCLY/SGEN object reader, adapted from Second Hunt's MREA
/// script format research. Reads the transforms and pickup properties needed
/// for later map conversion, without interpreting or executing script logic.
/// </summary>
public static class EchoesScriptSource
{
    public const uint EditorId = 0x255A4580;
    public const uint NameId = 0x494E414D;
    public const uint TransformId = 0x5846524D;
    public const uint ActiveId = 0x41435456;
    public const uint PickupKindId = 0xA02EF0C4;
    public const uint PickupAmountId = 0x94AF1445;
    private const int MaxObjects = 100000;
    private const int MaxConnections = 4096;

    public static IReadOnlyList<EchoesScriptObject> Read(EchoesMreaSections area)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (area.LayerCount == 0) return Array.Empty<EchoesScriptObject>();
        if (area.ScriptSection < 0 || area.LayerCount > area.SectionCount - area.ScriptSection)
            throw new InvalidDataException("Echoes script layers exceed area sections.");
        var results = new List<EchoesScriptObject>();
        for (int i = 0; i < area.LayerCount; i++)
            results.AddRange(ParseLayer(area.Section(area.ScriptSection + i).Span));
        return results;
    }

    public static IReadOnlyList<EchoesScriptObject> ParseLayer(ReadOnlySpan<byte> bytes)
    {
        bool generated = bytes.Length >= 4 && bytes[..4].SequenceEqual("SGEN"u8);
        bool scripted = bytes.Length >= 4 && bytes[..4].SequenceEqual("SCLY"u8);
        if (!generated && !scripted)
            throw new InvalidDataException("Echoes script layer is not SCLY or SGEN.");
        int header = generated ? 10 : 14;
        if (bytes.Length < header)
            throw new InvalidDataException("Truncated Echoes script layer.");
        int layer = generated ? -1 : checked((int)U32(bytes, 5));
        uint count = U32(bytes, generated ? 6 : 10);
        if (count > MaxObjects)
            throw new InvalidDataException("Echoes script object count exceeds budget.");
        int offset = header;
        var result = new List<EchoesScriptObject>((int)count);
        for (int i = 0; i < count; i++)
        {
            Require(bytes, offset, 12);
            string type = Encoding.ASCII.GetString(bytes.Slice(offset, 4));
            int bodySize = U16(bytes, offset + 4);
            int bodyStart = offset + 6;
            Require(bytes, bodyStart, bodySize);
            if (bodySize < 12)
                throw new InvalidDataException("Echoes script object body is too short.");
            int end = bodyStart + bodySize;
            uint id = U32(bytes, bodyStart);
            int connCount = U16(bytes, bodyStart + 4);
            if (connCount > MaxConnections)
                throw new InvalidDataException("Echoes script connection list exceeds budget.");
            long rootAt = bodyStart + 6L + 12L * connCount;
            Require(bytes, rootAt, 6);
            int rootBytes = U16(bytes, rootAt + 4);
            Require(bytes, rootAt + 6, rootBytes);
            if (rootAt + 6 + rootBytes > end)
                throw new InvalidDataException("Echoes script properties overrun the object.");
            ReadOnlySpan<byte> root = bytes.Slice((int)rootAt + 6, rootBytes);
            ReadOnlySpan<byte> editor = Find(root, EditorId);
            ReadOnlySpan<byte> activeBytes = Find(editor, ActiveId);
            bool active = activeBytes.IsEmpty || activeBytes[0] != 0;
            ReadOnlySpan<byte> nameBytes = Find(editor, NameId);
            string name = nameBytes.IsEmpty ? "" : Encoding.Latin1.GetString(nameBytes).TrimEnd('\0');
            if (name.Length > 1024) throw new InvalidDataException("Echoes script name exceeds limit.");
            ReadOnlySpan<byte> transformBytes = Find(editor, TransformId);
            float[] transform = new float[9];
            if (transformBytes.Length >= 36)
            {
                for (int n = 0; n < 9; n++)
                {
                    float f = BinaryPrimitives.ReadSingleBigEndian(transformBytes.Slice(4 * n, 4));
                    if (!float.IsFinite(f))
                        throw new InvalidDataException("Non-finite Echoes script transform.");
                    transform[n] = f;
                }
            }
            ReadOnlySpan<byte> pickupKind = Find(root, PickupKindId);
            ReadOnlySpan<byte> pickupAmount = Find(root, PickupAmountId);
            uint what = pickupKind.Length >= 4 ? U32(pickupKind, 0) : uint.MaxValue;
            int amount = pickupAmount.Length >= 4 ? I32(pickupAmount, 0) : 0;
            result.Add(new EchoesScriptObject(type, id, layer, active, name, transform,
                what, amount, transformBytes.Length >= 36));
            offset = end;
        }
        return result;
    }

    private static ReadOnlySpan<byte> Find(ReadOnlySpan<byte> structured, uint target)
    {
        if (structured.Length < 2) return default;
        int count = U16(structured, 0);
        if (count > 1024) return default;
        int at = 2;
        for (int i = 0; i < count; i++)
        {
            if (at > structured.Length - 6) return default;
            uint id = U32(structured, at);
            int len = U16(structured, at + 4);
            at += 6;
            if (len > structured.Length - at) return default;
            if (id == target) return structured.Slice(at, len);
            at += len;
        }
        return default;
    }

    private static uint U32(ReadOnlySpan<byte> bytes, long at)
    {
        Require(bytes, at, 4);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice((int)at, 4));
    }

    private static int I32(ReadOnlySpan<byte> bytes, int at) =>
        BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(at, 4));
    private static ushort U16(ReadOnlySpan<byte> bytes, long at)
    {
        Require(bytes, at, 2);
        return BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice((int)at, 2));
    }

    private static void Require(ReadOnlySpan<byte> bytes, long at, long count)
    {
        if (at < 0 || count < 0 || at > bytes.Length || count > bytes.Length - at)
            throw new InvalidDataException("Echoes script object exceeds layer bounds.");
    }
}

public sealed record EchoesScriptObject(string Type, uint Id, int Layer, bool Active,
    string Name, float[] Transform, uint PickupKind, int PickupAmount, bool HasTransform);
