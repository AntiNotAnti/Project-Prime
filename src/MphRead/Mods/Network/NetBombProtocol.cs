using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public enum BombAction : byte { Place, Detonate }
public enum BombEventKind : byte { Placed, Refused, Detonated, Removed, DetonateAccepted }

internal static class BombCodec
{
    internal const int KeySize = 19;
    internal static bool ValidKey(ShotKey k) => k.AuthorityEpoch != 0 && k.MatchId != 0
        && (uint)k.ShooterSlot < 8 && k.Generation != 0 && k.LifeId != 0 && k.ShotId != 0;
    internal static void WriteKey(Span<byte> b, ShotKey k)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(b, k.AuthorityEpoch);
        BinaryPrimitives.WriteUInt16LittleEndian(b[8..], k.MatchId); b[10] = (byte)k.ShooterSlot;
        BinaryPrimitives.WriteUInt16LittleEndian(b[11..], k.Generation);
        BinaryPrimitives.WriteUInt16LittleEndian(b[13..], k.LifeId);
        BinaryPrimitives.WriteUInt32LittleEndian(b[15..], k.ShotId);
    }
    internal static ShotKey ReadKey(ReadOnlySpan<byte> b) => new(BinaryPrimitives.ReadUInt64LittleEndian(b),
        BinaryPrimitives.ReadUInt16LittleEndian(b[8..]), b[10], BinaryPrimitives.ReadUInt16LittleEndian(b[11..]),
        BinaryPrimitives.ReadUInt16LittleEndian(b[13..]), BinaryPrimitives.ReadUInt32LittleEndian(b[15..]));
    internal static void Vector(Span<byte> b, Vector3 v)
    { BinaryPrimitives.WriteSingleLittleEndian(b, v.X); BinaryPrimitives.WriteSingleLittleEndian(b[4..], v.Y); BinaryPrimitives.WriteSingleLittleEndian(b[8..], v.Z); }
    internal static Vector3 Vector(ReadOnlySpan<byte> b) => new(BinaryPrimitives.ReadSingleLittleEndian(b), BinaryPrimitives.ReadSingleLittleEndian(b[4..]), BinaryPrimitives.ReadSingleLittleEndian(b[8..]));
    internal static bool Bounded(Vector3 v, float max) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z)
        && Math.Abs(v.X) <= max && Math.Abs(v.Y) <= max && Math.Abs(v.Z) <= max;
}

public readonly record struct BombIntent(ShotKey Key, uint Frame, BombAction Action)
{
    public const int Size = 24;
    public void Write(Span<byte> b)
    { if (b.Length < Size) throw new ArgumentException(nameof(b)); BombCodec.WriteKey(b, Key); BinaryPrimitives.WriteUInt32LittleEndian(b[19..], Frame); b[23] = (byte)Action; }
    public static bool TryRead(ReadOnlySpan<byte> b, out BombIntent value)
    {
        value = default; if (b.Length != Size) return false;
        var candidate = new BombIntent(BombCodec.ReadKey(b), BinaryPrimitives.ReadUInt32LittleEndian(b[19..]), (BombAction)b[23]);
        if (!BombCodec.ValidKey(candidate.Key) || candidate.Action > BombAction.Detonate) return false;
        value = candidate; return true;
    }
}

public readonly record struct BombState(ShotKey Key, BombType Kind, uint PlacedFrame, Vector3 Position,
    Vector3 Facing, Vector3 Up, Vector3 Speed, ushort Countdown, byte Index, uint Link0, uint Link1, byte Recolor)
{
    public const int Size = 84;
    public bool IsValid => BombCodec.ValidKey(Key) && Kind <= BombType.Lockjaw && Index < 3 && Recolor < 8
        && BombCodec.Bounded(Position, 32768) && BombCodec.Bounded(Speed, 1024)
        && BombCodec.Bounded(Facing, 1.01f) && BombCodec.Bounded(Up, 1.01f)
        && Math.Abs(Facing.LengthSquared - 1) < .05f && Math.Abs(Up.LengthSquared - 1) < .05f
        && Math.Abs(Vector3.Dot(Facing, Up)) < .05f
        && (Link0 == 0 || Link0 != Key.ShotId) && (Link1 == 0 || Link1 != Key.ShotId)
        && (Link0 == 0 || Link1 == 0 || Link0 != Link1)
        && (Kind == BombType.Lockjaw || Link0 == 0 && Link1 == 0);
    public void Write(Span<byte> b)
    {
        if (b.Length < Size) throw new ArgumentException(nameof(b)); BombCodec.WriteKey(b, Key); b[19] = (byte)Kind;
        BinaryPrimitives.WriteUInt32LittleEndian(b[20..], PlacedFrame);
        BombCodec.Vector(b[24..], Position); BombCodec.Vector(b[36..], Facing); BombCodec.Vector(b[48..], Up); BombCodec.Vector(b[60..], Speed);
        BinaryPrimitives.WriteUInt16LittleEndian(b[72..], Countdown); b[74] = Index;
        BinaryPrimitives.WriteUInt32LittleEndian(b[75..], Link0); BinaryPrimitives.WriteUInt32LittleEndian(b[79..], Link1); b[83] = Recolor;
    }
    internal static BombState ReadUnchecked(ReadOnlySpan<byte> b) => new(BombCodec.ReadKey(b), (BombType)b[19],
        BinaryPrimitives.ReadUInt32LittleEndian(b[20..]), BombCodec.Vector(b[24..]), BombCodec.Vector(b[36..]),
        BombCodec.Vector(b[48..]), BombCodec.Vector(b[60..]), BinaryPrimitives.ReadUInt16LittleEndian(b[72..]), b[74],
        BinaryPrimitives.ReadUInt32LittleEndian(b[75..]), BinaryPrimitives.ReadUInt32LittleEndian(b[79..]), b[83]);
    public static bool TryRead(ReadOnlySpan<byte> b, out BombState value)
    { value = default; if (b.Length != Size) return false; var candidate = ReadUnchecked(b); if (!candidate.IsValid) return false; value = candidate; return true; }
}

public readonly record struct BombEvent(ulong Sequence, uint Frame, BombEventKind Kind, byte Reason, BombState State)
{
    // Protocol 51: the detonation's reason byte carries the native authority
    // contact result. Other event kinds retain their existing reason semantics.
    public const byte OwnerJump = 1;
    public bool HasOwnerJump => Kind == BombEventKind.Detonated && (Reason & OwnerJump) != 0;
    public const int Size = 98;
    public void Write(Span<byte> b)
    { if (b.Length < Size) throw new ArgumentException(nameof(b)); BinaryPrimitives.WriteUInt64LittleEndian(b, Sequence); BinaryPrimitives.WriteUInt32LittleEndian(b[8..], Frame); b[12] = (byte)Kind; b[13] = Reason; State.Write(b[14..]); }
    public static bool TryRead(ReadOnlySpan<byte> b, out BombEvent value)
    {
        value = default; if (b.Length != Size) return false;
        var candidate = new BombEvent(BinaryPrimitives.ReadUInt64LittleEndian(b), BinaryPrimitives.ReadUInt32LittleEndian(b[8..]), (BombEventKind)b[12], b[13], BombState.ReadUnchecked(b[14..]));
        if (candidate.Sequence == 0 || candidate.Kind > BombEventKind.DetonateAccepted || !BombCodec.ValidKey(candidate.State.Key)
            || candidate.Kind == BombEventKind.Detonated && (candidate.Reason & ~OwnerJump) != 0
            || (candidate.Kind is not (BombEventKind.Refused or BombEventKind.DetonateAccepted) && !candidate.State.IsValid)) return false;
        // Identity-only refusals carry no transform. A supplied transform must still be valid.
        if ((candidate.Kind is BombEventKind.Refused or BombEventKind.DetonateAccepted) && !candidate.State.IsValid
            && candidate.State != new BombState(candidate.State.Key, default, 0, default, default, default, default, 0, 0, 0, 0, 0)) return false;
        value = candidate; return true;
    }
}

public sealed record BombStatePage(ushort MatchId, ulong Epoch, uint Frame, ulong Frontier, byte Page, BombState[] States)
{
    public const int Pages = 3, MaxRows = 8, HeaderSize = 24, MaximumSize = HeaderSize + MaxRows * BombState.Size;
    public byte[] Encode()
    {
        if (MatchId == 0 || Epoch == 0 || Page >= Pages || States.Length > MaxRows) throw new ArgumentException("Invalid bomb page");
        var b = new byte[HeaderSize + States.Length * BombState.Size];
        BinaryPrimitives.WriteUInt16LittleEndian(b, MatchId); BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(2), Epoch);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(10), Frame); BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(14), Frontier);
        b[22] = Page; b[23] = (byte)States.Length;
        for (int i = 0; i < States.Length; i++) States[i].Write(b.AsSpan(HeaderSize + i * BombState.Size));
        return b;
    }
    public static byte[][] PackSnapshot(ushort matchId, ulong epoch, uint frame, ulong frontier, ReadOnlySpan<BombState> states)
    {
        if (states.Length > Pages * MaxRows) throw new ArgumentException("Too many bombs");
        var keys = new HashSet<ShotKey>();
        foreach (var state in states) if (!state.IsValid || state.Key.MatchId != matchId || state.Key.AuthorityEpoch != epoch || !keys.Add(state.Key)) throw new ArgumentException("Invalid bomb snapshot");
        var result = new byte[Pages][];
        for (byte page = 0; page < Pages; page++) { int start = Math.Min(page * MaxRows, states.Length); int count = Math.Min(MaxRows, states.Length - start); result[page] = new BombStatePage(matchId, epoch, frame, frontier, page, states.Slice(start, count).ToArray()).Encode(); }
        return result;
    }
    public static bool TryRead(ReadOnlySpan<byte> b, out BombStatePage? value)
    {
        value = null; if (b.Length < HeaderSize || b[22] >= Pages || b[23] > MaxRows || b.Length != HeaderSize + b[23] * BombState.Size) return false;
        ushort match = BinaryPrimitives.ReadUInt16LittleEndian(b); ulong epoch = BinaryPrimitives.ReadUInt64LittleEndian(b[2..]); if (match == 0 || epoch == 0) return false;
        var states = new BombState[b[23]]; var keys = new HashSet<ShotKey>();
        for (int i = 0; i < states.Length; i++) if (!BombState.TryRead(b.Slice(HeaderSize + i * BombState.Size, BombState.Size), out states[i]) || states[i].Key.MatchId != match || states[i].Key.AuthorityEpoch != epoch || !keys.Add(states[i].Key)) return false;
        value = new(match, epoch, BinaryPrimitives.ReadUInt32LittleEndian(b[10..]), BinaryPrimitives.ReadUInt64LittleEndian(b[14..]), b[22], states); return true;
    }
}

public sealed class BombStateReceiver
{
    private readonly BombStatePage?[] _pages = new BombStatePage?[BombStatePage.Pages];
    private ushort _match; private ulong _epoch; private uint _pendingFrame; private ulong _pendingFrontier; private bool _pending;
    public BombState[] States { get; private set; } = Array.Empty<BombState>();
    public uint Frame { get; private set; }
    public bool HasSnapshot { get; private set; }
    public ulong Frontier { get; private set; }
    public void Reset() { Array.Clear(_pages); _match = 0; _epoch = 0; _pending = false; States = Array.Empty<BombState>(); Frame = 0; Frontier = 0; HasSnapshot = false; }
    public void WriteCheckpoint(BinaryWriter writer)
    {
        writer.Write(HasSnapshot);
        if (HasSnapshot)
            foreach (var bytes in BombStatePage.PackSnapshot(_match, _epoch, Frame, Frontier, States))
            { writer.Write(bytes.Length); writer.Write(bytes); }
        writer.Write(_pending);
        if (_pending)
        {
            writer.Write(_match); writer.Write(_epoch); writer.Write(_pendingFrame); writer.Write(_pendingFrontier);
            for (int i = 0; i < _pages.Length; i++)
            {
                writer.Write(_pages[i] != null);
                if (_pages[i] is { } page) { var bytes = page.Encode(); writer.Write(bytes.Length); writer.Write(bytes); }
            }
        }
    }
    public void ReadCheckpoint(BinaryReader reader)
    {
        // Decode into a temporary receiver so a malformed checkpoint cannot partially replace live state.
        var restored = new BombStateReceiver();
        BombStatePage ReadPage()
        {
            int length = reader.ReadInt32();
            if (length < BombStatePage.HeaderSize || length > BombStatePage.MaximumSize) throw new InvalidDataException("Invalid bomb checkpoint page length");
            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length || !BombStatePage.TryRead(bytes, out var page)) throw new InvalidDataException("Invalid bomb checkpoint page");
            return page!;
        }
        bool ReadFlag()
        { byte flag = reader.ReadByte(); if (flag > 1) throw new InvalidDataException("Invalid bomb checkpoint flag"); return flag != 0; }
        if (ReadFlag())
        {
            for (int i = 0; i < BombStatePage.Pages; i++)
            { var page = ReadPage(); if (page.Page != i || !restored.Accept(page)) throw new InvalidDataException("Invalid complete bomb checkpoint"); }
            if (!restored.HasSnapshot) throw new InvalidDataException("Incomplete bomb checkpoint snapshot");
        }
        bool pending = ReadFlag();
        if (pending)
        {
            ushort match = reader.ReadUInt16(); ulong epoch = reader.ReadUInt64(); uint frame = reader.ReadUInt32(); ulong frontier = reader.ReadUInt64();
            if (match == 0 || epoch == 0 || restored.HasSnapshot && (match != restored._match || epoch != restored._epoch
                || frame != restored.Frame && !NetLifecycleTracker.Newer(frame, restored.Frame) || frontier < restored.Frontier))
                throw new InvalidDataException("Invalid pending bomb checkpoint stream");
            var completeStates = restored.States; bool sameComplete = restored.HasSnapshot && frame == restored.Frame;
            if (sameComplete && frontier != restored.Frontier) throw new InvalidDataException("Conflicting complete bomb checkpoint frontier");
            Array.Clear(restored._pages); restored._pending = true; restored._match = match; restored._epoch = epoch;
            restored._pendingFrame = frame; restored._pendingFrontier = frontier;
            int count = 0;
            for (int i = 0; i < BombStatePage.Pages; i++) if (ReadFlag())
            {
                var page = ReadPage();
                if (page.Page != i || page.MatchId != match || page.Epoch != epoch || page.Frame != frame || page.Frontier != frontier
                    || !restored.Accept(page)) throw new InvalidDataException("Invalid pending bomb checkpoint page");
                count++;
            }
            if (count == 0) throw new InvalidDataException("Empty pending bomb checkpoint");
            if (sameComplete && (count != BombStatePage.Pages || !completeStates.AsSpan().SequenceEqual(restored.States)))
                throw new InvalidDataException("Conflicting complete bomb checkpoint state");
        }
        else if (restored.HasSnapshot) throw new InvalidDataException("Missing pending bomb checkpoint metadata");
        Reset(); _match = restored._match; _epoch = restored._epoch; _pendingFrame = restored._pendingFrame;
        _pendingFrontier = restored._pendingFrontier; _pending = restored._pending; HasSnapshot = restored.HasSnapshot;
        States = restored.States; Frame = restored.Frame; Frontier = restored.Frontier;
        Array.Copy(restored._pages, _pages, _pages.Length);
    }
    public bool Accept(ReadOnlySpan<byte> bytes) => BombStatePage.TryRead(bytes, out var page) && Accept(page!);
    public bool Accept(BombStatePage page)
    {
        if (page == null || page.States == null) return false;
        // Validate even object callers; caller-owned arrays never become receiver storage.
        BombStatePage? safe;
        try { if (!BombStatePage.TryRead(page.Encode(), out safe)) return false; }
        catch (ArgumentException) { return false; }
        page = safe!;
        if (_match != 0 && (page.MatchId != _match || page.Epoch != _epoch)) return false;
        if (_pending && page.Frame != _pendingFrame && !NetLifecycleTracker.Newer(page.Frame, _pendingFrame)) return false;
        if (_pending && page.Frame != _pendingFrame && page.Frontier < _pendingFrontier) return false;
        if (!_pending || page.Frame != _pendingFrame)
        { Array.Clear(_pages); _pending = true; _match = page.MatchId; _epoch = page.Epoch; _pendingFrame = page.Frame; _pendingFrontier = page.Frontier; }
        if (page.Frontier != _pendingFrontier || _pages[page.Page] != null) return false;
        var keys = new HashSet<ShotKey>(); foreach (var old in _pages) if (old != null) foreach (var state in old.States) keys.Add(state.Key);
        foreach (var state in page.States) if (!keys.Add(state.Key)) return false;
        _pages[page.Page] = page;
        foreach (var candidate in _pages) if (candidate == null) return true;
        var all = new List<BombState>(24); foreach (var candidate in _pages) all.AddRange(candidate!.States);
        States = all.ToArray(); Frame = _pendingFrame; Frontier = _pendingFrontier; HasSnapshot = true; return true;
    }
}
