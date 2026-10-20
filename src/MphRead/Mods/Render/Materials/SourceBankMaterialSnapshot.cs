using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MphRead.Mods.Import;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Render.Materials;

/// <summary>Detached material references for one model generation. No pixels,
/// open streams or GPU objects are retained; first verification runs on decode.</summary>
internal sealed class SourceBankMaterialSnapshot
{
    private readonly Dictionary<string, MaterialImage> _images = new(StringComparer.Ordinal);

    internal static SourceBankMaterialSnapshot? Capture(MapDefinition definition)
    {
        if (!definition.Materials.Any(m => m?.AlbedoBank != null)) return null;
        if (definition.BundlePath != null || definition.MapId == Guid.Empty)
            throw new InvalidDataException("Source banks require a private loose project with a map identity.");
        string root = Path.GetFullPath(definition.BaseDirectory ?? CustomRooms.MapDirectory);
        var result = new SourceBankMaterialSnapshot();
        var banks = new Dictionary<(string, string), Lazy<EchoesSourceTextureBank.Bank>>();
        foreach (MapMaterial material in definition.Materials)
        {
            if (material.AlbedoBank is not { } reference) continue;
            ValidateReference(definition, material);
            // Copy every mutable DTO field before creating the worker closure.
            string relative = reference.Bank, expectedBank = reference.BankSha256.ToLowerInvariant();
            string expectedPixels = reference.RgbaSha256.ToLowerInvariant();
            uint resource = reference.ResourceId;
            int width = reference.Width, height = reference.Height;
            var bankKey = (relative, expectedBank);
            if (!banks.TryGetValue(bankKey, out var bank))
            {
                bank = new(() => WithRead(root, relative, stream =>
                {
                    var inspected = EchoesSourceTextureBank.Inspect(stream);
                    if (inspected.Sha256 != expectedBank)
                        throw new InvalidDataException("Private source bank generation changed.");
                    return inspected;
                }), LazyThreadSafetyMode.ExecutionAndPublication);
                banks.Add(bankKey, bank);
            }
            string scope = $"map/{definition.MapId:N}/material/{material.Id:N}";
            string identity = $"source-bank/{expectedBank}/{resource:x8}/{expectedPixels}";
            MaterialImage image = MaterialImage.FromRgbaFactory(identity, width, height, () =>
            {
                var captured = bank.Value;
                var entry = captured.RequireEntry(resource);
                if (entry.Width != width || entry.Height != height || entry.RgbaSha256 != expectedPixels)
                    throw new InvalidDataException("Private source image reference does not match its bank.");
                byte[] pixels = WithRead(root, relative, stream => EchoesSourceTextureBank.ReadImage(stream, captured, resource));
                // This initial authoring binding inherits an opaque native
                // fallback. Preserve bank bytes, but refuse source alpha until
                // a separately qualified material policy owns classification.
                for (int at = 3; at < pixels.Length; at += 4)
                    if (pixels[at] != 255)
                        throw new InvalidDataException("Private source bank albedo currently requires opaque pixels.");
                return pixels;
            });
            if (!result._images.TryAdd(scope, image))
                throw new InvalidDataException("Duplicate private source material identity.");
        }
        return result;
    }

    internal ResolvedMaterial? Resolve(MaterialAssetKey key)
    {
        int separator = key.Value.LastIndexOf("/recolor/", StringComparison.Ordinal);
        if (separator < 0 || !int.TryParse(key.Value[(separator + 9)..], out int recolor) || recolor < 0)
            return null;
        return _images.TryGetValue(key.Value[..separator], out var image)
            ? new(key, image, null, null, null) : null;
    }

    internal static void ValidateReference(MapDefinition definition, MapMaterial material)
    {
        MapSourceBankImage reference = material.AlbedoBank
            ?? throw new InvalidDataException("Missing source bank reference.");
        MapPackageReader.CanonicalName(reference.Bank);
        if (!reference.Bank.EndsWith(".bin", StringComparison.Ordinal)
            || !definition.Assets.Any(a => a?.Path == reference.Bank && a.Kind == "source-texture-bank")
            || !Hex(reference.BankSha256) || !Hex(reference.RgbaSha256) || reference.ResourceId == 0
            || reference.Width is < 1 or > 4096 || reference.Height is < 1 or > 4096
            || (long)reference.Width * reference.Height * 4 > EchoesSourceTextureBank.MaxImageBytes
            || definition.MapId == Guid.Empty || material.Id == Guid.Empty || !material.ProceduralWhite || material.Texture != null
            || material.Albedo != null || material.Normal != null || material.SpecularRoughness != null
            || material.Emissive != null || material.Animation != null)
            throw new InvalidDataException("Invalid private source bank material or unsupported companion/animation.");
    }

    internal static T WithRead<T>(string root, string relative, Func<Stream, T> consume)
    {
        MapPackageReader.CanonicalName(relative);
        string path = Path.GetFullPath(Path.Combine(root, relative));
        LocalImportBundle.CheckPath(path, root);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.RandomAccess);
        if (stream.Length > EchoesSourceTextureBank.MaxBankBytes)
            throw new InvalidDataException("Private source bank exceeds its component budget.");
        return consume(stream);
    }

    private static bool Hex(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
}
