import { strict as assert } from "node:assert";
import { test } from "node:test";
import { registerHooks } from "node:module";
import { readFileSync } from "node:fs";

const actor = "11111111-1111-4111-8111-111111111111";
const target = "PP-2222-2222-4222-8222-2222";
const invite = "cccccccc-cccc-4ccc-8ccc-cccccccccccc";
let handler: (req: Request) => Promise<Response>;
let lastAction: unknown[] = [];

(globalThis as any).Deno = {
  env: {
    get: (name: string) => ({
      SUPABASE_DB_URL: "local-fixture",
      SUPABASE_URL: "http://auth.local",
      SUPABASE_SERVICE_ROLE_KEY: "local-service",
    } as Record<string, string>)[name],
  },
  serve: (fn: typeof handler) => { handler = fn; },
};

const snapshot = {
  party: {
    party_id: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
    leader_prime_id: "PP-1111-1111-4111-8111-1111",
    is_leader: true,
    members: [],
  },
  incoming_party_invites: [],
  outgoing_party_invites: [],
  recent_players: [],
};

const fixtureSql: any = async (strings: TemplateStringsArray, ...values: unknown[]) => {
  const query = strings.join("?");
  if (query.includes("project_prime_hunter_license_for"))
    return [{ project_prime_hunter_license_for: { profile: {} } }];
  if (query.includes("social_party_snapshot"))
    return [{ value: snapshot }];
  if (query.includes("social_party_action")) {
    lastAction = values;
    return [{ value: { ok: true, status: "party_invite_sent", snapshot } }];
  }
  throw new Error("Unexpected fixture SQL: " + query);
};
(globalThis as any).localSocialPartySql = fixtureSql;

registerHooks({
  resolve(specifier, context, next) {
    if (specifier.startsWith("npm:postgres")) {
      return {
        url: "data:text/javascript,export default()=>globalThis.localSocialPartySql",
        shortCircuit: true,
      };
    }
    return next(specifier, context);
  },
});

const originalFetch = globalThis.fetch;
globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
  if (String(input) === "http://auth.local/auth/v1/user") {
    const auth = new Headers(init?.headers).get("authorization");
    if (auth === "Bearer good-token")
      return new Response(JSON.stringify({ id: actor }), {
        status: 200,
        headers: { "content-type": "application/json" },
      });
    return new Response(JSON.stringify({ error: "invalid" }), { status: 401 });
  }
  return originalFetch(input, init);
};

await import("../../supabase/functions/social-party/index.ts");

const request = (value: unknown, token = "good-token") => new Request(
  "http://localhost/functions/v1/social-party",
  {
    method: "POST",
    headers: token ? { authorization: "Bearer " + token } : {},
    body: JSON.stringify(value),
  },
);

test("social party remains JWT/Auth gated", async () => {
  const config = readFileSync(new URL("../../supabase/config.toml", import.meta.url), "utf8");
  assert.equal(config.includes("[functions.social-party]"), false);
  assert.equal((await handler(request({ action: "snapshot" }, ""))).status, 401);
  assert.equal((await handler(request({ action: "snapshot" }, "bad-token"))).status, 401);
});

test("party snapshot returns party, invites and recent players", async () => {
  const response = await handler(request({ action: "snapshot" }));
  assert.equal(response.status, 200);
  const data: any = await response.json();
  assert.equal(data.ok, true);
  assert.equal(data.snapshot.party.is_leader, true);
  assert.deepEqual(data.snapshot.recent_players, []);
});

test("target party mutations use verified actor and canonical Prime ID", async () => {
  lastAction = [];
  const response = await handler(request({
    action: "invite",
    target_prime_id: target.toLowerCase(),
  }));
  assert.equal(response.status, 200);
  assert.deepEqual(lastAction, [actor, "invite", target, null]);
});

test("invite UUID actions remain distinct from target-player actions", async () => {
  lastAction = [];
  let response = await handler(request({
    action: "accept",
    invite_id: invite,
  }));
  assert.equal(response.status, 200);
  assert.deepEqual(lastAction, [actor, "accept", null, invite]);

  response = await handler(request({
    action: "leave",
  }));
  assert.equal(response.status, 200);
  assert.deepEqual(lastAction, [actor, "leave", null, null]);
});

test("malformed party IDs and oversized bodies fail before SQL", async () => {
  assert.equal((await handler(request({
    action: "kick",
    target_prime_id: "Player Two",
  }))).status, 400);
  assert.equal((await handler(request({
    action: "accept",
    invite_id: "not-a-uuid",
  }))).status, 400);

  const response = await handler(new Request(
    "http://localhost/functions/v1/social-party",
    {
      method: "POST",
      headers: {
        authorization: "Bearer good-token",
        "content-length": "5000",
      },
      body: "{}",
    },
  ));
  assert.equal(response.status, 413);
});
