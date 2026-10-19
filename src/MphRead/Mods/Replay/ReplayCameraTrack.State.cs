using System;
using System.IO;
using System.Security.Cryptography;

namespace MphRead.Mods.Replay;

internal sealed partial class ReplayCameraTrack
{
    internal const int MaxStateBytes = 8192;
    private const uint StateMagic = 0x53435046; // FPCS

    /// <summary>Bounded camera authoring state without replay identities or paths.
    /// Detached exports and portable bundles rebind it to their own source.</summary>
    internal byte[] ExportState()
    {
        var keys = _windowSource?._keys ?? _keys;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(StateMagic); writer.Write((byte)1); writer.Write(_windowSource != null);
        if (_windowSource != null) { writer.Write(_windowStart); writer.Write(_windowEnd); }
        writer.Write((ushort)keys.Count);
        foreach (var key in keys) WriteStateKey(writer, key);
        writer.Flush(); byte[] body = stream.ToArray();
        writer.Write(SHA256.HashData(body)); writer.Flush();
        return stream.ToArray();
    }

    internal bool ImportState(byte[] bytes)
    {
        try
        {
            if (bytes == null || bytes.Length < 40 || bytes.Length > MaxStateBytes)
                throw new InvalidDataException("Camera state exceeds its bounded size.");
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)), bytes.AsSpan(bytes.Length - 32)))
                throw new InvalidDataException("Camera state checksum failed.");
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != StateMagic || reader.ReadByte() != 1)
                throw new InvalidDataException("Unsupported camera state.");
            byte flag = reader.ReadByte();
            if (flag > 1) throw new InvalidDataException("Invalid camera state window.");
            uint start = 0, end = 0;
            if (flag == 1)
            { start = reader.ReadUInt32(); end = reader.ReadUInt32(); if (start >= end) throw new InvalidDataException("Invalid camera state window."); }
            int count = reader.ReadUInt16();
            if (count > MaxKeys || stream.Position + count * KeySizeV4 + 32 != bytes.Length)
                throw new InvalidDataException("Invalid camera state key count.");
            var source = new ReplayCameraTrack();
            for (int i = 0; i < count; i++)
            {
                var key = ReadStateKey(reader);
                if (!Valid(key) || i > 0 && key.Frame <= source._keys[^1].Frame)
                    throw new InvalidDataException("Invalid camera state key.");
                source._keys.Add(key);
            }
            var adopted = source;
            if (flag == 1)
            { adopted = new ReplayCameraTrack { _windowSource = source, _windowStart = start, _windowEnd = end }; adopted.RebuildWindowKeys(); }
            RestoreTrack(adopted); LastError = null; return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or NotSupportedException)
        { LastError = ex.Message; return false; }
    }

    private static void WriteStateKey(BinaryWriter writer, ReplayCameraKeyframe key)
    {
        writer.Write(key.Frame); writer.Write(key.Position.X); writer.Write(key.Position.Y); writer.Write(key.Position.Z);
        writer.Write(key.Rotation.X); writer.Write(key.Rotation.Y); writer.Write(key.Rotation.Z); writer.Write(key.Rotation.W);
        writer.Write(key.Fov); writer.Write(key.LookAtSlot); writer.Write(key.Roll);
        writer.Write((byte)key.Interpolation); writer.Write((byte)key.Ease);
        writer.Write(key.IncomingTangent.HasValue); var incoming = key.IncomingTangent ?? default;
        writer.Write(incoming.X); writer.Write(incoming.Y); writer.Write(incoming.Z);
        writer.Write(key.OutgoingTangent.HasValue); var outgoing = key.OutgoingTangent ?? default;
        writer.Write(outgoing.X); writer.Write(outgoing.Y); writer.Write(outgoing.Z);
        writer.Write(key.FovIncomingTangent); writer.Write(key.FovOutgoingTangent);
        writer.Write(key.RollIncomingTangent); writer.Write(key.RollOutgoingTangent);
    }
    private static ReplayCameraKeyframe ReadStateKey(BinaryReader reader)
    {
        uint frame = reader.ReadUInt32();
        var position = new OpenTK.Mathematics.Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        var rotation = new OpenTK.Mathematics.Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        float fov = reader.ReadSingle(); sbyte slot = reader.ReadSByte(); float roll = reader.ReadSingle();
        var interpolation = (ReplayCameraInterpolation)reader.ReadByte(); var ease = (ReplayCameraEase)reader.ReadByte();
        byte hasIn = reader.ReadByte(); var incoming = new OpenTK.Mathematics.Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        byte hasOut = reader.ReadByte(); var outgoing = new OpenTK.Mathematics.Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        if (hasIn > 1 || hasOut > 1) throw new InvalidDataException("Invalid camera tangent flag.");
        return new(frame, position, rotation, fov, slot, roll, interpolation, ease,
            hasIn == 1 ? incoming : null, hasOut == 1 ? outgoing : null,
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }
}
