import { strict as assert } from "node:assert";
import { test } from "node:test";
import { registerHooks } from "node:module";
import { readFileSync } from "node:fs";

const actor = "11111111-1111-4111-8111-111111111111";
const targetPrimeId = "PP-2222-2222-4222-8222-2222";
let handler: (req: Request) => Promise<Response>;
let lastBridge: unknown[] = [];
let lastMutation: unknown[] = [];

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

const fixtureSql: any = async (strings: TemplateStringsArray, ...values: unknown[]) => {
  const query = strings.join("?");
  if (query.includes("project_prime_hunter_license_for")) {
    lastBridge = values;
    return [{ project_prime_hunter_license_for: { profile: {} } }];
  }
  if (query.includes("social_snapshot")) {
    return [{
      value: {
        self: { prime_id: "PP-1111-1111-4111-8111-1111", display_name: "Jarrett" },
        friends: [],
        incoming_requests: [],
        outgoing_requests: [],
        blocked: [],
      },
    }];
  }
  if (query.includes("social_lookup")) {
    return [{
      value: values[1] === targetPrimeId
        ? { prime_id: targetPrimeId, display_name: "Target" }
        : null,
    }];
  }
  if (query.includes("social_mutate")) {
    lastMutation = values;
    return [{
      value: {
        ok: true,
        status: values[2] === "block_player" ? "blocked" : "request_sent",
        snapshot: {
          self: { prime_id: "PP-1111-1111-4111-8111-1111", display_name: "Jarrett" },
          friends: [],
          incoming_requests: [],
          outgoing_requests: [],
          blocked: [],
        },
      },
    }];
  }
  throw new Error("Unexpected fixture SQL: " + query);
};
(globalThis as any).localSocialSql = fixtureSql;

registerHooks({
  resolve(specifier, context, next) {
    if (specifier.startsWith("npm:postgres")) {
      return {
        url: "data:text/javascript,export default()=>globalThis.localSocialSql",
        shortCircuit: true,
      };
    }
    return next(specifier, context);
  },
});

const originalFetch = globalThis.fetch;
globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input);
  if (url === "http://auth.local/auth/v1/user") {
    const auth = new Headers(init?.headers).get("authorization");
    if (auth === "Bearer good-token") {
      return new Response(JSON.stringify({ id: actor }), {
        status: 200,
        headers: { "content-type": "application/json" },
      });
    }
    return new Response(JSON.stringify({ error: "invalid" }), { status: 401 });
  }
  return originalFetch(input, init);
};

await import("../../supabase/functions/social/index.ts");

const request = (value: unknown, token = "good-token") => new Request(
  "http://localhost/functions/v1/social",
  {
    method: "POST",
    headers: token ? { authorization: "Bearer " + token } : {},
    body: JSON.stringify(value),
  },
);

test("social remains JWT-gated and rejects bad sessions before database work", async () => {
  const config = readFileSync(new URL("../../supabase/config.toml", import.meta.url), "utf8");
  assert.equal(config.includes("[functions.social]"), false);
  assert.equal((await handler(request({ action: "snapshot" }, ""))).status, 401);
  assert.equal((await handler(request({ action: "snapshot" }, "bad-token"))).status, 401);
});

test("snapshot provisions the Hunter License identity and returns the social read model", async () => {
  lastBridge = [];
  const response = await handler(request({
    action: "snapshot",
    display_name: "Jarrett",
    favorite_hunter: 3,
  }));
  assert.equal(response.status, 200);
  const data: any = await response.json();
  assert.equal(data.ok, true);
  assert.equal(data.snapshot.self.display_name, "Jarrett");
  assert.deepEqual(lastBridge, [actor, "Jarrett", 3]);
});

test("Prime ID lookup is exact, normalized, and bounded", async () => {
  const response = await handler(request({
    action: "lookup",
    display_name: "Jarrett",
    target_prime_id: targetPrimeId.toLowerCase(),
  }));
  assert.equal(response.status, 200);
  const data: any = await response.json();
  assert.equal(data.status, "found");
  assert.equal(data.player.prime_id, targetPrimeId);

  assert.equal((await handler(request({
    action: "lookup",
    target_prime_id: "Jarrett#1234",
  }))).status, 400);
});

test("friend and block mutations use the verified actor and canonical Prime ID", async () => {
  lastMutation = [];
  let response = await handler(request({
    action: "send_request",
    display_name: "Jarrett",
    target_prime_id: targetPrimeId.toLowerCase(),
  }));
  assert.equal(response.status, 200);
  let data: any = await response.json();
  assert.equal(data.status, "request_sent");
  assert.deepEqual(lastMutation, [actor, targetPrimeId, "send_request"]);

  response = await handler(request({
    action: "block_player",
    target_prime_id: targetPrimeId,
  }));
  data = await response.json();
  assert.equal(response.status, 200);
  assert.equal(data.status, "blocked");
  assert.deepEqual(lastMutation, [actor, targetPrimeId, "block_player"]);
});

test("invalid actions and oversized JSON fail before social SQL", async () => {
  assert.equal((await handler(request({ action: "launch_missiles" }))).status, 400);
  const response = await handler(new Request(
    "http://localhost/functions/v1/social",
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
