import { strict as assert } from "node:assert";
import { test } from "node:test";
import { registerHooks } from "node:module";
import { createHash, createHmac } from "node:crypto";
import { readFileSync } from "node:fs";

const serviceRole = "local-service-role-secret-for-tests";
const serverKey = "server-reservation-key-1234567890abcdef";
const reporterId = "99999999-9999-4999-8999-999999999999";
const playerId = "22222222-2222-4222-8222-222222222222";
const clientId = 22345;
const epoch = "638000000000000000";
const requestId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
const reservationId = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
const partyId = "cccccccc-cccc-4ccc-8ccc-cccccccccccc";
const leaderId = "11111111-1111-4111-8111-111111111111";
const expectedServerHash = createHash("sha256").update(serverKey).digest("hex");

let handler: (req: Request) => Promise<Response>;
let lastValidate: unknown[] = [];
let lastActivate: unknown[] = [];
let lastAdmitted: unknown[] = [];
let lastCancel: unknown[] = [];

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
  if (query.includes("social_party_reservation_server_validate")) {
    lastValidate = values;
    return [{
      value: {
        ok: true,
        status: "pending",
        request_id: requestId,
        party_id: partyId,
        leader_id: leaderId,
        authority_epoch: epoch,
        requested_count: 2,
        server_reservation_id: null,
        members: [leaderId, playerId],
      },
    }];
  }
  if (query.includes("social_party_reservation_server_activate")) {
    lastActivate = values;
    return [{ value: { ok: true, status: "reservation_reserved" } }];
  }
  if (query.includes("social_party_reservation_server_admitted")) {
    lastAdmitted = values;
    return [{ value: { ok: true, status: "member_admitted" } }];
  }
  if (query.includes("social_party_reservation_server_cancel")) {
    lastCancel = values;
    return [{ value: { ok: true, status: "reservation_rejected" } }];
  }
  throw new Error("Unexpected fixture SQL: " + query);
};
(globalThis as any).localPartyReservationSql = fixtureSql;

registerHooks({
  resolve(specifier, context, next) {
    if (specifier.startsWith("npm:postgres")) {
      return {
        url: "data:text/javascript,export default()=>globalThis.localPartyReservationSql",
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

await import("../../supabase/functions/social-party-reservation-server/index.ts");

const request = (value: unknown, key = serverKey) => new Request(
  "http://localhost/functions/v1/social-party-reservation-server",
  {
    method: "POST",
    headers: { authorization: "Bearer " + key },
    body: JSON.stringify(value),
  },
);

test("party reservation relay uses explicit server-auth exception", async () => {
  const config = readFileSync(new URL("../../supabase/config.toml", import.meta.url), "utf8");
  assert.match(config,
    /\[functions\.social-party-reservation-server\][\s\S]*?verify_jwt\s*=\s*false/);

  const response = await handler(request({
    action: "validate",
    request_id: requestId,
    authority_epoch: epoch,
    client_id: clientId,
    career_ticket: ticket(),
  }, "unknown-reservation-key-1234567890abcdef"));
  assert.equal(response.status, 401);
});

test("validate binds request to signed Hunter License and authority epoch", async () => {
  lastValidate = [];
  const response = await handler(request({
    action: "validate",
    request_id: requestId,
    authority_epoch: epoch,
    client_id: clientId,
    career_ticket: ticket(),
  }));
  assert.equal(response.status, 200);
  const data: any = await response.json();
  assert.equal(data.ok, true);
  assert.equal(data.player_id, playerId);
  assert.equal(data.requested_count, 2);
  assert.deepEqual(lastValidate, [reporterId, epoch, requestId, playerId]);
});

test("validate rejects forged or wrong-client career tickets", async () => {
  let response = await handler(request({
    action: "validate",
    request_id: requestId,
    authority_epoch: epoch,
    client_id: clientId,
    career_ticket: ticket(playerId, clientId + 1),
  }));
  assert.equal(response.status, 401);

  response = await handler(request({
    action: "validate",
    request_id: requestId,
    authority_epoch: epoch,
    client_id: clientId,
    career_ticket: ticket().slice(0, -2) + "xx",
  }));
  assert.equal(response.status, 401);
});

test("activate reports exact member-slot assignment and expiry", async () => {
  lastActivate = [];
  const assignments = [
    { player_id: leaderId, slot: 2 },
    { player_id: playerId, slot: 4 },
  ];
  const response = await handler(request({
    action: "activate",
    request_id: requestId,
    reservation_id: reservationId,
    authority_epoch: epoch,
    assignments,
    expires_in_seconds: 30,
  }));
  assert.equal(response.status, 200);
  assert.deepEqual(lastActivate, [
    reporterId, epoch, requestId, reservationId,
    JSON.stringify(assignments), 30,
  ]);
});

test("admitted and pre-allocation cancel remain server authenticated", async () => {
  lastAdmitted = [];
  let response = await handler(request({
    action: "admitted",
    request_id: requestId,
    reservation_id: reservationId,
    player_id: playerId,
  }));
  assert.equal(response.status, 200);
  assert.deepEqual(lastAdmitted, [reporterId, requestId, reservationId, playerId]);

  lastCancel = [];
  response = await handler(request({
    action: "cancel",
    request_id: requestId,
    reservation_id: "00000000-0000-0000-0000-000000000000",
    status: "rejected",
  }));
  assert.equal(response.status, 200);
  assert.deepEqual(lastCancel, [
    reporterId,
    requestId,
    "00000000-0000-0000-0000-000000000000",
    "rejected",
  ]);
});

test("malformed reservation inputs fail before SQL", async () => {
  assert.equal((await handler(request({
    action: "validate",
    request_id: "not-a-uuid",
    authority_epoch: epoch,
    client_id: clientId,
    career_ticket: ticket(),
  }))).status, 400);

  assert.equal((await handler(request({
    action: "activate",
    request_id: requestId,
    reservation_id: reservationId,
    authority_epoch: "18446744073709551616",
    assignments: [{ player_id: playerId, slot: 1 }],
    expires_in_seconds: 30,
  }))).status, 400);

  assert.equal((await handler(request({
    action: "activate",
    request_id: requestId,
    reservation_id: reservationId,
    authority_epoch: epoch,
    assignments: [{ player_id: playerId, slot: 8 }],
    expires_in_seconds: 30,
  }))).status, 400);
});
