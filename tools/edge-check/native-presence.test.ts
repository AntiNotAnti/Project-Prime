import { strict as assert } from "node:assert";
import { test } from "node:test";
import { registerHooks } from "node:module";

const actor = "11111111-1111-4111-8111-111111111111";
const spoof = "99999999-9999-4999-8999-999999999999";
const self = "PP-1111-1111-4111-8111-1111";
const session = "cccccccc-cccc-4ccc-8ccc-cccccccccccc";
const settings = { presence_visibility: "friends", activity_visibility: "friends", invite_policy: "friends", do_not_disturb: false };
const changed = { presence_visibility: "hidden", activity_visibility: "private", invite_policy: "nobody", do_not_disturb: true };
const snapshot = { self_prime_id: self, settings: { ...settings, updated_at: "2026-10-17T12:00:00Z" }, players: [], expires_after_seconds: 45 };
let handler: (req: Request) => Promise<Response>;
let calls: { query: string; values: unknown[] }[] = [];
let failSql = "", authFailure = false;
let accepted = true;
(globalThis as any).Deno = {
  env: { get: (name: string) => ({ SUPABASE_DB_URL: "local-fixture", SUPABASE_URL: "http://auth.local",
    SUPABASE_SERVICE_ROLE_KEY: "local-service" } as Record<string,string>)[name] },
  serve: (callback: typeof handler) => { handler = callback; },
};
const sql: any = async (strings: TemplateStringsArray, ...values: unknown[]) => {
  const query = strings.join("?");
  calls.push({ query, values });
  if (failSql && query.includes(failSql)) throw new Error("private-db@example.test Bearer private-secret");
  if (query.includes("project_prime_hunter_license_for")) return [{ value: {} }];
  if (query.includes("social_native_presence_snapshot")) return [{ value: snapshot }];
  if (query.includes("social_native_presence_action")) return [{ value: { ok: accepted,
    status: accepted ? "accepted" : "privacy_changed", snapshot } }];
  throw new Error("Unexpected isolated presence fixture SQL");
};
sql.json = (value: unknown) => value;
(globalThis as any).localNativePresenceSql = sql;
registerHooks({ resolve(specifier, context, next) {
  if (specifier.startsWith("npm:postgres")) return { url: "data:text/javascript,export default()=>globalThis.localNativePresenceSql", shortCircuit: true };
  return next(specifier, context);
} });
globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
  assert.equal(String(input), "http://auth.local/auth/v1/user", "external fetch forbidden in fixture");
  if (authFailure) throw new Error("private-auth@example.test secret-token");
  const headers = new Headers(init?.headers);
  assert.equal(headers.get("apikey"), "local-service");
  const token = headers.get("authorization");
  if (token === "Bearer good-token") return new Response(JSON.stringify({ id: actor }), { status: 200 });
  if (token === "Bearer invalid-user") return new Response(JSON.stringify({ id: "not-a-uuid" }), { status: 200 });
  return new Response("{}", { status: 401 });
};
await import("../../supabase/functions/native-presence/index.ts");
const request = (body: unknown, token = "good-token") => new Request("http://localhost/functions/v1/native-presence", {
  method: "POST", headers: token ? { authorization: "Bearer " + token } : {}, body: JSON.stringify(body),
});
const mutation = (action: string, extra: Record<string, unknown> = {}) => ({ action, expected_self_prime_id: self, ...extra });
function reset() { calls = []; failSql = ""; authFailure = false; accepted = true; }
function actionValues() { return calls.find(call => call.query.includes("social_native_presence_action"))!.values; }

test("presence requires POST and verified Auth actor before database work", async () => {
  reset();
  assert.equal((await handler(new Request("http://localhost/functions/v1/native-presence"))).status, 405);
  for (const token of ["", "bad-token", "invalid-user"]) {
    reset();
    assert.equal((await handler(request({ action: "snapshot" }, token))).status, 401);
    assert.equal(calls.length, 0);
  }
  reset(); authFailure = true;
  const failure = await handler(request({ action: "snapshot" }));
  assert.equal(failure.status, 503);
  assert.equal((await failure.text()).includes("secret-token"), false);
  assert.equal(calls.length, 0);
});

test("snapshot uses verified actor and native self/settings/player/TTL envelope", async () => {
  reset();
  const response = await handler(request({ action: "snapshot", display_name: "Fixture", favorite_hunter: 3,
    actor: spoof, user_id: spoof }));
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { ok: true, snapshot });
  assert.ok(calls[0].query.includes("project_prime_hunter_license_for"));
  assert.deepEqual(calls[0].values, [actor, "Fixture", 3]);
  assert.deepEqual(calls[1].values, [actor]);
  assert.equal(JSON.stringify(calls).includes(spoof), false);
});

test("heartbeat and leave bind actor, expected self and session; client joinability is never SQL authority", async () => {
  for (const action of ["heartbeat", "leave"]) {
    reset();
    const response = await handler(request(mutation(action, { session_id: session, activity: "lobby", room_key: "SYNTHETIC ROOM",
      joinable: true, lobby_id: spoof, actor: spoof })));
    assert.equal(response.status, 200);
    assert.equal((await response.json() as any).ok, true);
    assert.deepEqual(actionValues(), [actor, action, self, session,
      action === "heartbeat" ? "lobby" : null, action === "heartbeat" ? "SYNTHETIC ROOM" : null, null, null]);
    assert.equal(JSON.stringify(calls).includes(spoof), false);
  }
});

test("privacy forwards exact four-field expected and next settings as separate CAS arguments", async () => {
  reset();
  const response = await handler(request(mutation("set_privacy", { expected_settings: settings, settings: changed })));
  assert.equal(response.status, 200);
  assert.deepEqual(actionValues(), [actor, "set_privacy", self, null, null, null, settings, changed]);
  reset(); accepted = false;
  const rejected = await handler(request(mutation("set_privacy", { expected_settings: settings, settings: changed })));
  assert.equal(rejected.status, 200);
  assert.equal((await rejected.json() as any).ok, false);
});

test("invalid session/account/activity/privacy bodies reject before any provisioning SQL", async () => {
  const heartbeat = mutation("heartbeat", { session_id: session, activity: "online" });
  const privacy = mutation("set_privacy", { expected_settings: settings, settings: changed });
  const malformed: unknown[] = [
    { action: "invalid" }, { action: "heartbeat", session_id: session, activity: "online" },
    { ...heartbeat, expected_self_prime_id: actor }, { ...heartbeat, expected_self_prime_id: 4 },
    { ...heartbeat, session_id: "invalid" }, { ...heartbeat, session_id: 4 },
    { ...heartbeat, activity: "hidden" }, { ...heartbeat, room_key: {} }, { ...heartbeat, room_key: "x".repeat(129) },
    mutation("leave"), mutation("leave", { session_id: "invalid" }),
    { ...privacy, expected_settings: undefined }, { ...privacy, settings: null }, { ...privacy, settings: [] },
    { ...privacy, settings: { ...changed, extra: "ignored" } },
    { ...privacy, settings: { ...changed, do_not_disturb: "true" } },
    { ...privacy, settings: { ...changed, presence_visibility: "private" } },
    { ...privacy, expected_settings: { ...settings, updated_at: "unexpected fifth field" } },
    { action: "snapshot", display_name: {} },
  ];
  for (const body of malformed) {
    reset();
    assert.equal((await handler(request(body))).status, 400, JSON.stringify(body));
    assert.equal(calls.length, 0);
  }
});

test("malformed JSON and oversized declared/actual bodies stay bounded before SQL", async () => {
  for (const body of ["null", "[]", "{", "5"]) {
    reset();
    assert.equal((await handler(new Request("http://localhost/functions/v1/native-presence", {
      method: "POST", headers: { authorization: "Bearer good-token" }, body,
    }))).status, 400);
    assert.equal(calls.length, 0);
  }
  for (const req of [request({ action: "snapshot", padding: "x".repeat(5000) }),
    new Request("http://localhost/functions/v1/native-presence", { method: "POST",
      headers: { authorization: "Bearer good-token", "content-length": "5000" }, body: "{}" })]) {
    reset(); assert.equal((await handler(req)).status, 413); assert.equal(calls.length, 0);
  }
});

test("SQL failures expose generic codes and no private account/database details", async () => {
  for (const functionName of ["project_prime_hunter_license_for", "social_native_presence_snapshot", "social_native_presence_action"]) {
    reset(); failSql = functionName;
    const response = await handler(request(functionName === "social_native_presence_action"
      ? mutation("heartbeat", { session_id: session, activity: "online" }) : { action: "snapshot" }));
    assert.equal(response.status, 503);
    const text = await response.text();
    assert.ok(JSON.parse(text).error);
    assert.equal(text.includes("private-db"), false);
    assert.equal(text.includes("private-secret"), false);
    assert.equal(text.includes(actor), false);
  }
  reset();
});
