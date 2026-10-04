using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MphRead.Mods.MapGen
{
    /// <summary>
    /// Compiles authored UV scrolling into the native MPH texture-coordinate animation format.
    /// Static maps intentionally keep the historical 24-byte empty payload.
    /// </summary>
    internal static class MapUvAnimation
    {
        internal const int NativeFramesPerSecond = 30;
        internal const int MinLoopFrames = 30;
        internal const int MaxLoopFrames = 6000;
        internal const float MaxScrollSpeed = 4f;
        internal const int MaxTranslationEntries = ushort.MaxValue;

        private sealed record Track(MapMaterial Material, MapMaterialAnimation Animation,
            ushort TranslateSIndex, ushort TranslateTIndex, ushort TranslateSLength, ushort TranslateTLength);

        internal static bool IsAnimated(MapMaterial material) => material.Animation != null;

        internal static bool NativeNameFits(string name) =>
            !String.IsNullOrEmpty(name) && name.Length <= 31 && name.All(c => c <= byte.MaxValue);

        internal static bool IsSeamless(float speed, int loopFrames)
        {
            if (speed == 0) return true;
            float tiles = speed * loopFrames / NativeFramesPerSecond;
            return MathF.Abs(tiles - MathF.Round(tiles)) <= 0.001f;
        }

        internal static bool TryGetGroupFrameCount(IReadOnlyList<MapMaterial> materials, out int frameCount)
        {
            frameCount = 0;
            foreach (MapMaterial material in materials)
            {
                MapMaterialAnimation? animation = material.Animation;
                if (animation == null) continue;
                if (animation.LoopFrames < MinLoopFrames || animation.LoopFrames > MaxLoopFrames) return false;
                if (frameCount == 0)
                {
                    frameCount = animation.LoopFrames;
                    continue;
                }
                int gcd = GreatestCommonDivisor(frameCount, animation.LoopFrames);
                long lcm = (long)frameCount / gcd * animation.LoopFrames;
                if (lcm > MaxLoopFrames) return false;
                frameCount = (int)lcm;
            }
            return frameCount > 0;
        }

        internal static int TranslationEntryCount(IReadOnlyList<MapMaterial> materials, int groupFrames)
        {
            long total = 0;
            foreach (MapMaterial material in materials)
            {
                MapMaterialAnimation? animation = material.Animation;
                if (animation?.UvScroll is not { Length: 2 }) continue;
                total += animation.UvScroll[0] == 0 ? 1 : groupFrames;
                total += animation.UvScroll[1] == 0 ? 1 : groupFrames;
                if (total > Int32.MaxValue) return Int32.MaxValue;
            }
            return (int)total;
        }

        public static byte[] Build(IReadOnlyList<MapMaterial> materials)
        {
            MapMaterial[] animated = materials.Where(IsAnimated).ToArray();
            if (animated.Length == 0) return new byte[24];
            if (!TryGetGroupFrameCount(animated, out int frameCount))
                throw new MapAuthoringException("FP-MAP-001",
                    "Animated material loop periods cannot share a native cycle of 6000 frames or less.");

            int translationEntries = TranslationEntryCount(animated, frameCount);
            if (translationEntries > MaxTranslationEntries)
                throw new MapAuthoringException("FP-MAP-003",
                    "Animated material translation lookup-table budget exceeded.");

            var translations = new List<int>(translationEntries);
            var tracks = new List<Track>(animated.Length);
            foreach (MapMaterial material in animated)
            {
                MapMaterialAnimation animation = material.Animation!;
                ushort sIndex = checked((ushort)translations.Count);
                ushort sLength = AppendAxis(translations, animation, axis: 0, frameCount);
                ushort tIndex = checked((ushort)translations.Count);
                ushort tLength = AppendAxis(translations, animation, axis: 1, frameCount);
                tracks.Add(new Track(material, animation, sIndex, tIndex, sLength, tLength));
            }

            const int headerSize = 24;
            const int offsetTableBytes = 5 * sizeof(uint);
            const int texcoordGroupSize = 28;
            const int scaleLutBytes = sizeof(int);
            const int rotateLutBytes = sizeof(ushort);
            int nodeOffsets = headerSize;
            int unusedOffsets = nodeOffsets + sizeof(uint);
            int materialOffsets = unusedOffsets + sizeof(uint);
            int texcoordOffsets = materialOffsets + sizeof(uint);
            int textureOffsets = texcoordOffsets + sizeof(uint);
            int groupOffset = headerSize + offsetTableBytes;
            int scaleOffset = groupOffset + texcoordGroupSize;
            int rotateOffset = scaleOffset + scaleLutBytes;
            int translateOffset = Align4(rotateOffset + rotateLutBytes);
            int animationOffset = translateOffset + translations.Count * sizeof(int);

            using var stream = new MemoryStream(animationOffset + tracks.Count * 60);
            using var writer = new BinaryWriter(stream);

            // AnimationHeader
            writer.Write((uint)nodeOffsets);
            writer.Write((uint)unusedOffsets);
            writer.Write((uint)materialOffsets);
            writer.Write((uint)texcoordOffsets);
            writer.Write((uint)textureOffsets);
            writer.Write((ushort)1);
            writer.Write((ushort)0);

            // One entry in each group-offset table. Only texcoord has a group.
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((uint)groupOffset);
            writer.Write(0u);

            // RawTexcoordAnimationGroup
            writer.Write((uint)frameCount);
            writer.Write((uint)scaleOffset);
            writer.Write((uint)rotateOffset);
            writer.Write((uint)translateOffset);
            writer.Write((uint)tracks.Count);
            writer.Write((uint)animationOffset);
            writer.Write((ushort)0);
            writer.Write((ushort)0);

            // Shared constant scale and rotation LUTs.
            writer.Write(Fixed.ToInt(1f));
            writer.Write((ushort)0);
            while (stream.Position < translateOffset) writer.Write((byte)0);

            foreach (int value in translations) writer.Write(value);

            foreach (Track track in tracks)
            {
                WriteNativeName(writer, track.Material.Name, 32);
                writer.Write((byte)0); // ScaleBlendS
                writer.Write((byte)0); // ScaleBlendT
                writer.Write((ushort)1); // ScaleLutLengthS
                writer.Write((ushort)1); // ScaleLutLengthT
                writer.Write((ushort)0); // ScaleLutIndexS
                writer.Write((ushort)0); // ScaleLutIndexT
                writer.Write((byte)0); // RotateBlendZ
                writer.Write((byte)0xFF); // Unused2B in MPH
                writer.Write((ushort)1); // RotateLutLengthZ
                writer.Write((ushort)0); // RotateLutIndexZ
                writer.Write((byte)(track.TranslateSLength > 1 ? 1 : 0));
                writer.Write((byte)(track.TranslateTLength > 1 ? 1 : 0));
                writer.Write(track.TranslateSLength);
                writer.Write(track.TranslateTLength);
                writer.Write(track.TranslateSIndex);
                writer.Write(track.TranslateTIndex);
                writer.Write((ushort)0);
            }

            return stream.ToArray();
        }

        private static ushort AppendAxis(List<int> values, MapMaterialAnimation animation, int axis, int groupFrames)
        {
            float speed = animation.UvScroll[axis];
            if (speed == 0)
            {
                values.Add(0);
                return 1;
            }

            for (int frame = 0; frame < groupFrames; frame++)
            {
                int localFrame = (frame + animation.PhaseFrames) % animation.LoopFrames;
                values.Add(Fixed.ToInt(speed * localFrame / NativeFramesPerSecond));
            }
            return checked((ushort)groupFrames);
        }

        private static int GreatestCommonDivisor(int left, int right)
        {
            while (right != 0)
            {
                int next = left % right;
                left = right;
                right = next;
            }
            return Math.Abs(left);
        }

        private static int Align4(int value) => (value + 3) & ~3;

        private static void WriteNativeName(BinaryWriter writer, string value, int length)
        {
            byte[] bytes = new byte[length];
            int count = Math.Min(value.Length, length - 1);
            for (int i = 0; i < count; i++) bytes[i] = (byte)value[i];
            writer.Write(bytes);
        }
    }
}
