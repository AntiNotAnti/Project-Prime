using System;
using System.IO;
using System.Security.Cryptography;

namespace MphRead.Mods.Replay;

internal sealed partial class ReplayCameraTrack
{
    /// <summary>Encode the retained v3 format with the identity frozen at review, before safe publication.</summary>
    internal byte[] EncodeReviewed(ReplayCameraTrackIdentity identity)
    {
        var keys=_windowSource?._keys??_keys;
        bool handles=_windowSource!=null||keys.Exists(key=>key.Interpolation==ReplayCameraInterpolation.Bezier||key.IncomingTangent.HasValue||key.OutgoingTangent.HasValue||key.FovIncomingTangent!=0||key.FovOutgoingTangent!=0||key.RollIncomingTangent!=0||key.RollOutgoingTangent!=0);
        using var stream=new MemoryStream(HeaderSize+MaxKeys*KeySizeV4+32);
        using var writer=new BinaryWriter(stream);
        writer.Write(Magic);writer.Write((byte)(_windowSource!=null?5:handles?4:3));writer.Write(identity.LogicalReplayId);
        if(identity.SourceContentHash.Length!=32)throw new InvalidDataException("The reviewed camera source hash is invalid.");
        writer.Write(identity.SourceContentHash);writer.Write(identity.StartFrame);writer.Write(identity.EndFrame);
        if(_windowSource!=null){writer.Write(_windowStart);writer.Write(_windowEnd);}
        writer.Write((ushort)keys.Count);
        foreach(var key in keys)
        {
            if(handles)WriteStateKey(writer,key);
            else
            {
                writer.Write(key.Frame);writer.Write(key.Position.X);writer.Write(key.Position.Y);writer.Write(key.Position.Z);
                writer.Write(key.Rotation.X);writer.Write(key.Rotation.Y);writer.Write(key.Rotation.Z);writer.Write(key.Rotation.W);
                writer.Write(key.Fov);writer.Write(key.LookAtSlot);writer.Write(key.Roll);writer.Write((byte)key.Interpolation);writer.Write((byte)key.Ease);
            }
        }
        writer.Flush();byte[] body=stream.ToArray();writer.Write(SHA256.HashData(body));writer.Flush();return stream.ToArray();
    }
}
