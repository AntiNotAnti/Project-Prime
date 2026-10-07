import { strict as assert } from "node:assert";
import { test } from "node:test";
import { registerHooks } from "node:module";
import { readFileSync } from "node:fs";

const actor = "11111111-1111-4111-8111-111111111111";
const target = "PP-2222-2222-4222-8222-2222";
const invite = "cccccccc-cccc-4ccc-8ccc-cccccccccccc";
const lobby = "dddddddd-dddd-4ddd-8ddd-dddddddddddd";
const travelId = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee";
let handler: (req: Request) => Promise<Response>;
let lastAction: unknown[] = [];
let lastTravel: unknown[] = [];

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

const travel = {
  travel_id: travelId,
  revision: 4,
  reason: "regroup",
  leader_prime_id: "PP-1111-1111-4111-8111-1111",
  leader_display_name: "Alpha",
  lobby_id: lobby,
  room_key: "MP1 SANCTORUS",
  server_name: "Prime Lobby",
  authority_epoch: "638000000000000000",
  expires_at: "2026-10-07T03:00:00Z",
  is_leader: false,
  self_status: "pending",
  members: [],
};

const fixtureSql: any = async (strings: TemplateStringsArray, ...values: unknown[]) => {
  const query = strings.join("?");
  if (query.includes("project_prime_hunter_license_for"))
    return [{ project_prime_hunter_license_for: { profile: {} } }];
  if (query.includes("social_party_snapshot")
    && query.includes("social_party_travel_snapshot"))
    return [{ party: snapshot, travel }];
  if (query.includes("social_party_travel_publish")) {
    lastTravel = values;
    return [{ value: { ok: true, status: "party_travel_published", travel } }];
  }
  if (query.includes("social_party_travel_clear")) {
    lastTravel = values;
    return [{ value: { ok: true, status: "party_travel_cleared" } }];
  }
  if (query.includes("social_party_travel_respond")) {
    lastTravel = values;
    return [{
      value: {
        ok: true,
        status: values[3] === "follow"
          ? "party_travel_following"
          : values[3] === "decline"
            ? "party_travel_declined" : "party_travel_joined",
        locator: values[3] === "follow" ? {
          lobby_id: lobby,
          host: "203.0.113.10",
          port: 27891,
          authority_epoch: "638000000000000000",
          protocol: 34,
          room_key: "MP1 SANCTORUS",
          server_name: "Prime Lobby",
          expires_at: "2026-10-07T03:00:00Z",
        } : null,
        travel,
      },
    }];
  }
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

test("party snapshot returns party, invites, recent players and travel", async () => {
  const response = await handler(request({ action: "snapshot" }));
  assert.equal(response.status, 200);
  const data: any = await response.json();
  assert.equal(data.ok, true);
  assert.equal(data.snapshot.party.is_leader, true);
  assert.deepEqual(data.snapshot.recent_players, []);
  assert.equal(data.snapshot.travel.travel_id, travelId);
  assert.equal(data.snapshot.travel.reason, "regroup");
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

test("leader travel publish is scoped to verified actor and lobby UUID", async () => {
  lastTravel = [];
  const response = await handler(request({
    action: "publish_travel",
    lobby_id: lobby,
    reason: "quick_play",
  }));
  assert.equal(response.status, 200);
  const data: any = await response.json();
  assert.equal(data.status, "party_travel_published");
  assert.deepEqual(lastTravel, [actor, lobby, "quick_play"]);
  assert.equal(data.snapshot.travel.travel_id, travelId);
});

test("travel follow/decline/joined are revision-bound and return locator only for follow", async () => {
  lastTravel = [];
  let response = await handler(request({
    action: "follow_travel",
    travel_id: travelId,
    revision: 4,
  }));
  assert.equal(response.status, 200);
  let data: any = await response.json();
  assert.equal(data.status, "party_travel_following");
  assert.equal(data.locator.host, "203.0.113.10");
  assert.deepEqual(lastTravel, [actor, travelId, 4, "follow"]);

  response = await handler(request({
    action: "decline_travel",
    travel_id: travelId,
    revision: 4,
  }));
  assert.equal(response.status, 200);
  data = await response.json();
  assert.equal(data.status, "party_travel_declined");
  assert.equal(data.locator, null);

  response = await handler(request({
    action: "joined_travel",
    travel_id: travelId,
    revision: 4,
  }));
  assert.equal(response.status, 200);
  data = await response.json();
  assert.equal(data.status, "party_travel_joined");
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
  assert.equal((await handler(request({
    action: "publish_travel",
    lobby_id: "not-a-uuid",
    reason: "quick_play",
  }))).status, 400);
  assert.equal((await handler(request({
    action: "follow_travel",
    travel_id: travelId,
    revision: 0,
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
