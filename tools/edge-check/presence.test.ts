import { strict as assert } from "node:assert";
import { test } from "node:test";
import { registerHooks } from "node:module";
import { readFileSync } from "node:fs";

const actor = "11111111-1111-4111-8111-111111111111";
const session = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
const lobby = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
let handler: (req: Request) => Promise<Response>;
let lastHeartbeat: unknown[] = [];
let lastPrivacy: unknown[] = [];
let lastLeave: unknown[] = [];

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
  settings: {
    presence_visibility: "everyone",
    activity_visibility: "friends",
    invite_policy: "friends",
    do_not_disturb: false,
  },
  players: [],
  expires_after_seconds: 45,
};

const fixtureSql: any = async (strings: TemplateStringsArray, ...values: unknown[]) => {
  const query = strings.join("?");
  if (query.includes("project_prime_hunter_license_for")) {
    return [{ project_prime_hunter_license_for: { profile: {} } }];
  }
  if (query.includes("social_presence_snapshot")) return [{ value: snapshot }];
  if (query.includes("social_presence_heartbeat")) {
    lastHeartbeat = values;
    return [{ value: snapshot }];
  }
  if (query.includes("social_privacy_update")) {
    lastPrivacy = values;
    return [{
      value: {
        ...snapshot,
        settings: {
          presence_visibility: values[1],
          activity_visibility: values[2],
          invite_policy: values[3],
        },
      },
    }];
  }
  if (query.includes("social_presence_leave")) {
    lastLeave = values;
    return [{ value: true }];
  }
  throw new Error("Unexpected fixture SQL: " + query);
};
(globalThis as any).localPresenceSql = fixtureSql;

registerHooks({
  resolve(specifier, context, next) {
    if (specifier.startsWith("npm:postgres")) {
      return {
        url: "data:text/javascript,export default()=>globalThis.localPresenceSql",
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

await import("../../supabase/functions/presence/index.ts");

const request = (value: unknown, token = "good-token") => new Request(
  "http://localhost/functions/v1/presence",
  {
    method: "POST",
    headers: token ? { authorization: "Bearer " + token } : {},
    body: JSON.stringify(value),
  },
);

test("presence keeps relay JWT verification enabled and rejects invalid Auth sessions", async () => {
  const config = readFileSync(new URL("../../supabase/config.toml", import.meta.url), "utf8");
  assert.equal(config.includes("[functions.presence]"), false);
  assert.equal((await handler(request({ action: "snapshot" }, ""))).status, 401);
  assert.equal((await handler(request({ action: "snapshot" }, "bad-token"))).status, 401);
});

test("presence snapshot returns server privacy and online feed", async () => {
  const response = await handler(request({
    action: "snapshot",
    display_name: "Jarrett",
    favorite_hunter: 2,
  }));
  assert.equal(response.status, 200);
  const data: any = await response.json();
  assert.equal(data.ok, true);
  assert.equal(data.snapshot.settings.activity_visibility, "friends");
  assert.equal(data.snapshot.expires_after_seconds, 45);
});

test("heartbeat uses verified actor, process session, activity and room state", async () => {
  lastHeartbeat = [];
  const response = await handler(request({
    action: "heartbeat",
    session_id: session,
    activity: "lobby",
    room_key: "MP1 SANCTORUS",
    joinable: true,
    lobby_id: lobby,
  }));
  assert.equal(response.status, 200);
  const data: any = await response.json();
  assert.equal(data.status, "online");
  assert.deepEqual(lastHeartbeat, [
    actor, session, "lobby", "MP1 SANCTORUS", true, lobby,
  ]);

  assert.equal((await handler(request({
    action: "heartbeat",
    session_id: "not-a-uuid",
    activity: "menu",
  }))).status, 400);
  assert.equal((await handler(request({
    action: "heartbeat",
    session_id: session,
    activity: "warping",
  }))).status, 400);
  assert.equal((await handler(request({
    action: "heartbeat",
    session_id: session,
    activity: "lobby",
    lobby_id: "not-a-uuid",
  }))).status, 400);
});

test("privacy update is validated before SQL", async () => {
  lastPrivacy = [];
  let response = await handler(request({
    action: "set_privacy",
    presence_visibility: "friends",
    activity_visibility: "private",
    invite_policy: "nobody",
    do_not_disturb: true,
  }));
  assert.equal(response.status, 200);
  let data: any = await response.json();
  assert.equal(data.status, "privacy_updated");
  assert.deepEqual(lastPrivacy, [actor, "friends", "private", "nobody", true]);

  response = await handler(request({
    action: "set_privacy",
    presence_visibility: "everyone",
    activity_visibility: "friends",
    invite_policy: "please",
  }));
  assert.equal(response.status, 400);
});

test("leave only removes the verified actor's matching process session", async () => {
  lastLeave = [];
  const response = await handler(request({
    action: "leave",
    session_id: session,
  }));
  assert.equal(response.status, 200);
  const data: any = await response.json();
  assert.equal(data.status, "offline");
  assert.deepEqual(lastLeave, [actor, session]);
});

test("oversized payload is rejected before database work", async () => {
  const response = await handler(new Request(
    "http://localhost/functions/v1/presence",
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
