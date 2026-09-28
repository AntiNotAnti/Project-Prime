// Native glyph repertoire from src/MphRead/Text/MphGlyphMap.cs.
// Keep this table in sync; player-name tests compare the two tables.
export const supportedPlayerGlyphs = new Set(Array.from(" !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~€‚ƒ„…†‡ˆ‰Š‹ŒŽ‘’“”•–—˜™š›œžŸ¡¢£¤¥¦§¨©ª«¬®¯°±²³´µ¶·¸¹º»¼½¾¿ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖ×ØÙÚÛÜÝÞßàáâãäåæçèéêëìíîïðñòóôõö÷øùúûüýþÿぁあぃいぅうぇえぉおかがきぎくぐけげこごさざしじすずせぜそぞただちぢっつづてでとどなにぬねのはばぱひびぴふぶぷへべぺほぼぽまみむめもゃやゅゆょよらりるれろゎわゐゑをんァアィイゥウェエォオカガキギクグケゲコゴサザシジスズセゼソゾタダチヂッツヅテデトドナニヌネノハバパヒビピフブプヘベペホボポマミムメモャヤュユョヨラリルレロヮワヰヱヲンヴヵㇰ、。・゛゜‾ゝゞ々−／＼「」∞∴ᐟ"+ "ー〜"));
export function normalizePlayerName(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.normalize("NFC").trim();
  const glyphs = Array.from(normalized);
  if (!glyphs.length || glyphs.length > 24 || glyphs.some(c => !supportedPlayerGlyphs.has(c))) return null;
  // Validate before trimming too, so controls cannot hide at the edges.
  if (Array.from(value.normalize("NFC")).some(c => !supportedPlayerGlyphs.has(c))) return null;
  return normalized;
}
