using System.Text;

namespace MphRead.Cosmetics.Tools;

/// <summary>Writes the deterministic runtime identity table after strict source validation.</summary>
public static class CosmeticPackCompiler
{
    private const uint Magic = 0x4F435050; // PPCO, little endian
    private const ushort Format = 1;

    public static string Compile(string packRoot, string outputPath)
    {
        CosmeticCatalogLoadResult result = CosmeticCatalogLoader.Load(packRoot);
        if (!result.IsValid)
            throw new InvalidDataException("Cosmetic package validation failed: "
                + string.Join("; ", result.Issues.Select(issue => issue.Message)));
        string output = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(output);
        if (directory != null) Directory.CreateDirectory(directory);
        string temporary = output + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: false))
            {
                CosmeticCatalog catalog = result.Catalog;
                writer.Write(Magic);
                writer.Write(Format);
                writer.Write(catalog.CatalogVersion);
                WriteString(writer, catalog.CatalogHash);
                WriteEntries(writer, catalog.Skins.Values.OrderBy(value => value.Id),
                    (writer, value) => writer.Write((byte)value.Hunter));
                WriteEntries(writer, catalog.ArmorEffects.Values.OrderBy(value => value.Id),
                    static (_, _) => { });
                WriteEntries(writer, catalog.DeathEffects.Values.OrderBy(value => value.Id),
                    static (_, _) => { });
            }
            File.Move(temporary, output, overwrite: true);
            return output;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void WriteEntries<T>(BinaryWriter writer, IEnumerable<T> entries,
        Action<BinaryWriter, T> extra) where T : class
    {
        T[] values = entries.ToArray();
        writer.Write(checked((ushort)values.Length));
        foreach (T value in values)
        {
            (ushort id, string key) = value switch
            {
                SkinDefinition skin => (skin.Id, skin.Key),
                ArmorEffectDefinition armor => (armor.Id, armor.Key),
                DeathEffectDefinition death => (death.Id, death.Key),
                _ => throw new InvalidOperationException("Unsupported cosmetic definition.")
            };
            writer.Write(id);
            WriteString(writer, key);
            extra(writer, value);
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(checked((ushort)bytes.Length));
        writer.Write(bytes);
    }
}
