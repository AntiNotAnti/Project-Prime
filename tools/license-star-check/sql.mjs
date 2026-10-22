import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

// Reuse the installed, pinned test dependency; never contact a database/server.
const require = createRequire(new URL('../social-db-check/package.json', import.meta.url));
const modulePath = require.resolve('@electric-sql/pglite');
const { PGlite } = require('@electric-sql/pglite');
const sourceUrl = new URL('../../supabase/migrations/20260921195500_authoritative_career_ingestion.sql', import.meta.url);
const source = await readFile(sourceUrl, 'utf8');
const hash = text => createHash('sha256').update(text).digest('hex');
function extract(name) {
  const start = source.indexOf(`CREATE OR REPLACE FUNCTION public.${name}(`);
  assert(start >= 0, `missing ${name}`);
  const end = source.indexOf('$function$;', source.indexOf('AS $function$', start));
  assert(end > start, `missing terminator for ${name}`);
  return source.slice(start, end + '$function$;'.length);
}
const definitions = ['project_prime_rating_tier', 'project_prime_rating_pair_delta'].map(name => ({ name, sql: extract(name) }));
// Explicit MPH table expectations: rows=self tier, columns=opponent tier.
const gains = [[2,4,7,10,15],[1,3,6,10,15],[1,2,4,8,12],[1,1,4,5,8],[1,1,2,4,6]];
const losses = [[1,1,1,0,0],[2,3,1,0,0],[4,3,4,3,2],[8,8,6,5,4],[15,12,10,8,6]];
const boundaries = [[0,1],[39,1],[40,2],[139,2],[140,3],[389,3],[390,4],[749,4],[750,5],[850,5]];
const representatives = [0,40,140,390,750];
const db = new PGlite();
const tierResults = [], pairResults = [];
try {
  for (const definition of definitions) await db.exec(definition.sql);
  for (const [points, expected] of boundaries) {
    const actual = (await db.query('select public.project_prime_rating_tier($1::integer) as value', [points])).rows[0].value;
    assert.equal(actual, expected, `tier at ${points}`);
    tierResults.push({ points, expected, actual });
  }
  for (let self = 0; self < 5; self++) for (let opponent = 0; opponent < 5; opponent++) {
    for (const [result, expected] of [[1,gains[self][opponent]],[-1,-losses[self][opponent]],[0,0]]) {
      const actual = (await db.query('select public.project_prime_rating_pair_delta($1::integer,$2::integer,$3::integer) as value', [representatives[self], representatives[opponent], result])).rows[0].value;
      // SQL may return numeric zero; JS -0 in a negated expectation is equivalent.
      assert.equal(actual === 0 ? 0 : actual, expected === 0 ? 0 : expected, `pair ${self+1}/${opponent+1} result ${result}`);
      pairResults.push({ selfTier: self+1, opponentTier: opponent+1, result, expected, actual });
    }
  }
} finally { await db.close(); }
const output = new URL('../../artifacts/rmlui-license-stars-rml90-20261018/acceptance/sql/', import.meta.url);
await mkdir(output, { recursive: true });
const proof = {
  status: 'PASS', scope: 'Exact checked-in pure SQL tier and pair-delta helpers executed in ephemeral in-memory PGlite. Does not verify full ingestion, schema constraints, deployment, live database, UI, GL, or external MPH table provenance.',
  command: 'node tools/license-star-check/sql.mjs', dependency: { name: '@electric-sql/pglite', version: JSON.parse(await readFile(new URL('../social-db-check/node_modules/@electric-sql/pglite/package.json', import.meta.url),'utf8')).version, resolvedPath: modulePath },
  source: { path: fileURLToPath(sourceUrl), sha256: hash(source) },
  helpers: definitions.map(({name,sql}) => ({ name, sha256: hash(sql) })),
  scriptSha256: hash(await readFile(new URL(import.meta.url), 'utf8')),
  checks: { tierBoundaries: tierResults.length, pairCases: pairResults.length, total: tierResults.length + pairResults.length },
  expectedTable: { gains, losses, provenance: 'Explicit test expectations matching the checked-in MPH-style policy; external original-game table provenance must be reviewed separately.' },
  tierResults, pairResults,
};
await writeFile(new URL('proof.json', output), JSON.stringify(proof, null, 2) + '\n');
console.log(`PASS ${proof.checks.total} pure SQL helper checks; proof: ${fileURLToPath(new URL('proof.json', output))}`);
