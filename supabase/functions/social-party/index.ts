/// <reference types="npm:@supabase/functions-js@2.117.2/src/edge-runtime.d.ts" />
import { readObjectBounded, RequestBodyError } from "../_shared/request-body.ts";
import { normalizePlayerName } from "../_shared/player-name.ts";
import postgres from "npm:postgres@3.4.7";

const sql = postgres(Deno.env.get("SUPABASE_DB_URL")!, {
  prepare: false,
  max: 1,
  idle_timeout: 20,
});

const actions = new Set([
  "snapshot", "invite", "accept", "decline", "cancel",
  "leave", "kick", "promote", "disband",
  "publish_travel", "clear_travel",
  "follow_travel", "decline_travel", "joined_travel",
]);
const primeIdPattern = /^PP-(?:[0-9A-F]{4}-){4}[0-9A-F]{4}$/;
const uuidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function json(status: number, value: unknown) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "content-type": "application/json" },
  });
}

Deno.serve(async (req: Request) => {
  if (req.method !== "POST") return json(405, { error: "method_not_allowed" });

  const authorization = req.headers.get("authorization") ?? "";
  if (!/^Bearer \S+$/.test(authorization))
    return json(401, { error: "authentication_required" });

  const auth = await fetch(`${Deno.env.get("SUPABASE_URL")}/auth/v1/user`, {
    headers: {
      authorization,
      apikey: Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
    },
  });
  if (!auth.ok) return json(401, { error: "invalid_session" });
  const user = await auth.json();
  if (typeof user.id !== "string" || !uuidPattern.test(user.id))
    return json(401, { error: "invalid_session" });

  let body: {
    action?: string;
    display_name?: string;
    favorite_hunter?: number;
    target_prime_id?: string;
    invite_id?: string;
    lobby_id?: string;
    travel_id?: string;
    revision?: number;
    reason?: string;
  };
  try {
    body = await readObjectBounded(req, 4096) as typeof body;
  } catch (error) {
    return json(error instanceof RequestBodyError ? error.status : 400, {
      error: "invalid_json",
    });
  }

  const action = typeof body.action === "string" ? body.action.trim() : "";
  if (!actions.has(action)) return json(400, { error: "invalid_action" });

  const displayName = normalizePlayerName(body.display_name ?? "Hunter");
  if (displayName === null) return json(400, { error: "invalid_display_name" });
  const favoriteHunter = Number.isInteger(body.favorite_hunter)
    ? Math.max(0, Math.min(6, body.favorite_hunter!))
    : 0;

  let targetPrimeId: string | null = null;
  if (action === "invite" || action === "kick" || action === "promote") {
    targetPrimeId = typeof body.target_prime_id === "string"
      ? body.target_prime_id.trim().toUpperCase()
      : "";
    if (!primeIdPattern.test(targetPrimeId))
      return json(400, { error: "invalid_prime_id" });
  }

  let inviteId: string | null = null;
  if (action === "accept" || action === "decline" || action === "cancel") {
    inviteId = typeof body.invite_id === "string" ? body.invite_id.trim() : "";
    if (!uuidPattern.test(inviteId))
      return json(400, { error: "invalid_invite_id" });
  }

  let lobbyId: string | null = null;
  let travelId: string | null = null;
  let revision = 0;
  let reason = "leader_lobby";
  if (action === "publish_travel") {
    lobbyId = typeof body.lobby_id === "string" ? body.lobby_id.trim() : "";
    reason = typeof body.reason === "string" ? body.reason.trim() : "leader_lobby";
    if (!uuidPattern.test(lobbyId)
      || !["leader_lobby", "quick_play", "regroup"].includes(reason))
      return json(400, { error: "invalid_travel" });
  }
  if (action === "follow_travel" || action === "decline_travel"
    || action === "joined_travel") {
    travelId = typeof body.travel_id === "string" ? body.travel_id.trim() : "";
    revision = Number.isInteger(body.revision) ? body.revision! : 0;
    if (!uuidPattern.test(travelId) || revision < 1 || revision > 1000000)
      return json(400, { error: "invalid_travel" });
  }

  async function combinedSnapshot() {
    const rows = await sql`
      select
        prime.social_party_snapshot(${user.id}::uuid) as party,
        prime.social_party_travel_snapshot(${user.id}::uuid) as travel
    `;
    const party = rows[0]?.party ?? {
      party: null,
      incoming_party_invites: [],
      outgoing_party_invites: [],
      recent_players: [],
    };
    return { ...party, travel: rows[0]?.travel ?? null };
  }

  try {
    await sql`
      select public.project_prime_hunter_license_for(
        ${user.id}::uuid,
        ${displayName || "Hunter"}::text,
        ${favoriteHunter}::integer
      )
    `;

    if (action === "snapshot") {
      return json(200, {
        ok: true,
        status: "snapshot",
        snapshot: await combinedSnapshot(),
      });
    }

    if (action === "publish_travel") {
      const rows = await sql`
        select prime.social_party_travel_publish(
          ${user.id}::uuid,
          ${lobbyId}::uuid,
          ${reason}::text
        ) as value
      `;
      const value = rows[0]?.value ?? {
        ok: false,
        status: "party_travel_empty_result",
      };
      return json(200, {
        ...value,
        snapshot: await combinedSnapshot(),
      });
    }

    if (action === "clear_travel") {
      const rows = await sql`
        select prime.social_party_travel_clear(${user.id}::uuid) as value
      `;
      const value = rows[0]?.value ?? {
        ok: false,
        status: "party_travel_empty_result",
      };
      return json(200, {
        ...value,
        snapshot: await combinedSnapshot(),
      });
    }

    if (action === "follow_travel" || action === "decline_travel"
      || action === "joined_travel") {
      const travelAction = action === "follow_travel" ? "follow"
        : action === "decline_travel" ? "decline" : "joined";
      const rows = await sql`
        select prime.social_party_travel_respond(
          ${user.id}::uuid,
          ${travelId}::uuid,
          ${revision}::integer,
          ${travelAction}::text
        ) as value
      `;
      const value = rows[0]?.value ?? {
        ok: false,
        status: "party_travel_empty_result",
      };
      return json(200, {
        ...value,
        snapshot: await combinedSnapshot(),
      });
    }

    const rows = await sql`
      select prime.social_party_action(
        ${user.id}::uuid,
        ${action}::text,
        ${targetPrimeId}::text,
        ${inviteId}::uuid
      ) as value
    `;
    const value = rows[0]?.value ?? {
      ok: false,
      status: "party_empty_result",
    };
    return json(200, {
      ...value,
      snapshot: await combinedSnapshot(),
    });
  } catch (error) {
    console.error("social party operation failed", error);
    return json(500, { error: "social_party_db_failed" });
  }
});
