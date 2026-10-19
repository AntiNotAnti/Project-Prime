/// <reference types="npm:@supabase/functions-js@2.117.2/src/edge-runtime.d.ts" />
import { readObjectBounded, RequestBodyError } from "../_shared/request-body.ts";
import { normalizePlayerName } from "../_shared/player-name.ts";
import postgres from "npm:postgres@3.4.7";

const sql = postgres(Deno.env.get("SUPABASE_DB_URL")!, { prepare: false, max: 1, idle_timeout: 20 });
const actions = new Set(["snapshot", "invite", "accept_invite", "decline_invite", "cancel_invite", "leave", "kick", "promote", "disband", "publish_travel", "follow_travel", "join_leader", "decline_travel", "joined_travel", "clear_travel", "reserve_intent", "reservation_snapshot", "cancel_reservation"]);
const primeId = /^PP-(?:[0-9A-F]{4}-){4}[0-9A-F]{4}$/;
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const reservationUuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const json = (status: number, value: unknown) => new Response(JSON.stringify(value), {
  status, headers: { "content-type": "application/json" },
});

// Native contract over the restored Social/Party authority. Neither caller
// supplied UUIDs nor client role flags establish an authenticated actor.
Deno.serve(async (req: Request) => {
  if (req.method !== "POST") return json(405, { error: "method_not_allowed" });
  const authorization = req.headers.get("authorization") ?? "";
  if (!/^Bearer \S+$/.test(authorization)) return json(401, { error: "authentication_required" });
  let actor: string;
  try {
    const auth = await fetch(`${Deno.env.get("SUPABASE_URL")}/auth/v1/user`, {
      headers: { authorization, apikey: Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")! },
    });
    if (!auth.ok) return json(401, { error: "invalid_session" });
    const user = await auth.json();
    if (typeof user.id !== "string" || !uuid.test(user.id)) return json(401, { error: "invalid_session" });
    actor = user.id;
  } catch { return json(503, { error: "account_service_unavailable" }); }

  let body: Record<string, unknown>;
  try { body = await readObjectBounded(req, 4096); }
  catch (error) { return json(error instanceof RequestBodyError ? error.status : 400, { error: "invalid_json" }); }
  const action = typeof body.action === "string" ? body.action : "";
  if (!actions.has(action)) return json(400, { error: "invalid_action" });
  const name = normalizePlayerName(body.display_name ?? "Hunter");
  if (name === null) return json(400, { error: "invalid_display_name" });
  const hunter = Number.isInteger(body.favorite_hunter) ? Math.max(0, Math.min(6, body.favorite_hunter as number)) : 0;
  const self = typeof body.expected_self_prime_id === "string" ? body.expected_self_prime_id.trim().toUpperCase() : "";
  const revision = body.expected_revision;
  if (action !== "snapshot" && (!primeId.test(self) || !Number.isSafeInteger(revision) || (revision as number) < 0))
    return json(400, { error: "invalid_account_witness" });
  if (body.party_id !== undefined && typeof body.party_id !== "string")
    return json(400, { error: "invalid_party_id" });
  const party = typeof body.party_id === "string" ? body.party_id.trim() : "";
  if (party && !uuid.test(party)) return json(400, { error: "invalid_party_id" });
  const target = typeof body.target_prime_id === "string" ? body.target_prime_id.trim().toUpperCase() : "";
  if (["invite", "kick", "promote"].includes(action) && !primeId.test(target))
    return json(400, { error: "invalid_prime_id" });
  const invite = typeof body.invite_id === "string" ? body.invite_id.trim() : "";
  if (["accept_invite", "decline_invite", "cancel_invite"].includes(action) && !uuid.test(invite))
    return json(400, { error: "invalid_invite_id" });
  const travelActions = ["publish_travel", "follow_travel", "join_leader", "decline_travel", "joined_travel", "clear_travel"];
  const travel = typeof body.travel_id === "string" ? body.travel_id : "";
  const travelRevision = body.travel_revision;
  if (["follow_travel", "join_leader", "decline_travel", "joined_travel", "clear_travel"].includes(action)
    && (!uuid.test(travel) || !Number.isInteger(travelRevision) || (travelRevision as number) < 1 || (travelRevision as number) > 1000000))
    return json(400, { error: "invalid_travel_witness" });
  if (action === "publish_travel" && !((travel === "" && (travelRevision === 0 || travelRevision === undefined))
    || (uuid.test(travel) && Number.isInteger(travelRevision) && (travelRevision as number)>=1 && (travelRevision as number)<=1000000)))
    return json(400, { error: "invalid_travel_witness" });
  const host = typeof body.host === "string" ? body.host : "";
  const port = body.port;
  const epoch = typeof body.authority_epoch === "string" ? body.authority_epoch : "";
  const protocol = body.protocol;
  const room = typeof body.room_key === "string" ? body.room_key : "";
  const server = typeof body.server_name === "string" ? body.server_name : "";
  if (action === "publish_travel" && (!/^([0-9]{1,3}\.){3}[0-9]{1,3}$/.test(host)
    || host.split(".").some(x => Number(x) > 255) || !Number.isInteger(port) || (port as number)<1 || (port as number)>65535
    || !/^[1-9][0-9]{0,19}$/.test(epoch) || BigInt(epoch)>18446744073709551615n
    || !Number.isInteger(protocol) || (protocol as number)<1 || (protocol as number)>255
    || room.trim().length<1 || room.length>128 || server.length>96))
    return json(400, { error: "invalid_lobby" });
  const reservationAction = ["reserve_intent", "reservation_snapshot", "cancel_reservation"].includes(action);
  let reservationInput: Record<string, unknown> | undefined;
  if (reservationAction) {
    const request = typeof body.request_id === "string" ? body.request_id : "";
    if (!party || (body.request_id !== undefined && typeof body.request_id !== "string") || !uuid.test(travel) || !Number.isInteger(travelRevision) || (travelRevision as number)<1 || (travelRevision as number)>1000000
      || !Number.isInteger(body.match_id) || (body.match_id as number)<1 || (body.match_id as number)>65535
      || !/^[1-9][0-9]{0,19}$/.test(epoch) || BigInt(epoch)>18446744073709551615n
      || (action !== "reservation_snapshot" && !reservationUuid.test(request))
      || (request !== "" && !reservationUuid.test(request))
      || ((action !== "reservation_snapshot" || body.operation_generation !== undefined) && (!Number.isSafeInteger(body.operation_generation) || (body.operation_generation as number)<1)))
      return json(400, { error: "invalid_reservation_witness" });
    if (action === "reserve_intent" && (!/^([0-9]{1,3}\.){3}[0-9]{1,3}$/.test(host)
      || host.split(".").some(x => Number(x)>255) || !Number.isInteger(port) || (port as number)<1 || (port as number)>65535
      || !Number.isInteger(protocol) || (protocol as number)<1 || (protocol as number)>255 || room.trim().length<1 || room.length>128))
      return json(400, { error: "invalid_lobby" });
    // Whitelist witnesses; an authenticated body cannot supply another actor.
    reservationInput = { expected_self_prime_id: self, expected_revision: revision, party_id: party,
      travel_id: travel, travel_revision: travelRevision, match_id: body.match_id, authority_epoch: epoch,
      request_id: request, operation_generation: body.operation_generation ?? 1,
      ...(action === "reserve_intent" ? { host, port, protocol, room_key: room } : {}) };
  }
  try {
    await sql`select public.project_prime_hunter_license_for(${actor}::uuid, ${name || "Hunter"}::text, ${hunter}::integer)`;
    if (reservationAction) {
      const rows = await sql`select prime.social_native_party_reservation_action(
        ${actor}::uuid, ${action}::text, ${JSON.stringify(reservationInput)}::jsonb) as value`;
      return rows[0]?.value ? json(200, rows[0].value) : json(503, { error: "party_unavailable" });
    }
    if (action === "snapshot") {
      const rows = await sql`select prime.social_native_party_travel_state(${actor}::uuid) as value`;
      if (!rows[0]?.value) return json(503, { error: "party_unavailable" });
      return json(200, { ok: true, snapshot: rows[0].value });
    }
    if (travelActions.includes(action)) {
      const rows = await sql`select prime.social_native_party_travel_action(
        ${actor}::uuid, ${action}::text, ${revision}::bigint, ${party || null}::uuid, ${self}::text,
        ${travel || null}::uuid, ${travelRevision ?? null}::integer, ${action === "publish_travel" ? host : null}::inet,
        ${action === "publish_travel" ? port : null}::integer, ${action === "publish_travel" ? epoch : null}::numeric, ${action === "publish_travel" ? protocol : null}::integer,
        ${action === "publish_travel" ? room : null}::text, ${action === "publish_travel" ? server : null}::text) as value`;
      return rows[0]?.value ? json(200, rows[0].value) : json(503, { error: "party_unavailable" });
    }
    const rows = await sql`select prime.social_native_party_action(
      ${actor}::uuid, ${action}::text, ${revision}::bigint, ${party || null}::uuid,
      ${target || null}::text, ${invite || null}::uuid, ${self}::text) as value`;
    return rows[0]?.value ? json(200, rows[0].value) : json(503, { error: "party_unavailable" });
  } catch { return json(503, { error: "party_service_unavailable" }); }
});
