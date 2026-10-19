import { strict as assert } from "node:assert";
import { test } from "node:test";
import { registerHooks } from "node:module";

const actor = "11111111-1111-4111-8111-111111111111";
const spoofedActor = "99999999-9999-4999-8999-999999999999";
const self = "PP-1111-1111-4111-8111-1111";
const target = "PP-2222-2222-4222-8222-2222";
const party = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
const invite = "cccccccc-cccc-4ccc-8ccc-cccccccccccc";
let handler: (request: Request) => Promise<Response>;
type SqlCall = { query: string; values: unknown[] };
let calls: SqlCall[] = [];
let authCalls = 0;
let failSql = "";
let mutationAccepted = true;
const secret = "private-sql-detail@example.test Bearer never-return-this-secret";
const snapshot = {
  self_prime_id: self,
  party_id: party,
  leader_prime_id: self,
  server_revision: 7,
  members: [{ prime_id: self, display_name: "SYNTHETIC PARTY OWNER", presence: "online" }],
  incoming: [],
  outgoing: [],
};

(globalThis as any).Deno = {
  env: { get: (key: string) => ({
    SUPABASE_DB_URL: "local-fixture",
    SUPABASE_URL: "http://auth.local",
    SUPABASE_SERVICE_ROLE_KEY: "local-service",
  } as Record<string, string>)[key] },
  serve: (callback: typeof handler) => { handler = callback; },
};
const fixtureSql: any = async (strings: TemplateStringsArray, ...values: unknown[]) => {
  const query = strings.join("?");
  calls.push({ query, values });
  if (failSql && query.includes(failSql)) throw new Error(secret);
  if (query.includes("project_prime_hunter_license_for"))
    return [{ project_prime_hunter_license_for: { profile: {} } }];
  if (query.includes("social_native_party_reservation_action")) return [{value:{ok:true,status:"pending",reservation:{request_id:invite,native_v1:true}}}];
  if (query.includes("social_native_party_travel_state")) return [{ value: snapshot }];
  if (query.includes("social_native_party_travel_action") || query.includes("social_native_party_action")) return [{ value: {
    ok: mutationAccepted,
    status: mutationAccepted ? "native_party_applied" : "revision_conflict",
    snapshot,
  } }];
  throw new Error("Unexpected SQL in isolated native Party handler fixture");
};
(globalThis as any).localNativePartySql = fixtureSql;
registerHooks({
  resolve(specifier, context, next) {
    if (specifier.startsWith("npm:postgres")) return {
      url: "data:text/javascript,export default()=>globalThis.localNativePartySql",
      shortCircuit: true,
    };
    return next(specifier, context);
  },
});
globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
  assert.equal(String(input), "http://auth.local/auth/v1/user", "fixture forbids any external fetch");
  authCalls++;
  const headers = new Headers(init?.headers);
  assert.equal(headers.get("apikey"), "local-service");
  const authorization = headers.get("authorization");
  if (authorization === "Bearer good-token")
    return new Response(JSON.stringify({ id: actor }), { status: 200 });
  if (authorization === "Bearer malformed-user")
    return new Response(JSON.stringify({ id: "not-an-auth-uuid" }), { status: 200 });
  return new Response(JSON.stringify({ error: "invalid_session" }), { status: 401 });
};

await import("../../supabase/functions/party/index.ts");

const request = (body: unknown, token = "good-token") => new Request("http://localhost/functions/v1/party", {
  method: "POST",
  headers: token ? { authorization: "Bearer " + token } : {},
  body: JSON.stringify(body),
});
const mutation = (action: string, extra: Record<string, unknown> = {}) => ({
  action, expected_self_prime_id: self, expected_revision: 7, party_id: party, ...extra,
});
const reset = () => { calls = []; authCalls = 0; failSql = ""; mutationAccepted = true; };
const actionCall = () => calls.find(call => call.query.includes("social_native_party_action"))!;

test("native Party rejects non-POST and missing/bad JWTs before all SQL", async () => {
  reset();
  assert.equal((await handler(new Request("http://localhost/functions/v1/party"))).status, 405);
  assert.equal(authCalls, 0);
  for (const token of ["", "bad-token", "malformed-user"]) {
    calls = [];
    assert.equal((await handler(request({ action: "snapshot" }, token))).status, 401);
    assert.equal(calls.length, 0);
  }
  calls = [];
  assert.equal((await handler(new Request("http://localhost/functions/v1/party", {
    method: "POST", headers: { authorization: "Basic fake" }, body: "{}",
  }))).status, 401);
  assert.equal(calls.length, 0);
});

test("snapshot provisions verified actor and returns the flat native snapshot envelope", async () => {
  reset();
  const response = await handler(request({ action: "snapshot", display_name: "Fixture", favorite_hunter: 3,
    actor: spoofedActor, user_id: spoofedActor, player_id: spoofedActor }));
  assert.equal(response.status, 200);
  const body: any = await response.json();
  assert.equal(body.ok, true);
  assert.deepEqual(body.snapshot, snapshot);
  assert.equal(body.snapshot.party, undefined, "native shape must not nest the donor party envelope");
  assert.equal(calls.length, 2);
  assert.ok(calls[0].query.includes("project_prime_hunter_license_for"));
  assert.deepEqual(calls[0].values, [actor, "Fixture", 3]);
  assert.ok(calls[1].query.includes("social_native_party_travel_state"));
  assert.deepEqual(calls[1].values, [actor]);
  assert.equal(JSON.stringify(body).includes(actor), false);
  assert.equal(JSON.stringify(calls).includes(spoofedActor), false);
});

test("every mutation carries verified actor, action, revision and private ownership witness in exact SQL order", async () => {
  for (const action of ["invite", "accept_invite", "decline_invite", "cancel_invite", "leave", "kick", "promote", "disband"]) {
    reset();
    const targetsPlayer = ["invite", "kick", "promote"].includes(action);
    const targetsInvite = ["accept_invite", "decline_invite", "cancel_invite"].includes(action);
    const response = await handler(request(mutation(action, {
      ...(targetsPlayer ? { target_prime_id: target } : {}),
      ...(targetsInvite ? { invite_id: invite } : {}),
      actor: spoofedActor,
    })));
    assert.equal(response.status, 200, action);
    const body: any = await response.json();
    assert.equal(body.ok, true);
    assert.deepEqual(body.snapshot, snapshot);
    assert.ok(calls[0].query.includes("project_prime_hunter_license_for"), action);
    assert.deepEqual(actionCall().values, [actor, action, 7, party,
      targetsPlayer ? target : null, targetsInvite ? invite : null, self], action);
    assert.equal(JSON.stringify(calls).includes(spoofedActor), false);
  }
});

test("optional empty party and safe revision endpoints preserve SQL witness without inventing a UUID", async () => {
  for (const revision of [0, Number.MAX_SAFE_INTEGER]) {
    for (const partyFields of [{}, { party_id: "" }]) {
      reset();
      const response = await handler(request({ action: "leave", expected_self_prime_id: self,
        expected_revision: revision, ...partyFields }));
      assert.equal(response.status, 200);
      assert.deepEqual(actionCall().values, [actor, "leave", revision, null, null, null, self]);
    }
  }
});

test("mutation validation rejects malformed identity, revision, party, target and invite before even provisioning SQL", async () => {
  const invalid: unknown[] = [
    { action: "unknown" },
    { action: "leave", expected_revision: 7 },
    mutation("leave", { expected_self_prime_id: "" }),
    mutation("leave", { expected_self_prime_id: actor }),
    mutation("leave", { expected_self_prime_id: 3 }),
    mutation("leave", { expected_revision: undefined }),
    ...[-1, 0.5, Number.MAX_SAFE_INTEGER + 1, "7", null].map(expected_revision => mutation("leave", { expected_revision })),
    mutation("leave", { party_id: "not-a-uuid" }),
    mutation("leave", { party_id: {} }),
    ...["invite", "kick", "promote"].flatMap(action => [mutation(action), mutation(action, { target_prime_id: "Player Name" }),
      mutation(action, { target_prime_id: 3 })]),
    ...["accept_invite", "decline_invite", "cancel_invite"].flatMap(action => [mutation(action), mutation(action, { invite_id: "invalid" }),
      mutation(action, { invite_id: 3 })]),
    { action: "snapshot", display_name: { nested: "invalid" } },
  ];
  for (const value of invalid) {
    reset();
    assert.equal((await handler(request(value))).status, 400, JSON.stringify(value));
    assert.equal(calls.length, 0, "invalid input must fail before Hunter License provisioning");
  }
});

test("malformed JSON and oversized declared or streamed bodies are bounded before SQL", async () => {
  for (const body of ["{", "null", "[]", "4", '"snapshot"']) {
    reset();
    const response = await handler(new Request("http://localhost/functions/v1/party", {
      method: "POST", headers: { authorization: "Bearer good-token" }, body,
    }));
    assert.equal(response.status, 400);
    assert.equal(calls.length, 0);
  }
  for (const oversized of [
    new Request("http://localhost/functions/v1/party", { method: "POST",
      headers: { authorization: "Bearer good-token", "content-length": "5000" }, body: "{}" }),
    request({ action: "snapshot", padding: "x".repeat(5000) }),
  ]) {
    reset();
    assert.equal((await handler(oversized)).status, 413);
    assert.equal(calls.length, 0);
  }
});

test("database refusal stays refused and database exceptions return only generic redacted errors", async () => {
  reset();
  mutationAccepted = false;
  let response = await handler(request(mutation("leave")));
  assert.equal(response.status, 200);
  assert.equal((await response.json() as any).ok, false);
  for (const functionName of ["project_prime_hunter_license_for", "social_native_party_travel_state", "social_native_party_action"]) {
    reset();
    failSql = functionName;
    response = await handler(request(functionName === "social_native_party_action" ? mutation("leave") : { action: "snapshot" }));
    assert.equal(response.status, 503);
    const text = await response.text();
    assert.equal(text.includes("private-sql-detail"), false);
    assert.equal(text.includes("never-return-this-secret"), false);
    assert.equal(text.includes(actor), false);
    assert.ok(JSON.parse(text).error, "database failure must have a generic error code");
  }
  failSql = "";
});


test("native travel endpoints preserve expected account, party and travel revisions without client actor authority", async () => {
  for (const action of ["follow_travel", "join_leader", "decline_travel", "joined_travel", "clear_travel"]) {
    reset();
    const response = await handler(request(mutation(action, { travel_id: invite, travel_revision: 9,
      host: "malicious-host-ignored", port: {}, actor: spoofedActor })));
    assert.equal(response.status, 200);
    const call = calls.find(call => call.query.includes("social_native_party_travel_action"))!;
    assert.deepEqual(call.values, [actor, action, 7, party, self, invite, 9, null, null, null, null, null, null]);
  }
  for (const action of ["follow_travel", "join_leader", "decline_travel", "joined_travel", "clear_travel"]) {
    for (const fields of [{}, { travel_id: invite, travel_revision: 0 }, { travel_id: "bad", travel_revision: 2 },
      { travel_id: invite, travel_revision: 1000001 }, { travel_id: invite, travel_revision: "2" }]) {
      reset();
      assert.equal((await handler(request(mutation(action, fields)))).status, 400);
      assert.equal(calls.length, 0);
    }
  }
});

test("publish validates exact integer locator and optional current travel CAS before SQL", async () => {
  const locator = { host: "203.0.113.4", port: 27015, protocol: 57,
    authority_epoch: "18446744073709551615", room_key: "MPH TEST", server_name: "Fixture" };
  for (const witness of [{ travel_id: "", travel_revision: 0 }, { travel_id: invite, travel_revision: 4 }]) {
    reset();
    assert.equal((await handler(request(mutation("publish_travel", { ...locator, ...witness })))).status, 200);
    const call = calls.find(call => call.query.includes("social_native_party_travel_action"))!;
    assert.deepEqual(call.values, [actor, "publish_travel", 7, party, self, witness.travel_id || null,
      witness.travel_revision, locator.host, locator.port, locator.authority_epoch, locator.protocol, locator.room_key, locator.server_name]);
  }
  for (const changed of [{ host: "999.0.0.1" }, { host: "server.invalid" }, { port: 0 }, { port: "42" },
    { protocol: 256 }, { authority_epoch: "18446744073709551616" }, { room_key: "" },
    { travel_id: invite, travel_revision: 0 }, { travel_id: "bad", travel_revision: 1 }]) {
    reset();
    assert.equal((await handler(request(mutation("publish_travel", { ...locator, ...changed })))).status, 400);
    assert.equal(calls.length, 0);
  }
});


test("native reservation intent preserves captured account, Party, travel and target without caller actor", async () => {
  reset();
  const input=mutation("reserve_intent",{request_id:invite,operation_generation:1,travel_id:invite,travel_revision:3,
    match_id:42,authority_epoch:"638000000000000000",host:"203.0.113.4",port:27015,protocol:57,room_key:"Fixture",actor:spoofedActor});
  assert.equal((await handler(request(input))).status,200);
  const sql=calls.find(c=>c.query.includes("social_native_party_reservation_action"))!;
  assert.equal(sql.values[0],actor);
  assert.equal(sql.values[1],"reserve_intent");
  const passed=JSON.parse(sql.values[2] as string);
  assert.equal(passed.expected_self_prime_id,self);
  assert.equal(passed.expected_revision,7);
  assert.equal(passed.match_id,42);
  assert.equal(passed.host,"203.0.113.4");
  assert.equal(passed.actor,undefined);
  assert.equal(JSON.stringify(sql).includes(spoofedActor),false);
});

test("native member reservation discovery allows absent request only on snapshot", async () => {
  const witness={travel_id:invite,travel_revision:3,match_id:42,authority_epoch:"638000000000000000"};
  reset();
  assert.equal((await handler(request(mutation("reservation_snapshot",witness)))).status,200);
  const passed=JSON.parse(calls.find(c=>c.query.includes("social_native_party_reservation_action"))!.values[2] as string);
  assert.equal(passed.request_id,"");
  for(const action of ["reserve_intent","cancel_reservation"]){
    reset(); assert.equal((await handler(request(mutation(action,witness)))).status,400); assert.equal(calls.length,0);
  }
});

test("native reservation invalid immutable witnesses fail before provisioning SQL", async () => {
  const valid={travel_id:invite,travel_revision:3,match_id:42,authority_epoch:"638000000000000000",request_id:invite,operation_generation:1};
  for(const extra of [{match_id:0},{match_id:65536},{match_id:"42"},{authority_epoch:"0"},{operation_generation:0},
    {operation_generation:1.1},{request_id:""},{party_id:""},{travel_revision:0}]){
    reset(); assert.equal((await handler(request(mutation("cancel_reservation",{...valid,...extra})))).status,400); assert.equal(calls.length,0);
  }
});
