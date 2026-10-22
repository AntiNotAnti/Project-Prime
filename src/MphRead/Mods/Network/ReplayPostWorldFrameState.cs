using System;
using System.IO;

namespace MphRead.Mods.Network;

// Shared by the format8 writer and whole-chunk reader validation. These are
// storage/application bounds, not network timing or a second simulation clock.
internal sealed class ReplayPostWorldFrameState
{
    internal const int MaximumRecords = 4096;
    internal const int MaximumBytes = 8 * 1024 * 1024;
    private uint? _frame;
    private int _records, _bytes, _postRecords, _postBytes;
    private bool _postWorld, _quiet;

    internal void Observe(uint frame, ReadOnlySpan<byte> packet)
    {
        bool post = !packet.IsEmpty && packet[0] == ReplayPostWorldRecord.Tag;
        bool quiet = packet.Length == 1 && packet[0] == 255;
        if (packet.IsEmpty || frame == 0 && !post && !quiet || _frame.HasValue && frame < _frame.Value)
            throw new InvalidDataException("Invalid format8 frame or complete-world baseline.");
        bool nextFrame = _frame != frame;
        bool carry = nextFrame && _frame.HasValue && (ulong)_frame.Value + 1 == frame;
        int records = (nextFrame ? carry ? _postRecords : 0 : _records) + 1;
        int bytes = (nextFrame ? carry ? _postBytes : 0 : _bytes) + packet.Length;
        if (records > MaximumRecords || bytes > MaximumBytes)
            throw new InvalidDataException("Format8 combined ordinary/deferred frame budget exceeded.");
        if (!nextFrame && (_quiet || _postWorld && !post && !quiet))
            throw new InvalidDataException("Format8 ordinary facts cannot follow a post-world phase or quiet frontier.");
        if (nextFrame)
        {
            _frame = frame; _postRecords = _postBytes = 0;
            _postWorld = _quiet = false;
        }
        _records = records; _bytes = bytes;
        if (post) { _postWorld = true; _postRecords++; _postBytes += packet.Length; }
        _quiet = quiet;
    }
}
