using System;
using System.IO;
using System.Text;

namespace MphRead.Mods.Render.Characters
{
    internal static class CharacterModelAssetCheck
    {
        public static int Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "project-prime-character-model-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "samus"));
                WriteGlb(Path.Combine(root, "samus", "biped.glb"),
                    """{"asset":{"version":"2.0"},"nodes":[{"name":"Body"}],"meshes":[{"primitives":[{}]}]}""");

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
                Check(pack.TryResolve(Hunter.Samus, CharacterModelPart.Biped, out CharacterModelAsset asset),
                    "Samus biped resolves by stable hunter/part identity");
                Check(asset.Skinning == CharacterSkinningMode.RigidNodes
                    && asset.SourceNodes.Contains("Body") && asset.PrimitiveCount == 1 && !asset.HasSkin,
                    "GLB metadata and rigid-node contract survive validation");

                string valid = File.ReadAllText(Path.Combine(root, "characters.json"));
                string traversal = valid.Replace("samus/biped.glb", "../outside.glb", StringComparison.Ordinal);
                File.WriteAllText(Path.Combine(root, "characters.json"), traversal);
                ExpectInvalid(() => CharacterModelPack.Load(root), "path traversal is rejected before model IO");

                File.WriteAllText(Path.Combine(root, "characters.json"),
                    valid.Replace(""rigidNodes"", ""weighted4"", StringComparison.Ordinal));
                ExpectInvalid(() => CharacterModelPack.Load(root),
                    "weighted skinning cannot claim a GLB with no skin");

                File.WriteAllText(Path.Combine(root, "characters.json"),
                    valid.Replace(""Body": "Body"", ""Missing": "Body"", StringComparison.Ordinal));
                ExpectInvalid(() => CharacterModelPack.Load(root),
                    "manifest mappings cannot name absent GLB nodes");

                File.WriteAllText(Path.Combine(root, "characters.json"), valid);
                Console.WriteLine("[charactermodelcheck] safe pack loading, GLB inspection and fallback contract passed");
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

        private static void WriteGlb(string path, string json)
        {
            byte[] source = Encoding.UTF8.GetBytes(json);
            int jsonLength = (source.Length + 3) & ~3;
            int totalLength = checked(12 + 8 + jsonLength);
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
            writer.Write(0x46546C67u);
            writer.Write(2u);
            writer.Write((uint)totalLength);
            writer.Write((uint)jsonLength);
            writer.Write(0x4E4F534Au);
            writer.Write(source);
            for (int i = source.Length; i < jsonLength; i++) writer.Write((byte)' ');
        }
    }
}
