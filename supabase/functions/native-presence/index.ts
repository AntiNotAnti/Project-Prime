/// <reference types="npm:@supabase/functions-js@2.117.2/src/edge-runtime.d.ts" />
import { readObjectBounded, RequestBodyError } from "../_shared/request-body.ts";
import { normalizePlayerName } from "../_shared/player-name.ts";
import postgres from "npm:postgres@3.4.7";

const sql = postgres(Deno.env.get("SUPABASE_DB_URL")!, { prepare: false, max: 1, idle_timeout: 20 });
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const primeId = /^PP-(?:[0-9A-F]{4}-){4}[0-9A-F]{4}$/;
const activities = new Set(["online","menu","lobby","in_match","spectating"]);
const json = (status: number, value: unknown) => new Response(JSON.stringify(value), {
  status, headers: { "content-type": "application/json" },
});
function privacy(value: unknown): value is Record<string,unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const v = value as Record<string,unknown>;
  return Object.keys(v).length === 4
    && ["everyone","friends","hidden"].includes(v.presence_visibility as string)
    && ["everyone","friends","private"].includes(v.activity_visibility as string)
    && ["everyone","friends","nobody"].includes(v.invite_policy as string)
    && typeof v.do_not_disturb === "boolean";
}

Deno.serve(async (req: Request) => {
  if (req.method !== "POST") return json(405,{error:"method_not_allowed"});
  const authorization = req.headers.get("authorization") ?? "";
  if (!/^Bearer \S+$/.test(authorization)) return json(401,{error:"authentication_required"});
  let actor: string;
  try {
    const auth = await fetch(`${Deno.env.get("SUPABASE_URL")}/auth/v1/user`, {
      headers:{authorization,apikey:Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!},
    });
    if (!auth.ok) return json(401,{error:"invalid_session"});
    const user = await auth.json();
    if (typeof user.id !== "string" || !uuid.test(user.id)) return json(401,{error:"invalid_session"});
    actor=user.id;
  } catch { return json(503,{error:"account_service_unavailable"}); }
  let body: Record<string,unknown>;
  try { body=await readObjectBounded(req,4096); }
  catch(error) { return json(error instanceof RequestBodyError ? error.status : 400,{error:"invalid_json"}); }
  const action=body.action;
  if (!["snapshot","heartbeat","set_privacy","leave"].includes(action as string))
    return json(400,{error:"invalid_action"});
  const name=normalizePlayerName(body.display_name ?? "Hunter");
  if (name === null) return json(400,{error:"invalid_display_name"});
  const hunter=Number.isInteger(body.favorite_hunter) ? Math.max(0,Math.min(6,body.favorite_hunter as number)) : 0;
  const self=typeof body.expected_self_prime_id === "string" ? body.expected_self_prime_id.trim().toUpperCase() : "";
  if (action !== "snapshot" && !primeId.test(self)) return json(400,{error:"invalid_account_witness"});
  const session=typeof body.session_id === "string" ? body.session_id : "";
  if ((action === "heartbeat" || action === "leave") && !uuid.test(session))
    return json(400,{error:"invalid_session_id"});
  if (action === "heartbeat" && (!activities.has(body.activity as string)
    || (body.room_key !== undefined && body.room_key !== null
      && (typeof body.room_key !== "string" || body.room_key.length > 128))))
    return json(400,{error:"invalid_activity"});
  if (action === "set_privacy" && (!privacy(body.expected_settings) || !privacy(body.settings)))
    return json(400,{error:"invalid_privacy"});
  try {
    await sql`select public.project_prime_hunter_license_for(${actor}::uuid,${name || "Hunter"}::text,${hunter}::integer)`;
    if (action === "snapshot") {
      const rows=await sql`select prime.social_native_presence_snapshot(${actor}::uuid) as value`;
      return rows[0]?.value ? json(200,{ok:true,snapshot:rows[0].value}) : json(503,{error:"presence_unavailable"});
    }
    const rows=await sql`select prime.social_native_presence_action(
      ${actor}::uuid,${action}::text,${self}::text,${session || null}::uuid,
      ${action === "heartbeat" ? body.activity : null}::text,${action === "heartbeat" ? body.room_key ?? null : null}::text,
      ${action === "set_privacy" ? sql.json(body.expected_settings as object) : null}::jsonb,
      ${action === "set_privacy" ? sql.json(body.settings as object) : null}::jsonb) as value`;
    return rows[0]?.value ? json(200,rows[0].value) : json(503,{error:"presence_unavailable"});
  } catch { return json(503,{error:"presence_service_unavailable"}); }
});
