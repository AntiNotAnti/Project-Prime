import { strict as assert } from "node:assert";
import { test } from "node:test";
import { registerHooks } from "node:module";
import { createHash, createHmac } from "node:crypto";
import { readFileSync } from "node:fs";

const serviceRole = "local-service-role-secret-for-tests";
const serverKey = "server-membership-key-1234567890abcdef";
const reporterId = "99999999-9999-4999-8999-999999999999";
const playerId = "11111111-1111-4111-8111-111111111111";
const clientId = 12345;
const epoch = "638000000000000000";
const expectedServerHash = createHash("sha256").update(serverKey).digest("hex");
let handler: (req: Request) => Promise<Response>;
let lastHeartbeat: unknown[] = [];
let lastRecent: unknown[] = [];
let lastLeave: unknown[] = [];

(globalThis as any).Deno = {
  env: {
    get: (name: string) => ({
      SUPABASE_DB_URL: "local-fixture",
      SUPABASE_SERVICE_ROLE_KEY: serviceRole,
    } as Record<string, string>)[name],
  },
  serve: (fn: typeof handler) => { handler = fn; },
};

const fixtureSql: any = async (strings: TemplateStringsArray, ...values: unknown[]) => {
  const query = strings.join("?");
  if (query.includes("project_prime_career_reporters")) {
    return values[0] === expectedServerHash
      ? [{ server_id: reporterId, enabled: true }]
      : [];
  }
  if (query.includes("insert into prime.social_lobby_memberships")) {
    lastHeartbeat = values;
    return [];
  }
  if (query.includes("social_recent_touch")) {
    lastRecent = values;
    return [{ social_recent_touch: null }];
  }
  if (query.includes("delete from prime.social_lobby_memberships")
    && query.includes("player_id =")) {
    lastLeave = values;
    return [];
  }
  if (query.includes("delete from prime.social_lobby_memberships")
    && query.includes("expires_at")) {
    return [];
  }
  throw new Error("Unexpected fixture SQL: " + query);
};
(globalThis as any).localLobbyMembershipSql = fixtureSql;

registerHooks({
  resolve(specifier, context, next) {
    if (specifier.startsWith("npm:postgres")) {
      return {
        url: "data:text/javascript,export default()=>globalThis.localLobbyMembershipSql",
        shortCircuit: true,
      };
    }
    return next(specifier, context);
  },
});

function ticket(subject = playerId, boundClient = clientId) {
  const now = Math.floor(Date.now() / 1000);
  const payload = Buffer.from(JSON.stringify({
    v: 1,
    sub: subject,
    cid: boundClient,
    iat: now - 5,
    exp: now + 3600,
  })).toString("base64url");
  const key = "project-prime-career-ticket:v1:" + serviceRole;
  const signature = createHmac("sha256", key).update(payload).digest("base64url");
  return `pp1.${payload}.${signature}`;
}

await import("../../supabase/functions/social-lobby-membership/index.ts");

const request = (value: unknown, key = serverKey) => new Request(
  "http://localhost/functions/v1/social-lobby-membership",
  {
    method: "POST",
    headers: { authorization: "Bearer " + key },
    body: JSON.stringify(value),
  },
);

test("lobby membership uses the explicit non-JWT reporter relay exception", async () => {
  const config = readFileSync(new URL("../../supabase/config.toml", import.meta.url), "utf8");
  assert.match(config,
    /\[functions\.social-lobby-membership\][\s\S]*?verify_jwt\s*=\s*false/);
  assert.equal((await handler(request({
    action: "heartbeat",
    authority_epoch: epoch,
    client_id: clientId,
    career_ticket: ticket(),
  }, "unknown-membership-key-1234567890abcdef"))).status, 401);
});

test("verified server heartbeat binds signed Hunter License to authority epoch", async () => {
  lastHeartbeat = [];
  const response = await handler(request({
    action: "heartbeat",
    authority_epoch: epoch,
    client_id: clientId,
    career_ticket: ticket(),
    lobby_eligible: true,
  }));
  assert.equal(response.status, 200);
  const data: any = await response.json();
  assert.equal(data.ok, true);
  assert.equal(data.status, "membership_verified");
  assert.equal(data.player_id, playerId);
  assert.deepEqual(lastHeartbeat, [
    playerId, epoch, reporterId, clientId, true,
  ]);
  assert.deepEqual(lastRecent, [playerId, epoch, reporterId]);
});

test("membership rejects forged and wrong-client Hunter License tickets", async () => {
  let response = await handler(request({
    action: "heartbeat",
    authority_epoch: epoch,
    client_id: clientId,
    career_ticket: ticket(playerId, clientId + 1),
    lobby_eligible: false,
  }));
  assert.equal(response.status, 401);

  response = await handler(request({
    action: "heartbeat",
    authority_epoch: epoch,
    client_id: clientId,
    career_ticket: ticket().slice(0, -2) + "xx",
    lobby_eligible: false,
  }));
  assert.equal(response.status, 401);
});

test("leave removes only the verified player, epoch and reporter tuple", async () => {
  lastLeave = [];
  const response = await handler(request({
    action: "leave",
    authority_epoch: epoch,
    client_id: clientId,
    career_ticket: ticket(),
    lobby_eligible: true,
  }));
  assert.equal(response.status, 200);
  const data: any = await response.json();
  assert.equal(data.status, "membership_removed");
  assert.deepEqual(lastLeave, [playerId, epoch, reporterId]);
});

test("invalid epochs and oversized request bodies fail before membership write", async () => {
  assert.equal((await handler(request({
    action: "heartbeat",
    authority_epoch: "18446744073709551616",
    client_id: clientId,
    career_ticket: ticket(),
  }))).status, 400);

  const response = await handler(new Request(
    "http://localhost/functions/v1/social-lobby-membership",
    {
      method: "POST",
      headers: {
        authorization: "Bearer " + serverKey,
        "content-length": "5000",
      },
      body: "{}",
    },
  ));
  assert.equal(response.status, 413);
});
