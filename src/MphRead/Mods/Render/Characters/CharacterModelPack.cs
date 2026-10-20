using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using MphRead.Mods.Import;
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

    internal enum CharacterPoseSource { Native, SourceRigPreview }

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
        public int Lod { get; set; }
        public string Model { get; set; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MobileModel { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CharacterId { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SkinSource { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Provenance { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ProvenanceSha256 { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SourceCannonReceipt { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SourceCannonSha256 { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SourceGraphProof { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SourceGraphProofSha256 { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SourceMotionManifest { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SourceAnimationArchive { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SourceAnimationArchiveSha256 { get; set; }
        public CharacterPoseSource PoseSource { get; set; } = CharacterPoseSource.Native;
        public CharacterSkinningMode Skinning { get; set; } = CharacterSkinningMode.RigidNodes;

        // Source glTF node/joint name -> native MPH node name.
        public Dictionary<string, string> BoneMap { get; set; } = new(StringComparer.Ordinal);
    }

    internal sealed record CharacterModelAsset(
        Hunter Hunter,
        CharacterModelPart Part,
        int Lod,
        CharacterSkinningMode Skinning,
        string ModelPath,
        IReadOnlyDictionary<string, string> BoneMap,
        IReadOnlySet<string> SourceNodes,
        bool HasSkin,
        int PrimitiveCount,
        PrivateCharacterAppearance? PrivateAppearance = null, CharacterPoseSource PoseSource = CharacterPoseSource.Native);

    /// <summary>Private local presentation input, never a Hunter or wire ID.</summary>
    internal readonly record struct PrivateCharacterAppearance(string CharacterId, string SkinSource)
    {
        // Only source variants supported by the existing PORT preview exporter.
        // Corruption remains catalog-reserved rather than silently supported.
        internal static bool TryCreate(Hunter hunter, string? character, string? skin,
            out PrivateCharacterAppearance appearance, out string sourceCharacter, bool generatedSource = false)
        {
            appearance = default;
            sourceCharacter = "";
            var definition = ExternalCharacterCatalog.Find(character);
            if (definition == null || definition.BaseHunter != hunter || skin == null) return false;
            string? canonical = definition.SkinSources.FirstOrDefault(value =>
                String.Equals(value, skin, StringComparison.OrdinalIgnoreCase));
            sourceCharacter = canonical switch
            {
                "MP2DarkSamus" => "darksamus-echoes",
                "MP2DarkSamusFinal" when generatedSource => "darksamus-overcharged-echoes",
                "MP1PowerTrooper" => "trooper-power",
                "MP1IceTrooper" => "trooper-ice",
                "MP1WaveTrooper" => "trooper-wave",
                "MP1PlasmaTrooper" => "trooper-plasma",
                _ => ""
            };
            if (sourceCharacter.Length == 0) return false;
            appearance = new(definition.Id, canonical!);
            return true;
        }
    }

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
    internal sealed partial class CharacterModelPack
    {
        internal static bool ForceMobileTierForCheck { get; set; }
        public const int CurrentFormat = 1;
        public const int MaximumManifestBytes = 1024 * 1024;
        public const long MaximumModelBytes = 128L * 1024 * 1024;
        public const int MaximumModels = 64;
        public const int MaximumLod = 1;
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

        private readonly Dictionary<(Hunter Hunter, CharacterModelPart Part, int Lod), CharacterModelAsset> _assets;
        private readonly Dictionary<(PrivateCharacterAppearance Appearance, CharacterModelPart Part, int Lod), CharacterModelAsset> _privateAssets;

        private string? _sourceRoot, _sourceManifestSha;
        private CharacterSourceRootWitness? _sourceRootWitness;
        private readonly object _sourceProofLock=new();
        private long _sourceGeneration=1;
        private bool _sourceProofAvailable;
        private readonly Dictionary<CharacterModelAsset,(CharacterModelManifestEntry Entry,string SourceCharacter,string ModelSha)> _sourceProofs = new(ReferenceEqualityComparer.Instance);
        // Actual owner, exact asset, immutable proof generation. No bool proof.
        internal sealed class SourcePreviewLease
        {
            private readonly CharacterModelPack _owner;
            internal CharacterModelAsset Asset { get; }
            internal string ModelSha256 { get; }
            internal long Generation { get; }
            internal CharacterSourceCannonRecipe? SourceCannon { get; }
            internal CharacterSourceGraphProof? SourceGraphProof { get; }
            internal CharacterSourceAnimationArchive? SourceAnimations { get; }
            internal CharacterSourceGxMaterials? SourceMaterials { get; }
            internal object CacheIdentity => _owner;
            internal CharacterSourceCacheKey CacheKey => new(_owner,Asset.ModelPath,ModelSha256,Asset.PrivateAppearance!.Value,Generation,Asset.Part,SourceCannon?.ReceiptSha256,SourceGraphProof?.ProofSha256,SourceAnimations?.Sha256,SourceMaterials?.Archive.Sha256,SourceMaterials==null?null:CharacterSourceGxProgram.Policy);
            private SourcePreviewLease(CharacterModelPack owner,CharacterModelAsset asset,string sha,long generation)
                { (_owner,Asset,ModelSha256,Generation)=(owner,asset,sha,generation);
                SourceMaterials=owner._sourceMaterials.GetValueOrDefault(asset)?.Materials;
                SourceCannon=owner._sourceCannons.GetValueOrDefault(asset);
                SourceGraphProof=owner._sourceGraphs.GetValueOrDefault(asset);
                SourceAnimations=owner._sourceAnimations.GetValueOrDefault(asset); }
            // No filesystem IO: immutable cached GPU data uses this generation.
            internal void RequireCurrent(CharacterModelAsset asset)
            { if(!ReferenceEquals(asset,Asset))throw new InvalidDataException("Source capability asset changed.");_owner.RequireSourceCurrent(asset,Generation); }
            // An explicit private material fixture reads the receipt belonging
            // to this exact lease. Never reparse a second unbound manifest.
            internal byte[] ReadProvenanceSnapshot()
            {
                Require(Asset);
                var proof=_owner._sourceProofs[Asset];
                string path=ContainedPath(_owner._sourceRoot!,proof.Entry.Provenance!);
                byte[] snapshot=CharacterSourceProofIO.ReadFile(path,MaximumManifestBytes);
                string sha=Convert.ToHexString(SHA256.HashData(snapshot)).ToLowerInvariant();
                if(sha!=proof.Entry.ProvenanceSha256)
                    throw new InvalidDataException("Source lease provenance snapshot changed.");
                Require(Asset);
                return snapshot;
            }
            // Strong proof is required at decode and before cache publication.
            internal void Require(CharacterModelAsset asset)
            { RequireCurrent(asset);_owner.ValidateAtGeneration(asset,ModelSha256,Generation); }
            internal static SourcePreviewLease IssueFromOwner(CharacterModelPack owner,CharacterModelAsset asset)
            {
                long generation;
                lock(owner._sourceProofLock){generation=owner._sourceGeneration;owner.RequireSourceCurrent(asset,generation);}
                owner.ValidateAtGeneration(asset,null,generation);
                return new(owner,asset,owner._sourceProofs[asset].ModelSha,generation);
            }
        }
        private void RequireSourceCurrent(CharacterModelAsset asset,long generation)
        {
            lock(_sourceProofLock)
                if(!_sourceProofAvailable||generation!=_sourceGeneration||!_sourceProofs.ContainsKey(asset)
                    ||asset.PrivateAppearance is not { } appearance
                    ||!_privateAssets.TryGetValue((appearance,asset.Part,asset.Lod),out var current)||!ReferenceEquals(current,asset))
                    throw new InvalidDataException("Source proof generation or exact owner is retired.");
        }
        private void ValidateAtGeneration(CharacterModelAsset asset,string? expectedSha,long generation)
        {
            RequireSourceCurrent(asset,generation);
            try{RevalidateSource(asset,expectedSha);RequireSourceCurrent(asset,generation);}
            catch
            {
                lock(_sourceProofLock)if(_sourceGeneration==generation){_sourceProofAvailable=false;_sourceGeneration++;}
                throw;
            }
        }
        internal bool TryCreateSourcePreviewLease(CharacterModelAsset asset,out SourcePreviewLease lease)
        {
            lease=null!;
            try{lease=SourcePreviewLease.IssueFromOwner(this,asset);return true;}
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException){return false;}
        }
        // Explicit editor/acceptance refresh only; never a watcher or draw call.
        // Every refresh retires every old lease before IO. Changed bytes/path
        // require loading a new pack; this owner cannot adopt changed proof.
        internal bool RefreshSourcePreviewProof()
        {
            long generation;
            lock(_sourceProofLock){_sourceProofAvailable=false;generation=++_sourceGeneration;}
            try
            {
                if(_sourceProofs.Count==0)return false;
                foreach(var asset in _sourceProofs.Keys)RevalidateSource(asset,null);
                lock(_sourceProofLock){if(_sourceGeneration!=generation)return false;_sourceProofAvailable=true;return true;}
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException){return false;}
        }
        private void RevalidateSource(CharacterModelAsset asset,string? expectedSha)
        {
            if(!OperatingSystem.IsMacOS()||_sourceRoot==null||_sourceManifestSha==null||_sourceRootWitness==null
                ||asset.PoseSource!=CharacterPoseSource.SourceRigPreview||asset.PrivateAppearance is not { } appearance
                ||!(asset.Part==CharacterModelPart.Biped || asset.Part==CharacterModelPart.ViewModel&&_sourceCannons.ContainsKey(asset))||asset.Lod!=0||asset.Skinning!=CharacterSkinningMode.Weighted4
                ||!_sourceProofs.TryGetValue(asset,out var proof))throw new InvalidDataException("No verified private source preview ownership.");
            _sourceRootWitness.RequireUnchanged(_sourceRoot);RejectLink(_sourceRoot,true);
            string manifest=ContainedPath(_sourceRoot,"characters.json");
            if(CharacterSourceProofIO.HashFile(manifest,MaximumManifestBytes)!=_sourceManifestSha)
                throw new InvalidDataException("Private source manifest changed.");
            string model=ContainedPath(_sourceRoot,proof.Entry.Model);
            if(model!=asset.ModelPath)throw new InvalidDataException("Private source path changed.");
            string sha=ValidatePrivateProvenance(_sourceRoot,proof.Entry,asset,appearance,proof.SourceCharacter);
            if(sha!=proof.ModelSha||(expectedSha!=null&&!String.Equals(sha,expectedSha,StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Private source content changed.");
            if(_sourceCannons.TryGetValue(asset,out var cannon))cannon.Revalidate(_sourceRoot,proof.Entry,sha);
            if(_sourceGraphs.TryGetValue(asset,out var graph))graph.Revalidate(_sourceRoot,proof.Entry,sha);
            if(_sourceAnimations.TryGetValue(asset,out var animations))animations.Revalidate(_sourceRoot,proof.Entry,sha);
            RevalidateSourceMaterials(asset);
            _sourceRootWitness.RequireUnchanged(_sourceRoot);
            _=ContainedPath(_sourceRoot,proof.Entry.Model);_=ContainedPath(_sourceRoot,proof.Entry.Provenance!);
        }

        private CharacterModelPack(Dictionary<(Hunter, CharacterModelPart, int), CharacterModelAsset> assets,
            Dictionary<(PrivateCharacterAppearance, CharacterModelPart, int), CharacterModelAsset>? privateAssets = null)
        {
            _assets = assets;
            _privateAssets = privateAssets ?? new();
        }

        public static string DefaultDirectory
            => Path.Combine(OperatingSystem.IsAndroid()
                ? LauncherPrefs.Directory : AppPaths.UserDataDirectory,
                "character-models", "default");

        public int Count => _assets.Count + _privateAssets.Count;
        internal IEnumerable<CharacterModelAsset> Assets => _assets.Values.Concat(_privateAssets.Values);

        public bool TryResolve(Hunter hunter, CharacterModelPart part, out CharacterModelAsset asset)
            => TryResolve(hunter, part, 0, out asset);

        public bool TryResolve(Hunter hunter, CharacterModelPart part, int lod, out CharacterModelAsset asset)
            => _assets.TryGetValue((hunter, part, lod), out asset!);

        internal bool TryResolvePrivate(Hunter hunter, PrivateCharacterAppearance appearance,
            CharacterModelPart part, int lod, out CharacterModelAsset asset)
        {
            asset = null!;
            return PrivateCharacterAppearance.TryCreate(hunter, appearance.CharacterId, appearance.SkinSource,
                out var canonical, out _, generatedSource: true) && _privateAssets.TryGetValue((canonical, part, lod), out asset!);
        }

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

        internal static CharacterModelPack Load(string root, bool? mobile = null)
        {
            root = Path.GetFullPath(root);
            RejectLink(root, directory: true);
            string manifestPath = ContainedPath(root, "characters.json");
            var manifestInfo = new FileInfo(manifestPath);
            if (!manifestInfo.Exists) throw new FileNotFoundException("Character model pack has no characters.json.", manifestPath);
            if (manifestInfo.Length <= 0 || manifestInfo.Length > MaximumManifestBytes)
                throw new InvalidDataException("Character model manifest exceeds its byte limit.");

            byte[] manifestBytes = CharacterSourceProofIO.ReadFile(manifestPath,MaximumManifestBytes);
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

            CharacterSourceRootWitness? sourceRootWitness=manifest.Models.Any(entry=>entry?.PoseSource==CharacterPoseSource.SourceRigPreview)
                ?CharacterSourceRootWitness.Capture(root):null;
            var assets = new Dictionary<(Hunter, CharacterModelPart, int), CharacterModelAsset>();
            var privateAssets = new Dictionary<(PrivateCharacterAppearance, CharacterModelPart, int), CharacterModelAsset>();
            var graphProofs=new List<(CharacterModelAsset Asset,CharacterSourceGraphProof Proof)>();
            var animationProofs=new List<(CharacterModelAsset Asset,CharacterSourceAnimationArchive Proof)>();
            var cannonProofs=new List<(CharacterModelAsset Asset,CharacterSourceCannonRecipe Recipe)>();
            var proofs = new List<(CharacterModelAsset Asset,CharacterModelManifestEntry Entry,string Source,string Sha)>();
            foreach (CharacterModelManifestEntry entry in manifest.Models)
            {
                if (entry == null) throw new InvalidDataException("Character model entry is missing.");
                if (!Enum.TryParse(entry.Hunter, ignoreCase: true, out Hunter hunter)
                    || !Enum.IsDefined(typeof(Hunter), hunter))
                    throw new InvalidDataException($"Unknown hunter '{entry.Hunter}'.");
                if (!Enum.IsDefined(typeof(CharacterModelPart), entry.Part))
                    throw new InvalidDataException($"Unknown character model part '{entry.Part}'.");
                if (entry.Lod < 0 || entry.Lod > MaximumLod)
                    throw new InvalidDataException($"{hunter}/{entry.Part} has invalid LOD {entry.Lod}.");
                if (entry.Part != CharacterModelPart.Biped && entry.Lod != 0)
                    throw new InvalidDataException($"{hunter}/{entry.Part} does not support replacement LOD {entry.Lod}.");
                if (!Enum.IsDefined(typeof(CharacterSkinningMode), entry.Skinning))
                    throw new InvalidDataException($"Unknown character skinning mode '{entry.Skinning}'.");
                if (!Enum.IsDefined(entry.PoseSource)) throw new InvalidDataException("Unknown character pose source.");
                bool sourcePreview = entry.PoseSource==CharacterPoseSource.SourceRigPreview;
                bool graphPreview=entry.SourceGraphProof!=null||entry.SourceGraphProofSha256!=null||entry.SourceMotionManifest!=null;
                if(graphPreview&&(!sourcePreview||entry.Part!=CharacterModelPart.Biped||entry.SourceGraphProof==null||entry.SourceGraphProofSha256==null||entry.SourceMotionManifest==null))
                    throw new InvalidDataException("Raw graph binding requires explicit source-only Biped proof/motion references; no graph evaluator.");
                bool archivePreview=entry.SourceAnimationArchive!=null||entry.SourceAnimationArchiveSha256!=null;
                if(archivePreview&&(!sourcePreview||!(entry.Part==CharacterModelPart.Biped||entry.Part==CharacterModelPart.ViewModel&&entry.SourceCannonReceipt!=null&&entry.SourceCannonSha256!=null)||entry.SourceAnimationArchive==null
                    ||entry.SourceAnimationArchiveSha256==null||graphPreview))
                    throw new InvalidDataException("Imported animation archive requires a complete exclusive source-rig Biped binding.");
                bool cannonPreview=entry.SourceCannonReceipt!=null||entry.SourceCannonSha256!=null;
                if(cannonPreview&&(!sourcePreview||entry.Part!=CharacterModelPart.ViewModel||entry.SourceCannonReceipt==null||entry.SourceCannonSha256==null))
                    throw new InvalidDataException("Cannon requires an explicit verified source-only ViewModel receipt.");
                if(sourcePreview && (!OperatingSystem.IsMacOS() || mobile==true || ForceMobileTierForCheck)) throw new InvalidDataException("Source preview is macOS private-only.");
                bool privateEntry = sourcePreview || entry.CharacterId != null || entry.SkinSource != null
                    || entry.Provenance != null || entry.ProvenanceSha256 != null || cannonPreview;
                PrivateCharacterAppearance? appearance = null;
                string sourceCharacter = "";
                if (privateEntry)
                {
                    if (!PrivateCharacterAppearance.TryCreate(hunter, entry.CharacterId, entry.SkinSource,
                            out var selected, out sourceCharacter, generatedSource: sourcePreview) || entry.Provenance == null
                        || entry.ProvenanceSha256 == null || !(entry.Part==CharacterModelPart.Biped || cannonPreview&&entry.Part==CharacterModelPart.ViewModel)
                        || entry.Lod != 0 || entry.Skinning != CharacterSkinningMode.Weighted4 || entry.MobileModel != null)
                        throw new InvalidDataException("Private appearance requires a supported catalog character/skin, matching native parent, provenance and one Weighted4 LOD0 biped.");
                    appearance = selected;
                }
                if (entry.BoneMap == null || (!sourcePreview && entry.BoneMap.Count == 0)
                    || entry.BoneMap.Count > MaximumBoneMappings || (sourcePreview && entry.BoneMap.Count != 0))
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

                string modelPath = "";
                string? verifiedModelSha=null;
                GlbInspection inspection = default;
                bool selectMobile = mobile ?? (OperatingSystem.IsAndroid() || ForceMobileTierForCheck);
                // Validate both contained references even when one tier is inactive.
                foreach (string relative in entry.MobileModel == null ? new[] {entry.Model} : new[] {entry.Model,entry.MobileModel})
                {
                    string candidatePath = ContainedPath(root, relative);
                    var info = new FileInfo(candidatePath);
                    if (!info.Exists) throw new FileNotFoundException($"{hunter}/{entry.Part} model is missing.", candidatePath);
                    if (!Path.GetExtension(candidatePath).Equals(".glb", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"{hunter}/{entry.Part} must use a binary glTF (.glb) model.");
                    if (info.Length <= 0 || info.Length > MaximumModelBytes)
                        throw new InvalidDataException($"{hunter}/{entry.Part} GLB exceeds the {MaximumModelBytes / (1024 * 1024)} MiB model limit.");

                    byte[]? sourceSnapshot=sourcePreview?CharacterSourceProofIO.ReadFile(candidatePath,checked((int)MaximumModelBytes)):null;
                    GlbInspection candidate = InspectGlb(candidatePath,sourceSnapshot);
                    if(sourceSnapshot!=null)verifiedModelSha=Convert.ToHexString(SHA256.HashData(sourceSnapshot)).ToLowerInvariant();
                    foreach (string source in boneMap.Keys)
                        if (!candidate.NodeNames.Contains(source))
                            throw new InvalidDataException($"{hunter}/{entry.Part} maps missing glTF node '{source}'.");
                    if (entry.Skinning == CharacterSkinningMode.Weighted4 && !candidate.HasSkin)
                        throw new InvalidDataException($"{hunter}/{entry.Part} requests Weighted4 skinning but the GLB has no skin.");
                    if (relative == (selectMobile && entry.MobileModel != null ? entry.MobileModel : entry.Model))
                    { modelPath=candidatePath; inspection=candidate; }
                }

                var asset = new CharacterModelAsset(
                    hunter, entry.Part, entry.Lod, entry.Skinning, modelPath, boneMap,
                    inspection.NodeNames, inspection.HasSkin, inspection.PrimitiveCount, appearance, entry.PoseSource);
                if (appearance is { } privateAppearance)
                {
                    string modelSha=ValidatePrivateProvenance(root,entry,asset,privateAppearance,sourceCharacter,verifiedModelSha);
                    if(graphPreview)graphProofs.Add((asset,CharacterSourceGraphProof.Load(root,entry,asset,modelSha)));
                    if(archivePreview)animationProofs.Add((asset,CharacterSourceAnimationArchive.Load(root,entry,modelSha)));
                    if(cannonPreview)cannonProofs.Add((asset,CharacterSourceCannonRecipe.Load(root,entry,modelSha)));
                    if(sourcePreview)proofs.Add((asset,entry,sourceCharacter,modelSha));
                    if (!privateAssets.TryAdd((privateAppearance, entry.Part, entry.Lod), asset))
                        throw new InvalidDataException("Duplicate private character/skin/part/LOD identity.");
                }
                else if (!assets.TryAdd((hunter, entry.Part, entry.Lod), asset))
                    throw new InvalidDataException(
                        $"Duplicate character model entry for {hunter}/{entry.Part}/lod{entry.Lod}.");
            }
            var pack = new CharacterModelPack(assets, privateAssets);
            sourceRootWitness?.RequireUnchanged(root);
            foreach(var graph in graphProofs)pack._sourceGraphs.Add(graph.Asset,graph.Proof);
            foreach(var animation in animationProofs)pack._sourceAnimations.Add(animation.Asset,animation.Proof);
            foreach(var cannon in cannonProofs)pack._sourceCannons.Add(cannon.Asset,cannon.Recipe);
            pack._sourceRootWitness=sourceRootWitness;
            pack._sourceProofAvailable=proofs.Count>0;
            pack._sourceRoot=root;
            pack._sourceManifestSha=Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
            foreach(var proof in proofs)pack._sourceProofs.Add(proof.Asset,(proof.Entry,proof.Source,proof.Sha));
            foreach(var proof in proofs.Where(p => p.Source == "darksamus-overcharged-echoes"))
                pack.CaptureSourceMaterials(proof.Asset, proof.Source);
            return pack;
        }

        private static string ValidatePrivateProvenance(string root, CharacterModelManifestEntry entry,
            CharacterModelAsset asset, PrivateCharacterAppearance appearance, string sourceCharacter,string? verifiedModelSha=null)
        {
            string receiptPath = ContainedPath(root, entry.Provenance!);
            var file = new FileInfo(receiptPath);
            if (!file.Exists || file.Length <= 0 || file.Length > MaximumManifestBytes)
                throw new InvalidDataException("Private appearance provenance is missing or oversized.");
            byte[] bytes = CharacterSourceProofIO.ReadFile(receiptPath,MaximumManifestBytes);
            if (!ValidSha256(entry.ProvenanceSha256)
                || Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != entry.ProvenanceSha256)
                throw new InvalidDataException("Private appearance provenance hash mismatch.");
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            RejectDuplicateProperties(document.RootElement);
            var receipt = document.RootElement;
            if (receipt.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Private appearance provenance must be a JSON object.");
            bool Text(string name, string expected) => receipt.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String && value.GetString() == expected;
            bool Boolean(string name, bool expected) => receipt.TryGetProperty(name, out var value)
                && value.ValueKind == (expected ? JsonValueKind.True : JsonValueKind.False);
            if (receipt.TryGetProperty("format", out var generatedFormat) && generatedFormat.ValueKind == JsonValueKind.Number && generatedFormat.TryGetInt32(out int generatedVersion) && generatedVersion == 2)
                return ValidateGeneratedSourceProvenance(receipt, entry, asset, appearance, sourceCharacter, verifiedModelSha);
            if (sourceCharacter == "darksamus-overcharged-echoes")
                throw new InvalidDataException("Final accepts only its exact generated source profile.");
            if (!receipt.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.Number
                || !format.TryGetInt32(out int version) || version != 1
                || !Text("sourceCharacter", sourceCharacter) || !Text("nativeHunter", asset.Hunter.ToString())
                || !Text("characterId", appearance.CharacterId) || !Text("skinSource", appearance.SkinSource)
                || !Text("presentationPart", "biped") || !Boolean("visualOnly", true))
                throw new InvalidDataException("Private appearance provenance identity does not match its model entry.");
            foreach (string key in new[] { "approvedForGameplay", "multiplayerEnabled", "playerSelectionEnabled",
                "firstPersonVerified", "alternateFormVerified", "sourceMotionRetargetVerified", "devicePerformanceVerified" })
                if (!Boolean(key, false)) throw new InvalidDataException("Private preview cannot claim unperformed gameplay, part or device acceptance.");
            foreach (string key in new[] { "sourceGlbSha256", "sourceNativeKitSha256", "sourceMappingSha256",
                "sourceSkeletonSha256", "sourceWeightsSha256" })
                if (!receipt.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String
                    || !ValidSha256(value.GetString())) throw new InvalidDataException("Private appearance source hash is missing or invalid.");
            string modelSha=verifiedModelSha??CharacterSourceProofIO.HashFile(asset.ModelPath,MaximumModelBytes);
            if(!Text("sourceGlbSha256",modelSha))throw new InvalidDataException("Private appearance GLB changed since its provenance receipt.");
            return modelSha;
        }

        private static bool ValidSha256(string? value) => value is { Length: 64 }
            && value.Any(c => c != '0') && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

        /// <summary>
        /// Validate only the native side of retargeting. The source side was
        /// already checked against the GLB at pack load time.
        /// </summary>
        public static bool ValidateNativeRig(CharacterModelAsset asset, Model nativeModel, out string? issue)
        {
            if(asset.PoseSource!=CharacterPoseSource.Native)
            {issue="Source preview cannot masquerade as a native retargeted rig.";return false;}
            var nativeNodes = nativeModel.Nodes.ToDictionary(node => node.Name, StringComparer.Ordinal);
            var mappedTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (string target in asset.BoneMap.Values)
            {
                if (!nativeNodes.ContainsKey(target))
                {
                    issue = $"HD {asset.Hunter}/{asset.Part}/lod{asset.Lod} expects native node '{target}', but {nativeModel.Name} does not contain it.";
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
                issue = $"HD {asset.Hunter}/{asset.Part}/lod{asset.Lod} maps {mappedTargets.Count} weighted bones; the current renderer contract supports 32.";
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
                || relative.Contains((char)92) || relative.Contains(':')
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

        private static GlbInspection InspectGlb(string path,byte[]? sourceSnapshot=null)
        {
            using Stream stream = sourceSnapshot==null?File.OpenRead(path):new MemoryStream(sourceSnapshot,writable:false);
            long openedLength=stream.Length;
            if(openedLength<=0||openedLength>MaximumModelBytes)throw new InvalidDataException("Character GLB exceeds opened stream budget.");
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
            if(stream.Length!=openedLength)throw new InvalidDataException("Character GLB changed length during inspection.");
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
