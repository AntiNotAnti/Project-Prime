import { strict as assert } from "node:assert";
import { test } from "node:test";
import { registerHooks } from "node:module";
import { readFileSync } from "node:fs";

const actor = "11111111-1111-4111-8111-111111111111";
const targetPrimeId = "PP-2222-2222-4222-8222-2222";
const lobbyId = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
const inviteId = "cccccccc-cccc-4ccc-8ccc-cccccccccccc";
let handler: (req: Request) => Promise<Response>;
let lastRegister: unknown[] = [];
let lastSend: unknown[] = [];
let lastAction: unknown[] = [];
let lastResolve: unknown[] = [];
let lastJoin: unknown[] = [];

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

const locator = {
  lobby_id: lobbyId,
  host: "203.0.113.10",
  port: 27891,
  authority_epoch: "638000000000000000",
  protocol: 34,
  room_key: "MP1 SANCTORUS",
  server_name: "Prime Lobby",
  expires_at: "2026-10-06T23:59:00Z",
};
const snapshot = {
  incoming: [],
  outgoing: [],
};

const fixtureSql: any = async (strings: TemplateStringsArray, ...values: unknown[]) => {
  const query = strings.join("?");
  if (query.includes("project_prime_hunter_license_for"))
    return [{ project_prime_hunter_license_for: { profile: {} } }];
  if (query.includes("social_invites_snapshot"))
    return [{ value: snapshot }];
  if (query.includes("social_lobby_register")) {
    lastRegister = values;
    return [{ value: { ok: true, status: "lobby_registered", lobby: locator } }];
  }
  if (query.includes("social_invite_send")) {
    lastSend = values;
    return [{
      value: {
        ok: true,
        status: "invite_sent",
        invite_id: inviteId,
        snapshot,
      },
    }];
  }
  if (query.includes("social_invite_action")) {
    lastAction = values;
    return [{
      value: {
        ok: true,
        status: values[2] === "accept" ? "invite_accepted" : "invite_declined",
        locator: values[2] === "accept" ? locator : undefined,
        snapshot,
      },
    }];
  }
  if (query.includes("social_invite_resolve")) {
    lastResolve = values;
    return [{ value: { ok: true, status: "invite_resolved", locator } }];
  }
  if (query.includes("social_join_friend")) {
    lastJoin = values;
    return [{ value: { ok: true, status: "friend_resolved", locator } }];
  }
  throw new Error("Unexpected fixture SQL: " + query);
};
(globalThis as any).localSocialInvitesSql = fixtureSql;

registerHooks({
  resolve(specifier, context, next) {
    if (specifier.startsWith("npm:postgres")) {
      return {
        url: "data:text/javascript,export default()=>globalThis.localSocialInvitesSql",
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

await import("../../supabase/functions/social-invites/index.ts");

const request = (value: unknown, token = "good-token") => new Request(
  "http://localhost/functions/v1/social-invites",
  {
    method: "POST",
    headers: token ? { authorization: "Bearer " + token } : {},
    body: JSON.stringify(value),
  },
);

test("social invites remain relay JWT gated and validate Auth before SQL", async () => {
  const config = readFileSync(new URL("../../supabase/config.toml", import.meta.url), "utf8");
  assert.equal(config.includes("[functions.social-invites]"), false);
  assert.equal((await handler(request({ action: "snapshot" }, ""))).status, 401);
  assert.equal((await handler(request({ action: "snapshot" }, "bad-token"))).status, 401);
});

test("lobby registration preserves epoch as an exact decimal string", async () => {
  lastRegister = [];
  const response = await handler(request({
    action: "register_lobby",
    host: "203.0.113.10",
    port: 27891,
    authority_epoch: "638000000000000000",
    protocol: 34,
    room_key: "MP1 SANCTORUS",
    server_name: "Prime Lobby",
  }));
  assert.equal(response.status, 200);
  const data: any = await response.json();
  assert.equal(data.status, "lobby_registered");
  assert.equal(data.lobby.authority_epoch, "638000000000000000");
  assert.deepEqual(lastRegister, [
    actor, "203.0.113.10", 27891, "638000000000000000",
    34, "MP1 SANCTORUS", "Prime Lobby",
  ]);

  assert.equal((await handler(request({
    action: "register_lobby",
    host: "not-an-ip",
    port: 27891,
    authority_epoch: "638000000000000000",
    protocol: 34,
    room_key: "MP1 SANCTORUS",
  }))).status, 400);
  assert.equal((await handler(request({
    action: "register_lobby",
    host: "203.0.113.10",
    port: 27891,
    authority_epoch: "18446744073709551616",
    protocol: 34,
    room_key: "MP1 SANCTORUS",
  }))).status, 400);
});

test("sending an invite is scoped to verified actor, Prime ID and lobby UUID", async () => {
  lastSend = [];
  const response = await handler(request({
    action: "send_invite",
    target_prime_id: targetPrimeId.toLowerCase(),
    lobby_id: lobbyId,
  }));
  assert.equal(response.status, 200);
  const data: any = await response.json();
  assert.equal(data.status, "invite_sent");
  assert.equal(data.invite_id, inviteId);
  assert.deepEqual(lastSend, [actor, targetPrimeId, lobbyId]);
});

test("accept, decline, cancel and resolve are keyed by invite UUID", async () => {
  lastAction = [];
  let response = await handler(request({
    action: "accept_invite",
    invite_id: inviteId,
  }));
  assert.equal(response.status, 200);
  let data: any = await response.json();
  assert.equal(data.status, "invite_accepted");
  assert.equal(data.locator.host, "203.0.113.10");
  assert.deepEqual(lastAction, [actor, inviteId, "accept"]);

  response = await handler(request({
    action: "decline_invite",
    invite_id: inviteId,
  }));
  data = await response.json();
  assert.equal(data.status, "invite_declined");
  assert.deepEqual(lastAction, [actor, inviteId, "decline"]);

  response = await handler(request({
    action: "cancel_invite",
    invite_id: inviteId,
  }));
  assert.equal(response.status, 200);
  assert.deepEqual(lastAction, [actor, inviteId, "cancel"]);

  response = await handler(request({
    action: "resolve_invite",
    invite_id: inviteId,
  }));
  data = await response.json();
  assert.equal(data.status, "invite_resolved");
  assert.deepEqual(lastResolve, [actor, inviteId]);
});

test("Join Friend resolves by stable Prime ID without accepting client endpoint input", async () => {
  lastJoin = [];
  const response = await handler(request({
    action: "join_friend",
    target_prime_id: targetPrimeId.toLowerCase(),
  }));
  assert.equal(response.status, 200);
  const data: any = await response.json();
  assert.equal(data.status, "friend_resolved");
  assert.equal(data.locator.port, 27891);
  assert.deepEqual(lastJoin, [actor, targetPrimeId]);

  assert.equal((await handler(request({
    action: "join_friend",
    target_prime_id: "Target Player",
    host: "198.51.100.7",
    port: 1234,
  }))).status, 400);
});

test("malformed UUIDs and oversized bodies are rejected before invite SQL", async () => {
  assert.equal((await handler(request({
    action: "accept_invite",
    invite_id: "not-a-uuid",
  }))).status, 400);

  const response = await handler(new Request(
    "http://localhost/functions/v1/social-invites",
    {
      method: "POST",
      headers: {
        authorization: "Bearer good-token",
        "content-length": "9000",
      },
      body: "{}",
    },
  ));
  assert.equal(response.status, 413);
});
