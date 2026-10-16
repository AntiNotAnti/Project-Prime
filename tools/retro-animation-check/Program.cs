using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;

try
{
    byte[] raw = new byte[52];
    Put32(raw, 0, 0x00010001);
    Put32(raw, 4, 0);  // No characters, synthetic animation set only
    Put16(raw, 8, 1);  // ANCS animation version 1
    Put32(raw, 10, 1); // One animation
    "idle\0"u8.CopyTo(raw.AsSpan(14));
    Put32(raw, 19, 0); // play meta animation
    Put32(raw, 23, 7); // resource ID 7
    Put32(raw, 27, 12); // primitive ID
    "idle\0"u8.CopyTo(raw.AsSpan(31));
    Put32(raw, 36, 0);  // float property
    Put32(raw, 40, 0);
    Put32(raw, 44, 3);  // empty transition
    Put32(raw, 48, 0);  // no MP1 resource table

    IReadOnlyList<RetroAnimationIndex.Clip> clips = RetroAnimationIndex.Parse(raw);
    Check(clips.Count == 1 && clips[0].Name == "idle" &&
        clips[0].AnimationResourceIds.SequenceEqual(new uint[] { 7 }),
        "positive ANCS source index with single play animation");

    var blend = new Ancs.MetaAnim { Kind = "blend" };
    blend.Children.Add(new Ancs.MetaAnim { Kind = "play", AnimId = 10 });
    blend.Children.Add(new Ancs.MetaAnim { Kind = "play", AnimId = 20 });
    Check(Ancs.Prims(blend).Select(x => x.AnimId).SequenceEqual(new uint[] { 10, 20 }),
        "source blend leaves retained in deterministic order");

    var half = Anim.Slerp(new[] { 1d, 0d, 0d, 0d },
        new[] { 0d, 1d, 0d, 0d }, 0.5);
    Check(Math.Abs(half[0] - Math.Sqrt(.5)) < 1e-6 &&
        Math.Abs(half[1] - Math.Sqrt(.5)) < 1e-6, "quaternion blend sampling");

    var malformed = (byte[])raw.Clone();
    Put32(malformed, 4, uint.MaxValue);
    Expect<InvalidDataException>(() => RetroAnimationIndex.Parse(malformed),
        "invalid character count");
    malformed = (byte[])raw.Clone();
    Put32(malformed, 10, 3000);
    Expect<InvalidDataException>(() => RetroAnimationIndex.Parse(malformed),
        "oversized animation count rejects before downstream use");
    Expect<InvalidDataException>(() => Anim.Load(new byte[] { 0, 0, 0, 3 }, false),
        "unsupported source animation compression");
    RetroMotionPreviewCheck.Run();
    Console.WriteLine("PASS: Retro ANCS clip index and source animation interpolation contracts");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex);
    return 1;
}

static void Put32(byte[] b, int at, uint value) =>
    BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at, 4), value);
static void Put16(byte[] b, int at, ushort value) =>
    BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at, 2), value);
static void Check(bool value, string reason)
{
    if (!value) throw new InvalidOperationException(reason);
}
static void Expect<T>(Action action, string reason) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name + " for " + reason);
}
