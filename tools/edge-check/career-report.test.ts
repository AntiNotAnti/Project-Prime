import { strict as assert } from "node:assert";
import { test } from "node:test";
import { registerHooks } from "node:module";
import { readFileSync } from "node:fs";
import { createHash } from "node:crypto";

// Execute the actual Edge handler with a local tagged-SQL fixture. No remote
// connection or production credential is loaded by this adapter.
const serverKey = "local-opaque-career-reporter-key-32";
const serverHash = createHash("sha256").update(serverKey).digest("hex");
let handler: (req: Request) => Promise<Response>;
(globalThis as any).Deno = { env: { get: () => "local-fixture" }, serve: (fn: typeof handler) => { handler = fn; } };
(globalThis as any).localCareerSql = (...args: any[]) => fixtureSql(...args);
(globalThis as any).localCareerSql.json = (value: unknown) => value;
registerHooks({ resolve(specifier, context, next) {
  if (specifier.startsWith("npm:postgres")) return { url: "data:text/javascript,export default()=>globalThis.localCareerSql", shortCircuit: true };
  return next(specifier, context);
} });
await import("../../supabase/functions/career-report/index.ts");
const request = (body?: string | ReadableStream<Uint8Array>, key = serverKey) => new Request("http://localhost/functions/v1/career-report", { method: body === undefined ? "GET" : "POST", headers: { authorization: "Bearer " + key }, body, duplex: "half" } as RequestInit);

test("opaque reporter gateway configuration and real handler probe", async () => {
  assert.match(readFileSync(new URL("../../supabase/config.toml", import.meta.url), "utf8"), /\[functions\.career-report\]\s*(?:#[^\n]*\s*)*verify_jwt\s*=\s*false/);
  const response = await handler(request());assert.equal(response.status, 200);assert.equal((await response.json()).ok, true);
  assert.equal((await handler(request(undefined, "unknown-key-of-at-least-32-characters"))).status, 401);
});
test("unknown reporter rejected before consuming streamed request body", async () => {
  let pulls = 0;
  const stream = new ReadableStream<Uint8Array>({ pull(c) { pulls++; c.enqueue(new Uint8Array(1024)); } }, { highWaterMark: 0 });
  assert.equal((await handler(request(stream, "unknown-key-of-at-least-32-characters"))).status, 401);assert.equal(pulls, 0);
});
test("authenticated streamed body ceiling and JSON object validation", async () => {
  let canceled = false;
  const stream = new ReadableStream<Uint8Array>({ pull(c) { c.enqueue(new Uint8Array(65536)); }, cancel() { canceled = true; } }, { highWaterMark: 0 });
  assert.equal((await handler(request(stream))).status, 413);assert.equal(canceled, true);
  for (const body of ["null", "[]", "{", "{}"] ) assert.equal((await handler(request(body))).status, 400);
});

let ingested: any;
const fixtureSql: any = async (strings: TemplateStringsArray, ...values: any[]) => {
  const text = strings.join("?");
  if (text.includes("select server_id")) return values[0] === serverHash ? [{ server_id: "12345678-1234-4234-9234-123456789abc", trust_class: 2, enabled: true }] : [];
  if (text.includes("update public.project_prime_career_reporters")) return [];
  if (text.includes("ingest_project_prime_career_match")) { ingested = { report: values[0], trust: values[2], hash: values[3] }; return [{ value: { status: "accepted" } }]; }
  throw new Error("Unexpected fixture SQL");
};
fixtureSql.json = (value: unknown) => value;
(globalThis as any).localCareerSql = fixtureSql;
const epoch = Math.floor(Date.now() / 1000) - 10;
const account = (n: number) => `00000000-0000-4000-8000-${n.toString(16).padStart(12, "0")}`;
const ticket = async (player: string, clientId: number, salt = 0, expires = epoch + 600) => {
  const payload = Buffer.from(JSON.stringify({ v: 1, sub: player, cid: clientId, iat: epoch - 600 + salt, exp: expires })).toString("base64url");
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode("project-prime-career-ticket:v1:local-fixture"), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  return "pp1." + payload + "." + Buffer.from(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(payload))).toString("base64url");
};
const participant = async (id: number, player = account(id), clientId = id, joined = 0, left = 60) => ({
  participant_id: account(id), client_id: clientId, career_ticket: await ticket(player, clientId), display_name: "Player", hunter: 0, team: 0,
  started_match: joined === 0, departed: left !== 120, played_ticks: left - joined, joined_ticks: joined, left_ticks: left,
  segment_ended_at_utc: new Date(epoch * 1000).toISOString(), standing: 0, team_standing: 0, single_hunter: true,
  metrics: { kills: id, deaths: 0, assists: 0, damage: 0, headshots: 0, octolith_scores: 0, nodes_captured: 0, kills_as_prime: 0, longest_kill_streak: id, beam_kills: Array(9).fill(0) },
});
const report = (participants: any[], extra = {}) => ({ version: 2, match_id: account(999), server_incarnation: account(998), room_key: "MP1 SANCTORUS", started_at_utc: new Date((epoch - 10) * 1000).toISOString(), ended_at_utc: new Date(epoch * 1000).toISOString(), played_ticks: 120, accounting_complete: true, rating_eligible: true, participants, ...extra });
const post = async (value: unknown) => { ingested = undefined; return await handler(request(JSON.stringify(value))); };

test("nine cumulative signed entrants and a full 128 segment report are accepted", async () => {
  const entrants = await Promise.all(Array.from({ length: 128 }, (_, i) => participant(i + 1, account(i + 1), i + 1, i < 8 ? 0 : 60, 120)));
  assert.equal((await post(report(entrants.slice(0, 9)))).status, 201);
  assert.equal(ingested.report.participants.length, 9); assert.equal(ingested.trust, 2);
  assert.equal((await post(report(entrants))).status, 201); assert.equal(ingested.report.participants.length, 128);
  assert.equal((await post(report([...entrants, await participant(129)]))).status, 400);
});
test("same-account refresh/rejoin merges disjoint metrics once; concurrent duplicate remains Practice", async () => {
  const a = await participant(1, account(1), 10, 0, 60), b = await participant(2, account(1), 11, 60, 120);
  b.career_ticket = await ticket(account(1), 11, 1);
  assert.equal((await post(report([a, b]))).status, 201);
  const merged = ingested.report.participants; assert.equal(merged.length, 1); assert.equal(merged[0].metrics.kills, 3); assert.equal(merged[0].played_ticks, 120); assert.equal(merged[0].started_match, true); assert.equal(merged[0].departed, false);
  b.joined_ticks = 30; b.played_ticks = 90;
  assert.equal((await post(report([a, b]))).status, 201); assert.equal(ingested.trust, 5); assert.equal(ingested.report.rating_eligible, false); assert.ok(ingested.report.participants.every((p: any) => p.player_id === null));
});
test("changed account on reused client ID retains independent signed counters", async () => {
  const a = await participant(1, account(1), 10, 0, 60), b = await participant(2, account(2), 10, 60, 120);
  assert.equal((await post(report([a, b]))).status, 201); assert.equal(ingested.report.participants.length, 2);
  assert.deepEqual(ingested.report.participants.map((p: any) => [p.player_id, p.metrics.kills]), [[account(1), 1], [account(2), 2]]);
  b.career_ticket = a.career_ticket; b.client_id = 11;
  assert.equal((await post(report([a, b]))).status, 201); assert.equal(ingested.report.participants[1].player_id, null);
});
test("spectators excluded; expired, modified signatures and mismatched client IDs cannot authorize", async () => {
  const a = await participant(1), spectator = { ...await participant(2), is_spectator: true };
  assert.equal((await post(report([a, spectator]))).status, 201); assert.equal(ingested.report.participants.length, 1);
  assert.equal((await post(report([spectator]))).status, 200); assert.equal(ingested, undefined);
  a.career_ticket = await ticket(account(1), 1, -3600, epoch - 601);
  assert.equal((await post(report([a]))).status, 201); assert.equal(ingested.trust, 5);
  a.career_ticket = await ticket(account(1), 1);
  a.career_ticket = a.career_ticket.slice(0, -2) + "AA";
  assert.equal((await post(report([a]))).status, 201); assert.equal(ingested.trust, 5); assert.equal(ingested.report.participants[0].player_id, null);
  assert.equal((await post(report([await participant(1)], { accounting_complete: false }))).status, 201); assert.equal(ingested.trust, 5);
});
test("immutable report retries produce the same normalized hash; interval and ID fences reject", async () => {
  const a = await participant(1); const payload = report([a]);
  assert.equal((await post(payload)).status, 201); const hash = ingested.hash;
  assert.equal((await post(payload)).status, 201); assert.equal(ingested.hash, hash);
  assert.equal((await post(report([a, a]))).status, 400);
  assert.equal((await post(report([{ ...a, left_ticks: 121 }]))).status, 400);
  assert.equal((await post(report([{ ...a, joined_ticks: 1 }]))).status, 400);
});

test("a departed segment verifies at its own end; legacy duplicate-account policy stays Practice", async () => {
  const a = await participant(1);
  a.career_ticket = await ticket(account(1), 1, -3600, epoch - 400);
  a.segment_ended_at_utc = new Date((epoch - 600) * 1000).toISOString();
  const data = report([a], { started_at_utc: new Date((epoch - 3600) * 1000).toISOString() });
  assert.equal((await post(data)).status, 201); assert.equal(ingested.trust, 2); assert.equal(ingested.report.participants[0].player_id, account(1));
  const b = await participant(2, account(1), 2);
  assert.equal((await post(report([await participant(1), b], { version: 1 }))).status, 201); assert.equal(ingested.trust, 5);
});

test("rejoined starting account receives one correct whole-match career outcome", async () => {
  const a = await participant(1, account(1), 10, 0, 60), b = await participant(2, account(1), 11, 60, 120);
  const opponent = await participant(3, account(3), 12, 0, 120); opponent.standing = 1;
  assert.equal((await post(report([a, b, opponent]))).status, 201);
  const returning = ingested.report.participants.find((p: any) => p.player_id === account(1));
  assert.equal(returning.started_match, true); assert.equal(returning.departed, false); assert.equal(returning.won, true); assert.equal(returning.tied, false);
  opponent.standing = 0;
  assert.equal((await post(report([a, b, opponent]))).status, 201);
  assert.equal(ingested.report.participants[0].won, false); assert.equal(ingested.report.participants[0].tied, true);
});

test("version 1 accepted outbox retry preserves exact legacy normalized PayloadHash", async () => {
  const player = account(10).toUpperCase();
  const a = await participant(10, player, 10); a.participant_id = account(20).toUpperCase();
  const data: any = report([a], { version: 1, accounting_complete: false, mode: 0, teams: false, team_count: 0, end_reason: "completed" });
  // This object is the pre-upgrade Edge normalization, including its original
  // insertion order and UUID text. New segment-only fields must stay absent.
  const legacy = {
    version: 1, match_id: data.match_id, wire_match_id: data.wire_match_id, server_incarnation: data.server_incarnation,
    build_version: data.build_version, protocol_version: data.protocol_version, started_at_utc: data.started_at_utc,
    ended_at_utc: data.ended_at_utc, played_ticks: data.played_ticks, end_reason: data.end_reason, room_key: data.room_key,
    mode: data.mode, teams: false, team_count: data.team_count, contains_bots: false, rating_eligible: true,
    participants: [{ participant_id: a.participant_id, client_id: a.client_id, player_id: player, display_name: a.display_name,
      hunter: a.hunter, single_hunter: true, team: a.team, started_match: a.started_match, departed: a.departed,
      played_ticks: a.played_ticks, standing: a.standing, team_standing: a.team_standing, won: false, tied: false,
      metrics: a.metrics }],
  };
  const expectedText = JSON.stringify(legacy);
  const expectedHash = createHash("sha256").update(expectedText).digest("hex").toUpperCase();
  assert.equal((await post(data)).status, 201);
  assert.equal(JSON.stringify(ingested.report), expectedText); assert.equal(ingested.hash, expectedHash); assert.equal(ingested.trust, 2);
});
