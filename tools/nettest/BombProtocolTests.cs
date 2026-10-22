using System;
using System.Linq;
using System.IO;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;
internal static class BombProtocolTests
{
    private static int _checks;
    private static void Check(bool ok, string name)
    { if (!ok) throw new InvalidOperationException(name); _checks++; Console.WriteLine($"BOMB PROTOCOL PASS {name}"); }
    private static BombState State(uint id, BombType kind = BombType.MorphBall) => new(new ShotKey(9, 2, 3, 4, 5, id), kind, 17,
        new Vector3(1, 2, 3), Vector3.UnitZ, Vector3.UnitY, new Vector3(.1f, 0, 0), 40, 0, 0, 0, 2);
    public static int Run()
    {
        _checks = 0;
        foreach (BombAction action in Enum.GetValues<BombAction>())
        { var intent = new BombIntent(State(1).Key, uint.MaxValue, action); byte[] b = new byte[BombIntent.Size]; intent.Write(b); Check(BombIntent.TryRead(b, out var read) && read == intent, $"intent {action}"); Check(!BombIntent.TryRead(b.AsSpan(1), out _), "intent truncated"); }
        foreach (BombType kind in Enum.GetValues<BombType>())
        { var state = State(1, kind); byte[] b = new byte[BombState.Size]; state.Write(b); Check(BombState.TryRead(b, out var read) && read == state, $"state {kind}"); Check(!BombState.TryRead(b.AsSpan(1), out _), "state truncated"); }
        foreach (BombEventKind kind in Enum.GetValues<BombEventKind>())
        { var e = new BombEvent(1, 19, kind, 0, State(1)); byte[] b = new byte[BombEvent.Size]; e.Write(b); Check(BombEvent.TryRead(b, out var read) && read == e, $"event {kind}"); Check(!BombEvent.TryRead(b.AsSpan(1), out _), "event truncated"); }
        var refusal = new BombEvent(2, 20, BombEventKind.Refused, 3, default(BombState) with { Key = State(2).Key }); byte[] refused = new byte[BombEvent.Size]; refusal.Write(refused); Check(BombEvent.TryRead(refused, out var r) && r == refusal, "identity refusal");
        var detonateAccepted = refusal with { Kind = BombEventKind.DetonateAccepted }; detonateAccepted.Write(refused);
        Check(BombEvent.TryRead(refused, out var accepted) && accepted == detonateAccepted, "identity detonate acceptance");
        var jump = new BombEvent(3, 21, BombEventKind.Detonated, BombEvent.OwnerJump, State(3));
        jump.Write(refused);
        Check(BombEvent.TryRead(refused, out var jumping) && jumping == jump && jumping.HasOwnerJump,
            "authority owner jump roundtrips without changing terminal size");
        (jump with { Reason = 2 }).Write(refused);
        Check(!BombEvent.TryRead(refused, out _), "unknown detonation outcome rejected");
        (jump with { Reason = 0 }).Write(refused);
        Check(BombEvent.TryRead(refused, out var legacy) && !legacy.HasOwnerJump,
            "historical protocol 48-50 terminal remains readable without jump outcome");
        byte[] bad = new byte[BombState.Size]; (State(1) with { Position = new Vector3(float.NaN, 0, 0) }).Write(bad); Check(!BombState.TryRead(bad, out _), "nonfinite transform");
        (State(1) with { Up = Vector3.UnitZ }).Write(bad); Check(!BombState.TryRead(bad, out _), "degenerate basis");
        (State(1) with { Key = State(1).Key with { LifeId = 0 } }).Write(bad); Check(!BombState.TryRead(bad, out _), "invalid life");
        (State(1, BombType.Lockjaw) with { Link0 = 1 }).Write(bad); Check(!BombState.TryRead(bad, out _), "self link");
        var states = Enumerable.Range(1, 24).Select(i => State((uint)i)).ToArray(); var pages = BombStatePage.PackSnapshot(2, 9, uint.MaxValue, 7, states);
        Check(pages.Length == 3 && pages.All(b => b.Length <= 1200 - 64), "24 bombs bounded MTU");
        var receiver = new BombStateReceiver(); Check(receiver.Accept(pages[2]) && receiver.States.Length == 0, "partial atomic");
        Check(receiver.Accept(pages[0]) && receiver.Accept(pages[1]) && receiver.States.SequenceEqual(states) && receiver.Frontier == 7, "out of order complete");
        Check(!receiver.Accept(pages[1]), "duplicate page rejected");
        var wrapped = BombStatePage.PackSnapshot(2, 9, 0, 8, states.AsSpan(0, 1)); Check(receiver.Accept(wrapped[0]) && receiver.Accept(wrapped[2]) && receiver.Accept(wrapped[1]) && receiver.Frame == 0 && receiver.States.Length == 1, "frame wrap atomic");
        Check(!receiver.Accept(pages[0]), "old frame rejected");
        receiver.Reset(); Check(receiver.Accept(pages[0]), "reset");
        var duplicate = new BombStatePage(2, 9, uint.MaxValue, 7, 1, new[] { State(1) }).Encode(); Check(!receiver.Accept(duplicate), "cross page duplicate key");
        var conflict = new BombStatePage(2, 9, uint.MaxValue, 8, 1, new[] { State(9) }).Encode(); Check(!receiver.Accept(conflict), "frontier conflict");
        var foreign = BombStatePage.PackSnapshot(2, 10, 0, 8, Array.Empty<BombState>()); Check(!receiver.Accept(foreign[0]), "stream fenced until reset");
        Check(!BombStatePage.TryRead(pages[0].AsSpan(1), out _), "page truncated");
        var tooMany = (byte[])pages[0].Clone(); tooMany[23] = 9; Check(!BombStatePage.TryRead(tooMany, out _), "row bound");
        bool bounded = false; try { BombStatePage.PackSnapshot(2, 9, 1, 1, states.Append(State(25)).ToArray()); } catch (ArgumentException) { bounded = true; } Check(bounded, "total bound");
        bool unique = false; try { BombStatePage.PackSnapshot(2, 9, 1, 1, new[] { State(1), State(1) }); } catch (ArgumentException) { unique = true; } Check(unique, "snapshot duplicate key");
        receiver.Reset(); Check(!receiver.HasSnapshot, "reset has no frame-zero snapshot");
        Check(receiver.Accept(wrapped[0]) && !receiver.HasSnapshot, "frame-zero partial has no snapshot");
        byte[] Save(BombStateReceiver source)
        { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); source.WriteCheckpoint(writer); return stream.ToArray(); }
        void Load(BombStateReceiver target, byte[] bytes)
        { using var stream = new MemoryStream(bytes); using var reader = new BinaryReader(stream); target.ReadCheckpoint(reader); }
        var partialBytes = Save(receiver); var checkpoint = new BombStateReceiver(); Load(checkpoint, partialBytes);
        Check(!checkpoint.HasSnapshot && checkpoint.Accept(wrapped[1]) && checkpoint.Accept(wrapped[2]) && checkpoint.HasSnapshot && checkpoint.Frame == 0, "partial checkpoint completes frame zero");
        var completeBytes = Save(checkpoint); var complete = new BombStateReceiver(); Load(complete, completeBytes);
        Check(complete.HasSnapshot && complete.Frame == 0 && complete.Frontier == 8 && complete.States.SequenceEqual(checkpoint.States), "complete checkpoint roundtrip");
        var newer = BombStatePage.PackSnapshot(2, 9, 1, 9, states); Check(complete.Accept(newer[2]), "new partial after snapshot");
        var mixed = new BombStateReceiver(); Load(mixed, Save(complete));
        Check(mixed.HasSnapshot && mixed.Frame == 0 && mixed.Accept(newer[0]) && mixed.Accept(newer[1]) && mixed.Frame == 1 && mixed.States.Length == 24, "checkpoint retains previous snapshot and newer partial");
        bool malformedCheckpoint = false; try { Load(mixed, partialBytes.AsSpan(0, partialBytes.Length - 1).ToArray()); } catch (EndOfStreamException) { malformedCheckpoint = true; } catch (InvalidDataException) { malformedCheckpoint = true; }
        Check(malformedCheckpoint && mixed.Frame == 1 && mixed.States.Length == 24, "malformed checkpoint is atomic");
        var invalidFlag = (byte[])completeBytes.Clone(); invalidFlag[0] = 2;
        bool strictFlag = false; try { Load(mixed, invalidFlag); } catch (InvalidDataException) { strictFlag = true; }
        Check(strictFlag && mixed.Frame == 1, "checkpoint boolean encoding is strict and atomic");
        Check(!mixed.Accept(new BombStatePage(2, 9, 2, 10, 0, null!)), "object null rows rejected");
        Console.WriteLine($"BOMB PROTOCOL {_checks} assertions PASS"); return 0;
    }
}
