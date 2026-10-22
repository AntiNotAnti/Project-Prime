// Stable keys mirror CosmeticCatalog; checked by the cosmetics validation suite.
const hunters = ["samus", "kanden", "trace", "sylux", "noxus", "spire", "weavel"];
const armor = new Set(["armor.none", "armor.lightning", "armor.pestilence", "armor.eclipse", "armor.inferno", "armor.glacial", "armor.void", "armor.radiant", "armor.phase", "armor.spectral", "armor.spike", "armor.solar", "armor.lumen", "armor.corruption", "armor.aurora", "armor.quantum", "armor.thunderstorm", "armor.orbital", "armor.dna", "armor.warp", "armor.starfall"]);
const death = new Set(["death.classic", "death.inferno_burst", "death.cryo_shatter", "death.plasma_dissolve", "death.void_collapse", "death.quantum", "death.radiant_ascension", "death.lightning_discharge"]);
export type Loadout = { hunter: number; skin_key: string; armor_effect_key: string; death_effect_key: string };
export function validLoadout(value: unknown): value is Loadout {
  if (value === null || typeof value !== "object" || Array.isArray(value)) return false;
  const v = value as Loadout;
  return Number.isInteger(v.hunter) && v.hunter >= 0 && v.hunter < 7
    && typeof v.skin_key === "string" && typeof v.armor_effect_key === "string" && typeof v.death_effect_key === "string"
    && ["skin.default", ...["obsidian", "alimbic", "ceramic", "circuit", "tiger", "nebula"].map(variant => `skin.${hunters[v.hunter]}.${variant}`)].includes(v.skin_key)
    && armor.has(v.armor_effect_key) && death.has(v.death_effect_key);
}
