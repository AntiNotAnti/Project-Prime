using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Mods.Platform;
using MphRead.Mods.Launcher;

namespace MphRead.Mods.Render.Characters
{
    internal enum CharacterModelPart
    {
        Biped,
        ViewModel,
        AlternateForm,
        Halfturret
    }

    internal enum CharacterSkinningMode
    {
        RigidNodes,
        Weighted4
    }

    internal sealed class CharacterModelPackManifest
    {
        public int Format { get; set; } = 1;
        public string Id { get; set; } = "default";
        public List<CharacterModelManifestEntry> Models { get; set; } = new();
    }

    internal sealed class CharacterModelManifestEntry
    {
        public string Hunter { get; set; } = "";
        public CharacterModelPart Part { get; set; }
        public string Model { get; set; } = "";
        public CharacterSkinningMode Skinning { get; set; } = CharacterSkinningMode.RigidNodes;

        // Source glTF node/joint name -> native MPH node name.
        public Dictionary<string, string> BoneMap { get; set; } = new(StringComparer.Ordinal);
    }

    internal sealed record CharacterModelAsset(
        Hunter Hunter,
        CharacterModelPart Part,
        CharacterSkinningMode Skinning,
        string ModelPath,
        IReadOnlyDictionary<string, string> BoneMap,
        IReadOnlySet<string> SourceNodes,
        bool HasSkin,
        int PrimitiveCount);

    /// <summary>
    /// Local-only HD character geometry catalog.
    ///
    /// This deliberately does not replace the native MPH Model object. The
    /// original model continues to own animation, gameplay dimensions,
    /// collision and network/replay state. A resolved asset is presentation
    /// geometry whose source nodes are retargeted to the already-animated
    /// native model nodes by <see cref="CharacterModelManifestEntry.BoneMap"/>.
    ///
    /// Slice one only establishes a safe, validated asset boundary. Rendering
    /// consumes this contract in the following slice. Missing or invalid
    /// assets are therefore indistinguishable from "Native" today rather than
    /// destabilising a live player.
    /// </summary>
    internal sealed class CharacterModelPack
    {
        public const int CurrentFormat = 1;
        public const int MaximumManifestBytes = 1024 * 1024;
        public const long MaximumModelBytes = 128L * 1024 * 1024;
        public const int MaximumModels = 64;
        public const int MaximumBoneMappings = 128;
        public const int MaximumGlbJsonBytes = 4 * 1024 * 1024;
        public const int MaximumSourceNodes = 4096;
        public const int MaximumPrimitives = 4096;

        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 32,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        private readonly Dictionary<(Hunter Hunter, CharacterModelPart Part), CharacterModelAsset> _assets;

        private CharacterModelPack(Dictionary<(Hunter, CharacterModelPart), CharacterModelAsset> assets)
        {
            _assets = assets;
        }

        public static string DefaultDirectory
            => Path.Combine(OperatingSystem.IsAndroid()
                ? LauncherPrefs.Directory : AppPaths.UserDataDirectory,
                "character-models", "default");

        public int Count => _assets.Count;

        public bool TryResolve(Hunter hunter, CharacterModelPart part, out CharacterModelAsset asset)
            => _assets.TryGetValue((hunter, part), out asset!);

        public static CharacterModelPack Empty { get; } = new(new());

        /// <summary>
        /// Runtime-safe loader. A broken optional presentation pack must never
        /// stop the client from reaching the native cartridge model.
        /// </summary>
        public static CharacterModelPack LoadDefault(out string? issue)
        {
            try
            {
                issue = null;
                string manifest = Path.Combine(DefaultDirectory, "characters.json");
                if (!File.Exists(manifest)) return Empty;
                return Load(DefaultDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidDataException or JsonException or ArgumentException)
            {
                issue = ex.Message;
                return Empty;
            }
        }

        internal static CharacterModelPack Load(string root)
        {
            root = Path.GetFullPath(root);
            RejectLink(root, directory: true);
            string manifestPath = ContainedPath(root, "characters.json");
            var manifestInfo = new FileInfo(manifestPath);
            if (!manifestInfo.Exists) throw new FileNotFoundException("Character model pack has no characters.json.", manifestPath);
            if (manifestInfo.Length <= 0 || manifestInfo.Length > MaximumManifestBytes)
                throw new InvalidDataException("Character model manifest exceeds its byte limit.");

            byte[] manifestBytes = File.ReadAllBytes(manifestPath);
            using (JsonDocument document = JsonDocument.Parse(manifestBytes, new JsonDocumentOptions { MaxDepth = 32 }))
                RejectDuplicateProperties(document.RootElement);

            CharacterModelPackManifest manifest = JsonSerializer.Deserialize<CharacterModelPackManifest>(manifestBytes, _json)
                ?? throw new InvalidDataException("Character model manifest is empty.");
            if (manifest.Format != CurrentFormat)
                throw new InvalidDataException($"Unsupported character model pack format {manifest.Format}.");
            if (String.IsNullOrWhiteSpace(manifest.Id) || manifest.Id.Length > 128)
                throw new InvalidDataException("Character model pack ID is invalid.");
            if (manifest.Models == null || manifest.Models.Count > MaximumModels)
                throw new InvalidDataException("Character model pack contains too many model entries.");

            var assets = new Dictionary<(Hunter, CharacterModelPart), CharacterModelAsset>();
            foreach (CharacterModelManifestEntry entry in manifest.Models)
            {
                if (entry == null) throw new InvalidDataException("Character model entry is missing.");
                if (!Enum.TryParse(entry.Hunter, ignoreCase: true, out Hunter hunter)
                    || !Enum.IsDefined(typeof(Hunter), hunter))
                    throw new InvalidDataException($"Unknown hunter '{entry.Hunter}'.");
                if (!Enum.IsDefined(typeof(CharacterModelPart), entry.Part))
                    throw new InvalidDataException($"Unknown character model part '{entry.Part}'.");
                if (!Enum.IsDefined(typeof(CharacterSkinningMode), entry.Skinning))
                    throw new InvalidDataException($"Unknown character skinning mode '{entry.Skinning}'.");
                if (entry.BoneMap == null || entry.BoneMap.Count == 0
                    || entry.BoneMap.Count > MaximumBoneMappings)
                    throw new InvalidDataException($"{hunter}/{entry.Part} needs 1-{MaximumBoneMappings} node mappings.");

                var boneMap = new Dictionary<string, string>(StringComparer.Ordinal);
                var targets = new HashSet<string>(StringComparer.Ordinal);
                foreach ((string source, string target) in entry.BoneMap)
                {
                    string sourceName = ValidateName(source, "source node");
                    string targetName = ValidateName(target, "native node");
                    if (!boneMap.TryAdd(sourceName, targetName))
                        throw new InvalidDataException($"{hunter}/{entry.Part} maps source node '{sourceName}' twice.");
                    if (entry.Skinning == CharacterSkinningMode.Weighted4 && !targets.Add(targetName))
                        throw new InvalidDataException($"{hunter}/{entry.Part} maps more than one glTF joint to native node '{targetName}'.");
                }

                string modelPath = ContainedPath(root, entry.Model);
                var info = new FileInfo(modelPath);
                if (!info.Exists) throw new FileNotFoundException($"{hunter}/{entry.Part} model is missing.", modelPath);
                if (!Path.GetExtension(modelPath).Equals(".glb", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{hunter}/{entry.Part} must use a binary glTF (.glb) model.");
                if (info.Length <= 0 || info.Length > MaximumModelBytes)
                    throw new InvalidDataException($"{hunter}/{entry.Part} GLB exceeds the {MaximumModelBytes / (1024 * 1024)} MiB model limit.");

                GlbInspection inspection = InspectGlb(modelPath);
                foreach (string source in boneMap.Keys)
                    if (!inspection.NodeNames.Contains(source))
                        throw new InvalidDataException($"{hunter}/{entry.Part} maps missing glTF node '{source}'.");
                if (entry.Skinning == CharacterSkinningMode.Weighted4 && !inspection.HasSkin)
                    throw new InvalidDataException($"{hunter}/{entry.Part} requests Weighted4 skinning but the GLB has no skin.");

                var key = (hunter, entry.Part);
                if (!assets.TryAdd(key, new CharacterModelAsset(
                    hunter, entry.Part, entry.Skinning, modelPath, boneMap,
                    inspection.NodeNames, inspection.HasSkin, inspection.PrimitiveCount)))
                    throw new InvalidDataException($"Duplicate character model entry for {hunter}/{entry.Part}.");
            }
            return new CharacterModelPack(assets);
        }

        /// <summary>
        /// Validate only the native side of retargeting. The source side was
        /// already checked against the GLB at pack load time.
        /// </summary>
        public static bool ValidateNativeRig(CharacterModelAsset asset, Model nativeModel, out string? issue)
        {
            var nativeNodes = nativeModel.Nodes.ToDictionary(node => node.Name, StringComparer.Ordinal);
            var mappedTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (string target in asset.BoneMap.Values)
            {
                if (!nativeNodes.ContainsKey(target))
                {
                    issue = $"HD {asset.Hunter}/{asset.Part} expects native node '{target}', but {nativeModel.Name} does not contain it.";
                    return false;
                }
                mappedTargets.Add(target);
            }

            // The current generated World shader exposes 32 matrices. The
            // rigid-node path does not need that palette because each segment
            // can submit one native node transform. Weighted4 will use the
            // palette in the later renderer slice, so reject oversized rigs
            // here rather than silently truncating influences.
            if (asset.Skinning == CharacterSkinningMode.Weighted4 && mappedTargets.Count > 32)
            {
                issue = $"HD {asset.Hunter}/{asset.Part} maps {mappedTargets.Count} weighted bones; the current renderer contract supports 32.";
                return false;
            }

            issue = null;
            return true;
        }

        private static string ValidateName(string? value, string label)
        {
            string name = value?.Trim() ?? "";
            if (name.Length == 0 || name.Length > 128 || name.Any(char.IsControl))
                throw new InvalidDataException($"Character model {label} name is invalid.");
            return name;
        }

        private static string ContainedPath(string root, string relative)
        {
            if (String.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)
                || relative.Contains('\') || relative.Contains(':')
                || relative.Split('/').Any(part => part.Length == 0 || part is "." or ".."))
                throw new InvalidDataException("Unsafe character model path: " + relative);

            string full = Path.GetFullPath(Path.Combine(root, relative));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException("Character model path escapes its pack.");

            string? current = full;
            while (current != null && !String.Equals(current, root, StringComparison.Ordinal))
            {
                if (File.Exists(current)) RejectLink(current, directory: false);
                else if (Directory.Exists(current)) RejectLink(current, directory: true);
                current = Path.GetDirectoryName(current);
            }
            return full;
        }

        private static void RejectLink(string path, bool directory)
        {
            if (!(directory ? Directory.Exists(path) : File.Exists(path))) return;
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Character model packs cannot contain symbolic links.");
        }

        private static GlbInspection InspectGlb(string path)
        {
            using FileStream stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[12];
            stream.ReadExactly(header);
            if (ReadUInt32(header) != 0x46546C67 || ReadUInt32(header[4..]) != 2)
                throw new InvalidDataException("Character model is not a glTF 2.0 GLB.");
            uint declaredLength = ReadUInt32(header[8..]);
            if (declaredLength != stream.Length)
                throw new InvalidDataException("Character GLB declared length does not match the file.");

            Span<byte> chunkHeader = stackalloc byte[8];
            stream.ReadExactly(chunkHeader);
            uint jsonLength = ReadUInt32(chunkHeader);
            uint chunkType = ReadUInt32(chunkHeader[4..]);
            if (chunkType != 0x4E4F534A || jsonLength == 0 || jsonLength > MaximumGlbJsonBytes
                || jsonLength > stream.Length - stream.Position)
                throw new InvalidDataException("Character GLB has an invalid JSON chunk.");

            byte[] json = new byte[(int)jsonLength];
            stream.ReadExactly(json);
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("asset", out JsonElement asset)
                || !asset.TryGetProperty("version", out JsonElement version)
                || version.GetString() is not string v || !v.StartsWith("2.", StringComparison.Ordinal))
                throw new InvalidDataException("Character GLB must declare glTF 2.x.");

            var nodes = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetProperty("nodes", out JsonElement nodeArray))
            {
                if (nodeArray.ValueKind != JsonValueKind.Array || nodeArray.GetArrayLength() > MaximumSourceNodes)
                    throw new InvalidDataException("Character GLB exceeds the node budget.");
                foreach (JsonElement node in nodeArray.EnumerateArray())
                    if (node.TryGetProperty("name", out JsonElement nameElement)
                        && nameElement.GetString() is string name && name.Length > 0)
                    {
                        if (name.Length > 128 || name.Any(char.IsControl))
                            throw new InvalidDataException("Character GLB contains an invalid node name.");
                        if (!nodes.Add(name))
                            throw new InvalidDataException($"Character GLB contains duplicate node name '{name}'.");
                    }
            }

            bool hasSkin = root.TryGetProperty("skins", out JsonElement skins)
                && skins.ValueKind == JsonValueKind.Array && skins.GetArrayLength() > 0;
            int primitives = 0;
            if (root.TryGetProperty("meshes", out JsonElement meshes))
            {
                if (meshes.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Character GLB meshes must be an array.");
                foreach (JsonElement mesh in meshes.EnumerateArray())
                {
                    if (!mesh.TryGetProperty("primitives", out JsonElement primitiveArray)
                        || primitiveArray.ValueKind != JsonValueKind.Array) continue;
                    primitives = checked(primitives + primitiveArray.GetArrayLength());
                    if (primitives > MaximumPrimitives)
                        throw new InvalidDataException("Character GLB exceeds the primitive budget.");
                }
            }
            if (primitives == 0) throw new InvalidDataException("Character GLB contains no mesh primitives.");
            return new(nodes, hasSkin, primitives);
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> bytes)
            => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes);

        private static void RejectDuplicateProperties(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new InvalidDataException("Duplicate JSON property: " + property.Name);
                    RejectDuplicateProperties(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in element.EnumerateArray())
                    RejectDuplicateProperties(child);
            }
        }

        private readonly record struct GlbInspection(
            IReadOnlySet<string> NodeNames,
            bool HasSkin,
            int PrimitiveCount);
    }
}
