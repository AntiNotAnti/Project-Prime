using System;
using System.IO;
using System.Numerics;
using System.Text;

namespace MphRead.Mods.Render.Characters
{
    internal static class CharacterModelAssetCheck
    {
        public static int Run()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "project-prime-character-model-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "samus"));
                WriteTriangleGlb(Path.Combine(root, "samus", "biped.glb"));

                File.WriteAllText(Path.Combine(root, "characters.json"),
                    """
                    {
                      "format": 1,
                      "id": "synthetic-check",
                      "models": [
                        {
                          "hunter": "Samus",
                          "part": "biped",
                          "model": "samus/biped.glb",
                          "skinning": "rigidNodes",
                          "boneMap": { "Body": "Body" }
                        }
                      ]
                    }
                    """);

                CharacterModelPack pack = CharacterModelPack.Load(root);
                Check(pack.Count == 1, "synthetic pack loads exactly one model");
                Check(pack.TryResolve(Hunter.Samus, CharacterModelPart.Biped,
                    out CharacterModelAsset asset),
                    "Samus biped resolves by stable hunter/part identity");
                Check(asset.Skinning == CharacterSkinningMode.RigidNodes
                    && asset.Lod == 0 && asset.SourceNodes.Contains("Body")
                    && asset.PrimitiveCount == 1 && !asset.HasSkin,
                    "GLB metadata and rigid-node contract survive validation");

                CharacterRigidModelData geometry = CharacterRigidModelLoader.Load(asset);
                Check(geometry.Primitives.Count == 1
                    && geometry.VertexCount == 3 && geometry.IndexCount == 3,
                    "rigid geometry reader preserves the synthetic triangle");
                CharacterRigidPrimitive primitive = geometry.Primitives[0];
                Check(primitive.SourceNode == "Body" && primitive.TargetNode == "Body"
                    && primitive.MaterialName == "BodyMat",
                    "rigid geometry preserves retarget and material identity");
                Check(primitive.Indices.AsSpan().SequenceEqual(new uint[] { 0, 1, 2 }),
                    "rigid geometry preserves triangle indices");
                Check(Near(primitive.Vertices[1].Position, new Vector3(1, 0, 0))
                    && Near(primitive.Vertices[2].Texcoord, new Vector2(0, 1))
                    && Near(primitive.Vertices[0].Normal, Vector3.UnitZ),
                    "POSITION/NORMAL/TEXCOORD_0 decode exactly");

                string valid = File.ReadAllText(Path.Combine(root, "characters.json"));

                File.WriteAllText(Path.Combine(root, "characters.json"),
                    valid.Replace("\"part\": \"biped\"",
                        "\"part\": \"alternateForm\"", StringComparison.Ordinal));
                CharacterModelPack altPack = CharacterModelPack.Load(root);
                Check(altPack.TryResolve(Hunter.Samus, CharacterModelPart.AlternateForm,
                        out CharacterModelAsset altAsset)
                    && altAsset.Lod == 0,
                    "alternate-form LOD0 uses the shared character replacement contract");

                File.WriteAllText(Path.Combine(root, "characters.json"),
                    valid.Replace("\"hunter\": \"Samus\"",
                            "\"hunter\": \"Weavel\"", StringComparison.Ordinal)
                        .Replace("\"part\": \"biped\"",
                            "\"part\": \"halfturret\"", StringComparison.Ordinal));
                CharacterModelPack turretPack = CharacterModelPack.Load(root);
                Check(turretPack.TryResolve(Hunter.Weavel, CharacterModelPart.Halfturret,
                        out CharacterModelAsset turretAsset)
                    && turretAsset.Lod == 0,
                    "Weavel halfturret LOD0 uses the shared character replacement contract");

                File.WriteAllText(Path.Combine(root, "characters.json"), valid);
                string lod1 = valid.Replace("\"part\": \"biped\",",
                    "\"part\": \"biped\",\n                          \"lod\": 1,",
                    StringComparison.Ordinal);
                File.WriteAllText(Path.Combine(root, "characters.json"), lod1);
                CharacterModelPack lodPack = CharacterModelPack.Load(root);
                Check(!lodPack.TryResolve(Hunter.Samus, CharacterModelPart.Biped, out _)
                    && lodPack.TryResolve(Hunter.Samus, CharacterModelPart.Biped, 1,
                        out CharacterModelAsset lodAsset)
                    && lodAsset.Lod == 1,
                    "missing lod remains backward-compatible LOD0 and explicit LOD1 resolves independently");

                File.WriteAllText(Path.Combine(root, "characters.json"),
                    lod1.Replace("\"part\": \"biped\"", "\"part\": \"viewModel\"",
                        StringComparison.Ordinal));
                ExpectInvalid(() => CharacterModelPack.Load(root),
                    "non-biped replacement LOD1 is rejected");

                File.WriteAllText(Path.Combine(root, "characters.json"), valid);
                string traversal = valid.Replace("samus/biped.glb", "../outside.glb",
                    StringComparison.Ordinal);
                File.WriteAllText(Path.Combine(root, "characters.json"), traversal);
                ExpectInvalid(() => CharacterModelPack.Load(root),
                    "path traversal is rejected before model IO");

                File.WriteAllText(Path.Combine(root, "characters.json"),
                    valid.Replace("rigidNodes", "weighted4", StringComparison.Ordinal));
                ExpectInvalid(() => CharacterModelPack.Load(root),
                    "weighted skinning cannot claim a GLB with no skin");

                File.WriteAllText(Path.Combine(root, "characters.json"),
                    valid.Replace("Body", "Missing", StringComparison.Ordinal));
                ExpectInvalid(() => CharacterModelPack.Load(root),
                    "manifest mappings cannot name absent GLB nodes");

                File.WriteAllText(Path.Combine(root, "characters.json"), valid);

                WriteWeightedTriangleGlb(Path.Combine(root, "samus", "weighted.glb"));
                File.WriteAllText(Path.Combine(root, "characters.json"),
                    """
                    {
                      "format": 1,
                      "id": "synthetic-weighted-check",
                      "models": [
                        {
                          "hunter": "Samus",
                          "part": "biped",
                          "model": "samus/weighted.glb",
                          "skinning": "weighted4",
                          "boneMap": { "Body": "Body", "Arm": "Arm" }
                        }
                      ]
                    }
                    """);
                CharacterModelPack weightedPack = CharacterModelPack.Load(root);
                Check(weightedPack.TryResolve(Hunter.Samus, CharacterModelPart.Biped,
                    out CharacterModelAsset weightedAsset)
                    && weightedAsset.Skinning == CharacterSkinningMode.Weighted4
                    && weightedAsset.HasSkin,
                    "weighted pack discovers a real glTF skin");
                CharacterWeightedModelData weightedGeometry =
                    CharacterWeightedModelLoader.Load(weightedAsset);
                Check(weightedGeometry.Joints.Count == 2
                    && weightedGeometry.Primitives.Count == 1
                    && weightedGeometry.VertexCount == 3
                    && weightedGeometry.IndexCount == 3,
                    "Weighted4 loader preserves synthetic skin geometry");
                CharacterWeightedVertex weightedVertex =
                    weightedGeometry.Primitives[0].Vertices[1];
                var unpacked = CharacterWeightedModelLoader.UnpackJoints(
                    weightedVertex.PackedJoints);
                Check(unpacked.J0 == 0 && unpacked.J1 == 1
                    && MathF.Abs(weightedVertex.Weights.X - 128f / 255f) < 0.00001f
                    && MathF.Abs(weightedVertex.Weights.Y - 127f / 255f) < 0.00001f
                    && weightedGeometry.Joints[0].SourceNode == "Body"
                    && weightedGeometry.Joints[1].SourceNode == "Arm",
                    "Weighted4 joint packing, normalized weights and retarget identity decode exactly");

                File.WriteAllText(Path.Combine(root, "characters.json"), valid);

                string blender = CharacterModelBlenderHelper.Generate(
                    "Synthetic/Biped", new[] { "Body" }, new[] { "BodyMat" },
                    "starter/synthetic/biped.glb");
                Check(blender.Contains("ASSET_LABEL = \"Synthetic/Biped\"", StringComparison.Ordinal)
                    && blender.Contains("EXPECTED_BONES = set([\"Body\"])", StringComparison.Ordinal)
                    && blender.Contains("EXPECTED_MATERIALS = set([\"BodyMat\"])", StringComparison.Ordinal)
                    && blender.Contains("RELATIVE_OUTPUT = \"starter/synthetic/biped.glb\"", StringComparison.Ordinal)
                    && blender.Contains("cross rigid bone boundaries", StringComparison.Ordinal)
                    && !blender.Contains("{{", StringComparison.Ordinal),
                    "Blender rigid helper receives deterministic authoring identities");

                string weightedBlender = CharacterModelWeightedBlenderHelper.Generate(
                    "Synthetic/Biped", new[] { "Body", "Arm" },
                    new[] { "BodyMat" }, "starter/synthetic/biped_weighted4.glb");
                Check(weightedBlender.Contains(
                        "ASSET_LABEL = \"Synthetic/Biped\"", StringComparison.Ordinal)
                    && weightedBlender.Contains(
                        "EXPECTED_BONES = set([\"Body\",\"Arm\"])", StringComparison.Ordinal)
                    && weightedBlender.Contains(
                        "RELATIVE_OUTPUT = \"starter/synthetic/biped_weighted4.glb\"",
                        StringComparison.Ordinal)
                    && weightedBlender.Contains("Weighted4 allows four", StringComparison.Ordinal)
                    && weightedBlender.Contains("export_def_bones", StringComparison.Ordinal)
                    && !weightedBlender.Contains("{{", StringComparison.Ordinal),
                    "Blender Weighted4 helper receives deterministic authoring identities");

                Console.WriteLine(
                    "[charactermodelcheck] pack safety, rigid/Weighted4 geometry and Blender helper generation passed");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[charactermodelcheck] FAIL: " + ex);
                return 1;
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
                catch { }
            }
        }

        private static bool Near(Vector3 a, Vector3 b)
            => Vector3.DistanceSquared(a, b) < 0.000001f;
        private static bool Near(Vector2 a, Vector2 b)
            => Vector2.DistanceSquared(a, b) < 0.000001f;

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
        }

        private static void ExpectInvalid(Action action, string name)
        {
            try
            {
                action();
                throw new InvalidOperationException(name + " (accepted invalid input)");
            }
            catch (InvalidDataException)
            {
            }
        }

        private static void WriteWeightedTriangleGlb(string path)
        {
            using var binary = new MemoryStream();
            using (var data = new BinaryWriter(binary, Encoding.UTF8, leaveOpen: true))
            {
                foreach (float value in new float[]
                {
                    0,0,0, 1,0,0, 0,1,0
                }) data.Write(value);
                foreach (float value in new float[]
                {
                    0,0,1, 0,0,1, 0,0,1
                }) data.Write(value);
                foreach (float value in new float[]
                {
                    0,0, 1,0, 0,1
                }) data.Write(value);

                // JOINTS_0: Body, Body/Arm blend, Arm.
                foreach (byte value in new byte[]
                {
                    0,0,0,0, 0,1,0,0, 1,0,0,0
                }) data.Write(value);
                // WEIGHTS_0 normalized UBYTE.
                foreach (byte value in new byte[]
                {
                    255,0,0,0, 128,127,0,0, 255,0,0,0
                }) data.Write(value);

                data.Write((ushort)0);
                data.Write((ushort)1);
                data.Write((ushort)2);
                data.Write((ushort)0); // alignment padding before MAT4 data

                for (int matrix = 0; matrix < 2; matrix++)
                {
                    foreach (float value in new float[]
                    {
                        1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1
                    }) data.Write(value);
                }
            }
            byte[] binaryBytes = binary.ToArray();
            if (binaryBytes.Length != 256)
                throw new InvalidOperationException("Synthetic Weighted4 GLB layout changed.");

            string json = """
                {
                  "asset":{"version":"2.0"},
                  "buffers":[{"byteLength":256}],
                  "bufferViews":[
                    {"buffer":0,"byteOffset":0,"byteLength":36},
                    {"buffer":0,"byteOffset":36,"byteLength":36},
                    {"buffer":0,"byteOffset":72,"byteLength":24},
                    {"buffer":0,"byteOffset":96,"byteLength":12},
                    {"buffer":0,"byteOffset":108,"byteLength":12},
                    {"buffer":0,"byteOffset":120,"byteLength":6},
                    {"buffer":0,"byteOffset":128,"byteLength":128}
                  ],
                  "accessors":[
                    {"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},
                    {"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"},
                    {"bufferView":2,"componentType":5126,"count":3,"type":"VEC2"},
                    {"bufferView":3,"componentType":5121,"count":3,"type":"VEC4"},
                    {"bufferView":4,"componentType":5121,"normalized":true,"count":3,"type":"VEC4"},
                    {"bufferView":5,"componentType":5123,"count":3,"type":"SCALAR"},
                    {"bufferView":6,"componentType":5126,"count":2,"type":"MAT4"}
                  ],
                  "materials":[{"name":"BodyMat"}],
                  "meshes":[{"primitives":[{
                    "attributes":{
                      "POSITION":0,"NORMAL":1,"TEXCOORD_0":2,
                      "JOINTS_0":3,"WEIGHTS_0":4
                    },
                    "indices":5,
                    "material":0
                  }]}],
                  "skins":[{"joints":[0,1],"inverseBindMatrices":6}],
                  "nodes":[
                    {"name":"Body"},
                    {"name":"Arm"},
                    {"name":"WeightedMesh","mesh":0,"skin":0}
                  ]
                }
                """;
            WriteGlb(path, json, binaryBytes);
        }

        private static void WriteGlb(string path, string json, byte[] binaryBytes)
        {
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            int jsonLength = (jsonBytes.Length + 3) & ~3;
            int binLength = (binaryBytes.Length + 3) & ~3;
            int totalLength = checked(12 + 8 + jsonLength + 8 + binLength);
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
            writer.Write(0x46546C67u);
            writer.Write(2u);
            writer.Write((uint)totalLength);
            writer.Write((uint)jsonLength);
            writer.Write(0x4E4F534Au);
            writer.Write(jsonBytes);
            for (int i = jsonBytes.Length; i < jsonLength; i++) writer.Write((byte)' ');
            writer.Write((uint)binLength);
            writer.Write(0x004E4942u);
            writer.Write(binaryBytes);
            for (int i = binaryBytes.Length; i < binLength; i++) writer.Write((byte)0);
        }

        private static void WriteTriangleGlb(string path)
        {
            using var binary = new MemoryStream();
            using (var data = new BinaryWriter(binary, Encoding.UTF8, leaveOpen: true))
            {
                // POSITION
                foreach (float value in new float[]
                {
                    0,0,0, 1,0,0, 0,1,0
                }) data.Write(value);
                // NORMAL
                foreach (float value in new float[]
                {
                    0,0,1, 0,0,1, 0,0,1
                }) data.Write(value);
                // TEXCOORD_0
                foreach (float value in new float[]
                {
                    0,0, 1,0, 0,1
                }) data.Write(value);
                // indices
                data.Write((ushort)0);
                data.Write((ushort)1);
                data.Write((ushort)2);
            }
            byte[] binaryBytes = binary.ToArray();
            const int bufferLength = 102;
            if (binaryBytes.Length != bufferLength)
                throw new InvalidOperationException("Synthetic character GLB layout changed.");

            string json = """
                {
                  "asset":{"version":"2.0"},
                  "buffers":[{"byteLength":102}],
                  "bufferViews":[
                    {"buffer":0,"byteOffset":0,"byteLength":36},
                    {"buffer":0,"byteOffset":36,"byteLength":36},
                    {"buffer":0,"byteOffset":72,"byteLength":24},
                    {"buffer":0,"byteOffset":96,"byteLength":6}
                  ],
                  "accessors":[
                    {"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},
                    {"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"},
                    {"bufferView":2,"componentType":5126,"count":3,"type":"VEC2"},
                    {"bufferView":3,"componentType":5123,"count":3,"type":"SCALAR"}
                  ],
                  "materials":[{"name":"BodyMat"}],
                  "meshes":[{"primitives":[{
                    "attributes":{"POSITION":0,"NORMAL":1,"TEXCOORD_0":2},
                    "indices":3,
                    "material":0
                  }]}],
                  "nodes":[{"name":"Body","mesh":0}]
                }
                """;
            WriteGlb(path, json, binaryBytes);
        }
    }
}
