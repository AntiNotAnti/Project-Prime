using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MphRead.Mods.MapGen
{
    internal readonly record struct MapFlipbookBinding(
        ushort MaterialId, ushort[] TextureIds, ushort[] PaletteIds);

    /// <summary>
    /// Compiles authored UV motion and texture flipbooks into the native MPH animation format.
    /// Static maps intentionally keep the historical 24-byte empty payload.
    /// </summary>
    internal static class MapUvAnimation
    {
        internal const int NativeFramesPerSecond = 30;
        internal const int MinLoopFrames = 30;
        internal const int MaxLoopFrames = 6000;
        internal const float MaxScrollSpeed = 4f;
        internal const float MaxRotationSpeed = 1440f;
        internal const float MaxScale = 8f;
        internal const int MaxLutEntries = ushort.MaxValue;
        internal const int MaxFlipbookImages = 64;

        private sealed record Track(
            MapMaterial Material,
            MapMaterialAnimation Animation,
            ushort ScaleSIndex,
            ushort ScaleTIndex,
            ushort ScaleSLength,
            ushort ScaleTLength,
            ushort RotateIndex,
            ushort RotateLength,
            ushort TranslateSIndex,
            ushort TranslateTIndex,
            ushort TranslateSLength,
            ushort TranslateTLength);

        private sealed record TextureTrack(
            MapMaterial Material,
            ushort StartIndex,
            ushort Count,
            ushort MinimumPaletteId,
            ushort MaterialId,
            ushort MinimumTextureId);

        internal static bool IsAnimated(MapMaterial material) => material.Animation != null;

        internal static bool HasUvAnimation(MapMaterial material)
        {
            MapMaterialAnimation? animation = material.Animation;
            if (animation == null) return false;
            bool scroll = animation.UvScroll is { Length: 2 }
                && (animation.UvScroll[0] != 0 || animation.UvScroll[1] != 0);
            bool scale = animation.UvScale is { Length: 2 }
                && (animation.UvScale[0] != 1 || animation.UvScale[1] != 1);
            bool pulse = animation.UvScalePulse is { Length: 2 }
                && (animation.UvScalePulse[0] != 0 || animation.UvScalePulse[1] != 0);
            return scroll || scale || pulse || animation.UvRotationDegreesPerSecond != 0;
        }

        internal static bool NativeNameFits(string name) =>
            !String.IsNullOrEmpty(name) && name.Length <= 31 && name.All(c => c <= byte.MaxValue);

        internal static bool IsSeamless(float speed, int loopFrames)
        {
            if (speed == 0) return true;
            float tiles = speed * loopFrames / NativeFramesPerSecond;
            return MathF.Abs(tiles - MathF.Round(tiles)) <= 0.001f;
        }

        internal static bool IsRotationSeamless(float degreesPerSecond, int loopFrames)
        {
            if (degreesPerSecond == 0) return true;
            float turns = degreesPerSecond * loopFrames
                / (NativeFramesPerSecond * 360f);
            return MathF.Abs(turns - MathF.Round(turns)) <= 0.001f;
        }

        internal static bool IsFlipbookSeamless(MapMaterialAnimation animation)
        {
            int imageCount = 1 + (animation.FlipbookFrames?.Count ?? 0);
            if (imageCount <= 1) return true;
            if (animation.FlipbookHoldFrames <= 0) return false;
            int cycle = imageCount * animation.FlipbookHoldFrames;
            return animation.LoopFrames % cycle == 0;
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

        internal static int ScaleEntryCount(IReadOnlyList<MapMaterial> materials, int groupFrames)
        {
            long total = 0;
            foreach (MapMaterial material in materials)
            {
                MapMaterialAnimation? animation = material.Animation;
                if (animation?.UvScalePulse is not { Length: 2 }) continue;
                total += animation.UvScalePulse[0] == 0 ? 1 : groupFrames;
                total += animation.UvScalePulse[1] == 0 ? 1 : groupFrames;
                if (total > Int32.MaxValue) return Int32.MaxValue;
            }
            return (int)total;
        }

        internal static int RotationEntryCount(IReadOnlyList<MapMaterial> materials, int groupFrames)
        {
            long total = 0;
            foreach (MapMaterial material in materials)
            {
                MapMaterialAnimation? animation = material.Animation;
                if (animation == null) continue;
                total += animation.UvRotationDegreesPerSecond == 0 ? 1 : groupFrames;
                if (total > Int32.MaxValue) return Int32.MaxValue;
            }
            return (int)total;
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

        internal static int FlipbookEntryCount(IReadOnlyList<MapMaterial> materials, int groupFrames)
        {
            long total = 0;
            foreach (MapMaterial material in materials)
            {
                if (material.Animation?.FlipbookFrames?.Count > 0)
                {
                    total += groupFrames;
                    if (total > Int32.MaxValue) return Int32.MaxValue;
                }
            }
            return (int)total;
        }

        public static byte[] Build(IReadOnlyList<MapMaterial> materials,
            IReadOnlyDictionary<string, MapFlipbookBinding>? flipbooks = null)
        {
            MapMaterial[] animated = materials.Where(IsAnimated).ToArray();
            if (animated.Length == 0) return new byte[24];
            if (!TryGetGroupFrameCount(animated, out int frameCount))
                throw new MapAuthoringException("FP-MAP-001",
                    "Animated material loop periods cannot share a native cycle of 6000 frames or less.");

            int scaleEntries = ScaleEntryCount(animated, frameCount);
            int rotationEntries = RotationEntryCount(animated, frameCount);
            int translationEntries = TranslationEntryCount(animated, frameCount);
            int flipbookEntries = FlipbookEntryCount(animated, frameCount);
            if (scaleEntries > MaxLutEntries || rotationEntries > MaxLutEntries
                || translationEntries > MaxLutEntries || flipbookEntries > MaxLutEntries)
            {
                throw new MapAuthoringException("FP-MAP-003",
                    "Animated material lookup-table budget exceeded.");
            }

            var scales = new List<int>(scaleEntries);
            var rotations = new List<ushort>(rotationEntries);
            var translations = new List<int>(translationEntries);
            MapMaterial[] uvAnimated = animated.Where(HasUvAnimation).ToArray();
            var tracks = new List<Track>(uvAnimated.Length);
            foreach (MapMaterial material in uvAnimated)
            {
                MapMaterialAnimation animation = material.Animation!;
                RequireShape(animation);

                ushort scaleSIndex = checked((ushort)scales.Count);
                ushort scaleSLength = AppendScale(scales, animation, axis: 0, frameCount);
                ushort scaleTIndex = checked((ushort)scales.Count);
                ushort scaleTLength = AppendScale(scales, animation, axis: 1, frameCount);

                ushort rotateIndex = checked((ushort)rotations.Count);
                ushort rotateLength = AppendRotation(rotations, animation, frameCount);

                ushort translateSIndex = checked((ushort)translations.Count);
                ushort translateSLength = AppendTranslation(translations, animation, axis: 0, frameCount);
                ushort translateTIndex = checked((ushort)translations.Count);
                ushort translateTLength = AppendTranslation(translations, animation, axis: 1, frameCount);

                tracks.Add(new Track(material, animation,
                    scaleSIndex, scaleTIndex, scaleSLength, scaleTLength,
                    rotateIndex, rotateLength,
                    translateSIndex, translateTIndex, translateSLength, translateTLength));
            }

            var frameIndices = new List<ushort>(flipbookEntries);
            var textureIds = new List<ushort>(flipbookEntries);
            var paletteIds = new List<ushort>(flipbookEntries);
            var textureTracks = new List<TextureTrack>();
            foreach (MapMaterial material in materials)
            {
                MapMaterialAnimation? animation = material.Animation;
                if (animation?.FlipbookFrames?.Count is not > 0) continue;
                if (flipbooks == null || !flipbooks.TryGetValue(material.Name, out MapFlipbookBinding binding))
                    throw new MapAuthoringException("FP-MAP-001",
                        $"Flipbook texture frames were not packed for material {material.Name}.");
                if (binding.TextureIds.Length != binding.PaletteIds.Length
                    || binding.TextureIds.Length != animation.FlipbookFrames!.Count + 1)
                {
                    throw new MapAuthoringException("FP-MAP-001",
                        $"Flipbook texture frame mapping is invalid for material {material.Name}.");
                }

                ushort start = checked((ushort)frameIndices.Count);
                for (int frame = 0; frame < frameCount; frame++)
                {
                    int localFrame = LocalFrame(animation, frame);
                    int image = (localFrame / animation.FlipbookHoldFrames) % binding.TextureIds.Length;
                    frameIndices.Add((ushort)frame);
                    textureIds.Add(binding.TextureIds[image]);
                    paletteIds.Add(binding.PaletteIds[image]);
                }

                textureTracks.Add(new TextureTrack(material, start, checked((ushort)frameCount),
                    binding.PaletteIds.Min(), binding.MaterialId, binding.TextureIds.Min()));
            }

            const int headerSize = 24;
            const int offsetTableBytes = 5 * sizeof(uint);
            const int texcoordGroupSize = 28;
            const int textureGroupSize = 32;
            int nodeOffsets = headerSize;
            int unusedOffsets = nodeOffsets + sizeof(uint);
            int materialOffsets = unusedOffsets + sizeof(uint);
            int texcoordOffsets = materialOffsets + sizeof(uint);
            int textureOffsets = texcoordOffsets + sizeof(uint);
            int texcoordGroupOffset = tracks.Count > 0 ? headerSize + offsetTableBytes : 0;
            int scaleOffset = texcoordGroupOffset == 0
                ? headerSize + offsetTableBytes
                : texcoordGroupOffset + texcoordGroupSize;
            int rotateOffset = scaleOffset + scales.Count * sizeof(int);
            int translateOffset = Align4(rotateOffset + rotations.Count * sizeof(ushort));
            int texcoordAnimationOffset = translateOffset + translations.Count * sizeof(int);
            int afterTexcoord = texcoordAnimationOffset + tracks.Count * 60;
            int textureGroupOffset = textureTracks.Count > 0 ? Align4(afterTexcoord) : 0;
            int frameIndexOffset = textureGroupOffset == 0 ? 0 : textureGroupOffset + textureGroupSize;
            int textureIdOffset = frameIndexOffset + frameIndices.Count * sizeof(ushort);
            int paletteIdOffset = textureIdOffset + textureIds.Count * sizeof(ushort);
            int textureAnimationOffset = paletteIdOffset + paletteIds.Count * sizeof(ushort);

            using var stream = new MemoryStream(
                textureTracks.Count == 0 ? afterTexcoord : textureAnimationOffset + textureTracks.Count * 44);
            using var writer = new BinaryWriter(stream);

            // AnimationHeader
            writer.Write((uint)nodeOffsets);
            writer.Write((uint)unusedOffsets);
            writer.Write((uint)materialOffsets);
            writer.Write((uint)texcoordOffsets);
            writer.Write((uint)textureOffsets);
            writer.Write((ushort)1);
            writer.Write((ushort)0);

            // One entry in each group-offset table.
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((uint)texcoordGroupOffset);
            writer.Write((uint)textureGroupOffset);

            if (tracks.Count > 0)
            {
                // RawTexcoordAnimationGroup
                writer.Write((uint)frameCount);
                writer.Write((uint)scaleOffset);
                writer.Write((uint)rotateOffset);
                writer.Write((uint)translateOffset);
                writer.Write((uint)tracks.Count);
                writer.Write((uint)texcoordAnimationOffset);
                writer.Write((ushort)0);
                writer.Write((ushort)0);

                foreach (int value in scales) writer.Write(value);
                foreach (ushort value in rotations) writer.Write(value);
                while (stream.Position < translateOffset) writer.Write((byte)0);
                foreach (int value in translations) writer.Write(value);

                foreach (Track track in tracks)
                {
                    WriteNativeName(writer, track.Material.Name, 32);
                    writer.Write((byte)(track.ScaleSLength > 1 ? 1 : 0));
                    writer.Write((byte)(track.ScaleTLength > 1 ? 1 : 0));
                    writer.Write(track.ScaleSLength);
                    writer.Write(track.ScaleTLength);
                    writer.Write(track.ScaleSIndex);
                    writer.Write(track.ScaleTIndex);
                    writer.Write((byte)(track.RotateLength > 1 ? 1 : 0));
                    writer.Write((byte)0xFF);
                    writer.Write(track.RotateLength);
                    writer.Write(track.RotateIndex);
                    writer.Write((byte)(track.TranslateSLength > 1 ? 1 : 0));
                    writer.Write((byte)(track.TranslateTLength > 1 ? 1 : 0));
                    writer.Write(track.TranslateSLength);
                    writer.Write(track.TranslateTLength);
                    writer.Write(track.TranslateSIndex);
                    writer.Write(track.TranslateTIndex);
                    writer.Write((ushort)0);
                }
            }

            if (textureTracks.Count > 0)
            {
                while (stream.Position < textureGroupOffset) writer.Write((byte)0);

                // RawTextureAnimationGroup
                writer.Write(checked((ushort)frameCount));
                writer.Write(checked((ushort)frameIndices.Count));
                writer.Write(checked((ushort)textureIds.Count));
                writer.Write(checked((ushort)paletteIds.Count));
                writer.Write(checked((ushort)textureTracks.Count));
                writer.Write((ushort)0);
                writer.Write((uint)frameIndexOffset);
                writer.Write((uint)textureIdOffset);
                writer.Write((uint)paletteIdOffset);
                writer.Write((uint)textureAnimationOffset);
                writer.Write((ushort)0);
                writer.Write((ushort)0);

                foreach (ushort value in frameIndices) writer.Write(value);
                foreach (ushort value in textureIds) writer.Write(value);
                foreach (ushort value in paletteIds) writer.Write(value);
                foreach (TextureTrack track in textureTracks)
                {
                    WriteNativeName(writer, track.Material.Name, 32);
                    writer.Write(track.Count);
                    writer.Write(track.StartIndex);
                    writer.Write(track.MinimumPaletteId);
                    writer.Write(track.MaterialId);
                    writer.Write(track.MinimumTextureId);
                    writer.Write((ushort)0);
                }
            }

            return stream.ToArray();
        }

        private static ushort AppendScale(List<int> values, MapMaterialAnimation animation,
            int axis, int groupFrames)
        {
            float baseline = animation.UvScale[axis];
            float pulse = animation.UvScalePulse[axis];
            if (pulse == 0)
            {
                values.Add(Fixed.ToInt(baseline));
                return 1;
            }

            for (int frame = 0; frame < groupFrames; frame++)
            {
                int localFrame = LocalFrame(animation, frame);
                float phase = MathF.Tau * localFrame / animation.LoopFrames;
                values.Add(Fixed.ToInt(baseline + pulse * MathF.Sin(phase)));
            }
            return checked((ushort)groupFrames);
        }

        private static ushort AppendRotation(List<ushort> values,
            MapMaterialAnimation animation, int groupFrames)
        {
            float speed = animation.UvRotationDegreesPerSecond;
            if (speed == 0)
            {
                values.Add(0);
                return 1;
            }

            for (int frame = 0; frame < groupFrames; frame++)
            {
                int localFrame = LocalFrame(animation, frame);
                float degrees = speed * localFrame / NativeFramesPerSecond;
                float turns = degrees / 360f;
                turns -= MathF.Floor(turns);
                int native = (int)MathF.Round(turns * 65536f) & 0xFFFF;
                values.Add((ushort)native);
            }
            return checked((ushort)groupFrames);
        }

        private static ushort AppendTranslation(List<int> values,
            MapMaterialAnimation animation, int axis, int groupFrames)
        {
            float speed = animation.UvScroll[axis];
            if (speed == 0)
            {
                values.Add(0);
                return 1;
            }

            for (int frame = 0; frame < groupFrames; frame++)
            {
                int localFrame = LocalFrame(animation, frame);
                values.Add(Fixed.ToInt(speed * localFrame / NativeFramesPerSecond));
            }
            return checked((ushort)groupFrames);
        }

        private static int LocalFrame(MapMaterialAnimation animation, int groupFrame) =>
            (groupFrame + animation.PhaseFrames) % animation.LoopFrames;

        private static void RequireShape(MapMaterialAnimation animation)
        {
            if (animation.UvScroll is not { Length: 2 }
                || animation.UvScale is not { Length: 2 }
                || animation.UvScalePulse is not { Length: 2 })
            {
                throw new MapAuthoringException("FP-MAP-001",
                    "Animated material UV vectors must contain exactly two values.");
            }
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
