namespace MphRead.Mods.Multiplayer;

// Private offline mechanics. A gameplay rule is an explicit finite value;
// imported assets, character previews, skin IDs and suit colors never select it.
internal enum CharacterGameplayVariant : byte { Native, TrooperV1, TrooperDroneV1, TrooperDroneCombatV1, TrooperArsenalV1 = 4 }
internal readonly record struct CharacterGameplayRules(byte Version, CharacterGameplayVariant Variant)
{
    internal const byte CurrentVersion = 1;
    internal static CharacterGameplayRules Native => new(CurrentVersion, CharacterGameplayVariant.Native);
    internal static CharacterGameplayRules TrooperV1 => new(CurrentVersion, CharacterGameplayVariant.TrooperV1);
    internal static CharacterGameplayRules TrooperDroneV1 => new(CurrentVersion, CharacterGameplayVariant.TrooperDroneV1);
    internal static CharacterGameplayRules TrooperDroneCombatV1 => new(CurrentVersion, CharacterGameplayVariant.TrooperDroneCombatV1);
    internal static CharacterGameplayRules TrooperArsenalV1 => new(CurrentVersion, CharacterGameplayVariant.TrooperArsenalV1);
    internal bool UsesPirateBlaster => Version == CurrentVersion && Variant == CharacterGameplayVariant.TrooperArsenalV1;
    internal bool UsesScoutDrone => Version == CurrentVersion
        && Variant is CharacterGameplayVariant.TrooperDroneV1 or CharacterGameplayVariant.TrooperDroneCombatV1 or CharacterGameplayVariant.TrooperArsenalV1;
    internal bool IsValidFor(Hunter hunter) => Version == CurrentVersion && (Variant switch
    {
        CharacterGameplayVariant.Native => hunter is >= Hunter.Samus and <= Hunter.Guardian,
        CharacterGameplayVariant.TrooperV1 or CharacterGameplayVariant.TrooperDroneV1 or CharacterGameplayVariant.TrooperDroneCombatV1 or CharacterGameplayVariant.TrooperArsenalV1 => hunter == Hunter.Weavel,
        _ => false
    });
}
