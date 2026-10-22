import { validLoadout } from './catalog.ts';
import assert from 'node:assert/strict';
const valid = { hunter: 0, skin_key: 'skin.samus.obsidian', armor_effect_key: 'armor.inferno', death_effect_key: 'death.quantum' };
assert(validLoadout(valid));
for (const value of [null, [], {}, {...valid, hunter: -1}, {...valid, hunter: 7}, {...valid, hunter: 0.5},
  {...valid, skin_key: '../../secret'}, {...valid, skin_key: 'skin.trace.obsidian'},
  {...valid, armor_effect_key: 'armor.future'}, {...valid, death_effect_key: 'death.future'},
  {...valid, skin_key: ['skin.default']}]) assert(!validLoadout(value));
const hunters = ['samus','kanden','trace','sylux','noxus','spire','weavel'];
for (let hunter = 0; hunter < 7; hunter++) for (const variant of ['obsidian','alimbic','ceramic','circuit','tiger','nebula'])
  assert(validLoadout({...valid,hunter,skin_key:`skin.${hunters[hunter]}.${variant}`}));
console.log('cosmetic endpoint catalog checks passed');

for (const effect of ["orbital", "dna", "warp", "starfall"]) assert(validLoadout({...valid, armor_effect_key: `armor.${effect}`}));
