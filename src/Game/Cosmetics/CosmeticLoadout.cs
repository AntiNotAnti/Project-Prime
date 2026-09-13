namespace MphRead.Cosmetics;

/// <summary>Stable persistent identities. These values never cross the gameplay wire.</summary>
public readonly record struct CosmeticLoadout(
    string SkinKey,
    string ArmorEffectKey,
    string DeathEffectKey)
{
    public static CosmeticLoadout DefaultFor(Hunter hunter) => new(
        CosmeticKeys.DefaultSkin(hunter),
        CosmeticKeys.NoArmorEffect,
        CosmeticKeys.DefaultDeathEffect);
}

/// <summary>Compact presentation-only values frozen into a match roster.</summary>
public readonly record struct CosmeticLoadoutIds(
    ushort SkinId,
    ushort ArmorEffectId,
    ushort DeathEffectId)
{
    public static CosmeticLoadoutIds Default => default;
}
