using System;
using System.IO;
using System.Security.Cryptography;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;
internal sealed partial class ReplayWorldCheckpoint
{
    /// <summary>Complete captured graph value identity; construction anchors are binding hints, not restored state.</summary>
    internal string GraphFingerprint()
    {
        using var source=_data.OpenRead();using var reader=new BinaryReader(source);
        using var normalized=new MemoryStream(Bytes.Length);using var writer=new BinaryWriter(normalized);
        writer.Write(reader.ReadUInt32());ushort version=reader.ReadUInt16();writer.Write(version);
        if(version!=Version&&!IsCustomWorldVersion(version))throw new InvalidDataException("Graph comparison requires this build's current checkpoint layout.");
        writer.Write(reader.ReadByte());writer.Write(reader.ReadString());writer.Write(reader.ReadString());writer.Write(reader.ReadInt32());writer.Write(reader.ReadUInt64());
        writer.Write(reader.ReadUInt32());writer.Write(reader.ReadUInt32());writer.Write(reader.ReadUInt32());
        int maximum=IsCustomWorldVersion(version)?ReplayReplicaCheckpoint.MaximumCustomBytes:ReplayReplicaCheckpoint.MaximumBytes;
        WriteBytes(writer,ReadBytes(reader,maximum));WriteBytes(writer,ReadBytes(reader,maximum));
        int count=Count(reader,MaximumObjects);writer.Write(count);
        for(int i=0;i<count;i++){writer.Write(reader.ReadUInt16());_ = reader.ReadUInt64();WriteBytes(writer,ReadBytes(reader,MaximumBytes));}
        // Remaining bounded component sections are already validated by Capture.
        writer.Flush();source.CopyTo(normalized);writer.Flush();
        return Convert.ToHexString(SHA256.HashData(normalized.GetBuffer().AsSpan(0,(int)normalized.Length)));
    }
}
