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
  "lookup",
  "send_request",
  "accept_request",
  "decline_request",
  "cancel_request",
  "remove_friend",
  "block_player",
  "unblock_player",
]);
const targetActions = new Set([
  "lookup",
  "send_request",
  "accept_request",
  "decline_request",
  "cancel_request",
  "remove_friend",
  "block_player",
  "unblock_player",
]);
const primeIdPattern = /^PP-(?:[0-9A-F]{4}-){4}[0-9A-F]{4}$/;

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

  // Validate the session with Supabase Auth. Never trust decoded JWT claims or
  // user_metadata for social identity or authorization.
  const auth = await fetch(`${Deno.env.get("SUPABASE_URL")}/auth/v1/user`, {
    headers: {
      authorization,
      apikey: Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
    },
  });
  if (!auth.ok) return json(401, { error: "invalid_session" });
  const user = await auth.json();
  if (typeof user.id !== "string") return json(401, { error: "invalid_session" });

  let body: {
    action?: string;
    target_prime_id?: string;
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

  let targetPrimeId = "";
  if (targetActions.has(action)) {
    targetPrimeId = typeof body.target_prime_id === "string"
      ? body.target_prime_id.trim().toUpperCase()
      : "";
    if (!primeIdPattern.test(targetPrimeId)) {
      return json(400, { error: "invalid_prime_id" });
    }
  }

  try {
    // Provision the same stable Hunter License identity used by career stats.
    // The bridge is create-only for profile fields, so recovery never overwrites
    // an existing display name with a fresh device's launcher defaults.
    await sql`
      select public.project_prime_hunter_license_for(
        ${user.id}::uuid,
        ${displayName || "Hunter"}::text,
        ${favoriteHunter}::integer
      )
    `;

    if (action === "snapshot") {
      const rows = await sql`
        select prime.social_snapshot(${user.id}::uuid) as value
      `;
      const snapshot = rows[0]?.value;
      if (!snapshot) return json(500, { error: "social_profile_missing" });
      return json(200, { ok: true, status: "snapshot", snapshot });
    }

    if (action === "lookup") {
      const rows = await sql`
        select prime.social_lookup(${user.id}::uuid, ${targetPrimeId}) as value
      `;
      const player = rows[0]?.value ?? null;
      return json(200, {
        ok: true,
        status: player ? "found" : "not_found",
        player,
      });
    }

    const rows = await sql`
      select prime.social_mutate(
        ${user.id}::uuid,
        ${targetPrimeId},
        ${action}
      ) as value
    `;
    const result = rows[0]?.value;
    if (!result || typeof result !== "object") {
      return json(500, { error: "social_empty_result" });
    }
    return json(200, result);
  } catch (error) {
    console.error("social database operation failed", error);
    return json(500, { error: "social_db_failed" });
  }
});
