using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MphRead.Mods.Import.Retro
{
    // A Retro animation character set (ANCS: Metroid Prime v6 characters, Echoes v10): its characters (model, skin,
    // skeleton, animation names, PAS states, particles, effects) and its animation set (named meta-animation trees whose
    // leaves play ANIM resources). Port of ancs_dump.py's parse (layout per PrimeWorldEditor's CAnimSetLoader, MIT, and
    // the Retro Modding Wiki ANCS page) and of prime_model.py's character() lookup.
    public sealed class Ancs
    {
        public sealed class Character
        {
            public uint Id;
            public int Version;
            public string Name = "";
            public uint Cmdl, Cskr, Cinf;
            public readonly List<(uint Index, string Name)> AnimNames = new();
            public readonly List<(uint Id, uint Parms, uint Anims)> PasStates = new();
            public readonly Dictionary<string, List<uint>> Particles = new();
            public readonly List<string> Aabbs = new();
            public readonly List<(string Name, List<(string Name, string Type, uint Id, string Bone)> Components)> Effects = new();
            public (uint, uint) Frozen;
            public List<uint>? UsedAnims;
            public int EchoesTailAt = -1;
        }

        // a meta-animation tree: "play" leaves (an ANIM id), "blend" / "phaseblend" (two children), "random" (children
        // with weights), "seq" (children in order)
        public sealed class MetaAnim
        {
            public string Kind = "";
            public uint AnimId, PrimId;
            public string PrimName = "";
            // Raw source descriptors only. Retention is not evaluation authority.
            public int SourceOffset = -1, SourceLength;
            public uint SourceType;
            public int PlayParametersOffset = -1, BlendParametersOffset = -1;
            public uint? PlayTimeBits, PlayTimeTypeRaw, BlendFactorBits;
            public byte? BlendFlagRaw;
            public readonly List<MetaAnim> Children = new();
            public readonly List<uint> Weights = new();
        }

        // Raw transition layout. Flags/bytes remain opaque data, not a policy.
        public sealed class MetaTrans
        {
            public int SourceOffset=-1,SourceLength,ParametersOffset=-1;
            public uint SourceType;
            public MetaAnim? Animation;
            public uint? DurationBits,DurationTypeRaw,FlagsRaw;
            public byte? UnknownRaw,RunARaw;
        }
        public sealed record Transition(uint Id,uint AnimA,uint AnimB,MetaTrans Meta,int SourceOffset,int SourceLength);
        public sealed record HalfTransition(uint Anim,MetaTrans Meta,int SourceOffset,int SourceLength);

        public sealed class AnimSet
        {
            public int Version;
            public int SourceOffset = -1, SourceLength;
            public readonly List<(string Name, MetaAnim Anim)> Anims = new();
            public readonly List<(uint Anim, uint Evnt)> Resources = new();
            public readonly List<Transition> Transitions=new();
            public MetaTrans? DefaultTransition;
            public readonly List<HalfTransition> HalfTransitions=new();
        }

        public readonly List<Character> Characters = new();
        public AnimSet? Set;

        sealed class Reader
        {
            public readonly byte[] B;
            public int O;
            public int MetaNodes,TransitionNodes;
            public Reader(byte[] b, int o = 0) { B = b; O = o; }
            public void Require(int count)
            { if(count<0||O<0||O>B.Length||count>B.Length-O)throw new InvalidDataException("ANCS read exceeds source bounds."); }
            public byte U8() { Require(1); byte v = B[O]; O++; return v; }
            public ushort U16() { Require(2); ushort v = Be.U16(B, O); O += 2; return v; }
            public uint U32() { Require(4); uint v = Be.U32(B, O); O += 4; return v; }
            public float F32() { Require(4); float v = Be.F32(B, O); O += 4; return v; }
            public string S()
            {
                Require(1);int maximum=Math.Min(4097,B.Length-O),end=Array.IndexOf(B,(byte)0,O,maximum);
                if(end<0)throw new InvalidDataException("ANCS string is unterminated or exceeds4096 bytes.");
                string value=Encoding.Latin1.GetString(B,O,end-O);O=end+1;return value;
            }
            public void Skip(int n) { Require(n); O += n; }
            public string Tag() { Require(4); string v = Encoding.Latin1.GetString(B, O, 4); O += 4; return v; }
        }

        // prime_model.py's character(): (CMDL, CSKR, CINF) of the named character, walking the char nodes by their
        // headers only (each next node is found by its index + version)
        public static (uint Cmdl, uint Cskr, uint Cinf) FindCharacter(byte[] a, string want)
        {
            int o = 4;
            int count = checked((int)Be.U32(a, o)); o += 4;
            Span<byte> next = stackalloc byte[6];
            for (int i = 0; i < count; i++)
            {
                ushort ver = Be.U16(a, o + 4);
                string name = Be.CString(a, o + 6, out int e);
                int o2 = e + 1;
                if (name == want)
                {
                    return (Be.U32(a, o2), Be.U32(a, o2 + 4), Be.U32(a, o2 + 8));
                }
                next[0] = (byte)((i + 1) >> 24); next[1] = (byte)((i + 1) >> 16); next[2] = (byte)((i + 1) >> 8); next[3] = (byte)(i + 1);
                next[4] = (byte)(ver >> 8); next[5] = (byte)ver;
                o = Be.Index(a, next, o2 + 12);
            }
            throw new KeyNotFoundException($"character {want}");
        }

        static List<(uint, uint, uint)> Pas(Reader r)
        {
            r.Require(4);
            if (r.B.AsSpan(r.O, 4).SequenceEqual("PAS4"u8) == false)
            {
                throw new InvalidDataException($"expected PAS4 at {r.O:x}");
            }
            r.Skip(4);
            int n = checked((int)r.U32()); r.U32();
            var states = new List<(uint, uint, uint)>();
            for (int i = 0; i < n; i++)
            {
                uint sid = r.U32(), np = r.U32(), na = r.U32();
                int skip = 0;
                for (uint k = 0; k < np; k++)
                {
                    uint t = r.U32(); r.Skip(8);
                    if (t is 0 or 1 or 2 or 4) { r.Skip(8); skip += 4; }
                    else if (t == 3) { r.Skip(2); skip += 1; }
                }
                r.Skip(checked((int)((4 + skip) * na)));
                states.Add((sid, np, na));
            }
            return states;
        }

        static List<uint> U32List(Reader r)
        {
            uint n = r.U32();
            var l = new List<uint>();
            for (uint i = 0; i < n; i++) l.Add(r.U32());
            return l;
        }

        static Character Char(Reader r)
        {
            var c = new Character { Id = r.U32() };
            int ver = r.U16();
            c.Version = ver;
            c.Name = r.S();
            c.Cmdl = r.U32(); c.Cskr = r.U32(); c.Cinf = r.U32();
            uint n = r.U32();
            for (uint i = 0; i < n; i++)
            {
                uint idx = r.U32();
                if (ver < 10) r.S();
                c.AnimNames.Add((idx, r.S()));
            }
            c.PasStates.AddRange(Pas(r));
            c.Particles["PART"] = U32List(r);
            c.Particles["SWHC"] = U32List(r);
            if (ver >= 6) c.Particles["unk"] = U32List(r);
            c.Particles["ELSC"] = U32List(r);
            if (ver >= 10) c.Particles["SPSC"] = U32List(r);
            r.U32();
            if (ver >= 10) r.U32();
            n = r.U32();
            for (uint i = 0; i < n; i++)
            {
                c.Aabbs.Add(r.S());
                r.Skip(24);
            }
            n = r.U32();
            for (uint i = 0; i < n; i++)
            {
                string nm = r.S();
                uint nc = r.U32();
                var comps = new List<(string, string, uint, string)>();
                for (uint k = 0; k < nc; k++)
                {
                    string cn = r.S();
                    string typ = r.Tag();
                    uint pid = r.U32();
                    string bone = ver >= 10 ? r.U32().ToString() : r.S();
                    r.Skip(12);
                    comps.Add((cn, typ, pid, bone));
                }
                c.Effects.Add((nm, comps));
            }
            c.Frozen = (r.U32(), r.U32());
            if (ver >= 5) c.UsedAnims = U32List(r);
            if (ver >= 10)
            {
                // spatial primitives (Echoes): not decoded (PWE: an "Unknown" block)
                c.EchoesTailAt = r.O;
            }
            return c;
        }

        // Supported private graph bounds are explicit. Counts are checked before
        // adding children; raw parameter bits (including NaN/-0) are retained and
        // are never treated as finite playback/evaluation inputs implicitly.
        const int MaxDepth = 64;
        const int MaxMetaNodes = 4096;
        static MetaAnim Meta(Reader r, int depth = 0)
        {
            if(depth>MaxDepth||++r.MetaNodes>MaxMetaNodes)throw new InvalidDataException("ANCS graph depth/node budget.");
            int start=r.O;uint type=r.U32();
            var m=new MetaAnim{SourceOffset=start,SourceType=type};
            if(type==0)
            {
                m.Kind="play";m.AnimId=r.U32();m.PrimId=r.U32();m.PrimName=r.S();
                m.PlayParametersOffset=r.O;m.PlayTimeBits=r.U32();m.PlayTimeTypeRaw=r.U32();
            }
            else if(type is 1 or 2)
            {
                m.Kind=type==1?"blend":"phaseblend";
                m.Children.Add(Meta(r,depth+1));m.Children.Add(Meta(r,depth+1));
                m.BlendParametersOffset=r.O;m.BlendFactorBits=r.U32();m.BlendFlagRaw=r.U8();
            }
            else if(type is 3 or 4)
            {
                m.Kind=type==3?"random":"seq";uint count=r.U32();
                int minimum=type==3?8:4;
                if(count>MaxMetaNodes-r.MetaNodes||count>(r.B.Length-r.O)/minimum)
                    throw new InvalidDataException("ANCS graph child count/source budget.");
                for(uint i=0;i<count;i++)
                {m.Children.Add(Meta(r,depth+1));if(type==3)m.Weights.Add(r.U32());}
            }
            else throw new InvalidDataException($"meta anim type {type} at {start:x}");
            m.SourceLength=checked(r.O-start);return m;
        }

        static MetaTrans Trans(Reader r)
        {
            if(++r.TransitionNodes>4096)throw new InvalidDataException("ANCS transition node budget.");
            int start=r.O;uint type=r.U32();var value=new MetaTrans{SourceOffset=start,SourceType=type};
            if(type==0)value.Animation=Meta(r);
            else if(type is 1 or 2)
            {
                value.ParametersOffset=r.O;value.DurationBits=r.U32();value.DurationTypeRaw=r.U32();
                value.UnknownRaw=r.U8();value.RunARaw=r.U8();value.FlagsRaw=r.U32();
            }
            else if(type!=3)throw new InvalidDataException($"meta trans {type} at {start:x}");
            value.SourceLength=checked(r.O-start);return value;
        }

        // the leaves of a meta-animation, in order
        public static List<MetaAnim> Prims(MetaAnim m)
        {
            var l = new List<MetaAnim>();
            void Walk(MetaAnim x)
            {
                if (x.Kind == "play") { l.Add(x); return; }
                foreach (MetaAnim c in x.Children) Walk(c);
            }
            Walk(m);
            return l;
        }

        static AnimSet ReadAnimSet(Reader r, bool echoes)
        {
            int sourceOffset=r.O;
            var set = new AnimSet { Version = r.U16(), SourceOffset=sourceOffset };
            int ver = set.Version;
            uint n = r.U32();
            if(n>4096)throw new InvalidDataException("ANCS named graph count budget.");
            for (uint i = 0; i < n; i++)
            {
                string nm = r.S();
                set.Anims.Add((nm, Meta(r)));
            }
            if (ver >= 2)
            {
                uint nt = r.U32();
                if(nt>4096||nt>(r.B.Length-r.O)/16)throw new InvalidDataException("ANCS full transition count budget.");
                for(uint i=0;i<nt;i++)
                {
                    int start=r.O;uint id=r.U32(),a=r.U32(),b=r.U32();var meta=Trans(r);
                    set.Transitions.Add(new(id,a,b,meta,start,checked(r.O-start)));
                }
            }
            set.DefaultTransition=Trans(r);
            if (ver >= 3)
            {
                uint nb = r.U32();
                for (uint i = 0; i < nb; i++) { r.U32(); r.F32(); r.F32(); }
                r.F32(); r.F32();
            }
            if (ver >= 4)
            {
                uint nh = r.U32();
                if(nh>4096||nh>(r.B.Length-r.O)/8)throw new InvalidDataException("ANCS half transition count budget.");
                for(uint i=0;i<nh;i++)
                {int start=r.O;uint anim=r.U32();var meta=Trans(r);set.HalfTransitions.Add(new(anim,meta,start,checked(r.O-start)));}
            }
            if (!echoes)
            {
                uint nr = r.U32();
                for (uint i = 0; i < nr; i++) set.Resources.Add((r.U32(), r.U32()));
            }
            set.SourceLength=checked(r.O-sourceOffset);return set;
        }

        static bool IsAnimName(string s, bool dash = false)
        {
            if (s.Length == 0) return false;
            foreach (char ch in s)
            {
                if (!(ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or ' ' || dash && ch == '-')) return false;
            }
            return true;
        }

        public static Ancs Parse(byte[] b)
        {
            if(b==null||b.Length<8||b.Length>32*1024*1024)throw new InvalidDataException("ANCS supported source length budget.");
            var ancs = new Ancs();
            var r = new Reader(b);
            if (r.U32() != 0x00010001) throw new InvalidDataException("not an ANCS");
            uint n = r.U32();
            if(n>1024)throw new InvalidDataException("ANCS character count budget.");
            bool echoes = false;
            Span<byte> next = stackalloc byte[6];
            for (uint i = 0; i < n; i++)
            {
                Character c = Char(r);
                echoes = c.Version >= 10;
                ancs.Characters.Add(c);
                if (echoes && i + 1 < n)
                {
                    // Echoes char tail (anim AABBs + spatial prims) not decoded: jump to the next char node
                    next[0] = (byte)((i + 1) >> 24); next[1] = (byte)((i + 1) >> 16); next[2] = (byte)((i + 1) >> 8); next[3] = (byte)(i + 1);
                    next[4] = (byte)(c.Version >> 8); next[5] = (byte)c.Version;
                    r.O = Be.Index(b, next, r.O);
                }
            }
            if (echoes)
            {
                // find the animation set: the first offset where it parses cleanly with plausible names; a second pass also
                // allows '-' (the Echoes commando ANCS 8169653a has "B-quick180turn_pirate"), so ANCSs found by the first
                // pass resolve exactly as before
                int scanCandidates=0;
                for (int pass = 0; pass < 2; pass++)
                for (int o = r.O; o < b.Length - 8; o++)
                {
                    if (b[o] != 0 || b[o + 1] is not (2 or 3 or 4 or 5)) continue;
                    if(++scanCandidates>4096)throw new InvalidDataException("ANCS supported candidate scan budget.");
                    AnimSet set;
                    try
                    {
                        set = ReadAnimSet(new Reader(b, o), true);
                    }
                    catch (Exception e) when (e is InvalidDataException or ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException)
                    {
                        continue;
                    }
                    if (set.Anims.Count > 0 && set.Anims.TrueForAll(a => IsAnimName(a.Name, dash: pass == 1)))
                    {
                        ancs.Set = set;
                        return ancs;
                    }
                }
                return ancs;
            }
            ancs.Set = ReadAnimSet(r, false);
            return ancs;
        }
    }
}
