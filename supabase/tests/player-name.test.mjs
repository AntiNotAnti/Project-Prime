// Run: node --experimental-strip-types supabase/tests/player-name.test.mjs
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { normalizePlayerName, supportedPlayerGlyphs } from '../functions/_shared/player-name.ts';
const source = readFileSync(new URL('../../src/MphRead/Text/MphGlyphMap.cs', import.meta.url), 'utf8');
const table = source.slice(source.indexOf('new string[]'), source.indexOf('private static readonly Dictionary'));
const glyphs = [...table.matchAll(/"(?:[^"\\]|\\.)*"/g)].map(m => JSON.parse(m[0])).filter(c => c.length === 1);
const expected = new Set([...Array.from({ length: 95 }, (_, i) => String.fromCharCode(32 + i)), ...glyphs]);
assert.deepEqual([...supportedPlayerGlyphs].sort(), [...expected].sort());
for (const value of ['ハンター∞', 'JÄRRETT™', '「PRIME」', 'あ'.repeat(24)]) assert.equal(normalizePlayerName(value), value);
for (const value of ['', 'A'.repeat(25), '😀', 'a\u202e', 'a\0', 'a\n', '\ud800', 'a\u200b']) assert.equal(normalizePlayerName(value), null);
assert.equal(normalizePlayerName('e\u0301'), 'é');
console.log('Backend name validation and native table parity passed.');
