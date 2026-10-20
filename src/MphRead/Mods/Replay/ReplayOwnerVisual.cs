using System;
using System.IO;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay;

// Value-only picture state. It is never installed in the simulation model.
internal readonly record struct ReplayOwnerAnimSlot(int Index, int Frame, int Count, AnimFlags Flags, int Step)
{
    internal bool Valid => Index is >= -1 and <= 4095 && Count is >= 0 and <= 65535
        && Frame >= 0 && Frame < Math.Max(1, Count) && Step is >= 0 and <= 1024 && ((int)Flags & ~31) == 0;
}
internal readonly record struct ReplayOwnerAnimGroup(short Index, byte Slot);
internal readonly record struct ReplayOwnerAnimation(ReplayOwnerAnimSlot A, ReplayOwnerAnimSlot B,
    ReplayOwnerAnimGroup Node, ReplayOwnerAnimGroup Material, ReplayOwnerAnimGroup Texture,
    ReplayOwnerAnimGroup Texcoord, int From, int To, float Fraction)
{
    internal bool Valid => A.Valid && B.Valid && Group(Node) && Group(Material) && Group(Texture) && Group(Texcoord)
        && From == (Node.Slot == 0 ? A.Frame : B.Frame) && From >= 0 && To >= 0 && From < Math.Max(1, Node.Slot == 0 ? A.Count : B.Count)
        && To < Math.Max(1, Node.Slot == 0 ? A.Count : B.Count) && float.IsFinite(Fraction) && Fraction is >= 0 and <= 1;
    private static bool Group(ReplayOwnerAnimGroup g) => g.Index is >= -1 and <= 4095 && g.Slot < 2;
    internal static ReplayOwnerAnimation Capture(ModelInstance model, AnimationSamples.Sample? sample = null)
    {
        var info = model.AnimInfo; var groups = model.Model.AnimationGroups;
        ReplayOwnerAnimSlot Slot(int i) => new(info.Index[i], info.Frame[i], info.FrameCount[i], info.Flags[i], info.Step[i]);
        static short Index<T>(System.Collections.Generic.IReadOnlyList<T> groups, T? group) where T : class
        { for (int i = 0; i < groups.Count; i++) if (ReferenceEquals(groups[i], group)) return checked((short)i); return -1; }
        return new(Slot(0), Slot(1), new(Index(groups.Node, info.Node.Group), (byte)info.Node.Slot),
            new(Index(groups.Material, info.Material.Group), (byte)info.Material.Slot),
            new(Index(groups.Texture, info.Texture.Group), (byte)info.Texture.Slot),
            new(Index(groups.Texcoord, info.Texcoord.Group), (byte)info.Texcoord.Slot),
            sample?.FromFrame ?? info.NodeFrame, sample?.ToFrame ?? info.NodeFrame, sample?.Fraction ?? 0);
    }
    internal bool CanApply(ModelInstance model)
    {
        var g = model.Model.AnimationGroups;
        return Valid && Node.Index < g.Node.Count && Material.Index < g.Material.Count
            && Texture.Index < g.Texture.Count && Texcoord.Index < g.Texcoord.Count
            && (Node.Index < 0 || (Node.Slot == 0 ? A.Count : B.Count) == g.Node[Node.Index].FrameCount);
    }
    internal void Apply(ModelInstance model)
    {
        var info = model.AnimInfo; var g = model.Model.AnimationGroups;
        void Slot(int i, ReplayOwnerAnimSlot v)
        { info.Index[i] = v.Index; info.Frame[i] = v.Frame; info.FrameCount[i] = v.Count; info.Flags[i] = v.Flags; info.Step[i] = v.Step; }
        Slot(0, A); Slot(1, B);
        info.Node.Slot = Node.Slot; info.Node.Group = Node.Index < 0 ? null : g.Node[Node.Index];
        info.Material.Slot = Material.Slot; info.Material.Group = Material.Index < 0 ? null : g.Material[Material.Index];
        info.Texture.Slot = Texture.Slot; info.Texture.Group = Texture.Index < 0 ? null : g.Texture[Texture.Index];
        info.Texcoord.Slot = Texcoord.Slot; info.Texcoord.Group = Texcoord.Index < 0 ? null : g.Texcoord[Texcoord.Index];
    }
    internal void Write(BinaryWriter w)
    {
        static void Slot(BinaryWriter w, ReplayOwnerAnimSlot s) { w.Write(s.Index); w.Write(s.Frame); w.Write(s.Count); w.Write((ushort)s.Flags); w.Write(s.Step); }
        static void Group(BinaryWriter w, ReplayOwnerAnimGroup g) { w.Write(g.Index); w.Write(g.Slot); }
        Slot(w, A); Slot(w, B); Group(w, Node); Group(w, Material); Group(w, Texture); Group(w, Texcoord);
        w.Write(From); w.Write(To); w.Write(Fraction);
    }
    internal static ReplayOwnerAnimation Read(BinaryReader r)
    {
        static ReplayOwnerAnimSlot Slot(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), (AnimFlags)r.ReadUInt16(), r.ReadInt32());
        static ReplayOwnerAnimGroup Group(BinaryReader r) => new(r.ReadInt16(), r.ReadByte());
        return new(Slot(r), Slot(r), Group(r), Group(r), Group(r), Group(r), r.ReadInt32(), r.ReadInt32(), r.ReadSingle());
    }
}
internal readonly record struct ReplayOwnerVisual(bool Present, byte Hunter, byte Weapon, byte Recolor,
    float Alpha, ReplayOwnerAnimation Animation, CosmeticSurface Surface)
{
    internal const int Bytes = 121;
    internal bool Valid => !Present || Hunter < 7 && Weapon < 9 && Recolor < 16
        && float.IsFinite(Alpha) && Alpha is >= 0 and <= 1 && Animation.Valid
        && Surface.Skin is >= 0 and <= 255 && Surface.Effect is >= 0 and <= 255
        && Finite(Surface.Time) && Finite(Surface.Primary.X) && Finite(Surface.Primary.Y) && Finite(Surface.Primary.Z)
        && Finite(Surface.Secondary.X) && Finite(Surface.Secondary.Y) && Finite(Surface.Secondary.Z)
        && Finite(Surface.Intensity) && Finite(Surface.Pulse) && Finite(Surface.Scroll) && Finite(Surface.Dissolve);
    private static bool Finite(float value) => float.IsFinite(value) && MathF.Abs(value) <= 1_000_000;
    internal void Write(BinaryWriter w)
    {
        w.Write(Present); w.Write(Hunter); w.Write(Weapon); w.Write(Recolor); w.Write(Alpha); Animation.Write(w);
        w.Write(Surface.Skin); w.Write(Surface.Effect); w.Write(Surface.Time);
        w.Write(Surface.Primary.X); w.Write(Surface.Primary.Y); w.Write(Surface.Primary.Z);
        w.Write(Surface.Secondary.X); w.Write(Surface.Secondary.Y); w.Write(Surface.Secondary.Z);
        w.Write(Surface.Intensity); w.Write(Surface.Pulse); w.Write(Surface.Scroll); w.Write(Surface.Dissolve); w.Write(Surface.PreservePalette);
    }
    internal static ReplayOwnerVisual Read(BinaryReader r)
        => new(r.ReadBoolean(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadSingle(), ReplayOwnerAnimation.Read(r),
            new(r.ReadInt32(), r.ReadInt32(), r.ReadSingle(), new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadBoolean()));
}
