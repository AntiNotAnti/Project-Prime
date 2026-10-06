/// <reference types="npm:@supabase/functions-js@2.117.2/src/edge-runtime.d.ts" />
import { readObjectBounded, RequestBodyError } from "../_shared/request-body.ts";
import { normalizePlayerName } from "../_shared/player-name.ts";
import postgres from "npm:postgres@3.4.7";

const sql = postgres(Deno.env.get("SUPABASE_DB_URL")!, {
  prepare: false,
  max: 1,
  idle_timeout: 20,
});

const actions = new Set(["snapshot", "heartbeat", "set_privacy", "leave"]);
const activities = new Set(["online", "menu", "lobby", "in_match", "spectating"]);
const presenceVisibility = new Set(["everyone", "friends", "hidden"]);
const activityVisibility = new Set(["everyone", "friends", "private"]);
const invitePolicy = new Set(["everyone", "friends", "nobody"]);
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
  if (!/^Bearer \S+$/.test(authorization)) {
    return json(401, { error: "authentication_required" });
  }

  const auth = await fetch(`${Deno.env.get("SUPABASE_URL")}/auth/v1/user`, {
    headers: {
      authorization,
      apikey: Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
    },
  });
  if (!auth.ok) return json(401, { error: "invalid_session" });
  const user = await auth.json();
  if (typeof user.id !== "string" || !uuidPattern.test(user.id)) {
    return json(401, { error: "invalid_session" });
  }

  let body: {
    action?: string;
    session_id?: string;
    activity?: string;
    room_key?: string | null;
    joinable?: boolean;
    lobby_id?: string | null;
    presence_visibility?: string;
    activity_visibility?: string;
    invite_policy?: string;
    display_name?: string;
    favorite_hunter?: number;
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

  let sessionId = "";
  if (action === "heartbeat" || action === "leave") {
    sessionId = typeof body.session_id === "string" ? body.session_id.trim() : "";
    if (!uuidPattern.test(sessionId)) return json(400, { error: "invalid_session_id" });
  }

  let activity = "";
  let roomKey: string | null = null;
  let joinable = false;
  let lobbyId: string | null = null;
  if (action === "heartbeat") {
    activity = typeof body.activity === "string" ? body.activity.trim() : "";
    if (!activities.has(activity)) return json(400, { error: "invalid_activity" });
    if (body.room_key !== undefined && body.room_key !== null) {
      if (typeof body.room_key !== "string" || body.room_key.length > 128) {
        return json(400, { error: "invalid_room_key" });
      }
      roomKey = body.room_key.trim() || null;
    }
    joinable = body.joinable === true;
    if (body.lobby_id !== undefined && body.lobby_id !== null) {
      if (typeof body.lobby_id !== "string" || !uuidPattern.test(body.lobby_id.trim())) {
        return json(400, { error: "invalid_lobby_id" });
      }
      lobbyId = body.lobby_id.trim();
    }
  }

  let presence = "";
  let detail = "";
  let invites = "";
  if (action === "set_privacy") {
    presence = typeof body.presence_visibility === "string"
      ? body.presence_visibility.trim()
      : "";
    detail = typeof body.activity_visibility === "string"
      ? body.activity_visibility.trim()
      : "";
    invites = typeof body.invite_policy === "string"
      ? body.invite_policy.trim()
      : "";
    if (!presenceVisibility.has(presence)
      || !activityVisibility.has(detail)
      || !invitePolicy.has(invites)) {
      return json(400, { error: "invalid_privacy" });
    }
  }

  try {
    // Presence uses the same stable Hunter License UUID as career/social state.
    // This bridge creates missing account rows but never overwrites a recovered
    // profile with launcher defaults from a fresh device.
    await sql`
      select public.project_prime_hunter_license_for(
        ${user.id}::uuid,
        ${displayName || "Hunter"}::text,
        ${favoriteHunter}::integer
      )
    `;

    if (action === "snapshot") {
      const rows = await sql`
        select prime.social_presence_snapshot(${user.id}::uuid) as value
      `;
      const snapshot = rows[0]?.value;
      if (!snapshot) return json(500, { error: "presence_profile_missing" });
      return json(200, { ok: true, status: "snapshot", snapshot });
    }

    if (action === "heartbeat") {
      const rows = await sql`
        select prime.social_presence_heartbeat(
          ${user.id}::uuid,
          ${sessionId}::uuid,
          ${activity},
          ${roomKey},
          ${joinable},
          ${lobbyId}::uuid
        ) as value
      `;
      const snapshot = rows[0]?.value;
      if (!snapshot) return json(500, { error: "presence_empty_result" });
      return json(200, { ok: true, status: "online", snapshot });
    }

    if (action === "set_privacy") {
      const rows = await sql`
        select prime.social_privacy_update(
          ${user.id}::uuid,
          ${presence},
          ${detail},
          ${invites}
        ) as value
      `;
      const snapshot = rows[0]?.value;
      if (!snapshot) return json(500, { error: "privacy_empty_result" });
      return json(200, { ok: true, status: "privacy_updated", snapshot });
    }

    const rows = await sql`
      select prime.social_presence_leave(
        ${user.id}::uuid,
        ${sessionId}::uuid
      ) as value
    `;
    return json(200, {
      ok: true,
      status: rows[0]?.value ? "offline" : "already_offline",
    });
  } catch (error) {
    console.error("presence database operation failed", error);
    return json(500, { error: "presence_db_failed" });
  }
});
