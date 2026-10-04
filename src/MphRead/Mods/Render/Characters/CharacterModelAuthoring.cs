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
            Model viewModel = Read.GetModelInstance(nativeNames[3]).Model;
            Model alternate = Read.GetModelInstance(nativeNames[2]).Model;

            string root = Path.GetFullPath(output ?? DefaultOutput(hunter));
            string referenceRoot = Path.Combine(root, "reference");
            string starterRoot = Path.Combine(root, "starter");
            Directory.CreateDirectory(referenceRoot);
            Directory.CreateDirectory(starterRoot);
            string previousExport = Paths.Export;
            try
            {
                Paths.SetPath("Export", referenceRoot);
                Collada.ExportModel(biped);
                Collada.ExportModel(viewModel);
                Collada.ExportModel(alternate);
            }
            finally
            {
                if (!String.IsNullOrWhiteSpace(previousExport))
                    Paths.SetPath("Export", previousExport);
            }

            WriteInventory(Path.Combine(root, "native-reference.json"), hunter,
                new[]
                {
                    (CharacterModelPart.Biped, biped),
                    (CharacterModelPart.ViewModel, viewModel),
                    (CharacterModelPart.AlternateForm, alternate)
                });

            string hunterFolder = hunter.ToString().ToLowerInvariant();
            var manifest = new CharacterModelPackManifest
            {
                Format = CharacterModelPack.CurrentFormat,
                Id = hunterFolder + "-hd",
                Models =
                {
                    StarterEntry(hunter, CharacterModelPart.Biped,
                        hunterFolder + "/biped.glb", biped),
                    StarterEntry(hunter, CharacterModelPart.ViewModel,
                        hunterFolder + "/viewmodel.glb", viewModel)
                }
            };
            File.WriteAllText(Path.Combine(starterRoot, "characters.json"),
                JsonSerializer.Serialize(manifest, Json));
            Directory.CreateDirectory(Path.Combine(starterRoot, hunterFolder));
            File.WriteAllText(Path.Combine(root, "README.md"), Readme(hunter, biped, viewModel, alternate));

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
                        if (!pack.TryResolve(hunter, part, out CharacterModelAsset asset)) continue;
                        found++;
                        Model native = Read.GetModelInstance(NativeModelName(hunter, part)).Model;
                        if (!CharacterModelPack.ValidateNativeRig(asset, native, out string? rigIssue))
                            throw new InvalidDataException(rigIssue);
                        CharacterRigidModelData geometry = CharacterRigidModelLoader.Load(asset);
                        ValidateMaterials(native, geometry);
                        Console.WriteLine($"[charactermodelvalidate] {hunter}/{part}: "
                            + $"{geometry.Primitives.Count} primitives, {geometry.VertexCount} vertices, "
                            + $"{geometry.IndexCount / 3} triangles");
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
            CharacterModelPart part, string path, Model model)
        {
            var entry = new CharacterModelManifestEntry
            {
                Hunter = hunter.ToString(),
                Part = part,
                Model = path,
                Skinning = CharacterSkinningMode.RigidNodes
            };
            foreach (string name in RigidNodeNames(model))
                entry.BoneMap[name] = name;
            return entry;
        }

        private static IReadOnlyList<string> RigidNodeNames(Model model)
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

        private static void ValidateMaterials(Model native, CharacterRigidModelData geometry)
        {
            var materialNames = native.Materials.Select(material => material.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (CharacterRigidPrimitive primitive in geometry.Primitives)
            {
                if (!String.IsNullOrWhiteSpace(primitive.MaterialName)
                    && !materialNames.Contains(primitive.MaterialName))
                    throw new InvalidDataException(
                        $"HD material '{primitive.MaterialName}' does not exist in native model {native.Name}.");
            }
        }

        private static string NativeModelName(Hunter hunter, CharacterModelPart part)
        {
            if (part == CharacterModelPart.Halfturret)
            {
                if (hunter != Hunter.Weavel)
                    throw new InvalidDataException("Only Weavel has a native halfturret model.");
                return "WeavelAlt_Turret_lod0";
            }
            IReadOnlyList<string> models = Metadata.HunterModels[hunter];
            return part switch
            {
                CharacterModelPart.Biped => models[0],
                CharacterModelPart.AlternateForm => models[2],
                CharacterModelPart.ViewModel => models[3],
                _ => throw new InvalidDataException($"Unsupported native character part {part}.")
            };
        }

        private static void WriteInventory(string path, Hunter hunter,
            IEnumerable<(CharacterModelPart Part, Model Model)> models)
        {
            var payload = new
            {
                format = 1,
                hunter = hunter.ToString(),
                models = models.Select(value => Describe(value.Part, value.Model)).ToArray()
            };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, Json));
        }

        private static object Describe(CharacterModelPart part, Model model)
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

        private static string Readme(Hunter hunter, Model biped, Model viewModel, Model alternate)
        {
            string folder = hunter.ToString().ToLowerInvariant();
            return $"""
# {hunter} HD character authoring kit

Generated from the currently configured extracted game data.

## Reference exports

- `reference/{biped.Name}/` contains the native biped DAE, textures and Blender import script.
- `reference/{viewModel.Name}/` contains the first-person model reference.
- `reference/{alternate.Name}/` contains the alternate-form reference.
- `native-reference.json` is the exact node, parent, matrix-palette and material inventory.

The generated Blender scripts already reconstruct the native armature and assign
vertices to native node groups from the MPH matrix IDs. Use those exports as the
proportion/pose reference.

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
7. Validate before installing:
   `ProjectPrime -charactermodelvalidate "starter"`.
8. Copy the completed starter contents to `character-models/default` and enable
   **HD character models** in Graphics.

The biped starter maps {RigidNodeNames(biped).Count} native matrix nodes.
The viewmodel starter maps {RigidNodeNames(viewModel).Count} native matrix nodes.

For smooth joints, keep the high-detail source project. The later Weighted4 slice
will accept JOINTS_0/WEIGHTS_0 without changing the material or pack identity.
""";
        }
    }
}
