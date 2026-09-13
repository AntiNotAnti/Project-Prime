namespace MphRead.Backend.Cosmetics;

/// <summary>
/// The account-owned cosmetic selection for one playable Hunter. Stable keys
/// are persisted so catalog ordering and authored asset locations can change
/// without rewriting player data.
/// </summary>
public sealed class PlayerCosmeticLoadout
{
    public Guid PlayerId { get; set; }
    public Hunter Hunter { get; set; }
    public string SkinKey { get; set; } = "";
    public string ArmorEffectKey { get; set; } = "";
    public string DeathEffectKey { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}
