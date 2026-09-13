using MphRead.Cosmetics;

namespace MphRead.Cosmetics.Tools;

public static class CosmeticValidator
{
    public static CosmeticCatalogLoadResult Validate(string packRoot)
        => CosmeticCatalogLoader.Load(packRoot);
}
