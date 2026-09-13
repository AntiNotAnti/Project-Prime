namespace MphRead.Cosmetics.Tools;

internal static class CosmeticCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length < 2 || args[0] is "help" or "--help" or "-h")
        {
            Console.WriteLine("ProjectPrimeTools cosmetics validate PACK | cook PACK [OUTPUT] | list PACK | inspect PACK KEY");
            return args.Length == 0 || args[0] is "help" or "--help" or "-h" ? 0 : 2;
        }
        string command = args[0];
        string root = args[1];
        CosmeticCatalogLoadResult result = CosmeticValidator.Validate(root);
        foreach (CosmeticValidationIssue issue in result.Issues)
            Console.Error.WriteLine($"{issue.Kind}: {issue.Message}"
                + (issue.Path == null ? "" : $" [{issue.Path}]"));
        if (!result.IsValid) return 1;
        if (command == "validate")
        {
            Console.WriteLine($"Valid cosmetic catalog {result.Catalog.CatalogHash}.");
            return 0;
        }
        if (command == "cook")
        {
            string output = args.Length >= 3 ? args[2] : Path.Combine(root, "catalog.ppcos");
            Console.WriteLine(CosmeticPackCompiler.Compile(root, output));
            return 0;
        }
        if (command == "list")
        {
            foreach ((string kind, ushort id, string key) in Entries(result.Catalog))
                Console.WriteLine($"{kind}\t{id}\t{key}");
            return 0;
        }
        if (command == "inspect" && args.Length == 3)
        {
            string key = args[2];
            var match = Entries(result.Catalog).FirstOrDefault(value => value.Key == key);
            if (match.Key == null) { Console.Error.WriteLine($"Cosmetic '{key}' was not found."); return 1; }
            Console.WriteLine($"{match.Kind}\t{match.Id}\t{match.Key}");
            return 0;
        }
        Console.Error.WriteLine($"Unknown or incomplete cosmetics command '{command}'.");
        return 2;
    }

    private static IEnumerable<(string Kind, ushort Id, string Key)> Entries(CosmeticCatalog catalog)
    {
        foreach (SkinDefinition value in catalog.Skins.Values.OrderBy(value => value.Id))
            yield return ("skin", value.Id, value.Key);
        foreach (ArmorEffectDefinition value in catalog.ArmorEffects.Values.OrderBy(value => value.Id))
            yield return ("armor", value.Id, value.Key);
        foreach (DeathEffectDefinition value in catalog.DeathEffects.Values.OrderBy(value => value.Id))
            yield return ("death", value.Id, value.Key);
    }
}
