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
  "snapshot",
  "register_lobby",
  "send_invite",
  "accept_invite",
  "decline_invite",
  "cancel_invite",
  "resolve_invite",
  "join_friend",
]);
const primeIdPattern = /^PP-(?:[0-9A-F]{4}-){4}[0-9A-F]{4}$/;
const uuidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const epochPattern = /^[1-9][0-9]{0,19}$/;

function json(status: number, value: unknown) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "content-type": "application/json" },
  });
}

function validIpv4(value: string) {
  const parts = value.split(".");
  return parts.length === 4 && parts.every((part) => {
    if (!/^[0-9]{1,3}$/.test(part)) return false;
    const value = Number(part);
    return value >= 0 && value <= 255;
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
    display_name?: string;
    favorite_hunter?: number;
    target_prime_id?: string;
    invite_id?: string;
    lobby_id?: string;
    host?: string;
    port?: number;
    authority_epoch?: string;
    protocol?: number;
    room_key?: string;
    server_name?: string;
  };
  try {
    body = await readObjectBounded(req, 8192) as typeof body;
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

  let targetPrimeId = "";
  if (action === "send_invite" || action === "join_friend") {
    targetPrimeId = typeof body.target_prime_id === "string"
      ? body.target_prime_id.trim().toUpperCase()
      : "";
    if (!primeIdPattern.test(targetPrimeId)) {
      return json(400, { error: "invalid_prime_id" });
    }
  }

  let inviteId = "";
  if (action === "accept_invite" || action === "decline_invite"
    || action === "cancel_invite" || action === "resolve_invite") {
    inviteId = typeof body.invite_id === "string" ? body.invite_id.trim() : "";
    if (!uuidPattern.test(inviteId)) return json(400, { error: "invalid_invite_id" });
  }

  let lobbyId = "";
  if (action === "send_invite") {
    lobbyId = typeof body.lobby_id === "string" ? body.lobby_id.trim() : "";
    if (!uuidPattern.test(lobbyId)) return json(400, { error: "invalid_lobby_id" });
  }

  let host = "";
  let port = 0;
  let epoch = "";
  let protocol = 0;
  let roomKey = "";
  let serverName = "";
  if (action === "register_lobby") {
    host = typeof body.host === "string" ? body.host.trim() : "";
    port = Number.isInteger(body.port) ? body.port! : 0;
    epoch = typeof body.authority_epoch === "string"
      ? body.authority_epoch.trim()
      : "";
    protocol = Number.isInteger(body.protocol) ? body.protocol! : 0;
    roomKey = typeof body.room_key === "string" ? body.room_key.trim() : "";
    serverName = typeof body.server_name === "string" ? body.server_name.trim() : "";

    if (!validIpv4(host)
      || port < 1 || port > 65535
      || !epochPattern.test(epoch)
      || protocol < 1 || protocol > 255
      || roomKey.length < 1 || roomKey.length > 128
      || serverName.length > 96) {
      return json(400, { error: "invalid_lobby" });
    }
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
      const rows = await sql`
        select prime.social_invites_snapshot(${user.id}::uuid) as value
      `;
      return json(200, {
        ok: true,
        status: "snapshot",
        snapshot: rows[0]?.value ?? { incoming: [], outgoing: [] },
      });
    }

    if (action === "register_lobby") {
      const rows = await sql`
        select prime.social_lobby_register(
          ${user.id}::uuid,
          ${host}::inet,
          ${port}::integer,
          ${epoch}::numeric,
          ${protocol}::integer,
          ${roomKey}::text,
          ${serverName}::text
        ) as value
      `;
      return json(200, rows[0]?.value ?? { ok: false, status: "lobby_empty_result" });
    }

    if (action === "send_invite") {
      const rows = await sql`
        select prime.social_invite_send(
          ${user.id}::uuid,
          ${targetPrimeId}::text,
          ${lobbyId}::uuid
        ) as value
      `;
      return json(200, rows[0]?.value ?? { ok: false, status: "invite_empty_result" });
    }

    if (action === "join_friend") {
      const rows = await sql`
        select prime.social_join_friend(
          ${user.id}::uuid,
          ${targetPrimeId}::text
        ) as value
      `;
      return json(200, rows[0]?.value ?? { ok: false, status: "join_empty_result" });
    }

    if (action === "resolve_invite") {
      const rows = await sql`
        select prime.social_invite_resolve(
          ${user.id}::uuid,
          ${inviteId}::uuid
        ) as value
      `;
      return json(200, rows[0]?.value ?? { ok: false, status: "invite_empty_result" });
    }

    const inviteAction = action === "accept_invite" ? "accept"
      : action === "decline_invite" ? "decline"
      : "cancel";
    const rows = await sql`
      select prime.social_invite_action(
        ${user.id}::uuid,
        ${inviteId}::uuid,
        ${inviteAction}::text
      ) as value
    `;
    return json(200, rows[0]?.value ?? { ok: false, status: "invite_empty_result" });
  } catch (error) {
    console.error("social invite operation failed", error);
    return json(500, { error: "social_invites_db_failed" });
  }
});
