import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const load = path => readFileSync(fileURLToPath(new URL(path, import.meta.url)), "utf8");
const migration = load("../migrations/20261016214500_hunter_public_default_opt_out.sql");
const api = load("../functions/hunter-leaderboard/index.ts");

test("unset profiles are public, explicit false is never overwritten", () => {
  assert.match(migration, /ALTER COLUMN is_public SET DEFAULT true/i);
  assert.match(migration, /coalesce\s*\([\s\S]*v\.is_public[\s\S]*true/i);
  assert.match(migration, /WHEN auth\.uid\(\) IS NULL THEN false/);
  assert.doesNotMatch(migration, /UPDATE\s+prime\.hunter_public_visibility\s+SET\s+is_public/);
  assert.doesNotMatch(migration, /DELETE\s+FROM\s+prime\.hunter_public_visibility/);
});

test("owner-only preference cannot be read by unauthenticated roles", () => {
  assert.match(migration,
    /REVOKE ALL ON FUNCTION public\.project_prime_hunter_visibility\(\) FROM PUBLIC, anon/);
  assert.match(migration, /GRANT EXECUTE ON FUNCTION public\.project_prime_hunter_visibility\(\) TO authenticated/);
  assert.match(migration, /WHERE v\.player_id = auth\.uid\(\)/);
});

test("public Hunter cards support newly-created Licenses without ranked matches", () => {
  assert.match(api, /FROM prime\.hunter_licenses l/);
  assert.match(api, /LEFT JOIN prime\.career_aggregates a/);
  assert.match(api, /WHERE coalesce\(visibility\.is_public,true\)/);
  assert.match(api, /coalesce\(a\."Matches",0\)::double precision AS matches/);
  assert.doesNotMatch(api, /WHERE a\."Matches"\s*>\s*0/);
});

test("registered Hunter directory may show empty careers but ranked board still requires accepted results", () => {
  assert.match(api, /coalesce\(a\."Matches",0\)>0/);
  assert.match(api, /query\.onlyPublic[\s\S]*'career'/);
  assert.match(api, /AND \(NOT \$\{query\.onlyPublic\}::boolean OR\s+\(identity\.id IS NOT NULL AND coalesce\(v\.is_public,true\)\)\)/);
  assert.match(api, /identity\.id IS NOT NULL AND coalesce\(v\.is_public,true\)\s+AND p\."DisplayName" ILIKE/);
});

test("legacy Licenses with no authenticating owner are never discoverable by default", () => {
  assert.match(api, /JOIN auth\.users identity ON identity\.id = l\."PlayerId"/);
  assert.match(api, /LEFT JOIN auth\.users identity ON identity\.id=l\."PlayerId"/);
  assert.match(api, /\(identity\.id IS NOT NULL AND coalesce\(v\.is_public,true\)\) AS is_public/);
});

test("explicit opt-outs are anonymous in leaderboards and never resolve as a public profile", () => {
  assert.match(api, /CASE WHEN is_public THEN public_name ELSE 'Private Hunter' END/);
  assert.match(api, /CASE WHEN is_public THEN[\s\S]*md5\('project-prime:public-hunter:v1:'/);
  assert.doesNotMatch(api, /p\."Email"|"PasswordHash"|"OriginalReport"|"RoomKey"|"AccessToken"/);
});
