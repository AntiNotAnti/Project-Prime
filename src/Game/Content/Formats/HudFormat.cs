using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MphRead.Hud
{
    public readonly struct UiAnimParams
    {
        public readonly byte ImageIndex;
        public readonly byte Delay; // sort of
        public readonly ushort Field2; // unused?
        public readonly int Field4; // unused?
        public readonly ushort ParamPa;
        public readonly ushort ParamPb;
        public readonly ushort ParamPc;
        public readonly ushort ParamPd;
    }

    public static class HudFormat
    {
        public readonly struct UiPartHeader
        {
            public readonly int Magic; // always zero
            public readonly int CharDataSize;
            public readonly int PalDataSize;
        }

        public readonly struct ScrDatInfo
        {
            public readonly ushort CharsX;
            public readonly ushort CharsY;
            public readonly int ScrDataSize;
        }

        public struct ScreenData
        {
            public int CharacterId;
            public bool FlipHorizontal;
            public bool FlipVertical;
            public int PaletteId;

            public ScreenData(ushort data)
            {
                CharacterId = data & 0x3FF;
                FlipHorizontal = (data & 0x400) != 0;
                FlipVertical = (data & 0x800) != 0;
                PaletteId = (data & 0xF000) >> 12;
            }
        }

        public static readonly int LayerHeaderSize = Marshal.SizeOf<UiPartHeader>();
        public static readonly int ScreenDataInfoSize = Marshal.SizeOf<ScrDatInfo>();

        public static readonly (int Width, int Height)[,] ObjectDimensions = new (int, int)[3, 4]
        {
            // tiny    small  medium   large
            { (1, 1), (2, 2), (4, 4), (8, 8) }, // square
            { (2, 1), (4, 1), (4, 2), (8, 4) }, // wide
            { (1, 2), (1, 4), (2, 4), (4, 8) }  // tall
        };

        public readonly struct UiObjectHeader
        {
            public readonly ushort FrameCount;
            public readonly ushort ImageCount;
            public readonly ushort Width;
            public readonly ushort Height;
            public readonly int ParamDataSize;
            public readonly int AttrDataSize;
            public readonly int CharDataSize;
            public readonly int PalDataSize;
        }

        public readonly struct RawUiOamAttrs
        {
            public readonly ushort Attr0;
            public readonly ushort Attr1;
            public readonly ushort Attr2;
            public readonly ushort Padding6; // not used to write affine params
        }

        public enum ObjType
        {
            Normal = 0,
            Transparent = 1,
            Window = 2,
            Bitmap = 3
        }

        public enum ObjColors
        {
            Color16 = 0,
            Color256 = 1
        }

        public enum ObjShape
        {
            Square = 0,
            Wide = 1,
            Tall = 2,
            Unused = 3
        }

        public enum ObjSize
        {
            Tiny = 0,
            Small = 1,
            Medium = 2,
            Large = 3
        }

        public struct UiOamAttrs
        {
            public ushort XPos;
            public ushort YPos;
            public ObjType Type;
            public ObjSize Size;
            public ObjShape Shape;
            public ObjColors Colors;
            public bool AffineEnable;
            public bool DoubleSize;
            public int AffineIndex;
            public bool FlipHorizontal;
            public bool FlipVertical;
            public bool Mosaic;
            public int CharacterId;
            public int PaletteId;
            public byte Alpha;
            public byte Priority;

            public UiOamAttrs(RawUiOamAttrs raw)
            {
                Debug.Assert(raw.Padding6 == 0);
                YPos = (ushort)(raw.Attr0 & 0xFF);
                AffineEnable = (raw.Attr0 & (1 << 8)) != 0;
                DoubleSize = (raw.Attr0 & (1 << 9)) != 0;
                Type = (ObjType)((raw.Attr0 & (3 << 10)) >> 10);
                Mosaic = (raw.Attr0 & (1 << 12)) != 0;
                Colors = (raw.Attr0 & (1 << 13)) == 0 ? ObjColors.Color16 : ObjColors.Color256;
                Shape = (ObjShape)(raw.Attr0 >> 14);
                Debug.Assert(Shape != ObjShape.Unused);
                XPos = (ushort)(raw.Attr1 & 0x1FF);
                if (AffineEnable)
                {
                    AffineIndex = (raw.Attr1 & (0x1F << 9)) >> 9;
                    FlipHorizontal = false;
                    FlipVertical = false;
                }
                else
                {
                    AffineIndex = -1;
                    FlipHorizontal = (raw.Attr1 & (1 << 12)) != 0;
                    FlipVertical = (raw.Attr1 & (1 << 13)) != 0;
                }
                Size = (ObjSize)(raw.Attr1 >> 14);
                CharacterId = raw.Attr2 & 0x3FF;
                Priority = (byte)((raw.Attr2 & (3 << 10)) >> 10);
                if (Type == ObjType.Bitmap)
                {
                    PaletteId = -1;
                    Alpha = (byte)(raw.Attr2 >> 12);
                }
                else
                {
                    PaletteId = raw.Attr2 >> 12;
                    Alpha = 0;
                }
                Debug.Assert(!AffineEnable);
                Debug.Assert(Type == ObjType.Normal);
                Debug.Assert(Colors == ObjColors.Color16);
                Debug.Assert((raw.Attr0 & 0x3FFF) == 0); // zeroes except shape
                Debug.Assert((raw.Attr1 & 0x3FFF) == 0); // zeroes except size
                Debug.Assert((raw.Attr2 & 0xFFF) == 0); // zeroes except palette ID
            }
        }

        public static readonly int ObjectHeaderSize = Marshal.SizeOf<UiObjectHeader>();
        public static readonly int AnimationParameterSize = Marshal.SizeOf<UiAnimParams>();
        public static readonly int OamAttributeSize = Marshal.SizeOf<RawUiOamAttrs>();

    }
}
