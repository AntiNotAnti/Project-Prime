/// <reference types="npm:@supabase/functions-js@2.117.2/src/edge-runtime.d.ts" />
import { readObjectBounded, RequestBodyError } from "../_shared/request-body.ts";
import postgres from "npm:postgres@3.4.7";
import { validLoadout } from "./catalog.ts";

const sql = postgres(Deno.env.get("SUPABASE_DB_URL")!, { prepare: false, max: 1, idle_timeout: 20 });
const json = (status: number, value: unknown) => new Response(JSON.stringify(value), {
  status, headers: { "content-type": "application/json" },
});
Deno.serve(async (req: Request) => {
  if (req.method !== "POST") return json(405, { error: "method_not_allowed" });
  const authorization = req.headers.get("authorization") ?? "";
  if (!/^Bearer \S+$/.test(authorization)) return json(401, { error: "authentication_required" });
  // Validate with Auth, never trust decoded JWT fields or user_metadata.
  const auth = await fetch(`${Deno.env.get("SUPABASE_URL")}/auth/v1/user`, {
    headers: { authorization, apikey: Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")! },
  });
  if (!auth.ok) return json(401, { error: "invalid_session" });
  const user = await auth.json();
  if (typeof user.id !== "string") return json(401, { error: "invalid_session" });
  let body: unknown;
  try {
    body = await readObjectBounded(req, 2048);
  } catch (error) { return json(error instanceof RequestBodyError ? error.status : 400, { error: "invalid_json" }); }
  if (!validLoadout(body)) return json(400, { error: "invalid_cosmetic_loadout" });
  try {
    // Identity comes solely from verified Auth. SQL values are parameterized.
    // Ensure the same identity rows as hunter-license, without overwriting them.
    await sql`select public.project_prime_hunter_license_for(${user.id}::uuid, 'Hunter', ${body.hunter}::integer)`;
    const rows = await sql`
      insert into prime.player_cosmetic_loadouts
        (player_id, hunter, skin_key, armor_effect_key, death_effect_key, updated_at)
      values (${user.id}::uuid, ${body.hunter}, ${body.skin_key}, ${body.armor_effect_key}, ${body.death_effect_key}, now())
      on conflict (player_id, hunter) do update set skin_key = excluded.skin_key,
        armor_effect_key = excluded.armor_effect_key, death_effect_key = excluded.death_effect_key, updated_at = now()
      returning hunter, skin_key, armor_effect_key, death_effect_key
    `;
    return json(200, rows[0]);
  } catch (error) {
    console.error("hunter-cosmetics write failed", error);
    return json(500, { error: "cosmetic_save_failed" });
  }
});
