/// <reference types="npm:@supabase/functions-js@2.117.2/src/edge-runtime.d.ts" />
import { readObjectBounded, RequestBodyError } from "../_shared/request-body.ts";
import postgres from "npm:postgres@3.4.7";

const enc = new TextEncoder();
const dec = new TextDecoder();
const sql = postgres(Deno.env.get("SUPABASE_DB_URL")!, {
  prepare: false,
  max: 1,
  idle_timeout: 20,
});

const uuidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const epochPattern = /^[1-9][0-9]{0,19}$/;
const maxUint64 = 18446744073709551615n;

function json(status: number, value: unknown) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "content-type": "application/json" },
  });
}

function fromB64url(value: string) {
  const padded = value.replaceAll("-", "+").replaceAll("_", "/")
    + "=".repeat((4 - value.length % 4) % 4);
  const binary = atob(padded);
  const result = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) result[i] = binary.charCodeAt(i);
  return result;
}

function hex(bytes: Uint8Array) {
  return [...bytes].map((b) => b.toString(16).padStart(2, "0")).join("");
}

async function sha256(text: string) {
  return hex(new Uint8Array(await crypto.subtle.digest("SHA-256", enc.encode(text))));
}

async function ticketKey() {
  const service = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
  if (!service) throw new Error("Supabase service role is unavailable");
  return await crypto.subtle.importKey(
    "raw",
    enc.encode("project-prime-career-ticket:v1:" + service),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["verify"],
  );
}

type Ticket = { v: number; sub: string; cid: number; iat: number; exp: number };

async function verifyTicket(token: unknown, clientId: number): Promise<string | null> {
  if (typeof token !== "string" || token.length > 768) return null;
  const parts = token.split(".");
  if (parts.length !== 3 || parts[0] !== "pp1") return null;
  try {
    const payloadBytes = fromB64url(parts[1]);
    const signature = fromB64url(parts[2]);
    const key = await ticketKey();
    if (!await crypto.subtle.verify("HMAC", key, signature, enc.encode(parts[1])))
      return null;

    const payload = JSON.parse(dec.decode(payloadBytes)) as Ticket;
    const now = Math.floor(Date.now() / 1000);
    if (payload.v !== 1 || payload.cid !== clientId
      || !Number.isInteger(payload.iat) || !Number.isInteger(payload.exp)
      || payload.iat > now + 300 || payload.exp < now - 60
      || payload.exp < payload.iat || payload.exp - payload.iat > 2 * 60 * 60
      || typeof payload.sub !== "string" || !uuidPattern.test(payload.sub)) {
      return null;
    }
    return payload.sub.toLowerCase();
  } catch {
    return null;
  }
}

function validEpoch(value: string) {
  if (!epochPattern.test(value)) return false;
  try {
    const parsed = BigInt(value);
    return parsed > 0n && parsed <= maxUint64;
  } catch {
    return false;
  }
}

Deno.serve(async (req: Request) => {
  if (req.method !== "POST" && req.method !== "GET")
    return json(405, { error: "method_not_allowed" });

  const authorization = req.headers.get("authorization") ?? "";
  if (!authorization.startsWith("Bearer "))
    return json(401, { error: "server_auth_required" });
  const serverKey = authorization.slice(7).trim();
  if (serverKey.length < 32 || serverKey.length > 256)
    return json(401, { error: "invalid_server_key" });

  let reporter: { server_id: string; enabled: boolean } | undefined;
  try {
    const serverHash = await sha256(serverKey);
    const rows = await sql`
      select server_id, enabled
      from public.project_prime_career_reporters
      where key_hash = ${serverHash}
      limit 1
    `;
    reporter = rows[0] as typeof reporter;
  } catch (error) {
    console.error("party reservation reporter lookup failed", error);
    return json(500, { error: "reporter_lookup_failed" });
  }
  if (!reporter?.enabled)
    return json(401, { error: "unknown_server" });

  if (req.method === "GET")
    return json(200, { ok: true, server_id: reporter.server_id });

  let body: {
    action?: string;
    authority_epoch?: string;
    request_id?: string;
    reservation_id?: string;
    client_id?: number;
    career_ticket?: string;
    player_id?: string;
    assignments?: Array<{ player_id?: string; slot?: number }>;
    expires_in_seconds?: number;
    status?: string;
  };
  try {
    body = await readObjectBounded(req, 8192) as typeof body;
  } catch (error) {
    return json(error instanceof RequestBodyError ? error.status : 400, {
      error: "invalid_json",
    });
  }

  const action = typeof body.action === "string" ? body.action.trim() : "";
  if (!["validate", "activate", "admitted", "cancel"].includes(action))
    return json(400, { error: "invalid_action" });

  const requestId = typeof body.request_id === "string" ? body.request_id.trim() : "";
  if (!uuidPattern.test(requestId))
    return json(400, { error: "invalid_request_id" });

  const epoch = typeof body.authority_epoch === "string"
    ? body.authority_epoch.trim() : "";
  if ((action === "validate" || action === "activate") && !validEpoch(epoch))
    return json(400, { error: "invalid_authority_epoch" });

  try {
    if (action === "validate") {
      const clientId = Number.isInteger(body.client_id) ? body.client_id! : 0;
      if (clientId < 1 || clientId > 0xffffffff)
        return json(400, { error: "invalid_client_id" });

      const playerId = await verifyTicket(body.career_ticket, clientId);
      if (!playerId)
        return json(401, { error: "invalid_career_ticket" });

      const rows = await sql`
        select prime.social_party_reservation_server_validate(
          ${reporter.server_id}::uuid,
          ${epoch}::numeric,
          ${requestId}::uuid,
          ${playerId}::uuid
        ) as value
      `;
      const value = rows[0]?.value ?? {
        ok: false,
        status: "reservation_empty_result",
      };
      return json(200, { ...value, player_id: playerId });
    }

    const reservationId = typeof body.reservation_id === "string"
      ? body.reservation_id.trim() : "";
    const zeroReservation = "00000000-0000-0000-0000-000000000000";
    if (!uuidPattern.test(reservationId)
      && !(action === "cancel" && reservationId === zeroReservation))
      return json(400, { error: "invalid_reservation_id" });

    if (action === "activate") {
      const assignments = Array.isArray(body.assignments) ? body.assignments : [];
      if (assignments.length < 1 || assignments.length > 8
        || assignments.some((item) =>
          typeof item !== "object" || item === null
          || typeof item.player_id !== "string" || !uuidPattern.test(item.player_id)
          || !Number.isInteger(item.slot) || item.slot! < 0 || item.slot! > 7)) {
        return json(400, { error: "invalid_assignments" });
      }
      const seconds = Number.isInteger(body.expires_in_seconds)
        ? body.expires_in_seconds! : 30;
      if (seconds < 10 || seconds > 60)
        return json(400, { error: "invalid_expiry" });

      const rows = await sql`
        select prime.social_party_reservation_server_activate(
          ${reporter.server_id}::uuid,
          ${epoch}::numeric,
          ${requestId}::uuid,
          ${reservationId}::uuid,
          ${JSON.stringify(assignments)}::jsonb,
          ${seconds}::integer
        ) as value
      `;
      return json(200, rows[0]?.value ?? {
        ok: false,
        status: "reservation_empty_result",
      });
    }

    if (action === "admitted") {
      const playerId = typeof body.player_id === "string"
        ? body.player_id.trim() : "";
      if (!uuidPattern.test(playerId))
        return json(400, { error: "invalid_player_id" });

      const rows = await sql`
        select prime.social_party_reservation_server_admitted(
          ${reporter.server_id}::uuid,
          ${requestId}::uuid,
          ${reservationId}::uuid,
          ${playerId}::uuid
        ) as value
      `;
      return json(200, rows[0]?.value ?? {
        ok: false,
        status: "reservation_empty_result",
      });
    }

    const status = typeof body.status === "string" ? body.status.trim() : "rejected";
    if (!["rejected", "cancelled", "expired"].includes(status))
      return json(400, { error: "invalid_status" });

    const rows = await sql`
      select prime.social_party_reservation_server_cancel(
        ${reporter.server_id}::uuid,
        ${requestId}::uuid,
        ${reservationId}::uuid,
        ${status}::text
      ) as value
    `;
    return json(200, rows[0]?.value ?? {
      ok: false,
      status: "reservation_empty_result",
    });
  } catch (error) {
    console.error("party reservation server operation failed", error);
    return json(500, { error: "reservation_server_db_failed" });
  }
});
