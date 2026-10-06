using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Formats;
using NVector3 = System.Numerics.Vector3;

namespace MphRead.Mods.Render.Characters
{
    /// <summary>Shared bounded embedded material contract for rigid and Weighted4 GLBs.</summary>
    internal static class CharacterEmbeddedMaterialLoader
    {
        internal static bool ReadDoubleSided(JsonElement[] materials, int index)
        {
            if (index < 0) return false;
            if ((uint)index >= materials.Length) throw new InvalidDataException("Character material index is invalid.");
            return materials[index].TryGetProperty("doubleSided", out var value) && value.GetBoolean();
        }

        internal static CharacterEmbeddedAlbedo? ReadAlbedo(JsonElement root, JsonElement[] materials,
            JsonElement[] views, byte[] binary, int materialIndex)
        {
            if (materialIndex < 0) return null;
            if ((uint)materialIndex >= materials.Length)
                throw new InvalidDataException("Character material index is invalid.");
            JsonElement material = materials[materialIndex];
            if (!material.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr)
                || !pbr.TryGetProperty("baseColorTexture", out JsonElement texture)) return null;
            bool opaque = !material.TryGetProperty("alphaMode", out JsonElement alphaMode)
                || alphaMode.GetString() == "OPAQUE";
            CharacterEmbeddedAlbedo albedo = ReadEmbeddedTexture(root, views, binary, texture, opaque);
            if (material.TryGetProperty("extras", out var extras)
                && extras.TryGetProperty("projectPrimeRecolors", out var recolors))
            {
                if (recolors.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Character recolors must be an object keyed by native suit index.");
                var variants = new Dictionary<int, CharacterEmbeddedAlbedo>();
                foreach (var entry in recolors.EnumerateObject())
                {
                    if (!int.TryParse(entry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int suit)
                        || suit is < 1 or > 5 || variants.ContainsKey(suit))
                        throw new InvalidDataException("Character recolor indices must be unique native indices 1–5.");
                    CharacterEmbeddedAlbedo variant = ReadEmbeddedTexture(root, views, binary, entry.Value, opaque);
                    if (variant.WrapS != albedo.WrapS || variant.WrapT != albedo.WrapT)
                        throw new InvalidDataException("Character recolors must share the base albedo sampler.");
                    variants.Add(suit, variant);
                }
                albedo = albedo with { Recolors = variants };
            }
            return albedo;
        }

        internal static CharacterEmbeddedMaterialMaps? ReadMaterialMaps(JsonElement root,
            JsonElement[] materials, JsonElement[] views, byte[] binary, int index, CharacterEmbeddedAlbedo? albedo)
        {
            if (index < 0) return null;
            JsonElement material = materials[index];
            CharacterEmbeddedAlbedo? normal = material.TryGetProperty("normalTexture", out var nt)
                ? ReadEmbeddedTexture(root, views, binary, nt, true) : null;
            CharacterEmbeddedAlbedo? emissive = material.TryGetProperty("emissiveTexture", out var et)
                ? ReadEmbeddedTexture(root, views, binary, et, true) : null;
            material.TryGetProperty("pbrMetallicRoughness", out var pbr);
            CharacterEmbeddedAlbedo? mr = pbr.ValueKind == JsonValueKind.Object
                && pbr.TryGetProperty("metallicRoughnessTexture", out var mt)
                ? ReadEmbeddedTexture(root, views, binary, mt, true) : null;
            if (normal == null && emissive == null && mr == null) return null;
            // The retained material path has one UV set and sampler per draw.
            // Reject a mismatch instead of silently sampling a map incorrectly.
            if (albedo == null) throw new InvalidDataException("Character material maps require an embedded albedo.");
            foreach (var map in new[] {normal, mr, emissive})
                if (map != null && (map.WrapS != albedo.WrapS || map.WrapT != albedo.WrapT))
                    throw new InvalidDataException("Character material maps must share the albedo sampler wrapping.");
            float normalScale = Factor(nt, "scale", 1, 100);
            float metallic = Factor(pbr, "metallicFactor", 1, 1);
            float roughness = Factor(pbr, "roughnessFactor", 1, 1);
            NVector3 ef = NVector3.Zero;
            if (material.TryGetProperty("emissiveFactor", out var e))
            {
                if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 3)
                    throw new InvalidDataException("Character emissiveFactor must contain three values.");
                ef = new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
                if (!Finite(ef) || ef.X < 0 || ef.Y < 0 || ef.Z < 0 || ef.X > 1 || ef.Y > 1 || ef.Z > 1)
                    throw new InvalidDataException("Character emissiveFactor is outside [0,1].");
            }
            bool runtimeEncoded = false;
            bool physicalOrm = false;
            if (material.TryGetProperty("extras", out var extras)
                && extras.TryGetProperty("projectPrimeRuntimeMaps", out var encoded))
            {
                if (encoded.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidDataException("Character runtime map encoding flag must be boolean.");
                runtimeEncoded = encoded.GetBoolean();
            }
            if (material.TryGetProperty("extras", out extras)
                && extras.TryGetProperty("projectPrimeMaterialEncoding", out var encoding))
            {
                if (encoding.ValueKind != JsonValueKind.String || encoding.GetString() != "orm")
                    throw new InvalidDataException("Character physical material encoding must be 'orm'.");
                if (mr == null)
                    throw new InvalidDataException("Character ORM encoding requires a metallicRoughnessTexture.");
                physicalOrm = true;
                // AO is packed into the material map's R channel. A separate
                // occlusion texture would require another sampler and is not
                // silently ignored by this bounded four-texture contract.
                if (material.TryGetProperty("occlusionTexture", out var ao)
                    && (Int(ao, "index", -1) != Int(pbr.GetProperty("metallicRoughnessTexture"), "index", -2)
                        || Int(ao, "texCoord", 0) != 0 || ao.TryGetProperty("extensions", out _)
                        || Factor(ao, "strength", 1, 1) != 1))
                    throw new InvalidDataException("Character occlusionTexture must share the ORM texture at unit strength.");
            }
            if (runtimeEncoded && ((normal != null && normalScale != 1)
                || (mr != null && (metallic != 1 || roughness != 1))
                || (emissive != null && ef != NVector3.One)))
                throw new InvalidDataException("Preconverted runtime maps require identity channel factors.");
            return new(normal, mr, emissive, normalScale, metallic, roughness, ef, runtimeEncoded, physicalOrm);
        }

        private static float Factor(JsonElement element, string name, float fallback, float max)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return fallback;
            float result = value.GetSingle();
            if (!float.IsFinite(result) || result < 0 || result > max)
                throw new InvalidDataException($"Character material {name} is outside [0,{max}].");
            return result;
        }

        private static CharacterEmbeddedAlbedo ReadEmbeddedTexture(JsonElement root,
            JsonElement[] views, byte[] binary, JsonElement texture, bool opaque)
        {
            if (Int(texture, "texCoord", 0) != 0 || texture.TryGetProperty("extensions", out _))
                throw new InvalidDataException("Character texture requires untransformed TEXCOORD_0.");
            JsonElement[] textures = Elements(root, "textures");
            int textureIndex = Int(texture, "index", -1);
            if ((uint)textureIndex >= textures.Length)
                throw new InvalidDataException("Character texture texture index is invalid.");
            JsonElement[] images = Elements(root, "images");
            JsonElement encodedTexture = textures[textureIndex];
            int imageIndex = Int(encodedTexture, "source", -1);
            if (encodedTexture.TryGetProperty("extensions", out var extensions))
            {
                if (extensions.ValueKind != JsonValueKind.Object || extensions.EnumerateObject().Count() != 1
                    || !extensions.TryGetProperty("KHR_texture_basisu", out var basis))
                    throw new InvalidDataException("Character texture only supports KHR_texture_basisu.");
                imageIndex = Int(basis,"source",-1);
            }
            if ((uint)imageIndex >= images.Length)
                throw new InvalidDataException("Character texture image index is invalid.");
            JsonElement image = images[imageIndex];
            string? mime = image.TryGetProperty("mimeType", out JsonElement mimeValue)
                ? mimeValue.GetString() : null;
            if (encodedTexture.TryGetProperty("extensions",out _) && mime != "image/ktx2")
                throw new InvalidDataException("KHR_texture_basisu must reference a KTX2 image.");
            int viewIndex = Int(image, "bufferView", -1);
            if (image.TryGetProperty("uri", out _) || mime is not ("image/png" or "image/jpeg" or "image/ktx2")
                || (uint)viewIndex >= views.Length)
                throw new InvalidDataException("Character texture must be an embedded PNG, JPEG or KTX2.");
            JsonElement view = views[viewIndex];
            int offset = Int(view, "byteOffset", 0);
            int length = Int(view, "byteLength", -1);
            if (Int(view, "buffer", 0) != 0 || offset < 0 || length <= 0
                || length > 32 * 1024 * 1024 || (long)offset + length > binary.Length)
                throw new InvalidDataException("Character texture buffer is invalid or exceeds 32 MiB.");
            RepeatMode wrapS = RepeatMode.Repeat;
            RepeatMode wrapT = RepeatMode.Repeat;
            if (textures[textureIndex].TryGetProperty("sampler", out JsonElement samplerIndex))
            {
                JsonElement[] samplers = Elements(root, "samplers");
                int index = samplerIndex.GetInt32();
                if ((uint)index >= samplers.Length)
                    throw new InvalidDataException("Character texture sampler index is invalid.");
                wrapS = ReadWrapMode(Int(samplers[index], "wrapS", 10497));
                wrapT = ReadWrapMode(Int(samplers[index], "wrapT", 10497));
            }
            byte[] payload = CharacterEmbeddedImages.Intern(binary.AsSpan(offset, length));
            if (mime == "image/ktx2")
            {
                using var stream = new MemoryStream(payload, writable:false);
                PreparedTextureCodec.ValidateKtx2(stream);
            }
            return new(payload, opaque, wrapS, wrapT);
        }

        private static RepeatMode ReadWrapMode(int value) => value switch
        {
            33071 => RepeatMode.Clamp,
            33648 => RepeatMode.Mirror,
            10497 => RepeatMode.Repeat,
            _ => throw new InvalidDataException("Character texture sampler wrap mode is invalid.")
        };

        private static JsonElement[] Elements(JsonElement root, string name)
            => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
        private static int Int(JsonElement value, string property, int fallback)
            => value.TryGetProperty(property, out JsonElement found) && found.TryGetInt32(out int result) ? result : fallback;
        private static bool Finite(NVector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }
}
