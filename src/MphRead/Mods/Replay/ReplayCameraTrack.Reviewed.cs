using System;
using System.IO;
using System.Security.Cryptography;

namespace MphRead.Mods.Replay;

internal sealed partial class ReplayCameraTrack
{
    /// <summary>Encode the compatible identity-bound format with the identity frozen at review, before safe publication.</summary>
    internal byte[] EncodeReviewed(ReplayCameraTrackIdentity identity)
    {
        var keys=_windowSource?._keys??_keys;
        bool channels=HasChannelCurves(keys);
        using var stream=new MemoryStream(HeaderSize+MaxKeys*KeySizeV7+32);
        using var writer=new BinaryWriter(stream);
        writer.Write(Magic);writer.Write((byte)(channels?7:6));writer.Write(identity.LogicalReplayId);
        if(identity.SourceContentHash.Length!=32)throw new InvalidDataException("The reviewed camera source hash is invalid.");
        writer.Write(identity.SourceContentHash);writer.Write(identity.StartFrame);writer.Write(identity.EndFrame);
        writer.Write(_windowSource!=null);
        if(_windowSource!=null){writer.Write(_windowStart);writer.Write(_windowEnd);}
        writer.Write(keys.Count);
        foreach(var key in keys)WriteStateKey(writer,key,channels);
        writer.Flush();byte[] body=stream.ToArray();writer.Write(SHA256.HashData(body));writer.Flush();return stream.ToArray();
    }
}
