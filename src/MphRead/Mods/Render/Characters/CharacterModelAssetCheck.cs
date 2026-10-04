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
                Console.WriteLine(
                    "[charactermodelcheck] pack safety and rigid POSITION/NORMAL/UV/index decode passed");
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
    }
}
