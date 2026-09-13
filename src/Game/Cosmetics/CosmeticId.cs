using System;

namespace MphRead.Cosmetics;

/// <summary>A bounded, canonical identity used by authored and persisted cosmetics.</summary>
public readonly record struct CosmeticId
{
    public const int MaximumLength = 96;

    public string Value { get; }

    public CosmeticId(string value)
    {
        if (!IsValid(value))
            throw new ArgumentException("Cosmetic keys must be lowercase dotted identifiers.", nameof(value));
        Value = value;
    }

    public override string ToString() => Value;

    public static bool TryCreate(string? value, out CosmeticId id)
    {
        if (IsValid(value))
        {
            id = new CosmeticId(value!);
            return true;
        }
        id = default;
        return false;
    }

    public static bool IsValid(string? value)
    {
        if (value is not { Length: >= 3 and <= MaximumLength }
            || value[0] == '.' || value[^1] == '.') return false;
        bool segmentHasCharacter = false;
        foreach (char character in value)
        {
            if (character == '.')
            {
                if (!segmentHasCharacter) return false;
                segmentHasCharacter = false;
            }
            else if (character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
            {
                segmentHasCharacter = true;
            }
            else return false;
        }
        return segmentHasCharacter;
    }
}

public static class CosmeticKeys
{
    public const string NoArmorEffect = "prime.armor_fx.none";
    public const string DefaultDeathEffect = "prime.death.default";

    public static string DefaultSkin(Hunter hunter)
    {
        if (hunter > Hunter.Guardian) throw new ArgumentOutOfRangeException(nameof(hunter));
        return $"prime.skin.{hunter.ToString().ToLowerInvariant()}.classic";
    }
}
