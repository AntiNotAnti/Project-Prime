using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace MphRead.Mods.MapGen
{
    /// <summary>
    /// Bakes a Quake level's own textures into the pack the packer feeds to
    /// the hardware.
    ///
    /// This used to be tools/bake-textures.py, and the Python is still there
    /// for anyone who wants it, but a conversion that needs a Python with
    /// Pillow on it is not a conversion anybody runs. Everything it needs is
    /// already in this process: the archive is a zip, the decoder is the one
    /// the exporter uses, and the quantiser is fifty lines.
    ///
    /// It is still done ahead of time rather than at load: the Android head
    /// has no image decoder at all -- its STB natives are desktop builds, left
    /// out of the APK on purpose -- so a phone can only copy the bytes.
    /// </summary>
    public static class MapTextureBake
    {
        public const int DefaultSize = 64;
        private const int PaletteSize = 256;

        /// <summary>
        /// Skybox and cloud-layer suffixes. A sky shader names no image of its
        /// own: `skyparms` points at a set of six sides or a pair of scrolling
        /// cloud layers, so `textures/skies/cloudsky` is answered by
        /// `cloudsky_1`. Taking the first that exists gives the sky one honest
        /// texture instead of none.
        /// </summary>
        private static readonly string[] _skySuffixes = new[] { "_1", "_2", "_ft", "_bk", "_lf", "_rt", "_up" };

        private static readonly string[] _extensions = new[] { ".tga", ".jpg", ".jpeg", ".png" };

        private sealed record ShaderDefinition(
            string Name,
            IReadOnlyList<string> EditorImages,
            IReadOnlyList<string> StageImages);

        public sealed class Result
        {
            public int Baked { get; init; }
            public IReadOnlyList<string> Missing { get; init; } = Array.Empty<string>();
            public long Bytes { get; init; }
        }

        /// <summary>
        /// Writes a pack for every shader the level's drawn surfaces use.
        /// Images are looked for in the archives given, in order; a level's own
        /// .pk3 first, then whatever else the player has.
        /// </summary>
        public static Result Bake(Q3Bsp bsp, IReadOnlyList<string> archivePaths, string outputPath,
            int size = DefaultSize, bool sky = true)
        {
            var archives = new List<ZipArchive>();
            try
            {
                foreach (string path in archivePaths)
                {
                    if (File.Exists(path) && !Path.GetExtension(path).Equals(".bsp", StringComparison.OrdinalIgnoreCase))
                    {
                        archives.Add(ZipFile.OpenRead(path));
                    }
                }
                // A shader name in a .bsp is not the spelling of the file it
                // came from: the compiler upper-cases some of them, and a level
                // whose author worked on Windows has "SandTrim.JPG" answering
                // to "textures/dust2/SANDTRIM". Matching exactly finds nothing
                // and the level comes out untextured.
                var files = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (ZipArchive archive in archives)
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string name = NormalizePath(entry.FullName);
                        if (name.Length == 0 || entry.FullName.EndsWith('/')) continue;
                        // TryAdd deliberately keeps the first archive (and the
                        // first spelling within that archive) authoritative.
                        // This is the same precedence used by the old direct
                        // image lookup, including when paths differ only by
                        // case or slash style.
                        files.TryAdd(name, entry);
                    }
                }
                Dictionary<string, ShaderDefinition> shaders = IndexShaders(archives);
                var entries = new List<(int Index, string Name, ushort[] Palette, byte[] Pixels)>();
                var missing = new List<string>();
                foreach ((int index, string name) in UsedTextures(bsp, sky))
                {
                    byte[]? raw = Find(files, shaders, name);
                    if (raw == null)
                    {
                        missing.Add(name);
                        continue;
                    }
                    (ushort[] palette, byte[] pixels) = Quantize(Decode(raw, size), size);
                    entries.Add((index, name, palette, pixels));
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
                using (var stream = File.Create(outputPath))
                using (var writer = new BinaryWriter(stream, Encoding.UTF8))
                {
                    writer.Write(new[] { 'F', 'P', 'T', 'X' });
                    writer.Write((ushort)1);
                    writer.Write((ushort)entries.Count);
                    foreach ((int index, string name, ushort[] palette, byte[] pixels) in entries)
                    {
                        byte[] encoded = Encoding.UTF8.GetBytes(name);
                        writer.Write((ushort)index);
                        writer.Write((ushort)size);
                        writer.Write((ushort)size);
                        writer.Write((ushort)palette.Length);
                        writer.Write((ushort)encoded.Length);
                        writer.Write(encoded);
                        foreach (ushort colour in palette)
                        {
                            writer.Write(colour);
                        }
                        writer.Write(pixels);
                    }
                }
                return new Result()
                {
                    Baked = entries.Count,
                    Missing = missing,
                    Bytes = new FileInfo(outputPath).Length
                };
            }
            finally
            {
                foreach (ZipArchive archive in archives)
                {
                    archive.Dispose();
                }
            }
        }

        /// <summary>Bakes editor image assets in deterministic material-index order.</summary>
        public static MapTexturePack BakeImages(
            IEnumerable<(int SourceIndex, string Name, ReadOnlyMemory<byte> Encoded)> images,
            int size = DefaultSize)
        {
            var entries = images.OrderBy(value => value.SourceIndex).ToArray();
            if (entries.Select(value => value.SourceIndex).Distinct().Count() != entries.Length)
                throw new MapCompilationException("Custom texture material indices must be unique.");
            using var memory = new MemoryStream();
            using (var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(new[] { 'F', 'P', 'T', 'X' });
                writer.Write((ushort)1);
                writer.Write(checked((ushort)entries.Length));
                foreach ((int sourceIndex, string name, ReadOnlyMemory<byte> encoded) in entries)
                {
                    (ushort[] palette, byte[] pixels) = Quantize(Decode(encoded.ToArray(), size), size);
                    byte[] encodedName = Encoding.UTF8.GetBytes(name);
                    writer.Write(checked((ushort)sourceIndex));
                    writer.Write(checked((ushort)size));
                    writer.Write(checked((ushort)size));
                    writer.Write(checked((ushort)palette.Length));
                    writer.Write(checked((ushort)encodedName.Length));
                    writer.Write(encodedName);
                    foreach (ushort color in palette) writer.Write(color);
                    writer.Write(pixels);
                }
            }
            return MapTexturePack.Load(memory.ToArray(), "native-editor-images.fptx");
        }

        /// <summary>Which shaders the drawn surfaces reference, and their names.</summary>
        private static IEnumerable<(int, string)> UsedTextures(Q3Bsp bsp, bool sky)
        {
            var seen = new HashSet<int>();
            var results = new List<(int, string)>();
            foreach (Q3Face face in bsp.Faces)
            {
                if (face.Type != 1 && face.Type != 2 && face.Type != 3)
                {
                    continue;
                }
                if (!seen.Add(face.Texture))
                {
                    continue;
                }
                Q3Texture texture = bsp.Textures[face.Texture];
                if ((texture.Flags & (Q3Bsp.SurfaceNoDraw | Q3Bsp.SurfaceHint | Q3Bsp.SurfaceSkip)) != 0)
                {
                    continue;
                }
                if ((texture.Flags & Q3Bsp.SurfaceSky) != 0 && !sky)
                {
                    continue;
                }
                results.Add((face.Texture, texture.Name));
            }
            results.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return results;
        }

        private static Dictionary<string, ShaderDefinition> IndexShaders(
            IReadOnlyList<ZipArchive> archives)
        {
            var shaders = new Dictionary<string, ShaderDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchive archive in archives)
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (!Path.GetExtension(entry.FullName).Equals(".shader",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    using Stream stream = entry.Open();
                    using var reader = new StreamReader(stream, Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: true);
                    foreach (ShaderDefinition definition in ParseShaders(reader.ReadToEnd()))
                    {
                        // A shader definition in the first supplied archive
                        // wins, just like an image with the same path. Keeping
                        // the complete definition also means its qer/stage
                        // paths still resolve through the global image index.
                        shaders.TryAdd(definition.Name, definition);
                    }
                }
            }
            return shaders;
        }

        private static byte[]? Find(
            Dictionary<string, ZipArchiveEntry> files,
            IReadOnlyDictionary<string, ShaderDefinition> shaders,
            string name)
            => Resolve(files, shaders, name, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                includeSkySuffixes: true);

        private static byte[]? Resolve(
            Dictionary<string, ZipArchiveEntry> files,
            IReadOnlyDictionary<string, ShaderDefinition> shaders,
            string name,
            HashSet<string> visiting,
            bool includeSkySuffixes)
        {
            string normalized = NormalizePath(name);
            if (normalized.Length == 0 || IsVirtualImage(normalized)) return null;

            byte[]? direct = FindDirect(files, normalized, includeSkySuffixes);
            if (direct != null) return direct;
            if (!shaders.TryGetValue(normalized, out ShaderDefinition? definition)
                || !visiting.Add(normalized))
            {
                return null;
            }

            try
            {
                // qer_editorimage is the editor's intended representative and
                // is therefore preferred over runtime stages. The stage pass
                // is only a static approximation of shader rendering.
                foreach (string image in definition.EditorImages.Concat(definition.StageImages))
                {
                    byte[]? resolved = Resolve(files, shaders, image, visiting,
                        includeSkySuffixes: false);
                    if (resolved != null)
                    {
                        return resolved;
                    }
                }
                return null;
            }
            finally
            {
                visiting.Remove(normalized);
            }
        }

        private static byte[]? FindDirect(
            Dictionary<string, ZipArchiveEntry> files, string name, bool includeSkySuffixes)
        {
            foreach (string candidate in ImageCandidates(name, includeSkySuffixes))
            {
                if (!files.TryGetValue(candidate, out ZipArchiveEntry? entry)) continue;
                using Stream stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }
            return null;
        }

        private static IEnumerable<string> ImageCandidates(string name, bool includeSkySuffixes)
        {
            string normalized = NormalizePath(name);
            if (normalized.Length == 0 || IsVirtualImage(normalized)) yield break;
            string extension = Path.GetExtension(normalized);
            if (extension.Length != 0)
            {
                // Q3 shader packs commonly author a .tga token and ship the
                // same image as .jpg. Try the authored path first, then the
                // other supported encodings with the same stem. An unrelated
                // extension (for example .dds) is not rewritten.
                if (_extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    yield return normalized;
                    string stem = normalized[..^extension.Length];
                    foreach (string imageExtension in _extensions)
                    {
                        if (!imageExtension.Equals(extension, StringComparison.OrdinalIgnoreCase))
                            yield return stem + imageExtension;
                    }
                }
                yield break;
            }

            IEnumerable<string> suffixes = includeSkySuffixes
                ? _skySuffixes.Prepend("") : new[] { "" };
            foreach (string suffix in suffixes)
            foreach (string imageExtension in _extensions)
                yield return normalized + suffix + imageExtension;
        }

        private static bool IsVirtualImage(string name) => name.StartsWith('$');

        private static string NormalizePath(string value)
        {
            string normalized = value.Trim().Replace('\\', '/');
            while (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized[2..];
            return normalized.TrimStart('/');
        }

        private static IEnumerable<ShaderDefinition> ParseShaders(string source)
        {
            List<string> tokens = TokenizeShader(source);
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                if (tokens[i] is "{" or "}" || tokens[i + 1] != "{") continue;
                int close = MatchingBrace(tokens, i + 1);
                if (close < 0) yield break;

                string name = NormalizePath(tokens[i]);
                if (name.Length != 0 && !IsVirtualImage(name))
                {
                    var editorImages = new List<string>();
                    var stageImages = new List<string>();
                    var editorSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var stageSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int depth = 0;
                    for (int j = i + 1; j < close; j++)
                    {
                        string token = tokens[j];
                        if (token == "{")
                        {
                            depth++;
                            continue;
                        }
                        if (token == "}")
                        {
                            depth--;
                            continue;
                        }
                        if (token.Equals("qer_editorimage", StringComparison.OrdinalIgnoreCase))
                        {
                            AddImageToken(tokens, j + 1, close, editorImages, editorSeen);
                        }
                        else if (depth >= 2 && IsStageImageCommand(token))
                        {
                            int imageIndex = j + 1;
                            if (token.Equals("animMap", StringComparison.OrdinalIgnoreCase)
                                && imageIndex < close
                                && double.TryParse(tokens[imageIndex], NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out _))
                            {
                                imageIndex++;
                            }
                            AddImageToken(tokens, imageIndex, close, stageImages, stageSeen);
                        }
                    }
                    yield return new ShaderDefinition(name, editorImages, stageImages);
                }
                i = close;
            }
        }

        private static bool IsStageImageCommand(string token)
            => token.Equals("map", StringComparison.OrdinalIgnoreCase)
                || token.Equals("clampmap", StringComparison.OrdinalIgnoreCase)
                || token.Equals("animMap", StringComparison.OrdinalIgnoreCase);

        private static void AddImageToken(
            IReadOnlyList<string> tokens, int index, int end,
            List<string> images, HashSet<string> seen)
        {
            if (index >= end || tokens[index] is "{" or "}") return;
            string image = NormalizePath(tokens[index]);
            if (image.Length == 0 || IsVirtualImage(image) || !seen.Add(image)) return;
            images.Add(image);
        }

        private static int MatchingBrace(IReadOnlyList<string> tokens, int open)
        {
            int depth = 0;
            for (int i = open; i < tokens.Count; i++)
            {
                if (tokens[i] == "{") depth++;
                else if (tokens[i] == "}" && --depth == 0) return i;
            }
            return -1;
        }

        private static List<string> TokenizeShader(string source)
        {
            var tokens = new List<string>();
            var token = new StringBuilder();
            bool quoted = false;

            void Flush()
            {
                if (token.Length == 0) return;
                tokens.Add(token.ToString());
                token.Clear();
            }

            for (int i = 0; i < source.Length; i++)
            {
                char current = source[i];
                if (quoted)
                {
                    if (current == '"')
                    {
                        quoted = false;
                        Flush();
                    }
                    else if (current == '\\' && i + 1 < source.Length
                        && source[i + 1] is '"' or '\\')
                    {
                        token.Append(source[++i]);
                    }
                    else
                    {
                        token.Append(current);
                    }
                    continue;
                }

                if (current == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    Flush();
                    i += 2;
                    while (i < source.Length && source[i] != '\n') i++;
                    i--;
                    continue;
                }
                if (current == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    Flush();
                    i += 2;
                    while (i + 1 < source.Length
                        && !(source[i] == '*' && source[i + 1] == '/')) i++;
                    i++;
                    continue;
                }
                if (current == '"')
                {
                    Flush();
                    quoted = true;
                    continue;
                }
                if (char.IsWhiteSpace(current))
                {
                    Flush();
                    continue;
                }
                if (current is '{' or '}')
                {
                    Flush();
                    tokens.Add(current.ToString());
                    continue;
                }
                token.Append(current);
            }
            Flush();
            return tokens;
        }

        /// <summary>Decode and box-filter down to the square the hardware wants.</summary>
        private static byte[] Decode(byte[] raw, int size)
        {
            RgbImage image = MapImageDecoding.Decode(raw);
            ReadOnlySpan<byte> pixels = image.Pixels.Span;
            int width = image.Width;
            int height = image.Height;
            var result = new byte[size * size * 3];
            for (int y = 0; y < size; y++)
            {
                int y0 = y * height / size;
                int y1 = Math.Max(y0 + 1, (y + 1) * height / size);
                for (int x = 0; x < size; x++)
                {
                    int x0 = x * width / size;
                    int x1 = Math.Max(x0 + 1, (x + 1) * width / size);
                    int r = 0;
                    int g = 0;
                    int b = 0;
                    int count = 0;
                    for (int sy = y0; sy < y1 && sy < height; sy++)
                    {
                        for (int sx = x0; sx < x1 && sx < width; sx++)
                        {
                            int offset = (sy * width + sx) * 3;
                            r += pixels[offset];
                            g += pixels[offset + 1];
                            b += pixels[offset + 2];
                            count++;
                        }
                    }
                    int target = (y * size + x) * 3;
                    result[target] = (byte)(r / Math.Max(1, count));
                    result[target + 1] = (byte)(g / Math.Max(1, count));
                    result[target + 2] = (byte)(b / Math.Max(1, count));
                }
            }
            return result;
        }

        /// <summary>
        /// Median cut to 256 colours. Split the box with the widest channel at
        /// that channel's median until there are enough boxes, then take each
        /// box's mean as its colour -- the usual answer, and enough for a
        /// 64x64 tile that will be seen at a distance on a texture unit that
        /// only reads 8-bit indices anyway.
        /// </summary>
        private static (ushort[], byte[]) Quantize(byte[] rgb, int size)
        {
            int count = size * size;
            var indices = new int[count];
            for (int i = 0; i < count; i++)
            {
                indices[i] = i;
            }
            var boxes = new List<(int Start, int Length)>() { (0, count) };
            while (boxes.Count < PaletteSize)
            {
                int widest = -1;
                int widestSpread = 0;
                int widestChannel = 0;
                for (int i = 0; i < boxes.Count; i++)
                {
                    (int start, int length) = boxes[i];
                    if (length < 2)
                    {
                        continue;
                    }
                    for (int channel = 0; channel < 3; channel++)
                    {
                        int low = 255;
                        int high = 0;
                        for (int j = start; j < start + length; j++)
                        {
                            int value = rgb[indices[j] * 3 + channel];
                            low = Math.Min(low, value);
                            high = Math.Max(high, value);
                        }
                        if (high - low > widestSpread)
                        {
                            widestSpread = high - low;
                            widest = i;
                            widestChannel = channel;
                        }
                    }
                }
                if (widest < 0 || widestSpread == 0)
                {
                    break;
                }
                (int boxStart, int boxLength) = boxes[widest];
                Array.Sort(indices, boxStart, boxLength,
                    Comparer<int>.Create((a, b) => rgb[a * 3 + widestChannel].CompareTo(rgb[b * 3 + widestChannel])));
                int half = boxLength / 2;
                boxes[widest] = (boxStart, half);
                boxes.Add((boxStart + half, boxLength - half));
            }
            var palette = new ushort[Math.Max(1, boxes.Count)];
            var lookup = new byte[count];
            for (int i = 0; i < boxes.Count; i++)
            {
                (int start, int length) = boxes[i];
                int r = 0;
                int g = 0;
                int b = 0;
                for (int j = start; j < start + length; j++)
                {
                    r += rgb[indices[j] * 3];
                    g += rgb[indices[j] * 3 + 1];
                    b += rgb[indices[j] * 3 + 2];
                }
                int divisor = Math.Max(1, length);
                r /= divisor;
                g /= divisor;
                b /= divisor;
                // BGR555, red in the low bits, which is what the palette format is
                palette[i] = (ushort)(((b >> 3) << 10) | ((g >> 3) << 5) | (r >> 3));
                for (int j = start; j < start + length; j++)
                {
                    lookup[indices[j]] = (byte)i;
                }
            }
            return (palette, lookup);
        }
    }
}
