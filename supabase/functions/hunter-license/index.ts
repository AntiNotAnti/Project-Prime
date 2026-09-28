import { normalizePlayerName } from "../_shared/player-name.ts";
import "jsr:@supabase/functions-js/edge-runtime.d.ts";
import { createClient } from "npm:@supabase/supabase-js@2";
import postgres from "npm:postgres@3.4.7";

const dbUrl = Deno.env.get("SUPABASE_DB_URL")!;
const sql = postgres(dbUrl, {
  prepare: false,
  max: 1,
  idle_timeout: 20,
});

function json(status: number, value: unknown) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "content-type": "application/json" },
  });
}

Deno.serve(async (req: Request) => {
  if (req.method !== "POST") return json(405, { error: "method_not_allowed" });

  const authorization = req.headers.get("authorization") ?? "";
  if (!authorization.startsWith("Bearer ")) {
    return json(401, { error: "authentication_required" });
  }
  const jwt = authorization.slice(7).trim();
  if (!jwt) return json(401, { error: "authentication_required" });

  let body: { display_name?: string; favorite_hunter?: number };
  try {
    body = await req.json();
  } catch {
    return json(400, { error: "invalid_json" });
  }

  const displayName = normalizePlayerName(body.display_name ?? "Hunter");
  if (displayName === null) return json(400, { error: "invalid_display_name" });
  const favoriteHunter = Number.isInteger(body.favorite_hunter)
    ? Math.max(0, Math.min(6, body.favorite_hunter!))
    : 0;

  const url = Deno.env.get("SUPABASE_URL")!;
  const service = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
  const admin = createClient(url, service, {
    auth: { persistSession: false, autoRefreshToken: false },
  });

  const { data: userData, error: userError } = await admin.auth.getUser(jwt);
  if (userError || !userData.user) {
    return json(401, { error: "invalid_session" });
  }

  try {
    const rows = await sql`
      select public.project_prime_hunter_license_for(
        ${userData.user.id}::uuid,
        ${displayName || "Hunter"}::text,
        ${favoriteHunter}::integer
      ) as value
    `;
    const value = rows[0]?.value;
    if (!value) return json(500, { error: "hunter_license_empty_result" });
    return json(200, value);
  } catch (error) {
    console.error("hunter-license database bridge failed", error);
    return json(500, { error: "hunter_license_db_failed" });
  }
});
