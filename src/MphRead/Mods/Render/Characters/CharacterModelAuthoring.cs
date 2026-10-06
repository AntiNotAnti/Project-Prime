using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Export;
using MphRead.Mods.Platform;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render.Characters
{
    internal static class CharacterModelAuthoring
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        public static int BuildKit(string hunterText, string? output)
        {
            if (!Enum.TryParse(hunterText, true, out Hunter hunter)
                || !Enum.IsDefined(typeof(Hunter), hunter)
                || hunter == Hunter.Guardian)
            {
                Console.Error.WriteLine("[charactermodelkit] unknown hunter: " + hunterText);
                return 1;
            }

            IReadOnlyList<string> nativeNames = Metadata.HunterModels[hunter];
            Model biped = Read.GetModelInstance(nativeNames[0]).Model;
            Model bipedLod1 = Read.GetModelInstance(nativeNames[1]).Model;
            Model viewModel = Read.GetModelInstance(nativeNames[3]).Model;
            Model alternate = Read.GetModelInstance(nativeNames[2]).Model;
            Model? halfturret = hunter == Hunter.Weavel
                ? Read.GetModelInstance("WeavelAlt_Turret_lod0").Model : null;

            var references = new List<(CharacterModelPart Part, int Lod, Model Model)>
            {
                (CharacterModelPart.Biped, 0, biped),
                (CharacterModelPart.Biped, 1, bipedLod1),
                (CharacterModelPart.ViewModel, 0, viewModel),
                (CharacterModelPart.AlternateForm, 0, alternate)
            };
            if (halfturret != null)
                references.Add((CharacterModelPart.Halfturret, 0, halfturret));

            string root = Path.GetFullPath(output ?? DefaultOutput(hunter));
            string referenceRoot = Path.Combine(root, "reference");
            string starterRoot = Path.Combine(root, "starter");
            Directory.CreateDirectory(referenceRoot);
            Directory.CreateDirectory(starterRoot);
            Collada.ExportModel(biped, exportRoot: referenceRoot);
            Collada.ExportModel(bipedLod1, exportRoot: referenceRoot);
            Collada.ExportModel(viewModel, exportRoot: referenceRoot);
            Collada.ExportModel(alternate, exportRoot: referenceRoot);
            if (halfturret != null)
                Collada.ExportModel(halfturret, exportRoot: referenceRoot);

            WriteInventory(Path.Combine(root, "native-reference.json"), hunter, references);
            // Export the actual engine pose as well as the Blender animation
            // reference. Native nodes can ignore parents or remain unanimated;
            // reconstructing their pose as a generic armature loses that behavior.
            var idleGun = new ModelInstance(viewModel);
            int idleId = Metadata.GunAnimationIds[(int)hunter, (int)Entities.GunAnimation.Idle, 0];
            idleGun.SetAnimation(idleId, AnimFlags.NoLoop);
            viewModel.ComputeNodeMatrices(0);
            viewModel.AnimateNodes(0, true, Matrix4.Identity, viewModel.Scale, idleGun.AnimInfo);
            File.WriteAllText(Path.Combine(root, "viewmodel-native-idle.json"), JsonSerializer.Serialize(new {
                hunter = hunter.ToString(), nativeAnimationId = idleId,
                gameplayEmitter = new[] {0f, 0f, Fixed.ToFloat(Metadata.PlayerValues[(int)hunter].MuzzleOffset)},
                nodes = viewModel.Nodes.Select(n => {
                    var m = n.Animation;
                    return new { n.Name, matrix = new[] {
                        new[] {m.M11,m.M21,m.M31,m.M41}, new[] {m.M12,m.M22,m.M32,m.M42},
                        new[] {m.M13,m.M23,m.M33,m.M43}, new[] {m.M14,m.M24,m.M34,m.M44} } };
                })
            }, Json));

            string hunterFolder = hunter.ToString().ToLowerInvariant();
            var manifest = new CharacterModelPackManifest
            {
                Format = CharacterModelPack.CurrentFormat,
                Id = hunterFolder + "-hd",
                Models =
                {
                    StarterEntry(hunter, CharacterModelPart.Biped,
                        hunterFolder + "/biped.glb", biped, lod: 0),
                    StarterEntry(hunter, CharacterModelPart.ViewModel,
                        hunterFolder + "/viewmodel.glb", viewModel, lod: 0)
                }
            };
            File.WriteAllText(Path.Combine(starterRoot, "characters.json"),
                JsonSerializer.Serialize(manifest, Json));
            File.WriteAllText(Path.Combine(starterRoot, "biped-lod1-entry.json"),
                JsonSerializer.Serialize(StarterEntry(hunter, CharacterModelPart.Biped,
                    hunterFolder + "/biped_lod1.glb", bipedLod1, lod: 1), Json));
            File.WriteAllText(Path.Combine(starterRoot, "biped-weighted4-entry.json"),
                JsonSerializer.Serialize(WeightedStarterEntry(hunter, CharacterModelPart.Biped,
                    hunterFolder + "/biped_weighted4.glb", biped, lod: 0), Json));
            File.WriteAllText(Path.Combine(starterRoot, "biped-lod1-weighted4-entry.json"),
                JsonSerializer.Serialize(WeightedStarterEntry(hunter, CharacterModelPart.Biped,
                    hunterFolder + "/biped_lod1_weighted4.glb", bipedLod1, lod: 1), Json));
            File.WriteAllText(Path.Combine(starterRoot, "viewmodel-weighted4-entry.json"),
                JsonSerializer.Serialize(WeightedStarterEntry(hunter, CharacterModelPart.ViewModel,
                    hunterFolder + "/viewmodel_weighted4.glb", viewModel, lod: 0), Json));
            File.WriteAllText(Path.Combine(starterRoot, "alternate-form-entry.json"),
                JsonSerializer.Serialize(StarterEntry(hunter, CharacterModelPart.AlternateForm,
                    hunterFolder + "/altform.glb", alternate, lod: 0), Json));
            File.WriteAllText(Path.Combine(starterRoot, "alternate-form-weighted4-entry.json"),
                JsonSerializer.Serialize(WeightedStarterEntry(hunter,
                    CharacterModelPart.AlternateForm,
                    hunterFolder + "/altform_weighted4.glb", alternate, lod: 0), Json));
            if (halfturret != null)
            {
                File.WriteAllText(Path.Combine(starterRoot, "halfturret-entry.json"),
                    JsonSerializer.Serialize(StarterEntry(hunter, CharacterModelPart.Halfturret,
                        hunterFolder + "/halfturret.glb", halfturret, lod: 0), Json));
                File.WriteAllText(Path.Combine(starterRoot, "halfturret-weighted4-entry.json"),
                    JsonSerializer.Serialize(WeightedStarterEntry(hunter,
                        CharacterModelPart.Halfturret,
                        hunterFolder + "/halfturret_weighted4.glb",
                        halfturret, lod: 0), Json));
            }
            Directory.CreateDirectory(Path.Combine(starterRoot, hunterFolder));
            File.WriteAllText(Path.Combine(root, "prepare-biped-rigid.py"),
                CharacterModelBlenderHelper.Generate(hunter, CharacterModelPart.Biped, biped,
                    $"starter/{hunterFolder}/biped.glb"));
            File.WriteAllText(Path.Combine(root, "prepare-biped-weighted4.py"),
                CharacterModelWeightedBlenderHelper.Generate(
                    hunter, CharacterModelPart.Biped, biped,
                    $"starter/{hunterFolder}/biped_weighted4.glb"));
            File.WriteAllText(Path.Combine(root, "prepare-biped-lod1-rigid.py"),
                CharacterModelBlenderHelper.Generate(hunter, CharacterModelPart.Biped, bipedLod1,
                    $"starter/{hunterFolder}/biped_lod1.glb"));
            File.WriteAllText(Path.Combine(root, "prepare-biped-lod1-weighted4.py"),
                CharacterModelWeightedBlenderHelper.Generate(
                    hunter, CharacterModelPart.Biped, bipedLod1,
                    $"starter/{hunterFolder}/biped_lod1_weighted4.glb"));
            File.WriteAllText(Path.Combine(root, "prepare-viewmodel-rigid.py"),
                CharacterModelBlenderHelper.Generate(hunter, CharacterModelPart.ViewModel, viewModel,
                    $"starter/{hunterFolder}/viewmodel.glb"));
            File.WriteAllText(Path.Combine(root, "prepare-viewmodel-weighted4.py"),
                CharacterModelWeightedBlenderHelper.Generate(
                    hunter, CharacterModelPart.ViewModel, viewModel,
                    $"starter/{hunterFolder}/viewmodel_weighted4.glb"));
            File.WriteAllText(Path.Combine(root, "prepare-altform-rigid.py"),
                CharacterModelBlenderHelper.Generate(hunter, CharacterModelPart.AlternateForm, alternate,
                    $"starter/{hunterFolder}/altform.glb"));
            File.WriteAllText(Path.Combine(root, "prepare-altform-weighted4.py"),
                CharacterModelWeightedBlenderHelper.Generate(
                    hunter, CharacterModelPart.AlternateForm, alternate,
                    $"starter/{hunterFolder}/altform_weighted4.glb"));
            if (halfturret != null)
            {
                File.WriteAllText(Path.Combine(root, "prepare-halfturret-rigid.py"),
                    CharacterModelBlenderHelper.Generate(hunter, CharacterModelPart.Halfturret,
                        halfturret, $"starter/{hunterFolder}/halfturret.glb"));
                File.WriteAllText(Path.Combine(root, "prepare-halfturret-weighted4.py"),
                    CharacterModelWeightedBlenderHelper.Generate(
                        hunter, CharacterModelPart.Halfturret, halfturret,
                        $"starter/{hunterFolder}/halfturret_weighted4.glb"));
            }
            File.WriteAllText(Path.Combine(root, "README.md"),
                Readme(hunter, biped, bipedLod1, viewModel, alternate, halfturret));

            Console.WriteLine($"[charactermodelkit] {hunter} authoring kit: {root}");
            Console.WriteLine($"[charactermodelkit] biped nodes={biped.Nodes.Count}, materials={biped.Materials.Count}, matrix palette={biped.NodeMatrixIds.Count}");
            Console.WriteLine($"[charactermodelkit] viewmodel nodes={viewModel.Nodes.Count}, materials={viewModel.Materials.Count}, matrix palette={viewModel.NodeMatrixIds.Count}");
            return 0;
        }

        public static int ValidatePack(string root)
        {
            try
            {
                CharacterModelPack pack = CharacterModelPack.Load(Path.GetFullPath(root));
                int found = 0;
                foreach (Hunter hunter in Enum.GetValues<Hunter>())
                {
                    foreach (CharacterModelPart part in Enum.GetValues<CharacterModelPart>())
                    {
                        for (int lod = 0; lod <= CharacterModelPack.MaximumLod; lod++)
                        {
                            if (!pack.TryResolve(hunter, part, lod, out CharacterModelAsset asset)) continue;
                            found++;
                            Model native = Read.GetModelInstance(NativeModelName(hunter, part, lod)).Model;
                            if (!CharacterModelPack.ValidateNativeRig(asset, native, out string? rigIssue))
                                throw new InvalidDataException(rigIssue);
                            if (asset.Skinning == CharacterSkinningMode.Weighted4)
                            {
                                CharacterWeightedModelData geometry =
                                    CharacterWeightedModelLoader.Load(asset);
                                ValidateMaterials(native, geometry, asset);
                                Console.WriteLine(
                                    $"[charactermodelvalidate] {hunter}/{part}/lod{lod}: "
                                    + $"weighted4, {geometry.Joints.Count} joints, "
                                    + $"{geometry.Primitives.Count} primitives, "
                                    + $"{geometry.VertexCount} vertices, "
                                    + $"{geometry.IndexCount / 3} triangles");
                            }
                            else
                            {
                                CharacterRigidModelData geometry =
                                    CharacterRigidModelLoader.Load(asset);
                                ValidateMaterials(native, geometry, asset);
                                Console.WriteLine(
                                    $"[charactermodelvalidate] {hunter}/{part}/lod{lod}: "
                                    + $"rigid, {geometry.Primitives.Count} primitives, "
                                    + $"{geometry.VertexCount} vertices, "
                                    + $"{geometry.IndexCount / 3} triangles");
                            }
                        }
                    }
                }
                if (found == 0) throw new InvalidDataException("Character model pack contains no resolvable models.");
                Console.WriteLine($"[charactermodelvalidate] PASS: {found} model(s)");
                return 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidDataException or JsonException or ArgumentException
                or FormatException or OverflowException or KeyNotFoundException)
            {
                Console.Error.WriteLine("[charactermodelvalidate] FAIL: " + ex.Message);
                return 1;
            }
        }

        private static CharacterModelManifestEntry StarterEntry(Hunter hunter,
            CharacterModelPart part, string path, Model model, int lod)
        {
            var entry = new CharacterModelManifestEntry
            {
                Hunter = hunter.ToString(),
                Part = part,
                Lod = lod,
                Model = path,
                Skinning = CharacterSkinningMode.RigidNodes
            };
            foreach (string name in RigidNodeNames(model))
                entry.BoneMap[name] = name;
            return entry;
        }

        private static CharacterModelManifestEntry WeightedStarterEntry(Hunter hunter,
            CharacterModelPart part, string path, Model model, int lod)
        {
            var entry = new CharacterModelManifestEntry
            {
                Hunter = hunter.ToString(),
                Part = part,
                Lod = lod,
                Model = path,
                Skinning = CharacterSkinningMode.Weighted4
            };
            foreach (string name in WeightedNodeNames(model))
                entry.BoneMap[name] = name;
            return entry;
        }

        internal static IReadOnlyList<string> WeightedNodeNames(Model model)
        {
            IEnumerable<int> ids = model.NodeMatrixIds.Count > 0
                ? model.NodeMatrixIds.Distinct()
                : model.Nodes.Select((node, index) => (node, index))
                    .Where(value => value.node.MeshCount > 0)
                    .Select(value => value.index);
            string[] names = ids.Where(index => index >= 0 && index < model.Nodes.Count)
                .Select(index => model.Nodes[index].Name)
                .Where(name => !String.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (names.Length == 0 || names.Length > CharacterWeightedModelLoader.MaximumJoints)
                throw new InvalidDataException(
                    $"{model.Name} exposes {names.Length} weighted joints; "
                    + $"Weighted4 supports 1-{CharacterWeightedModelLoader.MaximumJoints}.");
            return names;
        }

        internal static IReadOnlyList<string> RigidNodeNames(Model model)
        {
            IEnumerable<int> ids;
            if (model.NodeMatrixIds.Count > 0)
            {
                var usedSlots = new HashSet<int>();
                foreach (IReadOnlyList<RenderInstruction> list in model.RenderInstructionLists)
                {
                    foreach (RenderInstruction instruction in list)
                    {
                        if (instruction.Code == InstructionCode.MTX_RESTORE
                            && instruction.Arguments.Count > 0)
                            usedSlots.Add((int)instruction.Arguments[0]);
                    }
                }
                IEnumerable<int> slots = usedSlots.Count > 0
                    ? usedSlots.OrderBy(slot => slot)
                    : Enumerable.Range(0, model.NodeMatrixIds.Count);
                ids = slots.Where(slot => slot >= 0 && slot < model.NodeMatrixIds.Count)
                    .Select(slot => model.NodeMatrixIds[slot])
                    .Distinct();
            }
            else
            {
                ids = model.Nodes.Select((node, index) => (node, index))
                    .Where(value => value.node.MeshCount > 0)
                    .Select(value => value.index);
            }
            return ids.Where(index => index >= 0 && index < model.Nodes.Count)
                .Select(index => model.Nodes[index].Name)
                .Where(name => !String.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static void ValidateMaterials(Model native, CharacterWeightedModelData geometry, CharacterModelAsset asset)
        {
            var materialNames = native.Materials.Select(material => material.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (CharacterWeightedPrimitive primitive in geometry.Primitives)
            {
                if (String.IsNullOrWhiteSpace(primitive.MaterialName)
                    || !materialNames.Contains(primitive.MaterialName))
                    throw new InvalidDataException(
                        $"Weighted HD material '{primitive.MaterialName ?? "(unnamed)"}' "
                        + $"does not exist in native model {native.Name}.");
                var material = native.Materials.First(m => m.Name.Equals(primitive.MaterialName, StringComparison.OrdinalIgnoreCase));
                if (asset.NativeSupplementMaterials.Contains(material.Name))
                    throw new InvalidDataException($"HD material '{material.Name}' duplicates its explicit native supplement.");
                if (!CharacterModelRuntime.CanUseAuthoredTexcoords(material.TexgenMode, primitive.Albedo != null))
                    throw new InvalidDataException($"Weighted HD material '{material.Name}' requires generated coordinates without an authored albedo.");
            }
        }

        private static void ValidateMaterials(Model native, CharacterRigidModelData geometry, CharacterModelAsset asset)
        {
            var materialNames = native.Materials.Select(material => material.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (CharacterRigidPrimitive primitive in geometry.Primitives)
            {
                if (!String.IsNullOrWhiteSpace(primitive.MaterialName)
                    && !materialNames.Contains(primitive.MaterialName))
                    throw new InvalidDataException(
                        $"HD material '{primitive.MaterialName}' does not exist in native model {native.Name}.");
                if (asset.NativeSupplementMaterials.Count > 0)
                {
                    Material material;
                    if (!String.IsNullOrWhiteSpace(primitive.MaterialName))
                        material = native.Materials.First(m => m.Name.Equals(primitive.MaterialName, StringComparison.OrdinalIgnoreCase));
                    else
                    {
                        Node node = native.GetNodeByName(primitive.TargetNode)!;
                        if (node.MeshCount <= 0) throw new InvalidDataException($"HD node '{node.Name}' has no native material.");
                        material = native.Materials[native.Meshes[node.MeshId / 2].MaterialId];
                    }
                    if (asset.NativeSupplementMaterials.Contains(material.Name))
                        throw new InvalidDataException($"HD material '{material.Name}' duplicates its explicit native supplement.");
                }
            }
        }

        private static string NativeModelName(Hunter hunter, CharacterModelPart part, int lod)
        {
            if (part == CharacterModelPart.Halfturret)
            {
                if (hunter != Hunter.Weavel || lod != 0)
                    throw new InvalidDataException("Only Weavel halfturret LOD0 has a native model.");
                return "WeavelAlt_Turret_lod0";
            }
            IReadOnlyList<string> models = Metadata.HunterModels[hunter];
            return part switch
            {
                CharacterModelPart.Biped when lod is 0 or 1 => models[lod],
                CharacterModelPart.AlternateForm when lod == 0 => models[2],
                CharacterModelPart.ViewModel when lod == 0 => models[3],
                _ => throw new InvalidDataException($"Unsupported native character part {part}.")
            };
        }

        private static void WriteInventory(string path, Hunter hunter,
            IEnumerable<(CharacterModelPart Part, int Lod, Model Model)> models)
        {
            var payload = new
            {
                format = 1,
                hunter = hunter.ToString(),
                models = models.Select(value => Describe(value.Part, value.Lod, value.Model)).ToArray()
            };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, Json));
        }

        private static object Describe(CharacterModelPart part, int lod, Model model)
        {
            int[] paletteSlots = Enumerable.Repeat(-1, model.Nodes.Count).ToArray();
            for (int i = 0; i < model.NodeMatrixIds.Count; i++)
            {
                int node = model.NodeMatrixIds[i];
                if (node >= 0 && node < paletteSlots.Length) paletteSlots[node] = i;
            }

            return new
            {
                part,
                lod,
                model = model.Name,
                modelScale = Vec(model.Scale),
                matrixPalette = model.NodeMatrixIds.Select((nodeIndex, slot) => new
                {
                    slot,
                    nodeIndex,
                    nodeName = nodeIndex >= 0 && nodeIndex < model.Nodes.Count
                        ? model.Nodes[nodeIndex].Name : ""
                }).ToArray(),
                nodes = model.Nodes.Select((node, index) => new
                {
                    index,
                    node.Name,
                    node.ParentIndex,
                    parentName = node.ParentIndex >= 0 ? model.Nodes[node.ParentIndex].Name : null,
                    node.ChildIndex,
                    node.NextIndex,
                    node.MeshCount,
                    meshIds = node.GetMeshIds().ToArray(),
                    matrixPaletteSlot = paletteSlots[index],
                    position = Vec(node.Position),
                    rotationRadians = Vec(node.Angle),
                    scale = Vec(node.Scale),
                    billboard = node.BillboardMode.ToString()
                }).ToArray(),
                materials = model.Materials.Select((material, index) => new
                {
                    index,
                    material.Name,
                    material.TextureId,
                    material.PaletteId,
                    material.Lighting,
                    material.Alpha,
                    texgen = material.TexgenMode.ToString(),
                    culling = material.Culling.ToString(),
                    meshIds = model.Meshes.Select((mesh, meshIndex) => (mesh, meshIndex))
                        .Where(value => value.mesh.MaterialId == index)
                        .Select(value => value.meshIndex).ToArray()
                }).ToArray()
            };
        }

        private static float[] Vec(Vector3 value) => new[] { value.X, value.Y, value.Z };

        private static string DefaultOutput(Hunter hunter)
        {
            string basePath = !String.IsNullOrWhiteSpace(Paths.Export)
                ? Paths.Export : AppPaths.UserDataDirectory;
            return Path.Combine(basePath, "character-model-kit", hunter.ToString().ToLowerInvariant());
        }

        private static string Readme(Hunter hunter, Model biped, Model bipedLod1,
            Model viewModel, Model alternate, Model? halfturret)
        {
            string folder = hunter.ToString().ToLowerInvariant();
            string turretReference = halfturret == null ? ""
                : $"- `reference/{halfturret.Name}/` contains Weavel's halfturret reference.\n";
            string turretHelper = halfturret == null ? ""
                : " Weavel kits also include `prepare-halfturret-rigid.py`.";
            string turretStep = halfturret == null ? ""
                : $"9. Optional: author `starter/{folder}/halfturret.glb` and append `starter/halfturret-entry.json`.\n";
            int validateStep = halfturret == null ? 9 : 10;
            int installStep = validateStep + 1;
            return $"""
# {hunter} HD character authoring kit

Generated from the currently configured extracted game data.

## Reference exports

- `reference/{biped.Name}/` contains the native near biped DAE, textures and Blender import script.
- `reference/{bipedLod1.Name}/` contains the native distant biped reference.
- `reference/{viewModel.Name}/` contains the first-person model reference.
- `reference/{alternate.Name}/` contains the alternate-form reference.
{turretReference}- `native-reference.json` is the exact node, parent, matrix-palette and material inventory.

The generated Blender scripts already reconstruct the native armature and assign
vertices to native node groups from the MPH matrix IDs. Use those exports as the
proportion/pose reference.

The kit also contains `prepare-biped-rigid.py`,
`prepare-biped-lod1-rigid.py`, `prepare-viewmodel-rigid.py`, and
`prepare-altform-rigid.py`.{turretHelper} After the corresponding native
Blender import is loaded and your upgraded mesh is bound to
those native rigid groups, run the helper. It evaluates the current armature
pose, converts every segment back into native bone-local coordinates, preserves
UV/material identity, rejects soft/missing weights and cross-bone triangles, and
exports directly to the expected starter GLB path.

Apply subdivision/remesh/topology-changing modifiers before running the helper.
A live topology-changing modifier is rejected so vertex-group identity cannot
silently drift.

## Rigid replacement contract

The shipping GLBs must be segmented, not skinned:

1. Keep source mesh node names equal to the native node names in
   `starter/characters.json`.
2. Each segment's vertices must be authored in that native node's local space.
3. Do not export Armature modifiers, JOINTS_0/WEIGHTS_0, morph targets or external buffers.
4. Use native material names from `native-reference.json`. This makes Project Prime
   reuse the existing hunter texture/PBR replacement bindings.
5. Export biped as `starter/{folder}/biped.glb`.
6. Export the arm cannon as `starter/{folder}/viewmodel.glb`.
7. Optional: author `starter/{folder}/biped_lod1.glb` and append
   `starter/biped-lod1-entry.json` to the `models` array.
8. Optional: author `starter/{folder}/altform.glb` and append
   `starter/alternate-form-entry.json`.
{turretStep}{validateStep}. Validate before installing:
   `ProjectPrime -charactermodelvalidate "starter"`.
{installStep}. Copy the completed starter contents to `character-models/default` and enable
   **HD character models** in Graphics.

The biped starter maps {RigidNodeNames(biped).Count} native matrix nodes.
The viewmodel starter maps {RigidNodeNames(viewModel).Count} native matrix nodes.

## Weighted4 smooth-skin alternative

For smooth shoulders, elbows and other joints, use the matching
`prepare-*-weighted4.py` helper instead of the rigid helper. Weighted exporters
keep the Armature modifier and standard glTF skin, enforce at most four non-zero
native-bone influences per vertex, temporarily mark only the native 32-joint
palette as deform bones, and preserve native material names.

The starter folder contains matching `*-weighted4-entry.json` snippets. A rigid
and Weighted4 entry for the same hunter/part/LOD are alternatives: include
**one**, never both, because that presentation identity may resolve to only one
replacement asset.

Weighted4 GLBs use the same local-only pack, material/PBR bindings, fallback,
LOD identity and validation command as rigid replacements. Run
`ProjectPrime -charactermodelvalidate "starter"` after substituting the desired
weighted entry into `characters.json`.
""";
        }
    }
}
